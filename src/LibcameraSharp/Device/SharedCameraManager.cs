namespace LibcameraSharp;

/// <summary>
/// One <see cref="CameraManager"/> shared by everything that did not bring its own, counted so the
/// last user to let go is the one that stops libcamera.
/// </summary>
/// <remarks>
/// libcamera allows one manager per process, and disposing it disposes every camera it handed out,
/// so two open cameras must share one and only the last to close may dispose it.
/// </remarks>
internal static class SharedCameraManager
{
    private static readonly Lock Gate = new();
    private static CameraManager? _manager;
    private static int _users;

    /// <summary>The shared manager, creating it if this is the first user.</summary>
    /// <exception cref="InvalidOperationException">The caller already owns a <see cref="CameraManager"/> of their own.</exception>
    internal static CameraManager Acquire()
    {
        lock (Gate)
        {
            _manager ??= new CameraManager();
            _users++;
            return _manager;
        }
    }

    /// <summary>Gives up one use; the last one disposes the manager.</summary>
    internal static void Release()
    {
        CameraManager? closing = null;
        lock (Gate)
        {
            if (_users == 0)
                return;
            if (--_users == 0)
                (closing, _manager) = (_manager, null);
        }

        // Outside the lock: Dispose walks every camera it handed out and can take a while.
        closing?.Dispose();
    }

    /// <summary>How many users the shared manager currently has. For tests.</summary>
    internal static int Users
    {
        get { lock (Gate) return _users; }
    }
}
