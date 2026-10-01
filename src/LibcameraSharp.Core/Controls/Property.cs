using System.Runtime.CompilerServices;

namespace LibcameraSharp;

/// <summary>
/// A typed camera property key — static facts such as <see cref="Properties.Model"/> or
/// <see cref="Properties.PixelArraySize"/>, read with <c>camera.Properties.Get(key)</c>.
/// </summary>
/// <typeparam name="T">Managed value type.</typeparam>
public sealed class Property<T>(PropertyId id, string name, ControlType type, bool isArray, int? fixedLength)
    : ControlKey((uint)id, name, type, ControlDirection.Out, isArray, fixedLength, typeof(T))
{
    /// <summary>The id as a <see cref="PropertyId"/>.</summary>
    public PropertyId PropertyId { get; } = id;

    internal override object BoxInt32(int value) => typeof(T).IsEnum ? Unsafe.BitCast<int, T>(value)! : value;

    internal override bool IsProperty => true;
}
