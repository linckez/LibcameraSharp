namespace LibcameraSharp.Tests.Capture;

/// <summary>
/// With <c>LIBCAMERASHARP_TEST_GC_STRESS=1</c>, every call runs a full blocking collection plus finalizers,
/// so an object that only native code still points at gets finalized right away. Cheap way to
/// make "the GC collected it while libcamera was still using it" deterministic.
/// </summary>
internal static class GcStress
{
    public static readonly bool Enabled = Environment.GetEnvironmentVariable("LIBCAMERASHARP_TEST_GC_STRESS") == "1";

    public static void Point()
    {
        if (!Enabled)
            return;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
    }
}
