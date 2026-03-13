using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

public sealed class NativeSurfaceDescriptorTests
{
    [Fact]
    public void ForWindowsHwnd_PopulatesExpectedFields()
    {
        var hwnd = new nint(0x1234);

        var surface = NativeSurfaceDescriptor.ForWindowsHwnd(hwnd, composition: true);

        Assert.Equal(NativePlatform.Windows, surface.Platform);
        Assert.Equal(NativeSurfaceKind.CompositionTarget, surface.Kind);
        Assert.Equal(hwnd, surface.Handle0);
        Assert.True(surface.IsValid);
    }

    [Fact]
    public void RenderTargetCreation_UsesPlatformNeutralSurfacePath()
    {
        var native = new RenderTargetTestNative();
        var hwnd = new nint(0x2233);

        using var renderTarget = new RenderTarget(
            backend: RenderBackend.D3D12,
            contextHandle: new nint(0x1111),
            surface: NativeSurfaceDescriptor.ForWindowsHwnd(hwnd),
            width: 320,
            height: 240,
            useComposition: false,
            native: native);

        Assert.True(renderTarget.IsValid);
        Assert.NotNull(native.LastSurface);
        Assert.Equal(NativePlatform.Windows, native.LastSurface!.Value.Platform);
        Assert.Equal(hwnd, native.LastSurface!.Value.Handle0);
    }

    [Fact]
    public void ForLinuxX11_RequiresDisplayAndWindowHandles()
    {
        var display = new nint(0x1000);
        var window = new nint(0x2000);

        var validSurface = NativeSurfaceDescriptor.ForLinuxX11(display, window);
        var invalidSurface = NativeSurfaceDescriptor.ForLinuxX11(display, nint.Zero);

        Assert.Equal(NativePlatform.LinuxX11, validSurface.Platform);
        Assert.Equal(display, validSurface.Handle0);
        Assert.Equal(window, validSurface.Handle1);
        Assert.True(validSurface.IsValid);
        Assert.False(invalidSurface.IsValid);
    }
}
