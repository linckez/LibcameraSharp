namespace LibcameraSharp;

/// <summary>
/// The layout of a raw frame: colour <see cref="Order"/>, <see cref="BitDepth"/> and <see cref="Packing"/>.
/// Get one from a raw stream's format with <see cref="FromPixelFormat"/>, e.g. to know how to unpack
/// the samples or which CFA pattern to write into a DNG.
/// </summary>
public readonly record struct BayerFormat(BayerOrder Order, byte BitDepth, BayerPacking Packing)
{
    /// <summary>The layout of <paramref name="format"/>, or null when it isn't a raw format libcamera knows.</summary>
    public static BayerFormat? FromPixelFormat(PixelFormat format) => BayerFormats.FromPixelFormat(format);

    /// <summary>libcamera's pixel format for this layout, or null when it has none.</summary>
    public PixelFormat? ToPixelFormat() => BayerFormats.ToPixelFormat(this);

    /// <summary>False for the default value; libcamera uses bit depth 0 as "no format".</summary>
    public bool IsValid => BitDepth != 0;

    /// <summary>True for a sensor without a colour filter array.</summary>
    public bool IsMono => Order == BayerOrder.Mono;

    /// <summary>The unpacked 16-bit layout with the same order and depth, which is what a DNG holds.</summary>
    public BayerFormat Unpacked => this with { Packing = BayerPacking.None };

    /// <summary>
    /// The layout after the sensor image is flipped or transposed by <paramref name="orientation"/>:
    /// a horizontal flip swaps the columns of the 2×2 pattern, a vertical flip its rows.
    /// </summary>
    /// <remarks>libcamera <c>BayerFormat::transform</c> with <c>transformFromOrientation</c>; flipping bit 0 of the order mirrors horizontally, bit 1 vertically.</remarks>
    public BayerFormat Transform(Orientation orientation)
    {
        if (IsMono)
            return this;
        var (hflip, vflip, transpose) = FlipsFor(orientation);
        var order = (int)Order;
        if (hflip)
            order ^= 1;
        if (vflip)
            order ^= 2;
        if (transpose && order == 1)
            order = 2;
        else if (transpose && order == 2)
            order = 1;
        return this with { Order = (BayerOrder)order };
    }

    /// <summary>libcamera's spelling, e.g. <c>RGGB-10-CSI2P</c>, or <c>INVALID</c>.</summary>
    /// <remarks>libcamera <c>bayer_format.cpp:operator&lt;&lt;</c>.</remarks>
    public override string ToString()
    {
        if (!IsValid || Order > BayerOrder.Mono)
            return "INVALID";
        var order = Order == BayerOrder.Mono ? "MONO" : Order.ToString();
        var packing = Packing switch
        {
            BayerPacking.Csi2 => "-CSI2P",
            BayerPacking.Ipu3 => "-IPU3P",
            BayerPacking.Pisp1 => "-PISP1",
            BayerPacking.Pisp2 => "-PISP2",
            _ => "",
        };
        return $"{order}-{BitDepth}{packing}";
    }

    // libcamera transform.cpp:transformFromOrientation, as the three flags BayerFormat::transform reads.
    private static (bool HFlip, bool VFlip, bool Transpose) FlipsFor(Orientation orientation) => orientation switch
    {
        Orientation.Rotate0 => (false, false, false),
        Orientation.Rotate0Mirror => (true, false, false),
        Orientation.Rotate180 => (true, true, false),
        Orientation.Rotate180Mirror => (false, true, false),
        Orientation.Rotate90Mirror => (false, false, true),
        Orientation.Rotate90 => (false, true, true),
        Orientation.Rotate270Mirror => (true, true, true),
        Orientation.Rotate270 => (true, false, true),
        _ => (false, false, false),
    };
}
