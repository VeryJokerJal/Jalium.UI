namespace Jalium.UI.Controls;

/// <summary>
/// Positions child elements in sequential position from left to right,
/// breaking content to the next line at the edge of the containing box.
/// </summary>
public class WrapPanel : Panel
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(WrapPanel),
            new PropertyMetadata(Orientation.Horizontal, OnOrientationChanged));

    /// <summary>
    /// Identifies the ItemWidth dependency property. Shares the Width/Height value
    /// contract (NaN = natural size, otherwise non-negative finite), so it reuses
    /// <see cref="FrameworkElement.IsWidthHeightValid"/> — matching WPF's WrapPanel.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ItemWidthProperty =
        DependencyProperty.Register(nameof(ItemWidth), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(double.NaN, OnItemSizeChanged), FrameworkElement.IsWidthHeightValid);

    /// <summary>
    /// Identifies the ItemHeight dependency property. Same value contract as
    /// <see cref="ItemWidthProperty"/>.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(double.NaN, OnItemSizeChanged), FrameworkElement.IsWidthHeightValid);

    /// <summary>
    /// Identifies the HorizontalSpacing dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(nameof(HorizontalSpacing), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the VerticalSpacing dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(nameof(VerticalSpacing), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the orientation of the panel.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets the width of each item. NaN means use natural size.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double ItemWidth
    {
        get => (double)GetValue(ItemWidthProperty)!;
        set => SetValue(ItemWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the height of each item. NaN means use natural size.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty)!;
        set => SetValue(ItemHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal spacing between adjacent items (gap between columns for
    /// horizontal orientation, or between columns of wrapped lines for vertical orientation).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty)!;
        set => SetValue(HorizontalSpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical spacing between wrapped lines (gap between rows for
    /// horizontal orientation, or between items within a column for vertical orientation).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty)!;
        set => SetValue(VerticalSpacingProperty, value);
    }

    private static double SanitizeSpacing(double value) =>
        (double.IsNaN(value) || double.IsInfinity(value) || value < 0) ? 0 : value;

    #endregion

    private static void OnOrientationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WrapPanel panel)
        {
            panel.InvalidateMeasure();
        }
    }

    private static void OnItemSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WrapPanel panel)
        {
            panel.InvalidateMeasure();
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WrapPanel panel)
        {
            panel.InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var orientation = Orientation;
        var itemWidth = ItemWidth;
        var itemHeight = ItemHeight;
        bool hasFixedWidth = !double.IsNaN(itemWidth);
        bool hasFixedHeight = !double.IsNaN(itemHeight);
        var hSpacing = SanitizeSpacing(HorizontalSpacing);
        var vSpacing = SanitizeSpacing(VerticalSpacing);

        // Along the primary (stacking) axis, items are separated by primaryGap.
        // Between wrapped lines along the secondary axis, lines are separated by secondaryGap.
        double primaryGap = orientation == Orientation.Horizontal ? hSpacing : vSpacing;
        double secondaryGap = orientation == Orientation.Horizontal ? vSpacing : hSpacing;

        double totalWidth = 0;
        double totalHeight = 0;
        double lineSize = 0;      // Size in the primary direction (without trailing gap)
        double lineThickness = 0; // Size in the secondary direction
        int itemsOnLine = 0;
        int lineCount = 0;

        foreach (UIElement child in Children)
        {
            if (child is not FrameworkElement fe) continue;

            // Determine child constraint
            // A WrapPanel determines wrapping from each child's natural DesiredSize; the
            // panel's own changing viewport is not a child-size constraint. Keeping the
            // natural axes at Infinity also means a window-width change can reuse every
            // valid child measure and only recompute the cheap line breaks. This matches
            // WPF and avoids remeasuring an entire off-screen card subtree on every resize.
            var childConstraint = new Size(
                hasFixedWidth ? itemWidth : double.PositiveInfinity,
                hasFixedHeight ? itemHeight : double.PositiveInfinity);

            fe.Measure(childConstraint);

            var childWidth = hasFixedWidth ? itemWidth : fe.DesiredSize.Width;
            var childHeight = hasFixedHeight ? itemHeight : fe.DesiredSize.Height;
            var childPrimary = orientation == Orientation.Horizontal ? childWidth : childHeight;
            var childSecondary = orientation == Orientation.Horizontal ? childHeight : childWidth;
            var primaryLimit = orientation == Orientation.Horizontal ? availableSize.Width : availableSize.Height;
            var prospective = itemsOnLine > 0 ? lineSize + primaryGap + childPrimary : childPrimary;

            if (itemsOnLine > 0 && prospective > primaryLimit)
            {
                // Commit the completed line and start a new one.
                if (orientation == Orientation.Horizontal)
                {
                    totalWidth = Math.Max(totalWidth, lineSize);
                    totalHeight += lineThickness;
                }
                else
                {
                    totalHeight = Math.Max(totalHeight, lineSize);
                    totalWidth += lineThickness;
                }

                lineCount++;
                lineSize = childPrimary;
                lineThickness = childSecondary;
                itemsOnLine = 1;
            }
            else
            {
                if (itemsOnLine > 0) lineSize += primaryGap;
                lineSize += childPrimary;
                lineThickness = Math.Max(lineThickness, childSecondary);
                itemsOnLine++;
            }
        }

        // Add the last line.
        if (itemsOnLine > 0)
        {
            if (orientation == Orientation.Horizontal)
            {
                totalWidth = Math.Max(totalWidth, lineSize);
                totalHeight += lineThickness;
            }
            else
            {
                totalHeight = Math.Max(totalHeight, lineSize);
                totalWidth += lineThickness;
            }
            lineCount++;
        }

        // Inject secondary spacing between lines.
        if (lineCount > 1)
        {
            var secondaryTotal = (lineCount - 1) * secondaryGap;
            if (orientation == Orientation.Horizontal) totalHeight += secondaryTotal;
            else totalWidth += secondaryTotal;
        }

        // Measure 阶段必须报告"真实所需"尺寸，否则放在 ScrollViewer 内时
        // ScrollViewer 永远看不到溢出 → 滚动条触发不了。
        //
        // - Horizontal orientation：宽度方向受 availableSize.Width 限制（用于换行决策），
        //   返回时 width clamp 到 availableSize.Width；
        //   但高度方向 必须 报告真实总行高，不可 clamp，否则垂直滚动条无法触发。
        // - Vertical orientation：反过来。
        if (Orientation == Orientation.Horizontal)
        {
            return new Size(
                double.IsInfinity(availableSize.Width) ? totalWidth : Math.Min(totalWidth, availableSize.Width),
                totalHeight);
        }
        else
        {
            return new Size(
                totalWidth,
                double.IsInfinity(availableSize.Height) ? totalHeight : Math.Min(totalHeight, availableSize.Height));
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var orientation = Orientation;
        var itemWidth = ItemWidth;
        var itemHeight = ItemHeight;
        bool hasFixedWidth = !double.IsNaN(itemWidth);
        bool hasFixedHeight = !double.IsNaN(itemHeight);
        var hSpacing = SanitizeSpacing(HorizontalSpacing);
        var vSpacing = SanitizeSpacing(VerticalSpacing);
        double primaryGap = orientation == Orientation.Horizontal ? hSpacing : vSpacing;
        double secondaryGap = orientation == Orientation.Horizontal ? vSpacing : hSpacing;
        double primaryLimit = orientation == Orientation.Horizontal ? finalSize.Width : finalSize.Height;

        // Stream completed lines directly into ArrangeLine. The previous implementation
        // materialized a List<LineInfo>, one LineInfo per wrapped line, and a tuple list for
        // every line on every arrange pass. ScrollViewer moves non-IScrollInfo content by
        // arranging it at a new origin, so those short-lived objects landed directly in the
        // scrolling and live-resize hot paths. A completed line already has all the state
        // needed to arrange it, so retaining the whole layout is unnecessary.
        int lineStartIndex = -1;
        int itemsOnLine = 0;
        double lineSize = 0;
        double lineThickness = 0;
        double secondaryOffset = 0;

        for (int childIndex = 0; childIndex < Children.Count; childIndex++)
        {
            if (Children[childIndex] is not FrameworkElement fe) continue;

            var childWidth = hasFixedWidth ? itemWidth : fe.DesiredSize.Width;
            var childHeight = hasFixedHeight ? itemHeight : fe.DesiredSize.Height;
            var childPrimary = orientation == Orientation.Horizontal ? childWidth : childHeight;
            var childSecondary = orientation == Orientation.Horizontal ? childHeight : childWidth;
            var prospective = itemsOnLine > 0
                ? lineSize + primaryGap + childPrimary
                : childPrimary;

            if (itemsOnLine > 0 && prospective > primaryLimit)
            {
                ArrangeLine(
                    lineStartIndex,
                    childIndex,
                    secondaryOffset,
                    lineThickness,
                    orientation,
                    hasFixedWidth,
                    hasFixedHeight,
                    itemWidth,
                    itemHeight,
                    primaryGap);

                secondaryOffset += lineThickness + secondaryGap;
                lineStartIndex = childIndex;
                itemsOnLine = 0;
                lineSize = 0;
                lineThickness = 0;
            }

            if (itemsOnLine == 0)
            {
                lineStartIndex = childIndex;
            }
            else
            {
                lineSize += primaryGap;
            }

            lineSize += childPrimary;
            lineThickness = Math.Max(lineThickness, childSecondary);
            itemsOnLine++;
        }

        if (itemsOnLine > 0)
        {
            ArrangeLine(
                lineStartIndex,
                Children.Count,
                secondaryOffset,
                lineThickness,
                orientation,
                hasFixedWidth,
                hasFixedHeight,
                itemWidth,
                itemHeight,
                primaryGap);
        }

        return finalSize;
    }

    private void ArrangeLine(
        int startIndex,
        int endIndex,
        double secondaryOffset,
        double lineThickness,
        Orientation orientation,
        bool hasFixedWidth,
        bool hasFixedHeight,
        double itemWidth,
        double itemHeight,
        double primaryGap)
    {
        double primaryOffset = 0;
        bool firstOnLine = true;

        for (int childIndex = startIndex; childIndex < endIndex; childIndex++)
        {
            if (Children[childIndex] is not FrameworkElement element) continue;

            var width = hasFixedWidth ? itemWidth : element.DesiredSize.Width;
            var height = hasFixedHeight ? itemHeight : element.DesiredSize.Height;

            if (!firstOnLine) primaryOffset += primaryGap;

            Rect childRect;
            if (orientation == Orientation.Horizontal)
            {
                childRect = new Rect(primaryOffset, secondaryOffset, width, lineThickness);
                primaryOffset += width;
            }
            else
            {
                childRect = new Rect(secondaryOffset, primaryOffset, lineThickness, height);
                primaryOffset += height;
            }

            element.Arrange(childRect);
            firstOnLine = false;
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }
    }
}
