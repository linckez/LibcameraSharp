namespace LibcameraSharp;

/// <summary>
/// Exposure, gain, white balance, focus, zoom and the rest. The sensor applies them a few frames
/// after they are sent, and the call that takes them waits for the first frame taken with them.
/// </summary>
/// <remarks>
/// Only what you set is sent; anything left null stays as the camera has it. To return exposure or
/// gain to automatic, set <see cref="ExposureMode.Auto"/> or <see cref="GainMode.Auto"/>.
/// </remarks>
public sealed record CameraControls
{
    /// <summary>How long each frame is exposed: <see cref="ExposureMode.Auto"/>, or <see cref="ExposureMode.Fixed"/> with a time.</summary>
    public ExposureMode? Exposure { get; init; }

    /// <summary>Analogue sensor gain: <see cref="GainMode.Auto"/>, or <see cref="GainMode.Fixed"/> from 1.0 up.</summary>
    /// <remarks>The maximum depends on the sensor; ask <see cref="CameraDevice.Capabilities"/>.</remarks>
    public GainMode? Gain { get; init; }

    /// <summary>Frames per second, or a range the camera may vary within.</summary>
    /// <remarks>A fixed rate also caps exposure at one frame's duration.</remarks>
    public FrameRate? FrameRate { get; init; }

    /// <summary>An automatic white balance mode, or red and blue gains you fix yourself.</summary>
    public WhiteBalance? WhiteBalance { get; init; }

    /// <summary>
    /// Where to focus, on a camera with a movable lens. Unset, a photo focuses first and video and
    /// frames focus continuously; setting it turns that off.
    /// </summary>
    public FocusMode? Focus { get; init; }

    /// <summary>Digital zoom: the part of the sensor to read out.</summary>
    public RegionOfInterest? Zoom { get; init; }

    /// <summary>Image brightness, -1.0 to 1.0.</summary>
    public float? Brightness { get; init; }

    /// <summary>Image contrast; 1.0 is normal, 0.0 is none.</summary>
    public float? Contrast { get; init; }

    /// <summary>Colour saturation; 1.0 is normal, 0.0 is greyscale.</summary>
    public float? Saturation { get; init; }

    /// <summary>Sharpening; 1.0 is normal, 0.0 is none.</summary>
    public float? Sharpness { get; init; }

    /// <summary>Exposure compensation in stops, while exposure is automatic.</summary>
    public float? ExposureValue { get; init; }

    /// <summary>Which part of the scene automatic exposure meters.</summary>
    public AeMeteringMode? Metering { get; init; }

    /// <summary>
    /// Turns automatic exposure and gain on or off together. Off holds both where automatic exposure left them, so
    /// frames taken after it settles all match. A fixed <see cref="Exposure"/> or <see cref="Gain"/> turns off only its own.
    /// </summary>
    public bool? AutoExposure { get; init; }

    /// <summary>
    /// Turns automatic white balance on or off. Off holds the colour gains where it left them; fixed
    /// <see cref="WhiteBalance"/> gains also turn it off.
    /// </summary>
    public bool? AutoWhiteBalance { get; init; }

    /// <summary>How hard to denoise.</summary>
    public LibcameraSharp.NoiseReductionMode? Denoise { get; init; }

    /// <summary>High dynamic range, on sensors that have it.</summary>
    public HdrMode? Hdr { get; init; }

    /// <summary>
    /// Flicker avoidance: <see cref="FlickerMode.Manual"/> with how fast the lights in the scene pulse, so automatic
    /// exposure avoids dark bands across the picture, or <see cref="FlickerMode.Off"/>.
    /// </summary>
    public FlickerMode? Flicker { get; init; }

    /// <summary>A 3×3 colour correction matrix, replacing the tuning file's.</summary>
    /// <remarks>
    /// Takes effect only with fixed white balance gains (<see cref="LibcameraSharp.WhiteBalance.Manual"/>)
    /// set in the same options, or with <see cref="AutoWhiteBalance"/> off: the camera ignores a matrix that arrives
    /// while white balance is automatic.
    /// </remarks>
    public ColourCorrectionMatrix? ColourCorrectionMatrix { get; init; }

    /// <summary>How far autofocus searches: the whole range, or only near or far.</summary>
    public AfRange? AutofocusRange { get; init; }

    /// <summary>How quickly autofocus moves.</summary>
    public AfSpeed? AutofocusSpeed { get; init; }

    /// <summary>The parts of the scene autofocus looks at, in fractions of the full field like <see cref="Zoom"/>.</summary>
    /// <remarks>
    /// An empty list lets autofocus choose where to measure again; null leaves the windows as they are. Options compare
    /// lists by reference, so the same windows in a new list count as a change: the controls are sent again, and
    /// the camera isn't reconfigured.
    /// </remarks>
    public IReadOnlyList<RegionOfInterest>? AutofocusWindows { get; init; }

    /// <summary>
    /// Throws when a value can't be right on any camera: a frame rate that isn't above 0, or a zoom or focus window
    /// that doesn't lie inside the picture. Every call that takes controls checks this; call it yourself to refuse
    /// such values earlier, for example when they're saved.
    /// </summary>
    /// <remarks>
    /// What only the camera knows, such as its exposure limits, isn't checked here: the camera takes what it can, and
    /// a control it doesn't have is skipped with a warning.
    /// </remarks>
    /// <param name="paramName">The name the exception gives for the controls, or what holds them.</param>
    /// <exception cref="ArgumentOutOfRangeException">A frame rate, zoom or focus window is out of range.</exception>
    public void ThrowIfInvalid(string? paramName = null)
    {
        FrameRate?.ThrowIfInvalid(paramName);
        Zoom?.ThrowIfInvalid(paramName);
        foreach (var window in AutofocusWindows ?? [])
            window.ThrowIfInvalid(paramName);
    }

    /// <summary>
    /// Writes these controls onto a pending set, sending only what was given. Controls the camera doesn't
    /// have are skipped, as a camera without a lens motor has nothing to focus; their names are returned.
    /// </summary>
    internal IReadOnlyList<string> ApplyTo(PendingControls controls)
    {
        List<string> skipped = [];
        void Put<T>(Control<T> key, T value)
        {
            if (controls.Advertises(key))
                controls.Set(key, value);
            else if (!skipped.Contains(key.Name))
                skipped.Add(key.Name);
        }

        // Turning an automatic switch on goes first: it drops earlier manual values, and a manual value in these same
        // options then wins over it, as libcamera's own precedence has it. Turning one off goes in its usual place.
        if (AutoExposure is true)
            Put(Controls.AeEnable, true);
        if (AutoWhiteBalance is true)
            Put(Controls.AwbEnable, true);

        // Automatic goes out as a zero, which ControlPatching turns into the matching mode when the request is built.
        if (Exposure is { } exposure)
            Put(Controls.ExposureTime, exposure.Microseconds);
        if (Gain is { } gain)
            Put(Controls.AnalogueGain, gain.Value);
        if (FrameRate is { } frameRate)
            Put(Controls.FrameDurationLimits, frameRate.ToDurationLimits());

        if (WhiteBalance is { } whiteBalance)
        {
            // Fixed gains apply only with automatic white balance off, so that is sent with them.
            if (whiteBalance is { RedGain: { } red, BlueGain: { } blue })
            {
                Put(Controls.AwbEnable, false);
                Put(Controls.ColourGains, [red, blue]);
            }
            else if (whiteBalance.Mode is { } mode)
            {
                // A mode alone does not undo earlier fixed gains; enabling auto white balance does.
                Put(Controls.AwbEnable, true);
                Put(Controls.AwbMode, mode);
            }
        }

        if (Focus is { } focus)
        {
            Put(Controls.AfMode, focus.Mode);
            if (focus.Dioptres is { } dioptres)
                Put(Controls.LensPosition, dioptres);

            // In auto mode the lens only moves when a scan is triggered; cancelling keeps it where it is.
            if (focus.Mode == AfMode.Auto)
                Put(Controls.AfTrigger, focus.CancelsScan ? AfTrigger.Cancel : AfTrigger.Start);
        }

        if (Brightness is { } brightness)
            Put(Controls.Brightness, brightness);
        if (Contrast is { } contrast)
            Put(Controls.Contrast, contrast);
        if (Saturation is { } saturation)
            Put(Controls.Saturation, saturation);
        if (Sharpness is { } sharpness)
            Put(Controls.Sharpness, sharpness);
        if (ExposureValue is { } exposureValue)
            Put(Controls.ExposureValue, exposureValue);
        if (AutoExposure is false)
            Put(Controls.AeEnable, false);
        if (AutoWhiteBalance is false)
            Put(Controls.AwbEnable, false);
        if (Metering is { } metering)
            Put(Controls.AeMeteringMode, metering);
        if (Denoise is { } denoise)
            Put(Controls.Draft.NoiseReductionMode, denoise);
        if (Hdr is { } hdr)
            Put(Controls.HdrMode, hdr);
        // The period is only used in manual flicker mode, so both are sent.
        if (Flicker is { } flicker)
        {
            if (flicker.Period is { } period)
            {
                Put(Controls.AeFlickerMode, AeFlickerMode.Manual);
                Put(Controls.AeFlickerPeriod, (int)period.TotalMicroseconds);
            }
            else
            {
                Put(Controls.AeFlickerMode, AeFlickerMode.Off);
            }
        }
        if (ColourCorrectionMatrix is { } matrix)
            Put(Controls.ColourCorrectionMatrix, matrix.ToArray());
        if (AutofocusRange is { } range)
            Put(Controls.AfRange, range);
        if (AutofocusSpeed is { } speed)
            Put(Controls.AfSpeed, speed);

        return skipped;
    }
}
