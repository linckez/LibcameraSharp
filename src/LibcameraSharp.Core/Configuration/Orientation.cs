using LibcameraSharp.Native.Interop;

namespace LibcameraSharp;

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
