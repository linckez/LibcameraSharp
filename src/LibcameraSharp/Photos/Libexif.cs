using System.Runtime.InteropServices;
using LibcameraSharp.Photos.Interop.Exif;

namespace LibcameraSharp;

/// <summary>
/// Builds a JPEG's EXIF block with the system's libexif (<c>libexif.so.12</c>), via <c>libexif-shim.so</c>
/// (the imports in <see cref="NativeMethods"/> are generated from its header). Only what
/// <see cref="ExifSegment"/> needs.
/// </summary>
internal static unsafe class Libexif
{
    private const string LibraryName = "libexif.so.12";

    private static readonly Lazy<bool> Loaded = new(() =>
    {
        if (NativeLibrary.TryLoad(LibraryName, typeof(Libexif).Assembly, null, out _))
            return true;
        Console.Error.WriteLine($"LibcameraSharp: {LibraryName} was not found, so photos are saved without EXIF. Install it with 'apt install libexif12'.");
        return false;
    });

    /// <summary>Whether libexif could be loaded. Warns once on standard error when it can't.</summary>
    public static bool IsAvailable => Loaded.Value;

    /// <summary>A new, empty EXIF block in little-endian byte order. Release it with <see cref="Free"/>.</summary>
    public static nint Create()
    {
        var data = NativeMethods.exif_block_new();
        if (data is null)
            throw new InvalidOperationException("libexif could not allocate EXIF data.");
        return (nint)data;
    }

    /// <summary>
    /// Sets <paramref name="tag"/> to <paramref name="value"/>, stored as <paramref name="count"/> values of
    /// TIFF type <paramref name="format"/>, in IFD0 or, when <paramref name="exifDirectory"/>, the EXIF directory.
    /// </summary>
    public static void Set(nint data, bool exifDirectory, ushort tag, TiffType format, uint count, ReadOnlySpan<byte> value)
    {
        var ifd = exifDirectory ? exif_block_ifd.EXIF_BLOCK_IFD_EXIF : exif_block_ifd.EXIF_BLOCK_IFD_0;
        fixed (byte* p = value)
        {
            if (NativeMethods.exif_block_set((_ExifData*)data, ifd, tag, (ushort)format, count, p, (uint)value.Length) != 0)
                throw new InvalidOperationException($"libexif could not set tag {tag}.");
        }
    }

    /// <summary>The block's bytes, starting with <c>Exif\0\0</c>, ready for a JPEG's APP1 segment.</summary>
    public static byte[] Save(nint data)
    {
        uint length = 0;
        var bytes = NativeMethods.exif_block_save((_ExifData*)data, &length);
        if (bytes is null || length == 0)
            throw new InvalidOperationException("libexif could not write the EXIF data.");
        try
        {
            return new ReadOnlySpan<byte>(bytes, (int)length).ToArray();
        }
        finally
        {
            NativeMethods.exif_block_free_bytes(bytes);
        }
    }

    /// <summary>Releases a block from <see cref="Create"/>.</summary>
    public static void Free(nint data) => NativeMethods.exif_block_free((_ExifData*)data);
}
