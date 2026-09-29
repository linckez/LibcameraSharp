using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using LibcameraSharp.Native.Interop;

namespace LibcameraSharp;

/// <summary>
/// Converts between libcamera's <c>ControlValue</c> storage and the managed type of a
/// <see cref="Control{T}"/> / <see cref="Property{T}"/> key.
/// </summary>
/// <remarks>
/// Every key's managed type was chosen by the generator to match its <see cref="ControlKey.Type"/>,
/// so the runtime checks are only "does the stored value have the type the key says" and array-ness.
/// </remarks>
internal static unsafe class ControlValueCodec
{
    /// <summary>Reads the stored value as <typeparamref name="T"/>; throws when the stored type or shape doesn't match.</summary>
    /// <remarks>
    /// Shape comes from the stored value, not the key: libcamera stores the min/max of an array control
    /// such as <c>FrameDurationLimits</c> as scalars (per-element bounds), and the value itself as an array.
    /// </remarks>
    public static T Read<T>(libcamera_control_value* value, ControlKey key)
    {
        var (data, count, isArray) = Storage(value, key);

        // Strings are stored as a byte array without a terminator.
        if (key.Type == ControlType.String)
            return (T)(object)Encoding.UTF8.GetString((byte*)data, count);
        if (isArray != typeof(T).IsArray)
            throw new InvalidCastException($"{key.Name}: the stored value is {(isArray ? "an array" : "a scalar")} but {typeof(T).Name} was requested.");
        if (isArray)
            return (T)(object)ReadArray(key.Type, (void*)data, count);
        return ReadScalar<T>(key.Type, (void*)data);
    }

    /// <summary>Reads the stored value as a boxed object of the key's managed type: a string, an array, a scalar or an enum, as libcamera stores it.</summary>
    public static object ReadBoxed(libcamera_control_value* value, ControlKey key)
    {
        var (data, count, isArray) = Storage(value, key);
        if (key.Type == ControlType.String)
            return Encoding.UTF8.GetString((byte*)data, count);

        if (isArray)
            return ReadArray(key.Type, (void*)data, count);

        // Enumerated controls are stored as int32; box them as the generated enum so callers see AeState.Converged, not 2.
        var scalar = ReadArray(key.Type, (void*)data, 1).GetValue(0)!;
        return key.Type == ControlType.Integer32 ? key.BoxInt32((int)scalar) : scalar;
    }

    /// <summary>Stores a boxed value (as returned by <see cref="ReadBoxed"/>) into <paramref name="target"/>.</summary>
    public static void WriteBoxed(libcamera_control_value* target, object value, ControlKey key) => Write(target, value, key);

    /// <summary>Stores <paramref name="value"/> into <paramref name="target"/> using the key's libcamera type.</summary>
    public static void Write<T>(libcamera_control_value* target, T value, ControlKey key)
    {
        // Strings always go in as byte arrays; a "scalar" string would make libcamera allocate one byte.
        if (key.Type == ControlType.String)
        {
            var bytes = Encoding.UTF8.GetBytes((string)(object)value!);
            fixed (byte* p = bytes)
                NativeMethods.libcamera_control_value_set(target, (libcamera_control_type)key.Type, p, true, (nuint)bytes.Length);
            return;
        }

        if (key.IsArray)
        {
            WriteArray(target, key, (Array)(object)value!);
            return;
        }

        WriteScalar(target, key, value!);
    }

    // Scalars go through the boxed path so a T of object (untyped copies) and a concrete T behave the same.
    private static void WriteScalar(libcamera_control_value* target, ControlKey key, object value)
    {
        Span<byte> bytes = stackalloc byte[16];
        var size = key.Type switch
        {
            ControlType.Bool => Put(bytes, (bool)value),
            ControlType.Byte => Put(bytes, (byte)value),
            ControlType.Unsigned16 => Put(bytes, (ushort)value),
            ControlType.Unsigned32 => Put(bytes, (uint)value),
            ControlType.Integer32 => Put(bytes, value is Enum e ? Convert.ToInt32(e) : (int)value),
            ControlType.Integer64 => Put(bytes, (long)value),
            ControlType.Float => Put(bytes, (float)value),
            ControlType.Rectangle => Put(bytes, (Rectangle)value),
            ControlType.Size => Put(bytes, (Size)value),
            ControlType.Point => Put(bytes, (Point)value),
            _ => throw new NotSupportedException($"{key.Name}: control type {key.Type} isn't supported."),
        };
        fixed (byte* p = bytes)
            NativeMethods.libcamera_control_value_set(target, (libcamera_control_type)key.Type, p, false, 1);

        static int Put<TValue>(Span<byte> into, TValue v) where TValue : unmanaged
        {
            MemoryMarshal.Write(into, in v);
            return Unsafe.SizeOf<TValue>();
        }
    }

    private static (nint Data, int Count, bool IsArray) Storage(libcamera_control_value* value, ControlKey key)
    {
        var stored = (ControlType)NativeMethods.libcamera_control_value_type(value);
        if (stored != key.Type)
            throw new InvalidCastException($"{key.Name}: the stored value is a {stored}, but the key is a {key.Type}.");
        return ((nint)NativeMethods.libcamera_control_value_get(value),
                (int)NativeMethods.libcamera_control_value_num_elements(value),
                NativeMethods.libcamera_control_value_is_array(value));
    }

    // Public Point/Size/Rectangle are layout-identical to libcamera's (asserted by LayoutTests), so they read straight from storage.
    private static T ReadScalar<T>(ControlType type, void* data) => type switch
    {
        ControlType.Bool => Unsafe.BitCast<bool, T>(*(bool*)data),
        ControlType.Byte => Unsafe.BitCast<byte, T>(*(byte*)data),
        ControlType.Unsigned16 => Unsafe.BitCast<ushort, T>(*(ushort*)data),
        ControlType.Unsigned32 => Unsafe.BitCast<uint, T>(*(uint*)data),
        ControlType.Integer32 => Unsafe.BitCast<int, T>(*(int*)data),
        ControlType.Integer64 => Unsafe.BitCast<long, T>(*(long*)data),
        ControlType.Float => Unsafe.BitCast<float, T>(*(float*)data),
        ControlType.Rectangle => Unsafe.BitCast<Rectangle, T>(*(Rectangle*)data),
        ControlType.Size => Unsafe.BitCast<Size, T>(*(Size*)data),
        ControlType.Point => Unsafe.BitCast<Point, T>(*(Point*)data),
        _ => throw new NotSupportedException($"Control type {type} isn't supported."),
    };

    private static Array ReadArray(ControlType type, void* data, int count) => type switch
    {
        ControlType.Bool => new ReadOnlySpan<bool>(data, count).ToArray(),
        ControlType.Byte => new ReadOnlySpan<byte>(data, count).ToArray(),
        ControlType.Unsigned16 => new ReadOnlySpan<ushort>(data, count).ToArray(),
        ControlType.Unsigned32 => new ReadOnlySpan<uint>(data, count).ToArray(),
        ControlType.Integer32 => new ReadOnlySpan<int>(data, count).ToArray(),
        ControlType.Integer64 => new ReadOnlySpan<long>(data, count).ToArray(),
        ControlType.Float => new ReadOnlySpan<float>(data, count).ToArray(),
        ControlType.Rectangle => new ReadOnlySpan<Rectangle>(data, count).ToArray(),
        ControlType.Size => new ReadOnlySpan<Size>(data, count).ToArray(),
        ControlType.Point => new ReadOnlySpan<Point>(data, count).ToArray(),
        _ => throw new NotSupportedException($"Control type {type} isn't supported."),
    };

    private static void WriteArray(libcamera_control_value* target, ControlKey key, Array values)
    {
        if (key.FixedLength is { } expected && values.Length != expected)
            throw new ArgumentException($"{key.Name} expects exactly {expected} elements, got {values.Length}.");

        // All element types are blittable; pin the array and hand libcamera the raw bytes.
        var handle = GCHandle.Alloc(values, GCHandleType.Pinned);
        try
        {
            NativeMethods.libcamera_control_value_set(target, (libcamera_control_type)key.Type,
                (void*)handle.AddrOfPinnedObject(), true, (nuint)values.Length);
        }
        finally
        {
            handle.Free();
        }
    }
}
