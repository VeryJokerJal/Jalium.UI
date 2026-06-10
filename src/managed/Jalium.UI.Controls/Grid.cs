using Jalium.UI;

namespace Jalium.UI.Controls;

/// <summary>
/// Defines a flexible grid area that consists of columns and rows.
/// </summary>
public class Grid : Panel
{
    #region Attached Properties

    /// <summary>
    /// Identifies the Row attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RowProperty =
        DependencyProperty.RegisterAttached("Row", typeof(int), typeof(Grid),
            new PropertyMetadata(0));

    /// <summary>
    /// Identifies the Column attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnProperty =
        DependencyProperty.RegisterAttached("Column", typeof(int), typeof(Grid),
            new PropertyMetadata(0));

    /// <summary>
    /// Identifies the RowSpan attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RowSpanProperty =
        DependencyProperty.RegisterAttached("RowSpan", typeof(int), typeof(Grid),
            new PropertyMetadata(1));

    /// <summary>
    /// Identifies the ColumnSpan attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnSpanProperty =
        DependencyProperty.RegisterAttached("ColumnSpan", typeof(int), typeof(Grid),
            new PropertyMetadata(1));

    /// <summary>
    /// Gets the value of the Row attached property for a given element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static int GetRow(UIElement element) =>
        (int)(element.GetValue(RowProperty) ?? 0);

    /// <summary>
    /// Sets the value of the Row attached property for a given element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static void SetRow(UIElement element, int value) =>
        element.SetValue(RowProperty, value);

    /// <summary>
    /// Gets the value of the Column attached property for a given element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static int GetColumn(UIElement element) =>
        (int)(element.GetValue(ColumnProperty) ?? 0);

    /// <summary>
    /// Sets the value of the Column attached property for a given element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static void SetColumn(UIElement element, int value) =>
        element.SetValue(ColumnProperty, value);

    /// <summary>
    /// Gets the value of the RowSpan attached property for a given element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static int GetRowSpan(UIElement element) =>
        (int)(element.GetValue(RowSpanProperty) ?? 1);

    /// <summary>
    /// Sets the value of the RowSpan attached property for a given element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static void SetRowSpan(UIElement element, int value) =>
        element.SetValue(RowSpanProperty, Math.Max(1, value));

    /// <summary>
    /// Gets the value of the ColumnSpan attached property for a given element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static int GetColumnSpan(UIElement element) =>
        (int)(element.GetValue(ColumnSpanProperty) ?? 1);

    /// <summary>
    /// Sets the value of the ColumnSpan attached property for a given element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static void SetColumnSpan(UIElement element, int value) =>
        element.SetValue(ColumnSpanProperty, Math.Max(1, value));

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the RowSpacing dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RowSpacingProperty =
        DependencyProperty.Register(nameof(RowSpacing), typeof(double), typeof(Grid),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the ColumnSpacing dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnSpacingProperty =
        DependencyProperty.Register(nameof(ColumnSpacing), typeof(double), typeof(Grid),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Grid grid)
        {
            grid.InvalidateMeasure();
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the collection of row definitions.
    /// </summary>
    public RowDefinitionCollection RowDefinitions { get; } = new();

    /// <summary>
    /// Gets the collection of column definitions.
    /// </summary>
    public ColumnDefinitionCollection ColumnDefinitions { get; } = new();

    /// <summary>
    /// Gets or sets the uniform distance in device-independent pixels between adjacent rows.
    /// Spacing is inserted between rows only and never leads, trails, or inflates spanned cells.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty)!;
        set => SetValue(RowSpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets the uniform distance in device-independent pixels between adjacent columns.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty)!;
        set => SetValue(ColumnSpacingProperty, value);
    }

    private static double SanitizeSpacing(double value) =>
        (double.IsNaN(value) || double.IsInfinity(value) || value < 0) ? 0 : value;

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // 让所有 Row/ColumnDefinition 知道自己的 owner Grid，这样 Width/Height 等
        // 运行时被改变时（例如 OpenTabsCol.Width = new GridLength(160)）能反向通知
        // Grid 重新 layout — 否则 framework 完全不知道 column/row 尺寸变了。
        for (int i = 0; i < RowDefinitions.Count; i++) RowDefinitions[i].OwnerGrid = this;
        for (int i = 0; i < ColumnDefinitions.Count; i++) ColumnDefinitions[i].OwnerGrid = this;

        // Ensure at least one row and column
        var rowCount = Math.Max(1, RowDefinitions.Count);
        var columnCount = Math.Max(1, ColumnDefinitions.Count);

        var rowSpacing = SanitizeSpacing(RowSpacing);
        var columnSpacing = SanitizeSpacing(ColumnSpacing);
        var totalRowSpacing = Math.Max(0, rowCount - 1) * rowSpacing;
        var totalColumnSpacing = Math.Max(0, columnCount - 1) * columnSpacing;

        // Initialize row and column sizes
        var rowHeights = new double[rowCount];
        var columnWidths = new double[columnCount];
        var rowStarValues = new double[rowCount];
        var columnStarValues = new double[columnCount];

        // Get definitions (use default if not defined)
        var rowDefs = GetEffectiveRowDefinitions(rowCount);
        var columnDefs = GetEffectiveColumnDefinitions(columnCount);

        // First pass: Calculate auto and fixed sizes
        for (int i = 0; i < rowCount; i++)
        {
            var def = rowDefs[i];
            if (def.Height.IsAbsolute)
            {
                rowHeights[i] = Math.Clamp(def.Height.Value, def.MinHeight, def.MaxHeight);
            }
            else if (def.Height.IsStar)
            {
                rowStarValues[i] = def.Height.Value;
            }
        }

        for (int i = 0; i < columnCount; i++)
        {
            var def = columnDefs[i];
            if (def.Width.IsAbsolute)
            {
                columnWidths[i] = Math.Clamp(def.Width.Value, def.MinWidth, def.MaxWidth);
            }
            else if (def.Width.IsStar)
            {
                columnStarValues[i] = def.Width.Value;
            }
        }

        // Measure children in auto rows/columns
        foreach (var child in Children)
        {
            if (child is not FrameworkElement fe) continue;

            var row = Math.Min(GetRow(child), rowCount - 1);
            var column = Math.Min(GetColumn(child), columnCount - 1);
            var rowSpan = Math.Min(GetRowSpan(child), rowCount - row);
            var columnSpan = Math.Min(GetColumnSpan(child), columnCount - column);

            // Check if child is in any auto row/column
            bool inAutoRow = false;
            bool inAutoColumn = false;
            for (int i = row; i < row + rowSpan; i++)
            {
                if (rowDefs[i].Height.IsAuto) inAutoRow = true;
            }
            for (int i = column; i < column + columnSpan; i++)
            {
                if (columnDefs[i].Width.IsAuto) inAutoColumn = true;
            }

            if (inAutoRow || inAutoColumn)
            {
                // Measure with available space
                fe.Measure(new Size(
                    inAutoColumn ? double.PositiveInfinity : availableSize.Width,
                    inAutoRow ? double.PositiveInfinity : availableSize.Height));

                // Update auto sizes. For single-cell items the desired size feeds the auto row/col directly.
                // For spanned auto items the internal spacing belongs to the child, so subtract it out
                // before distributing the remaining desired size across the spanned auto tracks.
                if (inAutoRow && rowSpan == 1)
                {
                    var def = rowDefs[row];
                    if (def.Height.IsAuto)
                    {
                        rowHeights[row] = Math.Max(rowHeights[row],
                            Math.Clamp(fe.DesiredSize.Height, def.MinHeight, def.MaxHeight));
                    }
                }

                if (inAutoColumn && columnSpan == 1)
                {
                    var def = columnDefs[column];
                    if (def.Width.IsAuto)
                    {
                        columnWidths[column] = Math.Max(columnWidths[column],
                            Math.Clamp(fe.DesiredSize.Width, def.MinWidth, def.MaxWidth));
                    }
                }
            }
        }

        // Calculate remaining space for star sizing (spacing lives between rows/columns, not inside tracks)
        double fixedRowHeight = rowHeights.Sum();
        double fixedColumnWidth = columnWidths.Sum();
        double totalRowStars = rowStarValues.Sum();
        double totalColumnStars = columnStarValues.Sum();

        double availableRowSpace = double.IsPositiveInfinity(availableSize.Height)
            ? double.PositiveInfinity
            : Math.Max(0, availableSize.Height - fixedRowHeight - totalRowSpacing);
        double availableColumnSpace = double.IsPositiveInfinity(availableSize.Width)
            ? double.PositiveInfinity
            : Math.Max(0, availableSize.Width - fixedColumnWidth - totalColumnSpacing);

        // Distribute star space
        // When available size is infinite (e.g. inside ScrollViewer), treat star as Auto (WPF behavior)
        if (totalRowStars > 0)
        {
            if (double.IsPositiveInfinity(availableRowSpace))
            {
                // Treat star rows as Auto: measure children and use their desired height
                foreach (var child in Children)
                {
                    if (child is not FrameworkElement fe) continue;
                    var row = Math.Min(GetRow(child), rowCount - 1);
                    var rowSpan = Math.Min(GetRowSpan(child), rowCount - row);
                    bool inStarRow = false;
                    for (int i = row; i < row + rowSpan; i++)
                    {
                        if (rowStarValues[i] > 0) { inStarRow = true; break; }
                    }
                    if (inStarRow)
                    {
                        fe.Measure(new Size(availableSize.Width, double.PositiveInfinity));
                        if (rowSpan == 1)
                        {
                            var def = rowDefs[row];
                            rowHeights[row] = Math.Max(rowHeights[row],
                                Math.Clamp(fe.DesiredSize.Height, def.MinHeight, def.MaxHeight));
                        }
                    }
                }
            }
            else
            {
                double starUnitHeight = availableRowSpace / totalRowStars;
                for (int i = 0; i < rowCount; i++)
                {
                    if (rowStarValues[i] > 0)
                    {
                        var def = rowDefs[i];
                        rowHeights[i] = Math.Clamp(starUnitHeight * rowStarValues[i], def.MinHeight, def.MaxHeight);
                    }
                }
            }
        }

        if (totalColumnStars > 0)
        {
            if (double.IsPositiveInfinity(availableColumnSpace))
            {
                // Treat star columns as Auto: measure children and use their desired width
                foreach (var child in Children)
                {
                    if (child is not FrameworkElement fe) continue;
                    var column = Math.Min(GetColumn(child), columnCount - 1);
                    var columnSpan = Math.Min(GetColumnSpan(child), columnCount - column);
                    bool inStarColumn = false;
                    for (int i = column; i < column + columnSpan; i++)
                    {
                        if (columnStarValues[i] > 0) { inStarColumn = true; break; }
                    }
                    if (inStarColumn)
                    {
                        fe.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                        if (columnSpan == 1)
                        {
                            var def = columnDefs[column];
                            columnWidths[column] = Math.Max(columnWidths[column],
                                Math.Clamp(fe.DesiredSize.Width, def.MinWidth, def.MaxWidth));
                        }
                    }
                }
            }
            else
            {
                double starUnitWidth = availableColumnSpace / totalColumnStars;
                for (int i = 0; i < columnCount; i++)
                {
                    if (columnStarValues[i] > 0)
                    {
                        var def = columnDefs[i];
                        columnWidths[i] = Math.Clamp(starUnitWidth * columnStarValues[i], def.MinWidth, def.MaxWidth);
                    }
                }
            }
        }

        var treatStarRowsAsAuto = totalRowStars > 0 && double.IsPositiveInfinity(availableRowSpace);
        var treatStarColumnsAsAuto = totalColumnStars > 0 && double.IsPositiveInfinity(availableColumnSpace);

        // Measure all children with their final available sizes.
        // A spanned cell owns the spacing between the tracks it crosses, so the available
        // size includes (span - 1) gap(s) along each axis.
        foreach (var child in Children)
        {
            if (child is not FrameworkElement fe) continue;

            var row = Math.Min(GetRow(child), rowCount - 1);
            var column = Math.Min(GetColumn(child), columnCount - 1);
            var rowSpan = Math.Min(GetRowSpan(child), rowCount - row);
            var columnSpan = Math.Min(GetColumnSpan(child), columnCount - column);

            double cellWidth = 0;
            double cellHeight = 0;

            for (int i = column; i < column + columnSpan; i++)
                cellWidth += columnWidths[i];
            for (int i = row; i < row + rowSpan; i++)
                cellHeight += rowHeights[i];

            cellWidth += Math.Max(0, columnSpan - 1) * columnSpacing;
            cellHeight += Math.Max(0, rowSpan - 1) * rowSpacing;

            bool spansAutoLikeRow = false;
            for (int i = row; i < row + rowSpan; i++)
            {
                if (rowDefs[i].Height.IsAuto || (treatStarRowsAsAuto && rowStarValues[i] > 0))
                {
                    spansAutoLikeRow = true;
                    break;
                }
            }

            // For wrapped content in auto rows, remeasure with the finalized cell width
            // but unconstrained height so desired height can grow after width is known.
            fe.Measure(new Size(cellWidth, spansAutoLikeRow ? double.PositiveInfinity : cellHeight));

            // Reconcile auto (and star-as-auto) definitions with final constrained measure.
            // This is important for wrapped text: final column width can increase required row height.
            if (rowSpan == 1)
            {
                var rowDef = rowDefs[row];
                if (rowDef.Height.IsAuto || (treatStarRowsAsAuto && rowStarValues[row] > 0))
                {
                    rowHeights[row] = Math.Max(
                        rowHeights[row],
                        Math.Clamp(fe.DesiredSize.Height, rowDef.MinHeight, rowDef.MaxHeight));
                }
            }

            if (columnSpan == 1)
            {
                var columnDef = columnDefs[column];
                if (columnDef.Width.IsAuto || (treatStarColumnsAsAuto && columnStarValues[column] > 0))
                {
                    columnWidths[column] = Math.Max(
                        columnWidths[column],
                        Math.Clamp(fe.DesiredSize.Width, columnDef.MinWidth, columnDef.MaxWidth));
                }
            }
        }

        // Store auto sizes (and star-as-auto sizes) in definitions so ArrangeOverride can read them
        for (int i = 0; i < rowCount; i++)
        {
            if (rowDefs[i].Height.IsAuto || (treatStarRowsAsAuto && rowStarValues[i] > 0))
                rowDefs[i].ActualHeight = rowHeights[i];
        }
        for (int i = 0; i < columnCount; i++)
        {
            if (columnDefs[i].Width.IsAuto || (treatStarColumnsAsAuto && columnStarValues[i] > 0))
                columnDefs[i].ActualWidth = columnWidths[i];
        }

        // Return the total size (spacing contributes to the grid's outer bounds)
        return new Size(columnWidths.Sum() + totalColumnSpacing, rowHeights.Sum() + totalRowSpacing);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var rowCount = Math.Max(1, RowDefinitions.Count);
        var columnCount = Math.Max(1, ColumnDefinitions.Count);

        var rowSpacing = SanitizeSpacing(RowSpacing);
        var columnSpacing = SanitizeSpacing(ColumnSpacing);
        var totalRowSpacing = Math.Max(0, rowCount - 1) * rowSpacing;
        var totalColumnSpacing = Math.Max(0, columnCount - 1) * columnSpacing;

        // Get definitions
        var rowDefs = GetEffectiveRowDefinitions(rowCount);
        var columnDefs = GetEffectiveColumnDefinitions(columnCount);

        // Calculate final row heights and column widths
        var rowHeights = new double[rowCount];
        var columnWidths = new double[columnCount];
        var rowStarValues = new double[rowCount];
        var columnStarValues = new double[columnCount];

        double fixedRowHeight = 0;
        double fixedColumnWidth = 0;
        double totalRowStars = 0;
        double totalColumnStars = 0;

        // First pass: fixed and auto sizes
        for (int i = 0; i < rowCount; i++)
        {
            var def = rowDefs[i];
            if (def.Height.IsAbsolute)
            {
                rowHeights[i] = Math.Clamp(def.Height.Value, def.MinHeight, def.MaxHeight);
                fixedRowHeight += rowHeights[i];
            }
            else if (def.Height.IsAuto)
            {
                // Use the measured auto size
                rowHeights[i] = Math.Clamp(def.ActualHeight, def.MinHeight, def.MaxHeight);
                fixedRowHeight += rowHeights[i];
            }
            else if (def.Height.IsStar)
            {
                rowStarValues[i] = def.Height.Value;
                totalRowStars += def.Height.Value;
            }
        }

        for (int i = 0; i < columnCount; i++)
        {
            var def = columnDefs[i];
            if (def.Width.IsAbsolute)
            {
                columnWidths[i] = Math.Clamp(def.Width.Value, def.MinWidth, def.MaxWidth);
                fixedColumnWidth += columnWidths[i];
            }
            else if (def.Width.IsAuto)
            {
                columnWidths[i] = Math.Clamp(def.ActualWidth, def.MinWidth, def.MaxWidth);
                fixedColumnWidth += columnWidths[i];
            }
            else if (def.Width.IsStar)
            {
                columnStarValues[i] = def.Width.Value;
                totalColumnStars += def.Width.Value;
            }
        }

        // Distribute star space (reserve inter-track spacing before handing remainder to stars)
        double availableRowSpace = Math.Max(0, finalSize.Height - fixedRowHeight - totalRowSpacing);
        double availableColumnSpace = Math.Max(0, finalSize.Width - fixedColumnWidth - totalColumnSpacing);

        if (totalRowStars > 0)
        {
            if (double.IsPositiveInfinity(availableRowSpace))
            {
                // Use measured sizes from MeasureOverride (star treated as Auto)
                for (int i = 0; i < rowCount; i++)
                {
                    if (rowStarValues[i] > 0)
                        rowHeights[i] = rowDefs[i].ActualHeight;
                }
            }
            else
            {
                double starUnitHeight = availableRowSpace / totalRowStars;
                double allocatedStarHeight = 0;
                int lastStarRow = -1;
                for (int i = 0; i < rowCount; i++)
                {
                    if (rowStarValues[i] > 0)
                    {
                        var def = rowDefs[i];
                        rowHeights[i] = Math.Clamp(starUnitHeight * rowStarValues[i], def.MinHeight, def.MaxHeight);
                        allocatedStarHeight += rowHeights[i];
                        lastStarRow = i;
                    }
                }
                // Give remaining pixels to last star row to avoid floating-point gaps
                if (lastStarRow >= 0)
                {
                    double remainder = availableRowSpace - allocatedStarHeight;
                    if (Math.Abs(remainder) > 0.001)
                    {
                        var def = rowDefs[lastStarRow];
                        rowHeights[lastStarRow] = Math.Clamp(rowHeights[lastStarRow] + remainder, def.MinHeight, def.MaxHeight);
                    }
                }
            }
        }

        if (totalColumnStars > 0)
        {
            if (double.IsPositiveInfinity(availableColumnSpace))
            {
                // Use measured sizes from MeasureOverride (star treated as Auto)
                for (int i = 0; i < columnCount; i++)
                {
                    if (columnStarValues[i] > 0)
                        columnWidths[i] = columnDefs[i].ActualWidth;
                }
            }
            else
            {
                double starUnitWidth = availableColumnSpace / totalColumnStars;
                double allocatedStarWidth = 0;
                int lastStarColumn = -1;
                for (int i = 0; i < columnCount; i++)
                {
                    if (columnStarValues[i] > 0)
                    {
                        var def = columnDefs[i];
                        columnWidths[i] = Math.Clamp(starUnitWidth * columnStarValues[i], def.MinWidth, def.MaxWidth);
                        allocatedStarWidth += columnWidths[i];
                        lastStarColumn = i;
                    }
                }
                // Give remaining pixels to last star column to avoid floating-point gaps
                if (lastStarColumn >= 0)
                {
                    double remainder = availableColumnSpace - allocatedStarWidth;
                    if (Math.Abs(remainder) > 0.001)
                    {
                        var def = columnDefs[lastStarColumn];
                        columnWidths[lastStarColumn] = Math.Clamp(columnWidths[lastStarColumn] + remainder, def.MinWidth, def.MaxWidth);
                    }
                }
            }
        }

        // Calculate track starts. rowStarts[i] is the Y of the i-th row's top edge including
        // the cumulative spacing between rows 0..i-1. Same for columns. A spanned cell uses
        // rightEdge(lastCol) - leftEdge(firstCol) so it naturally includes the internal gaps.
        var rowStarts = new double[rowCount];
        var columnStarts = new double[columnCount];

        double rowCursor = 0;
        for (int i = 0; i < rowCount; i++)
        {
            rowStarts[i] = rowCursor;
            rowDefs[i].ActualHeight = rowHeights[i];
            rowDefs[i].Offset = rowCursor;
            rowCursor += rowHeights[i];
            if (i < rowCount - 1) rowCursor += rowSpacing;
        }

        double columnCursor = 0;
        for (int i = 0; i < columnCount; i++)
        {
            columnStarts[i] = columnCursor;
            columnDefs[i].ActualWidth = columnWidths[i];
            columnDefs[i].Offset = columnCursor;
            columnCursor += columnWidths[i];
            if (i < columnCount - 1) columnCursor += columnSpacing;
        }

        // Arrange children
        foreach (var child in Children)
        {
            if (child is not FrameworkElement fe) continue;

            var row = Math.Clamp(GetRow(child), 0, rowCount - 1);
            var column = Math.Clamp(GetColumn(child), 0, columnCount - 1);
            var rowSpan = Math.Clamp(GetRowSpan(child), 1, rowCount - row);
            var columnSpan = Math.Clamp(GetColumnSpan(child), 1, columnCount - column);

            var lastRow = row + rowSpan - 1;
            var lastColumn = column + columnSpan - 1;

            double x = columnStarts[column];
            double y = rowStarts[row];
            double width = columnStarts[lastColumn] + columnWidths[lastColumn] - x;
            double height = rowStarts[lastRow] + rowHeights[lastRow] - y;

            var cellRect = new Rect(x, y, width, height);
            fe.Arrange(cellRect);
            // Note: Do NOT call SetVisualBounds here - ArrangeCore already handles margin
        }

        return finalSize;
    }

    private RowDefinition[] GetEffectiveRowDefinitions(int count)
    {
        var defs = new RowDefinition[count];
        for (int i = 0; i < count; i++)
        {
            defs[i] = i < RowDefinitions.Count
                ? RowDefinitions[i]
                : new RowDefinition { Height = GridLength.Star };
        }
        return defs;
    }

    private ColumnDefinition[] GetEffectiveColumnDefinitions(int count)
    {
        var defs = new ColumnDefinition[count];
        for (int i = 0; i < count; i++)
        {
            defs[i] = i < ColumnDefinitions.Count
                ? ColumnDefinitions[i]
                : new ColumnDefinition { Width = GridLength.Star };
        }
        return defs;
    }

    #endregion
}
