namespace LibcameraSharp.Tests.ControlValues;

/// <summary>
/// The typed control values the surface offers, and the libcamera values they become. Needs no camera.
/// </summary>
public class ControlValueTests
{
    [Theory]
    [InlineData(30.0, 33_333, 33_333)]
    [InlineData(10.0, 100_000, 100_000)]
    public void A_fixed_rate_becomes_equal_duration_limits(double fps, long min, long max)
    {
        // Limits are (1e6/max, 1e6/min) µs, so a fixed rate gives both ends the same value.
        var limits = ((FrameRate)fps).ToDurationLimits();
        Assert.Equal(min, limits[0]);
        Assert.Equal(max, limits[1]);
    }

    [Fact]
    public void A_range_becomes_limits_longest_last()
    {
        // 5-30 fps is 33_333 us at the fast end and 200_000 us at the slow end.
        var limits = ((FrameRate)(5.0, 30.0)).ToDurationLimits();
        Assert.Equal(33_333, limits[0]);
        Assert.Equal(200_000, limits[1]);
    }

    [Fact]
    public void Controls_no_camera_can_take_are_refused_before_they_are_used()
    {
        new CameraControls { FrameRate = 30, Zoom = new RegionOfInterest(0.25, 0.25, 0.5, 0.5),
                             AutofocusWindows = [new RegionOfInterest(0.7, 0.7, 0.3, 0.3)] }.ThrowIfInvalid();

        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraControls { FrameRate = (0, 1000) }.ThrowIfInvalid());
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraControls { Zoom = new RegionOfInterest(0.9, 0.9, 0.5, 0.5) }.ThrowIfInvalid());
        var outside = Assert.Throws<ArgumentOutOfRangeException>(
            () => new CameraControls { AutofocusWindows = [new RegionOfInterest(1.5, 0, 0.3, 0.3)] }.ThrowIfInvalid("settings"));
        Assert.Equal("settings", outside.ParamName);
    }

    [Fact]
    public void Lens_position_is_dioptres_and_infinity_is_zero()
    {
        // libcamera's LensPosition is 1/metres; 0 means infinity, so AtMetres(0) must not divide by it.
        Assert.Equal(0f, FocusMode.Infinity.Dioptres);
        Assert.Equal(2f, FocusMode.AtMetres(0.5).Dioptres);
        Assert.Equal(0.5f, FocusMode.AtMetres(2.0).Dioptres);
        Assert.Equal(0f, FocusMode.AtMetres(double.PositiveInfinity).Dioptres);
        Assert.Throws<ArgumentOutOfRangeException>(() => FocusMode.AtMetres(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => FocusMode.AtMetres(-1));
    }
}
