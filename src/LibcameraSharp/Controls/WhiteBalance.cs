namespace LibcameraSharp;

/// <summary>How the camera should white-balance: an automatic mode, or gains you fix yourself.</summary>
public readonly record struct WhiteBalance
{
    // The default is automatic white balance in AwbMode.Auto (libcamera's 0), so default(WhiteBalance) means that.
    private readonly bool _manual;
    private readonly AwbMode _mode;
    private readonly float _red, _blue;

    private WhiteBalance(bool manual, AwbMode mode, float red, float blue) => (_manual, _mode, _red, _blue) = (manual, mode, red, blue);

    /// <summary>The automatic mode, or null when the gains are fixed.</summary>
    public AwbMode? Mode => _manual ? null : _mode;

    /// <summary>The fixed red gain, or null when white balance is automatic.</summary>
    public float? RedGain => _manual ? _red : null;

    /// <summary>The fixed blue gain, or null when white balance is automatic.</summary>
    public float? BlueGain => _manual ? _blue : null;

    /// <summary>True when fixed gains were given rather than a mode.</summary>
    public bool IsManual => _manual;

    /// <summary>Let the camera decide, within the named mode.</summary>
    public static WhiteBalance Auto(AwbMode mode = AwbMode.Auto) => new(false, mode, 0, 0);

    /// <summary>Fix the red and blue gains, which turns automatic white balance off.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A gain is zero, which the camera reads as "automatic".</exception>
    public static WhiteBalance Manual(float redGain, float blueGain)
    {
        if (redGain == 0)
            throw new ArgumentOutOfRangeException(nameof(redGain), redGain, "A gain of zero means automatic; use WhiteBalance.Auto.");
        if (blueGain == 0)
            throw new ArgumentOutOfRangeException(nameof(blueGain), blueGain, "A gain of zero means automatic; use WhiteBalance.Auto.");
        return new(true, AwbMode.Auto, redGain, blueGain);
    }

    /// <inheritdoc/>
    public override string ToString() => _manual ? $"gains {_red:0.##}/{_blue:0.##}" : _mode.ToString();
}
