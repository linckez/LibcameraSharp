using System.Globalization;

namespace LibcameraSharp;

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

    // EXIF's date-time layout (EXIF 2.3 §4.6.4, DateTime), written the same whatever the machine's culture.
    private const string DateTimeFormat = "yyyy:MM:dd HH:mm:ss";

    // ISO speed per unit of gain: gain 1 is ISO 100.
    private const int IsoPerUnitGain = 100;

    // ExposureTime is a rational of seconds; libcamera gives microseconds.
    private const uint MicrosecondsPerSecond = 1_000_000;

    // SubjectDistance is a rational of metres, kept to millimetres.
    private const uint MillimetresPerMetre = 1000;

    /// <summary>An EXIF date-time, such as <c>2026:10:01 14:30:00</c>.</summary>
    public static string DateTimeText(DateTime time) => time.ToString(DateTimeFormat, CultureInfo.InvariantCulture);

    /// <summary>The ISO speed for a total gain (analogue × digital).</summary>
    public static ushort Iso(float gain) => (ushort)(gain * IsoPerUnitGain);

    /// <summary>What <see cref="ExifTag.Software"/> says.</summary>
    public static string Software { get; } = "LibcameraSharp " + (typeof(ExifSegment).Assembly.GetName().Version?.ToString(3) ?? "");

    /// <summary>
    /// Builds the segment payload (<c>Exif\0\0</c> + TIFF), or an empty array when the metadata has
    /// no gains and <paramref name="exifData"/> is null, or when libexif isn't installed.
    /// </summary>
    public static byte[] Build(Metadata metadata, string? cameraModel, ExifData? exifData = null, DateTime? now = null)
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
            var timestamp = DateTimeText(now ?? DateTime.Now);
            if (Maker is { } maker)
                tags.Set(ExifTag.Make, maker);
            if (cameraModel is not null)
                tags.Set(ExifTag.Model, cameraModel);
            tags.Set(ExifTag.Software, Software);
            tags.Set(ExifTag.DateTime, timestamp);
            tags.Set(ExifTag.DateTimeOriginal, timestamp);
            tags.Set(ExifTag.DateTimeDigitized, timestamp);
            if (metadata.TryGet(Controls.ExposureTime, out var exposure))
                tags.Set(ExifTag.ExposureTime, (uint)exposure, MicrosecondsPerSecond);
            tags.Set(ExifTag.IsoSpeedRatings, Iso(analogueGain * digitalGain));
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
                Libexif.Set(data, ExifTagValues.DirectoryOf(tag), (ushort)tag, type, count, value);
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
        return ((uint)Math.Round(metres * MillimetresPerMetre), MillimetresPerMetre);
    }
}
