namespace LibcameraSharp;

/// <summary>How a recording is wrapped for a player: MP4, Matroska, MPEG-TS, or not at all.</summary>
/// <remarks>
/// Without a container you get the encoder's own bytes (a <c>.h264</c> or <c>.mjpeg</c> file), which
/// ffmpeg and VLC play and most other players do not.
/// </remarks>
public enum VideoContainer
{
    /// <summary>
    /// No container: the encoder's own bytes. With MJPEG, each <c>Write</c> to the stream is exactly one complete JPEG, so
    /// the stream can pass each picture on as it is.
    /// </summary>
    None,

    /// <summary>MP4, the format everything plays. Fragmented automatically when the destination cannot seek.</summary>
    Mp4,

    /// <summary>Matroska, which writes forwards and needs no seeking.</summary>
    Matroska,

    /// <summary>MPEG-TS, the usual choice for a live feed.</summary>
    MpegTs,
}
