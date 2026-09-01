using System.Runtime.CompilerServices;
using Jalium.UI;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Defines a flexible grid area that consists of columns and rows.
/// </summary>
public class Grid : Panel
{
    private const double LayoutEpsilon = 0.001;

    private static readonly ConditionalWeakTable<UIElement, SharedSizeScopeState> s_sharedSizeScopes = new();
    private static readonly Pen s_gridLinePen =
        new(new SolidColorBrush(Color.FromArgb(160, 96, 96, 96)), 1);

    private SharedSizeScopeState? _sharedSizeState;
    private RowDefinitionCollection? _rowDefinitions;
    private ColumnDefinitionCollection? _columnDefinitions;
    private RowDefinition[]? _effectiveRowDefinitions;
    private ColumnDefinition[]? _effectiveColumnDefinitions;

    // Reused track storage. The first eight fields deliberately retain their
    // historical names because layout diagnostics inspect them.
    private double[]? _rowHeights;
    private double[]? _columnWidths;
    private double[]? _rowStarValues;
    private double[]? _columnStarValues;
    private double[]? _rowContent;
    private double[]? _columnContent;
    private double[]? _rowSlots;
    private double[]? _columnSlots;
    private double[]? _rowOffsets;
    private double[]? _columnOffsets;
    private double[]? _localSharedRows;
    private double[]? _localSharedColumns;
    private double[]? _rowArrangeBase;
    private double[]? _columnArrangeBase;

    private CellLayout[]? _cells;
    private int _cellCount;
    private int _layoutVersion;
    private int _solvedVersion = -1;
    private int _solvedChildrenCount = -1;
    private Size _solvedConstraint;
    private bool _hasSolvedLayout;
    private double _correctionMeasureWidth = double.NaN;
    private double _correctionArrangeWidth = double.NaN;

    #region Attached Properties

    /// <summary>Identifies whether an element is the scope for shared row and column sizing.</summary>
    public static readonly DependencyProperty IsSharedSizeScopeProperty =
        DependencyProperty.RegisterAttached(
            "IsSharedSizeScope",
            typeof(bool),
            typeof(Grid),
            new PropertyMetadata(false, OnIsSharedSizeScopeChanged));

    /// <summary>Identifies the Row attached property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RowProperty =
        DependencyProperty.RegisterAttached(
            "Row",
            typeof(int),
            typeof(Grid),
            new PropertyMetadata(0, OnCellPropertyChanged));

    /// <summary>Identifies the Column attached property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnProperty =
        DependencyProperty.RegisterAttached(
            "Column",
            typeof(int),
            typeof(Grid),
            new PropertyMetadata(0, OnCellPropertyChanged));

    /// <summary>Identifies the RowSpan attached property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RowSpanProperty =
        DependencyProperty.RegisterAttached(
            "RowSpan",
            typeof(int),
            typeof(Grid),
            new PropertyMetadata(1, OnCellPropertyChanged));

    /// <summary>Identifies the ColumnSpan attached property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnSpanProperty =
        DependencyProperty.RegisterAttached(
            "ColumnSpan",
            typeof(int),
            typeof(Grid),
            new PropertyMetadata(1, OnCellPropertyChanged));

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

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static int GetRow(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (int)(element.GetValue(RowProperty) ?? 0);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static void SetRow(UIElement element, int value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(RowProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static int GetColumn(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (int)(element.GetValue(ColumnProperty) ?? 0);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static void SetColumn(UIElement element, int value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ColumnProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static int GetRowSpan(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (int)(element.GetValue(RowSpanProperty) ?? 1);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static void SetRowSpan(UIElement element, int value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(RowSpanProperty, Math.Max(1, value));
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static int GetColumnSpan(UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (int)(element.GetValue(ColumnSpanProperty) ?? 1);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static void SetColumnSpan(UIElement element, int value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ColumnSpanProperty, Math.Max(1, value));
    }

    private static void OnCellPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not UIElement child)
            return;

        var parent = child.VisualParent as Grid;
        if (parent is null && child is FrameworkElement frameworkElement)
            parent = frameworkElement.Parent as Grid;

        parent?.InvalidateLayoutState(definitionsChanged: false);
    }

    #endregion

    #region Dependency Properties

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RowSpacingProperty =
        DependencyProperty.Register(
            nameof(RowSpacing),
            typeof(double),
            typeof(Grid),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnSpacingProperty =
        DependencyProperty.Register(
            nameof(ColumnSpacing),
            typeof(double),
            typeof(Grid),
            new PropertyMetadata(0.0, OnLayoutPropertyChanged));

    public static readonly DependencyProperty ShowGridLinesProperty =
        DependencyProperty.Register(
            nameof(ShowGridLines),
            typeof(bool),
            typeof(Grid),
            new PropertyMetadata(false, OnShowGridLinesChanged));

    private static void OnLayoutPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is Grid grid)
            grid.InvalidateLayoutState(definitionsChanged: false);
    }

    private static void OnShowGridLinesChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args) =>
        ((Grid)dependencyObject).InvalidateVisual();

    private static void OnIsSharedSizeScopeChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is UIElement element)
            InvalidateSharedSizeDescendants(element);
    }

    #endregion

    #region Properties

    public RowDefinitionCollection RowDefinitions
    {
        get => _rowDefinitions ??= new RowDefinitionCollection(this);
        set
        {
            if (value?.Owner is not null)
            {
                if (ReferenceEquals(value.Owner, this))
                    return;

                throw new ArgumentException(
                    "The collection already belongs to another Grid.",
                    nameof(value));
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

    public ColumnDefinitionCollection ColumnDefinitions
    {
        get => _columnDefinitions ??= new ColumnDefinitionCollection(this);
        set
        {
            if (value?.Owner is not null)
            {
                if (ReferenceEquals(value.Owner, this))
                    return;

                throw new ArgumentException(
                    "The collection already belongs to another Grid.",
                    nameof(value));
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

    public bool ShowGridLines
    {
        get => (bool)(GetValue(ShowGridLinesProperty) ?? false);
        set => SetValue(ShowGridLinesProperty, value);
    }

    public bool ShouldSerializeColumnDefinitions() =>
        _columnDefinitions is { Count: > 0 };

    public bool ShouldSerializeRowDefinitions() =>
        _rowDefinitions is { Count: > 0 };

    internal void OnDefinitionChanged() =>
        InvalidateLayoutState(definitionsChanged: true);

    private void InvalidateLayoutState(bool definitionsChanged)
    {
        if (definitionsChanged)
        {
            _effectiveRowDefinitions = null;
            _effectiveColumnDefinitions = null;
            _sharedSizeState?.Remove(this);
            _sharedSizeState = null;
        }

        _layoutVersion++;
        _hasSolvedLayout = false;
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }

    #endregion

    #region Layout

    /// <summary>
    /// Solves both axes as one transaction. Width is resolved before content
    /// height so wrapping controls always see their committed cell width.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var desired = SolveLayout(NormalizeConstraint(availableSize), calculateDesiredSize: true);
        MeasureCssAbsoluteChildren(availableSize);
        return desired;
    }

    /// <summary>
    /// Reuses the intrinsic measurements produced by MeasureOverride and only
    /// redistributes tracks for the committed arrange size. Arrange must never
    /// measure the subtree: virtualizing panels and ScrollViewer publish their
    /// extent during measure, and overwriting it halfway through arrange corrupts
    /// the viewport and can clamp an active scroll offset back to zero.
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        finalSize = NormalizeConstraint(finalSize);

        var structureChanged =
            !_hasSolvedLayout ||
            _solvedVersion != _layoutVersion ||
            _solvedChildrenCount != Children.Count;
        var widthChanged =
            !AreClose(_solvedConstraint.Width, finalSize.Width);
        var heightChanged =
            !AreClose(_solvedConstraint.Height, finalSize.Height);

        if (structureChanged)
        {
            // A derived Grid can change definitions or cell coordinates while
            // entering ArrangeOverride. Keep the old transaction untouched and
            // let LayoutManager run the queued measure before rendering.
            InvalidateMeasure();
            return finalSize;
        }

        var measuredGridWidth = _solvedConstraint.Width;
        var requestWidthCorrection =
            widthChanged &&
            (!AreClose(
                 measuredGridWidth,
                 _correctionMeasureWidth) ||
             !AreClose(
                 finalSize.Width,
                 _correctionArrangeWidth));

        if (widthChanged || heightChanged)
        {
            ResolveArrangeTracks(finalSize);
        }

        var rowSlots = _rowSlots!;
        var columnSlots = _columnSlots!;
        var rowOffsets = _rowOffsets!;
        var columnOffsets = _columnOffsets!;
        var rowSpacing = SanitizeSpacing(RowSpacing);
        var columnSpacing = SanitizeSpacing(ColumnSpacing);
        var queuedWidthCorrection = false;

        for (var index = 0; index < _cellCount; index++)
        {
            ref readonly var cell = ref _cells![index];
            var rect = new Rect(
                columnOffsets[cell.Column],
                rowOffsets[cell.Row],
                GetTrackSpanSize(
                    columnSlots,
                    cell.Column,
                    cell.ColumnSpan,
                    columnSpacing),
                GetTrackSpanSize(
                    rowSlots,
                    cell.Row,
                    cell.RowSpan,
                    rowSpacing));

            cell.Element.Arrange(rect);

            if (requestWidthCorrection &&
                cell.Element.Visibility != Visibility.Collapsed &&
                !AreClose(cell.MeasuredWidth, rect.Width))
            {
                queuedWidthCorrection = true;
            }
        }

        if (queuedWidthCorrection)
        {
            _correctionMeasureWidth = measuredGridWidth;
            _correctionArrangeWidth = finalSize.Width;

            // Invalidate this Grid, rather than only the resized child. That
            // guarantees LayoutManager propagates the correction through the
            // ancestors which supply the new available width. The next normal
            // measure transaction then reaches the entire cell subtree with
            // coherent constraints before rendering. Never call Measure here:
            // doing so would overwrite ScrollViewer/IScrollInfo extent state
            // halfway through arrange.
            InvalidateMeasure();
        }

        ArrangeCssAbsoluteChildren(finalSize);

        return finalSize;
    }

    private void ResolveArrangeTracks(Size finalSize)
    {
        var rowCount = Math.Max(1, _rowDefinitions?.Count ?? 0);
        var columnCount =
            Math.Max(1, _columnDefinitions?.Count ?? 0);
        var rows = GetEffectiveRowDefinitions(rowCount);
        var columns =
            GetEffectiveColumnDefinitions(columnCount);
        var rowStars =
            GetClearedBuffer(ref _rowStarValues, rowCount);
        var rowArrangeBase =
            GetBuffer(ref _rowArrangeBase, rowCount);
        var columnStars =
            GetClearedBuffer(ref _columnStarValues, columnCount);
        var columnArrangeBase =
            GetBuffer(ref _columnArrangeBase, columnCount);
        var rowSlots = GetBuffer(ref _rowSlots, rowCount);
        var rowOffsets = GetBuffer(ref _rowOffsets, rowCount);
        var columnSlots =
            GetBuffer(ref _columnSlots, columnCount);
        var columnOffsets =
            GetBuffer(ref _columnOffsets, columnCount);

        for (var index = 0; index < rowCount; index++)
        {
            if (IsFlexible(rows[index], unbounded: false))
            {
                // Unbounded measure treated this star row as content-sized.
                // Finite arrange must start again at its minimum, including
                // the valid 0* case, before redistributing viewport height.
                rowArrangeBase[index] = GetMin(rows[index]);
                rowStars[index] =
                    GetStarWeight(rows[index].Height);
            }
            else
            {
                rowArrangeBase[index] = _rowHeights![index];
            }
        }

        for (var index = 0; index < columnCount; index++)
        {
            if (IsFlexible(columns[index], unbounded: false))
            {
                columnArrangeBase[index] =
                    GetMin(columns[index]);
                columnStars[index] =
                    GetStarWeight(columns[index].Width);
            }
            else
            {
                columnArrangeBase[index] =
                    _columnWidths![index];
            }
        }

        ResolveColumns(
            columns,
            columnArrangeBase,
            columnStars,
            finalSize.Width,
            SanitizeSpacing(ColumnSpacing),
            columnSlots);
        ResolveRows(
            rows,
            rowArrangeBase,
            rowStars,
            finalSize.Height,
            SanitizeSpacing(RowSpacing),
            rowSlots);
        ComputeOffsets(
            rows,
            columns,
            rowSlots,
            columnSlots,
            rowOffsets,
            columnOffsets,
            SanitizeSpacing(RowSpacing),
            SanitizeSpacing(ColumnSpacing));

        _solvedConstraint = finalSize;
        _solvedVersion = _layoutVersion;
        _solvedChildrenCount = Children.Count;
        _hasSolvedLayout = true;
    }

    private Size SolveLayout(Size constraint, bool calculateDesiredSize)
    {
        if (!AreClose(constraint.Width, _correctionMeasureWidth))
        {
            _correctionMeasureWidth = double.NaN;
            _correctionArrangeWidth = double.NaN;
        }

        var explicitRows = _rowDefinitions;
        var explicitColumns = _columnDefinitions;
        var rowCount = Math.Max(1, explicitRows?.Count ?? 0);
        var columnCount = Math.Max(1, explicitColumns?.Count ?? 0);

        EnsureDefinitionOwners(explicitRows, explicitColumns);

        var rows = GetEffectiveRowDefinitions(rowCount);
        var columns = GetEffectiveColumnDefinitions(columnCount);
        BuildCells(rowCount, columnCount);

        var rowHeights = GetClearedBuffer(ref _rowHeights, rowCount);
        var columnWidths = GetClearedBuffer(ref _columnWidths, columnCount);
        var rowStars = GetClearedBuffer(ref _rowStarValues, rowCount);
        var columnStars = GetClearedBuffer(ref _columnStarValues, columnCount);
        var rowContent = GetClearedBuffer(ref _rowContent, rowCount);
        var columnContent = GetClearedBuffer(ref _columnContent, columnCount);
        var rowSlots = GetClearedBuffer(ref _rowSlots, rowCount);
        var columnSlots = GetClearedBuffer(ref _columnSlots, columnCount);
        var rowOffsets = GetClearedBuffer(ref _rowOffsets, rowCount);
        var columnOffsets = GetClearedBuffer(ref _columnOffsets, columnCount);

        var rowSpacing = SanitizeSpacing(RowSpacing);
        var columnSpacing = SanitizeSpacing(ColumnSpacing);
        var unboundedWidth = double.IsPositiveInfinity(constraint.Width);
        var unboundedHeight = double.IsPositiveInfinity(constraint.Height);

        // Establish a provisional vertical allocation first. Intrinsic-width
        // measurement can then honor a finite fixed/star row instead of
        // needlessly offering infinity on both axes.
        InitializeRows(rows, rowHeights, rowStars, unboundedHeight);
        ResolveRows(
            rows,
            rowHeights,
            rowStars,
            constraint.Height,
            rowSpacing,
            rowSlots);

        // Phase 1: intrinsic widths for Auto and star-as-Auto columns.
        InitializeColumns(
            columns,
            columnWidths,
            columnStars,
            unboundedWidth);

        for (var index = 0; index < _cellCount; index++)
        {
            ref readonly var cell = ref _cells![index];
            if (!ShouldMeasureUnboundedColumn(
                    columns,
                    cell.Column,
                    cell.ColumnSpan,
                    unboundedWidth))
            {
                continue;
            }

            var height = ShouldMeasureUnboundedRow(
                    rows,
                    cell.Row,
                    cell.RowSpan,
                    unboundedHeight)
                ? double.PositiveInfinity
                : GetTrackSpanSize(
                    rowSlots,
                    cell.Row,
                    cell.RowSpan,
                    rowSpacing);

            cell.Element.Measure(new Size(double.PositiveInfinity, height));
            GrowMeasuredColumns(
                columns,
                columnWidths,
                cell,
                cell.Element.DesiredSize.Width,
                columnSpacing,
                unboundedWidth);
        }

        ResolveColumns(
            columns,
            columnWidths,
            columnStars,
            constraint.Width,
            columnSpacing,
            columnSlots);

        // Phase 2: Auto and star-as-Auto rows are measured at the resolved
        // horizontal cell width. This makes wrapped text deterministic.
        InitializeRows(rows, rowHeights, rowStars, unboundedHeight);
        MeasureContentRows(
            rows,
            columns,
            rowHeights,
            columnSlots,
            rowSpacing,
            columnSpacing,
            unboundedHeight);

        // Shared groups use local intrinsic contributions. The shared maximum
        // can widen a column, so rows are measured once more at that width.
        if (HasSharedSizeGroups(rows, columns))
        {
            var localRows = GetBuffer(ref _localSharedRows, rowCount);
            var localColumns = GetBuffer(
                ref _localSharedColumns,
                columnCount);
            Array.Copy(rowHeights, localRows, rowCount);
            Array.Copy(columnWidths, localColumns, columnCount);

            var sharedChanges = ApplySharedSizes(
                rows,
                columns,
                localRows,
                localColumns,
                rowHeights,
                columnWidths);

            if (sharedChanges.ColumnsChanged)
            {
                ResolveColumns(
                    columns,
                    columnWidths,
                    columnStars,
                    constraint.Width,
                    columnSpacing,
                    columnSlots);

                InitializeRows(
                    rows,
                    rowHeights,
                    rowStars,
                    unboundedHeight);
                MeasureContentRows(
                    rows,
                    columns,
                    rowHeights,
                    columnSlots,
                    rowSpacing,
                    columnSpacing,
                    unboundedHeight);
                Array.Copy(rowHeights, localRows, rowCount);

                // Keep local column contributions separate from the maximum
                // applied above, otherwise a smaller member would permanently
                // retain a removed larger member's contribution.
                Array.Copy(localColumns, columnWidths, columnCount);
                ApplySharedSizes(
                    rows,
                    columns,
                    localRows,
                    localColumns,
                    rowHeights,
                    columnWidths);
            }
        }
        else
        {
            _sharedSizeState?.Remove(this);
            _sharedSizeState = null;
        }

        ResolveColumns(
            columns,
            columnWidths,
            columnStars,
            constraint.Width,
            columnSpacing,
            columnSlots);
        ResolveRows(
            rows,
            rowHeights,
            rowStars,
            constraint.Height,
            rowSpacing,
            rowSlots);

        InitializeDesiredColumns(
            columns,
            columnWidths,
            columnContent,
            unboundedWidth);
        InitializeDesiredRows(
            rows,
            rowHeights,
            rowContent,
            unboundedHeight);

        // Phase 3: every child finishes with the exact constraint represented
        // by the committed track snapshot. Repeated equal constraints are
        // short-circuited by UIElement.Measure.
        for (var index = 0; index < _cellCount; index++)
        {
            ref var cell = ref _cells![index];
            var width = GetTrackSpanSize(
                columnSlots,
                cell.Column,
                cell.ColumnSpan,
                columnSpacing);
            var height = ShouldMeasureUnboundedRow(
                    rows,
                    cell.Row,
                    cell.RowSpan,
                    unboundedHeight)
                ? double.PositiveInfinity
                : GetTrackSpanSize(
                    rowSlots,
                    cell.Row,
                    cell.RowSpan,
                    rowSpacing);

            cell.Element.Measure(new Size(width, height));
            cell.MeasuredWidth = width;

            GrowDesiredColumns(
                columns,
                columnContent,
                cell,
                cell.Element.DesiredSize.Width,
                columnSpacing);
            GrowDesiredRows(
                rows,
                rowContent,
                cell,
                cell.Element.DesiredSize.Height,
                rowSpacing);
        }

        ComputeOffsets(
            rows,
            columns,
            rowSlots,
            columnSlots,
            rowOffsets,
            columnOffsets,
            rowSpacing,
            columnSpacing);

        _solvedConstraint = constraint;
        _solvedVersion = _layoutVersion;
        _solvedChildrenCount = Children.Count;
        _hasSolvedLayout = true;

        if (!calculateDesiredSize)
            return constraint;

        return new Size(
            SumDesiredColumns(
                columns,
                columnWidths,
                columnContent,
                columnSpacing,
                unboundedWidth),
            SumDesiredRows(
                rows,
                rowHeights,
                rowContent,
                rowSpacing,
                unboundedHeight));
    }

    private void BuildCells(int rowCount, int columnCount)
    {
        var childCount = Children.Count;
        if (_cells is null || _cells.Length < childCount)
            _cells = new CellLayout[Math.Max(4, childCount)];

        _cellCount = 0;
        foreach (var child in Children.EnumerateStruct())
        {
            // position:absolute children are out of flow: no cell, no track contribution.
            if (IsCssAbsolute(child))
                continue;

            var row = Math.Clamp(GetRow(child), 0, rowCount - 1);
            var column = Math.Clamp(
                GetColumn(child),
                0,
                columnCount - 1);
            var rowSpan = Math.Clamp(
                GetRowSpan(child),
                1,
                rowCount - row);
            var columnSpan = Math.Clamp(
                GetColumnSpan(child),
                1,
                columnCount - column);

            _cells[_cellCount++] = new CellLayout(
                child,
                row,
                column,
                rowSpan,
                columnSpan);
        }
    }

    private void MeasureContentRows(
        RowDefinition[] rows,
        ColumnDefinition[] columns,
        double[] rowHeights,
        double[] columnSlots,
        double rowSpacing,
        double columnSpacing,
        bool unboundedHeight)
    {
        for (var index = 0; index < _cellCount; index++)
        {
            ref readonly var cell = ref _cells![index];
            if (!ShouldMeasureUnboundedRow(
                    rows,
                    cell.Row,
                    cell.RowSpan,
                    unboundedHeight))
            {
                continue;
            }

            var width = GetTrackSpanSize(
                columnSlots,
                cell.Column,
                cell.ColumnSpan,
                columnSpacing);
            cell.Element.Measure(
                new Size(width, double.PositiveInfinity));

            GrowMeasuredRows(
                rows,
                rowHeights,
                cell,
                cell.Element.DesiredSize.Height,
                rowSpacing,
                unboundedHeight);
        }
    }

    private static void InitializeRows(
        RowDefinition[] definitions,
        double[] sizes,
        double[] starValues,
        bool unbounded)
    {
        Array.Clear(sizes);
        Array.Clear(starValues);

        for (var index = 0; index < definitions.Length; index++)
        {
            var definition = definitions[index];
            var min = GetMin(definition);
            var max = GetMax(definition, min);

            if (definition.Height.IsAbsolute)
            {
                sizes[index] = ClampTrack(
                    definition.Height.Value,
                    min,
                    max);
            }
            else
            {
                sizes[index] = min;
                if (IsFlexible(definition, unbounded))
                    starValues[index] = GetStarWeight(
                        definition.Height);
            }
        }
    }

    private static void InitializeColumns(
        ColumnDefinition[] definitions,
        double[] sizes,
        double[] starValues,
        bool unbounded)
    {
        Array.Clear(sizes);
        Array.Clear(starValues);

        for (var index = 0; index < definitions.Length; index++)
        {
            var definition = definitions[index];
            var min = GetMin(definition);
            var max = GetMax(definition, min);

            if (definition.Width.IsAbsolute)
            {
                sizes[index] = ClampTrack(
                    definition.Width.Value,
                    min,
                    max);
            }
            else
            {
                sizes[index] = min;
                if (IsFlexible(definition, unbounded))
                    starValues[index] = GetStarWeight(
                        definition.Width);
            }
        }
    }

    private static void InitializeDesiredRows(
        RowDefinition[] definitions,
        double[] measured,
        double[] desired,
        bool unbounded)
    {
        for (var index = 0; index < definitions.Length; index++)
        {
            desired[index] =
                definitions[index].Height.IsAbsolute ||
                IsContentSized(definitions[index], unbounded)
                    ? measured[index]
                    : GetMin(definitions[index]);
        }
    }

    private static void InitializeDesiredColumns(
        ColumnDefinition[] definitions,
        double[] measured,
        double[] desired,
        bool unbounded)
    {
        for (var index = 0; index < definitions.Length; index++)
        {
            desired[index] =
                definitions[index].Width.IsAbsolute ||
                IsContentSized(definitions[index], unbounded)
                    ? measured[index]
                    : GetMin(definitions[index]);
        }
    }

    private static void ResolveRows(
        RowDefinition[] definitions,
        double[] measured,
        double[] starValues,
        double available,
        double spacing,
        double[] result)
    {
        ResolveTracks(
            definitions,
            columns: null,
            measured,
            starValues,
            available,
            spacing,
            result);
    }

    private static void ResolveColumns(
        ColumnDefinition[] definitions,
        double[] measured,
        double[] starValues,
        double available,
        double spacing,
        double[] result)
    {
        ResolveTracks(
            rows: null,
            definitions,
            measured,
            starValues,
            available,
            spacing,
            result);
    }

    /// <summary>
    /// Resolves weighted tracks with iterative min/max redistribution. A track
    /// that hits a bound is removed before the remaining space is divided,
    /// avoiding the last-track correction and threshold drift of the old Grid.
    /// </summary>
    private static void ResolveTracks(
        RowDefinition[]? rows,
        ColumnDefinition[]? columns,
        double[] measured,
        double[] starValues,
        double available,
        double spacing,
        double[] result)
    {
        var count = rows?.Length ?? columns!.Length;
        Array.Copy(measured, result, count);

        if (double.IsPositiveInfinity(available))
            return;

        var spacingTotal = Math.Max(0, count - 1) * spacing;
        var flexibleTarget = Math.Max(0, available - spacingTotal);
        var totalWeight = 0.0;

        for (var index = 0; index < count; index++)
        {
            if (starValues[index] > 0)
            {
                result[index] = double.NaN;
                totalWeight += starValues[index];
            }
            else
            {
                flexibleTarget -= result[index];
            }
        }

        if (totalWeight <= 0)
            return;

        // Resolve one or more bounded tracks per iteration. Track counts are
        // normally tiny, and this keeps the hot path allocation-free.
        while (totalWeight > 0)
        {
            var changed = false;
            var distributable = Math.Max(0, flexibleTarget);

            for (var index = 0; index < count; index++)
            {
                if (!double.IsNaN(result[index]))
                    continue;

                var candidate =
                    distributable * starValues[index] / totalWeight;
                var min = GetTrackMin(rows, columns, index);
                var max = GetTrackMax(
                    rows,
                    columns,
                    index,
                    min);

                if (candidate < min - LayoutEpsilon)
                {
                    result[index] = min;
                }
                else if (candidate > max + LayoutEpsilon)
                {
                    result[index] = max;
                }
                else
                {
                    continue;
                }

                flexibleTarget -= result[index];
                totalWeight -= starValues[index];
                changed = true;
            }

            if (changed)
                continue;

            for (var index = 0; index < count; index++)
            {
                if (double.IsNaN(result[index]))
                {
                    result[index] = Math.Clamp(
                        Math.Max(0, flexibleTarget) *
                        starValues[index] /
                        totalWeight,
                        GetTrackMin(rows, columns, index),
                        GetTrackMax(
                            rows,
                            columns,
                            index,
                            GetTrackMin(rows, columns, index)));
                }
            }

            break;
        }
    }

    private static void GrowMeasuredRows(
        RowDefinition[] definitions,
        double[] sizes,
        in CellLayout cell,
        double desired,
        double spacing,
        bool unbounded)
    {
        GrowTracks(
            definitions,
            columns: null,
            sizes,
            cell.Row,
            cell.RowSpan,
            desired,
            spacing,
            TrackGrowthKind.ContentSized,
            useStarWeight: false,
            unbounded);
    }

    private static void GrowMeasuredColumns(
        ColumnDefinition[] definitions,
        double[] sizes,
        in CellLayout cell,
        double desired,
        double spacing,
        bool unbounded)
    {
        GrowTracks(
            rows: null,
            definitions,
            sizes,
            cell.Column,
            cell.ColumnSpan,
            desired,
            spacing,
            TrackGrowthKind.ContentSized,
            useStarWeight: false,
            unbounded);
    }

    private static void GrowDesiredRows(
        RowDefinition[] definitions,
        double[] sizes,
        in CellLayout cell,
        double desired,
        double spacing)
    {
        GrowDesiredTracks(
            definitions,
            columns: null,
            sizes,
            cell.Row,
            cell.RowSpan,
            desired,
            spacing);
    }

    private static void GrowDesiredColumns(
        ColumnDefinition[] definitions,
        double[] sizes,
        in CellLayout cell,
        double desired,
        double spacing)
    {
        GrowDesiredTracks(
            rows: null,
            definitions,
            sizes,
            cell.Column,
            cell.ColumnSpan,
            desired,
            spacing);
    }

    private static void GrowDesiredTracks(
        RowDefinition[]? rows,
        ColumnDefinition[]? columns,
        double[] sizes,
        int start,
        int span,
        double desired,
        double spacing)
    {
        // Spanning content first grows star tracks. Auto tracks already carry
        // their intrinsic contribution, so this avoids charging the same
        // content to both Auto and Star.
        GrowTracks(
            rows,
            columns,
            sizes,
            start,
            span,
            desired,
            spacing,
            TrackGrowthKind.Star,
            useStarWeight: true,
            unbounded: false);

        GrowTracks(
            rows,
            columns,
            sizes,
            start,
            span,
            desired,
            spacing,
            TrackGrowthKind.NonAbsolute,
            useStarWeight: false,
            unbounded: false);
    }

    private static void GrowTracks(
        RowDefinition[]? rows,
        ColumnDefinition[]? columns,
        double[] sizes,
        int start,
        int span,
        double desired,
        double spacing,
        TrackGrowthKind growthKind,
        bool useStarWeight,
        bool unbounded)
    {
        desired = SanitizeDesired(desired);
        var requiredTrackSize = Math.Max(
            0,
            desired - Math.Max(0, span - 1) * spacing);
        var current = 0.0;

        for (var index = start; index < start + span; index++)
            current += sizes[index];

        var remaining = requiredTrackSize - current;
        while (remaining > LayoutEpsilon)
        {
            var totalWeight = 0.0;
            for (var index = start; index < start + span; index++)
            {
                var maximum =
                    GetTrackMax(rows, columns, index);
                if (CanGrowTrack(
                        rows,
                        columns,
                        index,
                        growthKind,
                        unbounded) &&
                    sizes[index] < maximum - LayoutEpsilon)
                {
                    totalWeight += Math.Max(
                        LayoutEpsilon,
                        GetTrackGrowthWeight(
                            rows,
                            columns,
                            index,
                            useStarWeight));
                }
            }

            if (totalWeight <= 0)
                break;

            var consumed = 0.0;
            for (var index = start; index < start + span; index++)
            {
                if (!CanGrowTrack(
                        rows,
                        columns,
                        index,
                        growthKind,
                        unbounded))
                {
                    continue;
                }

                var room =
                    GetTrackMax(rows, columns, index) -
                    sizes[index];
                if (room <= LayoutEpsilon)
                    continue;

                var weight = Math.Max(
                    LayoutEpsilon,
                    GetTrackGrowthWeight(
                        rows,
                        columns,
                        index,
                        useStarWeight));
                var increase = Math.Min(
                    room,
                    remaining * weight / totalWeight);
                sizes[index] += increase;
                consumed += increase;
            }

            if (consumed <= LayoutEpsilon)
                break;

            remaining -= consumed;
        }
    }

    private static bool CanGrowTrack(
        RowDefinition[]? rows,
        ColumnDefinition[]? columns,
        int index,
        TrackGrowthKind growthKind,
        bool unbounded)
    {
        if (growthKind == TrackGrowthKind.ContentSized)
        {
            return rows is not null
                ? IsContentSized(rows[index], unbounded)
                : IsContentSized(columns![index], unbounded);
        }

        var length = GetTrackLength(rows, columns, index);
        return growthKind == TrackGrowthKind.Star
            ? length.IsStar
            : !length.IsAbsolute;
    }

    private static double GetTrackGrowthWeight(
        RowDefinition[]? rows,
        ColumnDefinition[]? columns,
        int index,
        bool useStarWeight)
    {
        if (!useStarWeight)
            return 1;

        return Math.Max(
            1,
            GetStarWeight(GetTrackLength(rows, columns, index)));
    }

    private static GridLength GetTrackLength(
        RowDefinition[]? rows,
        ColumnDefinition[]? columns,
        int index) =>
        rows is not null
            ? rows[index].Height
            : columns![index].Width;

    private static double GetTrackMin(
        RowDefinition[]? rows,
        ColumnDefinition[]? columns,
        int index) =>
        rows is not null
            ? GetMin(rows[index])
            : GetMin(columns![index]);

    private static double GetTrackMax(
        RowDefinition[]? rows,
        ColumnDefinition[]? columns,
        int index)
    {
        var minimum = GetTrackMin(rows, columns, index);
        return GetTrackMax(
            rows,
            columns,
            index,
            minimum);
    }

    private static double GetTrackMax(
        RowDefinition[]? rows,
        ColumnDefinition[]? columns,
        int index,
        double minimum) =>
        rows is not null
            ? GetMax(rows[index], minimum)
            : GetMax(columns![index], minimum);

    /// <summary>
    /// Returns whether a row span needs an unbounded intrinsic measurement.
    /// A mixed Auto + flexible Star span is measured against its finite slot:
    /// the Star track absorbs the remaining viewport. Letting the Auto member
    /// see infinity would charge the entire descendant extent to Auto and
    /// expand a window-sized Grid to the full height of scrollable content.
    /// </summary>
    private static bool ShouldMeasureUnboundedRow(
        RowDefinition[] definitions,
        int start,
        int span,
        bool unbounded)
    {
        var containsContentSizedTrack = false;
        for (var index = start; index < start + span; index++)
        {
            if (IsContentSized(definitions[index], unbounded))
                containsContentSizedTrack = true;

            if (CanAbsorbContent(definitions[index], unbounded))
                return false;
        }

        return containsContentSizedTrack;
    }

    private static bool ShouldMeasureUnboundedColumn(
        ColumnDefinition[] definitions,
        int start,
        int span,
        bool unbounded)
    {
        var containsContentSizedTrack = false;
        for (var index = start; index < start + span; index++)
        {
            if (IsContentSized(definitions[index], unbounded))
                containsContentSizedTrack = true;

            if (CanAbsorbContent(definitions[index], unbounded))
                return false;
        }

        return containsContentSizedTrack;
    }

    private static bool CanAbsorbContent(
        RowDefinition definition,
        bool unbounded)
    {
        var minimum = GetMin(definition);
        return IsFlexible(definition, unbounded) &&
               GetStarWeight(definition.Height) > 0 &&
               GetMax(definition, minimum) >
               minimum + LayoutEpsilon;
    }

    private static bool CanAbsorbContent(
        ColumnDefinition definition,
        bool unbounded)
    {
        var minimum = GetMin(definition);
        return IsFlexible(definition, unbounded) &&
               GetStarWeight(definition.Width) > 0 &&
               GetMax(definition, minimum) >
               minimum + LayoutEpsilon;
    }

    private static bool IsContentSized(
        RowDefinition definition,
        bool unbounded) =>
        definition.Height.IsAuto ||
        (definition.Height.IsStar &&
         (unbounded ||
          !string.IsNullOrWhiteSpace(definition.SharedSizeGroup)));

    private static bool IsContentSized(
        ColumnDefinition definition,
        bool unbounded) =>
        definition.Width.IsAuto ||
        (definition.Width.IsStar &&
         (unbounded ||
          !string.IsNullOrWhiteSpace(definition.SharedSizeGroup)));

    private static bool IsFlexible(
        RowDefinition definition,
        bool unbounded) =>
        definition.Height.IsStar &&
        !unbounded &&
        string.IsNullOrWhiteSpace(definition.SharedSizeGroup);

    private static bool IsFlexible(
        ColumnDefinition definition,
        bool unbounded) =>
        definition.Width.IsStar &&
        !unbounded &&
        string.IsNullOrWhiteSpace(definition.SharedSizeGroup);

    private static double SumDesiredRows(
        RowDefinition[] definitions,
        double[] measured,
        double[] content,
        double spacing,
        bool unbounded)
    {
        var result = Math.Max(0, definitions.Length - 1) * spacing;
        for (var index = 0; index < definitions.Length; index++)
        {
            result +=
                definitions[index].Height.IsAbsolute ||
                IsContentSized(definitions[index], unbounded)
                    ? measured[index]
                    : Math.Clamp(
                        content[index],
                        GetMin(definitions[index]),
                        GetMax(
                            definitions[index],
                            GetMin(definitions[index])));
        }

        return result;
    }

    private static double SumDesiredColumns(
        ColumnDefinition[] definitions,
        double[] measured,
        double[] content,
        double spacing,
        bool unbounded)
    {
        var result = Math.Max(0, definitions.Length - 1) * spacing;
        for (var index = 0; index < definitions.Length; index++)
        {
            result +=
                definitions[index].Width.IsAbsolute ||
                IsContentSized(definitions[index], unbounded)
                    ? measured[index]
                    : Math.Clamp(
                        content[index],
                        GetMin(definitions[index]),
                        GetMax(
                            definitions[index],
                            GetMin(definitions[index])));
        }

        return result;
    }

    private static void ComputeOffsets(
        RowDefinition[] rows,
        ColumnDefinition[] columns,
        double[] rowSlots,
        double[] columnSlots,
        double[] rowOffsets,
        double[] columnOffsets,
        double rowSpacing,
        double columnSpacing)
    {
        var cursor = 0.0;
        for (var index = 0; index < rows.Length; index++)
        {
            rowOffsets[index] = cursor;
            rows[index].ActualHeight = rowSlots[index];
            rows[index].Offset = cursor;
            cursor += rowSlots[index];
            if (index < rows.Length - 1)
                cursor += rowSpacing;
        }

        cursor = 0;
        for (var index = 0; index < columns.Length; index++)
        {
            columnOffsets[index] = cursor;
            columns[index].ActualWidth = columnSlots[index];
            columns[index].Offset = cursor;
            cursor += columnSlots[index];
            if (index < columns.Length - 1)
                cursor += columnSpacing;
        }
    }

    private static double GetTrackSpanSize(
        double[] trackSizes,
        int start,
        int span,
        double spacing)
    {
        var result = Math.Max(0, span - 1) * spacing;
        for (var index = start; index < start + span; index++)
            result += trackSizes[index];

        return result;
    }

    private static double GetMin(RowDefinition definition) =>
        SanitizeMinimum(definition.MinHeight);

    private static double GetMin(ColumnDefinition definition) =>
        SanitizeMinimum(definition.MinWidth);

    private static double GetMax(
        RowDefinition definition,
        double minimum) =>
        SanitizeMaximum(definition.MaxHeight, minimum);

    private static double GetMax(
        ColumnDefinition definition,
        double minimum) =>
        SanitizeMaximum(definition.MaxWidth, minimum);

    private static double ClampTrack(
        double value,
        double minimum,
        double maximum)
    {
        if (!double.IsFinite(value))
            value = 0;

        return Math.Clamp(Math.Max(0, value), minimum, maximum);
    }

    private static double SanitizeMinimum(double value) =>
        double.IsFinite(value) && value > 0 ? value : 0;

    private static double SanitizeMaximum(
        double value,
        double minimum)
    {
        if (double.IsNaN(value) ||
            double.IsPositiveInfinity(value))
        {
            return double.PositiveInfinity;
        }

        if (double.IsNegativeInfinity(value))
            return minimum;

        return Math.Max(minimum, value);
    }

    private static double GetStarWeight(GridLength length) =>
        double.IsFinite(length.Value) && length.Value > 0
            ? length.Value
            : 0;

    private static double SanitizeSpacing(double value) =>
        double.IsFinite(value) && value > 0 ? value : 0;

    private static double SanitizeDesired(double value) =>
        double.IsFinite(value) && value > 0 ? value : 0;

    private static bool AreClose(double left, double right)
    {
        if (left == right)
            return true;

        return double.IsFinite(left) &&
               double.IsFinite(right) &&
               Math.Abs(left - right) <= LayoutEpsilon;
    }

    private static Size NormalizeConstraint(Size size) =>
        new(
            NormalizeConstraintLength(size.Width),
            NormalizeConstraintLength(size.Height));

    private static double NormalizeConstraintLength(double value)
    {
        if (double.IsPositiveInfinity(value))
            return value;

        return double.IsFinite(value) && value > 0 ? value : 0;
    }

    private static void EnsureDefinitionOwners(
        RowDefinitionCollection? rows,
        ColumnDefinitionCollection? columns)
    {
        if (rows is not null)
        {
            for (var index = 0; index < rows.Count; index++)
                rows[index].OwnerGrid = rows.Owner;
        }

        if (columns is not null)
        {
            for (var index = 0; index < columns.Count; index++)
                columns[index].OwnerGrid = columns.Owner;
        }
    }

    #endregion

    #region Rendering and shared sizing

    protected override void OnPostRender(DrawingContext drawingContext)
    {
        base.OnPostRender(drawingContext);
        if (!ShowGridLines)
            return;

        var rowSpacing = SanitizeSpacing(RowSpacing);
        var rowCount = _rowDefinitions?.Count ?? 0;
        for (var index = 1; index < rowCount; index++)
        {
            var y = _rowDefinitions![index].Offset - rowSpacing / 2;
            drawingContext.DrawLine(
                s_gridLinePen,
                new Point(0, y),
                new Point(RenderSize.Width, y));
        }

        var columnSpacing = SanitizeSpacing(ColumnSpacing);
        var columnCount = _columnDefinitions?.Count ?? 0;
        for (var index = 1; index < columnCount; index++)
        {
            var x =
                _columnDefinitions![index].Offset -
                columnSpacing / 2;
            drawingContext.DrawLine(
                s_gridLinePen,
                new Point(x, 0),
                new Point(x, RenderSize.Height));
        }
    }

    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        _sharedSizeState?.Remove(this);
        _sharedSizeState = null;
        base.OnVisualParentChanged(oldParent);
    }

    private SharedSizeChanges ApplySharedSizes(
        RowDefinition[] rowDefinitions,
        ColumnDefinition[] columnDefinitions,
        double[] localRows,
        double[] localColumns,
        double[] rowHeights,
        double[] columnWidths)
    {
        Array.Copy(localRows, rowHeights, rowDefinitions.Length);
        Array.Copy(
            localColumns,
            columnWidths,
            columnDefinitions.Length);

        var scopeElement = FindSharedSizeScope();
        if (scopeElement is null)
        {
            _sharedSizeState?.Remove(this);
            _sharedSizeState = null;
            return default;
        }

        var state = s_sharedSizeScopes.GetValue(
            scopeElement,
            static _ => new SharedSizeScopeState());
        if (!ReferenceEquals(state, _sharedSizeState))
        {
            _sharedSizeState?.Remove(this);
            _sharedSizeState = state;
        }

        var contributions =
            new Dictionary<string, double>(StringComparer.Ordinal);

        for (var index = 0;
             index < rowDefinitions.Length;
             index++)
        {
            var group = rowDefinitions[index].SharedSizeGroup;
            if (!string.IsNullOrWhiteSpace(group))
                AddContribution(
                    contributions,
                    group,
                    localRows[index]);
        }

        for (var index = 0;
             index < columnDefinitions.Length;
             index++)
        {
            var group =
                columnDefinitions[index].SharedSizeGroup;
            if (!string.IsNullOrWhiteSpace(group))
                AddContribution(
                    contributions,
                    group,
                    localColumns[index]);
        }

        var maxima = state.Update(this, contributions);
        var rowsChanged = false;
        var columnsChanged = false;

        for (var index = 0;
             index < rowDefinitions.Length;
             index++)
        {
            var group = rowDefinitions[index].SharedSizeGroup;
            if (!string.IsNullOrWhiteSpace(group) &&
                maxima.TryGetValue(group, out var maximum))
            {
                rowsChanged |=
                    Math.Abs(rowHeights[index] - maximum) >
                    LayoutEpsilon;
                rowHeights[index] = maximum;
            }
        }

        for (var index = 0;
             index < columnDefinitions.Length;
             index++)
        {
            var group =
                columnDefinitions[index].SharedSizeGroup;
            if (!string.IsNullOrWhiteSpace(group) &&
                maxima.TryGetValue(group, out var maximum))
            {
                columnsChanged |=
                    Math.Abs(columnWidths[index] - maximum) >
                    LayoutEpsilon;
                columnWidths[index] = maximum;
            }
        }

        return new SharedSizeChanges(
            rowsChanged,
            columnsChanged);
    }

    private static bool HasSharedSizeGroups(
        RowDefinition[] rowDefinitions,
        ColumnDefinition[] columnDefinitions)
    {
        for (var index = 0;
             index < rowDefinitions.Length;
             index++)
        {
            if (!string.IsNullOrWhiteSpace(
                    rowDefinitions[index].SharedSizeGroup))
            {
                return true;
            }
        }

        for (var index = 0;
             index < columnDefinitions.Length;
             index++)
        {
            if (!string.IsNullOrWhiteSpace(
                    columnDefinitions[index].SharedSizeGroup))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddContribution(
        Dictionary<string, double> contributions,
        string key,
        double value)
    {
        value = SanitizeDesired(value);
        if (!contributions.TryGetValue(key, out var current) ||
            value > current)
        {
            contributions[key] = value;
        }
    }

    private UIElement? FindSharedSizeScope()
    {
        DependencyObject? current = this;
        const int MaxAncestorDepth = 1024;

        for (var depth = 0;
             current is not null && depth < MaxAncestorDepth;
             depth++)
        {
            if (current is UIElement element &&
                GetIsSharedSizeScope(element))
            {
                return element;
            }

            current = current switch
            {
                FrameworkElement frameworkElement =>
                    frameworkElement.Parent ??
                    frameworkElement.TemplatedParent,
                Visual visual => visual.VisualParent,
                _ => null,
            };
        }

        return null;
    }

    private static void InvalidateSharedSizeDescendants(
        UIElement element)
    {
        if (element is Grid grid)
        {
            grid._sharedSizeState?.Remove(grid);
            grid._sharedSizeState = null;
            grid.InvalidateLayoutState(
                definitionsChanged: false);
        }

        for (var index = 0;
             index < element.VisualChildrenCount;
             index++)
        {
            if (element.GetVisualChild(index) is UIElement child)
                InvalidateSharedSizeDescendants(child);
        }
    }

    private RowDefinition[] GetEffectiveRowDefinitions(int count)
    {
        if (_effectiveRowDefinitions is { } cached &&
            cached.Length == count)
        {
            return cached;
        }

        var definitions = new RowDefinition[count];
        var explicitCount = _rowDefinitions?.Count ?? 0;
        for (var index = 0; index < count; index++)
        {
            definitions[index] = index < explicitCount
                ? _rowDefinitions![index]
                : new RowDefinition();
        }

        return _effectiveRowDefinitions = definitions;
    }

    private ColumnDefinition[] GetEffectiveColumnDefinitions(int count)
    {
        if (_effectiveColumnDefinitions is { } cached &&
            cached.Length == count)
        {
            return cached;
        }

        var definitions = new ColumnDefinition[count];
        var explicitCount = _columnDefinitions?.Count ?? 0;
        for (var index = 0; index < count; index++)
        {
            definitions[index] = index < explicitCount
                ? _columnDefinitions![index]
                : new ColumnDefinition();
        }

        return _effectiveColumnDefinitions = definitions;
    }

    private static double[] GetClearedBuffer(
        ref double[]? buffer,
        int count)
    {
        var result = GetBuffer(ref buffer, count);
        Array.Clear(result);
        return result;
    }

    private static double[] GetBuffer(
        ref double[]? buffer,
        int count)
    {
        if (buffer is null || buffer.Length != count)
            buffer = new double[count];

        return buffer;
    }

    private enum TrackGrowthKind
    {
        ContentSized,
        Star,
        NonAbsolute
    }

    private struct CellLayout
    {
        public CellLayout(
            UIElement element,
            int row,
            int column,
            int rowSpan,
            int columnSpan)
        {
            Element = element;
            Row = row;
            Column = column;
            RowSpan = rowSpan;
            ColumnSpan = columnSpan;
            MeasuredWidth = double.NaN;
        }

        public UIElement Element { get; }
        public int Row { get; }
        public int Column { get; }
        public int RowSpan { get; }
        public int ColumnSpan { get; }
        public double MeasuredWidth { get; set; }
    }

    private readonly record struct SharedSizeChanges(
        bool RowsChanged,
        bool ColumnsChanged);

    private sealed class SharedSizeScopeState
    {
        private readonly Dictionary<
            Grid,
            Dictionary<string, double>> _contributions = new();
        private readonly Dictionary<string, double> _maxima =
            new(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, double> Update(
            Grid grid,
            Dictionary<string, double> contributions)
        {
            var affected = new HashSet<string>(
                contributions.Keys,
                StringComparer.Ordinal);
            if (_contributions.TryGetValue(
                    grid,
                    out var previous))
            {
                affected.UnionWith(previous.Keys);
            }

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

        private void Recompute(
            IEnumerable<string> keys,
            Grid changedGrid)
        {
            foreach (var key in keys.Distinct())
            {
                _maxima.TryGetValue(
                    key,
                    out var previousMaximum);
                var maximum = 0.0;
                var hasContribution = false;

                foreach (var gridContributions
                         in _contributions.Values)
                {
                    if (gridContributions.TryGetValue(
                            key,
                            out var contribution))
                    {
                        maximum = Math.Max(
                            maximum,
                            contribution);
                        hasContribution = true;
                    }
                }

                if (hasContribution)
                    _maxima[key] = maximum;
                else
                    _maxima.Remove(key);

                if (Math.Abs(previousMaximum - maximum) <=
                    LayoutEpsilon)
                {
                    continue;
                }

                foreach (var participatingGrid
                         in _contributions.Keys)
                {
                    if (!ReferenceEquals(
                            participatingGrid,
                            changedGrid))
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
