using LibcameraSharp.Native.Interop;

using LibcameraSharp.Advanced;

namespace LibcameraSharp;

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
                        offsetValid ? (long)NativeMethods.libcamera_framebuffer_plane_offset(plane) : -1,
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

/// <summary>One plane of a <see cref="FrameBuffer"/>: a DMA-BUF fd with an offset and length.</summary>
/// <param name="Fd">File descriptor of the DMA-BUF. Owned by libcamera; don't close it.</param>
/// <param name="Offset">Byte offset of the plane within the fd, or -1 when unknown.</param>
/// <param name="Length">Plane size in bytes.</param>
public readonly record struct FrameBufferPlane(int Fd, long Offset, long Length);

/// <summary>Result of the capture into a buffer.</summary>
public enum FrameStatus
{
    /// <summary>The frame was captured.</summary>
    Success = libcamera_frame_metadata_status.LIBCAMERA_FRAME_METADATA_STATUS_SUCCESS,
    /// <summary>An error occurred; the buffer contents are undefined.</summary>
    Error = libcamera_frame_metadata_status.LIBCAMERA_FRAME_METADATA_STATUS_ERROR,
    /// <summary>The request was cancelled before capture.</summary>
    Cancelled = libcamera_frame_metadata_status.LIBCAMERA_FRAME_METADATA_STATUS_CANCELLED,
    /// <summary>libcamera hasn't captured into this buffer yet.</summary>
    Startup = libcamera_frame_metadata_status.LIBCAMERA_FRAME_METADATA_STATUS_STARTUP,
}

/// <summary>What libcamera recorded about a captured frame (<see cref="FrameBuffer.Metadata"/>).</summary>
public sealed unsafe class FrameMetadata
{
    private readonly libcamera_frame_metadata* _metadata;

    internal FrameMetadata(libcamera_frame_metadata* metadata) => _metadata = metadata;

    /// <summary>Whether the capture succeeded.</summary>
    public FrameStatus Status => (FrameStatus)NativeMethods.libcamera_frame_metadata_status(_metadata);

    /// <summary>Frame sequence number from the sensor.</summary>
    public uint Sequence => NativeMethods.libcamera_frame_metadata_sequence(_metadata);

    /// <summary>Capture time on the monotonic clock (<c>CLOCK_MONOTONIC</c>), so only differences between frames are meaningful.</summary>
    public TimeSpan Timestamp => TimeSpan.FromTicks((long)(NativeMethods.libcamera_frame_metadata_timestamp(_metadata) / 100));

    /// <summary>Bytes actually written to each plane.</summary>
    public IReadOnlyList<uint> BytesUsed
    {
        get
        {
            var planes = NativeMethods.libcamera_frame_metadata_planes(_metadata);
            try
            {
                var count = (int)NativeMethods.libcamera_frame_metadata_planes_size(planes);
                var result = new uint[count];
                for (var i = 0; i < count; i++)
                    result[i] = NativeMethods.libcamera_frame_metadata_planes_at(planes, (nuint)i)->bytes_used;
                return result;
            }
            finally
            {
                NativeMethods.libcamera_frame_metadata_planes_destroy(planes);
            }
        }
    }
}
