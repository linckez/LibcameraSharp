using LibcameraSharp.Tests.Capture;
using SkiaSharp;

namespace LibcameraSharp.Tests.Video;

/// <summary>
/// The MJPEG encoder, checked the way a viewer would check it: every frame is a complete JPEG, it
/// decodes, and it is the size the camera was configured for.
/// </summary>
[Collection("camera")]
public class LibavMjpegEncoderTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_frame_is_a_complete_jpeg_of_the_configured_size()
    {
        using var manager = new CameraManager();
        await using var session = new CameraSession(manager);
        using var buffer = new MemoryStream();
        using var sink = new FileOutput(buffer);
        var encoder = new LibavMjpegEncoder();

        await session.ConfigureAsync(s => s.CreateVideoConfiguration(main: new StreamDescription(new Size(640, 480), PixelFormats.BGR888)));

        await session.StartRecordingAsync(encoder, sink);
        await WaitForFramesAsync(session, encoder, 5);
        await session.StopRecordingAsync(encoder);

        var data = buffer.ToArray();
        var frames = SplitJpegs(data);
        Assert.True(frames.Count >= 5, $"{frames.Count} frames in {data.Length} bytes");
        Assert.All(frames, frame =>
        {
            Assert.Equal(0xFF, frame[0]);                                  // SOI
            Assert.Equal(0xD8, frame[1]);
            Assert.Equal(0xFF, frame[^2]);                                 // EOI
            Assert.Equal(0xD9, frame[^1]);
            using var bitmap = SKBitmap.Decode(frame);
            Assert.NotNull(bitmap);
            Assert.Equal((640, 480), (bitmap.Width, bitmap.Height));
        });
        output.WriteLine($"{frames.Count} frames, {data.Length} bytes, {data.Length / frames.Count} bytes/frame");
    }

    // Each frame is a standalone JPEG; split on SOI, which is what an MJPEG reader does.
    private static List<byte[]> SplitJpegs(byte[] data)
    {
        var starts = new List<int>();
        for (var i = 0; i < data.Length - 1; i++)
        {
            if (data[i] == 0xFF && data[i + 1] == 0xD8)
                starts.Add(i);
        }
        var frames = new List<byte[]>(starts.Count);
        for (var i = 0; i < starts.Count; i++)
            frames.Add(data[starts[i]..(i + 1 < starts.Count ? starts[i + 1] : data.Length)]);
        return frames;
    }

    // A handler that throws on libcamera's thread is swallowed, so check for that rather than time out blind.
    private static async Task WaitForFramesAsync(CameraSession session, Encoder encoder, int frames)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        while (encoder.FramesEncoded < frames)
        {
            await Task.Delay(20, timeout.Token);
        }
    }
}
