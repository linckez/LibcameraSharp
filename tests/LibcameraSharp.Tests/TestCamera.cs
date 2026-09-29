namespace LibcameraSharp.Tests;

/// <summary>Whether this machine has a libcamera camera at all; tests that need one skip without it.</summary>
internal static class TestCamera
{
    public static bool Present
    {
        get
        {
            try
            {
                using var manager = new CameraManager();
                return manager.Cameras.Count > 0;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Whether the first camera is libcamera's virtual one, which timestamps frames when they're queued rather than
    /// exposed, so two frames can share a timestamp and an encoder refuses the second. Read before opening a camera:
    /// it opens a manager of its own.
    /// </summary>
    public static bool IsVirtual
    {
        get
        {
            try
            {
                using var manager = new CameraManager();
                return manager.Cameras.Count > 0
                       && manager.Cameras[0].Properties.TryGet(Properties.PipelineHandler, out var pipeline) && pipeline == "virtual";
            }
            catch
            {
                return false;
            }
        }
    }
}
