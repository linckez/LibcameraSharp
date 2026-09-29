namespace LibcameraSharp;

/// <summary>
/// One frame, straight from the camera, while you hold it.
/// </summary>
/// <remarks>
/// The pixels are not copied: <see cref="Plane"/> points at the camera's buffer, and the camera has
/// only a few. Dispose each frame (<c>using (frame)</c>) or the camera stalls.
/// </remarks>
public sealed class VideoFrame : IDisposable
{
    // A frame from the camera: its request, its mapped buffer and its stream's layout.
    private readonly CapturedFrame? _request;
    private readonly MappedFrame? _mapped;
    private readonly StreamDescription? _stream;

    // A frame built from arrays, by LibcameraSharpModelFactory: no buffer to give back.
    private readonly byte[][]? _planes;
    private readonly int[]? _strides;
    private readonly Size _size;
    private readonly PixelFormat _format;
    private readonly uint _sequence;
    private readonly CaptureMetadata? _metadata;

    private bool _disposed;

    internal VideoFrame(CapturedFrame request, SessionStream stream)
    {
        _request = request;
        _mapped = new MappedFrame(request, stream);
        _stream = request.Config[stream]!;
    }

    internal VideoFrame(byte[][] planes, int[] strides, Size size, PixelFormat format, uint sequence, CaptureMetadata metadata) =>
        (_planes, _strides, _size, _format, _sequence, _metadata) = (planes, strides, size, format, sequence, metadata);

    /// <summary>The frame's size in pixels.</summary>
    public Size Size => _mapped?.Size ?? _size;

    /// <summary>The frame's pixel format.</summary>
    public PixelFormat Format => _mapped?.Format ?? _format;

    /// <summary>
    /// The frame's sequence number. Gaps mean frames the camera produced and this loop never saw.
    /// </summary>
    /// <remarks><see cref="CameraDevice.FramesDropped"/> counts the gaps for you.</remarks>
    public uint Sequence => _request?.Sequence ?? _sequence;

    /// <summary>What the camera did for this frame.</summary>
    public CaptureMetadata Metadata => _request is { } request ? new(request.Metadata) : _metadata!;

    /// <summary>How many planes this format has: one for RGB, three for YUV420.</summary>
    public int PlaneCount => _mapped?.PlaneCount ?? _planes!.Length;

    /// <summary>
    /// One plane's bytes, not copied. Plane 0 of a YUV format is the luma, which is what motion
    /// detection and most models want.
    /// </summary>
    /// <remarks>Valid until the frame is disposed; use <see cref="ToArray"/> to keep the bytes.</remarks>
    /// <exception cref="ObjectDisposedException">The frame has been disposed.</exception>
    public ReadOnlyMemory<byte> Plane(int plane = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _mapped?.PlaneMemory(plane) ?? _planes![plane];
    }

    /// <summary>
    /// Bytes per row of a plane, including any padding. A row is not always <c>width</c> bytes, and
    /// treating it as if it were shears the image.
    /// </summary>
    public int Stride(int plane = 0)
    {
        if (_mapped is null)
            return _strides![plane];
        if (plane == 0)
            return (int)_mapped.Stride;

        // Planar YUV420 keeps its U and V rows at half the luma's stride.
        var stride = (int)_stream!.Stride!.Value;
        return _stream.Format == PixelFormats.YUV420 || _stream.Format == PixelFormats.YVU420 ? stride / 2 : stride;
    }

    /// <summary>Copies a plane, for when you need the bytes to outlive the frame.</summary>
    public byte[] ToArray(int plane = 0) => Plane(plane).ToArray();

    /// <summary>Returns the frame's buffer to the camera.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _mapped?.Dispose();
        _request?.Dispose();
    }
}
