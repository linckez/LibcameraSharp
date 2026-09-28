namespace LibcameraSharp;

/// <summary>A photograph, and what the camera did to take it.</summary>
/// <remarks>The pixels are a copy, so a photo can be kept as long as you like.</remarks>
public sealed class Photo
{
    private readonly FramePixels _pixels;
    private readonly string _cameraModel;

    internal Photo(FramePixels pixels, Metadata metadata, string cameraModel, RawImage? raw, PhotoOptions options)
    {
        _pixels = pixels;
        _cameraModel = cameraModel;
        Metadata = new CaptureMetadata(metadata);
        Raw = raw;
        Options = options;
    }

    /// <summary>The picture's size in pixels.</summary>
    public Size Size => _pixels.Size;

    /// <summary>What the camera did: the exposure and gain it chose, where the lens ended up.</summary>
    public CaptureMetadata Metadata { get; }

    /// <summary>The sensor's own data, when <see cref="StreamSettings.CaptureRaw"/> asked for it; otherwise null.</summary>
    public RawImage? Raw { get; }

    /// <summary>The options this photo was taken with.</summary>
    public PhotoOptions Options { get; }

    /// <summary>
    /// Writes the photo in its <see cref="PhotoOptions.Encoding"/> to <paramref name="path"/>, exactly as
    /// named: the name doesn't change the encoding, and nothing is added to it.
    /// </summary>
    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var file = File.Create(path);
        await SaveAsync(file, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes the photo in its <see cref="PhotoOptions.Encoding"/> to a stream.</summary>
    public Task SaveAsync(Stream output, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // JpegWriter is the only one that takes quality or EXIF; the others have nowhere to put them.
        switch (Options.Encoding)
        {
            case PhotoEncoding.Jpeg:
                JpegWriter.Save(_pixels, Metadata.Frame, output, _cameraModel,
                    Options.JpegQuality ?? JpegWriter.DefaultQuality, Options.Exif);
                break;
            case PhotoEncoding.Png:
                PngWriter.Save(_pixels, output);
                break;
            case PhotoEncoding.Bmp:
                BmpWriter.Save(_pixels, output);
                break;
            default:
                YuvWriter.Save(_pixels, Options.Encoding, output);
                break;
        }
        return Task.CompletedTask;
    }
}
