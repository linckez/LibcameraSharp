using LibcameraSharp.Native.Interop;

namespace LibcameraSharp;

/// <summary>Where libcamera writes its log.</summary>
public enum LibcameraLogTarget
{
    /// <summary>Discard everything.</summary>
    None = libcamera_logging_target.LIBCAMERA_LOGGING_TARGET_NONE,
    /// <summary>syslog.</summary>
    Syslog = libcamera_logging_target.LIBCAMERA_LOGGING_TARGET_SYSLOG,
}
