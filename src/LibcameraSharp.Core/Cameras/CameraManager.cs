using LibcameraSharp.Native;
using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Core;

/// <summary>
/// Entry point: enumerates the cameras libcamera can see. Create one per process, keep it alive
/// for as long as you use cameras from it.
/// <code>
/// using var manager = new CameraManager();
/// using var camera = manager.Cameras[0].Acquire();
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// libcamera allows a single manager per process; creating a second while one is alive is
/// undefined behaviour in libcamera, so this class refuses it. <see cref="Dispose"/> disposes
/// every <see cref="Camera"/> and <see cref="ActiveCamera"/> obtained from the manager first,
/// acquired cameras before plain handles, so nothing outlives it.
/// </para>
/// <para>
/// A libcamera of another minor version than <see cref="GeneratedAgainstVersion"/> still runs; the first
/// manager writes one warning to standard error.
/// </para>
/// </remarks>
public sealed unsafe class CameraManager : IDisposable
{
    private static readonly Lock CurrentLock = new();
    private static CameraManager? _current;
    private static bool _versionChecked;

    private readonly List<WeakReference<Camera>> _cameras = [];
    private readonly CameraManagerHandle _handle;

    /// <summary>Creates and starts the manager, probing for cameras.</summary>
    /// <exception cref="InvalidOperationException">Another <see cref="CameraManager"/> is still alive; dispose it first.</exception>
    /// <exception cref="LibcameraException">libcamera failed to start, e.g. no permission on <c>/dev/media*</c>.</exception>
    public CameraManager()
    {
        lock (CurrentLock)
        {
            if (_current is not null)
                throw new InvalidOperationException("Only one CameraManager may exist at a time; dispose the previous one first.");
            WarnIfUnsupportedVersion();
            _handle = new CameraManagerHandle();
            _current = this;
        }
        LibcameraException.ThrowIfError(NativeMethods.libcamera_camera_manager_start(_handle.Pointer), "start camera manager");
    }

    /// <summary>The running libcamera's version string, e.g. <c>v0.7.2+rpt20260817</c>.</summary>
    public static string Version => NativeMethods.libcamera_version_string()!;   // the shim returns a c_str(), never null

    /// <summary>The libcamera version the bindings were generated from, e.g. <c>0.7.2</c>.</summary>
    public static string GeneratedAgainstVersion => NativeMethods.GeneratedAgainstLibcamera;

    /// <summary>Snapshot of the cameras currently available.</summary>
    public IReadOnlyList<Camera> Cameras
    {
        get
        {
            var list = NativeMethods.libcamera_camera_manager_cameras(_handle.Pointer);
            try
            {
                var count = (int)NativeMethods.libcamera_camera_list_size(list);
                var cameras = new Camera[count];
                for (var i = 0; i < count; i++)
                    cameras[i] = Track(new Camera(NativeMethods.libcamera_camera_list_get(list, (nuint)i), this));
                return cameras;
            }
            finally
            {
                NativeMethods.libcamera_camera_list_destroy(list);
            }
        }
    }

    /// <summary>Looks up a camera by the id shown in <see cref="Camera.Id"/>, or null if none matches.</summary>
    public Camera? Get(string id)
    {
        var camera = NativeMethods.libcamera_camera_manager_get_id(_handle.Pointer, id);
        return camera is null ? null : Track(new Camera(camera, this));
    }

    /// <summary>Stops libcamera after disposing every camera obtained from this manager.</summary>
    public void Dispose()
    {
        if (_handle.IsClosed)
            return;

        // Acquired cameras first: they hold the pipeline running. Then the plain handles.
        foreach (var camera in LiveCameras().OrderByDescending(c => c is ActiveCamera))
            camera.Dispose();
        _cameras.Clear();

        _handle.Dispose();
        lock (CurrentLock)
        {
            if (_current == this)
                _current = null;
        }
    }

    internal Camera Track(Camera camera)
    {
        lock (_cameras)
            _cameras.Add(new WeakReference<Camera>(camera));
        return camera;
    }

    private List<Camera> LiveCameras()
    {
        lock (_cameras)
            return [.. _cameras.Select(w => w.TryGetTarget(out var c) ? c : null).OfType<Camera>()];
    }

    // Warns once when libcamera's major.minor differs from the generated one; never refuses. Runs under CurrentLock.
    private static void WarnIfUnsupportedVersion()
    {
        if (_versionChecked)
            return;
        _versionChecked = true;

        if (VersionWarning(Version, GeneratedAgainstVersion) is { } warning)
            Console.Error.WriteLine(warning);
    }

    /// <summary>The warning for <paramref name="running"/> under bindings generated for <paramref name="generatedAgainst"/>, or null when their major.minor agree.</summary>
    internal static string? VersionWarning(string running, string generatedAgainst)
    {
        static string MajorMinor(string v) => string.Join('.', v.TrimStart('v').Split('.', '+', '-').Take(2));

        var supported = MajorMinor(generatedAgainst);
        if (MajorMinor(running) == supported)
            return null;

        return $"LibcameraSharp: running libcamera {running}, but these bindings support {supported}.x (generated for {generatedAgainst}). " +
               "Continuing at your own risk: control ids can differ between minor versions, so a control may set a different one without any error.";
    }
}
