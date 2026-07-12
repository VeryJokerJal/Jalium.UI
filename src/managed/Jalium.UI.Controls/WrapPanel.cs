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
    /// Identifies the ItemWidth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ItemWidthProperty =
        DependencyProperty.Register(nameof(ItemWidth), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(double.NaN, OnItemSizeChanged));

    /// <summary>
    /// Identifies the ItemHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ItemHeightProperty =
        DependencyProperty.Register(nameof(ItemHeight), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(double.NaN, OnItemSizeChanged));

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
            var childConstraint = new Size(
                hasFixedWidth ? itemWidth : availableSize.Width,
                hasFixedHeight ? itemHeight : availableSize.Height);

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

        // First pass: calculate line layout honouring primary spacing.
        var lineInfo = new List<LineInfo>();
        var currentLine = new LineInfo();

        foreach (UIElement child in Children)
        {
            if (child is not FrameworkElement fe) continue;

            var childWidth = hasFixedWidth ? itemWidth : fe.DesiredSize.Width;
            var childHeight = hasFixedHeight ? itemHeight : fe.DesiredSize.Height;
            var childPrimary = orientation == Orientation.Horizontal ? childWidth : childHeight;
            var childSecondary = orientation == Orientation.Horizontal ? childHeight : childWidth;
            var prospective = currentLine.Elements.Count > 0
                ? currentLine.Size + primaryGap + childPrimary
                : childPrimary;

            if (currentLine.Elements.Count > 0 && prospective > primaryLimit)
            {
                lineInfo.Add(currentLine);
                currentLine = new LineInfo();
            }

            if (currentLine.Elements.Count > 0) currentLine.Size += primaryGap;
            currentLine.Elements.Add((fe, childWidth, childHeight));
            currentLine.Size += childPrimary;
            currentLine.Thickness = Math.Max(currentLine.Thickness, childSecondary);
        }

        if (currentLine.Elements.Count > 0)
        {
            lineInfo.Add(currentLine);
        }

        // Second pass: arrange children.
        double offset = 0;
        for (int lineIndex = 0; lineIndex < lineInfo.Count; lineIndex++)
        {
            if (lineIndex > 0) offset += secondaryGap;

            var line = lineInfo[lineIndex];
            double pos = 0;
            bool firstOnLine = true;
            foreach (var (element, width, height) in line.Elements)
            {
                if (!firstOnLine) pos += primaryGap;

                Rect childRect;
                if (orientation == Orientation.Horizontal)
                {
                    childRect = new Rect(pos, offset, width, line.Thickness);
                    pos += width;
                }
                else
                {
                    childRect = new Rect(offset, pos, line.Thickness, height);
                    pos += height;
                }
                element.Arrange(childRect);
                firstOnLine = false;
                // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
            }
            offset += line.Thickness;
        }

        return finalSize;
    }

    private class LineInfo
    {
        public List<(FrameworkElement Element, double Width, double Height)> Elements { get; } = new();
        public double Size { get; set; }
        public double Thickness { get; set; }
    }
}
