# Video

```csharp
await using CameraDevice camera = CameraDevice.Open();

VideoRecording recording = await camera.StartRecordingAsync("clip.mp4");
await Task.Delay(TimeSpan.FromSeconds(10));
await recording.StopAsync();
Console.WriteLine($"{recording.FrameCount} frames");
```

Recording runs until you stop it. Stopping writes the end of the file, so a recording that is never
stopped is a file that will not play. If writing failed along the way — a full disk, say — `StopAsync`
throws that failure as an `IOException`. Disposing a recording stops it too, but never throws.

## Options

```csharp
var clip = new VideoOptions
{
    Streams  = new StreamSettings { CaptureSize = new Size(1920, 1080) },
    Controls = new CameraControls { FrameRate = 30 },
    Quality  = Quality.High,
};

VideoRecording recording = await camera.StartRecordingAsync("clip.mp4", clip);
```

The default is H.264 at 1280×720, focusing continuously on a camera with autofocus. `Quality` trades
file size against picture quality; `KeyframeInterval` sets how many frames pass between full pictures,
which is where a player can start or seek (30 by default).

## Files and containers

The extension picks the container: `.mp4`, `.mkv` or `.ts`. Anything else gets the encoder's own
bytes with no container around them, which `ffmpeg` and VLC read and most players don't.

| Codec | For | Cost |
|---|---|---|
| `VideoCodec.H264` | recordings, and anything going over a network | a Pi 4 encodes in hardware, up to 1080p30; elsewhere, including a Pi 5, it takes about 1½ cores for 1080p30 |
| `VideoCodec.Mjpeg` | live viewing: every frame is a whole JPEG, so a viewer can join at any moment | about three times the size of H.264 |

H.264 and the containers use the FFmpeg libraries installed on the machine; Raspberry Pi OS has them.
Elsewhere, `sudo apt install ffmpeg`.

## Streaming to a browser

`RecordToAsync` writes to any `Stream` until its token is cancelled. In ASP.NET that is the request
itself, and the token fires when the viewer closes the tab:

```csharp
builder.Services.AddSingleton(_ => CameraDevice.Open());     // DI owns the camera and disposes it

var live = new VideoOptions { Streams = new StreamSettings { CaptureSize = new Size(1280, 720) } };

app.MapGet("/live.mp4", (CameraDevice camera, CancellationToken ct) =>
    Results.Stream(body => camera.RecordToAsync(body, live, VideoContainer.Mp4, ct), "video/mp4"));
```

A stream can't seek back to finish an MP4 the usual way, so it gets a fragmented MP4 that plays as it
arrives. `VideoContainer.MpegTs` is the other common choice for a live feed. The HTTP side — routes,
multipart framing, authentication — is yours; the SDK hands over bytes and stops there. The
[StreamingCamera demo](../demo/StreamingCamera/) is a whole webcam in one file.

## Nothing is dropped

A recording never drops a frame: a destination slower than the camera slows the camera. Dropping an
H.264 frame corrupts the frames after it, so thinning is not an option. For live pixels where only the
newest frame matters, use [frames](frames.md) instead.

## Controls

Controls given in the options are in effect from the first frame of the recording. To change them
while it runs, use `camera.SetControls` — see [Controls](controls.md).
