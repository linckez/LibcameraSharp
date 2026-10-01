using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Core;

/// <summary>Element type of a control or property value, as libcamera stores it.</summary>
public enum ControlType
{
    /// <summary>No value.</summary>
    None = libcamera_control_type.LIBCAMERA_CONTROL_TYPE_NONE,
    /// <summary><see cref="bool"/>.</summary>
    Bool = libcamera_control_type.LIBCAMERA_CONTROL_TYPE_BOOL,
    /// <summary><see cref="byte"/>.</summary>
    Byte = libcamera_control_type.LIBCAMERA_CONTROL_TYPE_BYTE,
    /// <summary><see cref="ushort"/>.</summary>
    Unsigned16 = libcamera_control_type.LIBCAMERA_CONTROL_TYPE_UINT16,
    /// <summary><see cref="uint"/>.</summary>
    Unsigned32 = libcamera_control_type.LIBCAMERA_CONTROL_TYPE_UINT32,
    /// <summary><see cref="int"/>.</summary>
    Integer32 = libcamera_control_type.LIBCAMERA_CONTROL_TYPE_INT32,
    /// <summary><see cref="long"/>.</summary>
    Integer64 = libcamera_control_type.LIBCAMERA_CONTROL_TYPE_INT64,
    /// <summary><see cref="float"/>.</summary>
    Float = libcamera_control_type.LIBCAMERA_CONTROL_TYPE_FLOAT,
    /// <summary><see cref="string"/>.</summary>
    String = libcamera_control_type.LIBCAMERA_CONTROL_TYPE_STRING,
    /// <summary><see cref="LibcameraSharp.Rectangle"/>.</summary>
    Rectangle = libcamera_control_type.LIBCAMERA_CONTROL_TYPE_RECTANGLE,
    /// <summary><see cref="LibcameraSharp.Size"/>.</summary>
    Size = libcamera_control_type.LIBCAMERA_CONTROL_TYPE_SIZE,
    /// <summary><see cref="LibcameraSharp.Point"/>.</summary>
    Point = libcamera_control_type.LIBCAMERA_CONTROL_TYPE_POINT,
}
