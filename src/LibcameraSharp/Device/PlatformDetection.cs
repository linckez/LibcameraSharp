using System.Runtime.InteropServices;

namespace LibcameraSharp;

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
    // The C runtime, for open/ioctl/close on the video devices.
    private const string CRuntime = "libc";

    private const int MaxVideoDevices = 64;
    private const int ORdwr = 2;

    // The card names the Raspberry Pi ISPs, and the pre-libcamera firmware camera, report.
    private const string Vc4Card = "bcm2835-isp", PispCard = "pispbe", LegacyCard = "bm2835 mmal";

    // v4l2_capability's fixed-size text fields (videodev2.h).
    private const int DriverLength = 16, CardLength = 32, BusInfoLength = 32;

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
                case Vc4Card: return Platform.Vc4;
                case PispCard: return Platform.Pisp;
                case LegacyCard: return Platform.Legacy;
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
        private fixed byte _driver[DriverLength];
        private fixed byte _card[CardLength];
        private fixed byte _busInfo[BusInfoLength];
        private readonly uint _version;
        private readonly uint _capabilities;
        private readonly uint _deviceCaps;
        private fixed uint _reserved[3];

        public ReadOnlySpan<byte> Driver { get { fixed (byte* p = _driver) return new(p, DriverLength); } }
        public ReadOnlySpan<byte> Card { get { fixed (byte* p = _card) return new(p, CardLength); } }
    }

    [LibraryImport(CRuntime, EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags);

    [LibraryImport(CRuntime, EntryPoint = "ioctl", SetLastError = true)]
    private static unsafe partial int Ioctl(int fd, uint request, V4l2Capability* argument);

    [LibraryImport(CRuntime, EntryPoint = "close")]
    private static partial int Close(int fd);
}
