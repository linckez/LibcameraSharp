using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Core;

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
    public TimeSpan Timestamp => TimeSpan.FromTicks((long)(NativeMethods.libcamera_frame_metadata_timestamp(_metadata) / TimeSpan.NanosecondsPerTick));

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
