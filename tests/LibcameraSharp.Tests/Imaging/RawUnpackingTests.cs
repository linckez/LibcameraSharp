using System.Text.Json;

namespace LibcameraSharp.Tests.Imaging;

/// <summary>
/// <c>RawUnpacking</c> against reference output for the same seeded random buffers, in CSI-2 packed,
/// unpacked and PiSP compressed formats (see <c>Fixtures/README.md</c>).
/// </summary>
public class RawUnpackingTests
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Imaging", "Fixtures");

    public static IEnumerable<object[]> Fixtures()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureDir, "raw_unpacking.json")));
        foreach (var entry in doc.RootElement.EnumerateArray())
            yield return [entry.GetProperty("name").GetString()!, entry.GetProperty("format").GetString()!,
                          entry.GetProperty("width").GetInt32(), entry.GetProperty("height").GetInt32(), entry.GetProperty("stride").GetInt32()];
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Unpacks_exactly_as_the_reference(string name, string formatName, int width, int height, int stride)
    {
        var format = BayerFormat.FromPixelFormat(PixelFormats.ByName(formatName)!.Value)!.Value;
        var packed = File.ReadAllBytes(Path.Combine(FixtureDir, $"{name}.packed.bin"));
        var expected = File.ReadAllBytes(Path.Combine(FixtureDir, $"{name}.unpacked.bin"));

        var samples = RawUnpacking.Unpack(packed, format, new Size((uint)width, (uint)height), (uint)stride);

        Assert.Equal(width * height, samples.Length);
        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);     // little-endian, like numpy's '<u2'
        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void Short_buffers_are_refused_and_unsupported_packings_say_so()
    {
        var csi2 = new BayerFormat(BayerOrder.RGGB, 10, BayerPacking.Csi2);
        Assert.Throws<ArgumentException>(() => RawUnpacking.Unpack(new byte[10], csi2, new Size(8, 2), 10));
        Assert.Throws<NotSupportedException>(() => RawUnpacking.Unpack(new byte[64], new BayerFormat(BayerOrder.RGGB, 10, BayerPacking.Ipu3), new Size(8, 2), 32));
        Assert.Equal(16, RawUnpacking.UnpackedBitDepth(new BayerFormat(BayerOrder.RGGB, 16, BayerPacking.Pisp1)));
        Assert.Equal(12, RawUnpacking.UnpackedBitDepth(new BayerFormat(BayerOrder.RGGB, 12, BayerPacking.Csi2)));
    }
}
