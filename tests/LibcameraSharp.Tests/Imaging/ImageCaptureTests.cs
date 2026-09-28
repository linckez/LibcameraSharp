using SkiaSharp;

namespace LibcameraSharp.Tests.Imaging;

/// <summary>
/// Photos decode back to what the camera saw: the <c>virtual</c> image camera plays a known picture
/// as NV12, so our YUV-to-RGB conversion and encoders can be checked pixel by pixel.
/// </summary>
[Collection("camera")]
public class ImageCaptureTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Virtual_image_camera_frames_encode_back_to_the_source_image()
    {
        // JPEG, 4:2:0 chroma and BT.601 rounding are the only losses, so the error stays small.
        var camera0 = CameraDevice.Enumerate().FirstOrDefault(camera => camera.Id == "VirtualImage");
        Assert.SkipWhen(camera0 == default, "the virtual pipeline is not present");
        var source = FindRepoFile(Path.Combine("tests", "vm", "virtual-frame.jpg"));
        Assert.SkipWhen(source is null, "tests/vm/virtual-frame.jpg not found from the test directory");

        using var camera = CameraDevice.Open(camera0.Id);
        var streams = new StreamSettings { CaptureSize = new Size(640, 480) };
        var dir = Directory.CreateTempSubdirectory().FullName;
        var jpeg = await camera.CapturePhotoAsync(new PhotoOptions { Streams = streams }, Ct);
        await jpeg.SaveAsync(Path.Combine(dir, "v.jpg"), Ct);
        var png = await camera.CapturePhotoAsync(new PhotoOptions { Streams = streams, Encoding = PhotoEncoding.Png }, Ct);
        await png.SaveAsync(Path.Combine(dir, "v.png"), Ct);

        using var expected = SKBitmap.Decode(source!);
        foreach (var name in new[] { "v.jpg", "v.png" })
        {
            using var actual = SKBitmap.Decode(Path.Combine(dir, name));
            Assert.Equal((expected.Width, expected.Height), (actual.Width, actual.Height));
            var error = MeanAbsoluteError(expected, actual);
            TestContext.Current.TestOutputHelper?.WriteLine($"{name}: mean channel error {error:F2}/255");
            Assert.True(error < 10, $"{name}: mean channel error {error:F2}/255 against the source image");
        }
    }

    private static double MeanAbsoluteError(SKBitmap a, SKBitmap b)
    {
        double sum = 0;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                var p = a.GetPixel(x, y);
                var q = b.GetPixel(x, y);
                sum += Math.Abs(p.Red - q.Red) + Math.Abs(p.Green - q.Green) + Math.Abs(p.Blue - q.Blue);
            }
        }
        return sum / (3.0 * a.Width * a.Height);
    }

    private static string? FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
