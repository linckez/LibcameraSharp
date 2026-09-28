using FFmpeg.AutoGen;
using SkiaSharp;

namespace LibcameraSharp;

/// <summary>
/// Turns a frame into the Skia bitmap the JPEG and PNG writers encode, through FFmpeg's converter:
/// it reads the camera's rows at their stride and handles every RGB and YUV layout. For YUV, the
/// frame's own colour space (matrix and range) tells FFmpeg how it was encoded.
/// </summary>
internal static unsafe class FrameBitmap
{
    /// <summary>A bitmap holding <paramref name="pixels"/> as 8-bit BGRA.</summary>
    /// <exception cref="NotSupportedException">FFmpeg has no name for the frame's format (raw, compressed or 16-bit formats).</exception>
    public static SKBitmap FromPixels(FramePixels pixels)
    {
        Libav.Initialise();
        var source = LibavPixelFormats.From(pixels.Format)
            ?? throw new NotSupportedException($"{pixels.Format} can't be made into an image; use an RGB or YUV format.");
        var (width, height) = ((int)pixels.Size.Width, (int)pixels.Size.Height);

        var scaler = ffmpeg.sws_getContext(width, height, source, width, height, AVPixelFormat.AV_PIX_FMT_BGRA,
                                           ffmpeg.SWS_BILINEAR, null, null, null);
        if (scaler is null)
            throw new NotSupportedException($"FFmpeg cannot convert {pixels.Format} to RGB.");

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        try
        {
            // For YUV, tell FFmpeg the matrix and range the camera encoded with; RGB out is full range.
            var isRgb = (ffmpeg.av_pix_fmt_desc_get(source)->flags & ffmpeg.AV_PIX_FMT_FLAG_RGB) != 0;
            if (!isRgb && pixels.ColourSpace is { } colourSpace)
            {
                var source4 = ffmpeg.sws_getCoefficients(Matrix(colourSpace));
                var coefficients = new int_array4();
                for (uint i = 0; i < 4; i++)
                    coefficients[i] = source4[i];
                ffmpeg.sws_setColorspaceDetails(scaler, coefficients, colourSpace.Range == ColorSpace.RangeKind.Full ? 1 : 0,
                                                coefficients, 1, 0, 1 << 16, 1 << 16);
            }

            var target = new byte_ptrArray8();
            var targetStrides = new int_array8();
            target[0] = (byte*)bitmap.GetPixels();
            targetStrides[0] = bitmap.RowBytes;

            fixed (byte* data = pixels.Data)
            {
                var planes = LibavPixelFormats.Planes(data, source, pixels.Stride, height, out var strides);
                ffmpeg.sws_scale(scaler, planes, strides, 0, height, target, targetStrides);
            }
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
        finally
        {
            ffmpeg.sws_freeContext(scaler);
        }
    }

    // FFmpeg's name for the camera's YCbCr matrix; BT.601 when the camera reports none.
    private static int Matrix(ColorSpace colourSpace) => colourSpace.YcbcrEncoding switch
    {
        ColorSpace.YcbcrEncodingKind.Rec709 => ffmpeg.SWS_CS_ITU709,
        ColorSpace.YcbcrEncodingKind.Rec2020 => ffmpeg.SWS_CS_BT2020,
        _ => ffmpeg.SWS_CS_ITU601,
    };
}
