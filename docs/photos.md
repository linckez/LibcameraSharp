# Photos

```csharp
await using CameraDevice camera = CameraDevice.Open();

Photo photo = await camera.CapturePhotoAsync();
await photo.SaveAsync("photo.jpg");
Console.WriteLine($"{photo.Size}, {photo.Metadata.ExposureTime?.TotalMilliseconds} ms at {photo.Metadata.AnalogueGain}x");
```

A `Photo` is a copy of the picture, so keep it as long as you like; the camera has moved on.

On a camera with autofocus, each photo focuses first, then shoots — even if focus fails, as a phone
does. To skip the scan, give the photo a focus of your own, such as
`FocusMode.AtDioptres(photo.Metadata.LensPosition!.Value)` to keep the last one.

To focus once and keep it, as a focus button does, run the same scan on its own and see how it ended:

```csharp
FocusResult focus = await camera.FocusAsync(options, ct);
if (focus.IsFocused)
    options = options with { Controls = options.Controls with { Focus = FocusMode.AtDioptres(focus.Metadata!.LensPosition!.Value) } };
```

A camera without autofocus has nothing to scan: you get a warning and a result that isn't focused.

## Encodings

`PhotoOptions.Encoding` decides how the photo is written, and it is chosen before the photo is taken:
it decides what the camera delivers.

```csharp
Photo photo = await camera.CapturePhotoAsync(new PhotoOptions { Encoding = PhotoEncoding.Png }, ct);
await photo.SaveAsync("bench.png", ct);          // the name is used exactly as given
await photo.SaveAsync(response.Body, ct);        // or any stream
```

The file name never changes the encoding, and nothing is added to it: a photo taken as PNG and saved
as `bench.jpg` is a PNG called `bench.jpg`. A photo is only ever written in its own encoding; for a
second encoding, take a second photo.

| Encoding | What you get |
|---|---|
| `Jpeg` | the default, with EXIF; quality from `PhotoOptions.JpegQuality` (90 when not set) |
| `Png` | lossless, bigger, slower to write |
| `Bmp` | uncompressed 24-bit |
| `Rgb`, `Yuv420` | bare pixels with no file header, for your own code or `ffmpeg -f rawvideo` |

## EXIF

A JPEG carries what the camera did: exposure time, ISO, the focus distance when the lens reports one, and
when the photo was saved. Add descriptive tags of your own, and where it was taken; `Make`, `Model` and `Software`
replace the generated ones:

```csharp
var tagged = new PhotoOptions
{
    Exif = new ExifData
    {
        Artist   = "A. Rossi", Copyright = "CC-BY", Model = "Garden cam",
        Location = new GpsLocation(55.6761, 12.5683, altitude: 12.3),   // degrees north and east, metres above sea level
    },
};
```

## Size, and the rest of the setup

```csharp
var small = new PhotoOptions { Streams = new StreamSettings { CaptureSize = new Size(1920, 1080) } };
```

The camera may adjust a size it cannot produce exactly; `photo.Size` is what it made. Changing
`Streams` between calls reconfigures the camera, which takes a moment, so keep one options record per
kind of shot rather than building a new one every time.

## Raw and DNG

A normal photo has been *developed*: debayered, white-balanced, colour-corrected. Raw is what the
sensor measured before any of that — one colour per pixel in a mosaic, usually 10 or 12 bits, packed.
DNG is the file raw is saved in, so a raw editor can develop it: it carries the black and white
levels, the colour matrix and the white balance the camera chose.

```csharp
var withRaw = new PhotoOptions { Streams = new StreamSettings { CaptureRaw = true } };

Photo shot = await camera.CapturePhotoAsync(withRaw);
await shot.SaveAsync("shot.jpg");                             // the developed picture
if (shot.Raw is { } raw)
    raw.Save("shot.dng");                                     // opens in Lightroom, darktable, RawTherapee
```

`raw.Bytes` is the same data in process, still packed; `raw.Format` says how. Cameras without a raw
stream, such as USB webcams, leave `Raw` null.

## Metadata

`photo.Metadata` is what the camera did for that frame: exposure, gain, lens position, colour
temperature, the colour gains, lux. A value is null when the camera doesn't report it.
`photo.Metadata.CapturedOn` is the date and time the frame was captured, in UTC, from the system clock. `Timestamp` is
the same moment as time since the system started; setting the clock doesn't change it, so use it to measure the time
between frames.
`photo.Metadata.All` has everything, by libcamera's control name.
