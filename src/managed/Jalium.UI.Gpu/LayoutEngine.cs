namespace Jalium.UI.Gpu;

/// <summary>
/// 布局引擎 - 编译时计算元素的最终位置和尺寸
/// </summary>
public sealed class LayoutEngine
{
    private readonly Dictionary<ElementNode, LayoutSlot> _layoutSlots = new();
    // Cache parsed grid definitions to avoid repeated string parsing in Measure+Arrange
    private readonly Dictionary<ElementNode, (List<GridLength> Rows, List<GridLength> Cols)> _gridDefCache = new();

    /// <summary>
    /// 计算整个元素树的布局
    /// </summary>
    public void ComputeLayout(ElementNode root, double availableWidth, double availableHeight)
    {
        _gridDefCache.Clear();

        // 第一遍：测量（自下而上）
        var desiredSize = Measure(root, availableWidth, availableHeight);

        // 第二遍：排列（自上而下）
        Arrange(root, new LayoutRect(0, 0, availableWidth, availableHeight));
    }

    private (List<GridLength> Rows, List<GridLength> Cols) GetGridDefs(ElementNode element)
    {
        if (!_gridDefCache.TryGetValue(element, out var defs))
        {
            defs = (ParseRowDefinitions(element), ParseColumnDefinitions(element));
            _gridDefCache[element] = defs;
        }
        return defs;
    }

    /// <summary>
    /// 获取元素的布局槽
    /// </summary>
    public LayoutSlot GetLayoutSlot(ElementNode element)
    {
        return _layoutSlots.TryGetValue(element, out var slot) ? slot : new LayoutSlot();
    }

    #region Measure Pass

    private LayoutSize Measure(ElementNode element, double availableWidth, double availableHeight)
    {
        // 获取 Margin
        var margin = element.Margin;
        var marginWidth = margin.Left + margin.Right;
        var marginHeight = margin.Top + margin.Bottom;

        // 减去 Margin 后的可用尺寸
        var contentAvailableWidth = Math.Max(0, availableWidth - marginWidth);
        var contentAvailableHeight = Math.Max(0, availableHeight - marginHeight);

        // 考虑显式尺寸
        var constrainedWidth = !double.IsNaN(element.Width) ? element.Width : contentAvailableWidth;
        var constrainedHeight = !double.IsNaN(element.Height) ? element.Height : contentAvailableHeight;

        // 根据元素类型计算期望尺寸
        var desiredSize = element.Type switch
        {
            ElementType.Grid => MeasureGrid(element, constrainedWidth, constrainedHeight),
            ElementType.StackPanel => MeasureStackPanel(element, constrainedWidth, constrainedHeight),
            ElementType.Canvas => MeasureCanvas(element, constrainedWidth, constrainedHeight),
            ElementType.DockPanel => MeasureDockPanel(element, constrainedWidth, constrainedHeight),
            ElementType.WrapPanel => MeasureWrapPanel(element, constrainedWidth, constrainedHeight),
            ElementType.TextBlock or ElementType.Label => MeasureText(element, constrainedWidth, constrainedHeight),
            ElementType.Image => MeasureImage(element, constrainedWidth, constrainedHeight),
            _ => MeasureDefault(element, constrainedWidth, constrainedHeight)
        };

        // 应用显式尺寸
        if (!double.IsNaN(element.Width))
            desiredSize = desiredSize with { Width = element.Width };
        if (!double.IsNaN(element.Height))
            desiredSize = desiredSize with { Height = element.Height };

        // 添加 Margin 到期望尺寸
        desiredSize = desiredSize with
        {
            Width = desiredSize.Width + marginWidth,
            Height = desiredSize.Height + marginHeight
        };

        // 存储测量结果
        if (!_layoutSlots.TryGetValue(element, out var slot))
        {
            slot = new LayoutSlot();
            _layoutSlots[element] = slot;
        }
        slot.DesiredSize = desiredSize;

        return desiredSize;
    }

    private LayoutSize MeasureGrid(ElementNode element, double availableWidth, double availableHeight)
    {
        // 使用缓存的行列定义（避免重复字符串解析）
        var (rowDefs, colDefs) = GetGridDefs(element);

        // 预计算固定尺寸和 star 计数（避免多次 LINQ 遍历）
        double fixedRowHeight = 0, fixedColWidth = 0;
        int starRowCount = 0, starColCount = 0;

        for (int i = 0; i < rowDefs.Count; i++)
        {
            if (rowDefs[i].Type == GridLengthType.Pixel) fixedRowHeight += rowDefs[i].Value;
            else if (rowDefs[i].Type == GridLengthType.Star) starRowCount++;
        }
        for (int i = 0; i < colDefs.Count; i++)
        {
            if (colDefs[i].Type == GridLengthType.Pixel) fixedColWidth += colDefs[i].Value;
            else if (colDefs[i].Type == GridLengthType.Star) starColCount++;
        }

        // 测量子元素
        foreach (var child in element.Children)
        {
            var row = GetGridRow(child);
            var col = GetGridColumn(child);
            var rowSpan = GetGridRowSpan(child);
            var colSpan = GetGridColumnSpan(child);

            // 计算可用空间（传入预计算的 star 数量）
            var cellWidth = CalculateCellSize(colDefs, col, colSpan, availableWidth, starColCount);
            var cellHeight = CalculateCellSize(rowDefs, row, rowSpan, availableHeight, starRowCount);

            Measure(child, cellWidth, cellHeight);
        }

        var desiredWidth = starColCount > 0 ? availableWidth : fixedColWidth;
        var desiredHeight = starRowCount > 0 ? availableHeight : fixedRowHeight;

        return new LayoutSize(desiredWidth, desiredHeight);
    }

    private LayoutSize MeasureStackPanel(ElementNode element, double availableWidth, double availableHeight)
    {
        var isHorizontal = GetOrientation(element) == Orientation.Horizontal;
        double totalWidth = 0, totalHeight = 0;

        foreach (var child in element.Children)
        {
            var childSize = Measure(child,
                isHorizontal ? double.PositiveInfinity : availableWidth,
                isHorizontal ? availableHeight : double.PositiveInfinity);

            if (isHorizontal)
            {
                totalWidth += childSize.Width;
                totalHeight = Math.Max(totalHeight, childSize.Height);
            }
            else
            {
                totalWidth = Math.Max(totalWidth, childSize.Width);
                totalHeight += childSize.Height;
            }
        }

        return new LayoutSize(totalWidth, totalHeight);
    }

    private LayoutSize MeasureCanvas(ElementNode element, double availableWidth, double availableHeight)
    {
        // Canvas 只测量子元素，不影响自身尺寸
        foreach (var child in element.Children)
        {
            Measure(child, double.PositiveInfinity, double.PositiveInfinity);
        }

        return new LayoutSize(availableWidth, availableHeight);
    }

    private LayoutSize MeasureDockPanel(ElementNode element, double availableWidth, double availableHeight)
    {
        double usedWidth = 0, usedHeight = 0;
        double remainingWidth = availableWidth, remainingHeight = availableHeight;

        for (int i = 0; i < element.Children.Count; i++)
        {
            var child = element.Children[i];
            var dock = GetDock(child);
            var isLast = i == element.Children.Count - 1;

            LayoutSize childSize;

            if (isLast)
            {
                childSize = Measure(child, remainingWidth, remainingHeight);
            }
            else
            {
                switch (dock)
                {
                    case Dock.Left:
                    case Dock.Right:
                        childSize = Measure(child, double.PositiveInfinity, remainingHeight);
                        remainingWidth -= childSize.Width;
                        break;
                    default:
                        childSize = Measure(child, remainingWidth, double.PositiveInfinity);
                        remainingHeight -= childSize.Height;
                        break;
                }
            }

            usedWidth = Math.Max(usedWidth, childSize.Width);
            usedHeight = Math.Max(usedHeight, childSize.Height);
        }

        return new LayoutSize(availableWidth, availableHeight);
    }

    private LayoutSize MeasureWrapPanel(ElementNode element, double availableWidth, double availableHeight)
    {
        var isHorizontal = GetOrientation(element) == Orientation.Horizontal;
        double totalWidth = 0, totalHeight = 0;
        double lineSize = 0, lineThickness = 0;

        foreach (var child in element.Children)
        {
            var childSize = Measure(child, double.PositiveInfinity, double.PositiveInfinity);

            if (isHorizontal)
            {
                if (lineSize + childSize.Width > availableWidth && lineSize > 0)
                {
                    totalWidth = Math.Max(totalWidth, lineSize);
                    totalHeight += lineThickness;
                    lineSize = 0;
                    lineThickness = 0;
                }
                lineSize += childSize.Width;
                lineThickness = Math.Max(lineThickness, childSize.Height);
            }
            else
            {
                if (lineSize + childSize.Height > availableHeight && lineSize > 0)
                {
                    totalHeight = Math.Max(totalHeight, lineSize);
                    totalWidth += lineThickness;
                    lineSize = 0;
                    lineThickness = 0;
                }
                lineSize += childSize.Height;
                lineThickness = Math.Max(lineThickness, childSize.Width);
            }
        }

        // 添加最后一行
        if (isHorizontal)
        {
            totalWidth = Math.Max(totalWidth, lineSize);
            totalHeight += lineThickness;
        }
        else
        {
            totalHeight = Math.Max(totalHeight, lineSize);
            totalWidth += lineThickness;
        }

        return new LayoutSize(totalWidth, totalHeight);
    }

    private LayoutSize MeasureText(ElementNode element, double availableWidth, double availableHeight)
    {
        var text = element.Text ?? "";
        var fontSize = element.FontSize > 0 ? element.FontSize : 14;

        // 简化的文本测量（实际需要字体度量）
        var charWidth = fontSize * 0.6;
        var lineHeight = fontSize * 1.2;

        // 简单估算
        var textWidth = text.Length * charWidth;
        var textHeight = lineHeight;

        // 如果需要换行
        if (textWidth > availableWidth && availableWidth > 0)
        {
            var lines = (int)Math.Ceiling(textWidth / availableWidth);
            textWidth = availableWidth;
            textHeight = lines * lineHeight;
        }

        return new LayoutSize(textWidth, textHeight);
    }

    private LayoutSize MeasureImage(ElementNode element, double availableWidth, double availableHeight)
    {
        // 使用显式尺寸或默认尺寸
        var width = !double.IsNaN(element.Width) ? element.Width : 100;
        var height = !double.IsNaN(element.Height) ? element.Height : 100;

        return new LayoutSize(width, height);
    }

    private LayoutSize MeasureDefault(ElementNode element, double availableWidth, double availableHeight)
    {
        // 测量所有子元素
        double maxWidth = 0, maxHeight = 0;

        foreach (var child in element.Children)
        {
            var childSize = Measure(child, availableWidth, availableHeight);
            maxWidth = Math.Max(maxWidth, childSize.Width);
            maxHeight = Math.Max(maxHeight, childSize.Height);
        }

        return new LayoutSize(
            maxWidth > 0 ? maxWidth : availableWidth,
            maxHeight > 0 ? maxHeight : availableHeight);
    }

    #endregion

    #region Arrange Pass

    private void Arrange(ElementNode element, LayoutRect finalRect)
    {
        // 获取 Margin
        var margin = element.Margin;

        // 计算内容区域（减去 Margin）
        var contentRect = new LayoutRect(
            finalRect.X + margin.Left,
            finalRect.Y + margin.Top,
            Math.Max(0, finalRect.Width - margin.Left - margin.Right),
            Math.Max(0, finalRect.Height - margin.Top - margin.Bottom));

        var slot = _layoutSlots[element];
        slot.FinalRect = contentRect;

        // 根据元素类型排列子元素
        switch (element.Type)
        {
            case ElementType.Grid:
                ArrangeGrid(element, contentRect);
                break;
            case ElementType.StackPanel:
                ArrangeStackPanel(element, contentRect);
                break;
            case ElementType.Canvas:
                ArrangeCanvas(element, contentRect);
                break;
            case ElementType.DockPanel:
                ArrangeDockPanel(element, contentRect);
                break;
            case ElementType.WrapPanel:
                ArrangeWrapPanel(element, contentRect);
                break;
            default:
                ArrangeDefault(element, contentRect);
                break;
        }
    }

    private void ArrangeGrid(ElementNode element, LayoutRect finalRect)
    {
        var (rowDefs, colDefs) = GetGridDefs(element);

        // 计算实际行高和列宽
        var rowHeights = CalculateGridSizes(rowDefs, finalRect.Height);
        var colWidths = CalculateGridSizes(colDefs, finalRect.Width);

        // 计算行列偏移
        var rowOffsets = new double[rowHeights.Length + 1];
        var colOffsets = new double[colWidths.Length + 1];

        for (int i = 0; i < rowHeights.Length; i++)
            rowOffsets[i + 1] = rowOffsets[i] + rowHeights[i];
        for (int i = 0; i < colWidths.Length; i++)
            colOffsets[i + 1] = colOffsets[i] + colWidths[i];

        // 排列子元素
        foreach (var child in element.Children)
        {
            var row = GetGridRow(child);
            var col = GetGridColumn(child);
            var rowSpan = GetGridRowSpan(child);
            var colSpan = GetGridColumnSpan(child);

            // 确保索引有效
            row = Math.Min(row, rowHeights.Length - 1);
            col = Math.Min(col, colWidths.Length - 1);
            var endRow = Math.Min(row + rowSpan, rowHeights.Length);
            var endCol = Math.Min(col + colSpan, colWidths.Length);

            var childRect = new LayoutRect(
                finalRect.X + colOffsets[col],
                finalRect.Y + rowOffsets[row],
                colOffsets[endCol] - colOffsets[col],
                rowOffsets[endRow] - rowOffsets[row]);

            Arrange(child, childRect);
        }
    }

    private void ArrangeStackPanel(ElementNode element, LayoutRect finalRect)
    {
        var isHorizontal = GetOrientation(element) == Orientation.Horizontal;
        double offset = 0;

        foreach (var child in element.Children)
        {
            var slot = _layoutSlots[child];

            LayoutRect childRect;
            if (isHorizontal)
            {
                childRect = new LayoutRect(
                    finalRect.X + offset,
                    finalRect.Y,
                    slot.DesiredSize.Width,
                    finalRect.Height);
                offset += slot.DesiredSize.Width;
            }
            else
            {
                childRect = new LayoutRect(
                    finalRect.X,
                    finalRect.Y + offset,
                    finalRect.Width,
                    slot.DesiredSize.Height);
                offset += slot.DesiredSize.Height;
            }

            Arrange(child, childRect);
        }
    }

    private void ArrangeCanvas(ElementNode element, LayoutRect finalRect)
    {
        foreach (var child in element.Children)
        {
            var slot = _layoutSlots[child];

            var childRect = new LayoutRect(
                finalRect.X + child.Left,
                finalRect.Y + child.Top,
                !double.IsNaN(child.Width) ? child.Width : slot.DesiredSize.Width,
                !double.IsNaN(child.Height) ? child.Height : slot.DesiredSize.Height);

            Arrange(child, childRect);
        }
    }

    private void ArrangeDockPanel(ElementNode element, LayoutRect finalRect)
    {
        double leftUsed = 0, topUsed = 0, rightUsed = 0, bottomUsed = 0;

        for (int i = 0; i < element.Children.Count; i++)
        {
            var child = element.Children[i];
            var slot = _layoutSlots[child];
            var dock = GetDock(child);
            var isLast = i == element.Children.Count - 1;

            LayoutRect childRect;

            if (isLast)
            {
                childRect = new LayoutRect(
                    finalRect.X + leftUsed,
                    finalRect.Y + topUsed,
                    Math.Max(0, finalRect.Width - leftUsed - rightUsed),
                    Math.Max(0, finalRect.Height - topUsed - bottomUsed));
            }
            else
            {
                switch (dock)
                {
                    case Dock.Left:
                        childRect = new LayoutRect(
                            finalRect.X + leftUsed,
                            finalRect.Y + topUsed,
                            slot.DesiredSize.Width,
                            Math.Max(0, finalRect.Height - topUsed - bottomUsed));
                        leftUsed += slot.DesiredSize.Width;
                        break;
                    case Dock.Right:
                        childRect = new LayoutRect(
                            finalRect.X + finalRect.Width - rightUsed - slot.DesiredSize.Width,
                            finalRect.Y + topUsed,
                            slot.DesiredSize.Width,
                            Math.Max(0, finalRect.Height - topUsed - bottomUsed));
                        rightUsed += slot.DesiredSize.Width;
                        break;
                    case Dock.Top:
                        childRect = new LayoutRect(
                            finalRect.X + leftUsed,
                            finalRect.Y + topUsed,
                            Math.Max(0, finalRect.Width - leftUsed - rightUsed),
                            slot.DesiredSize.Height);
                        topUsed += slot.DesiredSize.Height;
                        break;
                    default: // Bottom
                        childRect = new LayoutRect(
                            finalRect.X + leftUsed,
                            finalRect.Y + finalRect.Height - bottomUsed - slot.DesiredSize.Height,
                            Math.Max(0, finalRect.Width - leftUsed - rightUsed),
                            slot.DesiredSize.Height);
                        bottomUsed += slot.DesiredSize.Height;
                        break;
                }
            }

            Arrange(child, childRect);
        }
    }

    private void ArrangeWrapPanel(ElementNode element, LayoutRect finalRect)
    {
        var isHorizontal = GetOrientation(element) == Orientation.Horizontal;
        double lineOffset = 0, itemOffset = 0, lineThickness = 0;

        foreach (var child in element.Children)
        {
            var slot = _layoutSlots[child];

            if (isHorizontal)
            {
                if (itemOffset + slot.DesiredSize.Width > finalRect.Width && itemOffset > 0)
                {
                    lineOffset += lineThickness;
                    itemOffset = 0;
                    lineThickness = 0;
                }

                var childRect = new LayoutRect(
                    finalRect.X + itemOffset,
                    finalRect.Y + lineOffset,
                    slot.DesiredSize.Width,
                    slot.DesiredSize.Height);

                Arrange(child, childRect);

                itemOffset += slot.DesiredSize.Width;
                lineThickness = Math.Max(lineThickness, slot.DesiredSize.Height);
            }
            else
            {
                if (itemOffset + slot.DesiredSize.Height > finalRect.Height && itemOffset > 0)
                {
                    lineOffset += lineThickness;
                    itemOffset = 0;
                    lineThickness = 0;
                }

                var childRect = new LayoutRect(
                    finalRect.X + lineOffset,
                    finalRect.Y + itemOffset,
                    slot.DesiredSize.Width,
                    slot.DesiredSize.Height);

                Arrange(child, childRect);

                itemOffset += slot.DesiredSize.Height;
                lineThickness = Math.Max(lineThickness, slot.DesiredSize.Width);
            }
        }
    }

    private void ArrangeDefault(ElementNode element, LayoutRect finalRect)
    {
        foreach (var child in element.Children)
        {
            Arrange(child, finalRect);
        }
    }

    #endregion

    #region Helpers

    private static List<GridLength> ParseRowDefinitions(ElementNode element)
    {
        if (string.IsNullOrEmpty(element.RowDefinitions))
        {
            // 默认单行 Star
            return [new GridLength(GridLengthType.Star, 1)];
        }

        return ParseGridLengthList(element.RowDefinitions);
    }

    private static List<GridLength> ParseColumnDefinitions(ElementNode element)
    {
        if (string.IsNullOrEmpty(element.ColumnDefinitions))
        {
            // 默认单列 Star
            return [new GridLength(GridLengthType.Star, 1)];
        }

        return ParseGridLengthList(element.ColumnDefinitions);
    }

    private static List<GridLength> ParseGridLengthList(string definitions)
    {
        var result = new List<GridLength>();
        var parts = definitions.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            result.Add(ParseGridLength(part));
        }

        return result.Count > 0 ? result : [new GridLength(GridLengthType.Star, 1)];
    }

    private static GridLength ParseGridLength(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new GridLength(GridLengthType.Star, 1);

        value = value.Trim();

        // Auto
        if (value.Equals("Auto", StringComparison.OrdinalIgnoreCase))
            return new GridLength(GridLengthType.Auto, 0);

        // Star (e.g., "*", "2*", "1.5*")
        if (value.EndsWith('*'))
        {
            var starValue = value.TrimEnd('*');
            if (string.IsNullOrEmpty(starValue))
                return new GridLength(GridLengthType.Star, 1);

            if (double.TryParse(starValue, out var multiplier))
                return new GridLength(GridLengthType.Star, multiplier);

            return new GridLength(GridLengthType.Star, 1);
        }

        // Pixel value
        if (double.TryParse(value, out var pixels))
            return new GridLength(GridLengthType.Pixel, pixels);

        return new GridLength(GridLengthType.Star, 1);
    }

    private static int GetGridRow(ElementNode element) => element.GridRow;
    private static int GetGridColumn(ElementNode element) => element.GridColumn;
    private static int GetGridRowSpan(ElementNode element) => Math.Max(1, element.GridRowSpan);
    private static int GetGridColumnSpan(ElementNode element) => Math.Max(1, element.GridColumnSpan);

    private static Orientation GetOrientation(ElementNode element)
    {
        return element.Orientation;
    }

    private static Dock GetDock(ElementNode element)
    {
        return element.Dock;
    }

    private static double CalculateCellSize(List<GridLength> definitions, int start, int span, double available, int starCount)
    {
        double size = 0;
        var end = Math.Min(start + span, definitions.Count);

        for (int i = start; i < end; i++)
        {
            var def = definitions[i];
            size += def.Type switch
            {
                GridLengthType.Pixel => def.Value,
                GridLengthType.Star => starCount > 0 ? available / starCount * def.Value : 0,
                GridLengthType.Auto => double.PositiveInfinity,
                _ => 0
            };
        }

        return size;
    }

    private static double[] CalculateGridSizes(List<GridLength> definitions, double available)
    {
        var sizes = new double[definitions.Count == 0 ? 1 : definitions.Count];
        if (definitions.Count == 0)
        {
            sizes[0] = available;
            return sizes;
        }

        // 第一遍：分配固定尺寸
        double usedSpace = 0;
        double totalStars = 0;

        for (int i = 0; i < definitions.Count; i++)
        {
            var def = definitions[i];
            if (def.Type == GridLengthType.Pixel)
            {
                sizes[i] = def.Value;
                usedSpace += def.Value;
            }
            else if (def.Type == GridLengthType.Star)
            {
                totalStars += def.Value;
            }
        }

        // 第二遍：分配星号尺寸
        var remainingSpace = available - usedSpace;
        var starUnit = totalStars > 0 ? remainingSpace / totalStars : 0;

        for (int i = 0; i < definitions.Count; i++)
        {
            if (definitions[i].Type == GridLengthType.Star)
            {
                sizes[i] = starUnit * definitions[i].Value;
            }
        }

        return sizes;
    }

    #endregion
}

#region Layout Types

/// <summary>
/// 布局槽 - 存储元素的布局信息
/// </summary>
public sealed class LayoutSlot
{
    public LayoutSize DesiredSize { get; set; }
    public LayoutRect FinalRect { get; set; }
}

/// <summary>
/// 布局尺寸
/// </summary>
public readonly record struct LayoutSize(double Width, double Height)
{
    public static readonly LayoutSize Zero = new(0, 0);
    public static readonly LayoutSize Infinity = new(double.PositiveInfinity, double.PositiveInfinity);
}

/// <summary>
/// 布局矩形
/// </summary>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public static readonly LayoutRect Zero = new(0, 0, 0, 0);
}

/// <summary>
/// Grid 长度定义
/// </summary>
public readonly record struct GridLength(GridLengthType Type, double Value);

/// <summary>
/// Grid 长度类型
/// </summary>
public enum GridLengthType
{
    Auto,
    Pixel,
    Star
}

/// <summary>
/// 方向
/// </summary>
public enum Orientation
{
    Horizontal,
    Vertical
}

/// <summary>
/// DockPanel 停靠位置
/// </summary>
public enum Dock
{
    Left,
    Top,
    Right,
    Bottom
}

#endregion
