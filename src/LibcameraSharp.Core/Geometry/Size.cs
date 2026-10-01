using System.Runtime.InteropServices;

namespace LibcameraSharp;

/// <summary>A width and height in pixels. Layout matches libcamera's <c>Size</c>.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Size(uint Width, uint Height)
{
    /// <summary>Creates a size from signed dimensions; both must be non-negative.</summary>
    public Size(int width, int height) : this(checked((uint)width), checked((uint)height)) { }

    /// <summary>True when either dimension is zero.</summary>
    public bool IsEmpty => Width == 0 || Height == 0;

    /// <inheritdoc/>
    public override string ToString() => $"{Width}x{Height}";
}
