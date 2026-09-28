namespace LibcameraSharp.Tests.Capture;

/// <summary>
/// Port of libcamera's <c>test/camera/statemachine.cpp</c>: which operations are refused in
/// each camera state, and with which errno. Transitions the C# API rules out statically
/// (e.g. configuring an un-acquired camera) have no counterpart here.
/// </summary>
[Collection("camera")]
public class StateMachineTests
{
    private const int EACCES = 13;
    private const int EBUSY = 16;

    [Fact]
    public void Errnos_match_libcamera_per_state()
    {
        using var manager = new CameraManager();
        var camera = manager.Cameras[0];

        // Available -> Acquired. A second acquire is EBUSY, and says which camera.
        var active = camera.Acquire();
        var busy = Assert.Throws<CameraBusyException>(() => camera.Acquire());
        Assert.Equal(EBUSY, busy.Errno);
        Assert.Equal(camera.Id, busy.CameraId);

        // Acquired: nothing capture-related is allowed yet.
        using var config = active.GenerateConfiguration(StreamRole.VideoRecording)!;
        Assert.Equal(EACCES, Assert.Throws<LibcameraException>(() => active.Start()).Errno);
        Assert.Throws<LibcameraException>(() => active.CreateRequest());
        active.Stop();                      // stopping when not running is fine

        // Acquired -> Configured.
        active.Configure(config);
        var request = active.CreateRequest();
        Assert.Equal(EACCES, Assert.Throws<LibcameraException>(() => active.QueueRequest(request)).Errno);
        Assert.False(request.IsQueued, "a refused queue must not leave the request marked queued");
        active.Stop();

        // Configured -> Running.
        using var allocator = new FrameBufferAllocator(active);
        var stream = config[0].Stream;
        var buffers = allocator.Allocate(stream);
        active.Start();
        Assert.Equal(EACCES, Assert.Throws<LibcameraException>(() => active.Start()).Errno);
        Assert.Equal(EACCES, Assert.Throws<LibcameraException>(() => active.Configure(config)).Errno);
        Assert.Equal(EBUSY, Assert.Throws<CameraBusyException>(() => camera.Acquire()).Errno);

        request.AddBuffer(stream, buffers[0]);
        active.QueueRequest(request);

        // Running -> Available; the request comes back cancelled or complete, never lost.
        active.Stop();
        Assert.False(request.IsQueued);
        request.Dispose();
        active.Dispose();
        active.Dispose();                   // releasing twice is harmless (libcamera test/py/unittests.py:test_double_acquire)

        // Released: acquiring again works.
        using var again = camera.Acquire();
    }
}
