using Jalium.UI.Interop;
using static Jalium.UI.Interop.Win32.Win32Methods;

namespace Jalium.UI;

public partial class Window
{
    private bool TryPresentUnchangedSoftwareResizeFrame()
    {
        // USER32 may invalidate the client after a frame-clock resize already
        // presented its new pixels. Re-present the retained CPU framebuffer for
        // that OS paint, without clearing it or replaying the unchanged tree.
        // This is limited to the automatic empty-window path: custom drawing,
        // application content and GPU swap-chain histories keep normal rendering.
        var target = RenderTarget;
        if (!_isSizing || !UsesAutomaticEmptySoftwareContext || _rtActive ||
            _hasPendingResize || _resizeInProgress || _layoutManager.HasPendingLayout ||
            target is not { IsValid: true, IsDrawing: false, Backend: RenderBackend.Software } ||
            !GetClientRect(Handle, out var client) ||
            client.right - client.left != target.Width || client.bottom - client.top != target.Height)
        {
            return false;
        }

        lock (_dirtyLock)
        {
            if (_fullInvalidation || _dirtyElements.Count != 0 || _dirtyFreeRects.Count != 0)
                return false;
        }
        lock (_renderLifecycleGate)
        {
            if (_isClosing || _managedTeardownStarted || Handle == nint.Zero ||
                !ReferenceEquals(target, RenderTarget) || HasRenderFlag(RenderFlag_Rendering))
                return false;
            SetRenderFlag(RenderFlag_Rendering);
        }

        bool ownsDraw = false;
        try
        {
            _debugHud.OnRenderFrame();
            _debugHud.OnCached();
            _debugHud.SetDirtyInfo(0, Rect.Empty);
            _debugHud.MarkLayout();
            _debugHud.MarkRender();
            target.SetFullInvalidation();
            if (!target.TryBeginDraw()) return false;
            ownsDraw = true;
            try
            {
                // The ordinary completion path records the real Present in
                // FrameHistory/diagnostics and handles native failure/recovery.
                return CompleteEndDrawOrHandleFailure();
            }
            finally { ownsDraw = false; }
        }
        catch (RenderPipelineException ex)
        {
            LogRenderFailure(ex, "Paint.CachedSoftwareResize");
            return false;
        }
        finally
        {
            if (ownsDraw)
            {
                try { _ = target.TryEndDraw(); } catch { }
            }
            lock (_renderLifecycleGate) ClearRenderFlag(RenderFlag_Rendering);
            CompletePendingManagedTeardown();
            if (HasRenderFlag(RenderFlag_Requested))
            {
                ClearRenderFlag(RenderFlag_Requested);
                InvalidateWindow();
            }
        }
    }
}
