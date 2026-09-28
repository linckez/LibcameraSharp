namespace LibcameraSharp.Tests.SharedManager;

/// <summary>
/// The counted manager behind an <c>Open()</c> that takes no manager. libcamera allows one per
/// process and disposing it disposes every camera it handed out, so without counting the second
/// camera to close would kill the first. Needs a camera, and shares the <c>camera</c> collection with
/// every other test that opens one: two managers in one process is not an exception, it is an abort.
/// </summary>
[Collection("camera")]
public class SharedCameraManagerTests
{
    [Fact]
    public void The_same_manager_is_handed_out_until_the_last_user_lets_go()
    {
        Assert.SkipUnless(TestCamera.Present, "no libcamera device on this machine");
        Assert.Equal(0, SharedCameraManager.Users);

        var first = SharedCameraManager.Acquire();
        var second = SharedCameraManager.Acquire();

        Assert.Same(first, second);
        Assert.Equal(2, SharedCameraManager.Users);

        SharedCameraManager.Release();
        Assert.Equal(1, SharedCameraManager.Users);
        Assert.NotEmpty(first.Cameras);          // still alive: the first user has not let go

        SharedCameraManager.Release();
        Assert.Equal(0, SharedCameraManager.Users);
    }

    [Fact]
    public void A_later_user_gets_a_fresh_manager_after_the_last_one_let_go()
    {
        Assert.SkipUnless(TestCamera.Present, "no libcamera device on this machine");

        var first = SharedCameraManager.Acquire();
        SharedCameraManager.Release();

        var second = SharedCameraManager.Acquire();
        try
        {
            Assert.NotSame(first, second);
            Assert.NotEmpty(second.Cameras);
        }
        finally
        {
            SharedCameraManager.Release();
        }
    }

    [Fact]
    public void Releasing_more_than_was_acquired_is_ignored_rather_than_going_negative()
    {
        SharedCameraManager.Release();
        SharedCameraManager.Release();

        Assert.Equal(0, SharedCameraManager.Users);
    }
}
