# Getting started

## Install

```bash
dotnet add package LibcameraSharp
```

It runs on 64-bit Raspberry Pi OS, which already has libcamera. Check that the camera works first:
see [Raspberry Pi setup](raspberry-pi.md).

## First photo

```csharp
using LibcameraSharp;

using CameraDevice camera = CameraDevice.Open();            // the first camera

Photo photo = await camera.CapturePhotoAsync();
await photo.SaveAsync("photo.jpg");                          // .png and .bmp work the same way
```

The first call sets the camera up and starts it; the next photo reuses that. By default the picture
is the sensor's full resolution.

## Say what you want

Options are records you declare once and pass to each call. Passing the same options again doesn't
set the camera up again:

```csharp
static readonly PhotoOptions Night = new()
{
    Streams  = new StreamSettings { CaptureSize = new Size(2028, 1520) },
    Controls = new CameraControls { Exposure = TimeSpan.FromMilliseconds(80), Gain = 8.0f },
    JpegQuality = 95,
};

Photo dark = await camera.CapturePhotoAsync(Night);
```

Each options record has two parts, which take effect differently:

| | What | When it takes effect |
|---|---|---|
| `Streams` | sizes, formats, raw, orientation, sensor mode | the camera is reconfigured, if they changed |
| `Controls` | exposure, gain, white balance, focus, zoom, frame rate | a few frames later; the call waits for them |

Everything else — encoding, JPEG quality, EXIF — only shapes the file.

## Where next

| To… | Read |
|---|---|
| save JPEG, PNG or DNG, add EXIF | [Photos](photos.md) |
| record MP4, or stream video to a browser | [Video](video.md) |
| get pixels into your own code | [Frames](frames.md) |
| set exposure, gain, focus, zoom | [Controls](controls.md) |
| pick a camera or sensor mode | [Cameras](cameras.md) |
| do everything yourself with libcamera | [LibcameraSharp.Core](core.md) |

## Quieting libcamera

libcamera logs to stderr. In a service:

```csharp
LibcameraLog.SetLevel(LogLevel.Error);         // errors only, from here on
```

## Cleaning up

Dispose the `CameraDevice` to release the camera for other processes. Only one process can hold a
camera at a time; a second gets `CameraBusyException`.

## Testing without a camera

On a laptop or a CI runner there is no camera. Derive from `CameraDevice` (or mock it) and override what your
code calls; `LibcameraSharpModelFactory` builds the photos, frames and capabilities to return. A working fake
camera and a Moq example are in [`tests/LibcameraSharp.Scenarios/Testing.cs`](../tests/LibcameraSharp.Scenarios/Testing.cs).
