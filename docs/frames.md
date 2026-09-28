# Frames

Pixels into your own code — motion detection, a model, a custom encoder — with no file anywhere.

```csharp
var watching = new FrameOptions
{
    Streams = new StreamSettings { PreviewSize = new Size(640, 480), PreviewFormat = PixelFormats.YUV420 },
};

await foreach (VideoFrame frame in camera.ReadFramesAsync(watching, ct))
    using (frame)
    {
        ReadOnlyMemory<byte> luma = frame.Plane(0);
        await DetectAsync(luma, frame.Size, frame.Stride(0), ct);
    }
```

The loop runs until you `break`, or until `ct` is cancelled, which ends it with an
`OperationCanceledException`.

## Dispose every frame

A frame is the camera's own buffer, not a copy — that is what makes it cheap. The camera has only a
few, so a frame you keep is one it can't fill. `using (frame)` in the loop body; call
`frame.ToArray()` for bytes that must outlive it.

`frame.Plane(i)` survives an `await`, so you can hand it to asynchronous code, as long as the frame
isn't disposed yet.

## The newest frame wins

If your code is slower than the camera, the loop gets the newest frame and older ones are dropped.
`camera.FramesDropped` counts them. That is the right trade for live analysis; a [recording](video.md)
never drops.

## Formats and planes

| Format | Planes | Good for |
|---|---|---|
| `YUV420` | Y, then quarter-size U and V | analysis: plane 0 is a greyscale image; encoders |
| `NV12` | Y, then interleaved U and V | the same, as many ISPs produce it |
| `BGR888` | one, R G B per pixel | anything that wants colour |
| `XRGB8888` | one, B G R and padding per pixel | GPU and display code |

**Rows can be padded.** A row of plane `i` is `frame.Stride(i)` bytes, which may be more than the
width; reading it as `width` bytes shears the image.

**Names read backwards.** libcamera names describe a pixel as a little-endian number, so `BGR888` is
stored R, G, B in memory and `RGB888` is stored B, G, R. If colours come out swapped, this is why.

## Two streams

`CaptureSize` and `PreviewSize` give two streams from one sensor. `ReadFramesAsync` reads the preview
when there is one, so your code gets small frames while the capture stream keeps its own size.

## Metadata

`frame.Metadata` is what the camera did for that frame: exposure, gain, lux, the timestamp.
`frame.Sequence` counts frames; a gap means frames the loop never saw.
