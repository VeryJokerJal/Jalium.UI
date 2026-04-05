using System.Collections.ObjectModel;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Displays hierarchical data as nested, space-filling rectangles where each rectangle's area
/// is proportional to its data value. Supports squarified, slice-and-dice, and binary layout algorithms.
/// </summary>
public class TreeMap : ChartBase
{
    #region Private State

    private static readonly SolidColorBrush s_defaultBorderBrush = new(Color.FromArgb(80, 0, 0, 0));
    private static readonly SolidColorBrush s_defaultLabelBrush = new(Color.FromRgb(240, 240, 240));

    /// <summary>
    /// Cached cell rectangles for hit testing (deepest-first ordering for nested drill-down).
    /// </summary>
    private readonly List<(Rect rect, TreeMapItem item, int depth)> _cellCache = new();

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Items dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(nameof(Items), typeof(ObservableCollection<TreeMapItem>), typeof(TreeMap),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Algorithm dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty AlgorithmProperty =
        DependencyProperty.Register(nameof(Algorithm), typeof(TreeMapAlgorithm), typeof(TreeMap),
            new PropertyMetadata(TreeMapAlgorithm.Squarified, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the MinCellSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinCellSizeProperty =
        DependencyProperty.Register(nameof(MinCellSize), typeof(double), typeof(TreeMap),
            new PropertyMetadata(20.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowLabels dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowLabelsProperty =
        DependencyProperty.Register(nameof(ShowLabels), typeof(bool), typeof(TreeMap),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the LabelMinFontSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty LabelMinFontSizeProperty =
        DependencyProperty.Register(nameof(LabelMinFontSize), typeof(double), typeof(TreeMap),
            new PropertyMetadata(8.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the CellPadding dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty CellPaddingProperty =
        DependencyProperty.Register(nameof(CellPadding), typeof(double), typeof(TreeMap),
            new PropertyMetadata(2.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the CellBorderBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CellBorderBrushProperty =
        DependencyProperty.Register(nameof(CellBorderBrush), typeof(Brush), typeof(TreeMap),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the CellBorderThickness dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CellBorderThicknessProperty =
        DependencyProperty.Register(nameof(CellBorderThickness), typeof(double), typeof(TreeMap),
            new PropertyMetadata(1.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the DrillDownEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty DrillDownEnabledProperty =
        DependencyProperty.Register(nameof(DrillDownEnabled), typeof(bool), typeof(TreeMap),
            new PropertyMetadata(false));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the collection of top-level tree map items.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public ObservableCollection<TreeMapItem> Items
    {
        get
        {
            var items = (ObservableCollection<TreeMapItem>?)GetValue(ItemsProperty);
            if (items == null)
            {
                items = new ObservableCollection<TreeMapItem>();
                SetValue(ItemsProperty, items);
            }
            return items;
        }
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>
    /// Gets or sets the layout algorithm.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public TreeMapAlgorithm Algorithm
    {
        get => (TreeMapAlgorithm)GetValue(AlgorithmProperty)!;
        set => SetValue(AlgorithmProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum dimension for cells. Cells smaller than this are not drawn.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MinCellSize
    {
        get => (double)GetValue(MinCellSizeProperty)!;
        set => SetValue(MinCellSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets whether labels are shown inside cells.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowLabels
    {
        get => (bool)GetValue(ShowLabelsProperty)!;
        set => SetValue(ShowLabelsProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum font size for cell labels.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public double LabelMinFontSize
    {
        get => (double)GetValue(LabelMinFontSizeProperty)!;
        set => SetValue(LabelMinFontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the padding between cells.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double CellPadding
    {
        get => (double)GetValue(CellPaddingProperty)!;
        set => SetValue(CellPaddingProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for cell borders.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? CellBorderBrush
    {
        get => (Brush?)GetValue(CellBorderBrushProperty);
        set => SetValue(CellBorderBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the thickness of cell borders.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double CellBorderThickness
    {
        get => (double)GetValue(CellBorderThicknessProperty)!;
        set => SetValue(CellBorderThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets whether clicking a cell with children will drill down into it.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public bool DrillDownEnabled
    {
        get => (bool)GetValue(DrillDownEnabledProperty)!;
        set => SetValue(DrillDownEnabledProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="TreeMap"/> class.
    /// </summary>
    public TreeMap()
    {
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnTreeMapMouseMove));
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnTreeMapMouseLeave));
    }

    #endregion

    #region Automation

    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.TreeMapAutomationPeer(this);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void RenderChart(DrawingContext dc, Rect plotArea)
    {
        var items = (ObservableCollection<TreeMapItem>?)GetValue(ItemsProperty);
        if (items == null || items.Count == 0)
            return;

        _cellCache.Clear();

        var borderPen = CellBorderBrush != null && CellBorderThickness > 0
            ? new Pen(CellBorderBrush ?? s_defaultBorderBrush, CellBorderThickness)
            : new Pen(s_defaultBorderBrush, CellBorderThickness);

        RenderItems(dc, items, plotArea, borderPen, depth: 0);
    }

    private void RenderItems(DrawingContext dc, IList<TreeMapItem> items, Rect bounds,
        Pen borderPen, int depth)
    {
        if (bounds.Width < 1 || bounds.Height < 1 || items.Count == 0)
            return;

        // Filter items with positive values and compute layout
        var validItems = new List<TreeMapItem>();
        double totalValue = 0;
        foreach (var item in items)
        {
            double val = GetEffectiveValue(item);
            if (val > 0)
            {
                validItems.Add(item);
                totalValue += val;
            }
        }

        if (validItems.Count == 0 || totalValue < 1e-15)
            return;

        // Compute rectangles using the selected algorithm
        var rects = Algorithm switch
        {
            TreeMapAlgorithm.Squarified => LayoutSquarified(validItems, bounds, totalValue),
            TreeMapAlgorithm.SliceAndDice => LayoutSliceAndDice(validItems, bounds, totalValue, depth),
            TreeMapAlgorithm.Binary => LayoutBinary(validItems, bounds, totalValue),
            _ => LayoutSquarified(validItems, bounds, totalValue)
        };

        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var labelBrush = s_defaultLabelBrush;
        double padding = CellPadding;
        double cornerRadius = 2;

        for (int i = 0; i < rects.Count; i++)
        {
            var item = rects[i].item;
            var rect = rects[i].rect;

            // Apply padding
            var paddedRect = new Rect(
                rect.X + padding,
                rect.Y + padding,
                Math.Max(0, rect.Width - padding * 2),
                Math.Max(0, rect.Height - padding * 2));

            if (paddedRect.Width < MinCellSize || paddedRect.Height < MinCellSize)
                continue;

            // Get brush from item or palette
            var brush = item.Brush ?? GetSeriesBrush(i + depth * 7);

            dc.DrawRoundedRectangle(brush, borderPen, paddedRect, cornerRadius, cornerRadius);

            // Cache for hit testing
            _cellCache.Add((paddedRect, item, depth));

            // Render children recursively if item has children
            bool hasChildren = item.Children.Count > 0;
            if (hasChildren)
            {
                // Reserve top area for parent label, render children in remaining space
                double headerHeight = ShowLabels ? Math.Max(16, LabelMinFontSize + 6) : 0;
                var childArea = new Rect(
                    paddedRect.X + 1,
                    paddedRect.Y + headerHeight + 1,
                    Math.Max(0, paddedRect.Width - 2),
                    Math.Max(0, paddedRect.Height - headerHeight - 2));

                if (ShowLabels)
                {
                    DrawCellLabel(dc, item.Label, paddedRect, fontFamily, labelBrush, isHeader: true);
                }

                if (childArea.Width > MinCellSize && childArea.Height > MinCellSize)
                {
                    RenderItems(dc, item.Children, childArea, borderPen, depth + 1);
                }
            }
            else if (ShowLabels)
            {
                // Draw label clipped to cell
                DrawCellLabel(dc, item.Label, paddedRect, fontFamily, labelBrush, isHeader: false);
            }
        }
    }

    private void DrawCellLabel(DrawingContext dc, string label, Rect cellRect,
        string fontFamily, Brush foreground, bool isHeader)
    {
        if (string.IsNullOrEmpty(label))
            return;

        // Compute font size that fits the cell
        double fontSize = isHeader
            ? Math.Max(LabelMinFontSize, Math.Min(14, cellRect.Height * 0.8))
            : Math.Max(LabelMinFontSize, Math.Min(14, Math.Min(cellRect.Height * 0.4, cellRect.Width * 0.15)));

        if (fontSize < LabelMinFontSize)
            return;

        var ft = new FormattedText(label, fontFamily, fontSize)
        {
            Foreground = foreground
        };
        TextMeasurement.MeasureText(ft);

        // Clip to cell bounds
        dc.PushClip(new RectangleGeometry(cellRect));

        double textX, textY;
        if (isHeader)
        {
            // Top-left aligned header
            textX = cellRect.X + 3;
            textY = cellRect.Y + 2;
        }
        else
        {
            // Centered in cell
            textX = cellRect.X + (cellRect.Width - ft.Width) / 2.0;
            textY = cellRect.Y + (cellRect.Height - ft.Height) / 2.0;
        }

        dc.DrawText(ft, new Point(textX, textY));
        dc.Pop(); // Pop clip
    }

    #endregion

    #region Squarified Layout

    private static List<(TreeMapItem item, Rect rect)> LayoutSquarified(
        List<TreeMapItem> items, Rect bounds, double totalValue)
    {
        var result = new List<(TreeMapItem item, Rect rect)>(items.Count);

        // Sort by value descending for squarified algorithm
        var sorted = new List<TreeMapItem>(items);
        sorted.Sort((a, b) => GetEffectiveValue(b).CompareTo(GetEffectiveValue(a)));

        SquarifyRecursive(sorted, 0, bounds, totalValue, result);
        return result;
    }

    private static void SquarifyRecursive(List<TreeMapItem> items, int startIndex,
        Rect bounds, double totalValue, List<(TreeMapItem item, Rect rect)> result)
    {
        if (startIndex >= items.Count || bounds.Width < 1 || bounds.Height < 1)
            return;

        if (startIndex == items.Count - 1)
        {
            // Last item gets the remaining space
            result.Add((items[startIndex], bounds));
            return;
        }

        // Determine the shorter side of the remaining rectangle
        double shorterSide = Math.Min(bounds.Width, bounds.Height);
        bool layoutVertically = bounds.Width >= bounds.Height;

        // Greedily add items to the current row until the aspect ratio worsens
        var rowItems = new List<int>();
        double rowTotal = 0;
        double bestWorstAspect = double.MaxValue;

        for (int i = startIndex; i < items.Count; i++)
        {
            double itemValue = GetEffectiveValue(items[i]);
            double candidateTotal = rowTotal + itemValue;

            // Compute worst aspect ratio if we add this item to the row
            double rowLength = candidateTotal / totalValue * (layoutVertically ? bounds.Width : bounds.Height);
            if (rowLength < 1e-10)
            {
                rowItems.Add(i);
                rowTotal = candidateTotal;
                continue;
            }

            double worstAspect = 0;
            double remaining = candidateTotal;
            for (int j = startIndex; j <= i; j++)
            {
                double v = GetEffectiveValue(items[j]);
                double itemLength = v / candidateTotal * shorterSide;
                if (itemLength < 1e-10) continue;
                double aspect = Math.Max(rowLength / itemLength, itemLength / rowLength);
                worstAspect = Math.Max(worstAspect, aspect);
            }

            if (worstAspect <= bestWorstAspect || rowItems.Count == 0)
            {
                bestWorstAspect = worstAspect;
                rowItems.Add(i);
                rowTotal = candidateTotal;
            }
            else
            {
                break;
            }
        }

        // Layout the row
        double rowFraction = rowTotal / totalValue;
        double rowSize = layoutVertically
            ? bounds.Width * rowFraction
            : bounds.Height * rowFraction;

        double offset = 0;
        foreach (int idx in rowItems)
        {
            double itemFraction = GetEffectiveValue(items[idx]) / rowTotal;
            double itemSize = shorterSide * itemFraction;

            Rect itemRect;
            if (layoutVertically)
            {
                itemRect = new Rect(bounds.X, bounds.Y + offset, rowSize, itemSize);
            }
            else
            {
                itemRect = new Rect(bounds.X + offset, bounds.Y, itemSize, rowSize);
            }
            result.Add((items[idx], itemRect));
            offset += itemSize;
        }

        // Recurse for remaining items
        int nextStart = rowItems[rowItems.Count - 1] + 1;
        double remainingValue = totalValue - rowTotal;

        Rect remainingBounds;
        if (layoutVertically)
        {
            remainingBounds = new Rect(bounds.X + rowSize, bounds.Y,
                Math.Max(0, bounds.Width - rowSize), bounds.Height);
        }
        else
        {
            remainingBounds = new Rect(bounds.X, bounds.Y + rowSize,
                bounds.Width, Math.Max(0, bounds.Height - rowSize));
        }

        if (remainingValue > 1e-15)
        {
            SquarifyRecursive(items, nextStart, remainingBounds, remainingValue, result);
        }
    }

    #endregion

    #region Slice-and-Dice Layout

    private static List<(TreeMapItem item, Rect rect)> LayoutSliceAndDice(
        List<TreeMapItem> items, Rect bounds, double totalValue, int depth)
    {
        var result = new List<(TreeMapItem item, Rect rect)>(items.Count);
        bool horizontal = (depth % 2) == 0;

        double offset = 0;
        foreach (var item in items)
        {
            double val = GetEffectiveValue(item);
            if (val <= 0) continue;

            double fraction = val / totalValue;
            Rect rect;
            if (horizontal)
            {
                double width = bounds.Width * fraction;
                rect = new Rect(bounds.X + offset, bounds.Y, width, bounds.Height);
                offset += width;
            }
            else
            {
                double height = bounds.Height * fraction;
                rect = new Rect(bounds.X, bounds.Y + offset, bounds.Width, height);
                offset += height;
            }
            result.Add((item, rect));
        }

        return result;
    }

    #endregion

    #region Binary Layout

    private static List<(TreeMapItem item, Rect rect)> LayoutBinary(
        List<TreeMapItem> items, Rect bounds, double totalValue)
    {
        var result = new List<(TreeMapItem item, Rect rect)>(items.Count);
        BinaryRecursive(items, 0, items.Count - 1, bounds, totalValue, result);
        return result;
    }

    private static void BinaryRecursive(List<TreeMapItem> items, int lo, int hi,
        Rect bounds, double totalValue, List<(TreeMapItem item, Rect rect)> result)
    {
        if (lo > hi || bounds.Width < 1 || bounds.Height < 1)
            return;

        if (lo == hi)
        {
            result.Add((items[lo], bounds));
            return;
        }

        // Find the split point that divides the total value roughly in half
        double halfTarget = totalValue / 2.0;
        double runningSum = 0;
        int splitIdx = lo;

        for (int i = lo; i <= hi; i++)
        {
            runningSum += GetEffectiveValue(items[i]);
            if (runningSum >= halfTarget)
            {
                splitIdx = i;
                break;
            }
        }

        // Ensure at least one item in each partition
        if (splitIdx == lo && lo < hi) splitIdx = lo;
        if (splitIdx == hi && lo < hi) splitIdx = hi - 1;

        double leftTotal = 0;
        for (int i = lo; i <= splitIdx; i++)
            leftTotal += GetEffectiveValue(items[i]);
        double rightTotal = totalValue - leftTotal;

        double fraction = totalValue > 1e-15 ? leftTotal / totalValue : 0.5;

        Rect leftBounds, rightBounds;
        if (bounds.Width >= bounds.Height)
        {
            double splitX = bounds.X + bounds.Width * fraction;
            leftBounds = new Rect(bounds.X, bounds.Y, bounds.Width * fraction, bounds.Height);
            rightBounds = new Rect(splitX, bounds.Y, bounds.Width * (1 - fraction), bounds.Height);
        }
        else
        {
            double splitY = bounds.Y + bounds.Height * fraction;
            leftBounds = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height * fraction);
            rightBounds = new Rect(bounds.X, splitY, bounds.Width, bounds.Height * (1 - fraction));
        }

        BinaryRecursive(items, lo, splitIdx, leftBounds, leftTotal, result);
        if (splitIdx + 1 <= hi)
        {
            BinaryRecursive(items, splitIdx + 1, hi, rightBounds, rightTotal, result);
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Gets the effective value of a tree map item, considering its children.
    /// </summary>
    private static double GetEffectiveValue(TreeMapItem item)
    {
        if (item.Children.Count > 0)
        {
            double sum = 0;
            foreach (var child in item.Children)
                sum += GetEffectiveValue(child);
            return sum > 0 ? sum : item.Value;
        }
        return Math.Max(0, item.Value);
    }

    #endregion

    #region Hit Testing

    private void OnTreeMapMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsTooltipEnabled || _cellCache.Count == 0)
            return;

        var pos = e.GetPosition(this);

        // Find the deepest (last added at greatest depth) cell containing the mouse position
        TreeMapItem? hitItem = null;
        int bestDepth = -1;

        for (int i = _cellCache.Count - 1; i >= 0; i--)
        {
            var (rect, item, depth) = _cellCache[i];
            if (rect.Contains(pos) && depth > bestDepth)
            {
                hitItem = item;
                bestDepth = depth;
            }
        }

        if (hitItem != null)
        {
            var valueText = GetEffectiveValue(hitItem).ToString("G6");
            ShowTooltip(pos.X, pos.Y, null, hitItem.Label, valueText);
        }
        else
        {
            HideTooltip();
        }
    }

    private void OnTreeMapMouseLeave(object sender, MouseEventArgs e)
    {
        HideTooltip();
    }

    #endregion
}
