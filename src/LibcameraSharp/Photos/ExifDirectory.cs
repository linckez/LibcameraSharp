namespace LibcameraSharp;

/// <summary>The EXIF block's directories a tag can go in; <see cref="Libexif.Set"/> maps them to libexif's numbers.</summary>
internal enum ExifDirectory
{
    /// <summary>The main image's tags.</summary>
    Ifd0,
    /// <summary>The EXIF sub-directory: exposure, dates, comments.</summary>
    Exif,
    /// <summary>The GPS sub-directory.</summary>
    Gps,
}
