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
    private Popup? _popup;
    private Control? _presenter;
    private FrameworkElement? _target;
    private FlyoutPresenter? _frame;
    private bool _notifiedOpen;
    private Style? _appliedFrameStyle;
    private Style? _originalFrameStyle;
    private bool _originalFrameHasLocalStyle;
    private bool _overridingFrameStyle;
#region Dependency Properties
    /// <summary>
    /// Identifies the Placement dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty PlacementProperty = DependencyProperty.Register(nameof(Placement), typeof(FlyoutPlacementMode), typeof(FlyoutBase), new PropertyMetadata(FlyoutPlacementMode.Bottom));
    public static readonly DependencyProperty FlyoutPresenterStyleProperty = DependencyProperty.Register(nameof(FlyoutPresenterStyle), typeof(Style), typeof(FlyoutBase), new PropertyMetadata(null, (d, _) => ((FlyoutBase)d).UpdatePresenterStyle()));
#endregion
#region CLR Properties
    /// <summary>
    /// Gets or sets the default placement to be used for the flyout.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public FlyoutPlacementMode Placement { get => (FlyoutPlacementMode)(GetValue(PlacementProperty) ?? FlyoutPlacementMode.Bottom); set => SetValue(PlacementProperty, value); }
    /// <summary>Gets or sets the generic flyout frame style.</summary>
    public Style? FlyoutPresenterStyle { get => (Style? )GetValue(FlyoutPresenterStyleProperty); set => SetValue(FlyoutPresenterStyleProperty, value); }
    /// <summary>
    /// Gets a value indicating whether the flyout is currently open.
    /// </summary>
    public bool IsOpen => _popup?.IsOpen == true;
    internal FrameworkElement? PlacementTarget => _target;

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
        if (!ReferenceEquals(_target, placementTarget))
        {
            if (_target != null)
            {
                _target.DataContextChanged -= OnTargetDataContextChanged;
            }

            _target = placementTarget;
            _target.DataContextChanged += OnTargetDataContextChanged;
        }

        Opening?.Invoke(this, EventArgs.Empty);
        EnsurePopup();
        if (_popup == null)
            return;
        _popup.PlacementTarget = placementTarget;
        _popup.DataContext = placementTarget.DataContext;
        if (_frame != null)
        {
            _frame.UpdateDefaultStyle();
            _frame.ApplyImplicitStyleIfNeeded();
            _frame.ApplyTemplate();
        }

        if (_presenter != null)
            Control.ReactivateBindingsRecursive(_presenter);
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

        if (_frame is { IsEnabled: false })
            return;
        _popup.IsOpen = true;
        _frame?.SetOpen(_popup.IsOpen);
        if (_popup.IsOpen && !_notifiedOpen)
        {
            _notifiedOpen = true;
            Opened?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Closes the flyout.
    /// </summary>
    public void Hide()
    {
        if (_popup == null || !_popup.IsOpen)
            return;
        Closing?.Invoke(this, EventArgs.Empty);
        _popup.IsOpen = false;
        if (!_popup.IsOpen)
            NotifyClosed();
    }

    /// <summary>
    /// When overridden in a derived class, initializes a control to show the flyout content.
    /// </summary>
    protected abstract Control CreatePresenter();
    private void EnsurePopup()
    {
        if (_popup != null)
            return;
        _presenter = CreatePresenter();
        if (_presenter == null)
            return;
        var isMenuFlyout = _presenter is MenuFlyoutPresenter;
        var presenterPaintsOwnChrome = _presenter is MenuFlyoutPresenter or FlyoutPresenter;
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
            _frame = new FlyoutPresenter
            {
                Content = _presenter
            };
            _frame.CloseRequested += OnFrameCloseRequested;
            UpdatePresenterStyle();
            _popup.Child = _frame;
        }

        if (_presenter is FlyoutPresenter presenterFrame)
        {
            _frame = presenterFrame;
            _frame.CloseRequested += OnFrameCloseRequested;
            UpdatePresenterStyle();
        }

        _popup.Closed += OnPopupClosed;
    }

    private void UpdatePresenterStyle()
    {
        if (_frame == null)
            return;
        var style = FlyoutPresenterStyle;
        if (style != null)
        {
            if (!_overridingFrameStyle)
            {
                _originalFrameStyle = _frame.Style;
                _originalFrameHasLocalStyle = _frame.HasLocalValue(FrameworkElement.StyleProperty);
            }

            _frame.Style = style;
            _overridingFrameStyle = true;
        }
        else if (_overridingFrameStyle)
        {
            if (ReferenceEquals(_frame.Style, _appliedFrameStyle))
            {
                if (_originalFrameHasLocalStyle)
                    _frame.Style = _originalFrameStyle;
                else
                    _frame.ClearValue(FrameworkElement.StyleProperty);
            }

            _overridingFrameStyle = false;
        }

        _appliedFrameStyle = style;
    }

    private void OnTargetDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, _target))
            return;
        if (_popup != null)
            _popup.DataContext = e.NewValue;
        if (_presenter != null)
            Control.ReactivateBindingsRecursive(_presenter);
    }

    private void OnFrameCloseRequested(object? sender, EventArgs e) => Hide();
    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, _popup) && !_popup!.IsOpen)
            NotifyClosed();
    }

    private void NotifyClosed()
    {
        if (!_notifiedOpen)
            return;
        _notifiedOpen = false;
        _frame?.SetOpen(false);
        Closed?.Invoke(this, EventArgs.Empty);
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
