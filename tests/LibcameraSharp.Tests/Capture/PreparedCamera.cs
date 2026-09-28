using LibcameraSharp.Advanced;
using Stream = LibcameraSharp.Advanced.Stream;   // libcamera's stream, not System.IO's

namespace LibcameraSharp.Tests.Capture;

/// <summary>
/// Tests that open a camera share it, so they must not run in parallel: two <see cref="CameraManager"/>s
/// in one process is an abort, not an exception.
/// </summary>
[CollectionDefinition("camera", DisableParallelization = true)]
public class CameraCollection;

/// <summary>
/// The first camera, configured with buffers allocated and one request per buffer — the state every
/// capture test starts from.
/// </summary>
internal sealed class PreparedCamera : IDisposable
{
    public required CameraManager Manager { get; init; }
    public required ActiveCamera Camera { get; init; }
    public required CameraConfiguration Configuration { get; init; }
    public required StreamConfiguration Config { get; init; }
    public required Stream Stream { get; init; }
    public required FrameBufferAllocator Allocator { get; init; }
    public required IReadOnlyList<Request> Requests { get; init; }

    public static Task<PreparedCamera> OpenAsync(ITestOutputHelper output)
    {
        var manager = new CameraManager();
        GcStress.Point();
        var camera = manager.Cameras[0].Acquire();   // the unacquired handle is dropped on purpose
        GcStress.Point();
        var configuration = camera.GenerateConfiguration(StreamRole.ViewFinder)!;
        Assert.NotEqual(ConfigurationStatus.Invalid, configuration.Validate());
        camera.Configure(configuration);
        GcStress.Point();

        var config = configuration[0];
        var stream = config.Stream;
        output.WriteLine($"{camera.Id}: {config} stride={config.Stride} frameSize={config.FrameSize}");

        var allocator = new FrameBufferAllocator(camera);
        var buffers = allocator.Allocate(stream);
        GcStress.Point();
        var requests = buffers.Select((b, i) =>
        {
            var r = camera.CreateRequest((ulong)i);
            r.AddBuffer(stream, b);
            GcStress.Point();
            return r;
        }).ToList();

        return Task.FromResult(new PreparedCamera
        {
            Manager = manager, Camera = camera, Configuration = configuration, Config = config,
            Stream = stream, Allocator = allocator, Requests = requests,
        });
    }

    // Teardown in ownership order: stop, requests, buffers, configuration, camera, manager.
    public void Dispose()
    {
        Camera.Stop();
        foreach (var r in Requests)
            r.Dispose();
        Allocator.Dispose();
        Configuration.Dispose();
        Camera.Dispose();
        Manager.Dispose();
    }
}
