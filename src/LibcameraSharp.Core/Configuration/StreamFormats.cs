using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Core;

/// <summary>The formats and sizes a stream can be configured with (<see cref="StreamConfiguration.Formats"/>).</summary>
public unsafe sealed class StreamFormats
{
    private readonly libcamera_stream_formats* _formats;

    internal StreamFormats(libcamera_stream_formats* formats) => _formats = formats;

    /// <summary>Pixel formats the stream supports.</summary>
    public IReadOnlyList<PixelFormat> PixelFormats
    {
        get
        {
            var list = NativeMethods.libcamera_stream_formats_pixel_formats(_formats);
            try
            {
                var count = (int)NativeMethods.libcamera_pixel_formats_size(list);
                var result = new PixelFormat[count];
                for (var i = 0; i < count; i++)
                {
                    var f = NativeMethods.libcamera_pixel_formats_get(list, (nuint)i);
                    result[i] = new PixelFormat(f.fourcc, f.modifier);
                }
                return result;
            }
            finally
            {
                NativeMethods.libcamera_pixel_formats_destroy(list);
            }
        }
    }

    /// <summary>The discrete sizes supported for <paramref name="format"/>; empty when only a <see cref="Range"/> is advertised.</summary>
    public IReadOnlyList<Size> Sizes(PixelFormat format)
    {
        var native = new libcamera_pixel_format { fourcc = format.Fourcc, modifier = format.Modifier };
        var list = NativeMethods.libcamera_stream_formats_sizes(_formats, &native);
        try
        {
            var count = (int)NativeMethods.libcamera_sizes_size(list);
            var result = new Size[count];
            for (var i = 0; i < count; i++)
            {
                var s = NativeMethods.libcamera_sizes_at(list, (nuint)i);
                result[i] = new Size(s->width, s->height);
            }
            return result;
        }
        finally
        {
            NativeMethods.libcamera_sizes_destroy(list);
        }
    }

    /// <summary>The size range supported for <paramref name="format"/>.</summary>
    public SizeRange Range(PixelFormat format)
    {
        var native = new libcamera_pixel_format { fourcc = format.Fourcc, modifier = format.Modifier };
        var r = NativeMethods.libcamera_stream_formats_range(_formats, &native);
        return new SizeRange(new Size(r.min.width, r.min.height), new Size(r.max.width, r.max.height), r.hStep, r.vStep);
    }
}
