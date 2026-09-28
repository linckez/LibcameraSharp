using FFmpeg.AutoGen;

namespace LibcameraSharp;

/// <summary>libcamera's pixel formats by the names FFmpeg gives the same memory layouts.</summary>
internal static class LibavPixelFormats
{
    /// <summary>FFmpeg's name for <paramref name="format"/>, or null when FFmpeg has none.</summary>
    public static AVPixelFormat? From(PixelFormat format)
    {
        if (format == PixelFormats.YUV420) return AVPixelFormat.AV_PIX_FMT_YUV420P;
        if (format == PixelFormats.NV12) return AVPixelFormat.AV_PIX_FMT_NV12;
        if (format == PixelFormats.NV21) return AVPixelFormat.AV_PIX_FMT_NV21;
        if (format == PixelFormats.YUYV) return AVPixelFormat.AV_PIX_FMT_YUYV422;
        if (format == PixelFormats.YVYU) return AVPixelFormat.AV_PIX_FMT_YVYU422;
        if (format == PixelFormats.UYVY) return AVPixelFormat.AV_PIX_FMT_UYVY422;
        if (format == PixelFormats.BGR888) return AVPixelFormat.AV_PIX_FMT_RGB24;      // libcamera's BGR888 is R,G,B in memory
        if (format == PixelFormats.RGB888) return AVPixelFormat.AV_PIX_FMT_BGR24;
        if (format == PixelFormats.XBGR8888) return AVPixelFormat.AV_PIX_FMT_RGB0;     // the X byte is unused, not alpha
        if (format == PixelFormats.XRGB8888) return AVPixelFormat.AV_PIX_FMT_BGR0;
        return null;
    }

    /// <summary>Where each plane of a frame starts and how long its rows are, as FFmpeg expects them.</summary>
    /// <param name="buffer">The frame's first byte.</param>
    /// <param name="format">The frame's layout in FFmpeg's terms.</param>
    /// <param name="stride">Bytes per row of the first plane.</param>
    /// <param name="height">Rows in the first plane.</param>
    /// <param name="strides">Bytes per row of each plane.</param>
    public static unsafe byte_ptrArray8 Planes(byte* buffer, AVPixelFormat format, int stride, int height, out int_array8 strides)
    {
        var planes = new byte_ptrArray8();
        strides = new int_array8();
        planes[0] = buffer;
        strides[0] = stride;

        // Semi-planar: one plane of interleaved chroma after the luma, with the same row length.
        if (format is AVPixelFormat.AV_PIX_FMT_NV12 or AVPixelFormat.AV_PIX_FMT_NV21)
        {
            planes[1] = buffer + stride * height;
            strides[1] = stride;
        }
        // Planar YUV420: U then V after the luma, each at half the row length and half the rows.
        else if (format == AVPixelFormat.AV_PIX_FMT_YUV420P)
        {
            var chromaStride = stride / 2;
            planes[1] = buffer + stride * height;
            strides[1] = chromaStride;
            planes[2] = planes[1] + chromaStride * (height / 2);
            strides[2] = chromaStride;
        }
        return planes;
    }
}
