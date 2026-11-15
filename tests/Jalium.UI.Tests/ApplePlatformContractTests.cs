using System.Reflection;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

public sealed class ApplePlatformContractTests
{
    [Fact]
    public void NativePlatform_AppleValues_AreAppendOnly()
    {
        Assert.Equal(4, (int)NativePlatform.MacOS);
        Assert.Equal(5, (int)NativePlatform.LinuxWayland);
        Assert.Equal(6, (int)NativePlatform.IOS);
        Assert.Equal(7, (int)NativePlatform.TvOS);
        Assert.Equal(8, (int)NativePlatform.VisionOS);
    }

    [Fact]
    public void AppleSurfaceFactories_PreserveViewAndCompositionKind()
    {
        var view = (nint)0x1234;
        var ios = NativeSurfaceDescriptor.ForIOSView(view, composition: true);
        var tv = NativeSurfaceDescriptor.ForTvOSView(view);
        var vision = NativeSurfaceDescriptor.ForVisionOSView(view);

        Assert.Equal(NativePlatform.IOS, ios.Platform);
        Assert.Equal(NativeSurfaceKind.CompositionTarget, ios.Kind);
        Assert.Equal(view, ios.Handle0);
        Assert.Equal(NativePlatform.TvOS, tv.Platform);
        Assert.Equal(NativePlatform.VisionOS, vision.Platform);
    }

    [Fact]
    public void HostedLifecycle_IsPublicAndSymmetric()
    {
        MethodInfo? start = typeof(JaliumApp).GetMethod(
            nameof(JaliumApp.StartHosted), [typeof(string[])]);
        MethodInfo? stop = typeof(JaliumApp).GetMethod(
            nameof(JaliumApp.StopHosted), [typeof(int)]);

        Assert.NotNull(start);
        Assert.NotNull(stop);
        Assert.Equal(typeof(void), start!.ReturnType);
        Assert.Equal(typeof(int), stop!.ReturnType);
    }

    [Fact]
    public void VisionOsEnvironmentFlag_DoesNotRenumberExistingFlags()
    {
        Assert.Equal(1 << 11, (int)SystemEnvironmentKind.VirtualMachine);
        Assert.Equal(1 << 12, (int)SystemEnvironmentKind.VisionOS);
    }
}
