using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Core;

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
