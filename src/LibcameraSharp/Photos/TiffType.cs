namespace LibcameraSharp;

/// <summary>
/// TIFF field types (TIFF 6.0 §2), which EXIF uses unchanged and libexif calls formats; BigTIFF adds the 64-bit
/// <see cref="Long8"/> and <see cref="Ifd8"/>, as libtiff's <c>TIFFDataType</c> numbers them.
/// </summary>
internal enum TiffType : ushort
{
    Byte = 1, Ascii = 2, Short = 3, Long = 4, Rational = 5, SByte = 6, Undefined = 7,
    SShort = 8, SLong = 9, SRational = 10, Float = 11, Double = 12, Long8 = 16, Ifd8 = 18,
}
