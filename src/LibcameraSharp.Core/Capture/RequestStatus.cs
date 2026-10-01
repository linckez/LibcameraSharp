using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Core;

/// <summary>Where a request is in its life cycle.</summary>
public enum RequestStatus
{
    /// <summary>Created or queued, not yet completed.</summary>
    Pending = libcamera_request_status.LIBCAMERA_REQUEST_STATUS_PENDING,
    /// <summary>Completed; buffers hold frames and <see cref="Request.Metadata"/> is filled.</summary>
    Complete = libcamera_request_status.LIBCAMERA_REQUEST_STATUS_COMPLETE,
    /// <summary>Cancelled by <see cref="ActiveCamera.Stop"/> before completing.</summary>
    Cancelled = libcamera_request_status.LIBCAMERA_REQUEST_STATUS_CANCELLED,
}
