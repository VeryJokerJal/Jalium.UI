using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents the Track element used in ScrollBar and Slider controls.
/// Contains a Thumb and optionally two RepeatButtons for value manipulation.
/// </summary>
public class Track : FrameworkElement
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(Track),
            new PropertyMetadata(Orientation.Horizontal, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the Minimum dependency property.
    /// </summary>
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(Track),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the Maximum dependency property.
    /// </summary>
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(Track),
            new PropertyMetadata(1.0, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the Value dependency property.
    /// </summary>
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(Track),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the ViewportSize dependency property.
    /// </summary>
    public static readonly DependencyProperty ViewportSizeProperty =
        DependencyProperty.Register(nameof(ViewportSize), typeof(double), typeof(Track),
            new PropertyMetadata(double.NaN, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the IsDirectionReversed dependency property.
    /// </summary>
    public static readonly DependencyProperty IsDirectionReversedProperty =
        DependencyProperty.Register(nameof(IsDirectionReversed), typeof(bool), typeof(Track),
            new PropertyMetadata(false, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the ThumbCrossAxisThickness dependency property.
    /// When set to a positive value, the thumb is centered and constrained to this
    /// thickness on the axis perpendicular to scrolling.
    /// </summary>
    public static readonly DependencyProperty ThumbCrossAxisThicknessProperty =
        DependencyProperty.Register(nameof(ThumbCrossAxisThickness), typeof(double), typeof(Track),
            new PropertyMetadata(double.NaN, OnThumbCrossAxisThicknessChanged));

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="Track"/> class.
    /// </summary>
    public Track()
    {
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the orientation of the Track.
    /// </summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum value of the Track.
    /// </summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty)!;
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum value of the Track.
    /// </summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty)!;
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>
    /// Gets or sets the current value of the Track.
    /// </summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty)!;
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the size of the viewport for scrolling scenarios.
    /// </summary>
    public double ViewportSize
    {
        get => (double)GetValue(ViewportSizeProperty)!;
        set => SetValue(ViewportSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the direction of increasing value is reversed.
    /// </summary>
    public bool IsDirectionReversed
    {
        get => (bool)GetValue(IsDirectionReversedProperty)!;
        set => SetValue(IsDirectionReversedProperty, value);
    }

    /// <summary>
    /// Gets or sets the thumb thickness on the cross axis.
    /// Set to NaN (default) to use the track's normal inset behavior.
    /// </summary>
    public double ThumbCrossAxisThickness
    {
        get => (double)GetValue(ThumbCrossAxisThicknessProperty)!;
        set => SetValue(ThumbCrossAxisThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets the Thumb control.
    /// </summary>
    public Thumb? Thumb
    {
        get => _thumb;
        set
        {
            if (_thumb != value)
            {
                if (_thumb != null)
                {
                    RemoveVisualChild(_thumb);
                    _thumb.DragStarted -= OnThumbDragStarted;
                    _thumb.DragDelta -= OnThumbDragDelta;
                    _thumb.DragCompleted -= OnThumbDragCompleted;
                }

                _thumb = value;

                if (_thumb != null)
                {
                    AddVisualChild(_thumb);
                    _thumb.DragStarted += OnThumbDragStarted;
                    _thumb.DragDelta += OnThumbDragDelta;
                    _thumb.DragCompleted += OnThumbDragCompleted;
                }

                InvalidateMeasure();
            }
        }
    }

    /// <summary>
    /// Gets or sets the RepeatButton used to decrease the value.
    /// </summary>
    public RepeatButton? DecreaseRepeatButton
    {
        get => _decreaseButton;
        set
        {
            if (_decreaseButton != value)
            {
                if (_decreaseButton != null)
                {
                    RemoveVisualChild(_decreaseButton);
                }

                _decreaseButton = value;

                if (_decreaseButton != null)
                {
                    AddVisualChild(_decreaseButton);
                }

                InvalidateMeasure();
            }
        }
    }

    /// <summary>
    /// Gets or sets the RepeatButton used to increase the value.
    /// </summary>
    public RepeatButton? IncreaseRepeatButton
    {
        get => _increaseButton;
        set
        {
            if (_increaseButton != value)
            {
                if (_increaseButton != null)
                {
                    RemoveVisualChild(_increaseButton);
                }

                _increaseButton = value;

                if (_increaseButton != null)
                {
                    AddVisualChild(_increaseButton);
                }

                InvalidateMeasure();
            }
        }
    }

    #endregion

    #region Private Fields

    private Thumb? _thumb;
    private RepeatButton? _decreaseButton;
    private RepeatButton? _increaseButton;
    private double _density;
    private bool _isThumbDragging;
    private double _thumbDragStartValue;
    private double _thumbDragAccumulatedHorizontal;
    private double _thumbDragAccumulatedVertical;

    #endregion

    #region Visual Children

    /// <inheritdoc />
    public override int VisualChildrenCount
    {
        get
        {
            var count = 0;
            if (_decreaseButton != null) count++;
            if (_thumb != null) count++;
            if (_increaseButton != null) count++;
            return count;
        }
    }

    /// <inheritdoc />
    public override Visual? GetVisualChild(int index)
    {
        var currentIndex = 0;

        if (_decreaseButton != null)
        {
            if (index == currentIndex) return _decreaseButton;
            currentIndex++;
        }

        if (_thumb != null)
        {
            if (index == currentIndex) return _thumb;
            currentIndex++;
        }

        if (_increaseButton != null)
        {
            if (index == currentIndex) return _increaseButton;
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var desiredSize = new Size();

        _decreaseButton?.Measure(availableSize);
        _thumb?.Measure(availableSize);
        _increaseButton?.Measure(availableSize);

        double width, height;
        if (Orientation == Orientation.Horizontal)
        {
            height = Math.Max(
                Math.Max(_decreaseButton?.DesiredSize.Height ?? 0, _thumb?.DesiredSize.Height ?? 0),
                _increaseButton?.DesiredSize.Height ?? 0);
            width = (_decreaseButton?.DesiredSize.Width ?? 0) +
                    (_thumb?.DesiredSize.Width ?? 0) +
                    (_increaseButton?.DesiredSize.Width ?? 0);
        }
        else
        {
            width = Math.Max(
                Math.Max(_decreaseButton?.DesiredSize.Width ?? 0, _thumb?.DesiredSize.Width ?? 0),
                _increaseButton?.DesiredSize.Width ?? 0);
            height = (_decreaseButton?.DesiredSize.Height ?? 0) +
                     (_thumb?.DesiredSize.Height ?? 0) +
                     (_increaseButton?.DesiredSize.Height ?? 0);
        }

        return new Size(width, height);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        return ArrangeParts(finalSize);
    }

    internal void RefreshThumbVisualLayout()
    {
        if (!IsArrangeValid || RenderSize.Width <= 0 || RenderSize.Height <= 0)
        {
            InvalidateArrange();
            return;
        }

        ArrangeParts(RenderSize);
        InvalidateVisual();
    }

    private Size ArrangeParts(Size finalSize)
    {
        static double CoerceFiniteNonNegative(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            {
                return 0;
            }

            return value;
        }

        static double Clamp01(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return 0;
            }

            return Math.Clamp(value, 0, 1);
        }

        var isHorizontal = Orientation == Orientation.Horizontal;
        var minimum = double.IsNaN(Minimum) || double.IsInfinity(Minimum) ? 0 : Minimum;
        var maximum = double.IsNaN(Maximum) || double.IsInfinity(Maximum) ? minimum : Maximum;
        var range = CoerceFiniteNonNegative(maximum - minimum);
        var viewportSize = CoerceFiniteNonNegative(ViewportSize);
        var crossAxisThicknessOverride = ThumbCrossAxisThickness;
        var hasCrossAxisThicknessOverride = !double.IsNaN(crossAxisThicknessOverride) &&
                                            !double.IsInfinity(crossAxisThicknessOverride) &&
                                            crossAxisThicknessOverride > 0;

        var trackLength = isHorizontal ? finalSize.Width : finalSize.Height;
        if (double.IsNaN(trackLength) || double.IsInfinity(trackLength))
        {
            trackLength = 0;
        }

        if (trackLength <= 0)
        {
            _density = 0;

            if (isHorizontal)
            {
                _decreaseButton?.Arrange(new Rect(0, 0, 0, finalSize.Height));
                _thumb?.Arrange(new Rect(0, 0, 0, finalSize.Height));
                _increaseButton?.Arrange(new Rect(0, 0, 0, finalSize.Height));
            }
            else
            {
                _decreaseButton?.Arrange(new Rect(0, 0, finalSize.Width, 0));
                _thumb?.Arrange(new Rect(0, 0, finalSize.Width, 0));
                _increaseButton?.Arrange(new Rect(0, 0, finalSize.Width, 0));
            }

            return finalSize;
        }

        double minThumbSize = 10;
        if (_thumb != null)
        {
            double axisMin = isHorizontal ? _thumb.MinWidth : _thumb.MinHeight;
            if (!double.IsNaN(axisMin) && !double.IsInfinity(axisMin) && axisMin > 0)
            {
                minThumbSize = Math.Max(minThumbSize, axisMin);
            }
        }
        minThumbSize = Math.Min(minThumbSize, trackLength);

        // Calculate thumb size
        double thumbSize;
        if (viewportSize > 0)
        {
            if (range > 0)
            {
                // ScrollBar mode: thumb size is proportional to viewport
                var extent = range + viewportSize;
                var ratio = extent > 0 && !double.IsInfinity(extent)
                    ? viewportSize / extent
                    : 0;
                ratio = Clamp01(ratio);
                thumbSize = Math.Clamp(Math.Max(trackLength * ratio, minThumbSize), minThumbSize, trackLength);
            }
            else
            {
                // ScrollBar mode with no scrollable range: fill the track so thumb remains clearly visible.
                thumbSize = trackLength;
            }
        }
        else
        {
            // Slider mode: fixed thumb size
            var rawThumbSize = isHorizontal
                ? (_thumb?.DesiredSize.Width ?? 16)
                : (_thumb?.DesiredSize.Height ?? 16);
            if (double.IsNaN(rawThumbSize) || double.IsInfinity(rawThumbSize) || rawThumbSize <= 0)
                rawThumbSize = minThumbSize;

            thumbSize = Math.Clamp(rawThumbSize, minThumbSize, trackLength);
        }

        // Calculate track length and thumb position
        var availableLength = Math.Max(0, trackLength - thumbSize);

        double ratio2 = 0;
        if (range > 0)
        {
            var value = double.IsNaN(Value) || double.IsInfinity(Value) ? minimum : Value;
            ratio2 = (value - minimum) / range;
        }
        ratio2 = Clamp01(ratio2);

        if (IsDirectionReversed)
        {
            ratio2 = 1 - ratio2;
        }

        var thumbOffset = availableLength * ratio2;

        // Calculate density for value conversion
        _density = range > 0 ? availableLength / range : 0;
        if (double.IsNaN(_density) || double.IsInfinity(_density) || _density < 0)
        {
            _density = 0;
        }

        // Arrange children
        var isScrollBarMode = viewportSize > 0;

        if (isHorizontal)
        {
            var decreaseWidth = thumbOffset;
            var increaseWidth = availableLength - thumbOffset;
            var thumbTop = 0.0;
            var thumbHeight = finalSize.Height;

            if (isScrollBarMode)
            {
                if (hasCrossAxisThicknessOverride)
                {
                    thumbHeight = Math.Min(finalSize.Height, crossAxisThicknessOverride);
                    thumbTop = Math.Max(0, (finalSize.Height - thumbHeight) / 2);
                }
                else
                {
                    var inset = Math.Min(2.0, Math.Max(0, finalSize.Height / 2));
                    thumbTop = inset;
                    thumbHeight = Math.Max(0, finalSize.Height - inset * 2);
                }
            }

            _decreaseButton?.Arrange(new Rect(0, 0, decreaseWidth, finalSize.Height));
            _thumb?.Arrange(new Rect(decreaseWidth, thumbTop, thumbSize, thumbHeight));
            _increaseButton?.Arrange(new Rect(decreaseWidth + thumbSize, 0, increaseWidth, finalSize.Height));
        }
        else
        {
            var decreaseHeight = IsDirectionReversed ? availableLength - thumbOffset : thumbOffset;
            var increaseHeight = IsDirectionReversed ? thumbOffset : availableLength - thumbOffset;
            var thumbLeft = 0.0;
            var thumbWidth = finalSize.Width;

            if (isScrollBarMode)
            {
                if (hasCrossAxisThicknessOverride)
                {
                    thumbWidth = Math.Min(finalSize.Width, crossAxisThicknessOverride);
                    thumbLeft = Math.Max(0, (finalSize.Width - thumbWidth) / 2);
                }
                else
                {
                    var inset = Math.Min(2.0, Math.Max(0, finalSize.Width / 2));
                    thumbLeft = inset;
                    thumbWidth = Math.Max(0, finalSize.Width - inset * 2);
                }
            }

            if (IsDirectionReversed)
            {
                _increaseButton?.Arrange(new Rect(0, 0, finalSize.Width, increaseHeight));
                _thumb?.Arrange(new Rect(thumbLeft, increaseHeight, thumbWidth, thumbSize));
                _decreaseButton?.Arrange(new Rect(0, increaseHeight + thumbSize, finalSize.Width, decreaseHeight));
            }
            else
            {
                _decreaseButton?.Arrange(new Rect(0, 0, finalSize.Width, decreaseHeight));
                _thumb?.Arrange(new Rect(thumbLeft, decreaseHeight, thumbWidth, thumbSize));
                _increaseButton?.Arrange(new Rect(0, decreaseHeight + thumbSize, finalSize.Width, increaseHeight));
            }
        }

        return finalSize;
    }

    #endregion

    #region Value Conversion Methods

    /// <summary>
    /// Converts a point to a value on the track.
    /// </summary>
    /// <param name="pt">The point to convert.</param>
    /// <returns>The value at the specified point.</returns>
    public double ValueFromPoint(Point pt)
    {
        double val;

        if (Orientation == Orientation.Horizontal)
        {
            val = Value + ValueFromDistance(pt.X - (_thumb?.RenderSize.Width ?? 0) / 2, 0);
        }
        else
        {
            val = Value + ValueFromDistance(0, pt.Y - (_thumb?.RenderSize.Height ?? 0) / 2);
        }

        return Math.Clamp(val, Minimum, Maximum);
    }

    /// <summary>
    /// Converts a drag distance to a value change.
    /// </summary>
    /// <param name="horizontal">The horizontal distance.</param>
    /// <param name="vertical">The vertical distance.</param>
    /// <returns>The value change.</returns>
    public double ValueFromDistance(double horizontal, double vertical)
    {
        if (_density == 0)
        {
            return 0;
        }

        double change;
        if (Orientation == Orientation.Horizontal)
        {
            change = horizontal / _density;
        }
        else
        {
            change = vertical / _density;
        }

        if (IsDirectionReversed)
        {
            change = -change;
        }

        return change;
    }

    #endregion

    #region Event Handlers

    private void OnThumbDragStarted(object sender, DragStartedEventArgs e)
    {
        BeginThumbDrag();
    }

    private void OnThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_isThumbDragging)
        {
            BeginThumbDrag();
        }

        _thumbDragAccumulatedHorizontal += e.HorizontalChange;
        _thumbDragAccumulatedVertical += e.VerticalChange;

        var newValue = _thumbDragStartValue + ValueFromDistance(_thumbDragAccumulatedHorizontal, _thumbDragAccumulatedVertical);
        newValue = Math.Clamp(newValue, Minimum, Maximum);

        // This should be bound to the parent control's Value property
        // For now, we'll just update our local value
        Value = newValue;
    }

    private void OnThumbDragCompleted(object sender, DragCompletedEventArgs e)
    {
        EndThumbDrag();
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Track track)
        {
            track.InvalidateArrange();
        }
    }

    private static void OnThumbCrossAxisThicknessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Track track)
        {
            track.RefreshThumbVisualLayout();
        }
    }

    private void BeginThumbDrag()
    {
        _isThumbDragging = true;
        _thumbDragStartValue = Value;
        _thumbDragAccumulatedHorizontal = 0;
        _thumbDragAccumulatedVertical = 0;
    }

    private void EndThumbDrag()
    {
        _isThumbDragging = false;
        _thumbDragAccumulatedHorizontal = 0;
        _thumbDragAccumulatedVertical = 0;
    }

    #endregion
}
