using LibcameraSharp.Native;
using LibcameraSharp.Native.Interop;

using LibcameraSharp.Advanced;
using Stream = LibcameraSharp.Advanced.Stream;   // libcamera's stream, not System.IO's

namespace LibcameraSharp;

/// <summary>Where a request is in its life cycle.</summary>
public enum RequestStatus
{
    /// <summary>Created or queued, not yet completed.</summary>
    Pending = libcamera_request_status.LIBCAMERA_REQUEST_STATUS_PENDING,
    /// <summary>Completed; buffers hold frames and <see cref="Request.Metadata"/> is filled.</summary>
    Complete = libcamera_request_status.LIBCAMERA_REQUEST_STATUS_COMPLETE,
    /// <summary>Cancelled by <see cref="ActiveCamera.Stop"/> before completing.</summary>
    Cancelled = libcamera_request_status.LIBCAMERA_REQUEST_STATUS_CANCELLED,
}

/// <summary>
/// One capture: a buffer per stream plus the controls to apply. Fill it, queue it with
/// <see cref="ActiveCamera.QueueRequest"/>, and when it comes back read the frame from the
/// buffer and the results from <see cref="Metadata"/>. Call <see cref="Reuse"/> to queue it again.
/// </summary>
public sealed unsafe class Request : IDisposable
{
    private readonly RequestHandle _handle;
    private readonly Dictionary<Stream, FrameBuffer> _buffers = [];
    private volatile bool _queued;
    private volatile bool _disposeWhenCompleted;

    internal Request(libcamera_request* request) => _handle = new(request);

    internal libcamera_request* Pointer => _handle.Pointer;

    /// <summary>Controls to apply for this capture (exposure, gain, focus, …). Set before queueing.</summary>
    public ControlList Controls => new(NativeMethods.libcamera_request_controls(Pointer));

    /// <summary>What libcamera reports about the captured frame; valid once the request has completed.</summary>
    public ControlList Metadata => new(NativeMethods.libcamera_request_metadata(Pointer));

    /// <summary>The value passed to <see cref="ActiveCamera.CreateRequest"/>.</summary>
    public ulong Cookie => NativeMethods.libcamera_request_cookie(Pointer);

    /// <summary>Frame sequence number, assigned when the request completes.</summary>
    public uint Sequence => NativeMethods.libcamera_request_sequence(Pointer);

    /// <summary>Current status.</summary>
    public RequestStatus Status => (RequestStatus)NativeMethods.libcamera_request_status(Pointer);

    /// <summary>True while libcamera owns the request (between queue and completion).</summary>
    public bool IsQueued => _queued;

    /// <summary>Streams that have a buffer attached, with their buffers.</summary>
    public IReadOnlyDictionary<Stream, FrameBuffer> Buffers => _buffers;

    /// <summary>Attaches a buffer to receive <paramref name="stream"/>'s frame. One buffer per stream.</summary>
    /// <exception cref="LibcameraException"><c>EEXIST</c> when the stream already has a buffer.</exception>
    public void AddBuffer(Stream stream, FrameBuffer buffer)
    {
        LibcameraException.ThrowIfError(NativeMethods.libcamera_request_add_buffer(Pointer, stream.Pointer, buffer.Pointer), "add buffer to request");
        _buffers[stream] = buffer;
    }

    /// <summary>The buffer attached for <paramref name="stream"/>.</summary>
    /// <exception cref="KeyNotFoundException">No buffer was added for that stream.</exception>
    public FrameBuffer Buffer(Stream stream) => _buffers[stream];

    /// <summary>
    /// Resets the request so it can be queued again. Buffers stay attached when
    /// <paramref name="keepBuffers"/> is true (the usual case); otherwise add them again.
    /// Controls and metadata are cleared either way.
    /// </summary>
    /// <exception cref="InvalidOperationException">The request is queued.</exception>
    public void Reuse(bool keepBuffers = true)
    {
        if (_queued)
            throw new InvalidOperationException("Cannot reuse a request while it is queued.");
        var flags = keepBuffers
            ? libcamera_request_reuse_flag.LIBCAMERA_REQUEST_REUSE_FLAG_REUSE_BUFFERS
            : libcamera_request_reuse_flag.LIBCAMERA_REQUEST_REUSE_FLAG_DEFAULT;
        NativeMethods.libcamera_request_reuse(Pointer, flags);
        if (!keepBuffers)
            _buffers.Clear();
    }

    /// <summary>libcamera's description, e.g. <c>Request(0:C:0/1:0)</c>.</summary>
    public override string ToString() => NativeMethods.libcamera_request_to_string(Pointer) ?? "";

    /// <summary>
    /// Destroys the request. While it is queued libcamera still owns it, so the destroy is
    /// deferred until it completes — which <see cref="ActiveCamera.Stop"/> forces.
    /// </summary>
    public void Dispose()
    {
        if (_queued)
        {
            _disposeWhenCompleted = true;
            return;
        }
        _handle.Dispose();
    }

    internal void MarkQueued() => _queued = true;

    // Called from libcamera's thread on completion; honours a Dispose() that arrived while queued.
    internal void MarkCompleted()
    {
        _queued = false;
        if (_disposeWhenCompleted)
            _handle.Dispose();
    }
}
