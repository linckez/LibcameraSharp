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

/// <summary>The sizes a stream format supports, as a min/max range with step. Layout matches libcamera's <c>SizeRange</c>.</summary>
/// <param name="Min">Smallest supported size.</param>
/// <param name="Max">Largest supported size.</param>
/// <param name="HorizontalStep">Width granularity between <paramref name="Min"/> and <paramref name="Max"/>; 0 when only the bounds are valid.</param>
/// <param name="VerticalStep">Height granularity; 0 when only the bounds are valid.</param>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct SizeRange(Size Min, Size Max, uint HorizontalStep, uint VerticalStep)
{
    /// <inheritdoc/>
    public override string ToString() => HorizontalStep == 0 && VerticalStep == 0
        ? $"{Min}-{Max}"
        : $"{Min}-{Max}/(+{HorizontalStep},+{VerticalStep})";
}
