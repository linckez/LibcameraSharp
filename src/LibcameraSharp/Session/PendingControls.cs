using System.Collections;

namespace LibcameraSharp;

/// <summary>
/// Controls to apply to the camera, validated against what it advertises: setting a control the
/// camera doesn't support throws.
/// </summary>
/// <remarks>An instance made before the camera is known validates when it is attached.</remarks>
internal sealed class PendingControls : IEnumerable<KeyValuePair<ControlKey, object>>
{
    private readonly Dictionary<ControlKey, object> _values = [];
    private readonly Lock _lock = new();
    private ControlInfoMap? _advertised;
    private long _version;

    /// <summary>Creates an empty set.</summary>
    public PendingControls() { }

    /// <summary>Creates a set validated against what <paramref name="session"/>'s camera advertises.</summary>
    public PendingControls(CameraSession session) : this(session.CameraControls) { }

    internal PendingControls(ControlInfoMap advertised) => _advertised = advertised;

    /// <summary>Moves whenever a value actually changes, so the camera can tell when new controls went out.</summary>
    internal long Version { get { lock (_lock) return _version; } }

    /// <summary>Number of controls set.</summary>
    public int Count { get { lock (_lock) return _values.Count; } }

    /// <summary>Sets a control. Throws when the camera is known and doesn't advertise it.</summary>
    /// <exception cref="ArgumentException">The camera does not advertise <paramref name="key"/>.</exception>
    public PendingControls Set<T>(Control<T> key, T value)
    {
        Validate(key);
        lock (_lock)
            Store(key, value!);
        return this;
    }

    /// <summary>The value set for <paramref name="key"/>.</summary>
    /// <exception cref="KeyNotFoundException">The control isn't set.</exception>
    public T Get<T>(Control<T> key) { lock (_lock) return (T)_values[key]; }

    /// <summary>Gets the value set for <paramref name="key"/>, if any.</summary>
    public bool TryGet<T>(Control<T> key, out T value)
    {
        bool found;
        object? boxed;
        lock (_lock)
            found = _values.TryGetValue(key, out boxed);
        if (found)
        {
            value = (T)boxed!;
            return true;
        }
        value = default!;
        return false;
    }

    /// <summary>True when <paramref name="key"/> is set.</summary>
    public bool Contains(ControlKey key) { lock (_lock) return _values.ContainsKey(key); }

    /// <summary>Removes <paramref name="key"/>.</summary>
    public bool Remove(ControlKey key)
    {
        lock (_lock)
        {
            if (!_values.Remove(key))
                return false;
            _version++;
            return true;
        }
    }

    /// <summary>Copies every value from <paramref name="other"/>, overwriting.</summary>
    public void SetControls(PendingControls other)
    {
        // Snapshot the other's values under its lock, then take ours: holding both would let
        // a.SetControls(b) deadlock against b.SetControls(a).
        KeyValuePair<ControlKey, object>[] incoming;
        lock (other._lock)
            incoming = [.. other._values];

        foreach (var (key, _) in incoming)
            Validate(key);

        lock (_lock)
            foreach (var (key, value) in incoming)
                Store(key, value);
    }

    /// <summary>Removes every value.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            if (_values.Count == 0)
                return;
            _values.Clear();
            _version++;
        }
    }

    // Sets a value under _lock, counting it as a change only when it differs; arrays compare by element.
    private void Store(ControlKey key, object value)
    {
        if (_values.TryGetValue(key, out var existing) && StructuralComparisons.StructuralEqualityComparer.Equals(existing, value))
            return;
        _values[key] = value;
        _version++;
    }

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<ControlKey, object>> GetEnumerator()
    {
        // A snapshot, so iterating never throws because another thread set a control.
        KeyValuePair<ControlKey, object>[] snapshot;
        lock (_lock)
            snapshot = [.. _values];
        return ((IEnumerable<KeyValuePair<ControlKey, object>>)snapshot).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc/>
    public override string ToString() => "{" + string.Join(", ", this.Select(kv => $"{kv.Key.Name}: {Render(kv.Value)}")) + "}";

    internal PendingControls Clone()
    {
        var copy = new PendingControls(_advertised!);
        foreach (var (k, v) in _values)
            copy._values[k] = v;
        return copy;
    }

    // Attaching validates everything set so far, so a configuration built before the camera still gets checked.
    internal void Attach(ControlInfoMap advertised)
    {
        _advertised = advertised;
        foreach (var key in _values.Keys)
            Validate(key);
    }

    // Copies every value into a libcamera list, adding the exposure/gain modes the camera needs.
    internal void CopyTo(ControlList target, ControlInfoMap advertised)
    {
        KeyValuePair<ControlKey, object>[] pending;
        lock (_lock)
        {
            if (_values.Count == 0)
                return;
            pending = [.. _values];
        }

        using var source = new ControlList();
        foreach (var (key, value) in pending)
            source.SetValue(key, value);
        ControlPatching.Apply(source, target, advertised.Contains);
    }

    // Forgets the values once they have gone out: a value is sent once, with one request or the start
    // list. Forgetting is not a change, so the version stays and a wait for the sent values still ends.
    internal void Forget()
    {
        lock (_lock)
            _values.Clear();
    }

    /// <summary>True when the camera advertises <paramref name="key"/>, or isn't known yet.</summary>
    internal bool Advertises(ControlKey key) => _advertised is null || _advertised.Contains(key);

    private void Validate(ControlKey key)
    {
        if (_advertised is not null && !_advertised.Contains(key))
            throw new ArgumentException($"Control {key.Name} is not advertised by libcamera for this camera.", nameof(key));
    }

    internal static string Render(object value) => value is Array a ? "[" + string.Join(", ", a.Cast<object>()) + "]" : value.ToString() ?? "";
}
