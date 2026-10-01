namespace LibcameraSharp;

/// <summary>How long each frame is exposed: chosen by automatic exposure, or a time you fix.</summary>
public readonly record struct ExposureMode
{
    private ExposureMode(TimeSpan time) => Time = time;

    /// <summary>The fixed exposure time, or null when automatic exposure chooses.</summary>
    public TimeSpan? Time { get; }

    /// <summary>Let automatic exposure choose. This is also the <c>default</c>.</summary>
    public static ExposureMode Auto => default;

    /// <summary>Expose every frame for <paramref name="time"/>, sent in whole microseconds.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="time"/> is under one microsecond, which would reach the camera as zero.</exception>
    public static ExposureMode Fixed(TimeSpan time)
    {
        if (time < TimeSpan.FromMicroseconds(1))
            throw new ArgumentOutOfRangeException(nameof(time), time, "An exposure is at least one microsecond; use ExposureMode.Auto for automatic exposure.");
        return new(time);
    }

    // What goes to libcamera's ExposureTime: whole microseconds, with 0 for automatic, which the request turns into the
    // exposure mode.
    internal int Microseconds => Time is { } time ? (int)time.TotalMicroseconds : 0;

    /// <inheritdoc/>
    public override string ToString() => Time is { } time ? $"{time.TotalMilliseconds:0.###} ms" : "auto";
}
