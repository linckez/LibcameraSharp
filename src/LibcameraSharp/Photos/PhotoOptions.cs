namespace LibcameraSharp;

/// <summary>
/// How a photo is encoded. Chosen before the photo is taken: it decides what the camera delivers,
/// and the photo is written in it, whatever the file is called.
/// </summary>
/// <remarks>For the sensor's own data, save <see cref="Photo.Raw"/> as a DNG.</remarks>
public enum PhotoEncoding
{
    /// <summary>JPEG with EXIF. The default, and the only format that carries EXIF.</summary>
    Jpeg,
    /// <summary>Lossless PNG. Ignores quality, which it has no setting for.</summary>
    Png,
    /// <summary>Uncompressed 24-bit BMP.</summary>
    Bmp,
    /// <summary>Bare 24-bit RGB rows, no file header.</summary>
    Rgb,
    /// <summary>Bare YUV420 planes, no file header.</summary>
    Yuv420,
}

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
