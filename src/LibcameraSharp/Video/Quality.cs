namespace LibcameraSharp;

/// <summary>
/// How good the recording should look; each codec turns this into its own numbers, such as a bitrate
/// for H.264 or a quality level for MJPEG.
/// </summary>
public enum Quality
{
    /// <summary>Smallest files, visible artefacts.</summary>
    VeryLow = 0,
    /// <summary>Small files.</summary>
    Low = 1,
    /// <summary>The default.</summary>
    Medium = 2,
    /// <summary>Larger files, fewer artefacts.</summary>
    High = 3,
    /// <summary>Largest files.</summary>
    VeryHigh = 4,
}
