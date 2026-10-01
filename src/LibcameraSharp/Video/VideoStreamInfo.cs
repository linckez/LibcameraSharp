namespace LibcameraSharp;

/// <summary>What an encoder is about to produce, so an output can prepare for it.</summary>
/// <param name="Codec">The codec the frames are encoded with.</param>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
/// <param name="FrameRate">Nominal frames per second.</param>
/// <param name="CodecExtraData">Codec setup bytes a container must store — H.264's SPS and PPS. Empty when the codec needs none.</param>
/// <param name="ColorSpace">The colour space the frames are in, for the container to record; null when unknown.</param>
internal readonly record struct VideoStreamInfo(VideoCodec Codec, int Width, int Height, double FrameRate, ReadOnlyMemory<byte> CodecExtraData = default, ColorSpace? ColorSpace = null);
