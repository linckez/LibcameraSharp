using System.Reflection;
using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Tests.Generated;

/// <summary>
/// The generated control, property and pixel-format tables must agree with the libcamera that is
/// actually loaded: every key we generated exists with the same name/type/shape, and every id the
/// library knows is one we generated. No expected values are written by hand; libcamera is the oracle.
/// </summary>
public unsafe class ControlTableTests
{
    // Vendor id ranges from control_ranges.yaml; probing each range finds everything the library defines.
    private static readonly (uint Start, uint End)[] IdRanges = [(1, 999), (10001, 10999), (20001, 20999), (30001, 30999)];

    public static IEnumerable<object[]> GeneratedControls() =>
        AllKeys(typeof(Controls)).Select(k => new object[] { k.Name, k });

    public static IEnumerable<object[]> GeneratedProperties() =>
        AllKeys(typeof(Properties)).Select(k => new object[] { k.Name, k });

    public static IEnumerable<object[]> GeneratedFormats() =>
        PixelFormats.Named.Select(kv => new object[] { kv.Key, kv.Value });

    [Theory]
    [MemberData(nameof(GeneratedControls))]
    public void Control_matches_library(string name, ControlKey key)
    {
        var control = NativeMethods.libcamera_control_from_id((libcamera_control_id_enum)key.Id);
        Assert.True(control is not null, $"libcamera does not know control id {key.Id} ({name})");

        Assert.Equal(name, NativeMethods.libcamera_control_name(control));
        Assert.Equal((int)key.Type, (int)NativeMethods.libcamera_control_id_type(control));
        Assert.Equal(key.IsArray, NativeMethods.libcamera_control_id_is_array(control));

        // libcamera reports 0 for scalars, the element count for fixed arrays, and SIZE_MAX (dynamic_extent) for variable ones.
        var expectedSize = key.IsArray && key.FixedLength is null ? nuint.MaxValue : (nuint)(key.FixedLength ?? 0);
        Assert.Equal(expectedSize, NativeMethods.libcamera_control_id_size(control));

        Assert.Equal(key.Direction.HasFlag(ControlDirection.In), NativeMethods.libcamera_control_id_is_input(control));
        Assert.Equal(key.Direction.HasFlag(ControlDirection.Out), NativeMethods.libcamera_control_id_is_output(control));
    }

    // The managed type each libcamera ControlType is read and written as (ControlValueCodec.ReadScalar/ReadArray).
    private static readonly Dictionary<ControlType, Type> ManagedScalar = new()
    {
        [ControlType.Bool] = typeof(bool), [ControlType.Byte] = typeof(byte), [ControlType.Unsigned16] = typeof(ushort),
        [ControlType.Unsigned32] = typeof(uint), [ControlType.Integer32] = typeof(int), [ControlType.Integer64] = typeof(long),
        [ControlType.Float] = typeof(float), [ControlType.String] = typeof(string), [ControlType.Rectangle] = typeof(Rectangle),
        [ControlType.Size] = typeof(Size), [ControlType.Point] = typeof(Point),
    };

    /// <summary>
    /// The codec casts between the key's <c>T</c> and the stored bytes by the key's <see cref="ControlType"/>
    /// (Unsafe.BitCast, typed spans, boxed enums). That is only sound if <c>T</c> and the type agree for
    /// every generated key — checked here so it can never drift silently.
    /// </summary>
    [Theory]
    [MemberData(nameof(GeneratedControls))]
    [MemberData(nameof(GeneratedProperties))]
    public void Key_managed_type_agrees_with_its_control_type(string name, ControlKey key)
    {
        var element = key.IsArray ? key.ValueType.GetElementType()! : key.ValueType;
        Assert.Equal(key.IsArray, key.ValueType.IsArray);
        if (element.IsEnum)
        {
            Assert.Equal(ControlType.Integer32, key.Type);
            Assert.Equal(typeof(int), Enum.GetUnderlyingType(element));
            return;
        }
        Assert.True(ManagedScalar.TryGetValue(key.Type, out var expected), $"{name}: no managed type for {key.Type}");
        Assert.Equal(expected, element);
    }

    [Theory]
    [MemberData(nameof(GeneratedProperties))]
    public void Property_matches_library(string name, ControlKey key)
    {
        var id = (libcamera_property_id)key.Id;
        Assert.Equal(name, NativeMethods.libcamera_property_name_from_id(id));
        Assert.Equal((int)key.Type, (int)NativeMethods.libcamera_property_type_from_id(id));
    }

    [Fact]
    public void Library_knows_no_control_we_did_not_generate()
    {
        var generated = AllKeys(typeof(Controls)).Select(k => k.Id).ToHashSet();
        var missing = new List<string>();

        // Walk every plausible id; the shim returns null for ids libcamera doesn't define.
        foreach (var (start, end) in IdRanges)
        {
            for (var id = start; id <= end; id++)
            {
                var control = NativeMethods.libcamera_control_from_id((libcamera_control_id_enum)id);
                if (control is not null && !generated.Contains(id))
                    missing.Add($"{id}={NativeMethods.libcamera_control_name(control)}");
            }
        }

        Assert.True(missing.Count == 0, "controls in libcamera but not generated: " + string.Join(", ", missing));
    }

    [Fact]
    public void Library_knows_no_property_we_did_not_generate()
    {
        var generated = AllKeys(typeof(Properties)).Select(k => k.Id).ToHashSet();
        var missing = new List<string>();

        foreach (var (start, end) in IdRanges)
        {
            for (var id = start; id <= end; id++)
            {
                var name = NativeMethods.libcamera_property_name_from_id((libcamera_property_id)id);
                if (name is not null && !generated.Contains(id))
                    missing.Add($"{id}={name}");
            }
        }

        Assert.True(missing.Count == 0, "properties in libcamera but not generated: " + string.Join(", ", missing));
    }

    [Theory]
    [MemberData(nameof(GeneratedFormats))]
    public void Pixel_format_round_trips_through_library(string name, PixelFormat format)
    {
        // Name -> code: libcamera parses its own format names.
        var parsed = NativeMethods.libcamera_pixel_format_from_str(name);
        Assert.Equal(format.Fourcc, parsed.fourcc);
        Assert.Equal(format.Modifier, parsed.modifier);

        // Code -> name: libcamera prints the name we generated it under (the marshaller frees libcamera's copy).
        var native = new libcamera_pixel_format { fourcc = format.Fourcc, modifier = format.Modifier };
        Assert.Equal(name, NativeMethods.libcamera_pixel_format_str(&native));
    }

    private static IEnumerable<ControlKey> AllKeys(Type container)
    {
        // Core keys are fields on the class; vendor keys are fields on its nested classes.
        var types = new[] { container }.Concat(container.GetNestedTypes());
        return types
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Select(f => f.GetValue(null))
            .OfType<ControlKey>();
    }
}
