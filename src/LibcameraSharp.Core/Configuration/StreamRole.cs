using LibcameraSharp.Native.Interop;

using LibcameraSharp.Advanced;

namespace LibcameraSharp;

/// <summary>What a stream is for; libcamera picks sensible defaults per role in <see cref="Camera.GenerateConfiguration"/>.</summary>
public enum StreamRole
{
    /// <summary>Unprocessed sensor data (Bayer), for DNG or your own processing.</summary>
    Raw = libcamera_stream_role.LIBCAMERA_STREAM_ROLE_RAW,
    /// <summary>Full-resolution stills.</summary>
    StillCapture = libcamera_stream_role.LIBCAMERA_STREAM_ROLE_STILL_CAPTURE,
    /// <summary>Video recording: steady frame rate over resolution.</summary>
    VideoRecording = libcamera_stream_role.LIBCAMERA_STREAM_ROLE_VIDEO_RECORDING,
    /// <summary>Low-latency preview.</summary>
    ViewFinder = libcamera_stream_role.LIBCAMERA_STREAM_ROLE_VIEW_FINDER,
}

/// <summary>Image orientation after libcamera applies rotation and mirroring, as in EXIF.</summary>
public enum Orientation
{
    /// <summary>No transform.</summary>
    Rotate0 = libcamera_orientation.LIBCAMERA_ORIENTATION_ROTATE_0,
    /// <summary>Mirrored horizontally.</summary>
    Rotate0Mirror = libcamera_orientation.LIBCAMERA_ORIENTATION_ROTATE_0_MIRROR,
    /// <summary>Rotated 180°.</summary>
    Rotate180 = libcamera_orientation.LIBCAMERA_ORIENTATION_ROTATE_180,
    /// <summary>Rotated 180° and mirrored.</summary>
    Rotate180Mirror = libcamera_orientation.LIBCAMERA_ORIENTATION_ROTATE_180_MIRROR,
    /// <summary>Rotated 90° and mirrored.</summary>
    Rotate90Mirror = libcamera_orientation.LIBCAMERA_ORIENTATION_ROTATE_90_MIRROR,
    /// <summary>Rotated 270°.</summary>
    Rotate270 = libcamera_orientation.LIBCAMERA_ORIENTATION_ROTATE_270,
    /// <summary>Rotated 270° and mirrored.</summary>
    Rotate270Mirror = libcamera_orientation.LIBCAMERA_ORIENTATION_ROTATE_270_MIRROR,
    /// <summary>Rotated 90°.</summary>
    Rotate90 = libcamera_orientation.LIBCAMERA_ORIENTATION_ROTATE_90,
}

/// <summary>Result of <see cref="CameraConfiguration.Validate"/>.</summary>
public enum ConfigurationStatus
{
    /// <summary>The configuration can be applied as-is.</summary>
    Valid = libcamera_camera_configuration_status.LIBCAMERA_CAMERA_CONFIGURATION_STATUS_VALID,
    /// <summary>libcamera changed some settings to make it work; re-read the stream configurations.</summary>
    Adjusted = libcamera_camera_configuration_status.LIBCAMERA_CAMERA_CONFIGURATION_STATUS_ADJUSTED,
    /// <summary>The configuration can't be made to work.</summary>
    Invalid = libcamera_camera_configuration_status.LIBCAMERA_CAMERA_CONFIGURATION_STATUS_INVALID,
}
