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
    private readonly SessionStream _streamName;

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
        _streamName = stream;
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

    /// <summary>
    /// Encodes the frame as a JPEG, such as for a live view: no EXIF, and done before this returns, so the frame can
    /// be disposed straight after.
    /// </summary>
    /// <param name="quality">JPEG quality, 1 to 100. The default suits a live view; a photo's is 90.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quality"/> isn't 1 to 100.</exception>
    /// <exception cref="ObjectDisposedException">The frame has been disposed.</exception>
    /// <exception cref="NotSupportedException">The frame's format is neither RGB nor YUV.</exception>
    public byte[] ToJpeg(int quality = DefaultJpegQuality)
    {
        if (quality is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(quality), quality, "JPEG quality runs from 1 to 100.");
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var bitmap = FrameBitmap.FromPixels(_request is { } request ? request.CopyPixels(_streamName) : JoinPlanes());
        return JpegWriter.Encode(bitmap, quality);
    }

    // A live view's JPEG quality when none is given, lighter than a photo's.
    private const int DefaultJpegQuality = 50;

    // A frame built from arrays, packed into one buffer the way a camera's frame is laid out: the first plane's
    // stride for every row, half of it for planar YUV420's chroma. Arrays given their own strides are repacked, and
    // an odd height gets its last chroma row too, since the converter rounds the chroma's rows up.
    private FramePixels JoinPlanes()
    {
        var stride = _strides![0];
        var planarYuv = _format == PixelFormats.YUV420 || _format == PixelFormats.YVU420;
        var height = (int)_size.Height;
        var packed = new List<byte>();
        for (var i = 0; i < _planes!.Length; i++)
        {
            var (packedStride, rows) = i == 0 ? (stride, height) : (planarYuv ? stride / 2 : stride, (height + 1) / 2);
            var row = new byte[packedStride];
            for (var y = 0; y < rows; y++)
            {
                Array.Clear(row);
                var from = y * _strides[i];
                if (from < _planes[i].Length)
                    _planes[i].AsSpan(from, Math.Min(Math.Min(packedStride, _strides[i]), _planes[i].Length - from)).CopyTo(row);
                packed.AddRange(row);
            }
        }
        packed.AddRange(new byte[stride]);                                     // and an odd width's last chroma byte
        return new FramePixels([.. packed], _format, _size, stride, colourSpace: null);
    }

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
