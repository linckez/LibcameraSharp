namespace LibcameraSharp.Tests.Capture;

/// <summary>
/// What happens to frames and controls when the camera stops, starts again and closes: the camera's loop owns all
/// of it, so these are the cases it must get right.
/// </summary>
[Collection("camera")]
public class SessionLifetimeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Closing the camera ends a capture that is still waiting, rather than leaving it waiting forever.</summary>
    [Fact]
    public async Task Closing_ends_a_capture_that_waits()
    {
        await using var camera = CameraDevice.Open();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = Task.Run(async () =>
        {
            await foreach (var frame in camera.ReadFramesAsync(new FrameOptions(), stop.Token))
            {
                frame.Dispose();
                running.TrySetResult();
            }
        }, Ct);
        await running.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);

        await camera.DisposeAsync();

        var finished = await Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds(10), Ct));
        Assert.True(finished == reading, "the frame loop was left waiting after the camera closed");
        var ended = await Record.ExceptionAsync(() => reading);
        Assert.True(ended is ObjectDisposedException or OperationCanceledException, $"ended with {ended?.GetType().Name ?? "no exception"}");
    }

    /// <summary>A frame read after the camera closed has no buffer behind it, and says so.</summary>
    [Fact]
    public async Task A_frame_read_after_the_camera_closed_throws()
    {
        using var manager = new CameraManager();
        var session = new CameraSession(manager);
        await session.ConfigureAsync(s => s.CreatePreviewConfiguration());
        await session.StartAsync();
        var frame = await session.NextFrameAsync(cancellationToken: Ct);

        await session.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => frame.Metadata);
        frame.Dispose();
    }

    /// <summary>
    /// A frame someone holds across a stop and start stays theirs: the camera doesn't fill its buffer again until
    /// it comes back. Its buffer's sequence number would change if it had been refilled.
    /// </summary>
    [Fact]
    public async Task A_frame_held_across_a_stop_and_start_keeps_its_buffer()
    {
        using var manager = new CameraManager();
        await using var session = new CameraSession(manager);
        await session.ConfigureAsync(s => s.CreatePreviewConfiguration());
        await session.StartAsync();

        using var held = await session.NextFrameAsync(cancellationToken: Ct);
        var sequence = held.Sequence;

        await session.StopAsync();
        await session.StartAsync();
        for (var i = 0; i < 12; i++)
        {
            using var frame = await session.NextFrameAsync(cancellationToken: Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
        }

        Assert.Equal(sequence, held.Sequence);
    }

    /// <summary>
    /// A stop and start (as a timeout restart does) comes back with the controls it had, rather than the camera's
    /// defaults. Needs a camera with analogue gain, so a Pi camera, not the test VM's virtual one.
    /// </summary>
    [Fact]
    public async Task A_stop_and_start_comes_back_with_the_controls_it_had()
    {
        using var manager = new CameraManager();
        await using var session = new CameraSession(manager);
        Assert.SkipUnless(session.Facts.Capabilities.Supports(Controls.AnalogueGain), "this camera has no analogue gain");

        var setUp = await session.SetUpAsync(new StreamSettings(), new CameraControls { Gain = GainMode.Fixed(4.0f) }, CameraUse.Frames);
        using (var landed = await session.NextFrameAsync(setUp.Target, Ct))
            Assert.Equal(4.0f, landed.Metadata.Get(Controls.AnalogueGain), 0.1f);

        await session.StopAsync();
        await session.StartAsync();
        await session.DropFramesAsync(6, Ct);                           // past the sensor's delay

        using var after = await session.NextFrameAsync(cancellationToken: Ct);
        Assert.Equal(4.0f, after.Metadata.Get(Controls.AnalogueGain), 0.1f);
    }
}
