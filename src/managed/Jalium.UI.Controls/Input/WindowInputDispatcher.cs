using Jalium.UI.Input;
using Jalium.UI.Input.StylusPlugIns;

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
    private UIElement? _lastHitTestElement;
    private readonly List<UIElement> _mousePressedChain = [];
    private MouseButton? _suppressMouseUpButton;
    private TitleBarButton? _hoveredTitleBarButton;
    private TitleBarButton? _pressedTitleBarButton;

    // ── Keyboard state ──
    private readonly List<UIElement> _keyboardPressedChain = [];
    private bool _keyboardPressActive;
    private long _suppressEscapeUntilTick;
    private const int EscapeReactivateSuppressionMs = 250;

    // ── Pointer/Touch state ──
    internal const uint MousePointerId = 1;
    private readonly Dictionary<uint, UIElement?> _activePointerTargets = [];
    private readonly Dictionary<uint, PointerPoint> _lastPointerPoints = [];
    private readonly Dictionary<uint, PointerStylusDevice> _activeStylusDevices = [];
    private readonly Dictionary<uint, PointerManipulationSession> _activeManipulationSessions = [];
    private uint? _primaryTouchPointerId;

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
    internal UIElement? LastHitTestElement => _lastHitTestElement;
    internal TitleBarButton? HoveredTitleBarButton => _hoveredTitleBarButton;
    internal TitleBarButton? PressedTitleBarButton => _pressedTitleBarButton;
    internal TitleBarButton? PressedTitleBarButtonField { get => _pressedTitleBarButton; set => _pressedTitleBarButton = value; }
    internal MouseButton? SuppressMouseUpButton => _suppressMouseUpButton;
    internal Dictionary<uint, UIElement?> ActivePointerTargets => _activePointerTargets;
    internal Dictionary<uint, PointerPoint> LastPointerPoints => _lastPointerPoints;
    internal Dictionary<uint, PointerStylusDevice> ActiveStylusDevices => _activeStylusDevices;

    // ══════════════════════════════════════════════════════════════
    //  Mouse Events
    // ══════════════════════════════════════════════════════════════

    /// <summary>Handles mouse move from both Win32 and cross-platform paths.</summary>
    public void HandleMouseMove(Point position, MouseButtonStates buttons, ModifierKeys modifiers, int timestamp)
    {
        // Allow subclass to intercept
        if (_host.OnPreviewWindowMouseMove(position))
            return;

        // Check for title bar button hover (for custom title bar)
        if (_host.IsTitleBarVisible())
        {
            var titleBarButton = _host.GetTitleBarButtonAtPoint(position);
            UpdateTitleBarButtonHover(titleBarButton);
            _host.RequestTrackMouseLeave();
        }

        // If an element has captured the mouse, it receives all mouse events
        // Otherwise, find the target element via hit testing
        var captured = UIElement.MouseCapturedElement;
        UIElement? hitElement = _host.HitTestElement(position, "mouse-move");
        if (captured == null && hitElement == _host.OverlayLayer && _host.OverlayLayer.HasLightDismissPopups)
        {
            var topLevelMenuItem = HitTopLevelMenuItemBehindOverlay(position);
            if (topLevelMenuItem != null)
            {
                hitElement = topLevelMenuItem;
            }
        }
        var target = captured ?? hitElement ?? _host.Self;

        // Track mouse over state and raise MouseEnter/MouseLeave events
        var newMouseOverElement = hitElement;
        if (newMouseOverElement != _lastMouseOverElement)
        {
            if (_lastMouseOverElement != null)
                RaiseMouseLeaveChain(_lastMouseOverElement, newMouseOverElement, timestamp);
            if (newMouseOverElement != null)
                RaiseMouseEnterChain(newMouseOverElement, _lastMouseOverElement, timestamp);
            _lastMouseOverElement = newMouseOverElement;
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

        // Update cursor based on hit element
        if (hitElement is FrameworkElement fe && fe.Cursor != null)
            _host.SetPlatformCursor((int)fe.Cursor.CursorType);
    }

    /// <summary>Handles mouse button down.</summary>
    public void HandleMouseDown(MouseButton button, Point position, MouseButtonStates buttons,
        ModifierKeys modifiers, int clickCount, int timestamp)
    {
        // Allow subclass to intercept
        if (_host.OnPreviewWindowMouseDown(button, position, clickCount))
            return;

        var topLevelMenuItemBehindOverlay = _host.OverlayLayer.HasLightDismissPopups
            ? HitTopLevelMenuItemBehindOverlay(position)
            : null;

        // Check light dismiss via OverlayLayer — clicks outside popups close them
        if (topLevelMenuItemBehindOverlay == null && _host.OverlayLayer.TryHandleLightDismiss(position))
        {
            _suppressMouseUpButton = button;
            return;
        }

        // Light dismiss for external popup windows (rendered outside the parent window)
        if (_host.ActiveExternalPopups.Count > 0)
        {
            var popupsToClose = _host.ActiveExternalPopups.Where(p => !p.StaysOpen).ToList();
            foreach (var popup in popupsToClose)
                popup.IsOpen = false;
            if (popupsToClose.Count > 0)
            {
                _suppressMouseUpButton = button;
                return;
            }
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
        var captured = UIElement.MouseCapturedElement;
        var hitElement = topLevelMenuItemBehindOverlay ?? _host.HitTestElement(position, "mouse-down");
        UpdateMouseOverState(hitElement, timestamp);
        var target = captured ?? hitElement ?? _host.Self;

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

        var captured = UIElement.MouseCapturedElement;
        var hitElement = _host.HitTestElement(position, "mouse-up");
        UpdateMouseOverState(hitElement, timestamp);
        var target = captured ?? hitElement ?? _host.Self;

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
            ClearMousePressedChain();
    }

    /// <summary>Handles mouse wheel.</summary>
    public void HandleMouseWheel(Point position, int delta, MouseButtonStates buttons,
        ModifierKeys modifiers, int timestamp)
    {
        // Allow subclass to intercept
        if (_host.OnPreviewWindowMouseWheel(delta, position))
            return;

        var captured = UIElement.MouseCapturedElement;
        var target = captured ?? _host.HitTestElement(position, "mouse-wheel") ?? _host.Self;

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
        if (_host.TitleBarStyle == WindowTitleBarStyle.Custom)
            ClearTitleBarInteractionState();

        ClearMousePressedChain();

        if (_lastMouseOverElement != null)
        {
            RaiseMouseLeaveChain(_lastMouseOverElement, null, Environment.TickCount);
            _lastMouseOverElement = null;
        }

        _lastHitTestElement = null;
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

        // F3 toggles debug HUD
        if (key == Key.F3 && !isRepeat)
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
    //  Pointer/Touch Events
    // ══════════════════════════════════════════════════════════════

    /// <summary>Handles Win32 WM_POINTER messages for touch/pen.</summary>
    public void HandlePointerMessage(uint msg, nint wParam, nint lParam)
    {
        // TODO: Phase 6
    }

    /// <summary>Handles pointer wheel (touch/pen).</summary>
    public void HandlePointerWheel(nint wParam, nint lParam)
    {
        // TODO: Phase 6
    }

    /// <summary>Handles pointer capture changed.</summary>
    public void HandlePointerCaptureChanged(uint pointerId)
    {
        // TODO: Phase 6
    }

    // ══════════════════════════════════════════════════════════════
    //  Window Lifecycle (affecting input state)
    // ══════════════════════════════════════════════════════════════

    /// <summary>Native mouse/pointer capture was lost.</summary>
    public void HandleNativeCaptureChanged()
    {
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
        UIElement.ForceReleaseMouseCapture();
        ClearPressedChains();
        _suppressMouseUpButton = null;
        _lastHitTestElement = null;

        if (_host.TitleBarStyle == WindowTitleBarStyle.Custom)
            ClearTitleBarInteractionState();
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
        if (_mousePressedChain.Count > 0)
        {
            ApplyPressedState(_mousePressedChain, false);
            _mousePressedChain.Clear();
        }
    }

    internal void ActivateKeyboardPressedChain(UIElement target)
    {
        ClearKeyboardPressedChain();
        BuildAncestorChain(target, _keyboardPressedChain);
        ApplyPressedState(_keyboardPressedChain, true);
        _keyboardPressActive = true;
    }

    internal void ClearKeyboardPressedChain()
    {
        if (_keyboardPressedChain.Count > 0)
        {
            ApplyPressedState(_keyboardPressedChain, false);
            _keyboardPressedChain.Clear();
        }
        _keyboardPressActive = false;
    }

    internal void ClearPressedChains()
    {
        ClearMousePressedChain();
        ClearKeyboardPressedChain();
    }

    private static void BuildAncestorChain(UIElement start, List<UIElement> chain)
    {
        chain.Clear();
        UIElement? current = start;
        while (current != null)
        {
            chain.Add(current);
            current = current.VisualParent as UIElement;
        }
    }

    private static void ApplyPressedState(List<UIElement> chain, bool isPressed)
    {
        for (int i = 0; i < chain.Count; i++)
            chain[i].SetIsPressed(isPressed);
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

    internal void RaisePointerMovePipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        PointerMoveEventArgs previewArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PreviewPointerMoveEvent };
        target.RaiseEvent(previewArgs);
        if (previewArgs.Cancel)
        {
            RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            return;
        }

        bool handled = previewArgs.Handled;
        if (!previewArgs.Handled)
        {
            PointerMoveEventArgs bubbleArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PointerMoveEvent };
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
            PointerMovedEventArgs legacyArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PointerMovedEvent };
            target.RaiseEvent(legacyArgs);
            if (legacyArgs.Cancel)
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
        }
    }

    internal void RaisePointerCancelPipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        PointerCancelEventArgs previewArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PreviewPointerCancelEvent };
        target.RaiseEvent(previewArgs);
        if (!previewArgs.Handled)
        {
            PointerCancelEventArgs bubbleArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PointerCancelEvent };
            target.RaiseEvent(bubbleArgs);
        }
    }

    internal void RaisePointerDownPipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        PointerDownEventArgs previewArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PreviewPointerDownEvent };
        target.RaiseEvent(previewArgs);
        if (previewArgs.Cancel)
        {
            RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            return;
        }

        bool handled = previewArgs.Handled;
        if (!previewArgs.Handled)
        {
            PointerDownEventArgs bubbleArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PointerDownEvent };
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
            PointerPressedEventArgs legacyArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PointerPressedEvent };
            target.RaiseEvent(legacyArgs);
            if (legacyArgs.Cancel)
                RaisePointerCancelPipeline(target, point, modifiers, timestamp);
        }
    }

    internal void RaisePointerUpPipeline(UIElement target, PointerPoint point, ModifierKeys modifiers, int timestamp)
    {
        PointerUpEventArgs previewArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PreviewPointerUpEvent };
        target.RaiseEvent(previewArgs);
        if (previewArgs.Cancel)
        {
            RaisePointerCancelPipeline(target, point, modifiers, timestamp);
            return;
        }

        bool handled = previewArgs.Handled;
        if (!previewArgs.Handled)
        {
            PointerUpEventArgs bubbleArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PointerUpEvent };
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
            PointerReleasedEventArgs legacyArgs = new(point, modifiers, timestamp) { RoutedEvent = UIElement.PointerReleasedEvent };
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

        // Pointer events
        if (!sourceHandled)
        {
            if (isDown)
                RaisePointerDownPipeline(target, pointerData.Point, pointerData.Modifiers, timestamp);
            else if (isUp)
                RaisePointerUpPipeline(target, pointerData.Point, pointerData.Modifiers, timestamp);
            else
                RaisePointerMovePipeline(target, pointerData.Point, pointerData.Modifiers, timestamp);
        }

        // Synthesize mouse events for the primary touch pointer
        if (isTouch && _primaryTouchPointerId == pointerData.PointerId && !sourceHandled)
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
            : (_host.HitTestElement(pointerData.Position, "pointer-wheel") ?? _host.Self);

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
                    touchDevice.Deactivate();
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
        TouchDevice touchDevice = isDown
            ? Touch.RegisterTouchPoint((int)pointerData.PointerId, pointerData.Position, target)
            : Touch.GetDevice((int)pointerData.PointerId)
              ?? Touch.RegisterTouchPoint((int)pointerData.PointerId, pointerData.Position, target);

        touchDevice.UpdatePosition(pointerData.Position);
        touchDevice.DirectlyOver = target;

        // Touch → Stylus promotion
        PromoteTouchToStylus(target, pointerData, isDown, isUp, timestamp);

        RoutedEvent previewEvent = isDown ? UIElement.PreviewTouchDownEvent
            : (isUp ? UIElement.PreviewTouchUpEvent : UIElement.PreviewTouchMoveEvent);
        RoutedEvent bubbleEvent = isDown ? UIElement.TouchDownEvent
            : (isUp ? UIElement.TouchUpEvent : UIElement.TouchMoveEvent);

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
            _activeStylusDevices.Remove(pointerData.PointerId);
        }
    }

    private void PromoteTouchToStylus(
        UIElement target, PointerInputData pointerData,
        bool isDown, bool isUp, int timestamp)
    {
        if (!_activeStylusDevices.TryGetValue(pointerData.PointerId, out var stylusDevice))
        {
            stylusDevice = new PointerStylusDevice((int)pointerData.PointerId, $"Touch{pointerData.PointerId}");
            _activeStylusDevices[pointerData.PointerId] = stylusDevice;
        }

        stylusDevice.UpdateState(
            pointerData.Position, pointerData.StylusPoints,
            inAir: !pointerData.Point.IsInContact,
            inverted: false, inRange: pointerData.IsInRange,
            barrelPressed: false, eraserPressed: false,
            directlyOver: target);

        StylusInputAction inputAction = ResolveStylusInputAction(isDown, isUp, pointerData.Point.IsInContact);
        RealTimeStylusProcessResult processResult = _host.RealTimeStylus.Process(
            pointerData.PointerId, target, inputAction,
            stylusDevice.GetStylusPoints(target), timestamp,
            inAir: !pointerData.Point.IsInContact,
            inRange: pointerData.IsInRange,
            barrelButtonPressed: false, eraserPressed: false,
            inverted: false, pointerCanceled: pointerData.IsCanceled);

        stylusDevice.UpdateState(
            pointerData.Position, processResult.RawStylusInput.GetStylusPoints(),
            inAir: !pointerData.Point.IsInContact,
            inverted: false, inRange: pointerData.IsInRange,
            barrelPressed: false, eraserPressed: false,
            directlyOver: target);

        RoutedEvent previewEvent = isDown ? UIElement.PreviewStylusDownEvent
            : (isUp ? UIElement.PreviewStylusUpEvent : UIElement.PreviewStylusMoveEvent);
        RoutedEvent bubbleEvent = isDown ? UIElement.StylusDownEvent
            : (isUp ? UIElement.StylusUpEvent : UIElement.StylusMoveEvent);

        StylusEventArgs previewArgs = CreateStylusEventArgs(stylusDevice, timestamp, previewEvent, isDown);
        target.RaiseEvent(previewArgs);

        if (!previewArgs.Handled && !processResult.Canceled)
        {
            StylusEventArgs bubbleArgs = CreateStylusEventArgs(stylusDevice, timestamp, bubbleEvent, isDown);
            target.RaiseEvent(bubbleArgs);
        }

        _host.RealTimeStylus.QueueProcessedCallbacks(processResult);
    }

    // ── Stylus (Pen) Source Pipeline ──

    private void DispatchStylusSourcePipeline(
        UIElement target, PointerInputData pointerData,
        bool isDown, bool isUp, int timestamp,
        ref bool sourceHandled, ref bool sourceCanceled)
    {
        if (!_activeStylusDevices.TryGetValue(pointerData.PointerId, out var stylusDevice))
        {
            stylusDevice = new PointerStylusDevice((int)pointerData.PointerId);
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
        RealTimeStylusProcessResult processResult = _host.RealTimeStylus.Process(
            pointerData.PointerId, target, inputAction,
            stylusDevice.GetStylusPoints(target), timestamp,
            inAir: !pointerData.Point.IsInContact,
            inRange: pointerData.IsInRange,
            barrelButtonPressed: properties.IsBarrelButtonPressed,
            eraserPressed: properties.IsEraser,
            inverted: properties.IsInverted,
            pointerCanceled: pointerData.IsCanceled);

        stylusDevice.UpdateState(
            pointerData.Position, processResult.RawStylusInput.GetStylusPoints(),
            inAir: !pointerData.Point.IsInContact,
            inverted: properties.IsInverted,
            inRange: pointerData.IsInRange,
            barrelPressed: properties.IsBarrelButtonPressed,
            eraserPressed: properties.IsEraser,
            directlyOver: target);

        RaiseStylusExtendedEvents(target, stylusDevice, timestamp, inputAction, processResult);

        RoutedEvent previewEvent = isDown ? UIElement.PreviewStylusDownEvent
            : (isUp ? UIElement.PreviewStylusUpEvent : UIElement.PreviewStylusMoveEvent);
        RoutedEvent bubbleEvent = isDown ? UIElement.StylusDownEvent
            : (isUp ? UIElement.StylusUpEvent : UIElement.StylusMoveEvent);

        StylusEventArgs previewArgs = CreateStylusEventArgs(stylusDevice, timestamp, previewEvent, isDown);
        target.RaiseEvent(previewArgs);

        sourceHandled |= previewArgs.Handled;
        sourceCanceled |= previewArgs.Cancel || processResult.Canceled;

        if (!previewArgs.Handled && !processResult.Canceled)
        {
            StylusEventArgs bubbleArgs = CreateStylusEventArgs(stylusDevice, timestamp, bubbleEvent, isDown);
            target.RaiseEvent(bubbleArgs);
            sourceHandled |= bubbleArgs.Handled;
            sourceCanceled |= bubbleArgs.Cancel;
        }

        _host.RealTimeStylus.QueueProcessedCallbacks(processResult);

        if (isUp || sourceCanceled || processResult.SessionEnded)
        {
            _activeStylusDevices.Remove(pointerData.PointerId);
            if (ReferenceEquals(Tablet.CurrentStylusDevice, stylusDevice))
                Tablet.CurrentStylusDevice = null;
        }
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

    private static void RaiseStylusSystemGestureEvent(UIElement target, StylusDevice stylusDevice, int timestamp, SystemGesture gesture)
    {
        var args = new StylusSystemGestureEventArgs(stylusDevice, timestamp, gesture)
        {
            RoutedEvent = UIElement.StylusSystemGestureEvent
        };
        target.RaiseEvent(args);
    }

    private static void RaiseStylusButtonEvent(UIElement target, StylusDevice stylusDevice, int timestamp, RoutedEvent routedEvent)
    {
        StylusButton? button = GetBarrelButton(stylusDevice);
        if (button == null) return;

        var args = new StylusButtonEventArgs(stylusDevice, timestamp, button) { RoutedEvent = routedEvent };
        target.RaiseEvent(args);
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
            RaiseStylusSimpleEvent(target, stylusDevice, timestamp, UIElement.StylusInRangeEvent);
            RaiseStylusSystemGestureEvent(target, stylusDevice, timestamp, SystemGesture.HoverEnter);
        }

        if (processResult.ExitedRange)
        {
            RaiseStylusSimpleEvent(processResult.PreviousTarget ?? target, stylusDevice, timestamp, UIElement.StylusOutOfRangeEvent);
            RaiseStylusSystemGestureEvent(processResult.PreviousTarget ?? target, stylusDevice, timestamp, SystemGesture.HoverLeave);
        }

        if (processResult.BarrelButtonDown)
            RaiseStylusButtonEvent(target, stylusDevice, timestamp, UIElement.StylusButtonDownEvent);

        if (processResult.BarrelButtonUp)
            RaiseStylusButtonEvent(target, stylusDevice, timestamp, UIElement.StylusButtonUpEvent);

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
                RaiseStylusSimpleEvent(target, stylusDevice, timestamp, UIElement.StylusInAirMoveEvent);
                break;
            case StylusInputAction.Up:
                RaiseStylusSystemGestureEvent(target, stylusDevice, timestamp, SystemGesture.HoldLeave);
                break;
        }
    }

    // ── Manipulation Pipeline ──

    private void DispatchManipulationPipeline(
        UIElement target, PointerInputData pointerData,
        bool isDown, bool isUp, bool sourceHandled, int timestamp)
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

    private static bool RaiseManipulationStartingPipeline(UIElement target)
    {
        ManipulationStartingEventArgs previewArgs = new()
        {
            RoutedEvent = UIElement.PreviewManipulationStartingEvent,
            ManipulationContainer = target,
            Mode = ManipulationModes.All,
            Cancel = false
        };
        target.RaiseEvent(previewArgs);
        if (previewArgs.Cancel) return false;

        if (!previewArgs.Handled)
        {
            ManipulationStartingEventArgs bubbleArgs = new()
            {
                RoutedEvent = UIElement.ManipulationStartingEvent,
                ManipulationContainer = previewArgs.ManipulationContainer ?? target,
                Mode = previewArgs.Mode,
                Pivot = previewArgs.Pivot,
                IsSingleTouchEnabled = previewArgs.IsSingleTouchEnabled,
                Cancel = false
            };
            target.RaiseEvent(bubbleArgs);
            if (bubbleArgs.Cancel) return false;
        }

        return true;
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
            RoutedEvent = UIElement.PreviewManipulationDeltaEvent,
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
                RoutedEvent = UIElement.ManipulationDeltaEvent,
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
        if (session.LastVelocity.Length <= 0.01) return;

        ManipulationVelocities velocities = new()
        {
            LinearVelocity = session.LastVelocity,
            AngularVelocity = 0,
            ExpansionVelocity = Vector.Zero
        };

        ManipulationInertiaStartingEventArgs previewArgs = new()
        {
            RoutedEvent = UIElement.PreviewManipulationInertiaStartingEvent,
            ManipulationContainer = session.Target,
            ManipulationOrigin = session.Origin,
            InitialVelocities = velocities
        };
        session.Target.RaiseEvent(previewArgs);

        if (!previewArgs.Handled)
        {
            ManipulationInertiaStartingEventArgs bubbleArgs = new()
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

        ManipulationBoundaryFeedbackEventArgs previewBoundary = new()
        {
            RoutedEvent = UIElement.PreviewManipulationBoundaryFeedbackEvent,
            ManipulationContainer = session.Target,
            BoundaryFeedback = CreateManipulationDelta(Vector.Zero)
        };
        session.Target.RaiseEvent(previewBoundary);

        if (!previewBoundary.Handled)
        {
            ManipulationBoundaryFeedbackEventArgs bubbleBoundary = new()
            {
                RoutedEvent = UIElement.ManipulationBoundaryFeedbackEvent,
                ManipulationContainer = session.Target,
                BoundaryFeedback = previewBoundary.BoundaryFeedback
            };
            session.Target.RaiseEvent(bubbleBoundary);
        }

        RaiseManipulationCompletedPipeline(session, isInertial: false, timestamp);
        _activeManipulationSessions.Remove(pointerId);
    }

    private static ManipulationDelta CreateManipulationDelta(Vector translation) => new()
    {
        Translation = translation,
        Rotation = 0,
        Scale = new Vector(1, 1),
        Expansion = Vector.Zero
    };

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
            touchDevice.Deactivate();
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
}
