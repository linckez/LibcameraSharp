namespace LibcameraSharp;

/// <summary>One camera attached to this machine, as <see cref="CameraDevice.Enumerate"/> reports it.</summary>
/// <param name="Id">The camera's stable identifier, which <see cref="CameraDevice.Open"/> takes.</param>
/// <param name="Model">The sensor's model name, e.g. <c>imx477</c>.</param>
/// <param name="Rotation">How the module is mounted, in degrees.</param>
/// <param name="Location">Where the camera is, when the platform says.</param>
/// <param name="Num">The camera's index, in this list's order.</param>
public readonly record struct CameraInfo(string Id, string Model, int Rotation, Location? Location, int Num);
