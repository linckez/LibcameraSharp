using System.Buffers.Binary;
using System.Text;

namespace LibcameraSharp.Tests.Imaging;

/// <summary>
/// Just enough TIFF parsing to compare what two writers produced: every IFD (0th, EXIF, SubIFDs)
/// as tag → decoded value, either byte order, with an optional <c>Exif\0\0</c> prefix.
/// </summary>
internal sealed class TiffReader
{
    private readonly byte[] _data;
    private readonly bool _bigEndian;

    public TiffReader(byte[] data)
    {
        if (data.AsSpan().StartsWith("Exif\0\0"u8))
            data = data[6..];
        _data = data;
        _bigEndian = data[0] == 'M';
        Assert.Equal(42, U16(2));
        Ifd0 = ReadIfd(U32(4));
        if (Ifd0.TryGetValue(34665, out var exif))
            Exif = ReadIfd((uint)((uint[])exif)[0]);
        if (Ifd0.TryGetValue(330, out var subs))
            SubIfds = ((uint[])subs).Select(o => ReadIfd(o)).ToList();
    }

    public Dictionary<ushort, object> Ifd0 { get; }
    public Dictionary<ushort, object>? Exif { get; }
    public List<Dictionary<ushort, object>> SubIfds { get; } = [];

    private Dictionary<ushort, object> ReadIfd(uint offset)
    {
        var count = U16((int)offset);
        var ifd = new Dictionary<ushort, object>();
        for (var i = 0; i < count; i++)
        {
            var entry = (int)offset + 2 + 12 * i;
            var tag = U16(entry);
            var type = U16(entry + 2);
            var n = (int)U32(entry + 4);
            var size = type switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, 4 or 9 or 11 or 13 => 4, 5 or 10 or 12 => 8, _ => throw new InvalidDataException($"tag {tag} type {type}") };
            var valueOffset = n * size <= 4 ? entry + 8 : (int)U32(entry + 8);
            ifd[tag] = Decode(type, n, valueOffset);
        }
        return ifd;
    }

    private object Decode(int type, int n, int at) => type switch
    {
        2 => Encoding.ASCII.GetString(_data, at, n).TrimEnd('\0'),
        1 or 7 => _data.AsSpan(at, n).ToArray(),
        3 => Enumerable.Range(0, n).Select(i => U16(at + 2 * i)).ToArray(),
        4 or 13 => Enumerable.Range(0, n).Select(i => U32(at + 4 * i)).ToArray(),     // 13: IFD, an offset
        5 => Enumerable.Range(0, n).Select(i => (double)U32(at + 8 * i) / U32(at + 8 * i + 4)).ToArray(),
        10 => Enumerable.Range(0, n).Select(i => (double)I32(at + 8 * i) / I32(at + 8 * i + 4)).ToArray(),
        11 => Enumerable.Range(0, n).Select(i => BitConverter.Int32BitsToSingle(I32(at + 4 * i))).ToArray(),
        _ => _data.AsSpan(at, n * (type is 12 ? 8 : 2)).ToArray(),
    };

    private ushort U16(int at) => _bigEndian ? BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(at)) : BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(at));
    private uint U32(int at) => _bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(at)) : BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(at));
    private int I32(int at) => _bigEndian ? BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(at)) : BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(at));
}
