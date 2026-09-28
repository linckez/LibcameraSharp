namespace LibcameraSharp;

/// <summary>Where the lens should focus: an autofocus mode, or a distance you choose.</summary>
public readonly record struct FocusMode
{
    private FocusMode(AfMode mode, float? dioptres, bool cancelsScan = false) => (Mode, Dioptres, CancelsScan) = (mode, dioptres, cancelsScan);

    /// <summary>The autofocus mode.</summary>
    public AfMode Mode { get; }

    /// <summary>Lens position in dioptres (1/metres), when the mode is manual. 0 is infinity.</summary>
    public float? Dioptres { get; }

    /// <summary>Focus once: before each photo, or when set with <see cref="CameraDevice.SetControls"/>.</summary>
    public static FocusMode Auto => new(AfMode.Auto, null);

    // Autofocus that keeps the lens where the last scan left it: a photo taken after its focus scan.
    internal static FocusMode KeepScanned => new(AfMode.Auto, null, cancelsScan: true);

    // True for KeepScanned: the scan is cancelled rather than started, so the lens stays put.
    internal bool CancelsScan { get; }

    /// <summary>Keep focusing as the scene changes.</summary>
    public static FocusMode Continuous => new(AfMode.Continuous, null);

    /// <summary>Focus at infinity.</summary>
    public static FocusMode Infinity => new(AfMode.Manual, 0f);

    /// <summary>Focus at a fixed distance. Infinity is <c>double.PositiveInfinity</c>, or use <see cref="Infinity"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="metres"/> is zero or negative.</exception>
    public static FocusMode AtMetres(double metres) => metres switch
    {
        <= 0 => throw new ArgumentOutOfRangeException(nameof(metres), metres, "Focus distance must be positive; use FocusMode.Infinity for infinity."),
        double.PositiveInfinity => Infinity,
        _ => new(AfMode.Manual, (float)(1.0 / metres)),
    };

    /// <summary>Focus at a lens position libcamera's way, in dioptres.</summary>
    public static FocusMode AtDioptres(float dioptres) => new(AfMode.Manual, dioptres);

    /// <inheritdoc/>
    public override string ToString() => Dioptres is { } d ? (d == 0 ? "infinity" : $"{1 / d:0.##} m") : Mode.ToString();
}
