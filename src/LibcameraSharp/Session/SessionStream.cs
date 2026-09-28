namespace LibcameraSharp;

/// <summary>The streams a session can configure: the one you capture, a smaller preview, and the sensor's raw data.</summary>
internal enum SessionStream
{
    Capture,
    Preview,
    Raw,
}
