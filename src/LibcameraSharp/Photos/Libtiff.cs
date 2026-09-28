using System.Runtime.InteropServices;
using LibcameraSharp.Photos.Interop.Tiff;

namespace LibcameraSharp;

/// <summary>
/// Writes TIFF files through the system's libtiff (<c>libtiff.so.6</c>), via <c>libtiff-shim.so</c>
/// (the imports in <see cref="NativeMethods"/> are generated from its header). Only what
/// <see cref="DngWriter"/> needs.
/// </summary>
/// <remarks>
/// Every tag setter first asks libtiff how it reads that tag's value, and throws if that isn't how the
/// setter passes it: libtiff's <c>TIFFSetField</c> can't tell a wrong argument from a right one.
/// </remarks>
internal static unsafe class Libtiff
{
    private const string LibraryName = "libtiff.so.6";

    // libtiff's TIFFDataType values the setters accept.
    private const int DataAscii = 2, DataShort = 3, DataLong = 4, DataRational = 5, DataSRational = 10, DataFloat = 11,
        DataDouble = 12, DataLong8 = 16, DataIfd8 = 18;

    /// <summary>Creates <paramref name="path"/> as a new TIFF file, open for writing.</summary>
    /// <exception cref="InvalidOperationException">libtiff isn't installed, or the file can't be created.</exception>
    public static nint Create(string path)
    {
        if (!NativeLibrary.TryLoad(LibraryName, typeof(Libtiff).Assembly, null, out _))
            throw new InvalidOperationException($"Saving a DNG needs libtiff ({LibraryName}). Install it with 'apt install libtiff6'.");
        var tif = NativeMethods.tiff_open(path, "w");
        if (tif is null)
            throw new InvalidOperationException($"libtiff could not create {path}.");
        return (nint)tif;
    }

    /// <summary>Sets a tag holding one 16- or 32-bit unsigned value.</summary>
    public static void Set(nint tif, ushort tag, uint value)
    {
        var field = Field(tif, tag);
        Require(field, tag, "one 16- or 32-bit integer", !PassesCount(field) && NativeMethods.tiff_field_read_count(field) == 1
            && NativeMethods.tiff_field_data_type(field) is DataShort or DataLong && NativeMethods.tiff_field_value_size(field) is 2 or 4);
        Check(NativeMethods.tiff_set_field_uint32((tiff*)tif, tag, value), tag);
    }

    /// <summary>Sets a tag holding a directory offset, such as the EXIF directory's.</summary>
    public static void SetOffset(nint tif, ushort tag, ulong value)
    {
        var field = Field(tif, tag);
        Require(field, tag, "one 64-bit offset", !PassesCount(field)
            && NativeMethods.tiff_field_data_type(field) is DataIfd8 or DataLong8 && NativeMethods.tiff_field_value_size(field) == 8);
        Check(NativeMethods.tiff_set_field_uint64((tiff*)tif, tag, value), tag);
    }

    /// <summary>Sets a tag holding one rational, given as a number.</summary>
    public static void Set(nint tif, ushort tag, double value)
    {
        var field = Field(tif, tag);
        Require(field, tag, "one number", !PassesCount(field) && NativeMethods.tiff_field_read_count(field) == 1
            && NativeMethods.tiff_field_data_type(field) is DataRational or DataSRational or DataFloat or DataDouble);
        Check(NativeMethods.tiff_set_field_double((tiff*)tif, tag, value), tag);
    }

    /// <summary>Sets an ASCII tag.</summary>
    public static void Set(nint tif, ushort tag, string value)
    {
        var field = Field(tif, tag);
        Require(field, tag, "a string", !PassesCount(field) && NativeMethods.tiff_field_data_type(field) == DataAscii);
        Check(NativeMethods.tiff_set_field_string((tiff*)tif, tag, value), tag);
    }

    /// <summary>Sets a tag whose number of values the tag itself fixes, such as DNGVersion's four bytes.</summary>
    public static void SetFixed<T>(nint tif, ushort tag, ReadOnlySpan<T> values) where T : unmanaged
    {
        var field = Field(tif, tag);
        Require(field, tag, $"{values.Length} values of {sizeof(T)} bytes", !PassesCount(field)
            && NativeMethods.tiff_field_read_count(field) == values.Length && NativeMethods.tiff_field_value_size(field) == sizeof(T));
        fixed (T* p = values)
            Check(NativeMethods.tiff_set_field_array((tiff*)tif, tag, p), tag);
    }

    /// <summary>Sets a tag holding any number of values: libtiff is told how many.</summary>
    public static void Set<T>(nint tif, ushort tag, ReadOnlySpan<T> values) where T : unmanaged
    {
        var field = Field(tif, tag);
        Require(field, tag, $"a count, then values of {sizeof(T)} bytes", PassesCount(field)
            && NativeMethods.tiff_field_count_size(field) is 2 or 4 && NativeMethods.tiff_field_value_size(field) == sizeof(T));
        fixed (T* p = values)
            Check(NativeMethods.tiff_set_field_counted_array((tiff*)tif, tag, values.Length, p), tag);
    }

    /// <summary>Writes row <paramref name="index"/> of the current directory's image.</summary>
    public static void WriteRow<T>(nint tif, ReadOnlySpan<T> row, uint index) where T : unmanaged
    {
        fixed (T* p = row)
        {
            if (NativeMethods.tiff_write_scanline((tiff*)tif, p, index) != 1)
                throw new InvalidOperationException($"libtiff could not write row {index}.");
        }
    }

    /// <summary>Writes the current directory and starts a new one.</summary>
    public static void WriteDirectory(nint tif)
    {
        if (NativeMethods.tiff_write_directory((tiff*)tif) != 1)
            throw new InvalidOperationException("libtiff could not write a directory.");
    }

    /// <summary>Writes the current directory so far and returns its offset in the file.</summary>
    public static ulong CheckpointDirectory(nint tif)
    {
        if (NativeMethods.tiff_checkpoint_directory((tiff*)tif) != 1)
            throw new InvalidOperationException("libtiff could not write a directory.");
        return NativeMethods.tiff_current_directory_offset((tiff*)tif);
    }

    /// <summary>Makes sure this libtiff knows the CFA tags a DNG needs; libtiff 4.5.1 lost them.</summary>
    public static void RegisterCfaFields(nint tif)
    {
        if (NativeMethods.tiff_register_cfa_fields((tiff*)tif) != 0)
            throw new InvalidOperationException("libtiff could not register the CFA tags a DNG needs.");
    }

    /// <summary>Starts the EXIF directory; its tags follow.</summary>
    public static void CreateExifDirectory(nint tif)
    {
        if (NativeMethods.tiff_create_exif_directory((tiff*)tif) != 0)
            throw new InvalidOperationException("libtiff could not create the EXIF directory.");
    }

    /// <summary>Makes directory <paramref name="index"/> (from 0) the current one again.</summary>
    public static void SetDirectory(nint tif, uint index)
    {
        if (NativeMethods.tiff_set_directory((tiff*)tif, index) != 1)
            throw new InvalidOperationException($"libtiff could not return to directory {index}.");
    }

    /// <summary>Removes directory <paramref name="number"/> (from 1) from the file's chain.</summary>
    public static void UnlinkDirectory(nint tif, uint number) => NativeMethods.tiff_unlink_directory((tiff*)tif, number);

    /// <summary>Finishes and closes the file.</summary>
    public static void Close(nint tif) => NativeMethods.tiff_close((tiff*)tif);

    private static _TIFFField* Field(nint tif, ushort tag)
    {
        var field = NativeMethods.tiff_field((tiff*)tif, tag);
        if (field is null)
            throw new InvalidOperationException($"This libtiff doesn't know tag {tag}.");
        return field;
    }

    private static bool PassesCount(_TIFFField* field) => NativeMethods.tiff_field_passes_count(field) != 0;

    private static void Require(_TIFFField* field, ushort tag, string expected, bool matches)
    {
        if (matches)
            return;
        throw new InvalidOperationException(
            $"This libtiff takes tag {tag} differently from how LibcameraSharp passes it ({expected}): " +
            $"type {NativeMethods.tiff_field_data_type(field)}, count passed {PassesCount(field)}, " +
            $"read count {NativeMethods.tiff_field_read_count(field)}, value size {NativeMethods.tiff_field_value_size(field)}.");
    }

    private static void Check(int result, ushort tag)
    {
        if (result != 1)
            throw new InvalidOperationException($"libtiff could not set tag {tag}.");
    }
}
