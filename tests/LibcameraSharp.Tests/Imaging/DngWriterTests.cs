using System.Text.Json;

namespace LibcameraSharp.Tests.Imaging;

/// <summary>
/// <c>DngWriter</c> against a reference DNG made from the same raw buffer and metadata (see
/// <c>Fixtures/README.md</c>): the tags that describe the image must agree.
/// </summary>
public class DngWriterTests
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Imaging", "Fixtures");

    private static (byte[] Raw, StreamDescription Config, Metadata Metadata, string Model) Fixture()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureDir, "dng.json")));
        var root = doc.RootElement;
        var cfg = root.GetProperty("config");
        var config = new StreamDescription(new Size(cfg.GetProperty("size")[0].GetUInt32(), cfg.GetProperty("size")[1].GetUInt32()), PixelFormats.ByName(cfg.GetProperty("format").GetString()!))
        {
            Stride = cfg.GetProperty("stride").GetUInt32(),
        };
        var md = root.GetProperty("metadata");
        var list = new ControlList();
        list.Set(Controls.SensorBlackLevels, [.. md.GetProperty("SensorBlackLevels").EnumerateArray().Select(v => v.GetInt32())]);
        list.Set(Controls.ColourGains, [.. md.GetProperty("ColourGains").EnumerateArray().Select(v => v.GetSingle())]);
        list.Set(Controls.ColourCorrectionMatrix, [.. md.GetProperty("ColourCorrectionMatrix").EnumerateArray().Select(v => v.GetSingle())]);
        list.Set(Controls.ExposureTime, md.GetProperty("ExposureTime").GetInt32());
        list.Set(Controls.AnalogueGain, md.GetProperty("AnalogueGain").GetSingle());
        list.Set(Controls.DigitalGain, md.GetProperty("DigitalGain").GetSingle());
        list.Set(Controls.SensorTimestamp, md.GetProperty("SensorTimestamp").GetInt64());
        list.Set(Controls.LensPosition, 2.0f);                              // not in the reference; checked on its own below
        return (File.ReadAllBytes(Path.Combine(FixtureDir, "dng.raw.bin")), config, new Metadata(list), root.GetProperty("model").GetString()!);
    }

    [Fact]
    public void Describes_the_image_as_the_reference_does()
    {
        var (raw, config, metadata, model) = Fixture();
        var path = Path.Combine(Path.GetTempPath(), $"dng-writer-{Guid.NewGuid():N}.dng");
        byte[] written;
        try
        {
            DngWriter.Save(raw, config, metadata, model, path);
            written = File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }

        var theirs = new TiffReader(File.ReadAllBytes(Path.Combine(FixtureDir, "dng.reference.dng"))).Ifd0; // the reference: one IFD holds everything
        var ours = new TiffReader(written);
        var image = Assert.Single(ours.SubIfds);                                                            // ours: thumbnail, then the raw SubIFD
        Assert.Equal(Numbers(theirs[256]), Numbers(image[256]));                                           // ImageWidth (SHORT or LONG, TIFF 6.0 §8)
        Assert.Equal(Numbers(theirs[257]), Numbers(image[257]));                                           // ImageLength
        Assert.Equal(theirs[262], image[262]);                                                             // Photometric = CFA
        Assert.Equal(theirs[33422], image[33422]);                                                         // CFAPattern
        Assert.Equal(((ushort[])theirs[50717])[0], (uint)((uint[])image[50717])[0]);                        // WhiteLevel
        Assert.Equal(((ushort[])theirs[50714]).Select(v => (double)v), ((double[])image[50714]));           // BlackLevel
        Assert.Equal((ushort[])[16], (ushort[])image[258]);                                                // BitsPerSample: 16 here, 10 packed there
        AssertClose((double[])theirs[50721], (double[])ours.Ifd0[50721], 2e-4);                          // ColorMatrix1 (the reference truncates, we round)
        AssertClose((double[])theirs[50728], (double[])ours.Ifd0[50728], 1e-4);                          // AsShotNeutral
        Assert.Equal(theirs[50778], ours.Ifd0[50778]);                                                     // CalibrationIlluminant1 = D65
        Assert.Equal(theirs[34855], ours.Exif![34855]);                                                    // ISO
        AssertClose((double[])theirs[33434], (double[])ours.Exif[33434], 1e-6);                           // ExposureTime
        AssertClose([0.5], (double[])ours.Exif[37382], 1e-6);                                             // SubjectDistance = 1 / LensPosition
        // The maker is only known on a Raspberry Pi, as the Pi's own tools write it; elsewhere it is left out.
        if (LibcameraSharp.PlatformDetection.Current is Platform.Vc4 or Platform.Pisp)
            Assert.Equal("Raspberry Pi", ours.Ifd0[271]);
        else
            Assert.False(ours.Ifd0.ContainsKey(271));
        Assert.Equal(model, ours.Ifd0[272]);
        Assert.Equal((uint)(64 * 32 * 2), ((uint[])image[279])[0]);                                     // StripByteCounts: width × height × 2 bytes
    }

    private static long[] Numbers(object value) => value switch
    {
        ushort[] shorts => [.. shorts.Select(v => (long)v)],
        uint[] longs => [.. longs.Select(v => (long)v)],
        _ => throw new InvalidDataException($"not an integer tag: {value.GetType()}"),
    };

    private static void AssertClose(double[] expected, double[] actual, double tolerance)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
            Assert.True(Math.Abs(expected[i] - actual[i]) <= tolerance, $"[{i}]: {expected[i]} vs {actual[i]}");
    }
}
