namespace LibcameraSharp;

/// <summary>
/// A recording in progress. Stop it with <see cref="StopAsync"/>: that is when a container writes its index, so a
/// recording that is never stopped leaves a file that will not play.
/// </summary>
/// <remarks>
/// <see cref="StopAsync"/> reports a failure while recording, such as a full disk or a closed connection.
/// Disposing stops the recording too, but never throws, so use it as the fallback rather than the way to stop.
/// </remarks>
public class VideoRecording : IAsyncDisposable
{
    // Null in a recording made for mocking, which has no camera, encoder or output behind it.
    private readonly CameraSession? _session;
    private readonly Encoder? _encoder;
    private readonly Output? _output;

    // 1 once a stop has begun. The first caller runs it; every caller waits for the same one, so a second stop
    // doesn't return before the file is closed.
    private int _stopping;
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // 1 once disposed.
    private int _disposed;

    internal VideoRecording(CameraSession session, Encoder encoder, Output output) =>
        (_session, _encoder, _output) = (session, encoder, output);

    /// <summary>
    /// Creates a recording with nothing behind it, for mocking: what a fake
    /// <see cref="CameraDevice.StartRecordingAsync(string, VideoOptions?, CancellationToken)"/> returns. Stopping it
    /// does nothing; override the members your code reads.
    /// </summary>
    protected VideoRecording()
    {
        _stopping = 1;
        _stopped.SetResult();
    }

    /// <summary>How many frames have been encoded so far; still readable after the recording is stopped.</summary>
    /// <exception cref="ObjectDisposedException">The recording has been disposed.</exception>
    public virtual long FrameCount
    {
        get
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            return _encoder?.FramesEncoded ?? 0;
        }
    }

    // Completes when writing fails, so a recording that waits for cancellation can end early. A recording
    // made for mocking never fails, so it never completes.
    internal Task WhenFailed => _output?.WhenFailed ?? Task.Delay(Timeout.Infinite);

    /// <summary>
    /// Stops recording: flushes the encoder and closes the output. The camera stops when no other recording is
    /// running. Every call waits for the same stop, disposed or not.
    /// </summary>
    /// <exception cref="IOException">Writing failed while recording, such as a full disk or a client hanging up.</exception>
    public virtual Task StopAsync()
    {
        if (Interlocked.CompareExchange(ref _stopping, 1, 0) == 0)
            _ = RunStopAsync();
        return _stopped.Task;
    }

    /// <summary>Stops the recording, as <see cref="StopAsync"/> does, but never throws.</summary>
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>Stops the recording; a derived class releases its own resources here too.</summary>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;
        try
        {
            await StopAsync().ConfigureAwait(false);
        }
        catch (IOException)
        {
            // StopAsync is where a failure is reported; disposing only makes sure the recording ends.
        }
    }

    // The encoder drains first, while the camera still knows it, so a camera closing at the same time never frees a
    // buffer the encoder still holds; frames that arrive after that go straight back. Then the camera's loop lets go of
    // it (the last recording stops the camera), and the flush and the container's close run off the loop and off the
    // caller's thread, whatever failed before them.
    private async Task RunStopAsync()
    {
        Exception? thrown = null;
        try
        {
            await Task.Run(_encoder!.Drain).ConfigureAwait(false);
            await _session!.DetachEncoderAsync(_encoder).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The camera was closed first; the flush below is still ours.
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        try
        {
            await Task.Run(() =>
            {
                try
                {
                    _encoder!.Stop();
                }
                finally
                {
                    _output!.Dispose();
                }
            }).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            thrown ??= exception;
        }

        // The first failure is the one to report: one while recording, else one while stopping. A stream's failure is
        // already an IOException; anything else (libav's, on a file, or the camera failing) is wrapped in one.
        if ((_output!.Failure ?? thrown) is not { } failure)
        {
            _stopped.SetResult();
            return;
        }
        _stopped.SetException(failure as IOException ?? new IOException($"The recording failed: {failure.Message}", failure));
    }
}
