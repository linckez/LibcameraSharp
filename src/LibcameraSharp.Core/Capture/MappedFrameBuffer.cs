using System.Buffers;
using LibcameraSharp.Native;

using LibcameraSharp.Advanced;

namespace LibcameraSharp;

/// <summary>
/// A <see cref="FrameBuffer"/> mapped into memory. Index it by plane to get the bytes; for
/// single-plane formats (RGB, packed Bayer) <c>mapped[0]</c> is the whole frame. Dispose to unmap.
/// </summary>
/// <remarks>
/// Each distinct DMA-BUF fd is mapped once, page-aligned, covering every plane that lives in it.
/// The mapping stays valid across captures into the same buffer, so map once and reuse.
/// </remarks>
public sealed class MappedFrameBuffer : IDisposable
{
    private readonly record struct PlaneView(int Fd, long Offset, long Length);

    private readonly Dictionary<int, (MappingHandle Mapping, long Start)> _mappings = [];
    private readonly PlaneView[] _planes;

    /// <summary>The buffer this mapping belongs to.</summary>
    public FrameBuffer Buffer { get; }

    /// <summary>True when the mapping allows writes.</summary>
    public bool IsWritable { get; }

    internal MappedFrameBuffer(FrameBuffer buffer, bool writable)
    {
        Buffer = buffer;
        IsWritable = writable;

        var planes = buffer.Planes;
        _planes = new PlaneView[planes.Count];
        var pageSize = (long)Environment.SystemPageSize;

        // For every fd, find the page-aligned window that covers all of its planes.
        var windows = new Dictionary<int, (long Start, long End)>();
        for (var i = 0; i < planes.Count; i++)
        {
            var plane = planes[i];
            if (plane.Offset is not { } offset)
                throw new InvalidOperationException($"Plane {i} has no valid offset, so it can't be mapped.");

            _planes[i] = new PlaneView(plane.Fd, offset, plane.Length);
            var alignedStart = offset - offset % pageSize;
            var end = offset + plane.Length;
            windows[plane.Fd] = windows.TryGetValue(plane.Fd, out var w)
                ? (Math.Min(w.Start, alignedStart), Math.Max(w.End, end))
                : (alignedStart, end);
        }

        // One mmap per fd. Sizes aren't checked against the fd (DMA-BUFs often report 0); mmap rejects bad ranges.
        try
        {
            foreach (var (fd, (start, end)) in windows)
                _mappings[fd] = (MappingHandle.Map(fd, start, (nuint)(end - start), writable), start);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Number of planes.</summary>
    public int PlaneCount => _planes.Length;

    /// <summary>Read-only view of one plane.</summary>
    public ReadOnlySpan<byte> this[int plane] => PlaneSpan(plane);

    /// <summary>Writable view of one plane; requires a mapping created with <c>writable: true</c>.</summary>
    /// <exception cref="InvalidOperationException">The mapping is read-only.</exception>
    public Span<byte> AsWritable(int plane) =>
        IsWritable ? PlaneSpan(plane) : throw new InvalidOperationException("The mapping is read-only; call Map(writable: true).");

    /// <summary>Copies one plane into a new array.</summary>
    public byte[] ToArray(int plane = 0) => this[plane].ToArray();

    /// <summary>Unmaps the memory. Spans obtained earlier must not be used afterwards.</summary>
    public void Dispose()
    {
        foreach (var (mapping, _) in _mappings.Values)
            mapping.Dispose();
        _mappings.Clear();
    }

    // The one place a raw address is touched: the mapped region plus the plane's offset within it.
    /// <summary>
    /// A <see cref="ReadOnlyMemory{T}"/> over one plane, for code that must carry it across an
    /// <c>await</c> — which a <see cref="Span{T}"/> cannot do.
    /// </summary>
    /// <remarks>
    /// The memory points straight at the mapping: nothing is copied, and it dangles once this buffer
    /// is disposed, exactly as the spans do. Whoever hands it out owns making that impossible.
    /// </remarks>
    public unsafe ReadOnlyMemory<byte> PlaneMemory(int plane)
    {
        var (mapping, start) = _mappings[_planes[plane].Fd];
        var descriptor = _planes[plane];
        return new MappedMemory(mapping.Pointer + (descriptor.Offset - start), checked((int)descriptor.Length)).Memory;
    }

    // A MemoryManager is the only way to get a Memory<byte> over memory the GC does not own.
    private sealed unsafe class MappedMemory(byte* pointer, int length) : MemoryManager<byte>
    {
        public override Span<byte> GetSpan() => new(pointer, length);

        // Already pinned: it is an mmap, not a GC allocation.
        public override MemoryHandle Pin(int elementIndex = 0) => new(pointer + elementIndex);

        public override void Unpin() { }

        protected override void Dispose(bool disposing) { }
    }

    private unsafe Span<byte> PlaneSpan(int index)
    {
        var plane = _planes[index];
        var (mapping, start) = _mappings[plane.Fd];
        return new Span<byte>(mapping.Pointer + (plane.Offset - start), checked((int)plane.Length));
    }
}
