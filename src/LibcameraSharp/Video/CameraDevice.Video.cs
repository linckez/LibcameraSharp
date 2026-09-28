using System.Net.Sockets;

namespace LibcameraSharp;

public sealed partial class CameraDevice
{
    /// <summary>Starts recording to a file, until the returned recording is disposed.</summary>
    /// <remarks>The extension picks the container: <c>.mp4</c>, <c>.mkv</c> or <c>.ts</c>; anything else gets the codec's own bytes.</remarks>
    public VideoRecording RecordTo(string path, VideoOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Record(options ?? new VideoOptions(), chosen => VideoContainer.ForPath(path).WrapFile(path, chosen));
    }

    /// <summary>Starts recording to a stream, such as an HTTP response, a socket or a pipe.</summary>
    /// <remarks>A destination slower than the camera slows the camera: recordings never drop frames.</remarks>
    /// <exception cref="InvalidOperationException">A recording is running with different options.</exception>
    public VideoRecording RecordTo(Stream destination, VideoOptions? options = null, VideoContainer? container = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return Record(options ?? new VideoOptions(),
            chosen => container?.Wrap(destination, chosen) ?? new FileOutput(destination, ownsStream: true));
    }

    // Sets the camera up for the recording, then opens its output, so a setup that fails leaves no file behind.
    // One call sets the camera up at a time.
    private VideoRecording Record(VideoOptions options, Func<VideoOptions, Output> makeOutput)
    {
        _calls.Wait();
        try
        {
            return StartRecording(options, makeOutput);
        }
        finally
        {
            _calls.Release();
        }
    }

    private VideoRecording StartRecording(VideoOptions options, Func<VideoOptions, Output> makeOutput)
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
        ApplyOptions(streams, options.Controls, CameraUse.Video);

        // The recording begins with the first frame taken with these controls.
        if (!_session.Started)
            _session.Start();
        var target = _session.TakeControlsTarget();
        encoder.StartWhen = frame => _session.ControlsLanded(frame.Request, target);
        var output = makeOutput(options);

        // A recording that fails to start closes its output and, when no other recording runs, stops the camera.
        try
        {
            _session.StartRecording(encoder, output, quality: options.Quality);
        }
        catch
        {
            output.Dispose();
            if (_session.Encoders.Count == 0)
                _session.Stop();
            throw;
        }

        return new VideoRecording(_session, encoder, output);
    }

    /// <summary>
    /// Records to a stream until <paramref name="cancellationToken"/> is cancelled, then stops and
    /// closes it. In a web handler, that is when the client hangs up.
    /// </summary>
    /// <exception cref="IOException">Writing failed for a reason other than the destination going away, such as a full disk.</exception>
    /// <remarks>A destination that goes away, such as a client hanging up, ends the recording without an exception.</remarks>
    public async Task RecordToAsync(Stream destination, VideoOptions? options = null,
        VideoContainer? container = null, CancellationToken cancellationToken = default)
    {
        var recording = RecordTo(destination, options, container);
        try
        {
            // Record until the caller cancels, or writing fails.
            await Task.WhenAny(Task.Delay(Timeout.Infinite, cancellationToken), recording.WhenFailed).ConfigureAwait(false);
            await recording.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (DestinationWentAway(exception, cancellationToken))
        {
            // The other side went away, such as a client hanging up: the recording simply ends.
        }
    }

    // Only a destination that went away ends a recording quietly: the caller's own cancellation, or a
    // connection reset, aborted or shut down (a broken pipe). Anything else is a failure and is thrown.
    private static bool DestinationWentAway(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException => cancellationToken.IsCancellationRequested,
        IOException { InnerException: SocketException { SocketErrorCode: SocketError.ConnectionReset or SocketError.ConnectionAborted or SocketError.Shutdown } } => true,
        _ => false,
    };
}
