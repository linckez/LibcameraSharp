using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Core;

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
