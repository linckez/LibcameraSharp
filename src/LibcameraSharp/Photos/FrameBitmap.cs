using FFmpeg.AutoGen;
using SkiaSharp;

namespace LibcameraSharp;

/// <summary>
/// Turns a frame into the Skia bitmap the JPEG and PNG writers encode. The two 32-bit RGB formats are
/// laid out as Skia keeps pixels, so Skia reads them as they are; everything else goes through FFmpeg's
/// converter, which reads the camera's rows at their stride and handles every RGB and YUV layout. For
/// YUV, the frame's own colour space (matrix and range) tells FFmpeg how it was encoded.
/// </summary>
internal static unsafe class FrameBitmap
{
    // libswscale's colour-space settings: a matrix is four coefficients; range 1 is full (JPEG), 0 limited (MPEG);
    // brightness, contrast and saturation are 16.16 fixed point, so 0, 1.0 and 1.0 leave the picture as it is.
    private const uint CoefficientCount = 4;
    private const int FullRange = 1, LimitedRange = 0;
    private const int NeutralBrightness = 0, NeutralContrast = 1 << 16, NeutralSaturation = 1 << 16;

    /// <summary>A bitmap holding <paramref name="pixels"/> as 8-bit RGB: over the pixels themselves for the 32-bit formats, converted to BGRA otherwise.</summary>
    /// <exception cref="NotSupportedException">FFmpeg has no name for the frame's format (raw, compressed or 16-bit formats).</exception>
    public static SKBitmap FromPixels(FramePixels pixels)
    {
        // Already in a layout Skia reads: no conversion, and FFmpeg isn't needed.
        if (SkiaLayout(pixels.Format) is { } colourType)
            return Installed(pixels, colourType);
        return Converted(pixels);
    }

    // Any RGB or YUV layout, converted to BGRA by FFmpeg.
    internal static SKBitmap Converted(FramePixels pixels)
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
            if (!isRgb && pixels.ColorSpace is { } colourSpace)
            {
                var source4 = ffmpeg.sws_getCoefficients(Matrix(colourSpace));
                var coefficients = new int_array4();
                for (uint i = 0; i < CoefficientCount; i++)
                    coefficients[i] = source4[i];
                ffmpeg.sws_setColorspaceDetails(scaler, coefficients, colourSpace.Range == ColorSpace.RangeKind.Full ? FullRange : LimitedRange,
                                                coefficients, FullRange, NeutralBrightness, NeutralContrast, NeutralSaturation);
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

    // libcamera names a pixel as a little-endian number, so XRGB8888 is B, G, R, X in memory (Skia's
    // Bgra8888, alpha ignored as opaque) and XBGR8888 is R, G, B, X (Skia's Rgb888x).
    private static SKColorType? SkiaLayout(PixelFormat format)
    {
        if (format == PixelFormats.XRGB8888) return SKColorType.Bgra8888;
        if (format == PixelFormats.XBGR8888) return SKColorType.Rgb888x;
        return null;
    }

    // A bitmap over the pixels themselves, rows at their stride; the array stays pinned until Skia lets go.
    private static SKBitmap Installed(FramePixels pixels, SKColorType colourType)
    {
        var info = new SKImageInfo((int)pixels.Size.Width, (int)pixels.Size.Height, colourType, SKAlphaType.Opaque);
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(pixels.Data, System.Runtime.InteropServices.GCHandleType.Pinned);
        var bitmap = new SKBitmap();
        if (!bitmap.InstallPixels(info, pin.AddrOfPinnedObject(), pixels.Stride, (_, handle) => ((System.Runtime.InteropServices.GCHandle)handle!).Free(), pin))
        {
            pin.Free();
            bitmap.Dispose();
            throw new NotSupportedException($"Skia could not read {pixels.Format} at {pixels.Size} with a stride of {pixels.Stride}.");
        }
        return bitmap;
    }

    // FFmpeg's name for the camera's YCbCr matrix; BT.601 when the camera reports none.
    private static int Matrix(ColorSpace colourSpace) => colourSpace.YcbcrEncoding switch
    {
        ColorSpace.YcbcrEncodingKind.Rec709 => ffmpeg.SWS_CS_ITU709,
        ColorSpace.YcbcrEncodingKind.Rec2020 => ffmpeg.SWS_CS_BT2020,
        _ => ffmpeg.SWS_CS_ITU601,
    };
}
