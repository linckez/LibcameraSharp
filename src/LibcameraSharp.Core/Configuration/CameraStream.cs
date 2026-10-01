using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Core;

/// <summary>
/// One configured output of a camera. Obtain it from <see cref="StreamConfiguration.Stream"/>
/// after <see cref="ActiveCamera.Configure"/>; use it to allocate buffers and attach them to requests.
/// </summary>
public unsafe sealed class CameraStream : IEquatable<CameraStream>
{
    private readonly CameraConfiguration? _owner;   // libcamera's stream belongs to the camera, but the
                                                    // configuration that named it must outlive this view

    internal CameraStream(libcamera_stream* stream, CameraConfiguration? owner)
    {
        Pointer = stream;
        _owner = owner;
    }

    internal libcamera_stream* Pointer { get; }

    /// <summary>The configuration libcamera applied to this stream.</summary>
    public StreamConfiguration Configuration => new(NativeMethods.libcamera_stream_get_configuration(Pointer), _owner);

    /// <summary>Two instances are equal when they refer to the same libcamera stream.</summary>
    public bool Equals(CameraStream? other) => other is not null && other.Pointer == Pointer;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as CameraStream);

    /// <inheritdoc/>
    public override int GetHashCode() => ((nint)Pointer).GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => Configuration.ToString();
}
