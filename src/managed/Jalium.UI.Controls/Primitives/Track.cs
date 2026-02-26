using Jalium.UI.Media;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents the Track element used in ScrollBar and Slider controls.
/// Contains a Thumb and optionally two RepeatButtons for value manipulation.
/// </summary>
public sealed class Track : FrameworkElement
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
                    _thumb.DragDelta -= OnThumbDragDelta;
                }

                _thumb = value;

                if (_thumb != null)
                {
                    AddVisualChild(_thumb);
                    _thumb.DragDelta += OnThumbDragDelta;
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
        var isHorizontal = Orientation == Orientation.Horizontal;
        var range = Math.Max(0, Maximum - Minimum);
        var viewportSize = double.IsNaN(ViewportSize) ? 0 : ViewportSize;

        // Calculate thumb size
        double thumbSize;
        if (viewportSize > 0 && range > 0)
        {
            // ScrollBar mode: thumb size is proportional to viewport
            var extent = range + viewportSize;
            var ratio = viewportSize / extent;
            var trackLength = isHorizontal ? finalSize.Width : finalSize.Height;
            thumbSize = Math.Max(trackLength * ratio, 10); // Minimum thumb size
        }
        else
        {
            // Slider mode: fixed thumb size
            thumbSize = _thumb?.DesiredSize.Width ?? 16;
            if (!isHorizontal)
            {
                thumbSize = _thumb?.DesiredSize.Height ?? 16;
            }
        }

        // Calculate track length and thumb position
        var trackLength2 = isHorizontal ? finalSize.Width : finalSize.Height;
        var availableLength = trackLength2 - thumbSize;

        double ratio2 = 0;
        if (range > 0)
        {
            ratio2 = (Value - Minimum) / range;
        }

        if (IsDirectionReversed)
        {
            ratio2 = 1 - ratio2;
        }

        var thumbOffset = availableLength * ratio2;

        // Calculate density for value conversion
        _density = range > 0 ? availableLength / range : 0;

        // Arrange children
        if (isHorizontal)
        {
            var decreaseWidth = thumbOffset;
            var increaseWidth = availableLength - thumbOffset;

            _decreaseButton?.Arrange(new Rect(0, 0, decreaseWidth, finalSize.Height));
            _thumb?.Arrange(new Rect(decreaseWidth, 0, thumbSize, finalSize.Height));
            _increaseButton?.Arrange(new Rect(decreaseWidth + thumbSize, 0, increaseWidth, finalSize.Height));
        }
        else
        {
            var decreaseHeight = IsDirectionReversed ? availableLength - thumbOffset : thumbOffset;
            var increaseHeight = IsDirectionReversed ? thumbOffset : availableLength - thumbOffset;

            if (IsDirectionReversed)
            {
                _increaseButton?.Arrange(new Rect(0, 0, finalSize.Width, increaseHeight));
                _thumb?.Arrange(new Rect(0, increaseHeight, finalSize.Width, thumbSize));
                _decreaseButton?.Arrange(new Rect(0, increaseHeight + thumbSize, finalSize.Width, decreaseHeight));
            }
            else
            {
                _decreaseButton?.Arrange(new Rect(0, 0, finalSize.Width, decreaseHeight));
                _thumb?.Arrange(new Rect(0, decreaseHeight, finalSize.Width, thumbSize));
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

    private void OnThumbDragDelta(object sender, DragDeltaEventArgs e)
    {
        var newValue = Value + ValueFromDistance(e.HorizontalChange, e.VerticalChange);
        newValue = Math.Clamp(newValue, Minimum, Maximum);

        // This should be bound to the parent control's Value property
        // For now, we'll just update our local value
        Value = newValue;
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Track track)
        {
            track.InvalidateArrange();
        }
    }

    #endregion
}
