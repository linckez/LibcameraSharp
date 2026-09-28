namespace LibcameraSharp;

/// <summary>
/// Turns a raw (Bayer or mono) frame as the sensor delivered it into one 16-bit sample per pixel,
/// rows tightly packed: the form a DNG stores and the form you want for your own processing.
/// Handles CSI-2 packed 10/12-bit, unpacked 8/10/12/16-bit, and Raspberry Pi 5's PiSP compressed mode 1.
/// </summary>
internal static class RawUnpacking
{
    // PiSP compression parameters every Pi 5 driver uses.
    private const int CompressOffset = 2048;

    /// <summary>
    /// Unpacks <paramref name="data"/> (rows <paramref name="stride"/> bytes apart) into
    /// <c>width × height</c> samples. Samples keep the sensor's bit depth (a 10-bit sensor gives 0–1023),
    /// except PiSP-compressed frames, which decompress to 16-bit.
    /// </summary>
    /// <exception cref="NotSupportedException">IPU3 packing or PiSP mode 2 compression, which this SDK does not decode.</exception>
    /// <exception cref="ArgumentException">The buffer is shorter than the layout requires.</exception>
    public static ushort[] Unpack(ReadOnlySpan<byte> data, BayerFormat format, Size size, uint stride)
    {
        var width = (int)size.Width;
        var height = (int)size.Height;
        var samples = new ushort[width * height];
        switch (format.Packing, format.BitDepth)
        {
            case (BayerPacking.Csi2, 10):
                Unpack10Bit(data, width, height, (int)stride, samples);
                break;
            case (BayerPacking.Csi2, 12):
                Unpack12Bit(data, width, height, (int)stride, samples);
                break;
            case (BayerPacking.None, 8):
                Unpack8Bit(data, width, height, (int)stride, samples);
                break;
            case (BayerPacking.None, _):
                Unpack16Bit(data, width, height, (int)stride, samples);
                break;
            case (BayerPacking.Pisp1, _):
                Uncompress(data, width, height, (int)stride, samples);
                break;
            default:
                throw new NotSupportedException($"Raw packing {format.Packing} is not supported.");
        }
        return samples;
    }

    /// <summary>The bit depth of the samples <see cref="Unpack"/> returns for <paramref name="format"/>: 16 after PiSP decompression, else the sensor's.</summary>
    public static int UnpackedBitDepth(BayerFormat format) => format.Packing is BayerPacking.Pisp1 or BayerPacking.Pisp2 ? 16 : format.BitDepth;

    // Four 10-bit samples in five bytes: the high 8 bits of each, then a byte with their low 2 bits.
    private static void Unpack10Bit(ReadOnlySpan<byte> src, int width, int height, int stride, Span<ushort> dest)
    {
        RequireLength(src, height, stride, (width + 3) / 4 * 5);
        var alignedWidth = width & ~3;
        for (var y = 0; y < height; y++)
        {
            var row = src.Slice(y * stride);
            var d = y * width;
            var p = 0;
            var x = 0;
            for (; x < alignedWidth; x += 4, p += 5)
            {
                dest[d + x] = (ushort)((row[p] << 2) | (row[p + 4] & 3));
                dest[d + x + 1] = (ushort)((row[p + 1] << 2) | ((row[p + 4] >> 2) & 3));
                dest[d + x + 2] = (ushort)((row[p + 2] << 2) | ((row[p + 4] >> 4) & 3));
                dest[d + x + 3] = (ushort)((row[p + 3] << 2) | ((row[p + 4] >> 6) & 3));
            }
            for (; x < width; x++)
                dest[d + x] = (ushort)((row[p + (x & 3)] << 2) | ((row[p + 4] >> ((x & 3) << 1)) & 3));
        }
    }

    // Two 12-bit samples in three bytes: the high 8 bits of each, then a byte with their low 4 bits.
    private static void Unpack12Bit(ReadOnlySpan<byte> src, int width, int height, int stride, Span<ushort> dest)
    {
        RequireLength(src, height, stride, (width + 1) / 2 * 3);
        var alignedWidth = width & ~1;
        for (var y = 0; y < height; y++)
        {
            var row = src.Slice(y * stride);
            var d = y * width;
            var p = 0;
            var x = 0;
            for (; x < alignedWidth; x += 2, p += 3)
            {
                dest[d + x] = (ushort)((row[p] << 4) | (row[p + 2] & 15));
                dest[d + x + 1] = (ushort)((row[p + 1] << 4) | ((row[p + 2] >> 4) & 15));
            }
            if (x < width)
                dest[d + x] = (ushort)((row[p + (x & 1)] << 4) | ((row[p + 2] >> ((x & 1) << 2)) & 15));
        }
    }

    // One little-endian 16-bit word per sample, already in native order on every platform libcamera runs on.
    private static void Unpack16Bit(ReadOnlySpan<byte> src, int width, int height, int stride, Span<ushort> dest)
    {
        RequireLength(src, height, stride, width * 2);
        for (var y = 0; y < height; y++)
        {
            var row = src.Slice(y * stride, width * 2);
            for (var x = 0; x < width; x++)
                dest[y * width + x] = (ushort)(row[2 * x] | (row[2 * x + 1] << 8));
        }
    }

    // One byte per sample, widened, as libcamera's DNG writer does (packScanlineRaw8).
    private static void Unpack8Bit(ReadOnlySpan<byte> src, int width, int height, int stride, Span<ushort> dest)
    {
        RequireLength(src, height, stride, width);
        for (var y = 0; y < height; y++)
        {
            var row = src.Slice(y * stride, width);
            for (var x = 0; x < width; x++)
                dest[y * width + x] = row[x];
        }
    }

    // PiSP mode 1: each 8-pixel block is two 32-bit little-endian words, each holding four quantised samples for alternate pixels.
    private static void Uncompress(ReadOnlySpan<byte> src, int width, int height, int stride, Span<ushort> dest)
    {
        var paddedWidth = (width + 7) & ~7;
        RequireLength(src, height, stride, paddedWidth);
        Span<ushort> block = stackalloc ushort[8];
        for (var y = 0; y < height; y++)
        {
            var row = src.Slice(y * stride);
            for (var x = 0; x < width; x += 8)
            {
                var w0 = BitConverter.ToUInt32(row.Slice(x));
                var w1 = BitConverter.ToUInt32(row.Slice(x + 4));
                SubBlock(block, w0);
                SubBlock(block.Slice(1), w1);
                var count = Math.Min(8, width - x);
                for (var i = 0; i < count; i++)
                    dest[y * width + x + i] = PostProcess(block[i]);
            }
        }
    }

    // One 32-bit word -> samples 0, 2, 4, 6 of the block.
    private static void SubBlock(Span<ushort> d, uint w)
    {
        Span<int> q = stackalloc int[4];
        var qmode = (int)(w & 3);
        if (qmode < 3)
        {
            var field0 = (int)((w >> 2) & 511);
            var field1 = (int)((w >> 11) & 127);
            var field2 = (int)((w >> 18) & 127);
            var field3 = (int)((w >> 25) & 127);
            if (qmode == 2 && field0 >= 384)
            {
                q[1] = field0;
                q[2] = field1 + 384;
            }
            else
            {
                q[1] = field1 >= 64 ? field0 : field0 + 64 - field1;
                q[2] = field1 >= 64 ? field0 + field1 - 64 : field0;
            }
            var p1 = Math.Max(0, q[1] - 64);
            var p2 = Math.Max(0, q[2] - 64);
            if (qmode == 2)
            {
                p1 = Math.Min(384, p1);
                p2 = Math.Min(384, p2);
            }
            q[0] = p1 + field2;
            q[3] = p2 + field3;
        }
        else
        {
            var pack0 = (int)((w >> 2) & 32767);
            var pack1 = (int)((w >> 17) & 32767);
            q[0] = (pack0 & 15) + 16 * ((pack0 >> 8) / 11);
            q[1] = (pack0 >> 4) % 176;
            q[2] = (pack1 & 15) + 16 * ((pack1 >> 8) / 11);
            q[3] = (pack1 >> 4) % 176;
        }
        d[0] = Dequantize(q[0], qmode);
        d[2] = Dequantize(q[1], qmode);
        d[4] = Dequantize(q[2], qmode);
        d[6] = Dequantize(q[3], qmode);
    }

    private static ushort Dequantize(int q, int qmode) => qmode switch
    {
        0 => (ushort)(q < 320 ? 16 * q : 32 * (q - 160)),
        1 => (ushort)(64 * q),
        2 => (ushort)(128 * q),
        _ => (ushort)(q < 94 ? 256 * q : Math.Min(0xFFFF, 512 * (q - 47))),
    };

    // Mode 1 has no companding step; only the black-level offset is added back.
    private static ushort PostProcess(ushort a) => (ushort)Math.Min(0xFFFF, a + CompressOffset);

    private static void RequireLength(ReadOnlySpan<byte> src, int height, int stride, int rowBytes)
    {
        if (height == 0)
            return;
        var needed = (long)(height - 1) * stride + rowBytes;
        if (src.Length < needed)
            throw new ArgumentException($"Raw buffer holds {src.Length} bytes; the layout needs {needed}.", nameof(src));
    }
}
