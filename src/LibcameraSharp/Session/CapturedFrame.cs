using LibcameraSharp.Advanced;
using Stream = LibcameraSharp.Advanced.Stream;   // libcamera's stream, not System.IO's

namespace LibcameraSharp;

/// <summary>
/// A frame the camera has finished, with the configuration it was captured under. Read it with
/// <see cref="CopyPixels"/>, <see cref="MakeBuffer"/> or <see cref="Metadata"/>, then
/// <see cref="Dispose"/> to hand the buffer back to the camera (which re-queues it if still running).
/// </summary>
/// <remarks>The request is re-queued only if the camera has not been stopped or reconfigured since it completed.</remarks>
internal sealed class CapturedFrame : IDisposable
{
    private readonly CameraSession _camera;
    private readonly int _stopCount;
    private readonly BufferAllocation _allocation;
    private Request? _request;

    internal CapturedFrame(CameraSession camera, Request request, SessionConfiguration config, IReadOnlyDictionary<SessionStream, Stream> streams, int stopCount, BufferAllocation allocation)
    {
        _camera = camera;
        _request = request;
        Config = config;
        Streams = streams;
        _stopCount = stopCount;
        _allocation = allocation;
    }

    /// <summary>The configuration the frame was captured with (a snapshot; later reconfiguration doesn't change it).</summary>
    public SessionConfiguration Config { get; }

    /// <summary>The libcamera stream behind each configured stream.</summary>
    public IReadOnlyDictionary<SessionStream, Stream> Streams { get; }

    /// <summary>The underlying libcamera request, for the lower-level API.</summary>
    /// <exception cref="ObjectDisposedException">The request has been released.</exception>
    public Request Request => _request ?? throw new ObjectDisposedException(nameof(CapturedFrame));

    /// <summary>The frame's sequence number, from the buffer libcamera filled; gaps mean dropped frames.</summary>
    /// <remarks>Not <c>Request.Sequence</c>, which has no gaps when the application is the slow one.</remarks>
    public uint Sequence => Request.Buffer(StreamFor(SessionStream.Capture)).Metadata.Sequence;

    /// <summary>The raw bytes of one stream's buffer, padding included, as a copy.</summary>
    public byte[] MakeBuffer(SessionStream stream = SessionStream.Capture)
    {
        using var mapped = Request.Buffer(StreamFor(stream)).Map();
        var total = 0;
        for (var i = 0; i < mapped.PlaneCount; i++)
            total += mapped[i].Length;
        var data = new byte[total];
        var offset = 0;
        for (var i = 0; i < mapped.PlaneCount; i++)
        {
            mapped[i].CopyTo(data.AsSpan(offset));
            offset += mapped[i].Length;
        }
        return data;
    }

    /// <summary>A copy of one stream's frame as the camera wrote it, with its format, size, stride and colour space.</summary>
    public FramePixels CopyPixels(SessionStream stream = SessionStream.Capture)
    {
        var config = Config[stream] ?? throw new ArgumentException($"The {stream} stream is not configured.", nameof(stream));
        return new FramePixels(MakeBuffer(stream), config.Format!.Value, config.Size!.Value, (int)config.Stride!.Value, Config.ColourSpace);
    }

    /// <summary>What the camera reported for this frame, keyed by control; a snapshot you can keep after disposing the request.</summary>
    public Metadata Metadata => new(Request.Metadata);

    /// <summary>The camera run this frame came from; it changes each time the camera is stopped and started.</summary>
    internal int Run => _stopCount;

    /// <summary>
    /// Another handle on the same frame, for a reader on another thread: the buffer goes back to the
    /// camera only when this one and every other holder have been disposed.
    /// </summary>
    internal CapturedFrame Share()
    {
        var request = Request;
        _camera.Hold(request);
        _allocation.Acquire(request);
        return new CapturedFrame(_camera, request, Config, Streams, _stopCount, _allocation);
    }

    /// <summary>Returns the buffer to the camera; it is re-queued if the camera is still running with this configuration.</summary>
    public void Dispose()
    {
        if (_request is null)
            return;
        var request = _request;
        _request = null;
        _camera.Release(request, _stopCount, _allocation);
    }

    private Stream StreamFor(SessionStream stream) =>
        Streams.TryGetValue(stream, out var found) ? found : throw new ArgumentException($"The {stream} stream is not configured.", nameof(stream));
}
