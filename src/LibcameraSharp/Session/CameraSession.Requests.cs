using LibcameraSharp.Advanced;
using Stream = LibcameraSharp.Advanced.Stream;   // libcamera's stream, not System.IO's

namespace LibcameraSharp;

internal sealed partial class CameraSession
{
    /// <summary>The next completed frame, yours until you dispose it.</summary>
    public async Task<CapturedFrame> CaptureRequestAsync(CancellationToken cancellationToken = default)
    {
        EnsureStarted();
        var request = await NextCompletedAsync(cancellationToken).ConfigureAwait(false);
        _allocation!.Acquire(request);
        return new CapturedFrame(this, request, CameraConfiguration!, _streams, _stopCount, _allocation);
    }

    /// <summary>The next frame's metadata.</summary>
    public async Task<Metadata> CaptureMetadataAsync(CancellationToken cancellationToken = default)
    {
        using var request = await CaptureRequestAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return request.Metadata;
    }

    /// <summary>Skips the next <paramref name="numFrames"/> frames.</summary>
    public async Task DropFramesAsync(int numFrames, CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < numFrames; i++)
        {
            using var request = await CaptureRequestAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    // Runs on libcamera's thread for every completed request: drop failures, keep at most _maxQueueLength ready, recycle the rest.
    private void OnRequestCompleted(Request request)
    {
        // The channel is for ActiveCamera users; keep it from growing behind our back.
        while (_camera.CompletedRequests.TryRead(out _))
        {
        }
        if (request.Status == RequestStatus.Cancelled)
        {
            ScheduleTimeoutRestart();                                   // ignored while we stop the camera ourselves
            return;
        }
        if (request.Status != RequestStatus.Complete || _allocation is not { } allocation || !allocation.Owns(request))
            return;
        var stream = _streams[SessionStream.Capture];
        if (request.Buffer(stream).Metadata.Status != FrameStatus.Success)
        {
            Recycle(request, _stopCount);
            return;
        }

        // Watchers (a recording's encoder) see every completed frame first, on libcamera's thread.
        if (FrameCompleted is { } watchers)
        {
            var frame = new CapturedFrame(this, request, CameraConfiguration!, _streams, _stopCount, allocation);
            try
            {
                watchers(frame);
            }
            catch (Exception exception)
            {
                // A throwing watcher must not stall the camera, but silence makes a broken encoder look
                // like a hang — so the failure is reported and kept.
                LastFrameError = exception;
            }
        }

        List<Request> overflow;
        lock (_lock)
        {
            _completed.Add(request);
            SignalFrame();
            // A waiting capture takes the oldest and trims afterwards; with nobody waiting, keep at most _max_queue_len.
            overflow = _waiters > 0 ? [] : TrimCompleted();
        }
        foreach (var old in overflow)
            Recycle(old, _stopCount);
    }

    // Wakes every waiter and arms the next handoff. Call under _lock.
    //
    // Every waiter awaits the *same* task, so completing it has to be paired with replacing it here —
    // if a waiter installed its own task instead, a second waiter would overwrite the first one's and
    // the first would never be woken by anything. That is a silent permanent hang, and it was one:
    // two concurrent captures stranded the older of the two (ConcurrentCaptureTests).
    private void SignalFrame()
    {
        _frameArrived.TrySetResult();
        _frameArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // Drops all but the newest _maxQueueLength completed requests; call under _lock, recycle the result outside it.
    private List<Request> TrimCompleted()
    {
        List<Request> overflow = [];
        while (_completed.Count > _maxQueueLength)
        {
            overflow.Add(_completed[0]);
            _completed.RemoveAt(0);
        }
        return overflow;
    }

    private async ValueTask<Request> NextCompletedAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
            _waiters++;
        try
        {
            while (true)
            {
                Task wait;
                List<Request> overflow;
                Request? taken = null;
                lock (_lock)
                {
                    // Checked under the lock Stop signals under: a waiter arriving after that signal would
                    // otherwise take the next, never-completed task and hang.
                    if (!Started && !_restarting)
                        throw new OperationCanceledException("The camera was stopped while a capture was pending.");
                    if (_completed.Count > 0)
                    {
                        taken = _completed[0];
                        _completed.RemoveAt(0);
                        _waiters--;
                        overflow = _waiters > 0 ? [] : TrimCompleted();
                    }
                    else
                    {
                        overflow = [];
                    }
                    wait = _frameArrived.Task;                          // shared by every waiter; only SignalFrame replaces it
                }
                foreach (var old in overflow)
                    Recycle(old, _stopCount);
                if (taken is not null)
                    return taken;
                await wait.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (!Started && !_restarting)
                    throw new OperationCanceledException("The camera was stopped while a capture was pending.");
            }
        }
        catch
        {
            lock (_lock)
                _waiters--;
            throw;
        }
    }

    // A recording holds a frame while it encodes it on its own thread, so the request goes back to the
    // camera only once every holder is done with it.
    internal void Hold(Request request)
    {
        lock (_lock)
            _holds[request] = _holds.GetValueOrDefault(request) + 1;
    }

    // Puts a request back into circulation if the camera is still running the same session; otherwise it just stays parked.
    // A held request waits for its last holder: each call before that only counts one holder done.
    internal void Recycle(Request request, int stopCount)
    {
        lock (_lock)
        {
            if (_holds.TryGetValue(request, out var holders))
            {
                if (holders > 1)
                    _holds[request] = holders - 1;
                else
                    _holds.Remove(request);
                return;
            }
            // A run that timed out gets fresh requests when it restarts; this one stays parked.
            if (!Started || _stopping || stopCount != _stopCount || _restartScheduledFor == _stopCount)
                return;
            request.Reuse();

            // Read the version first: a change made while applying is then caught by the next request, never missed.
            var version = Controls.Version;
            Controls.CopyTo(request.Controls, _camera.Controls);    // what was set since the last request, once

            // Queued under the lock, so the count matches the order libcamera numbers them in. Only a
            // request that queued has sent its controls: one that failed leaves them for the next.
            _camera.QueueRequest(request);
            Controls.Forget();
            if (!ReferenceEquals(Controls, _sentControls) || version != _sentVersion)
                (_controlsFrom, _sentControls, _sentVersion) = (_queuedSinceStart, Controls, version);
            _queuedSinceStart++;
        }
    }

    // A CapturedFrame is done with its request: re-queue it if possible, then let its allocation go.
    internal void Release(Request request, int stopCount, BufferAllocation allocation)
    {
        if (ReferenceEquals(allocation, _allocation))
            Recycle(request, stopCount);
        allocation.Release(request);
    }

    private List<Request> MakeRequests()
    {
        // As many requests as the stream with the fewest buffers.
        var allocation = _allocation!;
        var count = _streams.Values.Min(s => allocation.Buffers(s).Count);
        var requests = new List<Request>(count);
        for (var i = 0; i < count; i++)
        {
            var request = allocation.Track(_camera.CreateRequest((ulong)i));
            foreach (var stream in _streams.Values)
                request.AddBuffer(stream, allocation.Buffers(stream)[i]);
            requests.Add(request);
        }
        return requests;
    }
}
