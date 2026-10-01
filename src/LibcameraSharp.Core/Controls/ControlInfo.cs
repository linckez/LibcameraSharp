using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Core;

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

    // The limits as boxed values of whatever type the control has, for a managed copy of the camera's controls.
    internal object MinValue => ControlValueCodec.ReadBoxed(NativeMethods.libcamera_control_info_min(_info), Key);

    internal object MaxValue => ControlValueCodec.ReadBoxed(NativeMethods.libcamera_control_info_max(_info), Key);

    internal object? DefaultValue => HasDefault ? ControlValueCodec.ReadBoxed(NativeMethods.libcamera_control_info_def(_info), Key) : null;

    /// <summary>The range as libcamera prints it, e.g. <c>[-1.000000..1.000000]</c> or <c>[0..1]</c>, with the default when there is one.</summary>
    public override string ToString()
    {
        var range = $"[{Render(MinValue)}..{Render(MaxValue)}]";
        return DefaultValue is { } defaultValue ? $"{range} default {Render(defaultValue)}" : range;
    }

    private static string Render(object value) => value switch
    {
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        Array a => "[" + string.Join(", ", a.Cast<object>()) + "]",
        _ => value.ToString() ?? "",
    };
}
