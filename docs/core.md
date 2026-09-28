# LibcameraSharp.Core

libcamera for .NET, for when you want to build the capture pipeline yourself.

```bash
dotnet add package LibcameraSharp.Core
```

`LibcameraSharp` is built on it and brings it along; add Core on its own when you don't want photos
and video, or their SkiaSharp and FFmpeg dependencies. It reads like libcamera's C++ API, because it
is that API: cameras, configurations, frame buffers, requests and every control, typed.

```csharp
using LibcameraSharp;
using LibcameraSharp.Advanced;

using var manager = new CameraManager();                          // one per process
using var camera = manager.Cameras[0].Acquire();                  // exclusive access

// 1. Configuration: one stream per role; adjust, validate, apply.
using var config = camera.GenerateConfiguration(StreamRole.StillCapture)!;
config[0].PixelFormat = PixelFormats.BGR888;
config[0].Size = new Size(1920, 1080);
config.Validate();                                                // may adjust what you asked for
camera.Configure(config);

// 2. Buffers, from the camera's own memory.
using var allocator = new FrameBufferAllocator(camera);
var stream = config[0].Stream;
var buffers = allocator.Allocate(stream);

// 3. Requests: one buffer per stream, controls per request.
var requests = buffers.Select((buffer, i) =>
{
    var request = camera.CreateRequest(cookie: (ulong)i);
    request.AddBuffer(stream, buffer);
    request.Controls.Set(Controls.ExposureTimeMode, ExposureTimeMode.Manual);
    request.Controls.Set(Controls.ExposureTime, 20_000);          // µs
    return request;
}).ToList();

camera.Start();
foreach (var request in requests)
    camera.QueueRequest(request);

// 4. Completed requests arrive in order.
var done = await camera.CompletedRequests.ReadAsync(ct);
using (var mapped = done.Buffer(stream).Map())
    Process(mapped[0], config[0].Stride);                         // the plane, not copied
Console.WriteLine(done.Metadata.Get(Controls.ExposureTime));

// 5. Reuse a request for the next frame.
done.Reuse();
camera.QueueRequest(done);

camera.Stop();
```

## From a CameraDevice

`camera.Advanced` on a `CameraDevice` is its `ActiveCamera`: its properties, the controls it
advertises with their ranges, and its configuration.

```csharp
string model = device.Advanced.Properties.Get(Properties.Model);
```

## Rules libcamera enforces

The binding turns each into an exception carrying libcamera's errno:

- A completed request is refused (`EINVAL`) until `Reuse()` resets it; reuse clears its controls.
- `Configure` and `Start` are refused (`EACCES`) while the camera is running.
- `Acquire` is refused while anyone holds the camera: `CameraBusyException`.
- `Stop()` hands every queued request back as `RequestStatus.Cancelled`.
- Queue enough requests for the pipeline to start streaming; usually all of them.

**Exposure and gain need their mode.** `ExposureTime` and `AnalogueGain` only take effect when
`ExposureTimeMode` / `AnalogueGainMode` is `Manual`.

## Controls, properties and formats

Every control is a typed key generated from libcamera's own definitions: `Controls.ExposureTime` is a
`Control<int>`, `Controls.AwbMode` a `Control<AwbMode>`. Vendor controls are nested:
`Controls.Rpi.*`, `Controls.Draft.*`. `Properties` hold fixed facts, such as the model and pixel array
size, and `PixelFormats` every format libcamera defines.

```csharp
foreach (var supported in camera.Controls)
    Console.WriteLine(supported.Key.Name);
```

## Logging

```csharp
LibcameraLog.SetTarget(LogTarget.None);        // before the manager: silences the startup banner
using var manager = new CameraManager();
LibcameraLog.SetLevel(LogLevel.Error);         // after: errors only from here on
```

## Events

`ActiveCamera.RequestCompleted` fires on libcamera's thread before a request reaches
`CompletedRequests`, for the lowest latency. Keep the handler short, and don't throw from it.
