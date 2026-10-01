namespace LibcameraSharp;

/// <summary>Severity threshold for libcamera's own log output.</summary>
public enum LibcameraLogLevel
{
    /// <summary>Everything, including per-frame pipeline chatter.</summary>
    Debug,
    /// <summary>libcamera's default: startup and configuration messages.</summary>
    Info,
    /// <summary>Warnings and worse.</summary>
    Warning,
    /// <summary>Errors and fatal only — sensible for a service.</summary>
    Error,
    /// <summary>Fatal only.</summary>
    Fatal,
}
