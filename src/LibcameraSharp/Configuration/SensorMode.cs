namespace LibcameraSharp;

/// <summary>One readout the sensor supports: how big, how deep, how fast, and how much of the scene.</summary>
/// <param name="Size">The size the sensor reads out.</param>
/// <param name="BitDepth">Bits per sample.</param>
/// <param name="MaxFrameRate">Fastest frame rate this readout allows, or null when the camera reports no frame-duration limits.</param>
/// <param name="CropLimits">How much of the sensor this readout covers, in pixel-array coordinates; a smaller rectangle means a
/// narrower view. Null when the camera has no crop control to report it.</param>
public sealed record SensorMode(Size Size, int BitDepth, double? MaxFrameRate, Rectangle? CropLimits);
