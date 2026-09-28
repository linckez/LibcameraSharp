using System.Diagnostics;

using LibcameraSharp.Advanced;
using Stream = LibcameraSharp.Advanced.Stream;   // libcamera's stream, not System.IO's

namespace LibcameraSharp;

/// <summary>
/// The one camera the SDK drives: configured for a use, started, frames handed over, controls applied
/// and waited for, stopped. <see cref="CameraDevice"/> is its public face.
/// </summary>
internal sealed partial class CameraSession : IDisposable
{
    private readonly Lock _lock = new();
    private readonly List<Request> _completed = [];
    private readonly Camera _cameraHandle;
    private ActiveCamera _camera;

    /// <summary>libcamera's id for this camera.</summary>
    internal string CameraId => _cameraHandle.Id;
    private LibcameraSharp.Advanced.CameraConfiguration? _libcameraConfig;
    private BufferAllocation? _allocation;
    private Dictionary<SessionStream, Stream> _streams = [];
    private TaskCompletionSource _frameArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _maxQueueLength;
    private int _waiters;
    private int _stopCount;
    private bool _stopping;
    private bool _restarting;                      // after a camera timeout; waiting captures keep waiting through it
    private int _restartScheduledFor = -1;         // the run a timeout restart was scheduled for, so it happens once
    private readonly object _runGate = new();      // Start, Stop and a timeout restart, one at a time
    private int _configureCount;

    // libcamera numbers requests in the order they are queued, from 0 after every start
    // (pipeline_handler.cpp:401,491), so counting as we queue gives each request its number.
    private long _queuedSinceStart;

    // The number of the first request that carried the latest change to Controls: frames taken from
    // it on have the new values. The PendingControls and version it was taken from tell a change apart.
    private long _controlsFrom;
    private PendingControls? _sentControls;
    private long _sentVersion;

    /// <summary>Opens camera number <paramref name="cameraNum"/> in <see cref="Cameras"/> order and acquires it.</summary>
    /// <param name="manager">The camera manager to open through.</param>
    /// <param name="cameraNum">Index into <see cref="Cameras"/>.</param>
    public CameraSession(CameraManager manager, int cameraNum = 0)
    {
        var cameras = manager.Cameras;
        if (cameraNum >= cameras.Count)
            throw new ArgumentOutOfRangeException(nameof(cameraNum), $"No camera number {cameraNum} found; {cameras.Count} camera(s) present.");
        _cameraHandle = cameras[cameraNum];
        _camera = _cameraHandle.Acquire();

        // The largest raw mode; cameras without a raw stream fall back to the sensor's size property.
        (SensorResolution, SensorFormat) = SelectNativeMode();

        Controls = new PendingControls(_camera.Controls);
        IsOpen = true;

        _camera.RequestCompleted += OnRequestCompleted;
    }

    /// <summary>The cameras libcamera sees, in a stable order.</summary>
    public static IReadOnlyList<CameraInfo> Cameras(CameraManager manager)
    {
        var infos = new List<CameraInfo>();
        var num = 0;
        foreach (var camera in manager.Cameras)
        {
            var properties = camera.Properties;
            infos.Add(new CameraInfo(
                camera.Id,
                properties.TryGet(Properties.Model, out var model) ? model : "",
                properties.TryGet(Properties.Rotation, out var rotation) ? rotation : 0,
                properties.TryGet(Properties.Location, out var location) ? location : null,
                num++));
        }
        return infos;
    }

    /// <summary>True until <see cref="Dispose"/>.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    public bool Started { get; private set; }

    /// <summary>Fixed facts about the camera: model, sensor size, location.</summary>
    public PropertyList CameraProperties => _camera.Properties;

    /// <summary>The controls the camera advertises, with their ranges.</summary>
    public ControlInfoMap CameraControls => _camera.Controls;

    /// <summary>The sensor's full resolution (its largest raw mode).</summary>
    public Size SensorResolution { get; }

    /// <summary>The sensor's native raw format, or null for cameras without a raw stream.</summary>
    public PixelFormat? SensorFormat { get; }

    /// <summary>True for a monochrome sensor.</summary>
    public bool IsMono =>
        _camera.Properties.TryGet(Properties.Draft.ColorFilterArrangement, out var cfa) && cfa == ColorFilterArrangement.MONO;

    /// <summary>
    /// Raised on libcamera's thread for every completed frame, before a capture can take it; recordings
    /// encode here. Keep handlers short and do not dispose the frame. A handler's exception is kept in
    /// <see cref="LastFrameError"/>.
    /// </summary>
    public event Action<CapturedFrame>? FrameCompleted;

    /// <summary>The last exception a <see cref="FrameCompleted"/> handler threw, or null. Kept so a silent failure is still diagnosable.</summary>
    public Exception? LastFrameError { get; private set; }

    /// <summary>Frames discarded because the caller had not taken the previous one. Recordings never drop.</summary>
    /// <summary>How many times the camera has been configured; each one stops the sensor and reallocates buffers.</summary>
    public int ConfigureCount => _configureCount;


    /// <summary>Controls to send with the next requests. <see cref="Start"/> applies them and starts a fresh set.</summary>
    public PendingControls Controls { get; private set; }

    /// <summary>The configuration in effect, with what libcamera chose, or null before <see cref="Configure"/>.</summary>
    public SessionConfiguration? CameraConfiguration { get; private set; }

    /// <summary>The libcamera camera underneath.</summary>
    public ActiveCamera Camera => _camera;

    /// <summary>Starts the camera in the configuration it was given, applying the pending controls with the first frame.</summary>
    /// <exception cref="InvalidOperationException">The camera has not been configured.</exception>
    public void Start()
    {
        lock (_runGate)
            StartLocked();
    }

    private void StartLocked()
    {
        if (CameraConfiguration is null)
            throw new InvalidOperationException("Configure the camera before starting it.");
        if (Started)
            return;

        // The pending controls go in the start list, which a Raspberry Pi applies from the first frame,
        // and on the first request too, since most other pipelines ignore the start list.
        using var initial = new ControlList();
        PendingControls sent;
        lock (_lock)
        {
            sent = Controls;
            sent.CopyTo(initial, _camera.Controls);
            Controls = new PendingControls(_camera.Controls);
            (_queuedSinceStart, _controlsFrom, _sentControls, _sentVersion) = (0, 0, Controls, Controls.Version);
        }
        _camera.Start(initial);
        Started = true;

        var first = true;
        foreach (var request in MakeRequests())
        {
            lock (_lock)
            {
                if (first)
                    sent.CopyTo(request.Controls, _camera.Controls);
                first = false;
                _camera.QueueRequest(request);
                _queuedSinceStart++;
            }
        }
    }

    // Requests a recording still encodes, with how many holders each has left; see Hold and Recycle.
    private readonly Dictionary<Request, int> _holds = [];

    /// <summary>Stops the camera; completed frames not yet taken are discarded.</summary>
    public void Stop()
    {
        lock (_runGate)
            StopLocked();
    }

    private void StopLocked()
    {
        if (!Started)
            return;
        StopCamera();
        lock (_lock)
        {
            _completed.Clear();
            _holds.Clear();                                             // a stopped camera re-queues nothing; the next start makes fresh requests
            SignalFrame();                                              // pending captures wake, see Started == false, and cancel
        }
        _allocation?.DiscardRequests();
    }

    /// <summary>Stops the camera and releases it.</summary>
    public void Dispose()
    {
        if (!IsOpen)
            return;
        Stop();
        _camera.RequestCompleted -= OnRequestCompleted;
        ReleaseConfiguration();
        _camera.Dispose();
        _cameraHandle.Dispose();
        IsOpen = false;
    }

    // Frames in flight complete on libcamera's thread while it stops, so Recycle is shut out for the
    // whole stop. A request re-queued into a stopping camera is refused but keeps its buffers pending,
    // and libcamera's ~Request then signals through a camera that may be gone (request.cpp:139-146).
    private void StopCamera()
    {
        lock (_lock)
        {
            _stopping = true;
            _stopCount++;
        }
        try
        {
            _camera.Stop();
        }
        finally
        {
            lock (_lock)
            {
                Started = false;
                _stopping = false;
            }
        }
    }

    private void EnsureStarted()
    {
        if (!Started && !_restarting)
            throw new InvalidOperationException("The camera is not started.");
    }

    // libcamera cancels every outstanding request when the camera times out (a loose cable, a sensor
    // glitch) and leaves recovery to the application: restart once per run, off libcamera's thread.
    private void ScheduleTimeoutRestart()
    {
        int run;
        lock (_lock)
        {
            if (!Started || _stopping || _restartScheduledFor == _stopCount)
                return;
            _restartScheduledFor = run = _stopCount;
        }
        _ = Task.Run(() => RestartAfterTimeout(run));
    }

    private void RestartAfterTimeout(int run)
    {
        lock (_runGate)
        {
            // Stopped or restarted meanwhile by a call of the SDK's own: nothing to recover.
            lock (_lock)
            {
                if (!Started || run != _stopCount)
                    return;
                _restarting = true;
            }
            try
            {
                Console.Error.WriteLine("LibcameraSharp: the camera timed out and libcamera cancelled every frame; restarting it. Check that the camera's cable is attached securely.");
                StopCamera();
                lock (_lock)
                {
                    _completed.Clear();
                    _holds.Clear();
                }
                _allocation?.DiscardRequests();
                StartLocked();
            }
            finally
            {
                lock (_lock)
                    _restarting = false;
            }
        }
    }
}
