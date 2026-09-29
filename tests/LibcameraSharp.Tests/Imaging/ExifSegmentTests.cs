using System.Text.Json;

namespace LibcameraSharp.Tests.Imaging;

/// <summary>
/// The EXIF block against a reference made from the same metadata (see <c>Fixtures/README.md</c>),
/// read back through <see cref="TiffReader"/> so byte order and layout don't matter — only tags and
/// values. Software names this library, so it is checked on its own; Make matches the reference on a
/// Raspberry Pi and is left out elsewhere.
/// </summary>
public class ExifSegmentTests
{
    private static readonly string FixtureDir = Path.Combine(AppContext.BaseDirectory, "Imaging", "Fixtures");

    private static (Metadata Metadata, string CameraId, DateTime Now, string CustomModel) Fixture()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureDir, "exif.json")));
        var root = doc.RootElement;
        var list = new ControlList();
        var md = root.GetProperty("metadata");
        list.Set(Controls.ExposureTime, md.GetProperty("ExposureTime").GetInt32());
        list.Set(Controls.AnalogueGain, md.GetProperty("AnalogueGain").GetSingle());
        list.Set(Controls.DigitalGain, md.GetProperty("DigitalGain").GetSingle());
        list.Set(Controls.LensPosition, md.GetProperty("LensPosition").GetSingle());
        list.Set(Controls.SensorTimestamp, md.GetProperty("SensorTimestamp").GetInt64());
        var now = DateTime.ParseExact(root.GetProperty("now").GetString()!, "yyyy:MM:dd HH:mm:ss", null);
        return (new Metadata(list), root.GetProperty("camera_id").GetString()!, now, root.GetProperty("custom_model").GetString()!);
    }

    [Fact]
    public void Writes_the_reference_tags_with_the_same_values()
    {
        var (metadata, cameraId, now, _) = Fixture();
        var theirs = new TiffReader(File.ReadAllBytes(Path.Combine(FixtureDir, "exif.default.bin")));
        var ours = new TiffReader(ExifSegment.Build(metadata, cameraId, now: now));

        foreach (var tag in new ushort[] { 272, 306 })                     // Model, DateTime
            Assert.Equal(theirs.Ifd0[tag], ours.Ifd0[tag]);
        Assert.NotNull(ours.Exif);
        foreach (var (tag, value) in theirs.Exif!)                          // ExposureTime, ISO, DateTimeOriginal
            Assert.Equal(value, ours.Exif![tag]);
        if (LibcameraSharp.PlatformDetection.Current is Platform.Vc4 or Platform.Pisp)
            Assert.Equal(theirs.Ifd0[271], ours.Ifd0[271]);                 // Make
        else
            Assert.False(ours.Ifd0.ContainsKey(271));
        Assert.StartsWith("LibcameraSharp", (string)ours.Ifd0[305]);
        // Two tags the reference does not write.
        Assert.Equal(theirs.Exif[36867], ours.Exif![36868]);                // DateTimeDigitized = DateTimeOriginal
        Assert.Equal(0.25, ((double[])ours.Exif[37382])[0]);                // SubjectDistance = 1 / LensPosition
    }

    [Fact]
    public void User_exif_data_overrides_a_generated_tag_and_keeps_the_rest()
    {
        // The Model tag round-trips, and an override replaces only its own tag.
        var (metadata, cameraId, now, customModel) = Fixture();
        var theirs = new TiffReader(File.ReadAllBytes(Path.Combine(FixtureDir, "exif.custom.bin")));
        var ours = new TiffReader(ExifSegment.Build(metadata, cameraId, new ExifData { Model = customModel }, now));

        Assert.Equal(customModel, ours.Ifd0[272]);
        Assert.Equal(theirs.Ifd0[272], ours.Ifd0[272]);
        Assert.Equal(theirs.Exif![33434], ours.Exif![33434]);
        Assert.True(ours.Ifd0.ContainsKey(306), "DateTime survives a per-tag override");
    }

    [Fact]
    public void No_gains_and_no_user_data_means_no_segment()
    {
        using var list = new ControlList();
        list.Set(Controls.SensorTimestamp, 1L);
        Assert.Empty(ExifSegment.Build(new Metadata(list), "cam"));
        Assert.NotEmpty(ExifSegment.Build(new Metadata(list), "cam", new ExifData { Artist = "me" }));
    }
}
