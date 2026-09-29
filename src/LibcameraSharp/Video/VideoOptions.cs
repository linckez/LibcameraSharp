namespace LibcameraSharp;

/// <summary>Which codec a recording is encoded with.</summary>
public enum VideoCodec
{
    /// <summary>H.264: small files that every player opens. Dropping a frame corrupts the frames after it.</summary>
    H264,

    /// <summary>Motion JPEG: every frame stands alone, so frames can be dropped freely; files are about three times larger.</summary>
    Mjpeg,
}

/// <summary>The options for a recording.</summary>
public sealed record VideoOptions
{
    /// <summary>How the camera is set up. Changing these reconfigures the camera.</summary>
    public StreamSettings Streams { get; init; } = new();

    /// <summary>Exposure, gain, focus and the rest. They take a few frames to land, and recording starts once they have.</summary>
    public CameraControls Controls { get; init; } = new();

    /// <summary>Which codec to use.</summary>
    public VideoCodec Codec { get; init; } = VideoCodec.H264;

    /// <summary>How hard to compress.</summary>
    public Quality Quality { get; init; } = Quality.Medium;

    /// <summary>
    /// Frames between full pictures in an H.264 recording. A player can only start or seek at one, so
    /// fewer means smaller files and slower joining. MJPEG ignores it: every frame is whole.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is 0 or less.</exception>
    public int KeyframeInterval
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, nameof(KeyframeInterval));
            field = value;
        }
    } = 30;
}
