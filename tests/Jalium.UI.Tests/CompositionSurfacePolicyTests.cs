using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

/// <summary>
/// Regression cover for the presentation-mode gate that decides whether a transparent
/// helper HWND (popup, dock indicator, <c>AllowsTransparency</c> window) may carry
/// <c>WS_EX_NOREDIRECTIONBITMAP</c>.
///
/// <para>The bug this locks down: the style was applied unconditionally. It tells DWM not
/// to allocate a redirection surface because DirectComposition will supply the content —
/// which only the D3D12 backend does. Vulkan (<c>vkCreateWin32SurfaceKHR</c> bound to the
/// HWND) and the software rasterizer present INTO that surface, so under those backends
/// every external popup became permanently invisible: the window existed, was visible,
/// correctly sized, hit-testable, and presented frames without error, but DWM had nothing
/// to composite. Since <c>MenuFlyout</c> always promotes to an external popup window, the
/// user-visible symptom was that no menu or drop-down could be opened at all under
/// Vulkan.</para>
/// </summary>
public class CompositionSurfacePolicyTests
{
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    [Theory]
    [InlineData(RenderBackend.D3D12, true)]
    [InlineData(RenderBackend.Metal, true)]
    [InlineData(RenderBackend.Vulkan, false)]
    [InlineData(RenderBackend.Software, false)]
    [InlineData(RenderBackend.Auto, false)]
    public void OnlyDirectCompositionBackendsMayDropTheRedirectionSurface(
        RenderBackend backend,
        bool expected)
    {
        Assert.Equal(expected, CompositionSurfacePolicy.BackendSupportsDirectComposition(backend));
    }

    [Fact]
    public void RedirectionStyle_IsAddedForCompositionBackends()
    {
        uint baseStyle = WS_EX_TOOLWINDOW | WS_EX_TOPMOST;

        uint result = CompositionSurfacePolicy.ApplyRedirectionStyle(baseStyle, useDirectComposition: true);

        Assert.Equal(WS_EX_NOREDIRECTIONBITMAP, result & WS_EX_NOREDIRECTIONBITMAP);
        Assert.Equal(baseStyle, result & baseStyle);
    }

    [Fact]
    public void RedirectionStyle_IsWithheldForRedirectionSurfaceBackends()
    {
        uint baseStyle = WS_EX_TOOLWINDOW | WS_EX_TOPMOST;

        uint result = CompositionSurfacePolicy.ApplyRedirectionStyle(baseStyle, useDirectComposition: false);

        Assert.Equal(0u, result & WS_EX_NOREDIRECTIONBITMAP);
        // Every other bit the caller asked for has to survive — a popup that loses
        // TOPMOST renders under its owner instead.
        Assert.Equal(baseStyle, result);
    }

    /// <summary>
    /// A live backend wins over the process-wide default: a popup shares its parent's
    /// <see cref="RenderContext"/>, including a backend the parent already fell back to.
    /// </summary>
    [Fact]
    public void ResolveHostBackend_PrefersTheOwnerBackend()
    {
        Assert.Equal(RenderBackend.Vulkan, CompositionSurfacePolicy.ResolveHostBackend(RenderBackend.Vulkan));
        Assert.Equal(RenderBackend.Software, CompositionSurfacePolicy.ResolveHostBackend(RenderBackend.Software));
    }

    /// <summary>
    /// The window-creation ex-style honours the same gate: a transparent window under a
    /// redirection-surface backend keeps its surface (and gets alpha from
    /// <c>EnableRedirectionSurfaceAlpha</c> instead).
    /// </summary>
    [Fact]
    public void TransparentWindow_KeepsRedirectionSurface_WhenBackendCannotComposite()
    {
        var exStyle = Window.ComputeWin32ExStyle(
            WindowTitleBarStyle.Native,
            showInTaskbar: false,
            allowsTransparency: true,
            topmost: true,
            supportsDirectComposition: false);

        Assert.Equal(0u, exStyle & WS_EX_NOREDIRECTIONBITMAP);
        Assert.Equal(WS_EX_TOPMOST, exStyle & WS_EX_TOPMOST);
        Assert.Equal(WS_EX_TOOLWINDOW, exStyle & WS_EX_TOOLWINDOW);
    }

    [Fact]
    public void TransparentWindow_DropsRedirectionSurface_WhenBackendCanComposite()
    {
        var exStyle = Window.ComputeWin32ExStyle(
            WindowTitleBarStyle.Native,
            showInTaskbar: false,
            allowsTransparency: true,
            topmost: true,
            supportsDirectComposition: true);

        Assert.Equal(WS_EX_NOREDIRECTIONBITMAP, exStyle & WS_EX_NOREDIRECTIONBITMAP);
    }

    /// <summary>
    /// An opaque window never carried the style, and the backend gate must not change
    /// that in either direction.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OpaqueWindow_NeverCarriesTheStyle(bool supportsDirectComposition)
    {
        var exStyle = Window.ComputeWin32ExStyle(
            WindowTitleBarStyle.Native,
            showInTaskbar: true,
            allowsTransparency: false,
            topmost: false,
            supportsDirectComposition);

        Assert.Equal(0u, exStyle & WS_EX_NOREDIRECTIONBITMAP);
    }
}
