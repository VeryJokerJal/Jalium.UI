using System.Runtime.InteropServices;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using Jalium.UI.Interop.Win32;
using static Jalium.UI.Interop.Win32.Win32Constants;
using static Jalium.UI.Interop.Win32.Win32Methods;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Specifies the placement of a Popup relative to its target element.
/// </summary>
public enum PlacementMode
{
    /// <summary>
    /// Popup is positioned at the bottom-left of the target element.
    /// </summary>
    Absolute = 0,

    /// <summary>
    /// Popup is positioned relative to the top-left of the target element.
    /// </summary>
    Relative = 1,

    Bottom = 2,

    /// <summary>
    /// Popup is positioned centered over the target element.
    /// </summary>
    Center = 3,

    /// <summary>
    /// Popup is positioned to the right of the target element.
    /// </summary>
    Right = 4,

    /// <summary>
    /// Popup is positioned at an absolute point.
    /// </summary>
    AbsolutePoint = 5,

    /// <summary>
    /// Popup is positioned at a point relative to the target.
    /// </summary>
    RelativePoint = 6,

    /// <summary>
    /// Popup is positioned at the top-left of the target element.
    /// </summary>
    Mouse = 7,

    /// <summary>
    /// Popup is positioned to the left of the target element.
    /// </summary>
    MousePoint = 8,

    /// <summary>
    /// Popup is positioned relative to the mouse cursor.
    /// </summary>
    Left = 9,

    /// <summary>
    /// Position at the mouse pointer location.
    /// </summary>
    Top = 10,

    /// <summary>
    /// Popup is positioned relative to the top-left of the target element.
    /// </summary>
    Custom = 11,
}

/// <summary>
/// Displays content on top of existing content (WinUI 3 style).
/// When content fits within the parent window, renders via OverlayLayer.
/// When content overflows (and ShouldConstrainToRootBounds is false),
/// creates a lightweight native window to render outside the parent window bounds.
/// </summary>
[Jalium.UI.Markup.ContentProperty("Child")]
public partial class Popup : FrameworkElement, Jalium.UI.Input.IInputRouteParentProvider
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.PopupAutomationPeer(this);
    }

    /// <summary>
    /// The declaration-site element is only an anchor for placement and state.
    /// Popup content receives input through <see cref="PopupRoot"/> (or the
    /// external popup window) after opening, so this placeholder must never
    /// cover siblings that render underneath it.
    /// </summary>
    protected override HitTestResult? HitTestCore(Point point) => null;

    private PopupRoot? _popupRoot;
    private OverlayLayer? _overlayLayer;
    private PopupWindow? _popupWindow;
    private Window? _parentWindow;
    private bool _isUsingExternalWindow;
    private DispatcherTimer? _openAnimationTimer;
    private PopupRoot? _pendingContentSizeRoot;

    /// <summary>
    /// Gap kept between the cursor hot spot and a popup flipped above it (DIPs).
    /// </summary>
    private const double CursorFlipGap = 2.0;

    /// <summary>
    /// Cursor-anchored placements derive their position from the pointer, so their
    /// overflow handling has to protect the hot spot instead of the placement target.
    /// </summary>
    private bool IsCursorAnchoredPlacement =>
        Placement is PlacementMode.Mouse or PlacementMode.MousePoint;

    /// <summary>
    /// Gets whether the hosted popup subtree owns an active pointer capture.
    /// </summary>
    internal bool HasPointerCaptureWithin => _popupRoot?.HasPointerCaptureWithin == true;

    internal Visual? GetMacOSInputRouteParent(PopupRoot root)
    {
        // Standalone PopupWindow hosts keep their existing physical route.
        if (!ReferenceEquals(_popupRoot, root)) return root.VisualParent;
        if (!IsOpen || _parentWindow == null) return null;
        if (_isUsingExternalWindow)
            return _macOSPopupWindow != null && IsMacOSExternalHostCurrent(_macOSPopupWindow)
                ? this : root.VisualParent;
        if (_overlayLayer == null || !ReferenceEquals(root.VisualParent, _overlayLayer)) return null;

        return this;
    }

    Visual? Jalium.UI.Input.IInputRouteParentProvider.InputRouteParent =>
        OperatingSystem.IsMacOS() && _popupRoot != null ? GetMacOSInputOwnerParent() : VisualParent as Visual;

    private Visual? GetMacOSInputOwnerParent()
    {
        if (!IsOpen || _parentWindow == null) return null;
        if (PlacementTarget is not { } anchor)
        {
            var declaration = VisualParent as Visual;
            return declaration == null ? (Visual?)_overlayLayer ?? _parentWindow.MacOSInputOwner :
                ReferenceEquals(ResolveOwningWindow(declaration), _parentWindow) ? declaration : null;
        }
        var owner = ResolveOwningWindow(anchor);
        if (!ReferenceEquals(owner, _parentWindow) || !anchor.IsEnabled || !anchor.IsVisible ||
            Jalium.UI.Styling.CssDisplayProperties.IsExitInert(anchor)) return null;
        return anchor;
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsOpen dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(Popup),
            new PropertyMetadata(false, OnIsOpenChanged));

    /// <summary>
    /// Identifies the Child dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ChildProperty =
        DependencyProperty.Register(nameof(Child), typeof(UIElement), typeof(Popup),
            new PropertyMetadata(null, OnChildChanged));

    /// <summary>
    /// Identifies the PlacementTarget dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty PlacementTargetProperty =
        DependencyProperty.Register(nameof(PlacementTarget), typeof(UIElement), typeof(Popup),
            new PropertyMetadata(null, (d, _) => ((Popup)d).OnMacOSPlacementTargetChanged()));

    /// <summary>
    /// Identifies the Placement dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.Register(nameof(Placement), typeof(PlacementMode), typeof(Popup),
            new PropertyMetadata(PlacementMode.Bottom, OnPlacementChanged));

    /// <summary>
    /// Identifies the HorizontalOffset dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.Register(nameof(HorizontalOffset), typeof(double), typeof(Popup),
            new PropertyMetadata(0.0, OnOffsetChanged));

    /// <summary>
    /// Identifies the VerticalOffset dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.Register(nameof(VerticalOffset), typeof(double), typeof(Popup),
            new PropertyMetadata(0.0, OnOffsetChanged));

    /// <summary>
    /// Identifies the StaysOpen dependency property.
    /// When false, the popup closes when the user clicks outside of it.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty StaysOpenProperty =
        DependencyProperty.Register(nameof(StaysOpen), typeof(bool), typeof(Popup),
            new PropertyMetadata(true, (d, _) => ((Popup)d).OnMacOSStaysOpenChanged()));

    /// <summary>
    /// Identifies the IsLightDismissEnabled dependency property.
    /// WinUI 3 style: when true, the popup closes when the user clicks outside of it.
    /// This is the inverse of StaysOpen.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsLightDismissEnabledProperty =
        DependencyProperty.Register(nameof(IsLightDismissEnabled), typeof(bool), typeof(Popup),
            new PropertyMetadata(false, OnIsLightDismissEnabledChanged));

    /// <summary>
    /// Identifies the OverflowStrategy dependency property.
    /// Controls how the popup handles content that would overflow window bounds.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty OverflowStrategyProperty =
        DependencyProperty.Register(nameof(OverflowStrategy), typeof(PopupOverflowStrategy), typeof(Popup),
            new PropertyMetadata(PopupOverflowStrategy.AutoFlip));

    /// <summary>
    /// Identifies the ShouldConstrainToRootBounds dependency property.
    /// When false (default, WinUI 3 style), the popup can render outside the window bounds
    /// by using a separate native window. When true, the popup is always constrained
    /// to the parent window bounds (overlay mode only).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ShouldConstrainToRootBoundsProperty =
        DependencyProperty.Register(nameof(ShouldConstrainToRootBounds), typeof(bool), typeof(Popup),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the <see cref="PreferExternalWindow"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PreferExternalWindowProperty =
        DependencyProperty.Register(nameof(PreferExternalWindow), typeof(bool), typeof(Popup),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the <see cref="CustomPopupPlacementCallback"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty CustomPopupPlacementCallbackProperty =
        DependencyProperty.Register(
            nameof(CustomPopupPlacementCallback),
            typeof(CustomPopupPlacementCallback),
            typeof(Popup),
            new PropertyMetadata(null, OnOffsetChanged));

    /// <summary>
    /// Identifies the <see cref="PlacementRectangle"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty PlacementRectangleProperty =
        DependencyProperty.Register(
            nameof(PlacementRectangle),
            typeof(Rect),
            typeof(Popup),
            new PropertyMetadata(Rect.Empty, OnOffsetChanged));

    /// <summary>
    /// Identifies the <see cref="PopupAnimation"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty PopupAnimationProperty =
        DependencyProperty.Register(
            nameof(PopupAnimation),
            typeof(PopupAnimation),
            typeof(Popup),
            new PropertyMetadata(PopupAnimation.None, null, CoercePopupAnimation),
            value => value is PopupAnimation animation && Enum.IsDefined(animation));

    /// <summary>
    /// Identifies the <see cref="AllowsTransparency"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty AllowsTransparencyProperty =
        DependencyProperty.Register(
            nameof(AllowsTransparency),
            typeof(bool),
            typeof(Popup),
            new PropertyMetadata(false, OnAllowsTransparencyChanged));

    private static readonly DependencyPropertyKey HasDropShadowPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(HasDropShadow),
            typeof(bool),
            typeof(Popup),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the read-only <see cref="HasDropShadow"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty HasDropShadowProperty =
        HasDropShadowPropertyKey.DependencyProperty;

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets whether the popup is open.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty)!;
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the content of the popup.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public UIElement? Child
    {
        get => (UIElement?)GetValue(ChildProperty);
        set => SetValue(ChildProperty, value);
    }

    /// <summary>
    /// Gets or sets the element relative to which the popup is positioned.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public UIElement? PlacementTarget
    {
        get => (UIElement?)GetValue(PlacementTargetProperty);
        set => SetValue(PlacementTargetProperty, value);
    }

    /// <summary>
    /// Gets or sets how the popup is positioned.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public PlacementMode Placement
    {
        get => (PlacementMode)GetValue(PlacementProperty)!;
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal offset from the placement position.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double HorizontalOffset
    {
        get => (double)GetValue(HorizontalOffsetProperty)!;
        set => SetValue(HorizontalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical offset from the placement position.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double VerticalOffset
    {
        get => (double)GetValue(VerticalOffsetProperty)!;
        set => SetValue(VerticalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the popup stays open when it loses focus.
    /// If false, the popup will close when clicking outside of it.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public bool StaysOpen
    {
        get => (bool)GetValue(StaysOpenProperty)!;
        set => SetValue(StaysOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets whether light dismiss is enabled (WinUI 3 style).
    /// When true, the popup closes when clicking outside of it.
    /// This is the inverse of <see cref="StaysOpen"/>.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsLightDismissEnabled
    {
        get => (bool)GetValue(IsLightDismissEnabledProperty)!;
        set => SetValue(IsLightDismissEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets how the popup handles content that would overflow window bounds.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public PopupOverflowStrategy OverflowStrategy
    {
        get => (PopupOverflowStrategy)GetValue(OverflowStrategyProperty)!;
        set => SetValue(OverflowStrategyProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the popup is constrained to parent window bounds.
    /// When false (default, WinUI 3 style), overflowing content renders in a separate native window.
    /// When true, content is always clamped to the parent window bounds.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool ShouldConstrainToRootBounds
    {
        get => (bool)GetValue(ShouldConstrainToRootBoundsProperty)!;
        set => SetValue(ShouldConstrainToRootBoundsProperty, value);
    }

    /// <summary>
    /// 偏好用独立的原生窗口（PopupWindow）渲染，而不是等"装不下"时才升级。
    /// <para>
    /// 默认行为是先按 overlay 走，只有溢出父窗口/屏幕工作区才切外飞窗口；这对真正的右键菜单
    /// （ContextMenu / MenuFlyout）不合适 —— context menu 按 Win32/WPF/WinUI 惯例总是独立顶层窗口，
    /// 才能正确处理"菜单贴到任意位置 / 不受父窗口裁切 / 自身 light dismiss"。设为 <c>true</c> 后
    /// <see cref="OpenPopup"/> 会在支持原生弹层的 Windows、Linux 和 macOS 窗口中跳过 overflow
    /// 检查；没有原生宿主时仍回退到 overlay。
    /// </para>
    /// <para>
    /// 与 <see cref="ShouldConstrainToRootBounds"/> 互斥：constrain=true 时不允许外飞，本属性被忽略。
    /// </para>
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool PreferExternalWindow
    {
        get => (bool)GetValue(PreferExternalWindowProperty)!;
        set => SetValue(PreferExternalWindowProperty, value);
    }

    /// <summary>
    /// Gets or sets the callback used when <see cref="Placement"/> is
    /// <see cref="PlacementMode.Custom"/>.
    /// </summary>
    public CustomPopupPlacementCallback? CustomPopupPlacementCallback
    {
        get => (CustomPopupPlacementCallback?)GetValue(CustomPopupPlacementCallbackProperty);
        set => SetValue(CustomPopupPlacementCallbackProperty, value);
    }

    /// <summary>
    /// Gets or sets the rectangle relative to the placement target used to position the popup.
    /// </summary>
    public Rect PlacementRectangle
    {
        get => (Rect)(GetValue(PlacementRectangleProperty) ?? Rect.Empty);
        set => SetValue(PlacementRectangleProperty, value);
    }

    /// <summary>
    /// Gets or sets the animation applied the next time the popup opens.
    /// </summary>
    public PopupAnimation PopupAnimation
    {
        get => (PopupAnimation)(GetValue(PopupAnimationProperty) ?? PopupAnimation.None);
        set => SetValue(PopupAnimationProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the popup can render transparent content.
    /// </summary>
    public bool AllowsTransparency
    {
        get => (bool)(GetValue(AllowsTransparencyProperty) ?? false);
        set => SetValue(AllowsTransparencyProperty, value);
    }

    /// <summary>
    /// Gets whether the popup should render a system drop shadow.
    /// </summary>
    public bool HasDropShadow => (bool)(GetValue(HasDropShadowProperty) ?? false);

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

    /// <summary>
    /// Connects a popup to a child that exposes the standard popup placement properties.
    /// </summary>
    /// <remarks>
    /// The one-way bindings intentionally mirror WPF's root-popup hookup.  In particular,
    /// <see cref="IsOpen"/> is bound last so all placement state is ready before the child
    /// can cause the popup to open.
    /// </remarks>
    public static void CreateRootPopup(Popup popup, UIElement child)
    {
        ArgumentNullException.ThrowIfNull(popup);
        ArgumentNullException.ThrowIfNull(child);

        if (child.VisualParent != null)
        {
            throw new InvalidOperationException("The popup child already has a visual parent.");
        }

        static Binding OneWay(UIElement source, string path) => new(path)
        {
            Mode = BindingMode.OneWay,
            Source = source
        };

        popup.SetBinding(PlacementTargetProperty, OneWay(child, nameof(PlacementTarget)));
        popup.Child = child;
        popup.SetBinding(VerticalOffsetProperty, OneWay(child, nameof(VerticalOffset)));
        popup.SetBinding(HorizontalOffsetProperty, OneWay(child, nameof(HorizontalOffset)));
        popup.SetBinding(PlacementRectangleProperty, OneWay(child, nameof(PlacementRectangle)));
        popup.SetBinding(PlacementProperty, OneWay(child, nameof(Placement)));
        popup.SetBinding(StaysOpenProperty, OneWay(child, nameof(StaysOpen)));
        popup.SetBinding(CustomPopupPlacementCallbackProperty, OneWay(child, nameof(CustomPopupPlacementCallback)));
        popup.SetBinding(IsOpenProperty, OneWay(child, nameof(IsOpen)));
    }

    #region Property Changed Callbacks

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Popup popup)
        {
            if ((bool)e.NewValue!)
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
            popup.SetCurrentValue(StaysOpenProperty, !popup.IsLightDismissEnabled);
        }
    }

    private static void OnAllowsTransparencyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var popup = (Popup)d;
        popup.SetValue(HasDropShadowPropertyKey, Jalium.UI.SystemParameters.DropShadow && (bool)e.NewValue!);
        popup.CoerceValue(PopupAnimationProperty);
    }

    private static object? CoercePopupAnimation(DependencyObject d, object? baseValue)
    {
        return ((Popup)d).AllowsTransparency
            ? baseValue
            : PopupAnimation.None;
    }

    #endregion

    #region Open / Close

    /// <summary>
    /// Ensures an already-logically-open popup has acquired a visual host.
    /// </summary>
    /// <remarks>
    /// Template bindings may set <see cref="IsOpen"/> while their expanded template is
    /// still detached. The property change is valid, but <see cref="OpenPopup"/> cannot
    /// resolve a parent window at that point. Owners call this once template attachment
    /// is complete so the missed first attempt does not leave a logically open popup blank.
    /// </remarks>
    internal void EnsureOpen()
    {
        if (IsOpen && _popupRoot == null)
        {
            OpenPopup();
        }
    }

    private void OpenPopup()
    {
        if (OperatingSystem.IsMacOS() && _macOSClosingRoot != null) return;
        if (OperatingSystem.IsMacOS() && HasClosingMacOSPopupAncestor())
        { SetCurrentValue(IsOpenProperty, false); return; }
        if (_popupRoot != null) return;

        var child = Child;
        if (child == null) return;

        _parentWindow = GetParentWindow();
        if (_parentWindow == null) return;
        if (OperatingSystem.IsMacOS())
        {
            if (!_parentWindow.IsMacOSPopupInputOwnerAlive || !_parentWindow.IsEnabled || !_parentWindow.IsVisible ||
                !IsMacOSPopupAnchorValid(_parentWindow))
            { _parentWindow = null; SetCurrentValue(IsOpenProperty, false); return; }
            _macOSFocusBeforeOpen = Jalium.UI.Input.Keyboard.FocusedElement as UIElement;
        }

        // Attach the child to its PopupRoot before preparing or measuring it.  The root
        // is the inheritance bridge for the detached popup tree (most importantly for
        // DataContext).  Measuring the raw child first used to size data-bound popup
        // content as though its Text values were still empty; the bindings activated
        // only after PopupRoot was created, leaving the now-taller content clipped by
        // the stale host height.
        if (child.VisualParent != null)
        {
            child.DetachFromVisualParent();
        }

        _popupRoot = new PopupRoot(this, child, isLightDismiss: !StaysOpen);

        // Prepare the fully inherited popup subtree before measuring.
        PreparePopupSubtree(child);

        // Force fresh layout when re-opening: child may have been detached
        // from a previous PopupRoot and its IsMeasureValid is stale
        InvalidateSubtree(_popupRoot);

        // Measure child to determine popup size
        var popupSize = MeasurePopupChild(child);

        // Calculate position in window-local coordinates
        var windowLocalPos = CalculateWindowLocalPosition(popupSize);
        var windowSize = new Size(_parentWindow.ActualWidth, _parentWindow.ActualHeight);

        bool supportsExternalPopup = Platform.PlatformFactory.IsWindows || Platform.PlatformFactory.IsLinux ||
            OperatingSystem.IsMacOS() && CanCreateMacOSPopupHost;
        bool useExternalWindow = ResolveHostPlacement(
            windowLocalPos, popupSize, windowSize, supportsExternalPopup, out var adjustedPos);

        _popupRoot.Width = popupSize.Width;
        _popupRoot.Height = popupSize.Height;

        // Constrained popups always remain overlays. Other popups promote only
        // when explicitly preferred or when the resolved placement really lands
        // outside the owner client area.
        if (useExternalWindow)
        {
            OpenAsExternalWindow(windowLocalPos, popupSize);
        }
        else
        {
            OpenAsOverlay(adjustedPos, popupSize, windowSize);
        }

        // Subscribe to parent window moves for repositioning
        _parentWindow.LocationChanged += OnParentWindowLocationChanged;

        if (OperatingSystem.IsMacOS()) ObserveMacOSPopupOwner();

        StartOpenAnimation();
        OnOpened(EventArgs.Empty);
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

        RequestHostRender();
    }

    internal void RequestHostRender()
    {
        IWindowHost? host = _isUsingExternalWindow
            ? (IWindowHost?)_macOSPopupWindow ?? _popupWindow
            : _parentWindow;

        if (host == null)
        {
            return;
        }

        // Popup open/close/slide/fade animations mutate opacity and render offset.
        // A full host invalidation avoids stale translucent pixels when the popup is
        // rendered as an overlay inside the parent window's retained back buffer.
        host.RequestFullInvalidation();
        host.InvalidateWindow();
    }

    private void OpenAsExternalWindow(Point windowLocalPos, Size popupSize)
    {
        _isUsingExternalWindow = true;

        // Convert window-local to screen coordinates
        var screenPos = WindowLocalToScreen(windowLocalPos);

        // Win32 needs client-to-screen placement and explicit work-area
        // clamping. Linux receives parent-relative coordinates; xdg_positioner
        // applies compositor constraints and X11 uses the translated owner
        // origin, so applying a second global clamp here would corrupt it.
        if (Platform.PlatformFactory.IsWindows || OperatingSystem.IsMacOS())
            screenPos = ApplyScreenAutoFlip(screenPos, popupSize);

        if (OperatingSystem.IsMacOS())
        {
            OpenAsMacOSExternalWindow(screenPos, popupSize);
            return;
        }

        _popupWindow = new PopupWindow(_parentWindow!, _popupRoot!);
        var dpiScale = _parentWindow!.DpiScale;
        _popupWindow.Show(
            ToNativeHostOffset(screenPos.X), ToNativeHostOffset(screenPos.Y),
            ToNativeHostSize(popupSize.Width, dpiScale), ToNativeHostSize(popupSize.Height, dpiScale));

        // Register with parent window for light dismiss
        if (!_parentWindow!.ActiveExternalPopups.Contains(this))
        {
            _parentWindow.ActiveExternalPopups.Add(this);
        }
    }

    private void ClosePopup()
    {
        if (OperatingSystem.IsMacOS()) CloseMacOSPopup();
        else ClosePopupCore();
    }

    private void ClosePopupCore(Action? beforeClosed = null)
    {
        if (_popupRoot == null) return;

        if (OperatingSystem.IsMacOS()) StopObservingMacOSPopupOwner();

        StopOpenAnimation(resetVisualState: true);
        _pendingContentSizeRoot = null;
        MacOSPopupWindowHost? retiredMacOSHost = null;

        if (_isUsingExternalWindow)
        {
            retiredMacOSHost = _macOSPopupWindow;
            _macOSPopupWindow = null;
            if (retiredMacOSHost != null) retiredMacOSHost.Content = null;
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
        RequestHostRender();
        _isUsingExternalWindow = false;

        if (_parentWindow != null)
        {
            _parentWindow.LocationChanged -= OnParentWindowLocationChanged;
            _parentWindow = null;
        }
        _overlayLayer = null;
        SetIsMouseOver(false);

        try { beforeClosed?.Invoke(); }
        finally { retiredMacOSHost?.CloseMacOSPopupHost(); }
        OnClosed(EventArgs.Empty);
    }

    /// <summary>
    /// Queues a content-driven host resize after the current popup layout pass.
    /// </summary>
    /// <remarks>
    /// Popup content commonly arrives after opening (for example, async search
    /// results). The initial host size must not remain frozen at the empty-state
    /// measurement. Deferring avoids resizing a native popup window re-entrantly
    /// from inside its measure pass while still coalescing a burst of child changes.
    /// </remarks>
    internal void QueueContentSizeUpdate(PopupRoot root)
    {
        if (!IsOpen || !ReferenceEquals(_popupRoot, root) ||
            ReferenceEquals(_pendingContentSizeRoot, root))
        {
            return;
        }

        _pendingContentSizeRoot = root;
        Dispatcher.CurrentDispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(_pendingContentSizeRoot, root))
            {
                return;
            }

            _pendingContentSizeRoot = null;
            if (!IsOpen || !ReferenceEquals(_popupRoot, root))
            {
                return;
            }

            UpdateContentSize();
        });
    }

    private void UpdateContentSize()
    {
        var child = Child;
        var root = _popupRoot;
        if (child == null || root == null || _parentWindow == null)
        {
            return;
        }

        var contentSize = MeasurePopupChild(child);
        if (Math.Abs(root.Width - contentSize.Width) < 0.01 &&
            Math.Abs(root.Height - contentSize.Height) < 0.01)
        {
            return;
        }

        root.Width = contentSize.Width;
        root.Height = contentSize.Height;
        UpdatePosition();
    }

    /// <summary>
    /// Called after the popup has opened.
    /// </summary>
    protected virtual void OnOpened(EventArgs e)
    {
        Opened?.Invoke(this, e);
    }

    /// <summary>
    /// Called after the popup has closed.
    /// </summary>
    protected virtual void OnClosed(EventArgs e)
    {
        Closed?.Invoke(this, e);
    }

    private void StartOpenAnimation()
    {
        StopOpenAnimation(resetVisualState: true);

        var root = _popupRoot;
        var animation = PopupAnimation;
        if (root == null || !AllowsTransparency || animation == PopupAnimation.None)
        {
            return;
        }

        var translate = new TranslateTransform();
        var startOpacity = animation == PopupAnimation.Fade ? 0.0 : 1.0;
        var distance = animation == PopupAnimation.Scroll ? 6.0 : 10.0;

        switch (Placement)
        {
            case PlacementMode.Top:
                translate.Y = distance;
                break;
            case PlacementMode.Left:
                translate.X = distance;
                break;
            case PlacementMode.Right:
                translate.X = -distance;
                break;
            default:
                translate.Y = -distance;
                break;
        }

        if (animation == PopupAnimation.Fade)
        {
            translate.X = 0;
            translate.Y = 0;
        }

        var startX = translate.X;
        var startY = translate.Y;
        var started = DateTime.UtcNow;
        var duration = animation == PopupAnimation.Fade
            ? TimeSpan.FromMilliseconds(150)
            : TimeSpan.FromMilliseconds(120);

        root.Opacity = startOpacity;
        root.RenderTransform = translate;

        _openAnimationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _openAnimationTimer.Tick += OnTick;
        _openAnimationTimer.Start();
        RequestHostRender();

        void OnTick(object? sender, EventArgs e)
        {
            if (!ReferenceEquals(root, _popupRoot))
            {
                StopOpenAnimation(resetVisualState: false);
                return;
            }

            var progress = Math.Clamp(
                (DateTime.UtcNow - started).TotalMilliseconds / duration.TotalMilliseconds,
                0.0,
                1.0);

            // Smoothstep avoids an abrupt stop while remaining deterministic and allocation-free.
            var eased = progress * progress * (3.0 - (2.0 * progress));
            root.Opacity = startOpacity + ((1.0 - startOpacity) * eased);
            translate.X = startX * (1.0 - eased);
            translate.Y = startY * (1.0 - eased);
            RequestHostRender();

            if (progress >= 1.0)
            {
                StopOpenAnimation(resetVisualState: true);
            }
        }
    }

    private void StopOpenAnimation(bool resetVisualState)
    {
        _openAnimationTimer?.Stop();
        _openAnimationTimer = null;

        if (resetVisualState && _popupRoot != null)
        {
            _popupRoot.Opacity = 1.0;
            _popupRoot.RenderTransform = null;
        }
    }

    private void OnParentWindowLocationChanged(object? sender, EventArgs e)
    {
        UpdatePosition();
    }

    private long _placementUpdateRevision;

    /// <summary>
    /// Updates the position of the popup.
    /// </summary>
    public void UpdatePosition()
    {
        var root = _popupRoot;
        var parent = _parentWindow;
        if (Child == null || root == null || parent == null) return;
        long update = unchecked(++_placementUpdateRevision);
        long ownerRevision = _macOSOwnerRevision;
        bool Current() => IsOpen && update == _placementUpdateRevision &&
            ownerRevision == _macOSOwnerRevision && ReferenceEquals(root, _popupRoot) &&
            ReferenceEquals(parent, _parentWindow);

        var popupSize = new Size(root.Width, root.Height);
        var windowLocalPos = CalculateWindowLocalPosition(popupSize);
        if (!Current()) return;
        var windowSize = new Size(parent.ActualWidth, parent.ActualHeight);
        var supportsExternalPopup = Platform.PlatformFactory.IsWindows || Platform.PlatformFactory.IsLinux ||
            OperatingSystem.IsMacOS() && CanCreateMacOSPopupHost;
        if (!Current()) return;
        var shouldPromote = ResolveHostPlacement(
            windowLocalPos, popupSize, windowSize, supportsExternalPopup, out var overlayPos);
        // Custom placement and screen queries can close, retarget or reopen
        // this Popup. Never apply an old size/position to the replacement root.
        if (!Current()) return;

        if (_isUsingExternalWindow && _macOSPopupWindow is { } macHost)
        {
            var screen = ApplyScreenAutoFlip(WindowLocalToScreen(windowLocalPos), popupSize);
            if (!Current() || !ReferenceEquals(macHost, _macOSPopupWindow)) return;
            macHost.MoveTo(screen, popupSize);
        }
        else if (_isUsingExternalWindow && _popupWindow is { } nativeHost)
        {
            var screenPos = WindowLocalToScreen(windowLocalPos);
            // Linux popup coordinates remain owner-relative; Win32 receives
            // global coordinates with screen work-area constraints applied.
            if (Platform.PlatformFactory.IsWindows)
                screenPos = ApplyScreenAutoFlip(screenPos, popupSize);
            var dpiScale = parent.DpiScale;
            if (!Current() || !ReferenceEquals(nativeHost, _popupWindow)) return;
            nativeHost.MoveTo(
                ToNativeHostOffset(screenPos.X), ToNativeHostOffset(screenPos.Y),
                ToNativeHostSize(popupSize.Width, dpiScale), ToNativeHostSize(popupSize.Height, dpiScale));
        }
        else if (shouldPromote)
        {
            _overlayLayer?.RemovePopupRoot(root);
            if (!Current()) return;
            _overlayLayer = null;
            OpenAsExternalWindow(windowLocalPos, popupSize);
        }
        else if (_overlayLayer is { } layer)
        {
            if (Canvas.GetLeft(root) == overlayPos.X && Canvas.GetTop(root) == overlayPos.Y) return;
            Canvas.SetLeft(root, overlayPos.X);
            if (!Current()) return;
            Canvas.SetTop(root, overlayPos.Y);
            if (!Current()) return;
            layer.InvalidateVisual();
            RequestHostRender();
        }
    }

    #endregion

    #region Position Calculation

    private Point CalculateWindowLocalPosition(Size popupSize)
    {
        var target = PlacementTarget ?? this;
        var targetWindowBounds = GetPlacementBounds(target);

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
            case PlacementMode.RelativePoint:
                x = targetWindowBounds.X;
                y = targetWindowBounds.Y;
                break;

            case PlacementMode.Absolute:
            case PlacementMode.AbsolutePoint:
                x = PlacementRectangle.IsEmpty ? 0 : PlacementRectangle.X;
                y = PlacementRectangle.IsEmpty ? 0 : PlacementRectangle.Y;
                break;

            case PlacementMode.Mouse:
            case PlacementMode.MousePoint:
                if (_parentWindow != null && _parentWindow.Handle != nint.Zero)
                {
                    if (OperatingSystem.IsWindows())
                    {
                        GetCursorPos(out var cursorPt);
                        var clientPt = new POINT { X = cursorPt.X, Y = cursorPt.Y };
                        ScreenToClient(_parentWindow.Handle, ref clientPt);
                        // ScreenToClient returns physical pixels, convert to DIPs
                        var dpiScale = _parentWindow.DpiScale;
                        x = clientPt.X / dpiScale;
                        y = clientPt.Y / dpiScale;
                    }
                    else
                    {
                        // user32 is unavailable and Window.Handle is a platform
                        // handle here (never zero), so this branch must not be
                        // treated as Windows-only by the handle check. The input
                        // pipeline already records the pointer position of the
                        // window that received the triggering event in
                        // window-local DIPs — exactly the coordinate space this
                        // method returns.
                        var mousePos = Jalium.UI.Input.Mouse.Position;
                        x = mousePos.X;
                        y = mousePos.Y;
                    }
                }
                break;

            case PlacementMode.Custom:
                var callback = CustomPopupPlacementCallback;
                if (callback != null)
                {
                    var placements = callback(
                        popupSize,
                        new Size(targetWindowBounds.Width, targetWindowBounds.Height),
                        new Point(HorizontalOffset, VerticalOffset));

                    if (placements is { Length: > 0 })
                    {
                        // Custom placement points are already responsible for applying the
                        // callback's offset argument, matching the WPF callback contract.
                        return new Point(
                            targetWindowBounds.X + placements[0].Point.X,
                            targetWindowBounds.Y + placements[0].Point.Y);
                    }
                }

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
        var targetBounds = GetPlacementBounds(target);

        // Cursor-anchored placements must never end up underneath the pointer.
        // Clamping them back into view would put popup pixels on the hot spot, so
        // the element that owns the tooltip loses hover, hides the tooltip, gets
        // MouseEnter again and the popup flickers open/closed forever. Flip above
        // the cursor instead, matching WPF.
        if (IsCursorAnchoredPlacement && position.Y + popupSize.Height > windowSize.Height)
        {
            double flippedY = position.Y - VerticalOffset - popupSize.Height - CursorFlipGap;
            if (flippedY >= 0)
                position = new Point(position.X, flippedY);
        }

        // Vertical flip: Bottom -> Top
        if (position.Y + popupSize.Height > windowSize.Height && Placement == PlacementMode.Bottom)
        {
            double flippedY = targetBounds.Y - popupSize.Height;
            if (flippedY >= 0)
                position = new Point(position.X, flippedY);
        }

        // Vertical flip: Top -> Bottom
        if (position.Y < 0 && Placement == PlacementMode.Top)
        {
            double flippedY = targetBounds.Y + targetBounds.Height;
            if (flippedY + popupSize.Height <= windowSize.Height)
                position = new Point(position.X, flippedY);
        }

        // Horizontal flip: Right -> left side of target
        if (position.X + popupSize.Width > windowSize.Width && Placement == PlacementMode.Right)
        {
            double flippedX = targetBounds.X - popupSize.Width;
            if (flippedX >= 0)
                position = new Point(flippedX, position.Y);
        }

        // Horizontal flip: Left -> right side of target
        if (position.X < 0 && Placement == PlacementMode.Left)
        {
            double flippedX = targetBounds.X + targetBounds.Width;
            if (flippedX + popupSize.Width <= windowSize.Width)
                position = new Point(flippedX, position.Y);
        }

        // Generic X shift for placements whose X derives from target.X (Bottom/Top/Custom/Relative/etc.)
        // keeps them inside the window when the popup is wider than expected.
        // Skip Right/Left placements: if their directional flip above failed, leave the position
        // overflowing so the caller can promote the popup to an External Window and render
        // beyond the owner window instead of clamping it back and clipping against the edge.
        if (Placement != PlacementMode.Right && Placement != PlacementMode.Left)
        {
            if (position.X + popupSize.Width > windowSize.Width)
            {
                position = new Point(Math.Max(0, windowSize.Width - popupSize.Width), position.Y);
            }

            if (position.X < 0)
            {
                position = new Point(0, position.Y);
            }
        }

        return position;
    }

    /// <summary>
    /// Reports whether the popup rectangle at <paramref name="position"/> lies entirely
    /// inside the owner client area.
    /// </summary>
    /// <remarks>
    /// A maximized window's client area ends exactly at the work-area edge, so a popup
    /// clamped against the taskbar lands flush with the owner bottom and the verdict is
    /// decided by rounding noise alone: the screen round trip truncates to physical pixels
    /// on the way out and rounds on the way back, worth up to half a physical pixel. The
    /// tolerance covers that with margin and deliberately biases the answer towards staying
    /// in the overlay — a popup one pixel short of the edge costs nothing, while a needless
    /// promotion brings back the flicker this policy exists to prevent.
    /// </remarks>
    private static bool FitsInsideWindow(Point position, Size popupSize, Size windowSize)
    {
        const double tolerance = 1.0;
        return position.X >= -tolerance
            && position.Y >= -tolerance
            && position.X + popupSize.Width <= windowSize.Width + tolerance
            && position.Y + popupSize.Height <= windowSize.Height + tolerance;
    }

    /// <summary>
    /// Pure host policy: a native popup window is required only when the resolved
    /// placement really leaves the owner client area.
    /// </summary>
    /// <param name="resolvedPosition">
    /// Owner-local position after every flip/clamp the platform can resolve up front.
    /// Hosts without screen-level resolution (Linux, headless) pass the requested
    /// position, which degrades this to a plain owner-bounds check.
    /// </param>
    internal static bool ShouldPromoteToExternalWindow(
        bool supportsExternalPopup,
        bool constrainToRootBounds,
        bool preferExternalWindow,
        Point resolvedPosition,
        Size popupSize,
        Size windowSize)
    {
        if (!supportsExternalPopup || constrainToRootBounds)
        {
            return false;
        }

        if (preferExternalWindow)
        {
            return true;
        }

        return !FitsInsideWindow(resolvedPosition, popupSize, windowSize);
    }

    /// <summary>
    /// Determines whether owner-relative bounds require a native popup host, applying this
    /// popup's own constrain/prefer policy on top of the shared rule.
    /// </summary>
    internal bool ShouldUseExternalWindowForBounds(
        Point position,
        Size popupSize,
        Size windowSize,
        bool supportsExternalPopup)
    {
        return ShouldPromoteToExternalWindow(
            supportsExternalPopup,
            ShouldConstrainToRootBounds,
            PreferExternalWindow,
            position,
            popupSize,
            windowSize);
    }

    /// <summary>
    /// Picks the popup host and returns the owner-local position the overlay path uses.
    /// </summary>
    /// <remarks>
    /// The invariant: a native popup window only pays off when the popup genuinely has to
    /// paint outside the owner client area. Near the taskbar or a screen edge the
    /// screen-level resolution (flip + work-area clamp) pushes the popup back inside the
    /// owner, and a separate HWND becomes actively harmful there — it is topmost and hit
    /// testable, so the moment it covers the cursor the owner receives WM_MOUSELEAVE, the
    /// tooltip hides, the cursor lands back on the owning element and it reopens: an endless
    /// flicker. Resolving the screen constraints *before* choosing the host removes that
    /// whole class of promotion, instead of judging overflow from an unclamped request.
    /// </remarks>
    private bool ResolveHostPlacement(
        Point requestedPosition,
        Size popupSize,
        Size windowSize,
        bool supportsExternalPopup,
        out Point overlayPosition)
    {
        // 锚点已经在一个外飞的 PopupWindow 里（级联子菜单、外飞 Flyout 里的下拉/提示）：Windows 上一律
        // 跟着外飞，不看装不装得下。退回主窗口覆盖层的话它会画在 topmost 的父弹窗 HWND 之下；更糟的是
        // 它的按下事件先经主窗口的输入分发器——那里把「外飞弹窗开着时落在主窗口里的按下」当 light
        // dismiss：父菜单被关、点击被吞、子菜单却留在原地还能继续点。
        // 只在 Windows 上强制：Wayland 的嵌套 xdg_popup 必须以最顶层的 grab 弹窗为 parent，而原生层目前
        // 一律挂在顶层窗口下，强制外飞会招来协议错误；Linux 仍按「装不下才外飞」。
        if (supportsExternalPopup
            && (Platform.PlatformFactory.IsWindows || OperatingSystem.IsMacOS())
            && !ShouldConstrainToRootBounds && IsAnchoredInsideExternalPopupWindow())
        {
            overlayPosition = requestedPosition;
            return true;
        }

        // Constrained popups never leave the owner, so their placement keeps using the
        // window-level flip exclusively — screen resolution would only add noise there.
        var resolvedPosition = !supportsExternalPopup || ShouldConstrainToRootBounds
            ? requestedPosition
            : ResolveScreenConstrainedPosition(requestedPosition, popupSize);

        if (ShouldUseExternalWindowForBounds(
                resolvedPosition, popupSize, windowSize, supportsExternalPopup))
        {
            overlayPosition = requestedPosition;
            return true;
        }

        // Window-level flip stays in charge of the overlay. It is an identity transform
        // whenever the screen resolution already moved the popup back inside the owner.
        overlayPosition = ClampToWindow(
            ApplyAutoFlip(resolvedPosition, popupSize, windowSize),
            popupSize,
            windowSize);
        return false;
    }

    /// <summary>
    /// Resolves the requested position against the monitor work area (flip + clamp) and
    /// converts it back to owner-local DIPs. Returns the input unchanged when no real HWND
    /// is available or the platform has no global screen coordinates, which degrades the
    /// caller's policy to an owner-bounds check.
    /// </summary>
    private Point ResolveScreenConstrainedPosition(Point requestedPosition, Size popupSize)
    {
        if ((!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) || _parentWindow is null ||
            _parentWindow.Handle == nint.Zero)
        {
            return requestedPosition;
        }

        var resolvedScreen = ApplyScreenAutoFlip(WindowLocalToScreen(requestedPosition), popupSize);
        return ScreenToWindowLocal(resolvedScreen);
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
        var targetWindowBounds = GetPlacementBounds(target);
        var targetScreenTopLeft = WindowLocalToScreen(new Point(targetWindowBounds.X, targetWindowBounds.Y));
        var physTargetW = targetWindowBounds.Width * dpiScale;
        var physTargetH = targetWindowBounds.Height * dpiScale;

        // Cursor-anchored placements flip above the pointer rather than being clamped onto
        // it — see the note in ApplyAutoFlip: a popup sitting on the hot spot costs the
        // owning element its hover state and makes tooltips flicker.
        if (IsCursorAnchoredPlacement && screenPos.Y + physPopupH > workArea.Bottom)
        {
            double cursorScreenY = screenPos.Y - (VerticalOffset * dpiScale);
            double flippedY = cursorScreenY - physPopupH - (CursorFlipGap * dpiScale);
            if (flippedY >= workArea.Top)
                screenPos = new Point(screenPos.X, flippedY);
        }

        // Vertical flip: Bottom -> Top of target
        if (screenPos.Y + physPopupH > workArea.Bottom &&
            (Placement == PlacementMode.Bottom || Placement == PlacementMode.Custom))
        {
            double flippedY = targetScreenTopLeft.Y - physPopupH;
            if (flippedY >= workArea.Top)
                screenPos = new Point(screenPos.X, flippedY);
        }

        // Vertical flip: Top -> Bottom of target
        if (screenPos.Y < workArea.Top && Placement == PlacementMode.Top)
        {
            double flippedY = targetScreenTopLeft.Y + physTargetH;
            if (flippedY + physPopupH <= workArea.Bottom)
                screenPos = new Point(screenPos.X, flippedY);
        }

        // Horizontal flip: Right -> left side of target on screen
        if (screenPos.X + physPopupW > workArea.Right && Placement == PlacementMode.Right)
        {
            double flippedX = targetScreenTopLeft.X - physPopupW;
            if (flippedX >= workArea.Left)
                screenPos = new Point(flippedX, screenPos.Y);
        }

        // Horizontal flip: Left -> right side of target on screen
        if (screenPos.X < workArea.Left && Placement == PlacementMode.Left)
        {
            double flippedX = targetScreenTopLeft.X + physTargetW;
            if (flippedX + physPopupW <= workArea.Right)
                screenPos = new Point(flippedX, screenPos.Y);
        }

        // Clamp X to working area (fallback when flipping to the opposite side still does not fit)
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
        if (OperatingSystem.IsMacOS() && _parentWindow != null)
        {
            var area = GetMacOSPopupWorkArea();
            var scale = _parentWindow.DpiScale;
            return new Rect(area.X * scale, area.Y * scale, area.Width * scale, area.Height * scale);
        }
        if (!OperatingSystem.IsWindows())
        {
            // Linux external-popup coordinates are intentionally owner-relative
            // (Wayland has no global desktop coordinates), and overlay popups
            // are clipped by this same client area. Keep overflow math in that
            // common physical coordinate space; the native Wayland positioner
            // applies output work-area constraints for external popups.
            var window = _parentWindow;
            if (window is null)
                return new Rect(0, 0, 1920, 1080);

            var dpiScale = window.DpiScale;
            if (double.IsNaN(dpiScale) || double.IsInfinity(dpiScale) || dpiScale <= 0)
                dpiScale = 1.0;

            var width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
            var height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
            if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0 ||
                double.IsNaN(height) || double.IsInfinity(height) || height <= 0)
            {
                return new Rect(0, 0, 1920, 1080);
            }

            return new Rect(0, 0, width * dpiScale, height * dpiScale);
        }

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
        if (OperatingSystem.IsMacOS())
        {
            var origin = _parentWindow.GetMacOSClientScreenOrigin();
            return new Point((origin.X + windowLocal.X) * dpiScale, (origin.Y + windowLocal.Y) * dpiScale);
        }
        if (!OperatingSystem.IsWindows())
        {
            // Degraded model matching GetWorkingArea above: treat the window
            // origin as the screen origin (physical pixels). Popups stay in the
            // overlay on these platforms, so window-relative math is sufficient.
            return new Point(pt.X, pt.Y);
        }

        ClientToScreen(_parentWindow!.Handle, ref pt);
        return new Point(pt.X, pt.Y);
    }

    /// <summary>
    /// Inverse of <see cref="WindowLocalToScreen"/>: physical screen pixels back to
    /// owner-local DIPs.
    /// </summary>
    private Point ScreenToWindowLocal(Point screenPhysical)
    {
        var dpiScale = _parentWindow!.DpiScale;
        if (double.IsNaN(dpiScale) || double.IsInfinity(dpiScale) || dpiScale <= 0)
            dpiScale = 1.0;

        var pt = new POINT
        {
            X = (int)Math.Round(screenPhysical.X),
            Y = (int)Math.Round(screenPhysical.Y)
        };

        if (OperatingSystem.IsMacOS())
        {
            var origin = _parentWindow.GetMacOSClientScreenOrigin();
            return new Point(screenPhysical.X / dpiScale - origin.X, screenPhysical.Y / dpiScale - origin.Y);
        }
        if (OperatingSystem.IsWindows())
        {
            ScreenToClient(_parentWindow.Handle, ref pt);
        }

        return new Point(pt.X / dpiScale, pt.Y / dpiScale);
    }

    /// <summary>
    /// Converts a DIP extent to the native popup host's physical pixel size, rounding up.
    /// </summary>
    /// <remarks>
    /// The host derives its layout slot back from <c>physical / dpi</c> and arranges the popup
    /// into it, so truncating leaves the slot a fraction of a pixel shorter than the content and
    /// the layout clip eats the last column and row — exactly the missing right and bottom
    /// border of an external popup. The overlay path stays in floating point end to end, which
    /// is why the same popup keeps its border when it renders inside the window. The extra pixel
    /// this rounding can add falls on the transparent margin and costs nothing.
    /// </remarks>
    internal static int ToNativeHostSize(double dipLength, double dpiScale)
    {
        if (double.IsNaN(dpiScale) || double.IsInfinity(dpiScale) || dpiScale <= 0)
        {
            dpiScale = 1.0;
        }

        var physical = dipLength * dpiScale;
        if (double.IsNaN(physical) || double.IsInfinity(physical) || physical <= 1)
        {
            return 1;
        }

        return (int)Math.Ceiling(physical);
    }

    /// <summary>
    /// Converts a physical screen coordinate to the native window origin, rounding down.
    /// </summary>
    /// <remarks>
    /// Paired with the size rounding above: truncation rounds towards zero, so on a monitor left
    /// of the primary one it would shift the origin right/down while the size grew, pushing
    /// content past the clamped work-area edge. Floor keeps "the origin never moves inwards" true
    /// for both signs.
    /// </remarks>
    internal static int ToNativeHostOffset(double screenPhysical)
    {
        if (double.IsNaN(screenPhysical) || double.IsInfinity(screenPhysical))
        {
            return 0;
        }

        return (int)Math.Floor(screenPhysical);
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

        var maxHeight = OperatingSystem.IsMacOS() && CanCreateMacOSPopupHost && !ShouldConstrainToRootBounds
            ? workAreaHeight : Math.Min(windowHeight, workAreaHeight);
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
            if (current is PopupWindow popupWindow)
            {
                var popupWindowBounds = popupWindow.GetBoundsInParentWindowDips();
                bounds = new Rect(
                    bounds.X + popupWindowBounds.X,
                    bounds.Y + popupWindowBounds.Y,
                    bounds.Width,
                    bounds.Height);
                break;
            }
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

    private Rect GetPlacementBounds(UIElement target)
    {
        if (OperatingSystem.IsMacOS() && _parentWindow != null)
        {
            // The popup and its anchor share the physical Window, including
            // anchors inside an independent native popup. Use the same affine
            // transform as rendering so scale, rotation, RTL and animation
            // offsets place the popup at the visible anchor.
            var local = PlacementRectangle;
            if (local.IsEmpty)
                local = new Rect(new Point(), target.RenderSize);
            if (target.TransformToVisual(_parentWindow) is { } transform)
                return transform.TransformBounds(local);
        }
        var targetBounds = GetElementWindowBounds(target);
        var rectangle = PlacementRectangle;
        if (rectangle.IsEmpty)
        {
            return targetBounds;
        }

        return new Rect(
            targetBounds.X + rectangle.X,
            targetBounds.Y + rectangle.Y,
            rectangle.Width,
            rectangle.Height);
    }

    private static void InvalidateSubtree(UIElement element)
    {
        element.InvalidateMeasure();
        element.InvalidateArrange();
        for (int i = 0; i < element.InternalVisualChildrenCount; i++)
        {
            if (element.InternalGetVisualChild(i) is UIElement child)
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

        for (int i = 0; i < element.InternalVisualChildrenCount; i++)
        {
            if (element.InternalGetVisualChild(i) is UIElement child)
                PreparePopupSubtree(child);
        }
    }

    private Window? GetParentWindow()
    {
        // 先从 Popup 自身向上找，再从 PlacementTarget 向上找。
        //
        // 级联场景（子菜单的 Popup 挂在一个已经外飞成独立 PopupWindow 的父菜单里）下，向上走 VisualParent
        // 不会遇到 Window，而是先遇到父菜单的 PopupWindow —— 它不是 Window，且自身没有通向 Window 的
        // VisualParent。必须经由 PopupWindow.OwnerWindow 解析到真正的顶层窗口，否则会一路走到 fallback 的
        // Application.Current.MainWindow：在主窗口里这恰好等于正确窗口（bug 隐身），但从第二个窗口打开时，
        // 就会按主窗口原点做 ClientToScreen，使子菜单整体偏移（偏移量 = 两窗口客户区原点之差）。
        return (OperatingSystem.IsMacOS() ? ResolveOwningWindow(PlacementTarget) : null)
            ?? ResolveOwningWindow(this)
            ?? ResolveOwningWindow(PlacementTarget)
            // Fallback: use Application.Current.MainWindow. This handles cases where the visual
            // tree is not fully connected (e.g., programmatically created Popups for ToolTips).
            ?? Jalium.UI.Application.Current?.MainWindow;
    }

    /// <summary>
    /// 从 <paramref name="start"/> 沿 VisualParent 向上解析所属的顶层 <see cref="Window"/>。
    /// 遇到 <see cref="PopupWindow"/>（外飞弹窗宿主，本身不是 Window）时返回它的
    /// <see cref="PopupWindow.OwnerWindow"/>，从而让嵌套在弹窗里的目标也能拿到正确的窗口原点。
    /// 找不到则返回 <see langword="null"/>，由调用方决定回退策略。
    /// </summary>
    private static Window? ResolveOwningWindow(Visual? start)
    {
        var current = start;
        while (current != null)
        {
            if (current is Window window)
                return window;
            if (current is PopupWindow popupWindow)
                return popupWindow.OwnerWindow;
            current = current.VisualParent;
        }

        return null;
    }

    /// <summary>
    /// 本弹窗的锚点（自身或 <see cref="PlacementTarget"/>）是否已经位于某个外飞的
    /// <see cref="PopupWindow"/> 之内——也就是说，它是一个「从外飞弹窗里再弹出来」的级联弹窗。
    /// 与 <see cref="ResolveOwningWindow"/> 同一条向上路径：先遇到 <see cref="Window"/> 算在窗口内，
    /// 先遇到 <see cref="PopupWindow"/> 算在外飞弹窗内。
    /// </summary>
    internal bool IsAnchoredInsideExternalPopupWindow()
    {
        return IsInsideExternalPopupWindow(this) || IsInsideExternalPopupWindow(PlacementTarget);
    }

    private static bool IsInsideExternalPopupWindow(Visual? start)
    {
        for (var current = start; current != null; current = current.VisualParent)
        {
            if (current is Window window)
                return OperatingSystem.IsMacOS() && window.IsMacOSPopupHost;
            if (current is PopupWindow)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 本弹窗是否是 <paramref name="root"/> 所属弹窗的祖先——即 <paramref name="root"/> 是从本弹窗的
    /// 内容里（直接或经由更多层弹窗）级联弹出来的。沿「弹窗根 → 其 Popup 的锚点 → 视觉祖先」逐层上溯，
    /// 每穿过一个 <see cref="PopupRoot"/> 就比对它的 OwnerPopup。
    /// </summary>
    /// <remarks>
    /// 供宿主窗口的输入分发器做链感知的 light dismiss：点击落在某个覆盖层弹窗里时，它的祖先外飞弹窗
    /// 不算「被点到外面」，不能关。
    /// </remarks>
    internal bool IsAncestorOfPopupRoot(PopupRoot root)
    {
        if (ReferenceEquals(root, _popupRoot))
            return false;

        var popup = root.OwnerPopup;
        // 防御环状 PlacementTarget（A 的锚点在 B 里、B 的锚点又指回 A）导致死循环。
        const int maxHops = 64;
        for (int hop = 0; hop < maxHops; hop++)
        {
            var ancestorRoot = FindAncestorPopupRoot(popup.PlacementTarget)
                ?? FindAncestorPopupRoot(popup);
            if (ancestorRoot == null)
                return false;
            if (ReferenceEquals(ancestorRoot.OwnerPopup, this))
                return true;

            popup = ancestorRoot.OwnerPopup;
        }

        return false;
    }

    private static PopupRoot? FindAncestorPopupRoot(Visual? start)
    {
        for (var current = start; current != null; current = current.VisualParent)
        {
            if (current is PopupRoot popupRoot)
                return popupRoot;
            if (current is Window)
                return null;
        }

        return null;
    }

    #endregion

}
