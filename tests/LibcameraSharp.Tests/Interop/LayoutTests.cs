using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Tests.Interop;

/// <summary>
/// The by-value structs must have the exact size and field offsets clang computed for the
/// target; the shim's own static_asserts tie those to libcamera's C++ types.
/// </summary>
public class LayoutTests
{
    private static readonly JsonDocument Layout =
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "layout.g.json")));

    public static IEnumerable<object[]> Structs() =>
        Layout.RootElement.GetProperty("structs").EnumerateObject().Select(p => new object[] { p.Name });

    [Theory]
    [MemberData(nameof(Structs))]
    public void Struct_matches_clang_layout(string name)
    {
        var type = typeof(NativeMethods).Assembly.GetType($"LibcameraSharp.Native.Interop.{name}")
                   ?? throw new Xunit.Sdk.XunitException($"generated struct {name} not found");
        var expected = Layout.RootElement.GetProperty("structs").GetProperty(name);

        Assert.Equal(expected.GetProperty("size").GetInt32(), Marshal.SizeOf(type));

        // Every field lands where clang put it.
        foreach (var field in expected.GetProperty("offsets").EnumerateObject())
            Assert.Equal(field.Value.GetInt32(), (int)Marshal.OffsetOf(type, field.Name));
    }

    /// <summary>The public geometry types are read straight out of libcamera's storage, so they must match its layout byte for byte.</summary>
    [Theory]
    [InlineData(typeof(Point), "libcamera_point")]
    [InlineData(typeof(Size), "libcamera_size")]
    [InlineData(typeof(Rectangle), "libcamera_rectangle")]
    [InlineData(typeof(SizeRange), "libcamera_size_range")]
    [InlineData(typeof(PixelFormat), "libcamera_pixel_format")]
    public void Public_struct_matches_native_layout(Type publicType, string nativeName)
    {
        var expected = Layout.RootElement.GetProperty("structs").GetProperty(nativeName);
        Assert.Equal(expected.GetProperty("size").GetInt32(), Marshal.SizeOf(publicType));

        // Same field order: compare offsets positionally, since the names differ (x vs X).
        var nativeOffsets = expected.GetProperty("offsets").EnumerateObject().Select(o => o.Value.GetInt32()).Order().ToArray();
        var publicOffsets = publicType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(f => (int)Marshal.OffsetOf(publicType, f.Name)).Order().ToArray();
        Assert.Equal(nativeOffsets, publicOffsets);
    }
}
