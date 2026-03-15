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

    protected override Size MeasureOverride(Size availableSize)
    {
        var orientation = Orientation;
        var itemWidth = ItemWidth;
        var itemHeight = ItemHeight;
        bool hasFixedWidth = !double.IsNaN(itemWidth);
        bool hasFixedHeight = !double.IsNaN(itemHeight);

        double totalWidth = 0;
        double totalHeight = 0;
        double lineSize = 0;      // Size in the primary direction
        double lineThickness = 0; // Size in the secondary direction

        foreach (var child in Children)
        {
            if (child is not FrameworkElement fe) continue;

            // Determine child constraint
            var childConstraint = new Size(
                hasFixedWidth ? itemWidth : availableSize.Width,
                hasFixedHeight ? itemHeight : availableSize.Height);

            fe.Measure(childConstraint);

            var childWidth = hasFixedWidth ? itemWidth : fe.DesiredSize.Width;
            var childHeight = hasFixedHeight ? itemHeight : fe.DesiredSize.Height;

            if (orientation == Orientation.Horizontal)
            {
                // Check if we need to wrap to next line
                if (lineSize + childWidth > availableSize.Width && lineSize > 0)
                {
                    // Wrap to next line
                    totalWidth = Math.Max(totalWidth, lineSize);
                    totalHeight += lineThickness;
                    lineSize = 0;
                    lineThickness = 0;
                }

                lineSize += childWidth;
                lineThickness = Math.Max(lineThickness, childHeight);
            }
            else // Vertical
            {
                // Check if we need to wrap to next column
                if (lineSize + childHeight > availableSize.Height && lineSize > 0)
                {
                    // Wrap to next column
                    totalHeight = Math.Max(totalHeight, lineSize);
                    totalWidth += lineThickness;
                    lineSize = 0;
                    lineThickness = 0;
                }

                lineSize += childHeight;
                lineThickness = Math.Max(lineThickness, childWidth);
            }
        }

        // Add the last line
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

        return new Size(
            double.IsInfinity(availableSize.Width) ? totalWidth : Math.Min(totalWidth, availableSize.Width),
            double.IsInfinity(availableSize.Height) ? totalHeight : Math.Min(totalHeight, availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var orientation = Orientation;
        var itemWidth = ItemWidth;
        var itemHeight = ItemHeight;
        bool hasFixedWidth = !double.IsNaN(itemWidth);
        bool hasFixedHeight = !double.IsNaN(itemHeight);

        double x = 0;
        double y = 0;
        double lineThickness = 0;

        // First pass: calculate line thicknesses for proper alignment
        var lineInfo = new List<LineInfo>();
        var currentLine = new LineInfo();

        foreach (var child in Children)
        {
            if (child is not FrameworkElement fe) continue;

            var childWidth = hasFixedWidth ? itemWidth : fe.DesiredSize.Width;
            var childHeight = hasFixedHeight ? itemHeight : fe.DesiredSize.Height;

            if (orientation == Orientation.Horizontal)
            {
                if (currentLine.Size + childWidth > finalSize.Width && currentLine.Elements.Count > 0)
                {
                    lineInfo.Add(currentLine);
                    currentLine = new LineInfo();
                }
                currentLine.Elements.Add((fe, childWidth, childHeight));
                currentLine.Size += childWidth;
                currentLine.Thickness = Math.Max(currentLine.Thickness, childHeight);
            }
            else
            {
                if (currentLine.Size + childHeight > finalSize.Height && currentLine.Elements.Count > 0)
                {
                    lineInfo.Add(currentLine);
                    currentLine = new LineInfo();
                }
                currentLine.Elements.Add((fe, childWidth, childHeight));
                currentLine.Size += childHeight;
                currentLine.Thickness = Math.Max(currentLine.Thickness, childWidth);
            }
        }

        if (currentLine.Elements.Count > 0)
        {
            lineInfo.Add(currentLine);
        }

        // Second pass: arrange children
        double offset = 0;
        foreach (var line in lineInfo)
        {
            double pos = 0;
            foreach (var (element, width, height) in line.Elements)
            {
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
