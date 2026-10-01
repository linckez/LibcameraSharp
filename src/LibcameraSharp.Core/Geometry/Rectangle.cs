using System.Runtime.InteropServices;

namespace LibcameraSharp;

/// <summary>A rectangle in pixel coordinates. Layout matches libcamera's <c>Rectangle</c>.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Rectangle(int X, int Y, uint Width, uint Height)
{
    /// <summary>The rectangle's size.</summary>
    public Size Size => new(Width, Height);

    /// <inheritdoc/>
    public override string ToString() => $"({X}, {Y})/{Width}x{Height}";
}
