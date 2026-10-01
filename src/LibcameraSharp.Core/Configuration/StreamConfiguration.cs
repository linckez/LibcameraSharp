using LibcameraSharp.Native.Interop;

namespace LibcameraSharp.Core;

/// <summary>
/// Settings for one stream: pixel format, size, and what libcamera derived from them (stride,
/// frame size, buffer count). Change <see cref="PixelFormat"/> / <see cref="Size"/> before
/// <see cref="CameraConfiguration.Validate"/>; read the rest after.
/// </summary>
/// <remarks>A view into its <see cref="CameraConfiguration"/>, valid for as long as that is.</remarks>
public sealed unsafe class StreamConfiguration
{
    private readonly libcamera_stream_configuration* _cfg;
    private readonly CameraConfiguration? _owner;   // keeps the parent alive while this view exists

    internal StreamConfiguration(libcamera_stream_configuration* cfg, CameraConfiguration? owner)
    {
        _cfg = cfg;
        _owner = owner;
    }

    /// <summary>Pixel format of the frames. See <see cref="Formats"/> for what's supported.</summary>
    public PixelFormat PixelFormat
    {
        get => new(_cfg->pixel_format.fourcc, _cfg->pixel_format.modifier);
        set => _cfg->pixel_format = new libcamera_pixel_format { fourcc = value.Fourcc, modifier = value.Modifier };
    }

    /// <summary>Frame size in pixels.</summary>
    public Size Size
    {
        get => new(_cfg->size.width, _cfg->size.height);
        set => _cfg->size = new libcamera_size { width = value.Width, height = value.Height };
    }

    /// <summary>Bytes per image row, including padding. Set by libcamera during validation.</summary>
    public uint Stride
    {
        get => _cfg->stride;
        set => _cfg->stride = value;
    }

    /// <summary>Bytes per frame. Set by libcamera during validation.</summary>
    public uint FrameSize
    {
        get => _cfg->frame_size;
        set => _cfg->frame_size = value;
    }

    /// <summary>How many buffers <see cref="FrameBufferAllocator"/> will allocate for the stream.</summary>
    public uint BufferCount
    {
        get => _cfg->buffer_count;
        set => _cfg->buffer_count = value;
    }

    /// <summary>Colour space, or null when libcamera hasn't chosen one yet.</summary>
    public ColorSpace? ColorSpace
    {
        get => NativeMethods.libcamera_stream_configuration_has_color_space(_cfg)
            ? LibcameraSharp.ColorSpace.From(NativeMethods.libcamera_stream_configuration_get_color_space(_cfg))
            : null;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var native = value.Value.ToNative();
            NativeMethods.libcamera_stream_configuration_set_color_space(_cfg, &native);
        }
    }

    /// <summary>Formats and sizes this stream supports.</summary>
    public StreamFormats Formats => new(NativeMethods.libcamera_stream_configuration_formats(_cfg));

    /// <summary>The configured stream. Only available after <see cref="ActiveCamera.Configure"/>.</summary>
    /// <exception cref="InvalidOperationException">The camera hasn't been configured with this configuration.</exception>
    public CameraStream Stream
    {
        get
        {
            var stream = NativeMethods.libcamera_stream_configuration_stream(_cfg);
            return stream is null ? throw new InvalidOperationException("The camera has not been configured with this configuration yet.") : new CameraStream(stream, _owner);
        }
    }

    /// <summary>libcamera's description, e.g. <c>1920x1080-NV12</c>.</summary>
    public override string ToString() => NativeMethods.libcamera_stream_configuration_to_string(_cfg) ?? "";
}
