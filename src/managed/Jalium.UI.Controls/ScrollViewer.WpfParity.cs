using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

/// <summary>
/// WPF-compatible dependency-property and protected surface for
/// <see cref="ScrollViewer"/>.
/// </summary>
public partial class ScrollViewer
{
    private static readonly DependencyPropertyKey ComputedHorizontalScrollBarVisibilityPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ComputedHorizontalScrollBarVisibility),
            typeof(Visibility),
            typeof(ScrollViewer),
            new PropertyMetadata(Visibility.Visible));

    private static readonly DependencyPropertyKey ComputedVerticalScrollBarVisibilityPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ComputedVerticalScrollBarVisibility),
            typeof(Visibility),
            typeof(ScrollViewer),
            new PropertyMetadata(Visibility.Visible));

    private static readonly DependencyPropertyKey ContentHorizontalOffsetPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ContentHorizontalOffset),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey ContentVerticalOffsetPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ContentVerticalOffset),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey ExtentHeightPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ExtentHeight),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey ExtentWidthPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ExtentWidth),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey HorizontalOffsetPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(HorizontalOffset),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey ScrollableHeightPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ScrollableHeight),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey ScrollableWidthPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ScrollableWidth),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey VerticalOffsetPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(VerticalOffset),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey ViewportHeightPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ViewportHeight),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(0.0));

    private static readonly DependencyPropertyKey ViewportWidthPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(ViewportWidth),
            typeof(double),
            typeof(ScrollViewer),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the computed horizontal-scrollbar visibility property.
    /// </summary>
    public static readonly DependencyProperty ComputedHorizontalScrollBarVisibilityProperty =
        ComputedHorizontalScrollBarVisibilityPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the computed vertical-scrollbar visibility property.
    /// </summary>
    public static readonly DependencyProperty ComputedVerticalScrollBarVisibilityProperty =
        ComputedVerticalScrollBarVisibilityPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the content horizontal-offset property.
    /// </summary>
    public static readonly DependencyProperty ContentHorizontalOffsetProperty =
        ContentHorizontalOffsetPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the content vertical-offset property.
    /// </summary>
    public static readonly DependencyProperty ContentVerticalOffsetProperty =
        ContentVerticalOffsetPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the extent-height property.
    /// </summary>
    public static readonly DependencyProperty ExtentHeightProperty =
        ExtentHeightPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the extent-width property.
    /// </summary>
    public static readonly DependencyProperty ExtentWidthProperty =
        ExtentWidthPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the horizontal-offset property.
    /// </summary>
    public static readonly DependencyProperty HorizontalOffsetProperty =
        HorizontalOffsetPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the scrollable-height property.
    /// </summary>
    public static readonly DependencyProperty ScrollableHeightProperty =
        ScrollableHeightPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the scrollable-width property.
    /// </summary>
    public static readonly DependencyProperty ScrollableWidthProperty =
        ScrollableWidthPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the vertical-offset property.
    /// </summary>
    public static readonly DependencyProperty VerticalOffsetProperty =
        VerticalOffsetPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the viewport-height property.
    /// </summary>
    public static readonly DependencyProperty ViewportHeightProperty =
        ViewportHeightPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the viewport-width property.
    /// </summary>
    public static readonly DependencyProperty ViewportWidthProperty =
        ViewportWidthPropertyKey.DependencyProperty;

    /// <summary>
    /// Gets the current computed horizontal-scrollbar visibility.
    /// </summary>
    public Visibility ComputedHorizontalScrollBarVisibility =>
        (Visibility)GetValue(ComputedHorizontalScrollBarVisibilityProperty)!;

    /// <summary>
    /// Gets the current computed vertical-scrollbar visibility.
    /// </summary>
    public Visibility ComputedVerticalScrollBarVisibility =>
        (Visibility)GetValue(ComputedVerticalScrollBarVisibilityProperty)!;

    /// <summary>
    /// Gets the horizontal position where content is visually located. During
    /// deferred thumb tracking this remains at the committed content position.
    /// </summary>
    public double ContentHorizontalOffset =>
        (double)GetValue(ContentHorizontalOffsetProperty)!;

    /// <summary>
    /// Gets the vertical position where content is visually located. During
    /// deferred thumb tracking this remains at the committed content position.
    /// </summary>
    public double ContentVerticalOffset =>
        (double)GetValue(ContentVerticalOffsetProperty)!;

    /// <inheritdoc />
    protected internal override bool HandlesScrolling => true;

    /// <summary>
    /// Gets or sets the scrolling provider used for extent, viewport, offset,
    /// and line/page commands.
    /// </summary>
    protected internal IScrollInfo? ScrollInfo
    {
        get => _scrollInfo;
        set
        {
            if (ReferenceEquals(_scrollInfo, value))
            {
                return;
            }

            if (_scrollInfo?.ScrollOwner == this)
            {
                _scrollInfo.ScrollOwner = null;
            }

            _scrollInfo = value;
            if (_scrollInfo != null)
            {
                _scrollInfo.ScrollOwner = this;
                _scrollInfo.CanHorizontallyScroll =
                    HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled;
                _scrollInfo.CanVerticallyScroll =
                    VerticalScrollBarVisibility != ScrollBarVisibility.Disabled;
            }

            InvalidateMeasure();
            InvalidateArrange();
        }
    }

    /// <summary>
    /// Gets the attached CanContentScroll value.
    /// </summary>
    public static bool GetCanContentScroll(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(CanContentScrollProperty)!;
    }

    /// <summary>
    /// Sets the attached CanContentScroll value.
    /// </summary>
    public static void SetCanContentScroll(DependencyObject element, bool canContentScroll)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(CanContentScrollProperty, canContentScroll);
    }

    /// <summary>
    /// Gets the attached horizontal-scrollbar visibility value.
    /// </summary>
    public static ScrollBarVisibility GetHorizontalScrollBarVisibility(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (ScrollBarVisibility)element.GetValue(HorizontalScrollBarVisibilityProperty)!;
    }

    /// <summary>
    /// Sets the attached horizontal-scrollbar visibility value.
    /// </summary>
    public static void SetHorizontalScrollBarVisibility(
        DependencyObject element,
        ScrollBarVisibility horizontalScrollBarVisibility)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(HorizontalScrollBarVisibilityProperty, horizontalScrollBarVisibility);
    }

    /// <summary>
    /// Gets the attached deferred-scrolling value.
    /// </summary>
    public static bool GetIsDeferredScrollingEnabled(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)element.GetValue(IsDeferredScrollingEnabledProperty)!;
    }

    /// <summary>
    /// Sets the attached deferred-scrolling value.
    /// </summary>
    public static void SetIsDeferredScrollingEnabled(DependencyObject element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsDeferredScrollingEnabledProperty, value);
    }

    /// <summary>
    /// Gets the attached panning-deceleration value.
    /// </summary>
    public static double GetPanningDeceleration(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)element.GetValue(PanningDecelerationProperty)!;
    }

    /// <summary>
    /// Sets the attached panning-deceleration value.
    /// </summary>
    public static void SetPanningDeceleration(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(PanningDecelerationProperty, value);
    }

    /// <summary>
    /// Gets the attached panning-mode value.
    /// </summary>
    public static PanningMode GetPanningMode(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (PanningMode)element.GetValue(PanningModeProperty)!;
    }

    /// <summary>
    /// Sets the attached panning-mode value.
    /// </summary>
    public static void SetPanningMode(DependencyObject element, PanningMode panningMode)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(PanningModeProperty, panningMode);
    }

    /// <summary>
    /// Gets the attached panning-ratio value.
    /// </summary>
    public static double GetPanningRatio(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)element.GetValue(PanningRatioProperty)!;
    }

    /// <summary>
    /// Sets the attached panning-ratio value.
    /// </summary>
    public static void SetPanningRatio(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(PanningRatioProperty, value);
    }

    /// <summary>
    /// Gets the attached vertical-scrollbar visibility value.
    /// </summary>
    public static ScrollBarVisibility GetVerticalScrollBarVisibility(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (ScrollBarVisibility)element.GetValue(VerticalScrollBarVisibilityProperty)!;
    }

    /// <summary>
    /// Sets the attached vertical-scrollbar visibility value.
    /// </summary>
    public static void SetVerticalScrollBarVisibility(
        DependencyObject element,
        ScrollBarVisibility verticalScrollBarVisibility)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(VerticalScrollBarVisibilityProperty, verticalScrollBarVisibility);
    }

    /// <summary>
    /// Raises the routed <see cref="ScrollChanged"/> event. Overrides should call
    /// the base implementation to preserve routed-event delivery.
    /// </summary>
    protected virtual void OnScrollChanged(ScrollChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(e);
    }

    private void UpdateRequestedOffsetsFromContent()
    {
        _requestedHorizontalOffset = _horizontalOffset;
        _requestedVerticalOffset = _verticalOffset;
        UpdateScrollMetricDependencyProperties();
    }

    private void UpdateDeferredRequestedOffset(bool isVertical, double value)
    {
        if (isVertical)
        {
            _requestedVerticalOffset = value;
        }
        else
        {
            _requestedHorizontalOffset = value;
        }

        UpdateScrollMetricDependencyProperties();
    }

    private void UpdateScrollMetricDependencyProperties()
    {
        SetValue(HorizontalOffsetPropertyKey, _requestedHorizontalOffset);
        SetValue(VerticalOffsetPropertyKey, _requestedVerticalOffset);
        SetValue(ContentHorizontalOffsetPropertyKey, _horizontalOffset);
        SetValue(ContentVerticalOffsetPropertyKey, _verticalOffset);
        SetValue(ExtentWidthPropertyKey, Math.Max(0.0, _extentWidth));
        SetValue(ExtentHeightPropertyKey, Math.Max(0.0, _extentHeight));
        SetValue(ViewportWidthPropertyKey, Math.Max(0.0, _viewportWidth));
        SetValue(ViewportHeightPropertyKey, Math.Max(0.0, _viewportHeight));
        SetValue(ScrollableWidthPropertyKey, Math.Max(0.0, _extentWidth - _viewportWidth));
        SetValue(ScrollableHeightPropertyKey, Math.Max(0.0, _extentHeight - _viewportHeight));
        SetValue(
            ComputedHorizontalScrollBarVisibilityPropertyKey,
            GetComputedScrollBarVisibility(HorizontalScrollBarVisibility, CanScrollHorizontally));
        SetValue(
            ComputedVerticalScrollBarVisibilityPropertyKey,
            GetComputedScrollBarVisibility(VerticalScrollBarVisibility, CanScrollVertically));
    }

    private static Visibility GetComputedScrollBarVisibility(
        ScrollBarVisibility visibility,
        bool canScroll)
    {
        return visibility switch
        {
            ScrollBarVisibility.Visible => Visibility.Visible,
            ScrollBarVisibility.Auto when canScroll => Visibility.Visible,
            _ => Visibility.Collapsed,
        };
    }

    private static bool IsValidScrollBarVisibility(object? value) =>
        value is ScrollBarVisibility visibility &&
        visibility is >= ScrollBarVisibility.Disabled and <= ScrollBarVisibility.Visible;

    private static bool IsFiniteNonNegative(object? value) =>
        value is double number && !double.IsNaN(number) && !double.IsInfinity(number) && number >= 0.0;

    private static double ValidateScrollOffset(double offset, string parameterName)
    {
        if (double.IsNaN(offset))
        {
            throw new ArgumentOutOfRangeException(parameterName, offset, $"'{parameterName}' parameter value cannot be NaN.");
        }

        return offset;
    }
}
