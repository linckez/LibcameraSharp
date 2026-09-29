using System.Reflection;
using SkiaSharp;

namespace LibcameraSharp.Tests.Mocking;

/// <summary>
/// A camera for machines without one: a <see cref="CameraDevice"/> made with the protected constructor, and
/// the models <see cref="LibcameraSharpModelFactory"/> builds for it. None of this needs a camera, libcamera
/// or FFmpeg, so it runs on any machine.
/// </summary>
public class MockingTests
{
    private static readonly Size Tiny = new(2, 2);

    [Fact]
    public async Task A_factory_photo_saves_with_its_colours_in_either_32_bit_format()
    {
        // One red pixel, then three blue, as each format lays them out in memory.
        byte[] xrgb = [0, 0, 255, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0];       // B, G, R, X
        byte[] xbgr = [255, 0, 0, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0];       // R, G, B, X

        foreach (var (pixels, format) in new[] { (xrgb, PixelFormats.XRGB8888), (xbgr, PixelFormats.XBGR8888) })
        {
            var photo = LibcameraSharpModelFactory.Photo(pixels, Tiny, format,
                options: new PhotoOptions { Encoding = PhotoEncoding.Png });
            using var file = new MemoryStream();
            await photo.SaveAsync(file, TestContext.Current.CancellationToken);

            // PNG, so the colours come back exactly.
            using var decoded = SKBitmap.Decode(file.ToArray());
            Assert.Equal((2, 2), (decoded.Width, decoded.Height));
            Assert.Equal(new SKColor(255, 0, 0), decoded.GetPixel(0, 0));
            Assert.Equal(new SKColor(0, 0, 255), decoded.GetPixel(1, 1));
        }
    }

    [Fact]
    public async Task A_factory_photo_saves_as_jpeg_without_ffmpeg()
    {
        var photo = LibcameraSharpModelFactory.Photo(new byte[4 * 4], Tiny);
        using var file = new MemoryStream();
        await photo.SaveAsync(file, TestContext.Current.CancellationToken);

        Assert.Equal([0xFF, 0xD8], file.ToArray()[..2]);             // JPEG SOI
    }

    [Fact]
    public void Skia_reads_the_32_bit_formats_as_ffmpeg_converts_them()
    {
        try
        {
            Libav.Initialise();
        }
        catch (InvalidOperationException)
        {
            Assert.Skip("FFmpeg 7.1 is not installed here");
        }

        // Every byte value in every channel, at a stride with padding, so a swapped channel or a stride slip shows.
        var size = new Size(16, 16);
        const int Stride = 16 * 4 + 8;
        var data = new byte[Stride * 16];
        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)i;

        foreach (var format in new[] { PixelFormats.XRGB8888, PixelFormats.XBGR8888 })
        {
            var pixels = new FramePixels(data, format, size, Stride, colourSpace: null);
            using var read = FrameBitmap.FromPixels(pixels);
            using var converted = FrameBitmap.Converted(pixels);

            // Colour only: the fourth byte is padding. The bitmap is opaque, so the encoders ignore it,
            // though Skia still reports it as alpha.
            for (var y = 0; y < 16; y++)
                for (var x = 0; x < 16; x++)
                    Assert.Equal(converted.GetPixel(x, y).WithAlpha(255), read.GetPixel(x, y).WithAlpha(255));
        }
    }

    [Fact]
    public void The_factory_refuses_what_a_camera_could_not_have_produced()
    {
        // Too few bytes for the size, format and stride.
        Assert.Throws<ArgumentException>(() => LibcameraSharpModelFactory.Photo(new byte[15], Tiny));
        Assert.Throws<ArgumentException>(() => LibcameraSharpModelFactory.Photo(new byte[5], Tiny, PixelFormats.YUV420));
        // The wrong number of planes for the format.
        Assert.Throws<ArgumentException>(() => LibcameraSharpModelFactory.VideoFrame([new byte[4]], Tiny, PixelFormats.YUV420));
        // A value that isn't its control's type, and a camera property passed as frame metadata.
        Assert.Throws<ArgumentException>(() => LibcameraSharpModelFactory.CaptureMetadata(
            otherControls: [new(Controls.AeState, 2)]));
        Assert.Throws<ArgumentException>(() => LibcameraSharpModelFactory.CaptureMetadata(
            otherControls: [new(Properties.Model, "imx230")]));
        // A control listed twice, and a range upside down.
        Assert.Throws<ArgumentException>(() => LibcameraSharpModelFactory.CameraCapabilities(
            [new(Controls.AnalogueGain, (1, 16, 1)), new(Controls.AnalogueGain, (1, 8, 1))]));
        Assert.Throws<ArgumentException>(() => LibcameraSharpModelFactory.CameraCapabilities(
            [new(Controls.AnalogueGain, (16, 1, null))]));
    }

    [Fact]
    public void A_factory_frame_has_the_planes_and_strides_of_its_format_until_disposed()
    {
        var size = new Size(4, 2);
        var frame = LibcameraSharpModelFactory.VideoFrame([new byte[8], new byte[2], new byte[2]], size, PixelFormats.YUV420,
            sequence: 7, metadata: LibcameraSharpModelFactory.CaptureMetadata(lux: 400));

        Assert.Equal((3, 4, 2, 2), (frame.PlaneCount, frame.Stride(0), frame.Stride(1), frame.Stride(2)));
        Assert.Equal((7u, 400f, size), (frame.Sequence, frame.Metadata.Lux, frame.Size));

        frame.Dispose();
        Assert.Throws<ObjectDisposedException>(() => frame.Plane(0));
    }

    [Fact]
    public void Factory_metadata_reads_back_through_every_property()
    {
        var metadata = LibcameraSharpModelFactory.CaptureMetadata(
            exposureTime: TimeSpan.FromMilliseconds(4), analogueGain: 3, digitalGain: 1.5f, lensPosition: 2,
            lux: 400, timestamp: TimeSpan.FromSeconds(12), frameDuration: TimeSpan.FromMilliseconds(33),
            scalerCrop: new Rectangle(0, 0, 640, 480), colourTemperature: 4100, colourGains: (1.8f, 1.4f),
            colourCorrectionMatrix: [1, 0, 0, 0, 1, 0, 0, 0, 1], sensorBlackLevels: [4096, 4096, 4096, 4096],
            otherControls: [new(Controls.AeState, AeState.Converged)]);

        Assert.Equal(TimeSpan.FromMilliseconds(4), metadata.ExposureTime);
        Assert.Equal((3f, 1.5f, 2f, 400f), (metadata.AnalogueGain, metadata.DigitalGain, metadata.LensPosition, metadata.Lux));
        Assert.Equal((TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(33)), (metadata.Timestamp, metadata.FrameDuration));
        Assert.Equal(new Rectangle(0, 0, 640, 480), metadata.ScalerCrop);
        Assert.Equal((4100, (1.8f, 1.4f)), (metadata.ColourTemperature, metadata.ColourGains));
        Assert.Equal([1f, 0, 0, 0, 1, 0, 0, 0, 1], metadata.ColourCorrectionMatrix!);
        Assert.Equal([4096, 4096, 4096, 4096], metadata.SensorBlackLevels!);
        Assert.Contains(metadata.All, entry => entry.Key.Id == Controls.AeState.Id && Equals(entry.Value, AeState.Converged));
    }

    [Fact]
    public void Factory_capabilities_answer_as_a_camera_would()
    {
        var capabilities = LibcameraSharpModelFactory.CameraCapabilities(
            [new(Controls.ExposureTime, (100, 1_000_000, 20_000)), new(Controls.ScalerCrop, null)], isMono: true);

        Assert.Equal([Controls.ExposureTime.Id, Controls.ScalerCrop.Id], capabilities.Controls.Select(key => key.Id));
        Assert.Equal((100d, 1_000_000d, (double?)20_000), capabilities.Range(Controls.ExposureTime));
        Assert.True(capabilities.Supports(Controls.ScalerCrop));
        Assert.Null(capabilities.Range(Controls.ScalerCrop));                  // advertised, but not a number
        Assert.False(capabilities.Supports(Controls.AnalogueGain));
        Assert.True(capabilities.IsMono);
    }

    [Fact]
    public void A_camera_made_for_mocking_holds_no_camera()
    {
        var before = SharedCameraManager.Users;
        var camera = new FakeCamera();

        // What isn't overridden says so, rather than failing somewhere inside.
        Assert.Throws<NotSupportedException>(() => camera.SetControls(new CameraControls()));

        // Disposing gives back nothing it never took, so a real camera open elsewhere keeps its manager.
        camera.Dispose();
        Assert.Equal(before, SharedCameraManager.Users);
    }

    [Fact]
    public async Task Recording_to_a_stream_through_a_fake_runs_until_cancelled()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancel.CancelAfter(TimeSpan.FromMilliseconds(200));
        var recording = Task.Run(() => new FakeCamera().RecordToAsync(Stream.Null, cancellationToken: cancel.Token), TestContext.Current.CancellationToken);

        // A fake recording never fails, so only the token ends it.
        var finished = await Task.WhenAny(recording, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Same(recording, finished);
        Assert.True(cancel.IsCancellationRequested);
    }

    [Fact]
    public void Every_instance_member_a_camera_offers_can_be_overridden()
    {
        var notVirtual = typeof(CameraDevice)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsVirtual && method.Name != nameof(IDisposable.Dispose))
            .Select(method => method.Name);

        Assert.Empty(notVirtual);
    }

    private sealed class FakeCamera : CameraDevice
    {
        public override VideoRecording RecordTo(Stream destination, VideoOptions? options = null, VideoContainer? container = null) =>
            new FakeRecording();
    }

    private sealed class FakeRecording : VideoRecording;
}
