# Controls

What the camera does while it takes the picture: exposure, gain, white balance, focus, zoom, frame rate.

```csharp
var manual = new CameraControls
{
    Exposure     = ExposureMode.Fixed(TimeSpan.FromMilliseconds(8)),
    Gain         = GainMode.Fixed(2.0f),
    WhiteBalance = WhiteBalance.Manual(redGain: 1.8f, blueGain: 1.4f),
    Focus        = FocusMode.AtMetres(0.5),
    Zoom         = new RegionOfInterest(0.25, 0.25, 0.5, 0.5),   // x, y, width, height, as fractions
};

Photo photo = await camera.CapturePhotoAsync(new PhotoOptions { Controls = manual });
Console.WriteLine($"got {photo.Metadata.ExposureTime?.TotalMilliseconds} ms at {photo.Metadata.AnalogueGain}x");
```

## Only what you set is sent

| | Means |
|---|---|
| `Exposure = null` (not set) | leave it as the camera has it |
| `Exposure = ExposureMode.Auto`, `Gain = GainMode.Auto` | back to automatic |
| `AutoExposure = false` | hold exposure and gain where automatic exposure left them (and `AutoWhiteBalance = false` the colour gains), so frames taken once it settles all match |

A manual exposure stays in the sensor until something changes it: a second photo whose options don't
mention exposure keeps the first one's.

## When they take effect

A sensor applies new controls two or three frames after it receives them. Every call that takes
options — `CapturePhotoAsync`, `StartRecordingAsync`, `ReadFramesAsync` — waits for the first frame taken with
them, so the photo you get is the photo you asked for. Controls that haven't changed aren't sent again. A fixed
value, such as an 8 ms exposure, is in effect on that frame; an automatic mode (auto exposure, auto
white balance, autofocus) starts from it and may still be settling. Calls that set the camera up take
turns: a second one waits until the first has its frame.

## Changing them while something runs

```csharp
camera.SetControls(new CameraControls { Exposure = ExposureMode.Fixed(TimeSpan.FromMilliseconds(12)) });
```

For a slider over a live stream or a running recording. It returns at once, and the frames that
follow change a few frames later.

## Checking values before you keep them

```csharp
controls.ThrowIfInvalid();
```

Throws `ArgumentOutOfRangeException` for a value no camera can take: a frame rate that isn't above 0, or
a zoom or focus window that doesn't lie inside the picture. Every call that takes controls checks this
anyway; call it when you save controls for later, so a bad value is refused then rather than failing
every photo after.

## What this camera can do

```csharp
if (camera.Capabilities.Exposure is { } exposure)
    Console.WriteLine($"exposure {exposure.Min.TotalMilliseconds}–{exposure.Max.TotalMilliseconds} ms");
```

`Exposure`, `Gain`, `Focus` (dioptres), `FrameRate` (frames a second), `Brightness`, `Contrast`,
`Saturation`, `Sharpness` and `ExposureValue` give each setting's range in the units `CameraControls` takes
it in. `Range(Controls.AnalogueGain)` gives any libcamera control's, in libcamera's own units.

Ask before you rely on a control. A setting the camera doesn't have is skipped, with one warning on
standard error, rather than throwing; ranges depend on the sensor mode, so they are only meaningful once a call has set the camera up.
`camera.Capabilities.Supports(Controls.AfMode)` and `.Controls` list what it offers; `.IsMono` says whether it
sees colour at all.

## Units

| Property | Unit |
|---|---|
| `Exposure` | `ExposureMode.Fixed(TimeSpan)`, sent in whole microseconds, or `ExposureMode.Auto` |
| `Gain` | `GainMode.Fixed(2.0f)`: analogue gain, 1.0 and up, the maximum in the camera's tuning, not a fixed number; or `GainMode.Auto` |
| `FrameRate` | a number (`30`) or a range (`(5, 30)`), above zero. A fixed rate caps exposure — 30 fps allows at most 33 ms — so give a range for low light |
| `Focus` | `FocusMode.AtMetres(0.5)`, `FocusMode.Infinity`, or `Auto` / `Continuous`. Left unset on a camera with autofocus, a photo focuses first and video and frames focus continuously |
| `Zoom`, `AutofocusWindows` | fractions of the full sensor, 0.0 to 1.0. An empty `AutofocusWindows` list lets autofocus choose where to measure again |
| `Flicker` | `FlickerMode.Manual(period)` with how fast the room's lights pulse, so automatic exposure picks times that avoid dark bands across the picture: 10 ms where mains power is 50 Hz, 8.33 ms where it is 60 Hz; `FlickerMode.Off` turns it off |

There are more: `Brightness`, `Contrast`, `Saturation`, `Sharpness`, `ExposureValue`, `Metering`,
`Denoise`, `Hdr`, `AutofocusRange`, `AutofocusSpeed` and the rest are on `CameraControls`, each
documented where you type it. A control `CameraControls` doesn't cover needs
[LibcameraSharp.Core](core.md).
