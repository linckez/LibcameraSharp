namespace LibcameraSharp;

/// <summary>
/// What this camera can actually do: the range of each control it supports, and whether it is
/// monochrome.
/// </summary>
/// <remarks>
/// Check here before relying on a control: a setting this camera does not advertise is skipped, with one
/// warning. Ranges follow the current configuration, so they can change with the sensor mode.
/// </remarks>
public sealed class CameraCapabilities
{
    // The camera's live control map, read only by Snapshot on the session's loop; or a list in advertised order
    // with each control's range (null when its values aren't numbers), which is what callers get.
    private readonly ControlInfoMap? _advertised;
    private readonly IReadOnlyList<KeyValuePair<ControlKey, (double Min, double Max, double? Default)?>>? _listed;
    private readonly bool _isMono;

    private CameraCapabilities(ControlInfoMap advertised, bool isMono) => (_advertised, _isMono) = (advertised, isMono);

    internal CameraCapabilities(IReadOnlyList<KeyValuePair<ControlKey, (double Min, double Max, double? Default)?>> listed, bool isMono) =>
        (_listed, _isMono) = (listed, isMono);

    // A managed copy of what the camera advertises now, made on the session's loop, so callers on other threads
    // never read the native map while the loop reconfigures it.
    internal static CameraCapabilities Snapshot(ControlInfoMap advertised, bool isMono)
    {
        var live = new CameraCapabilities(advertised, isMono);
        return new CameraCapabilities([.. live.Controls.Select(key => KeyValuePair.Create(key, live.Range(key)))], isMono);
    }

    /// <summary>True when the sensor has no colour filter, so colour controls do nothing.</summary>
    public bool IsMono => _isMono;

    /// <summary>Every control this camera advertises.</summary>
    public IEnumerable<ControlKey> Controls => _listed?.Select(entry => entry.Key) ?? _advertised!.Select(info => info.Key);

    /// <summary>True when the camera advertises <paramref name="control"/>, such as <c>Controls.AnalogueGain</c>.</summary>
    public bool Supports(ControlKey control) =>
        _listed is not null ? _listed.Any(entry => entry.Key.Id == control.Id) : Find(control) is not null;

    /// <summary>
    /// The smallest and largest values <paramref name="control"/> accepts, and its default, or null
    /// when the camera does not advertise it or its values are not numbers.
    /// </summary>
    /// <remarks>
    /// Controls whose values are rectangles, sizes or strings, such as <c>ScalerCrop</c>, report null;
    /// read those from <see cref="CameraDescription.Controls"/> on <see cref="CameraDevice.Advanced"/>. Array controls such as
    /// <c>FrameDurationLimits</c> report no default.
    /// </remarks>
    public (double Min, double Max, double? Default)? Range(ControlKey control)
    {
        if (_listed is not null)
            return _listed.FirstOrDefault(entry => entry.Key.Id == control.Id).Value;
        if (Find(control) is not { } info || !IsNumeric(info.Key.Type))
            return null;

        return (Read(info, Bound.Min), Read(info, Bound.Max),
                info.HasDefault && !info.Key.IsArray ? Read(info, Bound.Default) : null);
    }

    private ControlInfo? Find(ControlKey control) =>
        _advertised!.FirstOrDefault(info => info.Key.Id == control.Id);

    private static bool IsNumeric(ControlType type) => type
        is ControlType.Bool or ControlType.Byte or ControlType.Unsigned16 or ControlType.Unsigned32
        or ControlType.Integer32 or ControlType.Integer64 or ControlType.Float;

    private enum Bound { Min, Max, Default }

    // A control's range is typed as the control is -- int for exposure, float for gain -- and the
    // codec refuses to read one as any other type, so the type has to be dispatched, not boxed.
    private static double Read(ControlInfo info, Bound bound) => info.Key.Type switch
    {
        ControlType.Bool => Widen<bool>(info, bound),
        ControlType.Byte => Widen<byte>(info, bound),
        ControlType.Unsigned16 => Widen<ushort>(info, bound),
        ControlType.Unsigned32 => Widen<uint>(info, bound),
        ControlType.Integer32 => Widen<int>(info, bound),
        ControlType.Integer64 => Widen<long>(info, bound),
        ControlType.Float => Widen<float>(info, bound),
        var other => throw new NotSupportedException($"{info.Key.Name} is a {other}, which has no numeric range."),
    };

    private static double Widen<T>(ControlInfo info, Bound bound) where T : IConvertible
    {
        T value = bound switch
        {
            Bound.Min => info.Min<T>(),
            Bound.Max => info.Max<T>(),
            _ => info.Default<T>(),
        };
        return value.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
    }
}
