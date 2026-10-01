<div align="center">

![LibcameraSharp logo](https://raw.githubusercontent.com/linckez/LibcameraSharp/main/src/icon.png)

# LibcameraSharp

Your Raspberry Pi camera, from C#.

Photos, video and live frames in a few lines of .NET.

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://opensource.org/licenses/MIT)

</div>

Photos · RAW to DNG · H.264 & MJPEG · Live frames · Typed camera controls

---

## ⭐ Quick Start

```bash
dotnet add package LibcameraSharp
```

```csharp
using LibcameraSharp;

await using CameraDevice camera = CameraDevice.Open();

Photo photo = await camera.CapturePhotoAsync();
await photo.SaveAsync("photo.jpg");
```

A full-resolution JPEG with EXIF. Cameras with autofocus focus first.

---

## 🎬 A little more

**Record a video:**

```csharp
VideoRecording recording = await camera.StartRecordingAsync("clip.mp4");
await Task.Delay(TimeSpan.FromSeconds(10));
await recording.StopAsync();
```

**Stream to a browser** (ASP.NET Core):

```csharp
app.MapGet("/live.mp4", (CameraDevice camera, CancellationToken ct) =>
    Results.Stream(body => camera.RecordToAsync(body, new VideoOptions(), VideoContainer.Mp4, ct), "video/mp4"));
```

**Read frames** for motion detection or a model:

```csharp
await foreach (VideoFrame frame in camera.ReadFramesAsync(new FrameOptions()))
    using (frame)
        Analyse(frame.Plane(0), frame.Stride(0));
```

**Take control** of exposure, gain, focus and more:

```csharp
var manual = new PhotoOptions
{
    Controls = new CameraControls { Exposure = ExposureMode.Fixed(TimeSpan.FromMilliseconds(8)), Gain = GainMode.Fixed(2.0f) },
};
Photo sharp = await camera.CapturePhotoAsync(manual);
```

---

## 📦 Packages

| Package | |
|---------|---|
| **LibcameraSharp** | Photos, video and frames. Start here |
| **LibcameraSharp.Core** | libcamera itself, for building your own pipeline. Included in the one above |

Runs on .NET 10 and 64-bit Raspberry Pi OS (tested on a Pi 4), with any camera libcamera supports.

---

## 📚 Documentation

| | |
|---|---|
| [Getting started](docs/getting-started.md) | Install, first photo, options |
| [Photos](docs/photos.md) | JPEG, PNG, RAW and EXIF |
| [Video](docs/video.md) | Recording and streaming |
| [Frames](docs/frames.md) | Pixels for your own code |
| [Controls](docs/controls.md) | Exposure, gain, focus, zoom |
| [Cameras](docs/cameras.md) | Several cameras, sensor modes |
| [Raspberry Pi setup](docs/raspberry-pi.md) | Checking the camera, deploying, troubleshooting |
| [LibcameraSharp.Core](docs/core.md) | libcamera, directly |
| [Demos](demo/) | A photo booth and a browser webcam |

---

## FAQ

**Only Raspberry Pi?**
No. Any camera libcamera drives on Linux, USB webcams included. The Pi is just where libcamera is most at home.

**Is video encoded in hardware?**
On a Pi 4, yes, up to 1080p30. A Pi 5 encodes in software.

**What does it need installed?**
Raspberry Pi OS has everything: libcamera, FFmpeg, libtiff and libexif.

---

## Credits

Built on [libcamera](https://libcamera.org), [FFmpeg](https://ffmpeg.org), [SkiaSharp](https://github.com/mono/SkiaSharp),
[libtiff](https://libtiff.gitlab.io/libtiff/) and [libexif](https://github.com/libexif/libexif). The C shim comes from
[libcamera-rs](https://github.com/lit-robotics/libcamera-rs). Much of the design, and the reference output the tests compare
against, comes from [picamera2](https://github.com/raspberrypi/picamera2), [rpicam-apps](https://github.com/raspberrypi/rpicam-apps)
and [PiDNG](https://github.com/schoolpost/PiDNG).

## License

[MIT](LICENSE)
