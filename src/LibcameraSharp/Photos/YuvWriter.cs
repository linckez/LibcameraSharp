namespace LibcameraSharp;

/// <summary>
/// Dumps a frame's pixels to a file with no container: planar YUV420 as Y then U then V planes,
/// or RGB rows, each without padding, as <c>ffmpeg -f rawvideo</c> reads them.
/// </summary>
internal static class YuvWriter
{
    /// <summary>Writes the frame's pixels as <paramref name="format"/>. YUV420 frames must have even width and height.</summary>
    /// <exception cref="NotSupportedException">The pixels are not in <paramref name="format"/>.</exception>
    public static void Save(FramePixels pixels, PhotoEncoding format, Stream output)
    {
        if (format == PhotoEncoding.Yuv420)
        {
            if (pixels.Format == PixelFormats.YUV420)
            {
                SaveYuv420(pixels, output);
                return;
            }
            throw new NotSupportedException($"This photo's pixels are {pixels.Format}, not YUV420, so they can't be saved as YUV420.");
        }
        if (PixelLayout.BytesPerPixel(pixels.Format) is (3 or 6) and var bytesPerPixel)
        {
            // rgb24 / rgb48: each row as stored, without its padding.
            var rowLength = (int)pixels.Size.Width * bytesPerPixel;
            for (var y = 0; y < (int)pixels.Size.Height; y++)
                output.Write(pixels.Data.AsSpan(y * pixels.Stride, rowLength));
            return;
        }
        throw new NotSupportedException($"This photo was taken in {pixels.Format}, not RGB. Take it with a BGR888 or RGB888 capture format to save it as RGB24.");
    }

    // Y rows, then U and V rows at half the width, half the height and half the stride.
    private static void SaveYuv420(FramePixels pixels, Stream output)
    {
        var (w, h) = ((int)pixels.Size.Width, (int)pixels.Size.Height);
        if ((w & 1) != 0 || (h & 1) != 0)
            throw new NotSupportedException("both width and height must be even");
        var stride = pixels.Stride;
        var data = pixels.Data.AsSpan();
        for (var y = 0; y < h; y++)
            output.Write(data.Slice(y * stride, w));
        var chromaStride = stride / 2;
        var uOffset = stride * h;
        for (var y = 0; y < h / 2; y++)
            output.Write(data.Slice(uOffset + y * chromaStride, w / 2));
        var vOffset = uOffset + chromaStride * (h / 2);
        for (var y = 0; y < h / 2; y++)
            output.Write(data.Slice(vOffset + y * chromaStride, w / 2));
    }
}
