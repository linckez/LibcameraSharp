namespace LibcameraSharp;

public partial class CameraDevice
{
    /// <summary>Takes a photograph with the camera's default photo settings.</summary>
    public virtual Task<Photo> CapturePhotoAsync(CancellationToken cancellationToken = default) =>
        CapturePhotoAsync(new PhotoOptions(), cancellationToken);

    /// <summary>
    /// Takes a photograph with these options. The same options twice cost nothing. On a camera with
    /// autofocus, it focuses first unless the options set <see cref="CameraControls.Focus"/>.
    /// </summary>
    /// <returns>The photo, with its pixels, what the camera did, and the raw image when asked for.</returns>
    /// <exception cref="InvalidOperationException">A recording is running with different options.</exception>
    public virtual async Task<Photo> CapturePhotoAsync(PhotoOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // One call that sets the camera up at a time, so two callers can't reconfigure it under each other.
        await _calls.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await TakePhotoAsync(options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _calls.Release();
        }
    }

    private async Task<Photo> TakePhotoAsync(PhotoOptions options, CancellationToken cancellationToken)
    {
        var controls = options.Controls;

        // Unless the options fix the focus, a camera that can focus scans first, in a viewfinder, and
        // the photo keeps the lens where the scan left it.
        if (CanFocus && controls.Focus is null or { Mode: AfMode.Auto, CancelsScan: false })
        {
            await FocusAsync(options.Streams, controls, cancellationToken).ConfigureAwait(false);
            controls = controls with { Focus = FocusMode.KeepScanned };
        }

        // A YUV420 photo is taken in YUV420, so the file holds what the camera produced.
        var streams = options.Encoding == PhotoEncoding.Yuv420 && options.Streams.CaptureFormat is null
            ? options.Streams with { CaptureFormat = PixelFormats.YUV420 }
            : options.Streams;

        ApplyOptions(streams, controls, CameraUse.Photo);
        if (!Session.Started)
            Session.Start();

        // A frame taken with these controls, not one already in flight when they were sent.
        var target = Session.TakeControlsTarget();
        using var frame = await Session.CaptureRequestWithControlsAsync(target, cancellationToken).ConfigureAwait(false);

        var pixels = frame.CopyPixels();
        var metadata = frame.Metadata;
        // The model libcamera reports, or the camera's id when it reports none.
        var model = Session.CameraProperties.TryGet(Properties.Model, out var name) && name.Length > 0 ? name : Session.CameraId;

        RawImage? raw = null;
        if (frame.Streams.ContainsKey(SessionStream.Raw))
        {
            var rawStream = frame.Config[SessionStream.Raw]!;
            raw = new RawImage(frame.MakeBuffer(SessionStream.Raw), metadata, rawStream, model,
                rawStream.Format is { } format ? BayerFormat.FromPixelFormat(format) : null,
                rawStream.Size ?? default);
        }

        return new Photo(pixels, metadata, model, raw, options);
    }

    // Scans in a viewfinder shaped like the photo: waits for the scan to start (16 frames at most),
    // then for it to end, however long that takes. A scan that fails still ends the wait.
    private async Task FocusAsync(StreamSettings photo, CameraControls controls, CancellationToken cancellationToken)
    {
        const int FramesToStart = 16;

        ApplyOptions(new StreamSettings { CaptureSize = ViewfinderSize(photo.CaptureSize) }, controls with { Focus = FocusMode.Auto }, CameraUse.Frames);
        if (!Session.Started)
            Session.Start();
        var target = Session.TakeControlsTarget();

        var started = false;
        for (var frames = 0; ; frames++)
        {
            using var frame = await Session.CaptureRequestWithControlsAsync(target, cancellationToken).ConfigureAwait(false);
            var scanning = frame.Metadata.TryGet(Controls.AfState, out var state) && state == AfState.Scanning;
            if (scanning)
                started = true;
            else if (started || frames >= FramesToStart)
                return;
        }
    }

    // Half the sensor's active area, which most sensors bin to; with a photo size, the same field of
    // view as the photo. 1280×960 when the camera reports no active area.
    private Size ViewfinderSize(Size? photoSize)
    {
        if (!Session.CameraProperties.TryGet(Properties.PixelArrayActiveAreas, out var areas) || areas.Length == 0)
            return new Size(1280, 960);

        var size = new Size(areas[0].Width / 2, areas[0].Height / 2);
        if (photoSize is { Width: > 0, Height: > 0 } ratio)
            size = BoundedToAspectRatio(size, ratio);
        return new Size(size.Width & ~1u, size.Height & ~1u);
    }

    // The largest size within size that has ratio's aspect ratio.
    private static Size BoundedToAspectRatio(Size size, Size ratio)
    {
        var ratio1 = (ulong)size.Width * ratio.Height;
        var ratio2 = (ulong)ratio.Width * size.Height;
        return ratio1 > ratio2
            ? new Size((uint)(ratio2 / ratio.Height), size.Height)
            : new Size(size.Width, (uint)(ratio1 / ratio.Width));
    }
}
