namespace LibcameraSharp;

/// <summary>
/// Describes one control or property: its numeric id, name and value shape. Use the typed keys
/// in <see cref="Controls"/> and <see cref="Properties"/> rather than constructing these directly.
/// </summary>
public abstract class ControlKey
{
    private protected ControlKey(uint id, string name, ControlType type, ControlDirection direction, bool isArray, int? fixedLength, Type valueType)
    {
        Id = id;
        Name = name;
        Type = type;
        Direction = direction;
        IsArray = isArray;
        FixedLength = fixedLength;
        ValueType = valueType;
    }

    /// <summary>Numeric id libcamera uses (see <see cref="ControlId"/> / <see cref="PropertyId"/>).</summary>
    public uint Id { get; }

    /// <summary>libcamera's name, e.g. <c>"ExposureTime"</c>.</summary>
    public string Name { get; }

    /// <summary>Element type of the value.</summary>
    public ControlType Type { get; }

    /// <summary>Whether the value is set by the application, reported by libcamera, or both. Properties are always <see cref="ControlDirection.Out"/>.</summary>
    public ControlDirection Direction { get; }

    /// <summary>True when the value is an array of <see cref="Type"/> rather than a single element.</summary>
    public bool IsArray { get; }

    /// <summary>Element count for fixed-size arrays; null for scalars and variable-length arrays.</summary>
    public int? FixedLength { get; }

    /// <summary>The managed type values are read and written as, e.g. <see cref="int"/>, an enum, or <c>float[]</c>.</summary>
    public Type ValueType { get; }

    /// <inheritdoc/>
    public override string ToString() => Name;

    // Enumerated controls are stored as int32; the typed key knows the enum to box them as (AeState.Converged, not 2).
    internal abstract object BoxInt32(int value);

    // Control and property ids overlap; a lookup by id alone has to know which kind a key is.
    internal virtual bool IsProperty => false;
}
