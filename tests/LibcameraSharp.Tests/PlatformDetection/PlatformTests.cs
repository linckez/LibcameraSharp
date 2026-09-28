namespace LibcameraSharp.Tests.PlatformDetection;

/// <summary>
/// Platform detection: walk <c>/dev/video*</c>, <c>VIDIOC_QUERYCAP</c> each, match the card name.
/// </summary>
/// <remarks>
/// The assertion keys off whether video devices exist, which is what makes it mean anything:
/// <c>Unknown</c> can only be reached by a <b>successful</b> ioctl that returned a card name we do
/// not recognise, because a failed one skips the device and leaves <c>Missing</c>. So wherever video
/// devices exist this distinguishes "the syscall works" from "the syscall silently fails", which a
/// test accepting either value would not.
/// </remarks>
public class PlatformTests
{
    private static bool HasVideoDevices =>
        Enumerable.Range(0, 64).Any(n => File.Exists($"/dev/video{n}"));

    [Fact]
    public void Reads_the_card_name_when_there_are_video_devices()
    {
        var platform = LibcameraSharp.PlatformDetection.Detect();

        if (HasVideoDevices)
        {
            // Devices answered: a Pi's ISP, or Unknown for anything else — and reaching Unknown at all
            // proves VIDIOC_QUERYCAP succeeded, since a failing ioctl would leave Missing.
            Assert.NotEqual(Platform.Missing, platform);
            Assert.True(platform is Platform.Unknown or Platform.Vc4 or Platform.Pisp or Platform.Legacy);
        }
        else
        {
            // No /dev/video* at all: macOS, the test VM, or a container without them.
            Assert.Equal(Platform.Missing, platform);
        }
    }
}
