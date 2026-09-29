using LibcameraSharp.Advanced;
using Stream = LibcameraSharp.Advanced.Stream;   // libcamera's stream, not System.IO's

// Everything here runs on the session's loop.

namespace LibcameraSharp;

internal sealed partial class CameraSession
{
    /// <summary>Defaults for live frames: 640×480 XBGR8888, four buffers, and a raw stream on Pi cameras.</summary>
    public SessionConfiguration CreatePreviewConfiguration(StreamDescription? main = null) =>
        MakeConfiguration(Overlay(new StreamDescription(new Size(640, 480), PixelFormats.XBGR8888), main),
            ColorSpace.Sycc, bufferCount: 4, FactoryControls(NoiseReductionMode.Minimal, [100, 83333]));

    /// <summary>Defaults for photos: the full sensor in BGR888, one buffer, high-quality noise reduction.</summary>
    public SessionConfiguration CreateStillConfiguration(StreamDescription? main = null) =>
        MakeConfiguration(Overlay(new StreamDescription(SensorResolution, PixelFormats.BGR888), main),
            ColorSpace.Sycc, bufferCount: 1, FactoryControls(NoiseReductionMode.HighQuality, [100, 1_000_000_000]));

    /// <summary>The size a recording gets when the options don't set one.</summary>
    public static readonly Size DefaultVideoSize = new(1280, 720);

    /// <summary>Defaults for recordings: 1280×720 XBGR8888, six buffers, 30 fps, Rec. 709.</summary>
    public SessionConfiguration CreateVideoConfiguration(StreamDescription? main = null)
    {
        var captureStream = Overlay(new StreamDescription(DefaultVideoSize, PixelFormats.XBGR8888), main);
        return MakeConfiguration(captureStream, VideoColourSpace(captureStream.Size!.Value, motionJpeg: false), bufferCount: 6,
            FactoryControls(NoiseReductionMode.Fast, [33333, 33333]));
    }

    /// <summary>
    /// The colour space a recording is tagged with: sYCC for motion JPEG, Rec. 709 from 1280 wide or
    /// 720 high, SMPTE 170M below that.
    /// </summary>
    public static ColorSpace VideoColourSpace(Size size, bool motionJpeg)
    {
        if (motionJpeg)
            return ColorSpace.Sycc;
        return size.Width >= 1280 || size.Height >= 720 ? ColorSpace.Rec709 : ColorSpace.Smpte170m;
    }

    /// <summary>A configuration with the defaults for <paramref name="use"/>: full-resolution RGB for photos, 720p for video, VGA for frames.</summary>
    public SessionConfiguration CreateConfiguration(CameraUse use) => use switch
    {
        CameraUse.Photo => CreateStillConfiguration(),
        CameraUse.Video => CreateVideoConfiguration(),
        _ => CreatePreviewConfiguration(),
    };

    // The controls each use starts from, when the camera has them.
    private PendingControls FactoryControls(NoiseReductionMode mode, long[] frameDurationLimits)
    {
        var settings = new PendingControls(_camera.Controls);
        if (_camera.Controls.Contains(LibcameraSharp.Controls.Draft.NoiseReductionMode) && _camera.Controls.Contains(LibcameraSharp.Controls.FrameDurationLimits))
        {
            settings.Set(LibcameraSharp.Controls.Draft.NoiseReductionMode, mode);
            settings.Set(LibcameraSharp.Controls.FrameDurationLimits, frameDurationLimits);
        }
        return settings;
    }

    // The shared body of the three factories: 2-pixel alignment, and a raw stream on cameras that have one.
    private SessionConfiguration MakeConfiguration(StreamDescription captureStream, ColorSpace colourSpace, int bufferCount, PendingControls controls)
    {
        captureStream.Align(optimal: false);
        return new SessionConfiguration
        {
            ColourSpace = colourSpace, BufferCount = bufferCount, Controls = controls,
            Capture = captureStream,
            Raw = HasRawSensor ? new StreamDescription(captureStream.Size, SensorFormat) : null,
        };
    }

    private static StreamDescription Overlay(StreamDescription defaults, StreamDescription? updates)
    {
        if (updates is null)
            return defaults.Clone();
        return new StreamDescription(updates.Size ?? defaults.Size, updates.Format ?? defaults.Format)
        {
            Stride = updates.Stride,
        };
    }

    /// <summary>
    /// Applies <paramref name="cameraConfig"/>: asks libcamera for the streams, lets it adjust them,
    /// keeps what it chose in <see cref="CameraConfiguration"/>, and allocates buffers.
    /// </summary>
    /// <exception cref="InvalidOperationException">The camera is running.</exception>
    /// <exception cref="LibcameraException">libcamera rejected the configuration.</exception>
    public void Configure(SessionConfiguration cameraConfig)
    {
        ThrowIfClosing();
        if (Started)
            throw new InvalidOperationException("Camera must be stopped before configuring.");
        var config = cameraConfig.Clone();
        config.Controls.Attach(_camera.Controls);

        // Unset raw streams take the sensor's format and the main stream's size; non-Pi cameras get none.
        if (config.Raw is not null)
        {
            config.Raw.Format ??= SensorFormat;
            config.Raw.Size ??= config.Capture.Size;
        }
        if (!HasRawSensor)
            config.Raw = null;
        CheckCameraConfig(config);

        ReleaseConfiguration();

        // Every processed stream is requested as a viewfinder, then set to what was asked for.
        var roles = new List<StreamRole> { StreamRole.ViewFinder };
        if (config.Preview is not null)
            roles.Add(StreamRole.ViewFinder);
        if (config.Raw is not null)
            roles.Add(StreamRole.Raw);
        var libcameraConfig = _camera.GenerateConfiguration([.. roles])
                              ?? throw new LibcameraException("generate a configuration", $"the camera can't provide streams for {string.Join(", ", roles)}");
        libcameraConfig.Orientation = config.Transform;

        var index = 0;
        Apply(libcameraConfig[index++], config.Capture, config.BufferCount, ColourSpaceFor(config.ColourSpace, config.Capture.Format!.Value));
        if (config.Preview is not null)
            Apply(libcameraConfig[index++], config.Preview, config.BufferCount, config.ColourSpace);
        if (config.Raw is not null)
            Apply(libcameraConfig[index], config.Raw, config.BufferCount, ColorSpace.Raw);

        ApplySensorConfiguration(libcameraConfig, config);

        var status = libcameraConfig.Validate();
        UpdateCameraConfig(config, libcameraConfig);
        if (status == ConfigurationStatus.Invalid)
            throw new LibcameraException("validate the configuration", $"libcamera can't use {config}");
        _camera.Configure(libcameraConfig);
        _libcameraConfig = libcameraConfig;

        // Streams by name, in the order they were added.
        _streams = new Dictionary<SessionStream, Stream> { [SessionStream.Capture] = libcameraConfig[0].Stream };
        index = 1;
        if (config.Preview is not null)
            _streams[SessionStream.Preview] = libcameraConfig[index++].Stream;
        if (config.Raw is not null)
            _streams[SessionStream.Raw] = libcameraConfig[index].Stream;

        // Hang on to the last completed request only when there's more than one buffer; with one it would stall the pipeline.
        _maxQueueLength = config.BufferCount > 1 ? 1 : 0;

        _allocation = new BufferAllocation(_camera, _streams);

        CameraConfiguration = config;
        Controls = new PendingControls(_camera.Controls);
        Controls.SetControls(config.Controls);
        _applied = new PendingControls(_camera.Controls);
        _configureCount++;

        // Ranges can follow the sensor mode, so callers get a fresh snapshot of what the camera now advertises.
        Volatile.Write(ref _facts, ReadFacts());
    }

    // Pins the sensor readout by scoring every raw mode against the size and depth wanted; left alone,
    // libcamera guesses from the stream sizes.
    private void ApplySensorConfiguration(LibcameraSharp.Advanced.CameraConfiguration libcameraConfig, SessionConfiguration config)
    {
        if (!HasRawSensor || _rawModes.Count == 0)
            return;

        var bitDepth = config.Sensor.BitDepth
            ?? (config.Raw?.Format is { } rawFormat ? BitDepth(rawFormat) : (int?)null)
            ?? (SensorFormat is { } sensorFormat ? BitDepth(sensorFormat) : 0);

        var outputSize = config.Sensor.OutputSize
            ?? config.Raw?.Size
            ?? config.Capture.Size
            ?? SensorResolution;

        var best = _rawModes[0];
        var bestScore = ScoreMode(best.Size, BitDepth(best.Format), outputSize, bitDepth);
        foreach (var mode in _rawModes.Skip(1))
        {
            var score = ScoreMode(mode.Size, BitDepth(mode.Format), outputSize, bitDepth);
            if (score >= bestScore)
                continue;
            (best, bestScore) = (mode, score);
        }

        libcameraConfig.SetSensorConfiguration(best.Size, (uint)BitDepth(best.Format));
    }

    private static void Apply(LibcameraSharp.Advanced.StreamConfiguration target, StreamDescription ours, int bufferCount, ColorSpace? colourSpace)
    {
        target.Size = ours.Size!.Value;
        target.PixelFormat = ours.Format!.Value;
        target.BufferCount = (uint)bufferCount;
        target.Stride = ours.Stride ?? 0;
        if (colourSpace is { } cs)
            target.ColorSpace = cs;
    }

    // An RGB stream carries no YCbCr matrix or range, or libcamera complains.
    private static ColorSpace? ColourSpaceFor(ColorSpace? colourSpace, PixelFormat format) =>
        colourSpace is { } cs && IsRgb(format) ? cs with { YcbcrEncoding = ColorSpace.YcbcrEncodingKind.None, Range = ColorSpace.RangeKind.Full } : colourSpace;

    // Writes back what libcamera chose.
    private static void UpdateCameraConfig(SessionConfiguration config, LibcameraSharp.Advanced.CameraConfiguration libcameraConfig)
    {
        config.Transform = libcameraConfig.Orientation;
        config.ColourSpace = ColourSpaceFromLibcamera(libcameraConfig[0].ColorSpace);
        var index = 0;
        UpdateStream(config.Capture, libcameraConfig[index++]);
        if (config.Preview is not null)
            UpdateStream(config.Preview, libcameraConfig[index++]);
        if (config.Raw is not null)
            UpdateStream(config.Raw, libcameraConfig[index]);
    }

    private static void UpdateStream(StreamDescription ours, LibcameraSharp.Advanced.StreamConfiguration theirs)
    {
        ours.Format = theirs.PixelFormat;
        ours.Size = theirs.Size;
        ours.Stride = theirs.Stride;
        ours.FrameSize = theirs.FrameSize;
    }

    // Maps a colour space libcamera returns to one of the three standard ones where it matches.
    private static ColorSpace? ColourSpaceFromLibcamera(ColorSpace? cs)
    {
        if (cs is not { } value)
            return null;
        foreach (var standard in new[] { ColorSpace.Sycc, ColorSpace.Smpte170m, ColorSpace.Rec709 })
        {
            if (standard.Primaries == value.Primaries && standard.TransferFunction == value.TransferFunction)
                return standard;
        }
        return value;
    }

    private static void CheckCameraConfig(SessionConfiguration config)
    {
        // Configurations libcamera would refuse, caught with a clearer message.
        if (config.Capture.Size is null || config.Capture.Format is null)
            throw new ArgumentException("The main stream needs a size and a format.", nameof(config));
        if (config.BufferCount < 1)
            throw new ArgumentException("The buffer count must be at least 1.", nameof(config));
        if (config.Preview is { } preview)
        {
            if (preview.Size is null || preview.Format is null)
                throw new ArgumentException("The preview stream needs a size and a format.", nameof(config));
            if (preview.Size.Value.Width > config.Capture.Size.Value.Width || preview.Size.Value.Height > config.Capture.Size.Value.Height)
                throw new ArgumentException("The preview stream may not be larger than the capture stream.", nameof(config));
        }
    }

    // The old allocation is freed now, or kept until the last frame someone holds from it comes back.
    private void ReleaseConfiguration()
    {
        ReleaseReady();
        if (_allocation is { } old)
        {
            old.Retire();
            if (!old.Disposed)
                _retired.Add(old);
        }
        _allocation = null;
        _libcameraConfig?.Dispose();
        _libcameraConfig = null;
        _streams = [];
        CameraConfiguration = null;
    }

    private static bool IsRgb(PixelFormat format) =>
        format == PixelFormats.BGR888 || format == PixelFormats.RGB888 || format == PixelFormats.XBGR8888 || format == PixelFormats.XRGB8888
        || format == PixelFormats.RGB161616 || format == PixelFormats.BGR161616;
}
