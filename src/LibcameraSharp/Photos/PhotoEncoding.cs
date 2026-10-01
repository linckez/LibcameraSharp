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
