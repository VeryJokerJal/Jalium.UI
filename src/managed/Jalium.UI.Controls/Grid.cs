using System.Runtime.CompilerServices;
using Jalium.UI;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Defines a flexible grid area that consists of columns and rows.
/// </summary>
public class Grid : Panel
{
    private static readonly ConditionalWeakTable<UIElement, SharedSizeScopeState> s_sharedSizeScopes = new();
    private static readonly Pen s_gridLinePen = new(new SolidColorBrush(Color.FromArgb(160, 96, 96, 96)), 1);
    private SharedSizeScopeState? _sharedSizeState;
    private RowDefinitionCollection? _rowDefinitions;
    private ColumnDefinitionCollection? _columnDefinitions;

    #region Attached Properties

    /// <summary>Identifies whether an element is the scope for shared row and column sizing.</summary>
    public static readonly DependencyProperty IsSharedSizeScopeProperty =
        DependencyProperty.RegisterAttached("IsSharedSizeScope", typeof(bool), typeof(Grid),
            new PropertyMetadata(false, OnIsSharedSizeScopeChanged));

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

    public static bool GetIsSharedSizeScope(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (bool)(element.GetValue(IsSharedSizeScopeProperty) ?? false);
    }

    public static void SetIsSharedSizeScope(UIElement element, bool value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(IsSharedSizeScopeProperty, value);
    }

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

    /// <summary>Identifies the debug grid-line visibility property.</summary>
    public static readonly DependencyProperty ShowGridLinesProperty =
        DependencyProperty.Register(nameof(ShowGridLines), typeof(bool), typeof(Grid),
            new PropertyMetadata(false, OnShowGridLinesChanged));

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Grid grid)
        {
            grid.InvalidateMeasure();
        }
    }

    private static void OnShowGridLinesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((Grid)d).InvalidateVisual();

    private static void OnIsSharedSizeScopeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element)
        {
            InvalidateSharedSizeDescendants(element);
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the collection of row definitions.
    /// </summary>
    public RowDefinitionCollection RowDefinitions
    {
        get => _rowDefinitions ??= new RowDefinitionCollection(this);
        set
        {
            if (value?.Owner is not null)
            {
                if (ReferenceEquals(value.Owner, this))
                    return;

                throw new ArgumentException("The collection already belongs to another Grid.", nameof(value));
            }

            if (_rowDefinitions is not null)
                _rowDefinitions.Owner = null;

            _rowDefinitions = null;
            if (value is not null)
            {
                value.Owner = this;
                _rowDefinitions = value;
            }

            OnDefinitionChanged();
        }
    }

    /// <summary>
    /// Gets or sets the collection of column definitions.
    /// </summary>
    public ColumnDefinitionCollection ColumnDefinitions
    {
        get => _columnDefinitions ??= new ColumnDefinitionCollection(this);
        set
        {
            if (value?.Owner is not null)
            {
                if (ReferenceEquals(value.Owner, this))
                    return;

                throw new ArgumentException("The collection already belongs to another Grid.", nameof(value));
            }

            if (_columnDefinitions is not null)
                _columnDefinitions.Owner = null;

            _columnDefinitions = null;
            if (value is not null)
            {
                value.Owner = this;
                _columnDefinitions = value;
            }

            OnDefinitionChanged();
        }
    }

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

    /// <summary>Gets or sets whether debugging lines are drawn between grid cells.</summary>
    public bool ShowGridLines
    {
        get => (bool)(GetValue(ShowGridLinesProperty) ?? false);
        set => SetValue(ShowGridLinesProperty, value);
    }

    public bool ShouldSerializeColumnDefinitions() => ColumnDefinitions.Count > 0;

    public bool ShouldSerializeRowDefinitions() => RowDefinitions.Count > 0;

    private static double SanitizeSpacing(double value) =>
        (double.IsNaN(value) || double.IsInfinity(value) || value < 0) ? 0 : value;

    private static bool IsSharedStar(RowDefinition definition) =>
        definition.Height.IsStar && definition.SharedSizeGroup != null;

    private static bool IsSharedStar(ColumnDefinition definition) =>
        definition.Width.IsStar && definition.SharedSizeGroup != null;

    internal void OnDefinitionChanged()
    {
        _sharedSizeState?.Remove(this);
        _sharedSizeState = null;
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }

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

        // Content (max child desired) per track. A star track FILLS its proportional allocation
        // when arranged, but at measure time it must report only the size its content needs — same
        // as the infinite-constraint "treat star as Auto" path below. Reporting the full allocation
        // as the grid's DesiredSize makes the grid demand all available space, which balloons it
        // inside content-sizing parents (WrapPanel / horizontal StackPanel / auto-sized
        // Border|Button). These arrays capture the content size so the final return can use it for
        // star tracks measured under a finite constraint; ArrangeOverride still fills from finalSize.
        var rowContent = new double[rowCount];
        var columnContent = new double[columnCount];

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
            else if (def.Height.IsStar && string.IsNullOrEmpty(def.SharedSizeGroup))
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
            else if (def.Width.IsStar && string.IsNullOrEmpty(def.SharedSizeGroup))
            {
                columnStarValues[i] = def.Width.Value;
            }
        }

        // Measure children in auto rows/columns
        foreach (UIElement child in Children)
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
                if (rowDefs[i].Height.IsAuto || IsSharedStar(rowDefs[i])) inAutoRow = true;
            }
            for (int i = column; i < column + columnSpan; i++)
            {
                if (columnDefs[i].Width.IsAuto || IsSharedStar(columnDefs[i])) inAutoColumn = true;
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
                    if (def.Height.IsAuto || IsSharedStar(def))
                    {
                        rowHeights[row] = Math.Max(rowHeights[row],
                            Math.Clamp(fe.DesiredSize.Height, def.MinHeight, def.MaxHeight));
                    }
                }

                if (inAutoColumn && columnSpan == 1)
                {
                    var def = columnDefs[column];
                    if (def.Width.IsAuto || IsSharedStar(def))
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
                foreach (UIElement child in Children)
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
                foreach (UIElement child in Children)
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
        foreach (UIElement child in Children)
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

            var needsUnconstrainedHeight = false;
            for (var index = row; index < row + rowSpan; index++)
            {
                if (rowDefs[index].Height.IsAuto || IsSharedStar(rowDefs[index]) ||
                    (treatStarRowsAsAuto && rowStarValues[index] > 0))
                {
                    needsUnconstrainedHeight = true;
                    break;
                }
            }

            // Once columns have their final constrained widths, auto-height content must be
            // measured without the provisional row height. Wrapped text otherwise remains
            // clipped to the one-line height discovered during the initial infinite-width pass.
            fe.Measure(new Size(
                cellWidth,
                needsUnconstrainedHeight ? double.PositiveInfinity : cellHeight));

            // Capture content size for single-span children so star tracks can report a
            // content-based DesiredSize (see the rowContent/columnContent declaration). Spanned
            // children are skipped, consistent with the auto-track reconciliation below.
            if (rowSpan == 1)
                rowContent[row] = Math.Max(rowContent[row], fe.DesiredSize.Height);
            if (columnSpan == 1)
                columnContent[column] = Math.Max(columnContent[column], fe.DesiredSize.Width);

            // Reconcile auto (and star-as-auto) definitions with final constrained measure.
            // This is important for wrapped text: final column width can increase required row height.
            if (rowSpan == 1)
            {
                var rowDef = rowDefs[row];
                if (rowDef.Height.IsAuto || IsSharedStar(rowDef) ||
                    (treatStarRowsAsAuto && rowStarValues[row] > 0))
                {
                    rowHeights[row] = Math.Max(
                        rowHeights[row],
                        Math.Clamp(fe.DesiredSize.Height, rowDef.MinHeight, rowDef.MaxHeight));
                }
            }

            if (columnSpan == 1)
            {
                var columnDef = columnDefs[column];
                if (columnDef.Width.IsAuto || IsSharedStar(columnDef) ||
                    (treatStarColumnsAsAuto && columnStarValues[column] > 0))
                {
                    columnWidths[column] = Math.Max(
                        columnWidths[column],
                        Math.Clamp(fe.DesiredSize.Width, columnDef.MinWidth, columnDef.MaxWidth));
                }
            }
        }

        ApplySharedSizes(rowDefs, columnDefs, rowHeights, columnWidths);

        // Store auto sizes (and star-as-auto sizes) in definitions so ArrangeOverride can read them
        for (int i = 0; i < rowCount; i++)
        {
            if (rowDefs[i].Height.IsAuto || (treatStarRowsAsAuto && rowStarValues[i] > 0) ||
                !string.IsNullOrWhiteSpace(rowDefs[i].SharedSizeGroup))
                rowDefs[i].ActualHeight = rowHeights[i];
        }
        for (int i = 0; i < columnCount; i++)
        {
            if (columnDefs[i].Width.IsAuto || (treatStarColumnsAsAuto && columnStarValues[i] > 0) ||
                !string.IsNullOrWhiteSpace(columnDefs[i].SharedSizeGroup))
                columnDefs[i].ActualWidth = columnWidths[i];
        }

        // Return the grid's DESIRED size. Absolute/Auto tracks (and star tracks treated as Auto
        // under an infinite constraint) already hold their fixed/content size in row/columnWidths.
        // A star track measured under a FINITE constraint, however, holds its full proportional
        // ALLOCATION — returning that would make the grid demand all available space and balloon
        // inside content-sizing parents. For those tracks report the content size instead; the
        // allocation is still applied at arrange (ArrangeOverride), so stretch-to-fill is unaffected.
        // (Spacing contributes to the grid's outer bounds regardless.)
        double desiredWidth = totalColumnSpacing;
        for (int i = 0; i < columnCount; i++)
        {
            var def = columnDefs[i];
            desiredWidth += (def.Width.IsStar && !treatStarColumnsAsAuto && string.IsNullOrWhiteSpace(def.SharedSizeGroup))
                ? Math.Clamp(columnContent[i], def.MinWidth, def.MaxWidth)
                : columnWidths[i];
        }

        double desiredHeight = totalRowSpacing;
        for (int i = 0; i < rowCount; i++)
        {
            var def = rowDefs[i];
            desiredHeight += (def.Height.IsStar && !treatStarRowsAsAuto && string.IsNullOrWhiteSpace(def.SharedSizeGroup))
                ? Math.Clamp(rowContent[i], def.MinHeight, def.MaxHeight)
                : rowHeights[i];
        }

        return new Size(desiredWidth, desiredHeight);
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
            if (!string.IsNullOrWhiteSpace(def.SharedSizeGroup))
            {
                rowHeights[i] = Math.Max(def.ActualHeight, def.MinHeight);
                fixedRowHeight += rowHeights[i];
            }
            else if (def.Height.IsAbsolute)
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
            if (!string.IsNullOrWhiteSpace(def.SharedSizeGroup))
            {
                columnWidths[i] = Math.Max(def.ActualWidth, def.MinWidth);
                fixedColumnWidth += columnWidths[i];
            }
            else if (def.Width.IsAbsolute)
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
        foreach (UIElement child in Children)
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

    protected override void OnPostRender(DrawingContext drawingContext)
    {
        base.OnPostRender(drawingContext);
        if (!ShowGridLines)
            return;

        var rowSpacing = SanitizeSpacing(RowSpacing);
        for (var index = 1; index < RowDefinitions.Count; index++)
        {
            var y = RowDefinitions[index].Offset - rowSpacing / 2;
            drawingContext.DrawLine(s_gridLinePen, new Point(0, y), new Point(RenderSize.Width, y));
        }

        var columnSpacing = SanitizeSpacing(ColumnSpacing);
        for (var index = 1; index < ColumnDefinitions.Count; index++)
        {
            var x = ColumnDefinitions[index].Offset - columnSpacing / 2;
            drawingContext.DrawLine(s_gridLinePen, new Point(x, 0), new Point(x, RenderSize.Height));
        }
    }

    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        _sharedSizeState?.Remove(this);
        _sharedSizeState = null;
        base.OnVisualParentChanged(oldParent);
    }

    private void ApplySharedSizes(
        RowDefinition[] rowDefinitions,
        ColumnDefinition[] columnDefinitions,
        double[] rowHeights,
        double[] columnWidths)
    {
        var scopeElement = FindSharedSizeScope();
        if (scopeElement == null)
        {
            _sharedSizeState?.Remove(this);
            _sharedSizeState = null;
            return;
        }

        var state = s_sharedSizeScopes.GetValue(scopeElement, static _ => new SharedSizeScopeState());
        if (!ReferenceEquals(state, _sharedSizeState))
        {
            _sharedSizeState?.Remove(this);
            _sharedSizeState = state;
        }

        var contributions = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var index = 0; index < rowDefinitions.Length; index++)
        {
            var definition = rowDefinitions[index];
            if (string.IsNullOrWhiteSpace(definition.SharedSizeGroup))
                continue;

            var contribution = rowHeights[index];
            AddContribution(contributions, definition.SharedSizeGroup, contribution);
        }

        for (var index = 0; index < columnDefinitions.Length; index++)
        {
            var definition = columnDefinitions[index];
            if (string.IsNullOrWhiteSpace(definition.SharedSizeGroup))
                continue;

            var contribution = columnWidths[index];
            AddContribution(contributions, definition.SharedSizeGroup, contribution);
        }

        var maxima = state.Update(this, contributions);
        for (var index = 0; index < rowDefinitions.Length; index++)
        {
            var definition = rowDefinitions[index];
            if (!string.IsNullOrWhiteSpace(definition.SharedSizeGroup) &&
                maxima.TryGetValue(definition.SharedSizeGroup, out var maximum))
            {
                rowHeights[index] = maximum;
            }
        }

        for (var index = 0; index < columnDefinitions.Length; index++)
        {
            var definition = columnDefinitions[index];
            if (!string.IsNullOrWhiteSpace(definition.SharedSizeGroup) &&
                maxima.TryGetValue(definition.SharedSizeGroup, out var maximum))
            {
                columnWidths[index] = maximum;
            }
        }
    }

    private static void AddContribution(
        Dictionary<string, double> contributions,
        string key,
        double value)
    {
        if (!double.IsFinite(value) || value < 0)
            value = 0;

        if (!contributions.TryGetValue(key, out var current) || value > current)
            contributions[key] = value;
    }

    private UIElement? FindSharedSizeScope()
    {
        DependencyObject? current = this;
        var visited = new HashSet<DependencyObject>();
        while (current != null && visited.Add(current))
        {
            if (current is UIElement element && GetIsSharedSizeScope(element))
                return element;

            current = current switch
            {
                FrameworkElement frameworkElement =>
                    frameworkElement.Parent ?? frameworkElement.TemplatedParent,
                Visual visual => visual.VisualParent,
                _ => null
            };
        }

        return null;
    }

    private static void InvalidateSharedSizeDescendants(UIElement element)
    {
        if (element is Grid grid)
        {
            grid._sharedSizeState?.Remove(grid);
            grid._sharedSizeState = null;
            grid.InvalidateMeasure();
            grid.InvalidateArrange();
        }

        for (var index = 0; index < element.VisualChildrenCount; index++)
        {
            if (element.GetVisualChild(index) is UIElement child)
                InvalidateSharedSizeDescendants(child);
        }
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

    private sealed class SharedSizeScopeState
    {
        private readonly Dictionary<Grid, Dictionary<string, double>> _contributions = new();
        private readonly Dictionary<string, double> _maxima = new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, double> Update(
            Grid grid,
            Dictionary<string, double> contributions)
        {
            var affected = new HashSet<string>(contributions.Keys, StringComparer.Ordinal);
            if (_contributions.TryGetValue(grid, out var previous))
                affected.UnionWith(previous.Keys);

            _contributions[grid] = contributions;
            Recompute(affected, grid);
            return _maxima;
        }

        public void Remove(Grid grid)
        {
            if (!_contributions.Remove(grid, out var previous))
                return;

            Recompute(previous.Keys, grid);
        }

        private void Recompute(IEnumerable<string> keys, Grid changedGrid)
        {
            foreach (var key in keys.Distinct())
            {
                _maxima.TryGetValue(key, out var previousMaximum);
                var maximum = 0.0;
                var hasContribution = false;
                foreach (var gridContributions in _contributions.Values)
                {
                    if (gridContributions.TryGetValue(key, out var contribution))
                    {
                        maximum = Math.Max(maximum, contribution);
                        hasContribution = true;
                    }
                }

                if (hasContribution)
                    _maxima[key] = maximum;
                else
                    _maxima.Remove(key);

                if (Math.Abs(previousMaximum - maximum) <= 0.001)
                    continue;

                foreach (var participatingGrid in _contributions.Keys)
                {
                    if (!ReferenceEquals(participatingGrid, changedGrid))
                    {
                        participatingGrid.InvalidateMeasure();
                        participatingGrid.InvalidateArrange();
                    }
                }
            }
        }
    }

    #endregion
}
