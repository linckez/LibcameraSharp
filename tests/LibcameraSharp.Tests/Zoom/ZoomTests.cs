
namespace LibcameraSharp.Tests.Zoom;

/// <summary>
/// Digital zoom: the region is fractions of <c>ScalerCrop</c>'s maximum, scaled and then translated by that rectangle's
/// top-left. The top-left matters on sensors whose active area does not start at (0, 0).
/// Needs no camera.
/// </summary>
[Collection("camera")]
public class ZoomTests
{
    // An IMX477-shaped sensor area that does not start at the origin, so the translate is visible.
    private static readonly Rectangle SensorArea = new(8, 16, 4056, 3040);

    [Fact]
    public void The_middle_half_is_scaled_and_translated()
    {
        var crop = CameraSession.Scale(new RegionOfInterest(0.25, 0.25, 0.5, 0.5), SensorArea);

        // x = 0.25*4056 = 1014, then translated by 8 -> 1022; w = 0.5*4056 = 2028.
        Assert.Equal(new Rectangle(1022, 776, 2028, 1520), crop);
    }

    [Fact]
    public void The_full_field_reproduces_the_sensor_area()
    {
        var crop = CameraSession.Scale(RegionOfInterest.Full, SensorArea);

        Assert.Equal(SensorArea, crop);
    }

    [Fact]
    public void An_offset_region_keeps_the_sensor_origin()
    {
        var crop = CameraSession.Scale(new RegionOfInterest(0.5, 0, 0.5, 1.0), SensorArea);

        // Right half: x = 0.5*4056 + 8 = 2036, y stays at the sensor's own top.
        Assert.Equal(2036, crop.X);
        Assert.Equal(16, crop.Y);
        Assert.Equal(2028u, crop.Width);
        Assert.Equal(3040u, crop.Height);
    }

    [Fact]
    public void A_camera_without_scaler_crop_says_so_rather_than_failing_obscurely()
    {
        // Only cameras that advertise no ScalerCrop, such as the test VM's virtual ones, reach the check.
        // Skipped rather than failed where there is no libcamera at all, e.g. a macOS host.
        Assert.SkipUnless(TestCamera.Present, "no libcamera device on this machine");

        using var manager = new CameraManager();
        using var camera = new CameraSession(manager);

        if (camera.CameraControls.TryGet(LibcameraSharp.Controls.ScalerCrop) is not null)
            return;                                          // a real Pi: covered on hardware instead

        var refused = Assert.Throws<InvalidOperationException>(() => camera.SetZoom(new RegionOfInterest(0.25, 0.25, 0.5, 0.5)));
        Assert.Contains("ScalerCrop", refused.Message);
    }
}
