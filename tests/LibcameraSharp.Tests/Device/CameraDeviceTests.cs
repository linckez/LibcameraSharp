namespace LibcameraSharp.Tests.Device;

/// <summary>
/// The friendly surface, against the first camera present.
/// </summary>
[Collection("camera")]
public class CameraDeviceTests
{
    [Fact]
    public async Task Enumerate_lists_the_cameras_and_leaves_no_manager_behind()
    {
        Assert.SkipUnless(TestCamera.Present, "no libcamera device on this machine");

        var cameras = CameraDevice.Enumerate();

        Assert.NotEmpty(cameras);
        Assert.All(cameras, camera => Assert.False(string.IsNullOrWhiteSpace(camera.Id)));
        // Enumerating must not leave the shared manager acquired, or nothing could ever close it.
        Assert.Equal(0, SharedCameraManager.Users);

        // Opening takes one use of it, and closing gives that back.
        await using (var camera = CameraDevice.Open(cameras[0].Id))
            Assert.Equal(1, SharedCameraManager.Users);
        Assert.Equal(0, SharedCameraManager.Users);
    }

    [Fact]
    public void Opening_an_unknown_id_says_what_it_found()
    {
        Assert.SkipUnless(TestCamera.Present, "no libcamera device on this machine");

        var refused = Assert.Throws<ArgumentException>(() => CameraDevice.Open("/no/such/camera"));

        Assert.Contains("/no/such/camera", refused.Message);
        Assert.Equal(0, SharedCameraManager.Users);      // a failed open must not leak a use
    }

    [Fact]
    public async Task Capabilities_report_what_this_camera_advertises()
    {
        Assert.SkipUnless(TestCamera.Present, "no libcamera device on this machine");

        await using var camera = CameraDevice.Open();
        var capabilities = camera.Capabilities;

        // Numeric controls have a range; rectangles such as ScalerCrop have none.
        Assert.NotEmpty(capabilities.Controls);
        foreach (var control in capabilities.Controls)
        {
            if (capabilities.Range(control) is { } range)
                Assert.True(range.Min <= range.Max, $"{control}: {range.Min}..{range.Max}");
        }

        // A real control this camera does not advertise answers null and false, rather than throwing.
        var missing = Enum.GetValues<ControlId>().Select(id => ControlKeys.ByControlId((uint)id)).OfType<ControlKey>()
            .First(key => !capabilities.Controls.Any(advertised => advertised.Id == key.Id));
        Assert.Null(capabilities.Range(missing));
        Assert.False(capabilities.Supports(missing));
    }

    [Fact]
    public async Task A_setting_the_camera_does_not_have_is_skipped_and_named_once()
    {
        Assert.SkipUnless(TestCamera.Present, "no libcamera device on this machine");

        await using var camera = CameraDevice.Open();
        if (camera.Capabilities.Supports(Controls.ExposureTime))
            return;

        // Skipped rather than refused, and warned about once however often it is set.
        var warnings = new StringWriter();
        var previous = Console.Error;
        Console.SetError(warnings);
        try
        {
            camera.SetControls(new CameraControls { Exposure = TimeSpan.FromMilliseconds(8) });
            camera.SetControls(new CameraControls { Exposure = TimeSpan.FromMilliseconds(8) });

            // SetControls returns at once and the camera's loop warns; once it has run this, it has handled both.
            await camera.Session.CallAsync(() => { });
        }
        finally
        {
            Console.SetError(previous);
        }

        Assert.Single(warnings.ToString().Split('\n'), line => line.Contains("ExposureTime"));
    }

    /// <summary>Metadata right after opening sets the camera up for frames, as any call that takes options would, rather than failing.</summary>
    [Fact]
    public async Task Metadata_right_after_opening_sets_the_camera_up()
    {
        Assert.SkipUnless(TestCamera.Present, "no libcamera device on this machine");

        await using var camera = CameraDevice.Open();
        var metadata = await camera.CaptureMetadataAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(metadata.Timestamp);
    }
}

/// <summary>
/// Reading frames: the depth-1, newest-wins path that every reference uses for live viewing.
/// </summary>
[Collection("camera")]
public class ReadFramesTests
{
    [Fact]
    public async Task Frames_arrive_with_pixels_that_survive_an_await()
    {
        Assert.SkipUnless(TestCamera.Present, "no libcamera device on this machine");

        await using var camera = CameraDevice.Open();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var seen = 0;
        await foreach (var frame in camera.ReadFramesAsync(new FrameOptions(), stop.Token))
        {
            using (frame)
            {
                var luma = frame.Plane(0);

                // The whole point of ReadOnlyMemory over Span: it is still valid on the other side.
                await Task.Yield();

                Assert.False(luma.IsEmpty);
                Assert.True(frame.Stride(0) >= frame.Size.Width);
                Assert.Equal(1u, (uint)Math.Sign(frame.Size.Width));
            }

            if (++seen == 3)
                break;
        }

        Assert.Equal(3, seen);
    }

    [Fact]
    public async Task A_disposed_frame_refuses_to_hand_out_its_pixels()
    {
        Assert.SkipUnless(TestCamera.Present, "no libcamera device on this machine");

        await using var camera = CameraDevice.Open();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await foreach (var frame in camera.ReadFramesAsync(new FrameOptions(), stop.Token))
        {
            frame.Dispose();
            Assert.Throws<ObjectDisposedException>(() => frame.Plane(0));
            break;
        }
    }

    [Fact]
    public async Task Different_options_reconfigure()
    {
        Assert.SkipUnless(TestCamera.Present, "no libcamera device on this machine");

        await using var camera = CameraDevice.Open();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await foreach (var frame in camera.ReadFramesAsync(
            new FrameOptions { Streams = new StreamSettings { CaptureSize = new Size(640, 480) } }, stop.Token))
        {
            frame.Dispose();
            break;
        }
        var afterFirst = camera.Session.ConfigureCount;

        await foreach (var frame in camera.ReadFramesAsync(
            new FrameOptions { Streams = new StreamSettings { CaptureSize = new Size(800, 600) } }, stop.Token))
        {
            frame.Dispose();
            break;
        }

        Assert.True(camera.Session.ConfigureCount > afterFirst,
            "a different capture size must reconfigure; otherwise the options was ignored");
    }
}
