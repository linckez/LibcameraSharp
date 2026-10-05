# Cameras

## Which cameras are there

```csharp
IReadOnlyList<CameraInfo> cameras = CameraDevice.Enumerate();
foreach (CameraInfo found in cameras)
    Console.WriteLine($"{found.Model}  {found.Id}  {found.Location}");

await using CameraDevice camera = CameraDevice.Open(id: cameras[1].Id);
```

`Open()` with no id takes the first camera. The id is stable across reboots for the same wiring, so
it is what to store in a settings file. Two cameras can be open at once in one process.

A camera belongs to one process at a time. `Open` throws `CameraBusyException` when another holds it —
often a camera app left running, or a service you forgot.

## Sensor modes

A sensor can read out in a few ways: full resolution, or binned to half size at a higher frame rate.

```csharp
IReadOnlyList<SensorMode> modes = await camera.ProbeSensorModesAsync();
SensorMode fast = modes.MaxBy(mode => mode.MaxFrameRate)!;

var options = new VideoOptions
{
    Streams = new StreamSettings { CaptureSize = new Size(1920, 1080), SensorMode = fast },
};
```

Without `SensorMode`, the camera picks the mode that best fits `CaptureSize`. Probing reconfigures the
camera once per mode, so the first call stops a running camera (a running recording refuses it) and
forgets controls set with `SetControls`; ask right after `Open`. The answer is kept.

`mode.CropLimits` shows how much of the sensor a mode sees (null when the camera has no crop control): a smaller rectangle is a narrower view,
which matters when a fast mode crops rather than bins.

## Which way up

```csharp
var flipped = new StreamSettings { Orientation = Orientation.Rotate180 };
```

The camera may combine this with how the module is mounted, so check the photo rather than assume. Many cameras, a
Raspberry Pi's included, can only flip: upside down, mirrored, or both. A quarter turn becomes one they can give;
`camera.Advanced.Orientation` shows which.
