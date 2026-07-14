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
public partial class Popup : FrameworkElement
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.PopupAutomationPeer(this);
    }

    private PopupRoot? _popupRoot;
    private OverlayLayer? _overlayLayer;
    private PopupWindow? _popupWindow;
    private Window? _parentWindow;
    private bool _isUsingExternalWindow;
    private DispatcherTimer? _openAnimationTimer;

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
            new PropertyMetadata(null));

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
            new PropertyMetadata(true));

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
    /// <see cref="OpenPopup"/> 会跳过 overflow 检查直接走外飞窗口（仅 Windows 平台；其它平台仍回退
    /// 到 overlay）。
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
            popup.StaysOpen = !(bool)e.NewValue!;
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

        // 窗口级 AutoFlip 把 popup 夹回父窗口内（line 658-669 generic X clamp）；对 PreferExternalWindow
        // 的 context-menu 类 popup，这会破坏"贴鼠标 / 飞出窗口"的目标，所以跳过 —— 屏幕级 flip 由
        // OpenAsExternalWindow → ApplyScreenAutoFlip 兜底，保证不会跑到屏幕外或被任务栏遮住。
        bool supportsExternalPopup = Platform.PlatformFactory.IsWindows || Platform.PlatformFactory.IsLinux;
        var skipWindowAutoFlip = PreferExternalWindow && !ShouldConstrainToRootBounds && supportsExternalPopup;
        var adjustedPos = skipWindowAutoFlip ? windowLocalPos : ApplyAutoFlip(windowLocalPos, popupSize, windowSize);

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
        //   ShouldConstrainToRootBounds=true  → 永远 overlay（强约束）
        //   PreferExternalWindow=true         → 直接外飞（context menu 语义；前提是 Windows）
        //   否则                              → 看是否溢出父窗口/屏幕工作区，溢出才升级到外飞
        if (ShouldConstrainToRootBounds)
        {
            OpenAsOverlay(adjustedPos, popupSize, windowSize);
        }
        else if (PreferExternalWindow && supportsExternalPopup)
        {
            OpenAsExternalWindow(adjustedPos, popupSize);
        }
        else
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

            if ((overflowsWindow || overflowsScreen) && supportsExternalPopup)
            {
                OpenAsExternalWindow(adjustedPos, popupSize);
            }
            else
            {
                OpenAsOverlay(adjustedPos, popupSize, windowSize);
            }
        }

        // Subscribe to parent window moves for repositioning
        _parentWindow.LocationChanged += OnParentWindowLocationChanged;

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
            ? _popupWindow
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
        if (Platform.PlatformFactory.IsWindows)
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

        StopOpenAnimation(resetVisualState: true);

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
        RequestHostRender();
        _isUsingExternalWindow = false;

        if (_parentWindow != null)
        {
            _parentWindow.LocationChanged -= OnParentWindowLocationChanged;
            _parentWindow = null;
        }
        _overlayLayer = null;
        SetIsMouseOver(false);

        OnClosed(EventArgs.Empty);
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
        // 与 OpenPopup 保持一致：外飞窗口的 popup 不做窗口级 AutoFlip，避免被夹回父窗口内。
        var supportsExternalPopup = Platform.PlatformFactory.IsWindows || Platform.PlatformFactory.IsLinux;
        var skipWindowAutoFlip = PreferExternalWindow && !ShouldConstrainToRootBounds && supportsExternalPopup;
        var adjustedPos = skipWindowAutoFlip ? windowLocalPos : ApplyAutoFlip(windowLocalPos, popupSize, windowSize);

        if (_isUsingExternalWindow && _popupWindow != null)
        {
            var screenPos = WindowLocalToScreen(adjustedPos);
            // Keep this symmetric with OpenAsExternalWindow: Linux popup
            // coordinates are owner-relative (and Wayland compositor
            // constrained), while Win32 receives global screen coordinates.
            if (Platform.PlatformFactory.IsWindows)
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
        var targetWindowBounds = GetPlacementBounds(target);
        var targetScreenTopLeft = WindowLocalToScreen(new Point(targetWindowBounds.X, targetWindowBounds.Y));
        var physTargetW = targetWindowBounds.Width * dpiScale;
        var physTargetH = targetWindowBounds.Height * dpiScale;

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
        // 先从 Popup 自身向上找，再从 PlacementTarget 向上找。
        //
        // 级联场景（子菜单的 Popup 挂在一个已经外飞成独立 PopupWindow 的父菜单里）下，向上走 VisualParent
        // 不会遇到 Window，而是先遇到父菜单的 PopupWindow —— 它不是 Window，且自身没有通向 Window 的
        // VisualParent。必须经由 PopupWindow.OwnerWindow 解析到真正的顶层窗口，否则会一路走到 fallback 的
        // Application.Current.MainWindow：在主窗口里这恰好等于正确窗口（bug 隐身），但从第二个窗口打开时，
        // 就会按主窗口原点做 ClientToScreen，使子菜单整体偏移（偏移量 = 两窗口客户区原点之差）。
        return ResolveOwningWindow(this)
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

    #endregion

}
