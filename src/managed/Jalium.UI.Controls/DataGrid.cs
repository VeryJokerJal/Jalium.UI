using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that displays data in a customizable grid using TemplatedControl pattern.
/// </summary>
public class DataGrid : Control
{
    #region Dependency Properties

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(DataGrid),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(DataGrid),
            new PropertyMetadata(null, OnSelectedItemChanged));

    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(DataGrid),
            new PropertyMetadata(-1, OnSelectedIndexChanged));

    public static readonly DependencyProperty AutoGenerateColumnsProperty =
        DependencyProperty.Register(nameof(AutoGenerateColumns), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(true, OnAutoGenerateColumnsChanged));

    public static readonly DependencyProperty CanUserSortColumnsProperty =
        DependencyProperty.Register(nameof(CanUserSortColumns), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(true));

    public static readonly DependencyProperty CanUserResizeColumnsProperty =
        DependencyProperty.Register(nameof(CanUserResizeColumns), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(true));

    public static readonly DependencyProperty CanUserReorderColumnsProperty =
        DependencyProperty.Register(nameof(CanUserReorderColumns), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(true));

    public static readonly DependencyProperty EnableRowVirtualizationProperty =
        DependencyProperty.Register(nameof(EnableRowVirtualization), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(true, OnVirtualizationSettingsChanged));

    public static readonly DependencyProperty EnableColumnVirtualizationProperty =
        DependencyProperty.Register(nameof(EnableColumnVirtualization), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(false, OnVirtualizationSettingsChanged));

    public static readonly DependencyProperty SelectionModeProperty =
        DependencyProperty.Register(nameof(SelectionMode), typeof(DataGridSelectionMode), typeof(DataGrid),
            new PropertyMetadata(DataGridSelectionMode.Extended, OnSelectionModeChanged));

    public static readonly DependencyProperty SelectionUnitProperty =
        DependencyProperty.Register(nameof(SelectionUnit), typeof(DataGridSelectionUnit), typeof(DataGrid),
            new PropertyMetadata(DataGridSelectionUnit.FullRow, OnSelectionUnitChanged));

    public static readonly DependencyProperty GridLinesVisibilityProperty =
        DependencyProperty.Register(nameof(GridLinesVisibility), typeof(DataGridGridLinesVisibility), typeof(DataGrid),
            new PropertyMetadata(DataGridGridLinesVisibility.All));

    public static readonly DependencyProperty HeadersVisibilityProperty =
        DependencyProperty.Register(nameof(HeadersVisibility), typeof(DataGridHeadersVisibility), typeof(DataGrid),
            new PropertyMetadata(DataGridHeadersVisibility.All, OnHeadersVisibilityChanged));

    public static readonly DependencyProperty RowHeightProperty =
        DependencyProperty.Register(nameof(RowHeight), typeof(double), typeof(DataGrid),
            new PropertyMetadata(double.NaN, OnRowHeightChanged));

    public static readonly DependencyProperty ColumnHeaderHeightProperty =
        DependencyProperty.Register(nameof(ColumnHeaderHeight), typeof(double), typeof(DataGrid),
            new PropertyMetadata(double.NaN, OnColumnHeaderHeightChanged));

    public static readonly DependencyProperty RowHeaderWidthProperty =
        DependencyProperty.Register(nameof(RowHeaderWidth), typeof(double), typeof(DataGrid),
            new PropertyMetadata(double.NaN, OnRowHeaderWidthChanged));

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(false));

    public static readonly DependencyProperty RowDetailsTemplateProperty =
        DependencyProperty.Register(nameof(RowDetailsTemplate), typeof(DataTemplate), typeof(DataGrid),
            new PropertyMetadata(null));

    public static readonly DependencyProperty RowDetailsVisibilityModeProperty =
        DependencyProperty.Register(nameof(RowDetailsVisibilityMode), typeof(DataGridRowDetailsVisibilityMode), typeof(DataGrid),
            new PropertyMetadata(DataGridRowDetailsVisibilityMode.VisibleWhenSelected));

    public static readonly DependencyProperty AlternatingRowBackgroundProperty =
        DependencyProperty.Register(nameof(AlternatingRowBackground), typeof(Brush), typeof(DataGrid),
            new PropertyMetadata(null));

    public static readonly DependencyProperty RowBackgroundProperty =
        DependencyProperty.Register(nameof(RowBackground), typeof(Brush), typeof(DataGrid),
            new PropertyMetadata(null));

    public static readonly DependencyProperty HorizontalGridLinesBrushProperty =
        DependencyProperty.Register(nameof(HorizontalGridLinesBrush), typeof(Brush), typeof(DataGrid),
            new PropertyMetadata(null));

    public static readonly DependencyProperty VerticalGridLinesBrushProperty =
        DependencyProperty.Register(nameof(VerticalGridLinesBrush), typeof(Brush), typeof(DataGrid),
            new PropertyMetadata(null));

    #endregion

    #region Routed Events

    public static readonly RoutedEvent SelectionChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SelectionChanged), RoutingStrategy.Bubble,
            typeof(EventHandler<SelectionChangedEventArgs>), typeof(DataGrid));

    public static readonly RoutedEvent SortingEvent =
        EventManager.RegisterRoutedEvent(nameof(Sorting), RoutingStrategy.Bubble,
            typeof(EventHandler<DataGridSortingEventArgs>), typeof(DataGrid));

    public static readonly RoutedEvent BeginningEditEvent =
        EventManager.RegisterRoutedEvent(nameof(BeginningEdit), RoutingStrategy.Bubble,
            typeof(EventHandler<DataGridBeginningEditEventArgs>), typeof(DataGrid));

    public static readonly RoutedEvent CellEditEndingEvent =
        EventManager.RegisterRoutedEvent(nameof(CellEditEnding), RoutingStrategy.Bubble,
            typeof(EventHandler<DataGridCellEditEndingEventArgs>), typeof(DataGrid));

    public static readonly RoutedEvent PreparingCellForEditEvent =
        EventManager.RegisterRoutedEvent(nameof(PreparingCellForEdit), RoutingStrategy.Bubble,
            typeof(EventHandler<DataGridPreparingCellForEditEventArgs>), typeof(DataGrid));

    public event EventHandler<SelectionChangedEventArgs> SelectionChanged
    {
        add => AddHandler(SelectionChangedEvent, value);
        remove => RemoveHandler(SelectionChangedEvent, value);
    }

    public event EventHandler<DataGridSortingEventArgs> Sorting
    {
        add => AddHandler(SortingEvent, value);
        remove => RemoveHandler(SortingEvent, value);
    }

    public event EventHandler<DataGridBeginningEditEventArgs> BeginningEdit
    {
        add => AddHandler(BeginningEditEvent, value);
        remove => RemoveHandler(BeginningEditEvent, value);
    }

    public event EventHandler<DataGridCellEditEndingEventArgs> CellEditEnding
    {
        add => AddHandler(CellEditEndingEvent, value);
        remove => RemoveHandler(CellEditEndingEvent, value);
    }

    public event EventHandler<DataGridPreparingCellForEditEventArgs> PreparingCellForEdit
    {
        add => AddHandler(PreparingCellForEditEvent, value);
        remove => RemoveHandler(PreparingCellForEditEvent, value);
    }

    public event EventHandler? AutoGeneratedColumns;

    public event EventHandler<DataGridAutoGeneratingColumnEventArgs>? AutoGeneratingColumn;

    public event EventHandler<DataGridColumnEventArgs>? ColumnDisplayIndexChanged;

    public event EventHandler<DataGridColumnReorderingEventArgs>? ColumnReordering;

    public event EventHandler<DataGridColumnEventArgs>? ColumnReordered;

    #endregion

    #region CLR Properties

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty)!;
        set => SetValue(SelectedIndexProperty, value);
    }

    public bool AutoGenerateColumns
    {
        get => (bool)GetValue(AutoGenerateColumnsProperty)!;
        set => SetValue(AutoGenerateColumnsProperty, value);
    }

    public bool CanUserSortColumns
    {
        get => (bool)GetValue(CanUserSortColumnsProperty)!;
        set => SetValue(CanUserSortColumnsProperty, value);
    }

    public bool CanUserResizeColumns
    {
        get => (bool)GetValue(CanUserResizeColumnsProperty)!;
        set => SetValue(CanUserResizeColumnsProperty, value);
    }

    public bool CanUserReorderColumns
    {
        get => (bool)GetValue(CanUserReorderColumnsProperty)!;
        set => SetValue(CanUserReorderColumnsProperty, value);
    }

    public bool EnableRowVirtualization
    {
        get => (bool)GetValue(EnableRowVirtualizationProperty)!;
        set => SetValue(EnableRowVirtualizationProperty, value);
    }

    public bool EnableColumnVirtualization
    {
        get => (bool)GetValue(EnableColumnVirtualizationProperty)!;
        set => SetValue(EnableColumnVirtualizationProperty, value);
    }

    public DataGridSelectionMode SelectionMode
    {
        get => (DataGridSelectionMode)GetValue(SelectionModeProperty)!;
        set => SetValue(SelectionModeProperty, value);
    }

    public DataGridSelectionUnit SelectionUnit
    {
        get => (DataGridSelectionUnit)GetValue(SelectionUnitProperty)!;
        set => SetValue(SelectionUnitProperty, value);
    }

    public DataGridGridLinesVisibility GridLinesVisibility
    {
        get => (DataGridGridLinesVisibility)GetValue(GridLinesVisibilityProperty)!;
        set => SetValue(GridLinesVisibilityProperty, value);
    }

    public DataGridHeadersVisibility HeadersVisibility
    {
        get => (DataGridHeadersVisibility)GetValue(HeadersVisibilityProperty)!;
        set => SetValue(HeadersVisibilityProperty, value);
    }

    public double RowHeight
    {
        get => (double)GetValue(RowHeightProperty)!;
        set => SetValue(RowHeightProperty, value);
    }

    public double ColumnHeaderHeight
    {
        get => (double)GetValue(ColumnHeaderHeightProperty)!;
        set => SetValue(ColumnHeaderHeightProperty, value);
    }

    public double RowHeaderWidth
    {
        get => (double)GetValue(RowHeaderWidthProperty)!;
        set => SetValue(RowHeaderWidthProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty)!;
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>
    /// Gets or sets the template to use to display the details section of a row.
    /// </summary>
    public DataTemplate? RowDetailsTemplate
    {
        get => (DataTemplate?)GetValue(RowDetailsTemplateProperty);
        set => SetValue(RowDetailsTemplateProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates when the details section of a row is displayed.
    /// </summary>
    public DataGridRowDetailsVisibilityMode RowDetailsVisibilityMode
    {
        get => (DataGridRowDetailsVisibilityMode)(GetValue(RowDetailsVisibilityModeProperty) ?? DataGridRowDetailsVisibilityMode.VisibleWhenSelected);
        set => SetValue(RowDetailsVisibilityModeProperty, value);
    }

    public Brush? AlternatingRowBackground
    {
        get => (Brush?)GetValue(AlternatingRowBackgroundProperty);
        set => SetValue(AlternatingRowBackgroundProperty, value);
    }

    public Brush? RowBackground
    {
        get => (Brush?)GetValue(RowBackgroundProperty);
        set => SetValue(RowBackgroundProperty, value);
    }

    public Brush? HorizontalGridLinesBrush
    {
        get => (Brush?)GetValue(HorizontalGridLinesBrushProperty);
        set => SetValue(HorizontalGridLinesBrushProperty, value);
    }

    public Brush? VerticalGridLinesBrush
    {
        get => (Brush?)GetValue(VerticalGridLinesBrushProperty);
        set => SetValue(VerticalGridLinesBrushProperty, value);
    }

    public ObservableCollection<DataGridColumn> Columns { get; }

    public IList<object> SelectedItems { get; }

    #endregion

    #region Private Fields

    private const double DefaultRowHeight = 28.0;
    private const double DefaultColumnHeaderHeight = 32.0;
    private const double DefaultRowHeaderWidth = 20.0;

    private readonly List<object> _items = new();
    private readonly List<object> _selectedItems = new();
    private StackPanel? _columnHeadersHost;
    private StackPanel? _rowsHost;
    private Border? _columnHeadersBorder;
    private ScrollViewer? _dataScrollViewer;
    private readonly Dictionary<int, DataGridRow> _realizedRows = new();
    private Border? _topSpacer;
    private Border? _bottomSpacer;
    private int _realizedStartIndex = -1;
    private int _realizedEndIndex = -1;
    private int _realizedColumnStartIndex = -1;
    private int _realizedColumnEndIndex = -1;

    // Editing state
    private DataGridCell? _currentEditingCell;
    private DataGridColumn? _currentEditingColumn;
    private DataGridRow? _currentEditingRow;
    private bool _isUpdatingColumnWidthFromResize;
    private bool _isSynchronizingSelection;
    private bool _isSynchronizingColumnDisplayIndexes;

    #endregion

    #region Constructor

    public DataGrid()
    {
        Focusable = true;
        Columns = new ObservableCollection<DataGridColumn>();
        Columns.CollectionChanged += OnColumnsCollectionChanged;
        SelectedItems = _selectedItems;

        AddHandler(KeyDownEvent, new RoutedEventHandler(OnKeyDownHandler));
    }

    #endregion

    #region Template

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Unsubscribe from previous ScrollViewer
        if (_dataScrollViewer != null)
        {
            _dataScrollViewer.ScrollChanged -= OnDataScrollViewerScrollChanged;
        }

        _columnHeadersHost = GetTemplateChild("PART_ColumnHeadersHost") as StackPanel;
        _rowsHost = GetTemplateChild("PART_RowsHost") as StackPanel;
        _columnHeadersBorder = GetTemplateChild("PART_ColumnHeadersBorder") as Border;
        _dataScrollViewer = GetTemplateChild("PART_DataScrollViewer") as ScrollViewer;

        // Sync column headers with horizontal scroll
        if (_dataScrollViewer != null)
        {
            _dataScrollViewer.ScrollChanged += OnDataScrollViewerScrollChanged;
        }

        UpdateHeadersVisibility();

        RefreshColumnHeaders();
        RefreshRows();
    }

    private void OnDataScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Sync column headers horizontal position with data scroll
        if (_columnHeadersHost != null && _dataScrollViewer != null)
        {
            _columnHeadersHost.Margin = new Thickness(-_dataScrollViewer.HorizontalOffset, 0, 0, 0);
        }

        if (EnableRowVirtualization || EnableColumnVirtualization)
        {
            UpdateRealizedRows();
        }
    }

    private void OnAutoGeneratingColumn(DataGridAutoGeneratingColumnEventArgs e) =>
        AutoGeneratingColumn?.Invoke(this, e);

    private void OnAutoGeneratedColumns() =>
        AutoGeneratedColumns?.Invoke(this, EventArgs.Empty);

    private void OnColumnDisplayIndexChanged(DataGridColumn column) =>
        ColumnDisplayIndexChanged?.Invoke(this, new DataGridColumnEventArgs(column));

    private void OnColumnReordering(DataGridColumnReorderingEventArgs e) =>
        ColumnReordering?.Invoke(this, e);

    private void OnColumnReordered(DataGridColumn column) =>
        ColumnReordered?.Invoke(this, new DataGridColumnEventArgs(column));

    #endregion

    #region Column Headers

    private void UpdateHeadersVisibility()
    {
        if (_columnHeadersBorder != null)
        {
            _columnHeadersBorder.Visibility = HeadersVisibility.HasFlag(DataGridHeadersVisibility.Column)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void RefreshColumnHeaders()
    {
        if (_columnHeadersHost == null) return;

        _columnHeadersHost.Children.Clear();

        foreach (var column in Columns)
        {
            if (!IsColumnVisible(column))
            {
                continue;
            }

            var header = new DataGridColumnHeader
            {
                Content = column.Header,
                Width = column.ActualWidth,
                Height = double.IsNaN(ColumnHeaderHeight) ? double.NaN : GetEffectiveColumnHeaderHeight(),
                ParentDataGrid = this,
                Column = column
            };

            header.AddHandler(MouseDownEvent, new RoutedEventHandler(OnColumnHeaderClick));
            header.UpdateSortIndicator(column.SortDirection);

            _columnHeadersHost.Children.Add(header);
        }
    }

    private void OnColumnHeaderClick(object sender, RoutedEventArgs e)
    {
        if (!CanUserSortColumns) return;

        if (sender is DataGridColumnHeader header && header.Column != null)
        {
            SortByColumn(header.Column);
            e.Handled = true;
        }
    }

    #endregion

    #region Row Management

    private static bool IsColumnVisible(DataGridColumn column) =>
        column.Visibility == Visibility.Visible;

    private static double GetEffectiveLength(double value, double fallback) =>
        double.IsNaN(value) || double.IsInfinity(value) || value <= 0 ? fallback : value;

    private double GetEffectiveRowHeight() => GetEffectiveLength(RowHeight, DefaultRowHeight);

    private double GetEffectiveColumnHeaderHeight() => GetEffectiveLength(ColumnHeaderHeight, DefaultColumnHeaderHeight);

    private double GetEffectiveRowHeaderWidth() => GetEffectiveLength(RowHeaderWidth, DefaultRowHeaderWidth);

    private static double GetRenderableColumnWidth(DataGridColumn column) =>
        IsColumnVisible(column) ? Math.Max(1.0, column.ActualWidth) : 0.0;

    private void RefreshRows()
    {
        if (_rowsHost == null)
        {
            return;
        }

        _rowsHost.Children.Clear();
        _realizedRows.Clear();
        _topSpacer = new Border();
        _bottomSpacer = new Border();
        _realizedStartIndex = -1;
        _realizedEndIndex = -1;
        _realizedColumnStartIndex = -1;
        _realizedColumnEndIndex = -1;
        UpdateRealizedRows(forceRefresh: true);
    }

    private void UpdateRealizedRows(bool forceRefresh = false)
    {
        if (_rowsHost == null)
        {
            return;
        }

        if (_items.Count == 0)
        {
            _rowsHost.Children.Clear();
            _realizedRows.Clear();
            _realizedStartIndex = -1;
            _realizedEndIndex = -1;
            _realizedColumnStartIndex = -1;
            _realizedColumnEndIndex = -1;
            return;
        }

        var rowHeight = GetEffectiveRowHeight();
        int startIndex;
        int endIndex;

        if (EnableRowVirtualization && _dataScrollViewer != null)
        {
            var viewportHeight = _dataScrollViewer.ViewportHeight > 0
                ? _dataScrollViewer.ViewportHeight
                : _dataScrollViewer.ActualHeight;
            if (viewportHeight <= 0)
            {
                viewportHeight = 400;
            }

            var firstVisible = (int)Math.Floor(_dataScrollViewer.VerticalOffset / rowHeight);
            var visibleCount = Math.Max(1, (int)Math.Ceiling(viewportHeight / rowHeight));
            var cacheCount = Math.Max(2, visibleCount / 2);
            startIndex = Math.Max(0, firstVisible - cacheCount);
            endIndex = Math.Min(_items.Count - 1, firstVisible + visibleCount + cacheCount);
        }
        else
        {
            startIndex = 0;
            endIndex = _items.Count - 1;
        }

        var (columnStart, columnEnd) = GetVisibleColumnRange();
        var rowRangeUnchanged = startIndex == _realizedStartIndex && endIndex == _realizedEndIndex;
        var columnRangeUnchanged = columnStart == _realizedColumnStartIndex && columnEnd == _realizedColumnEndIndex;
        if (!forceRefresh && rowRangeUnchanged && columnRangeUnchanged)
        {
            UpdateRowSelectionVisuals();
            return;
        }

        var staleIndices = _realizedRows.Keys.Where(i => i < startIndex || i > endIndex).ToArray();
        foreach (var staleIndex in staleIndices)
        {
            _realizedRows.Remove(staleIndex);
        }

        for (var rowIndex = startIndex; rowIndex <= endIndex; rowIndex++)
        {
            if (!_realizedRows.TryGetValue(rowIndex, out var row) ||
                row.VisibleColumnStart != columnStart ||
                row.VisibleColumnEnd != columnEnd)
            {
                row = CreateRow(_items[rowIndex], rowIndex, columnStart, columnEnd, rowHeight);
                _realizedRows[rowIndex] = row;
            }
        }

        RebuildRowsHost(startIndex, endIndex, rowHeight);
        _realizedStartIndex = startIndex;
        _realizedEndIndex = endIndex;
        _realizedColumnStartIndex = columnStart;
        _realizedColumnEndIndex = columnEnd;
        UpdateRowSelectionVisuals();
    }

    private void RebuildRowsHost(int startIndex, int endIndex, double rowHeight)
    {
        if (_rowsHost == null)
        {
            return;
        }

        _topSpacer ??= new Border();
        _bottomSpacer ??= new Border();

        _rowsHost.Children.Clear();

        var topHeight = Math.Max(0, startIndex * rowHeight);
        _topSpacer.Height = topHeight;
        _topSpacer.Visibility = topHeight > 0 ? Visibility.Visible : Visibility.Collapsed;
        _rowsHost.Children.Add(_topSpacer);

        for (var i = startIndex; i <= endIndex; i++)
        {
            if (_realizedRows.TryGetValue(i, out var row))
            {
                _rowsHost.Children.Add(row);
            }
        }

        var bottomHeight = Math.Max(0, (_items.Count - endIndex - 1) * rowHeight);
        _bottomSpacer.Height = bottomHeight;
        _bottomSpacer.Visibility = bottomHeight > 0 ? Visibility.Visible : Visibility.Collapsed;
        _rowsHost.Children.Add(_bottomSpacer);
    }

    private (int start, int end) GetVisibleColumnRange()
    {
        if (Columns.Count == 0)
        {
            return (-1, -1);
        }

        var firstVisibleColumn = -1;
        var lastVisibleColumn = -1;
        for (var i = 0; i < Columns.Count; i++)
        {
            if (IsColumnVisible(Columns[i]))
            {
                firstVisibleColumn = firstVisibleColumn == -1 ? i : firstVisibleColumn;
                lastVisibleColumn = i;
            }
        }

        if (firstVisibleColumn == -1)
        {
            return (-1, -1);
        }

        if (!EnableColumnVirtualization || _dataScrollViewer == null)
        {
            return (firstVisibleColumn, lastVisibleColumn);
        }

        var viewportWidth = _dataScrollViewer.ViewportWidth > 0
            ? _dataScrollViewer.ViewportWidth
            : _dataScrollViewer.ActualWidth;
        if (viewportWidth <= 0)
        {
            return (firstVisibleColumn, lastVisibleColumn);
        }

        var offset = _dataScrollViewer.HorizontalOffset;
        var viewportEnd = offset + viewportWidth;
        var cumulative = 0.0;
        var start = firstVisibleColumn;
        var end = lastVisibleColumn;
        var foundStart = false;

        for (var i = 0; i < Columns.Count; i++)
        {
            var columnWidth = GetRenderableColumnWidth(Columns[i]);
            if (columnWidth <= 0)
            {
                continue;
            }

            var columnStart = cumulative;
            var columnEnd = cumulative + columnWidth;

            if (!foundStart && columnEnd >= offset)
            {
                start = Math.Max(0, i - 1);
                foundStart = true;
            }

            if (foundStart && columnStart > viewportEnd)
            {
                end = Math.Min(lastVisibleColumn, i + 1);
                break;
            }

            cumulative = columnEnd;
        }

        if (!foundStart)
        {
            start = lastVisibleColumn;
            end = lastVisibleColumn;
        }

        return (start, end);
    }

    private DataGridRow CreateRow(object item, int rowIndex, int columnStart, int columnEnd, double rowHeight)
    {
        var row = new DataGridRow
        {
            DataItem = item,
            RowIndex = rowIndex,
            Height = double.IsNaN(RowHeight) ? double.NaN : rowHeight,
            IsSelected = _selectedItems.Contains(item),
            ParentDataGrid = this,
            VisibleColumnStart = columnStart,
            VisibleColumnEnd = columnEnd
        };

        // Set alternating background
        if (rowIndex % 2 == 1 && AlternatingRowBackground != null)
        {
            row.AlternatingBackground = AlternatingRowBackground;
        }

        if (columnStart < 0 || columnEnd < 0 || Columns.Count == 0)
        {
            row.AddHandler(MouseDownEvent, new RoutedEventHandler(OnRowMouseDown));
            return row;
        }

        // Create cells for visible columns
        for (var colIndex = columnStart; colIndex <= columnEnd && colIndex < Columns.Count; colIndex++)
        {
            var column = Columns[colIndex];
            if (!IsColumnVisible(column))
            {
                continue;
            }

            var cell = new DataGridCell
            {
                Width = column.ActualWidth,
                Column = column
            };

            // Use the column's GenerateElement to create the display content
            var displayElement = column.GenerateElement(cell, item);
            cell.Content = displayElement;

            row.Cells.Add(cell);
            row.CellsByColumn[colIndex] = cell;
        }

        row.AddHandler(MouseDownEvent, new RoutedEventHandler(OnRowMouseDown));

        return row;
    }

    private void OnRowMouseDown(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (sender is DataGridRow row && e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            Focus();
            SelectRow(row.RowIndex, mouseArgs.KeyboardModifiers);
            e.Handled = true;
        }
    }

    #endregion

    #region Selection

    private void UpdateSelectionPropertiesFromSelectedItems(int preferredIndex = -1)
    {
        object? selectedItem = null;
        var selectedIndex = -1;

        if (_selectedItems.Count > 0)
        {
            if (preferredIndex >= 0 && preferredIndex < _items.Count)
            {
                var preferredItem = _items[preferredIndex];
                if (_selectedItems.Contains(preferredItem))
                {
                    selectedItem = preferredItem;
                    selectedIndex = preferredIndex;
                }
            }

            if (selectedItem == null)
            {
                selectedItem = _selectedItems[0];
                selectedIndex = _items.IndexOf(selectedItem);
            }
        }

        _isSynchronizingSelection = true;
        try
        {
            SetValue(SelectedItemProperty, selectedItem);
            SetValue(SelectedIndexProperty, selectedIndex);
        }
        finally
        {
            _isSynchronizingSelection = false;
        }
    }

    private void RaiseSelectionChangedIfNeeded(IList<object> oldSelection)
    {
        var removed = oldSelection.Where(item => !_selectedItems.Contains(item)).ToArray();
        var added = _selectedItems.Where(item => !oldSelection.Contains(item)).ToArray();
        if (removed.Length == 0 && added.Length == 0)
        {
            return;
        }

        RaiseEvent(new SelectionChangedEventArgs(SelectionChangedEvent, removed, added));
    }

    private void SelectRow(int rowIndex, ModifierKeys modifiers)
    {
        if (rowIndex < 0 || rowIndex >= _items.Count) return;

        var item = _items[rowIndex];
        var oldSelectedItems = _selectedItems.ToArray();

        if (SelectionMode == DataGridSelectionMode.Single)
        {
            _selectedItems.Clear();
            _selectedItems.Add(item);
        }
        else if (SelectionMode == DataGridSelectionMode.Extended)
        {
            if (modifiers.HasFlag(ModifierKeys.Control))
            {
                if (_selectedItems.Contains(item))
                    _selectedItems.Remove(item);
                else
                    _selectedItems.Add(item);
            }
            else if (modifiers.HasFlag(ModifierKeys.Shift) && SelectedIndex >= 0)
            {
                var start = Math.Min(SelectedIndex, rowIndex);
                var end = Math.Max(SelectedIndex, rowIndex);
                _selectedItems.Clear();
                for (var i = start; i <= end; i++)
                    _selectedItems.Add(_items[i]);
            }
            else
            {
                _selectedItems.Clear();
                _selectedItems.Add(item);
            }
        }

        UpdateSelectionPropertiesFromSelectedItems(rowIndex);

        // Update row visual states
        UpdateRowSelectionVisuals();

        RaiseSelectionChangedIfNeeded(oldSelectedItems);
    }

    private void UpdateRowSelectionVisuals()
    {
        foreach (var row in _realizedRows.Values)
        {
            row.IsSelected = row.DataItem != null && _selectedItems.Contains(row.DataItem);
        }
    }

    public void SelectAll()
    {
        if (SelectionMode != DataGridSelectionMode.Extended) return;

        var oldSelectedItems = _selectedItems.ToArray();
        _selectedItems.Clear();
        _selectedItems.AddRange(_items);
        UpdateSelectionPropertiesFromSelectedItems(preferredIndex: SelectedIndex);

        UpdateRowSelectionVisuals();
        RaiseSelectionChangedIfNeeded(oldSelectedItems);
    }

    public void UnselectAll()
    {
        var oldSelectedItems = _selectedItems.ToArray();
        _selectedItems.Clear();
        UpdateSelectionPropertiesFromSelectedItems();

        UpdateRowSelectionVisuals();
        RaiseSelectionChangedIfNeeded(oldSelectedItems);
    }

    #endregion

    #region Sorting

    private void SortByColumn(DataGridColumn column)
    {
        if (!column.CanUserSort) return;

        var sortingArgs = new DataGridSortingEventArgs(SortingEvent, column);
        RaiseEvent(sortingArgs);

        if (sortingArgs.Handled) return;

        var newDirection = column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        foreach (var col in Columns)
        {
            if (col != column)
                col.SortDirection = null;
        }

        column.SortDirection = newDirection;

        if (column is DataGridBoundColumn boundColumn && boundColumn.Binding?.Path != null)
        {
            var path = boundColumn.Binding.Path;
            _items.Sort((a, b) =>
            {
                var valueA = GetPropertyValue(a, path);
                var valueB = GetPropertyValue(b, path);
                var result = Comparer.Default.Compare(valueA, valueB);
                return newDirection == ListSortDirection.Descending ? -result : result;
            });
        }

        // Update sort indicator on column headers
        UpdateColumnHeaderSortIndicators();

        RefreshRows();
    }

    private void UpdateColumnHeaderSortIndicators()
    {
        if (_columnHeadersHost == null) return;

        for (var i = 0; i < _columnHeadersHost.Children.Count; i++)
        {
            if (_columnHeadersHost.Children[i] is DataGridColumnHeader header)
            {
                header.UpdateSortIndicator(header.Column?.SortDirection);
            }
        }
    }

    private static object? GetPropertyValue(object obj, string propertyPath)
    {
        var current = obj;
        foreach (var part in propertyPath.Split('.'))
        {
            if (current == null) return null;
            var prop = current.GetType().GetProperty(part);
            current = prop?.GetValue(current);
        }
        return current;
    }

    #endregion

    #region Auto-Generate Columns

    private void AutoGenerateColumnsFromSource()
    {
        if (!AutoGenerateColumns || _items.Count == 0) return;

        var firstItem = _items[0];
        var itemType = firstItem.GetType();
        var properties = itemType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Columns.Clear();
        foreach (var prop in properties)
        {
            if (!prop.CanRead) continue;

            var eventArgs = new DataGridAutoGeneratingColumnEventArgs(
                prop.Name,
                prop.PropertyType,
                CreateAutoGeneratedColumn(prop));

            OnAutoGeneratingColumn(eventArgs);
            if (eventArgs.Cancel || eventArgs.Column == null)
            {
                continue;
            }

            Columns.Add(eventArgs.Column);
        }

        OnAutoGeneratedColumns();
    }

    private static DataGridColumn CreateAutoGeneratedColumn(PropertyInfo property)
    {
        var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (propertyType == typeof(bool))
        {
            return new DataGridCheckBoxColumn
            {
                Header = property.Name,
                Binding = new Binding { Path = property.Name },
                Width = 120
            };
        }

        return new DataGridTextColumn
        {
            Header = property.Name,
            Binding = new Binding { Path = property.Name },
            Width = 120
        };
    }

    #endregion

    #region Input Handling

    private void OnKeyDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is KeyEventArgs keyArgs)
        {
            switch (keyArgs.Key)
            {
                case Key.Up:
                    if (SelectedIndex > 0)
                        SelectedIndex--;
                    e.Handled = true;
                    break;
                case Key.Down:
                    if (SelectedIndex < _items.Count - 1)
                        SelectedIndex++;
                    e.Handled = true;
                    break;
                case Key.Home:
                    if (_items.Count > 0)
                        SelectedIndex = 0;
                    e.Handled = true;
                    break;
                case Key.End:
                    if (_items.Count > 0)
                        SelectedIndex = _items.Count - 1;
                    e.Handled = true;
                    break;
                case Key.A when keyArgs.KeyboardModifiers.HasFlag(ModifierKeys.Control):
                    if (SelectionMode == DataGridSelectionMode.Extended)
                        SelectAll();
                    e.Handled = true;
                    break;
                case Key.F2:
                    BeginEdit();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    if (_currentEditingCell != null)
                    {
                        CancelEdit();
                        e.Handled = true;
                    }
                    break;
                case Key.Enter:
                    if (_currentEditingCell != null)
                    {
                        CommitEdit();
                        e.Handled = true;
                    }
                    break;
                case Key.Tab:
                    if (_currentEditingCell != null)
                    {
                        CommitEdit();
                        e.Handled = true;
                    }
                    break;
            }
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid)
        {
            if (e.OldValue is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= dataGrid.OnSourceCollectionChanged;
            }

            if (e.NewValue is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += dataGrid.OnSourceCollectionChanged;
            }

            dataGrid.RefreshItems();
        }
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshItems();
    }

    private void RefreshItems()
    {
        var oldSelectedItems = _selectedItems.ToArray();
        _items.Clear();
        if (ItemsSource != null)
        {
            foreach (var item in ItemsSource)
            {
                _items.Add(item);
            }
        }

        if (AutoGenerateColumns && Columns.Count == 0)
        {
            AutoGenerateColumnsFromSource();
        }

        ReconcileSelectionWithItems();
        RefreshColumnHeaders();
        RefreshRows();
        InvalidateMeasure();
        RaiseSelectionChangedIfNeeded(oldSelectedItems);
    }

    private void ReconcileSelectionWithItems()
    {
        _selectedItems.RemoveAll(item => !_items.Contains(item));

        if (SelectionMode == DataGridSelectionMode.Single && _selectedItems.Count > 1)
        {
            _selectedItems.RemoveRange(1, _selectedItems.Count - 1);
        }

        UpdateSelectionPropertiesFromSelectedItems(preferredIndex: SelectedIndex);
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid && !dataGrid._isSynchronizingSelection)
        {
            var oldSelectedItems = dataGrid._selectedItems.ToArray();
            var newItem = e.NewValue;

            dataGrid._isSynchronizingSelection = true;
            try
            {
                if (newItem != null)
                {
                    var index = dataGrid._items.IndexOf(newItem);
                    if (index >= 0)
                    {
                        dataGrid._selectedItems.Clear();
                        dataGrid._selectedItems.Add(newItem);
                        dataGrid.SetValue(SelectedIndexProperty, index);
                    }
                    else
                    {
                        dataGrid._selectedItems.Clear();
                        dataGrid.SetValue(SelectedIndexProperty, -1);
                        dataGrid.SetValue(SelectedItemProperty, null);
                    }
                }
                else
                {
                    dataGrid._selectedItems.Clear();
                    dataGrid.SetValue(SelectedIndexProperty, -1);
                }
            }
            finally
            {
                dataGrid._isSynchronizingSelection = false;
            }

            dataGrid.UpdateRowSelectionVisuals();
            dataGrid.RaiseSelectionChangedIfNeeded(oldSelectedItems);
        }
    }

    private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid && e.NewValue is int newIndex && !dataGrid._isSynchronizingSelection)
        {
            var oldSelectedItems = dataGrid._selectedItems.ToArray();
            dataGrid._isSynchronizingSelection = true;
            try
            {
                if (newIndex >= 0 && newIndex < dataGrid._items.Count)
                {
                    var item = dataGrid._items[newIndex];
                    dataGrid._selectedItems.Clear();
                    dataGrid._selectedItems.Add(item);
                    dataGrid.SetValue(SelectedItemProperty, item);
                }
                else
                {
                    dataGrid._selectedItems.Clear();
                    dataGrid.SetValue(SelectedItemProperty, null);
                    dataGrid.SetValue(SelectedIndexProperty, -1);
                }
            }
            finally
            {
                dataGrid._isSynchronizingSelection = false;
            }

            dataGrid.UpdateRowSelectionVisuals();
            dataGrid.RaiseSelectionChangedIfNeeded(oldSelectedItems);
        }
    }

    private static void OnAutoGenerateColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid && e.NewValue is bool autoGenerate && autoGenerate)
        {
            dataGrid.AutoGenerateColumnsFromSource();
        }
    }

    private static void OnVirtualizationSettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid)
        {
            dataGrid.RefreshRows();
        }
    }

    private static void OnSelectionModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid &&
            e.NewValue is DataGridSelectionMode newMode &&
            newMode == DataGridSelectionMode.Single &&
            dataGrid._selectedItems.Count > 1)
        {
            var oldSelectedItems = dataGrid._selectedItems.ToArray();
            var retainedItem = dataGrid.SelectedItem ?? dataGrid._selectedItems[0];
            dataGrid._selectedItems.Clear();
            dataGrid._selectedItems.Add(retainedItem);
            dataGrid.UpdateSelectionPropertiesFromSelectedItems(dataGrid._items.IndexOf(retainedItem));
            dataGrid.UpdateRowSelectionVisuals();
            dataGrid.RaiseSelectionChangedIfNeeded(oldSelectedItems);
        }
    }

    private static void OnSelectionUnitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid && !Equals(e.OldValue, e.NewValue))
        {
            dataGrid.UnselectAll();
        }
    }

    private static void OnHeadersVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid)
        {
            dataGrid.UpdateHeadersVisibility();
            dataGrid.InvalidateMeasure();
        }
    }

    private static void OnRowHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid)
        {
            dataGrid.RefreshRows();
            dataGrid.InvalidateMeasure();
        }
    }

    private static void OnColumnHeaderHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid)
        {
            dataGrid.RefreshColumnHeaders();
            dataGrid.InvalidateMeasure();
        }
    }

    private static void OnRowHeaderWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid)
        {
            dataGrid.InvalidateMeasure();
        }
    }

    private void SyncColumnDisplayIndexesWithCollection(bool raiseEvents = true)
    {
        if (_isSynchronizingColumnDisplayIndexes)
        {
            return;
        }

        List<DataGridColumn>? changedColumns = null;
        try
        {
            _isSynchronizingColumnDisplayIndexes = true;
            for (var i = 0; i < Columns.Count; i++)
            {
                var column = Columns[i];
                if (column.DisplayIndex != i)
                {
                    column.SetDisplayIndexSilently(i);
                    changedColumns ??= new List<DataGridColumn>();
                    changedColumns.Add(column);
                }
            }
        }
        finally
        {
            _isSynchronizingColumnDisplayIndexes = false;
        }

        if (raiseEvents && changedColumns != null)
        {
            foreach (var column in changedColumns)
            {
                OnColumnDisplayIndexChanged(column);
            }
        }
    }

    internal void RequestColumnDisplayIndex(DataGridColumn column, int requestedDisplayIndex)
    {
        if (_isSynchronizingColumnDisplayIndexes)
        {
            return;
        }

        if (Columns.Count == 0)
        {
            column.SetDisplayIndexSilently(-1);
            return;
        }

        var clampedDisplayIndex = Math.Clamp(requestedDisplayIndex, 0, Columns.Count - 1);
        var currentIndex = Columns.IndexOf(column);
        if (currentIndex < 0)
        {
            column.SetDisplayIndexSilently(clampedDisplayIndex);
            return;
        }

        if (currentIndex == clampedDisplayIndex)
        {
            column.SetDisplayIndexSilently(currentIndex);
            return;
        }

        var args = new DataGridColumnReorderingEventArgs(column);
        OnColumnReordering(args);
        if (args.Cancel)
        {
            column.SetDisplayIndexSilently(currentIndex);
            return;
        }

        Columns.Move(currentIndex, clampedDisplayIndex);
    }

    private void OnColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var oldItem in e.OldItems)
            {
                if (oldItem is DataGridColumn oldColumn && oldColumn.DataGridOwner == this)
                {
                    oldColumn.DataGridOwner = null;
                }
            }
        }

        if (e.NewItems != null)
        {
            foreach (var newItem in e.NewItems)
            {
                if (newItem is DataGridColumn newColumn)
                {
                    newColumn.DataGridOwner = this;
                }
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var column in Columns)
            {
                column.DataGridOwner = this;
            }
        }

        SyncColumnDisplayIndexesWithCollection(raiseEvents: true);
        if (e.Action == NotifyCollectionChangedAction.Move && e.NewItems != null)
        {
            foreach (var movedItem in e.NewItems)
            {
                if (movedItem is DataGridColumn movedColumn)
                {
                    OnColumnReordered(movedColumn);
                }
            }
        }

        RefreshColumnHeaders();
        RefreshRows();
        InvalidateMeasure();
    }

    internal void OnColumnPropertyChanged(DataGridColumn column, DataGridColumnPropertyChange change)
    {
        if (!Columns.Contains(column))
        {
            return;
        }

        if (change == DataGridColumnPropertyChange.SortDirection)
        {
            UpdateColumnHeaderSortIndicators();
            return;
        }

        if (change == DataGridColumnPropertyChange.Layout && _isUpdatingColumnWidthFromResize)
        {
            return;
        }

        RefreshColumnHeaders();
        UpdateRealizedRows(forceRefresh: true);
        InvalidateMeasure();
    }

    #endregion

    #region Column Resizing

    /// <summary>
    /// Resizes a column to the specified width, updating the header and all row cells.
    /// </summary>
    internal void ResizeColumn(DataGridColumn column, double newWidth)
    {
        var colIndex = Columns.IndexOf(column);
        if (colIndex < 0) return;

        _isUpdatingColumnWidthFromResize = true;
        try
        {
            column.Width = newWidth;
        }
        finally
        {
            _isUpdatingColumnWidthFromResize = false;
        }

        // Update column header width
        if (_columnHeadersHost != null)
        {
            for (var i = 0; i < _columnHeadersHost.Children.Count; i++)
            {
                if (_columnHeadersHost.Children[i] is DataGridColumnHeader header && header.Column == column)
                {
                    header.Width = newWidth;
                }
            }
        }

        // Update cell widths in all realized rows
        foreach (var row in _realizedRows.Values)
        {
            if (row.CellsByColumn.TryGetValue(colIndex, out var cell))
            {
                cell.Width = newWidth;
            }
        }

        if (EnableColumnVirtualization)
        {
            UpdateRealizedRows(forceRefresh: true);
        }

        InvalidateMeasure();
    }

    #endregion

    #region Editing

    /// <summary>
    /// Causes the cell being edited to enter edit mode.
    /// </summary>
    public bool BeginEdit()
    {
        if (IsReadOnly || _currentEditingCell != null) return false;

        var row = GetOrRealizeRow(SelectedIndex);
        if (row == null) return false;

        // Find the first editable column
        for (int i = 0; i < Columns.Count; i++)
        {
            if (row == null)
            {
                return false;
            }

            if (!Columns[i].IsReadOnly)
            {
                if (!row.CellsByColumn.TryGetValue(i, out var cell))
                {
                    EnsureColumnVisible(i);
                    row = GetOrRealizeRow(SelectedIndex);
                    if (row == null || !row.CellsByColumn.TryGetValue(i, out cell))
                    {
                        continue;
                    }
                }

                return BeginEditCell(row, cell, Columns[i]);
            }
        }

        return false;
    }

    /// <summary>
    /// Invokes the CommitEdit command, which will commit any pending editing.
    /// </summary>
    public bool CommitEdit()
    {
        return CommitEdit(DataGridEditingUnit.Cell, true);
    }

    /// <summary>
    /// Invokes the CommitEdit command for the given editing unit.
    /// </summary>
    public bool CommitEdit(DataGridEditingUnit editingUnit, bool exitEditingMode)
    {
        if (_currentEditingCell == null) return false;

        var endingArgs = new DataGridCellEditEndingEventArgs(CellEditEndingEvent,
            _currentEditingColumn!, _currentEditingRow!, _currentEditingCell, DataGridEditAction.Commit);
        RaiseEvent(endingArgs);

        if (endingArgs.Cancel) return false;

        // Write back the edited value to the data item
        if (_currentEditingColumn != null && _currentEditingRow?.DataItem != null && _currentEditingCell._editingElement != null)
        {
            _currentEditingColumn.CommitCellEdit(_currentEditingCell._editingElement, _currentEditingRow.DataItem);
        }

        if (exitEditingMode)
        {
            _currentEditingCell.IsEditing = false;

            // Refresh the display element to show the updated value
            RefreshCellDisplay(_currentEditingCell, _currentEditingColumn!, _currentEditingRow!);

            _currentEditingCell = null;
            _currentEditingColumn = null;
            _currentEditingRow = null;
        }

        return true;
    }

    /// <summary>
    /// Invokes the CancelEdit command.
    /// </summary>
    public bool CancelEdit()
    {
        return CancelEdit(DataGridEditingUnit.Cell);
    }

    /// <summary>
    /// Invokes the CancelEdit command for the given editing unit.
    /// </summary>
    public bool CancelEdit(DataGridEditingUnit editingUnit)
    {
        if (_currentEditingCell == null) return false;

        var endingArgs = new DataGridCellEditEndingEventArgs(CellEditEndingEvent,
            _currentEditingColumn!, _currentEditingRow!, _currentEditingCell, DataGridEditAction.Cancel);
        RaiseEvent(endingArgs);

        // Cancel the edit on the column (restore the editing element to the original value)
        if (_currentEditingColumn != null && _currentEditingRow?.DataItem != null && _currentEditingCell._editingElement != null)
        {
            _currentEditingColumn.CancelCellEdit(_currentEditingCell._editingElement, _currentEditingRow.DataItem);
        }

        // Setting IsEditing to false will restore the display element via the property change callback
        _currentEditingCell.IsEditing = false;
        _currentEditingCell = null;
        _currentEditingColumn = null;
        _currentEditingRow = null;

        return true;
    }

    private bool BeginEditCell(DataGridRow row, DataGridCell cell, DataGridColumn column)
    {
        if (column.IsReadOnly || IsReadOnly) return false;

        var beginArgs = new DataGridBeginningEditEventArgs(BeginningEditEvent, column, row, cell);
        RaiseEvent(beginArgs);

        if (beginArgs.Cancel) return false;

        _currentEditingCell = cell;
        _currentEditingColumn = column;
        _currentEditingRow = row;
        cell.IsEditing = true;

        var prepArgs = new DataGridPreparingCellForEditEventArgs(PreparingCellForEditEvent, column, row, cell);
        RaiseEvent(prepArgs);

        return true;
    }

    /// <summary>
    /// Refreshes the display content of a cell after editing is committed.
    /// Regenerates the display element from the column to reflect the updated data.
    /// </summary>
    private void RefreshCellDisplay(DataGridCell cell, DataGridColumn column, DataGridRow row)
    {
        if (row.DataItem != null)
        {
            var displayElement = column.GenerateElement(cell, row.DataItem);
            cell.Content = displayElement;
        }
    }

    #endregion

    #region Scrolling

    public void ScrollIntoView(object item)
    {
        var index = _items.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        if (_dataScrollViewer != null)
        {
            _dataScrollViewer.ScrollToVerticalOffset(index * GetEffectiveRowHeight());
        }

        UpdateRealizedRows(forceRefresh: true);
        if (_realizedRows.TryGetValue(index, out var row))
        {
            row.BringIntoView();
        }
    }

    private DataGridRow? GetOrRealizeRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _items.Count)
        {
            return null;
        }

        if (_realizedRows.TryGetValue(rowIndex, out var existing))
        {
            return existing;
        }

        if (_dataScrollViewer != null && EnableRowVirtualization)
        {
            _dataScrollViewer.ScrollToVerticalOffset(rowIndex * GetEffectiveRowHeight());
        }

        UpdateRealizedRows(forceRefresh: true);
        return _realizedRows.TryGetValue(rowIndex, out var realized) ? realized : null;
    }

    private void EnsureColumnVisible(int columnIndex)
    {
        if (_dataScrollViewer == null || columnIndex < 0 || columnIndex >= Columns.Count)
        {
            return;
        }

        if (!IsColumnVisible(Columns[columnIndex]))
        {
            return;
        }

        var viewportWidth = _dataScrollViewer.ViewportWidth > 0
            ? _dataScrollViewer.ViewportWidth
            : _dataScrollViewer.ActualWidth;
        if (viewportWidth <= 0)
        {
            return;
        }

        var columnStart = GetColumnStartOffset(columnIndex);
        var columnWidth = GetRenderableColumnWidth(Columns[columnIndex]);
        var columnEnd = columnStart + columnWidth;
        var currentOffset = _dataScrollViewer.HorizontalOffset;

        if (columnStart < currentOffset)
        {
            _dataScrollViewer.ScrollToHorizontalOffset(columnStart);
        }
        else if (columnEnd > currentOffset + viewportWidth)
        {
            _dataScrollViewer.ScrollToHorizontalOffset(Math.Max(0, columnEnd - viewportWidth));
        }

        UpdateRealizedRows(forceRefresh: true);
    }

    private double GetColumnStartOffset(int columnIndex)
    {
        var offset = 0.0;
        for (var i = 0; i < columnIndex && i < Columns.Count; i++)
        {
            offset += GetRenderableColumnWidth(Columns[i]);
        }

        return offset;
    }

    #endregion
}

#region DataGridRow

/// <summary>
/// Represents a row in a DataGrid.
/// </summary>
public class DataGridRow : Control
{
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(DataGridRow),
            new PropertyMetadata(false));

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty)!;
        set => SetValue(IsSelectedProperty, value);
    }

    internal object? DataItem { get; set; }
    internal int RowIndex { get; set; }
    internal DataGrid? ParentDataGrid { get; set; }
    internal Brush? AlternatingBackground { get; set; }
    internal int VisibleColumnStart { get; set; } = -1;
    internal int VisibleColumnEnd { get; set; } = -1;
    internal List<DataGridCell> Cells { get; } = new();
    internal Dictionary<int, DataGridCell> CellsByColumn { get; } = new();

    private StackPanel? _cellsPanel;

    public DataGridRow()
    {
        Focusable = false;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _cellsPanel = GetTemplateChild("PART_CellsPanel") as StackPanel;

        if (_cellsPanel != null)
        {
            foreach (var cell in Cells)
            {
                _cellsPanel.Children.Add(cell);
            }
        }

        // Apply alternating background if not selected
        if (!IsSelected && AlternatingBackground != null)
        {
            Background = AlternatingBackground;
        }
    }
}

#endregion

#region DataGridCell

/// <summary>
/// Represents a cell in a DataGrid.
/// </summary>
public class DataGridCell : ContentControl
{
    public static readonly DependencyProperty IsEditingProperty =
        DependencyProperty.Register(nameof(IsEditing), typeof(bool), typeof(DataGridCell),
            new PropertyMetadata(false, OnIsEditingChanged));

    /// <summary>
    /// Gets or sets whether this cell is in editing mode.
    /// </summary>
    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty)!;
        set => SetValue(IsEditingProperty, value);
    }

    /// <summary>
    /// Gets or sets the column associated with this cell.
    /// </summary>
    internal DataGridColumn? Column { get; set; }

    /// <summary>
    /// Stores the display element when the cell enters editing mode.
    /// </summary>
    internal FrameworkElement? _displayElement;

    /// <summary>
    /// Stores the editing element while the cell is in editing mode.
    /// </summary>
    internal FrameworkElement? _editingElement;

    /// <summary>
    /// Stores the original cell value before editing began, for cancel/restore.
    /// </summary>
    internal object? _originalValue;

    public DataGridCell()
    {
        UseTemplateContentManagement();
        Focusable = false;
    }

    private static void OnIsEditingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGridCell cell && cell.Column != null)
        {
            if ((bool)(e.NewValue ?? false))
            {
                // Entering edit mode: store the display content and swap in the editing element
                cell._displayElement = cell.Content as FrameworkElement;

                // Find the parent DataGridRow to get the data item
                var row = FindParentRow(cell);
                if (row?.DataItem != null)
                {
                    // Store the original value for cancel
                    cell._originalValue = cell.Column.GetCellContent(row.DataItem);

                    var editingElement = cell.Column.GenerateEditingElement(cell, row.DataItem);
                    if (editingElement != null)
                    {
                        cell._editingElement = editingElement;
                        cell.Content = editingElement;

                        // Focus the editing element after it's placed in the visual tree
                        editingElement.Focus();
                        if (editingElement is TextBox tb)
                        {
                            tb.SelectAll();
                        }
                    }
                }
            }
            else
            {
                // Exiting edit mode: restore the display element
                cell._editingElement = null;
                if (cell._displayElement != null)
                {
                    cell.Content = cell._displayElement;
                    cell._displayElement = null;
                }
                cell._originalValue = null;
            }
        }
    }

    /// <summary>
    /// Walks up the visual tree to find the parent DataGridRow.
    /// </summary>
    private static DataGridRow? FindParentRow(DataGridCell cell)
    {
        Visual? parent = cell.VisualParent;
        while (parent != null)
        {
            if (parent is DataGridRow row)
                return row;
            parent = parent.VisualParent;
        }
        return null;
    }
}

#endregion

#region DataGridColumnHeader

/// <summary>
/// Represents a column header in a DataGrid.
/// </summary>
public class DataGridColumnHeader : ContentControl
{
    private const double ResizeHotZoneWidth = 8.0;

    internal DataGrid? ParentDataGrid { get; set; }
    internal DataGridColumn? Column { get; set; }

    private TextBlock? _sortIndicator;
    private FrameworkElement? _resizeGrip;
    private bool _isResizing;
    private double _resizeStartX;
    private double _resizeStartWidth;

    public DataGridColumnHeader()
    {
        UseTemplateContentManagement();
        Focusable = false;

        AddHandler(PreviewMouseDownEvent, new RoutedEventHandler(OnPreviewMouseDownHandler), true);
        AddHandler(MouseMoveEvent, new RoutedEventHandler(OnMouseMoveHandler), true);
        AddHandler(MouseUpEvent, new RoutedEventHandler(OnMouseUpHandler), true);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _sortIndicator = GetTemplateChild("PART_SortIndicator") as TextBlock;
        _resizeGrip = GetTemplateChild("PART_ResizeGrip") as FrameworkElement;

        if (_resizeGrip != null)
        {
            _resizeGrip.IsHitTestVisible = false;
        }

        UpdateResizeGripState();
        UpdateSortIndicator(Column?.SortDirection);
    }

    private void UpdateResizeGripState()
    {
        if (_resizeGrip == null)
        {
            return;
        }

        var canResize = Column != null
            && (ParentDataGrid?.CanUserResizeColumns ?? false)
            && Column.CanUserResize;

        _resizeGrip.Visibility = canResize ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool CanResizeCurrentColumn() =>
        Column != null
        && (ParentDataGrid?.CanUserResizeColumns ?? false)
        && Column.CanUserResize;

    private bool IsInResizeZone(Point point)
    {
        if (!CanResizeCurrentColumn())
        {
            return false;
        }

        var hotZoneWidth = Math.Max(1.0, Math.Min(RenderSize.Width, ResizeHotZoneWidth));
        return point.X >= Math.Max(0.0, RenderSize.Width - hotZoneWidth);
    }

    private void UpdateResizeCursor(Point point)
    {
        Cursor = (_isResizing || IsInResizeZone(point))
            ? Jalium.UI.Cursors.SizeWE
            : null;
    }

    private void OnPreviewMouseDownHandler(object sender, RoutedEventArgs e)
    {
        if (e is MouseButtonEventArgs mouseArgs
            && mouseArgs.ChangedButton == MouseButton.Left
            && Column != null
            && IsInResizeZone(mouseArgs.GetPosition(this)))
        {
            _isResizing = true;
            _resizeStartX = mouseArgs.GetPosition(null).X;
            _resizeStartWidth = Column.Width;
            CaptureMouse();
            Cursor = Jalium.UI.Cursors.SizeWE;
            e.Handled = true;
        }
    }

    private void OnMouseMoveHandler(object sender, RoutedEventArgs e)
    {
        if (e is not MouseEventArgs mouseArgs)
        {
            return;
        }

        if (_isResizing && Column != null)
        {
            var currentX = mouseArgs.GetPosition(null).X;
            var delta = currentX - _resizeStartX;
            var newWidth = Math.Clamp(_resizeStartWidth + delta, Column.MinWidth, Column.MaxWidth);
            ParentDataGrid?.ResizeColumn(Column, newWidth);
            Cursor = Jalium.UI.Cursors.SizeWE;
            e.Handled = true;
            return;
        }

        UpdateResizeCursor(mouseArgs.GetPosition(this));
    }

    private void OnMouseUpHandler(object sender, RoutedEventArgs e)
    {
        if (_isResizing)
        {
            _isResizing = false;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            if (e is MouseEventArgs mouseArgs)
            {
                UpdateResizeCursor(mouseArgs.GetPosition(this));
            }
            else
            {
                Cursor = null;
            }

            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();
        _isResizing = false;
        Cursor = null;
    }

    internal void UpdateSortIndicator(ListSortDirection? direction)
    {
        if (_sortIndicator == null) return;

        if (direction.HasValue)
        {
            _sortIndicator.Text = direction == ListSortDirection.Ascending ? "\u25B2" : "\u25BC";
            _sortIndicator.Visibility = Visibility.Visible;
        }
        else
        {
            _sortIndicator.Text = "";
            _sortIndicator.Visibility = Visibility.Collapsed;
        }
    }
}

#endregion

#region Supporting Types

public enum DataGridSelectionMode
{
    Single,
    Extended
}

public enum DataGridSelectionUnit
{
    Cell,
    FullRow,
    CellOrRowHeader
}

public enum DataGridGridLinesVisibility
{
    All,
    Horizontal,
    Vertical,
    None
}

[Flags]
public enum DataGridHeadersVisibility
{
    None = 0,
    Column = 1,
    Row = 2,
    All = Column | Row
}

internal enum DataGridColumnPropertyChange
{
    Layout,
    Header,
    Visibility,
    SortDirection
}

public sealed class DataGridSortingEventArgs : RoutedEventArgs
{
    public DataGridColumn Column { get; }

    public DataGridSortingEventArgs(RoutedEvent routedEvent, DataGridColumn column)
        : base(routedEvent)
    {
        Column = column;
    }
}

/// <summary>
/// Provides data for the BeginningEdit event.
/// </summary>
public sealed class DataGridBeginningEditEventArgs : RoutedEventArgs
{
    public DataGridColumn Column { get; }
    public DataGridRow Row { get; }
    public DataGridCell Cell { get; }
    public bool Cancel { get; set; }

    public DataGridBeginningEditEventArgs(RoutedEvent routedEvent, DataGridColumn column, DataGridRow row, DataGridCell cell)
        : base(routedEvent)
    {
        Column = column;
        Row = row;
        Cell = cell;
    }
}

/// <summary>
/// Provides data for the CellEditEnding event.
/// </summary>
public sealed class DataGridCellEditEndingEventArgs : RoutedEventArgs
{
    public DataGridColumn Column { get; }
    public DataGridRow Row { get; }
    public DataGridCell Cell { get; }
    public DataGridEditAction EditAction { get; }
    public bool Cancel { get; set; }

    public DataGridCellEditEndingEventArgs(RoutedEvent routedEvent, DataGridColumn column, DataGridRow row, DataGridCell cell, DataGridEditAction editAction)
        : base(routedEvent)
    {
        Column = column;
        Row = row;
        Cell = cell;
        EditAction = editAction;
    }
}

/// <summary>
/// Provides data for the PreparingCellForEdit event.
/// </summary>
public sealed class DataGridPreparingCellForEditEventArgs : RoutedEventArgs
{
    public DataGridColumn Column { get; }
    public DataGridRow Row { get; }
    public DataGridCell Cell { get; }

    public DataGridPreparingCellForEditEventArgs(RoutedEvent routedEvent, DataGridColumn column, DataGridRow row, DataGridCell cell)
        : base(routedEvent)
    {
        Column = column;
        Row = row;
        Cell = cell;
    }
}

/// <summary>
/// Defines the unit of editing.
/// </summary>
public enum DataGridEditingUnit
{
    Cell,
    Row
}

/// <summary>
/// Defines the editing action.
/// </summary>
public enum DataGridEditAction
{
    Cancel,
    Commit
}

/// <summary>
/// Defines the visibility mode for row details.
/// </summary>
public enum DataGridRowDetailsVisibilityMode
{
    Collapsed,
    Visible,
    VisibleWhenSelected
}

#endregion

#region Column Types

public abstract class DataGridColumn : DependencyObject
{
    private const double DefaultWidth = 120.0;
    private const double DefaultMinWidth = 20.0;

    private ListSortDirection? _sortDirection;
    private Visibility _visibility = Visibility.Visible;
    private int _displayIndex = -1;

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(DataGridColumn),
            new PropertyMetadata(null, OnHeaderPropertyChanged));

    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.Register(nameof(Width), typeof(double), typeof(DataGridColumn),
            new PropertyMetadata(DefaultWidth, OnWidthPropertyChanged, CoerceWidth));

    public static readonly DependencyProperty MinWidthProperty =
        DependencyProperty.Register(nameof(MinWidth), typeof(double), typeof(DataGridColumn),
            new PropertyMetadata(DefaultMinWidth, OnMinWidthPropertyChanged, CoerceMinWidth));

    public static readonly DependencyProperty MaxWidthProperty =
        DependencyProperty.Register(nameof(MaxWidth), typeof(double), typeof(DataGridColumn),
            new PropertyMetadata(double.PositiveInfinity, OnMaxWidthPropertyChanged, CoerceMaxWidth));

    internal DataGrid? DataGridOwner { get; set; }

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public double Width
    {
        get => (double)GetValue(WidthProperty)!;
        set => SetValue(WidthProperty, value);
    }

    public double MinWidth
    {
        get => (double)GetValue(MinWidthProperty)!;
        set => SetValue(MinWidthProperty, value < 0 || double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : value);
    }

    public double MaxWidth
    {
        get => (double)GetValue(MaxWidthProperty)!;
        set => SetValue(MaxWidthProperty, value < 0 || double.IsNaN(value) ? double.PositiveInfinity : value);
    }

    public double ActualWidth => Width;

    public bool CanUserSort { get; set; } = true;
    public bool CanUserResize { get; set; } = true;
    public bool CanUserReorder { get; set; } = true;
    public ListSortDirection? SortDirection
    {
        get => _sortDirection;
        set
        {
            if (_sortDirection == value)
            {
                return;
            }

            _sortDirection = value;
            NotifyOwner(DataGridColumnPropertyChange.SortDirection);
        }
    }

    public bool IsReadOnly { get; set; }

    /// <summary>
    /// Gets or sets the style applied to cells in this column.
    /// </summary>
    public Style? CellStyle { get; set; }

    /// <summary>
    /// Gets or sets the style applied to the column header.
    /// </summary>
    public Style? HeaderStyle { get; set; }

    /// <summary>
    /// Gets or sets the Visibility of the column.
    /// </summary>
    public Visibility Visibility
    {
        get => _visibility;
        set
        {
            if (_visibility == value)
            {
                return;
            }

            _visibility = value;
            NotifyOwner(DataGridColumnPropertyChange.Visibility);
        }
    }

    public int DisplayIndex
    {
        get => _displayIndex;
        set
        {
            if (DataGridOwner != null)
            {
                DataGridOwner.RequestColumnDisplayIndex(this, value);
                return;
            }

            _displayIndex = value;
        }
    }

    private void NotifyOwner(DataGridColumnPropertyChange change) =>
        DataGridOwner?.OnColumnPropertyChanged(this, change);

    internal void SetDisplayIndexSilently(int displayIndex) =>
        _displayIndex = displayIndex;

    private static void OnHeaderPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGridColumn column)
        {
            column.NotifyOwner(DataGridColumnPropertyChange.Header);
        }
    }

    private static void OnWidthPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGridColumn column)
        {
            column.NotifyOwner(DataGridColumnPropertyChange.Layout);
        }
    }

    private static void OnMinWidthPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGridColumn column)
        {
            column.CoerceValue(WidthProperty);
            column.NotifyOwner(DataGridColumnPropertyChange.Layout);
        }
    }

    private static void OnMaxWidthPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGridColumn column)
        {
            column.CoerceValue(WidthProperty);
            column.NotifyOwner(DataGridColumnPropertyChange.Layout);
        }
    }

    private static object? CoerceWidth(DependencyObject d, object? baseValue)
    {
        if (baseValue is not double width)
        {
            width = DefaultWidth;
        }

        if (double.IsNaN(width) || double.IsInfinity(width) || width < 0)
        {
            width = DefaultWidth;
        }

        if (d is DataGridColumn column)
        {
            width = Math.Clamp(width, column.MinWidth, column.MaxWidth);
        }

        return width;
    }

    private static object? CoerceMinWidth(DependencyObject d, object? baseValue)
    {
        if (baseValue is not double minWidth)
        {
            minWidth = DefaultMinWidth;
        }

        if (double.IsNaN(minWidth) || double.IsInfinity(minWidth) || minWidth < 0)
        {
            minWidth = 0.0;
        }

        if (d is DataGridColumn column && minWidth > column.MaxWidth)
        {
            minWidth = column.MaxWidth;
        }

        return minWidth;
    }

    private static object? CoerceMaxWidth(DependencyObject d, object? baseValue)
    {
        if (baseValue is not double maxWidth)
        {
            maxWidth = double.PositiveInfinity;
        }

        if (double.IsNaN(maxWidth) || maxWidth < 0 || double.IsNegativeInfinity(maxWidth))
        {
            maxWidth = double.PositiveInfinity;
        }

        if (d is DataGridColumn column && maxWidth < column.MinWidth)
        {
            maxWidth = column.MinWidth;
        }

        return maxWidth;
    }

    public abstract object? GetCellContent(object item);

    /// <summary>
    /// Generates the display element for a cell.
    /// </summary>
    /// <param name="cell">The cell that will host the element.</param>
    /// <param name="dataItem">The data item for the row.</param>
    /// <returns>A FrameworkElement to display in the cell.</returns>
    public virtual FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
    {
        var value = GetCellContent(dataItem);
        return new TextBlock { Text = value?.ToString() ?? "" };
    }

    /// <summary>
    /// Generates the editing element for a cell.
    /// </summary>
    /// <param name="cell">The cell entering edit mode.</param>
    /// <param name="dataItem">The data item for the row.</param>
    /// <returns>A FrameworkElement for editing, or null if the column is not editable.</returns>
    public virtual FrameworkElement? GenerateEditingElement(DataGridCell cell, object dataItem)
    {
        return null;
    }

    /// <summary>
    /// Commits the edit from the editing element back to the data item.
    /// </summary>
    /// <param name="editingElement">The editing element containing the new value.</param>
    /// <param name="dataItem">The data item to write the value to.</param>
    public virtual void CommitCellEdit(FrameworkElement editingElement, object dataItem)
    {
    }

    /// <summary>
    /// Cancels the edit and restores the original value on the editing element.
    /// </summary>
    /// <param name="editingElement">The editing element to restore.</param>
    /// <param name="dataItem">The data item with the original value.</param>
    public virtual void CancelCellEdit(FrameworkElement editingElement, object dataItem)
    {
    }
}

public abstract class DataGridBoundColumn : DataGridColumn
{
    public Binding? Binding { get; set; }

    public override object? GetCellContent(object item)
    {
        if (Binding?.Path == null) return null;

        var current = item;
        foreach (var part in Binding.Path.Split('.'))
        {
            if (current == null) return null;
            var prop = current.GetType().GetProperty(part);
            current = prop?.GetValue(current);
        }
        return current;
    }

    /// <summary>
    /// Writes a value back to the data item using the column's Binding.Path via reflection.
    /// </summary>
    /// <param name="dataItem">The data item to write to.</param>
    /// <param name="value">The value to set.</param>
    protected void SetCellValue(object dataItem, object? value)
    {
        if (Binding?.Path == null) return;

        var parts = Binding.Path.Split('.');
        var target = dataItem;

        // Navigate to the parent object for nested paths
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (target == null) return;
            var prop = target.GetType().GetProperty(parts[i]);
            target = prop?.GetValue(target);
        }

        if (target == null) return;

        var finalProp = target.GetType().GetProperty(parts[^1]);
        if (finalProp?.CanWrite == true)
        {
            try
            {
                var convertedValue = value == null
                    ? null
                    : Convert.ChangeType(value, finalProp.PropertyType);
                finalProp.SetValue(target, convertedValue);
            }
            catch (InvalidCastException)
            {
                // If conversion fails, try setting the raw value
                finalProp.SetValue(target, value);
            }
            catch (FormatException)
            {
                // If format conversion fails, ignore the write-back
            }
        }
    }
}

public sealed class DataGridTextColumn : DataGridBoundColumn
{
    /// <summary>
    /// Gets or sets the style applied to the TextBlock display element.
    /// </summary>
    public Style? ElementStyle { get; set; }

    /// <summary>
    /// Gets or sets the style applied to the TextBox editing element.
    /// </summary>
    public Style? EditingElementStyle { get; set; }

    public override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
    {
        var value = GetCellContent(dataItem);
        var textBlock = new TextBlock
        {
            Text = value?.ToString() ?? "",
            VerticalAlignment = VerticalAlignment.Center
        };
        if (ElementStyle != null)
            textBlock.Style = ElementStyle;
        return textBlock;
    }

    public override FrameworkElement? GenerateEditingElement(DataGridCell cell, object dataItem)
    {
        var value = GetCellContent(dataItem);
        var textBox = new TextBox
        {
            Text = value?.ToString() ?? "",
            BorderThickness = new Thickness(0),
            Background = Jalium.UI.Media.Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (EditingElementStyle != null)
            textBox.Style = EditingElementStyle;
        return textBox;
    }

    public override void CommitCellEdit(FrameworkElement editingElement, object dataItem)
    {
        if (editingElement is TextBox textBox)
        {
            SetCellValue(dataItem, textBox.Text);
        }
    }

    public override void CancelCellEdit(FrameworkElement editingElement, object dataItem)
    {
        if (editingElement is TextBox textBox)
        {
            var originalValue = GetCellContent(dataItem);
            textBox.Text = originalValue?.ToString() ?? "";
        }
    }
}

public sealed class DataGridCheckBoxColumn : DataGridBoundColumn
{
    /// <summary>
    /// Gets or sets the style applied to the CheckBox display element.
    /// </summary>
    public Style? ElementStyle { get; set; }

    /// <summary>
    /// Gets or sets the style applied to the CheckBox editing element.
    /// </summary>
    public Style? EditingElementStyle { get; set; }

    /// <summary>
    /// Gets or sets whether the CheckBox is a three-state checkbox.
    /// </summary>
    public bool IsThreeState { get; set; }

    public override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
    {
        var value = GetCellContent(dataItem);
        var checkBox = new CheckBox
        {
            IsChecked = value is bool b ? b : false,
            IsEnabled = false,
            IsThreeState = IsThreeState,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (ElementStyle != null)
            checkBox.Style = ElementStyle;
        return checkBox;
    }

    public override FrameworkElement? GenerateEditingElement(DataGridCell cell, object dataItem)
    {
        var value = GetCellContent(dataItem);
        var checkBox = new CheckBox
        {
            IsChecked = value is bool b ? b : false,
            IsThreeState = IsThreeState,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (EditingElementStyle != null)
            checkBox.Style = EditingElementStyle;
        return checkBox;
    }

    public override void CommitCellEdit(FrameworkElement editingElement, object dataItem)
    {
        if (editingElement is CheckBox checkBox)
        {
            SetCellValue(dataItem, checkBox.IsChecked);
        }
    }

    public override void CancelCellEdit(FrameworkElement editingElement, object dataItem)
    {
        if (editingElement is CheckBox checkBox)
        {
            var originalValue = GetCellContent(dataItem);
            checkBox.IsChecked = originalValue is bool b ? b : false;
        }
    }
}

public sealed class DataGridComboBoxColumn : DataGridBoundColumn
{
    public IEnumerable? ItemsSource { get; set; }
    public string? DisplayMemberPath { get; set; }
    public string? SelectedValuePath { get; set; }

    /// <summary>
    /// Gets or sets the style applied to the TextBlock display element.
    /// </summary>
    public Style? ElementStyle { get; set; }

    /// <summary>
    /// Gets or sets the style applied to the ComboBox editing element.
    /// </summary>
    public Style? EditingElementStyle { get; set; }

    public override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
    {
        var value = GetCellContent(dataItem);
        var textBlock = new TextBlock
        {
            Text = value?.ToString() ?? "",
            VerticalAlignment = VerticalAlignment.Center
        };
        if (ElementStyle != null)
            textBlock.Style = ElementStyle;
        return textBlock;
    }

    public override FrameworkElement? GenerateEditingElement(DataGridCell cell, object dataItem)
    {
        var value = GetCellContent(dataItem);
        var comboBox = new ComboBox
        {
            VerticalAlignment = VerticalAlignment.Center
        };

        if (ItemsSource != null)
            comboBox.ItemsSource = ItemsSource;

        // Try to select the current value
        if (value != null)
        {
            comboBox.SelectedItem = value;
        }

        if (EditingElementStyle != null)
            comboBox.Style = EditingElementStyle;

        return comboBox;
    }

    public override void CommitCellEdit(FrameworkElement editingElement, object dataItem)
    {
        if (editingElement is ComboBox comboBox)
        {
            var value = !string.IsNullOrEmpty(SelectedValuePath)
                ? GetNestedPropertyValue(comboBox.SelectedItem, SelectedValuePath)
                : comboBox.SelectedItem;
            SetCellValue(dataItem, value);
        }
    }

    public override void CancelCellEdit(FrameworkElement editingElement, object dataItem)
    {
        if (editingElement is ComboBox comboBox)
        {
            var originalValue = GetCellContent(dataItem);
            comboBox.SelectedItem = originalValue;
        }
    }

    private static object? GetNestedPropertyValue(object? obj, string propertyPath)
    {
        if (obj == null || string.IsNullOrEmpty(propertyPath)) return obj;
        var current = obj;
        foreach (var part in propertyPath.Split('.'))
        {
            if (current == null) return null;
            var prop = current.GetType().GetProperty(part);
            current = prop?.GetValue(current);
        }
        return current;
    }
}

public sealed class DataGridTemplateColumn : DataGridColumn
{
    public DataTemplate? CellTemplate { get; set; }
    public DataTemplate? CellEditingTemplate { get; set; }

    public override object? GetCellContent(object item)
    {
        return item;
    }

    public override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
    {
        if (CellTemplate != null)
        {
            var element = CellTemplate.LoadContent();
            if (element != null)
            {
                element.DataContext = dataItem;
                return element;
            }
        }

        return new TextBlock { Text = dataItem?.ToString() ?? "" };
    }

    public override FrameworkElement? GenerateEditingElement(DataGridCell cell, object dataItem)
    {
        var template = CellEditingTemplate ?? CellTemplate;
        if (template != null)
        {
            var element = template.LoadContent();
            if (element != null)
            {
                element.DataContext = dataItem;
                return element;
            }
        }

        return null;
    }
}

/// <summary>
/// Represents a DataGrid column that hosts URI elements in its cells.
/// </summary>
public sealed class DataGridHyperlinkColumn : DataGridBoundColumn
{
    /// <summary>
    /// Gets or sets the binding for the text content of the hyperlink.
    /// </summary>
    public Binding? ContentBinding { get; set; }

    /// <summary>
    /// Gets or sets the name of a target window or frame for the hyperlink.
    /// </summary>
    public string? TargetName { get; set; }
}

public class Binding
{
    public string? Path { get; set; }
    public string? StringFormat { get; set; }
    public BindingMode Mode { get; set; } = BindingMode.Default;
}

public enum BindingMode
{
    Default,
    OneWay,
    TwoWay,
    OneTime,
    OneWayToSource
}

#endregion
