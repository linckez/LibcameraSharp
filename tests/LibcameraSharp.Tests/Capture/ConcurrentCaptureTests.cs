
namespace LibcameraSharp.Tests.Capture;

/// <summary>
/// Several captures in flight at once — what a web app with one shared camera produces on its first
/// concurrent request, and what any <c>Task.WhenAll</c> produces immediately.
/// </summary>
/// <remarks>
/// Regression test for a lost wakeup: each waiter used to install its own completion source, so a
/// second waiter overwrote the first one's and the first was never woken again — a permanent hang
/// with no exception. Now every waiter shares one signal and all are woken together.
/// </remarks>
[Collection("camera")]
public class ConcurrentCaptureTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(2)]
    [InlineData(8)]
    public async Task Captures_in_flight_together_all_complete(int concurrent)
    {
        using var manager = new CameraManager();
        using var session = new CameraSession(manager);
        session.Configure(session.CreatePreviewConfiguration());
        session.Start();

        var captures = Enumerable.Range(0, concurrent)
            .Select(_ => Task.Run(async () => await session.CaptureMetadataAsync(Ct), Ct))
            .ToArray();

        var all = Task.WhenAll(captures);
        var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(20), Ct));
        Assert.True(finished == all, $"only {captures.Count(c => c.IsCompletedSuccessfully)} of {concurrent} captures completed");

        await all;
        output.WriteLine($"{concurrent} concurrent captures, sequence numbers: {string.Join(", ", captures.Select(c => c.Result.Get(Controls.SensorTimestamp)))}");
        session.Stop();
    }

    /// <summary>Stopping the camera leaves nothing stranded: every pending capture ends, one way or another.</summary>
    /// <remarks>
    /// A <see cref="Task"/> that never completes is a leak, so <c>Stop</c> wakes every waiter. Whether a
    /// given capture cancels or is refused outright depends on whether it reached the wait before the
    /// stop, so the test asserts only that none of them hangs.
    /// </remarks>
    [Fact]
    public async Task Stopping_strands_no_pending_capture()
    {
        using var manager = new CameraManager();
        using var session = new CameraSession(manager);
        session.Configure(session.CreatePreviewConfiguration());
        session.Start();
        await session.CaptureMetadataAsync(Ct);                          // drain what is already buffered

        var pending = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(async () => await session.CaptureMetadataAsync(Ct), Ct))
            .ToArray();
        session.Stop();

        var all = Task.WhenAll(pending);
        var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(10), Ct));
        Assert.True(finished == all, $"{pending.Count(p => !p.IsCompleted)} of 4 captures were left hanging by Stop()");

        foreach (var capture in pending)
            output.WriteLine($"{capture.Status}: {capture.Exception?.InnerException?.GetType().Name ?? "completed"}");
    }
}
