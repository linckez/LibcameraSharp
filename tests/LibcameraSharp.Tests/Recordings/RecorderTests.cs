namespace LibcameraSharp.Tests.Recordings;

/// <summary>
/// ★3 end to end: record, stop, and get a file that plays. Stopping is what writes a container's
/// trailer, so a recording that is never stopped is a file that will not open.
/// </summary>
[Collection("camera")]
public class RecorderTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("recorder-tests").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Recording_mjpeg_to_a_file_writes_frames()
    {
        Assert.SkipUnless(TestCamera.Present, "no libcamera device on this machine");

        var path = Path.Combine(_directory, "clip.mjpeg");
        var ct = TestContext.Current.CancellationToken;

        await using (var camera = CameraDevice.Open())
        {
            var recording = await camera.StartRecordingAsync(path, new VideoOptions { Codec = VideoCodec.Mjpeg }, ct);

            // A camera takes about a second to start on a Pi; wait for frames rather than a fixed time.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (recording.FrameCount == 0)
                await Task.Delay(20, timeout.Token);

            await recording.StopAsync();
            await recording.StopAsync();             // stopping twice must be harmless
        }

        var written = await File.ReadAllBytesAsync(path, ct);
        Assert.True(written.Length > 0);
        Assert.Equal(0xFF, written[0]);            // an MJPEG stream is JPEGs back to back
        Assert.Equal(0xD8, written[1]);
    }

    [Fact]
    public async Task Recording_to_a_stream_stops_when_the_caller_hangs_up()
    {
        Assert.SkipUnless(TestCamera.Present, "no libcamera device on this machine");

        await using var camera = CameraDevice.Open();
        using var destination = new MemoryStream();
        using var hangUp = new CancellationTokenSource(TimeSpan.FromSeconds(3));   // past a Pi's camera start

        // This is ★2's shape: a web handler whose request cancellation is the client disconnecting.
        await camera.RecordToAsync(destination, new VideoOptions { Codec = VideoCodec.Mjpeg },
            cancellationToken: hangUp.Token);

        Assert.True(destination.ToArray().Length > 0, "nothing was written before the hang-up");
    }

    /// <summary>A write that fails while recording, such as a full disk, is reported by StopAsync; disposing never throws.</summary>
    [Fact]
    public async Task A_failed_write_is_reported_by_stopping_not_by_disposing()
    {
        Assert.SkipUnless(TestCamera.Present, "no libcamera device on this machine");
        var ct = TestContext.Current.CancellationToken;

        await using var camera = CameraDevice.Open();
        var recording = await camera.StartRecordingAsync(new FullDisk(), new VideoOptions { Codec = VideoCodec.Mjpeg }, cancellationToken: ct);
        await recording.WhenFailed.WaitAsync(TimeSpan.FromSeconds(10), ct);

        await Assert.ThrowsAsync<IOException>(recording.StopAsync);
        await recording.DisposeAsync();
    }

    // A destination that refuses every write, as a full disk does.
    private sealed class FullDisk : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("No space left on device");

        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("No space left on device");
    }
}
