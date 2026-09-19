using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// Unified input dispatcher that handles all input event processing for a Window.
/// Both the Win32 WndProc path and the cross-platform OnPlatformEvent path
/// translate their platform-specific data into normalized calls on this class,
/// ensuring identical behavior across all platforms.
/// </summary>
internal sealed class WindowInputDispatcher
{
    private readonly IInputDispatcherHost _host;

    // ── Mouse state ──
    private UIElement? _lastMouseOverElement;
    // Keep the ancestor snapshot captured while the element was still attached.
    // Content replacement severs VisualParent before Window gets its callback, so
    // walking the old leaf at teardown time can no longer reach the Window-side
    // ancestors whose IsMouseOver state also has to be cleared.
    private readonly List<UIElement> _mouseOverChain = [];
    private readonly List<UIElement> _mousePressedChain = [];
    private MouseButton? _suppressMouseUpButton;
    private TitleBarButton? _hoveredTitleBarButton;
    private TitleBarButton? _pressedTitleBarButton;
    private bool _isClientMouseLeaveTracking;
    private nint _clientMouseLeaveTrackingHandle;
    private int _clientMouseLeaveTrackingGeneration;

    // ── Keyboard state ──
    private readonly List<UIElement> _keyboardPressedChain = [];
    private long _suppressEscapeUntilTick;
    private const int EscapeReactivateSuppressionMs = 250;

    // ── Pointer/Touch state ──
    internal const uint MousePointerId = 1;
    private readonly Dictionary<uint, UIElement?> _activePointerTargets = [];
    private readonly Dictionary<uint, PointerPoint> _lastPointerPoints = [];
    private readonly Dictionary<uint, StylusDevice> _activeStylusDevices = [];
    private readonly Dictionary<uint, PointerManipulationSession> _activeManipulationSessions = [];
    private readonly Dictionary<uint, UIElement> _lastTouchOverDirect = [];
    private readonly Dictionary<uint, List<UIElement>> _touchOverChains = [];
    private readonly Dictionary<uint, long> _pointerSessionTokens = [];
    private readonly Dictionary<uint, (long Token, UIElement Target)> _pendingStylusTargets = [];
    private readonly List<InertialManipulationRegistration> _inertialManipulations = [];
    private long _nextPointerSessionToken;
    private uint? _primaryTouchPointerId;

    // ── Gesture (Tap/DoubleTap/Flick/TwoFingerTap) tracking ──
    // System-gesture (Tap/Drag/HoldEnter/HoldLeave) is already produced by
    // RaiseStylusExtendedEvents; this state extends recognition with:
    //   • Flick: emitted on Up when |velocity| at lift exceeds threshold.
    //   • DoubleTap: emitted on Down when two short taps land close in time + space.
    //   • TwoFingerTap: emitted on Up when a second touch lifts shortly after the first.
    private sealed class PointerGestureTracker
    {
        public long DownTimestampMs;
        public Point DownPosition;
        public Point LastPosition;
        public long LastSampleTimestampMs;
        public double LastSpeedDipsPerMs;
        public PointerDeviceType DeviceType;
    }
    private readonly Dictionary<uint, PointerGestureTracker> _gestureTrackers = [];
    private readonly Dictionary<PointerDeviceType, (long ticks, Point pos)> _lastTapByDevice = [];
    private long _firstTouchUpTicks;
    private Point _firstTouchUpPosition;

    private const double FlickVelocityThresholdDipsPerMs = 0.5;
    private const double DoubleTapDistanceThresholdDips = 16.0;
    private const long DoubleTapTimeoutMs = 500;
    private const long TwoFingerTapTimeoutMs = 300;
    private const double TwoFingerTapDistanceDips = 60.0;
    private const long ShortTapMaxDurationMs = 250;

    /// <summary>
    /// When true, mouse event handlers skip the mouse→pointer promotion step.
    /// Set by the cross-platform path when synthesizing mouse events from touch,
    /// because pointer events are already dispatched directly from the touch pipeline.
    /// </summary>
    internal bool SuppressMouseToPointerPromotion;

    public WindowInputDispatcher(IInputDispatcherHost host)
    {
        _host = host;
    }

    // ── Public state accessors ──

    internal UIElement? LastMouseOverElement => _lastMouseOverElement;
    internal TitleBarButton? HoveredTitleBarButton => _hoveredTitleBarButton;
    internal TitleBarButton? PressedTitleBarButton => _pressedTitleBarButton;
    internal TitleBarButton? PressedTitleBarButtonField { get => _pressedTitleBarButton; set => _pressedTitleBarButton = value; }
    internal MouseButton? SuppressMouseUpButton => _suppressMouseUpButton;
    internal Dictionary<uint, UIElement?> ActivePointerTargets => _activePointerTargets;
    internal Dictionary<uint, PointerPoint> LastPointerPoints => _lastPointerPoints;
    internal Dictionary<uint, StylusDevice> ActiveStylusDevices => _activeStylusDevices;

    // ══════════════════════════════════════════════════════════════
    //  Mouse Events
    // ══════════════════════════════════════════════════════════════

    /// <summary>Handles mouse move from both Win32 and cross-platform paths.</summary>
    public void HandleMouseMove(Point position, MouseButtonStates buttons, ModifierKeys modifiers, int timestamp)
    {
        Mouse.UpdateState(position, UIElement.MouseDirectlyOverElement, buttons);

        // Allow subclass to intercept
        if (_host.OnPreviewWindowMouseMove(position))
            return;

        // A popup promoted to its own top-level window cannot hold the subtree mouse
        // capture that keeps content under an in-window popup inert, so the parent
        // keeps receiving its own WM_MOUSEMOVE and everything underneath would go on
        // lighting up while the menu is open. Reproduce the WPF end state explicitly:
        // no hover, no cursor change, no move events for the window's own content until
        // the popup is dismissed. Overlay-hosted popups already block pass-through in
        // OverlayLayer.HitTestCore, and an active capture still outranks all of this.
        // 例外：指针落在覆盖层的某个弹窗里（典型是从外飞弹窗里级联出来、又落回主窗口覆盖层的
        // 子弹窗）——它是弹窗链的一部分，不是「弹窗底下的窗口内容」，悬停照常。
        if (Mouse.Captured == null
            && HasBlockingExternalPopup()
            && _host.OverlayLayer.FindPopupRootAt(position) == null)
        {
            UpdateTitleBarButtonHover(null);
            ClearContentMouseOver(timestamp);
            Mouse.UpdateState(position, null, buttons);
            ApplyPopupBlockedCursor();
            return;
        }

        // Check for title bar button hover (for custom title bar)
        if (_host.IsTitleBarVisible())
        {
            var titleBarButton = _host.GetTitleBarButtonAtPoint(position);
            UpdateTitleBarButtonHover(titleBarButton);
            EnsureClientMouseLeaveTracking();
        }

        // A Thumb owns direct capture for the whole drag. Hit testing here used to force every
        // pending virtualized layout synchronously (EnsureLayoutValidForInput) before the move
        // could reach the Thumb, multiplying one layout per physical mouse sample. Preserve the
        // last hover target while captured and route directly; the first move after release
        // refreshes hover normally.
        var capturedThumb = Mouse.Captured as Primitives.Thumb;
        UIElement? hitElement = capturedThumb != null
            ? _lastMouseOverElement ?? UIElement.MouseDirectlyOverElement
            : _host.HitTestElement(position, "mouse-move");
        if (capturedThumb == null && hitElement == _host.OverlayLayer && _host.OverlayLayer.HasLightDismissPopups)
        {
            var topLevelMenuItem = HitTopLevelMenuItemBehindOverlay(position);
            if (topLevelMenuItem != null)
            {
                hitElement = topLevelMenuItem;
            }
        }
        // InputBlocked subtree (designer host etc.): redirect hit to the blocked ancestor itself,
        // so descendant controls never receive routed input events.
        if (capturedThumb == null)
        {
            hitElement = RedirectInputBlocked(hitElement);
        }
        Mouse.UpdateState(position, hitElement, buttons);
        var target = (UIElement?)capturedThumb ?? Mouse.GetMouseTarget(hitElement) ?? _host.Self;

        // Track mouse over state and raise MouseEnter/MouseLeave events
        var newMouseOverElement = hitElement;
        if (capturedThumb == null && newMouseOverElement != _lastMouseOverElement)
        {
            if (_lastMouseOverElement != null)
                RaiseMouseLeaveChain(_lastMouseOverElement, newMouseOverElement, timestamp);
            if (newMouseOverElement != null)
                RaiseMouseEnterChain(newMouseOverElement, _lastMouseOverElement, timestamp);
            _lastMouseOverElement = newMouseOverElement;
            CaptureAncestorChain(newMouseOverElement, _mouseOverChain);
            UIElement.SetMouseDirectlyOverElement(newMouseOverElement);
        }

        // Raise tunnel event (PreviewMouseMove)
        MouseEventArgs tunnelArgs = new(
            UIElement.PreviewMouseMoveEvent, position,
            buttons.Left, buttons.Middle, buttons.Right,
            buttons.XButton1, buttons.XButton2, modifiers, timestamp);
        target.RaiseEvent(tunnelArgs);

        bool sourceHandled = tunnelArgs.Handled;
        bool sourceCanceled = tunnelArgs.Cancel;

        // Raise bubble event (MouseMove) if not handled
        if (!tunnelArgs.Handled)
        {
            MouseEventArgs bubbleArgs = new(
                UIElement.MouseMoveEvent, position,
                buttons.Left, buttons.Middle, buttons.Right,
                buttons.XButton1, buttons.XButton2, modifiers, timestamp);
            target.RaiseEvent(bubbleArgs);
            sourceHandled = sourceHandled || bubbleArgs.Handled;
            sourceCanceled = sourceCanceled || bubbleArgs.Cancel;
        }

        if (!SuppressMouseToPointerPromotion)
        {
            PointerPoint pointerPoint = CreateMousePointerPoint(
                position, buttons, modifiers, timestamp, PointerUpdateKind.Other);
            _activePointerTargets[MousePointerId] = target;
            _lastPointerPoints[MousePointerId] = pointerPoint;

            if (sourceCanceled)
                RaisePointerCancelPipeline(target, pointerPoint, modifiers, timestamp);
            else if (!sourceHandled)
                RaisePointerMovePipeline(target, pointerPoint, modifiers, timestamp);
        }

        // Update cursor based on hit element. A disabled element shows the
        // standard arrow — its Cursor (including any inherited cursor) must not
        // apply, mirroring how disabled controls drop hover/pressed visuals.
        if (Mouse.OverrideCursor is { } overrideCursor)
        {
            Mouse.SetCursor(overrideCursor);
            _host.SetPlatformCursor((int)overrideCursor.CursorType);
        }
        else
        {
            // 没有任何祖先设过 Cursor 时 ResolveEffectiveCursor 返回 null —— 这时必须**显式落回箭头**，
            // 不能什么都不做：什么都不做的话，上一个元素设过的光标会一直留在屏幕上（鼠标从一个
            // Cursor="Hand" 的按钮移到没设光标的空白区，光标仍是手型，直到碰到下一个设了光标的元素）。
            // WPF 的契约是「无人指定即箭头」，框架里另外两条路径（Window.ResolveCursor 的注释、
            // Mouse.UpdateCursor 的 `cursor ?? Cursors.Arrow`）也都是这么兜的，唯独这里漏了。
            var target2 = capturedThumb ?? hitElement;
            var cursor = target2 is FrameworkElement fe && fe.IsEnabled
                ? FrameworkElement.ResolveEffectiveCursor(fe) ?? Jalium.UI.Input.Cursors.Arrow
                : Jalium.UI.Input.Cursors.Arrow;
            Mouse.SetCursor(cursor);
            _host.SetPlatformCursor((int)cursor.CursorType);
        }
    }

    /// <summary>
    /// True when a light-dismiss popup is hosted in its own top-level window. Such a
    /// popup cannot capture the parent window's pointer input, so the parent has to
    /// stop reacting to it on its own.
    /// </summary>
    private bool HasBlockingExternalPopup()
    {
        var popups = _host.ActiveExternalPopups;
        for (int i = 0; i < popups.Count; i++)
        {
            if (!popups[i].StaysOpen)
                return true;
        }

        return false;
    }

    /// <summary>Drops the window's hover chain without attributing it to a new element.</summary>
    private void ClearContentMouseOver(int timestamp)
    {
        if (_lastMouseOverElement == null)
            return;

        RaiseMouseLeaveChain(_lastMouseOverElement, null, timestamp);
        _lastMouseOverElement = null;
        _mouseOverChain.Clear();
        UIElement.SetMouseDirectlyOverElement(null);
    }

    private void ApplyPopupBlockedCursor()
    {
        var cursor = Mouse.OverrideCursor ?? Jalium.UI.Input.Cursors.Arrow;
        Mouse.SetCursor(cursor);
        _host.SetPlatformCursor((int)cursor.CursorType);
    }

    /// <summary>Handles mouse button down.</summary>
    public void HandleMouseDown(MouseButton button, Point position, MouseButtonStates buttons,
        ModifierKeys modifiers, int clickCount, int timestamp)
    {
        Mouse.UpdateState(position, UIElement.MouseDirectlyOverElement, buttons);

        // Allow subclass to intercept
        if (_host.OnPreviewWindowMouseDown(button, position, clickCount))
            return;

        var topLevelMenuItemBehindOverlay = _host.OverlayLayer.HasLightDismissPopups
            ? HitTopLevelMenuItemBehindOverlay(position)
            : null;

        // 按下点落在覆盖层的哪个弹窗里（没有则 null）。下面两段 light dismiss 都要用它：
        // 落在弹窗里的按下不是「点到外面」，既不关它的祖先、也不吞掉点击。
        var hitOverlayRoot = _host.OverlayLayer.FindPopupRootAt(position);

        // Check light dismiss via OverlayLayer — clicks outside popups close them
        bool dismissed = topLevelMenuItemBehindOverlay == null
            && _host.OverlayLayer.TryHandleLightDismiss(position);

        // Light dismiss for external popup windows (rendered outside the parent window).
        // 与覆盖层那一段同一次按下里一起判：点在所有弹窗之外时两种宿主形态一起关（和非客户区按下、
        // 窗口失活走的 CloseAllLightDismissPopups 一致），不再是先关覆盖层、外飞的留到下一次点击。
        if (_host.ActiveExternalPopups.Count > 0)
        {
            // 链感知：按下落在覆盖层的某个弹窗里时（典型是外飞菜单里级联出来、又落回主窗口覆盖层的
            // 子菜单/下拉），它的祖先外飞弹窗不算「被点到外面」——保持打开，点击照常派发给弹窗内容；
            // 只有与按下点无关的外飞弹窗才关。否则父菜单被关、点击被吞、子菜单却留着还能继续点。
            var popupsToClose = _host.ActiveExternalPopups
                .Where(p => !p.StaysOpen
                            && !p.HasPointerCaptureWithin
                            && (hitOverlayRoot == null || !p.IsAncestorOfPopupRoot(hitOverlayRoot)))
                .ToList();
            foreach (var popup in popupsToClose)
                popup.IsOpen = false;
            if (popupsToClose.Count > 0 && hitOverlayRoot == null)
                dismissed = true;
        }

        if (dismissed)
        {
            _suppressMouseUpButton = button;
            return;
        }

        // Handle title bar button press (for custom title bar)
        if (_host.IsTitleBarVisible() && button == MouseButton.Left)
        {
            var titleBarButton = _host.GetTitleBarButtonAtPoint(position);
            if (titleBarButton != null)
            {
                ClearMousePressedChain();
                _pressedTitleBarButton = titleBarButton;
                titleBarButton.SetIsPressed(true);
                return;
            }
        }

        // If an element has captured the mouse, it receives all mouse events
        // Otherwise, find the target element via hit testing
        var hitElement = topLevelMenuItemBehindOverlay ?? _host.HitTestElement(position, "mouse-down");
        hitElement = RedirectInputBlocked(hitElement);
        UpdateMouseOverState(hitElement, timestamp);
        Mouse.UpdateState(position, hitElement, buttons);
        Mouse.RaiseOutsideCapturedElementEvent(
            true,
            hitElement,
            position,
            button,
            MouseButtonState.Pressed,
            clickCount,
            buttons,
            modifiers,
            timestamp);
        var target = Mouse.GetMouseTarget(hitElement) ?? _host.Self;

        if (button == MouseButton.Left)
        {
            ActivateMousePressedChain(target);

            // Activate the DockTabPanel that contains the click target
            UIElement? walk = target;
            while (walk != null)
            {
                if (walk is DockTabPanel dockPanel)
                {
                    DockManager.SetActivePanel(dockPanel);
                    break;
                }
                walk = walk.VisualParent as UIElement;
            }
        }

        var currentState = MouseButtonState.Pressed;

        // Raise tunnel event (PreviewMouseDown)
        MouseButtonEventArgs tunnelArgs = new(
            UIElement.PreviewMouseDownEvent, position, button, currentState, clickCount,
            buttons.Left, buttons.Middle, buttons.Right,
            buttons.XButton1, buttons.XButton2, modifiers, timestamp);
        target.RaiseEvent(tunnelArgs);

        bool sourceHandled = tunnelArgs.Handled;
        bool sourceCanceled = tunnelArgs.Cancel;

        // Raise bubble event (MouseDown) if not handled
        if (!tunnelArgs.Handled)
        {
            MouseButtonEventArgs bubbleArgs = new(
                UIElement.MouseDownEvent, position, button, currentState, clickCount,
                buttons.Left, buttons.Middle, buttons.Right,
                buttons.XButton1, buttons.XButton2, modifiers, timestamp);
            target.RaiseEvent(bubbleArgs);
            sourceHandled = sourceHandled || bubbleArgs.Handled;
            sourceCanceled = sourceCanceled || bubbleArgs.Cancel;
        }

        if (!SuppressMouseToPointerPromotion)
        {
            PointerPoint pointerPoint = CreateMousePointerPoint(
                position, buttons, modifiers, timestamp,
                MapMouseButtonToPointerUpdateKind(button, isPressed: true));
            _activePointerTargets[MousePointerId] = target;
            _lastPointerPoints[MousePointerId] = pointerPoint;

            if (sourceCanceled)
                RaisePointerCancelPipeline(target, pointerPoint, modifiers, timestamp);
            else if (!sourceHandled)
                RaisePointerDownPipeline(target, pointerPoint, modifiers, timestamp);
        }
    }

    /// <summary>Handles mouse button up.</summary>
    public void HandleMouseUp(MouseButton button, Point position, MouseButtonStates buttons,
        ModifierKeys modifiers, int timestamp)
    {
        Mouse.UpdateState(position, UIElement.MouseDirectlyOverElement, buttons);

        if (_suppressMouseUpButton == button)
        {
            _suppressMouseUpButton = null;
            if (button == MouseButton.Left)
                ClearMousePressedChain();
            return;
        }

        // Allow subclass to intercept
        if (_host.OnPreviewWindowMouseUp(button, position))
            return;

        // Handle title bar button release (for custom title bar)
        if (_host.IsTitleBarVisible() && button == MouseButton.Left && _pressedTitleBarButton != null)
        {
            var titleBarButton = _host.GetTitleBarButtonAtPoint(position);
            _pressedTitleBarButton.SetIsPressed(false);

            if (titleBarButton == _pressedTitleBarButton)
            {
                switch (_pressedTitleBarButton.Kind)
                {
                    case TitleBarButtonKind.Minimize:
                        _host.TitleBar?.RaiseMinimizeClicked();
                        break;
                    case TitleBarButtonKind.Maximize:
                    case TitleBarButtonKind.Restore:
                        _host.TitleBar?.RaiseMaximizeRestoreClicked();
                        break;
                    case TitleBarButtonKind.Close:
                        _host.TitleBar?.RaiseCloseClicked();
                        break;
                }
            }

            _pressedTitleBarButton = null;
            ClearMousePressedChain();
            return;
        }

        // Release must reach a captured Thumb without first forcing an outstanding layout. That
        // guarantees capture is dropped promptly even when the dragged viewport still has work
        // queued, so the next click cannot appear to be ignored.
        var capturedThumb = Mouse.Captured as Primitives.Thumb;
        var hitElement = capturedThumb != null
            ? _lastMouseOverElement ?? UIElement.MouseDirectlyOverElement
            : RedirectInputBlocked(_host.HitTestElement(position, "mouse-up"));
        if (capturedThumb == null)
        {
            UpdateMouseOverState(hitElement, timestamp);
        }
        Mouse.UpdateState(position, hitElement, buttons);
        if (capturedThumb == null)
        {
            Mouse.RaiseOutsideCapturedElementEvent(
                false,
                hitElement,
                position,
                button,
                MouseButtonState.Released,
                1,
                buttons,
                modifiers,
                timestamp);
        }
        var target = (UIElement?)capturedThumb ?? Mouse.GetMouseTarget(hitElement) ?? _host.Self;

        var currentState = MouseButtonState.Released;

        MouseButtonEventArgs tunnelArgs = new(
            UIElement.PreviewMouseUpEvent, position, button, currentState, clickCount: 1,
            buttons.Left, buttons.Middle, buttons.Right,
            buttons.XButton1, buttons.XButton2, modifiers, timestamp);
        target.RaiseEvent(tunnelArgs);

        bool sourceHandled = tunnelArgs.Handled;
        bool sourceCanceled = tunnelArgs.Cancel;

        if (!tunnelArgs.Handled)
        {
            MouseButtonEventArgs bubbleArgs = new(
                UIElement.MouseUpEvent, position, button, currentState, clickCount: 1,
                buttons.Left, buttons.Middle, buttons.Right,
                buttons.XButton1, buttons.XButton2, modifiers, timestamp);
            target.RaiseEvent(bubbleArgs);
            sourceHandled = sourceHandled || bubbleArgs.Handled;
            sourceCanceled = sourceCanceled || bubbleArgs.Cancel;
        }

        if (!SuppressMouseToPointerPromotion)
        {
            PointerPoint pointerPoint = CreateMousePointerPoint(
                position, buttons, modifiers, timestamp,
                MapMouseButtonToPointerUpdateKind(button, isPressed: false));
            _lastPointerPoints[MousePointerId] = pointerPoint;

            if (sourceCanceled)
                RaisePointerCancelPipeline(target, pointerPoint, modifiers, timestamp);
            else if (!sourceHandled)
                RaisePointerUpPipeline(target, pointerPoint, modifiers, timestamp);

            _activePointerTargets.Remove(MousePointerId);
        }

        if (button == MouseButton.Left)
        {
            ClearMousePressedChain();

            // 不变式：左键抬起后若没有任何托管元素持有捕获，窗口就不该还扣着 Win32 捕获。
            // 托管捕获可能被 WM_CAPTURECHANGED 清成 null（那条路径按契约不碰原生捕获），
            // 此后 ReleaseMouseCapture 的 `_mouseCaptured == this` 判断也不再成立，
            // 原生捕获就成了没人认领的孤儿 —— 系统随即停发 WM_SETCURSOR、也不再做标题栏命中，
            // 用户看到的是「光标卡在上一个形状」和「窗口拖不动」。
            if (Mouse.Captured == null)
                _host.Self.ReleaseNativeCaptureIfOwned();
        }
    }

    /// <summary>Handles mouse wheel.</summary>
    public void HandleMouseWheel(Point position, int delta, MouseButtonStates buttons,
        ModifierKeys modifiers, int timestamp)
    {
        Mouse.UpdateState(position, UIElement.MouseDirectlyOverElement, buttons);

        // Allow subclass to intercept
        if (_host.OnPreviewWindowMouseWheel(delta, position))
            return;

        var hitElement = RedirectInputBlocked(_host.HitTestElement(position, "mouse-wheel"));
        Mouse.UpdateState(position, hitElement, buttons);
        var target = Mouse.GetMouseTarget(hitElement) ?? _host.Self;

        // Raise tunnel event (PreviewMouseWheel)
        MouseWheelEventArgs tunnelArgs = new(
            UIElement.PreviewMouseWheelEvent, position, delta,
            buttons.Left, buttons.Middle, buttons.Right,
            buttons.XButton1, buttons.XButton2, modifiers, timestamp);
        target.RaiseEvent(tunnelArgs);

        bool sourceHandled = tunnelArgs.Handled;
        bool sourceCanceled = tunnelArgs.Cancel;

        if (!tunnelArgs.Handled)
        {
            MouseWheelEventArgs bubbleArgs = new(
                UIElement.MouseWheelEvent, position, delta,
                buttons.Left, buttons.Middle, buttons.Right,
                buttons.XButton1, buttons.XButton2, modifiers, timestamp);
            target.RaiseEvent(bubbleArgs);
            sourceHandled = sourceHandled || bubbleArgs.Handled;
            sourceCanceled = sourceCanceled || bubbleArgs.Cancel;
        }

        if (!SuppressMouseToPointerPromotion)
        {
            PointerPoint pointerPoint = CreateMousePointerPoint(
                position, buttons, modifiers, timestamp,
                PointerUpdateKind.Other, mouseWheelDelta: delta);
            _lastPointerPoints[MousePointerId] = pointerPoint;

            if (sourceCanceled)
                RaisePointerCancelPipeline(target, pointerPoint, modifiers, timestamp);
            else if (!sourceHandled)
                RaisePointerWheelPipeline(target, pointerPoint, modifiers, timestamp);
        }
    }

    /// <summary>Handles mouse leaving the window.</summary>
    public void HandleMouseLeave()
    {
        ResetClientMouseLeaveTracking();
        Mouse.OnMouseLeaveWindow();

        if (_host.TitleBarStyle == WindowTitleBarStyle.Custom)
            ClearTitleBarInteractionState();

        ClearMousePressedChain();

        if (_lastMouseOverElement != null)
        {
            RaiseMouseLeaveChain(_lastMouseOverElement, null, Environment.TickCount);
            _lastMouseOverElement = null;
            _mouseOverChain.Clear();
            UIElement.SetMouseDirectlyOverElement(null);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Keyboard Events
    // ══════════════════════════════════════════════════════════════

    /// <summary>Handles key down. Returns true if handled.</summary>
    public bool HandleKeyDown(Key key, ModifierKeys modifiers, bool isRepeat, int timestamp)
    {
        if (ShouldSuppressReactivatedEscape(key, isKeyDown: true))
            return true;

        // Allow subclass to intercept before any processing
        if (_host.OnPreviewWindowKeyDown(key, modifiers, isRepeat))
            return true;

        // F3 toggles debug HUD — silently ignored unless the app opted in via
        // app.UseDebugHud(). Treat as unhandled so other handlers can claim F3.
        if (key == Key.F3 && !isRepeat && _host.CanToggleDebugHud)
        {
            _host.DebugHudEnabled = !_host.DebugHudEnabled;
            _host.DebugHudOverlayVisibility = _host.DebugHudEnabled ? Visibility.Visible : Visibility.Collapsed;
            _host.RequestFullInvalidation();
            _host.InvalidateWindow();
            return true;
        }

        // F12 opens DevTools
        if (key == Key.F12 && !isRepeat && _host.CanOpenDevTools)
        {
            _host.ToggleDevTools();
            return true;
        }

        // Ctrl+Shift+C activates element picker
        if (key == Key.C && !isRepeat && _host.CanOpenDevTools &&
            (modifiers & ModifierKeys.Control) != 0 && (modifiers & ModifierKeys.Shift) != 0)
        {
            _host.OpenDevTools();
            _host.ActivateDevToolsPicker();
            return true;
        }

        var target = _host.GetKeyboardEventTarget();

        if (!isRepeat && (key == Key.Space || key == Key.Enter))
            ActivateKeyboardPressedChain(target);

        // Raise tunnel event (PreviewKeyDown)
        KeyEventArgs tunnelArgs = new(UIElement.PreviewKeyDownEvent, key, modifiers, isDown: true, isRepeat, timestamp);
        target.RaiseEvent(tunnelArgs);

        // Raise bubble event (KeyDown) if not handled
        if (!tunnelArgs.Handled)
        {
            KeyEventArgs bubbleArgs = new(UIElement.KeyDownEvent, key, modifiers, isDown: true, isRepeat, timestamp);
            target.RaiseEvent(bubbleArgs);

            // Auto Tab/Shift+Tab focus navigation
            if (!bubbleArgs.Handled && key == Key.Tab)
            {
                var reverse = (modifiers & ModifierKeys.Shift) != 0;
                if (target is UIElement targetElement)
                {
                    KeyboardNavigation.MoveFocus(targetElement, reverse);
                    bubbleArgs.Handled = true;
                }
            }

            // Auto arrow-key focus navigation
            if (!bubbleArgs.Handled &&
                modifiers == ModifierKeys.None &&
                target is UIElement directionalTarget)
            {
                var direction = key switch
                {
                    Key.Left => FocusNavigationDirection.Left,
                    Key.Right => FocusNavigationDirection.Right,
                    Key.Up => FocusNavigationDirection.Up,
                    Key.Down => FocusNavigationDirection.Down,
                    _ => (FocusNavigationDirection?)null
                };

                if (direction.HasValue && KeyboardNavigation.MoveFocus(directionalTarget, direction.Value))
                    bubbleArgs.Handled = true;
            }

            // IsDefault (Enter) / IsCancel (Escape) button handling
            if (!bubbleArgs.Handled && !isRepeat)
            {
                if (key == Key.Enter)
                {
                    var buttonSearchRoot = (UIElement?)_host.ActiveContentDialog ?? (UIElement?)_host.FindContainingInPlaceDialog() ?? _host.Self;
                    var defaultButton = _host.FindButton(buttonSearchRoot, b => b.IsDefault);
                    if (defaultButton != null)
                    {
                        defaultButton.PerformClick();
                        bubbleArgs.Handled = true;
                    }
                }
                else if (key == Key.Escape)
                {
                    var buttonSearchRoot = (UIElement?)_host.ActiveContentDialog ?? (UIElement?)_host.FindContainingInPlaceDialog() ?? _host.Self;
                    var cancelButton = _host.FindButton(buttonSearchRoot, b => b.IsCancel);
                    if (cancelButton != null)
                    {
                        cancelButton.PerformClick();
                        bubbleArgs.Handled = true;
                    }
                }
            }

            return bubbleArgs.Handled;
        }

        return true;
    }

    /// <summary>Handles key up. Returns true if handled.</summary>
    public bool HandleKeyUp(Key key, ModifierKeys modifiers, int timestamp)
    {
        if (ShouldSuppressReactivatedEscape(key, isKeyDown: false))
            return true;

        if (_host.OnPreviewWindowKeyUp(key, modifiers))
            return true;

        var target = Keyboard.FocusedElement as UIElement ?? _host.Self;

        KeyEventArgs tunnelArgs = new(UIElement.PreviewKeyUpEvent, key, modifiers, isDown: false, isRepeat: false, timestamp);
        target.RaiseEvent(tunnelArgs);
        bool handled = tunnelArgs.Handled;

        if (!handled)
        {
            KeyEventArgs bubbleArgs = new(UIElement.KeyUpEvent, key, modifiers, isDown: false, isRepeat: false, timestamp);
            target.RaiseEvent(bubbleArgs);
            handled = bubbleArgs.Handled;
        }

        if (key == Key.Space || key == Key.Enter)
            ClearKeyboardPressedChain();

        return handled;
    }

    /// <summary>Handles character input (WM_CHAR or PlatformEvent.CharInput).</summary>
    public void HandleCharInput(string text, int timestamp)
    {
        var target = _host.GetTextInputTarget();
        if (target == null)
            return;

        TextCompositionEventArgs tunnelArgs = new(UIElement.PreviewTextInputEvent, text, timestamp);
        target.RaiseEvent(tunnelArgs);

        if (!tunnelArgs.Handled)
        {
            TextCompositionEventArgs bubbleArgs = new(UIElement.TextInputEvent, text, timestamp);
            target.RaiseEvent(bubbleArgs);
        }
    }

    private bool ShouldSuppressReactivatedEscape(Key key, bool isKeyDown)
    {
        if (key != Key.Escape)
            return false;

        if (_suppressEscapeUntilTick == 0)
            return false;

        if (Environment.TickCount64 > _suppressEscapeUntilTick)
        {
            _suppressEscapeUntilTick = 0;
            return false;
        }

        if (!isKeyDown)
            _suppressEscapeUntilTick = 0;

        return true;
    }

    // ══════════════════════════════════════════════════════════════
    //  Window Lifecycle (affecting input state)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// WM_CAPTURECHANGED：<paramref name="newCaptureWindow"/> 是**即将拿到**捕获的那个窗口。
    ///
    /// <para>
    /// 它等于本窗口时说明捕获压根没易主 —— 同一个窗口内换捕获元素（按钮按下先 CaptureMouse，
    /// 祖先随后接管拖拽再 CaptureMouse）会再调一次 <c>SetCapture(自己)</c>，Win32 会**同步**回一条
    /// WM_CAPTURECHANGED。此时若照常清掉托管捕获，接管者刚拿到的捕获就在 <see cref="UIElement.CaptureMouse"/>
    /// 返回前被清成 null（调用方毫无察觉，返回值仍是 true），之后的鼠标事件不再路由给它 ——
    /// 表现就是「拖拽刚起步就断、元素不跟手」。
    /// </para>
    /// </summary>
    public void HandleNativeCaptureChanged(nint newCaptureWindow, nint selfWindow)
    {
        if (newCaptureWindow != nint.Zero && newCaptureWindow == selfWindow)
            return;

        ResetClientMouseLeaveTracking();
        UIElement.OnNativeCaptureChanged();
        ClearMousePressedChain();
    }

    /// <summary>Window was deactivated — reset transient input state.</summary>
    public void HandleWindowDeactivated(nint newForegroundWindow, bool clearKeyboardFocus)
    {
        CloseLightDismissPopupsOnDeactivate(newForegroundWindow);
        ResetTransientInputStateOnDeactivate();

        if (clearKeyboardFocus)
            Keyboard.ClearFocus();

        _host.UpdateInputMethodAssociation();
        _host.WakeRenderPipeline();
    }

    /// <summary>WM_CANCELMODE — cancel all modal input state.</summary>
    public void HandleCancelMode()
    {
        HandleWindowDeactivated(nint.Zero, clearKeyboardFocus: false);
    }

    /// <summary>Window set focus — update IME association.</summary>
    public void HandleSetFocus()
    {
        _host.UpdateInputMethodAssociation();
        _host.WakeRenderPipeline();
    }

    /// <summary>Arms escape key suppression after window reactivation.</summary>
    public void ArmEscapeSuppressionIfNeeded()
    {
        _suppressEscapeUntilTick = _host.IsVirtualKeyDown(0x1B) // VK_ESCAPE
            ? Environment.TickCount64 + EscapeReactivateSuppressionMs
            : 0;
    }

    private void CloseLightDismissPopupsOnDeactivate(nint newForegroundWindow)
    {
        if (_host.IsPopupWindow(newForegroundWindow))
            return;

        _ = _host.OverlayLayer.CloseLightDismissPopups();

        if (_host.ActiveExternalPopups.Count == 0)
            return;

        var popupsToClose = _host.ActiveExternalPopups
            .Where(p => !p.StaysOpen)
            .ToList();
        foreach (var popup in popupsToClose)
            popup.IsOpen = false;
    }

    private void ResetTransientInputStateOnDeactivate()
    {
        ResetClientMouseLeaveTracking();
        UIElement.ForceReleaseMouseCapture();
        ClearPressedChains();
        _suppressMouseUpButton = null;

        if (_host.TitleBarStyle == WindowTitleBarStyle.Custom)
            ClearTitleBarInteractionState();
    }

    /// <summary>
    /// Ends input sessions that still reference <paramref name="root"/> after that
    /// subtree has left this window. State is detached before routed notifications
    /// are raised so a handler may synchronously establish input on the replacement
    /// tree (including reusing the same pointer id) without the old cleanup erasing it.
    /// </summary>
    internal void HandleSubtreeDetached(UIElement root)
    {
        ArgumentNullException.ThrowIfNull(root);

        int timestamp = Environment.TickCount;
        bool detachingWholeWindow = ReferenceEquals(root, _host.Self);

        // Snapshot hover while the dispatcher still has the ancestor chain captured
        // at hit-test time. VisualParent on the old content root is already null here.
        UIElement? detachedMouseOver = null;
        UIElement[] detachedMouseOverChain = [];
        if (_lastMouseOverElement is { } lastMouseOver && IsDescendantOf(lastMouseOver, root))
        {
            detachedMouseOver = lastMouseOver;
            detachedMouseOverChain = _mouseOverChain.Count == 0
                ? CaptureAncestorChain(lastMouseOver)
                : _mouseOverChain.ToArray();
            _lastMouseOverElement = null;
            _mouseOverChain.Clear();
        }
        else if (UIElement.MouseDirectlyOverElement is { } directlyOver
                 && IsDescendantOf(directlyOver, root))
        {
            detachedMouseOver = directlyOver;
            detachedMouseOverChain = CaptureAncestorChain(directlyOver);
        }

        UIElement[] detachedMousePressedChain = ChainIntersectsSubtree(_mousePressedChain, root)
            ? DetachPressedChain(_mousePressedChain)
            : [];
        UIElement[] detachedKeyboardPressedChain = ChainIntersectsSubtree(_keyboardPressedChain, root)
            ? DetachPressedChain(_keyboardPressedChain)
            : [];

        UIElement? detachedMouseCapture = UIElement.MouseCapturedElement is { } mouseCapture
            && IsDescendantOf(mouseCapture, root)
                ? mouseCapture
                : null;
        UIElement? detachedStylusCapture = UIElement.StylusCapturedElement is { } stylusCapture
            && IsDescendantOf(stylusCapture, root)
                ? stylusCapture
                : null;

        TitleBarButton? detachedHoveredTitleBarButton =
            _hoveredTitleBarButton is { } hoveredTitleBarButton
            && IsDescendantOf(hoveredTitleBarButton, root)
                ? hoveredTitleBarButton
                : null;
        TitleBarButton? detachedPressedTitleBarButton =
            _pressedTitleBarButton is { } pressedTitleBarButton
            && IsDescendantOf(pressedTitleBarButton, root)
                ? pressedTitleBarButton
                : null;
        if (detachedHoveredTitleBarButton != null)
            _hoveredTitleBarButton = null;
        if (detachedPressedTitleBarButton != null)
            _pressedTitleBarButton = null;

        HashSet<uint> detachedPointerIds = CollectDetachedPointerIds(root);
        List<DetachedPointerState> detachedPointers = new(detachedPointerIds.Count);
        HashSet<PointerManipulationSession> detachedManipulations = [];
        HashSet<PointerManipulationSession> detachedInertialManipulations = [];

        foreach (uint pointerId in detachedPointerIds)
        {
            _activePointerTargets.TryGetValue(pointerId, out UIElement? activeTarget);
            _lastPointerPoints.TryGetValue(pointerId, out PointerPoint? point);
            _lastTouchOverDirect.TryGetValue(pointerId, out UIElement? touchOver);
            _touchOverChains.TryGetValue(pointerId, out List<UIElement>? touchOverChain);
            _activeStylusDevices.TryGetValue(pointerId, out StylusDevice? stylusDevice);
            _activeManipulationSessions.TryGetValue(pointerId, out PointerManipulationSession? manipulation);
            TouchDevice? registeredTouchDevice = Touch.GetDevice(unchecked((int)pointerId));
            TouchDevice? touchDevice = registeredTouchDevice != null
                && (IsWithinSubtree(registeredTouchDevice.DirectlyOver, root)
                    || IsWithinSubtree(registeredTouchDevice.Captured, root)
                    || (registeredTouchDevice.DirectlyOver == null
                        && registeredTouchDevice.Captured == null
                        && (IsWithinSubtree(activeTarget, root) || IsWithinSubtree(touchOver, root))))
                    ? registeredTouchDevice
                    : FindTouchDevice(touchOver, pointerId);
            bool hasRegisteredTouchState = ReferenceEquals(registeredTouchDevice, touchDevice);

            UIElement? cancelTarget = IsWithinSubtree(activeTarget, root)
                ? activeTarget
                : IsWithinSubtree(point?.SourceElement, root)
                    ? point!.SourceElement
                    : hasRegisteredTouchState && IsWithinSubtree(touchOver, root)
                        ? touchOver
                        : hasRegisteredTouchState && IsWithinSubtree(touchDevice?.DirectlyOver, root)
                            ? (UIElement?)touchDevice!.DirectlyOver
                            : IsWithinSubtree(stylusDevice?.DirectlyOver, root)
                                ? (UIElement?)stylusDevice!.DirectlyOver
                                : null;

            if (manipulation != null && IsDescendantOf(manipulation.Target, root))
            {
                detachedManipulations.Add(manipulation);
                if (manipulation.InertiaProcessor?.IsRunning == true)
                    detachedInertialManipulations.Add(manipulation);
            }

            detachedPointers.Add(new DetachedPointerState(
                pointerId,
                cancelTarget,
                point,
                touchOverChain?.ToArray() ?? (touchOver != null ? CaptureAncestorChain(touchOver) : []),
                touchDevice,
                stylusDevice,
                IsWithinSubtree(stylusDevice?.DirectlyOver, root)
                    ? (UIElement?)stylusDevice!.DirectlyOver
                    : null));

            // Remove every id-owned slot before any LostCapture/Leave/Cancel callback.
            _activePointerTargets.Remove(pointerId);
            _lastPointerPoints.Remove(pointerId);
            _lastTouchOverDirect.Remove(pointerId);
            _touchOverChains.Remove(pointerId);
            _activeStylusDevices.Remove(pointerId);
            _gestureTrackers.Remove(pointerId);
            _activeManipulationSessions.Remove(pointerId);
            InvalidatePointerSession(pointerId);
            _host.RealTimeStylus.CancelSession(pointerId);

            if (_primaryTouchPointerId == pointerId)
                _primaryTouchPointerId = null;
        }

        List<ManipulationInertiaProcessor> detachedInertiaProcessors = [];
        PruneInertialManipulations();
        for (int i = _inertialManipulations.Count - 1; i >= 0; i--)
        {
            InertialManipulationRegistration registration = _inertialManipulations[i];
            if (!IsDescendantOf(registration.Target, root))
            {
                continue;
            }

            _inertialManipulations.RemoveAt(i);
            registration.Detach();
            detachedManipulations.Add(registration.Session);
            detachedInertialManipulations.Add(registration.Session);
            detachedInertiaProcessors.Add(registration.Processor);
        }

        // Stop timers before user code runs. A detached target must not receive an
        // inertia tick while another cancellation callback is replacing the page.
        foreach (ManipulationInertiaProcessor processor in detachedInertiaProcessors)
            processor.Cancel();

        if (detachedMousePressedChain.Length > 0 || detachedMouseCapture != null || detachedMouseOver != null
            || detachedPointerIds.Contains(MousePointerId))
        {
            _suppressMouseUpButton = null;
        }

        if (detachingWholeWindow)
        {
            ResetClientMouseLeaveTracking();
            _suppressEscapeUntilTick = 0;
        }

        // Remove each old touch device from the global id registry before its
        // LostTouchCapture notification can re-enter and register a replacement
        // device with the same id. Later cleanup only uses the old device snapshot.
        foreach (DetachedPointerState state in detachedPointers)
        {
            TouchDevice? touchDevice = state.TouchDevice;
            if (touchDevice == null)
                continue;

            touchDevice.SetDirectlyOver(null);
            if (ReferenceEquals(Touch.GetDevice(unchecked((int)state.PointerId)), touchDevice))
            {
                Touch.UnregisterTouchPoint(unchecked((int)state.PointerId));
            }
            else if (IsWithinSubtree(touchDevice.Captured, root))
            {
                touchDevice.Capture(null);
            }
        }

        if (detachedMouseOver != null
            && UIElement.MouseDirectlyOverElement is { } currentDirectlyOver
            && IsDescendantOf(currentDirectlyOver, root))
        {
            Mouse.OnMouseLeaveWindow();
        }

        // From here on every old state slot has been relinquished. Routed handlers
        // are free to create replacement state; all remaining work is snapshot-only.
        if (detachedMouseCapture != null)
        {
            detachedMouseCapture.ReleaseMouseCapture();
            if (Mouse.Captured == null)
                _host.Self.ReleaseNativeCaptureIfOwned();
        }

        if (detachedStylusCapture != null)
            detachedStylusCapture.ReleaseStylusCapture();

        ClearDetachedPressedChain(detachedMousePressedChain, _mousePressedChain);
        ClearDetachedPressedChain(detachedKeyboardPressedChain, _keyboardPressedChain);

        if (detachedHoveredTitleBarButton != null
            && !ReferenceEquals(_hoveredTitleBarButton, detachedHoveredTitleBarButton))
        {
            detachedHoveredTitleBarButton.SetIsMouseOver(false);
        }
        if (detachedPressedTitleBarButton != null
            && !ReferenceEquals(_pressedTitleBarButton, detachedPressedTitleBarButton))
        {
            detachedPressedTitleBarButton.SetIsPressed(false);
        }

        if (detachedMouseOver != null)
            RaiseDetachedMouseLeaveChain(detachedMouseOverChain);

        foreach (DetachedPointerState state in detachedPointers)
        {
            TouchDevice? touchDevice = state.TouchDevice;
            if (touchDevice != null)
            {
                if (state.TouchOverChain.Length > 0)
                    RaiseDetachedTouchLeaveChain(state.TouchOverChain, touchDevice, timestamp);
            }

            StylusDevice? stylusDevice = state.StylusDevice;
            if (stylusDevice != null)
            {
                if (IsWithinSubtree(stylusDevice.DirectlyOver, root))
                {
                    stylusDevice.DirectlyOver = null;
                    if (ReferenceEquals(Tablet.CurrentStylusDevice, stylusDevice))
                        stylusDevice.Synchronize();
                }

                if (state.StylusOver != null)
                    RaiseStylusSimpleEvent(state.StylusOver, stylusDevice, timestamp, UIElement.StylusLeaveEvent);

                if (ReferenceEquals(Tablet.CurrentStylusDevice, stylusDevice))
                {
                    if (IsWithinSubtree(stylusDevice.Captured, root))
                        stylusDevice.Capture(null);

                    // A LostStylusCapture handler may have re-captured this device to
                    // the replacement tree. Preserve that new capture/current device.
                    if (ReferenceEquals(Tablet.CurrentStylusDevice, stylusDevice)
                        && stylusDevice.Captured == null
                        && stylusDevice.DirectlyOver == null)
                    {
                        Tablet.CurrentStylusDevice = null;
                    }
                }
            }
        }

        foreach (PointerManipulationSession session in detachedManipulations)
        {
            TerminateManipulationSession(
                session,
                isInertial: detachedInertialManipulations.Contains(session),
                timestamp);
        }

        foreach (DetachedPointerState state in detachedPointers)
        {
            if (state.CancelTarget == null)
                continue;

            PointerDeviceType deviceType = state.PointerId == MousePointerId
                ? PointerDeviceType.Mouse
                : state.TouchDevice != null || state.TouchOverChain.Length > 0
                    ? PointerDeviceType.Touch
                    : PointerDeviceType.Pen;
            PointerPoint point = state.Point
                ?? CreateCanceledPointerPoint(state.PointerId, deviceType, timestamp);
            RaisePointerCancelPipeline(state.CancelTarget, point, ModifierKeys.None, timestamp);
        }
    }

    internal bool HasInputStateInSubtree(UIElement root)
    {
        if (IsWithinSubtree(_lastMouseOverElement, root) ||
            IsWithinSubtree(_hoveredTitleBarButton, root) ||
            IsWithinSubtree(_pressedTitleBarButton, root) ||
            IsWithinSubtree(UIElement.MouseCapturedElement, root) ||
            IsWithinSubtree(UIElement.StylusCapturedElement, root) ||
            root.AreAnyTouchesCapturedWithin ||
            ChainIntersectsSubtree(_mousePressedChain, root) ||
            ChainIntersectsSubtree(_keyboardPressedChain, root))
            return true;
        foreach (var target in _activePointerTargets.Values)
            if (IsWithinSubtree(target, root)) return true;
        foreach (var point in _lastPointerPoints.Values)
            if (IsWithinSubtree(point.SourceElement, root)) return true;
        foreach (var target in _lastTouchOverDirect.Values)
            if (IsWithinSubtree(target, root)) return true;
        foreach (var session in _activeManipulationSessions.Values)
            if (IsWithinSubtree(session.Target, root)) return true;
        foreach (var pending in _pendingStylusTargets.Values)
            if (IsWithinSubtree(pending.Target, root)) return true;
        foreach (var registration in _inertialManipulations)
            if (IsWithinSubtree(registration.Target, root)) return true;
        return false;
    }

    /// <summary>
    /// Arms client-area leave tracking once per native handle. TrackMouseEvent keeps
    /// the registration active until WM_MOUSELEAVE, so repeating it for every raw move
    /// is redundant. Failed registrations remain unarmed and are retried by the next
    /// move. The generation check protects against a re-entrant leave/deactivation
    /// delivered while the native registration call is in progress.
    /// </summary>
    private void EnsureClientMouseLeaveTracking()
    {
        var handle = _host.Handle;
        if (_isClientMouseLeaveTracking && _clientMouseLeaveTrackingHandle == handle)
            return;

        var generation = _clientMouseLeaveTrackingGeneration;
        var registered = _host.RequestTrackMouseLeave();

        if (generation != _clientMouseLeaveTrackingGeneration || _host.Handle != handle)
            return;

        _isClientMouseLeaveTracking = registered;
        _clientMouseLeaveTrackingHandle = registered ? handle : nint.Zero;
    }

    private void ResetClientMouseLeaveTracking()
    {
        _clientMouseLeaveTrackingGeneration++;
        _isClientMouseLeaveTracking = false;
        _clientMouseLeaveTrackingHandle = nint.Zero;
    }

    // ══════════════════════════════════════════════════════════════
    //  Title Bar Button State (NC messages delegate here)
    // ══════════════════════════════════════════════════════════════

    /// <summary>Updates title bar button hover state.</summary>
    public void UpdateTitleBarButtonHover(TitleBarButton? newHoveredButton)
    {
        if (_hoveredTitleBarButton == newHoveredButton)
            return;

        _hoveredTitleBarButton?.SetIsMouseOver(false);
        _hoveredTitleBarButton = newHoveredButton;
        _hoveredTitleBarButton?.SetIsMouseOver(true);
    }

    /// <summary>Clears all title bar interaction state.</summary>
    public void ClearTitleBarInteractionState()
    {
        UpdateTitleBarButtonHover(null);
        if (_pressedTitleBarButton != null)
        {
            _pressedTitleBarButton.SetIsPressed(false);
            _pressedTitleBarButton = null;
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Mouse Enter/Leave Chain
    // ══════════════════════════════════════════════════════════════

    internal void RaiseMouseLeaveChain(UIElement oldElement, UIElement? newElement, int timestamp)
    {
        HashSet<UIElement> newAncestors = [];
        Visual? current = newElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
                _ = newAncestors.Add(uiElement);
            current = current.VisualParent;
        }

        current = oldElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
            {
                if (newAncestors.Contains(uiElement))
                    break;
                uiElement.SetIsMouseOver(false);
                MouseEventArgs args = new(UIElement.MouseLeaveEvent) { Source = uiElement };
                uiElement.RaiseEvent(args);
            }
            current = current.VisualParent;
        }
    }

    internal void RaiseMouseEnterChain(UIElement newElement, UIElement? oldElement, int timestamp)
    {
        HashSet<UIElement> oldAncestors = [];
        Visual? current = oldElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
                _ = oldAncestors.Add(uiElement);
            current = current.VisualParent;
        }

        List<UIElement> enterElements = [];
        current = newElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
            {
                if (oldAncestors.Contains(uiElement))
                    break;
                enterElements.Add(uiElement);
            }
            current = current.VisualParent;
        }

        for (int i = enterElements.Count - 1; i >= 0; i--)
        {
            var uiElement = enterElements[i];
            uiElement.SetIsMouseOver(true);
            MouseEventArgs args = new(UIElement.MouseEnterEvent) { Source = uiElement };
            uiElement.RaiseEvent(args);
        }
    }

    internal void UpdateMouseOverState(UIElement? newMouseOverElement, int timestamp)
    {
        if (newMouseOverElement != _lastMouseOverElement)
        {
            if (_lastMouseOverElement != null)
                RaiseMouseLeaveChain(_lastMouseOverElement, newMouseOverElement, timestamp);
            if (newMouseOverElement != null)
                RaiseMouseEnterChain(newMouseOverElement, _lastMouseOverElement, timestamp);
            _lastMouseOverElement = newMouseOverElement;
            CaptureAncestorChain(newMouseOverElement, _mouseOverChain);
            UIElement.SetMouseDirectlyOverElement(newMouseOverElement);
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Pressed Chain Management
    // ══════════════════════════════════════════════════════════════

    internal void ActivateMousePressedChain(UIElement target)
    {
        ClearMousePressedChain();
        BuildAncestorChain(target, _mousePressedChain);
        ApplyPressedState(_mousePressedChain, true);
    }

    internal void ClearMousePressedChain()
    {
        ClearPressedChain(_mousePressedChain);
    }

    internal void ActivateKeyboardPressedChain(UIElement target)
    {
        ClearKeyboardPressedChain();
        BuildAncestorChain(target, _keyboardPressedChain);
        ApplyPressedState(_keyboardPressedChain, true);
    }

    internal void ClearKeyboardPressedChain()
    {
        ClearPressedChain(_keyboardPressedChain);
    }

    internal void ClearPressedChains()
    {
        ClearMousePressedChain();
        ClearKeyboardPressedChain();
    }

    private static void BuildAncestorChain(UIElement start, List<UIElement> chain)
        => CaptureAncestorChain(start, chain);

    private static void CaptureAncestorChain(UIElement? start, List<UIElement> chain)
    {
        chain.Clear();
        UIElement? current = start;
        while (current != null)
        {
            chain.Add(current);
            current = current.VisualParent as UIElement;
        }
    }

    private static UIElement[] CaptureAncestorChain(UIElement start)
    {
        List<UIElement> chain = [];
        CaptureAncestorChain(start, chain);
        return chain.ToArray();
    }

    private static void ApplyPressedState(List<UIElement> chain, bool isPressed)
    {
        for (int i = 0; i < chain.Count; i++)
            chain[i].SetIsPressed(isPressed);
    }

    private static void ClearPressedChain(List<UIElement> chain)
    {
        if (chain.Count == 0)
            return;

        // Relinquish ownership before notifying dependency-property observers.
        // A callback may synchronously start a new press; entries adopted by that
        // new chain must stay pressed when the old snapshot finishes unwinding.
        UIElement[] previous = DetachPressedChain(chain);
        ClearDetachedPressedChain(previous, chain);
    }

    private static UIElement[] DetachPressedChain(List<UIElement> chain)
    {
        UIElement[] previous = chain.ToArray();
        chain.Clear();
        return previous;
    }

    private static void ClearDetachedPressedChain(
        UIElement[] previous,
        List<UIElement> currentChain)
    {
        foreach (UIElement element in previous)
        {
            if (!currentChain.Contains(element))
                element.SetIsPressed(false);
        }
    }

    private static bool ChainIntersectsSubtree(List<UIElement> chain, UIElement root)
    {
        foreach (UIElement element in chain)
        {
            if (IsDescendantOf(element, root))
                return true;
        }

        return false;
    }

    // ══════════════════════════════════════════════════════════════
    //  InputBlocked subtree redirect
    // ══════════════════════════════════════════════════════════════
    //
    //  If hit test lands inside a subtree marked with InputManager.InputBlocked,
    //  rewrite the input target to that blocked element itself — descendant
    //  controls never receive routed input events. This is the framework-level
    //  hook designer / preview hosts use to make their content "look real but
    //  inert" without disabling individual controls.

    private static UIElement? RedirectInputBlocked(UIElement? hit)
    {
        if (hit == null) return null;
        var blocked = InputManager.FindInputBlockedAncestor(hit);
        return blocked ?? hit;
    }

    // ══════════════════════════════════════════════════════════════
    //  Menu Mode Hit Testing
    // ══════════════════════════════════════════════════════════════

    private MenuItem? HitTopLevelMenuItemBehindOverlay(Point windowPosition)
    {
        var hitElement = _host.HitIgnoringOverlay(windowPosition)?.VisualHit as UIElement;
        return FindTopLevelMenuItemAncestor(hitElement);
    }

    private static MenuItem? FindTopLevelMenuItemAncestor(UIElement? element)
    {
        var current = element;
        while (current != null)
        {
            if (current is MenuItem menuItem
                && menuItem.VisualParent is Panel panel
                && panel.VisualParent is Menu)
            {
                return menuItem;
            }
            current = current.VisualParent as UIElement;
        }
        return null;
    }

    // ══════════════════════════════════════════════════════════════
    //  Pointer Pipeline Methods
    // ══════════════════════════════════════════════════════════════

    internal void RaisePointerMovePipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp, StylusPointCollection? stylusPoints = null)
    {
        point.SourceElement = target;
        PointerMoveEventArgs previewArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PreviewPointerMoveEvent, StylusPoints = stylusPoints };
        target.RaiseEvent(previewArgs);
        if (previewArgs.Cancel)
        {
            RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            return;
        }

        bool handled = previewArgs.Handled;
        if (!previewArgs.Handled)
        {
            PointerMoveEventArgs bubbleArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PointerMoveEvent, StylusPoints = stylusPoints };
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
            PointerMovedEventArgs legacyArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PointerMovedEvent, StylusPoints = stylusPoints };
            target.RaiseEvent(legacyArgs);
            if (legacyArgs.Cancel)
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
        }
    }

    internal void RaisePointerCancelPipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        point.SourceElement = target;
        PointerCancelEventArgs previewArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PreviewPointerCancelEvent };
        target.RaiseEvent(previewArgs);
        if (!previewArgs.Handled)
        {
            PointerCancelEventArgs bubbleArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PointerCancelEvent };
            target.RaiseEvent(bubbleArgs);
        }
    }

    internal void RaisePointerDownPipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp, StylusPointCollection? stylusPoints = null)
    {
        point.SourceElement = target;
        PointerDownEventArgs previewArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PreviewPointerDownEvent, StylusPoints = stylusPoints };
        target.RaiseEvent(previewArgs);
        if (previewArgs.Cancel)
        {
            RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            return;
        }

        bool handled = previewArgs.Handled;
        if (!previewArgs.Handled)
        {
            PointerDownEventArgs bubbleArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PointerDownEvent, StylusPoints = stylusPoints };
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
            PointerPressedEventArgs legacyArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PointerPressedEvent, StylusPoints = stylusPoints };
            target.RaiseEvent(legacyArgs);
            if (legacyArgs.Cancel)
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
        }
    }

    internal void RaisePointerUpPipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp, StylusPointCollection? stylusPoints = null)
    {
        point.SourceElement = target;
        PointerUpEventArgs previewArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PreviewPointerUpEvent, StylusPoints = stylusPoints };
        target.RaiseEvent(previewArgs);
        if (previewArgs.Cancel)
        {
            RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            return;
        }

        bool handled = previewArgs.Handled;
        if (!previewArgs.Handled)
        {
            PointerUpEventArgs bubbleArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PointerUpEvent, StylusPoints = stylusPoints };
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
            PointerReleasedEventArgs legacyArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PointerReleasedEvent, StylusPoints = stylusPoints };
            target.RaiseEvent(legacyArgs);
            if (legacyArgs.Cancel)
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
        }
    }

    internal static void RaisePointerWheelPipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        PointerWheelChangedEventArgs args = new(point, modifiers, timestamp) { RoutedEvent = PointerEvents.PointerWheelChangedEvent };
        target.RaiseEvent(args);
    }

    // ══════════════════════════════════════════════════════════════
    //  Helper Methods
    // ══════════════════════════════════════════════════════════════

    internal static PointerPoint CreateMousePointerPoint(
        Point position, MouseButtonStates buttons, ModifierKeys modifiers,
        int timestamp, PointerUpdateKind updateKind, int mouseWheelDelta = 0)
    {
        PointerPointProperties properties = new()
        {
            IsLeftButtonPressed = buttons.Left == MouseButtonState.Pressed,
            IsMiddleButtonPressed = buttons.Middle == MouseButtonState.Pressed,
            IsRightButtonPressed = buttons.Right == MouseButtonState.Pressed,
            IsXButton1Pressed = buttons.XButton1 == MouseButtonState.Pressed,
            IsXButton2Pressed = buttons.XButton2 == MouseButtonState.Pressed,
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

    internal static PointerUpdateKind MapMouseButtonToPointerUpdateKind(MouseButton button, bool isPressed)
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

    internal static bool IsDescendantOf(UIElement descendant, UIElement ancestor)
    {
        int depthGuard = 0;
        for (Visual? current = descendant; current != null && depthGuard++ < 4096; current = current.VisualParent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }
        return false;
    }

    private static bool IsWithinSubtree(IInputElement? candidate, UIElement root) =>
        candidate is UIElement element && IsDescendantOf(element, root);

    private static TouchDevice? FindTouchDevice(UIElement? directlyOver, uint pointerId)
    {
        if (directlyOver == null)
            return null;

        int deviceId = unchecked((int)pointerId);
        return directlyOver.TouchesDirectlyOver.FirstOrDefault(device => device.Id == deviceId)
            ?? directlyOver.TouchesOver.FirstOrDefault(device => device.Id == deviceId);
    }

    private HashSet<uint> CollectDetachedPointerIds(UIElement root)
    {
        HashSet<uint> result = [];

        foreach (var pair in _activePointerTargets)
        {
            if (IsWithinSubtree(pair.Value, root))
                result.Add(pair.Key);
        }

        foreach (var pair in _lastPointerPoints)
        {
            if (IsWithinSubtree(pair.Value.SourceElement, root))
                result.Add(pair.Key);
        }

        foreach (var pair in _lastTouchOverDirect)
        {
            if (IsDescendantOf(pair.Value, root))
                result.Add(pair.Key);
        }

        foreach (var pair in _activeStylusDevices)
        {
            if (IsWithinSubtree(pair.Value.DirectlyOver, root)
                || IsWithinSubtree(pair.Value.Captured, root))
            {
                result.Add(pair.Key);
            }
        }

        foreach (var pair in _activeManipulationSessions)
        {
            if (IsDescendantOf(pair.Value.Target, root))
                result.Add(pair.Key);
        }

        foreach (var pair in _pendingStylusTargets)
        {
            if (IsDescendantOf(pair.Value.Target, root))
                result.Add(pair.Key);
        }

        foreach (TouchDevice device in Touch.ActiveDevices)
        {
            if (IsWithinSubtree(device.DirectlyOver, root)
                || IsWithinSubtree(device.Captured, root))
            {
                result.Add(unchecked((uint)device.Id));
            }
        }

        return result;
    }

    private void RaiseDetachedMouseLeaveChain(UIElement[] oldChain)
    {
        HashSet<UIElement> raised = [];
        foreach (UIElement element in oldChain)
        {
            if (!raised.Add(element) || _mouseOverChain.Contains(element))
                continue;

            element.SetIsMouseOver(false);
            MouseEventArgs args = new(UIElement.MouseLeaveEvent) { Source = element };
            element.RaiseEvent(args);
        }
    }

    private static PointerPoint CreateCanceledPointerPoint(
        uint pointerId,
        PointerDeviceType deviceType,
        int timestamp)
    {
        return new PointerPoint(
            pointerId,
            new Point(0, 0),
            deviceType,
            false,
            new PointerPointProperties(),
            (ulong)timestamp);
    }

    private long GetPointerSessionToken(uint pointerId, bool startNewSession)
    {
        if (startNewSession || !_pointerSessionTokens.TryGetValue(pointerId, out long token))
        {
            token = unchecked(++_nextPointerSessionToken);
            if (token == 0)
                token = unchecked(++_nextPointerSessionToken);
            _pointerSessionTokens[pointerId] = token;
        }

        return token;
    }

    private bool IsPointerSessionCurrent(uint pointerId, long token) =>
        _pointerSessionTokens.TryGetValue(pointerId, out long current) && current == token;

    private void InvalidatePointerSession(uint pointerId)
    {
        _pointerSessionTokens.Remove(pointerId);
        _pendingStylusTargets.Remove(pointerId);
    }

    private void CompletePointerSession(uint pointerId, long token)
    {
        if (IsPointerSessionCurrent(pointerId, token))
        {
            _pointerSessionTokens.Remove(pointerId);
            if (_pendingStylusTargets.TryGetValue(pointerId, out var pending)
                && pending.Token == token)
            {
                _pendingStylusTargets.Remove(pointerId);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    //  Pointer/Touch/Stylus Input Pipeline
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Unified entry point for pointer input (touch, pen) from both Win32 and cross-platform paths.
    /// Routes through the full Touch → Stylus → Manipulation → Pointer pipeline.
    /// </summary>
    public void HandlePointerInput(PointerInputData pointerData, bool isDown, bool isUp, int timestamp)
    {
        // Mouse pointer type: route through existing mouse handlers
        if (pointerData.Kind == PointerInputKind.Mouse)
        {
            var buttons = MouseButtonStates.AllReleased;
            if (isDown)
            {
                buttons = buttons.WithButton(MouseButton.Left, MouseButtonState.Pressed);
                HandleMouseDown(MouseButton.Left, pointerData.Position, buttons, pointerData.Modifiers, clickCount: 1, timestamp);
            }
            else if (isUp)
            {
                HandleMouseUp(MouseButton.Left, pointerData.Position, buttons, pointerData.Modifiers, timestamp);
            }
            else
            {
                HandleMouseMove(pointerData.Position, buttons, pointerData.Modifiers, timestamp);
            }
            return;
        }

        bool isTouch = pointerData.Kind == PointerInputKind.Touch;

        // Track primary touch pointer for mouse synthesis
        if (isTouch && isDown && _primaryTouchPointerId == null)
            _primaryTouchPointerId = pointerData.PointerId;
        // Hit test and target resolution
        var captured = UIElement.MouseCapturedElement;
        var hitTarget = _host.HitTestElement(pointerData.Position, "pointer-route");
        // Touch-mode fallback: when the strict hit landed on the window itself
        // (i.e. nothing precise was hit), widen the hit-test region for touch
        // and pen contacts and retry. Mouse never gets the expanded hit area —
        // it has a pixel-accurate cursor.
        if (hitTarget == _host.Self
            && Jalium.UI.Hosting.TouchModeOptions.Current.Enabled
            && pointerData.Kind != PointerInputKind.Mouse)
        {
            hitTarget = HitTestWithTouchMargin(pointerData.Position) ?? hitTarget;
        }
        hitTarget = RedirectInputBlocked(hitTarget);
        var fallbackTarget = captured ?? hitTarget ?? _host.Self;
        var target = isDown
            ? fallbackTarget
            : (_activePointerTargets.TryGetValue(pointerData.PointerId, out var existingTarget)
                ? existingTarget ?? fallbackTarget : fallbackTarget);
        _activePointerTargets[pointerData.PointerId] = target;
        _lastPointerPoints[pointerData.PointerId] = pointerData.Point;

        // Dispatch source-level events (Touch or Stylus)
        bool sourceHandled = false;
        bool sourceCanceled = pointerData.IsCanceled;

        if (pointerData.Kind == PointerInputKind.Touch)
            DispatchTouchSourcePipeline(target, pointerData, isDown, isUp, timestamp, ref sourceHandled, ref sourceCanceled);
        else if (pointerData.Kind == PointerInputKind.Pen)
            DispatchStylusSourcePipeline(target, pointerData, isDown, isUp, timestamp, ref sourceHandled, ref sourceCanceled);
        if (sourceCanceled)
        {
            CancelManipulationSession(pointerData.PointerId, timestamp);
            RaisePointerCancelPipeline(target, pointerData.Point, pointerData.Modifiers, timestamp);
            CleanupPointerSession(pointerData.PointerId);
            if (isTouch && _primaryTouchPointerId == pointerData.PointerId)
                _primaryTouchPointerId = null;
            return;
        }

        // Manipulation pipeline
        DispatchManipulationPipeline(target, pointerData, isDown, isUp, sourceHandled, timestamp);

        // Pointer events — always raise so ancestors like ScrollViewer can run
        // their PointerDown/Move/Up panning gate even when a descendant Button
        // marked the TouchDown handled. Per-element Handled on the pointer
        // routed events still suppresses duplicate handlers naturally.
        if (isDown)
            RaisePointerDownPipeline(target, pointerData.Point, pointerData.Modifiers, timestamp, pointerData.StylusPoints);
        else if (isUp)
            RaisePointerUpPipeline(target, pointerData.Point, pointerData.Modifiers, timestamp, pointerData.StylusPoints);
        else
            RaisePointerMovePipeline(target, pointerData.Point, pointerData.Modifiers, timestamp, pointerData.StylusPoints);

        // Synthesize mouse events for the primary touch pointer
        bool willSynthesize = isTouch && _primaryTouchPointerId == pointerData.PointerId && !sourceHandled;
        if (willSynthesize)
            SynthesizeMouseFromTouch(pointerData.Position, pointerData.Modifiers, isDown, isUp, timestamp);

        if (isUp)
        {
            CleanupPointerSession(pointerData.PointerId);
            if (isTouch && _primaryTouchPointerId == pointerData.PointerId)
                _primaryTouchPointerId = null;
        }
    }

    /// <summary>Handles pointer wheel (touch/pen wheel events).</summary>
    public void HandlePointerWheel(PointerInputData pointerData, int timestamp)
    {
        if (pointerData.Kind == PointerInputKind.Mouse)
            return;

        var target = _activePointerTargets.TryGetValue(pointerData.PointerId, out var existingTarget)
            ? existingTarget ?? _host.Self
            : (RedirectInputBlocked(_host.HitTestElement(pointerData.Position, "pointer-wheel")) ?? _host.Self);

        if (pointerData.IsCanceled)
        {
            RaisePointerCancelPipeline(target, pointerData.Point, pointerData.Modifiers, timestamp);
            CleanupPointerSession(pointerData.PointerId);
            return;
        }

        RaisePointerWheelPipeline(target, pointerData.Point, pointerData.Modifiers, timestamp);
    }

    /// <summary>Handles pointer capture changed (Win32 WM_POINTERCAPTURECHANGED).</summary>
    public void HandlePointerCaptureChanged(uint pointerId, int timestamp)
    {
        if (_activePointerTargets.TryGetValue(pointerId, out var target) && target != null)
        {
            if (!_lastPointerPoints.TryGetValue(pointerId, out var point))
            {
                point = new PointerPoint(
                    pointerId, new Point(0, 0), PointerDeviceType.Touch, false,
                    new PointerPointProperties(), (ulong)timestamp);
            }

            CancelManipulationSession(pointerId, timestamp);
            RaisePointerCancelPipeline(target, point, ModifierKeys.None, timestamp);
        }

        CleanupPointerSession(pointerId);
    }

    /// <summary>Handles pointer cancel from cross-platform path.</summary>
    public void HandlePointerCancel(PointerInputData pointerData, int timestamp)
    {
        if (_activePointerTargets.TryGetValue(pointerData.PointerId, out var target) && target != null)
        {
            if (!_lastPointerPoints.TryGetValue(pointerData.PointerId, out var point))
                point = pointerData.Point;

            if (pointerData.Kind == PointerInputKind.Touch)
            {
                var touchDevice = Touch.GetDevice((int)pointerData.PointerId);
                if (touchDevice != null)
                {
                    touchDevice.DeactivateForManager();
                    Touch.UnregisterTouchPoint((int)pointerData.PointerId);
                }
                _activeStylusDevices.Remove(pointerData.PointerId);
            }

            CancelManipulationSession(pointerData.PointerId, timestamp);
            RaisePointerCancelPipeline(target, point, pointerData.Modifiers, timestamp);
        }

        CleanupPointerSession(pointerData.PointerId);

        if (_primaryTouchPointerId == pointerData.PointerId)
        {
            _primaryTouchPointerId = null;
            HandleMouseLeave();
        }
    }

    // ── Touch Source Pipeline ──

    private void DispatchTouchSourcePipeline(
        UIElement target, PointerInputData pointerData,
        bool isDown, bool isUp, int timestamp,
        ref bool sourceHandled, ref bool sourceCanceled)
    {
        int touchId = (int)pointerData.PointerId;
        TouchAction action = sourceCanceled ? TouchAction.Cancel
            : (isDown ? TouchAction.Down : (isUp ? TouchAction.Up : TouchAction.Move));
        // The visual target captured for this contact, if any. Capture wins over hit-test.
        UIElement? captured = UIElement.GetTouchCapture(touchId);
        UIElement effectiveTarget = captured ?? target;

        // Acquire or create the TouchDevice keyed on this pointer.
        TouchDevice touchDevice = isDown
            ? Touch.RegisterTouchPoint(touchId, pointerData.Position, effectiveTarget)
            : Touch.GetDevice(touchId) ?? Touch.RegisterTouchPoint(touchId, pointerData.Position, effectiveTarget);

        touchDevice.RetargetTo(effectiveTarget);
        touchDevice.UpdatePosition(pointerData.Position);
        touchDevice.SetDirectlyOver(target); // raw hit, ignoring capture
        touchDevice.RecordFrame(pointerData.StylusPoints, pointerData.Point.Properties.ContactRect, action, timestamp);

        // ── TouchEnter / TouchLeave routing (Direct, walking ancestor chains) ──
        _lastTouchOverDirect.TryGetValue(pointerData.PointerId, out UIElement? previousOver);
        if (!ReferenceEquals(previousOver, target))
        {
            if (previousOver != null)
                RaiseTouchLeaveChain(previousOver, target, touchDevice, timestamp);
            if (target != null)
                RaiseTouchEnterChain(target, previousOver, touchDevice, timestamp);
            if (target == null)
            {
                _lastTouchOverDirect.Remove(pointerData.PointerId);
                _touchOverChains.Remove(pointerData.PointerId);
            }
            else
            {
                _lastTouchOverDirect[pointerData.PointerId] = target;
                CaptureTouchOverChain(pointerData.PointerId, target);
            }
        }

        // Touch → Stylus promotion (InkCanvas, RealTimeStylus).
        PromoteTouchToStylus(effectiveTarget, pointerData, isDown, isUp, timestamp);

        RoutedEvent previewEvent = isDown ? UIElement.PreviewTouchDownEvent
            : (isUp ? UIElement.PreviewTouchUpEvent : UIElement.PreviewTouchMoveEvent);
        RoutedEvent bubbleEvent = isDown ? UIElement.TouchDownEvent
            : (isUp ? UIElement.TouchUpEvent : UIElement.TouchMoveEvent);

        TouchEventArgs previewArgs = new(touchDevice, timestamp) { RoutedEvent = previewEvent };
        effectiveTarget.RaiseEvent(previewArgs);
        sourceHandled |= previewArgs.Handled;
        sourceCanceled |= previewArgs.Cancel;

        if (!previewArgs.Handled)
        {
            TouchEventArgs bubbleArgs = new(touchDevice, timestamp) { RoutedEvent = bubbleEvent };
            effectiveTarget.RaiseEvent(bubbleArgs);
            sourceHandled |= bubbleArgs.Handled;
            sourceCanceled |= bubbleArgs.Cancel;
        }

        if (isUp || sourceCanceled)
        {
            // Force-release any captured contact so handlers see LostTouchCapture
            // before the device is torn down.
            UIElement? captureOwner = UIElement.GetTouchCapture(touchId);
            captureOwner?.ReleaseTouchCapture(touchDevice);

            // Mirror the leave chain for any remaining over-elements.
            if (_lastTouchOverDirect.TryGetValue(pointerData.PointerId, out var residual))
            {
                RaiseTouchLeaveChain(residual, null, touchDevice, timestamp);
                _lastTouchOverDirect.Remove(pointerData.PointerId);
                _touchOverChains.Remove(pointerData.PointerId);
            }

            touchDevice.DeactivateForManager();
            Touch.UnregisterTouchPoint(touchId);
            _activeStylusDevices.Remove(pointerData.PointerId);
        }
    }

    private static void RaiseTouchLeaveChain(UIElement oldElement, UIElement? newElement, TouchDevice device, int timestamp)
    {
        HashSet<UIElement> newAncestors = [];
        Visual? current = newElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
                _ = newAncestors.Add(uiElement);
            current = current.VisualParent;
        }

        bool isDirect = true;
        current = oldElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
            {
                if (newAncestors.Contains(uiElement))
                    break;
                uiElement.RemoveOverTouchInternal(device);
                if (isDirect)
                {
                    uiElement.RemoveDirectlyOverTouchInternal(device);
                    isDirect = false;
                }
                TouchEventArgs args = new(device, timestamp) { RoutedEvent = UIElement.TouchLeaveEvent, Source = uiElement };
                uiElement.RaiseEvent(args);
            }
            current = current.VisualParent;
        }
    }

    private static void RaiseTouchEnterChain(UIElement newElement, UIElement? oldElement, TouchDevice device, int timestamp)
    {
        HashSet<UIElement> oldAncestors = [];
        Visual? current = oldElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
                _ = oldAncestors.Add(uiElement);
            current = current.VisualParent;
        }

        List<UIElement> enterElements = [];
        current = newElement;
        while (current != null)
        {
            if (current is UIElement uiElement)
            {
                if (oldAncestors.Contains(uiElement))
                    break;
                enterElements.Add(uiElement);
            }
            current = current.VisualParent;
        }

        // Walk root → leaf so parent enter fires before child enter (matching MouseEnter semantics).
        for (int i = enterElements.Count - 1; i >= 0; i--)
        {
            UIElement uiElement = enterElements[i];
            uiElement.AddOverTouchInternal(device);
            if (i == 0)
                uiElement.AddDirectlyOverTouchInternal(device);
            TouchEventArgs args = new(device, timestamp) { RoutedEvent = UIElement.TouchEnterEvent, Source = uiElement };
            uiElement.RaiseEvent(args);
        }
    }

    private void PromoteTouchToStylus(
        UIElement target, PointerInputData pointerData,
        bool isDown, bool isUp, int timestamp)
    {
        if (!_activeStylusDevices.TryGetValue(pointerData.PointerId, out var stylusDevice))
        {
            stylusDevice = new StylusDevice((int)pointerData.PointerId, $"Touch{pointerData.PointerId}");
            _activeStylusDevices[pointerData.PointerId] = stylusDevice;
        }

        stylusDevice.UpdateState(
            pointerData.Position, pointerData.StylusPoints,
            inAir: !pointerData.Point.IsInContact,
            inverted: false, inRange: pointerData.IsInRange,
            barrelPressed: false, eraserPressed: false,
            directlyOver: target);

        StylusInputAction inputAction = ResolveStylusInputAction(isDown, isUp, pointerData.Point.IsInContact);

        // Gesture tracking + DoubleTap detection on the UI thread (synchronous):
        // these need to observe the live packet timing before the RTS thread
        // potentially mutates / delays anything. Touch handlers see the same
        // sequence they always have; only the Stylus* routed events are
        // marshalled to fire after the RTS-thread plug-ins complete.
        if (isDown)
        {
            TrackGestureDown(pointerData, timestamp);
            TryEmitDoubleTap(target, stylusDevice, pointerData, timestamp);
        }
        else if (!isUp)
        {
            TrackGestureMove(pointerData, timestamp);
        }

        // Snapshot locals so the continuation does not depend on dispatcher
        // mutation that may have happened between enqueueing and the UI
        // dispatcher picking up the callback.
        var deviceCopy = stylusDevice;
        var targetCopy = target;
        var pointerDataCopy = pointerData;
        bool isDownCopy = isDown, isUpCopy = isUp;
        int timestampCopy = timestamp;
        long sessionToken = GetPointerSessionToken(pointerData.PointerId, isDown);
        _pendingStylusTargets[pointerData.PointerId] = (sessionToken, target);

        _host.RealTimeStylus.BeginProcess(
            pointerData.PointerId, target, inputAction,
            stylusDevice.GetStylusPoints(target), timestamp,
            inAir: !pointerData.Point.IsInContact,
            inRange: pointerData.IsInRange,
            barrelButtonPressed: false, eraserPressed: false,
            inverted: false, pointerCanceled: pointerData.IsCanceled,
            onCompleted: processResult =>
            {
                if (!IsPointerSessionCurrent(pointerDataCopy.PointerId, sessionToken))
                    return;

                // Re-apply mutated points + raise StylusXxx routed events on the UI thread.
                deviceCopy.UpdateState(
                    pointerDataCopy.Position, processResult.RawStylusInput.GetStylusPoints(),
                    inAir: !pointerDataCopy.Point.IsInContact,
                    inverted: false, inRange: pointerDataCopy.IsInRange,
                    barrelPressed: false, eraserPressed: false,
                    directlyOver: targetCopy);

                RaiseStylusExtendedEvents(targetCopy, deviceCopy, timestampCopy,
                    ResolveStylusInputAction(isDownCopy, isUpCopy, pointerDataCopy.Point.IsInContact),
                    processResult);
                if (isUpCopy)
                {
                    TrackGestureUpAndEmit(targetCopy, deviceCopy, pointerDataCopy, timestampCopy);
                }

                RoutedEvent previewEvent = isDownCopy ? UIElement.PreviewStylusDownEvent
                    : (isUpCopy ? UIElement.PreviewStylusUpEvent : UIElement.PreviewStylusMoveEvent);
                RoutedEvent bubbleEvent = isDownCopy ? UIElement.StylusDownEvent
                    : (isUpCopy ? UIElement.StylusUpEvent : UIElement.StylusMoveEvent);

                StylusEventArgs previewArgs = CreateStylusEventArgs(deviceCopy, timestampCopy, previewEvent, isDownCopy);
                targetCopy.RaiseEvent(previewArgs);

                if (!previewArgs.Handled && !processResult.Canceled)
                {
                    StylusEventArgs bubbleArgs = CreateStylusEventArgs(deviceCopy, timestampCopy, bubbleEvent, isDownCopy);
                    targetCopy.RaiseEvent(bubbleArgs);
                }

                QueueProcessedStylusCallbacks(
                    processResult,
                    pointerDataCopy.PointerId,
                    sessionToken,
                    completeSession: isUpCopy || processResult.Canceled || processResult.SessionEnded);
            });
    }

    // ── Stylus (Pen) Source Pipeline ──

    private void DispatchStylusSourcePipeline(
        UIElement target, PointerInputData pointerData,
        bool isDown, bool isUp, int timestamp,
        ref bool sourceHandled, ref bool sourceCanceled)
    {
        if (!_activeStylusDevices.TryGetValue(pointerData.PointerId, out var stylusDevice))
        {
            stylusDevice = new StylusDevice((int)pointerData.PointerId);
            _activeStylusDevices[pointerData.PointerId] = stylusDevice;
        }

        Tablet.CurrentStylusDevice = stylusDevice;

        var properties = pointerData.Point.Properties;
        stylusDevice.UpdateState(
            pointerData.Position, pointerData.StylusPoints,
            inAir: !pointerData.Point.IsInContact,
            inverted: properties.IsInverted,
            inRange: pointerData.IsInRange,
            barrelPressed: properties.IsBarrelButtonPressed,
            eraserPressed: properties.IsEraser,
            directlyOver: target);

        StylusInputAction inputAction = ResolveStylusInputAction(isDown, isUp, pointerData.Point.IsInContact);

        // UI-thread synchronous bookkeeping: gesture tracker + double-tap.
        if (isDown)
        {
            TrackGestureDown(pointerData, timestamp);
            TryEmitDoubleTap(target, stylusDevice, pointerData, timestamp);
        }
        else if (!isUp)
        {
            TrackGestureMove(pointerData, timestamp);
        }

        // Snapshot locals — see PromoteTouchToStylus for rationale.
        var deviceCopy = stylusDevice;
        var targetCopy = target;
        var pointerDataCopy = pointerData;
        var propertiesCopy = properties;
        bool isDownCopy = isDown, isUpCopy = isUp;
        int timestampCopy = timestamp;
        StylusInputAction actionCopy = inputAction;
        long sessionToken = GetPointerSessionToken(pointerData.PointerId, isDown);
        _pendingStylusTargets[pointerData.PointerId] = (sessionToken, target);

        _host.RealTimeStylus.BeginProcess(
            pointerData.PointerId, target, inputAction,
            stylusDevice.GetStylusPoints(target), timestamp,
            inAir: !pointerData.Point.IsInContact,
            inRange: pointerData.IsInRange,
            barrelButtonPressed: properties.IsBarrelButtonPressed,
            eraserPressed: properties.IsEraser,
            inverted: properties.IsInverted,
            pointerCanceled: pointerData.IsCanceled,
            onCompleted: processResult =>
            {
                if (!IsPointerSessionCurrent(pointerDataCopy.PointerId, sessionToken))
                    return;

                deviceCopy.UpdateState(
                    pointerDataCopy.Position, processResult.RawStylusInput.GetStylusPoints(),
                    inAir: !pointerDataCopy.Point.IsInContact,
                    inverted: propertiesCopy.IsInverted,
                    inRange: pointerDataCopy.IsInRange,
                    barrelPressed: propertiesCopy.IsBarrelButtonPressed,
                    eraserPressed: propertiesCopy.IsEraser,
                    directlyOver: targetCopy);

                RaiseStylusExtendedEvents(targetCopy, deviceCopy, timestampCopy, actionCopy, processResult);
                if (isUpCopy)
                {
                    TrackGestureUpAndEmit(targetCopy, deviceCopy, pointerDataCopy, timestampCopy);
                }

                RoutedEvent previewEvent = isDownCopy ? UIElement.PreviewStylusDownEvent
                    : (isUpCopy ? UIElement.PreviewStylusUpEvent : UIElement.PreviewStylusMoveEvent);
                RoutedEvent bubbleEvent = isDownCopy ? UIElement.StylusDownEvent
                    : (isUpCopy ? UIElement.StylusUpEvent : UIElement.StylusMoveEvent);

                StylusEventArgs previewArgs = CreateStylusEventArgs(deviceCopy, timestampCopy, previewEvent, isDownCopy);
                targetCopy.RaiseEvent(previewArgs);
                if (!previewArgs.Handled && !processResult.Canceled)
                {
                    StylusEventArgs bubbleArgs = CreateStylusEventArgs(deviceCopy, timestampCopy, bubbleEvent, isDownCopy);
                    targetCopy.RaiseEvent(bubbleArgs);
                }
                bool completesSession = isUpCopy || processResult.Canceled || processResult.SessionEnded;
                QueueProcessedStylusCallbacks(
                    processResult,
                    pointerDataCopy.PointerId,
                    sessionToken,
                    completeSession: completesSession);

                if (completesSession)
                {
                    if (_activeStylusDevices.TryGetValue(pointerDataCopy.PointerId, out var activeDevice)
                        && ReferenceEquals(activeDevice, deviceCopy))
                    {
                        _activeStylusDevices.Remove(pointerDataCopy.PointerId);
                    }
                    if (ReferenceEquals(Tablet.CurrentStylusDevice, deviceCopy))
                        Tablet.CurrentStylusDevice = null;
                }
            });

        // sourceHandled / sourceCanceled stay as the caller set them — the
        // Stylus* routed events fire asynchronously and intentionally do not
        // gate the main-thread Pointer / mouse-synthesis pipeline (matches
        // WPF, where stylus and pointer pipelines are independent).
    }

    // ── Stylus Helper Methods ──

    private static StylusInputAction ResolveStylusInputAction(bool isDown, bool isUp, bool isInContact)
    {
        if (isDown) return StylusInputAction.Down;
        if (isUp) return StylusInputAction.Up;
        return isInContact ? StylusInputAction.Move : StylusInputAction.InAirMove;
    }

    private static StylusEventArgs CreateStylusEventArgs(StylusDevice stylusDevice, int timestamp, RoutedEvent routedEvent, bool isDown)
    {
        StylusEventArgs args = isDown
            ? new StylusDownEventArgs(stylusDevice, timestamp)
            : new StylusEventArgs(stylusDevice, timestamp);
        args.RoutedEvent = routedEvent;
        return args;
    }

    private void QueueProcessedStylusCallbacks(
        RealTimeStylusProcessResult processResult,
        uint pointerId,
        long sessionToken,
        bool completeSession)
    {
        IReadOnlyList<RawStylusInput.ProcessedCallback> callbacks =
            processResult.RawStylusInput.DrainProcessedCallbacks();
        Dispatcher dispatcher = _host.Self.Dispatcher;

        foreach (RawStylusInput.ProcessedCallback callback in callbacks)
        {
            RawStylusInput.ProcessedCallback pending = callback;
            dispatcher.BeginInvoke(() =>
            {
                if (!IsPointerSessionCurrent(pointerId, sessionToken))
                    return;

                try
                {
                    UIElement? plugInElement = pending.PlugIn.Element;
                    bool targetVerified = plugInElement != null
                        && IsDescendantOf(processResult.RawStylusInput.Target, plugInElement);
                    pending.PlugIn.InvokeProcessed(
                        pending.Action,
                        pending.CallbackData,
                        targetVerified);
                }
                catch
                {
                    // Processed-stage failures must not escape the input loop.
                }
            });
        }

        if (completeSession)
        {
            // Always retire on a later dispatcher turn. Earlier packets in this
            // session may already have processed callbacks queued, and FIFO order
            // lets them run before this final retirement. A re-entrant new Down gets
            // a fresh token and is therefore not removed by this old completion.
            dispatcher.BeginInvoke(() => CompletePointerSession(pointerId, sessionToken));
        }
    }

    private void CaptureTouchOverChain(uint pointerId, UIElement target)
    {
        if (!_touchOverChains.TryGetValue(pointerId, out List<UIElement>? chain))
        {
            chain = [];
            _touchOverChains[pointerId] = chain;
        }

        CaptureAncestorChain(target, chain);
    }

    private static void RaiseDetachedTouchLeaveChain(
        UIElement[] oldChain,
        TouchDevice device,
        int timestamp)
    {
        bool isDirect = true;
        HashSet<UIElement> raised = [];
        foreach (UIElement element in oldChain)
        {
            if (!raised.Add(element))
                continue;

            element.RemoveOverTouchInternal(device);
            if (isDirect)
            {
                element.RemoveDirectlyOverTouchInternal(device);
                isDirect = false;
            }

            TouchEventArgs args = new(device, timestamp)
            {
                RoutedEvent = UIElement.TouchLeaveEvent,
                Source = element,
            };
            element.RaiseEvent(args);
        }
    }

    private static StylusButton? GetBarrelButton(StylusDevice stylusDevice)
    {
        foreach (var button in stylusDevice.StylusButtons)
        {
            if (button.Name.Equals("Barrel", StringComparison.OrdinalIgnoreCase))
                return button;
        }
        return stylusDevice.StylusButtons.Count > 0 ? stylusDevice.StylusButtons[0] : null;
    }

    private static void RaiseStylusSimpleEvent(UIElement target, StylusDevice stylusDevice, int timestamp, RoutedEvent routedEvent)
    {
        var args = new StylusEventArgs(stylusDevice, timestamp) { RoutedEvent = routedEvent };
        target.RaiseEvent(args);
    }

    private static void RaiseStylusSimpleEventPair(
        UIElement target,
        StylusDevice stylusDevice,
        int timestamp,
        RoutedEvent previewEvent,
        RoutedEvent bubbleEvent)
    {
        var previewArgs = new StylusEventArgs(stylusDevice, timestamp) { RoutedEvent = previewEvent };
        target.RaiseEvent(previewArgs);
        if (!previewArgs.Handled)
        {
            target.RaiseEvent(new StylusEventArgs(stylusDevice, timestamp) { RoutedEvent = bubbleEvent });
        }
    }

    private static void RaiseStylusSystemGestureEvent(UIElement target, StylusDevice stylusDevice, int timestamp, SystemGesture gesture)
    {
        var previewArgs = new StylusSystemGestureEventArgs(stylusDevice, timestamp, gesture)
        {
            RoutedEvent = UIElement.PreviewStylusSystemGestureEvent
        };
        target.RaiseEvent(previewArgs);
        if (previewArgs.Handled)
        {
            return;
        }

        var bubbleArgs = new StylusSystemGestureEventArgs(stylusDevice, timestamp, gesture)
        {
            RoutedEvent = UIElement.StylusSystemGestureEvent
        };
        target.RaiseEvent(bubbleArgs);
    }

    // ── Touch-mode hit-test fallback ──

    /// <summary>
    /// When strict hit-test misses, scan 8 cardinal/diagonal probes inside the
    /// configured touch-target radius and return the first non-window element.
    /// Honours Z-order naturally because each probe runs through the same hit-
    /// test stack.
    /// </summary>
    private UIElement? HitTestWithTouchMargin(Point center)
    {
        double radius = Jalium.UI.Hosting.TouchModeOptions.Current.MinHitTargetSize / 2.0;
        if (radius <= 0) return null;
        // Inner ring first (75% of radius) so we prefer near-hits; then the outer ring.
        double r1 = radius * 0.5;
        ReadOnlySpan<(double dx, double dy)> directions = stackalloc (double, double)[]
        {
            (1, 0), (-1, 0), (0, 1), (0, -1),
            (0.707, 0.707), (-0.707, 0.707), (0.707, -0.707), (-0.707, -0.707)
        };
        for (int ring = 0; ring < 2; ring++)
        {
            double r = ring == 0 ? r1 : radius;
            for (int i = 0; i < directions.Length; i++)
            {
                var (dx, dy) = directions[i];
                var probe = new Point(center.X + dx * r, center.Y + dy * r);
                var hit = _host.HitTestElement(probe, "touch-margin");
                if (hit != null && !ReferenceEquals(hit, _host.Self))
                {
                    return hit;
                }
            }
        }
        return null;
    }

    // ── Gesture tracker entry points (Tap/Flick/DoubleTap/TwoFingerTap) ──

    private void TrackGestureDown(PointerInputData data, int timestamp)
    {
        _gestureTrackers[data.PointerId] = new PointerGestureTracker
        {
            DownTimestampMs = timestamp,
            DownPosition = data.Position,
            LastPosition = data.Position,
            LastSampleTimestampMs = timestamp,
            LastSpeedDipsPerMs = 0,
            DeviceType = data.Point.PointerDeviceType
        };
    }

    private void TrackGestureMove(PointerInputData data, int timestamp)
    {
        if (!_gestureTrackers.TryGetValue(data.PointerId, out var tracker)) return;
        long dt = Math.Max(1, timestamp - tracker.LastSampleTimestampMs);
        Vector delta = data.Position - tracker.LastPosition;
        tracker.LastSpeedDipsPerMs = delta.Length / dt;
        tracker.LastPosition = data.Position;
        tracker.LastSampleTimestampMs = timestamp;
    }

    /// <summary>
    /// Returns the additional system gestures to fire on Up (Flick, TwoFingerTap).
    /// Always clears the per-pointer tracker.
    /// </summary>
    private void TrackGestureUpAndEmit(UIElement target, StylusDevice stylusDevice, PointerInputData data, int timestamp)
    {
        if (!_gestureTrackers.TryGetValue(data.PointerId, out var tracker)) return;
        _gestureTrackers.Remove(data.PointerId);

        long duration = timestamp - tracker.DownTimestampMs;
        bool isShortTap = duration <= ShortTapMaxDurationMs
                          && (tracker.LastPosition - tracker.DownPosition).Length <= DoubleTapDistanceThresholdDips;

        // Flick: lift velocity exceeds threshold, regardless of tap classification.
        if (tracker.LastSpeedDipsPerMs > FlickVelocityThresholdDipsPerMs)
        {
            RaiseStylusSystemGestureEvent(target, stylusDevice, timestamp, SystemGesture.Flick);
        }

        // TwoFingerTap: two touch contacts that lifted within TwoFingerTapTimeoutMs of one another.
        if (isShortTap && tracker.DeviceType == PointerDeviceType.Touch)
        {
            if (_firstTouchUpTicks > 0
                && (timestamp - _firstTouchUpTicks) <= TwoFingerTapTimeoutMs
                && (tracker.LastPosition - _firstTouchUpPosition).Length <= TwoFingerTapDistanceDips)
            {
                RaiseStylusSystemGestureEvent(target, stylusDevice, timestamp, SystemGesture.TwoFingerTap);
                _firstTouchUpTicks = 0;
            }
            else
            {
                _firstTouchUpTicks = timestamp;
                _firstTouchUpPosition = tracker.LastPosition;
            }
        }

        // DoubleTap: short tap whose down was within DoubleTapTimeoutMs of the previous tap's up.
        // We record the *up* timestamp here so the *next* Down can detect a double-tap.
        if (isShortTap)
        {
            _lastTapByDevice[tracker.DeviceType] = (timestamp, tracker.LastPosition);
        }
        else
        {
            _lastTapByDevice.Remove(tracker.DeviceType);
        }
    }

    /// <summary>
    /// Called from Down handlers to detect a paired tap → emit DoubleTap and clear the previous-tap latch.
    /// </summary>
    private bool TryEmitDoubleTap(UIElement target, StylusDevice stylusDevice, PointerInputData data, int timestamp)
    {
        if (!_lastTapByDevice.TryGetValue(data.Point.PointerDeviceType, out var prev)) return false;
        if (timestamp - prev.ticks > DoubleTapTimeoutMs) { _lastTapByDevice.Remove(data.Point.PointerDeviceType); return false; }
        if ((prev.pos - data.Position).Length > DoubleTapDistanceThresholdDips) return false;
        _lastTapByDevice.Remove(data.Point.PointerDeviceType);
        RaiseStylusSystemGestureEvent(target, stylusDevice, timestamp, SystemGesture.DoubleTap);
        return true;
    }

    private static void RaiseStylusButtonEvent(
        UIElement target,
        StylusDevice stylusDevice,
        int timestamp,
        RoutedEvent previewEvent,
        RoutedEvent bubbleEvent)
    {
        StylusButton? button = GetBarrelButton(stylusDevice);
        if (button == null) return;

        var previewArgs = new StylusButtonEventArgs(stylusDevice, timestamp, button) { RoutedEvent = previewEvent };
        target.RaiseEvent(previewArgs);
        if (!previewArgs.Handled)
        {
            target.RaiseEvent(new StylusButtonEventArgs(stylusDevice, timestamp, button) { RoutedEvent = bubbleEvent });
        }
    }

    private static void RaiseStylusExtendedEvents(
        UIElement target, StylusDevice stylusDevice, int timestamp,
        StylusInputAction inputAction, RealTimeStylusProcessResult processResult)
    {
        if (processResult.LeftElement && processResult.PreviousTarget != null)
            RaiseStylusSimpleEvent(processResult.PreviousTarget, stylusDevice, timestamp, UIElement.StylusLeaveEvent);

        if (processResult.EnteredElement)
            RaiseStylusSimpleEvent(target, stylusDevice, timestamp, UIElement.StylusEnterEvent);

        if (processResult.EnteredRange)
        {
            RaiseStylusSimpleEventPair(target, stylusDevice, timestamp,
                UIElement.PreviewStylusInRangeEvent, UIElement.StylusInRangeEvent);
            RaiseStylusSystemGestureEvent(target, stylusDevice, timestamp, SystemGesture.HoverEnter);
        }

        if (processResult.ExitedRange)
        {
            UIElement rangeTarget = processResult.PreviousTarget ?? target;
            RaiseStylusSimpleEventPair(rangeTarget, stylusDevice, timestamp,
                UIElement.PreviewStylusOutOfRangeEvent, UIElement.StylusOutOfRangeEvent);
            RaiseStylusSystemGestureEvent(rangeTarget, stylusDevice, timestamp, SystemGesture.HoverLeave);
        }

        if (processResult.BarrelButtonDown)
            RaiseStylusButtonEvent(target, stylusDevice, timestamp,
                UIElement.PreviewStylusButtonDownEvent, UIElement.StylusButtonDownEvent);

        if (processResult.BarrelButtonUp)
            RaiseStylusButtonEvent(target, stylusDevice, timestamp,
                UIElement.PreviewStylusButtonUpEvent, UIElement.StylusButtonUpEvent);

        switch (inputAction)
        {
            case StylusInputAction.Down:
                RaiseStylusSystemGestureEvent(target, stylusDevice, timestamp,
                    stylusDevice.StylusButtons.Count > 0 && stylusDevice.StylusButtons[0].StylusButtonState == StylusButtonState.Down
                        ? SystemGesture.RightTap : SystemGesture.Tap);
                RaiseStylusSystemGestureEvent(target, stylusDevice, timestamp, SystemGesture.HoldEnter);
                break;
            case StylusInputAction.Move:
                RaiseStylusSystemGestureEvent(target, stylusDevice, timestamp,
                    stylusDevice.StylusButtons.Count > 0 && stylusDevice.StylusButtons[0].StylusButtonState == StylusButtonState.Down
                        ? SystemGesture.RightDrag : SystemGesture.Drag);
                break;
            case StylusInputAction.InAirMove:
                RaiseStylusSimpleEventPair(target, stylusDevice, timestamp,
                    UIElement.PreviewStylusInAirMoveEvent, UIElement.StylusInAirMoveEvent);
                break;
            case StylusInputAction.Up:
                RaiseStylusSystemGestureEvent(target, stylusDevice, timestamp, SystemGesture.HoldLeave);
                break;
        }
    }

    // ── Manipulation Pipeline ──
    //
    //  A session is per-target. Multiple pointer contacts hitting the same
    //  manipulation-enabled target join the same session and contribute to a
    //  multi-finger transform: translation (centroid motion), scale (spread
    //  ratio relative to centroid) and rotation (mean angle of pointers
    //  about the centroid). _activeManipulationSessions keys by pointer id
    //  for fast cleanup, but every pointer entry that belongs to one target
    //  points to the same PointerManipulationSession instance.

    private void DispatchManipulationPipeline(
        UIElement target, PointerInputData pointerData,
        bool isDown, bool isUp, bool sourceHandled, int timestamp)
    {
        if (isDown)
        {
            if (sourceHandled || !target.IsManipulationEnabled)
                return;

            // Try joining an existing session anchored on this target (or one of its
            // ancestors / descendants for nested manipulations — WPF anchors on the
            // first manipulation-enabled target; we keep it simple by anchoring on
            // `target` itself for new sessions and joining only when target matches).
            PointerManipulationSession? existing = FindActiveSessionFor(target);
            if (existing != null)
            {
                if (existing.IsSingleTouchEnabled)
                    return; // don't admit additional contacts in single-touch mode
                existing.AddPointer(pointerData.PointerId, pointerData.Point.Position, timestamp);
                _activeManipulationSessions[pointerData.PointerId] = existing;
                return;
            }

            ManipulationStartingEventArgs? startingArgs = RaiseManipulationStartingPipeline(target);
            if (startingArgs == null) return;

            UIElement container = startingArgs.ManipulationContainer as UIElement ?? target;
            var session = new PointerManipulationSession(
                container,
                pointerData.Point.Position,
                timestamp,
                startingArgs.Mode,
                startingArgs.IsSingleTouchEnabled,
                startingArgs.Pivot);
            session.AddPointer(pointerData.PointerId, pointerData.Point.Position, timestamp);
            _activeManipulationSessions[pointerData.PointerId] = session;

            RaiseManipulationStartedPipeline(container, pointerData.Point.Position, timestamp);
            return;
        }

        if (!_activeManipulationSessions.TryGetValue(pointerData.PointerId, out var activeSession))
            return;

        if (isUp)
        {
            _activeManipulationSessions.Remove(pointerData.PointerId);
            activeSession.RemovePointer(pointerData.PointerId);
            if (activeSession.PointerCount == 0)
            {
                // All contacts have lifted: hand off to inertia (or terminate).
                StartManipulationInertiaOrComplete(activeSession, timestamp);
            }
            else
            {
                // Re-baseline so the next move doesn't see a jump from the removed pointer.
                activeSession.Rebaseline(timestamp);
            }
            return;
        }

        if (sourceHandled)
            return;

        activeSession.UpdatePointer(pointerData.PointerId, pointerData.Point.Position);
        RaiseManipulationDeltaPipeline(activeSession, timestamp);
    }

    private PointerManipulationSession? FindActiveSessionFor(UIElement target)
    {
        foreach (var session in _activeManipulationSessions.Values)
        {
            if (ReferenceEquals(session.Target, target))
                return session;
        }
        return null;
    }

    /// <returns>The (possibly bubble-mutated) starting args if the manipulation should proceed; null when cancelled.</returns>
    private static ManipulationStartingEventArgs? RaiseManipulationStartingPipeline(UIElement target)
    {
        ManipulationStartingEventArgs previewArgs = new()
        {
            RoutedEvent = UIElement.PreviewManipulationStartingEvent,
            ManipulationContainer = target,
            Mode = ManipulationModes.All
        };
        target.RaiseEvent(previewArgs);
        if (previewArgs.CancelRequested) return null;

        if (previewArgs.Handled)
            return previewArgs;

        ManipulationStartingEventArgs bubbleArgs = new()
        {
            RoutedEvent = UIElement.ManipulationStartingEvent,
            ManipulationContainer = previewArgs.ManipulationContainer ?? target,
            Mode = previewArgs.Mode,
            Pivot = previewArgs.Pivot,
            IsSingleTouchEnabled = previewArgs.IsSingleTouchEnabled
        };
        target.RaiseEvent(bubbleArgs);
        if (bubbleArgs.CancelRequested) return null;
        return bubbleArgs;
    }

    private static void RaiseManipulationStartedPipeline(UIElement target, Point origin, int timestamp)
    {
        ManipulationStartedEventArgs previewArgs = new()
        {
            RoutedEvent = UIElement.PreviewManipulationStartedEvent,
            ManipulationContainer = target,
            ManipulationOrigin = origin
        };
        target.RaiseEvent(previewArgs);

        if (!previewArgs.Handled)
        {
            ManipulationStartedEventArgs bubbleArgs = new()
            {
                RoutedEvent = UIElement.ManipulationStartedEvent,
                ManipulationContainer = target,
                ManipulationOrigin = origin
            };
            target.RaiseEvent(bubbleArgs);
        }
    }

    private void RaiseManipulationDeltaPipeline(PointerManipulationSession session, int timestamp)
    {
        ManipulationFrameDelta frame = session.ComputeFrameDelta(timestamp);
        if (frame.Trivial && frame.DtMs > 0)
        {
            // Still emit something so handlers see live updates; but skip zero-delta frames entirely.
            return;
        }

        ManipulationDelta deltaThisFrame = new()
        {
            Translation = frame.DeltaTranslation,
            Rotation = frame.DeltaRotation,
            Expansion = frame.DeltaExpansion,
            Scale = frame.FrameScale
        };
        ManipulationDelta cumulative = new()
        {
            Translation = session.CumulativeTranslation,
            Rotation = session.CumulativeRotation,
            Expansion = session.CumulativeExpansion,
            Scale = session.CumulativeScale
        };
        ManipulationVelocities velocities = new()
        {
            LinearVelocity = session.LastLinearVelocity,
            AngularVelocity = session.LastAngularVelocity,
            ExpansionVelocity = session.LastExpansionVelocity
        };

        ManipulationDeltaEventArgs previewArgs = new()
        {
            RoutedEvent = UIElement.PreviewManipulationDeltaEvent,
            ManipulationContainer = session.Target,
            ManipulationOrigin = session.Origin,
            DeltaManipulation = deltaThisFrame,
            CumulativeManipulation = cumulative,
            Velocities = velocities,
            IsInertial = false
        };
        session.Target.RaiseEvent(previewArgs);

        ManipulationDeltaEventArgs bubbleArgs = previewArgs;
        if (!previewArgs.Handled)
        {
            bubbleArgs = new()
            {
                RoutedEvent = UIElement.ManipulationDeltaEvent,
                ManipulationContainer = session.Target,
                ManipulationOrigin = session.Origin,
                DeltaManipulation = deltaThisFrame,
                CumulativeManipulation = cumulative,
                Velocities = velocities,
                IsInertial = false
            };
            session.Target.RaiseEvent(bubbleArgs);
        }

        // Boundary feedback surfaced from handler.
        ManipulationDelta? unused = previewArgs.UnusedManipulation ?? bubbleArgs.UnusedManipulation;
        if (unused != null && DeltaHasContent(unused))
            RaiseBoundaryFeedback(session.Target, unused);

        // Honour Complete/Cancel/StartInertia requests.
        if (previewArgs.CancelRequested || bubbleArgs.CancelRequested)
        {
            CancelManipulationSessionByInstance(session, raiseBoundary: false, timestamp);
        }
        else if (previewArgs.CompleteRequested || bubbleArgs.CompleteRequested)
        {
            TerminateManipulationSession(session, isInertial: false, timestamp);
        }
        else if (previewArgs.StartInertiaRequested || bubbleArgs.StartInertiaRequested)
        {
            // Force an immediate inertia phase even while contacts remain.
            StartManipulationInertiaOrComplete(session, timestamp);
        }
    }

    private void StartManipulationInertiaOrComplete(PointerManipulationSession session, int timestamp)
    {
        // First raise the InertiaStarting event so handlers can supply behaviors.
        ManipulationVelocities velocities = new()
        {
            LinearVelocity = session.LastLinearVelocity,
            AngularVelocity = session.LastAngularVelocity,
            ExpansionVelocity = session.LastExpansionVelocity
        };

        ManipulationInertiaStartingEventArgs previewArgs = new()
        {
            RoutedEvent = UIElement.PreviewManipulationInertiaStartingEvent,
            ManipulationContainer = session.Target,
            ManipulationOrigin = session.Origin,
            InitialVelocities = velocities
        };
        session.Target.RaiseEvent(previewArgs);

        ManipulationInertiaStartingEventArgs bubbleArgs = previewArgs;
        if (!previewArgs.Handled)
        {
            bubbleArgs = new()
            {
                RoutedEvent = UIElement.ManipulationInertiaStartingEvent,
                ManipulationContainer = session.Target,
                ManipulationOrigin = session.Origin,
                InitialVelocities = velocities,
                TranslationBehavior = previewArgs.TranslationBehavior,
                RotationBehavior = previewArgs.RotationBehavior,
                ExpansionBehavior = previewArgs.ExpansionBehavior
            };
            session.Target.RaiseEvent(bubbleArgs);
        }

        if (previewArgs.CancelRequested || bubbleArgs.CancelRequested)
        {
            TerminateManipulationSession(session, isInertial: false, timestamp);
            return;
        }

        // Build the integrator and start.
        ManipulationDelta cumulative = new()
        {
            Translation = session.CumulativeTranslation,
            Rotation = session.CumulativeRotation,
            Expansion = session.CumulativeExpansion,
            Scale = session.CumulativeScale
        };

        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        var processor = new ManipulationInertiaProcessor(session.Target, session.Origin, cumulative, dispatcher);
        if (!processor.Start(
            session.LastLinearVelocity,
            session.LastAngularVelocity,
            session.LastExpansionVelocity,
            bubbleArgs.TranslationBehavior,
            bubbleArgs.RotationBehavior,
            bubbleArgs.ExpansionBehavior))
        {
            // Velocity below stop threshold — go straight to Completed (non-inertial).
            TerminateManipulationSession(session, isInertial: false, timestamp);
            return;
        }

        session.InertiaProcessor = processor;
        TrackInertialManipulation(session, processor);
    }

    private void TerminateManipulationSession(PointerManipulationSession session, bool isInertial, int timestamp)
    {
        // Remove all pointers that point to this session.
        var keys = _activeManipulationSessions
            .Where(kv => ReferenceEquals(kv.Value, session))
            .Select(kv => kv.Key)
            .ToArray();
        foreach (var key in keys)
            _activeManipulationSessions.Remove(key);

        session.InertiaProcessor?.Cancel();
        RemoveInertialManipulation(session);
        session.InertiaProcessor = null;

        RaiseManipulationCompletedPipeline(session, isInertial, timestamp);
    }

    private static void RaiseManipulationCompletedPipeline(PointerManipulationSession session, bool isInertial, int timestamp)
    {
        ManipulationDelta total = new()
        {
            Translation = session.CumulativeTranslation,
            Rotation = session.CumulativeRotation,
            Expansion = session.CumulativeExpansion,
            Scale = session.CumulativeScale
        };
        ManipulationVelocities velocities = new()
        {
            LinearVelocity = session.LastLinearVelocity,
            AngularVelocity = session.LastAngularVelocity,
            ExpansionVelocity = session.LastExpansionVelocity
        };

        ManipulationCompletedEventArgs previewArgs = new()
        {
            RoutedEvent = UIElement.PreviewManipulationCompletedEvent,
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
                RoutedEvent = UIElement.ManipulationCompletedEvent,
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
        CancelManipulationSessionByInstance(session, raiseBoundary: true, timestamp);
    }

    private void CancelManipulationSessionByInstance(PointerManipulationSession session, bool raiseBoundary, int timestamp)
    {
        if (raiseBoundary)
        {
            RaiseBoundaryFeedback(session.Target, new ManipulationDelta { Scale = new Vector(1, 1) });
        }
        TerminateManipulationSession(session, isInertial: false, timestamp);
    }

    private static void RaiseBoundaryFeedback(UIElement target, ManipulationDelta unused)
    {
        ManipulationBoundaryFeedbackEventArgs previewArgs = new()
        {
            RoutedEvent = UIElement.PreviewManipulationBoundaryFeedbackEvent,
            ManipulationContainer = target,
            BoundaryFeedback = unused
        };
        target.RaiseEvent(previewArgs);
        if (!previewArgs.Handled)
        {
            ManipulationBoundaryFeedbackEventArgs bubbleArgs = new()
            {
                RoutedEvent = UIElement.ManipulationBoundaryFeedbackEvent,
                ManipulationContainer = target,
                BoundaryFeedback = unused
            };
            target.RaiseEvent(bubbleArgs);
        }
    }

    private static bool DeltaHasContent(ManipulationDelta delta) =>
        delta.Translation.Length > 0.0001
        || Math.Abs(delta.Rotation) > 0.0001
        || delta.Expansion.Length > 0.0001
        || delta.Scale.X != 1.0 || delta.Scale.Y != 1.0;

    private void TrackInertialManipulation(
        PointerManipulationSession session,
        ManipulationInertiaProcessor processor)
    {
        PruneInertialManipulations();
        _inertialManipulations.Add(new InertialManipulationRegistration(this, session, processor));
    }

    private void RemoveInertialManipulation(PointerManipulationSession session)
    {
        for (int i = _inertialManipulations.Count - 1; i >= 0; i--)
        {
            InertialManipulationRegistration registration = _inertialManipulations[i];
            if (ReferenceEquals(registration.Session, session))
            {
                _inertialManipulations.RemoveAt(i);
                registration.Detach();
            }
        }
    }

    private void RemoveInertialManipulation(InertialManipulationRegistration registration)
    {
        if (_inertialManipulations.Remove(registration))
            registration.Detach();
    }

    private void PruneInertialManipulations()
    {
        for (int i = _inertialManipulations.Count - 1; i >= 0; i--)
        {
            InertialManipulationRegistration registration = _inertialManipulations[i];
            if (!registration.Processor.IsRunning)
            {
                _inertialManipulations.RemoveAt(i);
                registration.Detach();
            }
        }
    }

    // ── Session Cleanup ──

    internal void CleanupPointerSession(uint pointerId)
    {
        _activePointerTargets.Remove(pointerId);
        _lastPointerPoints.Remove(pointerId);
        if (_activeStylusDevices.TryGetValue(pointerId, out var stylusDevice))
        {
            _activeStylusDevices.Remove(pointerId);
            if (ReferenceEquals(Tablet.CurrentStylusDevice, stylusDevice))
                Tablet.CurrentStylusDevice = null;
        }

        _host.RealTimeStylus.CancelSession(pointerId);
        _activeManipulationSessions.Remove(pointerId);

        TouchDevice? touchDevice = Touch.GetDevice((int)pointerId);
        if (touchDevice != null)
        {
            touchDevice.DeactivateForManager();
            Touch.UnregisterTouchPoint((int)pointerId);
        }
    }

    // ── Mouse Synthesis from Touch ──

    private void SynthesizeMouseFromTouch(
        Point position, ModifierKeys modifiers,
        bool isDown, bool isUp, int timestamp)
    {
        var buttons = new MouseButtonStates
        {
            Left = isUp ? MouseButtonState.Released : MouseButtonState.Pressed
        };

        SuppressMouseToPointerPromotion = true;
        try
        {
            if (isDown)
                HandleMouseDown(MouseButton.Left, position, buttons, modifiers, clickCount: 1, timestamp);
            else if (isUp)
                HandleMouseUp(MouseButton.Left, position, buttons, modifiers, timestamp);
            else
                HandleMouseMove(position, buttons, modifiers, timestamp);
        }
        finally
        {
            SuppressMouseToPointerPromotion = false;
        }
    }

    // ── PointerManipulationSession ──

    private sealed class DetachedPointerState
    {
        public DetachedPointerState(
            uint pointerId,
            UIElement? cancelTarget,
            PointerPoint? point,
            UIElement[] touchOverChain,
            TouchDevice? touchDevice,
            StylusDevice? stylusDevice,
            UIElement? stylusOver)
        {
            PointerId = pointerId;
            CancelTarget = cancelTarget;
            Point = point;
            TouchOverChain = touchOverChain;
            TouchDevice = touchDevice;
            StylusDevice = stylusDevice;
            StylusOver = stylusOver;
        }

        public uint PointerId { get; }
        public UIElement? CancelTarget { get; }
        public PointerPoint? Point { get; }
        public UIElement[] TouchOverChain { get; }
        public TouchDevice? TouchDevice { get; }
        public StylusDevice? StylusDevice { get; }
        public UIElement? StylusOver { get; }
    }

    private sealed class InertialManipulationRegistration
    {
        public InertialManipulationRegistration(
            WindowInputDispatcher owner,
            PointerManipulationSession session,
            ManipulationInertiaProcessor processor)
        {
            Session = session;
            Processor = processor;
            _completedHandler = (_, args) =>
            {
                if (args is ManipulationCompletedEventArgs { IsInertial: true })
                    owner.RemoveInertialManipulation(this);
            };
            Target.AddHandler(
                UIElement.PreviewManipulationCompletedEvent,
                _completedHandler,
                handledEventsToo: true);
        }

        private readonly RoutedEventHandler _completedHandler;
        private bool _isAttached = true;

        public PointerManipulationSession Session { get; }
        public ManipulationInertiaProcessor Processor { get; }
        public UIElement Target => Session.Target;

        public void Detach()
        {
            if (!_isAttached)
                return;

            _isAttached = false;
            Target.RemoveHandler(UIElement.PreviewManipulationCompletedEvent, _completedHandler);
        }
    }

    /// <summary>
    /// Aggregated per-frame motion computed from the current multi-touch pointer set.
    /// </summary>
    private readonly struct ManipulationFrameDelta
    {
        public ManipulationFrameDelta(Vector translation, double rotation, Vector expansion, Vector frameScale, double dtMs, bool trivial)
        {
            DeltaTranslation = translation;
            DeltaRotation = rotation;
            DeltaExpansion = expansion;
            FrameScale = frameScale;
            DtMs = dtMs;
            Trivial = trivial;
        }
        public Vector DeltaTranslation { get; }
        public double DeltaRotation { get; }   // degrees
        public Vector DeltaExpansion { get; }
        public Vector FrameScale { get; }      // multiplicative scale this frame
        public double DtMs { get; }
        public bool Trivial { get; }
    }

    private sealed class PointerManipulationSession
    {
        // current and initial pointer positions, keyed by pointer id
        private readonly Dictionary<uint, Point> _current = new();
        private readonly Dictionary<uint, Point> _initial = new();

        // last-frame centroid metrics; baselined whenever the pointer set changes
        private Point _baseCentroid;
        private double _baseSpread;          // avg distance from centroid to pointers
        private double _baseAngle;           // angle of first pointer to centroid (deg)
        private int _baseTimestamp;

        public PointerManipulationSession(
            UIElement target, Point origin, int timestamp,
            ManipulationModes mode, bool isSingleTouchEnabled, ManipulationPivot? pivot)
        {
            Target = target;
            Origin = origin;
            Mode = mode;
            IsSingleTouchEnabled = isSingleTouchEnabled;
            Pivot = pivot;
            _baseCentroid = origin;
            _baseTimestamp = timestamp;
            CumulativeScale = new Vector(1, 1);
        }

        public UIElement Target { get; }
        public Point Origin { get; }
        public ManipulationModes Mode { get; }
        public bool IsSingleTouchEnabled { get; }
        public ManipulationPivot? Pivot { get; }
        public int PointerCount => _current.Count;

        public Vector CumulativeTranslation { get; private set; } = Vector.Zero;
        public double CumulativeRotation { get; private set; }                   // degrees
        public Vector CumulativeExpansion { get; private set; } = Vector.Zero;   // DIPs
        public Vector CumulativeScale { get; private set; }                      // multiplicative

        public Vector LastLinearVelocity { get; private set; } = Vector.Zero;    // DIP / ms
        public double LastAngularVelocity { get; private set; }                  // deg / ms
        public Vector LastExpansionVelocity { get; private set; } = Vector.Zero; // DIP / ms

        public ManipulationInertiaProcessor? InertiaProcessor { get; set; }

        public void AddPointer(uint id, Point position, int timestamp)
        {
            _current[id] = position;
            _initial[id] = position;
            Rebaseline(timestamp);
        }

        public void RemovePointer(uint id)
        {
            _current.Remove(id);
            _initial.Remove(id);
            // Caller may invoke Rebaseline if any pointers remain.
        }

        public void UpdatePointer(uint id, Point position)
        {
            if (_current.ContainsKey(id))
                _current[id] = position;
        }

        public void Rebaseline(int timestamp)
        {
            if (_current.Count == 0)
            {
                _baseSpread = 0;
                _baseAngle = 0;
                _baseTimestamp = timestamp;
                return;
            }
            _baseCentroid = ComputeCentroid(_current.Values);
            _baseSpread = ComputeSpread(_current.Values, _baseCentroid);
            _baseAngle = ComputeAngle(_current.Values, _baseCentroid);
            _baseTimestamp = timestamp;
            LastLinearVelocity = Vector.Zero;
            LastAngularVelocity = 0;
            LastExpansionVelocity = Vector.Zero;
        }

        public ManipulationFrameDelta ComputeFrameDelta(int timestamp)
        {
            if (_current.Count == 0)
                return new ManipulationFrameDelta(Vector.Zero, 0, Vector.Zero, new Vector(1, 1), 0, trivial: true);

            Point centroidNow = ComputeCentroid(_current.Values);
            double spreadNow = ComputeSpread(_current.Values, centroidNow);
            double angleNow = ComputeAngle(_current.Values, centroidNow);

            // Translation: centroid delta — gated by Mode.
            Vector deltaT = centroidNow - _baseCentroid;
            if ((Mode & ManipulationModes.TranslateX) == 0) deltaT = new Vector(0, deltaT.Y);
            if ((Mode & ManipulationModes.TranslateY) == 0) deltaT = new Vector(deltaT.X, 0);

            // Scale + Expansion: derived from spread ratio. Need ≥ 2 pointers.
            Vector frameScale = new(1, 1);
            Vector deltaExpansion = Vector.Zero;
            if (_current.Count >= 2 && _baseSpread > 0.0001 && (Mode & ManipulationModes.Scale) != 0)
            {
                double ratio = spreadNow / _baseSpread;
                frameScale = new Vector(ratio, ratio);
                deltaExpansion = new Vector(spreadNow - _baseSpread, spreadNow - _baseSpread);
            }

            // Rotation: angle delta (degrees), wrapped to (-180, 180]. Need ≥ 2 pointers.
            double deltaR = 0;
            if (_current.Count >= 2 && (Mode & ManipulationModes.Rotate) != 0)
            {
                deltaR = WrapAngle(angleNow - _baseAngle);
            }

            double dt = Math.Max(1.0, timestamp - _baseTimestamp);

            // Accumulate.
            CumulativeTranslation += deltaT;
            CumulativeRotation += deltaR;
            CumulativeExpansion += deltaExpansion;
            CumulativeScale = new Vector(CumulativeScale.X * frameScale.X, CumulativeScale.Y * frameScale.Y);

            // Velocities.
            LastLinearVelocity = new Vector(deltaT.X / dt, deltaT.Y / dt);
            LastAngularVelocity = deltaR / dt;
            LastExpansionVelocity = new Vector(deltaExpansion.X / dt, deltaExpansion.Y / dt);

            // Re-baseline for next frame.
            _baseCentroid = centroidNow;
            _baseSpread = spreadNow;
            _baseAngle = angleNow;
            _baseTimestamp = timestamp;

            bool trivial = deltaT.Length < 0.001 && Math.Abs(deltaR) < 0.001 && deltaExpansion.Length < 0.001;
            return new ManipulationFrameDelta(deltaT, deltaR, deltaExpansion, frameScale, dt, trivial);
        }

        private static Point ComputeCentroid(IEnumerable<Point> points)
        {
            double sx = 0, sy = 0;
            int n = 0;
            foreach (var p in points) { sx += p.X; sy += p.Y; n++; }
            return n == 0 ? new Point(0, 0) : new Point(sx / n, sy / n);
        }

        private static double ComputeSpread(IEnumerable<Point> points, Point centroid)
        {
            double sum = 0;
            int n = 0;
            foreach (var p in points)
            {
                double dx = p.X - centroid.X;
                double dy = p.Y - centroid.Y;
                sum += Math.Sqrt(dx * dx + dy * dy);
                n++;
            }
            return n == 0 ? 0 : sum / n;
        }

        private static double ComputeAngle(IEnumerable<Point> points, Point centroid)
        {
            // Use the first pointer's angle relative to centroid as the reference.
            // For 2+ pointers this captures pinch rotation; for 1 pointer it's
            // arbitrary and unused (caller guards on count).
            foreach (var p in points)
            {
                return Math.Atan2(p.Y - centroid.Y, p.X - centroid.X) * (180.0 / Math.PI);
            }
            return 0;
        }

        private static double WrapAngle(double deg)
        {
            while (deg > 180) deg -= 360;
            while (deg <= -180) deg += 360;
            return deg;
        }
    }
}
