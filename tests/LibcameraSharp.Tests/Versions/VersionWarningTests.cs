namespace LibcameraSharp.Tests.Versions;

/// <summary>A libcamera outside the generated minor version gets a warning, never a refusal. Needs no camera.</summary>
public class VersionWarningTests
{
    [Theory]
    [InlineData("v0.7.2+rpt20260817")]   // Raspberry Pi OS's own build
    [InlineData("v0.7.0")]               // an older patch release
    [InlineData("0.7.9")]                // a newer one, reported without the v
    public void Any_release_of_the_generated_minor_is_supported(string running)
    {
        Assert.Null(CameraManager.VersionWarning(running, "0.7.2"));
    }

    [Theory]
    [InlineData("v0.8.0")]               // the next minor
    [InlineData("v0.6.1")]               // an older one
    [InlineData("v1.0.0-custom")]        // a fork with numbering of its own
    public void Another_minor_warns_and_names_both_versions(string running)
    {
        var warning = CameraManager.VersionWarning(running, "0.7.2");

        Assert.NotNull(warning);
        Assert.Contains(running, warning);
        Assert.Contains("0.7.x", warning);
    }

    [Fact]
    public void An_unreported_version_warns_rather_than_matching_by_accident()
    {
        Assert.NotNull(CameraManager.VersionWarning("", "0.7.2"));
    }
}
