namespace LibcameraSharp;

internal sealed partial class CameraSession
{
    // Recordings running now. Each gets every frame from the first one taken with its controls; until then, Gate
    // names that frame.
    private readonly List<EncoderFeed> _feeds = [];

    private sealed class EncoderFeed(Encoder encoder, ControlsTarget? gate)
    {
        public Encoder Encoder { get; } = encoder;

        public ControlsTarget? Gate { get; set; } = gate;
    }

    /// <summary>
    /// Attaches a started encoder: it gets every frame from the first one taken with the controls
    /// <paramref name="gate"/> names, or from the next one when there is no gate.
    /// </summary>
    /// <remarks>
    /// The camera is started if it isn't: another recording's end may have stopped it since this one's setup. A start
    /// that fails leaves the encoder unattached.
    /// </remarks>
    public Task AttachEncoderAsync(Encoder encoder, ControlsTarget? gate = null) => CallAsync(() =>
    {
        ThrowIfClosing();
        if (!Started)
            Start();
        _feeds.Add(new EncoderFeed(encoder, gate));
    });

    /// <summary>
    /// Detaches <paramref name="encoder"/>; the last recording stops the camera. Flushing and closing are the caller's,
    /// off the loop.
    /// </summary>
    public Task DetachEncoderAsync(Encoder encoder) => CallAsync(() =>
    {
        // Detached first, so a stop that fails still leaves the recording detached.
        if (_feeds.RemoveAll(feed => feed.Encoder == encoder) > 0 && _feeds.Count == 0)
            Stop();
    });

    /// <summary>Stops the camera when no recording is running, as after a recording that failed to start.</summary>
    public Task StopIfIdleAsync() => CallAsync(() =>
    {
        if (_feeds.Count == 0)
            Stop();
    });

    /// <summary>
    /// Sets an encoder up for the capture stream it will be fed: frame size, stride, format, colour space and frame
    /// rate.
    /// </summary>
    internal static void PrepareEncoder(Encoder encoder, SessionConfiguration configuration, double frameRate)
    {
        var stream = configuration.Capture;
        encoder.Width = (int)stream.Size!.Value.Width;
        encoder.Height = (int)stream.Size.Value.Height;
        encoder.Stride = stream.Stride!.Value;
        encoder.Format = stream.Format!.Value;
        encoder.ColorSpace = configuration.ColorSpace;
        encoder.FrameRate = frameRate;
    }

    // Every recording gets its own hold on the frame, once its controls have landed. Recordings never drop: a slow
    // one keeps the buffer, and so slows the camera.
    private void FeedEncoders(Request request, BufferAllocation.Slot slot)
    {
        foreach (var feed in _feeds)
        {
            if (feed.Gate is { } gate)
            {
                if (!ControlsLanded(request, gate))
                    continue;
                feed.Gate = null;
            }

            slot.Leases++;
            feed.Encoder.Enqueue(NewFrame(request));
        }
    }
}
