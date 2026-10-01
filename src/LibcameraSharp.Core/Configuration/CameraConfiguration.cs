using LibcameraSharp.Native;
using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Core;

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
    public SensorConfiguration? SensorConfiguration
    {
        get
        {
            var sensor = NativeMethods.libcamera_camera_configuration_get_sensor_configuration(Pointer);
            return sensor is null ? null : new SensorConfiguration(sensor);
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
            throw new LibcameraException("validate the configuration", $"libcamera can't use {this}");
        return status == ConfigurationStatus.Adjusted;
    }

    /// <summary>libcamera's description of every stream, e.g. <c>1920x1080-NV12</c>.</summary>
    public override string ToString() => NativeMethods.libcamera_camera_configuration_to_string(Pointer) ?? "";

    /// <summary>Releases the native configuration. Streams obtained from it stay valid; they belong to the camera.</summary>
    public void Dispose() => _handle.Dispose();
}
