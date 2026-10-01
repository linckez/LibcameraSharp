using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Core;

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
