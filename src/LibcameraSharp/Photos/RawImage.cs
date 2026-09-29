namespace LibcameraSharp;

/// <summary>The sensor's own data for a frame: one colour per pixel, in a mosaic, still packed.</summary>
/// <remarks>
/// Save it as a DNG to open it in a raw editor: the file carries the black and white levels, the
/// colour matrix and the white balance the camera chose.
/// </remarks>
public sealed class RawImage
{
    private readonly byte[] _bytes;
    private readonly Metadata _metadata;
    private readonly StreamDescription _config;
    private readonly string _cameraModel;

    internal RawImage(byte[] bytes, Metadata metadata, StreamDescription config, string cameraModel, BayerFormat? format, Size size)
    {
        (_bytes, _metadata, _config, _cameraModel) = (bytes, metadata, config, cameraModel);
        (Format, Size) = (format, size);
    }

    /// <summary>The Bayer order, bit depth and packing of the data.</summary>
    /// <remarks>Check this before reading <see cref="Bytes"/>: CSI-2 packed is four pixels in five bytes.</remarks>
    public BayerFormat? Format { get; }

    /// <summary>The size the sensor read out.</summary>
    public Size Size { get; }

    /// <summary>The raw bytes, exactly as the sensor produced them.</summary>
    public ReadOnlyMemory<byte> Bytes => _bytes;

    /// <summary>Writes a DNG file at <paramref name="path"/>, which Lightroom, darktable and RawTherapee open.</summary>
    /// <remarks>
    /// Synchronous: libtiff opens and writes the file itself, by name, so there's nothing to await. Run it on another
    /// thread if the caller mustn't wait.
    /// </remarks>
    /// <exception cref="InvalidOperationException">libtiff (<c>libtiff6</c>) isn't installed, or the file couldn't be written.</exception>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        DngWriter.Save(_bytes, _config, _metadata, _cameraModel, path);
    }
}
