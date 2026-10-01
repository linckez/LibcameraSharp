using System.Threading.Channels;
using LibcameraSharp.Advanced;
using Stream = LibcameraSharp.Advanced.Stream;   // libcamera's stream, not System.IO's

namespace LibcameraSharp;

/// <summary>
/// The one camera the SDK drives: configured for a use, started, frames handed over, controls applied and
/// waited for, stopped. <see cref="CameraDevice"/> is its public face.
/// </summary>
/// <remarks>
/// <para>
/// One loop, on its own thread, owns the camera and everything the session knows about it. It handles, in
/// order: frames libcamera completed, frames their holders handed back, and calls from any other thread.
/// libcamera wants configure, start and stop synchronised by the caller (<c>camera.cpp</c>), and a single
/// owner does that with no locks.
/// </para>
/// <para>
/// Members whose names end in <c>Async</c> may be called from any thread: they post work to the loop and
/// return its result. Every other member runs on the loop, and only there.
/// </para>
/// </remarks>
internal sealed partial class CameraSession : IAsyncDisposable
{
    // Unbounded, so nobody posting ever waits: libcamera's thread writes into it even while the loop is
    // inside a stop. Continuations run elsewhere, never inline on a writer's thread.
    private readonly Channel<Message> _inbox = Channel.CreateUnbounded<Message>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
    private readonly Task _loop;

    private abstract record Message;

    // libcamera finished a request; posted from its thread.
    private sealed record Completed(Request Request) : Message;

    // A frame's holder is done with it; posted from whichever thread disposed it.
    private sealed record Released(Request Request, BufferAllocation Allocation) : Message;

    // A call from another thread, and how to fail it when the loop can no longer run it.
    private sealed record Work(Action Run, Action<Exception> Fail) : Message;

    private readonly Camera _cameraHandle;
    private readonly ActiveCamera _camera;
    private CameraFacts _facts;
    private LibcameraSharp.Advanced.CameraConfiguration? _libcameraConfig;
    private BufferAllocation? _allocation;
    private readonly List<BufferAllocation> _retired = [];      // replaced or closed, but a frame from it is still held
    private Dictionary<SessionStream, Stream> _streams = [];
    private int _configureCount;

    // Bumped by every stop: frames from an earlier run are told apart by it.
    private int _run;

    private bool _closing;
    private bool _closed;

    /// <summary>Opens camera number <paramref name="cameraNum"/> in <see cref="Cameras"/> order, acquires it and starts the loop.</summary>
    /// <param name="manager">The camera manager to open through.</param>
    /// <param name="cameraNum">Index into <see cref="Cameras"/>.</param>
    public CameraSession(CameraManager manager, int cameraNum = 0)
    {
        var cameras = manager.Cameras;
        if (cameraNum >= cameras.Count)
            throw new ArgumentOutOfRangeException(nameof(cameraNum), $"No camera number {cameraNum} found; {cameras.Count} camera(s) present.");
        _cameraHandle = cameras[cameraNum];
        _camera = _cameraHandle.Acquire();

        // Anything failing from here lets the camera go again, so a failed open never leaves it held, as
        // CameraDevice.Open lets the camera manager go when opening fails.
        try
        {
            // The largest raw mode; cameras without a raw stream fall back to the sensor's size property.
            (SensorResolution, SensorFormat) = SelectNativeMode();

            Controls = new PendingControls(_camera.Controls);
            _applied = new PendingControls(_camera.Controls);
            _facts = ReadFacts();
        }
        catch
        {
            _camera.Dispose();
            _cameraHandle.Dispose();
            throw;
        }

        // libcamera's thread only posts; everything else happens on the loop.
        _camera.RequestCompleted += (_, completed) => _inbox.Writer.TryWrite(new Completed(completed.Request));
        _loop = Task.Factory.StartNew(RunLoop, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    /// <summary>The cameras libcamera sees, in a stable order.</summary>
    public static IReadOnlyList<CameraInfo> Cameras(CameraManager manager)
    {
        var infos = new List<CameraInfo>();
        foreach (var camera in manager.Cameras)
        {
            var properties = camera.Properties;
            infos.Add(new CameraInfo(
                camera.Id,
                properties.TryGet(Properties.Model, out var model) ? model : null,
                properties.TryGet(Properties.Rotation, out var rotation) ? rotation : null,
                properties.TryGet(Properties.Location, out var location) ? location : null));
        }
        return infos;
    }

    /// <summary>libcamera's id for this camera.</summary>
    internal string CameraId => _cameraHandle.Id;

    /// <summary>
    /// What callers on any thread may know about the camera: its capabilities in the current configuration, its
    /// model and its sensor area. The loop publishes a new one after every configure.
    /// </summary>
    public CameraFacts Facts => Volatile.Read(ref _facts);

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    public bool Started { get; private set; }

    /// <summary>The sensor's full resolution (its largest raw mode).</summary>
    public Size SensorResolution { get; }

    /// <summary>The sensor's native raw format, or null for cameras without a raw stream.</summary>
    public PixelFormat? SensorFormat { get; }

    /// <summary>True for a monochrome sensor.</summary>
    public bool IsMono =>
        _camera.Properties.TryGet(Properties.Draft.ColorFilterArrangement, out var cfa) && cfa == ColorFilterArrangement.MONO;

    /// <summary>How many times the camera has been configured; each one stops the sensor and reallocates buffers.</summary>
    public int ConfigureCount => _configureCount;

    /// <summary>The configuration in effect, with what libcamera chose, or null before <see cref="Configure"/>.</summary>
    public SessionConfiguration? CameraConfiguration { get; private set; }

    /// <summary>Runs <paramref name="onLoop"/> on the loop and hands back what it returns, or what it throws.</summary>
    /// <exception cref="ObjectDisposedException">The camera is closed.</exception>
    internal Task<T> CallAsync<T>(Func<T> onLoop)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(new Work(
            () =>
            {
                try
                {
                    result.TrySetResult(onLoop());
                }
                catch (Exception exception)
                {
                    result.TrySetException(exception);
                }
            },
            exception => result.TrySetException(exception)));
        return result.Task;
    }

    /// <summary>Runs <paramref name="onLoop"/> on the loop and completes when it has.</summary>
    /// <exception cref="ObjectDisposedException">The camera is closed.</exception>
    internal Task CallAsync(Action onLoop) => CallAsync(() =>
    {
        onLoop();
        return true;
    });

    // Posts to the loop; a closed camera refuses.
    private void Post(Message message)
    {
        if (!_inbox.Writer.TryWrite(message))
            throw CameraClosed();
    }

    // The loop: one message at a time, each to its end. A failure it can't pin on a call ends it.
    private void RunLoop()
    {
        var inbox = _inbox.Reader;
        try
        {
            while (inbox.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (inbox.TryRead(out var message))
                    Handle(message);
            }
        }
        catch (Exception exception)
        {
            Fault(exception);
        }
    }

    private void Handle(Message message)
    {
        switch (message)
        {
            case Completed completed:
                OnCompleted(completed.Request);
                break;
            case Released released:
                OnReleased(released.Request, released.Allocation);
                break;
            case Work work when _closed:
                // The camera is gone; only frames coming back still matter.
                work.Fail(CameraClosed());
                break;
            case Work work:
                work.Run();
                break;
        }
    }

    // Everyone waiting gets the failure, the camera is stopped and let go, and the loop takes no more work: a
    // caller is never left waiting on a loop that has died.
    // The camera is let go before anyone hears, so a caller that disposes next finds it closed.
    private void Fault(Exception exception)
    {
        FailWaits(exception);

        // Each encoder drains before any buffer is freed, and before its recording hears through its output: failing an
        // output closes it, which must not happen while the encoder's thread still writes to it. Each recording flushes
        // and closes its own when it's stopped. Blocking the loop is fine here: it takes no more work, and an encoder's
        // thread never waits on it.
        foreach (var feed in _feeds)
        {
            try
            {
                feed.Encoder.Drain();
                feed.Encoder.Output?.Fail(exception);
            }
            catch (Exception)
            {
                // One recording failing to wind down mustn't keep the others, or the callers, waiting.
            }
        }
        _feeds.Clear();

        try
        {
            CloseCamera();
        }
        catch (Exception)
        {
            // The camera is already in trouble; the failure that matters is the one callers got.
        }
        _inbox.Writer.TryComplete();
        while (_inbox.Reader.TryRead(out var message))
        {
            if (message is Work work)
                work.Fail(new ObjectDisposedException(nameof(CameraDevice), $"The camera failed: {exception.Message}"));
        }
    }

    /// <summary>Starts the camera in the configuration it was given, queuing every request nobody holds.</summary>
    /// <exception cref="InvalidOperationException">The camera has not been configured.</exception>
    public void Start()
    {
        ThrowIfClosing();
        if (CameraConfiguration is null || _allocation is null)
            throw new InvalidOperationException("Configure the camera before starting it.");
        if (Started)
            return;

        // The start list goes to a Raspberry Pi, which applies it from the first frame; the first request gets the
        // same, since most other pipelines ignore the start list.
        var initial = TakeStartControls();
        using (var list = new ControlList())
        {
            initial.CopyTo(list, _camera.Controls);
            _camera.Start(list);
        }
        Started = true;

        // A frame someone still holds stays theirs: only free requests go back to the camera.
        var first = true;
        foreach (var (request, slot) in _allocation.Slots)
        {
            if (slot.Leases > 0 || slot.Queued)
                continue;
            request.Reuse();
            if (first)
                initial.CopyTo(request.Controls, _camera.Controls);
            first = false;
            Enqueue(request, slot);
        }
    }

    /// <summary>
    /// Stops the camera. Frames not yet taken are handed back; captures still waiting fail with
    /// <see cref="OperationCanceledException"/>, unless the stop is a timeout restart, which they wait through.
    /// </summary>
    public void Stop(bool restarting = false)
    {
        if (!Started)
            return;

        // libcamera cancels every queued request before stop returns; each slot counts the completion still on its way,
        // so it's ignored when it arrives. The run changes too, so frames taken before the stop are told apart.
        _run++;
        _camera.Stop();
        Started = false;
        foreach (var slot in _allocation!.Slots.Values)
        {
            if (slot.Queued)
                slot.StaleCompletions++;
            slot.Queued = false;
        }

        ReleaseReady();
        if (!restarting)
            FailWaits(new OperationCanceledException("The camera was stopped while a capture was pending."));
    }

    // libcamera cancels every outstanding request when the camera times out (a loose cable, a sensor glitch) and
    // leaves recovery to the application: restart, with the controls it had. The rest of the run's cancellations arrive
    // afterwards, from an old run, and are ignored. A camera that keeps timing out keeps restarting.
    private void Restart(string why)
    {
        Console.Error.WriteLine($"LibcameraSharp: {why}; restarting the camera. Check that the camera's cable is attached securely.");
        Stop(restarting: true);
        Start();
    }

    /// <summary>The exception every call on a closed camera gets.</summary>
    internal static ObjectDisposedException CameraClosed() => new(nameof(CameraDevice), "The camera is closed.");

    // Work that would touch the camera is refused once a close has begun.
    private void ThrowIfClosing()
    {
        if (_closing)
            throw CameraClosed();
    }

    /// <summary>
    /// Closes the camera: every call still waiting fails, recordings get no more frames, the camera is stopped and
    /// let go. Each recording still flushes and closes its file when it's stopped. The loop runs on until the last
    /// frame someone holds comes back, then ends.
    /// </summary>
    public ValueTask DisposeAsync() => CloseAsync(Task.CompletedTask);

    /// <summary>
    /// Closes the camera as <see cref="DisposeAsync"/> does, but frees nothing until <paramref name="jobsEnded"/>
    /// completes: a job still copying a frame it holds, such as a photo, never reads a buffer the close has freed.
    /// </summary>
    internal async ValueTask CloseAsync(Task jobsEnded)
    {
        Task<Encoder[]> closing;
        try
        {
            closing = CallAsync(BeginClose);
        }
        catch (ObjectDisposedException)
        {
            // The loop has ended, or is ending after a failure that already let the camera go.
            await _loop.ConfigureAwait(false);
            return;
        }

        Encoder[] encoders;
        try
        {
            encoders = await closing.ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Closed already, by an earlier dispose or a failure.
            return;
        }

        // Encoders finish their queued frames off the loop, and the running job its work (every wait it could be in has
        // just failed), so nothing reads a buffer the close is about to free.
        foreach (var encoder in encoders)
            await Task.Run(encoder.Drain).ConfigureAwait(false);
        await jobsEnded.ConfigureAwait(false);

        try
        {
            await CallAsync(FinishClose).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The loop failed in between, and its failure let the camera go.
        }
    }

    // First half of a close, on the loop: no more calls, no more waits, no more recordings.
    private Encoder[] BeginClose()
    {
        ThrowIfClosing();
        _closing = true;
        FailWaits(new ObjectDisposedException(nameof(CameraDevice), "The camera was closed while a capture was pending."));

        // A stop that fails leaves the release to FinishClose, which lets the camera go regardless.
        try
        {
            Stop();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"LibcameraSharp: the camera failed to stop while closing: {exception.Message}");
        }
        var encoders = _feeds.Select(feed => feed.Encoder).ToArray();
        _feeds.Clear();
        return encoders;
    }

    // Second half: let the camera go. What someone still holds is freed when it comes back.
    private void FinishClose()
    {
        CloseCamera();
        _closed = true;
        EndIfIdle();
    }

    private void CloseCamera()
    {
        // Letting the camera go stops it too, and ignores a stop that fails, so one here doesn't keep the camera held.
        try
        {
            Stop();
        }
        catch (Exception exception)
        {
            Started = false;
            Console.Error.WriteLine($"LibcameraSharp: the camera failed to stop while closing: {exception.Message}");
        }
        ReleaseReady();
        _allocation?.Close();
        foreach (var retired in _retired)
            retired.Close();
        ReleaseConfiguration();
        _camera.Dispose();
        _cameraHandle.Dispose();
    }

    // Once closed and every frame is back, the loop takes nothing more and ends.
    private void EndIfIdle()
    {
        _retired.RemoveAll(allocation => allocation.Disposed);
        if (_closed && _retired.Count == 0)
            _inbox.Writer.TryComplete();
    }

    private CameraFacts ReadFacts()
    {
        var properties = _camera.Properties;
        var model = properties.TryGet(Properties.Model, out var name) && name.Length > 0 ? name : CameraId;
        Rectangle? activeArea = properties.TryGet(Properties.PixelArrayActiveAreas, out var areas) && areas.Length > 0 ? areas[0] : null;
        return new CameraFacts(CameraCapabilities.Snapshot(_camera.Controls, IsMono), model, activeArea, Describe(properties));
    }

    // A managed copy of what libcamera reports: every property and control limit read out now, since the native lists
    // change with the next configure.
    private CameraDescription Describe(PropertyList properties)
    {
        var reported = new List<KeyValuePair<ControlKey, object>>();
        foreach (var (id, _) in properties)
        {
            var key = properties.KeyFor(id);
            reported.Add(new(key, properties.GetValue(key)));
        }
        var limits = _camera.Controls.Select(info => new ControlLimits(info.Key, info.MinValue, info.MaxValue, info.DefaultValue)).ToList();

        if (CameraConfiguration is not { } config)
            return new CameraDescription(CameraId, reported, limits);
        return new CameraDescription(CameraId, reported, limits, Configured(config.Capture), Configured(config.Preview), Configured(config.Raw),
            config.ColourSpace, config.Transform, config.BufferCount);
    }

    private static ConfiguredStream? Configured(StreamDescription? stream) =>
        stream is { Size: { } size, Format: { } format }
            ? new ConfiguredStream(size, format, stream.Stride is 0 ? null : stream.Stride, stream.FrameSize!.Value)   // libcamera's 0 stride: no rows
            : null;
}

/// <summary>What callers on any thread may know about a camera, published by its session's loop.</summary>
/// <param name="Capabilities">The controls it advertises in its current configuration, with their ranges.</param>
/// <param name="Model">The model libcamera reports, or the camera's id when it reports none.</param>
/// <param name="ActiveArea">The sensor's active pixel area, when the camera reports one.</param>
/// <param name="Description">What libcamera reports about the camera, for <see cref="CameraDevice.Advanced"/>.</param>
internal sealed record CameraFacts(CameraCapabilities Capabilities, string Model, Rectangle? ActiveArea, CameraDescription Description);
