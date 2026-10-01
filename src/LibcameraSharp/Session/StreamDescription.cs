using LibcameraSharp.Advanced;

namespace LibcameraSharp;

/// <summary>
/// One stream in a <see cref="SessionConfiguration"/>: what to ask libcamera for, and after
/// <see cref="CameraSession.Configure(SessionConfiguration)"/>, what it gave back.
/// </summary>
internal sealed class StreamDescription
{
    /// <summary>Creates a stream request; size and format may be left unset to take the defaults.</summary>
    public StreamDescription(Size? size = null, PixelFormat? format = null)
    {
        Size = size;
        Format = format;
    }

    /// <summary>Frame size in pixels. Null until set or configured.</summary>
    public Size? Size { get; set; }

    /// <summary>Pixel format. Null until set or configured.</summary>
    public PixelFormat? Format { get; set; }

    /// <summary>Bytes per row. Filled in by <see cref="CameraSession.Configure(SessionConfiguration)"/>; setting it asks libcamera for a specific stride.</summary>
    public uint? Stride { get; set; }

    /// <summary>Bytes per frame. Filled in by <see cref="CameraSession.Configure(SessionConfiguration)"/>.</summary>
    public uint? FrameSize { get; set; }

    /// <summary>Rounds the size down to even width and height, which every pixel format and the ISP accept.</summary>
    public void Align()
    {
        if (Size is not { } size)
            return;
        Size = new Size(size.Width - size.Width % 2, size.Height - size.Height % 2);
    }

    internal StreamDescription Clone() => new(Size, Format) { Stride = Stride, FrameSize = FrameSize };

    /// <inheritdoc/>
    public override string ToString() => $"{Size?.ToString() ?? "?"}-{Format?.ToString() ?? "?"}" + (Stride is { } s ? $" stride {s}" : "");
}

/// <summary>Sensor mode request: which bit depth and sensor output size to run the sensor at (Raspberry Pi pipelines).</summary>
internal sealed class SensorConfiguration
{
    /// <summary>Sensor output size, or null to let libcamera choose from the main stream's size.</summary>
    public Size? OutputSize { get; set; }

    /// <summary>Raw bit depth, or null to let libcamera choose.</summary>
    public int? BitDepth { get; set; }

    internal SensorConfiguration Clone() => new() { OutputSize = OutputSize, BitDepth = BitDepth };
}
