using FFmpeg.AutoGen;

namespace LibcameraSharp;

/// <summary>
/// Loads the system's FFmpeg libraries and explains their return codes. The libraries are the system's —
/// this package binds them, it does not carry them.
/// </summary>
/// <remarks>Raspberry Pi OS and Debian install them as <c>libavcodec</c> and <c>libavformat</c>.</remarks>
internal static class Libav
{
    private static bool _initialised;

    internal static void Initialise()
    {
        if (_initialised)
            return;

        // An empty path hands FFmpeg.AutoGen's versioned names (libavcodec.so.61 and so on) to the
        // system's dynamic loader, which finds them as it does for any program.
        ffmpeg.RootPath = "";
        try
        {
            DynamicallyLoadedBindings.Initialize();
            _ = ffmpeg.av_version_info();
            _initialised = true;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "The FFmpeg libraries could not be loaded. Install them (libavcodec, libavformat, libswscale); " +
                "if they live somewhere unusual, add that folder to LD_LIBRARY_PATH. This package binds FFmpeg 7.1, " +
                "which Raspberry Pi OS trixie ships.", exception);
        }
    }

    /// <summary>Throws when <paramref name="code"/> is a libav error, with the message libav gives.</summary>
    internal static unsafe int Check(int code, string operation)
    {
        if (code >= 0)
            return code;

        var buffer = stackalloc byte[ffmpeg.AV_ERROR_MAX_STRING_SIZE];
        ffmpeg.av_strerror(code, buffer, (ulong)ffmpeg.AV_ERROR_MAX_STRING_SIZE);
        var message = System.Runtime.InteropServices.Marshal.PtrToStringAnsi((nint)buffer) ?? code.ToString();
        throw new InvalidOperationException($"libav: {operation} failed: {message} ({code})");
    }
}
