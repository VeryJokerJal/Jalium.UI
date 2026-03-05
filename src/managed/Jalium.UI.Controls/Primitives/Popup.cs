using System.Runtime.InteropServices;
using Jalium.UI.Controls;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Specifies the placement of a Popup relative to its target element.
/// </summary>
public enum PlacementMode
{
    /// <summary>
    /// Popup is positioned at the bottom-left of the target element.
    /// </summary>
    Bottom,

    /// <summary>
    /// Popup is positioned centered over the target element.
    /// </summary>
    Center,

    /// <summary>
    /// Popup is positioned to the right of the target element.
    /// </summary>
    Right,

    /// <summary>
    /// Popup is positioned at the top-left of the target element.
    /// </summary>
    Top,

    /// <summary>
    /// Popup is positioned to the left of the target element.
    /// </summary>
    Left,

    /// <summary>
    /// Popup is positioned relative to the mouse cursor.
    /// </summary>
    Mouse,

    /// <summary>
    /// Position at the mouse pointer location.
    /// </summary>
    MousePoint,

    /// <summary>
    /// Popup is positioned relative to the top-left of the target element.
    /// </summary>
    Relative,

    /// <summary>
    /// Popup is positioned at the specified absolute position within the window.
    /// </summary>
    Absolute,

    /// <summary>
    /// Popup is positioned at the bottom-left of the target element,
    /// but repositioned if it would go off the window bounds.
    /// </summary>
    Custom
}

/// <summary>
/// Displays content on top of existing content (WinUI 3 style).
/// When content fits within the parent window, renders via OverlayLayer.
/// When content overflows (and ShouldConstrainToRootBounds is false),
/// creates a lightweight native window to render outside the parent window bounds.
/// </summary>
[ContentProperty("Child")]
public sealed partial class Popup : FrameworkElement
{
    private PopupRoot? _popupRoot;
    private OverlayLayer? _overlayLayer;
    private PopupWindow? _popupWindow;
    private Window? _parentWindow;
    private bool _isUsingExternalWindow;

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsOpen dependency property.
    /// </summary>
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(Popup),
            new PropertyMetadata(false, OnIsOpenChanged));

    /// <summary>
    /// Identifies the Child dependency property.
    /// </summary>
    public static readonly DependencyProperty ChildProperty =
        DependencyProperty.Register(nameof(Child), typeof(UIElement), typeof(Popup),
            new PropertyMetadata(null, OnChildChanged));

    /// <summary>
    /// Identifies the PlacementTarget dependency property.
    /// </summary>
    public static readonly DependencyProperty PlacementTargetProperty =
        DependencyProperty.Register(nameof(PlacementTarget), typeof(UIElement), typeof(Popup),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Placement dependency property.
    /// </summary>
    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.Register(nameof(Placement), typeof(PlacementMode), typeof(Popup),
            new PropertyMetadata(PlacementMode.Bottom, OnPlacementChanged));

    /// <summary>
    /// Identifies the HorizontalOffset dependency property.
    /// </summary>
    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.Register(nameof(HorizontalOffset), typeof(double), typeof(Popup),
            new PropertyMetadata(0.0, OnOffsetChanged));

    /// <summary>
    /// Identifies the VerticalOffset dependency property.
    /// </summary>
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.Register(nameof(VerticalOffset), typeof(double), typeof(Popup),
            new PropertyMetadata(0.0, OnOffsetChanged));

    /// <summary>
    /// Identifies the StaysOpen dependency property.
    /// When false, the popup closes when the user clicks outside of it.
    /// </summary>
    public static readonly DependencyProperty StaysOpenProperty =
        DependencyProperty.Register(nameof(StaysOpen), typeof(bool), typeof(Popup),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the IsLightDismissEnabled dependency property.
    /// WinUI 3 style: when true, the popup closes when the user clicks outside of it.
    /// This is the inverse of StaysOpen.
    /// </summary>
    public static readonly DependencyProperty IsLightDismissEnabledProperty =
        DependencyProperty.Register(nameof(IsLightDismissEnabled), typeof(bool), typeof(Popup),
            new PropertyMetadata(false, OnIsLightDismissEnabledChanged));

    /// <summary>
    /// Identifies the OverflowStrategy dependency property.
    /// Controls how the popup handles content that would overflow window bounds.
    /// </summary>
    public static readonly DependencyProperty OverflowStrategyProperty =
        DependencyProperty.Register(nameof(OverflowStrategy), typeof(PopupOverflowStrategy), typeof(Popup),
            new PropertyMetadata(PopupOverflowStrategy.AutoFlip));

    /// <summary>
    /// Identifies the ShouldConstrainToRootBounds dependency property.
    /// When false (default, WinUI 3 style), the popup can render outside the window bounds
    /// by using a separate native window. When true, the popup is always constrained
    /// to the parent window bounds (overlay mode only).
    /// </summary>
    public static readonly DependencyProperty ShouldConstrainToRootBoundsProperty =
        DependencyProperty.Register(nameof(ShouldConstrainToRootBounds), typeof(bool), typeof(Popup),
            new PropertyMetadata(false));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets whether the popup is open.
    /// </summary>
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the content of the popup.
    /// </summary>
    public UIElement? Child
    {
        get => (UIElement?)GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    /// <summary>
    /// Gets or sets the element relative to which the popup is positioned.
    /// </summary>
    public UIElement? PlacementTarget
    {
        get => (UIElement?)GetValue(PlacementTargetProperty);
        set => SetValue(PlacementTargetProperty, value);
    }

    /// <summary>
    /// Gets or sets how the popup is positioned.
    /// </summary>
    public PlacementMode Placement
    {
        get => (PlacementMode)GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal offset from the placement position.
    /// </summary>
    public double HorizontalOffset
    {
        get => (double)GetValue(HorizontalOffsetProperty);
        set => SetValue(HorizontalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical offset from the placement position.
    /// </summary>
    public double VerticalOffset
    {
        get => (double)GetValue(VerticalOffsetProperty);
        set => SetValue(VerticalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the popup stays open when it loses focus.
    /// If false, the popup will close when clicking outside of it.
    /// </summary>
    public bool StaysOpen
    {
        get => (bool)GetValue(StaysOpenProperty);
        set => SetValue(StaysOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets whether light dismiss is enabled (WinUI 3 style).
    /// When true, the popup closes when clicking outside of it.
    /// This is the inverse of <see cref="StaysOpen"/>.
    /// </summary>
    public bool IsLightDismissEnabled
    {
        get => (bool)GetValue(IsLightDismissEnabledProperty);
        set => SetValue(IsLightDismissEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets how the popup handles content that would overflow window bounds.
    /// </summary>
    public PopupOverflowStrategy OverflowStrategy
    {
        get => (PopupOverflowStrategy)GetValue(OverflowStrategyProperty);
        set => SetValue(OverflowStrategyProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the popup is constrained to parent window bounds.
    /// When false (default, WinUI 3 style), overflowing content renders in a separate native window.
    /// When true, content is always clamped to the parent window bounds.
    /// </summary>
    public bool ShouldConstrainToRootBounds
    {
        get => (bool)GetValue(ShouldConstrainToRootBoundsProperty);
        set => SetValue(ShouldConstrainToRootBoundsProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the popup is opened.
    /// </summary>
    public event EventHandler? Opened;

    /// <summary>
    /// Occurs when the popup is closed.
    /// </summary>
    public event EventHandler? Closed;

    #endregion

    #region Property Changed Callbacks

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Popup popup)
        {
            if ((bool)e.NewValue)
                popup.OpenPopup();
            else
                popup.ClosePopup();
        }
    }

    private static void OnChildChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Popup popup && popup._popupRoot != null && popup.IsOpen)
        {
            popup.ClosePopup();
            popup.OpenPopup();
        }
    }

    private static void OnPlacementChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Popup popup && popup.IsOpen)
            popup.UpdatePosition();
    }

    private static void OnOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Popup popup && popup.IsOpen)
            popup.UpdatePosition();
    }

    private static void OnIsLightDismissEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Popup popup)
        {
            popup.StaysOpen = !(bool)e.NewValue;
        }
    }

    #endregion

    #region Open / Close

    private void OpenPopup()
    {
        if (_popupRoot != null) return;

        var child = Child;
        if (child == null) return;

        _parentWindow = GetParentWindow();
        if (_parentWindow == null) return;

        // Prepare full popup subtree before measuring.
        // Popup children are measured before attachment, so style/template/bindings must be ready now.
        PreparePopupSubtree(child);

        // Force fresh layout when re-opening: child may have been detached
        // from a previous PopupRoot and its IsMeasureValid is stale
        InvalidateSubtree(child);

        // Measure child to determine popup size
        var popupSize = MeasurePopupChild(child);

        // Calculate position in window-local coordinates
        var windowLocalPos = CalculateWindowLocalPosition(popupSize);
        var windowSize = new Size(_parentWindow.ActualWidth, _parentWindow.ActualHeight);

        // Apply AutoFlip if enabled (based on window bounds)
        var adjustedPos = ApplyAutoFlip(windowLocalPos, popupSize, windowSize);

        // Detach child from any existing visual parent before wrapping in PopupRoot.
        // This handles cases where the child was previously attached to another tree
        // (e.g., ToolTip reuse, or implicit style application adding to a container).
        if (child.VisualParent != null)
        {
            child.DetachFromVisualParent();
        }

        // Create PopupRoot wrapper
        _popupRoot = new PopupRoot(this, child, isLightDismiss: !StaysOpen);
        _popupRoot.Width = popupSize.Width;
        _popupRoot.Height = popupSize.Height;

        // Decide: overlay or external window?
        if (!ShouldConstrainToRootBounds)
        {
            // Check if popup would overflow the window bounds
            bool overflowsWindow = WouldOverflowWindow(adjustedPos, popupSize, windowSize);

            // Even if within window bounds, check if popup would be outside
            // the screen working area (behind taskbar, etc.)
            bool overflowsScreen = false;
            if (!overflowsWindow)
            {
                var screenPos = WindowLocalToScreen(adjustedPos);
                var workArea = GetWorkingArea();
                // screenPos and workArea are physical pixels; convert popupSize to physical
                var dpiScale = _parentWindow!.DpiScale;
                var physPopupW = popupSize.Width * dpiScale;
                var physPopupH = popupSize.Height * dpiScale;
                overflowsScreen = screenPos.Y + physPopupH > workArea.Bottom
                    || screenPos.Y < workArea.Top
                    || screenPos.X + physPopupW > workArea.Right
                    || screenPos.X < workArea.Left;
            }

            if (overflowsWindow || overflowsScreen)
            {
                OpenAsExternalWindow(adjustedPos, popupSize);
            }
            else
            {
                OpenAsOverlay(adjustedPos, popupSize, windowSize);
            }
        }
        else
        {
            OpenAsOverlay(adjustedPos, popupSize, windowSize);
        }

        // Subscribe to parent window moves for repositioning
        _parentWindow.LocationChanged += OnParentWindowLocationChanged;

        Opened?.Invoke(this, EventArgs.Empty);
    }

    private void OpenAsOverlay(Point position, Size popupSize, Size windowSize)
    {
        _isUsingExternalWindow = false;
        _overlayLayer = _parentWindow!.OverlayLayer;

        // Clamp to window bounds for overlay mode
        position = ClampToWindow(position, popupSize, windowSize);

        Canvas.SetLeft(_popupRoot!, position.X);
        Canvas.SetTop(_popupRoot!, position.Y);

        _overlayLayer.AddPopupRoot(_popupRoot!);

        // Force a full invalidation so the overlay content is rendered immediately,
        // even if CompositionTarget is throttling between-frame InvalidateWindow calls.
        _parentWindow.RequestFullInvalidation();
        _parentWindow.InvalidateWindow();
    }

    private void OpenAsExternalWindow(Point windowLocalPos, Size popupSize)
    {
        _isUsingExternalWindow = true;

        // Convert window-local to screen coordinates
        var screenPos = WindowLocalToScreen(windowLocalPos);

        // Apply auto-flip based on screen working area (respects taskbar)
        screenPos = ApplyScreenAutoFlip(screenPos, popupSize);

        _popupWindow = new PopupWindow(_parentWindow!, _popupRoot!);
        var dpiScale = _parentWindow!.DpiScale;
        _popupWindow.Show(
            (int)screenPos.X, (int)screenPos.Y,
            (int)(popupSize.Width * dpiScale), (int)(popupSize.Height * dpiScale));

        // Register with parent window for light dismiss
        if (!_parentWindow!.ActiveExternalPopups.Contains(this))
        {
            _parentWindow.ActiveExternalPopups.Add(this);
        }
    }

    private void ClosePopup()
    {
        if (_popupRoot == null) return;

        if (_isUsingExternalWindow)
        {
            _popupWindow?.Dispose();
            _popupWindow = null;
            while (_parentWindow?.ActiveExternalPopups.Remove(this) == true)
            {
            }
        }
        else if (_overlayLayer != null)
        {
            _overlayLayer.RemovePopupRoot(_popupRoot);
        }

        // Detach event subscriptions
        _popupRoot.Detach();
        _popupRoot = null;
        _isUsingExternalWindow = false;

        if (_parentWindow != null)
        {
            _parentWindow.LocationChanged -= OnParentWindowLocationChanged;
            _parentWindow = null;
        }
        _overlayLayer = null;

        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void OnParentWindowLocationChanged(object? sender, EventArgs e)
    {
        UpdatePosition();
    }

    /// <summary>
    /// Updates the position of the popup.
    /// </summary>
    public void UpdatePosition()
    {
        if (Child == null || _popupRoot == null || _parentWindow == null)
            return;

        var popupSize = new Size(_popupRoot.Width, _popupRoot.Height);
        var windowLocalPos = CalculateWindowLocalPosition(popupSize);
        var windowSize = new Size(_parentWindow.ActualWidth, _parentWindow.ActualHeight);
        var adjustedPos = ApplyAutoFlip(windowLocalPos, popupSize, windowSize);

        if (_isUsingExternalWindow && _popupWindow != null)
        {
            var screenPos = WindowLocalToScreen(adjustedPos);
            screenPos = ApplyScreenAutoFlip(screenPos, popupSize);
            var dpiScale = _parentWindow!.DpiScale;
            _popupWindow.MoveTo(
                (int)screenPos.X, (int)screenPos.Y,
                (int)(popupSize.Width * dpiScale), (int)(popupSize.Height * dpiScale));
        }
        else if (_overlayLayer != null)
        {
            adjustedPos = ClampToWindow(adjustedPos, popupSize, windowSize);
            Canvas.SetLeft(_popupRoot, adjustedPos.X);
            Canvas.SetTop(_popupRoot, adjustedPos.Y);
            _overlayLayer.InvalidateVisual();
        }
    }

    #endregion

    #region Position Calculation

    private Point CalculateWindowLocalPosition(Size popupSize)
    {
        var target = PlacementTarget ?? this;
        var targetWindowBounds = GetElementWindowBounds(target);

        double x = 0, y = 0;

        switch (Placement)
        {
            case PlacementMode.Bottom:
                x = targetWindowBounds.X;
                y = targetWindowBounds.Y + targetWindowBounds.Height;
                break;

            case PlacementMode.Top:
                x = targetWindowBounds.X;
                y = targetWindowBounds.Y - popupSize.Height;
                break;

            case PlacementMode.Left:
                x = targetWindowBounds.X - popupSize.Width;
                y = targetWindowBounds.Y;
                break;

            case PlacementMode.Right:
                x = targetWindowBounds.X + targetWindowBounds.Width;
                y = targetWindowBounds.Y;
                break;

            case PlacementMode.Center:
                x = targetWindowBounds.X + (targetWindowBounds.Width - popupSize.Width) / 2;
                y = targetWindowBounds.Y + (targetWindowBounds.Height - popupSize.Height) / 2;
                break;

            case PlacementMode.Relative:
                x = targetWindowBounds.X;
                y = targetWindowBounds.Y;
                break;

            case PlacementMode.Absolute:
                x = 0;
                y = 0;
                break;

            case PlacementMode.Mouse:
            case PlacementMode.MousePoint:
                if (_parentWindow != null && _parentWindow.Handle != nint.Zero)
                {
                    GetCursorPos(out var cursorPt);
                    var clientPt = new POINT { X = cursorPt.X, Y = cursorPt.Y };
                    ScreenToClient(_parentWindow.Handle, ref clientPt);
                    // ScreenToClient returns physical pixels, convert to DIPs
                    var dpiScale = _parentWindow.DpiScale;
                    x = clientPt.X / dpiScale;
                    y = clientPt.Y / dpiScale;
                }
                break;

            case PlacementMode.Custom:
                x = targetWindowBounds.X;
                y = targetWindowBounds.Y + targetWindowBounds.Height;
                break;
        }

        x += HorizontalOffset;
        y += VerticalOffset;

        return new Point(x, y);
    }

    private Point ApplyAutoFlip(Point position, Size popupSize, Size windowSize)
    {
        if (OverflowStrategy != PopupOverflowStrategy.AutoFlip)
            return position;

        var target = PlacementTarget ?? this;
        var targetBounds = GetElementWindowBounds(target);

        // Check bottom overflow → flip to top
        if (position.Y + popupSize.Height > windowSize.Height && Placement == PlacementMode.Bottom)
        {
            double flippedY = targetBounds.Y - popupSize.Height;
            if (flippedY >= 0)
                position = new Point(position.X, flippedY);
        }

        // Check top overflow → flip to bottom
        if (position.Y < 0 && Placement == PlacementMode.Top)
        {
            double flippedY = targetBounds.Y + targetBounds.Height;
            if (flippedY + popupSize.Height <= windowSize.Height)
                position = new Point(position.X, flippedY);
        }

        // Check right overflow → shift left
        if (position.X + popupSize.Width > windowSize.Width)
        {
            position = new Point(Math.Max(0, windowSize.Width - popupSize.Width), position.Y);
        }

        // Check left overflow → shift right
        if (position.X < 0)
        {
            position = new Point(0, position.Y);
        }

        return position;
    }

    private static bool WouldOverflowWindow(Point position, Size popupSize, Size windowSize)
    {
        return position.X < 0
            || position.Y < 0
            || position.X + popupSize.Width > windowSize.Width
            || position.Y + popupSize.Height > windowSize.Height;
    }

    private static Point ClampToWindow(Point position, Size popupSize, Size windowSize)
    {
        return new Point(
            Math.Clamp(position.X, 0, Math.Max(0, windowSize.Width - popupSize.Width)),
            Math.Clamp(position.Y, 0, Math.Max(0, windowSize.Height - popupSize.Height)));
    }

    private Point ApplyScreenAutoFlip(Point screenPos, Size popupSize)
    {
        var workArea = GetWorkingArea();

        // screenPos and workArea are in physical pixels; convert popupSize to physical
        var dpiScale = _parentWindow!.DpiScale;
        var physPopupW = popupSize.Width * dpiScale;
        var physPopupH = popupSize.Height * dpiScale;

        // Get target element's screen position for flipping
        var target = PlacementTarget ?? this;
        var targetWindowBounds = GetElementWindowBounds(target);
        var targetScreenTopLeft = WindowLocalToScreen(new Point(targetWindowBounds.X, targetWindowBounds.Y));
        var physTargetH = targetWindowBounds.Height * dpiScale;

        // Check bottom overflow → flip to top of target
        if (screenPos.Y + physPopupH > workArea.Bottom &&
            (Placement == PlacementMode.Bottom || Placement == PlacementMode.Custom))
        {
            double flippedY = targetScreenTopLeft.Y - physPopupH;
            if (flippedY >= workArea.Top)
                screenPos = new Point(screenPos.X, flippedY);
        }

        // Check top overflow → flip to bottom of target
        if (screenPos.Y < workArea.Top && Placement == PlacementMode.Top)
        {
            double flippedY = targetScreenTopLeft.Y + physTargetH;
            if (flippedY + physPopupH <= workArea.Bottom)
                screenPos = new Point(screenPos.X, flippedY);
        }

        // Clamp X to working area
        if (screenPos.X + physPopupW > workArea.Right)
            screenPos = new Point(Math.Max(workArea.Left, workArea.Right - physPopupW), screenPos.Y);
        if (screenPos.X < workArea.Left)
            screenPos = new Point(workArea.Left, screenPos.Y);

        // Final Y clamp to working area
        if (screenPos.Y + physPopupH > workArea.Bottom)
            screenPos = new Point(screenPos.X, Math.Max(workArea.Top, workArea.Bottom - physPopupH));
        if (screenPos.Y < workArea.Top)
            screenPos = new Point(screenPos.X, workArea.Top);

        return screenPos;
    }

    private Rect GetWorkingArea()
    {
        var monitor = MonitorFromWindow(_parentWindow!.Handle, MONITOR_DEFAULTTONEAREST);
        MONITORINFO info = new() { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (GetMonitorInfo(monitor, ref info))
        {
            return new Rect(
                info.rcWork.left, info.rcWork.top,
                info.rcWork.right - info.rcWork.left,
                info.rcWork.bottom - info.rcWork.top);
        }
        return new Rect(0, 0, 1920, 1080);
    }

    private Point WindowLocalToScreen(Point windowLocal)
    {
        // Input is DIPs — convert to physical pixels before ClientToScreen
        var dpiScale = _parentWindow!.DpiScale;
        var pt = new POINT { X = (int)(windowLocal.X * dpiScale), Y = (int)(windowLocal.Y * dpiScale) };
        ClientToScreen(_parentWindow!.Handle, ref pt);
        return new Point(pt.X, pt.Y);
    }

    private double GetAutomaticPopupMaxHeight()
    {
        if (_parentWindow == null)
            return double.PositiveInfinity;

        var dpiScale = _parentWindow.DpiScale;
        if (double.IsNaN(dpiScale) || double.IsInfinity(dpiScale) || dpiScale <= 0)
            dpiScale = 1.0;

        var windowHeight = _parentWindow.ActualHeight > 0 ? _parentWindow.ActualHeight : _parentWindow.Height;
        if (double.IsNaN(windowHeight) || double.IsInfinity(windowHeight) || windowHeight <= 0)
            windowHeight = double.PositiveInfinity;

        var workArea = GetWorkingArea();
        var workAreaHeight = workArea.Height > 0 ? workArea.Height / dpiScale : double.PositiveInfinity;

        var maxHeight = Math.Min(windowHeight, workAreaHeight);
        if (double.IsInfinity(maxHeight))
            maxHeight = windowHeight;
        if (double.IsInfinity(maxHeight))
            maxHeight = workAreaHeight;

        if (double.IsNaN(maxHeight) || maxHeight <= 0 || double.IsInfinity(maxHeight))
            return double.PositiveInfinity;

        // Keep popup slightly away from monitor/window edges.
        return Math.Max(20, maxHeight - 8);
    }

    #endregion

    #region Helpers

    private Size MeasurePopupChild(UIElement child)
    {
        // Resolve width constraints on Popup itself.
        var popupExplicitWidth = !double.IsNaN(Width) && !double.IsInfinity(Width) && Width > 0 ? Width : double.NaN;
        var popupMinWidth = MinWidth > 0 && !double.IsNaN(MinWidth) && !double.IsInfinity(MinWidth) ? MinWidth : 0;
        var popupMaxWidth = !double.IsNaN(MaxWidth) && !double.IsInfinity(MaxWidth) && MaxWidth > 0 ? MaxWidth : double.PositiveInfinity;

        // Resolve height constraints on Popup itself.
        var popupExplicitHeight = !double.IsNaN(Height) && !double.IsInfinity(Height) && Height > 0 ? Height : double.NaN;
        var popupMinHeight = MinHeight > 0 && !double.IsNaN(MinHeight) && !double.IsInfinity(MinHeight) ? MinHeight : 20;
        var hasExplicitPopupMaxHeight = !double.IsNaN(MaxHeight) && !double.IsInfinity(MaxHeight) && MaxHeight > 0;
        var popupMaxHeight = hasExplicitPopupMaxHeight ? MaxHeight : double.PositiveInfinity;

        // If caller did not provide explicit height/max height, cap to screen/window work area.
        // This keeps long menus/dropdowns reachable without manual MaxHeight.
        if (!hasExplicitPopupMaxHeight && double.IsNaN(popupExplicitHeight))
        {
            var autoMaxHeight = GetAutomaticPopupMaxHeight();
            if (!double.IsNaN(autoMaxHeight) && !double.IsInfinity(autoMaxHeight) && autoMaxHeight > 0)
            {
                popupMaxHeight = autoMaxHeight;
            }
        }

        // Keep global popup sizing content-driven by default.
        // Controls that need width matching (e.g., ComboBox dropdown) should set
        // Popup.Width/MinWidth/MaxWidth explicitly when opening.
        var minWidth = popupMinWidth;
        if (!double.IsInfinity(popupMaxWidth) && popupMaxWidth > 0)
            minWidth = Math.Min(minWidth, popupMaxWidth);

        // Measure unconstrained to avoid stretching star layouts to an arbitrary large width.
        child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var childSize = child is FrameworkElement fe ? fe.DesiredSize : new Size(100, 100);

        var maxReasonableSize = 4096.0;
        var childWidth = double.IsInfinity(childSize.Width) || childSize.Width > maxReasonableSize
            ? Math.Max(100, minWidth) : childSize.Width;
        var childHeight = double.IsInfinity(childSize.Height) || childSize.Height > maxReasonableSize
            ? 200.0 : childSize.Height;

        var width = childWidth;
        var height = childHeight;

        // If child has explicit Width/Height set, use those
        if (child is FrameworkElement childFe)
        {
            if (!double.IsNaN(childFe.Width) && childFe.Width > 0)
                width = childFe.Width;
            if (!double.IsNaN(childFe.Height) && childFe.Height > 0)
                height = childFe.Height;
            if (childFe.MinWidth > 0)
                minWidth = Math.Max(minWidth, childFe.MinWidth);
            if (childFe.MinHeight > 0)
                popupMinHeight = Math.Max(popupMinHeight, childFe.MinHeight);
            if (!double.IsNaN(childFe.MaxWidth) && !double.IsInfinity(childFe.MaxWidth) && childFe.MaxWidth > 0)
                popupMaxWidth = Math.Min(popupMaxWidth, childFe.MaxWidth);
            if (!double.IsNaN(childFe.MaxHeight) && !double.IsInfinity(childFe.MaxHeight) && childFe.MaxHeight > 0)
                popupMaxHeight = Math.Min(popupMaxHeight, childFe.MaxHeight);
        }

        if (!double.IsNaN(popupExplicitWidth))
            width = popupExplicitWidth;
        if (!double.IsNaN(popupExplicitHeight))
            height = popupExplicitHeight;

        if (!double.IsInfinity(popupMaxWidth) && popupMaxWidth > 0)
            minWidth = Math.Min(minWidth, popupMaxWidth);
        if (!double.IsInfinity(popupMaxHeight) && popupMaxHeight > 0)
            popupMinHeight = Math.Min(popupMinHeight, popupMaxHeight);

        width = double.IsInfinity(popupMaxWidth) || popupMaxWidth <= 0
            ? Math.Max(minWidth, width)
            : Math.Clamp(width, minWidth, popupMaxWidth);

        height = double.IsInfinity(popupMaxHeight) || popupMaxHeight <= 0
            ? Math.Max(popupMinHeight, height)
            : Math.Clamp(height, popupMinHeight, popupMaxHeight);

        return new Size(width, height);
    }

    private Rect GetElementWindowBounds(UIElement element)
    {
        // Accumulate offsets up to the window
        var bounds = element.VisualBounds;
        var current = element.VisualParent;
        while (current != null)
        {
            if (current is Window)
                break;
            if (current is UIElement uiElement)
            {
                var parentBounds = uiElement.VisualBounds;
                bounds = new Rect(
                    bounds.X + parentBounds.X,
                    bounds.Y + parentBounds.Y,
                    bounds.Width,
                    bounds.Height);
            }
            current = current.VisualParent;
        }

        return bounds;
    }

    private static void InvalidateSubtree(UIElement element)
    {
        element.InvalidateMeasure();
        element.InvalidateArrange();
        for (int i = 0; i < element.VisualChildrenCount; i++)
        {
            if (element.GetVisualChild(i) is UIElement child)
                InvalidateSubtree(child);
        }
    }

    private static void PreparePopupSubtree(UIElement element)
    {
        if (element is FrameworkElement fe)
        {
            fe.ApplyImplicitStyleIfNeeded();
            fe.ReactivateBindings();
        }

        for (int i = 0; i < element.VisualChildrenCount; i++)
        {
            if (element.GetVisualChild(i) is UIElement child)
                PreparePopupSubtree(child);
        }
    }

    private Window? GetParentWindow()
    {
        // First try from this popup
        Visual? current = this;
        while (current != null)
        {
            if (current is Window window)
                return window;
            current = current.VisualParent;
        }

        // If not found, try from PlacementTarget
        current = PlacementTarget;
        while (current != null)
        {
            if (current is Window window)
                return window;
            current = current.VisualParent;
        }

        // Fallback: use Application.Current.MainWindow
        // This handles cases where the visual tree is not fully connected
        // (e.g., programmatically created Popups for ToolTips)
        return Jalium.UI.Application.Current?.MainWindow;
    }

    #endregion

    #region Win32 Interop

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ScreenToClient(nint hWnd, ref POINT lpPoint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left, top, right, bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    #endregion
}
