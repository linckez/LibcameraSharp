using System.Runtime.InteropServices;

namespace LibcameraSharp.Core;

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
