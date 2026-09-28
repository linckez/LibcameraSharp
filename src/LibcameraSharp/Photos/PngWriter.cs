using SkiaSharp;

namespace LibcameraSharp;

/// <summary>Writes a frame as a lossless PNG.</summary>
internal static class PngWriter
{
    /// <summary>Encodes <paramref name="pixels"/> (an RGB or YUV format) to <paramref name="output"/>.</summary>
    public static void Save(FramePixels pixels, Stream output)
    {
        using var bitmap = FrameBitmap.FromPixels(pixels);
        Save(bitmap, output);
    }

    /// <summary>Encodes an already-made bitmap.</summary>
    public static void Save(SKBitmap bitmap, Stream output)
    {
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100)
                            ?? throw new InvalidOperationException("Skia could not encode the bitmap as PNG.");
        output.Write(encoded.AsSpan());
    }
}
