namespace LibcameraSharp;

/// <summary>
/// Descriptive EXIF tags to write into a saved JPEG, on top of the ones the camera's metadata provides; yours win on
/// conflict: <c>new ExifData { Artist = "A. Rossi", Location = new GpsLocation(55.6761, 12.5683) }</c>.
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

    /// <summary>A comment of your own, such as a batch number.</summary>
    public string? UserComment { get; init; }

    /// <summary>Where the photo was taken, written to EXIF's GPS directory.</summary>
    public GpsLocation? Location { get; init; }

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
        Location?.WriteTo(tags);
    }
}
