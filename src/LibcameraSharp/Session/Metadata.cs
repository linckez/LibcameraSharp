using System.Collections;

namespace LibcameraSharp;

/// <summary>
/// What the camera reported for one frame — exposure, gain, colour temperature, focus state,
/// sensor timestamp — as a snapshot that outlives the request. Read it with the typed keys:
/// <code>
/// if (metadata.TryGet(Controls.ExposureTime, out var exposure)) ...   // int, no cast
/// var state = metadata.Get(Controls.AeState);                          // AeState enum
/// </code>
/// </summary>
/// <remarks>
/// Iterating yields boxed values, for logging. Controls with no generated key are kept too, under
/// their id; any key for that id reads them.
/// </remarks>
internal sealed class Metadata : IReadOnlyCollection<KeyValuePair<ControlKey, object>>
{
    // By id: a key built elsewhere for the same id must find the same entry.
    private readonly Dictionary<uint, KeyValuePair<ControlKey, object>> _values = [];

    /// <summary>A snapshot of <paramref name="list"/>, e.g. a request's metadata, or one you built to feed an encoder.</summary>
    public Metadata(ControlList list)
    {
        foreach (var (id, _) in list)
        {
            var key = list.KeyFor(id);
            _values[id] = new(key, list.GetValue(key));
        }
    }

    /// <summary>Metadata from values you already have, such as a model factory's; a later value for the same control wins.</summary>
    public Metadata(IEnumerable<KeyValuePair<ControlKey, object>> values)
    {
        foreach (var (key, value) in values)
            _values[key.Id] = new(key, value);
    }

    /// <summary>No metadata — for encoding a frame that did not come from a request.</summary>
    public static Metadata Empty { get; } = new();

    private Metadata()
    {
    }

    /// <summary>Number of controls reported.</summary>
    public int Count => _values.Count;

    /// <summary>The controls reported, e.g. to see what a camera offers.</summary>
    public IEnumerable<ControlKey> Keys => _values.Values.Select(entry => entry.Key);

    /// <summary>The value for <paramref name="key"/>.</summary>
    /// <exception cref="KeyNotFoundException">The frame has no value for that control.</exception>
    public T Get<T>(Control<T> key) => (T)GetValue(key);

    /// <summary>The value for <paramref name="key"/>, if the frame reported it.</summary>
    public bool TryGet<T>(Control<T> key, out T value)
    {
        if (_values.TryGetValue(key.Id, out var entry))
        {
            value = (T)entry.Value;
            return true;
        }
        value = default!;
        return false;
    }

    /// <summary>True when the frame reported <paramref name="key"/>.</summary>
    public bool Contains(ControlKey key) => !key.IsProperty && _values.ContainsKey(key.Id);

    /// <summary>The value for any key as a boxed object, for generic code such as logging. Prefer <see cref="Get{T}"/> at ordinary call sites.</summary>
    /// <exception cref="KeyNotFoundException">The frame has no value for that control.</exception>
    public object GetValue(ControlKey key)
    {
        if (!key.IsProperty && _values.TryGetValue(key.Id, out var entry))
            return entry.Value;
        throw new KeyNotFoundException($"The frame reported no {key.Name}.");
    }

    /// <summary>Every reported control with its boxed value, for logging; use <see cref="Get{T}"/> for typed access.</summary>
    public IEnumerator<KeyValuePair<ControlKey, object>> GetEnumerator() => _values.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>One line per control, e.g. <c>ExposureTime: 20000, AnalogueGain: 1.5</c>.</summary>
    public override string ToString() => string.Join(", ", _values.Values.Select(kv => $"{kv.Key.Name}: {PendingControls.Render(kv.Value)}"));
}
