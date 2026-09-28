namespace LibcameraSharp;

/// <summary>
/// Copies user controls into a request, adding what libcamera needs but
/// doesn't infer: since libcamera 0.4, <see cref="LibcameraSharp.Controls.ExposureTime"/> and
/// <see cref="LibcameraSharp.Controls.AnalogueGain"/> only take effect when their <c>*Mode</c> is
/// <c>Manual</c>, and a value of 0 means "back to auto". libcamera itself only patches the modes
/// for <see cref="LibcameraSharp.Controls.AeEnable"/> (<c>Camera::patchControlList</c>).
/// </summary>
internal static class ControlPatching
{
    /// <summary>
    /// Copies every value from <paramref name="source"/> into <paramref name="target"/>, translating
    /// fixed exposure/gain into the corresponding mode when the camera supports that mode control.
    /// </summary>
    /// <param name="source">The user's controls.</param>
    /// <param name="target">A request's controls, or a list for <see cref="ActiveCamera.Start"/>.</param>
    /// <param name="supports">Whether the camera advertises a given control, e.g. <c>camera.Controls.Contains</c>.</param>
    public static void Apply(ControlList source, ControlList target, Func<ControlKey, bool> supports)
    {
        foreach (var (id, _) in source)
        {
            // No generated key: nothing to patch, so pass it on as stored.
            if (ControlKeys.ByControlId(id) is not { } key)
            {
                target.CopyValue(source, id);
                continue;
            }
            var value = source.GetValue(key);

            // A fixed value means manual mode; zero means auto, and the zero itself is not sent.
            if (TryModeFor(key, out var modeKey) && supports(modeKey))
            {
                var isZero = value is int i ? i == 0 : value is float f && f == 0f;
                target.SetValue(modeKey, isZero ? ModeAuto(modeKey) : ModeManual(modeKey));
                if (isZero)
                    continue;
            }

            target.SetValue(key, value);
        }
    }

    // The two controls libcamera pairs with a mode.
    private static bool TryModeFor(ControlKey key, out ControlKey mode)
    {
        if (key.Id == Controls.ExposureTime.Id)
        {
            mode = Controls.ExposureTimeMode;
            return true;
        }
        if (key.Id == Controls.AnalogueGain.Id)
        {
            mode = Controls.AnalogueGainMode;
            return true;
        }
        mode = null!;
        return false;
    }

    private static object ModeAuto(ControlKey mode) =>
        mode.Id == Controls.ExposureTimeMode.Id ? ExposureTimeMode.Auto : AnalogueGainMode.Auto;

    private static object ModeManual(ControlKey mode) =>
        mode.Id == Controls.ExposureTimeMode.Id ? ExposureTimeMode.Manual : AnalogueGainMode.Manual;
}
