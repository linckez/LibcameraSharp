namespace LibcameraSharp;

/// <summary>How an autofocus scan ended: whether it found focus, and the frame it ended on.</summary>
public sealed class FocusResult
{
    internal FocusResult(bool isFocused, CaptureMetadata? metadata) => (IsFocused, Metadata) = (isFocused, metadata);

    /// <summary>True when the scan ended focused; false when it failed, never started, or the camera has no autofocus.</summary>
    public bool IsFocused { get; }

    /// <summary>
    /// The frame the scan ended on: its <see cref="CaptureMetadata.LensPosition"/> is where the lens is now. Null when the
    /// camera has no autofocus, so there was no scan.
    /// </summary>
    public CaptureMetadata? Metadata { get; }
}
