namespace LibcameraSharp;

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
