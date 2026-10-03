namespace LibcameraSharp.Tests.Device;

/// <summary>Ranges in the units <see cref="CameraControls"/> takes them in, where they differ from libcamera's.</summary>
public class CapabilityRangeTests
{
    // The IMX230 unit's own figures (trixie, libcamera 0.7.2).
    private static readonly CameraCapabilities Unit = LibcameraSharpModelFactory.CameraCapabilities(
    [
        KeyValuePair.Create<ControlKey, (double, double, double?)?>(Controls.ExposureTime, (1, 66_666, 20_000)),
        KeyValuePair.Create<ControlKey, (double, double, double?)?>(Controls.FrameDurationLimits, (33_333, 250_000_000, null)),
    ]);

    private static readonly CameraCapabilities Bare = LibcameraSharpModelFactory.CameraCapabilities([]);

    [Fact]
    public void Exposure_comes_in_time_not_microseconds()
    {
        Assert.Equal((TimeSpan.FromMicroseconds(1), TimeSpan.FromMicroseconds(66_666), TimeSpan.FromMilliseconds(20)), Unit.Exposure);
        Assert.Null(Bare.Exposure);
    }

    [Fact]
    public void The_frame_rate_is_one_frame_per_duration_with_the_ends_swapped()
    {
        var (slowest, fastest, @default) = Unit.FrameRate!.Value;

        Assert.Equal(1_000_000.0 / 250_000_000, slowest, precision: 9);
        Assert.Equal(1_000_000.0 / 33_333, fastest, precision: 9);
        Assert.Null(@default);
        Assert.Null(Bare.FrameRate);
    }
}
