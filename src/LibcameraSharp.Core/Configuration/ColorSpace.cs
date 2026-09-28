using System.Runtime.InteropServices;
using LibcameraSharp.Native.Interop;

namespace LibcameraSharp;

/// <summary>How pixel values map to colours: primaries, transfer function, YCbCr encoding and range.</summary>
/// <remarks>Layout matches libcamera's <c>ColorSpace</c>. Use the presets (<see cref="Srgb"/>, <see cref="Rec709"/>, …) unless you need something specific.</remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct ColorSpace(
    ColorSpace.PrimariesKind Primaries,
    ColorSpace.TransferFunctionKind TransferFunction,
    ColorSpace.YcbcrEncodingKind YcbcrEncoding,
    ColorSpace.RangeKind Range)
{
    /// <summary>Colour primaries.</summary>
    public enum PrimariesKind
    {
        /// <summary>Raw sensor colour, no defined primaries.</summary>
        Raw = libcamera_color_space_primaries.LIBCAMERA_COLOR_SPACE_PRIMARIES_RAW,
        /// <summary>SMPTE 170M (SD video).</summary>
        Smpte170m = libcamera_color_space_primaries.LIBCAMERA_COLOR_SPACE_PRIMARIES_SMPTE170M,
        /// <summary>Rec. 709 / sRGB.</summary>
        Rec709 = libcamera_color_space_primaries.LIBCAMERA_COLOR_SPACE_PRIMARIES_REC709,
        /// <summary>Rec. 2020 (wide gamut).</summary>
        Rec2020 = libcamera_color_space_primaries.LIBCAMERA_COLOR_SPACE_PRIMARIES_REC2020,
    }

    /// <summary>Transfer (gamma) function.</summary>
    public enum TransferFunctionKind
    {
        /// <summary>No gamma.</summary>
        Linear = libcamera_color_space_transfer_function.LIBCAMERA_COLOR_SPACE_TRANSFER_FUNCTION_LINEAR,
        /// <summary>sRGB curve.</summary>
        Srgb = libcamera_color_space_transfer_function.LIBCAMERA_COLOR_SPACE_TRANSFER_FUNCTION_SRGB,
        /// <summary>Rec. 709 curve.</summary>
        Rec709 = libcamera_color_space_transfer_function.LIBCAMERA_COLOR_SPACE_TRANSFER_FUNCTION_REC709,
    }

    /// <summary>YCbCr encoding, or None for RGB/raw.</summary>
    public enum YcbcrEncodingKind
    {
        /// <summary>Not YCbCr.</summary>
        None = libcamera_color_space_ycbcr_encoding.LIBCAMERA_COLOR_SPACE_YCBCR_ENCODING_NONE,
        /// <summary>Rec. 601 matrix.</summary>
        Rec601 = libcamera_color_space_ycbcr_encoding.LIBCAMERA_COLOR_SPACE_YCBCR_ENCODING_REC601,
        /// <summary>Rec. 709 matrix.</summary>
        Rec709 = libcamera_color_space_ycbcr_encoding.LIBCAMERA_COLOR_SPACE_YCBCR_ENCODING_REC709,
        /// <summary>Rec. 2020 matrix.</summary>
        Rec2020 = libcamera_color_space_ycbcr_encoding.LIBCAMERA_COLOR_SPACE_YCBCR_ENCODING_REC2020,
    }

    /// <summary>Full (0–255) or limited (16–235) range.</summary>
    public enum RangeKind
    {
        /// <summary>Full range.</summary>
        Full = libcamera_color_space_range.LIBCAMERA_COLOR_SPACE_RANGE_FULL,
        /// <summary>Limited (video) range.</summary>
        Limited = libcamera_color_space_range.LIBCAMERA_COLOR_SPACE_RANGE_LIMITED,
    }

    /// <summary>Raw sensor data.</summary>
    public static ColorSpace Raw => From(NativeMethods.libcamera_color_space_raw());
    /// <summary>sRGB, for RGB streams.</summary>
    public static ColorSpace Srgb => From(NativeMethods.libcamera_color_space_srgb());
    /// <summary>sYCC: sRGB primaries with full-range Rec.601 YCbCr, typical for JPEG.</summary>
    public static ColorSpace Sycc => From(NativeMethods.libcamera_color_space_sycc());
    /// <summary>SMPTE 170M (SD video).</summary>
    public static ColorSpace Smpte170m => From(NativeMethods.libcamera_color_space_smpte170m());
    /// <summary>Rec.709 (HD video).</summary>
    public static ColorSpace Rec709 => From(NativeMethods.libcamera_color_space_rec709());
    /// <summary>Rec.2020 (UHD video).</summary>
    public static ColorSpace Rec2020 => From(NativeMethods.libcamera_color_space_rec2020());

    /// <summary>libcamera's name for the colour space, e.g. <c>"sRGB"</c>.</summary>
    public override unsafe string ToString()
    {
        var native = ToNative();
        return NativeMethods.libcamera_color_space_to_string(&native) ?? "";
    }

    internal static ColorSpace From(libcamera_color_space n) =>
        new((PrimariesKind)n.primaries, (TransferFunctionKind)n.transfer_function, (YcbcrEncodingKind)n.ycbcr_encoding, (RangeKind)n.range);

    internal libcamera_color_space ToNative() => new()
    {
        primaries = (libcamera_color_space_primaries)Primaries,
        transfer_function = (libcamera_color_space_transfer_function)TransferFunction,
        ycbcr_encoding = (libcamera_color_space_ycbcr_encoding)YcbcrEncoding,
        range = (libcamera_color_space_range)Range,
    };
}
