using System.Buffers.Binary;

namespace LibcameraSharp;

/// <summary>Writes a frame as an uncompressed 24-bit BMP.</summary>
/// <remarks>Rows are written top-down and padded to 4 bytes; any of the four RGB formats is reordered to B,G,R.</remarks>
internal static class BmpWriter
{
    // A 14-byte file header, then the 40-byte BITMAPINFOHEADER; one plane of 24-bit pixels; rows padded to 4 bytes.
    private const int FileHeaderSize = 14, InfoHeaderSize = 40, HeaderSize = FileHeaderSize + InfoHeaderSize;
    private const ushort Planes = 1, BitsPerPixel = 24;
    private const int RowAlignment = 4, BytesPerPixelOut = BitsPerPixel / 8;

    // The resolution written, in pixels per metre (about 2540 dpi); BMP readers ignore it for display.
    private const uint PixelsPerMetre = 100_000;

    /// <summary>Encodes <paramref name="pixels"/> (an RGB format) to <paramref name="output"/>.</summary>
    /// <exception cref="NotSupportedException">The frame's format isn't one of the four RGB formats.</exception>
    public static void Save(FramePixels pixels, Stream output)
    {
        var (width, height) = ((int)pixels.Size.Width, (int)pixels.Size.Height);
        var line = width * BytesPerPixelOut;
        var pitch = (line + RowAlignment - 1) & ~(RowAlignment - 1);
        var pad = pitch - line;
        var swap = pixels.Format == PixelFormats.BGR888 || pixels.Format == PixelFormats.XBGR8888;      // R,G,B in memory → B,G,R on disk
        if (PixelLayout.BytesPerPixel(pixels.Format) is not ((3 or 4) and var bytesPerPixel))
            throw new NotSupportedException($"Stream format {pixels.Format} not supported for BMP; use an RGB format.");

        // The file header (type, file size, pixel offset), then the info header; a negative height means rows top-down.
        Span<byte> header = stackalloc byte[HeaderSize];
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(header[2..], (uint)(HeaderSize + height * pitch));
        BinaryPrimitives.WriteUInt32LittleEndian(header[10..], HeaderSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header[FileHeaderSize..], InfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[18..], width);
        BinaryPrimitives.WriteInt32LittleEndian(header[22..], -height);
        BinaryPrimitives.WriteUInt16LittleEndian(header[26..], Planes);
        BinaryPrimitives.WriteUInt16LittleEndian(header[28..], BitsPerPixel);
        BinaryPrimitives.WriteUInt32LittleEndian(header[38..], PixelsPerMetre);
        BinaryPrimitives.WriteUInt32LittleEndian(header[42..], PixelsPerMetre);
        output.Write(header);

        var row = new byte[pitch];
        for (var y = 0; y < height; y++)
        {
            var src = pixels.Data.AsSpan(y * pixels.Stride, width * bytesPerPixel);   // one row, without its padding
            for (var x = 0; x < width; x++)
            {
                var p = x * bytesPerPixel;
                row[3 * x] = swap ? src[p + 2] : src[p];
                row[3 * x + 1] = src[p + 1];
                row[3 * x + 2] = swap ? src[p] : src[p + 2];
            }
            row.AsSpan(line, pad).Clear();
            output.Write(row);
        }
    }
}
