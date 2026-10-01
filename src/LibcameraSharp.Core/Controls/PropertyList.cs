using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Core;

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
