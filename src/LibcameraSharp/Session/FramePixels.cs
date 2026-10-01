namespace LibcameraSharp;

/// <summary>
/// A copy of a frame's bytes exactly as the camera wrote them (every plane, rows padded to
/// <see cref="Stride"/>), with what's needed to read them: format, size and colour space.
/// </summary>
internal sealed class FramePixels
{
    internal FramePixels(byte[] data, PixelFormat format, Size size, int stride, ColorSpace? colourSpace)
    {
        Data = data;
        Format = format;
        Size = size;
        Stride = stride;
        ColorSpace = colourSpace;
    }

    /// <summary>The bytes: the first plane's rows, each <see cref="Stride"/> long, then any further planes.</summary>
    public byte[] Data { get; }

    /// <summary>The pixel format the bytes are in.</summary>
    public PixelFormat Format { get; }

    /// <summary>The frame's size in pixels.</summary>
    public Size Size { get; }

    /// <summary>Bytes from the start of one row of the first plane to the next, padding included.</summary>
    public int Stride { get; }

    /// <summary>The colour space the camera reported for these pixels, when known.</summary>
    public ColorSpace? ColorSpace { get; }
}
