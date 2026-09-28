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
}
