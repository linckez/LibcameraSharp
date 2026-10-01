namespace LibcameraSharp;

/// <summary>Which codec a recording is encoded with.</summary>
public enum VideoCodec
{
    /// <summary>H.264: small files that every player opens. Dropping a frame corrupts the frames after it.</summary>
    H264,

    /// <summary>Motion JPEG: every frame stands alone, so frames can be dropped freely; files are about three times larger.</summary>
    Mjpeg,
}
