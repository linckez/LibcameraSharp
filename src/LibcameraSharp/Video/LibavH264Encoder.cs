using FFmpeg.AutoGen;

namespace LibcameraSharp;

/// <summary>
/// Encodes H.264 through the FFmpeg libraries: the Pi 4's hardware encoder when there is one, and
/// software everywhere else, including the Pi 5.
/// </summary>
/// <remarks>Software H.264 at 1080p30 takes about a core and a half on a Pi 5.</remarks>
internal sealed unsafe class LibavH264Encoder : LibavEncoder
{
    /// <summary>Bits per second. Left null, it is derived from the <see cref="Quality"/> and the frame size.</summary>
    public long? Bitrate { get; set; }

    /// <summary>Frames between keyframes.</summary>
    public int KeyframeInterval { get; set; } = 30;

    // FFmpeg's names for the Pi 4's hardware H.264 encoder and for x264, and x264's fastest preset.
    private const string HardwareEncoder = "h264_v4l2m2m";
    private const string SoftwareEncoder = "libx264";
    private const string SoftwarePreset = "ultrafast";

    /// <inheritdoc/>
    protected override VideoCodec Codec => VideoCodec.H264;

    /// <inheritdoc/>
    protected override void Setup(Quality quality)
    {
        if (Bitrate is not null)
            return;

        // Mbps at 1080p30, scaled by the square root of how many more or fewer pixels per second this is.
        var table = new Dictionary<Quality, int> { [Quality.VeryLow] = 3, [Quality.Low] = 4, [Quality.Medium] = 7, [Quality.High] = 10, [Quality.VeryHigh] = 14 };
        var referenceComplexity = 1920.0 * 1080 * 30;
        var actualComplexity = (double)Width * Height * (FrameRate ?? 30);
        Bitrate = (long)(table[quality] * 1_000_000 * Math.Sqrt(actualComplexity / referenceComplexity));
    }

    /// <inheritdoc/>
    protected override void Started()
    {
        Libav.Initialise();

        // A Pi 4 encodes H.264 in hardware; a Pi 5 has no encoder block, so it and everything else use x264.
        if (PlatformDetection.Current == Platform.Vc4 && TryOpen(HardwareEncoder, hardware: true))
            return;
        if (!TryOpen(SoftwareEncoder, hardware: false))
            throw new InvalidOperationException("This FFmpeg build has no H.264 encoder; install libavcodec with libx264.");
    }

    /// <inheritdoc/>
    protected override void Configure(AVCodecContext* context, bool hardware)
    {
        context->bit_rate = Bitrate ?? 0;
        context->gop_size = KeyframeInterval;
        // Above 1080p30's macroblock rate, H.264 needs level 4.2; the Pi 4 encoder refuses to start without it.
        var macroblocksPerSecond = ((Width + 15) >> 4) * ((Height + 15) >> 4) * (FrameRate ?? 30);
        if (macroblocksPerSecond > 245760)
            context->level = 42;
        if (hardware)
            context->max_b_frames = 0;
    }

    /// <inheritdoc/>
    protected override void AddOptions(AVDictionary** options, bool hardware)
    {
        if (hardware)
            return;
        ffmpeg.av_dict_set(options, "preset", SoftwarePreset, 0);
        ffmpeg.av_dict_set(options, "tune", "zerolatency", 0);
    }
}
