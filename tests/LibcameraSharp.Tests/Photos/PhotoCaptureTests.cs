namespace LibcameraSharp.Tests.Photos;

/// <summary>
/// ★1 and ★5 end to end: take a photograph, write it, and read back what the camera actually did.
/// </summary>
[Collection("camera")]
public class PhotoCaptureTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("photo-tests").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task A_photo_is_written_in_its_encoding_whatever_the_file_is_called()
    {
        Assert.SkipUnless(TestCamera.Present, "no libcamera device on this machine");

        var jpeg = Path.Combine(_directory, "photo.jpg");
        var png = Path.Combine(_directory, "photo.png");

        await using (var camera = CameraDevice.Open())
        {
            var photo = await camera.CapturePhotoAsync(cancellationToken: TestContext.Current.CancellationToken);
            await photo.SaveAsync(jpeg, TestContext.Current.CancellationToken);
            await photo.SaveAsync(png, TestContext.Current.CancellationToken);

            Assert.True(photo.Size.Width > 0 && photo.Size.Height > 0);
            Assert.Null(photo.Raw);                    // not asked for
        }

        // The name is used as given and never picks the encoding: both files are the default JPEG.
        foreach (var file in new[] { jpeg, png })
        {
            var written = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);
            Assert.Equal([0xFF, 0xD8], written[..2]);              // JPEG SOI
        }
    }

    [Fact]
    public async Task The_options_decide_the_size_and_the_encoding()
    {
        Assert.SkipUnless(TestCamera.Present, "no libcamera device on this machine");

        var path = Path.Combine(_directory, "small.png");
        await using var camera = CameraDevice.Open();

        var photo = await camera.CapturePhotoAsync(new PhotoOptions
        {
            Streams = new StreamSettings { CaptureSize = new Size(640, 480) },
            Encoding = PhotoEncoding.Png,
        }, TestContext.Current.CancellationToken);

        await photo.SaveAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(new Size(640, 480), photo.Size);
        var written = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], written[..4]);     // PNG signature
    }

    [Fact]
    public async Task Two_photos_with_the_same_options_reconfigure_once()
    {
        Assert.SkipUnless(TestCamera.Present, "no libcamera device on this machine");

        await using var camera = CameraDevice.Open();
        var options = new PhotoOptions { Streams = new StreamSettings { CaptureSize = new Size(640, 480) } };

        await camera.CapturePhotoAsync(options, TestContext.Current.CancellationToken);
        var afterFirst = camera.Session.ConfigureCount;

        await camera.CapturePhotoAsync(options, TestContext.Current.CancellationToken);

        Assert.Equal(afterFirst, camera.Session.ConfigureCount);
    }

    [Fact]
    public async Task Focusing_a_camera_without_autofocus_reports_it_not_focused()
    {
        Assert.SkipUnless(TestCamera.Present, "no libcamera device on this machine");
        await using var camera = CameraDevice.Open();
        Assert.SkipWhen(camera.Capabilities.Supports(Controls.AfMode), "this camera has autofocus");

        var result = await camera.FocusAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsFocused);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task Exif_too_long_for_a_jpeg_is_refused_rather_than_written_corrupt()
    {
        Assert.SkipUnless(TestCamera.Present, "no libcamera device on this machine");
        Assert.SkipUnless(Libexif.IsAvailable, "libexif isn't installed, so no EXIF is written at all");
        await using var camera = CameraDevice.Open();
        var photo = await camera.CapturePhotoAsync(new PhotoOptions
        {
            Streams = new StreamSettings { CaptureSize = new Size(640, 480) },
            Exif = new ExifData { UserComment = new string('x', 70_000) },
        }, TestContext.Current.CancellationToken);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => photo.SaveAsync(Stream.Null, TestContext.Current.CancellationToken));
        Assert.Contains("a JPEG holds at most", refused.Message);
    }
}
