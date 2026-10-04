using Jalium.UI.Interop;
using Jalium.UI.Interop.Win32;
using static Jalium.UI.Interop.Win32.Win32Constants;
using static Jalium.UI.Interop.Win32.Win32Methods;

namespace Jalium.UI.Controls.Platform;

/// <summary>
/// Decides how a transparent, top-level helper HWND (popup, dock indicator) gets its
/// pixels onto the desktop, and repairs the per-pixel alpha that choice costs.
/// </summary>
/// <remarks>
/// <para>
/// Two presentation paths exist on Windows:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///     <b>DirectComposition</b> — <c>CreateSwapChainForComposition</c> plus a DComp visual
///     bound to the HWND. The window must carry <c>WS_EX_NOREDIRECTIONBITMAP</c>: DWM
///     allocates no redirection surface and takes the content from the compositor
///     instead. Per-pixel alpha comes for free. <b>Only the D3D12 backend implements
///     this.</b>
///     </description>
///   </item>
///   <item>
///     <description>
///     <b>Redirection surface</b> — the backend presents into the surface DWM allocated
///     for the window. Vulkan (<c>vkCreateWin32SurfaceKHR</c> bound straight to the HWND)
///     and the software rasterizer (blit to the window DC) both land here.
///     </description>
///   </item>
/// </list>
/// <para>
/// Setting <c>WS_EX_NOREDIRECTIONBITMAP</c> for a backend on the second path is the bug
/// this type exists to prevent: the window is created, visible, correctly sized and
/// hit-testable, every frame presents without error — and nothing is ever shown, because
/// DWM has no surface to composite. It reads to the user as "the popup will not open".
/// </para>
/// <para>
/// A redirection surface is composited as opaque by default, which would turn the
/// transparent area outside a rounded menu into solid black, so
/// <see cref="EnableRedirectionSurfaceAlpha"/> puts the window on DWM's alpha-blended
/// path. The render target stays a <em>composition</em> target on both paths — that is
/// what asks the backend for a premultiplied-alpha surface (Vulkan then picks
/// <c>VK_COMPOSITE_ALPHA_PRE_MULTIPLIED_BIT_KHR</c> when the WSI offers it).
/// </para>
/// </remarks>
internal static class CompositionSurfacePolicy
{
    /// <summary>
    /// Whether <paramref name="backend"/> can present a redirection-less
    /// (<c>WS_EX_NOREDIRECTIONBITMAP</c>) window.
    /// </summary>
    internal static bool BackendSupportsDirectComposition(RenderBackend backend)
        => backend is RenderBackend.D3D12 or RenderBackend.Metal;

    /// <summary>
    /// Resolves the backend that will present a helper window, preferring the owner
    /// window's live backend (a popup shares its parent's <see cref="RenderContext"/>,
    /// including any backend the parent already fell back to).
    /// </summary>
    internal static RenderBackend ResolveHostBackend(RenderBackend ownerBackend)
    {
        if (ownerBackend != RenderBackend.Auto)
        {
            return ownerBackend;
        }

        try
        {
            return RenderContext.GetOrCreateCurrent(RenderBackend.Auto).Backend;
        }
        catch (RenderPipelineException)
        {
            // No usable context yet. Assume the DirectComposition path — it is the
            // Windows default (D3D12) and the caller re-syncs the style once the real
            // backend is known.
            return RenderBackend.D3D12;
        }
    }

    /// <summary>
    /// Applies <c>WS_EX_NOREDIRECTIONBITMAP</c> to <paramref name="baseExStyle"/> only when
    /// the backend can actually present without a redirection surface.
    /// </summary>
    internal static uint ApplyRedirectionStyle(uint baseExStyle, bool useDirectComposition)
        => useDirectComposition ? baseExStyle | WS_EX_NOREDIRECTIONBITMAP : baseExStyle;

    /// <summary>
    /// Makes DWM honour the per-pixel alpha of a window that owns a redirection surface.
    /// </summary>
    /// <remarks>
    /// Enabling blur-behind with an <em>empty</em> region ("blur nothing") is the
    /// documented way to move a plain HWND onto DWM's alpha-blended composition path
    /// without applying any blur. Call only for windows that do NOT carry
    /// <c>WS_EX_NOREDIRECTIONBITMAP</c>; the DirectComposition path already has alpha.
    /// </remarks>
    internal static void EnableRedirectionSurfaceAlpha(nint hwnd)
    {
        if (!PlatformFactory.IsWindows || hwnd == nint.Zero)
        {
            return;
        }

        // right < left ⇒ empty region.
        nint emptyRegion = CreateRectRgn(0, 0, -1, -1);
        var blurBehind = new DWM_BLURBEHIND
        {
            dwFlags = DWM_BB_ENABLE | DWM_BB_BLURREGION,
            fEnable = 1,
            hRgnBlur = emptyRegion,
            fTransitionOnMaximized = 0,
        };

        _ = DwmEnableBlurBehindWindow(hwnd, ref blurBehind);
        _ = DeleteObject(emptyRegion);
    }

    /// <summary>
    /// Adds or removes <c>WS_EX_NOREDIRECTIONBITMAP</c> on a live window so it matches the
    /// presentation mode of the backend that is about to draw into it, and restores alpha
    /// when switching to the redirection-surface path. Returns the resolved mode.
    /// </summary>
    internal static void SyncRedirectionStyle(nint hwnd, bool useDirectComposition)
    {
        if (!PlatformFactory.IsWindows || hwnd == nint.Zero)
        {
            return;
        }

        long exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        long updated = useDirectComposition
            ? exStyle | WS_EX_NOREDIRECTIONBITMAP
            : exStyle & ~(long)WS_EX_NOREDIRECTIONBITMAP;

        if (updated != exStyle)
        {
            _ = SetWindowLong(hwnd, GWL_EXSTYLE, updated);
            _ = SetWindowPos(hwnd, nint.Zero, 0, 0, 0, 0,
                SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE);
        }

        if (!useDirectComposition)
        {
            EnableRedirectionSurfaceAlpha(hwnd);
        }
    }
}
