using System.IO.Pipelines;

namespace LibcameraSharp;

/// <summary>Copies a pipe into a stream with asynchronous writes, for streams that accept no other kind.</summary>
internal static class PipeToStream
{
    /// <summary>
    /// Copies until the pipe's writer completes, then flushes. A failure is handed to
    /// <paramref name="failed"/> and ends the pipe, so the writer's next flush sees it.
    /// </summary>
    public static async Task CopyAsync(PipeReader reader, Stream destination, Action<Exception> failed)
    {
        try
        {
            await reader.CopyToAsync(destination).ConfigureAwait(false);
            await destination.FlushAsync().ConfigureAwait(false);
            await reader.CompleteAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failed(exception);
            await reader.CompleteAsync(exception).ConfigureAwait(false);
        }
    }
}
