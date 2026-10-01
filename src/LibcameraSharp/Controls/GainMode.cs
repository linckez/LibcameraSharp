namespace LibcameraSharp;

/// <summary>The sensor's analogue gain: chosen by automatic exposure, or a gain you fix.</summary>
public readonly record struct GainMode
{
    private GainMode(float gain) => Gain = gain;

    /// <summary>The fixed analogue gain, or null when automatic exposure chooses.</summary>
    public float? Gain { get; }

    /// <summary>Let automatic exposure choose. This is also the <c>default</c>.</summary>
    public static GainMode Auto => default;

    /// <summary>Hold the analogue gain at <paramref name="gain"/>, from 1.0 up; the maximum depends on the sensor.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="gain"/> is below 1.0, which libcamera doesn't allow (zero would mean automatic), or isn't a finite number.
    /// </exception>
    public static GainMode Fixed(float gain)
    {
        if (!(gain >= 1) || float.IsInfinity(gain))
            throw new ArgumentOutOfRangeException(nameof(gain), gain, "A fixed gain is a finite multiplier from 1.0 up; use GainMode.Auto for automatic.");
        return new(gain);
    }

    // What goes to libcamera's AnalogueGain: the gain, with 0 for automatic, which the request turns into the gain mode.
    internal float Value => Gain ?? 0f;

    /// <inheritdoc/>
    public override string ToString() => Gain is { } gain ? $"{gain:0.##}×" : "auto";
}
