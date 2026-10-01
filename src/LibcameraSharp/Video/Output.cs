namespace LibcameraSharp;

/// <summary>Where encoded frames go: an encoder pushes every frame here, and the output writes it out.</summary>
internal abstract class Output : IDisposable
{
    private readonly TaskCompletionSource _failed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    public bool Recording { get; private set; }

    /// <summary>Why writing failed — a client hung up, a disk filled — or null while it hasn't.</summary>
    public Exception? Failure { get; private set; }

    /// <summary>Completes when writing fails.</summary>
    public Task WhenFailed => _failed.Task;

    /// <summary>
    /// True when the output keeps the codec's stream headers (H.264's SPS and PPS) in a header of its
    /// own, so the encoder need not repeat them in the stream. A bare stream written to a file has
    /// nowhere else to keep them.
    /// </summary>
    public virtual bool StoresStreamHeaders => false;

    /// <summary>
    /// Tells the output what it is about to receive, before the first frame. A file output ignores
    /// this; a container output needs it to create the stream it will mux into.
    /// </summary>
    public virtual void DescribeStream(VideoStreamInfo stream)
    {
    }

    // The codec's headers (H.264's SPS and PPS) when the encoder only has them after its first frame.
    internal virtual void SetCodecExtraData(ReadOnlySpan<byte> extraData)
    {
    }

    /// <summary>Opens the output. Called by the encoder when recording starts.</summary>
    public virtual void Start() => Recording = true;

    /// <summary>Closes the output. Called by the encoder when recording stops.</summary>
    public virtual void Stop() => Recording = false;

    /// <summary>
    /// Writes one encoded frame. <paramref name="keyframe"/> marks a frame a player can start at;
    /// <paramref name="timestamp"/> is microseconds since the first frame of this recording.
    /// </summary>
    public abstract void OutputFrame(ReadOnlySpan<byte> frame, bool keyframe = true, long? timestamp = null);

    // Keeps the first failure, writing or encoding, and stops the output; the recording reports it when disposed.
    internal void Fail(Exception exception)
    {
        Failure ??= exception;
        try
        {
            Stop();
        }
        catch (Exception)
        {
            // Closing a destination that has just failed can fail too; the first failure is the one to report.
        }
        _failed.TrySetResult();
    }

    /// <summary>Stops the output if it is still running.</summary>
    public virtual void Dispose()
    {
        if (Recording)
            Stop();
    }
}
