namespace LibcameraSharp.Photos.Interop.Tiff;

/// <summary>The non-generated half of <see cref="NativeMethods"/>: wires up the loader before the first import.</summary>
internal static partial class NativeMethods
{
    // Runs before the first P/Invoke, so the shim path override is honoured from the start.
    static NativeMethods() => NativeLoader.Register();
}
