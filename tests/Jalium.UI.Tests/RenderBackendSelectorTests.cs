using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

public sealed class RenderBackendSelectorTests
{
    [Fact]
    public void ResolvePreferredBackend_PrefersD3D12OnWindows()
    {
        var backend = RenderBackendSelector.ResolvePreferredBackend(
            isAvailable: available => available == RenderBackend.D3D12 || available == RenderBackend.Vulkan,
            isWindows: true,
            isMacOS: false,
            isLinux: false);

        Assert.Equal(RenderBackend.D3D12, backend);
    }

    [Fact]
    public void ResolvePreferredBackend_PrefersMetalOnMac()
    {
        var backend = RenderBackendSelector.ResolvePreferredBackend(
            isAvailable: available => available == RenderBackend.Metal || available == RenderBackend.Vulkan,
            isWindows: false,
            isMacOS: true,
            isLinux: false);

        Assert.Equal(RenderBackend.Metal, backend);
    }

    [Fact]
    public void ResolvePreferredBackend_UsesOverrideWhenAvailable()
    {
        var backend = RenderBackendSelector.ResolvePreferredBackend(
            isAvailable: available => available == RenderBackend.Vulkan,
            backendOverride: "vulkan",
            isWindows: true,
            isMacOS: false,
            isLinux: false);

        Assert.Equal(RenderBackend.Vulkan, backend);
    }

    [Fact]
    public void ResolvePreferredBackend_IgnoresUnavailableOverride()
    {
        var backend = RenderBackendSelector.ResolvePreferredBackend(
            isAvailable: available => available == RenderBackend.D3D12,
            backendOverride: "metal",
            isWindows: true,
            isMacOS: false,
            isLinux: false);

        Assert.Equal(RenderBackend.D3D12, backend);
    }

    [Fact]
    public void TryParseBackend_RecognizesAliases()
    {
        Assert.True(RenderBackendSelector.TryParseBackend("dx12", out var dx12));
        Assert.Equal(RenderBackend.D3D12, dx12);

        Assert.True(RenderBackendSelector.TryParseBackend("vk", out var vulkan));
        Assert.Equal(RenderBackend.Vulkan, vulkan);

        Assert.True(RenderBackendSelector.TryParseBackend("metal", out var metal));
        Assert.Equal(RenderBackend.Metal, metal);
    }
}
