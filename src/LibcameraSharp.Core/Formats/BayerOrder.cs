namespace LibcameraSharp;

/// <summary>The 2×2 colour filter pattern of a raw format, or <see cref="Mono"/> for a sensor without one.</summary>
/// <remarks>libcamera <c>bayer_format.h:BayerFormat::Order</c>; the numeric values matter, see <see cref="BayerFormat.Transform"/>.</remarks>
public enum BayerOrder : byte
{
    /// <summary>Blue, green / green, red.</summary>
    BGGR = 0,
    /// <summary>Green, blue / red, green.</summary>
    GBRG = 1,
    /// <summary>Green, red / blue, green.</summary>
    GRBG = 2,
    /// <summary>Red, green / green, blue.</summary>
    RGGB = 3,
    /// <summary>No colour filter: every pixel is luminance.</summary>
    Mono = 4,
}
