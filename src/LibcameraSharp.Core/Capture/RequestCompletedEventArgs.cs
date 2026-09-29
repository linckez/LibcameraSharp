namespace LibcameraSharp;

/// <summary>The request that has just completed, for <see cref="ActiveCamera.RequestCompleted"/>.</summary>
/// <param name="request">The completed request.</param>
public sealed class RequestCompletedEventArgs(Request request) : EventArgs
{
    /// <summary>The completed request.</summary>
    public Request Request { get; } = request;
}
