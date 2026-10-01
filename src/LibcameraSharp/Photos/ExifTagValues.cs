using System.Text;

namespace LibcameraSharp;

/// <summary>The tags one file gets, each with its TIFF type, count and bytes, as libexif takes them.</summary>
internal sealed class ExifTagValues
{
    public Dictionary<ExifTag, (TiffType Type, uint Count, byte[] Value)> Values { get; } = [];

    /// <summary>
    /// Sets a text tag. EXIF names the type ASCII, but the bytes are UTF-8: plain ASCII is unchanged, and other letters
    /// survive.
    /// </summary>
    public void Set(ExifTag tag, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        Values[tag] = (TiffType.Ascii, (uint)bytes.Length, bytes);
    }

    /// <summary>Sets a tag of bytes such as <see cref="ExifTag.GpsVersionId"/>.</summary>
    public void Set(ExifTag tag, byte[] value) => Values[tag] = (TiffType.Byte, (uint)value.Length, value);

    /// <summary>Sets a tag of several rationals, such as <see cref="ExifTag.GpsLatitude"/>'s degrees, minutes and seconds.</summary>
    public void Set(ExifTag tag, ReadOnlySpan<(uint Numerator, uint Denominator)> values)
    {
        var bytes = new byte[values.Length * 8];
        for (var i = 0; i < values.Length; i++)
        {
            BitConverter.GetBytes(values[i].Numerator).CopyTo(bytes, i * 8);
            BitConverter.GetBytes(values[i].Denominator).CopyTo(bytes, i * 8 + 4);
        }
        Values[tag] = (TiffType.Rational, (uint)values.Length, bytes);
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

    // UserComment's 8-byte character codes (EXIF 2.3 §4.6.5, Table 9).
    private static ReadOnlySpan<byte> AsciiCode => "ASCII\0\0\0"u8;
    private static ReadOnlySpan<byte> UnicodeCode => "UNICODE\0"u8;

    /// <summary>
    /// Sets <see cref="ExifTag.UserComment"/>: UNDEFINED bytes behind the 8-byte character code EXIF requires (EXIF 2.3
    /// §4.6.5, the UserComment character codes). Plain ASCII
    /// gets the ASCII code; anything else the UNICODE code with UCS-2 in the file's byte order, which the EXIF shim sets
    /// to little-endian (<c>exif_shim.c</c>).
    /// </summary>
    public void SetComment(string value)
    {
        byte[] bytes = Ascii.IsValid(value)
            ? [.. AsciiCode, .. Encoding.ASCII.GetBytes(value)]
            : [.. UnicodeCode, .. Encoding.Unicode.GetBytes(value)];
        Values[ExifTag.UserComment] = (TiffType.Undefined, (uint)bytes.Length, bytes);
    }

    /// <summary>The directory <paramref name="tag"/> belongs in.</summary>
    public static ExifDirectory DirectoryOf(ExifTag tag) => tag switch
    {
        ExifTag.ExposureTime or ExifTag.IsoSpeedRatings or ExifTag.DateTimeOriginal or ExifTag.DateTimeDigitized
            or ExifTag.SubjectDistance or ExifTag.UserComment => ExifDirectory.Exif,
        ExifTag.GpsVersionId or ExifTag.GpsLatitudeRef or ExifTag.GpsLatitude or ExifTag.GpsLongitudeRef or ExifTag.GpsLongitude
            or ExifTag.GpsAltitudeRef or ExifTag.GpsAltitude or ExifTag.GpsMapDatum => ExifDirectory.Gps,
        _ => ExifDirectory.Ifd0,
    };
}
