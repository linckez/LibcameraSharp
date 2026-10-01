using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace LibcameraSharp;

/// <summary>
/// Muxes encoded frames into a container (<c>.mp4</c>, <c>.mkv</c>, <c>.ts</c>) so the result plays in
/// a browser, a phone or VLC. Writes to a file, or to any <see cref="Stream"/>, such as an HTTP response.
/// </summary>
/// <remarks>Expects timestamps in microseconds.</remarks>
internal sealed unsafe class ContainerOutput : Output
{
    // Encoded frames are tens of kilobytes, so the container writes through a buffer that holds one.
    private const int IoBufferSize = 64 * 1024;

    // How much the pipe to a non-seekable destination holds before libav's writes wait.
    private const int PipeLimit = 4 * 1024 * 1024;

    // errno for "I/O error": what libav expects back from a write callback that could not write.
    private const int EIO = 5;

    // The mov muxer's option that writes an MP4 as fragments, each with its own index, so it plays as it arrives.
    private const string MovFlags = "movflags", FragmentedMp4 = "frag_keyframe+empty_moov+default_base_moof";

    // The whence values libav passes to a seek callback, as C's stdio.h numbers them.
    private const int SeekCur = 1, SeekEnd = 2;

    private readonly Stream? _stream;
    private readonly string? _path;
    private readonly bool _ownsStream;
    private readonly VideoContainer _container;
    private AVFormatContext* _format;
    private AVStream* _videoStream;
    private AVIOContext* _io;
    private AVPacket* _packet;
    private GCHandle _self;
    private Exception? _streamFailure;

    // For a destination that can't seek, such as an ASP.NET response that only takes asynchronous
    // writes: libav writes into the pipe, and _drain copies it out asynchronously.
    private Pipe? _pipe;
    private Task? _drain;
    private bool _useFragments;
    private bool _headerWritten;
    private bool _seenKeyframe;
    private long? _firstTimestamp;

    /// <summary>Muxes into <paramref name="stream"/> — a response body, a socket, a <see cref="MemoryStream"/>.</summary>
    /// <param name="stream">Where the container is written. Disposed with this output only when <paramref name="ownsStream"/> is set.</param>
    /// <param name="container">The container to write.</param>
    /// <param name="ownsStream">Dispose <paramref name="stream"/> when recording stops.</param>
    /// <remarks>An MP4 into a stream that cannot seek is written fragmented, so it plays as it arrives.</remarks>
    public ContainerOutput(Stream stream, VideoContainer container, bool ownsStream = false)
    {
        _stream = stream;
        _container = container;
        _ownsStream = ownsStream;
    }

    /// <summary>Muxes into the file at <paramref name="path"/>, which libav opens and writes itself.</summary>
    /// <param name="path">The file to write, created or replaced.</param>
    /// <param name="container">The container to write.</param>
    public ContainerOutput(string path, VideoContainer container)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
        _container = container;
    }

    // libav's muxer name for each container.
    private string MuxerName => _container switch
    {
        VideoContainer.Mp4 => "mp4",
        VideoContainer.Matroska => "matroska",
        VideoContainer.MpegTs => "mpegts",
        _ => throw new ArgumentOutOfRangeException(nameof(_container), _container, "Not a container libav writes."),
    };

    /// <inheritdoc/>
    /// <remarks>The muxer says so itself: MP4 and Matroska keep the headers, MPEG-TS carries them in the stream.</remarks>
    public override bool StoresStreamHeaders
    {
        get
        {
            Libav.Initialise();
            var format = ffmpeg.av_guess_format(MuxerName, null, null);
            return format is not null && (format->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0;
        }
    }

    /// <inheritdoc/>
    public override void DescribeStream(VideoStreamInfo stream)
    {
        Libav.Initialise();
        AVFormatContext* context = null;
        Libav.Check(ffmpeg.avformat_alloc_output_context2(&context, null, MuxerName, null), $"open {Destination} for writing");
        _format = context;

        try
        {
            Prepare(stream);
        }
        catch
        {
            Stop();                                                             // hand libav its context back before the exception leaves
            throw;
        }
    }

    /// <summary>Stores the encoder's stream headers (H.264's SPS and PPS) for the container header.</summary>
    // The hardware encoder's headers arrive with its first frame; the container header is written then too, so it is not too late.
    internal override void SetCodecExtraData(ReadOnlySpan<byte> extraData)
    {
        if (_headerWritten || _videoStream is null || extraData.IsEmpty)
            return;

        var parameters = _videoStream->codecpar;
        if (parameters->extradata is not null)
            ffmpeg.av_freep(&parameters->extradata);
        parameters->extradata = (byte*)ffmpeg.av_mallocz((ulong)extraData.Length + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE);
        parameters->extradata_size = extraData.Length;
        extraData.CopyTo(new Span<byte>(parameters->extradata, extraData.Length));
    }

    private void Prepare(VideoStreamInfo stream)
    {
        _videoStream = ffmpeg.avformat_new_stream(_format, null);
        if (_videoStream is null)
            throw new InvalidOperationException("libav could not add a stream to the container.");

        var parameters = _videoStream->codecpar;
        parameters->codec_type = AVMediaType.AVMEDIA_TYPE_VIDEO;
        parameters->codec_id = CodecId(stream.Codec);
        parameters->width = stream.Width;
        parameters->height = stream.Height;
        parameters->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
        if (stream.ColourSpace is { } colourSpace)
        {
            parameters->color_primaries = LibavColourTags.Primaries(colourSpace);
            parameters->color_trc = LibavColourTags.Transfer(colourSpace);
            parameters->color_space = LibavColourTags.Matrix(colourSpace);
            parameters->color_range = LibavColourTags.Range(colourSpace);
        }

        // H.264 keeps its SPS and PPS out of band; a container must store them or nothing can decode the file.
        if (!stream.CodecExtraData.IsEmpty)
        {
            parameters->extradata = (byte*)ffmpeg.av_mallocz((ulong)stream.CodecExtraData.Length + ffmpeg.AV_INPUT_BUFFER_PADDING_SIZE);
            parameters->extradata_size = stream.CodecExtraData.Length;
            stream.CodecExtraData.Span.CopyTo(new Span<byte>(parameters->extradata, stream.CodecExtraData.Length));
        }

        _videoStream->time_base = Libav.MicrosecondTimeBase;
        _videoStream->avg_frame_rate = Libav.FrameRate(stream.FrameRate);

        // A plain MP4 seeks back at the end to write its index; a stream that cannot seek gets fragments instead.
        _useFragments = _container == VideoContainer.Mp4 && _stream is { CanSeek: false };

        // A file libav opens and writes itself; a stream goes through the write and seek callbacks.
        if (_path is not null)
            Libav.Check(ffmpeg.avio_open2(&_format->pb, _path, ffmpeg.AVIO_FLAG_WRITE, null, null), $"open {_path} for writing");
        else
            OpenManagedStream();
        _packet = ffmpeg.av_packet_alloc();
    }

    /// <inheritdoc/>
    public override void OutputFrame(ReadOnlySpan<byte> frame, bool keyframe = true, long? timestamp = null)
    {
        if (!Recording || _format is null || frame.IsEmpty)
            return;

        // A container must start at a keyframe, and its timestamps must start at zero.
        _seenKeyframe |= keyframe;
        if (!_seenKeyframe)
            return;

        var microseconds = timestamp ?? 0;
        _firstTimestamp ??= microseconds;
        var pts = ffmpeg.av_rescale_q(microseconds - _firstTimestamp.Value, Libav.MicrosecondTimeBase, _videoStream->time_base);

        try
        {
            if (!_headerWritten)
            {
                WriteHeader();
                _headerWritten = true;
            }

            fixed (byte* data = frame)
            {
                _packet->data = data;
                _packet->size = frame.Length;
                _packet->stream_index = _videoStream->index;
                _packet->pts = pts;
                _packet->dts = pts;
                _packet->flags = keyframe ? ffmpeg.AV_PKT_FLAG_KEY : 0;
                Libav.Check(ffmpeg.av_interleaved_write_frame(_format, _packet), $"write a frame to {Destination}");
            }
        }
        catch (Exception exception)
        {
            Fail(_streamFailure ?? exception);
        }
    }

    /// <inheritdoc/>
    public override void Stop()
    {
        base.Stop();
        if (_format is null)
            return;

        // Take everything out of the fields first: cleanup runs once, even if a step below throws and
        // Stop is called again (a failing write does that through Fail).
        var format = _format;
        var io = _io;
        var packet = _packet;
        var (pipe, drain) = (_pipe, _drain);
        var headerWritten = _headerWritten;
        _format = null;
        _io = null;
        _packet = null;
        _videoStream = null;
        (_pipe, _drain) = (null, null);
        (_headerWritten, _seenKeyframe, _firstTimestamp) = (false, false, null);

        try
        {
            if (headerWritten)
                ffmpeg.av_write_trailer(format);                                // the index; without it the file is unplayable

            if (_path is not null && format->pb is not null)
                ffmpeg.avio_closep(&format->pb);                                // the file libav opened

            if (io is not null)
            {
                ffmpeg.avio_flush(io);
                ffmpeg.av_freep(&io->buffer);                                   // libav may have replaced the buffer we allocated
                ffmpeg.avio_context_free(&io);
                format->pb = null;

                // Through a pipe: let the copy finish, then close the destination asynchronously too.
                if (pipe is not null)
                {
                    pipe.Writer.Complete();
                    drain!.GetAwaiter().GetResult();
                    if (_ownsStream)
                        _stream!.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                else
                {
                    _stream!.Flush();
                    if (_ownsStream)
                        _stream.Dispose();
                }
            }
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
            ffmpeg.avformat_free_context(format);
            if (_self.IsAllocated)
                _self.Free();
        }
    }

    private string Destination => _path ?? $"the {MuxerName} stream";

    /// <summary>Hands libav a managed stream to write through, instead of a filename it opens itself.</summary>
    private void OpenManagedStream()
    {
        _self = GCHandle.Alloc(this);
        var buffer = (byte*)ffmpeg.av_malloc(IoBufferSize);
        _io = ffmpeg.avio_alloc_context(
            buffer, IoBufferSize, write_flag: 1, (void*)GCHandle.ToIntPtr(_self),
            read_packet: new avio_alloc_context_read_packet_func { Pointer = IntPtr.Zero },
            write_packet: new avio_alloc_context_write_packet_func { Pointer = (IntPtr)(delegate* unmanaged[Cdecl]<void*, byte*, int, int>)&WritePacket },
            seek: new avio_alloc_context_seek_func { Pointer = _stream!.CanSeek ? (IntPtr)(delegate* unmanaged[Cdecl]<void*, long, int, long>)&Seek : IntPtr.Zero });
        if (_io is null)
            throw new InvalidOperationException("libav could not allocate an I/O context for the stream.");

        _format->pb = _io;
        _format->flags |= ffmpeg.AVFMT_FLAG_CUSTOM_IO;

        // A destination that can't seek gets its bytes through a pipe, copied out asynchronously; the
        // pipe holds a few megabytes before libav's writes wait, so a slow reader slows the camera.
        if (!_stream.CanSeek)
        {
            _pipe = new Pipe(new PipeOptions(pauseWriterThreshold: PipeLimit, resumeWriterThreshold: PipeLimit / 2, useSynchronizationContext: false));
            _drain = Task.Run(() => PipeToStream.CopyAsync(_pipe.Reader, _stream, exception => _streamFailure ??= exception));
        }
    }

    /// <summary>Writes the container header, with the muxer options.</summary>
    private void WriteHeader()
    {
        AVDictionary* options = null;
        if (_useFragments)
            ffmpeg.av_dict_set(&options, MovFlags, FragmentedMp4, 0);

        var result = ffmpeg.avformat_write_header(_format, &options);
        ffmpeg.av_dict_free(&options);
        Libav.Check(result, $"write the {Destination} header");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int WritePacket(void* opaque, byte* buffer, int length)
    {
        var output = Target(opaque);
        try
        {
            if (output._pipe is { } pipe)
            {
                new ReadOnlySpan<byte>(buffer, length).CopyTo(pipe.Writer.GetSpan(length));
                pipe.Writer.Advance(length);

                // Waits while the destination is behind; a finished pipe means the destination failed.
                var flushed = pipe.Writer.FlushAsync().AsTask().GetAwaiter().GetResult();
                if (flushed.IsCompleted)
                {
                    output._streamFailure ??= new IOException("The destination stopped accepting data.");
                    return ffmpeg.AVERROR(EIO);
                }
                return length;
            }

            output._stream!.Write(new ReadOnlySpan<byte>(buffer, length));
            return length;
        }
        catch (Exception exception)
        {
            // An exception must not cross back into libav; carry it out through the return code.
            output._streamFailure = exception;
            return ffmpeg.AVERROR(EIO);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static long Seek(void* opaque, long offset, int whence)
    {
        var output = Target(opaque);
        try
        {
            // AVSEEK_FORCE is advisory; AVSEEK_SIZE asks for the length rather than moving.
            var origin = (whence & ~ffmpeg.AVSEEK_FORCE) switch
            {
                ffmpeg.AVSEEK_SIZE => (SeekOrigin?)null,
                SeekCur => SeekOrigin.Current,
                SeekEnd => SeekOrigin.End,
                _ => SeekOrigin.Begin,
            };
            return origin is { } from ? output._stream!.Seek(offset, from) : output._stream!.Length;
        }
        catch (Exception exception)
        {
            output._streamFailure = exception;
            return ffmpeg.AVERROR(EIO);
        }
    }

    private static ContainerOutput Target(void* opaque) => (ContainerOutput)GCHandle.FromIntPtr((IntPtr)opaque).Target!;

    private static AVCodecID CodecId(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => AVCodecID.AV_CODEC_ID_H264,
        _ => AVCodecID.AV_CODEC_ID_MJPEG,
    };
}
