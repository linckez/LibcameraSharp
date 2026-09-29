using LibcameraSharp.Advanced;
using Stream = LibcameraSharp.Advanced.Stream;   // libcamera's stream, not System.IO's

namespace LibcameraSharp;

/// <summary>
/// The buffers of one configuration, with one request per buffer for as long as the allocation lives. Only the
/// session's loop touches it. It outlives a reconfigure for as long as a <see cref="CapturedFrame"/> still reads
/// from it, and is freed when the last one is disposed.
/// </summary>
/// <remarks>
/// A request stays tied to its buffer, so a frame someone holds across a stop and start stays theirs: a start
/// queues only the requests nobody holds.
/// </remarks>
internal sealed class BufferAllocation
{
    private readonly FrameBufferAllocator _allocator;
    private readonly Dictionary<Request, Slot> _slots = [];
    private bool _closed;
    private bool _retired;

    /// <summary>What the loop knows about one request: whether libcamera has it, in which run, and who holds its frame.</summary>
    internal sealed class Slot
    {
        /// <summary>True while libcamera has the request.</summary>
        public bool Queued { get; set; }

        /// <summary>
        /// Completions from before a stop still on their way. For each stop that found libcamera holding the request,
        /// exactly one more completion comes, cancelled by the stop or already in flight; it belongs to an old run and
        /// is ignored, even if the request has been queued again since.
        /// </summary>
        public int StaleCompletions { get; set; }

        /// <summary>Holders of its frame: frame loops, captures, recordings and the ready slot. Queued again only at zero.</summary>
        public int Leases { get; set; }
    }

    /// <summary>Allocates the buffers of every stream and makes one request per buffer, as many as the stream with the fewest buffers has.</summary>
    public BufferAllocation(ActiveCamera camera, IReadOnlyDictionary<SessionStream, Stream> streams)
    {
        _allocator = new FrameBufferAllocator(camera);
        foreach (var stream in streams.Values)
            _allocator.Allocate(stream);

        var count = streams.Values.Min(stream => _allocator.Buffers(stream).Count);
        for (var i = 0; i < count; i++)
        {
            var request = camera.CreateRequest((ulong)i);
            foreach (var stream in streams.Values)
                request.AddBuffer(stream, _allocator.Buffers(stream)[i]);
            _slots[request] = new Slot();
        }
    }

    /// <summary>Every request of this allocation, with what the loop knows about it.</summary>
    public IReadOnlyDictionary<Request, Slot> Slots => _slots;

    /// <summary>What the loop knows about <paramref name="request"/>, or null when it isn't one of this allocation's.</summary>
    public Slot? SlotOf(Request request) => _slots.GetValueOrDefault(request);

    /// <summary>True once the camera is closed: a frame read after that has no buffer behind it. Read from any thread.</summary>
    public bool Closed => Volatile.Read(ref _closed);

    /// <summary>Marks the allocation closed, before the camera frees its buffers.</summary>
    public void Close() => Volatile.Write(ref _closed, true);

    /// <summary>True once freed.</summary>
    public bool Disposed { get; private set; }

    /// <summary>Replaced by a newer allocation, or the camera closed: free now, or when the last frame comes back.</summary>
    public void Retire()
    {
        _retired = true;
        FreeIfUnused();
    }

    /// <summary>Called after a lease is given back: a retired allocation is freed with its last frame.</summary>
    public void FreeIfUnused()
    {
        if (!_retired || Disposed || _slots.Values.Any(slot => slot.Leases > 0))
            return;
        Disposed = true;
        foreach (var request in _slots.Keys)
            request.Dispose();
        _allocator.Dispose();
    }
}
