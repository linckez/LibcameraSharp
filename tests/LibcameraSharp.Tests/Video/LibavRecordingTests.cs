using System.Diagnostics;
using System.Text.Json;

namespace LibcameraSharp.Tests.Video;

/// <summary>
/// H.264 recording and MP4 muxing, judged by ffprobe — the tool everyone else uses to decide whether
/// a video file is real — and by decoding the result back to pictures.
/// </summary>
/// <remarks>
/// The reference here is FFmpeg itself: if ffprobe reports the stream we intended and ffmpeg can
/// decode frames out of it, the file is correct.
/// </remarks>
[Collection("camera")]
public class LibavRecordingTests(ITestOutputHelper output)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_recorded_mp4_decodes_back_to_pictures()
    {
        Assert.SkipUnless(HasFfmpeg() && HasFfprobe(), "ffmpeg is not installed in this environment");
        var dir = Directory.CreateTempSubdirectory().FullName;
        var path = Path.Combine(dir, "clip.mp4");

        using var manager = new CameraManager();
        SkipOnTheVirtualCamera(manager);
        using var session = new CameraSession(manager);
        var encoder = new LibavH264Encoder();
        using (var container = new ContainerOutput(File.Create(path), "mp4", ownsStream: true))
        {
            session.Configure(session.CreateVideoConfiguration(main: new StreamDescription(new Size(640, 480), PixelFormats.BGR888)));
            session.StartRecording(encoder, container);
            await WaitForFramesAsync(session, encoder, 12);
            session.StopRecording();
        }

        var probe = Probe(path);
        output.WriteLine(probe.ToString());
        Assert.Equal("h264", probe.GetProperty("codec_name").GetString());
        Assert.True(int.Parse(probe.GetProperty("nb_frames").GetString()!) >= 10, "too few frames in the file");

        // Decode the first frame back out; if the container or the bitstream were wrong this produces nothing.
        Run("ffmpeg", $"-v error -i {path} -frames:v 1 -y {Path.Combine(dir, "frame.png")}");
        var frame = new FileInfo(Path.Combine(dir, "frame.png"));
        Assert.True(frame.Exists && frame.Length > 1024, "ffmpeg could not decode a frame from the recording");

        using var bitmap = SkiaSharp.SKBitmap.Decode(frame.FullName);
        Assert.Equal((640, 480), (bitmap.Width, bitmap.Height));
        output.WriteLine($"{encoder.FramesEncoded} frames, {new FileInfo(path).Length} bytes, bitrate target {encoder.Bitrate}");
    }

    /// <summary>
    /// MPEG-TS straight into memory: nothing touches the disk, and ffprobe still reads a real stream.
    /// This is the shape a web handler needs.
    /// </summary>
    [Fact]
    public async Task Muxes_mpegts_into_a_memory_stream()
    {
        Assert.SkipUnless(HasFfprobe(), "ffprobe is not installed in this environment");
        using var buffer = new MemoryStream();

        using var manager = new CameraManager();
        SkipOnTheVirtualCamera(manager);
        using var session = new CameraSession(manager);
        var encoder = new LibavH264Encoder();
        using (var container = new ContainerOutput(buffer, "mpegts"))
        {
            session.Configure(session.CreateVideoConfiguration(main: new StreamDescription(new Size(640, 480), PixelFormats.BGR888)));
            session.StartRecording(encoder, container);
            await WaitForFramesAsync(session, encoder, 15);
            session.StopRecording();
        }

        var bytes = buffer.ToArray();
        Assert.Equal(0x47, bytes[0]);                                           // every MPEG-TS packet starts with the sync byte
        var probe = Probe(WriteTemp(bytes, "clip.ts"));
        output.WriteLine($"{bytes.Length} bytes in memory; {probe}");
        Assert.Equal("h264", probe.GetProperty("codec_name").GetString());
        Assert.Equal(640, probe.GetProperty("width").GetInt32());
        Assert.True(int.Parse(probe.GetProperty("nb_read_frames").GetString()!) >= 10, "too few frames in the stream");
    }

    /// <summary>
    /// A stream that cannot seek — a socket, a response body — gets fragmented MP4 without being asked,
    /// because a plain MP4 seeks back to write its index and would produce an unplayable file here.
    /// </summary>
    [Fact]
    public async Task An_unseekable_stream_gets_a_fragmented_mp4()
    {
        Assert.SkipUnless(HasFfprobe(), "ffprobe is not installed in this environment");
        using var buffer = new MemoryStream();
        await using var forwardOnly = new ForwardOnlyStream(buffer);

        using var manager = new CameraManager();
        SkipOnTheVirtualCamera(manager);
        using var session = new CameraSession(manager);
        var encoder = new LibavH264Encoder();
        using (var container = new ContainerOutput(forwardOnly, "mp4"))
        {
            session.Configure(session.CreateVideoConfiguration(main: new StreamDescription(new Size(640, 480), PixelFormats.BGR888)));
            session.StartRecording(encoder, container);
            await WaitForFramesAsync(session, encoder, 15);
            session.StopRecording();
        }

        var bytes = buffer.ToArray();
        Assert.Contains("moof", Boxes(bytes));                                  // fragments, not one index at the end
        var probe = Probe(WriteTemp(bytes, "clip.mp4"));
        output.WriteLine($"{bytes.Length} bytes, boxes: {string.Join(' ', Boxes(bytes).Distinct())}");
        Assert.Equal("h264", probe.GetProperty("codec_name").GetString());
        Assert.True(int.Parse(probe.GetProperty("nb_read_frames").GetString()!) >= 10, "too few frames in the stream");
    }

    /// <summary>A seekable stream gets an ordinary MP4, which is what a file or a blob upload wants.</summary>
    [Fact]
    public async Task A_seekable_stream_gets_an_ordinary_mp4()
    {
        Assert.SkipUnless(HasFfprobe(), "ffprobe is not installed in this environment");
        using var buffer = new MemoryStream();

        using var manager = new CameraManager();
        SkipOnTheVirtualCamera(manager);
        using var session = new CameraSession(manager);
        var encoder = new LibavH264Encoder();
        using (var container = new ContainerOutput(buffer, "mp4"))
        {
            session.Configure(session.CreateVideoConfiguration(main: new StreamDescription(new Size(640, 480), PixelFormats.BGR888)));
            session.StartRecording(encoder, container);
            await WaitForFramesAsync(session, encoder, 15);
            session.StopRecording();
        }

        var bytes = buffer.ToArray();
        Assert.DoesNotContain("moof", Boxes(bytes));
        Assert.Contains("moov", Boxes(bytes));                                  // the index libav seeked back to write
        Assert.Equal("h264", Probe(WriteTemp(bytes, "clip.mp4")).GetProperty("codec_name").GetString());
    }

    /// <summary>The ISO-BMFF box types in a file, in order — enough to tell a fragmented MP4 from a plain one.</summary>
    private static List<string> Boxes(byte[] bytes)
    {
        var boxes = new List<string>();
        for (var offset = 0; offset + 8 <= bytes.Length;)
        {
            var size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
            boxes.Add(System.Text.Encoding.ASCII.GetString(bytes, offset + 4, 4));
            if (size < 8)
                break;
            offset += (int)size;
        }
        return boxes;
    }

    private static string WriteTemp(byte[] bytes, string name)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>A stream you can only write forwards, like a socket or an HTTP response body.</summary>
    private sealed class ForwardOnlyStream(System.IO.Stream inner) : System.IO.Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }

    private static JsonElement Probe(string path)
    {
        var json = Run("ffprobe", $"-v error -select_streams v:0 -show_streams -count_frames -of json {path}");
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("streams")[0].Clone();
    }

    private static string Run(string file, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true })!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"{file} {arguments}\n{stderr}");
        return stdout;
    }

    private static bool HasFfprobe() => Exists("ffprobe");

    private static bool HasFfmpeg() => Exists("ffmpeg");

    private static bool Exists(string tool)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(tool, "-version") { RedirectStandardOutput = true, RedirectStandardError = true })!;
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // The `virtual` camera stamps SensorTimestamp when it queues a request (virtual.cpp:361-363) and
    // queues them in bursts, so frames can share a timestamp, which every muxer rejects. A real sensor
    // stamps each exposure, so these run on real cameras; the camera-free test still loads FFmpeg here.
    private static void SkipOnTheVirtualCamera(CameraManager manager) =>
        Assert.SkipWhen(manager.Cameras[0].Properties.TryGet(Properties.PipelineHandler, out var pipeline) && pipeline == "virtual",
            "the virtual camera timestamps frames when they are queued, not exposed");

    private static async Task WaitForFramesAsync(CameraSession session, Encoder encoder, int frames)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (encoder.FramesEncoded < frames)
        {
            if (session.LastFrameError is { } error)
                throw new InvalidOperationException($"encoding failed after {encoder.FramesEncoded} frames", error);
            await Task.Delay(20, timeout.Token);
        }
        Assert.Null(session.LastFrameError);
    }
}
