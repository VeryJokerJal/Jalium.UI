using System.Runtime.InteropServices;
using System.Diagnostics;
using Jalium.UI.Interop;
using static Jalium.UI.Interop.Win32.Win32Constants;
using static Jalium.UI.Interop.Win32.Win32GdiMethods;
using static Jalium.UI.Interop.Win32.Win32Methods;

namespace Jalium.UI;

public partial class Window
{
    private const string LiveResizeEdgeProperty = "Jalium.UI.LiveResizeEdge";
    private int _nativeResizeEdge;
    private bool _nativeResizeEdgeHintPublished;

    private void PublishLiveResizeEdge(int edge)
    {
        if (edge is < 1 or > 8 || Handle == 0 ||
            (_nativeResizeEdgeHintPublished && _nativeResizeEdge == edge))
            return;
        if (SetLiveResizeEdgeProperty(Handle, LiveResizeEdgeProperty, edge))
        {
            _nativeResizeEdge = edge;
            _nativeResizeEdgeHintPublished = true;
        }
    }

    private void ClearLiveResizeEdge()
    {
        if (_nativeResizeEdgeHintPublished && Handle != 0)
            _ = RemoveLiveResizeEdgeProperty(Handle, LiveResizeEdgeProperty);
        _nativeResizeEdge = 0;
        _nativeResizeEdgeHintPublished = false;
    }

    [LibraryImport("user32.dll", EntryPoint = "SetPropW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetLiveResizeEdgeProperty(nint window, string name, nint edge);

    [LibraryImport("user32.dll", EntryPoint = "RemovePropW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint RemoveLiveResizeEdgeProperty(nint window, string name);

    internal static int ComputeLiveResizePreservation(
        (int left, int top, int right, int bottom) oldClient,
        (int left, int top, int right, int bottom) newClient)
    {
        if (!IsValidRect(oldClient) || !IsValidRect(newClient))
            return WVR_REDRAW;

        // USER32's retained composition image must use the same stationary
        // edges as the renderer. Invalidating that image with WVR_REDRAW can
        // briefly expose the desktop before the new client clip is published.
        // Framework layout/render invalidation is still requested separately.
        bool fixedRight = newClient.right == oldClient.right && newClient.left != oldClient.left;
        bool fixedBottom = newClient.bottom == oldClient.bottom && newClient.top != oldClient.top;
        return (fixedRight ? WVR_ALIGNRIGHT : WVR_ALIGNLEFT) |
               (fixedBottom ? WVR_ALIGNBOTTOM : WVR_ALIGNTOP);
    }

    private void UpdateLiveResizeImagePreservation(nint windowPosition)
    {
        if (!_isSizing || windowPosition == nint.Zero ||
            RenderTarget is not { Backend: RenderBackend.D3D12, IsCompositionTarget: true })
            return;

        var position = Marshal.PtrToStructure<LiveResizeWindowPosition>(windowPosition);
        if ((position.Flags & SWP_NOSIZE) != 0)
            return;

        if (_suppressNativeCaptionForLiveResize)
        {
            // With the native caption absent, retain the existing client image
            // using NCCALCSIZE's stationary-edge alignment until the new frame.
            position.Flags &= ~SWP_NOCOPYBITS;
            Marshal.StructureToPtr(position, windowPosition, false);
            return;
        }

        // The compositor follows the stationary edge itself. Copying the old
        // HWND bitmap over it during SetWindowPos can expose a shifted native
        // caption and a wide strip of stale pixels before the next GPU frame.
        position.Flags |= SWP_NOCOPYBITS;
        Marshal.StructureToPtr(position, windowPosition, false);
    }

    private void PrepareLiveResize()
    {
        if (RenderTarget?.Backend != RenderBackend.D3D12 || Handle == 0 ||
            HasRenderFlag(RenderFlag_Rendering) || _resizeInProgress || _isClosing)
            return;

        // Composition-only windows have no GDI image to prepare. Creating a DC
        // there can publish a separate background during the first resize frame.
        if ((GetWindowLong(Handle, GWL_EXSTYLE) & WS_EX_NOREDIRECTIONBITMAP) == 0)
        {
            nint backgroundDc = GetDC(Handle);
            try { EraseBackgroundToWindowColor(backgroundDc); }
            finally { if (backgroundDc != nint.Zero) _ = ReleaseDC(Handle, backgroundDc); }
        }

        // Establish the stationary-edge copy while the HWND still has its old
        // rectangle. Relative composition then follows subsequent size deltas
        // without waiting for a new layout or a separate visual-offset commit.
        var (width, height) = GetFinalPhysicalResizeSize();
        TryResizeRenderTarget(width, height, "EnterSizeMove");
        long preparedResizeVersion = _pendingResizeVersion;
        long before = Interlocked.Read(ref _successfulPresentCount);
        var elapsed = Stopwatch.StartNew();
        do
        {
            // The worker may still own an older capture. Keep offering the
            // current layout until it can acquire the recording slot; a posted
            // capture callback cannot run while this native message is waiting.
            ForceRenderFrame();
            if (IsPrepared()) break;
            Thread.Sleep(1);
        } while (elapsed.ElapsedMilliseconds < 40);

        if (IsPrepared())
            _ = FlushResizeComposition();

        // A successful older frame or a viewport-only resize acknowledgement
        // does not prove that the stationary-edge image has been presented.
        bool IsPrepared() => _rtActive
            ? Interlocked.Read(ref _rtPresentedResizeVersion) >= preparedResizeVersion
            : Interlocked.Read(ref _successfulPresentCount) != before;
    }

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmFlush")]
    private static partial int FlushResizeComposition();

    [StructLayout(LayoutKind.Sequential)]
    private struct LiveResizeWindowPosition
    {
        public nint Window;
        public nint InsertAfter;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public uint Flags;
    }
}
