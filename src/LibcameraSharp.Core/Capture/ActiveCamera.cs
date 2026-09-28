using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using LibcameraSharp.Native.Interop;

using LibcameraSharp.Advanced;

namespace LibcameraSharp;

/// <summary>
/// An acquired camera: configure it, allocate buffers, queue <see cref="Request"/>s, and read
/// completed ones from <see cref="CompletedRequests"/>.
/// <code>
/// using var camera = manager.Cameras[0].Acquire();
/// using var config = camera.GenerateConfiguration(StreamRole.StillCapture)!;
/// camera.Configure(config);
/// var stream = config[0].Stream;
/// using var allocator = new FrameBufferAllocator(camera);
/// var request = camera.CreateRequest();
/// request.AddBuffer(stream, allocator.Allocate(stream)[0]);
/// camera.Start();
/// camera.QueueRequest(request);
/// var done = await camera.CompletedRequests.ReadAsync();
/// </code>
/// </summary>
public sealed unsafe class ActiveCamera : Camera
{
    private readonly ConcurrentDictionary<nint, Request> _queued = new();
    private readonly List<WeakReference<FrameBufferAllocator>> _allocators = [];
    private readonly Channel<Request> _completed = Channel.CreateUnbounded<Request>();
    private readonly GCHandle _self;
    private libcamera_callback_handle* _completedHandle;

    internal ActiveCamera(libcamera_camera_t* camera, CameraManager manager) : base(camera, manager)
    {
        // libcamera calls back on its own thread with this handle as user data; freed in Dispose.
        _self = GCHandle.Alloc(this);
        _completedHandle = NativeMethods.libcamera_camera_request_completed_connect(camera, &OnRequestCompleted, (void*)GCHandle.ToIntPtr(_self));
    }

    // Releasing a camera whose buffers are still allocated makes libcamera tear down a media device
    // that is still in use, which crashes. So the camera frees the allocators it handed out.
    internal void Track(FrameBufferAllocator allocator)
    {
        lock (_allocators)
        {
            _allocators.RemoveAll(reference => !reference.TryGetTarget(out _));
            _allocators.Add(new WeakReference<FrameBufferAllocator>(allocator));
        }
    }

    /// <summary>Requests libcamera has finished, in completion order. Each request appears exactly once per queueing.</summary>
    /// <remarks>
    /// Unbounded, and you must drain it: a recycled request completes again, so entries accumulate per
    /// completion rather than per buffer. If you handle <see cref="RequestCompleted"/> instead, drain this
    /// as well; reading neither grows it without bound.
    /// Dropping here is not an option: libcamera's <c>requestCompleted</c> signal never drops, and a
    /// request that never arrives is a buffer that never returns.
    /// </remarks>
    public ChannelReader<Request> CompletedRequests => _completed.Reader;

    /// <summary>
    /// Raised on libcamera's thread when a request completes, before it is written to
    /// <see cref="CompletedRequests"/>. Keep handlers short; exceptions are swallowed.
    /// </summary>
    public event Action<Request>? RequestCompleted;

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    public bool IsStarted { get; private set; }

    /// <summary>Applies a validated configuration. Streams become available on it afterwards.</summary>
    /// <exception cref="LibcameraException"><c>EACCES</c> while running; <c>EINVAL</c> for an invalid configuration.</exception>
    public void Configure(CameraConfiguration configuration) =>
        LibcameraException.ThrowIfError(NativeMethods.libcamera_camera_configure(Pointer, configuration.Pointer), "configure camera");

    /// <summary>Creates an empty request. Attach a buffer per stream, set controls, then <see cref="QueueRequest"/>.</summary>
    /// <param name="cookie">An opaque value returned unchanged in <see cref="Request.Cookie"/>, for correlating requests.</param>
    /// <exception cref="LibcameraException">The camera has not been configured.</exception>
    public Request CreateRequest(ulong cookie = 0)
    {
        var request = NativeMethods.libcamera_camera_create_request(Pointer, cookie);
        return request is null ? throw new LibcameraException("create request: configure the camera first") : new Request(request);
    }

    /// <summary>Hands the request to libcamera. It comes back through <see cref="CompletedRequests"/>.</summary>
    /// <exception cref="LibcameraException"><c>EACCES</c> when not started; <c>EINVAL</c> when the request has no buffers or wasn't reset with <see cref="Request.Reuse"/>.</exception>
    public void QueueRequest(Request request)
    {
        // Register before queueing: completion can fire on libcamera's thread before this call returns.
        request.MarkQueued();
        _queued[(nint)request.Pointer] = request;
        var ret = NativeMethods.libcamera_camera_queue_request(Pointer, request.Pointer);
        if (ret < 0)
        {
            _queued.TryRemove((nint)request.Pointer, out _);
            request.MarkCompleted();
            throw new LibcameraException("queue request", -ret);
        }
    }

    /// <summary>Starts streaming. Queue requests after this.</summary>
    /// <param name="initialControls">Controls to apply before the first frame, e.g. exposure settings.</param>
    public void Start(ControlList? initialControls = null)
    {
        LibcameraException.ThrowIfError(NativeMethods.libcamera_camera_start(Pointer, initialControls is null ? null : initialControls.Pointer), "start camera");
        IsStarted = true;
    }

    /// <summary>
    /// Stops streaming. Requests still queued complete with <see cref="RequestStatus.Cancelled"/> and,
    /// like every completed request, arrive in <see cref="CompletedRequests"/> — so drain it before
    /// starting again, or the next session reads the previous one's requests (whose buffers may be
    /// freed by then).
    /// </summary>
    public void Stop()
    {
        if (!IsStarted)
            return;
        LibcameraException.ThrowIfError(NativeMethods.libcamera_camera_stop(Pointer), "stop camera");
        IsStarted = false;
    }

    /// <summary>Stops the camera if running, disconnects callbacks and releases it for others to acquire.</summary>
    public override void Dispose()
    {
        if (_completedHandle is null)
            return;

        // Stop first: libcamera hands every queued request back (cancelled) through the callback, synchronously.
        NativeMethods.libcamera_camera_stop(Pointer);
        IsStarted = false;

        // Then free any buffers still allocated for this camera; releasing it with them alive would
        // leave libcamera removing a media device that is still in use.
        FrameBufferAllocator[] allocators;
        lock (_allocators)
        {
            allocators = [.. _allocators.Select(reference => reference.TryGetTarget(out var allocator) ? allocator : null).OfType<FrameBufferAllocator>()];
            _allocators.Clear();
        }
        foreach (var allocator in allocators)
            allocator.Dispose();

        NativeMethods.libcamera_camera_request_completed_disconnect(Pointer, _completedHandle);
        _completedHandle = null;
        NativeMethods.libcamera_camera_release(Pointer);
        _completed.Writer.TryComplete();
        _queued.Clear();

        _self.Free();
        base.Dispose();
    }

    // Whether any request libcamera still holds carries a buffer from `allocator`.
    internal bool HasQueuedBufferFrom(FrameBufferAllocator allocator) =>
        _queued.Values.Any(request => request.Buffers.Values.Any(buffer => buffer.IsOwnedBy(allocator)));

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnRequestCompleted(void* data, libcamera_request* native)
    {
        var self = (ActiveCamera)GCHandle.FromIntPtr((nint)data).Target!;
        if (!self._queued.TryRemove((nint)native, out var request))
            return;
        request.MarkCompleted();

        // A throwing handler must not stop the request reaching the channel, nor escape into libcamera's thread.
        try
        {
            self.RequestCompleted?.Invoke(request);
        }
        catch
        {
            // Swallowed by design; see RequestCompleted docs.
        }
        self._completed.Writer.TryWrite(request);
    }
}
