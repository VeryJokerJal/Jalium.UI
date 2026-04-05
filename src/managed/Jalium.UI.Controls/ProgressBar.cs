using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Indicates the progress of an operation.
/// </summary>
public class ProgressBar : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.ProgressBarAutomationPeer(this);
    }

    // Cached brushes for OnRender
    private static readonly SolidColorBrush s_trackBrush = new(Color.FromRgb(45, 45, 45));
    private static readonly SolidColorBrush s_accentBrush = new(ThemeColors.ProgressBarFill);
    private static readonly SolidColorBrush s_disabledAccentBrush = new(Color.FromRgb(90, 90, 90));
    private static readonly CornerRadius s_defaultCornerRadius = new(4);

    #region Dependency Properties

    /// <summary>
    /// Identifies the Minimum dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(ProgressBar),
            new PropertyMetadata(0.0, OnRangePropertyChanged));

    /// <summary>
    /// Identifies the Maximum dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(ProgressBar),
            new PropertyMetadata(100.0, OnRangePropertyChanged));

    /// <summary>
    /// Identifies the Value dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(ProgressBar),
            new PropertyMetadata(0.0, OnValuePropertyChanged, CoerceValue));

    /// <summary>
    /// Identifies the IsIndeterminate dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsIndeterminateProperty =
        DependencyProperty.Register(nameof(IsIndeterminate), typeof(bool), typeof(ProgressBar),
            new PropertyMetadata(false, OnIsIndeterminateChanged));

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(ProgressBar),
            new PropertyMetadata(Orientation.Horizontal, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ProgressBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
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
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty)!;
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum value of the ProgressBar.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty)!;
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>
    /// Gets or sets the current value of the ProgressBar.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double Value
    {
        get => (double)GetValue(ValueProperty)!;
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the ProgressBar shows indeterminate progress.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsIndeterminate
    {
        get => (bool)GetValue(IsIndeterminateProperty)!;
        set => SetValue(IsIndeterminateProperty, value);
    }

    /// <summary>
    /// Gets or sets the orientation of the ProgressBar.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used for the progress indicator.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
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
    private const double IndeterminateSpeedPerSecond = 1.2; // Track lengths per second
    private const double MaxAnimationDeltaSeconds = 0.1; // Clamp to avoid huge jumps after stalls
    private const long AnimationUpdateIntervalMs = 1; // Uncapped by render speed (game-loop style)
    private bool _isAnimationSubscribed;
    private long _lastAnimationTickMs;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressBar"/> class.
    /// </summary>
    public ProgressBar()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
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

        _indicatorBorder.Background = ResolveProgressBrush();
        _indicatorBorder.CornerRadius = ResolveCornerRadius();

        var isVertical = Orientation == Orientation.Vertical;

        if (IsIndeterminate)
        {
            // For indeterminate mode, set indicator geometry once and animate offset per frame.
            var totalSize = isVertical ? RenderSize.Height : RenderSize.Width;
            var indicatorSize = totalSize * IndeterminateBlockWidth;

            if (isVertical)
            {
                _indicatorBorder.Height = indicatorSize;
                _indicatorBorder.Width = double.NaN;
                _indicatorBorder.Margin = new Thickness(0);
                _indicatorBorder.HorizontalAlignment = HorizontalAlignment.Stretch;
                _indicatorBorder.VerticalAlignment = VerticalAlignment.Top;
            }
            else
            {
                _indicatorBorder.Width = indicatorSize;
                _indicatorBorder.Height = double.NaN;
                _indicatorBorder.Margin = new Thickness(0);
                _indicatorBorder.HorizontalAlignment = HorizontalAlignment.Left;
                _indicatorBorder.VerticalAlignment = VerticalAlignment.Stretch;
            }

            UpdateIndeterminateRenderOffset();
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
                _indicatorBorder.HorizontalAlignment = HorizontalAlignment.Stretch;
                _indicatorBorder.VerticalAlignment = VerticalAlignment.Bottom;
            }
            else
            {
                _indicatorBorder.Width = indicatorSize;
                _indicatorBorder.Height = double.NaN;
                _indicatorBorder.HorizontalAlignment = HorizontalAlignment.Left;
                _indicatorBorder.VerticalAlignment = VerticalAlignment.Stretch;
            }
            _indicatorBorder.Margin = new Thickness(0);
            _indicatorBorder.RenderOffset = default;
        }
    }

    #endregion

    #region Animation

    private void StartIndeterminateAnimation()
    {
        if (_isAnimationSubscribed) return;

        // Use the centralized frame loop for maximum smoothness and to keep
        // animation updates synchronized with the render pipeline.
        CompositionTarget.Rendering += OnAnimationTick;
        CompositionTarget.Subscribe();
        _isAnimationSubscribed = true;
        _lastAnimationTickMs = Environment.TickCount64;
    }

    private void StopIndeterminateAnimation()
    {
        if (_isAnimationSubscribed)
        {
            CompositionTarget.Rendering -= OnAnimationTick;
            CompositionTarget.Unsubscribe();
            _isAnimationSubscribed = false;
        }
        _lastAnimationTickMs = 0;
        _indeterminateOffset = 0;
        _indeterminateDirection = 1.0;
        if (_indicatorBorder != null)
        {
            _indicatorBorder.RenderOffset = default;
        }
    }

    private void UpdateIndeterminateRenderOffset()
    {
        if (_indicatorBorder == null || !IsIndeterminate)
        {
            return;
        }

        var isVertical = Orientation == Orientation.Vertical;
        var totalSize = isVertical ? RenderSize.Height : RenderSize.Width;
        var indicatorSize = totalSize * IndeterminateBlockWidth;
        var offset = (totalSize - indicatorSize) * _indeterminateOffset;

        _indicatorBorder.RenderOffset = isVertical
            ? new Point(0, offset)
            : new Point(offset, 0);
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (!IsIndeterminate) return;

        if (!ShouldRunIndeterminateAnimation())
        {
            StopIndeterminateAnimation();
            return;
        }

        long nowMs = Environment.TickCount64;
        if (_lastAnimationTickMs <= 0)
        {
            _lastAnimationTickMs = nowMs;
            return;
        }

        long elapsedMs = nowMs - _lastAnimationTickMs;
        if (elapsedMs < AnimationUpdateIntervalMs)
        {
            return;
        }
        _lastAnimationTickMs = nowMs;

        // Time-based movement keeps animation speed stable across frame/GPU changes.
        double deltaSeconds = Math.Clamp(elapsedMs / 1000.0, 0.0, MaxAnimationDeltaSeconds);
        if (deltaSeconds <= 0)
        {
            return;
        }

        var next = _indeterminateOffset + (IndeterminateSpeedPerSecond * deltaSeconds * _indeterminateDirection);
        while (next > 1.0 || next < 0.0)
        {
            if (next > 1.0)
            {
                next = 2.0 - next;
                _indeterminateDirection = -1.0;
            }
            else
            {
                next = -next;
                _indeterminateDirection = 1.0;
            }
        }
        _indeterminateOffset = next;

        UpdateIndeterminateRenderOffset();
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
        // so the new width wouldn't take effect until the NEXT frame 闁?causing a 1-frame
        // delay where the indicator renders at its old size (often 0).
        if (_indicatorBorder != null)
            return;

        if (drawingContext is not DrawingContext dc)
            return;

        var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);
        var cornerRadius = ResolveCornerRadius();

        // Draw track background
        var trackBrush = ResolveTrackBrush();
        dc.DrawRoundedRectangle(trackBrush, null, bounds, cornerRadius);

        // Draw progress
        var progressBrush = ResolveProgressBrush();

        if (IsIndeterminate)
        {
            DrawIndeterminateProgress(dc, bounds, progressBrush, cornerRadius);
        }
        else
        {
            DrawDeterminateProgress(dc, bounds, progressBrush, cornerRadius);
        }
    }

    private Brush ResolveTrackBrush()
    {
        return Background
            ?? ResolveThemeBrush("SliderTrack", s_trackBrush, "ControlBackground");
    }

    private Brush ResolveProgressBrush()
    {
        if (ProgressBrush != null)
        {
            return ProgressBrush;
        }

        if (HasLocalValue(Control.ForegroundProperty) && Foreground != null)
        {
            return Foreground;
        }

        return IsEnabled
            ? ResolveThemeBrush("AccentBrush", s_accentBrush, "AccentFillColorDefaultBrush")
            : ResolveThemeBrush("AccentBrushDisabled", s_disabledAccentBrush, "AccentFillColorDisabledBrush");
    }

    private CornerRadius ResolveCornerRadius()
    {
        var radius = CornerRadius;
        if (radius.TopLeft > 0 || radius.TopRight > 0 || radius.BottomRight > 0 || radius.BottomLeft > 0)
        {
            return radius;
        }

        return s_defaultCornerRadius;
    }

    private Brush ResolveThemeBrush(string resourceKey, Brush fallback, string? secondaryResourceKey = null)
    {
        if (TryFindResource(resourceKey) is Brush brush)
        {
            return brush;
        }

        if (secondaryResourceKey != null && TryFindResource(secondaryResourceKey) is Brush secondaryBrush)
        {
            return secondaryBrush;
        }

        var app = Jalium.UI.Application.Current;
        if (app?.Resources != null)
        {
            if (app.Resources.TryGetValue(resourceKey, out var appResource) && appResource is Brush appBrush)
            {
                return appBrush;
            }

            if (secondaryResourceKey != null &&
                app.Resources.TryGetValue(secondaryResourceKey, out var secondaryAppResource) &&
                secondaryAppResource is Brush secondaryAppBrush)
            {
                return secondaryAppBrush;
            }
        }

        return fallback;
    }

    private void DrawDeterminateProgress(DrawingContext dc, Rect bounds, Brush progressBrush, CornerRadius cornerRadius)
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

        dc.DrawRoundedRectangle(progressBrush, null, progressRect, cornerRadius);
    }

    private void DrawIndeterminateProgress(DrawingContext dc, Rect bounds, Brush progressBrush, CornerRadius cornerRadius)
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

        dc.DrawRoundedRectangle(progressBrush, null, progressRect, cornerRadius);
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
            progressBar.UpdateIndeterminateAnimationState();
            progressBar.UpdateIndicator();
            progressBar.InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == VisibilityProperty)
        {
            UpdateIndeterminateAnimationState();
        }
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        UpdateIndeterminateAnimationState();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        StopIndeterminateAnimation();
    }

    private void UpdateIndeterminateAnimationState()
    {
        if (ShouldRunIndeterminateAnimation())
        {
            StartIndeterminateAnimation();
            return;
        }

        if (_isAnimationSubscribed)
        {
            StopIndeterminateAnimation();
        }
    }

    private bool ShouldRunIndeterminateAnimation()
    {
        if (!IsIndeterminate || Visibility != Visibility.Visible)
        {
            return false;
        }

        Visual? current = this;
        while (current != null)
        {
            if (current is IWindowHost)
            {
                return true;
            }

            current = current.VisualParent;
        }

        return false;
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
