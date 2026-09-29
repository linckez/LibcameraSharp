namespace LibcameraSharp;

// Everything here runs on the session's loop, except the members ending in Async and SetControls(CameraControls),
// which post to it.
internal sealed partial class CameraSession
{
    // What the camera was last set up with, so repeating the same options changes nothing.
    private StreamSettings? _appliedStreams;
    private CameraUse? _appliedUse;
    private int _appliedAt;

    // The controls of the last setup; a direct SetControls forgets them, since it may have changed what the
    // camera is doing.
    private CameraControls? _appliedControls;

    // Frame loops reading now, with the streams each asked for. A second loop asking for different ones would
    // reconfigure the camera under the first, which would reconfigure it back, forever; so it is refused.
    private readonly Dictionary<object, StreamSettings> _readers = [];

    // Settings named in options that this camera doesn't have; each is warned about once.
    private readonly HashSet<string> _warnedSkipped = [];

    /// <summary>
    /// Puts the camera into the shape the options describe, starts it, and says which frames will carry the
    /// controls. The same options twice change nothing.
    /// </summary>
    /// <remarks><c>reader</c> is a frame loop's identity, so a second loop with different options can be refused; null for a one-off call.</remarks>
    /// <exception cref="InvalidOperationException">A recording, or another frame loop, is using the camera with different options.</exception>
    public Task<SetUpResult> SetUpAsync(StreamSettings streams, CameraControls controls, CameraUse use, object? reader = null) =>
        CallAsync(() => SetUp(streams, controls, use, reader));

    /// <summary>A frame loop has ended; its options no longer hold the camera.</summary>
    public void ForgetReader(object reader) =>
        _inbox.Writer.TryWrite(new Work(() => _readers.Remove(reader), _ => { }));

    /// <summary>
    /// Changes controls on a camera that is already running, such as from a slider over a live view. Returns at
    /// once; they go out with the next requests.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The camera is closed.</exception>
    public void SetControls(CameraControls controls) => Post(new Work(() =>
    {
        // Nobody waits on this call, so a failure is reported rather than thrown into the loop.
        try
        {
            ThrowIfClosing();
            _appliedControls = null;
            var pending = new PendingControls(_camera.Controls);
            WarnSkipped(controls.ApplyTo(pending));
            SetControls(pending);
            ApplyGeometry(controls);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"LibcameraSharp: the controls could not be applied: {exception.Message}");
        }
    }, _ => { }));

    /// <summary>True for a camera with a lens that autofocus can move.</summary>
    public bool CanFocus => Facts.Capabilities.Supports(LibcameraSharp.Controls.AfMode);

    private SetUpResult SetUp(StreamSettings streams, CameraControls controls, CameraUse use, object? reader)
    {
        ThrowIfClosing();
        if (reader is not null)
        {
            if (_readers.Any(other => other.Key != reader && other.Value != streams))
                throw new InvalidOperationException("Another frame loop is reading this camera with different options; read with the same options, or end the other loop first.");
            _readers[reader] = streams;
        }

        ApplyOptions(streams, controls, use);
        if (!Started)
            Start();
        return new SetUpResult(TakeControlsTarget(), CameraConfiguration!, NominalFrameRate());
    }

    // Puts the camera into the shape the options describe, and does nothing when it is already in it.
    private void ApplyOptions(StreamSettings streams, CameraControls controls, CameraUse use)
    {
        controls = WithDefaultFocus(controls, use, CanFocus);

        // Compare with what was asked for, not the live configuration: libcamera adjusts what it cannot honour, so
        // the two never match and comparing them would reconfigure on every call.
        if (_appliedUse == use && _appliedStreams == streams && CameraConfiguration is not null && _configureCount == _appliedAt)
        {
            // The same controls as the last call are already in effect; anything else goes out once.
            if (controls == _appliedControls)
                return;
            var pending = new PendingControls(_camera.Controls);
            WarnSkipped(controls.ApplyTo(pending));
            SetControls(pending);
            ApplyGeometry(controls);
            _appliedControls = controls;
            return;
        }

        // A running recording keeps the size and format it started with, so the camera can't be reconfigured under it.
        if (_feeds.Count > 0)
            throw new InvalidOperationException("Stop the recording before using the camera with different options.");

        var wanted = CreateConfiguration(use);
        streams.ApplyTo(wanted);
        WarnSkipped(controls.ApplyTo(wanted.Controls));

        Stop();
        Configure(wanted);
        (_appliedStreams, _appliedUse, _appliedAt, _appliedControls) = (streams, use, _configureCount, controls);
        ApplyGeometry(controls);
    }

    // Zoom and autofocus windows are fractions of the sensor area, so they need the camera's own geometry; a camera
    // that can't crop, or has no autofocus windows, skips them.
    private void ApplyGeometry(CameraControls controls)
    {
        var canCrop = _camera.Controls.Contains(LibcameraSharp.Controls.ScalerCrop);
        if (controls.Zoom is { } zoom)
        {
            if (canCrop)
                SetZoom(zoom);
            else
                WarnSkipped([LibcameraSharp.Controls.ScalerCrop.Name]);
        }
        if (controls.AutofocusWindows is { Count: > 0 } windows)
        {
            if (canCrop && _camera.Controls.Contains(LibcameraSharp.Controls.AfWindows))
                SetAutofocusWindows(windows);
            else
                WarnSkipped([LibcameraSharp.Controls.AfWindows.Name]);
        }
    }

    // Options can name settings a camera doesn't have; they are skipped, with one warning per setting per camera.
    private void WarnSkipped(IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            if (_warnedSkipped.Add(name))
                Console.Error.WriteLine($"LibcameraSharp: this camera has no {name} control, so that setting is skipped.");
        }
    }

    // A camera that can focus does, unless the options say how: before each photo, continuously otherwise.
    internal static CameraControls WithDefaultFocus(CameraControls controls, CameraUse use, bool canFocus)
    {
        if (controls.Focus is not null || !canFocus)
            return controls;
        return controls with { Focus = use == CameraUse.Photo ? FocusMode.Auto : FocusMode.Continuous };
    }

    // A nominal frame rate, for encoders that must state one: the camera's fastest, at most 30 fps.
    internal double NominalFrameRate()
    {
        if (_camera.Controls.TryGet(LibcameraSharp.Controls.FrameDurationLimits) is not { } limits)
            return 30;
        return 1_000_000.0 / Math.Max(limits.Min<long>(), 33333);
    }
}

/// <summary>What a setup gives the call that asked for it.</summary>
/// <param name="Target">Which frames carry the call's controls.</param>
/// <param name="Configuration">The configuration now in effect, with what libcamera chose.</param>
/// <param name="FrameRate">A nominal frame rate for an encoder that must state one.</param>
internal readonly record struct SetUpResult(ControlsTarget Target, SessionConfiguration Configuration, double FrameRate);

/// <summary>What the camera is being set up for; each starts from its own defaults.</summary>
internal enum CameraUse
{
    Photo,
    Video,
    Frames,
}
