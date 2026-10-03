namespace LibcameraSharp;

public partial class CameraDevice
{
    /// <summary>
    /// Takes a photograph with these options. The same options twice don't reconfigure the camera, except on a
    /// camera with autofocus: unless the options set <see cref="CameraControls.Focus"/>, each photo first scans in a
    /// viewfinder setup, then reconfigures for the photo.
    /// </summary>
    /// <returns>The photo, with its pixels, what the camera did, and the raw image when asked for.</returns>
    /// <exception cref="InvalidOperationException">A recording is running: a photo would reconfigure the camera under it.</exception>
    /// <param name="options">How to take the photo; the camera's default photo settings when null. An override gets null when the caller passed none.</param>
    /// <param name="cancellationToken">Cancels the capture.</param>
    /// <exception cref="ArgumentOutOfRangeException">A frame rate or region in the options is out of range.</exception>
    public virtual Task<Photo> CapturePhotoAsync(PhotoOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new PhotoOptions();
        options.Controls.ThrowIfInvalid(nameof(options));

        // A photo is one job, from its focus scan to its frame, so no other call's setup can land in between.
        return RunExclusiveAsync(() => TakePhotoAsync(options, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Runs one autofocus scan for a photo with these options, the scan <see cref="CapturePhotoAsync"/> runs before a
    /// photo whose options leave <see cref="CameraControls.Focus"/> unset, and reports how it ended.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scan runs in a viewfinder setup shaped like the photo, so the next photo reconfigures. To take photos at the
    /// focus it found, pass its lens position, <c>FocusMode.AtDioptres(result.Metadata!.LensPosition!.Value)</c>; a photo
    /// with <c>Focus</c> unset scans again.
    /// </para>
    /// <para>The scan is always automatic: the options' <see cref="CameraControls.Focus"/> is ignored.</para>
    /// <para>
    /// On a camera without autofocus there is nothing to scan: a warning (once), and a result that isn't focused. Check
    /// <c>Capabilities.Supports(Controls.AfMode)</c> to know beforehand.
    /// </para>
    /// </remarks>
    /// <param name="options">The photo to focus for; the default photo settings when null.</param>
    /// <param name="cancellationToken">Cancels the wait for the scan.</param>
    /// <returns>Whether the scan ended focused, and the frame it ended on.</returns>
    /// <exception cref="InvalidOperationException">
    /// On a camera with autofocus, a recording or another frame loop holds the camera with other options.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A frame rate or region in the options is out of range.</exception>
    /// <exception cref="ObjectDisposedException">The camera has been disposed.</exception>
    public virtual Task<FocusResult> FocusAsync(PhotoOptions? options = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new PhotoOptions();
        options.Controls.ThrowIfInvalid(nameof(options));
        if (!Session.CanFocus)
        {
            if (Interlocked.Exchange(ref _warnedNoAutofocus, 1) == 0)
                Console.Error.WriteLine("LibcameraSharp: this camera has no autofocus, so there is nothing to scan.");
            return Task.FromResult(new FocusResult(isFocused: false, metadata: null));
        }
        return RunExclusiveAsync(() => FocusAsync(options.Streams, options.Controls, cancellationToken), cancellationToken);
    }

    private int _warnedNoAutofocus;

    private async Task<Photo> TakePhotoAsync(PhotoOptions options, CancellationToken cancellationToken)
    {
        var controls = options.Controls;

        // Unless the options fix the focus, a camera that can focus scans first, in a viewfinder, and
        // the photo keeps the lens where the scan left it.
        if (Session.CanFocus && controls.Focus is null or { Mode: AfMode.Auto, CancelsScan: false })
        {
            await FocusAsync(options.Streams, controls, cancellationToken).ConfigureAwait(false);
            controls = controls with { Focus = FocusMode.KeepScanned };
        }

        // A YUV420 photo is taken in YUV420, so the file holds what the camera produced.
        var streams = options.Encoding == PhotoEncoding.Yuv420 && options.Streams.CaptureFormat is null
            ? options.Streams with { CaptureFormat = PixelFormats.YUV420 }
            : options.Streams;

        // A frame taken with these controls, not one already in flight when they were sent. It's copied here, off
        // the camera's loop, so a full-sensor copy never holds up other streams.
        var setup = await Session.SetUpAsync(streams, controls, CameraUse.Photo).ConfigureAwait(false);
        using var frame = await Session.NextFrameAsync(setup.Target, cancellationToken).ConfigureAwait(false);

        var pixels = frame.CopyPixels();
        var metadata = frame.Metadata;
        var model = Session.Facts.Model;

        RawImage? raw = null;
        if (frame.Streams.ContainsKey(SessionStream.Raw))
        {
            var rawStream = frame.Config[SessionStream.Raw]!;
            raw = new RawImage(frame.MakeBuffer(SessionStream.Raw), metadata, rawStream, model,
                rawStream.Format is { } format ? BayerFormat.FromPixelFormat(format) : null,
                rawStream.Size!.Value);                                         // set once configured
        }

        return new Photo(pixels, metadata, model, raw, options);
    }

    // Scans in a viewfinder shaped like the photo: waits for the scan to start (16 frames at most),
    // then for it to end, however long that takes. A scan that fails still ends the wait.
    private async Task<FocusResult> FocusAsync(StreamSettings photo, CameraControls controls, CancellationToken cancellationToken)
    {
        const int FramesToStart = 16;

        var setup = await Session.SetUpAsync(new StreamSettings { CaptureSize = ViewfinderSize(photo.CaptureSize) },
            controls with { Focus = FocusMode.Auto }, CameraUse.Frames).ConfigureAwait(false);
        using var ended = await Session.WaitForFocusScanAsync(setup.Target, FramesToStart, cancellationToken).ConfigureAwait(false);
        var focused = ended.Metadata.TryGet(Controls.AfState, out var state) && state == AfState.Focused;
        return new FocusResult(focused, new CaptureMetadata(ended.Metadata));
    }

    // Half the sensor's active area, which most sensors bin to; with a photo size, the same field of
    // view as the photo. The fallback size when the camera reports no active area.
    private Size ViewfinderSize(Size? photoSize)
    {
        if (Session.Facts.ActiveArea is not { } area)
            return FallbackViewfinderSize;

        var size = new Size(area.Width / 2, area.Height / 2);
        if (photoSize is { Width: > 0, Height: > 0 } ratio)
            size = BoundedToAspectRatio(size, ratio);
        return new Size(size.Width & ~1u, size.Height & ~1u);                  // even, which every format accepts
    }

    private static readonly Size FallbackViewfinderSize = new(1280, 960);

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
