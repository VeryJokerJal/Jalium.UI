using System.Diagnostics;
using System.Runtime.InteropServices;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Lightweight native popup window for rendering Popup content outside the parent window bounds.
/// Uses WS_POPUP | WS_EX_NOACTIVATE so keyboard input stays with the parent window.
/// Implements IWindowHost/ILayoutManagerHost so child elements can find their host.
/// </summary>
internal sealed partial class PopupWindow : Decorator, IWindowHost, ILayoutManagerHost, IDisposable
{
    private nint _hwnd;
    private readonly Window _parentWindow;
    private RenderTarget? _renderTarget;
    private RenderTargetDrawingContext? _drawingContext;
    private readonly LayoutManager _layoutManager = new();
    private readonly Dispatcher _dispatcher;

    private volatile bool _renderScheduled;
    private bool _isRendering;
    private volatile bool _renderRequested;
    private bool _disposed;
    private int _width;
    private int _height;

    // Mouse tracking
    private UIElement? _lastMouseOverElement;
    private bool _isMouseTracking;
    private const uint MousePointerId = 1;
    private readonly Dictionary<uint, UIElement?> _activePointerTargets = [];
    private readonly Dictionary<uint, PointerPoint> _lastPointerPoints = [];
    private readonly Dictionary<uint, PointerStylusDevice> _activeStylusDevices = [];
    private readonly Dictionary<uint, PointerManipulationSession> _activeManipulationSessions = [];

    // Static WndProc delegate and window class
    private static WndProcDelegate? _wndProcDelegate;
    private static bool _popupClassRegistered;
    private static readonly Dictionary<nint, PopupWindow> _popupWindows = [];

    private delegate nint WndProcDelegate(nint hWnd, uint msg, nint wParam, nint lParam);

    private sealed class PointerManipulationSession
    {
        public PointerManipulationSession(UIElement target, Point origin, int timestamp)
        {
            Target = target;
            Origin = origin;
            LastPoint = origin;
            LastTimestamp = timestamp;
            CumulativeTranslation = Vector.Zero;
            LastVelocity = Vector.Zero;
        }

        public UIElement Target { get; }
        public Point Origin { get; }
        public Point LastPoint { get; set; }
        public int LastTimestamp { get; set; }
        public Vector CumulativeTranslation { get; set; }
        public Vector LastVelocity { get; set; }
    }

    LayoutManager ILayoutManagerHost.LayoutManager => _layoutManager;

    internal nint Handle => _hwnd;

    internal PopupWindow(Window parentWindow, PopupRoot popupRoot)
    {
        _parentWindow = parentWindow;
        _dispatcher = Dispatcher.CurrentDispatcher;

        // Set PopupRoot as child in the Decorator visual tree
        // This ensures PopupRoot → GetWindowHost() walks up to this PopupWindow
        Child = popupRoot;
    }

    internal void Show(int screenX, int screenY, int width, int height)
    {
        _width = width;
        _height = height;

        RegisterPopupWindowClass();

        _hwnd = CreateWindowEx(
            WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_NOREDIRECTIONBITMAP,
            PopupWindowClassName,
            "",
            WS_POPUP,
            screenX, screenY, width, height,
            _parentWindow.Handle,
            nint.Zero,
            GetModuleHandle(null),
            nint.Zero);

        if (_hwnd == nint.Zero)
            throw new InvalidOperationException("Failed to create popup window.");

        _popupWindows[_hwnd] = this;

        // Create composition render target for per-pixel alpha transparency.
        // Uses CreateSwapChainForComposition + DirectComposition (WinUI 3 / Avalonia approach).
        // WS_EX_NOREDIRECTIONBITMAP tells DWM not to allocate a redirection surface;
        // DirectComposition provides content directly to DWM compositor.
        var context = RenderContext.Current;
        if (context == null || !context.IsValid)
            context = new RenderContext(RenderBackend.D3D12);

        _renderTarget = context.CreateRenderTargetForComposition(_hwnd, width, height);

        // Set D2D DPI to match the parent window's monitor DPI
        var dpi = (float)(_parentWindow.DpiScale * 96.0);
        _renderTarget.SetDpi(dpi, dpi);

        // Show without activating
        _ = ShowWindow(_hwnd, SW_SHOWNOACTIVATE);

        // Trigger initial layout and render
        InvalidateMeasure();
        InvalidateWindow();
    }

    internal void Hide()
    {
        if (_hwnd != nint.Zero)
        {
            _ = ShowWindow(_hwnd, SW_HIDE);
        }
    }

    internal void MoveTo(int screenX, int screenY, int width, int height)
    {
        if (_hwnd == nint.Zero) return;

        bool sizeChanged = width != _width || height != _height;
        _width = width;
        _height = height;

        _ = SetWindowPos(_hwnd, HWND_TOPMOST, screenX, screenY, width, height,
            SWP_NOACTIVATE | SWP_NOOWNERZORDER);

        if (sizeChanged && _renderTarget != null)
        {
            _renderTarget.Resize(width, height);
        }

        InvalidateWindow();
    }

    /// <summary>
    /// Checks whether the given HWND belongs to a PopupWindow.
    /// Used by Window to distinguish popup activation from external app activation.
    /// </summary>
    internal static bool IsPopupWindow(nint hwnd) => _popupWindows.ContainsKey(hwnd);

    #region IWindowHost

    public void InvalidateWindow()
    {
        if (_hwnd == nint.Zero || _disposed) return;

        if (_isRendering)
        {
            _renderRequested = true;
            return;
        }

        if (!_renderScheduled)
        {
            _renderScheduled = true;
            _dispatcher.BeginInvokeCritical(ProcessRender);
        }
    }

    public void AddDirtyElement(UIElement element)
    {
        // PopupWindow always does full invalidation (small surface)
        InvalidateWindow();
    }

    public void RequestFullInvalidation()
    {
        InvalidateWindow();
    }

    public void SetNativeCapture()
    {
        if (_hwnd != nint.Zero)
            SetCapture(_hwnd);
    }

    public void ReleaseNativeCapture()
    {
        _ = ReleaseCapture();
    }

    #endregion

    #region Rendering

    private void ProcessRender()
    {
        _renderScheduled = false;
        if (_hwnd == nint.Zero || _disposed) return;
        RenderFrame();
    }

    private void RenderFrame()
    {
        if (_isRendering) return;
        _isRendering = true;
        _renderRequested = false;

        try
        {
            if (_renderTarget == null || !_renderTarget.IsValid) return;

            // Layout pass — _width/_height are physical pixels, layout uses DIPs
            var dpiScale = _parentWindow.DpiScale;
            var dipWidth = _width / dpiScale;
            var dipHeight = _height / dpiScale;

            if (Child != null)
                _layoutManager.UpdateLayout(Child, new Size(dipWidth, dipHeight));

            // Ensure PopupRoot has been measured and arranged
            if (Child != null)
            {
                var constraint = new Size(dipWidth, dipHeight);
                Child.Measure(constraint);
                Child.Arrange(new Rect(0, 0, dipWidth, dipHeight));
            }

            _renderTarget.SetFullInvalidation();
            _renderTarget.BeginDraw();

            // Clear with transparent background
            _renderTarget.Clear(0f, 0f, 0f, 0f);

            var context = RenderContext.Current;
            if (context != null && Child != null)
            {
                _drawingContext ??= new RenderTargetDrawingContext(_renderTarget, context);
                _drawingContext.Offset = Point.Zero;
                Child.Render(_drawingContext);
            }

            _renderTarget.EndDraw();
            _drawingContext?.TrimCacheIfNeeded();
        }
        catch (Exception ex)
        {
            LogRenderFailure(ex, "RenderFrame");
            throw;
        }
        finally
        {
            _isRendering = false;
        }

        if (_renderRequested)
        {
            _renderRequested = false;
            InvalidateWindow();
        }
    }

    private void LogRenderFailure(Exception exception, string fallbackStage)
    {
        string stage = fallbackStage;
        int resultCode = (int)JaliumResult.Unknown;
        if (exception is RenderPipelineException pipelineException)
        {
            stage = pipelineException.Stage;
            resultCode = pipelineException.ResultCode;
        }

        double dpi = _parentWindow.DpiScale * 96.0;
        string backend = _renderTarget?.Backend.ToString() ?? RenderContext.Current?.Backend.ToString() ?? "Unknown";

        Debug.WriteLine(
            $"RenderFailure windowType={GetType().Name} hwnd=0x{_hwnd.ToInt64():X} size={_width}x{_height} dpi={dpi:F2} backend={backend} stage={stage} resultCode={resultCode}");
    }

    #endregion

    #region WndProc and Input Routing

    private static void RegisterPopupWindowClass()
    {
        if (_popupClassRegistered) return;

        _wndProcDelegate = PopupWndProc;

        WNDCLASSEX wc = new()
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            hInstance = GetModuleHandle(null),
            hCursor = LoadCursor(nint.Zero, IDC_ARROW),
            hbrBackground = nint.Zero,
            lpszClassName = PopupWindowClassName
        };

        var atom = RegisterClassEx(ref wc);
        if (atom == 0)
            throw new InvalidOperationException("Failed to register popup window class.");

        _popupClassRegistered = true;
    }

    private static nint PopupWndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (_popupWindows.TryGetValue(hWnd, out var popupWindow))
        {
            switch (msg)
            {
                case WM_DESTROY:
                    _ = _popupWindows.Remove(hWnd);
                    return nint.Zero;

                case WM_ERASEBKGND:
                    return 1;

                case WM_PAINT:
                    popupWindow.OnPaint();
                    return nint.Zero;

                case WM_MOUSEACTIVATE:
                    return MA_NOACTIVATE;

                case Win32PointerInterop.WM_POINTERDOWN:
                case Win32PointerInterop.WM_POINTERUPDATE:
                case Win32PointerInterop.WM_POINTERUP:
                    popupWindow.OnPointerMessage(msg, wParam, lParam);
                    return nint.Zero;

                case Win32PointerInterop.WM_POINTERWHEEL:
                case Win32PointerInterop.WM_POINTERHWHEEL:
                    popupWindow.OnPointerWheel(wParam, lParam);
                    return nint.Zero;

                case Win32PointerInterop.WM_POINTERCAPTURECHANGED:
                    popupWindow.OnPointerCaptureChanged(wParam);
                    return nint.Zero;

                case WM_MOUSEMOVE:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                        return nint.Zero;
                    popupWindow.OnMouseMove(wParam, lParam);
                    return nint.Zero;

                case WM_LBUTTONDOWN:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                        return nint.Zero;
                    popupWindow.OnMouseButtonDown(MouseButton.Left, wParam, lParam);
                    return nint.Zero;

                case WM_LBUTTONUP:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                        return nint.Zero;
                    popupWindow.OnMouseButtonUp(MouseButton.Left, wParam, lParam);
                    return nint.Zero;

                case WM_RBUTTONDOWN:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                        return nint.Zero;
                    popupWindow.OnMouseButtonDown(MouseButton.Right, wParam, lParam);
                    return nint.Zero;

                case WM_RBUTTONUP:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                        return nint.Zero;
                    popupWindow.OnMouseButtonUp(MouseButton.Right, wParam, lParam);
                    return nint.Zero;

                case WM_MBUTTONDOWN:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                        return nint.Zero;
                    popupWindow.OnMouseButtonDown(MouseButton.Middle, wParam, lParam);
                    return nint.Zero;

                case WM_MBUTTONUP:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                        return nint.Zero;
                    popupWindow.OnMouseButtonUp(MouseButton.Middle, wParam, lParam);
                    return nint.Zero;

                case WM_MOUSEWHEEL:
                    if (Win32PointerInterop.IsPromotedMouseMessage())
                        return nint.Zero;
                    popupWindow.OnMouseWheel(wParam, lParam);
                    return nint.Zero;

                case WM_MOUSELEAVE:
                    popupWindow.OnMouseLeave();
                    return nint.Zero;

                case WM_CAPTURECHANGED:
                    UIElement.OnNativeCaptureChanged();
                    return nint.Zero;

                case WM_SETCURSOR:
                    if (popupWindow.OnSetCursor(lParam))
                        return 1;
                    break;
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

    private void OnMouseMove(nint wParam, nint lParam)
    {
        var position = GetMousePosition(lParam);
        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = GetModifierKeys();
        int timestamp = Environment.TickCount;

        // Track mouse leave
        if (!_isMouseTracking)
        {
            TRACKMOUSEEVENT tme = new()
            {
                cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
                dwFlags = TME_LEAVE,
                hwndTrack = _hwnd,
                dwHoverTime = 0
            };
            _ = TrackMouseEvent(ref tme);
            _isMouseTracking = true;
        }

        var captured = UIElement.MouseCapturedElement;
        UIElement? hitElement = HitTest(position)?.VisualHit as UIElement;
        var target = captured ?? hitElement ?? (UIElement)this;

        // Track mouse over state and raise MouseEnter/MouseLeave chains
        var newMouseOverElement = hitElement;
        if (newMouseOverElement != _lastMouseOverElement)
        {
            if (_lastMouseOverElement != null)
            {
                RaiseMouseLeaveChain(_lastMouseOverElement, newMouseOverElement);
            }

            if (newMouseOverElement != null)
            {
                RaiseMouseEnterChain(newMouseOverElement, _lastMouseOverElement);
            }

            _lastMouseOverElement = newMouseOverElement;
        }

        // Raise tunnel event
        MouseEventArgs tunnelArgs = new(
            PreviewMouseMoveEvent, position,
            left, middle, right,
            xButton1, xButton2, modifiers, timestamp);
        target.RaiseEvent(tunnelArgs);

        bool sourceHandled = tunnelArgs.Handled;
        bool sourceCanceled = tunnelArgs.Cancel;

        // Raise bubble event
        if (!tunnelArgs.Handled)
        {
            MouseEventArgs bubbleArgs = new(
                MouseMoveEvent, position,
                left, middle, right,
                xButton1, xButton2, modifiers, timestamp);
            target.RaiseEvent(bubbleArgs);
            sourceHandled = sourceHandled || bubbleArgs.Handled;
            sourceCanceled = sourceCanceled || bubbleArgs.Cancel;
        }

        PointerPoint pointerPoint = CreateMousePointerPoint(
            position,
            left, middle, right, xButton1, xButton2,
            modifiers,
            timestamp,
            PointerUpdateKind.Other);
        _activePointerTargets[MousePointerId] = target;
        _lastPointerPoints[MousePointerId] = pointerPoint;

        if (sourceCanceled)
        {
            RaisePointerCancelPipeline(target, pointerPoint, modifiers, timestamp);
        }
        else if (!sourceHandled)
        {
            RaisePointerMovePipeline(target, pointerPoint, modifiers, timestamp);
        }
    }

    private void OnMouseButtonDown(MouseButton button, nint wParam, nint lParam)
    {
        var position = GetMousePosition(lParam);
        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = GetModifierKeys();
        int timestamp = Environment.TickCount;

        var captured = UIElement.MouseCapturedElement;
        var target = captured ?? HitTest(position)?.VisualHit as UIElement ?? (UIElement)this;

        // Raise tunnel event
        MouseButtonEventArgs tunnelArgs = new(
            PreviewMouseDownEvent, position, button, MouseButtonState.Pressed, clickCount: 1,
            left, middle, right,
            xButton1, xButton2, modifiers, timestamp);
        target.RaiseEvent(tunnelArgs);

        bool sourceHandled = tunnelArgs.Handled;
        bool sourceCanceled = tunnelArgs.Cancel;

        // Raise bubble event
        if (!tunnelArgs.Handled)
        {
            MouseButtonEventArgs bubbleArgs = new(
                MouseDownEvent, position, button, MouseButtonState.Pressed, clickCount: 1,
                left, middle, right,
                xButton1, xButton2, modifiers, timestamp);
            target.RaiseEvent(bubbleArgs);
            sourceHandled = sourceHandled || bubbleArgs.Handled;
            sourceCanceled = sourceCanceled || bubbleArgs.Cancel;
        }

        PointerPoint pointerPoint = CreateMousePointerPoint(
            position,
            left, middle, right, xButton1, xButton2,
            modifiers,
            timestamp,
            MapMouseButtonToPointerUpdateKind(button, isPressed: true));
        _activePointerTargets[MousePointerId] = target;
        _lastPointerPoints[MousePointerId] = pointerPoint;

        if (sourceCanceled)
        {
            RaisePointerCancelPipeline(target, pointerPoint, modifiers, timestamp);
        }
        else if (!sourceHandled)
        {
            RaisePointerDownPipeline(target, pointerPoint, modifiers, timestamp);
        }
    }

    private void OnMouseButtonUp(MouseButton button, nint wParam, nint lParam)
    {
        var position = GetMousePosition(lParam);
        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = GetModifierKeys();
        int timestamp = Environment.TickCount;

        var captured = UIElement.MouseCapturedElement;
        var target = captured ?? HitTest(position)?.VisualHit as UIElement ?? (UIElement)this;

        // Raise tunnel event
        MouseButtonEventArgs tunnelArgs = new(
            PreviewMouseUpEvent, position, button, MouseButtonState.Released, clickCount: 1,
            left, middle, right,
            xButton1, xButton2, modifiers, timestamp);
        target.RaiseEvent(tunnelArgs);

        bool sourceHandled = tunnelArgs.Handled;
        bool sourceCanceled = tunnelArgs.Cancel;

        // Raise bubble event
        if (!tunnelArgs.Handled)
        {
            MouseButtonEventArgs bubbleArgs = new(
                MouseUpEvent, position, button, MouseButtonState.Released, clickCount: 1,
                left, middle, right,
                xButton1, xButton2, modifiers, timestamp);
            target.RaiseEvent(bubbleArgs);
            sourceHandled = sourceHandled || bubbleArgs.Handled;
            sourceCanceled = sourceCanceled || bubbleArgs.Cancel;
        }

        PointerPoint pointerPoint = CreateMousePointerPoint(
            position,
            left, middle, right, xButton1, xButton2,
            modifiers,
            timestamp,
            MapMouseButtonToPointerUpdateKind(button, isPressed: false));
        _lastPointerPoints[MousePointerId] = pointerPoint;

        if (sourceCanceled)
        {
            RaisePointerCancelPipeline(target, pointerPoint, modifiers, timestamp);
        }
        else if (!sourceHandled)
        {
            RaisePointerUpPipeline(target, pointerPoint, modifiers, timestamp);
        }

        _activePointerTargets.Remove(MousePointerId);
    }

    private void OnMouseWheel(nint wParam, nint lParam)
    {
        // WM_MOUSEWHEEL lParam contains SCREEN coordinates (physical pixels)
        int screenX = (short)(lParam.ToInt64() & 0xFFFF);
        int screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        POINT pt = new() { X = screenX, Y = screenY };
        _ = ScreenToClient(_hwnd, ref pt);
        var dpiScale = _parentWindow.DpiScale;
        Point position = new(pt.X / dpiScale, pt.Y / dpiScale);

        var (left, middle, right, xButton1, xButton2) = GetMouseButtonStates(wParam);
        var modifiers = GetModifierKeys();
        int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
        int timestamp = Environment.TickCount;

        var captured = UIElement.MouseCapturedElement;
        var target = captured ?? HitTest(position)?.VisualHit as UIElement ?? (UIElement)this;

        MouseWheelEventArgs tunnelArgs = new(
            PreviewMouseWheelEvent, position, delta,
            left, middle, right,
            xButton1, xButton2, modifiers, timestamp);
        target.RaiseEvent(tunnelArgs);

        bool sourceHandled = tunnelArgs.Handled;
        bool sourceCanceled = tunnelArgs.Cancel;

        if (!tunnelArgs.Handled)
        {
            MouseWheelEventArgs bubbleArgs = new(
                MouseWheelEvent, position, delta,
                left, middle, right,
                xButton1, xButton2, modifiers, timestamp);
            target.RaiseEvent(bubbleArgs);
            sourceHandled = sourceHandled || bubbleArgs.Handled;
            sourceCanceled = sourceCanceled || bubbleArgs.Cancel;
        }

        PointerPoint pointerPoint = CreateMousePointerPoint(
            position,
            left, middle, right, xButton1, xButton2,
            modifiers,
            timestamp,
            PointerUpdateKind.Other,
            mouseWheelDelta: delta);
        _lastPointerPoints[MousePointerId] = pointerPoint;

        if (sourceCanceled)
        {
            RaisePointerCancelPipeline(target, pointerPoint, modifiers, timestamp);
        }
        else if (!sourceHandled)
        {
            RaisePointerWheelPipeline(target, pointerPoint, modifiers, timestamp);
        }
    }

    private void OnPointerMessage(uint msg, nint wParam, nint lParam)
    {
        if (!Win32PointerInterop.TryGetPointerData(_hwnd, wParam, _parentWindow.DpiScale, out var pointerData))
            return;

        if (pointerData.Kind == Win32PointerKind.Mouse)
            return;

        bool isDown = msg == Win32PointerInterop.WM_POINTERDOWN;
        bool isUp = msg == Win32PointerInterop.WM_POINTERUP;
        int timestamp = Environment.TickCount;

        var captured = UIElement.MouseCapturedElement;
        var hitTarget = HitTest(pointerData.Position)?.VisualHit as UIElement;
        var fallbackTarget = captured ?? hitTarget ?? (UIElement)this;
        var target = isDown
            ? fallbackTarget
            : (_activePointerTargets.TryGetValue(pointerData.PointerId, out var existingTarget) ? existingTarget ?? fallbackTarget : fallbackTarget);

        _activePointerTargets[pointerData.PointerId] = target;
        _lastPointerPoints[pointerData.PointerId] = pointerData.Point;

        bool sourceHandled = false;
        bool sourceCanceled = pointerData.IsCanceled;

        if (pointerData.Kind == Win32PointerKind.Touch)
        {
            DispatchTouchSourcePipeline(target, pointerData, isDown, isUp, timestamp, ref sourceHandled, ref sourceCanceled);
        }
        else if (pointerData.Kind == Win32PointerKind.Pen)
        {
            DispatchStylusSourcePipeline(target, pointerData, isDown, isUp, timestamp, ref sourceHandled, ref sourceCanceled);
        }

        if (sourceCanceled)
        {
            CancelManipulationSession(pointerData.PointerId, timestamp);
            RaisePointerCancelPipeline(target, pointerData.Point, pointerData.Modifiers, timestamp);
            CleanupPointerSession(pointerData.PointerId);
            return;
        }

        DispatchManipulationPipeline(target, pointerData, isDown, isUp, sourceHandled, timestamp);

        if (!sourceHandled)
        {
            if (isDown)
            {
                RaisePointerDownPipeline(target, pointerData.Point, pointerData.Modifiers, timestamp);
            }
            else if (isUp)
            {
                RaisePointerUpPipeline(target, pointerData.Point, pointerData.Modifiers, timestamp);
            }
            else
            {
                RaisePointerMovePipeline(target, pointerData.Point, pointerData.Modifiers, timestamp);
            }
        }

        if (isUp)
        {
            CleanupPointerSession(pointerData.PointerId);
        }
    }

    private void OnPointerWheel(nint wParam, nint lParam)
    {
        if (!Win32PointerInterop.TryGetPointerData(_hwnd, wParam, _parentWindow.DpiScale, out var pointerData))
            return;

        if (pointerData.Kind == Win32PointerKind.Mouse)
            return;

        int timestamp = Environment.TickCount;
        var target = _activePointerTargets.TryGetValue(pointerData.PointerId, out var existingTarget)
            ? existingTarget ?? (UIElement)this
            : (HitTest(pointerData.Position)?.VisualHit as UIElement ?? (UIElement)this);

        if (pointerData.IsCanceled)
        {
            RaisePointerCancelPipeline(target, pointerData.Point, pointerData.Modifiers, timestamp);
            CleanupPointerSession(pointerData.PointerId);
            return;
        }

        RaisePointerWheelPipeline(target, pointerData.Point, pointerData.Modifiers, timestamp);
    }

    private void OnPointerCaptureChanged(nint wParam)
    {
        uint pointerId = Win32PointerInterop.GetPointerId(wParam);

        if (_activePointerTargets.TryGetValue(pointerId, out var target) && target != null)
        {
            if (!_lastPointerPoints.TryGetValue(pointerId, out var point))
            {
                point = new PointerPoint(
                    pointerId,
                    new Point(0, 0),
                    PointerDeviceType.Touch,
                    false,
                    new PointerPointProperties(),
                    (ulong)Environment.TickCount);
            }

            CancelManipulationSession(pointerId, Environment.TickCount);
            RaisePointerCancelPipeline(target, point, ModifierKeys.None, Environment.TickCount);
        }

        CleanupPointerSession(pointerId);
    }

    private void DispatchTouchSourcePipeline(
        UIElement target,
        Win32PointerData pointerData,
        bool isDown,
        bool isUp,
        int timestamp,
        ref bool sourceHandled,
        ref bool sourceCanceled)
    {
        TouchDevice touchDevice = isDown
            ? Touch.RegisterTouchPoint((int)pointerData.PointerId, pointerData.Position, target)
            : Touch.GetDevice((int)pointerData.PointerId) ?? Touch.RegisterTouchPoint((int)pointerData.PointerId, pointerData.Position, target);

        touchDevice.UpdatePosition(pointerData.Position);
        touchDevice.DirectlyOver = target;

        RoutedEvent previewEvent = isDown ? PreviewTouchDownEvent : (isUp ? PreviewTouchUpEvent : PreviewTouchMoveEvent);
        RoutedEvent bubbleEvent = isDown ? TouchDownEvent : (isUp ? TouchUpEvent : TouchMoveEvent);

        TouchEventArgs previewArgs = new(touchDevice, timestamp) { RoutedEvent = previewEvent };
        target.RaiseEvent(previewArgs);
        sourceHandled |= previewArgs.Handled;
        sourceCanceled |= previewArgs.Cancel;

        if (!previewArgs.Handled)
        {
            TouchEventArgs bubbleArgs = new(touchDevice, timestamp) { RoutedEvent = bubbleEvent };
            target.RaiseEvent(bubbleArgs);
            sourceHandled |= bubbleArgs.Handled;
            sourceCanceled |= bubbleArgs.Cancel;
        }

        if (isUp || sourceCanceled)
        {
            touchDevice.Deactivate();
            Touch.UnregisterTouchPoint((int)pointerData.PointerId);
        }
    }

    private void DispatchStylusSourcePipeline(
        UIElement target,
        Win32PointerData pointerData,
        bool isDown,
        bool isUp,
        int timestamp,
        ref bool sourceHandled,
        ref bool sourceCanceled)
    {
        if (!_activeStylusDevices.TryGetValue(pointerData.PointerId, out var stylusDevice))
        {
            stylusDevice = new PointerStylusDevice((int)pointerData.PointerId);
            _activeStylusDevices[pointerData.PointerId] = stylusDevice;
        }

        var properties = pointerData.Point.Properties;
        stylusDevice.UpdateState(
            pointerData.Position,
            Math.Clamp(properties.Pressure, 0f, 1f),
            inAir: !pointerData.Point.IsInContact,
            inverted: properties.IsInverted,
            inRange: true,
            barrelPressed: properties.IsBarrelButtonPressed,
            eraserPressed: properties.IsEraser,
            directlyOver: target);

        RoutedEvent previewEvent = isDown ? PreviewStylusDownEvent : (isUp ? PreviewStylusUpEvent : PreviewStylusMoveEvent);
        RoutedEvent bubbleEvent = isDown ? StylusDownEvent : (isUp ? StylusUpEvent : StylusMoveEvent);

        StylusEventArgs previewArgs = isDown
            ? new StylusDownEventArgs(stylusDevice, timestamp) { RoutedEvent = previewEvent }
            : new StylusEventArgs(stylusDevice, timestamp) { RoutedEvent = previewEvent };
        target.RaiseEvent(previewArgs);
        sourceHandled |= previewArgs.Handled;
        sourceCanceled |= previewArgs.Cancel;

        if (!previewArgs.Handled)
        {
            StylusEventArgs bubbleArgs = isDown
                ? new StylusDownEventArgs(stylusDevice, timestamp) { RoutedEvent = bubbleEvent }
                : new StylusEventArgs(stylusDevice, timestamp) { RoutedEvent = bubbleEvent };
            target.RaiseEvent(bubbleArgs);
            sourceHandled |= bubbleArgs.Handled;
            sourceCanceled |= bubbleArgs.Cancel;
        }

        if (isUp || sourceCanceled)
        {
            _activeStylusDevices.Remove(pointerData.PointerId);
        }
    }

    private void DispatchManipulationPipeline(
        UIElement target,
        Win32PointerData pointerData,
        bool isDown,
        bool isUp,
        bool sourceHandled,
        int timestamp)
    {
        PointerManipulationSession? existingSession = null;
        if (!isDown && !_activeManipulationSessions.TryGetValue(pointerData.PointerId, out existingSession))
            return;

        if (isDown)
        {
            if (sourceHandled || !target.IsManipulationEnabled)
                return;

            if (!RaiseManipulationStartingPipeline(target))
                return;

            RaiseManipulationStartedPipeline(target, pointerData.Point.Position, timestamp);
            _activeManipulationSessions[pointerData.PointerId] = new PointerManipulationSession(target, pointerData.Point.Position, timestamp);
            return;
        }

        if (existingSession == null)
            return;

        if (isUp)
        {
            RaiseManipulationInertiaStartingPipeline(existingSession, timestamp);
            RaiseManipulationCompletedPipeline(existingSession, isInertial: false, timestamp);
            _activeManipulationSessions.Remove(pointerData.PointerId);
            return;
        }

        if (sourceHandled)
            return;

        RaiseManipulationDeltaPipeline(existingSession, pointerData.Point.Position, timestamp);
    }

    private bool RaiseManipulationStartingPipeline(UIElement target)
    {
        ManipulationStartingEventArgs previewArgs = new()
        {
            RoutedEvent = PreviewManipulationStartingEvent,
            ManipulationContainer = target,
            Mode = ManipulationModes.All,
            Cancel = false
        };
        target.RaiseEvent(previewArgs);
        if (previewArgs.Cancel)
            return false;

        if (!previewArgs.Handled)
        {
            ManipulationStartingEventArgs bubbleArgs = new()
            {
                RoutedEvent = ManipulationStartingEvent,
                ManipulationContainer = previewArgs.ManipulationContainer ?? target,
                Mode = previewArgs.Mode,
                Pivot = previewArgs.Pivot,
                IsSingleTouchEnabled = previewArgs.IsSingleTouchEnabled,
                Cancel = false
            };
            target.RaiseEvent(bubbleArgs);
            if (bubbleArgs.Cancel)
                return false;
        }

        return true;
    }

    private static void RaiseManipulationStartedPipeline(UIElement target, Point origin, int timestamp)
    {
        ManipulationStartedEventArgs previewArgs = new()
        {
            RoutedEvent = PreviewManipulationStartedEvent,
            ManipulationContainer = target,
            ManipulationOrigin = origin
        };
        target.RaiseEvent(previewArgs);

        if (!previewArgs.Handled)
        {
            ManipulationStartedEventArgs bubbleArgs = new()
            {
                RoutedEvent = ManipulationStartedEvent,
                ManipulationContainer = target,
                ManipulationOrigin = origin
            };
            target.RaiseEvent(bubbleArgs);
        }
    }

    private static void RaiseManipulationDeltaPipeline(PointerManipulationSession session, Point currentPoint, int timestamp)
    {
        Vector deltaTranslation = currentPoint - session.LastPoint;
        int dt = Math.Max(1, timestamp - session.LastTimestamp);
        Vector velocity = new(deltaTranslation.X / dt, deltaTranslation.Y / dt);
        Vector cumulative = session.CumulativeTranslation + deltaTranslation;

        ManipulationDelta delta = CreateManipulationDelta(deltaTranslation);
        ManipulationDelta cumulativeDelta = CreateManipulationDelta(cumulative);
        ManipulationVelocities velocities = new()
        {
            LinearVelocity = velocity,
            AngularVelocity = 0,
            ExpansionVelocity = Vector.Zero
        };

        ManipulationDeltaEventArgs previewArgs = new()
        {
            RoutedEvent = PreviewManipulationDeltaEvent,
            ManipulationContainer = session.Target,
            ManipulationOrigin = session.Origin,
            DeltaManipulation = delta,
            CumulativeManipulation = cumulativeDelta,
            Velocities = velocities,
            IsInertial = false
        };
        session.Target.RaiseEvent(previewArgs);

        if (!previewArgs.Handled)
        {
            ManipulationDeltaEventArgs bubbleArgs = new()
            {
                RoutedEvent = ManipulationDeltaEvent,
                ManipulationContainer = session.Target,
                ManipulationOrigin = session.Origin,
                DeltaManipulation = delta,
                CumulativeManipulation = cumulativeDelta,
                Velocities = velocities,
                IsInertial = false
            };
            session.Target.RaiseEvent(bubbleArgs);
        }

        session.LastPoint = currentPoint;
        session.LastTimestamp = timestamp;
        session.CumulativeTranslation = cumulative;
        session.LastVelocity = velocity;
    }

    private static void RaiseManipulationInertiaStartingPipeline(PointerManipulationSession session, int timestamp)
    {
        if (session.LastVelocity.Length <= 0.01)
            return;

        ManipulationVelocities velocities = new()
        {
            LinearVelocity = session.LastVelocity,
            AngularVelocity = 0,
            ExpansionVelocity = Vector.Zero
        };

        ManipulationInertiaStartingEventArgs previewArgs = new()
        {
            RoutedEvent = PreviewManipulationInertiaStartingEvent,
            ManipulationContainer = session.Target,
            ManipulationOrigin = session.Origin,
            InitialVelocities = velocities
        };
        session.Target.RaiseEvent(previewArgs);

        if (!previewArgs.Handled)
        {
            ManipulationInertiaStartingEventArgs bubbleArgs = new()
            {
                RoutedEvent = ManipulationInertiaStartingEvent,
                ManipulationContainer = session.Target,
                ManipulationOrigin = session.Origin,
                InitialVelocities = velocities,
                TranslationBehavior = previewArgs.TranslationBehavior,
                RotationBehavior = previewArgs.RotationBehavior,
                ExpansionBehavior = previewArgs.ExpansionBehavior
            };
            session.Target.RaiseEvent(bubbleArgs);
        }
    }

    private static void RaiseManipulationCompletedPipeline(PointerManipulationSession session, bool isInertial, int timestamp)
    {
        ManipulationDelta total = CreateManipulationDelta(session.CumulativeTranslation);
        ManipulationVelocities velocities = new()
        {
            LinearVelocity = session.LastVelocity,
            AngularVelocity = 0,
            ExpansionVelocity = Vector.Zero
        };

        ManipulationCompletedEventArgs previewArgs = new()
        {
            RoutedEvent = PreviewManipulationCompletedEvent,
            ManipulationContainer = session.Target,
            ManipulationOrigin = session.Origin,
            TotalManipulation = total,
            FinalVelocities = velocities,
            IsInertial = isInertial
        };
        session.Target.RaiseEvent(previewArgs);

        if (!previewArgs.Handled)
        {
            ManipulationCompletedEventArgs bubbleArgs = new()
            {
                RoutedEvent = ManipulationCompletedEvent,
                ManipulationContainer = session.Target,
                ManipulationOrigin = session.Origin,
                TotalManipulation = total,
                FinalVelocities = velocities,
                IsInertial = isInertial
            };
            session.Target.RaiseEvent(bubbleArgs);
        }
    }

    private void CancelManipulationSession(uint pointerId, int timestamp)
    {
        if (!_activeManipulationSessions.TryGetValue(pointerId, out var session))
            return;

        ManipulationBoundaryFeedbackEventArgs previewBoundary = new()
        {
            RoutedEvent = PreviewManipulationBoundaryFeedbackEvent,
            ManipulationContainer = session.Target,
            BoundaryFeedback = CreateManipulationDelta(Vector.Zero)
        };
        session.Target.RaiseEvent(previewBoundary);

        if (!previewBoundary.Handled)
        {
            ManipulationBoundaryFeedbackEventArgs bubbleBoundary = new()
            {
                RoutedEvent = ManipulationBoundaryFeedbackEvent,
                ManipulationContainer = session.Target,
                BoundaryFeedback = previewBoundary.BoundaryFeedback
            };
            session.Target.RaiseEvent(bubbleBoundary);
        }

        RaiseManipulationCompletedPipeline(session, isInertial: false, timestamp);
        _activeManipulationSessions.Remove(pointerId);
    }

    private static ManipulationDelta CreateManipulationDelta(Vector translation)
    {
        return new ManipulationDelta
        {
            Translation = translation,
            Rotation = 0,
            Scale = new Vector(1, 1),
            Expansion = Vector.Zero
        };
    }

    private void RaisePointerDownPipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        PointerDownEventArgs previewArgs = new(point, modifiers, timestamp) { RoutedEvent = PreviewPointerDownEvent };
        target.RaiseEvent(previewArgs);
        if (previewArgs.Cancel)
        {
            RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            return;
        }

        bool handled = previewArgs.Handled;
        if (!previewArgs.Handled)
        {
            PointerDownEventArgs bubbleArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerDownEvent };
            target.RaiseEvent(bubbleArgs);
            handled = handled || bubbleArgs.Handled || bubbleArgs.Cancel;
            if (bubbleArgs.Cancel)
            {
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
                return;
            }
        }

        if (!handled)
        {
            PointerPressedEventArgs legacyArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerPressedEvent };
            target.RaiseEvent(legacyArgs);
            if (legacyArgs.Cancel)
            {
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            }
        }
    }

    private void RaisePointerMovePipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        PointerMoveEventArgs previewArgs = new(point, modifiers, timestamp) { RoutedEvent = PreviewPointerMoveEvent };
        target.RaiseEvent(previewArgs);
        if (previewArgs.Cancel)
        {
            RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            return;
        }

        bool handled = previewArgs.Handled;
        if (!previewArgs.Handled)
        {
            PointerMoveEventArgs bubbleArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerMoveEvent };
            target.RaiseEvent(bubbleArgs);
            handled = handled || bubbleArgs.Handled || bubbleArgs.Cancel;
            if (bubbleArgs.Cancel)
            {
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
                return;
            }
        }

        if (!handled)
        {
            PointerMovedEventArgs legacyArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerMovedEvent };
            target.RaiseEvent(legacyArgs);
            if (legacyArgs.Cancel)
            {
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            }
        }
    }

    private void RaisePointerUpPipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        PointerUpEventArgs previewArgs = new(point, modifiers, timestamp) { RoutedEvent = PreviewPointerUpEvent };
        target.RaiseEvent(previewArgs);
        if (previewArgs.Cancel)
        {
            RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            return;
        }

        bool handled = previewArgs.Handled;
        if (!previewArgs.Handled)
        {
            PointerUpEventArgs bubbleArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerUpEvent };
            target.RaiseEvent(bubbleArgs);
            handled = handled || bubbleArgs.Handled || bubbleArgs.Cancel;
            if (bubbleArgs.Cancel)
            {
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
                return;
            }
        }

        if (!handled)
        {
            PointerReleasedEventArgs legacyArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerReleasedEvent };
            target.RaiseEvent(legacyArgs);
            if (legacyArgs.Cancel)
            {
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            }
        }
    }

    private void RaisePointerCancelPipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        PointerCancelEventArgs previewArgs = new(point, modifiers, timestamp) { RoutedEvent = PreviewPointerCancelEvent };
        target.RaiseEvent(previewArgs);
        if (!previewArgs.Handled)
        {
            PointerCancelEventArgs bubbleArgs = new(point, modifiers, timestamp) { RoutedEvent = PointerCancelEvent };
            target.RaiseEvent(bubbleArgs);
        }
    }

    private static void RaisePointerWheelPipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        PointerWheelChangedEventArgs args = new(point, modifiers, timestamp) { RoutedEvent = PointerEvents.PointerWheelChangedEvent };
        target.RaiseEvent(args);
    }

    private void CleanupPointerSession(uint pointerId)
    {
        _activePointerTargets.Remove(pointerId);
        _lastPointerPoints.Remove(pointerId);
        _activeStylusDevices.Remove(pointerId);
        _activeManipulationSessions.Remove(pointerId);

        TouchDevice? touchDevice = Touch.GetDevice((int)pointerId);
        if (touchDevice != null)
        {
            touchDevice.Deactivate();
            Touch.UnregisterTouchPoint((int)pointerId);
        }
    }

    private static PointerPoint CreateMousePointerPoint(
        Point position,
        MouseButtonState left,
        MouseButtonState middle,
        MouseButtonState right,
        MouseButtonState xButton1,
        MouseButtonState xButton2,
        ModifierKeys modifiers,
        int timestamp,
        PointerUpdateKind updateKind,
        int mouseWheelDelta = 0)
    {
        PointerPointProperties properties = new()
        {
            IsLeftButtonPressed = left == MouseButtonState.Pressed,
            IsMiddleButtonPressed = middle == MouseButtonState.Pressed,
            IsRightButtonPressed = right == MouseButtonState.Pressed,
            IsXButton1Pressed = xButton1 == MouseButtonState.Pressed,
            IsXButton2Pressed = xButton2 == MouseButtonState.Pressed,
            MouseWheelDelta = mouseWheelDelta,
            PointerUpdateKind = updateKind,
            IsPrimary = true
        };

        bool isInContact = properties.IsLeftButtonPressed ||
                           properties.IsMiddleButtonPressed ||
                           properties.IsRightButtonPressed ||
                           properties.IsXButton1Pressed ||
                           properties.IsXButton2Pressed;

        return new PointerPoint(
            MousePointerId,
            position,
            PointerDeviceType.Mouse,
            isInContact,
            properties,
            (ulong)timestamp,
            0);
    }

    private static PointerUpdateKind MapMouseButtonToPointerUpdateKind(MouseButton button, bool isPressed)
    {
        return (button, isPressed) switch
        {
            (MouseButton.Left, true) => PointerUpdateKind.LeftButtonPressed,
            (MouseButton.Left, false) => PointerUpdateKind.LeftButtonReleased,
            (MouseButton.Right, true) => PointerUpdateKind.RightButtonPressed,
            (MouseButton.Right, false) => PointerUpdateKind.RightButtonReleased,
            (MouseButton.Middle, true) => PointerUpdateKind.MiddleButtonPressed,
            (MouseButton.Middle, false) => PointerUpdateKind.MiddleButtonReleased,
            (MouseButton.XButton1, true) => PointerUpdateKind.XButton1Pressed,
            (MouseButton.XButton1, false) => PointerUpdateKind.XButton1Released,
            (MouseButton.XButton2, true) => PointerUpdateKind.XButton2Pressed,
            (MouseButton.XButton2, false) => PointerUpdateKind.XButton2Released,
            _ => PointerUpdateKind.Other
        };
    }

    private void OnMouseLeave()
    {
        _isMouseTracking = false;

        if (_lastMouseOverElement != null)
        {
            RaiseMouseLeaveChain(_lastMouseOverElement, null);
            _lastMouseOverElement = null;
        }
    }

    private void RaiseMouseLeaveChain(UIElement oldElement, UIElement? newElement)
    {
        // Build ancestor set of new element
        HashSet<UIElement> newAncestors = [];
        Visual? current = newElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
                _ = newAncestors.Add(uiElement);
            current = current.VisualParent;
        }

        // Raise MouseLeave for elements no longer under the mouse
        current = oldElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
            {
                if (newAncestors.Contains(uiElement))
                    break; // Stop at common ancestor

                uiElement.SetIsMouseOver(false);
                RoutedEventArgs args = new(MouseLeaveEvent, uiElement);
                uiElement.RaiseEvent(args);
            }
            current = current.VisualParent;
        }
    }

    private void RaiseMouseEnterChain(UIElement newElement, UIElement? oldElement)
    {
        // Build ancestor set of old element
        HashSet<UIElement> oldAncestors = [];
        Visual? current = oldElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
                _ = oldAncestors.Add(uiElement);
            current = current.VisualParent;
        }

        // Collect elements that need MouseEnter (ancestor to descendant order)
        List<UIElement> enterElements = [];
        current = newElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
            {
                if (oldAncestors.Contains(uiElement))
                    break; // Stop at common ancestor

                enterElements.Add(uiElement);
            }
            current = current.VisualParent;
        }

        // Raise MouseEnter from ancestor to descendant
        for (int i = enterElements.Count - 1; i >= 0; i--)
        {
            var uiElement = enterElements[i];
            uiElement.SetIsMouseOver(true);
            RoutedEventArgs args = new(MouseEnterEvent, uiElement);
            uiElement.RaiseEvent(args);
        }
    }

    private bool OnSetCursor(nint lParam)
    {
        int hitTest = (short)(lParam.ToInt64() & 0xFFFF);
        if (hitTest != HTCLIENT) return false;

        // Get current cursor position and find element under it
        if (GetCursorPos(out var cursorPt))
        {
            POINT clientPt = new() { X = cursorPt.X, Y = cursorPt.Y };
            _ = ScreenToClient(_hwnd, ref clientPt);
            var dpiScale = _parentWindow.DpiScale;
            Point position = new(clientPt.X / dpiScale, clientPt.Y / dpiScale);

            var hitResult = HitTest(position);
            if (hitResult?.VisualHit is FrameworkElement fe)
            {
                var cursor = fe.Cursor;
                if (cursor != null)
                {
                    var cursorHandle = GetCursorHandle(cursor.CursorType);
                    if (cursorHandle != nint.Zero)
                    {
                        _ = SetCursor(cursorHandle);
                        return true;
                    }
                }
            }
        }

        // Default arrow
        _ = SetCursor(LoadCursor(nint.Zero, IDC_ARROW));
        return true;
    }

    private static nint GetCursorHandle(CursorType cursorType)
    {
        nint cursorId = cursorType switch
        {
            CursorType.Arrow => IDC_ARROW,
            CursorType.IBeam => IDC_IBEAM,
            CursorType.Wait => IDC_WAIT,
            CursorType.Cross => IDC_CROSS,
            CursorType.Hand => IDC_HAND,
            CursorType.SizeNS => IDC_SIZENS,
            CursorType.SizeWE => IDC_SIZEWE,
            CursorType.SizeNWSE => IDC_SIZENWSE,
            CursorType.SizeNESW => IDC_SIZENESW,
            CursorType.SizeAll => IDC_SIZEALL,
            CursorType.No => IDC_NO,
            CursorType.Help => IDC_HELP,
            CursorType.AppStarting => IDC_APPSTARTING,
            CursorType.UpArrow => IDC_UPARROW,
            _ => IDC_ARROW,
        };
        return LoadCursor(nint.Zero, cursorId);
    }

    #endregion

    #region Helpers

    private Point GetMousePosition(nint lParam)
    {
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        var dpiScale = _parentWindow.DpiScale;
        return new Point(x / dpiScale, y / dpiScale);
    }

    private static (MouseButtonState left, MouseButtonState middle, MouseButtonState right, MouseButtonState xButton1, MouseButtonState xButton2) GetMouseButtonStates(nint wParam)
    {
        int flags = (int)wParam.ToInt64();
        return (
            (flags & MK_LBUTTON) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released,
            (flags & MK_MBUTTON) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released,
            (flags & MK_RBUTTON) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released,
            (flags & MK_XBUTTON1) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released,
            (flags & MK_XBUTTON2) != 0 ? MouseButtonState.Pressed : MouseButtonState.Released
        );
    }

    private static ModifierKeys GetModifierKeys()
    {
        ModifierKeys modifiers = ModifierKeys.None;
        if ((GetKeyState(VK_SHIFT) & 0x8000) != 0) modifiers |= ModifierKeys.Shift;
        if ((GetKeyState(VK_CONTROL) & 0x8000) != 0) modifiers |= ModifierKeys.Control;
        if ((GetKeyState(VK_MENU) & 0x8000) != 0) modifiers |= ModifierKeys.Alt;
        return modifiers;
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Remove child before destroying window
        Child = null;
        _lastMouseOverElement = null;

        _drawingContext = null;

        if (_renderTarget != null)
        {
            _renderTarget.Dispose();
            _renderTarget = null;
        }

        if (_hwnd != nint.Zero)
        {
            _ = _popupWindows.Remove(_hwnd);
            _ = DestroyWindow(_hwnd);
            _hwnd = nint.Zero;
        }

        GC.SuppressFinalize(this);
    }

    ~PopupWindow()
    {
        Dispose();
    }

    #endregion

    #region Win32 Interop

    private const string PopupWindowClassName = "JaliumPopupWindow";

    // Window styles
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint WS_EX_NOACTIVATE = 0x08000000;
    private const uint WS_EX_TOPMOST = 0x00000008;
    private const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    // ShowWindow commands
    private const int SW_HIDE = 0;
    private const int SW_SHOWNOACTIVATE = 4;

    // SetWindowPos flags and constants
    private static readonly nint HWND_TOPMOST = -1;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;

    // Window messages
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_PAINT = 0x000F;
    private const uint WM_ERASEBKGND = 0x0014;
    private const uint WM_SETCURSOR = 0x0020;
    private const uint WM_MOUSEACTIVATE = 0x0021;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_MBUTTONDOWN = 0x0207;
    private const uint WM_MBUTTONUP = 0x0208;
    private const uint WM_MOUSEWHEEL = 0x020A;
    private const uint WM_MOUSELEAVE = 0x02A3;
    private const uint WM_CAPTURECHANGED = 0x0215;

    // WM_MOUSEACTIVATE return values
    private const nint MA_NOACTIVATE = 3;

    // Hit test
    private const int HTCLIENT = 1;

    // Mouse button state flags
    private const int MK_LBUTTON = 0x0001;
    private const int MK_RBUTTON = 0x0002;
    private const int MK_MBUTTON = 0x0010;
    private const int MK_XBUTTON1 = 0x0020;
    private const int MK_XBUTTON2 = 0x0040;

    // TrackMouseEvent
    private const uint TME_LEAVE = 0x00000002;

    // Virtual key codes
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;

    // Cursor IDs
    private const nint IDC_ARROW = 32512;
    private const nint IDC_IBEAM = 32513;
    private const nint IDC_WAIT = 32514;
    private const nint IDC_CROSS = 32515;
    private const nint IDC_UPARROW = 32516;
    private const nint IDC_SIZENWSE = 32642;
    private const nint IDC_SIZENESW = 32643;
    private const nint IDC_SIZEWE = 32644;
    private const nint IDC_SIZENS = 32645;
    private const nint IDC_SIZEALL = 32646;
    private const nint IDC_NO = 32648;
    private const nint IDC_HAND = 32649;
    private const nint IDC_APPSTARTING = 32650;
    private const nint IDC_HELP = 32651;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct TRACKMOUSEEVENT
    {
        public uint cbSize;
        public uint dwFlags;
        public nint hwndTrack;
        public uint dwHoverTime;
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

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static partial nint LoadCursor(nint hInstance, nint lpCursorName);

    [LibraryImport("user32.dll")]
    private static partial nint SetCursor(nint hCursor);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ScreenToClient(nint hWnd, ref POINT lpPoint);

    [LibraryImport("user32.dll")]
    private static partial short GetKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    private static extern nint BeginPaint(nint hWnd, out PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(nint hWnd, ref PAINTSTRUCT lpPaint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

    [DllImport("user32.dll")]
    private static extern nint SetCapture(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    #endregion
}
