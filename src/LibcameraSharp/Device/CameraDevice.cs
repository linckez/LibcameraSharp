namespace LibcameraSharp;

/// <summary>
/// A camera. Every call that takes options puts the camera into the shape they describe first, so there is no
/// separate configure step: the same options twice don't reconfigure the camera, different ones do. Photos on a
/// camera with autofocus are the exception; see <see cref="CapturePhotoAsync(PhotoOptions, CancellationToken)"/>.
/// </summary>
/// <remarks>
/// Photos, recordings and frames are in the other parts of this class. Dispose it with <c>await using</c>; it has no
/// synchronous dispose, since closing waits for the camera to stop. For tests and machines without a camera, derive
/// from it and override what your code calls; <see cref="LibcameraSharpModelFactory"/> builds the photos, frames and
/// metadata to return.
/// </remarks>
public partial class CameraDevice : IAsyncDisposable
{
    private readonly CameraSession? _session;

    // 1 once disposal has begun; set atomically, so two disposals release the camera once.
    private int _disposed;

    // Calls that set the camera up run one at a time; a camera made for mocking has none.
    private readonly JobQueue? _jobs;

    private CameraDevice(CameraSession session)
    {
        _session = session;
        _jobs = new JobQueue();
    }

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

    /// <summary>
    /// What libcamera reports about this camera, for anything this class does not cover: its properties, the limits
    /// of every control it advertises, and the configuration in effect. Read-only; taken again after every reconfigure.
    /// </summary>
    public virtual CameraDescription Advanced
    {
        get
        {
            ThrowIfDisposed();
            return Session.Facts.Description;
        }
    }

    /// <summary>What this camera supports, in the configuration it is currently in.</summary>
    public virtual CameraCapabilities Capabilities
    {
        get
        {
            ThrowIfDisposed();
            return Session.Facts.Capabilities;
        }
    }

    /// <summary>Frames that arrived while your code was still busy with the previous one, and were dropped.</summary>
    public virtual long FramesDropped
    {
        get
        {
            ThrowIfDisposed();
            return Interlocked.Read(ref _framesDropped);
        }
    }

    private long _framesDropped;

    // The running camera the photo, frame and video parts work through; a camera made for mocking has none.
    internal CameraSession Session => _session ?? throw MadeForMocking();

    private static NotSupportedException MadeForMocking() => new(
        "This CameraDevice was made with the protected constructor for mocking, so it has no camera. Override the member your code calls.");

    /// <summary>
    /// Changes controls on a camera that is already running, such as from a slider over a live view.
    /// Returns at once; they go out with the next requests. A later call that takes options sends its own controls.
    /// </summary>
    public virtual void SetControls(CameraControls controls)
    {
        ArgumentNullException.ThrowIfNull(controls);
        ThrowIfDisposed();
        Session.SetControls(controls);
    }

    /// <summary>
    /// Skips the next <paramref name="count"/> frames. A camera that isn't running is started first: set up for frames
    /// with the default options if nothing has set it up yet, else as it was last set up.
    /// </summary>
    /// <exception cref="OperationCanceledException">Another call stopped or reconfigured the camera meanwhile, or <paramref name="cancellationToken"/> fired.</exception>
    public virtual Task DropFramesAsync(int count, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return Session.DropFramesAsync(count, cancellationToken);
    }

    /// <summary>
    /// What the camera did for the next frame: exposure, gain, focus and the rest. A camera that isn't running is
    /// started first, as for <see cref="DropFramesAsync"/>.
    /// </summary>
    /// <exception cref="OperationCanceledException">Another call stopped or reconfigured the camera meanwhile, or <paramref name="cancellationToken"/> fired.</exception>
    public virtual async Task<CaptureMetadata> CaptureMetadataAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return new(await Session.CaptureMetadataAsync(cancellationToken).ConfigureAwait(false));
    }

    // Every public member refuses a disposed camera the same way.
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>Every readout the sensor supports.</summary>
    /// <remarks>
    /// Finding them reconfigures the camera once per mode, so the first call stops a running camera and forgets
    /// controls set with <see cref="SetControls"/>; a frame loop that was running sets itself up again afterwards.
    /// The answer is cached.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A recording is running.</exception>
    public virtual Task<IReadOnlyList<SensorMode>> ProbeSensorModesAsync(CancellationToken cancellationToken = default)
    {
        // Probing reconfigures the camera once per mode, so it waits its turn like any call that sets it up.
        return RunExclusiveAsync(() => Session.ProbeSensorModesAsync(), cancellationToken);
    }

    // Runs a call that sets the camera up after the ones before it have finished.
    private Task<T> RunExclusiveAsync<T>(Func<Task<T>> job, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        return (_jobs ?? throw MadeForMocking()).RunAsync(job, cancellationToken);
    }

    /// <summary>Closes the camera: calls still waiting end, and the camera is let go.</summary>
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    /// <summary>Closes the camera; a derived class releases its own resources here too.</summary>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        // Only the first call closes, however many threads dispose at once. A camera made for mocking never took a
        // camera, so it has nothing to give back.
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0 || _session is null)
            return;

        // No new setups. The close fails whatever is waiting on the camera, so a running job ends and the queue
        // drains; the camera is freed only after that, and the camera manager let go even if the close failed.
        var jobsEnded = _jobs!.DisposeAsync().AsTask();
        try
        {
            await _session.CloseAsync(jobsEnded).ConfigureAwait(false);
        }
        finally
        {
            await jobsEnded.ConfigureAwait(false);
            SharedCameraManager.Release();
        }
    }
}
