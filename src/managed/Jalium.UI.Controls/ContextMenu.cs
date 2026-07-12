using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a context menu that appears on right-click.
/// Uses a <see cref="Popup"/> to display its content at the correct screen position.
/// </summary>
public class ContextMenu : MenuBase
{
    #region Static Brushes

    private static readonly SolidColorBrush s_defaultBackgroundBrush = new(ThemeColors.ControlBackground);
    private static readonly SolidColorBrush s_defaultBorderBrush = new(ThemeColors.ControlBorder);

    #endregion

    #region Animation Constants

    private const double PopupOpenDurationMs = 250;
    private const double PopupCloseDurationMs = 150;
    private const double PopupSlideOffsetY = -6;
    private static readonly BackEase s_popupOpenEase = new() { EasingMode = EasingMode.EaseOut, Amplitude = 0.85 };
    private static readonly CubicEase s_popupCloseEase = new() { EasingMode = EasingMode.EaseInOut };

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsOpen dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(ContextMenu),
            new PropertyMetadata(false, OnIsOpenChanged));

    /// <summary>
    /// Identifies the PlacementTarget dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty PlacementTargetProperty =
        DependencyProperty.Register(nameof(PlacementTarget), typeof(UIElement), typeof(ContextMenu),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Placement dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.Register(nameof(Placement), typeof(PlacementMode), typeof(ContextMenu),
            new PropertyMetadata(PlacementMode.MousePoint));

    /// <summary>
    /// Identifies the HorizontalOffset dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.Register(nameof(HorizontalOffset), typeof(double), typeof(ContextMenu),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the VerticalOffset dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.Register(nameof(VerticalOffset), typeof(double), typeof(ContextMenu),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the StaysOpen dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty StaysOpenProperty =
        DependencyProperty.Register(nameof(StaysOpen), typeof(bool), typeof(ContextMenu),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the CustomPopupPlacementCallback dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty CustomPopupPlacementCallbackProperty =
        DependencyProperty.Register(nameof(CustomPopupPlacementCallback),
            typeof(CustomPopupPlacementCallback), typeof(ContextMenu),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the HasDropShadow dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty HasDropShadowProperty =
        DependencyProperty.Register(nameof(HasDropShadow), typeof(bool), typeof(ContextMenu),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the PlacementRectangle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty PlacementRectangleProperty =
        DependencyProperty.Register(nameof(PlacementRectangle), typeof(Rect), typeof(ContextMenu),
            new PropertyMetadata(Rect.Empty));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the Opened routed event.
    /// </summary>
    public static readonly RoutedEvent OpenedEvent =
        EventManager.RegisterRoutedEvent(nameof(Opened), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(ContextMenu));

    /// <summary>
    /// Identifies the Closed routed event.
    /// </summary>
    public static readonly RoutedEvent ClosedEvent =
        EventManager.RegisterRoutedEvent(nameof(Closed), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(ContextMenu));

    /// <summary>
    /// Occurs when the context menu is opened.
    /// </summary>
    public event RoutedEventHandler Opened
    {
        add => AddHandler(OpenedEvent, value);
        remove => RemoveHandler(OpenedEvent, value);
    }

    /// <summary>
    /// Occurs when the context menu is closed.
    /// </summary>
    public event RoutedEventHandler Closed
    {
        add => AddHandler(ClosedEvent, value);
        remove => RemoveHandler(ClosedEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a value indicating whether the context menu is open.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty)!;
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the element relative to which the context menu is positioned.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public UIElement? PlacementTarget
    {
        get => (UIElement?)GetValue(PlacementTargetProperty);
        set => SetValue(PlacementTargetProperty, value);
    }

    /// <summary>
    /// Gets or sets the placement mode.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public PlacementMode Placement
    {
        get => (PlacementMode)GetValue(PlacementProperty)!;
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal offset from the placement target.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double HorizontalOffset
    {
        get => (double)GetValue(HorizontalOffsetProperty)!;
        set => SetValue(HorizontalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical offset from the placement target.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double VerticalOffset
    {
        get => (double)GetValue(VerticalOffsetProperty)!;
        set => SetValue(VerticalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the menu stays open when clicked outside.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public bool StaysOpen
    {
        get => (bool)GetValue(StaysOpenProperty)!;
        set => SetValue(StaysOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the callback used when <see cref="Placement"/> is
    /// <see cref="PlacementMode.Custom"/>.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public CustomPopupPlacementCallback? CustomPopupPlacementCallback
    {
        get => (CustomPopupPlacementCallback?)GetValue(CustomPopupPlacementCallbackProperty);
        set => SetValue(CustomPopupPlacementCallbackProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the popup should render a drop shadow.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool HasDropShadow
    {
        get => (bool)GetValue(HasDropShadowProperty)!;
        set => SetValue(HasDropShadowProperty, value);
    }

    /// <summary>
    /// Gets or sets the rectangle relative to the placement target used to position the popup.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public Rect PlacementRectangle
    {
        get => (Rect)(GetValue(PlacementRectangleProperty) ?? Rect.Empty);
        set => SetValue(PlacementRectangleProperty, value);
    }

    #endregion

    #region Private Fields

    private Point _openPosition;
    private Popup? _popup;
    private Border? _popupBorder;
    private DispatcherTimer? _popupAnimTimer;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ContextMenu"/> class.
    /// </summary>
    public ContextMenu()
    {
    }

    #endregion

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == BackgroundProperty ||
            e.Property == BorderBrushProperty ||
            e.Property == BorderThicknessProperty ||
            e.Property == PaddingProperty ||
            e.Property == CornerRadiusProperty)
        {
            UpdatePopupChrome();
        }

        if (e.Property == PlacementProperty ||
            e.Property == HorizontalOffsetProperty ||
            e.Property == VerticalOffsetProperty ||
            e.Property == PlacementTargetProperty ||
            e.Property == StaysOpenProperty ||
            e.Property == CustomPopupPlacementCallbackProperty ||
            e.Property == HasDropShadowProperty ||
            e.Property == PlacementRectangleProperty)
        {
            UpdatePopupSettings();
        }
    }

    #region Item Generation

    /// <inheritdoc />
    protected override Panel CreateItemsPanel()
    {
        return new StackPanel { Orientation = Orientation.Vertical };
    }

    /// <inheritdoc />
    protected override FrameworkElement GetContainerForItem(object item)
    {
        return new MenuItem();
    }

    /// <inheritdoc />
    protected override bool IsItemItsOwnContainerOverride(object item)
    {
        return item is MenuItem || item is Separator;
    }

    /// <inheritdoc />
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        if (element is MenuItem menuItem && item is string text)
        {
            menuItem.Header = text;
        }
    }

    #endregion

    #region Opening/Closing

    /// <summary>
    /// Opens the context menu at the specified position.
    /// </summary>
    /// <param name="position">The position to open at.</param>
    public void Open(Point position)
    {
        _openPosition = position;
        EnsurePopup();
        // Use Absolute placement with explicit offsets for the given position
        _popup!.Placement = PlacementMode.Absolute;
        _popup.HorizontalOffset = position.X;
        _popup.VerticalOffset = position.Y;
        IsOpen = true;
    }

    /// <summary>
    /// Closes the context menu.
    /// </summary>
    public void Close()
    {
        IsOpen = false;
    }

    #endregion

    #region Popup Management

    /// <summary>
    /// Ensures the internal <see cref="Popup"/> is created and configured.
    /// </summary>
    private void EnsurePopup()
    {
        if (_popup != null) return;

        // ContextMenu is normally created standalone (no visual parent), so the
        // implicit theme style — which supplies CornerRadius, Padding, brushes —
        // never gets auto-applied through OnVisualParentChanged. Force it here so
        // the popup chrome matches the theme even for menus built in code.
        ApplyImplicitStyleIfNeeded();

        _popupBorder = new Border
        {
        };
        UpdatePopupChrome();

        _popup = new Popup
        {
            Placement = PlacementMode.MousePoint,
            StaysOpen = false,
            CustomPopupPlacementCallback = CustomPopupPlacementCallback,
            PlacementRectangle = PlacementRectangle,
            AllowsTransparency = HasDropShadow,
            // Context menus should move to external PopupWindow when they overflow the host window.
            ShouldConstrainToRootBounds = false,
            Child = _popupBorder
        };
        _popup.Closed += OnPopupClosed;
        UpdatePopupSettings();
    }

    /// <summary>
    /// Populates the popup border with the menu items from the Items collection.
    /// </summary>
    private void PopulatePopup()
    {
        if (_popupBorder == null) return;

        var scrollHost = new MenuPopupScrollHost();
        var panel = scrollHost.ItemsPanel;

        panel.Children.BeginBatchUpdate();
        try
        {
            foreach (var item in Items)
            {
                if (item is UIElement element)
                {
                    // Detach from any existing visual parent before re-parenting
                    if (element.VisualParent != null)
                    {
                        element.DetachFromVisualParent();
                    }
                    panel.Children.Add(element);
                }
                else if (item is string text)
                {
                    panel.Children.Add(new MenuItem { Header = text });
                }
            }
        }
        finally
        {
            panel.Children.EndBatchUpdate();
        }

        _popupBorder.Child = scrollHost;
    }

    private void UpdatePopupChrome()
    {
        if (_popupBorder == null)
            return;

        _popupBorder.Background = ResolvePopupBackgroundBrush();
        _popupBorder.BorderBrush = ResolvePopupBorderBrush();
        _popupBorder.BorderThickness = BorderThickness;
        _popupBorder.CornerRadius = CornerRadius;
        _popupBorder.Padding = Padding;
    }

    private Brush ResolvePopupBackgroundBrush()
    {
        return Background
            ?? TryFindResource("MenuFlyoutPresenterBackground") as Brush
            ?? TryFindResource("SurfaceBackground") as Brush
            ?? s_defaultBackgroundBrush;
    }

    private Brush ResolvePopupBorderBrush()
    {
        return BorderBrush
            ?? TryFindResource("MenuFlyoutPresenterBorderBrush") as Brush
            ?? TryFindResource("ControlBorder") as Brush
            ?? s_defaultBorderBrush;
    }

    private void UpdatePopupSettings()
    {
        if (_popup == null)
            return;

        _popup.PlacementTarget = PlacementTarget;
        _popup.StaysOpen = StaysOpen;
        _popup.CustomPopupPlacementCallback = CustomPopupPlacementCallback;
        _popup.PlacementRectangle = PlacementRectangle;
        _popup.AllowsTransparency = HasDropShadow;

        if (_popup.IsOpen)
        {
            _popup.Placement = Placement;
            _popup.HorizontalOffset = HorizontalOffset;
            _popup.VerticalOffset = VerticalOffset;
        }
    }

    private void OnPopupClosed(object? sender, EventArgs e)
    {
        if (IsOpen)
        {
            SetCurrentValue(IsOpenProperty, false);
        }
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // The ContextMenu itself should not take any layout space.
        // All visual content is displayed via the Popup.
        return default(Size);
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ContextMenu contextMenu)
        {
            var isOpen = (bool)e.NewValue!;

            if (isOpen)
            {
                contextMenu.EnsurePopup();
                contextMenu.PopulatePopup();

                var popup = contextMenu._popup!;
                var wasExplicitPosition = popup.Placement == PlacementMode.Absolute;

                // Transfer placement properties to the popup.
                // When Open(Point) was called, it already set Absolute placement
                // and the offsets to the requested position — keep those.
                if (!wasExplicitPosition)
                {
                    popup.Placement = contextMenu.Placement;
                    popup.HorizontalOffset = contextMenu.HorizontalOffset;
                    popup.VerticalOffset = contextMenu.VerticalOffset;
                }
                else
                {
                    // Add the ContextMenu-level offsets on top of the explicit position
                    popup.HorizontalOffset += contextMenu.HorizontalOffset;
                    popup.VerticalOffset += contextMenu.VerticalOffset;
                }

                popup.PlacementTarget = contextMenu.PlacementTarget;
                popup.StaysOpen = contextMenu.StaysOpen;
                popup.CustomPopupPlacementCallback = contextMenu.CustomPopupPlacementCallback;
                popup.PlacementRectangle = contextMenu.PlacementRectangle;
                popup.AllowsTransparency = contextMenu.HasDropShadow;
                popup.IsOpen = true;

                contextMenu.AnimateOpen();

                contextMenu.OnOpened(new RoutedEventArgs(OpenedEvent, contextMenu));
            }
            else
            {
                contextMenu.OnClosed(new RoutedEventArgs(ClosedEvent, contextMenu));
                contextMenu.AnimateClose();
            }
        }
    }

    /// <summary>
    /// Raises the <see cref="Opened"/> routed event.
    /// </summary>
    protected virtual void OnOpened(RoutedEventArgs e)
    {
        RaiseEvent(e);
    }

    /// <summary>
    /// Raises the <see cref="Closed"/> routed event.
    /// </summary>
    protected virtual void OnClosed(RoutedEventArgs e)
    {
        RaiseEvent(e);
    }

    #endregion

    #region Animation

    private void AnimateOpen()
    {
        _popupAnimTimer?.Stop();

        var target = _popupBorder;
        if (target == null) return;

        target.Opacity = 0;
        target.RenderOffset = new Point(0, PopupSlideOffsetY);
        _popup?.RequestHostRender();

        var startTick = Environment.TickCount64;
        _popupAnimTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(1, CompositionTarget.FrameIntervalMs))
        };
        _popupAnimTimer.Tick += (_, _) =>
        {
            var elapsed = Environment.TickCount64 - startTick;
            var progress = Math.Clamp(elapsed / PopupOpenDurationMs, 0.0, 1.0);
            var eased = s_popupOpenEase.Ease(progress);

            target.Opacity = eased;
            target.RenderOffset = new Point(0, PopupSlideOffsetY * (1.0 - eased));

            if (progress >= 1.0)
            {
                target.Opacity = 1;
                target.RenderOffset = default;
                _popupAnimTimer?.Stop();
                _popupAnimTimer = null;
            }

            _popup?.RequestHostRender();
        };
        _popupAnimTimer.Start();
    }

    private void AnimateClose()
    {
        _popupAnimTimer?.Stop();

        // If the popup is already closed (e.g. via light-dismiss), finalize synchronously.
        if (_popup == null || _popupBorder == null || !_popup.IsOpen)
        {
            FinalizePopupClose();
            return;
        }

        var target = _popupBorder;
        var startOpacity = target.Opacity;
        var startOffsetY = target.RenderOffset.Y;
        var startTick = Environment.TickCount64;

        _popupAnimTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(1, CompositionTarget.FrameIntervalMs))
        };
        _popupAnimTimer.Tick += (_, _) =>
        {
            var elapsed = Environment.TickCount64 - startTick;
            var progress = Math.Clamp(elapsed / PopupCloseDurationMs, 0.0, 1.0);
            var eased = s_popupCloseEase.Ease(progress);

            target.Opacity = startOpacity * (1.0 - eased);
            target.RenderOffset = new Point(0, startOffsetY + (PopupSlideOffsetY - startOffsetY) * eased);
            _popup?.RequestHostRender();

            if (progress >= 1.0)
            {
                _popupAnimTimer?.Stop();
                _popupAnimTimer = null;

                target.Opacity = 1;
                target.RenderOffset = default;

                FinalizePopupClose();
            }
        };
        _popupAnimTimer.Start();
    }

    private void FinalizePopupClose()
    {
        if (_popup == null) return;

        _popup.IsOpen = false;

        // Reset popup state so the next open uses the ContextMenu's own
        // Placement property instead of a stale Absolute from Open(Point).
        _popup.Placement = PlacementMode.MousePoint;
        _popup.HorizontalOffset = 0;
        _popup.VerticalOffset = 0;
    }

    #endregion
}

// Note: PlacementMode is defined in Popup.cs
