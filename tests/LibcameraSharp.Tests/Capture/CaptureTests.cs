

namespace LibcameraSharp.Tests.Capture;

/// <summary>
/// The request and buffer machinery of the managed API on the first camera: stopping, disposal
/// order, and freeing buffers, checked against what libcamera reports.
/// </summary>
[Collection("camera")]
public class CaptureTests(ITestOutputHelper output)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Stop_returns_queued_requests_as_cancelled()
    {
        using var session = await PreparedCamera.OpenAsync(output);
        var camera = session.Camera;

        camera.Start();
        foreach (var r in session.Requests)
            camera.QueueRequest(r);
        camera.Stop();

        // Every request comes back exactly once; those not captured are cancelled.
        var seen = new HashSet<Request>();
        while (seen.Count < session.Requests.Count)
        {
            var done = await camera.CompletedRequests.ReadAsync(new CancellationTokenSource(Timeout).Token);
            Assert.True(seen.Add(done), "request delivered twice");
            Assert.False(done.IsQueued);
            Assert.Contains(done.Status, new[] { RequestStatus.Complete, RequestStatus.Cancelled });
        }
    }

    [Fact]
    public async Task Disposing_out_of_order_while_streaming_is_safe()
    {
        // The crash this guards against: freeing buffers under a running camera segfaults libcamera.
        var session = await PreparedCamera.OpenAsync(output);
        session.Camera.Start();
        foreach (var r in session.Requests)
            session.Camera.QueueRequest(r);

        foreach (var r in session.Requests)
            r.Dispose();                 // queued: deferred until completion
        session.Allocator.Dispose();     // running: stops the camera first
        session.Camera.Dispose();
        session.Manager.Dispose();
    }

    [Fact]
    public async Task Buffers_can_be_freed_per_stream_and_allocated_again()
    {
        // lc-compliance helpers/capture.cpp:stop() frees each stream's buffers and asserts the
        // allocator reports nothing allocated; its next run allocates again on the same allocator.
        using var session = await PreparedCamera.OpenAsync(output);
        Assert.True(session.Allocator.IsAllocated);
        Assert.NotEmpty(session.Allocator.Buffers(session.Stream));

        foreach (var request in session.Requests)
            request.Dispose();
        session.Allocator.Free(session.Stream);

        Assert.False(session.Allocator.IsAllocated);
        Assert.Empty(session.Allocator.Buffers(session.Stream));
        Assert.NotEmpty(session.Allocator.Allocate(session.Stream));
        Assert.True(session.Allocator.IsAllocated);
    }

    [Fact]
    public async Task Freeing_buffers_the_camera_is_streaming_into_is_refused()
    {
        using var session = await PreparedCamera.OpenAsync(output);
        session.Camera.Start();
        foreach (var request in session.Requests)
            session.Camera.QueueRequest(request);

        Assert.Throws<InvalidOperationException>(() => session.Allocator.Free(session.Stream));
        session.Camera.Stop();
        session.Allocator.Free(session.Stream);                      // allowed once stopped
        Assert.False(session.Allocator.IsAllocated);
    }
}
