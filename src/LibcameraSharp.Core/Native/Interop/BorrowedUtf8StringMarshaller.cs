using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace LibcameraSharp.Native.Interop;

/// <summary>
/// Marshals a <c>const char *</c> that libcamera keeps ownership of: the bytes are copied into a
/// managed string and the native pointer is left alone. The default UTF-8 marshaller would free it.
/// </summary>
[CustomMarshaller(typeof(string), MarshalMode.ManagedToUnmanagedOut, typeof(BorrowedUtf8StringMarshaller))]
internal static unsafe class BorrowedUtf8StringMarshaller
{
    /// <summary>Copies the NUL-terminated string, or returns null for a null pointer.</summary>
    public static string? ConvertToManaged(byte* unmanaged) => Marshal.PtrToStringUTF8((nint)unmanaged);
}
