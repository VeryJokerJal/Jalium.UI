namespace Jalium.UI.Controls;

/// <summary>
/// Arranges child elements into a single line that can be oriented horizontally or vertically.
/// Implements IScrollInfo for physical scrolling support when hosted in a ScrollViewer.
/// </summary>
public class StackPanel : Panel, IScrollInfo
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(StackPanel),
            new PropertyMetadata(Orientation.Vertical, OnLayoutPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the dimension by which child elements are stacked.
    /// </summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    #endregion

    #region IScrollInfo

    private bool _canHorizontallyScroll;
    private bool _canVerticallyScroll;
    private Size _extent;
    private Size _viewport;
    private double _horizontalOffset;
    private double _verticalOffset;
    private const double LineDelta = 16.0;
    private const double WheelDelta = 48.0;

    /// <inheritdoc />
    public bool CanHorizontallyScroll
    {
        get => _canHorizontallyScroll;
        set => _canHorizontallyScroll = value;
    }

    /// <inheritdoc />
    public bool CanVerticallyScroll
    {
        get => _canVerticallyScroll;
        set => _canVerticallyScroll = value;
    }

    /// <inheritdoc />
    public double ExtentWidth => _extent.Width;

    /// <inheritdoc />
    public double ExtentHeight => _extent.Height;

    /// <inheritdoc />
    public double ViewportWidth => _viewport.Width;

    /// <inheritdoc />
    public double ViewportHeight => _viewport.Height;

    /// <inheritdoc />
    public double HorizontalOffset => _horizontalOffset;

    /// <inheritdoc />
    public double VerticalOffset => _verticalOffset;

    /// <inheritdoc />
    public ScrollViewer? ScrollOwner { get; set; }

    /// <inheritdoc />
    public void LineUp() => SetVerticalOffset(VerticalOffset - LineDelta);

    /// <inheritdoc />
    public void LineDown() => SetVerticalOffset(VerticalOffset + LineDelta);

    /// <inheritdoc />
    public void LineLeft() => SetHorizontalOffset(HorizontalOffset - LineDelta);

    /// <inheritdoc />
    public void LineRight() => SetHorizontalOffset(HorizontalOffset + LineDelta);

    /// <inheritdoc />
    public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);

    /// <inheritdoc />
    public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);

    /// <inheritdoc />
    public void PageLeft() => SetHorizontalOffset(HorizontalOffset - ViewportWidth);

    /// <inheritdoc />
    public void PageRight() => SetHorizontalOffset(HorizontalOffset + ViewportWidth);

    /// <inheritdoc />
    public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - WheelDelta);

    /// <inheritdoc />
    public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + WheelDelta);

    /// <inheritdoc />
    public void MouseWheelLeft() => SetHorizontalOffset(HorizontalOffset - WheelDelta);

    /// <inheritdoc />
    public void MouseWheelRight() => SetHorizontalOffset(HorizontalOffset + WheelDelta);

    /// <inheritdoc />
    public void SetHorizontalOffset(double offset)
    {
        offset = Math.Clamp(offset, 0, Math.Max(0, ExtentWidth - ViewportWidth));
        if (offset != _horizontalOffset)
        {
            _horizontalOffset = offset;
            InvalidateArrange();
        }
    }

    /// <inheritdoc />
    public void SetVerticalOffset(double offset)
    {
        offset = Math.Clamp(offset, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        if (offset != _verticalOffset)
        {
            _verticalOffset = offset;
            InvalidateArrange();
        }
    }

    /// <inheritdoc />
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (rectangle.IsEmpty || visual == null)
            return Rect.Empty;

        // Find the child that contains this visual
        var child = visual as UIElement;
        while (child != null && child != this)
        {
            var parent = child.VisualParent as UIElement;
            if (parent == this) break;
            child = parent;
        }

        if (child == null || child == this)
            return rectangle;

        // Calculate position of child
        double childOffset = 0;
        var isVertical = Orientation == Orientation.Vertical;

        foreach (var c in Children)
        {
            if (c == child) break;
            if (c.Visibility == Visibility.Collapsed) continue;
            childOffset += isVertical ? c.DesiredSize.Height : c.DesiredSize.Width;
        }

        if (isVertical)
        {
            var top = childOffset + rectangle.Y;
            var bottom = top + rectangle.Height;

            if (top < _verticalOffset)
                SetVerticalOffset(top);
            else if (bottom > _verticalOffset + _viewport.Height)
                SetVerticalOffset(bottom - _viewport.Height);
        }
        else
        {
            var left = childOffset + rectangle.X;
            var right = left + rectangle.Width;

            if (left < _horizontalOffset)
                SetHorizontalOffset(left);
            else if (right > _horizontalOffset + _viewport.Width)
                SetHorizontalOffset(right - _viewport.Width);
        }

        return rectangle;
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var isVertical = Orientation == Orientation.Vertical;
        double totalWidth = 0;
        double totalHeight = 0;
        double maxCross = 0;

        foreach (var child in Children)
        {
            // Skip collapsed children
            if (child.Visibility == Visibility.Collapsed)
                continue;

            // Give each child infinite space in the stack direction
            var childAvailable = isVertical
                ? new Size(availableSize.Width, double.PositiveInfinity)
                : new Size(double.PositiveInfinity, availableSize.Height);

            child.Measure(childAvailable);
            var childSize = child.DesiredSize;

            if (isVertical)
            {
                totalHeight += childSize.Height;
                maxCross = Math.Max(maxCross, childSize.Width);
            }
            else
            {
                totalWidth += childSize.Width;
                maxCross = Math.Max(maxCross, childSize.Height);
            }
        }

        var extent = isVertical
            ? new Size(maxCross, totalHeight)
            : new Size(totalWidth, maxCross);

        // Update scroll info
        if (ScrollOwner != null)
        {
            _extent = extent;
            _viewport = availableSize;

            // Clamp offsets to valid range
            _horizontalOffset = Math.Clamp(_horizontalOffset, 0, Math.Max(0, _extent.Width - _viewport.Width));
            _verticalOffset = Math.Clamp(_verticalOffset, 0, Math.Max(0, _extent.Height - _viewport.Height));
        }

        return extent;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var isVertical = Orientation == Orientation.Vertical;
        double offset = 0;

        // Apply scroll offset
        double scrollOffsetX = ScrollOwner != null ? -_horizontalOffset : 0;
        double scrollOffsetY = ScrollOwner != null ? -_verticalOffset : 0;

        foreach (var child in Children)
        {
            // Skip collapsed children
            if (child.Visibility == Visibility.Collapsed)
                continue;

            var childSize = child.DesiredSize;

            Rect childRect;
            if (isVertical)
            {
                childRect = new Rect(scrollOffsetX, offset + scrollOffsetY, finalSize.Width, childSize.Height);
                offset += childSize.Height;
            }
            else
            {
                childRect = new Rect(offset + scrollOffsetX, scrollOffsetY, childSize.Width, finalSize.Height);
                offset += childSize.Width;
            }

            child.Arrange(childRect);
        }

        // Update viewport if scrolling
        if (ScrollOwner != null)
        {
            _viewport = finalSize;
        }

        return finalSize;
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StackPanel panel)
        {
            panel.InvalidateMeasure();
        }
    }

    #endregion
}

/// <summary>
/// Defines the different orientations that a control or layout can have.
/// </summary>
public enum Orientation
{
    /// <summary>
    /// Horizontal orientation.
    /// </summary>
    Horizontal,

    /// <summary>
    /// Vertical orientation.
    /// </summary>
    Vertical
}
