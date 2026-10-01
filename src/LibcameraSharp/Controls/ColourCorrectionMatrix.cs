namespace LibcameraSharp;

/// <summary>
/// A 3×3 colour correction matrix, row by row: it turns the sensor's colours into sRGB. Read the one the camera used
/// from <see cref="CaptureMetadata.ColourCorrectionMatrix"/> and set it on <see cref="CameraControls.ColourCorrectionMatrix"/>
/// to keep it for the next capture.
/// </summary>
/// <remarks>A value, so two options with the same matrix are equal, as for the rest of an options record.</remarks>
public readonly record struct ColourCorrectionMatrix(
    float M11, float M12, float M13,
    float M21, float M22, float M23,
    float M31, float M32, float M33)
{
    /// <summary>The matrix that changes nothing.</summary>
    public static ColourCorrectionMatrix Identity { get; } = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    // libcamera's form: nine floats, row by row.
    internal float[] ToArray() => [M11, M12, M13, M21, M22, M23, M31, M32, M33];

    // From libcamera's form; anything but nine values isn't a 3×3 matrix.
    internal static ColourCorrectionMatrix? FromArray(float[] values) => values.Length == 9
        ? new(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8])
        : null;
}
