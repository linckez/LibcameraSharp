namespace LibcameraSharp;

/// <summary>
/// What the camera actually did for a frame: the exposure and gain it chose, where the lens ended
/// up, how bright the scene was.
/// </summary>
/// <remarks>
/// A value is null when the camera does not report it; cameras without automatic exposure report
/// little more than a timestamp.
/// </remarks>
public sealed class CaptureMetadata
{
    private readonly Metadata _metadata;

    internal CaptureMetadata(Metadata metadata) => _metadata = metadata;

    // The frame's metadata as the writers read it.
    internal Metadata Frame => _metadata;

    /// <summary>How long the frame was exposed for.</summary>
    /// <remarks>libcamera <c>Controls.ExposureTime</c>, in microseconds.</remarks>
    public TimeSpan? ExposureTime =>
        _metadata.TryGet(Controls.ExposureTime, out var microseconds) ? TimeSpan.FromMicroseconds(microseconds) : null;

    /// <summary>The analogue gain the sensor applied.</summary>
    public float? AnalogueGain => _metadata.TryGet(Controls.AnalogueGain, out var gain) ? gain : null;

    /// <summary>The digital gain applied after the sensor.</summary>
    public float? DigitalGain => _metadata.TryGet(Controls.DigitalGain, out var gain) ? gain : null;

    /// <summary>Where the lens ended up, in dioptres; 0 is infinity.</summary>
    public float? LensPosition => _metadata.TryGet(Controls.LensPosition, out var position) ? position : null;

    /// <summary>Estimated scene brightness in lux.</summary>
    public float? Lux => _metadata.TryGet(Controls.Lux, out var lux) ? lux : null;

    /// <summary>When the frame was captured: time since the system booted, including suspend (<c>CLOCK_BOOTTIME</c>).</summary>
    /// <remarks>libcamera <c>Controls.SensorTimestamp</c>, in nanoseconds — the one thing every pipeline reports.</remarks>
    public TimeSpan? Timestamp =>
        _metadata.TryGet(Controls.SensorTimestamp, out var nanoseconds) ? TimeSpan.FromTicks(nanoseconds / 100) : null;

    /// <summary>How long the frame took, end to end.</summary>
    public TimeSpan? FrameDuration =>
        _metadata.TryGet(Controls.FrameDuration, out var microseconds) ? TimeSpan.FromMicroseconds(microseconds) : null;

    /// <summary>The part of the sensor this frame was read from.</summary>
    public Rectangle? ScalerCrop => _metadata.TryGet(Controls.ScalerCrop, out var crop) ? crop : null;

    /// <summary>The colour temperature the camera estimated, in kelvin.</summary>
    public int? ColourTemperature =>
        _metadata.TryGet(Controls.ColourTemperature, out var kelvin) ? kelvin : null;

    /// <summary>The red and blue gains white balance applied.</summary>
    /// <remarks>A DNG's <c>AsShotNeutral</c> is <c>[1/red, 1, 1/blue]</c> of these.</remarks>
    public (float Red, float Blue)? ColourGains =>
        _metadata.TryGet(Controls.ColourGains, out var gains) && gains.Length >= 2 ? (gains[0], gains[1]) : null;

    /// <summary>The colour correction matrix the camera used; set it on <see cref="CameraControls.ColourCorrectionMatrix"/> to keep it.</summary>
    public ColourCorrectionMatrix? ColourCorrectionMatrix =>
        _metadata.TryGet(Controls.ColourCorrectionMatrix, out var matrix) ? LibcameraSharp.ColourCorrectionMatrix.FromArray(matrix) : null;

    /// <summary>
    /// The sensor's black level per Bayer position, on a 16-bit scale whatever the sensor's real
    /// depth is — so a 10-bit sensor reporting 4096 here means 64 in its own units.
    /// </summary>
    public IReadOnlyList<int>? SensorBlackLevels =>
        _metadata.TryGet(Controls.SensorBlackLevels, out var levels) ? Array.AsReadOnly((int[])levels.Clone()) : null;



    /// <summary>Everything libcamera reported, for anything not surfaced above.</summary>
    /// <remarks>
    /// Array values are copies: the metadata is kept for saving the photo later, so a change to what you read can't
    /// reach the file.
    /// </remarks>
    public IReadOnlyCollection<KeyValuePair<ControlKey, object>> All =>
        [.. _metadata.Select(entry => entry.Value is Array array ? KeyValuePair.Create(entry.Key, array.Clone()) : entry)];

    /// <inheritdoc/>
    public override string ToString() => _metadata.ToString();
}
