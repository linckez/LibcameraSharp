namespace LibcameraSharp;

/// <summary>Flicker avoidance: off, or the period of the lights to avoid.</summary>
public readonly record struct FlickerMode
{
    private FlickerMode(TimeSpan period) => Period = period;

    /// <summary>How fast the lights pulse, or null when flicker avoidance is off.</summary>
    public TimeSpan? Period { get; }

    /// <summary>No flicker avoidance. This is also the <c>default</c>.</summary>
    public static FlickerMode Off => default;

    /// <summary>
    /// Keep automatic exposure to multiples of <paramref name="period"/>, so lights pulsing at that rate don't leave dark
    /// bands across the picture: 10 ms where mains power is 50 Hz, 8.33 ms where it is 60 Hz.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="period"/> is under one microsecond, which would reach the camera as zero.</exception>
    public static FlickerMode Manual(TimeSpan period)
    {
        if (period < TimeSpan.FromMicroseconds(1))
            throw new ArgumentOutOfRangeException(nameof(period), period, "A flicker period is at least one microsecond; use FlickerMode.Off to turn it off.");
        return new(period);
    }

    /// <inheritdoc/>
    public override string ToString() => Period is { } period ? $"{period.TotalMilliseconds:0.##} ms" : "off";
}
