using System.Runtime.InteropServices;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Interop;
using Jalium.UI.Interop.Win32;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using static Jalium.UI.Interop.Win32.Win32Constants;
using static Jalium.UI.Interop.Win32.Win32Methods;

namespace Jalium.UI.Controls;

/// <summary>
/// Transparent, click-through, topmost native window that renders dock indicator buttons.
/// Sits above all other windows (including floating dock windows) so indicators are always visible.
/// Based on the same DirectComposition approach as <see cref="PopupWindow"/>.
/// </summary>
internal sealed partial class DockIndicatorWindow : IDisposable
{
    private nint _hwnd;
    private IPlatformWindow? _platformWindow;
    private RenderTarget? _renderTarget;
    private RenderTargetDrawingContext? _drawingContext;
    private readonly DockIndicatorVisual _visual;
    private readonly Dispatcher _dispatcher;

    private int _renderState; // Bitfield: 1=Scheduled, 2=Rendering, 4=Requested
    private const int RenderFlag_Scheduled = 1;
    private const int RenderFlag_Rendering = 2;
    private const int RenderFlag_Requested = 4;
    private bool _renderRecoveryInProgress;
    private DispatcherTimer? _renderRecoveryRetryTimer;
    private bool _disposed;
    private int _width;
    private int _height;
    private double _dpiScale = 1.0;
    private const int RenderRecoveryRetryDelayMs = 120;

    // Static window class registration
    private static WndProcDelegate? _wndProcDelegate;
    private static bool _classRegistered;
    private static readonly Dictionary<nint, DockIndicatorWindow> _windows = [];

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    internal DockIndicatorWindow(bool showCenterCross, bool showEdgeButtons)
    {
        _visual = new DockIndicatorVisual
        {
            ShowCenterCross = showCenterCross,
            ShowEdgeButtons = showEdgeButtons
        };
        _dispatcher = Dispatcher.CurrentDispatcher
            ?? throw new InvalidOperationException("DockIndicatorWindow requires a current Dispatcher.");
    }

    /// <summary>
    /// Creates and shows the indicator window at the specified screen position (physical pixels).
    /// </summary>
    internal void Show(nint parentHwnd, int screenX, int screenY, int width, int height, double dpiScale)
    {
        if (PlatformFactory.IsAndroid) return; // No dock indicators on Android (single full-screen window)
        if (_hwnd != nint.Zero || _platformWindow != null) return; // Already shown

        _width = width;
        _height = height;
        _dpiScale = dpiScale;
        _screenX = screenX;
        _screenY = screenY;

        if (PlatformFactory.IsWindows)
        {
            ShowWin32(parentHwnd, screenX, screenY, width, height);
        }
        else
        {
            ShowCrossPlatform(screenX, screenY, width, height);
        }
    }

    private void ShowWin32(nint parentHwnd, int screenX, int screenY, int width, int height)
    {
        RegisterWindowClass();

        _hwnd = CreateWindowEx(
            WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST
                | WS_EX_NOREDIRECTIONBITMAP | WS_EX_TRANSPARENT,
            IndicatorWindowClassName,
            "",
            WS_POPUP,
            screenX, screenY, width, height,
            parentHwnd,
            nint.Zero,
            GetModuleHandle(null),
            nint.Zero);

        if (_hwnd == nint.Zero) return;

        _windows[_hwnd] = this;

        try
        {
            EnsureRenderTarget();
        }
        catch (RenderPipelineException ex) when (IsRecoverableRenderPipelineException(ex))
        {
            ScheduleRenderRecoveryRetry();
        }

        _ = ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        ScheduleRender();
    }

    private void ShowCrossPlatform(int screenX, int screenY, int width, int height)
    {
        PlatformFactory.InitializePlatform();

        // POPUP | TOPMOST | TRANSPARENT style flags (matches JaliumWindowStyle enum in jalium_platform.h)
        uint style = (1u << 7) | (1u << 6) | (1u << 8); // POPUP=0x80, TOPMOST=0x40, TRANSPARENT=0x100

        _platformWindow = PlatformFactory.CreateWindow("", screenX, screenY, width, height, style, nint.Zero);
        if (_platformWindow == null) return;

        _hwnd = _platformWindow.NativeHandle;

        try
        {
            EnsureRenderTarget();
        }
        catch (RenderPipelineException ex) when (IsRecoverableRenderPipelineException(ex))
        {
            ScheduleRenderRecoveryRetry();
        }

        _platformWindow.Show();
        ScheduleRender();
    }

    /// <summary>
    /// Updates the hovered dock position and re-renders.
    /// </summary>
    internal void UpdateIndicator(DockPosition hoveredPosition)
    {
        if (_visual.HoveredPosition == hoveredPosition) return;
        _visual.HoveredPosition = hoveredPosition;
        ScheduleRender();
    }

    /// <summary>
    /// Moves the indicator window to a new screen position and/or size (physical pixels).
    /// </summary>
    private int _screenX, _screenY;

    internal void MoveTo(int screenX, int screenY, int width, int height)
    {
        if (_hwnd == nint.Zero && _platformWindow == null) return;

        bool sizeChanged = width != _width || height != _height;
        bool posChanged = screenX != _screenX || screenY != _screenY;

        if (!sizeChanged && !posChanged) return; // Nothing changed — skip expensive calls

        _screenX = screenX;
        _screenY = screenY;
        _width = width;
        _height = height;

        if (_platformWindow != null)
        {
            _platformWindow.Move(screenX, screenY);
            if (sizeChanged)
                _platformWindow.Resize(width, height);
        }
        else
        {
            _ = SetWindowPos(_hwnd, HWND_TOPMOST, screenX, screenY, width, height,
                SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        if (sizeChanged && _renderTarget != null)
            TryResizeRenderTarget(width, height, "MoveToResize");

        ScheduleRender();
    }

    /// <summary>
    /// Hides the indicator window without destroying it.
    /// </summary>
    internal void Hide()
    {
        if (_platformWindow != null)
            _platformWindow.Hide();
        else if (_hwnd != nint.Zero)
            _ = ShowWindow(_hwnd, SW_HIDE);
    }

    internal bool IsVisible => _hwnd != nint.Zero || _platformWindow != null;

    #region Rendering

    private void EnsureRenderTarget(bool forceReplaceContext = false)
    {
        if (_hwnd == nint.Zero && _platformWindow == null)
        {
            return;
        }

        if (!forceReplaceContext && _renderTarget != null && _renderTarget.IsValid)
        {
            return;
        }

        _renderTarget?.Dispose();
        _renderTarget = null;

        var context = RenderContext.GetOrCreateCurrent(RenderBackend.Auto, forceReplace: forceReplaceContext);
        if (_platformWindow != null)
        {
            var surface = _platformWindow.GetSurface();
            _renderTarget = context.CreateRenderTargetForComposition(surface, Math.Max(1, _width), Math.Max(1, _height));
        }
        else
        {
            _renderTarget = context.CreateRenderTargetForComposition(_hwnd, Math.Max(1, _width), Math.Max(1, _height));
        }

        var dpi = (float)(_dpiScale * 96.0);
        _renderTarget.SetDpi(dpi, dpi);
    }

    private void ScheduleRender()
    {
        if ((_hwnd == nint.Zero && _platformWindow == null) || _disposed) return;

        if ((Volatile.Read(ref _renderState) & RenderFlag_Rendering) != 0)
        {
            int p, n; do { p = Volatile.Read(ref _renderState); n = p | RenderFlag_Requested; } while (Interlocked.CompareExchange(ref _renderState, n, p) != p);
            return;
        }

        {
            int p, n;
            do { p = Volatile.Read(ref _renderState); if ((p & RenderFlag_Scheduled) != 0) return; n = p | RenderFlag_Scheduled; }
            while (Interlocked.CompareExchange(ref _renderState, n, p) != p);
            _dispatcher.BeginInvokeCritical(ProcessRender);
        }
    }

    private void ProcessRender()
    {
        { int p, n; do { p = Volatile.Read(ref _renderState); n = p & ~RenderFlag_Scheduled; } while (Interlocked.CompareExchange(ref _renderState, n, p) != p); }
        if ((_hwnd == nint.Zero && _platformWindow == null) || _disposed) return;
        RenderFrame();
    }

    private void RenderFrame()
    {
        if ((Volatile.Read(ref _renderState) & RenderFlag_Rendering) != 0) return;
        { int p, n; do { p = Volatile.Read(ref _renderState); n = (p | RenderFlag_Rendering) & ~RenderFlag_Requested; } while (Interlocked.CompareExchange(ref _renderState, n, p) != p); }

        try
        {
            if (_renderTarget == null || !_renderTarget.IsValid)
            {
                EnsureRenderTarget();
            }

            if (_renderTarget == null || !_renderTarget.IsValid)
            {
                return;
            }

            // Layout in DIPs
            var dipWidth = _width / _dpiScale;
            var dipHeight = _height / _dpiScale;

            var constraint = new Size(dipWidth, dipHeight);
            _visual.Measure(constraint);
            _visual.Arrange(new Rect(0, 0, dipWidth, dipHeight));

            _renderTarget.SetFullInvalidation();
            // 同 PopupWindow：合成 swapchain 背压时 BeginDraw 返回 InvalidState；跳过本帧并稍后重画，
            // 绝不能在原生 WM_PAINT / dispatcher-critical 回调里抛异常（会触发 0xC000041D 进程级崩溃）。
            if (!_renderTarget.TryBeginDraw())
            {
                ScheduleRenderAfterRecovery();
                return;
            }

            // Clear with fully transparent background
            _renderTarget.Clear(0f, 0f, 0f, 0f);

            var context = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
            _drawingContext ??= new RenderTargetDrawingContext(_renderTarget, context);
            _drawingContext.Offset = Point.Zero;
            _visual.Render(_drawingContext);

            _renderTarget.EndDraw();
            _drawingContext?.TrimCacheIfNeeded();
        }
        catch (RenderPipelineException ex)
        {
            // 运行在原生 WM_PAINT / dispatcher-critical 回调内：异常绝不能逃逸（会触发 0xC000041D 进程级崩溃）。
            // 一律走恢复 / 重排，连 Begin 阶段可恢复失败也不再重抛。
            if (TryRecoverFromRenderPipelineFailure(ex, "RenderFrame"))
            {
                return;
            }

            if (IsRecoverableRenderPipelineException(ex))
            {
                ScheduleRenderRecoveryRetry();
                return;
            }

            LogRenderFailure(ex, "RenderFrame");
        }
        catch (Exception ex)
        {
            LogRenderFailure(ex, "RenderFrame");
        }
        finally
        {
            { int p, n; do { p = Volatile.Read(ref _renderState); n = p & ~RenderFlag_Rendering; } while (Interlocked.CompareExchange(ref _renderState, n, p) != p); }
        }

        if ((Volatile.Read(ref _renderState) & RenderFlag_Requested) != 0)
        {
            { int p, n; do { p = Volatile.Read(ref _renderState); n = p & ~RenderFlag_Requested; } while (Interlocked.CompareExchange(ref _renderState, n, p) != p); }
            ScheduleRender();
        }
    }

    private void TryResizeRenderTarget(int width, int height, string stage)
    {
        var renderTarget = _renderTarget;
        if (renderTarget == null || !renderTarget.IsValid)
        {
            return;
        }

        try
        {
            // Busy is unreachable here: dock-indicator windows render inline (never
            // a render thread), so Resize takes the same-thread close path and
            // returns Ok. Discard the result — these transient overlays are
            // full-invalidated on each show, so a (hypothetical) deferred resize
            // would self-heal.
            _ = renderTarget.Resize(width, height);
        }
        catch (RenderPipelineException ex)
        {
            if (TryRecoverFromRenderPipelineFailure(ex, stage))
            {
                return;
            }

            if (IsRecoverableRenderPipelineException(ex))
            {
                ScheduleRenderRecoveryRetry();
                return;
            }

            LogRenderFailure(ex, stage);
        }
    }

    private static bool IsRecoverableRenderPipelineException(RenderPipelineException exception)
        => exception.Result is JaliumResult.DeviceLost
            or JaliumResult.InvalidState
            or JaliumResult.ResourceCreationFailed
            || (exception.Result == JaliumResult.Unknown &&
                string.Equals(exception.Stage, "Create", StringComparison.OrdinalIgnoreCase));

    private bool TryRecoverFromRenderPipelineFailure(RenderPipelineException exception, string stage)
    {
        if (!IsRecoverableRenderPipelineException(exception) ||
            (_hwnd == nint.Zero && _platformWindow == null) ||
            _disposed ||
            _renderRecoveryInProgress)
        {
            return false;
        }

        _renderRecoveryInProgress = true;
        try
        {
            // Evict + drain the indicator tree's retained layers on the OLD
            // target before it is replaced (mirrors Window's device-lost
            // recovery; stale handles would otherwise reach the new device).
            Visual.ReleaseRetainedLayersRecursive(_visual);
            _drawingContext?.DrainPendingRetainedLayers();

            _drawingContext?.ClearCache();
            _drawingContext = null;

            bool forceReplaceContext = exception.Result == JaliumResult.DeviceLost ||
                string.Equals(exception.Stage, "Create", StringComparison.OrdinalIgnoreCase);
            EnsureRenderTarget(forceReplaceContext);

            ScheduleRenderAfterRecovery();
            return true;
        }
        catch (RenderPipelineException recoveryException) when (IsRecoverableRenderPipelineException(recoveryException))
        {
            LogRenderFailure(recoveryException, $"{stage}:Recover");
            return false;
        }
        catch (Exception recoveryException)
        {
            LogRenderFailure(recoveryException, $"{stage}:Recover");
            return false;
        }
        finally
        {
            _renderRecoveryInProgress = false;
        }
    }

    private void ScheduleRenderAfterRecovery()
    {
        if ((_hwnd == nint.Zero && _platformWindow == null) || _disposed)
        {
            return;
        }

        {
            int p, n;
            do { p = Volatile.Read(ref _renderState); if ((p & RenderFlag_Scheduled) != 0) return; n = p | RenderFlag_Scheduled; }
            while (Interlocked.CompareExchange(ref _renderState, n, p) != p);
            _dispatcher.BeginInvokeCritical(ProcessRender);
        }
    }

    private void ScheduleRenderRecoveryRetry()
    {
        if ((_hwnd == nint.Zero && _platformWindow == null) || _disposed)
        {
            return;
        }

        _renderRecoveryRetryTimer ??= CreateRenderRecoveryRetryTimer();
        if (!_renderRecoveryRetryTimer.IsEnabled)
        {
            _renderRecoveryRetryTimer.Start();
        }
    }

    private DispatcherTimer CreateRenderRecoveryRetryTimer()
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(RenderRecoveryRetryDelayMs)
        };
        timer.Tick += OnRenderRecoveryRetryTimerTick;
        return timer;
    }

    private void OnRenderRecoveryRetryTimerTick(object? sender, EventArgs e)
    {
        _renderRecoveryRetryTimer?.Stop();

        if ((_hwnd == nint.Zero && _platformWindow == null) || _disposed)
        {
            return;
        }

        {
            int p, n;
            do { p = Volatile.Read(ref _renderState); if ((p & RenderFlag_Scheduled) != 0) return; n = p | RenderFlag_Scheduled; }
            while (Interlocked.CompareExchange(ref _renderState, n, p) != p);
            _dispatcher.BeginInvokeCritical(ProcessRender);
        }
    }

    private void StopRenderRecoveryRetry()
    {
        if (_renderRecoveryRetryTimer == null)
        {
            return;
        }

        _renderRecoveryRetryTimer.Stop();
        _renderRecoveryRetryTimer.Tick -= OnRenderRecoveryRetryTimerTick;
        _renderRecoveryRetryTimer = null;
    }

    private void LogRenderFailure(Exception exception, string fallbackStage)
    {
        _ = exception;
        _ = fallbackStage;
    }

    #endregion

    #region WndProc

    private static void RegisterWindowClass()
    {
        if (_classRegistered) return;

        _wndProcDelegate = IndicatorWndProc;

        WNDCLASSEX wc = new()
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = GetModuleHandle(null),
            hCursor = nint.Zero,
            hbrBackground = nint.Zero,
            lpszClassName = IndicatorWindowClassName
        };

        var atom = RegisterClassEx(ref wc);
        if (atom == 0)
            throw new InvalidOperationException("Failed to register dock indicator window class.");

        _classRegistered = true;
    }

    private static nint IndicatorWndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (_windows.TryGetValue(hWnd, out var window))
        {
            switch (msg)
            {
                case WM_DESTROY:
                    _ = _windows.Remove(hWnd);
                    return nint.Zero;

                case WM_ERASEBKGND:
                    return 1;

                case WM_PAINT:
                    window.OnPaint();
                    return nint.Zero;

                case WM_MOUSEACTIVATE:
                    return MA_NOACTIVATE;
            }
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void OnPaint()
    {
        var ps = new PAINTSTRUCT();
        _ = BeginPaint(_hwnd, out ps);
        RenderFrame();
        EndPaint(_hwnd, ref ps);
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        StopRenderRecoveryRetry();

        _drawingContext?.ClearBitmapCache();
        _drawingContext = null;

        if (_renderTarget != null)
        {
            _renderTarget.Dispose();
            _renderTarget = null;
        }

        if (_platformWindow != null)
        {
            _platformWindow.Close();
            _platformWindow = null;
            _hwnd = nint.Zero;
        }
        else if (_hwnd != nint.Zero)
        {
            _ = _windows.Remove(_hwnd);
            _ = DestroyWindow(_hwnd);
            _hwnd = nint.Zero;
        }

        GC.SuppressFinalize(this);
    }

    ~DockIndicatorWindow()
    {
        Dispose();
    }

    #endregion

    #region Win32 Interop

    private const string IndicatorWindowClassName = "JaliumDockIndicator";

    // Click-through transparency extended style — unique to the indicator window; the rest
    // of the WS_/SW_/SWP_/WM_/MA_ constants now come from Win32Constants (issue #151).
    private const uint WS_EX_TRANSPARENT = 0x00000020;

    #endregion
}
