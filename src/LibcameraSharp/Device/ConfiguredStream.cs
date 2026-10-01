namespace LibcameraSharp;

/// <summary>One stream as libcamera configured it.</summary>
public sealed record ConfiguredStream
{
    internal ConfiguredStream(Size size, PixelFormat format, uint? stride, uint frameSize) =>
        (Size, Format, Stride, FrameSize) = (size, format, stride, frameSize);

    /// <summary>Its size in pixels.</summary>
    public Size Size { get; }

    /// <summary>Its pixel format.</summary>
    public PixelFormat Format { get; }

    /// <summary>Bytes from the start of one row to the next, or null for a compressed format such as MJPEG, which has no rows.</summary>
    public uint? Stride { get; }

    /// <summary>Bytes in one frame.</summary>
    public uint FrameSize { get; }
}
