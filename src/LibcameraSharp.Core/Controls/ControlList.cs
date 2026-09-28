using System.Collections;
using LibcameraSharp.Native;
using LibcameraSharp.Native.Interop;

namespace LibcameraSharp;

/// <summary>
/// The controls attached to a request (<see cref="Request.Controls"/>), or the metadata
/// libcamera reports back (<see cref="Request.Metadata"/>). Read and write with the typed keys
/// in <see cref="Controls"/>:
/// <code>
/// request.Controls.Set(Controls.ExposureTime, 10_000);
/// if (request.Metadata.TryGet(Controls.AnalogueGain, out var gain)) …
/// </code>
/// </summary>
public unsafe class ControlList : IEnumerable<KeyValuePair<uint, ControlType>>, IDisposable
{
    private readonly ControlListHandle? _owned;
    private readonly libcamera_control_list* _borrowed;

    /// <summary>Wraps a list libcamera owns (a request's controls/metadata, a camera's properties).</summary>
    internal ControlList(libcamera_control_list* list) => _borrowed = list;

    /// <summary>Creates an empty, standalone list, e.g. to pass to <see cref="ActiveCamera.Start"/>.</summary>
    public ControlList() => _owned = new ControlListHandle();

    internal libcamera_control_list* Pointer => _owned is null ? _borrowed : _owned.Pointer;

    /// <summary>Number of controls currently set.</summary>
    public int Count => (int)NativeMethods.libcamera_control_list_size(Pointer);

    /// <summary>True when <paramref name="key"/> has a value in this list.</summary>
    public bool Contains(ControlKey key) => NativeMethods.libcamera_control_list_contains(Pointer, key.Id);

    /// <summary>Gets the value for <paramref name="key"/>.</summary>
    /// <exception cref="KeyNotFoundException">The list has no value for the key.</exception>
    public T Get<T>(Control<T> key) => GetCore<T>(key);

    /// <summary>Gets the value for <paramref name="key"/> if the list contains it.</summary>
    public bool TryGet<T>(Control<T> key, out T value) => TryGetCore(key, out value);

    /// <summary>
    /// Gets the value for any key as a boxed object (scalar, string, or array), for generic code
    /// such as logging. Prefer the typed <see cref="Get{T}"/> at ordinary call sites.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The list has no value for the key.</exception>
    public object GetValue(ControlKey key)
    {
        var value = NativeMethods.libcamera_control_list_get(Pointer, (libcamera_property_id)key.Id);
        return value is null
            ? throw new KeyNotFoundException($"{key.Name} is not present in this list.")
            : ControlValueCodec.ReadBoxed(value, key);
    }

    /// <summary>Sets <paramref name="key"/> to <paramref name="value"/>.</summary>
    public void Set<T>(Control<T> key, T value)
    {
        var tmp = NativeMethods.libcamera_control_value_create();
        try
        {
            ControlValueCodec.Write(tmp, value, key);
            NativeMethods.libcamera_control_list_set(Pointer, (libcamera_property_id)key.Id, tmp);
        }
        finally
        {
            NativeMethods.libcamera_control_value_destroy(tmp);
        }
    }

    /// <summary>Sets any key from a boxed value, the counterpart of <see cref="GetValue"/>, for copying values between lists generically.</summary>
    public void SetValue(ControlKey key, object value)
    {
        var tmp = NativeMethods.libcamera_control_value_create();
        try
        {
            ControlValueCodec.WriteBoxed(tmp, value, key);
            NativeMethods.libcamera_control_list_set(Pointer, (libcamera_property_id)key.Id, tmp);
        }
        finally
        {
            NativeMethods.libcamera_control_value_destroy(tmp);
        }
    }

    /// <summary>Removes every value.</summary>
    public void Clear() => NativeMethods.libcamera_control_list_clear(Pointer);

    /// <summary>Copies every value from <paramref name="other"/>, including controls with no generated key, keeping existing values unless <paramref name="overwrite"/> is set.</summary>
    public void Merge(ControlList other, bool overwrite = false)
    {
        // libcamera's keep-existing path logs the name of every control it skips, which reads the
        // list's id map — and a list created with `new ControlList()` has none, so libcamera would
        // dereference null (it documents merging lists from different id maps as undefined). The
        // shim cannot hand us libcamera's global id map, so do the same copy here when a collision
        // could happen on such a list. The outcome is identical, minus libcamera's log line.
        if (!overwrite && NativeMethods.libcamera_control_list_id_map(Pointer) is null)
        {
            MergeKeepingExisting(other);
            return;
        }

        var policy = overwrite
            ? libcamera_control_merge_policy.LIBCAMERA_CONTROL_MERGE_OVERWRITE_EXISTING
            : libcamera_control_merge_policy.LIBCAMERA_CONTROL_MERGE_KEEP_EXISTING;
        NativeMethods.libcamera_control_list_merge(Pointer, other.Pointer, policy);
    }

    private void MergeKeepingExisting(ControlList other)
    {
        foreach (var (id, _) in other)
        {
            if (!Contains(id))
                CopyValue(other, id);
        }
    }

    /// <summary>Copies one stored value by id without decoding it, so ids with no generated key copy too.</summary>
    internal void CopyValue(ControlList source, uint id)
    {
        var value = NativeMethods.libcamera_control_list_get(source.Pointer, (libcamera_property_id)id);
        if (value is not null)
            NativeMethods.libcamera_control_list_set(Pointer, (libcamera_property_id)id, value);
    }

    /// <summary>The generated key for an id in this list, or one described from its stored value.</summary>
    internal ControlKey KeyFor(uint id)
    {
        if (ControlKeys.ByControlId(id) is { } key)
            return key;

        var value = NativeMethods.libcamera_control_list_get(Pointer, (libcamera_property_id)id);
        return new UnrecognisedControl(
            id,
            (ControlType)NativeMethods.libcamera_control_value_type(value),
            NativeMethods.libcamera_control_value_is_array(value));
    }

    private bool Contains(uint id) => NativeMethods.libcamera_control_list_contains(Pointer, id);

    /// <summary>Releases a standalone list. Lists that belong to a request or camera are unaffected.</summary>
    public void Dispose() => _owned?.Dispose();

    /// <summary>The ids and types of every value set, for diagnostics.</summary>
    public IEnumerator<KeyValuePair<uint, ControlType>> GetEnumerator() => Snapshot().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Every value by name, e.g. <c>{ExposureTime: 20000, AnalogueGain: 1.5}</c>. Unknown ids show numerically.</summary>
    public override string ToString()
    {
        var parts = Snapshot().Select(kv =>
        {
            if (LookupKey(kv.Key) is not { } key)
                return $"{kv.Key}: <{kv.Value}>";
            var value = GetValue(key);
            var text = value is Array array ? "[" + string.Join(", ", array.Cast<object>()) + "]" : value.ToString();
            return $"{key.Name}: {text}";
        });
        return "{" + string.Join(", ", parts) + "}";
    }

    private protected virtual ControlKey? LookupKey(uint id) => ControlKeys.ByControlId(id);

    // Shared by ControlList and PropertyList: libcamera stores properties in the same structure.
    private protected T GetCore<T>(ControlKey key) =>
        TryGetCore<T>(key, out var value) ? value : throw new KeyNotFoundException($"{key.Name} is not present in this list.");

    private protected bool TryGetCore<T>(ControlKey key, out T value)
    {
        var stored = NativeMethods.libcamera_control_list_get(Pointer, (libcamera_property_id)key.Id);
        if (stored is null)
        {
            value = default!;
            return false;
        }
        value = ControlValueCodec.Read<T>(stored, key);
        return true;
    }

    private List<KeyValuePair<uint, ControlType>> Snapshot()
    {
        var items = new List<KeyValuePair<uint, ControlType>>(Count);
        var iter = NativeMethods.libcamera_control_list_iter(Pointer);
        try
        {
            while (!NativeMethods.libcamera_control_list_iter_end(iter))
            {
                var id = NativeMethods.libcamera_control_list_iter_id(iter);
                var type = (ControlType)NativeMethods.libcamera_control_value_type(NativeMethods.libcamera_control_list_iter_value(iter));
                items.Add(new(id, type));
                NativeMethods.libcamera_control_list_iter_next(iter);
            }
        }
        finally
        {
            NativeMethods.libcamera_control_list_iter_destroy(iter);
        }
        return items;
    }
}

/// <summary>
/// A camera's static properties (<see cref="Camera.Properties"/>), read with the keys in
/// <see cref="Properties"/>: <c>camera.Properties.Get(Properties.Model)</c>.
/// </summary>
public sealed unsafe class PropertyList : ControlList
{
    internal PropertyList(libcamera_control_list* list) : base(list) { }

    /// <summary>Gets the value for <paramref name="key"/>.</summary>
    /// <exception cref="KeyNotFoundException">The camera doesn't report this property.</exception>
    public T Get<T>(Property<T> key) => GetCore<T>(key);

    /// <summary>Gets the value for <paramref name="key"/> if the camera reports it.</summary>
    public bool TryGet<T>(Property<T> key, out T value) => TryGetCore(key, out value);

    private protected override ControlKey? LookupKey(uint id) => ControlKeys.ByPropertyId(id);
}
