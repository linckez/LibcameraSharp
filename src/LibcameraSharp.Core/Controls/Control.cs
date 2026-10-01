using System.Runtime.CompilerServices;

namespace LibcameraSharp;

/// <summary>
/// A typed control key. <typeparamref name="T"/> is the managed type you read and write, e.g.
/// <c>request.Controls.Set(Controls.ExposureTime, 10_000)</c>.
/// </summary>
/// <typeparam name="T">Managed value type: a scalar, an enum, or an array of either.</typeparam>
public sealed class Control<T>(ControlId id, string name, ControlType type, ControlDirection direction, bool isArray, int? fixedLength)
    : ControlKey((uint)id, name, type, direction, isArray, fixedLength, typeof(T))
{
    /// <summary>The id as a <see cref="ControlId"/>.</summary>
    public ControlId ControlId { get; } = id;

    internal override object BoxInt32(int value) => typeof(T).IsEnum ? Unsafe.BitCast<int, T>(value)! : value;
}
