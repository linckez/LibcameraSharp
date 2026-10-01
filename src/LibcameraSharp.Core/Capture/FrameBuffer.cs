using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Core;

/// <summary>
/// Memory for one frame, as one or more planes backed by DMA-BUF file descriptors. Get buffers
/// from <see cref="FrameBufferAllocator.Allocate"/>; read pixels through <see cref="Map"/>.
/// </summary>
public sealed unsafe class FrameBuffer
{
    private libcamera_framebuffer* _buffer;
    private readonly FrameBufferAllocator _owner;   // keeps the allocator alive while a buffer is referenced

    internal FrameBuffer(libcamera_framebuffer* buffer, FrameBufferAllocator owner)
    {
        _buffer = buffer;
        _owner = owner;
    }

    internal bool IsOwnedBy(FrameBufferAllocator allocator) => ReferenceEquals(_owner, allocator);

    internal libcamera_framebuffer* Pointer
    {
        get
        {
            ObjectDisposedException.ThrowIf(_buffer is null, this);
            return _buffer;
        }
    }

    /// <summary>The planes making up the frame (1 for packed formats, 2–3 for planar YUV).</summary>
    public IReadOnlyList<FrameBufferPlane> Planes
    {
        get
        {
            var planes = NativeMethods.libcamera_framebuffer_planes(Pointer);
            try
            {
                var count = (int)NativeMethods.libcamera_framebuffer_planes_size(planes);
                var result = new FrameBufferPlane[count];
                for (var i = 0; i < count; i++)
                {
                    var plane = NativeMethods.libcamera_framebuffer_planes_at(planes, (nuint)i);
                    var offsetValid = NativeMethods.libcamera_framebuffer_plane_offset_valid(plane);
                    result[i] = new FrameBufferPlane(
                        NativeMethods.libcamera_framebuffer_plane_fd(plane),
                        offsetValid ? (long)NativeMethods.libcamera_framebuffer_plane_offset(plane) : null,
                        (long)NativeMethods.libcamera_framebuffer_plane_length(plane));
                }
                return result;
            }
            finally
            {
                NativeMethods.libcamera_framebuffer_planes_destroy(planes);
            }
        }
    }

    /// <summary>What libcamera reports about the last frame captured into this buffer.</summary>
    public FrameMetadata Metadata => new(NativeMethods.libcamera_framebuffer_metadata(Pointer));

    /// <summary>An opaque value you can attach to the buffer.</summary>
    public ulong Cookie
    {
        get => NativeMethods.libcamera_framebuffer_cookie(Pointer);
        set => NativeMethods.libcamera_framebuffer_set_cookie(Pointer, value);
    }

    /// <summary>Maps the planes into memory for reading. Dispose the mapping when done.</summary>
    public MappedFrameBuffer Map(bool writable = false) => new(this, writable);

    internal void Invalidate() => _buffer = null;
}
