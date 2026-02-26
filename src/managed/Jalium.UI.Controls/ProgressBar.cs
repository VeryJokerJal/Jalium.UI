using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// Indicates the progress of an operation.
/// </summary>
public sealed class ProgressBar : Control
{
    // Cached brushes for OnRender
    private static readonly SolidColorBrush s_trackBrush = new(Color.FromRgb(45, 45, 45));
    private static readonly SolidColorBrush s_accentBrush = new(Color.FromRgb(0, 120, 212));

    #region Dependency Properties

    /// <summary>
    /// Identifies the Minimum dependency property.
    /// </summary>
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(ProgressBar),
            new PropertyMetadata(0.0, OnRangePropertyChanged));

    /// <summary>
    /// Identifies the Maximum dependency property.
    /// </summary>
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(ProgressBar),
            new PropertyMetadata(100.0, OnRangePropertyChanged));

    /// <summary>
    /// Identifies the Value dependency property.
    /// </summary>
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(ProgressBar),
            new PropertyMetadata(0.0, OnValuePropertyChanged, CoerceValue));

    /// <summary>
    /// Identifies the IsIndeterminate dependency property.
    /// </summary>
    public static readonly DependencyProperty IsIndeterminateProperty =
        DependencyProperty.Register(nameof(IsIndeterminate), typeof(bool), typeof(ProgressBar),
            new PropertyMetadata(false, OnIsIndeterminateChanged));

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(ProgressBar),
            new PropertyMetadata(Orientation.Horizontal, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ProgressBrush dependency property.
    /// </summary>
    public static readonly DependencyProperty ProgressBrushProperty =
        DependencyProperty.Register(nameof(ProgressBrush), typeof(Brush), typeof(ProgressBar),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the ValueChanged routed event.
    /// </summary>
    public static readonly RoutedEvent ValueChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(ValueChanged), RoutingStrategy.Bubble,
            typeof(RoutedPropertyChangedEventHandler<double>), typeof(ProgressBar));

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
    /// Gets or sets the minimum value of the ProgressBar.
    /// </summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty)!;
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum value of the ProgressBar.
    /// </summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty)!;
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>
    /// Gets or sets the current value of the ProgressBar.
    /// </summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty)!;
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the ProgressBar shows indeterminate progress.
    /// </summary>
    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty)!;
        set => SetValue(IsIndeterminateProperty, value);
    }

    /// <summary>
    /// Gets or sets the orientation of the ProgressBar.
    /// </summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for the progress indicator.
    /// </summary>
    public Brush? ProgressBrush
    {
        get => (Brush?)GetValue(ProgressBrushProperty);
        set => SetValue(ProgressBrushProperty, value);
    }

    /// <summary>
    /// Gets the current progress as a percentage (0.0 to 1.0).
    /// </summary>
    public double Percentage
    {
        get
        {
            var range = Maximum - Minimum;
            if (range <= 0) return 0;
            return Math.Clamp((Value - Minimum) / range, 0, 1);
        }
    }

    #endregion

    #region Template Parts

    private Border? _trackBorder;
    private Border? _indicatorBorder;

    #endregion

    #region Constructor

    private double _indeterminateOffset = 0;
    private double _indeterminateDirection = 1.0;
    private const double IndeterminateBlockWidth = 0.3; // 30% of track width
    private const double IndeterminateSpeed = 0.02; // Animation speed per tick
    private DispatcherTimer? _animationTimer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressBar"/> class.
    /// </summary>
    public ProgressBar()
    {
        // Default size
        Height = 8;
    }

    #endregion

    #region Template

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _trackBorder = GetTemplateChild("PART_Track") as Border;
        _indicatorBorder = GetTemplateChild("PART_Indicator") as Border;

        UpdateIndicator();
    }

    private void UpdateIndicator()
    {
        if (_indicatorBorder == null) return;

        var isVertical = Orientation == Orientation.Vertical;

        if (IsIndeterminate)
        {
            // For indeterminate mode, animate the indicator
            var totalSize = isVertical ? RenderSize.Height : RenderSize.Width;
            var indicatorSize = totalSize * IndeterminateBlockWidth;
            var offset = (totalSize - indicatorSize) * _indeterminateOffset;

            if (isVertical)
            {
                _indicatorBorder.Height = indicatorSize;
                _indicatorBorder.Width = double.NaN;
                _indicatorBorder.Margin = new Thickness(0, 0, 0, offset);
            }
            else
            {
                _indicatorBorder.Width = indicatorSize;
                _indicatorBorder.Height = double.NaN;
                _indicatorBorder.Margin = new Thickness(offset, 0, 0, 0);
            }
        }
        else
        {
            // For determinate mode, set size based on percentage
            var totalSize = isVertical ? RenderSize.Height : RenderSize.Width;
            var indicatorSize = totalSize * Percentage;

            if (isVertical)
            {
                _indicatorBorder.Height = indicatorSize;
                _indicatorBorder.Width = double.NaN;
                // Vertical progress fills from bottom to top
                _indicatorBorder.VerticalAlignment = VerticalAlignment.Bottom;
            }
            else
            {
                _indicatorBorder.Width = indicatorSize;
                _indicatorBorder.Height = double.NaN;
            }
            _indicatorBorder.Margin = new Thickness(0);
        }
    }

    #endregion

    #region Animation

    private void StartIndeterminateAnimation()
    {
        if (_animationTimer != null) return;

        _animationTimer = new DispatcherTimer
        {
            Interval = CompositionTarget.FrameInterval
        };
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Start();
    }

    private void StopIndeterminateAnimation()
    {
        if (_animationTimer != null)
        {
            _animationTimer.Stop();
            _animationTimer.Tick -= OnAnimationTick;
            _animationTimer = null;
        }
        _indeterminateOffset = 0;
        _indeterminateDirection = 1.0;
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (!IsIndeterminate) return;

        // Bounce animation: move back and forth
        _indeterminateOffset += IndeterminateSpeed * _indeterminateDirection;

        if (_indeterminateOffset >= 1.0)
        {
            _indeterminateOffset = 1.0;
            _indeterminateDirection = -1.0;
        }
        else if (_indeterminateOffset <= 0.0)
        {
            _indeterminateOffset = 0.0;
            _indeterminateDirection = 1.0;
        }

        // DispatcherTimer fires on the UI thread, so all property
        // modifications (Width, Margin) are thread-safe.
        UpdateIndicator();
        InvalidateVisual();
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnSizeChanged(sizeInfo);

        // UpdateIndicator reads RenderSize.Width to compute indicator width.
        // Property change callbacks fire BEFORE layout, so RenderSize is (0,0) at that point.
        // OnSizeChanged fires INSIDE ArrangeCore AFTER _renderSize is set (WPF pattern),
        // so RenderSize.Width is correct here. The LayoutManager's iterative loop
        // will re-process the indicator's measure/arrange in the same layout pass.
        UpdateIndicator();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // MUST measure template children (Grid with PART_Track + PART_Indicator)
        // so they get the correct PreviousAvailableSize for subsequent re-layout.
        // Without this, the indicator border never gets properly measured.
        base.MeasureOverride(availableSize);

        // ProgressBar returns its own desired size (not template root's)
        if (Orientation == Orientation.Horizontal)
        {
            var height = double.IsNaN(Height) || Height <= 0 ? 8 : Height;
            return new Size(Math.Min(availableSize.Width, double.IsPositiveInfinity(availableSize.Width) ? 200 : availableSize.Width), height);
        }
        else
        {
            var width = double.IsNaN(Width) || Width <= 0 ? 8 : Width;
            return new Size(width, Math.Min(availableSize.Height, double.IsPositiveInfinity(availableSize.Height) ? 200 : availableSize.Height));
        }
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        // Template-based rendering: indicator is updated from property change callbacks
        // and animation ticks (NOT here). Modifying _indicatorBorder.Width during OnRender
        // triggers InvalidateMeasure, but UpdateLayout() already ran for this frame,
        // so the new width wouldn't take effect until the NEXT frame — causing a 1-frame
        // delay where the indicator renders at its old size (often 0).
        if (_indicatorBorder != null)
            return;

        if (drawingContext is not DrawingContext dc)
            return;

        var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);

        // Draw track background
        var trackBrush = Background ?? s_trackBrush;
        dc.DrawRoundedRectangle(trackBrush, null, bounds, 4, 4);

        // Draw progress
        var progressBrush = ProgressBrush ?? s_accentBrush;

        if (IsIndeterminate)
        {
            DrawIndeterminateProgress(dc, bounds, progressBrush);
        }
        else
        {
            DrawDeterminateProgress(dc, bounds, progressBrush);
        }
    }

    private void DrawDeterminateProgress(DrawingContext dc, Rect bounds, Brush progressBrush)
    {
        var percentage = Percentage;
        if (percentage <= 0) return;

        Rect progressRect;
        if (Orientation == Orientation.Horizontal)
        {
            var progressWidth = bounds.Width * percentage;
            progressRect = new Rect(0, 0, progressWidth, bounds.Height);
        }
        else
        {
            var progressHeight = bounds.Height * percentage;
            progressRect = new Rect(0, bounds.Height - progressHeight, bounds.Width, progressHeight);
        }

        dc.DrawRoundedRectangle(progressBrush, null, progressRect, 4, 4);
    }

    private void DrawIndeterminateProgress(DrawingContext dc, Rect bounds, Brush progressBrush)
    {
        // Draw an animated block that moves across the track
        var blockSize = Orientation == Orientation.Horizontal
            ? bounds.Width * IndeterminateBlockWidth
            : bounds.Height * IndeterminateBlockWidth;

        Rect progressRect;
        if (Orientation == Orientation.Horizontal)
        {
            var x = (bounds.Width - blockSize) * _indeterminateOffset;
            progressRect = new Rect(x, 0, blockSize, bounds.Height);
        }
        else
        {
            var y = (bounds.Height - blockSize) * _indeterminateOffset;
            progressRect = new Rect(0, y, bounds.Width, blockSize);
        }

        dc.DrawRoundedRectangle(progressBrush, null, progressRect, 4, 4);
    }

    /// <summary>
    /// Updates the indeterminate animation offset.
    /// Call this method periodically to animate the indeterminate progress.
    /// </summary>
    public void UpdateIndeterminateAnimation(double offset)
    {
        _indeterminateOffset = Math.Clamp(offset, 0, 1);
        if (IsIndeterminate)
        {
            UpdateIndicator();
            InvalidateVisual();
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnRangePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ProgressBar progressBar)
        {
            // Re-coerce value to stay within new range by re-setting it
            var currentValue = progressBar.Value;
            var coercedValue = (double)(CoerceValue(progressBar, currentValue) ?? currentValue);
            if (coercedValue != currentValue)
            {
                progressBar.Value = coercedValue;
            }
            progressBar.UpdateIndicator();
            progressBar.InvalidateVisual();
        }
    }

    private static void OnValuePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ProgressBar progressBar)
        {
            progressBar.OnValueChanged((double)(e.OldValue ?? 0.0), (double)(e.NewValue ?? 0.0));
        }
    }

    private static void OnIsIndeterminateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ProgressBar progressBar)
        {
            var isIndeterminate = (bool)(e.NewValue ?? false);
            if (isIndeterminate && progressBar.Visibility == Visibility.Visible)
            {
                progressBar.StartIndeterminateAnimation();
            }
            else
            {
                progressBar.StopIndeterminateAnimation();
            }
            progressBar.UpdateIndicator();
            progressBar.InvalidateVisual();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ProgressBar progressBar)
        {
            progressBar.UpdateIndicator();
            progressBar.InvalidateVisual();
        }
    }

    private static object? CoerceValue(DependencyObject d, object? value)
    {
        if (d is ProgressBar progressBar && value is double doubleValue)
        {
            return Math.Clamp(doubleValue, progressBar.Minimum, progressBar.Maximum);
        }
        return value;
    }

    /// <summary>
    /// Called when the Value property changes.
    /// </summary>
    protected void OnValueChanged(double oldValue, double newValue)
    {
        UpdateIndicator();
        InvalidateVisual();
        RaiseEvent(new RoutedPropertyChangedEventArgs<double>(oldValue, newValue, ValueChangedEvent));
    }

    #endregion
}

/// <summary>
/// Provides data for the ValueChanged event.
/// </summary>
public sealed class RoutedPropertyChangedEventArgs<T> : RoutedEventArgs
{
    /// <summary>
    /// Gets the old value.
    /// </summary>
    public T OldValue { get; }

    /// <summary>
    /// Gets the new value.
    /// </summary>
    public T NewValue { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RoutedPropertyChangedEventArgs{T}"/> class.
    /// </summary>
    public RoutedPropertyChangedEventArgs(T oldValue, T newValue, RoutedEvent routedEvent)
        : base(routedEvent)
    {
        OldValue = oldValue;
        NewValue = newValue;
    }
}

/// <summary>
/// Represents the method that handles value change events.
/// </summary>
public delegate void RoutedPropertyChangedEventHandler<T>(object sender, RoutedPropertyChangedEventArgs<T> e);
