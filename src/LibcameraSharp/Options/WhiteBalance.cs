namespace LibcameraSharp;

/// <summary>How the camera should white-balance: an automatic mode, or gains you fix yourself.</summary>
public readonly record struct WhiteBalance
{
    private WhiteBalance(AwbMode mode, float red, float blue) => (Mode, RedGain, BlueGain) = (mode, red, blue);

    /// <summary>The automatic mode, when <see cref="IsManual"/> is false.</summary>
    public AwbMode Mode { get; }

    /// <summary>Red gain, when <see cref="IsManual"/>.</summary>
    public float RedGain { get; }

    /// <summary>Blue gain, when <see cref="IsManual"/>.</summary>
    public float BlueGain { get; }

    /// <summary>True when fixed gains were given rather than a mode.</summary>
    public bool IsManual => RedGain > 0 || BlueGain > 0;

    /// <summary>Let the camera decide, within the named mode.</summary>
    public static WhiteBalance Auto(AwbMode mode = AwbMode.Auto) => new(mode, 0, 0);

    /// <summary>Fix the red and blue gains.</summary>
    public static WhiteBalance Manual(float redGain, float blueGain) => new(AwbMode.Auto, redGain, blueGain);

    /// <inheritdoc/>
    public override string ToString() => IsManual ? $"gains {RedGain:0.##}/{BlueGain:0.##}" : Mode.ToString();
}
