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
