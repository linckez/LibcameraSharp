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
    public void A_location_goes_into_the_gps_directory_as_degrees_minutes_and_seconds()
    {
        // Read back as |degrees| + minutes/60 + seconds/3600, as EXIF readers do.
        static double Degrees(object value)
        {
            var dms = (double[])value;
            return dms[0] + dms[1] / 60 + dms[2] / 3600;
        }

        using var list = new ControlList();
        var north = new TiffReader(ExifSegment.Build(new Metadata(list), "cam", new ExifData { Location = new GpsLocation(55.6761, 12.5683, 12.3) }));
        Assert.Equal(new byte[] { 2, 2, 0, 0 }, north.Gps![0]);
        Assert.Equal("N", north.Gps[1]);
        Assert.Equal(new[] { 55.0, 40.0, 33.96 }, (double[])north.Gps[2]);
        Assert.Equal(55.6761, Degrees(north.Gps[2]), 1e-9);
        Assert.Equal("E", north.Gps[3]);
        Assert.Equal(12.5683, Degrees(north.Gps[4]), 1e-9);
        Assert.Equal(new byte[] { 0 }, north.Gps[5]);
        Assert.Equal(12.3, ((double[])north.Gps[6])[0]);
        Assert.Equal("WGS-84", north.Gps[18]);

        var south = new TiffReader(ExifSegment.Build(new Metadata(list), "cam", new ExifData { Location = new GpsLocation(-33.9, -18.4, -3.5) }));
        Assert.Equal("S", south.Gps![1]);
        Assert.Equal(33.9, Degrees(south.Gps[2]), 1e-9);
        Assert.Equal("W", south.Gps[3]);
        Assert.Equal(new byte[] { 1 }, south.Gps[5]);
        Assert.Equal(3.5, ((double[])south.Gps[6])[0]);

        Assert.False(new TiffReader(ExifSegment.Build(new Metadata(list), "cam", new ExifData { Artist = "me" })).Ifd0.ContainsKey(34853));
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
