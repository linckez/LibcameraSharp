namespace LibcameraSharp;

/// <summary>
/// Writes encoded frames, unwrapped, to any <see cref="Stream"/>. Frames before the first keyframe are
/// dropped, so a player joining late starts cleanly.
/// </summary>
internal sealed class FileOutput : Output
{
    private readonly bool _ownsStream;
    private Stream? _stream;
    private bool _seenKeyframe;

    /// <summary>Writes to <paramref name="stream"/>; it is disposed with this output only when <paramref name="ownsStream"/> is set.</summary>
    public FileOutput(Stream stream, bool ownsStream = false)
    {
        _stream = stream;
        _ownsStream = ownsStream;
    }

    /// <inheritdoc/>
    public override void Start()
    {
        _seenKeyframe = false;
        base.Start();
    }

    /// <inheritdoc/>
    public override void OutputFrame(ReadOnlySpan<byte> frame, bool keyframe = true, long? timestamp = null)
    {
        if (!Recording || _stream is not { } stream)
            return;

        // Start at a keyframe: earlier frames reference pictures the reader never saw.
        _seenKeyframe |= keyframe;
        if (!_seenKeyframe)
            return;

        try
        {
            stream.Write(frame);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or OperationCanceledException)
        {
            Fail(exception);
        }
    }

    /// <inheritdoc/>
    public override void Stop()
    {
        base.Stop();
        _stream?.Flush();
        if (!_ownsStream)
            return;
        _stream?.Dispose();
        _stream = null;
    }
}
