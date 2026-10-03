using SkiaSharp;

namespace LibcameraSharp;

/// <summary>Writes a frame as JPEG, with EXIF from the frame's metadata.</summary>
/// <remarks>Skia encodes; the EXIF segment is then inserted right after the start-of-image marker.</remarks>
internal static class JpegWriter
{
    /// <summary>The JPEG quality used when none is given.</summary>
    public const int DefaultQuality = 90;

    // A JPEG starts with the two-byte start-of-image marker; an EXIF block goes in the APP1 segment right after it
    // (ITU T.81, Table B.1), whose two-byte length counts itself.
    private const int StartOfImageLength = 2, SegmentLengthSize = 2, MaxSegmentLength = ushort.MaxValue;
    private const byte MarkerPrefix = 0xFF, App1 = 0xE1;

    /// <summary>
    /// Encodes <paramref name="pixels"/> (an RGB or YUV format) and writes it with EXIF to <paramref name="output"/>.
    /// <paramref name="cameraModel"/> goes into the EXIF Model tag, when there is one;
    /// <paramref name="exifData"/> holds your own tags, which override the generated ones.
    /// </summary>
    public static void Save(FramePixels pixels, Metadata metadata, Stream output, string? cameraModel, int quality = DefaultQuality, ExifData? exifData = null)
    {
        using var bitmap = FrameBitmap.FromPixels(pixels);
        Save(bitmap, metadata, output, cameraModel, quality, exifData);
    }

    /// <summary>Encodes <paramref name="bitmap"/> as a JPEG with no EXIF, such as a live view's frame.</summary>
    public static byte[] Encode(SKBitmap bitmap, int quality)
    {
        using var encoded = EncodeData(bitmap, quality);
        return encoded.ToArray();
    }

    private static SKData EncodeData(SKBitmap bitmap, int quality) =>
        bitmap.Encode(SKEncodedImageFormat.Jpeg, quality) ?? throw new InvalidOperationException("Skia could not encode the bitmap as JPEG.");

    /// <summary>Encodes an already-made bitmap; see <see cref="Save(FramePixels, Metadata, Stream, string, int, ExifData?)"/>.</summary>
    public static void Save(SKBitmap bitmap, Metadata metadata, Stream output, string? cameraModel, int quality = DefaultQuality, ExifData? exifData = null)
    {
        using var encoded = EncodeData(bitmap, quality);
        var jpeg = encoded.AsSpan();
        var exif = ExifSegment.Build(metadata, cameraModel, exifData);
        if (exif.Length == 0)
        {
            output.Write(jpeg);
            return;
        }

        // SOI, then APP1 (marker, big-endian length including the length bytes, payload), then the rest. The length is
        // 16 bits, so a longer segment can't be written; it's refused rather than written corrupt.
        var segmentLength = exif.Length + SegmentLengthSize;
        if (segmentLength > MaxSegmentLength)
            throw new InvalidOperationException(
                $"The EXIF data is {exif.Length} bytes; a JPEG holds at most {MaxSegmentLength - SegmentLengthSize}. Shorten the EXIF text.");
        output.Write(jpeg[..StartOfImageLength]);
        output.Write([MarkerPrefix, App1, (byte)(segmentLength >> 8), (byte)(segmentLength & 0xFF)]);
        output.Write(exif);
        output.Write(jpeg[StartOfImageLength..]);
    }
}
