using LibcameraSharp.Advanced;
using Stream = LibcameraSharp.Advanced.Stream;   // libcamera's stream, not System.IO's

namespace LibcameraSharp;

internal sealed partial class CameraSession
{
    /// <summary>Every readout the sensor supports, with the frame rate and field of view each allows.</summary>
    /// <remarks>Found by configuring the camera for each mode once, which forgets pending controls; cached after the first call.</remarks>
    /// <exception cref="InvalidOperationException">The camera is running.</exception>
    public IReadOnlyList<SensorMode> SensorModes
    {
        get
        {
            if (_sensorModes is not null)
                return _sensorModes;
            if (Started)
                throw new InvalidOperationException("Sensor modes are probed by reconfiguring, so the camera must be stopped first.");

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
                var fastest = durations is null ? 0 : 1e6 / durations.Min<long>();   // an array control's bounds are scalars
                var crop = _camera.Controls.TryGet(LibcameraSharp.Controls.ScalerCrop)?.Max<Rectangle>()
                           ?? new Rectangle(0, 0, size.Width, size.Height);

                modes.Add(new SensorMode(size, bayer.BitDepth, Math.Round(fastest, 2), crop));
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

    // The sensor's pixel array size, or VGA when even that isn't reported.
    private Size FallbackResolution() =>
        _camera.Properties.TryGet(Properties.PixelArraySize, out var size) ? size : new Size(640, 480);

    /// <summary>
    /// How badly a sensor mode fits the size and depth wanted; lower is better. A mode smaller than
    /// wanted costs more than a larger one, aspect ratio counts most, and so does each bit of depth.
    /// </summary>
    internal static double ScoreMode(Size modeSize, int modeBitDepth, Size outputSize, int bitDepth)
    {
        // A mode larger than wanted costs a quarter per pixel; a smaller one, double.
        static double ScoreFormat(double desired, double actual)
        {
            var score = desired - actual;
            return score < 0 ? -score / 4 : score * 2;
        }

        var aspect = (double)outputSize.Width / outputSize.Height;
        var modeAspect = (double)modeSize.Width / modeSize.Height;

        var score = ScoreFormat(outputSize.Width, modeSize.Width);
        score += ScoreFormat(outputSize.Height, modeSize.Height);
        score += 1500 * ScoreFormat(aspect, modeAspect);
        score += 500 * Math.Abs(bitDepth - modeBitDepth);
        return score;
    }

    // Bits per sample of a raw format; 0 for anything else.
    private static int BitDepth(PixelFormat format) => BayerFormat.FromPixelFormat(format)?.BitDepth ?? 0;
}
