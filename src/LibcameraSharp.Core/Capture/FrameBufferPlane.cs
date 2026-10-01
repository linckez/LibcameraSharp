namespace LibcameraSharp.Core;

/// <summary>One plane of a <see cref="FrameBuffer"/>: a DMA-BUF fd with an offset and length.</summary>
/// <param name="Fd">File descriptor of the DMA-BUF. Owned by libcamera; don't close it.</param>
/// <param name="Offset">Byte offset of the plane within the fd, or null when libcamera reports it as not valid.</param>
/// <param name="Length">Plane size in bytes.</param>
public readonly record struct FrameBufferPlane(int Fd, long? Offset, long Length);
