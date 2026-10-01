using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Core;

/// <summary>Whether a control is set by the application, reported by libcamera, or both.</summary>
[Flags]
public enum ControlDirection
{
    /// <summary>Set on a request's controls before queueing.</summary>
    In = libcamera_control_direction.LIBCAMERA_CONTROL_DIRECTION_IN,
    /// <summary>Reported in a completed request's metadata.</summary>
    Out = libcamera_control_direction.LIBCAMERA_CONTROL_DIRECTION_OUT,
}
