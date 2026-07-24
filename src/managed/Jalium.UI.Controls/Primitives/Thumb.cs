using Jalium.UI.Controls;
using Jalium.UI;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a control that can be dragged by the user, typically used in scrollbars and sliders.
/// </summary>
public class Thumb : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.ThumbAutomationPeer(this);
    }

    #region Static Brushes & Pens

    private static readonly SolidColorBrush s_draggingBgBrush = new(Color.FromRgb(0, 100, 190));
    private static readonly SolidColorBrush s_defaultBgBrush = new(Color.FromRgb(100, 100, 100));
    private static readonly SolidColorBrush s_gripBrush = new(Color.FromRgb(60, 60, 60));
    private static readonly Pen s_gripPen = new(s_gripBrush, 1);

    // Cached border pen
    private Pen? _borderPen;
    private Brush? _borderPenBrush;
    private double _borderPenThickness;

    private const string DefaultBackgroundKey = "ControlBackground";
    private const string DraggingBackgroundKey = "ControlBackgroundPressed";
    private const string GripBrushKey = "TextTertiary";

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsDragging read-only dependency property key.
    /// </summary>
    private static readonly DependencyPropertyKey IsDraggingPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsDragging), typeof(bool), typeof(Thumb),
            new PropertyMetadata(false, OnIsDraggingPropertyChanged));

    /// <summary>
    /// Identifies the IsDragging dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsDraggingProperty = IsDraggingPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the ShowGrip dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ShowGripProperty =
        DependencyProperty.Register(nameof(ShowGrip), typeof(bool), typeof(Thumb),
            new PropertyMetadata(true, OnVisualPropertyChanged));

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
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsDragging
    {
        get => (bool)GetValue(IsDraggingProperty)!;
        protected set => SetValue(IsDraggingPropertyKey, value);
    }

    /// <summary>
    /// Gets or sets whether grip lines are rendered by the default thumb renderer.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public bool ShowGrip
    {
        get => (bool)GetValue(ShowGripProperty)!;
        set => SetValue(ShowGripProperty, value);
    }

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
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");
        Focusable = true;

        // Register mouse event handlers
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));

        // Register touch event handlers — per-contact capture so multi-finger
        // simultaneous drag on neighbouring thumbs (e.g. RangeSlider) works.
        AddHandler(TouchDownEvent, new RoutedEventHandler(OnTouchDownHandler));
        AddHandler(TouchMoveEvent, new RoutedEventHandler(OnTouchMoveHandler));
        AddHandler(TouchUpEvent, new RoutedEventHandler(OnTouchUpHandler));
        AddHandler(LostTouchCaptureEvent, new RoutedEventHandler(OnLostTouchCaptureHandler));
    }

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Thumb thumb)
        {
            thumb.InvalidateVisual();
        }
    }

    private static void OnIsDraggingPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var thumb = (Thumb)d;
        thumb.OnDraggingChanged(e);
        thumb.InvalidateVisual();
    }

    /// <summary>
    /// Called when <see cref="IsDragging"/> changes.
    /// </summary>
    /// <param name="e">The property change data.</param>
    protected virtual void OnDraggingChanged(DependencyPropertyChangedEventArgs e)
    {
    }

    #endregion

    #region Template Parts

    private Border? _thumbBorder;

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _thumbBorder = GetTemplateChild("ThumbBorder") as Border;
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            Focus();
            var position = e.GetPosition(this);
            StartDrag(position, e.Position);
            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (IsDragging)
            {
                FinishDrag(false);
            }
            e.Handled = true;
        }
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (IsDragging)
        {
            UpdateDrag(e.Position);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();
        if (IsDragging && _activeTouchId == -1)
        {
            FinishDrag(true);
        }
    }

    // ── Touch handlers ──
    // Touch drag mirrors mouse drag but captures the specific TouchDevice
    // rather than the global mouse, so multi-finger drag (e.g. RangeSlider's
    // two thumbs in parallel) works.
    private int _activeTouchId = -1;

    private void OnTouchDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled || e is not TouchEventArgs touchArgs) return;
        if (!TouchHelper.GetIsTouchInteractive(this)) return;
        if (_activeTouchId >= 0)
        {
            // One Thumb has one drag owner. A second contact must not replace
            // the captured finger or restart DragStarted mid-gesture.
            e.Handled = true;
            return;
        }

        _activeTouchId = touchArgs.TouchDevice.Id;
        CaptureTouch(touchArgs.TouchDevice);
        Focus();
        Point localPos = touchArgs.GetTouchPoint(this).Position;
        Point windowPos = touchArgs.GetTouchPoint(null).Position;
        StartTouchDrag(localPos, windowPos);
        e.Handled = true;
    }

    private void OnTouchMoveHandler(object sender, RoutedEventArgs e)
    {
        if (!IsDragging || e is not TouchEventArgs touchArgs) return;
        if (touchArgs.TouchDevice.Id != _activeTouchId) return;
        Point windowPos = touchArgs.GetTouchPoint(null).Position;
        UpdateDrag(windowPos);
        e.Handled = true;
    }

    private void OnTouchUpHandler(object sender, RoutedEventArgs e)
    {
        if (e is not TouchEventArgs touchArgs) return;
        if (touchArgs.TouchDevice.Id != _activeTouchId) return;
        ReleaseTouchCapture(touchArgs.TouchDevice);
        _activeTouchId = -1;
        if (IsDragging) FinishDrag(false);
        e.Handled = true;
    }

    private void OnLostTouchCaptureHandler(object sender, RoutedEventArgs e)
    {
        if (e is not TouchEventArgs touchArgs) return;
        if (touchArgs.TouchDevice.Id != _activeTouchId) return;
        _activeTouchId = -1;
        if (IsDragging) FinishDrag(true);
    }

    #endregion

    #region Drag Operations

    private void StartDrag(Point position, Point windowPosition)
    {
        CaptureMouse();
        StartTouchDrag(position, windowPosition);
    }

    private void StartTouchDrag(Point position, Point windowPosition)
    {
        IsDragging = true;

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

        IsDragging = false;

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
    protected override void OnRender(DrawingContext drawingContext)
    {
        // If using template, let the template handle rendering
        if (_thumbBorder != null)
        {
            return;
        }

        var dc = drawingContext;

        var rect = new Rect(RenderSize);
        var cornerRadius = CornerRadius;

        // Get visual state colors
        var bgBrush = Background ?? (IsDragging
            ? ResolveDraggingBackgroundBrush()
            : ResolveDefaultBackgroundBrush());

        var borderBrush = BorderBrush;
        var gripPen = ResolveGripPen();

        // Draw background
        dc.DrawRoundedRectangle(bgBrush, null, rect, cornerRadius);

        // Draw border
        if (borderBrush != null && BorderThickness.TotalWidth > 0)
        {
            if (_borderPen == null || _borderPenBrush != borderBrush || _borderPenThickness != BorderThickness.Left)
            {
                _borderPen = new Pen(borderBrush, BorderThickness.Left);
                _borderPenBrush = borderBrush;
                _borderPenThickness = BorderThickness.Left;
            }
            dc.DrawRoundedRectangle(null, _borderPen, rect, cornerRadius);
        }

        // Draw grip lines for visual feedback when requested.
        if (ShowGrip && RenderSize.Width >= 8 && RenderSize.Height >= 8)
        {
            var centerX = rect.Width / 2;
            var centerY = rect.Height / 2;
            var gripWidth = Math.Min(8, rect.Width - 4);
            var startX = centerX - gripWidth / 2;

            dc.DrawLine(gripPen, new Point(startX, centerY - 2), new Point(startX + gripWidth, centerY - 2));
            dc.DrawLine(gripPen, new Point(startX, centerY), new Point(startX + gripWidth, centerY));
            dc.DrawLine(gripPen, new Point(startX, centerY + 2), new Point(startX + gripWidth, centerY + 2));
        }
    }

    private Brush ResolveDefaultBackgroundBrush()
    {
        return ResolveBrushResource(DefaultBackgroundKey) ?? s_defaultBgBrush;
    }

    private Brush ResolveDraggingBackgroundBrush()
    {
        return ResolveBrushResource(DraggingBackgroundKey) ?? s_draggingBgBrush;
    }

    private Pen ResolveGripPen()
    {
        var gripBrush = ResolveBrushResource(GripBrushKey) ?? s_gripBrush;
        return CreatePen(gripBrush, s_gripPen);
    }

    private Brush? ResolveBrushResource(string key)
    {
        if (TryFindResource(key) is Brush localBrush)
            return localBrush;

        if (Application.Current?.Resources.TryGetValue(key, out var appResource) == true &&
            appResource is Brush appBrush)
        {
            return appBrush;
        }

        return null;
    }

    private static Pen CreatePen(Brush brush, Pen template)
    {
        return new Pen(brush, template.Thickness)
        {
            LineJoin = template.LineJoin,
            StartLineCap = template.StartLineCap,
            EndLineCap = template.EndLineCap
        };
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
