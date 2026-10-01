namespace LibcameraSharp;

/// <summary>How raw samples are packed into bytes.</summary>
/// <remarks>libcamera <c>bayer_format.h:BayerFormat::Packing</c>.</remarks>
public enum BayerPacking : ushort
{
    /// <summary>One sample per byte (8-bit) or per little-endian 16-bit word.</summary>
    None = 0,
    /// <summary>MIPI CSI-2 packing: four 10-bit samples in five bytes, or two 12-bit samples in three.</summary>
    Csi2 = 1,
    /// <summary>Intel IPU3 packing (32 samples in 40 bytes).</summary>
    Ipu3 = 2,
    /// <summary>Raspberry Pi 5 PiSP compressed mode 1.</summary>
    Pisp1 = 3,
    /// <summary>Raspberry Pi 5 PiSP compressed mode 2.</summary>
    Pisp2 = 4,
}
