using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a control that can be dragged by the user, typically used in scrollbars and sliders.
/// </summary>
public sealed class Thumb : Control
{
    #region Static Brushes & Pens

    private static readonly SolidColorBrush s_draggingBgBrush = new(Color.FromRgb(0, 100, 190));
    private static readonly SolidColorBrush s_defaultBgBrush = new(Color.FromRgb(100, 100, 100));
    private static readonly SolidColorBrush s_gripBrush = new(Color.FromRgb(60, 60, 60));
    private static readonly Pen s_gripPen = new(s_gripBrush, 1);

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsDragging read-only dependency property key.
    /// </summary>
    private static readonly DependencyPropertyKey IsDraggingPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsDragging), typeof(bool), typeof(Thumb),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsDragging dependency property.
    /// </summary>
    public static readonly DependencyProperty IsDraggingProperty = IsDraggingPropertyKey.DependencyProperty;

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the DragStarted routed event.
    /// </summary>
    public static readonly RoutedEvent DragStartedEvent =
        EventManager.RegisterRoutedEvent(nameof(DragStarted), RoutingStrategy.Bubble,
            typeof(DragStartedEventHandler), typeof(Thumb));

    /// <summary>
    /// Identifies the DragDelta routed event.
    /// </summary>
    public static readonly RoutedEvent DragDeltaEvent =
        EventManager.RegisterRoutedEvent(nameof(DragDelta), RoutingStrategy.Bubble,
            typeof(DragDeltaEventHandler), typeof(Thumb));

    /// <summary>
    /// Identifies the DragCompleted routed event.
    /// </summary>
    public static readonly RoutedEvent DragCompletedEvent =
        EventManager.RegisterRoutedEvent(nameof(DragCompleted), RoutingStrategy.Bubble,
            typeof(DragCompletedEventHandler), typeof(Thumb));

    /// <summary>
    /// Occurs when a drag operation starts.
    /// </summary>
    public event DragStartedEventHandler DragStarted
    {
        add => AddHandler(DragStartedEvent, value);
        remove => RemoveHandler(DragStartedEvent, value);
    }

    /// <summary>
    /// Occurs one or more times during a drag operation.
    /// </summary>
    public event DragDeltaEventHandler DragDelta
    {
        add => AddHandler(DragDeltaEvent, value);
        remove => RemoveHandler(DragDeltaEvent, value);
    }

    /// <summary>
    /// Occurs when a drag operation completes.
    /// </summary>
    public event DragCompletedEventHandler DragCompleted
    {
        add => AddHandler(DragCompletedEvent, value);
        remove => RemoveHandler(DragCompletedEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets a value indicating whether the thumb is currently being dragged.
    /// </summary>
    public bool IsDragging => (bool)GetValue(IsDraggingProperty)!;

    #endregion

    #region Private Fields

    private Point _dragStartPosition;
    private Point _dragStartWindowPosition;
    private Point _previousDragWindowPosition;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="Thumb"/> class.
    /// </summary>
    public Thumb()
    {
        Focusable = true;

        // Register mouse event handlers
        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new RoutedEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new RoutedEventHandler(OnMouseMoveHandler));
    }

    #endregion

    #region Template Parts

    private Border? _thumbBorder;

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _thumbBorder = GetTemplateChild("ThumbBorder") as Border;
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            Focus();
            var position = mouseArgs.GetPosition(this);
            StartDrag(position, mouseArgs.Position);
            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, RoutedEventArgs e)
    {
        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            if (IsDragging)
            {
                FinishDrag(false);
            }
            e.Handled = true;
        }
    }

    private void OnMouseMoveHandler(object sender, RoutedEventArgs e)
    {
        if (IsDragging && e is MouseEventArgs mouseArgs)
        {
            UpdateDrag(mouseArgs.Position);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();
        if (IsDragging)
        {
            FinishDrag(true);
        }
    }

    #endregion

    #region Drag Operations

    private void StartDrag(Point position, Point windowPosition)
    {
        CaptureMouse();
        SetValue(IsDraggingPropertyKey.DependencyProperty, true);

        _dragStartPosition = position;
        _dragStartWindowPosition = windowPosition;
        _previousDragWindowPosition = windowPosition;

        RaiseDragStarted(_dragStartPosition.X, _dragStartPosition.Y);
        InvalidateVisual();
    }

    private void UpdateDrag(Point windowPosition)
    {
        var horizontalChange = windowPosition.X - _previousDragWindowPosition.X;
        var verticalChange = windowPosition.Y - _previousDragWindowPosition.Y;

        _previousDragWindowPosition = windowPosition;

        if (Math.Abs(horizontalChange) > 0.001 || Math.Abs(verticalChange) > 0.001)
        {
            RaiseDragDelta(horizontalChange, verticalChange);
        }
    }

    private void FinishDrag(bool canceled)
    {
        if (!IsDragging)
        {
            return;
        }

        var horizontalChange = _previousDragWindowPosition.X - _dragStartWindowPosition.X;
        var verticalChange = _previousDragWindowPosition.Y - _dragStartWindowPosition.Y;

        SetValue(IsDraggingPropertyKey.DependencyProperty, false);

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        RaiseDragCompleted(horizontalChange, verticalChange, canceled);
        InvalidateVisual();
    }

    /// <summary>
    /// Cancels the current drag operation.
    /// </summary>
    public void CancelDrag()
    {
        if (IsDragging)
        {
            FinishDrag(true);
        }
    }

    #endregion

    #region Event Raisers

    private void RaiseDragStarted(double horizontalOffset, double verticalOffset)
    {
        RaiseEvent(new DragStartedEventArgs(horizontalOffset, verticalOffset)
        {
            RoutedEvent = DragStartedEvent,
            Source = this
        });
    }

    private void RaiseDragDelta(double horizontalChange, double verticalChange)
    {
        RaiseEvent(new DragDeltaEventArgs(horizontalChange, verticalChange)
        {
            RoutedEvent = DragDeltaEvent,
            Source = this
        });
    }

    private void RaiseDragCompleted(double horizontalChange, double verticalChange, bool canceled)
    {
        RaiseEvent(new DragCompletedEventArgs(horizontalChange, verticalChange, canceled)
        {
            RoutedEvent = DragCompletedEvent,
            Source = this
        });
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // Default thumb size
        var width = double.IsNaN(Width) || Width <= 0 ? 12 : Width;
        var height = double.IsNaN(Height) || Height <= 0 ? 12 : Height;

        return new Size(
            double.IsPositiveInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width),
            double.IsPositiveInfinity(availableSize.Height) ? height : Math.Min(height, availableSize.Height));
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        // If using template, let the template handle rendering
        if (_thumbBorder != null)
        {
            return;
        }

        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(RenderSize);
        var cornerRadius = CornerRadius;

        // Get visual state colors
        var bgBrush = Background ?? (IsDragging
            ? s_draggingBgBrush
            : s_defaultBgBrush);

        var borderBrush = BorderBrush;

        // Draw background
        dc.DrawRoundedRectangle(bgBrush, null, rect, cornerRadius);

        // Draw border
        if (borderBrush != null && BorderThickness.TotalWidth > 0)
        {
            var pen = new Pen(borderBrush, BorderThickness.Left);
            dc.DrawRoundedRectangle(null, pen, rect, cornerRadius);
        }

        // Draw grip lines for visual feedback (3 horizontal lines)
        if (RenderSize.Width >= 8 && RenderSize.Height >= 8)
        {
            var centerX = rect.Width / 2;
            var centerY = rect.Height / 2;
            var gripWidth = Math.Min(8, rect.Width - 4);
            var startX = centerX - gripWidth / 2;

            dc.DrawLine(s_gripPen, new Point(startX, centerY - 2), new Point(startX + gripWidth, centerY - 2));
            dc.DrawLine(s_gripPen, new Point(startX, centerY), new Point(startX + gripWidth, centerY));
            dc.DrawLine(s_gripPen, new Point(startX, centerY + 2), new Point(startX + gripWidth, centerY + 2));
        }
    }

    #endregion
}

/// <summary>
/// Delegate for the DragStarted event.
/// </summary>
public delegate void DragStartedEventHandler(object sender, DragStartedEventArgs e);

/// <summary>
/// Delegate for the DragDelta event.
/// </summary>
public delegate void DragDeltaEventHandler(object sender, DragDeltaEventArgs e);

/// <summary>
/// Delegate for the DragCompleted event.
/// </summary>
public delegate void DragCompletedEventHandler(object sender, DragCompletedEventArgs e);

/// <summary>
/// Provides data for the DragStarted event.
/// </summary>
public sealed class DragStartedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the horizontal offset of the mouse position when the drag started.
    /// </summary>
    public double HorizontalOffset { get; }

    /// <summary>
    /// Gets the vertical offset of the mouse position when the drag started.
    /// </summary>
    public double VerticalOffset { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DragStartedEventArgs"/> class.
    /// </summary>
    public DragStartedEventArgs(double horizontalOffset, double verticalOffset)
    {
        HorizontalOffset = horizontalOffset;
        VerticalOffset = verticalOffset;
    }
}

/// <summary>
/// Provides data for the DragDelta event.
/// </summary>
public sealed class DragDeltaEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the horizontal change in the mouse position since the last DragDelta event.
    /// </summary>
    public double HorizontalChange { get; }

    /// <summary>
    /// Gets the vertical change in the mouse position since the last DragDelta event.
    /// </summary>
    public double VerticalChange { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DragDeltaEventArgs"/> class.
    /// </summary>
    public DragDeltaEventArgs(double horizontalChange, double verticalChange)
    {
        HorizontalChange = horizontalChange;
        VerticalChange = verticalChange;
    }
}

/// <summary>
/// Provides data for the DragCompleted event.
/// </summary>
public sealed class DragCompletedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the total horizontal change from the start of the drag.
    /// </summary>
    public double HorizontalChange { get; }

    /// <summary>
    /// Gets the total vertical change from the start of the drag.
    /// </summary>
    public double VerticalChange { get; }

    /// <summary>
    /// Gets a value indicating whether the drag operation was canceled.
    /// </summary>
    public bool Canceled { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DragCompletedEventArgs"/> class.
    /// </summary>
    public DragCompletedEventArgs(double horizontalChange, double verticalChange, bool canceled)
    {
        HorizontalChange = horizontalChange;
        VerticalChange = verticalChange;
        Canceled = canceled;
    }
}
