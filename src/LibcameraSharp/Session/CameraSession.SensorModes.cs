using LibcameraSharp.Advanced;
using Stream = LibcameraSharp.Advanced.Stream;   // libcamera's stream, not System.IO's

namespace LibcameraSharp;

internal sealed partial class CameraSession
{
    /// <summary>Every readout the sensor supports, with the frame rate and field of view each allows.</summary>
    /// <remarks>
    /// Found by configuring the camera for each mode once, so the first call stops a running camera (captures still
    /// waiting fail with <see cref="OperationCanceledException"/>) and forgets pending controls; cached after that.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A recording is running, and probing would reconfigure the camera under it.</exception>
    public Task<IReadOnlyList<SensorMode>> ProbeSensorModesAsync() => CallAsync(() =>
    {
        ThrowIfClosing();
        return SensorModes;
    });

    // On the loop: probes once, then answers from the cache.
    private IReadOnlyList<SensorMode> SensorModes
    {
        get
        {
            if (_sensorModes is not null)
                return _sensorModes;

            // A running recording keeps the size and format it started with; anything else is stopped for the probe.
            if (_feeds.Count > 0)
                throw new InvalidOperationException("Stop the recording before probing sensor modes: probing reconfigures the camera.");
            Stop();

            var previous = CameraConfiguration;
            var modes = new List<SensorMode>();
            foreach (var (size, format) in _rawModes)
            {
                if (BayerFormat.FromPixelFormat(format) is not { } bayer)
                    continue;                    // not a raw format

                // Asking for a raw stream of this size and format selects the mode.
                var probe = CreatePreviewConfiguration();
                probe.Raw = new StreamDescription(size, format);
                Configure(probe);

                var durations = _camera.Controls.TryGet(LibcameraSharp.Controls.FrameDurationLimits);
                double? fastest = durations is null ? null : Math.Round(1e6 / durations.Min<long>(), 2);   // an array control's bounds are scalars
                var crop = _camera.Controls.TryGet(LibcameraSharp.Controls.ScalerCrop)?.Max<Rectangle>();

                modes.Add(new SensorMode(size, bayer.BitDepth, fastest, crop));
            }

            if (previous is not null)
                Configure(previous);             // put back what the caller had

            return _sensorModes = modes;
        }
    }

    private IReadOnlyList<SensorMode>? _sensorModes;

    // Sensors with a colour filter array; only they have a raw stream and sensor modes.
    private bool HasRawSensor => _camera.Properties.Contains(Properties.Draft.ColorFilterArrangement);

    // Every (size, format) the sensor can read out; scored on every configure.
    private IReadOnlyList<(Size Size, PixelFormat Format)> _rawModes = [];

    private (Size Resolution, PixelFormat? Format) SelectNativeMode()
    {
        using var rawConfig = _camera.GenerateConfiguration(StreamRole.Raw);
        if (rawConfig is null)
            return (FallbackResolution(), null);      // no raw stream, as on USB and virtual cameras

        var modes = new List<(Size, PixelFormat)>();
        foreach (var pixelFormat in rawConfig[0].Formats.PixelFormats)
            foreach (var rawSize in rawConfig[0].Formats.Sizes(pixelFormat))
                modes.Add((rawSize, pixelFormat));
        _rawModes = modes;

        // The largest raw mode; on a Pi sensor, the deepest among equals.
        (Size Size, PixelFormat Format)? best = null;
        foreach (var format in rawConfig[0].Formats.PixelFormats)
        {
            foreach (var size in rawConfig[0].Formats.Sizes(format))
            {
                if (best is { } current && !IsBetterNativeMode(size, format, current.Size, current.Format))
                    continue;
                best = (size, format);
            }
        }
        if (best is not { } native)
            return (FallbackResolution(), null);
        return (native.Size, native.Format);
    }

    private bool IsBetterNativeMode(Size size, PixelFormat format, Size bestSize, PixelFormat bestFormat)
    {
        var area = (long)size.Width * size.Height;
        var bestArea = (long)bestSize.Width * bestSize.Height;
        if (area != bestArea)
            return area > bestArea;
        return HasRawSensor && BitDepth(format) > BitDepth(bestFormat);
    }

    // The sensor's pixel array size, or the live-frame default when even that isn't reported. That last choice is
    // the SDK's own: no reference has a camera with neither raw modes nor a pixel array size.
    private Size FallbackResolution() =>
        _camera.Properties.TryGet(Properties.PixelArraySize, out var size) ? size : DefaultPreviewSize;

    /// <summary>
    /// How badly a sensor mode fits the size and depth wanted; lower is better. A mode smaller than
    /// wanted costs more than a larger one, aspect ratio counts most, and so does each bit of depth.
    /// </summary>
    internal static double ScoreMode(Size modeSize, int modeBitDepth, Size outputSize, int bitDepth)
    {
        // A mode larger than wanted costs a quarter per pixel; a smaller one, double. The aspect ratio's difference
        // weighs 1500 times a pixel's, and each bit of depth 500.
        const double LargerPenalty = 1.0 / 4, SmallerPenalty = 2, AspectWeight = 1500, BitDepthWeight = 500;
        static double ScoreFormat(double desired, double actual)
        {
            var score = desired - actual;
            return score < 0 ? -score * LargerPenalty : score * SmallerPenalty;
        }

        var aspect = (double)outputSize.Width / outputSize.Height;
        var modeAspect = (double)modeSize.Width / modeSize.Height;

        var score = ScoreFormat(outputSize.Width, modeSize.Width);
        score += ScoreFormat(outputSize.Height, modeSize.Height);
        score += AspectWeight * ScoreFormat(aspect, modeAspect);
        score += BitDepthWeight * Math.Abs(bitDepth - modeBitDepth);
        return score;
    }

    // Bits per sample of a raw format; 0 for anything else.
    private static int BitDepth(PixelFormat format) => BayerFormat.FromPixelFormat(format)?.BitDepth ?? 0;
}
