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
    /// <remarks>
    /// The photo is encoded before the file is opened, so a photo that can't be encoded leaves no file. A save cancelled
    /// partway through writing leaves a partial one.
    /// </remarks>
    public async Task SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        cancellationToken.ThrowIfCancellationRequested();
        using var encoded = EncodeToMemory();

        // Opened for asynchronous I/O, as ImageSharp opens a file it saves to (LocalFileSystem.CreateAsynchronous).
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await WriteAsync(encoded, file, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Writes the photo in its <see cref="PhotoOptions.Encoding"/> to a stream, such as an HTTP response.</summary>
    /// <remarks>
    /// The photo is encoded in memory first, then written with the stream's asynchronous writes, so a stream that refuses
    /// synchronous writes, as ASP.NET Core's response body does, takes it.
    /// </remarks>
    public async Task SaveAsync(Stream output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        cancellationToken.ThrowIfCancellationRequested();
        using var encoded = EncodeToMemory();
        await WriteAsync(encoded, output, cancellationToken).ConfigureAwait(false);
    }

    // Encoding is CPU work, done here; the writing is I/O, awaited. System.Text.Json's SerializeAsync works the same
    // way: it fills a buffer, then awaits the stream's WriteAsync and FlushAsync.
    private MemoryStream EncodeToMemory()
    {
        var encoded = new MemoryStream();
        Encode(encoded);
        return encoded;
    }

    private static async Task WriteAsync(MemoryStream encoded, Stream output, CancellationToken cancellationToken)
    {
        await output.WriteAsync(encoded.GetBuffer().AsMemory(0, (int)encoded.Length), cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    // JpegWriter is the only writer that takes quality or EXIF; the others have nowhere to put them.
    private void Encode(Stream output)
    {
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
    }
}
