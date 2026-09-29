
namespace LibcameraSharp.Tests.Capture;

/// <summary>
/// Several captures in flight at once — what a web app with one shared camera produces on its first
/// concurrent request, and what any <c>Task.WhenAll</c> produces immediately.
/// </summary>
/// <remarks>
/// Regression tests for a lost wakeup (a waiter that was never woken, a permanent hang with no exception) and
/// for buffers that never came back. Every waiter now has its own result, which the camera's loop hands it.
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
        await using var session = new CameraSession(manager);
        await session.ConfigureAsync(s => s.CreatePreviewConfiguration());
        await session.StartAsync();

        var captures = Enumerable.Range(0, concurrent)
            .Select(_ => Task.Run(async () => await session.CaptureMetadataAsync(Ct), Ct))
            .ToArray();

        var all = Task.WhenAll(captures);
        var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(20), Ct));
        Assert.True(finished == all, $"only {captures.Count(c => c.IsCompletedSuccessfully)} of {concurrent} captures completed");

        await all;
        output.WriteLine($"{concurrent} concurrent captures, sequence numbers: {string.Join(", ", captures.Select(c => c.Result.Get(Controls.SensorTimestamp)))}");
        await session.StopAsync();
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
        await using var session = new CameraSession(manager);
        await session.ConfigureAsync(s => s.CreatePreviewConfiguration());
        await session.StartAsync();
        await session.CaptureMetadataAsync(Ct);                          // drain what is already buffered

        var pending = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(async () => await session.CaptureMetadataAsync(Ct), Ct))
            .ToArray();
        await session.StopAsync();

        var all = Task.WhenAll(pending);
        var finished = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(10), Ct));
        Assert.True(finished == all, $"{pending.Count(p => !p.IsCompleted)} of 4 captures were left hanging by Stop()");

        foreach (var capture in pending)
            output.WriteLine($"{capture.Status}: {capture.Exception?.InnerException?.GetType().Name ?? "completed"}");
    }

    /// <summary>A photo taken while a frame loop runs pauses the loop; the loop sets the camera back up and carries on.</summary>
    [Fact]
    public async Task A_photo_during_a_frame_loop_pauses_it_and_the_loop_carries_on()
    {
        await using var camera = CameraDevice.Open();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var framesAfterPhoto = 0;
        var photoTaken = false;
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var reading = Task.Run(async () =>
        {
            await foreach (var frame in camera.ReadFramesAsync(new FrameOptions(), stop.Token))
            {
                using (frame)
                {
                    running.TrySetResult();
                    if (Volatile.Read(ref photoTaken) && ++framesAfterPhoto >= 5)
                        return;
                }
            }
        }, Ct);

        await running.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        var photo = await camera.CapturePhotoAsync(cancellationToken: Ct);
        Volatile.Write(ref photoTaken, true);

        var finished = await Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds(20), Ct));
        Assert.True(finished == reading, "the frame loop did not carry on after the photo");
        await reading;
        Assert.True(photo.Size.Width > 0);
    }

    /// <summary>A frame a recording and a capture both hold goes back to the camera only when both are done with it.</summary>
    [Fact]
    public async Task A_frame_shared_with_a_recording_goes_back_once_both_are_done()
    {
        using var manager = new CameraManager();
        await using var session = new CameraSession(manager);
        await session.ConfigureAsync(s => s.CreatePreviewConfiguration());
        var encoder = new CountingEncoder();
        await session.StartRecordingAsync(encoder, new FileOutput(Stream.Null));

        // More frames than the camera has buffers: a buffer lost on either side would stall it.
        for (var i = 0; i < 20; i++)
        {
            using var frame = await session.NextFrameAsync(cancellationToken: Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
        }

        await session.StopRecordingAsync(encoder);
        Assert.True(encoder.FramesEncoded >= 10, $"the recording saw {encoder.FramesEncoded} frames");
    }

    /// <summary>Captures that give up leave no buffer behind: frames keep coming afterwards.</summary>
    [Fact]
    public async Task Cancelled_captures_leave_no_buffer_behind()
    {
        using var manager = new CameraManager();
        await using var session = new CameraSession(manager);
        await session.ConfigureAsync(s => s.CreatePreviewConfiguration());
        await session.StartAsync();

        // Each gives up at a different moment, some before the loop sees it and some after it has a frame.
        for (var i = 0; i < 50; i++)
        {
            using var giveUp = new CancellationTokenSource(TimeSpan.FromMilliseconds(i % 7));
            try
            {
                using var frame = await session.NextFrameAsync(cancellationToken: giveUp.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        for (var i = 0; i < 10; i++)
        {
            using var frame = await session.NextFrameAsync(cancellationToken: Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);
        }
    }

    // An encoder that only counts what it is fed, so the test depends on no codec.
    private sealed class CountingEncoder : Encoder
    {
        protected override void EncodeFrame(MappedFrame frame, long timestamp)
        {
        }

        protected override VideoStreamInfo StreamInfo => new(VideoCodec.Mjpeg, Width, Height, FrameRate ?? 30);
    }
}
