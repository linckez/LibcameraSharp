namespace LibcameraSharp;

public partial class CameraDevice
{
    /// <summary>
    /// Frames as fast as you can take them, newest first. Frames that arrive while you are still busy
    /// with the previous one are dropped and counted in <see cref="FramesDropped"/>.
    /// </summary>
    /// <remarks>
    /// Dispose each frame inside the loop: it holds one of the camera's few buffers. A photo taken while
    /// the loop runs pauses it; once the photo is done, the loop sets the camera back up and carries on.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A recording is running, or another frame loop is using the camera with different options.</exception>
    public virtual async IAsyncEnumerable<VideoFrame> ReadFramesAsync(
        FrameOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The camera is set up by one call at a time. The loop takes its target once per setup, so later
        // SetControls calls, such as a slider, don't hold its frames back. The loop's identity lets the camera
        // refuse a second loop that wants different options.
        var reader = new object();
        async Task<ControlsTarget> SetUpAsync() =>
            (await RunExclusiveAsync(() => Session.SetUpAsync(options.Streams, options.Controls, CameraUse.Frames, reader), cancellationToken)
                .ConfigureAwait(false)).Target;

        try
        {
            var target = await SetUpAsync().ConfigureAwait(false);

            // The smaller stream when the options asked for one, else the capture stream.
            var stream = options.Streams.PreviewSize is not null ? SessionStream.Preview : SessionStream.Capture;

            uint? previous = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                CapturedFrame captured;
                try
                {
                    captured = await Session.NextFrameAsync(target, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException
                                                  && !cancellationToken.IsCancellationRequested && !IsDisposed)
                {
                    // Another call stopped the camera: a photo setting it up its own way, or a recording that ended.
                    // Once it is done, switch back to this loop's setup and carry on.
                    target = await SetUpAsync().ConfigureAwait(false);
                    previous = null;
                    continue;
                }

                // A frame from another setup (the camera restarted under the loop) goes back; then switch back.
                if (captured.Run != target.Run)
                {
                    captured.Dispose();
                    target = await SetUpAsync().ConfigureAwait(false);
                    previous = null;
                    continue;
                }

                VideoFrame frame;
                try
                {
                    frame = new VideoFrame(captured, stream);
                }
                catch
                {
                    captured.Dispose();
                    throw;
                }

                // Frames the camera produced between this one and the last one delivered were missed.
                if (previous is { } last && frame.Sequence > last + 1)
                    Interlocked.Add(ref _framesDropped, frame.Sequence - last - 1);
                previous = frame.Sequence;
                yield return frame;
            }
        }
        finally
        {
            // This loop's options no longer hold the camera.
            Session.ForgetReader(reader);
        }
    }
}
