using System.Net.Sockets;

namespace LibcameraSharp;

public partial class CameraDevice
{
    /// <summary>Starts recording to a file; it records until you stop the returned recording.</summary>
    /// <remarks>The extension picks the container: <c>.mp4</c>, <c>.mkv</c> or <c>.ts</c>; anything else gets the codec's own bytes.</remarks>
    /// <returns>The recording, once the camera is recording.</returns>
    /// <exception cref="InvalidOperationException">A recording is running with different options.</exception>
    public virtual Task<VideoRecording> StartRecordingAsync(string path, VideoOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        options ??= new VideoOptions();
        return RunExclusiveAsync(() => OpenRecordingAsync(options, chosen => VideoContainer.ForPath(path).WrapFile(path, chosen)), cancellationToken);
    }

    /// <summary>Starts recording to a stream, such as an HTTP response, a socket or a pipe; it records until you stop the returned recording.</summary>
    /// <remarks>
    /// A destination slower than the camera slows the camera: recordings never drop frames. Stopping the
    /// recording closes <paramref name="destination"/>.
    /// </remarks>
    /// <returns>The recording, once the camera is recording.</returns>
    /// <exception cref="InvalidOperationException">A recording is running with different options.</exception>
    public virtual Task<VideoRecording> StartRecordingAsync(Stream destination, VideoOptions? options = null, VideoContainer? container = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        options ??= new VideoOptions();
        return RunExclusiveAsync(() => OpenRecordingAsync(options,
            chosen => container?.Wrap(destination, chosen) ?? new FileOutput(destination, ownsStream: true)), cancellationToken);
    }

    // A recording's start is one job, like a photo. It sets the camera up for the recording, then opens its output,
    // so a setup that fails leaves no file behind.
    private async Task<VideoRecording> OpenRecordingAsync(VideoOptions options, Func<VideoOptions, Output> makeOutput)
    {
        var encoder = VideoContainer.CreateEncoder(options.Codec);
        if (encoder is LibavH264Encoder h264)
            h264.KeyframeInterval = options.KeyframeInterval;

        // The encoder states the rate asked for; without one, the camera's fastest, at most 30 fps.
        if (options.Controls.FrameRate is { Max: > 0 } frameRate)
            encoder.FrameRate = frameRate.Max;

        // Both encoders take YUV420, so have the camera deliver it rather than convert every frame.
        var streams = options.Streams.CaptureFormat is null
            ? options.Streams with { CaptureFormat = PixelFormats.YUV420 }
            : options.Streams;

        // The colour space follows the size actually recorded, unless the options set one.
        if (streams.ColourSpace is null)
            streams = streams with
            {
                ColourSpace = CameraSession.VideoColourSpace(streams.CaptureSize ?? CameraSession.DefaultVideoSize, options.Codec == VideoCodec.Mjpeg),
            };
        var setup = await Session.SetUpAsync(streams, options.Controls, CameraUse.Video).ConfigureAwait(false);

        // Opening the output, the encoder and the container is file I/O and libav, done here rather than on the camera's
        // loop. The loop then feeds it from the first frame taken with these controls. A recording that fails to start
        // closes its output and, when no other recording runs, stops the camera.
        Output? output = null;
        try
        {
            output = makeOutput(options);
            CameraSession.PrepareEncoder(encoder, setup.Configuration, setup.FrameRate);
            encoder.Output = output;
            encoder.Start(options.Quality);
            await Session.AttachEncoderAsync(encoder, setup.Target).ConfigureAwait(false);
        }
        catch
        {
            // Each step runs whatever the one before it did, and the failure that stopped the start is what's thrown.
            try
            {
                encoder.Stop();
            }
            catch (Exception)
            {
                // Closing an encoder that failed to start can fail too.
            }
            finally
            {
                output?.Dispose();
            }
            try
            {
                await Session.StopIfIdleAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The camera closing meanwhile, say; the failure to report is the one that stopped the start.
            }
            throw;
        }

        return new VideoRecording(Session, encoder, output);
    }

    /// <summary>
    /// Records to a stream until <paramref name="cancellationToken"/> is cancelled, then stops and
    /// closes it. In a web handler, that is when the client hangs up.
    /// </summary>
    /// <exception cref="IOException">Writing failed for a reason other than the destination going away, such as a full disk.</exception>
    /// <remarks>A destination that goes away, such as a client hanging up, ends the recording without an exception.</remarks>
    public virtual async Task RecordToAsync(Stream destination, VideoOptions? options = null,
        VideoContainer? container = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // Record until the caller cancels, or writing fails; stopping reports the failure, if there was one.
            var recording = await StartRecordingAsync(destination, options, container, cancellationToken).ConfigureAwait(false);
            await Task.WhenAny(Task.Delay(Timeout.Infinite, cancellationToken), recording.WhenFailed).ConfigureAwait(false);
            await recording.StopAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (DestinationWentAway(exception, cancellationToken))
        {
            // The other side went away, such as a client hanging up: the recording simply ends.
        }
    }

    // Only a destination that went away ends a recording quietly: the caller's own cancellation, or a
    // connection reset, aborted or shut down (a broken pipe). Anything else is a failure and is thrown. Stopping reports
    // a cancelled write wrapped in an IOException, so the cancellation is found inside it.
    private static bool DestinationWentAway(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException or IOException { InnerException: OperationCanceledException } => cancellationToken.IsCancellationRequested,
        IOException { InnerException: SocketException { SocketErrorCode: SocketError.ConnectionReset or SocketError.ConnectionAborted or SocketError.Shutdown } } => true,
        _ => false,
    };
}
