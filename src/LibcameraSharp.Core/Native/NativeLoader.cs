using System.Reflection;
using System.Runtime.InteropServices;
using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Native;

/// <summary>
/// Locates <c>libcamera-shim.so</c> for the generated <see cref="NativeMethods"/> imports.
/// </summary>
/// <remarks>
/// Resolution order: the <c>LIBCAMERASHARP_SHIM</c> environment variable (full path to the .so);
/// <c>runtimes/linux-&lt;arch&gt;/native/</c> next to the application, which is where the file
/// lands for project references and self-contained publishes; then the runtime's default probing,
/// which covers the NuGet package case. Set the variable when running against a locally built shim.
/// </remarks>
internal static class NativeLoader
{
    /// <summary>Environment variable holding an explicit path to the shim library.</summary>
    public const string PathVariable = "LIBCAMERASHARP_SHIM";

    /// <summary>Installs the resolver; called once from <see cref="NativeMethods"/>'s static constructor.</summary>
    internal static void Register()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeLoader).Assembly, Resolve);
    }

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        // C runtime calls (free, mmap) go through the process's global symbol scope so that an
        // LD_PRELOADed allocator or sanitizer interposes them, exactly as it does for native callers.
        if (libraryName == Libc.LibraryName)
            return NativeLibrary.GetMainProgramHandle();

        // Only intercept our own shim; everything else keeps the default behaviour.
        if (libraryName != NativeMethods.LibraryName)
            return IntPtr.Zero;

        var explicitPath = Environment.GetEnvironmentVariable(PathVariable);
        if (!string.IsNullOrEmpty(explicitPath))
            return NativeLibrary.Load(explicitPath);

        // Loose runtimes/<rid>/native folders aren't probed by the runtime unless they come from a package.
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            var other => other.ToString().ToLowerInvariant(),
        };
        var local = Path.Combine(AppContext.BaseDirectory, "runtimes", $"linux-{arch}", "native", "libcamera-shim.so");
        if (File.Exists(local))
            return NativeLibrary.Load(local);

        // Returning zero hands over to the runtime's normal probing for "libcamera-shim".
        return IntPtr.Zero;
    }
}
