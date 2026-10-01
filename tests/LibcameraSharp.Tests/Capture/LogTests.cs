namespace LibcameraSharp.Tests.Capture;

/// <summary>libcamera's log is the oracle: redirect it to a file and see what the level lets through.</summary>
[Collection("camera")]
public class LogTests
{
    [Fact]
    public void Level_filters_libcamera_output()
    {
        var path = Path.GetTempFileName();
        try
        {
            // Categories only exist once libcamera has logged to them; a first start registers the usual ones.
            LibcameraLog.SetFile(path);
            using (new CameraManager()) { }
            File.WriteAllText(path, "");

            // At ERROR nothing from a normal start should appear; at INFO the version banner does.
            LibcameraLog.SetLevel(LibcameraLogLevel.Error);
            using (new CameraManager()) { }
            var quiet = File.ReadAllText(path);

            LibcameraLog.SetLevel(LibcameraLogLevel.Info);
            using (new CameraManager()) { }
            var chatty = File.ReadAllText(path);

            Assert.DoesNotContain("INFO", quiet);
            Assert.Contains("INFO", chatty);
            Assert.Contains("libcamera v", chatty);
        }
        finally
        {
            LibcameraLog.SetStream();
            LibcameraLog.SetLevel(LibcameraLogLevel.Info);
            File.Delete(path);
        }
    }
}
