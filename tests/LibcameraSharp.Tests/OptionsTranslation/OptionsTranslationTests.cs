namespace LibcameraSharp.Tests.OptionsTranslation;

/// <summary>
/// Translating options into what the camera layer already understands. The rule being tested is
/// the one the whole design rests on: <b>a control list is sparse</b> — null does not send the
/// control, and automatic sends the zero that returns it to automatic. Needs no camera.
/// </summary>
public class OptionsTranslationTests
{
    private static PendingControls Translate(CameraControls controls)
    {
        var settings = new PendingControls();
        controls.ApplyTo(settings);
        return settings;
    }

    [Fact]
    public void Empty_options_send_nothing_at_all()
    {
        Assert.Equal(0, Translate(new CameraControls()).Count);
    }

    [Fact]
    public void Only_what_was_set_is_sent()
    {
        var settings = Translate(new CameraControls { Gain = GainMode.Fixed(2.0f) });

        Assert.Equal(1, settings.Count);
        Assert.True(settings.Contains(Controls.AnalogueGain));
        Assert.False(settings.Contains(Controls.ExposureTime));
    }

    [Fact]
    public void Exposure_is_microseconds()
    {
        var settings = Translate(new CameraControls { Exposure = ExposureMode.Fixed(TimeSpan.FromMilliseconds(8)) });

        Assert.Equal(8_000, settings.Get(Controls.ExposureTime));
    }

    [Fact]
    public void Automatic_is_sent_as_the_zero_that_returns_the_camera_to_automatic()
    {
        // ExposureTime 0 becomes ExposureTimeMode Auto; dropping it as "empty" would leave a camera
        // stuck in manual with no way back.
        var settings = Translate(new CameraControls { Exposure = ExposureMode.Auto, Gain = GainMode.Auto });

        Assert.Equal(0, settings.Get(Controls.ExposureTime));
        Assert.Equal(0f, settings.Get(Controls.AnalogueGain));
    }

    [Fact]
    public void A_frame_rate_becomes_duration_limits()
    {
        var settings = Translate(new CameraControls { FrameRate = (5.0, 30.0) });

        Assert.Equal([33_333L, 200_000L], settings.Get(Controls.FrameDurationLimits));
    }

    [Fact]
    public void Manual_white_balance_sends_gains_and_automatic_sends_a_mode()
    {
        // Gains apply only with automatic white balance off, so that goes with them.
        var manual = Translate(new CameraControls { WhiteBalance = WhiteBalance.Manual(1.8f, 1.4f) });
        Assert.Equal([1.8f, 1.4f], manual.Get(Controls.ColourGains));
        Assert.False(manual.Get(Controls.AwbEnable));
        Assert.False(manual.Contains(Controls.AwbMode));

        // A mode alone leaves earlier fixed gains in force, so automatic also re-enables auto white balance.
        var auto = Translate(new CameraControls { WhiteBalance = WhiteBalance.Auto(AwbMode.Incandescent) });
        Assert.Equal(AwbMode.Incandescent, auto.Get(Controls.AwbMode));
        Assert.True(auto.Get(Controls.AwbEnable));
        Assert.False(auto.Contains(Controls.ColourGains));
    }

    [Fact]
    public void Focusing_at_a_distance_sends_both_the_mode_and_the_position()
    {
        // LensPosition without AfMode.Manual is ignored by libcamera, the same trap as exposure.
        var settings = Translate(new CameraControls { Focus = FocusMode.AtMetres(0.5) });

        Assert.Equal(AfMode.Manual, settings.Get(Controls.AfMode));
        Assert.Equal(2f, settings.Get(Controls.LensPosition));
    }

    [Fact]
    public void Continuous_focus_sends_a_mode_and_no_position()
    {
        var settings = Translate(new CameraControls { Focus = FocusMode.Continuous });

        Assert.Equal(AfMode.Continuous, settings.Get(Controls.AfMode));
        Assert.False(settings.Contains(Controls.LensPosition));
        Assert.False(settings.Contains(Controls.AfTrigger));
    }

    [Fact]
    public void Auto_focus_triggers_a_scan()
    {
        // In AfMode.Auto the lens never moves until AfTrigger starts a scan.
        var settings = Translate(new CameraControls { Focus = FocusMode.Auto });

        Assert.Equal(AfMode.Auto, settings.Get(Controls.AfMode));
        Assert.Equal(AfTrigger.Start, settings.Get(Controls.AfTrigger));
    }

    [Fact]
    public void A_camera_that_can_focus_focuses_unless_told_otherwise()
    {
        // Photos scan before they are taken; video and frames focus continuously.
        var photo = Translate(CameraSession.WithDefaultFocus(new CameraControls(), CameraUse.Photo, canFocus: true));
        Assert.Equal(AfMode.Auto, photo.Get(Controls.AfMode));
        Assert.Equal(AfTrigger.Start, photo.Get(Controls.AfTrigger));

        var video = Translate(CameraSession.WithDefaultFocus(new CameraControls(), CameraUse.Video, canFocus: true));
        Assert.Equal(AfMode.Continuous, video.Get(Controls.AfMode));

        // A focus you chose is kept, and a fixed-focus camera is sent nothing.
        var chosen = Translate(CameraSession.WithDefaultFocus(new CameraControls { Focus = FocusMode.Infinity }, CameraUse.Photo, canFocus: true));
        Assert.Equal(AfMode.Manual, chosen.Get(Controls.AfMode));
        Assert.False(chosen.Contains(Controls.AfTrigger));
        Assert.False(Translate(CameraSession.WithDefaultFocus(new CameraControls(), CameraUse.Photo, canFocus: false)).Contains(Controls.AfMode));
    }

    [Fact]
    public void A_flicker_period_turns_manual_flicker_avoidance_on_and_off_turns_it_off()
    {
        // The period is ignored unless AeFlickerMode is Manual.
        var on = Translate(new CameraControls { Flicker = FlickerMode.Manual(TimeSpan.FromMilliseconds(10)) });
        Assert.Equal(AeFlickerMode.Manual, on.Get(Controls.AeFlickerMode));
        Assert.Equal(10_000, on.Get(Controls.AeFlickerPeriod));        // microseconds

        var off = Translate(new CameraControls { Flicker = FlickerMode.Off });
        Assert.Equal(AeFlickerMode.Off, off.Get(Controls.AeFlickerMode));
        Assert.False(off.Contains(Controls.AeFlickerPeriod));
    }

    [Fact]
    public void The_rest_of_the_controls_translate_too()
    {
        var settings = Translate(new CameraControls
        {
            Denoise = NoiseReductionMode.Fast,
            Hdr = HdrMode.SingleExposure,
            AutofocusRange = AfRange.Macro,
            AutofocusSpeed = AfSpeed.Fast,
            Brightness = 0.1f,
            Contrast = 1.2f,
            Saturation = 0.9f,
            Sharpness = 1.5f,
            ExposureValue = -0.5f,
        });

        Assert.Equal(NoiseReductionMode.Fast, settings.Get(Controls.Draft.NoiseReductionMode));
        Assert.Equal(HdrMode.SingleExposure, settings.Get(Controls.HdrMode));
        Assert.Equal(AfRange.Macro, settings.Get(Controls.AfRange));
        Assert.Equal(AfSpeed.Fast, settings.Get(Controls.AfSpeed));
        Assert.Equal(0.1f, settings.Get(Controls.Brightness));
        Assert.Equal(1.2f, settings.Get(Controls.Contrast));
        Assert.Equal(0.9f, settings.Get(Controls.Saturation));
        Assert.Equal(1.5f, settings.Get(Controls.Sharpness));
        Assert.Equal(-0.5f, settings.Get(Controls.ExposureValue));
    }

    [Fact]
    public void Stream_settings_write_only_what_they_carry()
    {
        var config = new SessionConfiguration();
        var before = config.Capture.Size;

        new StreamSettings { Orientation = Orientation.Rotate180 }.ApplyTo(config);

        Assert.Equal(Orientation.Rotate180, config.Transform);
        Assert.Equal(before, config.Capture.Size);      // untouched
        Assert.Null(config.Preview);
        Assert.Null(config.Raw);
    }

    [Fact]
    public void A_preview_size_creates_the_second_stream_and_defaults_its_format()
    {
        var config = new SessionConfiguration();

        new StreamSettings { PreviewSize = new Size(640, 480) }.ApplyTo(config);

        Assert.NotNull(config.Preview);
        Assert.Equal(new Size(640, 480), config.Preview!.Size);
        Assert.Equal(PixelFormats.YUV420, config.Preview.Format);
    }

    [Fact]
    public void A_preview_format_with_no_preview_size_is_refused_rather_than_ignored()
    {
        var refused = Assert.Throws<ArgumentException>(
            () => new StreamSettings { PreviewFormat = PixelFormats.YUV420 }.ApplyTo(new SessionConfiguration()));

        Assert.Contains(nameof(StreamSettings.PreviewSize), refused.Message);
    }

    [Fact]
    public void A_sensor_mode_becomes_the_sensor_configuration()
    {
        var config = new SessionConfiguration();

        new StreamSettings { SensorMode = new SensorMode(new Size(2028, 1520), 12, 40, new Rectangle(0, 0, 4056, 3040)) }
            .ApplyTo(config);

        Assert.Equal(new Size(2028, 1520), config.Sensor.OutputSize);
        Assert.Equal(12, config.Sensor.BitDepth);
    }

    [Fact]
    public void Turning_automatic_back_on_drops_the_manual_values_it_replaces()
    {
        // Kept, an old manual value would go out with the switch (in the next request, or replayed after a restart)
        // and win: an exposure sets its mode to manual, and the camera applies colour gains after AwbEnable.
        var settings = new PendingControls();
        new CameraControls
        {
            Exposure = ExposureMode.Fixed(TimeSpan.FromMilliseconds(8)),
            Gain = GainMode.Fixed(2f),
            WhiteBalance = WhiteBalance.Manual(1.8f, 1.4f),
        }.ApplyTo(settings);
        new CameraControls { AutoExposure = true, AutoWhiteBalance = true }.ApplyTo(settings);

        Assert.False(settings.Contains(Controls.ExposureTime));
        Assert.False(settings.Contains(Controls.AnalogueGain));
        Assert.False(settings.Contains(Controls.ColourGains));

        // In one set of options, the manual value still wins, as libcamera's precedence has it.
        var both = Translate(new CameraControls { AutoExposure = true, Exposure = ExposureMode.Fixed(TimeSpan.FromMilliseconds(8)) });
        Assert.Equal(8_000, both.Get(Controls.ExposureTime));
    }

    [Fact]
    public void The_set_replayed_after_a_restart_drops_manual_values_when_automatic_comes_back()
    {
        // What the session keeps and replays on a restart: every set merged in, in order.
        var applied = new PendingControls();
        void Merge(CameraControls controls) => applied.SetControls(Translate(controls));

        // Automatic already on, then a fixed exposure, then automatic again: the switch is unchanged, the exposure goes.
        Merge(new CameraControls { AutoExposure = true });
        Merge(new CameraControls { Exposure = ExposureMode.Fixed(TimeSpan.FromMilliseconds(8)) });
        Merge(new CameraControls { AutoExposure = true });
        Assert.False(applied.Contains(Controls.ExposureTime));

        // A fixed exposure that comes in with the switch is newer, and stays.
        Merge(new CameraControls { AutoExposure = true, Exposure = ExposureMode.Fixed(TimeSpan.FromMilliseconds(10)) });
        Assert.Equal(10_000, applied.Get(Controls.ExposureTime));
    }

    [Fact]
    public void A_value_that_would_mean_automatic_is_refused_where_a_fixed_one_is_asked_for()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExposureMode.Fixed(TimeSpan.FromTicks(5)));     // reaches the camera as 0 µs
        Assert.Throws<ArgumentOutOfRangeException>(() => GainMode.Fixed(0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => FlickerMode.Manual(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => WhiteBalance.Manual(1.8f, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraControls { FrameRate = 0 }.ThrowIfInvalid("options"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CameraControls { FrameRate = (0, 30) }.ThrowIfInvalid("options"));
    }

    [Theory]
    [InlineData(0.5f)]                       // libcamera: AnalogueGain "cannot be lower than 1.0"
    [InlineData(-2f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void A_gain_libcamera_does_not_allow_is_refused(float gain) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => GainMode.Fixed(gain));

    [Fact]
    public void A_gain_of_exactly_one_is_allowed() => Assert.Equal(1f, GainMode.Fixed(1f).Gain);
}
