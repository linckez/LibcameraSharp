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
        ColourSpace = colourSpace;
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
    public ColorSpace? ColourSpace { get; }
}

/// <summary>Bytes per pixel of the packed RGB formats, which the photo writers read row by row.</summary>
internal static class PixelLayout
{
    /// <summary>Bytes per pixel for packed formats, or null for planar and compressed ones.</summary>
    public static int? BytesPerPixel(PixelFormat format)
    {
        if (format == PixelFormats.BGR888 || format == PixelFormats.RGB888) return 3;
        if (format == PixelFormats.XBGR8888 || format == PixelFormats.XRGB8888) return 4;
        if (format == PixelFormats.BGR161616 || format == PixelFormats.RGB161616) return 6;
        if (format == PixelFormats.RGB565 || format == PixelFormats.RGB565_BE) return 2;
        return null;
    }
}
