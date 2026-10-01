using LibcameraSharp.Native.Interop;

namespace LibcameraSharp;

/// <summary>
/// Controls libcamera's native logging, which otherwise prints INFO lines to stderr.
/// <code>
/// LibcameraLog.SetTarget(LibcameraLogTarget.None);                               // silence everything, incl. the startup banner
/// using var manager = new CameraManager();
/// LibcameraLog.SetLevel(LibcameraLogLevel.Error);                                // from here on: errors only, every category
/// LibcameraLog.SetLevel(LibcameraLogLevel.Debug, LibcameraLogCategories.RPI);    // …except one pipeline's category
/// </code>
/// </summary>
/// <remarks>
/// libcamera registers a log category the first time it logs to it, and <see cref="SetLevel(LibcameraLogLevel, string)"/>
/// only affects categories registered at that moment — so call it after the <see cref="CameraManager"/>
/// exists, and expect categories a pipeline registers later (e.g. during <see cref="ActiveCamera.Start"/>)
/// to keep their default until set again. The startup banner is printed while the manager starts;
/// only <see cref="SetTarget"/>, <see cref="SetFile"/> or the <c>LIBCAMERA_LOG_LEVELS=*:ERROR</c>
/// environment variable (set before the process starts; libcamera reads it once) can suppress it.
/// Names match exactly; the no-category overload walks <see cref="LibcameraLogCategories.All"/>.
/// </remarks>
public static class LibcameraLog
{
    /// <summary>Sets the threshold for every category in <see cref="LibcameraLogCategories.All"/>.</summary>
    public static void SetLevel(LibcameraLogLevel level)
    {
        foreach (var category in LibcameraLogCategories.All)
            SetLevel(level, category);
    }

    /// <summary>Sets the threshold for one category, e.g. <see cref="LibcameraLogCategories.Camera"/>. Unknown names are ignored by libcamera.</summary>
    public static void SetLevel(LibcameraLogLevel level, string category)
    {
        var name = level switch
        {
            LibcameraLogLevel.Debug => "DEBUG",
            LibcameraLogLevel.Info => "INFO",
            LibcameraLogLevel.Warning => "WARN",
            LibcameraLogLevel.Error => "ERROR",
            LibcameraLogLevel.Fatal => "FATAL",
            _ => throw new ArgumentOutOfRangeException(nameof(level)),
        };
        NativeMethods.libcamera_log_set_level(category, name);
    }

    /// <summary>Sends the log to a file, replacing the current destination.</summary>
    /// <param name="path">File to append to.</param>
    /// <param name="color">Keep ANSI colour codes.</param>
    public static void SetFile(string path, bool color = false) =>
        LibcameraException.ThrowIfError(NativeMethods.libcamera_log_set_file(path, color), "set log file");

    /// <summary>Sends the log to standard error (libcamera's default) or standard output.</summary>
    public static void SetStream(bool stdout = false, bool color = false)
    {
        var stream = stdout ? libcamera_logging_stream.LIBCAMERA_LOGGING_STREAM_STDOUT : libcamera_logging_stream.LIBCAMERA_LOGGING_STREAM_STDERR;
        LibcameraException.ThrowIfError(NativeMethods.libcamera_log_set_stream(stream, color), "set log stream");
    }

    /// <summary>Discards the log or sends it to syslog.</summary>
    public static void SetTarget(LibcameraLogTarget target) =>
        LibcameraException.ThrowIfError(NativeMethods.libcamera_log_set_target((libcamera_logging_target)target), "set log target");
}
