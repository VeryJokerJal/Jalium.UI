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

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ResolvePreferredBackend_PrefersMetalOnEmbeddedApple(bool ios, bool tvos)
    {
        var backend = RenderBackendSelector.ResolvePreferredBackend(
            isAvailable: available => available is RenderBackend.Metal or RenderBackend.Software,
            isWindows: false,
            isMacOS: false,
            isLinux: false,
            isAndroid: false,
            isIOS: ios,
            isTvOS: tvos);

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

    [Fact]
    public void ResolveGpuPreference_DefaultsToHighPerformanceForD3D12OnWindows()
    {
        var preference = RenderBackendSelector.ResolveGpuPreference(
            RenderBackend.D3D12,
            preferenceOverride: "",
            isWindows: true);

        Assert.Equal(GpuPreference.HighPerformance, preference);
    }

    [Theory]
    [InlineData(RenderBackend.Vulkan, true)]
    [InlineData(RenderBackend.Software, true)]
    [InlineData(RenderBackend.D3D12, false)]
    public void ResolveGpuPreference_KeepsAutoOutsideWindowsD3D12(
        RenderBackend backend,
        bool isWindows)
    {
        var preference = RenderBackendSelector.ResolveGpuPreference(
            backend,
            preferenceOverride: "",
            isWindows: isWindows);

        Assert.Equal(GpuPreference.Auto, preference);
    }

    [Theory]
    [InlineData("auto", GpuPreference.Auto)]
    [InlineData("system", GpuPreference.Auto)]
    [InlineData("discrete", GpuPreference.HighPerformance)]
    [InlineData("high_performance", GpuPreference.HighPerformance)]
    [InlineData("integrated", GpuPreference.MinimumPower)]
    [InlineData("minimum_power", GpuPreference.MinimumPower)]
    public void ResolveGpuPreference_ExplicitOverrideWins(
        string value,
        GpuPreference expected)
    {
        var preference = RenderBackendSelector.ResolveGpuPreference(
            RenderBackend.D3D12,
            preferenceOverride: value,
            isWindows: true);

        Assert.Equal(expected, preference);
    }

    [Theory]
    [InlineData(GpuPreference.HighPerformance, GpuPreference.MinimumPower)]
    [InlineData(GpuPreference.MinimumPower, GpuPreference.HighPerformance)]
    public void GetFallbackGpuPreference_UsesTheOtherHardwareClass(
        GpuPreference current,
        GpuPreference expected)
    {
        Assert.Equal(
            expected,
            RenderBackendSelector.GetFallbackGpuPreference(current));
    }

    [Fact]
    public void GetFallbackGpuPreference_DoesNotOverrideExplicitAuto()
    {
        Assert.Null(
            RenderBackendSelector.GetFallbackGpuPreference(GpuPreference.Auto));
    }
}
