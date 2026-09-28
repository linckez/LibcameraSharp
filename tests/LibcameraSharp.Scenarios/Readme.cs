using LibcameraSharp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

// The README's listings, compiled. If the README drifts from the surface, this breaks.
static class ReadmeSamples
{
    public static async Task QuickStart()
    {
        using CameraDevice camera = CameraDevice.Open();

        Photo photo = await camera.CapturePhotoAsync();
        await photo.SaveAsync("photo.jpg");
    }

    public static async Task ALittleMore(CameraDevice camera, WebApplication app)
    {
        await using (camera.RecordTo("clip.mp4"))
            await Task.Delay(TimeSpan.FromSeconds(10));

        app.MapGet("/live.mp4", (CameraDevice camera, CancellationToken ct) =>
            Results.Stream(body => camera.RecordToAsync(body, new VideoOptions(), VideoContainer.Mp4, ct), "video/mp4"));

        await foreach (VideoFrame frame in camera.ReadFramesAsync(new FrameOptions()))
            using (frame)
                Analyse(frame.Plane(0), frame.Stride(0));

        var manual = new PhotoOptions
        {
            Controls = new CameraControls { Exposure = TimeSpan.FromMilliseconds(8), Gain = 2.0f },
        };
        Photo sharp = await camera.CapturePhotoAsync(manual);
    }

    static void Analyse(ReadOnlyMemory<byte> luma, int stride) { }
}
