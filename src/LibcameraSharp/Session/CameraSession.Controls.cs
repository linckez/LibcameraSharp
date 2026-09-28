using LibcameraSharp.Advanced;
using Stream = LibcameraSharp.Advanced.Stream;   // libcamera's stream, not System.IO's

namespace LibcameraSharp;

internal sealed partial class CameraSession
{
    /// <summary>Merges <paramref name="controls"/> into the set sent with the next requests.</summary>
    /// <exception cref="ArgumentException">The camera does not advertise one of them.</exception>
    public void SetControls(PendingControls controls)
    {
        lock (_lock)
        {
            Controls.SetControls(controls);
        }
    }

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
    /// Which request carries the controls sent so far, or the next one when a change hasn't gone out
    /// yet. A call takes this once and waits for it, so later changes, from anyone, don't make it wait.
    /// </summary>
    internal ControlsTarget TakeControlsTarget()
    {
        lock (_lock)
        {
            var sent = ReferenceEquals(Controls, _sentControls) && Controls.Version == _sentVersion;
            return new ControlsTarget(_stopCount, sent ? _controlsFrom : _queuedSinceStart);
        }
    }

    /// <summary>
    /// True when <paramref name="request"/>'s frame was taken with the controls <paramref name="target"/>
    /// names. A Raspberry Pi camera reports which request's controls it applied
    /// (<c>rpi::ControlListSequence</c>), after the sensor's delays; any other camera is assumed to apply
    /// them with the request that carried them. After a restart, the new run started with the latest
    /// controls, so any of its frames will do.
    /// </summary>
    internal bool ControlsLanded(Request request, ControlsTarget target)
    {
        if (target.Run != _stopCount)
            return true;
        return request.Metadata.TryGet(LibcameraSharp.Controls.Rpi.ControlListSequence, out var applied)
            ? applied >= target.From
            : request.Sequence >= target.From;
    }

    /// <summary>
    /// The next frame taken with the controls <paramref name="target"/> names; frames still in flight
    /// from before are returned to the camera. Used by the calls that take options, which promise
    /// pictures taken with them.
    /// </summary>
    internal async Task<CapturedFrame> CaptureRequestWithControlsAsync(ControlsTarget target, CancellationToken cancellationToken)
    {
        EnsureStarted();
        while (true)
        {
            var request = await NextCompletedAsync(cancellationToken).ConfigureAwait(false);
            if (!ControlsLanded(request, target))
            {
                Recycle(request, _stopCount);
                continue;
            }
            _allocation!.Acquire(request);
            return new CapturedFrame(this, request, CameraConfiguration!, _streams, _stopCount, _allocation);
        }
    }
}
