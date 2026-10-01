using LibcameraSharp.Advanced;

namespace LibcameraSharp;

internal sealed partial class CameraSession
{
    // The newest completed frame nobody has taken, kept for the next call. Only with more than one buffer: with
    // one, keeping it would stall the camera.
    private Request? _ready;
    private int _maxQueueLength;

    // Calls waiting for a frame, oldest first; each takes the first frame it accepts.
    private readonly List<FrameWait> _waits = [];

    // A call waiting for a frame: which frames it accepts (every one, when null), whether it sets an idle camera up
    // for frames first, and where the frame goes.
    private sealed class FrameWait(Func<Request, bool>? accepts, bool startIfIdle, CancellationToken cancellationToken)
    {
        public TaskCompletionSource<CapturedFrame> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<Request, bool>? Accepts { get; } = accepts;

        public bool StartIfIdle { get; } = startIfIdle;

        public CancellationToken CancellationToken { get; } = cancellationToken;

        public CancellationTokenRegistration Registration { get; set; }
    }

    /// <summary>
    /// The next completed frame, yours until you dispose it; with <paramref name="target"/>, the next one taken with
    /// the controls it names.
    /// </summary>
    /// <exception cref="InvalidOperationException">The camera is not started.</exception>
    /// <exception cref="OperationCanceledException">The camera was stopped while waiting, or <paramref name="cancellationToken"/> fired.</exception>
    public Task<CapturedFrame> NextFrameAsync(ControlsTarget? target = null, CancellationToken cancellationToken = default) =>
        WaitForFrameAsync(target is { } landed ? request => ControlsLanded(request, landed) : null, cancellationToken);

    /// <summary>
    /// The frame an autofocus scan ends on, among frames taken with the controls <paramref name="target"/> names:
    /// the scan has up to <paramref name="framesToStart"/> frames to start, then as long as it takes to end. A scan
    /// that fails still ends the wait.
    /// </summary>
    public Task<CapturedFrame> WaitForFocusScanAsync(ControlsTarget target, int framesToStart, CancellationToken cancellationToken)
    {
        var seen = 0;
        var started = false;
        return WaitForFrameAsync(request =>
        {
            if (!ControlsLanded(request, target))
                return false;
            var index = seen++;
            if (request.Metadata.TryGet(LibcameraSharp.Controls.AfState, out var state) && state == AfState.Scanning)
            {
                started = true;
                return false;
            }
            return started || index >= framesToStart;
        }, cancellationToken);
    }

    /// <summary>
    /// The next frame <paramref name="accepts"/> takes; it runs on the loop, once per completed frame, so it may keep
    /// state. With <paramref name="startIfIdle"/>, a camera that isn't running is set up for frames with the default
    /// options first, in the same step, so nothing can stop it in between.
    /// </summary>
    internal Task<CapturedFrame> WaitForFrameAsync(Func<Request, bool>? accepts, CancellationToken cancellationToken, bool startIfIdle = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var wait = new FrameWait(accepts, startIfIdle, cancellationToken);

        // Cancelling is a message too, so only the loop ever takes a wait out of its list, and a frame handed to a
        // call that has just given up goes back rather than being lost. Registered before the wait is posted, so the
        // loop never sees it half made; a cancel that arrives first is caught when the wait is added.
        wait.Registration = cancellationToken.Register(() => _inbox.Writer.TryWrite(new Work(() => CancelWait(wait), _ => { })));
        try
        {
            Post(new Work(() => AddWait(wait), exception =>
            {
                wait.Registration.Dispose();
                wait.Result.TrySetException(exception);
            }));
        }
        catch
        {
            wait.Registration.Dispose();
            throw;
        }
        return wait.Result.Task;
    }

    /// <summary>The next frame's metadata; a camera that isn't running is set up for frames first.</summary>
    public async Task<Metadata> CaptureMetadataAsync(CancellationToken cancellationToken = default)
    {
        using var frame = await WaitForFrameAsync(null, cancellationToken, startIfIdle: true).ConfigureAwait(false);
        return frame.Metadata;
    }

    /// <summary>Skips the next <paramref name="numFrames"/> frames; a camera that isn't running is set up for frames first.</summary>
    public async Task DropFramesAsync(int numFrames, CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < numFrames; i++)
        {
            using var frame = await WaitForFrameAsync(null, cancellationToken, startIfIdle: true).ConfigureAwait(false);
        }
    }

    /// <summary>A holder is done with a frame; the loop queues its buffer again once every holder is. Safe from any thread.</summary>
    /// <remarks>After the loop has ended there is nothing left to give back to, so this does nothing.</remarks>
    internal void Release(Request request, BufferAllocation allocation) =>
        _inbox.Writer.TryWrite(new Released(request, allocation));

    // A wait that can't join the list ends here, with its reason: it is in no list a close or a fault could fail.
    private void AddWait(FrameWait wait)
    {
        try
        {
            ThrowIfClosing();
            wait.CancellationToken.ThrowIfCancellationRequested();
            if (!Started)
            {
                if (!wait.StartIfIdle)
                    throw new InvalidOperationException("The camera is not started.");

                // A camera already configured starts as it is: a recording set up for that configuration may be about to
                // attach, and must find the camera still in it. Only one never configured gets the frame defaults.
                if (CameraConfiguration is null)
                    SetUp(new StreamSettings(), new CameraControls(), CameraUse.Frames, reader: null);
                else
                    Start();
            }

            // The frame already waiting, if this call takes it; otherwise the call waits for the next.
            if (_ready is { } ready && (wait.Accepts?.Invoke(ready) ?? true))
            {
                _ready = null;
                Hand(wait, ready, alreadyHeld: true);
                return;
            }
            _waits.Add(wait);
        }
        catch (Exception exception)
        {
            wait.Registration.Dispose();
            if (exception is OperationCanceledException)
                wait.Result.TrySetCanceled(wait.CancellationToken);
            else
                wait.Result.TrySetException(exception);
        }
    }

    private void CancelWait(FrameWait wait)
    {
        if (_waits.Remove(wait))
            wait.Result.TrySetCanceled(wait.CancellationToken);
    }

    // Every waiting call fails with the same reason, as when the camera stops.
    private void FailWaits(Exception reason)
    {
        foreach (var wait in _waits)
        {
            wait.Registration.Dispose();
            wait.Result.TrySetException(reason);
        }
        _waits.Clear();
    }

    // libcamera finished a request. Only a request of the current allocation that it has now counts: our own stop's
    // cancellations, and completions already in flight when it stopped, arrive after that stop and are ignored.
    private void OnCompleted(Request request)
    {
        // Core keeps a channel of completions for ActiveCamera users; nobody reads it here, so it's kept empty.
        while (_camera.CompletedRequests.TryRead(out _))
        {
        }

        if (_allocation?.SlotOf(request) is not { } slot)
            return;
        if (slot.StaleCompletions > 0)
        {
            slot.StaleCompletions--;
            return;
        }
        if (!slot.Queued)
            return;
        slot.Queued = false;

        // A close has begun: the camera is on its way out, and nothing is restarted or handed over.
        if (_closing)
            return;

        if (request.Status == RequestStatus.Cancelled)
        {
            Restart("the camera timed out and libcamera cancelled every frame");
            return;
        }

        // A frame the sensor didn't fill goes straight back.
        if (request.Status != RequestStatus.Complete || request.Buffer(_streams[SessionStream.Capture]).Metadata.Status != FrameStatus.Success)
        {
            Requeue(request, slot);
            return;
        }
        Distribute(request, slot);
    }

    // Every recording gets every frame (from the one its controls landed on); then the oldest waiting call that
    // accepts it takes it for itself. A frame nobody took is kept for the next call, or goes straight back.
    private void Distribute(Request request, BufferAllocation.Slot slot)
    {
        FeedEncoders(request, slot);

        var taker = _waits.FirstOrDefault(wait => wait.Accepts?.Invoke(request) ?? true);
        if (taker is not null)
        {
            _waits.Remove(taker);
            Hand(taker, request, alreadyHeld: false);
        }
        else if (_maxQueueLength > 0)
        {
            ReleaseReady();
            slot.Leases++;
            _ready = request;
        }

        if (slot.Leases == 0)
            Requeue(request, slot);
    }

    // Gives a frame to a waiting call. Only the loop ever completes a wait it holds (a cancel reaches it as a message
    // and takes the wait out of the list first), so the hand-over always succeeds.
    private void Hand(FrameWait wait, Request request, bool alreadyHeld)
    {
        if (!alreadyHeld)
            _allocation!.SlotOf(request)!.Leases++;
        wait.Registration.Dispose();
        wait.Result.SetResult(NewFrame(request));
    }

    // A holder's own handle on a frame of the current allocation; the caller has counted its lease.
    private CapturedFrame NewFrame(Request request) =>
        new(this, request, CameraConfiguration!, _streams, _run, _allocation!);

    // The kept frame goes back: a newer one replaced it, or the camera stopped.
    private void ReleaseReady()
    {
        if (_ready is not { } ready)
            return;
        _ready = null;
        OnReleased(ready, _allocation!);
    }

    // A holder gave a frame back. At zero holders the request goes to the camera again, when the camera runs and
    // it belongs to the current allocation, whatever the run; an older allocation is freed with its last frame.
    private void OnReleased(Request request, BufferAllocation allocation)
    {
        if (allocation.Disposed || allocation.SlotOf(request) is not { } slot)
            return;
        slot.Leases--;
        if (slot.Leases > 0)
            return;

        if (!ReferenceEquals(allocation, _allocation))
        {
            allocation.FreeIfUnused();
            EndIfIdle();
            return;
        }
        if (!slot.Queued)
            Requeue(request, slot);
    }

    // Queues a request again with whatever controls changed meanwhile. A stopped camera leaves it parked: the next
    // start queues it. A request libcamera refuses has no call to fail, so the run restarts, once, as after a timeout.
    private void Requeue(Request request, BufferAllocation.Slot slot)
    {
        if (!Started)
            return;
        request.Reuse();
        try
        {
            Enqueue(request, slot);
        }
        catch (LibcameraException exception)
        {
            Restart($"libcamera refused a frame's request ({exception.Message})");
        }
    }
}
