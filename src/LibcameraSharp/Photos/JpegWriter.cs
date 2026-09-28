using SkiaSharp;

namespace LibcameraSharp;

/// <summary>Writes a frame as JPEG, with EXIF from the frame's metadata.</summary>
/// <remarks>Skia encodes; the EXIF segment is then inserted right after the start-of-image marker.</remarks>
internal static class JpegWriter
{
    /// <summary>The JPEG quality used when none is given.</summary>
    public const int DefaultQuality = 90;

    /// <summary>
    /// Encodes <paramref name="pixels"/> (an RGB or YUV format) and writes it with EXIF to <paramref name="output"/>.
    /// <paramref name="cameraModel"/> goes into the EXIF Model tag;
    /// <paramref name="exifData"/> holds your own tags, which override the generated ones.
    /// </summary>
    public static void Save(FramePixels pixels, Metadata metadata, Stream output, string cameraModel, int quality = DefaultQuality, ExifData? exifData = null)
    {
        using var bitmap = FrameBitmap.FromPixels(pixels);
        Save(bitmap, metadata, output, cameraModel, quality, exifData);
    }

    /// <summary>Encodes an already-made bitmap; see <see cref="Save(FramePixels, Metadata, Stream, string, int, ExifData?)"/>.</summary>
    public static void Save(SKBitmap bitmap, Metadata metadata, Stream output, string cameraModel, int quality = DefaultQuality, ExifData? exifData = null)
    {
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, quality)
                            ?? throw new InvalidOperationException("Skia could not encode the bitmap as JPEG.");
        var jpeg = encoded.AsSpan();
        var exif = ExifSegment.Build(metadata, cameraModel, exifData);
        if (exif.Length == 0)
        {
            output.Write(jpeg);
            return;
        }

        // SOI, then APP1 (marker, big-endian length including the length bytes, payload), then the rest.
        output.Write(jpeg[..2]);
        output.Write([0xFF, 0xE1, (byte)((exif.Length + 2) >> 8), (byte)((exif.Length + 2) & 0xFF)]);
        output.Write(exif);
        output.Write(jpeg[2..]);
    }
}
