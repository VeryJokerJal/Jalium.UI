using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a context menu that appears on right-click.
/// </summary>
public class ContextMenu : ItemsControl
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the IsOpen dependency property.
    /// </summary>
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(ContextMenu),
            new PropertyMetadata(false, OnIsOpenChanged));

    /// <summary>
    /// Identifies the PlacementTarget dependency property.
    /// </summary>
    public static readonly DependencyProperty PlacementTargetProperty =
        DependencyProperty.Register(nameof(PlacementTarget), typeof(UIElement), typeof(ContextMenu),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Placement dependency property.
    /// </summary>
    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.Register(nameof(Placement), typeof(PlacementMode), typeof(ContextMenu),
            new PropertyMetadata(PlacementMode.MousePoint));

    /// <summary>
    /// Identifies the HorizontalOffset dependency property.
    /// </summary>
    public static readonly DependencyProperty HorizontalOffsetProperty =
        DependencyProperty.Register(nameof(HorizontalOffset), typeof(double), typeof(ContextMenu),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the VerticalOffset dependency property.
    /// </summary>
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.Register(nameof(VerticalOffset), typeof(double), typeof(ContextMenu),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the StaysOpen dependency property.
    /// </summary>
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
    public bool IsOpen
    {
        get => (bool)(GetValue(IsOpenProperty) ?? false);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the element relative to which the context menu is positioned.
    /// </summary>
    public UIElement? PlacementTarget
    {
        get => (UIElement?)GetValue(PlacementTargetProperty);
        set => SetValue(PlacementTargetProperty, value);
    }

    /// <summary>
    /// Gets or sets the placement mode.
    /// </summary>
    public PlacementMode Placement
    {
        get => (PlacementMode)(GetValue(PlacementProperty) ?? PlacementMode.MousePoint);
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal offset from the placement target.
    /// </summary>
    public double HorizontalOffset
    {
        get => (double)(GetValue(HorizontalOffsetProperty) ?? 0.0);
        set => SetValue(HorizontalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical offset from the placement target.
    /// </summary>
    public double VerticalOffset
    {
        get => (double)(GetValue(VerticalOffsetProperty) ?? 0.0);
        set => SetValue(VerticalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the menu stays open when clicked outside.
    /// </summary>
    public bool StaysOpen
    {
        get => (bool)(GetValue(StaysOpenProperty) ?? false);
        set => SetValue(StaysOpenProperty, value);
    }

    #endregion

    #region Private Fields

    private Point _openPosition;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ContextMenu"/> class.
    /// </summary>
    public ContextMenu()
    {
        Background = new SolidColorBrush(Color.FromRgb(45, 45, 48));
        BorderBrush = new SolidColorBrush(Color.FromRgb(67, 67, 70));
        BorderThickness = new Thickness(1);
        Padding = new Thickness(2);
        CornerRadius = new CornerRadius(4);

        // Initially not visible
        Visibility = Visibility.Collapsed;
    }

    #endregion

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

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (!IsOpen)
        {
            return Size.Empty;
        }

        var padding = Padding;
        var border = BorderThickness;

        // Measure items
        base.MeasureOverride(availableSize);

        var itemsSize = ItemsHost?.DesiredSize ?? Size.Empty;

        return new Size(
            itemsSize.Width + padding.TotalWidth + border.TotalWidth,
            itemsSize.Height + padding.TotalHeight + border.TotalHeight);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc || !IsOpen)
            return;

        var rect = new Rect(RenderSize);
        var cornerRadius = CornerRadius;
        var hasCornerRadius = cornerRadius.TopLeft > 0;

        // Draw shadow (simplified)
        var shadowRect = new Rect(rect.X + 2, rect.Y + 2, rect.Width, rect.Height);
        var shadowBrush = new SolidColorBrush(Color.FromArgb(64, 0, 0, 0));
        if (hasCornerRadius)
        {
            dc.DrawRoundedRectangle(shadowBrush, null, shadowRect, cornerRadius.TopLeft, cornerRadius.TopLeft);
        }
        else
        {
            dc.DrawRectangle(shadowBrush, null, shadowRect);
        }

        // Draw background
        if (Background != null)
        {
            if (hasCornerRadius)
            {
                dc.DrawRoundedRectangle(Background, null, rect, cornerRadius.TopLeft, cornerRadius.TopLeft);
            }
            else
            {
                dc.DrawRectangle(Background, null, rect);
            }
        }

        // Draw border
        if (BorderBrush != null && BorderThickness.TotalWidth > 0)
        {
            var pen = new Pen(BorderBrush, BorderThickness.Left);
            if (hasCornerRadius)
            {
                dc.DrawRoundedRectangle(null, pen, rect, cornerRadius.TopLeft, cornerRadius.TopLeft);
            }
            else
            {
                dc.DrawRectangle(null, pen, rect);
            }
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ContextMenu contextMenu)
        {
            var isOpen = (bool)e.NewValue;
            contextMenu.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;

            if (isOpen)
            {
                contextMenu.RaiseEvent(new RoutedEventArgs(OpenedEvent, contextMenu));
            }
            else
            {
                contextMenu.RaiseEvent(new RoutedEventArgs(ClosedEvent, contextMenu));
            }

            contextMenu.InvalidateMeasure();
            contextMenu.InvalidateVisual();
        }
    }

    #endregion
}

// Note: PlacementMode is defined in Popup.cs
