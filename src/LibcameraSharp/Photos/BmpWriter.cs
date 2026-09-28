using System.Buffers.Binary;

namespace LibcameraSharp;

/// <summary>Writes a frame as an uncompressed 24-bit BMP.</summary>
/// <remarks>Rows are written top-down and padded to 4 bytes; any of the four RGB formats is reordered to B,G,R.</remarks>
internal static class BmpWriter
{
    /// <summary>Encodes <paramref name="pixels"/> (an RGB format) to <paramref name="output"/>.</summary>
    /// <exception cref="NotSupportedException">The frame's format isn't one of the four RGB formats.</exception>
    public static void Save(FramePixels pixels, Stream output)
    {
        var (width, height) = ((int)pixels.Size.Width, (int)pixels.Size.Height);
        var line = width * 3;
        var pitch = (line + 3) & ~3;
        var pad = pitch - line;
        var swap = pixels.Format == PixelFormats.BGR888 || pixels.Format == PixelFormats.XBGR8888;      // R,G,B in memory → B,G,R on disk
        var bytesPerPixel = PixelLayout.BytesPerPixel(pixels.Format) ?? 0;
        if (bytesPerPixel is not (3 or 4))
            throw new NotSupportedException($"Stream format {pixels.Format} not supported for BMP; use an RGB format.");

        Span<byte> header = stackalloc byte[54];
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(header[2..], (uint)(54 + height * pitch));
        BinaryPrimitives.WriteUInt32LittleEndian(header[10..], 54);
        BinaryPrimitives.WriteUInt32LittleEndian(header[14..], 40);
        BinaryPrimitives.WriteInt32LittleEndian(header[18..], width);
        BinaryPrimitives.WriteInt32LittleEndian(header[22..], -height);
        BinaryPrimitives.WriteUInt16LittleEndian(header[26..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[28..], 24);
        BinaryPrimitives.WriteUInt32LittleEndian(header[38..], 100000);
        BinaryPrimitives.WriteUInt32LittleEndian(header[42..], 100000);
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
