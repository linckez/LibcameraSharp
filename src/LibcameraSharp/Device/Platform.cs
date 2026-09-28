using System.Runtime.InteropServices;

namespace LibcameraSharp;

/// <summary>Which Raspberry Pi imaging hardware this is running on.</summary>
internal enum Platform
{
    /// <summary>No video device answered, so there is no Raspberry Pi ISP here.</summary>
    Missing,
    /// <summary>Video devices answered, but none was a Raspberry Pi ISP — a USB camera, or a test pipeline.</summary>
    Unknown,
    /// <summary>The pre-libcamera firmware stack (<c>bm2835 mmal</c>).</summary>
    Legacy,
    /// <summary>Pi 4 and earlier: the VideoCore IV ISP (<c>bcm2835-isp</c>).</summary>
    Vc4,
    /// <summary>Pi 5: the PiSP back end (<c>pispbe</c>).</summary>
    Pisp,
}

/// <summary>
/// Detects the imaging hardware, which some controls have to be spelled differently for — notably
/// digital zoom, where VC4 takes one <c>ScalerCrop</c> and PiSP takes one per stream.
/// </summary>
/// <remarks>
/// Asks each <c>/dev/video*</c> device for its card name (<c>VIDIOC_QUERYCAP</c>) and matches the
/// Raspberry Pi ISPs.
/// </remarks>
internal static partial class PlatformDetection
{
    private const int MaxVideoDevices = 64;
    private const int ORdwr = 2;

    // _IOR('V', 0, struct v4l2_capability): read direction, 104-byte payload.
    private const uint VidiocQuerycap = 0x80685600;

    private static Platform? _cached;

    /// <summary>The platform, detected once per process.</summary>
    public static Platform Current => _cached ??= Detect();

    /// <summary>Re-runs detection, ignoring the cached answer. For tests.</summary>
    internal static Platform Detect()
    {
        var unknown = false;
        for (var num = 0; num < MaxVideoDevices; num++)
        {
            var device = $"/dev/video{num}";
            if (!File.Exists(device))
                continue;

            var fd = Open(device, ORdwr);
            if (fd < 0)
                continue;

            V4l2Capability caps = default;
            int result;
            unsafe
            {
                result = Ioctl(fd, VidiocQuerycap, &caps);
            }
            Close(fd);
            if (result != 0)
                continue;

            var driver = AsString(caps.Driver);
            var card = AsString(caps.Card);

            // USB webcams are never the ISP.
            if (driver == "uvcvideo")
                continue;

            switch (card)
            {
                case "bcm2835-isp": return Platform.Vc4;
                case "pispbe": return Platform.Pisp;
                case "bm2835 mmal": return Platform.Legacy;
                default: unknown = true; break;
            }
        }
        return unknown ? Platform.Unknown : Platform.Missing;
    }

    private static unsafe string AsString(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0);
        return System.Text.Encoding.UTF8.GetString(end < 0 ? field : field[..end]);
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct V4l2Capability
    {
        private fixed byte _driver[16];
        private fixed byte _card[32];
        private fixed byte _busInfo[32];
        private readonly uint _version;
        private readonly uint _capabilities;
        private readonly uint _deviceCaps;
        private fixed uint _reserved[3];

        public ReadOnlySpan<byte> Driver { get { fixed (byte* p = _driver) return new(p, 16); } }
        public ReadOnlySpan<byte> Card { get { fixed (byte* p = _card) return new(p, 32); } }
    }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static unsafe partial int Ioctl(int fd, uint request, V4l2Capability* argument);

    [LibraryImport("libc", EntryPoint = "close")]
    private static partial int Close(int fd);
}
