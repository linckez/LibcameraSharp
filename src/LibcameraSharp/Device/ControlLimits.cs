namespace LibcameraSharp;

/// <summary>The limits libcamera advertises for one control, as it stores them.</summary>
public sealed record ControlLimits
{
    internal ControlLimits(ControlKey control, object min, object max, object? @default) =>
        (Control, Min, Max, Default) = (control, min, max, @default);

    /// <summary>The control.</summary>
    public ControlKey Control { get; }

    /// <summary>Its smallest value.</summary>
    public object Min { get; }

    /// <summary>Its largest value.</summary>
    public object Max { get; }

    /// <summary>The value libcamera uses when it isn't set, or null when it has none.</summary>
    public object? Default { get; }
}
