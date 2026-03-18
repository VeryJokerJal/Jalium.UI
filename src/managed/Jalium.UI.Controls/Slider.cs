using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that lets the user select from a range of values by moving a thumb.
/// </summary>
public class Slider : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.SliderAutomationPeer(this);
    }

    // Cached brushes and pens for OnRender
    private static readonly SolidColorBrush s_trackBrush = new(Color.FromRgb(60, 60, 60));
    private static readonly SolidColorBrush s_accentBrush = new(Color.FromRgb(0, 120, 212));
    private static readonly SolidColorBrush s_accentPressedBrush = new(Color.FromRgb(0, 100, 190));
    private static readonly SolidColorBrush s_tickBrush = new(Color.FromRgb(100, 100, 100));
    private static readonly Pen s_tickPen = new(s_tickBrush, 1);
    private static readonly SolidColorBrush s_whiteBrush = new(Color.FromRgb(255, 255, 255));
    private static readonly Pen s_thumbBorderPen = new(s_whiteBrush, 2);

    #region Dependency Properties

    /// <summary>
    /// Identifies the Minimum dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(Slider),
            new PropertyMetadata(0.0, OnRangePropertyChanged));

    /// <summary>
    /// Identifies the Maximum dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(Slider),
            new PropertyMetadata(100.0, OnRangePropertyChanged));

    /// <summary>
    /// Identifies the Value dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(Slider),
            new PropertyMetadata(0.0, OnValuePropertyChanged, CoerceValue));

    /// <summary>
    /// Identifies the SmallChange dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SmallChangeProperty =
        DependencyProperty.Register(nameof(SmallChange), typeof(double), typeof(Slider),
            new PropertyMetadata(1.0));

    /// <summary>
    /// Identifies the LargeChange dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty LargeChangeProperty =
        DependencyProperty.Register(nameof(LargeChange), typeof(double), typeof(Slider),
            new PropertyMetadata(10.0));

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(Slider),
            new PropertyMetadata(Orientation.Horizontal, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the TickFrequency dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty TickFrequencyProperty =
        DependencyProperty.Register(nameof(TickFrequency), typeof(double), typeof(Slider),
            new PropertyMetadata(0.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsSnapToTickEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSnapToTickEnabledProperty =
        DependencyProperty.Register(nameof(IsSnapToTickEnabled), typeof(bool), typeof(Slider),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the TrackBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty TrackBrushProperty =
        DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(Slider),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ThumbBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ThumbBrushProperty =
        DependencyProperty.Register(nameof(ThumbBrush), typeof(Brush), typeof(Slider),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the ValueChanged routed event.
    /// </summary>
    public static readonly RoutedEvent ValueChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(ValueChanged), RoutingStrategy.Bubble,
            typeof(RoutedPropertyChangedEventHandler<double>), typeof(Slider));

    /// <summary>
    /// Occurs when the Value property changes.
    /// </summary>
    public event RoutedPropertyChangedEventHandler<double> ValueChanged
    {
        add => AddHandler(ValueChangedEvent, value);
        remove => RemoveHandler(ValueChangedEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the minimum value of the Slider.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty)!;
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum value of the Slider.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty)!;
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>
    /// Gets or sets the current value of the Slider.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double Value
    {
        get => (double)GetValue(ValueProperty)!;
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the value to add or subtract when the user moves the thumb a small amount.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double SmallChange
    {
        get => (double)GetValue(SmallChangeProperty)!;
        set => SetValue(SmallChangeProperty, value);
    }

    /// <summary>
    /// Gets or sets the value to add or subtract when the user moves the thumb a large amount.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double LargeChange
    {
        get => (double)GetValue(LargeChangeProperty)!;
        set => SetValue(LargeChangeProperty, value);
    }

    /// <summary>
    /// Gets or sets the orientation of the Slider.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets the interval between tick marks.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double TickFrequency
    {
        get => (double)GetValue(TickFrequencyProperty)!;
        set => SetValue(TickFrequencyProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the Slider snaps to tick marks.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSnapToTickEnabled
    {
        get => (bool)GetValue(IsSnapToTickEnabledProperty)!;
        set => SetValue(IsSnapToTickEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for the track.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for the thumb.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? ThumbBrush
    {
        get => (Brush?)GetValue(ThumbBrushProperty);
        set => SetValue(ThumbBrushProperty, value);
    }

    #endregion

    #region Private Fields

    private bool _isDragging;
    private double _dragStartValue;
    private Point _dragStartPoint;
    private const double ThumbSize = 16.0;
    private const double TrackThickness = 4.0;

    #endregion

    #region Template Parts

    private Border? _trackBorder;
    private Border? _selectionRangeBorder;
    private Border? _thumbBorder;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="Slider"/> class.
    /// </summary>
    public Slider()
    {
        Focusable = true;
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");

        // Register input event handlers
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
    }

    #endregion

    /// <inheritdoc />
    protected override bool ShouldSuppressAutomaticTransition(DependencyProperty dp)
    {
        return ReferenceEquals(dp, ValueProperty) && _isDragging;
    }

    #region Template

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _trackBorder = GetTemplateChild("PART_Track") as Border;
        _selectionRangeBorder = GetTemplateChild("PART_SelectionRange") as Border;
        _thumbBorder = GetTemplateChild("PART_Thumb") as Border;

        UpdateSliderLayout();
    }

    private void UpdateSliderLayout()
    {
        if (_thumbBorder == null) return;

        var range = Maximum - Minimum;
        var percentage = range > 0 ? (Value - Minimum) / range : 0;

        if (Orientation == Orientation.Horizontal)
        {
            var trackWidth = RenderSize.Width - ThumbSize;
            var thumbX = percentage * trackWidth;

            _thumbBorder.Margin = new Thickness(thumbX, 0, 0, 0);

            if (_selectionRangeBorder != null)
            {
                _selectionRangeBorder.Width = thumbX + ThumbSize / 2;
            }
        }
        else
        {
            var trackHeight = RenderSize.Height - ThumbSize;
            var thumbY = (1 - percentage) * trackHeight;

            _thumbBorder.Margin = new Thickness(0, thumbY, 0, 0);

            if (_selectionRangeBorder != null)
            {
                _selectionRangeBorder.Height = (RenderSize.Height - thumbY - ThumbSize / 2);
                _selectionRangeBorder.VerticalAlignment = VerticalAlignment.Bottom;
            }
        }
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnSizeChanged(sizeInfo);
        UpdateSliderLayout();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // MUST measure template children so they get correct PreviousAvailableSize.
        base.MeasureOverride(availableSize);

        if (Orientation == Orientation.Horizontal)
        {
            var height = double.IsNaN(Height) || Height <= 0 ? 24 : Height;
            return new Size(Math.Min(availableSize.Width, double.IsPositiveInfinity(availableSize.Width) ? 200 : availableSize.Width), height);
        }
        else
        {
            var width = double.IsNaN(Width) || Width <= 0 ? 24 : Width;
            return new Size(width, Math.Min(availableSize.Height, double.IsPositiveInfinity(availableSize.Height) ? 200 : availableSize.Height));
        }
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            Focus();
            var position = e.GetPosition(this);
            var thumbRect = GetThumbRect();

            // Capture mouse for dragging
            CaptureMouse();

            if (thumbRect.Contains(position))
            {
                // Start dragging the thumb
                _isDragging = true;
                _dragStartValue = Value;
                _dragStartPoint = position;
            }
            else
            {
                // Click on track - move thumb to that position and start dragging
                _isDragging = true;
                SetValueFromPosition(position);
                _dragStartValue = Value;
                _dragStartPoint = position;
            }

            InvalidateVisual();
            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
                InvalidateVisual();
            }
            e.Handled = true;
        }
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            var position = e.GetPosition(this);
            SetValueFromPosition(position);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();
        if (_isDragging)
        {
            _isDragging = false;
            InvalidateVisual();
        }
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
            case Key.Down:
                Value -= SmallChange;
                e.Handled = true;
                break;
            case Key.Right:
            case Key.Up:
                Value += SmallChange;
                e.Handled = true;
                break;
            case Key.PageDown:
                Value -= LargeChange;
                e.Handled = true;
                break;
            case Key.PageUp:
                Value += LargeChange;
                e.Handled = true;
                break;
            case Key.Home:
                Value = Minimum;
                e.Handled = true;
                break;
            case Key.End:
                Value = Maximum;
                e.Handled = true;
                break;
        }
    }

    private void SetValueFromPosition(Point position)
    {
        var range = Maximum - Minimum;
        if (range <= 0) return;

        double percentage;
        if (Orientation == Orientation.Horizontal)
        {
            var trackWidth = RenderSize.Width - ThumbSize;
            percentage = (position.X - ThumbSize / 2) / trackWidth;
        }
        else
        {
            var trackHeight = RenderSize.Height - ThumbSize;
            percentage = 1 - (position.Y - ThumbSize / 2) / trackHeight;
        }

        percentage = Math.Clamp(percentage, 0, 1);
        var newValue = Minimum + percentage * range;

        if (IsSnapToTickEnabled && TickFrequency > 0)
        {
            newValue = Math.Round(newValue / TickFrequency) * TickFrequency;
        }

        Value = newValue;
    }

    private Rect GetThumbRect()
    {
        var range = Maximum - Minimum;
        var percentage = range > 0 ? (Value - Minimum) / range : 0;

        if (Orientation == Orientation.Horizontal)
        {
            var trackWidth = RenderSize.Width - ThumbSize;
            var thumbX = percentage * trackWidth;
            var thumbY = (RenderSize.Height - ThumbSize) / 2;
            return new Rect(thumbX, thumbY, ThumbSize, ThumbSize);
        }
        else
        {
            var trackHeight = RenderSize.Height - ThumbSize;
            var thumbY = (1 - percentage) * trackHeight;
            var thumbX = (RenderSize.Width - ThumbSize) / 2;
            return new Rect(thumbX, thumbY, ThumbSize, ThumbSize);
        }
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        // Template-based rendering: layout is updated from OnSizeChanged and
        // property callbacks (NOT here). Modifying child Margin/Width during OnRender
        // triggers InvalidateMeasure, but UpdateLayout() already ran for this frame.
        if (_thumbBorder != null)
            return;

        if (drawingContext is not DrawingContext dc)
            return;

        var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);

        // Draw track
        DrawTrack(dc, bounds);

        // Draw filled portion
        DrawFilledTrack(dc, bounds);

        // Draw tick marks if enabled
        if (TickFrequency > 0)
        {
            DrawTicks(dc, bounds);
        }

        // Draw thumb
        DrawThumb(dc);
    }

    private void DrawTrack(DrawingContext dc, Rect bounds)
    {
        var trackBrush = TrackBrush ?? s_trackBrush;

        Rect trackRect;
        if (Orientation == Orientation.Horizontal)
        {
            var trackY = (bounds.Height - TrackThickness) / 2;
            trackRect = new Rect(ThumbSize / 2, trackY, bounds.Width - ThumbSize, TrackThickness);
        }
        else
        {
            var trackX = (bounds.Width - TrackThickness) / 2;
            trackRect = new Rect(trackX, ThumbSize / 2, TrackThickness, bounds.Height - ThumbSize);
        }

        dc.DrawRoundedRectangle(trackBrush, null, trackRect, 2, 2);
    }

    private void DrawFilledTrack(DrawingContext dc, Rect bounds)
    {
        var filledBrush = s_accentBrush;
        var range = Maximum - Minimum;
        var percentage = range > 0 ? (Value - Minimum) / range : 0;

        Rect filledRect;
        if (Orientation == Orientation.Horizontal)
        {
            var trackY = (bounds.Height - TrackThickness) / 2;
            var filledWidth = (bounds.Width - ThumbSize) * percentage;
            filledRect = new Rect(ThumbSize / 2, trackY, filledWidth, TrackThickness);
        }
        else
        {
            var trackX = (bounds.Width - TrackThickness) / 2;
            var trackHeight = bounds.Height - ThumbSize;
            var filledHeight = trackHeight * percentage;
            filledRect = new Rect(trackX, ThumbSize / 2 + trackHeight - filledHeight, TrackThickness, filledHeight);
        }

        dc.DrawRoundedRectangle(filledBrush, null, filledRect, 2, 2);
    }

    private void DrawTicks(DrawingContext dc, Rect bounds)
    {
        var tickPen = s_tickPen;
        var range = Maximum - Minimum;
        if (range <= 0 || TickFrequency <= 0) return;

        var tickCount = (int)Math.Round(range / TickFrequency);
        for (var i = 0; i <= tickCount; i++)
        {
            var value = Minimum + i * TickFrequency;
            if (value > Maximum + 1e-10) break;
            var percentage = (value - Minimum) / range;

            if (Orientation == Orientation.Horizontal)
            {
                var x = ThumbSize / 2 + (bounds.Width - ThumbSize) * percentage;
                dc.DrawLine(tickPen, new Point(x, bounds.Height - 6), new Point(x, bounds.Height - 2));
            }
            else
            {
                var y = ThumbSize / 2 + (bounds.Height - ThumbSize) * (1 - percentage);
                dc.DrawLine(tickPen, new Point(bounds.Width - 6, y), new Point(bounds.Width - 2, y));
            }
        }
    }

    private void DrawThumb(DrawingContext dc)
    {
        var thumbRect = GetThumbRect();
        var thumbBrush = ThumbBrush ?? (_isDragging
            ? s_accentPressedBrush
            : s_accentBrush);

        var centerX = thumbRect.X + thumbRect.Width / 2;
        var centerY = thumbRect.Y + thumbRect.Height / 2;
        var radius = ThumbSize / 2 - 1;

        // Draw thumb circle
        dc.DrawEllipse(thumbBrush, null, new Point(centerX, centerY), radius, radius);

        // Draw thumb border
        dc.DrawEllipse(null, s_thumbBorderPen, new Point(centerX, centerY), radius - 1, radius - 1);
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnRangePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Slider slider)
        {
            // Re-coerce value to stay within new range by re-setting it
            var currentValue = slider.Value;
            var coercedValue = (double)(CoerceValue(slider, currentValue) ?? currentValue);
            if (coercedValue != currentValue)
            {
                slider.Value = coercedValue;
            }
            slider.UpdateSliderLayout();
            slider.InvalidateVisual();
        }
    }

    private static void OnValuePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Slider slider)
        {
            slider.OnValueChanged((double)(e.OldValue ?? 0.0), (double)(e.NewValue ?? 0.0));
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Slider slider)
        {
            slider.InvalidateMeasure();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Slider slider)
        {
            slider.InvalidateVisual();
        }
    }

    private static object? CoerceValue(DependencyObject d, object? value)
    {
        if (d is Slider slider && value is double doubleValue)
        {
            return Math.Clamp(doubleValue, slider.Minimum, slider.Maximum);
        }
        return value;
    }

    /// <summary>
    /// Called when the Value property changes.
    /// </summary>
    protected void OnValueChanged(double oldValue, double newValue)
    {
        UpdateSliderLayout();
        InvalidateVisual();
        RaiseEvent(new RoutedPropertyChangedEventArgs<double>(oldValue, newValue, ValueChangedEvent));
    }

    #endregion
}
