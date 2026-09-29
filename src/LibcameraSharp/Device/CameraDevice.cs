namespace LibcameraSharp;

/// <summary>
/// A camera. Every call that takes options puts the camera into the shape they describe first, so
/// there is no separate configure step: the same options twice cost nothing, different ones cost the
/// change.
/// </summary>
/// <remarks>
/// Photos, recordings and frames are in the other parts of this class. For tests and machines without a
/// camera, derive from it and override what your code calls; <see cref="LibcameraSharpModelFactory"/> builds
/// the photos, frames and metadata to return.
/// </remarks>
public partial class CameraDevice : IDisposable
{
    private readonly CameraSession? _session;
    private bool _disposed;

    // Held by each call that sets the camera up (a photo, a recording's start, a frame loop's start),
    // so two callers never reconfigure it under each other.
    private readonly HashSet<string> _warnedSkipped = [];
    private readonly SemaphoreSlim _calls = new(1, 1);

    // What the camera was last set up with, so repeating the same options changes nothing.
    private StreamSettings? _appliedStreams;
    private CameraUse? _appliedUse;
    private int _appliedAt;

    // The controls of the last call that took options; a direct SetControls forgets them, since it
    // may have changed what the camera is doing.
    private CameraControls? _appliedControls;

    private CameraDevice(CameraSession session) => _session = session;

    /// <summary>
    /// Creates a camera with no hardware behind it, for mocking: derive from it, or use a mocking library,
    /// and override the members your code calls. Those you don't override throw
    /// <see cref="NotSupportedException"/>.
    /// </summary>
    protected CameraDevice()
    {
    }

    /// <summary>The cameras attached to this machine, in a stable order.</summary>
    public static IReadOnlyList<CameraInfo> Enumerate()
    {
        var manager = SharedCameraManager.Acquire();
        try
        {
            return CameraSession.Cameras(manager);
        }
        finally
        {
            SharedCameraManager.Release();
        }
    }

    /// <summary>Opens a camera and takes exclusive ownership of it.</summary>
    /// <param name="id">The camera's <see cref="CameraInfo.Id"/>, or null for the first one.</param>
    /// <exception cref="CameraBusyException">Another process or object already holds the camera.</exception>
    /// <exception cref="ArgumentException">No camera has that id, or no camera is attached at all.</exception>
    public static CameraDevice Open(string? id = null)
    {
        var manager = SharedCameraManager.Acquire();
        try
        {
            if (manager.Cameras.Count == 0)
                throw new ArgumentException("No camera found. On a Raspberry Pi, check that `rpicam-hello --list-cameras` lists one.", nameof(id));

            var index = 0;
            if (id is not null)
            {
                var cameras = CameraSession.Cameras(manager);
                index = cameras.ToList().FindIndex(camera => camera.Id == id);
                if (index < 0)
                    throw new ArgumentException($"No camera with id '{id}'. Found: {string.Join(", ", cameras.Select(c => c.Id))}.", nameof(id));
            }
            return new CameraDevice(new CameraSession(manager, index));
        }
        catch
        {
            SharedCameraManager.Release();
            throw;
        }
    }

    /// <summary>The libcamera camera underneath, for anything this class does not cover.</summary>
    public virtual ActiveCamera Advanced => Session.Camera;

    /// <summary>What this camera supports, in the configuration it is currently in.</summary>
    public virtual CameraCapabilities Capabilities => new(Session.CameraControls, Session.IsMono);

    /// <summary>Frames that arrived while your code was still busy with the previous one, and were dropped.</summary>
    public virtual long FramesDropped => Interlocked.Read(ref _framesDropped);

    private long _framesDropped;

    // The running camera the photo, frame and video parts work through; a camera made for mocking has none.
    internal CameraSession Session => _session ?? throw new NotSupportedException(
        "This CameraDevice was made with the protected constructor for mocking, so it has no camera. Override the member your code calls.");

    // True for a camera with a lens that autofocus can move.
    internal bool CanFocus => Session.CameraControls.Contains(Controls.AfMode);

    /// <summary>
    /// Changes controls on a camera that is already running, such as from a slider over a live view.
    /// Returns at once; the next call that takes options waits for them to land.
    /// </summary>
    public virtual void SetControls(CameraControls controls)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SetControlsCore(controls);
    }

    // Sends controls to the running camera. Setting up for a call uses this rather than SetControls, so an
    // override of the public method never runs in the middle of a photo or a recording's start.
    private void SetControlsCore(CameraControls controls)
    {
        _appliedControls = null;

        var pending = new PendingControls(Session.CameraControls);
        WarnSkipped(controls.ApplyTo(pending));
        Session.SetControls(pending);
        ApplyGeometry(controls);
    }

    /// <summary>Skips the next <paramref name="count"/> frames.</summary>
    public virtual Task DropFramesAsync(int count, CancellationToken cancellationToken = default) =>
        Session.DropFramesAsync(count, cancellationToken);

    /// <summary>What the camera did for the next frame: exposure, gain, focus and the rest.</summary>
    public virtual async Task<CaptureMetadata> CaptureMetadataAsync(CancellationToken cancellationToken = default) =>
        new(await Session.CaptureMetadataAsync(cancellationToken).ConfigureAwait(false));

    /// <summary>Every readout the sensor supports.</summary>
    /// <remarks>
    /// Finding them reconfigures the camera once per mode and forgets controls set with
    /// <see cref="SetControls"/>, so ask before setting up. The answer is cached.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The camera is running.</exception>
    public virtual Task<IReadOnlyList<SensorMode>> ProbeSensorModesAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        // Probing reconfigures the camera once per mode, so it waits its turn like any call that sets it up.
        _calls.Wait(cancellationToken);
        try
        {
            return Task.FromResult(Session.SensorModes);
        }
        finally
        {
            _calls.Release();
        }
    }

    // Puts the camera into the shape the options describe, and does nothing when it is already in it.
    internal void ApplyOptions(StreamSettings streams, CameraControls controls, CameraUse use)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        controls = WithDefaultFocus(controls, use, CanFocus);

        // Compare with what was asked for, not the live configuration: libcamera adjusts what it cannot
        // honour, so the two never match and comparing them would reconfigure on every call.
        if (_appliedUse == use && _appliedStreams == streams && Session.CameraConfiguration is not null && Session.ConfigureCount == _appliedAt)
        {
            // The same controls as the last call are already in effect; anything else goes out once.
            if (controls == _appliedControls)
                return;
            SetControlsCore(controls);
            _appliedControls = controls;
            return;
        }

        // A running recording keeps the size and format it started with, so the camera can't be
        // reconfigured under it.
        if (Session.Encoders.Count > 0)
            throw new InvalidOperationException("Stop the recording before using the camera with different options.");

        var wanted = Session.CreateConfiguration(use);
        streams.ApplyTo(wanted);
        WarnSkipped(controls.ApplyTo(wanted.Controls));

        if (Session.Started)
            Session.Stop();
        Session.Configure(wanted);
        (_appliedStreams, _appliedUse, _appliedAt, _appliedControls) = (streams, use, Session.ConfigureCount, controls);
        ApplyGeometry(controls);
    }

    // Zoom and autofocus windows are fractions of the sensor area, so they need the camera's own geometry;
    // a camera that can't crop, or has no autofocus windows, skips them.
    private void ApplyGeometry(CameraControls controls)
    {
        var canCrop = Session.CameraControls.Contains(Controls.ScalerCrop);
        if (controls.Zoom is { } zoom)
        {
            if (canCrop)
                Session.SetZoom(zoom);
            else
                WarnSkipped([Controls.ScalerCrop.Name]);
        }
        if (controls.AutofocusWindows is { Count: > 0 } windows)
        {
            if (canCrop && Session.CameraControls.Contains(Controls.AfWindows))
                Session.SetAutofocusWindows(windows);
            else
                WarnSkipped([Controls.AfWindows.Name]);
        }
    }

    // Options can name settings a camera doesn't have; they are skipped, with one warning per setting per camera.
    private void WarnSkipped(IReadOnlyList<string> names)
    {
        foreach (var name in names)
        {
            bool first;
            lock (_warnedSkipped)
                first = _warnedSkipped.Add(name);
            if (first)
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

    /// <summary>Releases the camera.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the camera; a derived class releases its own resources here too.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed || !disposing)
            return;
        _disposed = true;

        // A camera made for mocking never took a camera, so it has nothing to give back.
        if (_session is null)
            return;
        _session.Dispose();
        SharedCameraManager.Release();
    }
}

/// <summary>What the camera is being set up for; each starts from its own defaults.</summary>
internal enum CameraUse
{
    Photo,
    Video,
    Frames,
}
