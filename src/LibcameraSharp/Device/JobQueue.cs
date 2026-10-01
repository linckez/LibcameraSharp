using System.Threading.Channels;

namespace LibcameraSharp;

/// <summary>
/// Calls that set the camera up (a photo, a recording's start, a frame loop's setup, a sensor-mode probe) run one at
/// a time, in the order they came, each to its end: no other setup lands between a photo's focus scan and its capture.
/// </summary>
internal sealed class JobQueue : IAsyncDisposable
{
    private readonly Channel<Func<Task>> _jobs = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _runner;

    public JobQueue() => _runner = Task.Run(RunJobsAsync);

    /// <summary>
    /// Runs <paramref name="job"/> after the jobs before it have finished. A call cancelled while it waits its turn
    /// stops waiting at once, and never runs. Once its turn comes, the token no longer cancels the call: a job that has
    /// started (a recording that has opened its file, say) always hands back what it made, and cancels itself only
    /// where it passes the token on.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The queue takes no more jobs.</exception>
    public Task<T> RunAsync<T>(Func<Task<T>> job, CancellationToken cancellationToken)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = cancellationToken.Register(() => result.TrySetCanceled(cancellationToken));
        var queued = _jobs.Writer.TryWrite(async () =>
        {
            // Disposing waits for a cancellation already running, so after it the call is either cancelled or can no
            // longer be.
            waiting.Dispose();
            if (result.Task.IsCanceled)
                return;
            try
            {
                result.TrySetResult(await job().ConfigureAwait(false));
            }
            catch (Exception exception)
            {
                result.TrySetException(exception);
            }
        });
        if (!queued)
        {
            waiting.Dispose();
            throw CameraSession.CameraClosed();
        }
        return result.Task;
    }

    // The queue's one reader: each job runs to its end before the next starts.
    private async Task RunJobsAsync()
    {
        await foreach (var job in _jobs.Reader.ReadAllAsync().ConfigureAwait(false))
            await job().ConfigureAwait(false);
    }

    /// <summary>Takes no more jobs, and completes once the ones already queued have run.</summary>
    public async ValueTask DisposeAsync()
    {
        _jobs.Writer.TryComplete();
        await _runner.ConfigureAwait(false);
    }
}
