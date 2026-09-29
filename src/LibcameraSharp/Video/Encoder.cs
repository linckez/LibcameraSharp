
using System.Threading.Channels;

namespace LibcameraSharp;

/// <summary>
/// Turns camera frames into an encoded stream and pushes it to its <see cref="Output"/>. It is fed
/// every frame of the stream it is bound to until recording stops.
/// </summary>
/// <remarks>
/// Timestamps are microseconds since the first frame, taken from <c>SensorTimestamp</c>. A recording starts,
/// feeds and stops its encoder in that order: the session's loop enqueues frames only between an attach and a
/// detach. Stopping has two parts with one owner each: the camera's close only drains the encoder
/// (<see cref="Drain"/>), so no frame is read after its buffer is freed; the recording alone finishes it
/// (<see cref="Stop"/>), flushing libav and closing the output.
/// </remarks>
internal abstract class Encoder
{
    private long? _firstTimestamp;
    private Channel<CapturedFrame>? _queue;
    private Task? _worker;

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    public bool Running { get; private set; }

    /// <summary>Where encoded frames go. Set before starting.</summary>
    public Output? Output { get; set; }

    /// <summary>Frames encoded since <see cref="Start"/>.</summary>
    public long FramesEncoded { get; private set; }

    /// <summary>Nominal frame rate, used by encoders that must state one. Filled in from the camera if left null.</summary>
    public double? FrameRate { get; set; }

    /// <summary>Frame width in pixels. Set from the camera configuration by <c>CameraSession.PrepareEncoder</c>.</summary>
    public int Width { get; set; }

    /// <summary>Frame height in pixels. Set from the camera configuration by <c>CameraSession.PrepareEncoder</c>.</summary>
    public int Height { get; set; }

    /// <summary>Bytes from one row of the source frame to the next. Set from the camera configuration.</summary>
    public uint Stride { get; set; }

    /// <summary>The pixel format the camera delivers. Set from the camera configuration.</summary>
    public PixelFormat Format { get; set; }

    /// <summary>The colour space the camera delivers, for encoders that tag their output. Set from the camera configuration.</summary>
    public ColorSpace? ColourSpace { get; set; }

    /// <summary>
    /// Prepares the encoder and opens its outputs. <paramref name="quality"/> applies only when the
    /// encoder has no explicit bitrate set.
    /// </summary>
    /// <exception cref="InvalidOperationException">Already running, or there is no output.</exception>
    public void Start(Quality? quality = null)
    {
        if (Running)
            throw new InvalidOperationException("The encoder is already running.");
        if (Output is not { } output)
            throw new InvalidOperationException("Set an output before starting the encoder.");

        FramesEncoded = 0;
        _firstTimestamp = null;
        Setup(quality ?? Quality.Medium);

        // A start that fails part-way releases what it opened, so nothing is left allocated or open.
        try
        {
            output.Start();
            Started();
            output.DescribeStream(StreamInfo);
        }
        catch
        {
            try
            {
                Stopped();
            }
            finally
            {
                output.Stop();
            }
            throw;
        }
        Running = true;

        // Frames are encoded on this encoder's own thread, so the camera's loop only hands them over.
        var queue = Channel.CreateUnbounded<CapturedFrame>(new UnboundedChannelOptions { SingleReader = true });
        _queue = queue;
        _worker = Task.Factory.StartNew(() => EncodeQueued(queue.Reader), CancellationToken.None,
                                        TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    /// <summary>
    /// Queues a frame to be encoded on this encoder's thread; the frame is disposed, returning its
    /// buffer, once it is encoded, or at once when the encoder isn't running.
    /// </summary>
    internal void Enqueue(CapturedFrame frame)
    {
        if (_queue is not { } queue || !queue.Writer.TryWrite(frame))
            frame.Dispose();
    }

    // The encoder's thread: each queued frame is encoded, then released. A failure fails the output, so
    // the recording reports it; the frames after it are still released.
    private void EncodeQueued(ChannelReader<CapturedFrame> frames)
    {
        while (frames.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
        {
            while (frames.TryRead(out var frame))
            {
                using (frame)
                {
                    try
                    {
                        Encode(frame);
                    }
                    catch (Exception exception)
                    {
                        Output?.Fail(exception);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Takes no more frames and waits until the ones already queued are encoded and handed back. Safe to call from
    /// several threads, and more than once: each call waits for the same thread to end.
    /// </summary>
    public void Drain()
    {
        _queue?.Writer.TryComplete();
        _worker?.GetAwaiter().GetResult();
    }

    /// <summary>Drains the encoder, then flushes and closes it and its output. Only the recording that owns it calls this.</summary>
    /// <remarks>A stop before a start, or a second stop, does nothing.</remarks>
    public void Stop()
    {
        Drain();
        if (!Running)
            return;
        Running = false;
        Stopped();
        Output?.Stop();
    }

    /// <summary>
    /// Encodes one completed frame. Called on this encoder's thread for every queued frame of the
    /// capture stream while recording; call it yourself only to encode frames from somewhere else.
    /// </summary>
    public void Encode(CapturedFrame request)
    {
        if (!Running)
            return;

        using var mapped = new MappedFrame(request, SessionStream.Capture);
        EncodeFrame(mapped, TimestampOf(request));
        FramesEncoded++;
    }

    /// <summary>Encodes one frame and calls <see cref="OutputFrame"/> with the result.</summary>
    /// <param name="frame">
    /// The camera's buffer, mapped. Planar formats arrive in several planes, so read it through
    /// <see cref="MappedFrame.ToBuffer"/> rather than
    /// <see cref="MappedFrame.Data"/>, which is only the first plane.
    /// </param>
    /// <param name="timestamp">Microseconds since the first frame of this recording.</param>
    protected abstract void EncodeFrame(MappedFrame frame, long timestamp);

    /// <summary>What this encoder produces, handed to each output before the first frame.</summary>
    protected abstract VideoStreamInfo StreamInfo { get; }

    /// <summary>Applies <paramref name="quality"/> to the encoder's own settings before it starts.</summary>
    protected virtual void Setup(Quality quality)
    {
    }

    /// <summary>Opens encoder resources after the outputs are open.</summary>
    protected virtual void Started()
    {
    }

    /// <summary>Flushes and closes encoder resources before the outputs close.</summary>
    protected virtual void Stopped()
    {
    }

    // Hands the output the codec's headers, for encoders that only have them once a frame is encoded.
    private protected void SetCodecExtraData(ReadOnlySpan<byte> extraData) => Output?.SetCodecExtraData(extraData);

    /// <summary>Sends an encoded frame to the output.</summary>
    protected void OutputFrame(ReadOnlySpan<byte> frame, bool keyframe, long timestamp) =>
        Output?.OutputFrame(frame, keyframe, timestamp);

    // SensorTimestamp in µs, rebased so the first frame is zero.
    private long TimestampOf(CapturedFrame request)
    {
        var sensorTimestamp = request.Metadata.TryGet(Controls.SensorTimestamp, out var nanoseconds) ? nanoseconds / 1000 : 0;
        _firstTimestamp ??= sensorTimestamp;
        return sensorTimestamp - _firstTimestamp.Value;
    }
}
