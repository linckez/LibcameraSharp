using System.Runtime.InteropServices;

namespace LibcameraSharp;

/// <summary>A pixel position. Layout matches libcamera's <c>Point</c>.</summary>
/// <param name="X">Horizontal coordinate.</param>
/// <param name="Y">Vertical coordinate.</param>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct Point(int X, int Y)
{
    /// <inheritdoc/>
    public override string ToString() => $"({X}, {Y})";
}
