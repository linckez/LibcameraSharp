using System.Text;

namespace LibcameraSharp;

/// <summary>TIFF field types (TIFF 6.0 §2), which EXIF uses unchanged and libexif calls formats.</summary>
internal enum TiffType : ushort
{
    Byte = 1, Ascii = 2, Short = 3, Long = 4, Rational = 5, SByte = 6, Undefined = 7,
    SShort = 8, SLong = 9, SRational = 10, Float = 11, Double = 12,
}

/// <summary>The EXIF tags the SDK writes. Each knows its directory; the value's type is the one the <c>Set</c> overload called takes.</summary>
internal enum ExifTag : ushort
{
    /// <summary>Camera manufacturer (IFD0, ASCII).</summary>
    Make = 271,
    /// <summary>Camera model (IFD0, ASCII).</summary>
    Model = 272,
    /// <summary>Software that wrote the file (IFD0, ASCII).</summary>
    Software = 305,
    /// <summary>File change time, <c>YYYY:MM:DD HH:MM:SS</c> (IFD0, ASCII).</summary>
    DateTime = 306,
    /// <summary>Person who created the image (IFD0, ASCII).</summary>
    Artist = 315,
    /// <summary>Copyright notice (IFD0, ASCII).</summary>
    Copyright = 33432,
    /// <summary>Free-text description (IFD0, ASCII).</summary>
    ImageDescription = 270,
    /// <summary>Exposure time in seconds (EXIF, rational).</summary>
    ExposureTime = 33434,
    /// <summary>ISO speed (EXIF, short).</summary>
    IsoSpeedRatings = 34855,
    /// <summary>When the picture was taken (EXIF, ASCII).</summary>
    DateTimeOriginal = 36867,
    /// <summary>When the picture was stored (EXIF, ASCII).</summary>
    DateTimeDigitized = 36868,
    /// <summary>Distance to the subject in metres (EXIF, rational).</summary>
    SubjectDistance = 37382,
    /// <summary>User comment (EXIF, undefined bytes behind an 8-byte character code).</summary>
    UserComment = 37510,
}

/// <summary>
/// Descriptive EXIF tags to write into a saved JPEG, on top of the ones the camera's metadata provides; yours win on
/// conflict: <c>new ExifData { Artist = "A. Rossi", UserComment = "55.68,12.57" }</c>.
/// </summary>
/// <remarks>
/// Only the tags a camera can't know are here. What the camera measured (exposure time, ISO, subject distance) always
/// comes from the frame; the dates are when the photo is saved. Text is written as UTF-8, so names keep their letters.
/// </remarks>
public sealed record ExifData
{
    /// <summary>Person who created the image.</summary>
    public string? Artist { get; init; }

    /// <summary>Copyright notice.</summary>
    public string? Copyright { get; init; }

    /// <summary>Free-text description of the image.</summary>
    public string? ImageDescription { get; init; }

    /// <summary>Camera manufacturer, replacing the SDK's ("Raspberry Pi" on a Raspberry Pi).</summary>
    public string? Make { get; init; }

    /// <summary>Camera model, replacing the one libcamera reports.</summary>
    public string? Model { get; init; }

    /// <summary>Software that wrote the file, replacing the SDK's name and version.</summary>
    public string? Software { get; init; }

    /// <summary>A comment of your own, such as a location or a batch number.</summary>
    public string? UserComment { get; init; }

    // Writes the tags that are set over the camera's.
    internal void WriteTo(ExifTagValues tags)
    {
        if (Artist is { } artist)
            tags.Set(ExifTag.Artist, artist);
        if (Copyright is { } copyright)
            tags.Set(ExifTag.Copyright, copyright);
        if (ImageDescription is { } description)
            tags.Set(ExifTag.ImageDescription, description);
        if (Make is { } make)
            tags.Set(ExifTag.Make, make);
        if (Model is { } model)
            tags.Set(ExifTag.Model, model);
        if (Software is { } software)
            tags.Set(ExifTag.Software, software);
        if (UserComment is { } comment)
            tags.SetComment(comment);
    }
}

/// <summary>The tags one file gets, each with its TIFF type, count and bytes, as libexif takes them.</summary>
internal sealed class ExifTagValues
{
    public Dictionary<ExifTag, (TiffType Type, uint Count, byte[] Value)> Values { get; } = [];

    /// <summary>
    /// Sets a text tag. EXIF names the type ASCII, but the bytes are UTF-8, as rpicam-apps writes a user's text
    /// (<c>image/jpeg.cpp</c>, <c>exif_set_string</c>): plain ASCII is unchanged, and other letters survive.
    /// </summary>
    public void Set(ExifTag tag, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        Values[tag] = (TiffType.Ascii, (uint)bytes.Length, bytes);
    }

    /// <summary>Sets a short (16-bit) tag such as <see cref="ExifTag.IsoSpeedRatings"/>.</summary>
    public void Set(ExifTag tag, ushort value) => Values[tag] = (TiffType.Short, 1, BitConverter.GetBytes(value));

    /// <summary>Sets a rational tag such as <see cref="ExifTag.ExposureTime"/> (e.g. <c>(1, 250)</c> for 1/250 s).</summary>
    public void Set(ExifTag tag, uint numerator, uint denominator)
    {
        var bytes = new byte[8];
        BitConverter.GetBytes(numerator).CopyTo(bytes, 0);
        BitConverter.GetBytes(denominator).CopyTo(bytes, 4);
        Values[tag] = (TiffType.Rational, 1, bytes);
    }

    /// <summary>
    /// Sets <see cref="ExifTag.UserComment"/>: UNDEFINED bytes behind the 8-byte character code EXIF requires (EXIF 2.3
    /// §4.6.5, the UserComment character codes; ImageSharp writes it the same way, as an encoded string). Plain ASCII
    /// gets the ASCII code; anything else the UNICODE code with UCS-2 in the file's byte order, which the EXIF shim sets
    /// to little-endian (<c>exif_shim.c</c>).
    /// </summary>
    public void SetComment(string value)
    {
        byte[] bytes = Ascii.IsValid(value)
            ? [.. "ASCII\0\0\0"u8, .. Encoding.ASCII.GetBytes(value)]
            : [.. "UNICODE\0"u8, .. Encoding.Unicode.GetBytes(value)];
        Values[ExifTag.UserComment] = (TiffType.Undefined, (uint)bytes.Length, bytes);
    }

    /// <summary>Whether <paramref name="tag"/> belongs in the EXIF sub-directory rather than IFD0.</summary>
    public static bool IsExifIfd(ExifTag tag) => tag is ExifTag.ExposureTime or ExifTag.IsoSpeedRatings or ExifTag.DateTimeOriginal
        or ExifTag.DateTimeDigitized or ExifTag.SubjectDistance or ExifTag.UserComment;
}

/// <summary>
/// The EXIF block for a JPEG's APP1 segment: what the camera reported for the frame, with the
/// user's <see cref="ExifData"/> on top.
/// </summary>
/// <remarks>
/// ISO is analogue × digital gain × 100, so ISO 100 means no gain. The dates are when the photo is
/// written, and SubjectDistance is 1/LensPosition when the lens reports one.
/// </remarks>
internal static class ExifSegment
{
    /// <summary>
    /// What <see cref="ExifTag.Make"/> says: "Raspberry Pi" on a Raspberry Pi, and nothing elsewhere,
    /// since libcamera reports a camera's model but not its maker.
    /// </summary>
    public static string? Maker => PlatformDetection.Current is Platform.Vc4 or Platform.Pisp ? "Raspberry Pi" : null;

    /// <summary>What <see cref="ExifTag.Software"/> says.</summary>
    public static string Software { get; } = "LibcameraSharp " + (typeof(ExifSegment).Assembly.GetName().Version?.ToString(3) ?? "");

    /// <summary>
    /// Builds the segment payload (<c>Exif\0\0</c> + TIFF), or an empty array when the metadata has
    /// no gains and <paramref name="exifData"/> is null, or when libexif isn't installed.
    /// </summary>
    public static byte[] Build(Metadata metadata, string cameraModel, ExifData? exifData = null, DateTime? now = null)
    {
        var hasGains = metadata.TryGet(Controls.AnalogueGain, out var analogueGain) & metadata.TryGet(Controls.DigitalGain, out var digitalGain);
        if (!hasGains && exifData is null)
            return [];
        if (!Libexif.IsAvailable)
            return [];

        // The tags from the camera's metadata first; the user's values replace them tag by tag.
        var tags = new ExifTagValues();
        if (hasGains)
        {
            var timestamp = (now ?? DateTime.Now).ToString("yyyy:MM:dd HH:mm:ss");
            if (Maker is { } maker)
                tags.Set(ExifTag.Make, maker);
            tags.Set(ExifTag.Model, cameraModel);
            tags.Set(ExifTag.Software, Software);
            tags.Set(ExifTag.DateTime, timestamp);
            tags.Set(ExifTag.DateTimeOriginal, timestamp);
            tags.Set(ExifTag.DateTimeDigitized, timestamp);
            if (metadata.TryGet(Controls.ExposureTime, out var exposure))
                tags.Set(ExifTag.ExposureTime, (uint)exposure, 1_000_000);
            tags.Set(ExifTag.IsoSpeedRatings, (ushort)(analogueGain * digitalGain * 100));
            if (metadata.TryGet(Controls.LensPosition, out var lensPosition) && lensPosition > 0)
            {
                var (numerator, denominator) = SubjectDistance(lensPosition);
                tags.Set(ExifTag.SubjectDistance, numerator, denominator);
            }
        }
        exifData?.WriteTo(tags);

        var data = Libexif.Create();
        try
        {
            foreach (var (tag, (type, count, value)) in tags.Values)
                Libexif.Set(data, ExifTagValues.IsExifIfd(tag), (ushort)tag, type, count, value);
            return Libexif.Save(data);
        }
        finally
        {
            Libexif.Free(data);
        }
    }

    // LensPosition is in dioptres; the distance in metres is its reciprocal.
    private static (uint, uint) SubjectDistance(float dioptres)
    {
        var metres = 1.0 / dioptres;
        return ((uint)Math.Round(metres * 1000), 1000);
    }
}
