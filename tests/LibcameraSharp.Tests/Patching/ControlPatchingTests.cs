
namespace LibcameraSharp.Tests.Patching;

/// <summary>
/// The exposure/gain mode rule: a fixed value
/// implies manual mode, zero means auto (and isn't sent), and nothing is added for cameras that
/// don't advertise the mode control. Needs no camera.
/// </summary>
public class ControlPatchingTests
{
    private static readonly HashSet<uint> PiLikeCamera =
        [Controls.ExposureTime.Id, Controls.ExposureTimeMode.Id, Controls.AnalogueGain.Id, Controls.AnalogueGainMode.Id, Controls.Brightness.Id];

    [Fact]
    public void Fixed_exposure_and_gain_switch_their_modes_to_manual()
    {
        using var source = new ControlList();
        using var target = new ControlList();
        source.Set(Controls.ExposureTime, 20_000);
        source.Set(Controls.AnalogueGain, 2.0f);
        source.Set(Controls.Brightness, 0.5f);

        ControlPatching.Apply(source, target, k => PiLikeCamera.Contains(k.Id));

        Assert.Equal(20_000, target.Get(Controls.ExposureTime));
        Assert.Equal(ExposureTimeMode.Manual, target.Get(Controls.ExposureTimeMode));
        Assert.Equal(2.0f, target.Get(Controls.AnalogueGain));
        Assert.Equal(AnalogueGainMode.Manual, target.Get(Controls.AnalogueGainMode));
        Assert.Equal(0.5f, target.Get(Controls.Brightness));
    }

    [Fact]
    public void Zero_means_auto_and_the_zero_is_not_sent()
    {
        using var source = new ControlList();
        using var target = new ControlList();
        source.Set(Controls.ExposureTime, 0);
        source.Set(Controls.AnalogueGain, 0f);

        ControlPatching.Apply(source, target, k => PiLikeCamera.Contains(k.Id));

        Assert.Equal(ExposureTimeMode.Auto, target.Get(Controls.ExposureTimeMode));
        Assert.Equal(AnalogueGainMode.Auto, target.Get(Controls.AnalogueGainMode));
        Assert.False(target.Contains(Controls.ExposureTime));
        Assert.False(target.Contains(Controls.AnalogueGain));
    }

    [Fact]
    public void Cameras_without_mode_controls_get_the_value_as_is()
    {
        using var source = new ControlList();
        using var target = new ControlList();
        source.Set(Controls.ExposureTime, 20_000);

        ControlPatching.Apply(source, target, _ => false);   // a camera without ExposureTimeMode

        Assert.Equal(20_000, target.Get(Controls.ExposureTime));
        Assert.False(target.Contains(Controls.ExposureTimeMode));
    }

    [Fact]
    public void Existing_target_values_are_overwritten()
    {
        using var source = new ControlList();
        using var target = new ControlList();
        target.Set(Controls.Brightness, -1f);
        source.Set(Controls.Brightness, 1f);

        ControlPatching.Apply(source, target, _ => true);

        Assert.Equal(1f, target.Get(Controls.Brightness));
    }
}
