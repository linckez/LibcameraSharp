using System.Runtime.InteropServices;

namespace LibcameraSharp;

/// <summary>
/// Raised when libcamera reports an error. <see cref="Errno"/> is the underlying Linux error
/// code (e.g. <c>EBUSY</c> when a camera is already acquired, <c>EACCES</c> when an operation
/// isn't allowed in the camera's current state).
/// </summary>
public class LibcameraException : Exception
{
    /// <summary>Creates an exception for a failed libcamera call.</summary>
    /// <param name="operation">What was attempted, e.g. <c>"acquire camera"</c>.</param>
    /// <param name="errno">Positive Linux error number.</param>
    public LibcameraException(string operation, int errno)
        : base($"libcamera: failed to {operation}: {Marshal.GetPInvokeErrorMessage(errno)} (errno {errno})")
    {
        Operation = operation;
        Errno = errno;
    }

    /// <summary>Creates an exception for a failed libcamera call that reports no errno.</summary>
    /// <param name="operation">What was attempted, e.g. <c>"create request"</c>.</param>
    /// <param name="reason">Why it failed, e.g. <c>"configure the camera first"</c>.</param>
    public LibcameraException(string operation, string reason)
        : base($"libcamera: failed to {operation}: {reason}")
    {
        Operation = operation;
    }

    /// <summary>What was being attempted.</summary>
    public string Operation { get; }

    /// <summary>Linux errno, or null when libcamera reported none.</summary>
    public int? Errno { get; }

    /// <summary>Throws when <paramref name="ret"/> is negative, following libcamera's "negative errno" convention.</summary>
    internal static void ThrowIfError(int ret, string operation)
    {
        if (ret < 0)
            throw new LibcameraException(operation, -ret);
    }
}
