namespace LibcameraSharp;

/// <summary>How a recording is wrapped for a player: MP4, Matroska, MPEG-TS, or not at all.</summary>
/// <remarks>
/// Without a container you get the encoder's own bytes (a <c>.h264</c> or <c>.mjpeg</c> file), which
/// ffmpeg and VLC play and most other players do not.
/// </remarks>
public abstract class VideoContainer
{
    private VideoContainer()
    {
    }

    /// <summary>No container: the encoder's own bytes.</summary>
    public static VideoContainer None { get; } = new ElementaryStream();

    /// <summary>MP4, the format everything plays. Fragmented automatically when the destination cannot seek.</summary>
    public static VideoContainer Mp4 { get; } = new Muxed("mp4");

    /// <summary>Matroska, which writes forwards and needs no seeking.</summary>
    public static VideoContainer Matroska { get; } = new Muxed("matroska");

    /// <summary>MPEG-TS, the usual choice for a live feed.</summary>
    public static VideoContainer MpegTs { get; } = new Muxed("mpegts");

    // The container a file extension names; anything else is written unwrapped.
    internal static VideoContainer ForPath(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp4" => Mp4,
            ".mkv" => Matroska,
            ".ts" => MpegTs,
            _ => None,
        };

    internal static Encoder CreateEncoder(VideoCodec codec) =>
        codec == VideoCodec.Mjpeg ? new LibavMjpegEncoder() : new LibavH264Encoder();

    // Wraps a destination so encoded frames are muxed into it.
    internal abstract Output Wrap(Stream destination, VideoOptions options);

    // An output for the file at path, created or replaced.
    internal abstract Output WrapFile(string path, VideoOptions options);

    private sealed class ElementaryStream : VideoContainer
    {
        internal override Output Wrap(Stream destination, VideoOptions options) =>
            new FileOutput(destination, ownsStream: true);

        internal override Output WrapFile(string path, VideoOptions options) =>
            new FileOutput(File.Create(path), ownsStream: true);
    }

    private sealed class Muxed(string format) : VideoContainer
    {
        internal override Output Wrap(Stream destination, VideoOptions options) =>
            new ContainerOutput(destination, format, ownsStream: true);

        // libav opens and writes the file itself, as it does for a file name it is given.
        internal override Output WrapFile(string path, VideoOptions options) =>
            new ContainerOutput(path, format);
    }
}
