namespace LibcameraSharp.Tests.Compliance;

/// <summary>
/// One <c>using LibcameraSharp</c> reaches everything a typical user writes; libcamera's own classes sit in
/// <c>LibcameraSharp.Core</c>, named after its package. Folders never decide a namespace, so this pins it.
/// </summary>
public class NamespaceContractTests
{
    private static readonly string[] LibcameraClasses =
    [
        "ActiveCamera", "BayerFormats", "Camera", "CameraConfiguration", "CameraManager", "CameraStream",
        "ConfigurationStatus", "ControlDirection", "ControlId", "ControlInfo", "ControlInfoMap", "ControlKeys",
        "ControlList", "ControlType", "FrameBuffer", "FrameBufferAllocator", "FrameBufferPlane", "FrameMetadata",
        "FrameStatus", "MappedFrameBuffer", "PropertyId", "PropertyList", "Request", "RequestCompletedEventArgs",
        "RequestStatus", "SensorConfiguration", "SizeRange", "StreamConfiguration", "StreamFormats", "StreamRole",
    ];

    private static IEnumerable<Type> PublicTypes(Type anyTypeInAssembly) =>
        anyTypeInAssembly.Assembly.GetExportedTypes().Where(t => !t.IsNested);

    [Fact]
    public void Libcamera_classes_are_in_their_own_namespace()
    {
        var core = PublicTypes(typeof(CameraManager)).Where(t => t.Namespace == "LibcameraSharp.Core").Select(t => t.Name.Split('`')[0]);
        Assert.Equal(LibcameraClasses, core.Order());
    }

    [Fact]
    public void Core_package_uses_only_the_two_public_namespaces()
    {
        var namespaces = PublicTypes(typeof(CameraManager)).Select(t => t.Namespace).Distinct().Order();
        Assert.Equal(["LibcameraSharp", "LibcameraSharp.Core"], namespaces);
    }

    [Fact]
    public void Convenience_package_is_all_in_the_main_namespace()
    {
        var elsewhere = PublicTypes(typeof(CameraDevice)).Where(t => t.Namespace != "LibcameraSharp").Select(t => t.FullName);
        Assert.Empty(elsewhere);
    }
}
