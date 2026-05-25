using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Specifies the preferred placement of a flyout relative to a visual element.
/// </summary>
public enum FlyoutPlacementMode
{
    Top,
    Bottom,
    Left,
    Right,
    Full,
    TopEdgeAlignedLeft,
    TopEdgeAlignedRight,
    BottomEdgeAlignedLeft,
    BottomEdgeAlignedRight,
    LeftEdgeAlignedTop,
    LeftEdgeAlignedBottom,
    RightEdgeAlignedTop,
    RightEdgeAlignedBottom,
    Auto
}

/// <summary>
/// Specifies the show mode for a flyout.
/// </summary>
public enum FlyoutShowMode
{
    Auto,
    Standard,
    Transient,
    TransientWithDismissOnPointerMoveAway
}

/// <summary>
/// Provides options for showing a flyout.
/// </summary>
public sealed class FlyoutShowOptions
{
    /// <summary>
    /// Gets or sets the preferred placement of the flyout.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public FlyoutPlacementMode Placement { get; set; } = FlyoutPlacementMode.Auto;

    /// <summary>
    /// Gets or sets the point relative to the placement target at which to show the flyout.
    /// </summary>
    public Point? Position { get; set; }

    /// <summary>
    /// Gets or sets the show mode for the flyout.
    /// </summary>
    public FlyoutShowMode ShowMode { get; set; } = FlyoutShowMode.Auto;
}

/// <summary>
/// Represents the base class for flyout controls that display lightweight UI.
/// </summary>
public abstract class FlyoutBase : DependencyObject
{
    private static readonly SolidColorBrush s_fallbackPopupBackgroundBrush = new(Color.FromRgb(45, 45, 48));
    private static readonly SolidColorBrush s_fallbackPopupBorderBrush = new(Color.FromRgb(67, 67, 70));
    private Popup? _popup;
    private Control? _presenter;
    private FrameworkElement? _target;

    #region Dependency Properties

    /// <summary>
    /// Identifies the Placement dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.Register(nameof(Placement), typeof(FlyoutPlacementMode), typeof(FlyoutBase),
            new PropertyMetadata(FlyoutPlacementMode.Bottom));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the default placement to be used for the flyout.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public FlyoutPlacementMode Placement
    {
        get => (FlyoutPlacementMode)(GetValue(PlacementProperty) ?? FlyoutPlacementMode.Bottom);
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether the flyout is currently open.
    /// </summary>
    public bool IsOpen => _popup?.IsOpen == true;

    #endregion

    #region Events

    /// <summary>
    /// Occurs before the flyout is opened.
    /// </summary>
    public event EventHandler? Opening;

    /// <summary>
    /// Occurs after the flyout is opened.
    /// </summary>
    public event EventHandler? Opened;

    /// <summary>
    /// Occurs before the flyout is closed.
    /// </summary>
    public event EventHandler? Closing;

    /// <summary>
    /// Occurs after the flyout is closed.
    /// </summary>
    public event EventHandler? Closed;

    #endregion

    /// <summary>
    /// Shows the flyout placed in relation to the specified element.
    /// </summary>
    public void ShowAt(FrameworkElement placementTarget)
    {
        ShowAt(placementTarget, new FlyoutShowOptions());
    }

    /// <summary>
    /// Shows the flyout placed in relation to the specified element using the specified options.
    /// </summary>
    public void ShowAt(FrameworkElement placementTarget, FlyoutShowOptions options)
    {
        _target = placementTarget;

        Opening?.Invoke(this, EventArgs.Empty);

        EnsurePopup();
        if (_popup == null) return;

        _popup.PlacementTarget = placementTarget;

        if (options.Position.HasValue)
        {
            // WinUI 3 语义：Position 是相对 PlacementTarget 左上角的明确落点（context-menu / 右键菜单
            // 这种场景就该精确贴鼠标，而不是再叠到 Bottom/Top 锚点上）。用 Relative 让 Popup 把偏移
            // 从 target 左上角起算；忽略 Placement，由调用方通过 Position 完全决定位置。
            _popup.Placement = PlacementMode.Relative;
            _popup.HorizontalOffset = options.Position.Value.X;
            _popup.VerticalOffset = options.Position.Value.Y;
        }
        else
        {
            _popup.Placement = MapPlacement(options.Placement != FlyoutPlacementMode.Auto ? options.Placement : Placement);
            _popup.HorizontalOffset = 0;
            _popup.VerticalOffset = 0;
        }

        _popup.IsOpen = true;
        Opened?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Closes the flyout.
    /// </summary>
    public void Hide()
    {
        if (_popup == null || !_popup.IsOpen) return;

        Closing?.Invoke(this, EventArgs.Empty);
        _popup.IsOpen = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// When overridden in a derived class, initializes a control to show the flyout content.
    /// </summary>
    protected abstract Control CreatePresenter();

    private Brush ResolvePopupBackgroundBrush()
    {
        return ResolveApplicationBrush("MenuFlyoutPresenterBackground")
            ?? ResolveApplicationBrush("TooltipBackground")
            ?? ResolveApplicationBrush("SurfaceBackground")
            ?? s_fallbackPopupBackgroundBrush;
    }

    private Brush ResolvePopupBorderBrush()
    {
        return ResolveApplicationBrush("MenuFlyoutPresenterBorderBrush")
            ?? ResolveApplicationBrush("TooltipBorder")
            ?? ResolveApplicationBrush("ControlBorder")
            ?? s_fallbackPopupBorderBrush;
    }

    private static Brush? ResolveApplicationBrush(object resourceKey)
    {
        var app = global::Jalium.UI.Application.Current;
        if (app?.Resources != null &&
            app.Resources.TryGetValue(resourceKey, out var resource) &&
            resource is Brush brush)
        {
            return brush;
        }

        return null;
    }

    private void EnsurePopup()
    {
        if (_popup != null) return;

        _presenter = CreatePresenter();
        if (_presenter == null) return;

        // MenuFlyoutPresenter 自带 chrome（边框/圆角/背景由 OnRender 画），不要再外包 Border，
        // 否则会出现双重边框；其它 Flyout 没自带 chrome，需要 Border 提供视觉容器。
        var isMenuFlyout = _presenter is global::Jalium.UI.Controls.MenuFlyoutPresenter;
        var presenterPaintsOwnChrome = isMenuFlyout;

        _popup = new Popup
        {
            StaysOpen = false,
            IsLightDismissEnabled = true,
            ShouldConstrainToRootBounds = false,
            // MenuFlyout 是右键/上下文菜单语义：按 Win32/WPF/WinUI 惯例总是独立顶层窗口，
            // 这样才能贴鼠标任意位置、不受父窗口裁切、独立 light dismiss。其它 Flyout（按钮下拉等）
            // 仍走"溢出才升级"的默认策略，避免引入不必要的窗口创建开销。
            PreferExternalWindow = isMenuFlyout
        };

        if (presenterPaintsOwnChrome)
        {
            _popup.Child = _presenter;
        }
        else
        {
            var border = new Border
            {
                Child = _presenter,
                Background = ResolvePopupBackgroundBrush(),
                BorderBrush = ResolvePopupBorderBrush(),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4)
            };

            _popup.Child = border;
        }
        _popup.Closed += (_, _) =>
        {
            Closed?.Invoke(this, EventArgs.Empty);
        };
    }

    private static PlacementMode MapPlacement(FlyoutPlacementMode flyoutPlacement)
    {
        return flyoutPlacement switch
        {
            FlyoutPlacementMode.Top => PlacementMode.Top,
            FlyoutPlacementMode.Left => PlacementMode.Left,
            FlyoutPlacementMode.Right => PlacementMode.Right,
            _ => PlacementMode.Bottom
        };
    }
}
