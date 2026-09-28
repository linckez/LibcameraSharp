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

    /// <inheritdoc/>
    public override string ToString() => $"{X:0.###},{Y:0.###} {Width:0.###}x{Height:0.###}";
}
