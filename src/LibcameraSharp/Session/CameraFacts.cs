namespace LibcameraSharp;

/// <summary>What callers on any thread may know about a camera, published by its session's loop.</summary>
/// <param name="Capabilities">The controls it advertises in its current configuration, with their ranges.</param>
/// <param name="Model">The model libcamera reports, or the camera's id when it reports none.</param>
/// <param name="ActiveArea">The sensor's active pixel area, when the camera reports one.</param>
/// <param name="Description">What libcamera reports about the camera, for <see cref="CameraDevice.Advanced"/>.</param>
internal sealed record CameraFacts(CameraCapabilities Capabilities, string Model, Rectangle? ActiveArea, CameraDescription Description);
