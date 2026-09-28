
namespace LibcameraSharp.Tests.UnknownControls;

/// <summary>A control id with no generated key passes through every layer that copies or reports controls. Needs no camera.</summary>
public class UnknownControlTests
{
    // An id no libcamera release uses, standing in for a fork's own control.
    private static readonly Control<int> ForkControl =
        new((ControlId)9001, "ForkControl", ControlType.Integer32, ControlDirection.In, isArray: false, fixedLength: null);

    [Fact]
    public void Merging_keeps_it()
    {
        using var source = new ControlList();
        source.Set(ForkControl, 42);
        using var target = new ControlList();
        target.Set(Controls.Brightness, 0.5f);

        target.Merge(source);                            // keep-existing, into a list with no id map

        Assert.Equal(42, target.Get(ForkControl));
        Assert.Equal(0.5f, target.Get(Controls.Brightness));
    }

    [Fact]
    public void Patching_passes_it_on_unchanged()
    {
        using var source = new ControlList();
        using var target = new ControlList();
        source.Set(ForkControl, 42);

        ControlPatching.Apply(source, target, _ => true);

        Assert.Equal(42, target.Get(ForkControl));
    }

    [Fact]
    public void Metadata_keeps_it_and_any_key_for_its_id_reads_it()
    {
        using var list = new ControlList();
        list.Set(ForkControl, 42);
        list.Set(Controls.ExposureTime, 20_000);

        var metadata = new Metadata(list);

        Assert.Equal(2, metadata.Count);
        Assert.True(metadata.TryGet(ForkControl, out var value));
        Assert.Equal(42, value);

        // A separate key object for the same id.
        var sameId = new Control<int>((ControlId)9001, "ForkControl", ControlType.Integer32, ControlDirection.In, isArray: false, fixedLength: null);
        Assert.Equal(42, metadata.Get(sameId));
        Assert.Contains(metadata.Keys, key => key.Id == 9001 && key.Name == "9001");
    }

    [Fact]
    public void Metadata_never_answers_a_property_with_a_control_of_the_same_id()
    {
        using var list = new ControlList();
        list.Set(Controls.AeEnable, true);               // control id 1, which is also the property Location's

        var metadata = new Metadata(list);

        Assert.True(metadata.Contains(Controls.AeEnable));
        Assert.False(metadata.Contains(Properties.Location));
        Assert.Throws<KeyNotFoundException>(() => metadata.GetValue(Properties.Location));
    }
}
