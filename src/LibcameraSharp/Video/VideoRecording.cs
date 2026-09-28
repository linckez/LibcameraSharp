namespace LibcameraSharp;

/// <summary>
/// A recording in progress. Dispose it to stop: that is when a container writes its index, so a
/// recording that is never disposed leaves a file that will not play.
/// </summary>
/// <remarks>If writing failed while recording — a full disk, a closed connection — disposing throws that failure.</remarks>
public sealed class VideoRecording : IAsyncDisposable, IDisposable
{
    private readonly CameraSession _session;
    private readonly Encoder _encoder;
    private readonly Output _output;
    private bool _stopped;

    internal VideoRecording(CameraSession session, Encoder encoder, Output output) =>
        (_session, _encoder, _output) = (session, encoder, output);

    /// <summary>How many frames have been encoded so far.</summary>
    public long FrameCount => _encoder.FramesEncoded;

    // Completes when writing fails, so a recording that waits for cancellation can end early.
    internal Task WhenFailed => _output.WhenFailed;

    /// <summary>Flushes the encoder and closes the output; the camera stops when no other recording is running.</summary>
    /// <exception cref="IOException">Writing to a stream failed while recording, such as a client hanging up.</exception>
    /// <exception cref="InvalidOperationException">Writing a file failed while recording, such as a full disk; the message is libav's.</exception>
    public async ValueTask DisposeAsync()
    {
        if (_stopped)
            return;
        _stopped = true;

        // Closing a container is file I/O, so it runs off the caller's thread.
        await Task.Run(Stop).ConfigureAwait(false);
    }

    /// <summary>Stops the recording. Prefer <see cref="DisposeAsync"/>, since closing a container is I/O.</summary>
    /// <exception cref="IOException">Writing to a stream failed while recording.</exception>
    /// <exception cref="InvalidOperationException">Writing a file failed while recording; the message is libav's.</exception>
    public void Dispose()
    {
        if (_stopped)
            return;
        _stopped = true;
        Stop();
    }

    private void Stop()
    {
        _session.StopRecording(_encoder);
        _output.Dispose();
        _encoder.Dispose();

        // Rethrown as it happened, keeping its type and stack.
        if (_output.Failure is { } failure)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
