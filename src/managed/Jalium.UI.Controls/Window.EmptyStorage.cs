using System.Runtime.InteropServices;
using Jalium.UI.Interop;

namespace Jalium.UI;

public partial class Window
{
    private const uint EmptyStorageTimerMessage = 0x0113; // WM_TIMER
    private const nuint EmptyStorageTimerId = 0x4A4D454D;
    private const uint EmptyStorageIdleMilliseconds = 250;

    private nint _emptyStorageTimerWindow;
    private RenderTarget? _emptyStorageTarget;
    private int _emptyStorageLifecycle;
    private long _emptyStorageDueTick;

    private void ScheduleEmptyStorageCompaction(RenderTarget target)
    {
        if (!OperatingSystem.IsWindows() ||
            !UsesAutomaticEmptySoftwareContext ||
            _isClosing || _managedTeardownStarted || _rtActive ||
            Handle == nint.Zero || !target.IsValid ||
            target.Backend != RenderBackend.Software)
        {
            return;
        }

        // A native HWND timer is delivered on the existing UI message loop.
        // Resetting the same timer after a present debounces hover and resize
        // without creating another managed timer or waking the thread pool.
        _emptyStorageTarget = target;
        _emptyStorageLifecycle = Volatile.Read(ref _renderLifecycleGeneration);
        _emptyStorageDueTick = Environment.TickCount64 + EmptyStorageIdleMilliseconds;
        if (SetEmptyStorageTimer(Handle, EmptyStorageTimerId,
                EmptyStorageIdleMilliseconds, nint.Zero) != 0)
        {
            _emptyStorageTimerWindow = Handle;
        }
        else
        {
            StopEmptyStorageCompaction();
        }
    }

    private void OnEmptyStorageTimer()
    {
        // KillTimer does not remove already-queued WM_TIMER messages. A stale
        // tick must neither compact a replacement target nor defeat a reset.
        if (_emptyStorageTimerWindow == nint.Zero ||
            Environment.TickCount64 < _emptyStorageDueTick)
        {
            return;
        }

        var target = _emptyStorageTarget;
        int lifecycle = _emptyStorageLifecycle;
        StopEmptyStorageCompaction();

        if (target == null || !UsesAutomaticEmptySoftwareContext ||
            _isClosing || _managedTeardownStarted || _rtActive || _isSizing ||
            !IsRenderLifecycleCurrent(lifecycle, target) ||
            target.IsDrawing || _hasPendingResize || _layoutManager.HasPendingLayout ||
            HasRenderFlag(RenderFlag_Rendering))
        {
            return;
        }

        // A scheduled frame can already have been satisfied by a synchronous
        // present. Inspect the remaining work, not just its wake-up flags: a
        // clean frame exits before BeginDraw and does not restore the buffer.
        lock (_dirtyLock)
        {
            if (_fullInvalidation || _dirtyElements.Count != 0 || _dirtyFreeRects.Count != 0)
                return;
        }

        // All pixels are preserved by the backend, which restores the dense
        // framebuffer before the next draw. This does not evict OS pages or
        // discard raster/text caches, and it does not request a collection.
        _ = target.TryCompactIdleStorage();
    }

    private void StopEmptyStorageCompaction()
    {
        nint timerWindow = _emptyStorageTimerWindow;
        _emptyStorageTimerWindow = nint.Zero;
        _emptyStorageTarget = null;
        if (timerWindow != nint.Zero && OperatingSystem.IsWindows())
        {
            _ = KillEmptyStorageTimer(timerWindow, EmptyStorageTimerId);
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "SetTimer")]
    private static partial nuint SetEmptyStorageTimer(
        nint window, nuint id, uint interval, nint callback);

    [LibraryImport("user32.dll", EntryPoint = "KillTimer")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool KillEmptyStorageTimer(nint window, nuint id);
}
