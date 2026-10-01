using FFmpeg.AutoGen;

namespace LibcameraSharp;

/// <summary>
/// Encodes video through the FFmpeg libraries: the camera's frames go to an FFmpeg encoder as YUV420
/// (converted by FFmpeg when the camera delivers another layout), and its packets to the output.
/// Each codec sets its own options on top.
/// </summary>
internal abstract unsafe class LibavEncoder : Encoder
{
    private AVCodecContext* _context;
    private AVFrame* _frame;
    private AVPacket* _packet;
    private SwsContext* _scaler;
    private byte[] _extraData = [];

    /// <summary>The codec the output is told it receives.</summary>
    protected abstract VideoCodec Codec { get; }

    /// <inheritdoc/>
    protected override VideoStreamInfo StreamInfo => new(Codec, Width, Height, FrameRate, _extraData, ColorSpace);

    /// <summary>Sets the codec's own options on the context before it opens.</summary>
    protected virtual void Configure(AVCodecContext* context, bool hardware)
    {
    }

    /// <summary>Adds the codec's private options, such as x264's preset.</summary>
    protected virtual void AddOptions(AVDictionary** options, bool hardware)
    {
    }

    /// <summary>Opens the named encoder and allocates what encoding needs; false when FFmpeg lacks it or it will not open.</summary>
    protected bool TryOpen(string name, bool hardware = false)
    {
        var codec = ffmpeg.avcodec_find_encoder_by_name(name);
        if (codec is null)
            return false;

        _context = ffmpeg.avcodec_alloc_context3(codec);
        _context->width = Width;
        _context->height = Height;
        _context->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
        _context->time_base = Libav.MicrosecondTimeBase;
        _context->framerate = Libav.FrameRate(FrameRate);
        _context->thread_count = 0;                                             // let libav choose
        // SPS/PPS out of band only when the output keeps them; otherwise they repeat in the stream.
        if (Output?.StoresStreamHeaders == true)
            _context->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;

        // Tag the stream with the camera's colour space, so players don't have to guess it.
        if (ColorSpace is { } colourSpace)
        {
            _context->color_primaries = LibavColourTags.Primaries(colourSpace);
            _context->color_trc = LibavColourTags.Transfer(colourSpace);
            _context->colorspace = LibavColourTags.Matrix(colourSpace);
            _context->color_range = LibavColourTags.Range(colourSpace);
        }
        Configure(_context, hardware);

        AVDictionary* options = null;
        AddOptions(&options, hardware);
        var opened = ffmpeg.avcodec_open2(_context, codec, &options);
        ffmpeg.av_dict_free(&options);
        if (opened < 0)
        {
            fixed (AVCodecContext** context = &_context)
                ffmpeg.avcodec_free_context(context);
            return false;
        }

        _extraData = new byte[_context->extradata_size];
        new ReadOnlySpan<byte>(_context->extradata, _context->extradata_size).CopyTo(_extraData);

        _frame = ffmpeg.av_frame_alloc();
        _frame->format = (int)_context->pix_fmt;
        _frame->width = Width;
        _frame->height = Height;
        Libav.Check(ffmpeg.av_frame_get_buffer(_frame, 0), "allocate the encoder frame");
        _packet = ffmpeg.av_packet_alloc();
        _scaler = ffmpeg.sws_getContext(Width, Height, SourcePixelFormat(), Width, Height, AVPixelFormat.AV_PIX_FMT_YUV420P,
                                        ffmpeg.SWS_BILINEAR, null, null, null);
        if (_scaler is null)
            throw new InvalidOperationException($"libav cannot convert {Format} to yuv420p.");
        return true;
    }

    /// <inheritdoc/>
    protected override void EncodeFrame(MappedFrame frame, long timestamp)
    {
        // A copy of every plane as the camera wrote them; libswscale reads the padded rows through its stride argument.
        var buffer = frame.ToBuffer();
        fixed (byte* source = buffer)
        {
            var planes = SourcePlanes(source, buffer.Length, out var strides);
            ffmpeg.av_frame_make_writable(_frame);
            ffmpeg.sws_scale(_scaler, planes, strides, 0, Height, _frame->data, _frame->linesize);
        }

        _frame->pts = timestamp;
        Libav.Check(ffmpeg.avcodec_send_frame(_context, _frame), "send a frame to the encoder");
        DrainPackets();
    }

    /// <inheritdoc/>
    protected override void Stopped()
    {
        if (_context is not null)
        {
            ffmpeg.avcodec_send_frame(_context, null);                          // flush what the encoder is holding
            DrainPackets();
        }

        fixed (AVFrame** frame = &_frame)
            ffmpeg.av_frame_free(frame);
        fixed (AVPacket** packet = &_packet)
            ffmpeg.av_packet_free(packet);
        fixed (AVCodecContext** context = &_context)
            ffmpeg.avcodec_free_context(context);
        if (_scaler is not null)
        {
            ffmpeg.sws_freeContext(_scaler);
            _scaler = null;
        }
    }

    private void DrainPackets()
    {
        while (true)
        {
            var status = ffmpeg.avcodec_receive_packet(_context, _packet);
            if (status == ffmpeg.AVERROR(ffmpeg.EAGAIN) || status == ffmpeg.AVERROR_EOF)
                break;                                                          // nothing more until the next frame
            Libav.Check(status, "read a packet from the encoder");

            // The hardware encoder fills in its headers only with the first frame.
            if (_extraData.Length == 0 && _context->extradata_size > 0)
            {
                _extraData = new ReadOnlySpan<byte>(_context->extradata, _context->extradata_size).ToArray();
                SetCodecExtraData(_extraData);
            }

            var keyframe = (_packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
            OutputFrame(new ReadOnlySpan<byte>(_packet->data, _packet->size), keyframe, _packet->pts);
            ffmpeg.av_packet_unref(_packet);
        }
    }

    // The camera formats these encoders take, as libav pixel formats.
    private AVPixelFormat SourcePixelFormat() => LibavPixelFormats.From(Format)
        ?? throw new NotSupportedException($"{Format} cannot be encoded; use YUV420, NV12 or one of the 8-bit RGB formats.");

    // Where each plane starts in the camera's buffer, and how long its rows are.
    private byte_ptrArray8 SourcePlanes(byte* buffer, int length, out int_array8 strides) =>
        LibavPixelFormats.Planes(buffer, SourcePixelFormat(), (int)Stride, Height, out strides);
}
