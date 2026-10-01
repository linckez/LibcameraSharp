using System.Runtime.InteropServices;
using LibcameraSharp.Native.Interop;
using Microsoft.Win32.SafeHandles;

namespace LibcameraSharp.Native;

/// <summary>
/// Owns one libcamera object and releases it exactly once, on <see cref="SafeHandle.Dispose()"/>
/// or, failing that, from the finalizer. The typed <c>Pointer</c> on each subclass is what the
/// generated <see cref="NativeMethods"/> take; it throws once the handle is closed.
/// </summary>
internal abstract class LibcameraHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    // Every libcamera object belongs to the one CameraManager in the process; once that is destroyed
    // they are all gone, and a finalizer that still calls into libcamera would free freed memory.
    // Finalization order is unspecified, so a handle the caller forgot to dispose can be finalized
    // after the manager — hence this flag rather than a reference to it.
    private static volatile bool _managerAlive;

    protected LibcameraHandle(nint handle) : base(ownsHandle: true) => SetHandle(handle);

    /// <summary>Called by <see cref="CameraManagerHandle"/> around the manager's own lifetime.</summary>
    internal static void SetManagerAlive(bool alive) => _managerAlive = alive;

    /// <summary>False once the camera manager is gone; a handle released after that must not call libcamera.</summary>
    protected static bool LibcameraAlive => _managerAlive;

    /// <summary>The raw handle for a native call; throws if the object has been released.</summary>
    protected nint Checked
    {
        get
        {
            ObjectDisposedException.ThrowIf(IsClosed || IsInvalid, this);
            return handle;
        }
    }
}

/// <summary>A <c>libcamera::CameraManager</c>.</summary>
internal sealed unsafe class CameraManagerHandle : LibcameraHandle
{
    public CameraManagerHandle() : base((nint)NativeMethods.libcamera_camera_manager_create()) => SetManagerAlive(true);

    public libcamera_camera_manager_t* Pointer => (libcamera_camera_manager_t*)Checked;

    protected override bool ReleaseHandle()
    {
        NativeMethods.libcamera_camera_manager_stop((libcamera_camera_manager_t*)handle);
        NativeMethods.libcamera_camera_manager_destroy((libcamera_camera_manager_t*)handle);
        SetManagerAlive(false);          // anything finalized after this must not call libcamera
        return true;
    }
}

/// <summary>One <c>shared_ptr&lt;libcamera::Camera&gt;</c>; releasing it drops that reference.</summary>
internal sealed unsafe class CameraHandle(libcamera_camera_t* camera) : LibcameraHandle((nint)camera)
{
    public libcamera_camera_t* Pointer => (libcamera_camera_t*)Checked;

    protected override bool ReleaseHandle()
    {
        if (!LibcameraAlive)
            return true;                 // the manager is gone and took this object with it

        NativeMethods.libcamera_camera_destroy((libcamera_camera_t*)handle);
        return true;
    }
}

/// <summary>A <c>libcamera::CameraConfiguration</c>.</summary>
internal sealed unsafe class CameraConfigurationHandle(libcamera_camera_configuration_t* config) : LibcameraHandle((nint)config)
{
    public libcamera_camera_configuration_t* Pointer => (libcamera_camera_configuration_t*)Checked;

    protected override bool ReleaseHandle()
    {
        if (!LibcameraAlive)
            return true;                 // the manager is gone and took this object with it

        NativeMethods.libcamera_camera_configuration_destroy((libcamera_camera_configuration_t*)handle);
        return true;
    }
}

/// <summary>A <c>libcamera::Request</c>. Only release it while libcamera doesn't hold it (see <see cref="Request"/>).</summary>
internal sealed unsafe class RequestHandle(libcamera_request* request) : LibcameraHandle((nint)request)
{
    public libcamera_request* Pointer => (libcamera_request*)Checked;

    protected override bool ReleaseHandle()
    {
        if (!LibcameraAlive)
            return true;                 // the manager is gone and took this object with it

        NativeMethods.libcamera_request_destroy((libcamera_request*)handle);
        return true;
    }
}

/// <summary>A <c>libcamera::FrameBufferAllocator</c>; destroying it frees every buffer it allocated.</summary>
internal sealed unsafe class FrameBufferAllocatorHandle(libcamera_framebuffer_allocator* allocator) : LibcameraHandle((nint)allocator)
{
    public libcamera_framebuffer_allocator* Pointer => (libcamera_framebuffer_allocator*)Checked;

    protected override bool ReleaseHandle()
    {
        if (!LibcameraAlive)
            return true;                 // the manager is gone and took this object with it

        NativeMethods.libcamera_framebuffer_allocator_destroy((libcamera_framebuffer_allocator*)handle);
        return true;
    }
}

/// <summary>A standalone <c>libcamera::ControlList</c> this binding created (lists inside requests belong to libcamera).</summary>
internal sealed unsafe class ControlListHandle() : LibcameraHandle((nint)NativeMethods.libcamera_control_list_create())
{
    public libcamera_control_list* Pointer => (libcamera_control_list*)Checked;

    protected override bool ReleaseHandle()
    {
        if (!LibcameraAlive)
            return true;                 // the manager is gone and took this object with it

        NativeMethods.libcamera_control_list_destroy((libcamera_control_list*)handle);
        return true;
    }
}

/// <summary>One <c>mmap</c> region, unmapped on release.</summary>
internal sealed unsafe class MappingHandle : LibcameraHandle
{
    private readonly nuint _length;

    private MappingHandle(nint address, nuint length) : base(address) => _length = length;

    /// <summary>Maps <paramref name="length"/> bytes of <paramref name="fd"/> from <paramref name="offset"/>, shared, read-only unless <paramref name="writable"/>.</summary>
    /// <exception cref="IOException">mmap failed; the message says why.</exception>
    public static MappingHandle Map(int fd, long offset, nuint length, bool writable)
    {
        var prot = Libc.PROT_READ | (writable ? Libc.PROT_WRITE : 0);
        var address = Libc.mmap(null, length, prot, Libc.MAP_SHARED, fd, offset);
        if (address == Libc.MAP_FAILED)
        {
            // An OS failure, not libcamera's: reported as .NET's own memory-mapped files report a failed mmap.
            var errno = Marshal.GetLastPInvokeError();
            throw new IOException($"mmap of {length} bytes of fd {fd} failed: {Marshal.GetPInvokeErrorMessage(errno)} (errno {errno})");
        }
        return new MappingHandle((nint)address, length);
    }

    public byte* Pointer => (byte*)Checked;

    protected override bool ReleaseHandle() => Libc.munmap((void*)handle, _length) == 0;
}
