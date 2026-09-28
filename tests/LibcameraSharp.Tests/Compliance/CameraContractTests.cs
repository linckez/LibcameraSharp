namespace LibcameraSharp.Tests.Compliance;

/// <summary>
/// Ports of libcamera's own camera tests <c>test/camera/capture.cpp</c> and
/// <c>camera_reconfigure.cpp</c>, kept for what they catch in the binding: the re-queue loop and leaked
/// buffer descriptors.
/// </summary>
[Collection("camera")]
public class CameraContractTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Queue one request per buffer and keep re-queueing: twice the buffer count of frames must arrive, all complete.</summary>
    /// <remarks>libcamera <c>test/camera/capture.cpp</c>. Its <c>bufferCompleted</c> half needs a signal we don't expose yet (phase G).</remarks>
    [Fact]
    public async Task Capture_delivers_twice_the_buffer_count_of_frames()
    {
        using var manager = new CameraManager();
        using var camera = manager.Cameras[0].Acquire();
        using var config = camera.GenerateConfiguration(StreamRole.VideoRecording)!;
        Assert.NotEqual(ConfigurationStatus.Invalid, config.Validate());
        camera.Configure(config);

        var stream = config[0].Stream;
        using var allocator = new FrameBufferAllocator(camera);
        var buffers = allocator.Allocate(stream);
        var requests = buffers.Select((buffer, i) =>
        {
            var request = camera.CreateRequest((ulong)i);
            request.AddBuffer(stream, buffer);
            return request;
        }).ToList();

        var target = buffers.Count * 2;
        var completed = 0;
        camera.Start();
        foreach (var request in requests)
            camera.QueueRequest(request);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(500 * target));        // libcamera allows 500 ms a frame
        while (completed < target)
        {
            var done = await camera.CompletedRequests.ReadAsync(timeout.Token);
            Assert.Equal(RequestStatus.Complete, done.Status);
            completed++;
            done.Reuse();
            camera.QueueRequest(done);
        }

        camera.Stop();
        foreach (var request in requests)
            request.Dispose();
        output.WriteLine($"{completed} frames from {buffers.Count} buffers");
    }

    /// <summary>Reconfiguring and restarting repeatedly must not leak file descriptors.</summary>
    /// <remarks>
    /// libcamera <c>test/camera/camera_reconfigure.cpp</c> counts the IPA proxy process's open fds
    /// across ten reconfigurations. That process is libcamera-internal; the binding's own fds are
    /// what we can leak (DMA-BUF handles, mappings), so this counts <c>/proc/self/fd</c> instead.
    /// </remarks>
    [Fact]
    public async Task Reconfiguring_repeatedly_leaks_no_file_descriptors()
    {
        using var manager = new CameraManager();
        using var camera = manager.Cameras[0].Acquire();
        var counts = new List<int>();

        for (var cycle = 0; cycle < 4; cycle++)
        {
            await StartCaptureStopAsync(camera);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            counts.Add(OpenFileDescriptors());
        }

        output.WriteLine($"open fds per cycle: {string.Join(", ", counts)}");
        Assert.All(counts, count => Assert.True(count > 0, "no open fds at all — the count is not measuring anything"));
        Assert.Equal(counts[^2], counts[^1]);           // the first cycles warm caches; the last two must match
    }

    private static async Task StartCaptureStopAsync(ActiveCamera camera)
    {
        using var config = camera.GenerateConfiguration(StreamRole.ViewFinder)!;
        Assert.NotEqual(ConfigurationStatus.Invalid, config.Validate());
        camera.Configure(config);

        var stream = config[0].Stream;
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
        using var mapped = (await camera.CompletedRequests.ReadAsync(Ct)).Buffer(stream).Map();
        Assert.NotEqual(0, mapped[0].Length);

        camera.Stop();
        while (camera.CompletedRequests.TryRead(out _))
        {
            // Cancelled requests land in the channel too; drain them so the next cycle doesn't read this one's.
        }
        foreach (var request in requests)
            request.Dispose();
        allocator.Free(stream);
    }

    private static int OpenFileDescriptors() => Directory.GetFileSystemEntries("/proc/self/fd").Length;
}
