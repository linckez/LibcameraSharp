using LibcameraSharp.Native;
using LibcameraSharp.Native.Interop;

using LibcameraSharp.Advanced;
using Stream = LibcameraSharp.Advanced.Stream;   // libcamera's stream, not System.IO's

namespace LibcameraSharp;

/// <summary>
/// Allocates frame buffers from the camera's own memory (DMA-capable, so the ISP can write to them).
/// One allocator per camera; allocate once per stream after <see cref="ActiveCamera.Configure"/>.
/// </summary>
public sealed unsafe class FrameBufferAllocator : IDisposable
{
    private readonly ActiveCamera _camera;
    private readonly FrameBufferAllocatorHandle _handle;
    private readonly Dictionary<Stream, List<FrameBuffer>> _streams = [];

    /// <param name="camera">The acquired camera to allocate for. Disposing it frees anything this allocator still holds.</param>
    public FrameBufferAllocator(ActiveCamera camera)
    {
        _camera = camera;
        _handle = new FrameBufferAllocatorHandle(NativeMethods.libcamera_framebuffer_allocator_create(camera.Pointer));
        _camera.Track(this);
    }

    /// <summary>
    /// Allocates <see cref="StreamConfiguration.BufferCount"/> buffers for <paramref name="stream"/>.
    /// The buffers belong to this allocator and are freed with it.
    /// </summary>
    /// <exception cref="InvalidOperationException">Buffers were already allocated for the stream.</exception>
    /// <exception cref="LibcameraException">libcamera couldn't allocate, e.g. <c>ENOMEM</c> or an unconfigured stream.</exception>
    public IReadOnlyList<FrameBuffer> Allocate(Stream stream)
    {
        if (_streams.ContainsKey(stream))
            throw new InvalidOperationException("Buffers are already allocated for this stream.");

        LibcameraException.ThrowIfError(NativeMethods.libcamera_framebuffer_allocator_allocate(_handle.Pointer, stream.Pointer), "allocate frame buffers");

        // The list is owned by the allocator; only the individual buffer pointers are kept.
        var list = NativeMethods.libcamera_framebuffer_allocator_buffers(_handle.Pointer, stream.Pointer);
        var count = (int)NativeMethods.libcamera_framebuffer_list_size(list);
        var buffers = new List<FrameBuffer>(count);
        for (var i = 0; i < count; i++)
            buffers.Add(new FrameBuffer(NativeMethods.libcamera_framebuffer_list_get(list, (nuint)i), this));
        _streams[stream] = buffers;
        return buffers;
    }

    /// <summary>The buffers allocated for <paramref name="stream"/>, or empty when none were.</summary>
    public IReadOnlyList<FrameBuffer> Buffers(Stream stream) => _streams.GetValueOrDefault(stream) ?? [];

    /// <summary>
    /// Frees one stream's buffers, so the stream can be reconfigured and allocated again without
    /// discarding the allocator. Buffers already handed out become unusable.
    /// </summary>
    /// <exception cref="InvalidOperationException">The camera is running and still holds one of these buffers.</exception>
    /// <exception cref="LibcameraException">libcamera refused, e.g. <c>EBUSY</c> while the stream is in use.</exception>
    public void Free(Stream stream)
    {
        if (!_streams.TryGetValue(stream, out var buffers))
            return;
        if (_camera.IsStarted && _camera.HasQueuedBufferFrom(this))
            throw new InvalidOperationException("Stop the camera before freeing buffers it is streaming into.");

        LibcameraException.ThrowIfError(NativeMethods.libcamera_framebuffer_allocator_free(_handle.Pointer, stream.Pointer), "free frame buffers");
        foreach (var buffer in buffers)
            buffer.Invalidate();
        _streams.Remove(stream);
    }

    /// <summary>True while any stream still has buffers allocated.</summary>
    /// <remarks>libcamera <c>FrameBufferAllocator::allocated</c>; <c>lc-compliance</c> asserts it after freeing.</remarks>
    public bool IsAllocated => !_handle.IsClosed && NativeMethods.libcamera_framebuffer_allocator_allocated(_handle.Pointer);

    /// <summary>Frees every buffer. Stops the camera first if any of them is still queued, since libcamera can't stream into freed memory.</summary>
    public void Dispose()
    {
        if (_handle.IsClosed)
            return;
        if (_camera.IsStarted && _camera.HasQueuedBufferFrom(this))
            _camera.Stop();
        foreach (var buffer in _streams.Values.SelectMany(b => b))
            buffer.Invalidate();
        _streams.Clear();
        _handle.Dispose();
    }
}
