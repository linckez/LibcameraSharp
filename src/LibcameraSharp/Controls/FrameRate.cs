namespace LibcameraSharp;

/// <summary>
/// A frame rate, or a range the camera may vary within. Assign a number for a fixed rate
/// (<c>FrameRate = 30</c>) or a pair for a range (<c>FrameRate = (5, 30)</c>).
/// </summary>
/// <remarks>
/// A fixed rate also caps exposure: 30 fps allows at most 33 ms. Give a range to let the camera
/// slow down in the dark. To leave the frame rate as the camera has it, leave it null.
/// </remarks>
public readonly record struct FrameRate(double Min, double Max)
{
    /// <summary>A fixed rate.</summary>
    public static implicit operator FrameRate(double framesPerSecond) => new(framesPerSecond, framesPerSecond);

    /// <summary>A range the camera may vary within.</summary>
    public static implicit operator FrameRate((double Min, double Max) range) => new(range.Min, range.Max);

    /// <summary>True when this pins a single rate rather than allowing a range.</summary>
    public bool IsFixed => Min == Max;

    // A rate is a finite number of frames a second above zero, and a range can't end below where it starts.
    internal void ThrowIfInvalid(string? paramName)
    {
        if (!double.IsFinite(Min) || !double.IsFinite(Max) || Min <= 0 || Max < Min)
            throw new ArgumentOutOfRangeException(paramName, this, "A frame rate is a finite number of frames a second above 0, and a range's Max can't be below its Min.");
    }

    /// <summary>The <c>FrameDurationLimits</c> pair, in microseconds, shortest first.</summary>
    internal long[] ToDurationLimits() => [(long)(1_000_000 / Max), (long)(1_000_000 / Min)];

    /// <inheritdoc/>
    public override string ToString() => IsFixed ? $"{Min:0.##} fps" : $"{Min:0.##}-{Max:0.##} fps";
}
