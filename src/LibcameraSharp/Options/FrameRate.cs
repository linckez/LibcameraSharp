namespace LibcameraSharp;

/// <summary>
/// A frame rate, or a range the camera may vary within. Assign a number for a fixed rate
/// (<c>FrameRate = 30</c>) or a pair for a range (<c>FrameRate = (5, 30)</c>).
/// </summary>
/// <remarks>
/// A fixed rate also caps exposure: 30 fps allows at most 33 ms. Give a range to let the camera
/// slow down in the dark. Zero means no limit: <c>FrameRate = 0</c> sends nothing, and a range
/// starting at zero lets the camera slow down as far as it can.
/// </remarks>
public readonly record struct FrameRate(double Min, double Max)
{
    /// <summary>A fixed rate.</summary>
    public static implicit operator FrameRate(double framesPerSecond) => new(framesPerSecond, framesPerSecond);

    /// <summary>A range the camera may vary within.</summary>
    public static implicit operator FrameRate((double Min, double Max) range) => new(range.Min, range.Max);

    /// <summary>True when this pins a single rate rather than allowing a range.</summary>
    public bool IsFixed => Min == Max;

    /// <summary>The <c>FrameDurationLimits</c> pair, in microseconds, shortest first.</summary>
    internal long[] ToDurationLimits() => [(long)(1_000_000 / Max), Min > 0 ? (long)(1_000_000 / Min) : LongestFrameDuration];

    // The longest frame libcamera is asked to allow when there is no lower limit: 1000 s.
    private const long LongestFrameDuration = 1_000_000_000;

    /// <inheritdoc/>
    public override string ToString() => IsFixed ? $"{Min:0.##} fps" : $"{Min:0.##}-{Max:0.##} fps";
}
