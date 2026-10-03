
namespace LibcameraSharp.Tests.SensorModes;

/// <summary>
/// Mode scoring, which decides which sensor readout a size-only configuration gets. The expected
/// picks and scores were produced by another implementation's scoring over the IMX477's real modes, so
/// they are an independent answer, not our own. Needs no camera.
/// </summary>
public class ScoreModeTests
{
    // The IMX477's raw modes: (size, bit depth).
    private static readonly (Size Size, int BitDepth)[] Imx477 =
    [
        (new Size(1332, 990), 10),
        (new Size(2028, 1080), 12),
        (new Size(2028, 1520), 12),
        (new Size(4056, 3040), 12),
    ];

    private static Size Pick(Size want, int bitDepth) =>
        Imx477.MinBy(mode => CameraSession.ScoreMode(mode.Size, mode.BitDepth, want, bitDepth)).Size;

    [Theory]
    // want                      depth   expected pick
    [InlineData(1920, 1080, 12,  2028, 1080)]   // widescreen video: the matching aspect wins
    [InlineData(1280,  720, 12,  2028, 1080)]   // ...and again, rather than the smaller 4:3 mode
    [InlineData( 640,  480, 10,  1332,  990)]   // small and 4:3: the smallest mode, at its own depth
    [InlineData(4056, 3040, 12,  4056, 3040)]   // full resolution: an exact match scores zero
    public void Picks_the_expected_mode(int wantWidth, int wantHeight, int bitDepth, int pickWidth, int pickHeight)
    {
        Assert.Equal(new Size((uint)pickWidth, (uint)pickHeight), Pick(new Size((uint)wantWidth, (uint)wantHeight), bitDepth));
    }

    [Theory]
    // Expected scores for the 1920x1080 at 12 bits request.
    [InlineData(1332,  990, 10, 3652.9697)]
    [InlineData(2028, 1080, 12,   64.5000)]
    [InlineData(2028, 1520, 12, 1467.7018)]
    [InlineData(4056, 3040, 12, 2354.7018)]
    public void Scores_match_to_four_places(int modeWidth, int modeHeight, int modeDepth, double expected)
    {
        var score = CameraSession.ScoreMode(
            new Size((uint)modeWidth, (uint)modeHeight), modeDepth, new Size(1920, 1080), 12);

        Assert.Equal(expected, score, 4);
    }
}
