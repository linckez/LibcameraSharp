using LibcameraSharp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

// The seven scenarios the SDK is built around, compiled against the real surface: if the public
// API stops fitting them, this stops building.
static class Champions
{
    static readonly PhotoOptions Night = new()
    {
        Streams = new StreamSettings { CaptureSize = new Size(4056, 3040) },
        Controls = new CameraControls { Exposure = TimeSpan.FromMilliseconds(80), Gain = 8.0f, FrameRate = (5, 30) },
        Encoding = PhotoEncoding.Jpeg,
        JpegQuality = 95,
        Exif = new ExifData { Artist = "A. Rossi", Copyright = "CC-BY" },
    };

    public static async Task One(CancellationToken ct)
    {
        using CameraDevice camera = CameraDevice.Open();

        Photo photo = await camera.CapturePhotoAsync(cancellationToken: ct);
        await photo.SaveAsync(path: "photo.jpg", cancellationToken: ct);

        Photo dark = await camera.CapturePhotoAsync(options: Night, cancellationToken: ct);
        await dark.SaveAsync(path: "night.jpg", cancellationToken: ct);
    }

    public static void Two(WebApplicationBuilder builder)
    {
        VideoOptions live = new()
        {
            Streams = new StreamSettings { CaptureSize = new Size(1280, 720) },
            Codec = VideoCodec.H264,
        };

        builder.Services.AddSingleton(_ => CameraDevice.Open());
        WebApplication app = builder.Build();

        app.MapGet("/live.mp4", (CameraDevice camera, CancellationToken ct) =>
            Results.Stream(body => camera.RecordToAsync(body, live, VideoContainer.Mp4, ct), "video/mp4"));
    }

    public static async Task Three(CameraDevice camera, CancellationToken ct)
    {
        VideoOptions clip = new()
        {
            Streams = new StreamSettings { CaptureSize = new Size(1920, 1080) },
            Controls = new CameraControls { FrameRate = 30 },
            Codec = VideoCodec.H264,
            Quality = Quality.High,
            KeyframeInterval = 60,                                   // frames
        };

        await using (VideoRecording recording = camera.RecordTo("clip.mp4", clip))
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            Console.WriteLine($"{recording.FrameCount} frames");
        }
    }

    public static async Task Four(CameraDevice camera, CancellationToken ct)
    {
        FrameOptions watching = new()
        {
            Streams = new StreamSettings { PreviewSize = new Size(640, 480), PreviewFormat = PixelFormats.YUV420 },
        };

        await foreach (VideoFrame frame in camera.ReadFramesAsync(watching, ct))
            using (frame)
            {
                if (frame.Metadata.Lux < 10) continue;
                await DetectAsync(frame.Plane(0), frame.Size, frame.Stride(0), ct);   // yours, not ours
            }

        Console.WriteLine($"kept up with all but {camera.FramesDropped} frames");
    }

    public static async Task Five(CameraDevice camera, CancellationToken ct)
    {
        CameraControls manual = new()
        {
            Exposure = TimeSpan.FromMilliseconds(8),
            Gain = 2.0f,
            WhiteBalance = WhiteBalance.Manual(redGain: 1.8f, blueGain: 1.4f),
            Focus = FocusMode.AtMetres(0.5),
            Zoom = new RegionOfInterest(0.25, 0.25, 0.5, 0.5),
        };

        Photo photo = await camera.CapturePhotoAsync(new PhotoOptions { Controls = manual }, ct);
        Console.WriteLine($"asked 8 ms; got {photo.Metadata.ExposureTime?.TotalMilliseconds} ms at gain {photo.Metadata.AnalogueGain}");

        camera.SetControls(new CameraControls { Exposure = TimeSpan.FromMilliseconds(12) });

        (double Min, double Max, double? Default)? gain = camera.Capabilities.Range(Controls.AnalogueGain);
        Console.WriteLine($"this sensor goes to {gain?.Max}x");
    }

    public static async Task Six(CameraDevice camera, CancellationToken ct)
    {
        PhotoOptions withRaw = new()
        {
            Streams = new StreamSettings { CaptureRaw = true },
            Encoding = PhotoEncoding.Jpeg,
        };

        Photo shot = await camera.CapturePhotoAsync(options: withRaw, cancellationToken: ct);
        await shot.SaveAsync(path: "shot.jpg", cancellationToken: ct);

        RawImage raw = shot.Raw ?? throw new InvalidOperationException("this camera has no raw stream");
        await raw.SaveAsync(path: "shot.dng", cancellationToken: ct);
        ReadOnlyMemory<byte> bayer = raw.Bytes;
        Console.WriteLine(bayer.Length);
    }

    public static async Task Seven(CancellationToken ct)
    {
        IReadOnlyList<CameraInfo> cameras = CameraDevice.Enumerate();
        foreach (CameraInfo found in cameras)
            Console.WriteLine($"{found.Model}  {found.Id}");

        using CameraDevice camera = CameraDevice.Open(cameras[1].Id);

        IReadOnlyList<SensorMode> modes = await camera.ProbeSensorModesAsync(ct);
        SensorMode fast = modes.MaxBy(mode => mode.MaxFrameRate)!;

        VideoOptions options = new()
        {
            Streams = new StreamSettings { CaptureSize = new Size(1920, 1080), SensorMode = fast },
        };

        await using VideoRecording recording = camera.RecordTo("clip.mp4", options);
        await Task.Delay(TimeSpan.FromSeconds(10), ct);
    }

    static Task DetectAsync(ReadOnlyMemory<byte> luma, Size size, int stride, CancellationToken ct) => Task.CompletedTask;
}
