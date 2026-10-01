namespace LibcameraSharp;

/// <summary>One camera attached to this machine, as <see cref="CameraDevice.Enumerate"/> reports it.</summary>
/// <param name="Id">The camera's stable identifier, which <see cref="CameraDevice.Open"/> takes.</param>
/// <param name="Model">The sensor's model name, e.g. <c>imx477</c>, or null when the camera doesn't report one.</param>
/// <param name="Rotation">How the module is mounted, in degrees, or null when the camera doesn't report it.</param>
/// <param name="Location">Where the camera is, when the platform says.</param>
public readonly record struct CameraInfo(string Id, string? Model, int? Rotation, Location? Location);
