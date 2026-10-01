using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Core;

/// <summary>
/// The sensor mode a configuration asks for: the size the sensor itself reads out, and at what bit
/// depth. Raspberry Pi pipelines honour it; others ignore it.
/// </summary>
public sealed unsafe class SensorConfiguration
{
    private readonly libcamera_sensor_configuration_t* _sensor;

    internal SensorConfiguration(libcamera_sensor_configuration_t* sensor) => _sensor = sensor;

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
