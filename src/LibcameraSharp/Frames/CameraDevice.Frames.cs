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
    /// <exception cref="InvalidOperationException">A recording is running with different options.</exception>
    public virtual async IAsyncEnumerable<VideoFrame> ReadFramesAsync(
        FrameOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The camera is set up by one call at a time. The loop takes its target once per setup, so later
        // SetControls calls, such as a slider, don't hold its frames back.
        async Task<ControlsTarget> SetUpAsync()
        {
            await _calls.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                ApplyOptions(options.Streams, options.Controls, CameraUse.Frames);
                if (!Session.Started)
                    Session.Start();
                return Session.TakeControlsTarget();
            }
            finally
            {
                _calls.Release();
            }
        }
        var target = await SetUpAsync().ConfigureAwait(false);

        // The smaller stream when the options asked for one, else the capture stream.
        var stream = options.Streams.PreviewSize is not null ? SessionStream.Preview : SessionStream.Capture;

        uint? previous = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            CapturedFrame captured;
            try
            {
                captured = await Session.CaptureRequestWithControlsAsync(target, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !_disposed)
            {
                // Another call, such as a photo, stopped the camera to set it up its own way: once it is
                // done, switch back to this loop's setup and carry on.
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
}
