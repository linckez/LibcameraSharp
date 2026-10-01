// PhotoBooth: the whole library in one sitting.
//
//   dotnet run --project demo/PhotoBooth               # first camera, files in ./photos
//   dotnet run --project demo/PhotoBooth -- 1 ~/shots  # camera number 1, another folder
//
// What it does, in order: lists the cameras libcamera sees; opens one and prints what it is and what
// it can do; watches a few frames; takes a full-resolution photo as JPEG and (on cameras with a raw
// stream) DNG; then takes a second JPEG with the exposure locked to what the first one used.

using LibcameraSharp;

var cameraNumber = args.Length > 0 ? int.Parse(args[0]) : 0;
var outDir = Path.GetFullPath(args.Length > 1 ? args[1] : "photos");
Directory.CreateDirectory(outDir);

LibcameraLog.SetLevel(LogLevel.Error);                        // libcamera's own chatter off from here on
Console.WriteLine($"libcamera {CameraManager.Version}");

// 1. Who's there.
var cameras = CameraDevice.Enumerate();
if (cameras.Count == 0)
{
    Console.Error.WriteLine("No cameras found. On a Raspberry Pi, check `rpicam-hello --list-cameras` first.");
    return 1;
}
for (var number = 0; number < cameras.Count; number++)
    Console.WriteLine($"  [{number}] {cameras[number].Model} — {cameras[number].Id}");

if (cameraNumber >= cameras.Count)
{
    Console.Error.WriteLine($"No camera number {cameraNumber}; there are {cameras.Count}.");
    return 1;
}

// 2. Open one and look at what it can do.
await using var camera = CameraDevice.Open(cameras[cameraNumber].Id);
Console.WriteLine($"\nopened {cameras[cameraNumber].Model}{(camera.Capabilities.IsMono ? " (monochrome)" : "")}");

foreach (ControlKey control in new ControlKey[] { Controls.ExposureTime, Controls.AnalogueGain, Controls.LensPosition })
{
    // Ask rather than assume: a camera ignores a control it doesn't support instead of throwing.
    var range = camera.Capabilities.Range(control);
    Console.WriteLine(range is { } r
        ? $"  {control.Name,-14} {r.Min} .. {r.Max}"
        : $"  {control.Name,-14} not offered by this camera");
}

// 3. Watch a few frames go by, with what the camera did for each.
Console.WriteLine("\nwatching frames...");
using var settling = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var watched = 0;
await foreach (var frame in camera.ReadFramesAsync(new FrameOptions(), settling.Token))
{
    using (frame)
        Console.WriteLine($"  #{frame.Sequence} {frame.Size} exposure {frame.Metadata.ExposureTime?.TotalMilliseconds.ToString("0.0 ms") ?? "unreported"}" +
                          $" gain {frame.Metadata.AnalogueGain?.ToString("0.00") ?? "unreported"}");
    if (++watched == 6)
        break;
}
if (camera.FramesDropped > 0)
    Console.WriteLine($"  ({camera.FramesDropped} frames dropped while printing — live view drops, recording does not)");

// 4. A full-resolution photo, with the sensor's own data alongside it if this camera has any.
var full = new PhotoOptions
{
    Streams = new StreamSettings { CaptureRaw = true },
    Exif = new ExifData { ImageDescription = "Taken by LibcameraSharp PhotoBooth" },
};

Console.WriteLine("\ntaking the photo...");
var photo = await camera.CapturePhotoAsync(full);

await photo.SaveAsync(Path.Combine(outDir, "photo.jpg"));
Console.WriteLine($"  {photo.Size} at {photo.Metadata.ExposureTime?.TotalMilliseconds.ToString("0.0 ms") ?? "unreported"}," +
                  $" gain {photo.Metadata.AnalogueGain?.ToString("0.00") ?? "unreported"}");

if (photo.Raw is { } raw)
{
    // A DNG carries the raw mosaic with everything a raw editor needs to develop it.
    raw.Save(Path.Combine(outDir, "photo.dng"));
    Console.WriteLine($"  raw {raw.Size} {raw.Format?.ToString() ?? "unknown format"}, {raw.Bytes.Length / 1024} KiB");
}
else
{
    Console.WriteLine("  (this camera has no raw stream, so no DNG)");
}

// 5. The same shot again, with exposure and gain pinned to what the camera just chose.
if (photo.Metadata.ExposureTime is { } exposure && photo.Metadata.AnalogueGain is { } gain)
{
    var locked = full with
    {
        Controls = new CameraControls { Exposure = ExposureMode.Fixed(exposure), Gain = GainMode.Fixed(gain) },
    };

    var second = await camera.CapturePhotoAsync(locked);
    await second.SaveAsync(Path.Combine(outDir, "photo-locked.jpg"));
    Console.WriteLine($"\nlocked to {exposure.TotalMilliseconds:0.0} ms / {gain:0.00}x," +
                      $" got {second.Metadata.ExposureTime?.TotalMilliseconds.ToString("0.0 ms") ?? "unreported"}" +
                      $" / {(second.Metadata.AnalogueGain?.ToString("0.00") is { } g ? g + "x" : "unreported")}");
}
else
{
    Console.WriteLine("\n(this camera reports no exposure or gain, so there is nothing to lock to)");
}

Console.WriteLine($"\nwritten to {outDir}");
return 0;
