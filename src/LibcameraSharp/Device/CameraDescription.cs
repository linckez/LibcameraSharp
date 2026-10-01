namespace LibcameraSharp;

/// <summary>
/// What libcamera reports about a camera: its properties, the limits of every control it advertises, and the
/// configuration in effect. Read it for anything <see cref="CameraDevice"/>'s own members don't cover.
/// </summary>
/// <remarks>
/// A snapshot, taken again each time the camera is reconfigured, so read <see cref="CameraDevice.Advanced"/> again
/// rather than keeping one. To drive a camera yourself, use <c>LibcameraSharp.Core</c> directly instead of a
/// <see cref="CameraDevice"/>.
/// </remarks>
public sealed class CameraDescription
{
    internal CameraDescription(string id, IReadOnlyList<KeyValuePair<ControlKey, object>> properties, IReadOnlyList<ControlLimits> controls,
        ConfiguredStream? capture = null, ConfiguredStream? preview = null, ConfiguredStream? raw = null,
        ColorSpace? colourSpace = null, Orientation? orientation = null, int? bufferCount = null)
    {
        Id = id;
        _properties = [.. properties.Select(entry => KeyValuePair.Create(entry.Key, Copy(entry.Value)))];
        Controls = controls.ToList().AsReadOnly();
        Capture = capture;
        Preview = preview;
        Raw = raw;
        ColorSpace = colourSpace;
        Orientation = orientation;
        BufferCount = bufferCount;
    }

    /// <summary>libcamera's id for the camera, as in <see cref="CameraInfo.Id"/>.</summary>
    public string Id { get; }

    private readonly KeyValuePair<ControlKey, object>[] _properties;

    /// <summary>Every property the camera reports, such as its model, location and pixel array.</summary>
    /// <remarks>Array values are copies, so a change to what you read can't reach anyone else's.</remarks>
    public IReadOnlyList<KeyValuePair<ControlKey, object>> Properties =>
        Array.AsReadOnly(_properties.Select(entry => KeyValuePair.Create(entry.Key, Copy(entry.Value))).ToArray());

    /// <summary>
    /// Every control the camera advertises, with its smallest, largest and default value as libcamera stores them: a
    /// rectangle for <c>ScalerCrop</c>, say. An array control's limits are single values, such as one duration for
    /// <c>FrameDurationLimits</c>.
    /// </summary>
    public IReadOnlyList<ControlLimits> Controls { get; }

    /// <summary>The main stream, as libcamera configured it, or null before the camera is first set up.</summary>
    public ConfiguredStream? Capture { get; }

    /// <summary>The smaller second stream, when the options asked for one.</summary>
    public ConfiguredStream? Preview { get; }

    /// <summary>The sensor's raw stream, on cameras that have one.</summary>
    public ConfiguredStream? Raw { get; }

    /// <summary>The colour space of the processed streams.</summary>
    public ColorSpace? ColorSpace { get; }

    /// <summary>How the image is rotated and flipped, or null before the camera is first set up.</summary>
    public Orientation? Orientation { get; }

    /// <summary>How many buffers each stream has, or null before the camera is first set up.</summary>
    public int? BufferCount { get; }

    /// <summary>The value of <paramref name="property"/>, such as <c>Properties.Model</c>, if the camera reports it.</summary>
    public bool TryGetProperty<T>(Property<T> property, out T value)
    {
        foreach (var (key, found) in _properties)
        {
            if (key.Id == property.Id)
            {
                value = (T)Copy(found);
                return true;
            }
        }
        value = default!;
        return false;
    }

    private static object Copy(object value) => value is Array array ? array.Clone() : value;
}
