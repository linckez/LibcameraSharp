namespace LibcameraSharp.Tests.Configuration;

/// <summary>
/// Port of libcamera <c>test/bayer-format.cpp</c> (the V4L2 cases become PixelFormat cases, since
/// V4L2 formats are not part of libcamera's public API), plus a round trip over the generated table.
/// </summary>
public class BayerFormatTests
{
    [Fact]
    public void Pixel_format_round_trips_and_unknown_formats_are_invalid()
    {
        var bggr8 = BayerFormat.FromPixelFormat(PixelFormats.SBGGR8);
        Assert.Equal(new BayerFormat(BayerOrder.BGGR, 8, BayerPacking.None), bggr8);
        Assert.Equal(PixelFormats.SBGGR8, bggr8!.Value.ToPixelFormat());
        Assert.Null(default(BayerFormat).ToPixelFormat());
        Assert.Null(BayerFormat.FromPixelFormat(PixelFormats.BGR888));
    }

    [Fact]
    public void To_string_matches_libcamera()
    {
        Assert.Equal("BGGR-8", new BayerFormat(BayerOrder.BGGR, 8, BayerPacking.None).ToString());
        Assert.Equal("INVALID", default(BayerFormat).ToString());
        Assert.Equal("RGGB-10-CSI2P", new BayerFormat(BayerOrder.RGGB, 10, BayerPacking.Csi2).ToString());
        Assert.Equal("MONO-16-PISP1", new BayerFormat(BayerOrder.Mono, 16, BayerPacking.Pisp1).ToString());
    }

    [Fact]
    public void Transform_flips_and_transposes_the_pattern()
    {
        var bggr = new BayerFormat(BayerOrder.BGGR, 8, BayerPacking.None);
        Assert.Equal(BayerOrder.GBRG, bggr.Transform(Orientation.Rotate0Mirror).Order);       // HFlip
        Assert.Equal(BayerOrder.GRBG, bggr.Transform(Orientation.Rotate180Mirror).Order);     // VFlip
        Assert.Equal(bggr, bggr.Transform(Orientation.Rotate90Mirror));                       // Transpose leaves BGGR alone
        Assert.Equal(BayerOrder.GRBG, new BayerFormat(BayerOrder.GBRG, 8, BayerPacking.None).Transform(Orientation.Rotate90Mirror).Order);
        Assert.Equal(BayerOrder.Mono, new BayerFormat(BayerOrder.Mono, 8, BayerPacking.None).Transform(Orientation.Rotate180).Order);
    }

    [Fact]
    public void Every_raw_pixel_format_in_the_table_round_trips()
    {
        var count = 0;
        foreach (var (name, format) in PixelFormats.Named)
        {
            if (BayerFormat.FromPixelFormat(format) is not { } bayer)
                continue;
            count++;
            Assert.Equal(format, bayer.ToPixelFormat());
            Assert.True(bayer.IsValid, name);
        }
        Assert.Equal(47, count);
    }
}
