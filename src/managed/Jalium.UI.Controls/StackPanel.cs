using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Arranges child elements into a single line that can be oriented horizontally or vertically.
/// Implements IScrollInfo for physical scrolling support when hosted in a ScrollViewer.
/// </summary>
public class StackPanel : Panel, IScrollInfo
{
    // Layout callbacks may mutate Children, so retain the existing snapshot
    // behavior without allocating through Cast().ToArray() on every pass.
    // Reentrant layout gets its own fallback list and cannot corrupt the outer
    // snapshot.
    private readonly List<UIElement> _layoutChildrenSnapshot = new();
    private int _layoutChildrenSnapshotDepth;

    protected internal override bool HasLogicalOrientation => true;

    protected internal override Orientation LogicalOrientation => Orientation;

    // Layout snapping uses the inherited DPI-aware FrameworkElement.SnapLayoutValue,
    // which rounds to the nearest physical-pixel boundary. A previous local override
    // here did a plain Math.Round and silently lost DPI awareness on fractional scales
    // (1.5 / 1.75), so scroll arrange offsets landed off the physical grid.

    #region Dependency Properties

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(StackPanel),
            new PropertyMetadata(Orientation.Vertical, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the Spacing dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(StackPanel),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the dimension by which child elements are stacked.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets the uniform distance in device-independent pixels between
    /// adjacent visible child elements along the stacking axis.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty)!;
        set => SetValue(SpacingProperty, value);
    }

    private double EffectiveSpacing
    {
        get
        {
            var value = Spacing;
            return (double.IsNaN(value) || double.IsInfinity(value) || value < 0) ? 0 : value;
        }
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
        var spacing = EffectiveSpacing;
        var sawFirstVisible = false;

        foreach (UIElement c in Children)
        {
            if (c == child) break;
            if (c.Visibility == Visibility.Collapsed) continue;
            if (sawFirstVisible)
            {
                childOffset += spacing;
            }
            // Use RenderSize (actual arranged size) rather than DesiredSize (measured size)
            // to correctly calculate scroll offset after layout is complete.
            var size = c.RenderSize.Width > 0 || c.RenderSize.Height > 0 ? c.RenderSize : c.DesiredSize;
            childOffset += isVertical ? size.Height : size.Width;
            sawFirstVisible = true;
        }

        if (sawFirstVisible && child.Visibility != Visibility.Collapsed)
        {
            // Account for the gap between the previous visible sibling and the target child.
            childOffset += spacing;
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
        var spacing = EffectiveSpacing;
        double totalWidth = 0;
        double totalHeight = 0;
        double maxCross = 0;
        bool sawVisible = false;

        var layoutChildren = CaptureLayoutChildren();
        try
        {
            foreach (var child in layoutChildren)
            {
                // Skip collapsed children
                if (child.Visibility == Visibility.Collapsed)
                    continue;

                // position:absolute children are out of flow (measured separately below).
                if (IsCssAbsolute(child))
                    continue;

                // Measure with the constrained cross-axis first so stretch/layout-driven content
                // (for example Grid with star columns) can establish its viewport-based layout.
                // Only if the constrained pass already overflows on the scrollable cross axis do
                // we remeasure unconstrained to compute the true scroll extent.
                var finiteChildAvailable = isVertical
                    ? new Size(availableSize.Width, double.PositiveInfinity)
                    : new Size(double.PositiveInfinity, availableSize.Height);

                child.Measure(finiteChildAvailable);
                var childSize = child.DesiredSize;

                if (isVertical &&
                    _canHorizontallyScroll &&
                    !double.IsPositiveInfinity(availableSize.Width) &&
                    childSize.Width > availableSize.Width + 0.5)
                {
                    child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    childSize = child.DesiredSize;
                }
                else if (!isVertical &&
                         _canVerticallyScroll &&
                         !double.IsPositiveInfinity(availableSize.Height) &&
                         childSize.Height > availableSize.Height + 0.5)
                {
                    child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    childSize = child.DesiredSize;
                }

                if (isVertical)
                {
                    if (sawVisible) totalHeight += spacing;
                    totalHeight += childSize.Height;
                    maxCross = Math.Max(maxCross, childSize.Width);
                }
                else
                {
                    if (sawVisible) totalWidth += spacing;
                    totalWidth += childSize.Width;
                    maxCross = Math.Max(maxCross, childSize.Height);
                }

                sawVisible = true;
            }
        }
        finally
        {
            ReleaseLayoutChildren(layoutChildren);
        }

        MeasureCssAbsoluteChildren(availableSize);

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
        var spacing = EffectiveSpacing;
        double offset = 0;
        bool sawVisible = false;

        // Apply scroll offset
        double scrollOffsetX = ScrollOwner != null ? -_horizontalOffset : 0;
        double scrollOffsetY = ScrollOwner != null ? -_verticalOffset : 0;

        var layoutChildren = CaptureLayoutChildren();
        try
        {
            foreach (var child in layoutChildren)
            {
                // Skip collapsed children
                if (child.Visibility == Visibility.Collapsed)
                    continue;

                // position:absolute children are placed by the inset protocol below.
                if (IsCssAbsolute(child))
                    continue;

                if (sawVisible)
                {
                    offset += spacing;
                }

                var childSize = child.DesiredSize;
                var arrangeWidth = isVertical && _canHorizontallyScroll ? Math.Max(finalSize.Width, _extent.Width) : finalSize.Width;
                var arrangeHeight = !isVertical && _canVerticallyScroll ? Math.Max(finalSize.Height, _extent.Height) : finalSize.Height;

                Rect childRect;
                if (isVertical)
                {
                    var startY = offset + scrollOffsetY;
                    var endY = startY + childSize.Height;
                    var snappedY = SnapLayoutValue(startY);
                    var snappedBottom = SnapLayoutValue(endY);
                    childRect = new Rect(
                        SnapLayoutValue(scrollOffsetX),
                        snappedY,
                        SnapLayoutValue(arrangeWidth),
                        Math.Max(0, snappedBottom - snappedY));
                    offset += childSize.Height;
                }
                else
                {
                    var startX = offset + scrollOffsetX;
                    var endX = startX + childSize.Width;
                    var snappedX = SnapLayoutValue(startX);
                    var snappedRight = SnapLayoutValue(endX);
                    childRect = new Rect(
                        snappedX,
                        SnapLayoutValue(scrollOffsetY),
                        Math.Max(0, snappedRight - snappedX),
                        SnapLayoutValue(arrangeHeight));
                    offset += childSize.Width;
                }

                child.Arrange(childRect);
                sawVisible = true;
            }
        }
        finally
        {
            ReleaseLayoutChildren(layoutChildren);
        }

        ArrangeCssAbsoluteChildren(finalSize);

        // Update viewport if scrolling
        if (ScrollOwner != null)
        {
            _viewport = finalSize;
        }

        return finalSize;
    }

    private List<UIElement> CaptureLayoutChildren()
    {
        List<UIElement> snapshot;
        if (_layoutChildrenSnapshotDepth++ == 0)
        {
            snapshot = _layoutChildrenSnapshot;
            snapshot.Clear();
        }
        else
        {
            snapshot = new List<UIElement>(Children.Count);
        }

        for (int index = 0; index < Children.Count; index++)
        {
            snapshot.Add(Children[index]);
        }

        return snapshot;
    }

    private void ReleaseLayoutChildren(List<UIElement> snapshot)
    {
        if (ReferenceEquals(snapshot, _layoutChildrenSnapshot))
        {
            snapshot.Clear();
        }

        _layoutChildrenSnapshotDepth--;
    }

    #endregion

    #region Property Changed Callbacks

    /// <summary>
    /// CSS interception: StackPanel understands flex-direction by mapping it onto its own
    /// Orientation (the canonical example of the panel-level CSS interception protocol).
    /// Values flow through the setter into the CSS layers, so a rule that stops matching
    /// automatically restores the previous orientation.
    /// </summary>
    protected internal override bool TryApplyCssPropertyCore(
        string propertyName, string rawValue, in Jalium.UI.Styling.CssDeclarationSetter setter)
    {
        if (propertyName.Equals("flex-direction", StringComparison.OrdinalIgnoreCase))
        {
            var value = rawValue.Trim();
            if (value.Equals("row", StringComparison.OrdinalIgnoreCase))
            {
                setter.Set(OrientationProperty, Orientation.Horizontal);
                return true;
            }

            if (value.Equals("column", StringComparison.OrdinalIgnoreCase))
            {
                setter.Set(OrientationProperty, Orientation.Vertical);
                return true;
            }

            if (value.Equals("row-reverse", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("column-reverse", StringComparison.OrdinalIgnoreCase))
            {
                setter.ReportLossy("StackPanel cannot reverse; use FlexPanel for reverse directions");
                setter.Set(OrientationProperty,
                    value.StartsWith("row", StringComparison.OrdinalIgnoreCase)
                        ? Orientation.Horizontal
                        : Orientation.Vertical);
                return true;
            }

            return false;
        }

        return base.TryApplyCssPropertyCore(propertyName, rawValue, in setter);
    }

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
