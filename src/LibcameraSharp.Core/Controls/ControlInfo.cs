using System.Collections;
using LibcameraSharp.Native.Interop;

namespace LibcameraSharp;

/// <summary>The range a camera supports for one control: minimum, maximum, default and, for enums, the allowed values.</summary>
public sealed unsafe class ControlInfo
{
    private readonly libcamera_control_info* _info;

    internal ControlInfo(libcamera_control_info* info, ControlKey key)
    {
        _info = info;
        Key = key;
    }

    /// <summary>The control this range applies to.</summary>
    public ControlKey Key { get; }

    /// <summary>Smallest allowed value.</summary>
    public T Min<T>() => ControlValueCodec.Read<T>(NativeMethods.libcamera_control_info_min(_info), Key);

    /// <summary>Largest allowed value.</summary>
    public T Max<T>() => ControlValueCodec.Read<T>(NativeMethods.libcamera_control_info_max(_info), Key);

    /// <summary>True when libcamera provides a default for the control; not every control has one.</summary>
    public bool HasDefault => (ControlType)NativeMethods.libcamera_control_value_type(NativeMethods.libcamera_control_info_def(_info)) != ControlType.None;

    /// <summary>Value libcamera uses when the control isn't set.</summary>
    /// <exception cref="InvalidOperationException">The control has no default (<see cref="HasDefault"/> is false).</exception>
    public T Default<T>() =>
        TryGetDefault<T>(out var value) ? value : throw new InvalidOperationException($"{Key.Name} has no default value.");

    /// <summary>Gets the default value if libcamera provides one.</summary>
    public bool TryGetDefault<T>(out T value)
    {
        if (!HasDefault)
        {
            value = default!;
            return false;
        }
        value = ControlValueCodec.Read<T>(NativeMethods.libcamera_control_info_def(_info), Key);
        return true;
    }

    /// <summary>The discrete values allowed, for enumerated controls; empty for ranged ones.</summary>
    public IReadOnlyList<T> Values<T>()
    {
        nuint count;
        var values = NativeMethods.libcamera_control_info_values(_info, &count);
        var stride = NativeMethods.libcamera_control_value_size();

        // A contiguous array of ControlValue objects; step through by their native size.
        var result = new List<T>((int)count);
        for (nuint i = 0; i < count; i++)
            result.Add(ControlValueCodec.Read<T>((libcamera_control_value*)((byte*)values + i * stride), Key));
        return result;
    }

    /// <summary>The range as libcamera prints it, e.g. <c>[-1.000000..1.000000]</c> or <c>[0..1]</c>, with the default when there is one.</summary>
    public override string ToString()
    {
        var min = ControlValueCodec.ReadBoxed(NativeMethods.libcamera_control_info_min(_info), Key);
        var max = ControlValueCodec.ReadBoxed(NativeMethods.libcamera_control_info_max(_info), Key);
        var range = $"[{Render(min)}..{Render(max)}]";
        if (!HasDefault)
            return range;
        return $"{range} default {Render(ControlValueCodec.ReadBoxed(NativeMethods.libcamera_control_info_def(_info), Key))}";
    }

    private static string Render(object value) => value switch
    {
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        Array a => "[" + string.Join(", ", a.Cast<object>()) + "]",
        _ => value.ToString() ?? "",
    };
}

/// <summary>
/// Which controls a camera supports and their ranges (<see cref="Camera.Controls"/>).
/// Index with a typed key: <c>camera.Controls[Controls.ExposureTime].Max&lt;int&gt;()</c>.
/// </summary>
public sealed unsafe class ControlInfoMap : IEnumerable<ControlInfo>
{
    private readonly libcamera_control_info_map* _map;

    internal ControlInfoMap(libcamera_control_info_map* map) => _map = map;

    /// <summary>Number of supported controls.</summary>
    public int Count => (int)NativeMethods.libcamera_control_info_map_size(_map);

    /// <summary>True when the camera supports <paramref name="key"/>.</summary>
    public bool Contains(ControlKey key) => NativeMethods.libcamera_control_info_map_count(_map, key.Id) > 0;

    /// <summary>The range for <paramref name="key"/>.</summary>
    /// <exception cref="KeyNotFoundException">The camera doesn't support the control.</exception>
    public ControlInfo this[ControlKey key] =>
        TryGet(key) ?? throw new KeyNotFoundException($"The camera does not support {key.Name}.");

    /// <summary>The range for <paramref name="key"/>, or null when the camera doesn't support it.</summary>
    public ControlInfo? TryGet(ControlKey key)
    {
        var info = NativeMethods.libcamera_control_info_map_find(_map, key.Id);
        return info is null ? null : new ControlInfo(info, key);
    }

    /// <summary>Every supported control with its range.</summary>
    public IEnumerator<ControlInfo> GetEnumerator() => Snapshot().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private List<ControlInfo> Snapshot()
    {
        var items = new List<ControlInfo>(Count);
        var iter = NativeMethods.libcamera_control_info_map_iter_create(_map);
        if (iter is null)
            return items;
        try
        {
            // Ids this binding doesn't know (a newer libcamera) are skipped rather than failing the whole listing.
            while (NativeMethods.libcamera_control_info_map_iter_has_next(iter))
            {
                if (ControlKeys.ByControlId(NativeMethods.libcamera_control_info_map_iter_key(iter)) is { } key)
                    items.Add(new ControlInfo(NativeMethods.libcamera_control_info_map_iter_value(iter), key));
                NativeMethods.libcamera_control_info_map_iter_next(iter);
            }
        }
        finally
        {
            NativeMethods.libcamera_control_info_map_iter_destroy(iter);
        }
        return items;
    }
}
