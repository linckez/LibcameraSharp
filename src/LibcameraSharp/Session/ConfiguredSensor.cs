namespace LibcameraSharp;

/// <summary>Sensor mode request: which bit depth and sensor output size to run the sensor at (Raspberry Pi pipelines).</summary>
internal sealed class ConfiguredSensor
{
    /// <summary>Sensor output size, or null to let libcamera choose from the main stream's size.</summary>
    public Size? OutputSize { get; set; }

    /// <summary>Raw bit depth, or null to let libcamera choose.</summary>
    public int? BitDepth { get; set; }

    internal ConfiguredSensor Clone() => new() { OutputSize = OutputSize, BitDepth = BitDepth };
}
