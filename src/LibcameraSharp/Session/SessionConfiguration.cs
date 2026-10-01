using LibcameraSharp.Advanced;

namespace LibcameraSharp;

/// <summary>
/// What the session asks the camera for: a <see cref="Capture"/> stream, optional
/// <see cref="Preview"/> and <see cref="Raw"/> streams, buffer count, controls to apply and the
/// sensor mode. Get one from <see cref="CameraSession.CreatePreviewConfiguration"/>,
/// <see cref="CameraSession.CreateStillConfiguration"/> or <see cref="CameraSession.CreateVideoConfiguration"/>,
/// adjust it, and pass it to <see cref="CameraSession.Configure(SessionConfiguration)"/>.
/// </summary>
/// <remarks>Not libcamera's <see cref="LibcameraSharp.Advanced.CameraConfiguration"/>; <c>Configure</c> builds that from this.</remarks>
internal sealed class SessionConfiguration
{
    /// <summary>Buffers per stream; each use's factory sets it.</summary>
    public int BufferCount { get; set; }

    /// <summary>Image orientation to request.</summary>
    public Orientation Transform { get; set; } = Orientation.Rotate0;

    /// <summary>Colour space for the main stream, or null to let libcamera choose, as video does.</summary>
    public ColorSpace? ColourSpace { get; set; }

    /// <summary>Controls applied when the camera starts with this configuration.</summary>
    public PendingControls Controls { get; set; } = new();

    /// <summary>The main stream. Always present.</summary>
    public StreamDescription Capture { get; set; } = new();

    /// <summary>A smaller YUV420 companion stream, or null.</summary>
    public StreamDescription? Preview { get; set; }

    /// <summary>A raw Bayer stream, or null. See <see cref="EnableRaw"/>. Ignored for cameras without a Bayer sensor.</summary>
    public StreamDescription? Raw { get; set; }

    /// <summary>Sensor mode hints for Raspberry Pi pipelines.</summary>
    public SensorConfiguration Sensor { get; set; } = new();

    /// <summary>Adds (or removes) a raw stream; its size and format are filled in from the sensor at configure time.</summary>
    public void EnableRaw(bool on = true) => Raw = on ? new StreamDescription() : null;

    /// <summary>The configuration of <paramref name="stream"/>, or null when that stream isn't part of the configuration.</summary>
    public StreamDescription? this[SessionStream stream] => stream switch
    {
        SessionStream.Capture => Capture,
        SessionStream.Preview => Preview,
        _ => Raw,
    };

    internal SessionConfiguration Clone() => new()
    {
        BufferCount = BufferCount, Transform = Transform, ColourSpace = ColourSpace,
        Controls = Controls.Clone(), Capture = Capture.Clone(), Preview = Preview?.Clone(), Raw = Raw?.Clone(),
        Sensor = Sensor.Clone(),
    };

    /// <inheritdoc/>
    public override string ToString() =>
        $"capture {Capture}" + (Preview is null ? "" : $", preview {Preview}") + (Raw is null ? "" : $", raw {Raw}") + $", {BufferCount} buffers";
}
