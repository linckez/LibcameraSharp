using LibcameraSharp.Native;
using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Advanced;

/// <summary>
/// The set of streams to configure a camera with. Get one from
/// <see cref="Camera.GenerateConfiguration"/>, adjust the <see cref="StreamConfiguration"/>s,
/// <see cref="Validate"/>, then pass it to <see cref="ActiveCamera.Configure"/>.
/// </summary>
public sealed unsafe class CameraConfiguration : IDisposable
{
    private readonly CameraConfigurationHandle _handle;

    internal CameraConfiguration(libcamera_camera_configuration_t* config) => _handle = new(config);

    internal libcamera_camera_configuration_t* Pointer => _handle.Pointer;

    /// <summary>Number of streams in the configuration.</summary>
    public int Count => (int)NativeMethods.libcamera_camera_configuration_size(Pointer);

    /// <summary>The stream configuration at <paramref name="index"/>, in the order roles were passed to <see cref="Camera.GenerateConfiguration"/>.</summary>
    public StreamConfiguration this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            return new StreamConfiguration(NativeMethods.libcamera_camera_configuration_at(Pointer, (nuint)index), this);
        }
    }

    /// <summary>All stream configurations.</summary>
    public IEnumerable<StreamConfiguration> Streams => Enumerable.Range(0, Count).Select(i => this[i]);

    /// <summary>Image orientation to request; libcamera adjusts it to what the camera supports during <see cref="Validate"/>.</summary>
    public Orientation Orientation
    {
        get => (Orientation)NativeMethods.libcamera_camera_configuration_get_orientation(Pointer);
        set => NativeMethods.libcamera_camera_configuration_set_orientation(Pointer, (libcamera_orientation)value);
    }

    /// <summary>
    /// Asks libcamera to check the configuration and fix what it can. Re-read the stream
    /// configurations when the result is <see cref="ConfigurationStatus.Adjusted"/>.
    /// </summary>
    public ConfigurationStatus Validate() => (ConfigurationStatus)NativeMethods.libcamera_camera_configuration_validate(Pointer);

    /// <summary>
    /// The sensor mode this configuration asks for, or null when it asks for none. Setting one tells
    /// the pipeline which sensor readout to use rather than letting it choose from the stream sizes.
    /// </summary>
    /// <remarks>libcamera <c>camera.h:CameraConfiguration::sensorConfig</c>.</remarks>
    public SensorSettings? SensorConfiguration
    {
        get
        {
            var sensor = NativeMethods.libcamera_camera_configuration_get_sensor_configuration(Pointer);
            return sensor is null ? null : new SensorSettings(sensor);
        }
    }

    /// <summary>Asks the sensor to read out at <paramref name="outputSize"/> and <paramref name="bitDepth"/>.</summary>
    public void SetSensorConfiguration(Size outputSize, uint bitDepth)
    {
        var sensor = NativeMethods.libcamera_sensor_configuration_create();
        try
        {
            NativeMethods.libcamera_sensor_configuration_set_output_size(sensor, outputSize.Width, outputSize.Height);
            NativeMethods.libcamera_sensor_configuration_set_bit_depth(sensor, bitDepth);
            NativeMethods.libcamera_camera_set_sensor_configuration(Pointer, sensor);
        }
        finally
        {
            // set_sensor_configuration copies it in, so ours is ours to free.
            NativeMethods.libcamera_sensor_configuration_destroy(sensor);
        }
    }

    /// <summary>Validates and throws when the configuration is <see cref="ConfigurationStatus.Invalid"/>.</summary>
    /// <returns>True when libcamera adjusted something.</returns>
    public bool ValidateOrThrow()
    {
        var status = Validate();
        if (status == ConfigurationStatus.Invalid)
            throw new LibcameraException($"configuration is invalid: {this}");
        return status == ConfigurationStatus.Adjusted;
    }

    /// <summary>libcamera's description of every stream, e.g. <c>1920x1080-NV12</c>.</summary>
    public override string ToString() => NativeMethods.libcamera_camera_configuration_to_string(Pointer) ?? "";

    /// <summary>Releases the native configuration. Streams obtained from it stay valid; they belong to the camera.</summary>
    public void Dispose() => _handle.Dispose();
}

/// <summary>
/// Settings for one stream: pixel format, size, and what libcamera derived from them (stride,
/// frame size, buffer count). Change <see cref="PixelFormat"/> / <see cref="Size"/> before
/// <see cref="CameraConfiguration.Validate"/>; read the rest after.
/// </summary>
/// <remarks>A view into its <see cref="CameraConfiguration"/>, valid for as long as that is.</remarks>
public sealed unsafe class StreamConfiguration
{
    private readonly libcamera_stream_configuration* _cfg;
    private readonly CameraConfiguration? _owner;   // keeps the parent alive while this view exists

    internal StreamConfiguration(libcamera_stream_configuration* cfg, CameraConfiguration? owner)
    {
        _cfg = cfg;
        _owner = owner;
    }

    /// <summary>Pixel format of the frames. See <see cref="Formats"/> for what's supported.</summary>
    public PixelFormat PixelFormat
    {
        get => new(_cfg->pixel_format.fourcc, _cfg->pixel_format.modifier);
        set => _cfg->pixel_format = new libcamera_pixel_format { fourcc = value.Fourcc, modifier = value.Modifier };
    }

    /// <summary>Frame size in pixels.</summary>
    public Size Size
    {
        get => new(_cfg->size.width, _cfg->size.height);
        set => _cfg->size = new libcamera_size { width = value.Width, height = value.Height };
    }

    /// <summary>Bytes per image row, including padding. Set by libcamera during validation.</summary>
    public uint Stride
    {
        get => _cfg->stride;
        set => _cfg->stride = value;
    }

    /// <summary>Bytes per frame. Set by libcamera during validation.</summary>
    public uint FrameSize
    {
        get => _cfg->frame_size;
        set => _cfg->frame_size = value;
    }

    /// <summary>How many buffers <see cref="FrameBufferAllocator"/> will allocate for the stream.</summary>
    public uint BufferCount
    {
        get => _cfg->buffer_count;
        set => _cfg->buffer_count = value;
    }

    /// <summary>Colour space, or null when libcamera hasn't chosen one yet.</summary>
    public ColorSpace? ColorSpace
    {
        get => NativeMethods.libcamera_stream_configuration_has_color_space(_cfg)
            ? LibcameraSharp.ColorSpace.From(NativeMethods.libcamera_stream_configuration_get_color_space(_cfg))
            : null;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var native = value.Value.ToNative();
            NativeMethods.libcamera_stream_configuration_set_color_space(_cfg, &native);
        }
    }

    /// <summary>Formats and sizes this stream supports.</summary>
    public StreamFormats Formats => new(NativeMethods.libcamera_stream_configuration_formats(_cfg));

    /// <summary>The configured stream. Only available after <see cref="ActiveCamera.Configure"/>.</summary>
    /// <exception cref="InvalidOperationException">The camera hasn't been configured with this configuration.</exception>
    public Stream Stream
    {
        get
        {
            var stream = NativeMethods.libcamera_stream_configuration_stream(_cfg);
            return stream is null ? throw new InvalidOperationException("The camera has not been configured with this configuration yet.") : new Stream(stream, _owner);
        }
    }

    /// <summary>libcamera's description, e.g. <c>1920x1080-NV12</c>.</summary>
    public override string ToString() => NativeMethods.libcamera_stream_configuration_to_string(_cfg) ?? "";
}

/// <summary>
/// The sensor mode a configuration asks for: the size the sensor itself reads out, and at what bit
/// depth. Raspberry Pi pipelines honour it; others ignore it.
/// </summary>
public sealed unsafe class SensorSettings
{
    private readonly libcamera_sensor_configuration_t* _sensor;

    internal SensorSettings(libcamera_sensor_configuration_t* sensor) => _sensor = sensor;

    /// <summary>The size the sensor reads out, before any scaling.</summary>
    public Size OutputSize
    {
        get { var size = NativeMethods.libcamera_sensor_configuration_get_output_size(_sensor); return new(size.width, size.height); }
        set => NativeMethods.libcamera_sensor_configuration_set_output_size(_sensor, value.Width, value.Height);
    }

    /// <summary>Bits per sample the sensor reads out at.</summary>
    public uint BitDepth
    {
        get => NativeMethods.libcamera_sensor_configuration_get_bit_depth(_sensor);
        set => NativeMethods.libcamera_sensor_configuration_set_bit_depth(_sensor, value);
    }

    /// <summary>True when libcamera considers this a usable request.</summary>
    public bool IsValid => NativeMethods.libcamera_sensor_configuration_is_valid(_sensor);

    /// <inheritdoc/>
    public override string ToString() => $"{OutputSize} at {BitDepth} bits";
}
