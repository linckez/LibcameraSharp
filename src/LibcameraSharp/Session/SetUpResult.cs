namespace LibcameraSharp;

/// <summary>What a setup gives the call that asked for it.</summary>
/// <param name="Target">Which frames carry the call's controls.</param>
/// <param name="Configuration">The configuration now in effect, with what libcamera chose.</param>
/// <param name="FrameRate">A nominal frame rate for an encoder that must state one.</param>
internal readonly record struct SetUpResult(ControlsTarget Target, SessionConfiguration Configuration, double FrameRate);
