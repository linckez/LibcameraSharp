namespace LibcameraSharp;

/// <summary>
/// Builds the photos, frames, metadata and capabilities a camera hands back, for tests and for a fake
/// camera: a <see cref="CameraDevice"/> made with its protected constructor returns these.
/// </summary>
/// <remarks>
/// <see cref="RawImage"/> isn't covered: a DNG needs the sensor's own layout, which a fake doesn't have.
/// </remarks>
public static class LibcameraSharpModelFactory
{
    /// <summary>Creates a <see cref="LibcameraSharp.CaptureMetadata"/> for tests and fakes.</summary>
    /// <remarks>
    /// A value left null isn't reported, as with a camera that doesn't report it. Each typed value reads back
    /// through the property of the same name. <c>otherControls</c> takes anything else, by libcamera control,
    /// each value of the control's own type (an enum control takes its enum); those win over the typed values
    /// for the same control.
    /// </remarks>
    /// <exception cref="ArgumentException">A value in <paramref name="otherControls"/> isn't of its control's type, or its key is a property.</exception>
    public static CaptureMetadata CaptureMetadata(
        TimeSpan? exposureTime = null,
        float? analogueGain = null,
        float? digitalGain = null,
        float? lensPosition = null,
        float? lux = null,
        TimeSpan? timestamp = null,
        TimeSpan? frameDuration = null,
        Rectangle? scalerCrop = null,
        int? colourTemperature = null,
        (float Red, float Blue)? colourGains = null,
        float[]? colourCorrectionMatrix = null,
        int[]? sensorBlackLevels = null,
        IEnumerable<KeyValuePair<ControlKey, object>>? otherControls = null)
    {
        var values = new List<KeyValuePair<ControlKey, object>>();

        // Each typed value in the unit libcamera reports it in, which is what the matching property reads back.
        if (exposureTime is { } exposure)
            values.Add(new(Controls.ExposureTime, (int)exposure.TotalMicroseconds));
        if (analogueGain is { } analogue)
            values.Add(new(Controls.AnalogueGain, analogue));
        if (digitalGain is { } digital)
            values.Add(new(Controls.DigitalGain, digital));
        if (lensPosition is { } lens)
            values.Add(new(Controls.LensPosition, lens));
        if (lux is { } brightness)
            values.Add(new(Controls.Lux, brightness));
        if (timestamp is { } time)
            values.Add(new(Controls.SensorTimestamp, time.Ticks * 100));
        if (frameDuration is { } duration)
            values.Add(new(Controls.FrameDuration, (long)duration.TotalMicroseconds));
        if (scalerCrop is { } crop)
            values.Add(new(Controls.ScalerCrop, crop));
        if (colourTemperature is { } kelvin)
            values.Add(new(Controls.ColourTemperature, kelvin));
        if (colourGains is { } gains)
            values.Add(new(Controls.ColourGains, new[] { gains.Red, gains.Blue }));
        if (colourCorrectionMatrix is not null)
            values.Add(new(Controls.ColourCorrectionMatrix, colourCorrectionMatrix));
        if (sensorBlackLevels is not null)
            values.Add(new(Controls.SensorBlackLevels, sensorBlackLevels));

        // The rest, checked against each control's type now rather than failing later when read.
        foreach (var (key, value) in otherControls ?? [])
        {
            if (key.IsProperty)
                throw new ArgumentException($"{key.Name} is a camera property, not something a frame reports.", nameof(otherControls));
            if (value?.GetType() != key.ValueType)
                throw new ArgumentException($"{key.Name} holds a {key.ValueType.Name}, not a {value?.GetType().Name ?? "null"}.", nameof(otherControls));
            values.Add(new(key, value));
        }

        return new CaptureMetadata(new Metadata(values));
    }

    /// <summary>Creates a <see cref="LibcameraSharp.Photo"/> for tests and fakes.</summary>
    /// <remarks>
    /// Saved as JPEG or PNG, an <c>XRGB8888</c> or <c>XBGR8888</c> photo needs nothing but Skia, so it saves
    /// on any machine; other formats go through FFmpeg, as a camera's photos do.
    /// </remarks>
    /// <param name="pixels">The rows of each plane, one after another, each <paramref name="stride"/> bytes apart.</param>
    /// <param name="size">The photo's size in pixels.</param>
    /// <param name="format">An RGB or YUV format; <c>PixelFormats.XRGB8888</c> when not given.</param>
    /// <param name="stride">Bytes from one row of the first plane to the next; the narrowest the format allows when not given.</param>
    /// <param name="metadata">What the camera "did"; none when not given.</param>
    /// <param name="options">The options it was "taken" with, which decide how it saves; the defaults when not given.</param>
    /// <param name="cameraModel">The camera model written into the EXIF data.</param>
    /// <exception cref="NotSupportedException">The format is neither RGB nor YUV.</exception>
    /// <exception cref="ArgumentException"><paramref name="pixels"/> is too short for the size, format and stride.</exception>
    public static Photo Photo(
        byte[] pixels,
        Size size,
        PixelFormat? format = null,
        int? stride = null,
        CaptureMetadata? metadata = null,
        PhotoOptions? options = null,
        string cameraModel = "")
    {
        ArgumentNullException.ThrowIfNull(pixels);
        var chosen = format ?? PixelFormats.XRGB8888;
        var planes = Planes(chosen, size);
        var rowBytes = stride ?? planes[0].Stride;

        // All planes sit in one array, each at the first plane's stride scaled as the format scales it.
        var needed = planes.Sum(plane => (long)plane.Stride * rowBytes / planes[0].Stride * plane.Rows);
        if (pixels.Length < needed)
            throw new ArgumentException($"{chosen} at {size} with a stride of {rowBytes} needs {needed} bytes; got {pixels.Length}.", nameof(pixels));

        return new Photo(new FramePixels(pixels, chosen, size, rowBytes, colourSpace: null),
            (metadata ?? CaptureMetadata()).Frame, cameraModel, raw: null, options ?? new PhotoOptions());
    }

    /// <summary>Creates a <see cref="LibcameraSharp.VideoFrame"/> for tests and fakes; disposing it gives nothing back.</summary>
    /// <param name="planes">One array per plane: one for RGB, three for YUV420, two for NV12.</param>
    /// <param name="size">The frame's size in pixels.</param>
    /// <param name="format">An RGB or YUV format; <c>PixelFormats.XRGB8888</c> when not given.</param>
    /// <param name="strides">Bytes per row of each plane; the narrowest the format allows when not given.</param>
    /// <param name="sequence">The frame's sequence number.</param>
    /// <param name="metadata">What the camera "did" for the frame; none when not given.</param>
    /// <exception cref="NotSupportedException">The format is neither RGB nor YUV.</exception>
    /// <exception cref="ArgumentException">The planes or strides don't match the format, or a plane is too short.</exception>
    public static VideoFrame VideoFrame(
        IReadOnlyList<byte[]> planes,
        Size size,
        PixelFormat? format = null,
        IReadOnlyList<int>? strides = null,
        uint sequence = 0,
        CaptureMetadata? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(planes);
        var chosen = format ?? PixelFormats.XRGB8888;
        var layout = Planes(chosen, size);
        if (planes.Count != layout.Length)
            throw new ArgumentException($"{chosen} has {layout.Length} plane(s); got {planes.Count}.", nameof(planes));
        if (strides is not null && strides.Count != layout.Length)
            throw new ArgumentException($"{chosen} has {layout.Length} plane(s); got {strides.Count} strides.", nameof(strides));

        // Every plane holds at least its rows at its stride.
        var rowBytes = new int[layout.Length];
        for (var i = 0; i < layout.Length; i++)
        {
            rowBytes[i] = strides?[i] ?? layout[i].Stride;
            if (planes[i].Length < (long)rowBytes[i] * layout[i].Rows)
                throw new ArgumentException($"Plane {i} of {chosen} at {size} needs {(long)rowBytes[i] * layout[i].Rows} bytes; got {planes[i].Length}.", nameof(planes));
        }

        return new VideoFrame([.. planes], rowBytes, size, chosen, sequence, metadata ?? CaptureMetadata());
    }

    /// <summary>Creates <see cref="LibcameraSharp.CameraCapabilities"/> for tests and fakes.</summary>
    /// <param name="controls">
    /// The controls the camera advertises, in order, each with its range; a null range for a control whose
    /// values aren't numbers, such as <c>Controls.ScalerCrop</c>.
    /// </param>
    /// <param name="isMono">True for a sensor with no colour filter.</param>
    /// <exception cref="ArgumentException">A key is a property, appears twice, or has a minimum above its maximum.</exception>
    public static CameraCapabilities CameraCapabilities(
        IEnumerable<KeyValuePair<ControlKey, (double Min, double Max, double? Default)?>> controls,
        bool isMono = false)
    {
        ArgumentNullException.ThrowIfNull(controls);
        var listed = controls.ToList();

        // Each control once, as a camera advertises it, with a range that makes sense.
        var seen = new HashSet<uint>();
        foreach (var (key, range) in listed)
        {
            if (key.IsProperty)
                throw new ArgumentException($"{key.Name} is a camera property, not a control.", nameof(controls));
            if (!seen.Add(key.Id))
                throw new ArgumentException($"{key.Name} is listed twice.", nameof(controls));
            if (range is { } bounds && bounds.Min > bounds.Max)
                throw new ArgumentException($"{key.Name}'s minimum {bounds.Min} is above its maximum {bounds.Max}.", nameof(controls));
        }

        return new CameraCapabilities(listed, isMono);
    }

    // Each plane's narrowest stride and its rows. Packed RGB is one plane; planar YUV has its chroma at half
    // height, and at half width too when U and V are separate planes.
    private static (int Stride, int Rows)[] Planes(PixelFormat format, Size size)
    {
        var (width, height) = ((int)size.Width, (int)size.Height);
        if (PixelLayout.BytesPerPixel(format) is { } bytesPerPixel)
            return [(width * bytesPerPixel, height)];
        if (format == PixelFormats.YUV420 || format == PixelFormats.YVU420)
            return [(width, height), (width / 2, height / 2), (width / 2, height / 2)];
        if (format == PixelFormats.NV12 || format == PixelFormats.NV21)
            return [(width, height), (width, height / 2)];
        throw new NotSupportedException($"{format} isn't an RGB or YUV format.");
    }
}
