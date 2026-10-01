namespace LibcameraSharp;

/// <summary>
/// Options for reading frames: how the camera should be set up, and what it should be doing, while
/// you take them.
/// </summary>
/// <remarks>
/// Set <see cref="StreamSettings.PreviewSize"/> to read a second stream, no larger than the capture stream, alongside it.
/// </remarks>
public sealed record FrameOptions
{
    /// <summary>How the camera is set up. Changing these reconfigures the camera.</summary>
    public StreamSettings Streams { get; init; } = new();

    /// <summary>Exposure, gain, focus and the rest. They take a few frames to land, and the frames you get start once they have.</summary>
    public CameraControls Controls { get; init; } = new();
}
