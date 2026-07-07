using Jalium.UI.Hosting;

namespace Jalium.UI.Tests;

/// <summary>
/// Covers <see cref="RenderQualityOptions"/> — the path anti-aliasing selection behind
/// <c>app.UsePathAntiAliasing(...)</c>. Uses fresh instances (not the
/// <see cref="RenderQualityOptions.Current"/> singleton) so tests neither pollute nor
/// depend on global state.
/// </summary>
public class RenderQualityOptionsTests
{
    [Fact]
    public void Default_Is_Msaa8x_So_Omitting_The_Call_Changes_Nothing()
    {
        var options = new RenderQualityOptions();
        Assert.Equal(PathAntiAliasing.Msaa8x, options.PathAntiAliasing);
        Assert.Equal(8u, options.ResolvePathMsaaSampleCount());
    }

    [Theory]
    [InlineData(PathAntiAliasing.Msaa16x, 16u)]
    [InlineData(PathAntiAliasing.Msaa8x, 8u)]
    [InlineData(PathAntiAliasing.Msaa4x, 4u)]
    [InlineData(PathAntiAliasing.Analytic, 0u)]
    public void ResolvePathMsaaSampleCount_Maps_Each_Mode(PathAntiAliasing mode, uint expected)
    {
        var options = new RenderQualityOptions { PathAntiAliasing = mode };
        Assert.Equal(expected, options.ResolvePathMsaaSampleCount());
    }

    [Fact]
    public void Analytic_Maps_To_The_Zero_Sentinel_That_Bypasses_The_Msaa_Stencil_Path()
    {
        // Analytic resolves to 0 — the native sentinel that routes solid fills to the
        // analytic-coverage rasterizer (WPF / Chromium 2D vector AA) instead of MSAA.
        var options = new RenderQualityOptions { PathAntiAliasing = PathAntiAliasing.Analytic };
        Assert.Equal(0u, options.ResolvePathMsaaSampleCount());
    }
}
