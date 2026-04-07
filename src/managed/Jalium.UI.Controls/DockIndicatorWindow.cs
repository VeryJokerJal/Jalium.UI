using System.Runtime.InteropServices;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;

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
        _dispatcher = Dispatcher.CurrentDispatcher;
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
            _renderTarget.BeginDraw();

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
            if (string.Equals(ex.Stage, "Begin", StringComparison.OrdinalIgnoreCase))
            {
                LogRenderFailure(ex, "RenderFrame");
                throw;
            }

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
            throw;
        }
        catch (Exception ex)
        {
            LogRenderFailure(ex, "RenderFrame");
            throw;
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
            renderTarget.Resize(width, height);
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

    // Window styles
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    private const uint WS_EX_TRANSPARENT = 0x00000020;

    // ShowWindow commands
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;

    // SetWindowPos
    private static readonly nint HWND_TOPMOST = -1;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;

    // Window messages
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_PAINT = 0x000F;
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_MOUSEACTIVATE = 0x0021;

    // WM_MOUSEACTIVATE return values
    private const nint MA_NOACTIVATE = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct PAINTSTRUCT
    {
        public nint hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[]? rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public nint hIconSm;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpWndClass);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowEx(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern nint BeginPaint(nint hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(nint hWnd, ref PAINTSTRUCT lpPaint);

    #endregion
}
