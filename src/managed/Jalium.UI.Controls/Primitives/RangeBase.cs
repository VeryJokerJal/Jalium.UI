namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents an element that has a value within a specific range.
/// This is the abstract base class for Slider, ProgressBar, and ScrollBar.
/// </summary>
public abstract class RangeBase : Control
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Minimum dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(RangeBase),
            new PropertyMetadata(0.0, OnMinimumChanged));

    /// <summary>
    /// Identifies the Maximum dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(RangeBase),
            new PropertyMetadata(1.0, OnMaximumChanged));

    /// <summary>
    /// Identifies the Value dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(RangeBase),
            new PropertyMetadata(0.0, OnValuePropertyChanged, CoerceValue));

    /// <summary>
    /// Identifies the SmallChange dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SmallChangeProperty =
        DependencyProperty.Register(nameof(SmallChange), typeof(double), typeof(RangeBase),
            new PropertyMetadata(0.1));

    /// <summary>
    /// Identifies the LargeChange dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty LargeChangeProperty =
        DependencyProperty.Register(nameof(LargeChange), typeof(double), typeof(RangeBase),
            new PropertyMetadata(1.0));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the ValueChanged routed event.
    /// </summary>
    public static readonly RoutedEvent ValueChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(ValueChanged), RoutingStrategy.Bubble,
            typeof(RoutedPropertyChangedEventHandler<double>), typeof(RangeBase));

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
    /// Gets or sets the minimum value of the range.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty)!;
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum value of the range.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty)!;
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>
    /// Gets or sets the current value of the range.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double Value
    {
        get => (double)GetValue(ValueProperty)!;
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the value to add or subtract when the user moves the control a small amount.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double SmallChange
    {
        get => (double)GetValue(SmallChangeProperty)!;
        set => SetValue(SmallChangeProperty, value);
    }

    /// <summary>
    /// Gets or sets the value to add or subtract when the user moves the control a large amount.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double LargeChange
    {
        get => (double)GetValue(LargeChangeProperty)!;
        set => SetValue(LargeChangeProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="RangeBase"/> class.
    /// </summary>
    protected RangeBase()
    {
        Focusable = true;
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnMinimumChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RangeBase rangeBase)
        {
            rangeBase.CoerceValueInternal();
            rangeBase.OnMinimumChanged((double)(e.OldValue ?? 0.0), (double)(e.NewValue ?? 0.0));
        }
    }

    private static void OnMaximumChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RangeBase rangeBase)
        {
            rangeBase.CoerceValueInternal();
            rangeBase.OnMaximumChanged((double)(e.OldValue ?? 1.0), (double)(e.NewValue ?? 1.0));
        }
    }

    private static void OnValuePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RangeBase rangeBase)
        {
            rangeBase.OnValueChanged((double)(e.OldValue ?? 0.0), (double)(e.NewValue ?? 0.0));
        }
    }

    private static object? CoerceValue(DependencyObject d, object? value)
    {
        if (d is RangeBase rangeBase && value is double doubleValue)
        {
            var min = rangeBase.Minimum;
            var max = rangeBase.Maximum;

            if (max < min)
            {
                max = min;
            }

            if (doubleValue < min)
            {
                return min;
            }

            if (doubleValue > max)
            {
                return max;
            }

            return doubleValue;
        }
        return value;
    }

    private void CoerceValueInternal()
    {
        var currentValue = Value;
        var coercedValue = (double)(CoerceValue(this, currentValue) ?? currentValue);
        if (Math.Abs(coercedValue - currentValue) > double.Epsilon)
        {
            Value = coercedValue;
        }
    }

    #endregion

    #region Virtual Methods

    /// <summary>
    /// Called when the Minimum property changes.
    /// </summary>
    /// <param name="oldMinimum">The old minimum value.</param>
    /// <param name="newMinimum">The new minimum value.</param>
    protected virtual void OnMinimumChanged(double oldMinimum, double newMinimum)
    {
        InvalidateVisual();
    }

    /// <summary>
    /// Called when the Maximum property changes.
    /// </summary>
    /// <param name="oldMaximum">The old maximum value.</param>
    /// <param name="newMaximum">The new maximum value.</param>
    protected virtual void OnMaximumChanged(double oldMaximum, double newMaximum)
    {
        InvalidateVisual();
    }

    /// <summary>
    /// Called when the Value property changes.
    /// </summary>
    /// <param name="oldValue">The old value.</param>
    /// <param name="newValue">The new value.</param>
    protected virtual void OnValueChanged(double oldValue, double newValue)
    {
        InvalidateVisual();
        RaiseEvent(new RoutedPropertyChangedEventArgs<double>(oldValue, newValue, ValueChangedEvent));
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Gets the ratio of the current value within the range (0.0 to 1.0).
    /// </summary>
    protected double GetValueRatio()
    {
        var range = Maximum - Minimum;
        if (range <= 0)
        {
            return 0;
        }
        return (Value - Minimum) / range;
    }

    #endregion
}
