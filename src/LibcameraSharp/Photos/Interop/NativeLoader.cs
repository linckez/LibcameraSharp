using System.Reflection;
using System.Runtime.InteropServices;

namespace LibcameraSharp.Photos.Interop;

/// <summary>
/// Locates this assembly's shims, <c>libtiff-shim.so</c> and <c>libexif-shim.so</c>, for their
/// generated <c>NativeMethods</c> imports. .NET allows one resolver per assembly, so this one serves both.
/// </summary>
/// <remarks>
/// Resolution order: the shim's environment variable (full path to the .so); <c>runtimes/linux-&lt;arch&gt;/native/</c>
/// next to the application, which is where the file lands for project references and self-contained
/// publishes; then the runtime's default probing, which covers the NuGet package case. Set the variable
/// when running against a locally built shim.
/// </remarks>
internal static class NativeLoader
{
    // Each shim's library name, as its generated imports name it, and the variable holding an explicit path.
    private static readonly Dictionary<string, string> PathVariables = new()
    {
        [Tiff.NativeMethods.LibraryName] = "LIBCAMERASHARP_TIFF_SHIM",
        [Exif.NativeMethods.LibraryName] = "LIBCAMERASHARP_EXIF_SHIM",
    };

    private static readonly Lazy<bool> Registered = new(() =>
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeLoader).Assembly, Resolve);
        return true;
    });

    /// <summary>Installs the resolver once; called from each shim's <c>NativeMethods</c> static constructor.</summary>
    internal static void Register() => _ = Registered.Value;

    private static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        // Only intercept our own shims; everything else keeps the default behaviour.
        if (!PathVariables.TryGetValue(libraryName, out var variable))
            return IntPtr.Zero;

        var explicitPath = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrEmpty(explicitPath))
            return NativeLibrary.Load(explicitPath);

        // Loose runtimes/<rid>/native folders aren't probed by the runtime unless they come from a package.
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            var other => other.ToString().ToLowerInvariant(),
        };
        var local = Path.Combine(AppContext.BaseDirectory, "runtimes", $"linux-{arch}", "native", libraryName + ".so");
        if (File.Exists(local))
            return NativeLibrary.Load(local);

        // Returning zero hands over to the runtime's normal probing for the library name.
        return IntPtr.Zero;
    }
}
