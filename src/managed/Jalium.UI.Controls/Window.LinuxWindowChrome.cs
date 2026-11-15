using Jalium.UI.Input;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using static Jalium.UI.Interop.Win32.Win32Constants;

namespace Jalium.UI;

public partial class Window
{
    private PlatformWindowDecorationMode _linuxDecorationMode;
    private bool _linuxFocusVisibilityPending;
    private bool _linuxRenderingSuspended;
    private PlatformWindowResizeEdges _linuxResizableEdges = PlatformWindowResizeEdges.All;
    private bool _linuxIsTiled;
    private bool _linuxSuppressHandledSpace;

    private void RefreshLinuxResizeConstraints()
    {
        if (!OperatingSystem.IsLinux() || _platformWindow == null) return;
        var edges = _platformWindow.ResizableEdges;
        bool tiled = _platformWindow.IsTiled;
        if (_linuxResizableEdges != edges)
        {
            _linuxResizableEdges = edges;
            _inputDispatcher.RefreshCursor();
        }
        if (_linuxIsTiled == tiled) return;
        _linuxIsTiled = tiled;
        if (SizeToContent != SizeToContent.Manual)
        {
            // A tile can end without changing either client dimensions or
            // resize edges. Resume the preserved auto-size preference then.
            InvalidateMeasure();
            RequestFullInvalidation();
        }
    }

    private int GetLinuxResizeEdge(Point point)
    {
        if (!OperatingSystem.IsLinux() || _platformWindow == null || !IsEnabled ||
            WindowState != WindowState.Normal ||
            (!UsesManagedTitleBar && WindowStyle != WindowStyle.None) ||
            !IsLinuxCapabilityAvailable(PlatformWindowCapabilities.Resize) ||
            ResizeMode is not ResizeMode.CanResize and not ResizeMode.CanResizeWithGrip ||
            OverlayLayer.HasLightDismissPopups || ActiveExternalPopups.Count > 0) return 0;
        // At a constrained corner the remaining free edge can still resize its
        // own dimension. Use the same edge for the cursor and native request.
        return GetMacOSResizeEdge(point, new Size(Width, Height)) & (int)_linuxResizableEdges;
    }

    private Cursor? GetLinuxWindowChromeCursor(Point point) => GetLinuxResizeEdge(point) switch
    {
        1 or 2 => Cursors.SizeNS,
        4 or 8 => Cursors.SizeWE,
        5 or 10 => Cursors.SizeNWSE,
        6 or 9 => Cursors.SizeNESW,
        _ => null,
    };

    private void RefreshLinuxRenderingAvailability()
    {
        if (!OperatingSystem.IsLinux() || _platformWindow == null) return;
        bool suspended = _platformWindow.IsRenderingSuspended;
        if (_linuxRenderingSuspended == suspended) return;
        _linuxRenderingSuspended = suspended;
        UpdateRenderableRegistration();
        if (!suspended) RequestFullInvalidation();
    }
    private PlatformWindowCapabilities _linuxWindowCapabilities = PlatformWindowCapabilities.All;

    private bool IsLinuxCapabilityAvailable(PlatformWindowCapabilities capability) =>
        !OperatingSystem.IsLinux() || _platformWindow == null || (_linuxWindowCapabilities & capability) != 0;

    private void RefreshLinuxWindowCapabilities()
    {
        if (!OperatingSystem.IsLinux() || _platformWindow == null) return;
        // Dependency-property getters reapply coercion. Capture the effective
        // value before replacing the capability snapshot, then publish its
        // change so a previously restricted binding can reach the native window.
        var oldState = WindowState;
        _linuxWindowCapabilities = _platformWindow.Capabilities;
        var newState = WindowState;
        if (oldState == newState) return;

        bool wasSyncing = _isSyncingWindowState;
        _isSyncingWindowState = newState == _platformWindow.GetState();
        try
        {
            OnPropertyChanged(new DependencyPropertyChangedEventArgs(WindowStateProperty, oldState, newState));
        }
        finally { _isSyncingWindowState = wasSyncing; }
    }

    private static object? CoercePlatformWindowState(DependencyObject d, object? value)
    {
        if (!OperatingSystem.IsLinux() || d is not Window window || window._platformWindow == null ||
            window._isSyncingWindowState || value is not WindowState state ||
            (window._linuxWindowCapabilities & PlatformWindowCapabilities.StateRequestsRestricted) == 0)
            return value;
        var capability = state switch
        {
            WindowState.Minimized => PlatformWindowCapabilities.Minimize,
            WindowState.Maximized => PlatformWindowCapabilities.Maximize,
            WindowState.FullScreen => PlatformWindowCapabilities.FullScreen,
            _ => PlatformWindowCapabilities.All,
        };
        return state == WindowState.Normal || window.IsLinuxCapabilityAvailable(capability)
            ? value : window._platformWindow.GetState();
    }

    // Keep the application's requested style intact. xdg-decoration can choose
    // either provider, and core xdg-shell always needs application chrome.
    private bool UsesManagedTitleBar => OperatingSystem.IsLinux() &&
        _linuxDecorationMode != PlatformWindowDecorationMode.Unspecified
        ? _linuxDecorationMode == PlatformWindowDecorationMode.Client &&
          (TitleBarStyle == WindowTitleBarStyle.Custom || WindowStyle != WindowStyle.None)
        : TitleBarStyle == WindowTitleBarStyle.Custom;

    private void RefreshLinuxWindowChrome()
    {
        if (!OperatingSystem.IsLinux() || _platformWindow == null) return;
        _linuxDecorationMode = _platformWindow.DecorationMode;
        bool changesTitleBar = UsesManagedTitleBar != (TitleBar != null);
        if (UsesManagedTitleBar && TitleBar == null)
            CreateTitleBar();
        else if (!UsesManagedTitleBar && TitleBar != null)
        {
            ClearTitleBarInteractionState();
            RemoveTitleBar();
        }
        ApplyTitleBarPresentation();
        InvalidateMeasure();
        RequestFullInvalidation();
        if (changesTitleBar) _linuxFocusVisibilityPending = true;
    }

    private bool RestoreLinuxFocusedElementVisibilityAfterLayout()
    {
        if (!_linuxFocusVisibilityPending) return false;
        _linuxFocusVisibilityPending = false;
        if (_isClosing || _managedTeardownStarted ||
            Keyboard.FocusedElement is not FrameworkElement focused ||
            !focused.IsVisible || !IsDescendantOf(focused, this)) return false;
        focused.BringIntoView();
        return !_isClosing && !_managedTeardownStarted && (!IsMeasureValid || !IsArrangeValid);
    }

    private bool IsLinuxInputTargetValid(UIElement element) => IsEnabled &&
        element.IsEnabled && element.IsVisible && !Jalium.UI.Styling.CssDisplayProperties.IsExitInert(element) &&
        (IsDescendantOf(element, this) || IsOwnedLinuxPopupInputTarget(element, this));

    private Func<bool> CaptureLinuxImeInputLifetime(UIElement target)
    {
        var platform = _platformWindow;
        nint handle = Handle;
        int generation = System.Threading.Volatile.Read(ref _renderLifecycleGeneration);
        long focus = Keyboard.FocusRevision;
        long focusIntent = FocusService.FocusIntentRevision;
        var dialog = ActiveContentDialog;
        Visual root = target;
        while (root.VisualParent is { } parent) root = parent;
        var route = InputRoute.Capture(target, root);
        return () => !_isClosing && !_managedTeardownStarted && IsEnabled && Visibility == Visibility.Visible &&
            ReferenceEquals(platform, _platformWindow) && handle == Handle &&
            generation == System.Threading.Volatile.Read(ref _renderLifecycleGeneration) && focus == Keyboard.FocusRevision &&
            focusIntent == FocusService.FocusIntentRevision &&
            ReferenceEquals(dialog, ActiveContentDialog) && InputRoute.IsCurrent(route) &&
            IsLinuxInputTargetValid(target) && ReferenceEquals(target, GetTextInputTarget()) &&
            InputMethod.GetIsInputMethodEnabled(target) && !InputMethod.GetIsInputMethodSuspended(target) &&
            target is IImeSupport { IsImeAllowed: true };
    }

    private bool IsOwnedLinuxPopupInputTarget(UIElement element, UIElement scope)
    {
        // External popup roots have their own visual tree. Follow their open
        // placement chain so menus and submenus keep the owner's keyboard scope.
        var visited = new HashSet<Popup>();
        UIElement? target = element;
        while (target != null && visited.Count < 64)
        {
            PopupRoot? root = null;
            for (Visual? ancestor = target; ancestor != null; ancestor = ancestor.VisualParent)
                if (ancestor is PopupRoot popupRoot) { root = popupRoot; break; }
            if (root == null || !root.OwnerPopup.IsOpen || !ActiveExternalPopups.Contains(root.OwnerPopup) ||
                !visited.Add(root.OwnerPopup)) return false;
            target = root.OwnerPopup.PlacementTarget;
            if (target != null && IsDescendantOf(target, scope)) return true;
        }
        return false;
    }

    private bool TryHandleLinuxWindowSystemKey(Key key, ModifierKeys modifiers, bool isRepeat)
    {
        if (!OperatingSystem.IsLinux() || _platformWindow == null ||
            !IsEnabled || Visibility != Visibility.Visible || _isClosing || _managedTeardownStarted ||
            modifiers != ModifierKeys.Alt) return false;
        if (key == Key.F4)
        {
            if (!isRepeat && IsLinuxCapabilityAvailable(PlatformWindowCapabilities.Close)) Close();
            return true;
        }
        if (key == Key.Space)
        {
            _linuxSuppressHandledSpace = true;
            // A compositor may leave these keys to the application. Use the
            // existing menu here so a protocol request that has no acknowledgement
            // cannot consume Alt+Space without providing an operable menu.
            if (!isRepeat && HasSystemMenu)
            {
                if (_fallbackSystemMenu is { IsOpen: true } menu) menu.CloseImmediately();
                else ShowFallbackSystemMenu(new Point(0, GetCurrentTitleBarHeightDip()));
            }
            return true;
        }
        return false;
    }

    private bool CanBeginLinuxKeyboardOperation(bool resize)
    {
        if (!OperatingSystem.IsLinux() || _platformWindow == null || !IsEnabled ||
            _isClosing || _managedTeardownStarted || WindowState != WindowState.Normal) return false;
        var operation = resize ? PlatformWindowKeyboardOperations.Resize : PlatformWindowKeyboardOperations.Move;
        var capability = resize ? PlatformWindowCapabilities.Resize : PlatformWindowCapabilities.Move;
        return (_platformWindow.KeyboardOperations & operation) != 0 && IsLinuxCapabilityAvailable(capability) &&
            (!resize || ((ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip) &&
                _platformWindow.ResizableEdges != PlatformWindowResizeEdges.None));
    }

    private void AddLinuxSystemMenuKeyboardOperations(ContextMenu menu)
    {
        if (!OperatingSystem.IsLinux()) return;
        Add("Move", false);
        Add("Resize", true);
        void Add(string header, bool resize)
        {
            var item = new MenuItem { Header = header, IsEnabled = CanBeginLinuxKeyboardOperation(resize) };
            item.Click += (_, _) =>
            {
                if (!menu.IsOpen || !CanBeginLinuxKeyboardOperation(resize)) return;
                // Closed is raised before the normal close animation ends.
                // Release the popup itself before the WM takes keyboard input.
                menu.CloseImmediately();
                Dispatcher.BeginInvokeCritical(() =>
                {
                    if (!CanBeginLinuxKeyboardOperation(resize)) return;
                    UIElement.ForceReleaseMouseCapture();
                    _platformWindow!.BeginKeyboardMoveResize(resize);
                });
            };
            menu.Items.Add(item);
        }
    }

    // Route custom chrome through the window manager while the original native
    // button grab is still active. Caption controls keep their routed input.
    private bool TryHandleLinuxWindowChrome(MouseButton button, Point point, int clickCount)
    {
        if (!OperatingSystem.IsLinux() || _platformWindow == null ||
            button != MouseButton.Left || !IsEnabled ||
            (!UsesManagedTitleBar && WindowStyle != WindowStyle.None) ||
            OverlayLayer.HasLightDismissPopups || ActiveExternalPopups.Count > 0)
            return false;

        int edge = GetLinuxResizeEdge(point);
        if (edge != 0)
        {
            UIElement.ForceReleaseMouseCapture();
            if (_platformWindow.BeginResizeDrag(edge)) return true;
        }

        if (WindowState == WindowState.FullScreen ||
            ComputeNcHitTestFromClientDip(point.X, point.Y, WindowState == WindowState.Maximized) != HTCAPTION)
            return false;

        if (clickCount == 2 && IsShowMaximizeButton &&
            (WindowState == WindowState.Maximized || IsLinuxCapabilityAvailable(PlatformWindowCapabilities.Maximize)) &&
            ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)
        {
            SetCurrentValue(WindowStateProperty,
                WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
            return true;
        }

        UIElement.ForceReleaseMouseCapture();
        return IsLinuxCapabilityAvailable(PlatformWindowCapabilities.Move) && _platformWindow.BeginMoveDrag();
    }
}
