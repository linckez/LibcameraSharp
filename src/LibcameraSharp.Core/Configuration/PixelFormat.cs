using System.Runtime.InteropServices;
using System.Text;

namespace LibcameraSharp;

/// <summary>
/// A pixel format: a DRM/V4L2 fourcc code plus a modifier (packing, tiling). Use the named
/// values in <see cref="PixelFormats"/>, e.g. <see cref="PixelFormats.RGB888"/>.
/// </summary>
/// <param name="Fourcc">DRM fourcc code, e.g. <c>0x34324752</c> for <c>RG24</c>.</param>
/// <param name="Modifier">DRM format modifier; 0 for plain linear layouts.</param>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct PixelFormat(uint Fourcc, ulong Modifier)
{
    /// <summary>A format with no fourcc, which libcamera treats as "unset".</summary>
    public static readonly PixelFormat Invalid = default;

    /// <summary>True when the format has a fourcc code.</summary>
    public bool IsValid => Fourcc != 0;

    /// <summary>The libcamera name of a known format (e.g. <c>"SRGGB10_CSI2P"</c>), or null when not in <see cref="PixelFormats.All"/>.</summary>
    public string? Name => PixelFormats.NameOf(this);

    /// <summary>Looks up a format by its libcamera name, e.g. <c>"YUV420"</c>.</summary>
    /// <returns>The format, or null if the name is unknown.</returns>
    public static PixelFormat? FromName(string name) => PixelFormats.ByName(name);

    /// <summary>The libcamera name when known, otherwise the fourcc characters and modifier, like libcamera's own <c>toString()</c>.</summary>
    public override string ToString()
    {
        if (!IsValid)
            return "<INVALID>";
        return Name ?? FourccText();
    }

    private string FourccText()
    {
        // Render the four code bytes as characters; non-printables become '.', like libcamera.
        var sb = new StringBuilder(24);
        for (var i = 0; i < 4; i++)
        {
            var c = (char)((Fourcc >> (8 * i)) & 0x7F);
            sb.Append(char.IsControl(c) ? '.' : c);
        }
        if ((Fourcc & 0x8000_0000u) != 0)
            sb.Append("-BE");
        if (Modifier != 0)
            sb.Append($"-0x{Modifier:X}");
        return sb.ToString();
    }
}
