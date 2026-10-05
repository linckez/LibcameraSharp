using System.Net.Sockets;

namespace LibcameraSharp;

public partial class CameraDevice
{
    /// <summary>Starts recording to a file; it records until you stop the returned recording.</summary>
    /// <remarks>The extension picks the container: <c>.mp4</c>, <c>.mkv</c> or <c>.ts</c>; anything else gets the codec's own bytes.</remarks>
    /// <param name="path">The file to write, created or replaced.</param>
    /// <param name="options">How to record; the default video options when null. An override gets null when the caller passed none.</param>
    /// <param name="cancellationToken">Cancels the start while it waits its turn.</param>
    /// <returns>The recording, once the camera is recording.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A frame rate or region in the options is out of range.</exception>
    /// <exception cref="InvalidOperationException">A recording is running with different options.</exception>
    public virtual Task<VideoRecording> StartRecordingAsync(string path, VideoOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        options ??= new VideoOptions();
        options.Controls.ThrowIfInvalid(nameof(options));
        return RunExclusiveAsync(() => OpenRecordingAsync(options, () => OpenFile(path)), cancellationToken);
    }

    /// <summary>Starts recording to a stream, such as an HTTP response, a socket or a pipe; it records until you stop the returned recording.</summary>
    /// <remarks>
    /// A destination slower than the camera slows the camera: recordings never drop frames. Stopping the
    /// recording closes <paramref name="destination"/>. With MJPEG and no container, each <c>Write</c> to
    /// <paramref name="destination"/> is exactly one complete JPEG.
    /// </remarks>
    /// <param name="destination">Where the recording goes; stopping the recording closes it.</param>
    /// <param name="options">How to record; the default video options when null. An override gets null when the caller passed none.</param>
    /// <param name="container">How the recording is wrapped; the codec's own bytes by default.</param>
    /// <param name="cancellationToken">Cancels the start while it waits its turn.</param>
    /// <returns>The recording, once the camera is recording.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A frame rate or region in the options is out of range, or <paramref name="container"/> is none of the containers.</exception>
    /// <exception cref="InvalidOperationException">A recording is running with different options.</exception>
    public virtual Task<VideoRecording> StartRecordingAsync(Stream destination, VideoOptions? options = null, VideoContainer container = VideoContainer.None,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!Enum.IsDefined(container))
            throw new ArgumentOutOfRangeException(nameof(container), container, "Not a VideoContainer.");
        options ??= new VideoOptions();
        options.Controls.ThrowIfInvalid(nameof(options));
        return RunExclusiveAsync(() => OpenRecordingAsync(options, () => OpenStream(destination, container)), cancellationToken);
    }

    // The output for a file: the extension picks the container, and anything else gets the codec's own bytes. libav
    // opens and writes a muxed file itself, as it does for a file name it is given.
    private static Output OpenFile(string path)
    {
        var container = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp4" => VideoContainer.Mp4,
            ".mkv" => VideoContainer.Matroska,
            ".ts" => VideoContainer.MpegTs,
            _ => VideoContainer.None,
        };
        return container == VideoContainer.None ? new FileOutput(File.Create(path), ownsStream: true) : new ContainerOutput(path, container);
    }

    // The output for a stream, which the recording owns from here on.
    private static Output OpenStream(Stream destination, VideoContainer container) =>
        container == VideoContainer.None ? new FileOutput(destination, ownsStream: true) : new ContainerOutput(destination, container, ownsStream: true);

    // A recording's start is one job, like a photo. It sets the camera up for the recording, then opens its output,
    // so a setup that fails leaves no file behind.
    private async Task<VideoRecording> OpenRecordingAsync(VideoOptions options, Func<Output> makeOutput)
    {
        Encoder encoder = options.Codec == VideoCodec.Mjpeg ? new LibavMjpegEncoder() : new LibavH264Encoder();
        if (encoder is LibavH264Encoder h264)
            h264.KeyframeInterval = options.KeyframeInterval;

        // Both encoders take YUV420, so have the camera deliver it rather than convert every frame.
        var streams = options.Streams.CaptureFormat is null
            ? options.Streams with { CaptureFormat = PixelFormats.YUV420 }
            : options.Streams;

        // The colour space follows the size actually recorded, unless the options set one.
        if (streams.ColorSpace is null)
            streams = streams with
            {
                ColorSpace = CameraSession.VideoColourSpace(streams.CaptureSize ?? CameraSession.DefaultVideoSize, options.Codec == VideoCodec.Mjpeg),
            };
        var setup = await Session.SetUpAsync(streams, options.Controls, CameraUse.Video).ConfigureAwait(false);

        // Opening the output, the encoder and the container is file I/O and libav, done here rather than on the camera's
        // loop. The loop then feeds it from the first frame taken with these controls. A recording that fails to start
        // closes its output and, when no other recording runs, stops the camera.
        Output? output = null;
        try
        {
            output = makeOutput();
            // The encoder states the rate asked for; without one, the camera's fastest, at most 30 fps.
            var frameRate = options.Controls.FrameRate is { } asked ? asked.Max : setup.FrameRate;
            CameraSession.PrepareEncoder(encoder, setup.Configuration, frameRate);
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
    /// <exception cref="ArgumentOutOfRangeException">A frame rate or region in the options is out of range, or <paramref name="container"/> is none of the containers.</exception>
    /// <remarks>
    /// A destination that goes away, such as a client hanging up, ends the recording without an exception. With MJPEG and
    /// no container, each <c>Write</c> to <paramref name="destination"/> is exactly one complete JPEG.
    /// </remarks>
    public virtual async Task RecordToAsync(Stream destination, VideoOptions? options = null,
        VideoContainer container = VideoContainer.None, CancellationToken cancellationToken = default)
    {
        try
        {
            // Record until the caller cancels, or writing fails; stopping reports the failure, if there was one.
            var recording = await StartRecordingAsync(destination, options, container, cancellationToken).ConfigureAwait(false);
            await Task.WhenAny(Task.Delay(Timeout.Infinite, cancellationToken), recording.WhenFailed).ConfigureAwait(false);
            // Not with cancellationToken: it has usually just been cancelled, and the stop must be waited for.
            await recording.StopAsync(CancellationToken.None).ConfigureAwait(false);
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
