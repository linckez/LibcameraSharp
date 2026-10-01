using FFmpeg.AutoGen;

namespace LibcameraSharp;

/// <summary>
/// Encodes each frame as a JPEG with FFmpeg's MJPEG encoder: Motion JPEG. Every frame stands alone, so a
/// viewer can join at any point and a dropped frame costs nothing. Files are about three times the size of H.264's.
/// </summary>
/// <remarks>The camera's YUV420 goes straight to the encoder, which encodes several frames at once on its own threads.</remarks>
internal sealed unsafe class LibavMjpegEncoder : LibavEncoder
{
    private int _quantiser;

    /// <inheritdoc/>
    protected override VideoCodec Codec => VideoCodec.Mjpeg;

    // FFmpeg's name for its motion-JPEG encoder.
    private const string EncoderName = "mjpeg";

    /// <inheritdoc/>
    protected override void Setup(Quality quality) => _quantiser = QualityToQuantiser(quality);

    /// <inheritdoc/>
    protected override void Started()
    {
        Libav.Initialise();
        if (!TryOpen(EncoderName))
            throw new InvalidOperationException("This FFmpeg build has no MJPEG encoder.");
    }

    /// <inheritdoc/>
    protected override void Configure(AVCodecContext* context, bool hardware)
    {
        // Frames encode in parallel on FFmpeg's threads; every frame at the same fixed quantiser; JPEG's full range.
        context->thread_type = ffmpeg.FF_THREAD_FRAME;
        context->qmin = _quantiser;
        context->qmax = _quantiser;
        context->flags |= ffmpeg.AV_CODEC_FLAG_QSCALE;
        context->color_range = AVColorRange.AVCOL_RANGE_JPEG;
    }

    // FFmpeg's quantiser per setting: lower is better quality and bigger frames.
    private static int QualityToQuantiser(Quality quality) => quality switch
    {
        Quality.VeryLow => 31,
        Quality.Low => 15,
        Quality.Medium => 10,
        Quality.High => 5,
        _ => 3,
    };
}
