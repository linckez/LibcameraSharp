namespace LibcameraSharp;

/// <summary>
/// The part of the sensor to read, as fractions of the full field: <c>new(0.25, 0.25, 0.5, 0.5)</c>
/// is the middle half. Digital zoom.
/// </summary>
public readonly record struct RegionOfInterest(double X, double Y, double Width, double Height)
{
    /// <summary>The whole field.</summary>
    public static RegionOfInterest Full { get; } = new(0, 0, 1, 1);

    /// <summary>True when this selects everything, so no crop need be sent.</summary>
    public bool IsFull => this == Full;

    // A region lies inside the frame and has a size. A little slack for sums like 0.7 + 0.3 that land just past 1.
    internal void ThrowIfInvalid(string paramName)
    {
        const double Slack = 1e-9;
        if (!(X >= 0 && Y >= 0 && Width > 0 && Height > 0 && X + Width <= 1 + Slack && Y + Height <= 1 + Slack))
            throw new ArgumentOutOfRangeException(paramName, this, "A region is in fractions of the frame: inside 0 to 1, with a width and height above 0.");
    }

    /// <inheritdoc/>
    public override string ToString() => $"{X:0.###},{Y:0.###} {Width:0.###}x{Height:0.###}";
}
