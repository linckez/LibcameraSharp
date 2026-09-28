using LibcameraSharp.Advanced;

namespace LibcameraSharp;

/// <summary>
/// The camera's own buffer for one stream of a completed request, mapped read-only for the lifetime
/// of this object — no copy. Rows are <see cref="Stride"/> bytes apart. Dispose before disposing the request.
/// </summary>
internal sealed class MappedFrame : IDisposable
{
    private readonly MappedFrameBuffer _mapped;

    /// <summary>Maps the buffer of <paramref name="stream"/>.</summary>
    /// <exception cref="ArgumentException">The stream is not part of the request's configuration.</exception>
    public MappedFrame(CapturedFrame request, SessionStream stream = SessionStream.Capture)
    {
        var config = request.Config[stream] ?? throw new ArgumentException($"The {stream} stream is not configured.", nameof(stream));
        Format = config.Format!.Value;
        Size = config.Size!.Value;
        Stride = config.Stride!.Value;
        _mapped = request.Request.Buffer(request.Streams[stream]).Map();
    }

    /// <summary>The pixel format the bytes are in.</summary>
    public PixelFormat Format { get; }

    /// <summary>Frame size in pixels.</summary>
    public Size Size { get; }

    /// <summary>Bytes from the start of one row to the next in <see cref="Data"/>, padding included.</summary>
    public uint Stride { get; }

    /// <summary>Number of memory planes libcamera delivered the frame in (1 for packed formats; 2–3 for planar YUV on some pipelines).</summary>
    public int PlaneCount => _mapped.PlaneCount;

    /// <summary>The first plane's bytes as the camera wrote them, rows padded to <see cref="Stride"/>.</summary>
    public ReadOnlySpan<byte> Data => _mapped[0];

    /// <summary>One memory plane; see <see cref="PlaneCount"/>.</summary>
    public ReadOnlySpan<byte> Plane(int index) => _mapped[index];

    /// <summary>
    /// One plane as memory rather than a span, for code that carries it across an <c>await</c>.
    /// </summary>
    /// <remarks>Still a view of the mapping, so it stops being valid when this is disposed.</remarks>
    public ReadOnlyMemory<byte> PlaneMemory(int index) => _mapped.PlaneMemory(index);

    /// <summary>Every plane of the frame joined, padding included — the bytes as the camera wrote them.</summary>
    public byte[] ToBuffer()
    {
        var total = 0;
        for (var i = 0; i < _mapped.PlaneCount; i++)
            total += _mapped[i].Length;
        var buffer = new byte[total];
        var offset = 0;
        for (var i = 0; i < _mapped.PlaneCount; i++)
        {
            _mapped[i].CopyTo(buffer.AsSpan(offset));
            offset += _mapped[i].Length;
        }
        return buffer;
    }

    /// <summary>Unmaps the buffer.</summary>
    public void Dispose() => _mapped.Dispose();
}
