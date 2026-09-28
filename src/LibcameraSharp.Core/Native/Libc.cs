using System.Runtime.InteropServices;

namespace LibcameraSharp.Native;

/// <summary>
/// Mapping DMA-BUF file descriptors. .NET's <c>MemoryMappedFile</c> needs a sizable regular file,
/// which a DMA-BUF is not, so this is the one place the binding calls the C runtime directly.
/// </summary>
internal static unsafe partial class Libc
{
    /// <summary>Pseudo library name; <see cref="NativeLoader"/> maps it to the process's global scope.</summary>
    public const string LibraryName = "libc";

    // Linux ABI values; stable across glibc and musl.
    public const int PROT_READ = 0x1;
    public const int PROT_WRITE = 0x2;
    public const int MAP_SHARED = 0x01;
    public static readonly void* MAP_FAILED = (void*)-1;

    [LibraryImport(LibraryName, SetLastError = true)]
    public static partial void* mmap(void* addr, nuint length, int prot, int flags, int fd, long offset);

    [LibraryImport(LibraryName, SetLastError = true)]
    public static partial int munmap(void* addr, nuint length);
}
