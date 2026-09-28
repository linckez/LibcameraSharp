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

    /// <summary>
    /// Rounds the size down so every plane's row is a multiple of the ISP's preferred alignment
    /// (32 bytes, or 64 for YUV420; 2 pixels when <paramref name="optimal"/> is false).
    /// </summary>
    public void Align(bool optimal = true)
    {
        if (Size is not { } size)
            return;
        var align = 2u;
        if (optimal)
        {
            align = 32;
            if (Format == PixelFormats.YUV420 || Format == PixelFormats.YVU420)
                align = 64;     // the UV planes have half this alignment
            else if (Format == PixelFormats.XBGR8888 || Format == PixelFormats.XRGB8888)
                align = 16;     // 4 bytes per pixel gives an automatic extra factor of 2
        }
        Size = new Size(size.Width - size.Width % align, size.Height - size.Height % 2);
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
