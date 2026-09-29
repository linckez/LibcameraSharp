using System.Runtime.CompilerServices;
using LibcameraSharp;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

// Testing without a camera: the samples docs/getting-started.md points to, compiled, so they can't drift
// from the surface.
//
// Things to know:
// - Pixels are XRGB8888 unless you say otherwise (four bytes a pixel: blue, green, red, unused). In that
//   format a photo saves as JPEG or PNG with Skia alone, on any machine; other formats need FFmpeg, as a
//   real camera's photos do.
// - CapturePhotoAsync() without options calls CapturePhotoAsync(options): a subclass gets both by
//   overriding the second, but a mock set up on one returns nothing for the other.
// - An override of ReadFramesAsync needs its own [EnumeratorCancellation]; it isn't inherited.
// - FramesDropped reads the real camera's count, so a fake that reports drops overrides it too.
// - DisposeAsync isn't virtual (it runs DisposeAsyncCore, as .NET's dispose pattern has it), so a mock can't set it
//   up or verify it directly; with Moq, verify Protected().Verify<ValueTask>("DisposeAsyncCore", Times.Once()).

// A camera for a laptop or a CI runner: it replays one picture. Only what the app calls is overridden;
// anything else throws NotSupportedException.
sealed class ReplayCamera(byte[] pixels, Size size) : CameraDevice
{
    public override Task<Photo> CapturePhotoAsync(PhotoOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(LibcameraSharpModelFactory.Photo(pixels, size, options: options,
            metadata: LibcameraSharpModelFactory.CaptureMetadata(exposureTime: TimeSpan.FromMilliseconds(4), analogueGain: 3.0f)));

    public override async IAsyncEnumerable<VideoFrame> ReadFramesAsync(FrameOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (uint sequence = 0; ; sequence++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(33), cancellationToken);
            yield return LibcameraSharpModelFactory.VideoFrame([pixels], size, sequence: sequence);
        }
    }

    public override CameraCapabilities Capabilities => LibcameraSharpModelFactory.CameraCapabilities(
        new Dictionary<ControlKey, (double Min, double Max, double? Default)?>
        {
            [Controls.ExposureTime] = (100, 1_000_000, 20_000),
            [Controls.AnalogueGain] = (1, 16, 1),
        });
}

static class TestingSamples
{
    public static void Register(WebApplicationBuilder builder, byte[] pixels)
    {
        if (builder.Environment.IsDevelopment())
            builder.Services.AddSingleton<CameraDevice>(_ => new ReplayCamera(pixels, new Size(640, 480)));
        else
            builder.Services.AddSingleton(_ => CameraDevice.Open());
    }

    public static async Task WithMoq(byte[] pixels)
    {
        var camera = new Mock<CameraDevice>();
        camera.Setup(c => c.CapturePhotoAsync(It.IsAny<PhotoOptions>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(LibcameraSharpModelFactory.Photo(pixels, new Size(640, 480)));

        Photo photo = await camera.Object.CapturePhotoAsync(new PhotoOptions());
        await photo.SaveAsync("photo.jpg");
    }
}
