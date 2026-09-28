
namespace LibcameraSharp.Tests.Concurrency;

/// <summary>
/// The camera thread reads the pending controls while the application writes them. Without a
/// lock, a <c>Set</c> racing the camera thread's <c>ApplyTo</c>
/// threw <see cref="InvalidOperationException"/> on the camera thread — where it is swallowed,
/// leaving the request unrecycled and the camera stalled. Needs no camera.
/// </summary>
public class ControlSettingsConcurrencyTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task Enumerating_while_another_thread_sets_does_not_throw()
    {
        var test = TestContext.Current.CancellationToken;
        var settings = new PendingControls();
        settings.Set(Controls.Brightness, 0.0f);

        using var stop = new CancellationTokenSource(Window);
        var writer = Task.Run(() =>
        {
            for (var i = 0; !stop.IsCancellationRequested; i++)
                settings.Set(Controls.Contrast, i % 32 / 32.0f);
        }, test);

        // Before the lock this threw "Collection was modified" within a few thousand iterations.
        for (var i = 0; i < 100_000 && !stop.IsCancellationRequested; i++)
            foreach (var (_, value) in settings)
                Assert.NotNull(value);

        await stop.CancelAsync();
        await writer;
    }

    [Fact]
    public async Task Two_sets_copying_from_each_other_do_not_deadlock()
    {
        // Taking both locks would let this pair deadlock.
        var test = TestContext.Current.CancellationToken;
        var a = new PendingControls().Set(Controls.Brightness, 0.1f);
        var b = new PendingControls().Set(Controls.Contrast, 1.1f);

        var both = Task.WhenAll(
            Task.Run(() => { for (var i = 0; i < 20_000; i++) a.SetControls(b); }, test),
            Task.Run(() => { for (var i = 0; i < 20_000; i++) b.SetControls(a); }, test));

        var finished = await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(20), test));
        Assert.Same(both, finished);
        await both;
    }
}
