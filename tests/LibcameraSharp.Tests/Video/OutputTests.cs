namespace LibcameraSharp.Tests.Video;

/// <summary>A file of encoded video starts at a keyframe, and a destination that fails is reported, not ignored.</summary>
public class OutputTests
{
    private static byte[] Frame(byte value) => [value, value, value, value];

    [Fact]
    public void File_output_starts_at_the_first_keyframe()
    {
        using var file = new MemoryStream();
        using var sink = new FileOutput(file);
        sink.Start();

        sink.OutputFrame(Frame(1), keyframe: false);          // dropped: nothing to decode it against
        sink.OutputFrame(Frame(2), keyframe: false);
        sink.OutputFrame(Frame(3), keyframe: true);
        sink.OutputFrame(Frame(4), keyframe: false);
        sink.Stop();

        Assert.Equal([3, 3, 3, 3, 4, 4, 4, 4], file.ToArray());
    }

    [Fact]
    public void A_failed_write_is_kept_and_stops_the_output()
    {
        // A full disk or a closed connection must reach the recording, which rethrows it on dispose.
        using var sink = new FileOutput(new FullDisk());
        sink.Start();

        sink.OutputFrame(Frame(1), keyframe: true);

        Assert.IsType<IOException>(sink.Failure);
        Assert.False(sink.Recording);
        Assert.True(sink.WhenFailed.IsCompleted);
    }

    private sealed class FullDisk : MemoryStream
    {
        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("No space left on device");
    }
}
