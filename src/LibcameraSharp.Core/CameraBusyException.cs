namespace LibcameraSharp;

/// <summary>
/// The camera is already held by someone else. <c>Camera::acquire</c> is exclusive per process, so
/// the second caller — another application, another service, or a second <c>Open</c> in this one —
/// gets this rather than a bare errno.
/// </summary>
public sealed class CameraBusyException : LibcameraException
{
    internal const int Ebusy = 16;

    /// <summary>Creates the exception for a camera that could not be acquired.</summary>
    public CameraBusyException(string operation, string? cameraId = null)
        : base(operation, Ebusy) => CameraId = cameraId;

    /// <summary>The camera that was already held, when known.</summary>
    public string? CameraId { get; }
}
