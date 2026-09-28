using LibcameraSharp.Advanced;
using Stream = LibcameraSharp.Advanced.Stream;   // libcamera's stream, not System.IO's

namespace LibcameraSharp;

/// <summary>
/// The buffers and requests of one configuration. They outlive a reconfigure for as long as a
/// <see cref="CapturedFrame"/> still reads from them, and are freed once the last one is disposed.
/// </summary>
/// <remarks>The allocation also owns the requests made over its buffers, and disposes them with it.</remarks>
internal sealed class BufferAllocation : IDisposable
{
    private readonly Lock _lock = new();
    private readonly FrameBufferAllocator _allocator;
    private readonly List<Request> _requests = [];
    private readonly Dictionary<Request, int> _acquired = [];          // readers per request: a frame loop and each recording
    private bool _retired;
    private bool _disposed;

    /// <summary>Allocates <c>buffer_count</c> buffers for every stream of the configured camera.</summary>
    public BufferAllocation(ActiveCamera camera, IEnumerable<Stream> streams)
    {
        _allocator = new FrameBufferAllocator(camera);
        foreach (var stream in streams)
            _allocator.Allocate(stream);
    }

    public IReadOnlyList<FrameBuffer> Buffers(Stream stream) => _allocator.Buffers(stream);

    /// <summary>A request created for this allocation; it is disposed with the allocation.</summary>
    public Request Track(Request request)
    {
        lock (_lock)
            _requests.Add(request);
        return request;
    }

    public bool Owns(Request request)
    {
        lock (_lock)
            return _requests.Contains(request);
    }

    /// <summary>Marks the request as read by one more <see cref="CapturedFrame"/>.</summary>
    public void Acquire(Request request)
    {
        lock (_lock)
            _acquired[request] = _acquired.GetValueOrDefault(request) + 1;
    }

    /// <summary>Marks the request as no longer read; frees everything if this allocation was retired meanwhile.</summary>
    public void Release(Request request)
    {
        lock (_lock)
        {
            if (_acquired.TryGetValue(request, out var readers) && readers > 1)
                _acquired[request] = readers - 1;
            else
                _acquired.Remove(request);
            if (!_retired || _acquired.Count > 0)
                return;
        }
        Dispose();
    }

    /// <summary>Replaced by a newer allocation: free now, or when the last reader lets go.</summary>
    public void Retire()
    {
        lock (_lock)
        {
            _retired = true;
            if (_acquired.Count > 0)
                return;
        }
        Dispose();
    }

    /// <summary>Disposes the requests of a stopped camera, except those still being read; the next start makes fresh ones.</summary>
    public void DiscardRequests()
    {
        List<Request> unused;
        lock (_lock)
        {
            unused = [.. _requests.Where(r => !_acquired.ContainsKey(r))];
            _requests.RemoveAll(unused.Contains);
        }
        foreach (var request in unused)
            request.Dispose();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        foreach (var request in _requests)
            request.Dispose();
        _requests.Clear();
        _allocator.Dispose();
    }
}
