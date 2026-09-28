// StreamingCamera: the Raspberry Pi webcam, in one file.
//
//   dotnet run --project demo/StreamingCamera            # then open http://<pi>:5000
//
// GET /             an HTML page showing the live stream
// GET /live.mp4     the camera as fragmented MP4, which plays in a browser and in VLC
// GET /stream.mjpg  the same picture as multipart MJPEG, for motionEye and Home Assistant
//
// Note what the SDK does and does not do here. It hands over container bytes and JPEG frames; the
// HTTP verbs, the multipart framing and the hosting are this file's business, not the library's.

using LibcameraSharp;
using Microsoft.AspNetCore.Http.Features;
using StreamingCamera;

var builder = WebApplication.CreateBuilder(args);

// Registered as a factory, so the container owns the camera and disposes it on shutdown.
builder.Services.AddSingleton(_ => CameraDevice.Open());

var app = builder.Build();

// One set of options for every route, so the camera is configured once. The frame rate is only
// asked for when the camera offers it: a camera ignores controls it doesn't support.
var camera = app.Services.GetRequiredService<CameraDevice>();

var live = new VideoOptions
{
    Streams = new StreamSettings { CaptureSize = new Size(1280, 720) },
    Controls = camera.Capabilities.Supports(Controls.FrameDurationLimits)
        ? new CameraControls { FrameRate = 30 }
        : new CameraControls(),
    Codec = VideoCodec.H264,
};

app.MapGet("/", () => Results.Content("""
    <!doctype html>
    <html><head><title>LibcameraSharp</title>
    <style>body{font-family:system-ui;margin:2rem;background:#111;color:#eee}video,img{max-width:100%;border-radius:8px}</style>
    </head><body>
    <h1>LibcameraSharp</h1>
    <video src="/live.mp4" autoplay muted playsinline></video>
    <p><a href="/stream.mjpg" style="color:#8cf">MJPEG instead</a></p>
    </body></html>
    """, "text/html"));

// Fragmented MP4, pushed. The request's cancellation is the viewer closing the tab, which stops the
// recording and closes the muxer.
app.MapGet("/live.mp4", (CameraDevice camera, CancellationToken cancellationToken) =>
    Results.Stream(body => camera.RecordToAsync(body, live, VideoContainer.Mp4, cancellationToken), "video/mp4"));

// multipart/x-mixed-replace is the oldest trick on the web and still the one every browser renders.
// The framing is HTTP's convention, not video's, so it lives here rather than in the library.
app.MapGet("/stream.mjpg", async (CameraDevice camera, HttpContext context, CancellationToken cancellationToken) =>
{
    const string boundary = "frame";
    context.Response.Headers.CacheControl = "no-cache, private";
    context.Response.ContentType = $"multipart/x-mixed-replace; boundary={boundary}";

    // Frames are written synchronously, as they are encoded, and Kestrel forbids synchronous writes
    // by default, so this endpoint opts in. The MP4 route above does not need this because the
    // muxer buffers.
    if (context.Features.Get<IHttpBodyControlFeature>() is { } bodyControl)
        bodyControl.AllowSynchronousIO = true;

    var mjpeg = live with { Codec = VideoCodec.Mjpeg };
    await camera.RecordToAsync(new MultipartStream(context.Response.Body, boundary), mjpeg,
        VideoContainer.None, cancellationToken);
});

app.Run();
