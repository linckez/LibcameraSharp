namespace LibcameraSharp;

/// <summary>
/// Options for a photo: how the camera should be set up, what it should be doing, and what to write.
/// </summary>
public sealed record PhotoOptions
{
    /// <summary>How the camera is set up. Changing these reconfigures the camera.</summary>
    public StreamSettings Streams { get; init; } = new();

    /// <summary>Exposure, gain, focus and the rest. They take a few frames to land, and the photo is taken once they have.</summary>
    public CameraControls Controls { get; init; } = new();

    /// <summary>How the photo is encoded, and so what the camera delivers. Defaults to JPEG.</summary>
    public PhotoEncoding Encoding { get; init; } = PhotoEncoding.Jpeg;

    /// <summary>JPEG quality, 1 to 100. Ignored by every other format.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside 1 to 100.</exception>
    public int? JpegQuality
    {
        get;
        init
        {
            if (value is { } quality && quality is < 1 or > 100)
                throw new ArgumentOutOfRangeException(nameof(JpegQuality), quality, "JPEG quality runs from 1 to 100.");
            field = value;
        }
    }

    /// <summary>EXIF tags to write. JPEG only — no other format here carries them.</summary>
    public ExifData? Exif { get; init; }
}
