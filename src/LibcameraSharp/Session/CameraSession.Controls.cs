using LibcameraSharp.Advanced;

namespace LibcameraSharp;

internal sealed partial class CameraSession
{
    // libcamera numbers requests in the order they are queued, from 0 after every start
    // (pipeline_handler.cpp:401,491), so counting as we queue gives each request its number.
    private long _queuedSinceStart;

    // The number of the first request that carried the latest change to Controls: frames taken from it on have
    // the new values. The PendingControls and version it was taken from tell a change apart.
    private long _controlsFrom;
    private PendingControls? _sentControls;
    private long _sentVersion;

    // Everything sent since the last configure. Every start list carries it, so a restart (after a timeout, or a
    // stop and start with the same options) comes back with the exposure, gain and focus it had.
    private PendingControls _applied;

    /// <summary>Controls to send with the next requests; each value goes out once.</summary>
    public PendingControls Controls { get; private set; }

    /// <summary>Merges <paramref name="controls"/> into the set sent with the next requests.</summary>
    /// <exception cref="ArgumentException">The camera does not advertise one of them.</exception>
    public void SetControls(PendingControls controls) => Controls.SetControls(controls);

    /// <summary>
    /// Digital zoom to <paramref name="region"/>, in fractions of the full field.
    /// <see cref="RegionOfInterest.Full"/> restores the camera's default crop.
    /// </summary>
    /// <exception cref="InvalidOperationException">The camera cannot crop.</exception>
    public void SetZoom(RegionOfInterest region)
    {
        // `Controls` here is this camera's pending set, so the key table needs its full name.
        var info = _camera.Controls.TryGet(LibcameraSharp.Controls.ScalerCrop)
                   ?? throw new InvalidOperationException("This camera does not advertise ScalerCrop, so it cannot zoom.");

        var sensorArea = info.Max<Rectangle>();
        var crop = region.IsFull
            ? (info.TryGetDefault<Rectangle>(out var preferred) ? preferred : sensorArea)
            : Scale(region, sensorArea);

        // A Pi 5 takes one crop per output stream; the preview stream gets the same one.
        var streamCount = CameraConfiguration is { Preview: not null } ? 2 : 1;

        SetControls(controls =>
        {
            if (CropsPerStream(PlatformDetection.Current))
                controls.Set(LibcameraSharp.Controls.Rpi.ScalerCrops, [.. Enumerable.Repeat(crop, streamCount)]);
            else
                controls.Set(LibcameraSharp.Controls.ScalerCrop, crop);
        });
    }

    // Only a Pi 5 takes a crop per output stream.
    internal static bool CropsPerStream(Platform platform) => platform == Platform.Pisp;

    /// <summary>Restricts autofocus to <paramref name="windows"/>, in fractions of the full field.</summary>
    /// <remarks>libcamera ignores windows unless AfMetering is set to windows, so both are sent.</remarks>
    /// <exception cref="InvalidOperationException">The camera cannot crop, so it has no geometry to place them in.</exception>
    public void SetAutofocusWindows(IReadOnlyList<RegionOfInterest> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);

        var info = _camera.Controls.TryGet(LibcameraSharp.Controls.ScalerCrop)
                   ?? throw new InvalidOperationException("This camera does not advertise ScalerCrop, so autofocus windows cannot be placed.");
        var sensorArea = info.Max<Rectangle>();

        SetControls(controls =>
        {
            controls.Set(LibcameraSharp.Controls.AfMetering, AfMetering.Windows);
            controls.Set(LibcameraSharp.Controls.AfWindows, [.. windows.Select(window => Scale(window, sensorArea))]);
        });
    }

    // A region in fractions of the sensor area, as sensor coordinates.
    internal static Rectangle Scale(RegionOfInterest region, Rectangle sensorArea) => new(
        sensorArea.X + (int)(region.X * sensorArea.Width),
        sensorArea.Y + (int)(region.Y * sensorArea.Height),
        (uint)(region.Width * sensorArea.Width),
        (uint)(region.Height * sensorArea.Height));

    /// <summary>Builds a set of controls with <paramref name="configure"/> and merges it in.</summary>
    public void SetControls(Action<PendingControls> configure)
    {
        var settings = new PendingControls(_camera.Controls);
        configure(settings);
        SetControls(settings);
    }

    /// <summary>
    /// Which request carries the controls sent so far, or the next one when a change hasn't gone out yet. A call
    /// takes this once and waits for it, so later changes, from anyone, don't make it wait.
    /// </summary>
    internal ControlsTarget TakeControlsTarget()
    {
        var sent = ReferenceEquals(Controls, _sentControls) && Controls.Version == _sentVersion;
        return new ControlsTarget(_run, sent ? _controlsFrom : _queuedSinceStart);
    }

    /// <summary>
    /// True when <paramref name="request"/>'s frame was taken with the controls <paramref name="target"/> names. A
    /// Raspberry Pi camera reports which request's controls it applied (<c>rpi::ControlListSequence</c>), after the
    /// sensor's delays; any other camera is assumed to apply them with the request that carried them. A later run
    /// started with every control applied so far (see <see cref="TakeStartControls"/>), so any of its frames will do.
    /// </summary>
    internal bool ControlsLanded(Request request, ControlsTarget target)
    {
        if (target.Run != _run)
            return true;
        return request.Metadata.TryGet(LibcameraSharp.Controls.Rpi.ControlListSequence, out var applied)
            ? applied >= target.From
            : request.Sequence >= target.From;
    }

    // The start list: everything applied since the configure, plus what's pending. It becomes the new applied set,
    // and the request numbering starts again, as libcamera's does.
    private PendingControls TakeStartControls()
    {
        var initial = _applied.Clone();
        initial.SetControls(Controls);
        _applied = initial.Clone();
        ForgetTriggers(_applied);
        Controls = new PendingControls(_camera.Controls);
        (_queuedSinceStart, _controlsFrom, _sentControls, _sentVersion) = (0, 0, Controls, Controls.Version);
        return initial;
    }

    // Triggers act once, when sent (an autofocus scan, a precapture sequence), so a restart doesn't send them again.
    private static void ForgetTriggers(PendingControls applied)
    {
        applied.Remove(LibcameraSharp.Controls.AfTrigger);
        applied.Remove(LibcameraSharp.Controls.Draft.AePrecaptureTrigger);
    }

    // Hands a request to libcamera with whatever controls changed since the last one; a request that fails to queue
    // leaves those controls for the next.
    private void Enqueue(Request request, BufferAllocation.Slot slot)
    {
        // The version these controls were sent at, so a waiter can tell whether its change has gone out. A value the camera
        // can't take (one it doesn't advertise, or of the wrong type) is reported and dropped, or it would fail every
        // request after this one too.
        var version = Controls.Version;
        try
        {
            Controls.CopyTo(request.Controls, _camera.Controls);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"LibcameraSharp: controls the camera couldn't take were dropped: {exception.Message}");
            Controls.Forget();
        }

        _camera.QueueRequest(request);
        slot.Queued = true;

        _applied.SetControls(Controls);
        ForgetTriggers(_applied);
        Controls.Forget();
        if (!ReferenceEquals(Controls, _sentControls) || version != _sentVersion)
            (_controlsFrom, _sentControls, _sentVersion) = (_queuedSinceStart, Controls, version);
        _queuedSinceStart++;
    }
}
