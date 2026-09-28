using LibcameraSharp.Advanced;
using Stream = LibcameraSharp.Advanced.Stream;   // every Stream in this file is libcamera's

namespace LibcameraSharp.Tests.Compliance;

/// <summary>
/// Object lifetime: who keeps whom alive, and what happens to a buffer whose owner is gone.
/// </summary>
/// <remarks>
/// libcamera's ownership rules: a stream keeps its configuration alive, a buffer keeps its allocator
/// alive, and a captured frame stays readable after the camera stops. .NET keeps the owner alive by
/// holding a reference, and a freed buffer throws rather than dangling.
/// </remarks>
[Collection("camera")]
public class LifetimeContractTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A stream outlives the local reference to the configuration that named it.</summary>
    [Fact]
    public void Stream_keeps_its_configuration_alive()
    {
        using var manager = new CameraManager();
        using var camera = manager.Cameras[0].Acquire();
        var stream = ConfigureAndLeak(camera);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.NotEqual(new Size(0, 0), stream.Configuration.Size);      // reading through it still works
        output.WriteLine($"stream survived its configuration: {stream}");
    }

    // The CameraConfiguration goes out of scope here; only the Stream is returned.
    private static Stream ConfigureAndLeak(ActiveCamera camera)
    {
        var configuration = camera.GenerateConfiguration(StreamRole.ViewFinder)!;
        Assert.NotEqual(ConfigurationStatus.Invalid, configuration.Validate());
        camera.Configure(configuration);
        return configuration[0].Stream;
    }

    /// <summary>A frame buffer outlives the local reference to its allocator, and mapping it still works.</summary>
    [Fact]
    public void Frame_buffer_keeps_its_allocator_alive()
    {
        using var manager = new CameraManager();
        using var camera = manager.Cameras[0].Acquire();
        using var configuration = camera.GenerateConfiguration(StreamRole.ViewFinder)!;
        Assert.NotEqual(ConfigurationStatus.Invalid, configuration.Validate());
        camera.Configure(configuration);

        var (buffer, weakAllocator) = AllocateAndDropTheReference(camera, configuration[0].Stream);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.True(weakAllocator.TryGetTarget(out var allocator), "the buffer did not keep its allocator alive");
        using (var mapped = buffer.Map())
        {
            Assert.NotEqual(0, mapped[0].Length);
            output.WriteLine($"buffer survived the last reference to its allocator: {mapped[0].Length} bytes");
        }

        allocator.Dispose();                                             // the camera would do this too, at its own disposal
        Assert.Throws<ObjectDisposedException>(() => buffer.Map());
    }

    private static (FrameBuffer Buffer, WeakReference<FrameBufferAllocator> Allocator) AllocateAndDropTheReference(ActiveCamera camera, Stream stream)
    {
        var allocator = new FrameBufferAllocator(camera);
        return (allocator.Allocate(stream)[0], new WeakReference<FrameBufferAllocator>(allocator));
    }

    /// <summary>
    /// A captured frame stays readable after the camera stops — and once its buffers are freed,
    /// reading throws instead of touching memory libcamera has released.
    /// </summary>
    /// <remarks>The buffers are freed with their allocator, so a read after that fails loudly.</remarks>
    [Fact]
    public async Task A_captured_frame_survives_the_stop_but_not_the_free()
    {
        using var manager = new CameraManager();
        using var camera = manager.Cameras[0].Acquire();
        using var configuration = camera.GenerateConfiguration(StreamRole.ViewFinder)!;
        Assert.NotEqual(ConfigurationStatus.Invalid, configuration.Validate());
        camera.Configure(configuration);

        var stream = configuration[0].Stream;
        using var allocator = new FrameBufferAllocator(camera);
        var requests = allocator.Allocate(stream).Select((buffer, i) =>
        {
            var request = camera.CreateRequest((ulong)i);
            request.AddBuffer(stream, buffer);
            return request;
        }).ToList();

        camera.Start();
        foreach (var request in requests)
            camera.QueueRequest(request);
        var done = await camera.CompletedRequests.ReadAsync(Ct);
        var frame = done.Buffer(stream);

        camera.Stop();
        while (camera.CompletedRequests.TryRead(out _))
        {
        }

        using (var mapped = frame.Map())                                  // still readable after the stop
            Assert.NotEqual(0, mapped[0].Length);

        allocator.Free(stream);
        Assert.Throws<ObjectDisposedException>(() => frame.Map());        // and refused once freed
        foreach (var request in requests)
            request.Dispose();
    }

    /// <summary>A camera frees buffers still allocated for it, because libcamera crashes if it doesn't.</summary>
    /// <remarks>
    /// An allocator .NET has not collected yet still holds the camera's buffers, and releasing the
    /// camera meanwhile makes libcamera log "Removing media device /dev/media0 while still in use"
    /// and then abort.
    /// </remarks>
    [Fact]
    public void Disposing_a_camera_frees_buffers_still_allocated_for_it()
    {
        using var manager = new CameraManager();
        var camera = manager.Cameras[0].Acquire();
        using var configuration = camera.GenerateConfiguration(StreamRole.ViewFinder)!;
        Assert.NotEqual(ConfigurationStatus.Invalid, configuration.Validate());
        camera.Configure(configuration);

        var allocator = new FrameBufferAllocator(camera);
        var buffer = allocator.Allocate(configuration[0].Stream)[0];
        Assert.True(allocator.IsAllocated);

        camera.Dispose();                                                // without freeing anything first

        Assert.False(allocator.IsAllocated);
        Assert.Throws<ObjectDisposedException>(() => buffer.Map());
    }
}
