using System.Collections;
using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Core;

/// <summary>
/// Which controls a camera supports and their ranges (<see cref="Camera.Controls"/>).
/// Index with a typed key: <c>camera.Controls[Controls.ExposureTime].Max&lt;int&gt;()</c>.
/// </summary>
public sealed unsafe class ControlInfoMap : IEnumerable<ControlInfo>
{
    private readonly libcamera_control_info_map* _map;

    internal ControlInfoMap(libcamera_control_info_map* map) => _map = map;

    /// <summary>Number of supported controls.</summary>
    public int Count => (int)NativeMethods.libcamera_control_info_map_size(_map);

    /// <summary>True when the camera supports <paramref name="key"/>.</summary>
    public bool Contains(ControlKey key) => NativeMethods.libcamera_control_info_map_count(_map, key.Id) > 0;

    /// <summary>The range for <paramref name="key"/>.</summary>
    /// <exception cref="KeyNotFoundException">The camera doesn't support the control.</exception>
    public ControlInfo this[ControlKey key] =>
        TryGet(key) ?? throw new KeyNotFoundException($"The camera does not support {key.Name}.");

    /// <summary>The range for <paramref name="key"/>, or null when the camera doesn't support it.</summary>
    public ControlInfo? TryGet(ControlKey key)
    {
        var info = NativeMethods.libcamera_control_info_map_find(_map, key.Id);
        return info is null ? null : new ControlInfo(info, key);
    }

    /// <summary>Every supported control with its range.</summary>
    public IEnumerator<ControlInfo> GetEnumerator() => Snapshot().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private List<ControlInfo> Snapshot()
    {
        var items = new List<ControlInfo>(Count);
        var iter = NativeMethods.libcamera_control_info_map_iter_create(_map);
        if (iter is null)
            return items;
        try
        {
            // Ids this binding doesn't know (a newer libcamera) are skipped rather than failing the whole listing.
            while (NativeMethods.libcamera_control_info_map_iter_has_next(iter))
            {
                if (ControlKeys.ByControlId(NativeMethods.libcamera_control_info_map_iter_key(iter)) is { } key)
                    items.Add(new ControlInfo(NativeMethods.libcamera_control_info_map_iter_value(iter), key));
                NativeMethods.libcamera_control_info_map_iter_next(iter);
            }
        }
        finally
        {
            NativeMethods.libcamera_control_info_map_iter_destroy(iter);
        }
        return items;
    }
}
