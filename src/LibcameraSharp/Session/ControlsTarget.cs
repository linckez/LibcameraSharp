namespace LibcameraSharp;

/// <summary>A request whose frames a call waits for: its number, in the run the camera was in.</summary>
/// <param name="Run">The camera's stop count when the call started waiting.</param>
/// <param name="From">The first request of that run to carry the call's controls.</param>
internal readonly record struct ControlsTarget(int Run, long From);
