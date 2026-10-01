using LibcameraSharp.Native;
using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Core;

/// <summary>
/// A camera libcamera found. Inspect <see cref="Properties"/> and <see cref="Controls"/> freely;
/// call <see cref="Acquire"/> for exclusive access before configuring and capturing.
/// </summary>
public unsafe class Camera : IDisposable
{
    private readonly CameraHandle _handle;
    private readonly CameraManager _manager;

    internal Camera(libcamera_camera_t* camera, CameraManager manager)
    {
        _handle = new CameraHandle(camera);
        _manager = manager;
    }

    internal libcamera_camera_t* Pointer => _handle.Pointer;

    /// <summary>Unique id, e.g. <c>/base/soc/i2c0mux/i2c@1/imx708@1a</c> on a Pi or <c>platform/vimc.0 Sensor B</c>.</summary>
    public string Id => NativeMethods.libcamera_camera_id(Pointer)!;       // the shim returns the id's c_str(), never null

    /// <summary>Static facts about the camera: model, sensor size, location, …</summary>
    public PropertyList Properties => new(NativeMethods.libcamera_camera_properties(Pointer));

    /// <summary>The controls this camera supports and their ranges.</summary>
    public ControlInfoMap Controls => new(NativeMethods.libcamera_camera_controls(Pointer));

    /// <summary>
    /// Builds a default configuration with one stream per role. Adjust it, validate, then
    /// <see cref="ActiveCamera.Configure"/>.
    /// </summary>
    /// <returns>The configuration, or null when the camera can't provide that combination of roles.</returns>
    public CameraConfiguration? GenerateConfiguration(params ReadOnlySpan<StreamRole> roles)
    {
        Span<libcamera_stream_role> native = stackalloc libcamera_stream_role[roles.Length];
        for (var i = 0; i < roles.Length; i++)
            native[i] = (libcamera_stream_role)roles[i];

        fixed (libcamera_stream_role* p = native)
        {
            var config = NativeMethods.libcamera_camera_generate_configuration(Pointer, p, (nuint)roles.Length);
            return config is null ? null : new CameraConfiguration(config);
        }
    }

    /// <summary>Takes exclusive ownership of the camera so it can be configured and started.</summary>
    /// <exception cref="CameraBusyException">Another process or object already holds it.</exception>
    /// <exception cref="LibcameraException">Any other libcamera failure.</exception>
    public ActiveCamera Acquire()
    {
        var acquired = NativeMethods.libcamera_camera_acquire(Pointer);
        // Camera::acquire is exclusive per process; -EBUSY is the commonest failure on a Pi and
        // deserves better than an errno the caller has to decode.
        if (-acquired == CameraBusyException.Ebusy)
            throw new CameraBusyException("acquire camera", Id);
        LibcameraException.ThrowIfError(acquired, "acquire camera");
        return (ActiveCamera)_manager.Track(new ActiveCamera(NativeMethods.libcamera_camera_copy(Pointer), _manager));
    }

    /// <summary>Releases this reference. An <see cref="ActiveCamera"/> acquired from it is independent and stays valid.</summary>
    public virtual void Dispose() => _handle.Dispose();

    /// <inheritdoc/>
    public override string ToString() => Id;
}
