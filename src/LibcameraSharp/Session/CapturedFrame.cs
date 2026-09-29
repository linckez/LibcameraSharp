using LibcameraSharp.Advanced;
using Stream = LibcameraSharp.Advanced.Stream;   // libcamera's stream, not System.IO's

namespace LibcameraSharp;

/// <summary>
/// A frame the camera has finished, with the configuration it was captured under. Read it with
/// <see cref="CopyPixels"/>, <see cref="MakeBuffer"/> or <see cref="Metadata"/>, then
/// <see cref="Dispose"/> to hand the buffer back to the camera.
/// </summary>
/// <remarks>
/// One frame can have several holders at once (a frame loop and a recording, say), each with its own
/// <see cref="CapturedFrame"/>; the camera gets the buffer back once all of them are disposed.
/// </remarks>
internal sealed class CapturedFrame : IDisposable
{
    private readonly CameraSession _camera;
    private readonly BufferAllocation _allocation;
    private Request? _request;

    internal CapturedFrame(CameraSession camera, Request request, SessionConfiguration config, IReadOnlyDictionary<SessionStream, Stream> streams, int run, BufferAllocation allocation)
    {
        _camera = camera;
        _request = request;
        Config = config;
        Streams = streams;
        Run = run;
        _allocation = allocation;
    }

    /// <summary>The configuration the frame was captured with (a snapshot; later reconfiguration doesn't change it).</summary>
    public SessionConfiguration Config { get; }

    /// <summary>The libcamera stream behind each configured stream.</summary>
    public IReadOnlyDictionary<SessionStream, Stream> Streams { get; }

    /// <summary>The underlying libcamera request, for the lower-level API.</summary>
    /// <exception cref="ObjectDisposedException">The frame has been disposed, or the camera closed, which freed its buffer.</exception>
    public Request Request => _request is { } request && !_allocation.Closed ? request : throw new ObjectDisposedException(nameof(CapturedFrame));

    /// <summary>The frame's sequence number, from the buffer libcamera filled; gaps mean dropped frames.</summary>
    /// <remarks>Not <c>Request.Sequence</c>, which has no gaps when the application is the slow one.</remarks>
    public uint Sequence => Request.Buffer(StreamFor(SessionStream.Capture)).Metadata.Sequence;

    /// <summary>The raw bytes of one stream's buffer, padding included, as a copy.</summary>
    public byte[] MakeBuffer(SessionStream stream = SessionStream.Capture)
    {
        using var mapped = Request.Buffer(StreamFor(stream)).Map();
        var total = 0;
        for (var i = 0; i < mapped.PlaneCount; i++)
            total += mapped[i].Length;
        var data = new byte[total];
        var offset = 0;
        for (var i = 0; i < mapped.PlaneCount; i++)
        {
            mapped[i].CopyTo(data.AsSpan(offset));
            offset += mapped[i].Length;
        }
        return data;
    }

    /// <summary>A copy of one stream's frame as the camera wrote it, with its format, size, stride and colour space.</summary>
    public FramePixels CopyPixels(SessionStream stream = SessionStream.Capture)
    {
        var config = Config[stream] ?? throw new ArgumentException($"The {stream} stream is not configured.", nameof(stream));
        return new FramePixels(MakeBuffer(stream), config.Format!.Value, config.Size!.Value, (int)config.Stride!.Value, Config.ColourSpace);
    }

    /// <summary>What the camera reported for this frame, keyed by control; a snapshot you can keep after disposing the request.</summary>
    public Metadata Metadata => new(Request.Metadata);

    /// <summary>The camera run this frame came from; it changes each time the camera is stopped and started.</summary>
    internal int Run { get; }

    /// <summary>Gives the frame's buffer back; the camera queues it again once every holder has.</summary>
    public void Dispose()
    {
        // Taken atomically, so two threads disposing the same frame give its lease back once.
        if (Interlocked.Exchange(ref _request, null) is { } request)
            _camera.Release(request, _allocation);
    }

    private Stream StreamFor(SessionStream stream) =>
        Streams.TryGetValue(stream, out var found) ? found : throw new ArgumentException($"The {stream} stream is not configured.", nameof(stream));
}
