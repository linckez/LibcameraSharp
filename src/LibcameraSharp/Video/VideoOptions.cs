namespace LibcameraSharp;

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
    public VideoQuality Quality { get; init; } = VideoQuality.Medium;

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
    } = DefaultKeyframeInterval;

    // The frame rate an encoder states when neither the options nor the camera give one, and the keyframe interval a
    // recording gets by default: one keyframe a second at that rate.
    internal const double DefaultFrameRate = 30;
    internal const int DefaultKeyframeInterval = 30;
}
