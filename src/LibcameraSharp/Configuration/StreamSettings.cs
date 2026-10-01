namespace LibcameraSharp;

/// <summary>
/// How the camera is set up: what sizes and formats it produces, which way up, and how much of the
/// sensor it reads. Changing any of these stops the sensor and reallocates buffers, which is why
/// they are kept apart from <see cref="CameraControls"/>.
/// </summary>
public sealed record StreamSettings
{
    /// <summary>
    /// Size of the picture you capture. Null uses the call's default: the sensor's full resolution for a
    /// photo, 1280×720 for a recording, 640×480 for frames.
    /// </summary>
    public Size? CaptureSize { get; init; }

    /// <summary>Pixel format of the capture stream. Null takes the configuration's default.</summary>
    public PixelFormat? CaptureFormat { get; init; }

    /// <summary>Size of a second, smaller stream, for previewing or feeding an algorithm while the first is captured or recorded.</summary>
    public Size? PreviewSize { get; init; }

    /// <summary>Pixel format of the preview stream. Defaults to YUV420, which is what encoders and most algorithms want.</summary>
    public PixelFormat? PreviewFormat { get; init; }

    /// <summary>Also produce the sensor's own unprocessed data, reachable as a photo's raw image.</summary>
    public bool CaptureRaw { get; init; }

    /// <summary>Which way up the picture comes out.</summary>
    /// <remarks>
    /// The camera may combine this with how the module is mounted, so the orientation you get can
    /// differ from the one you asked for.
    /// </remarks>
    public Orientation Orientation { get; init; } = Orientation.Rotate0;

    /// <summary>How many frames the camera keeps in flight. More costs memory and latency; fewer risks dropping.</summary>
    public int? BufferCount { get; init; }

    /// <summary>The colour space to produce. Null picks by use: sYCC for stills, Rec.709 for video.</summary>
    public ColorSpace? ColorSpace { get; init; }

    /// <summary>Which sensor readout to use. Null scores the available modes against the capture size.</summary>
    public SensorMode? SensorMode { get; init; }

    /// <summary>Writes these settings onto a session configuration, leaving unset ones alone.</summary>
    /// <exception cref="ArgumentException">A preview format was given with no preview size to apply it to.</exception>
    internal void ApplyTo(SessionConfiguration config)
    {
        if (CaptureSize is { } captureSize)
            config.Capture.Size = captureSize;
        if (CaptureFormat is { } captureFormat)
            config.Capture.Format = captureFormat;

        if (PreviewSize is { } previewSize)
            config.Preview = new StreamDescription(previewSize, PreviewFormat ?? PixelFormats.YUV420);
        else if (PreviewFormat is not null)
            throw new ArgumentException(
                $"{nameof(PreviewFormat)} was set without {nameof(PreviewSize)}, so there is no preview stream to give a format to.",
                nameof(PreviewFormat));

        // The default configurations add a raw stream on every Pi camera; keep it only when asked for.
        config.EnableRaw(CaptureRaw);

        config.Transform = Orientation;

        if (BufferCount is { } bufferCount)
            config.BufferCount = bufferCount;
        if (ColorSpace is { } colourSpace)
            config.ColorSpace = colourSpace;
        if (SensorMode is { } mode)
            config.Sensor = new ConfiguredSensor { OutputSize = mode.Size, BitDepth = mode.BitDepth };
    }
}
