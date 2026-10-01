using FFmpeg.AutoGen;

namespace LibcameraSharp;

/// <summary>
/// Encodes H.264 through the FFmpeg libraries: the Pi 4's hardware encoder when there is one, and
/// software everywhere else, including the Pi 5.
/// </summary>
/// <remarks>Software H.264 at 1080p30 takes about a core and a half on a Pi 5.</remarks>
internal sealed unsafe class LibavH264Encoder : LibavEncoder
{
    /// <summary>Bits per second. Left null, it is derived from the <see cref="VideoQuality"/> and the frame size.</summary>
    public long? Bitrate { get; set; }

    /// <summary>Frames between keyframes.</summary>
    public int KeyframeInterval { get; set; } = VideoOptions.DefaultKeyframeInterval;

    // FFmpeg's names for the Pi 4's hardware H.264 encoder and for x264, and x264's fastest preset.
    private const string HardwareEncoder = "h264_v4l2m2m";
    private const string SoftwareEncoder = "libx264";
    private const string SoftwarePreset = "ultrafast";

    // x264's tuning that turns off frame look-ahead, so each frame leaves the encoder as it is encoded.
    private const string SoftwareTune = "zerolatency";

    // The quality table's Mbps is for this many pixels a second: 1080p at 30 fps.
    private const double ReferencePixelsPerSecond = 1920.0 * 1080 * 30;

    // H.264 level 4.1's macroblock rate (Table A-1); above it the stream is marked level 4.2, as level_idc 42.
    private const int Level41MacroblocksPerSecond = 245_760;
    private const int Level42 = 42;

    /// <inheritdoc/>
    protected override VideoCodec Codec => VideoCodec.H264;

    /// <inheritdoc/>
    protected override void Setup(VideoQuality quality)
    {
        if (Bitrate is not null)
            return;

        // Mbps at 1080p30, scaled by the square root of how many more or fewer pixels per second this is.
        var table = new Dictionary<VideoQuality, int> { [VideoQuality.VeryLow] = 3, [VideoQuality.Low] = 4, [VideoQuality.Medium] = 7, [VideoQuality.High] = 10, [VideoQuality.VeryHigh] = 14 };
        var pixelsPerSecond = (double)Width * Height * FrameRate;
        Bitrate = (long)(table[quality] * 1_000_000 * Math.Sqrt(pixelsPerSecond / ReferencePixelsPerSecond));
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
        context->bit_rate = Bitrate!.Value;                                   // Setup always sets it
        context->gop_size = KeyframeInterval;
        // Above 1080p30's macroblock rate, H.264 needs level 4.2; the Pi 4 encoder refuses to start without it.
        var macroblocksPerSecond = ((Width + 15) >> 4) * ((Height + 15) >> 4) * FrameRate;   // 16×16 pixels each
        if (macroblocksPerSecond > Level41MacroblocksPerSecond)
            context->level = Level42;
        if (hardware)
            context->max_b_frames = 0;
    }

    /// <inheritdoc/>
    protected override void AddOptions(AVDictionary** options, bool hardware)
    {
        if (hardware)
            return;
        ffmpeg.av_dict_set(options, "preset", SoftwarePreset, 0);
        ffmpeg.av_dict_set(options, "tune", SoftwareTune, 0);
    }
}
