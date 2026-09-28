using FFmpeg.AutoGen;

namespace LibcameraSharp;

/// <summary>
/// libcamera's colour space as FFmpeg's four colour tags, so players decode the colours the camera
/// produced. A part with no FFmpeg equivalent is left unspecified.
/// </summary>
internal static class LibavColourTags
{
    /// <summary>The colour primaries tag.</summary>
    public static AVColorPrimaries Primaries(ColorSpace colourSpace) => colourSpace.Primaries switch
    {
        ColorSpace.PrimariesKind.Smpte170m => AVColorPrimaries.AVCOL_PRI_SMPTE170M,
        ColorSpace.PrimariesKind.Rec709 => AVColorPrimaries.AVCOL_PRI_BT709,
        ColorSpace.PrimariesKind.Rec2020 => AVColorPrimaries.AVCOL_PRI_BT2020,
        _ => AVColorPrimaries.AVCOL_PRI_UNSPECIFIED,
    };

    /// <summary>The transfer function tag.</summary>
    public static AVColorTransferCharacteristic Transfer(ColorSpace colourSpace) => colourSpace.TransferFunction switch
    {
        ColorSpace.TransferFunctionKind.Linear => AVColorTransferCharacteristic.AVCOL_TRC_LINEAR,
        ColorSpace.TransferFunctionKind.Srgb => AVColorTransferCharacteristic.AVCOL_TRC_IEC61966_2_1,
        ColorSpace.TransferFunctionKind.Rec709 => AVColorTransferCharacteristic.AVCOL_TRC_BT709,
        _ => AVColorTransferCharacteristic.AVCOL_TRC_UNSPECIFIED,
    };

    /// <summary>The YCbCr matrix tag.</summary>
    public static AVColorSpace Matrix(ColorSpace colourSpace) => colourSpace.YcbcrEncoding switch
    {
        ColorSpace.YcbcrEncodingKind.Rec601 => AVColorSpace.AVCOL_SPC_SMPTE170M,
        ColorSpace.YcbcrEncodingKind.Rec709 => AVColorSpace.AVCOL_SPC_BT709,
        ColorSpace.YcbcrEncodingKind.Rec2020 => AVColorSpace.AVCOL_SPC_BT2020_CL,
        _ => AVColorSpace.AVCOL_SPC_UNSPECIFIED,
    };

    /// <summary>The range tag: full (JPEG) or limited (video).</summary>
    public static AVColorRange Range(ColorSpace colourSpace) =>
        colourSpace.Range == ColorSpace.RangeKind.Full ? AVColorRange.AVCOL_RANGE_JPEG : AVColorRange.AVCOL_RANGE_MPEG;
}
