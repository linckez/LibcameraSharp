namespace LibcameraSharp.Tests.Compliance;

/// <summary>
/// Ports of libcamera's own control tests: <c>test/controls/control_value.cpp</c>,
/// <c>control_list.cpp</c> and <c>control_info_map.cpp</c>.
/// </summary>
/// <remarks>
/// libcamera builds a bare <c>ControlValue</c> and sets each type into it in turn. Our
/// <c>ControlValueCodec</c> is internal and always writes through a typed key, so the same coverage
/// comes from round-tripping one real control of each libcamera type through a <see cref="ControlList"/> —
/// which is how a caller meets those types anyway. <c>ControlInfo</c>'s own test constructs infos from
/// literals; ours only wraps a camera's, so that file's coverage is the camera-backed part below.
/// </remarks>
[Collection("camera")]
public class ControlContractTests(ITestOutputHelper output)
{
    /// <summary>Every libcamera control type survives a write and a read, as a scalar and as an array.</summary>
    /// <remarks>libcamera <c>control_value.cpp</c>, one case per <c>ControlType</c>.</remarks>
    [Fact]
    public void Every_control_type_round_trips()
    {
        using var list = new ControlList();

        list.Set(Controls.AeEnable, true);                                          // Bool
        Assert.True(list.Get(Controls.AeEnable));

        list.Set(Controls.ExposureTime, 0x42000000);                                // Integer32
        Assert.Equal(0x42000000, list.Get(Controls.ExposureTime));

        list.Set(Controls.FrameWallClock, -42L);                                    // Integer64
        Assert.Equal(-42L, list.Get(Controls.FrameWallClock));

        list.Set(Controls.Brightness, -0.42f);                                      // Float
        Assert.Equal(-0.42f, list.Get(Controls.Brightness));

        list.Set(Controls.ScalerCrop, new Rectangle(1, 2, 3, 4));                   // Rectangle
        Assert.Equal(new Rectangle(1, 2, 3, 4), list.Get(Controls.ScalerCrop));

        list.Set(Controls.Draft.FaceDetectFaceScores, [3, 14, 15, 9]);                    // Byte array
        Assert.Equal<byte[]>([3, 14, 15, 9], list.Get(Controls.Draft.FaceDetectFaceScores));

        list.Set(Controls.SensorBlackLevels, [3, 14, 15, 9]);                       // Integer32 array
        Assert.Equal<int[]>([3, 14, 15, 9], list.Get(Controls.SensorBlackLevels));

        list.Set(Controls.FrameDurationLimits, [33333L, 1000000L]);                 // Integer64 array
        Assert.Equal<long[]>([33333L, 1000000L], list.Get(Controls.FrameDurationLimits));

        list.Set(Controls.ColourGains, [3.141593f, 2.718282f]);                     // Float array
        Assert.Equal<float[]>([3.141593f, 2.718282f], list.Get(Controls.ColourGains));

        list.Set(Controls.AfWindows, [new Rectangle(0, 0, 8, 8)]);                  // Rectangle array
        Assert.Equal<Rectangle[]>([new Rectangle(0, 0, 8, 8)], list.Get(Controls.AfWindows));

        list.Set(Controls.Draft.FaceDetectFaceLandmarks, [new Point(4, 2)]);              // Point array
        Assert.Equal<Point[]>([new Point(4, 2)], list.Get(Controls.Draft.FaceDetectFaceLandmarks));

        list.Set(Controls.AeState, AeState.Converged);                              // enumerated Integer32
        Assert.Equal(AeState.Converged, list.Get(Controls.AeState));

        output.WriteLine(list.ToString());
    }

    /// <summary>A control that isn't in the list is a miss, and an untyped write of the wrong type is refused rather than reinterpreted.</summary>
    /// <remarks>
    /// Guards the casts <c>ControlValueCodec</c> makes. libcamera relies on C++ types for this; the
    /// typed keys do the same here, so only the untyped <see cref="ControlList.SetValue"/> escape
    /// hatch can be handed a wrong value.
    /// </remarks>
    [Fact]
    public void Missing_controls_and_wrongly_typed_writes_are_refused()
    {
        using var list = new ControlList();
        list.Set(Controls.FrameDurationLimits, [33333L, 1000000L]);

        Assert.Throws<KeyNotFoundException>(() => list.Get(Controls.Brightness));
        Assert.Throws<KeyNotFoundException>(() => list.GetValue(Controls.Brightness));
        Assert.False(list.TryGet(Controls.Brightness, out _));

        Assert.Throws<InvalidCastException>(() => list.SetValue(Controls.Brightness, "not a float"));
        Assert.Equal(new long[] { 33333L, 1000000L }, list.GetValue(Controls.FrameDurationLimits));
    }

    /// <summary>
    /// Merging into a standalone list whose keys collide must not reach libcamera's keep-existing
    /// warning path, which reads an id map a standalone list does not have.
    /// </summary>
    /// <remarks>
    /// Found porting <c>control_list.cpp</c>: libcamera's own test merges lists built from
    /// <c>controls::controls</c>, so it never meets this. Through our API it segfaulted the process.
    /// </remarks>
    [Fact]
    public void Merging_colliding_controls_into_a_standalone_list_does_not_crash()
    {
        using var source = new ControlList();
        source.Set(Controls.Brightness, 0.25f);
        source.Set(Controls.Contrast, 1.25f);

        using var keep = new ControlList();
        keep.Set(Controls.Brightness, 0.75f);
        keep.Merge(source);
        Assert.Equal(0.75f, keep.Get(Controls.Brightness));
        Assert.Equal(1.25f, keep.Get(Controls.Contrast));

        using var overwrite = new ControlList();
        overwrite.Set(Controls.Brightness, 0.75f);
        overwrite.Merge(source, overwrite: true);
        Assert.Equal(0.25f, overwrite.Get(Controls.Brightness));
    }
}
