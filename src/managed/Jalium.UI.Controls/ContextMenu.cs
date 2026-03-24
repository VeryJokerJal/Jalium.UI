using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a context menu that appears on right-click.
/// Uses a <see cref="Popup"/> to display its content at the correct screen position.
/// </summary>
public class ContextMenu : ItemsControl
{
    #region Static Brushes

    private static readonly SolidColorBrush s_defaultBackgroundBrush = new(Color.FromRgb(45, 45, 48));
    private static readonly SolidColorBrush s_defaultBorderBrush = new(Color.FromRgb(67, 67, 70));

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

    #endregion

    #region Private Fields

    private Point _openPosition;
    private Popup? _popup;
    private Border? _popupBorder;

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
            e.Property == StaysOpenProperty)
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
    protected override bool IsItemItsOwnContainer(object item)
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

        _popupBorder = new Border
        {
        };
        UpdatePopupChrome();

        _popup = new Popup
        {
            Placement = PlacementMode.MousePoint,
            StaysOpen = false,
            // Context menus should move to external PopupWindow when they overflow the host window.
            ShouldConstrainToRootBounds = false,
            Child = _popupBorder
        };
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

        if (_popup.IsOpen)
        {
            _popup.Placement = Placement;
            _popup.HorizontalOffset = HorizontalOffset;
            _popup.VerticalOffset = VerticalOffset;
        }
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // The ContextMenu itself should not take any layout space.
        // All visual content is displayed via the Popup.
        return Size.Empty;
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ContextMenu contextMenu)
        {
            var isOpen = (bool)e.NewValue;

            if (isOpen)
            {
                contextMenu.EnsurePopup();
                contextMenu.PopulatePopup();

                var popup = contextMenu._popup!;
                var wasExplicitPosition = popup.Placement == PlacementMode.Absolute;

                // Transfer placement properties to the popup.
                // When Open(Point) was called, it already set Absolute placement
                // and the offsets to the requested position 閳?keep those.
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
                popup.IsOpen = true;

                contextMenu.RaiseEvent(new RoutedEventArgs(OpenedEvent, contextMenu));
            }
            else
            {
                if (contextMenu._popup != null)
                {
                    contextMenu._popup.IsOpen = false;
                }

                contextMenu.RaiseEvent(new RoutedEventArgs(ClosedEvent, contextMenu));

                // Reset popup state so the next open uses the ContextMenu's own
                // Placement property instead of a stale Absolute from Open(Point).
                if (contextMenu._popup != null)
                {
                    contextMenu._popup.Placement = PlacementMode.MousePoint;
                    contextMenu._popup.HorizontalOffset = 0;
                    contextMenu._popup.VerticalOffset = 0;
                }
            }
        }
    }

    #endregion
}

// Note: PlacementMode is defined in Popup.cs
