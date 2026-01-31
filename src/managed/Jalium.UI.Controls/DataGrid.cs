using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that displays data in a customizable grid.
/// </summary>
public class DataGrid : Control
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the ItemsSource dependency property.
    /// </summary>
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(DataGrid),
            new PropertyMetadata(null, OnItemsSourceChanged));

    /// <summary>
    /// Identifies the SelectedItem dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(DataGrid),
            new PropertyMetadata(null, OnSelectedItemChanged));

    /// <summary>
    /// Identifies the SelectedIndex dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(DataGrid),
            new PropertyMetadata(-1, OnSelectedIndexChanged));

    /// <summary>
    /// Identifies the AutoGenerateColumns dependency property.
    /// </summary>
    public static readonly DependencyProperty AutoGenerateColumnsProperty =
        DependencyProperty.Register(nameof(AutoGenerateColumns), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(true, OnAutoGenerateColumnsChanged));

    /// <summary>
    /// Identifies the CanUserSortColumns dependency property.
    /// </summary>
    public static readonly DependencyProperty CanUserSortColumnsProperty =
        DependencyProperty.Register(nameof(CanUserSortColumns), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the CanUserResizeColumns dependency property.
    /// </summary>
    public static readonly DependencyProperty CanUserResizeColumnsProperty =
        DependencyProperty.Register(nameof(CanUserResizeColumns), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the CanUserReorderColumns dependency property.
    /// </summary>
    public static readonly DependencyProperty CanUserReorderColumnsProperty =
        DependencyProperty.Register(nameof(CanUserReorderColumns), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the SelectionMode dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectionModeProperty =
        DependencyProperty.Register(nameof(SelectionMode), typeof(DataGridSelectionMode), typeof(DataGrid),
            new PropertyMetadata(DataGridSelectionMode.Single));

    /// <summary>
    /// Identifies the SelectionUnit dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectionUnitProperty =
        DependencyProperty.Register(nameof(SelectionUnit), typeof(DataGridSelectionUnit), typeof(DataGrid),
            new PropertyMetadata(DataGridSelectionUnit.FullRow));

    /// <summary>
    /// Identifies the GridLinesVisibility dependency property.
    /// </summary>
    public static readonly DependencyProperty GridLinesVisibilityProperty =
        DependencyProperty.Register(nameof(GridLinesVisibility), typeof(DataGridGridLinesVisibility), typeof(DataGrid),
            new PropertyMetadata(DataGridGridLinesVisibility.All, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the HeadersVisibility dependency property.
    /// </summary>
    public static readonly DependencyProperty HeadersVisibilityProperty =
        DependencyProperty.Register(nameof(HeadersVisibility), typeof(DataGridHeadersVisibility), typeof(DataGrid),
            new PropertyMetadata(DataGridHeadersVisibility.Column, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the RowHeight dependency property.
    /// </summary>
    public static readonly DependencyProperty RowHeightProperty =
        DependencyProperty.Register(nameof(RowHeight), typeof(double), typeof(DataGrid),
            new PropertyMetadata(24.0, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the ColumnHeaderHeight dependency property.
    /// </summary>
    public static readonly DependencyProperty ColumnHeaderHeightProperty =
        DependencyProperty.Register(nameof(ColumnHeaderHeight), typeof(double), typeof(DataGrid),
            new PropertyMetadata(28.0, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the RowHeaderWidth dependency property.
    /// </summary>
    public static readonly DependencyProperty RowHeaderWidthProperty =
        DependencyProperty.Register(nameof(RowHeaderWidth), typeof(double), typeof(DataGrid),
            new PropertyMetadata(20.0, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the IsReadOnly dependency property.
    /// </summary>
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the AlternatingRowBackground dependency property.
    /// </summary>
    public static readonly DependencyProperty AlternatingRowBackgroundProperty =
        DependencyProperty.Register(nameof(AlternatingRowBackground), typeof(Brush), typeof(DataGrid),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the RowBackground dependency property.
    /// </summary>
    public static readonly DependencyProperty RowBackgroundProperty =
        DependencyProperty.Register(nameof(RowBackground), typeof(Brush), typeof(DataGrid),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the HorizontalGridLinesBrush dependency property.
    /// </summary>
    public static readonly DependencyProperty HorizontalGridLinesBrushProperty =
        DependencyProperty.Register(nameof(HorizontalGridLinesBrush), typeof(Brush), typeof(DataGrid),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the VerticalGridLinesBrush dependency property.
    /// </summary>
    public static readonly DependencyProperty VerticalGridLinesBrushProperty =
        DependencyProperty.Register(nameof(VerticalGridLinesBrush), typeof(Brush), typeof(DataGrid),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the SelectionChanged routed event.
    /// </summary>
    public static readonly RoutedEvent SelectionChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SelectionChanged), RoutingStrategy.Bubble,
            typeof(EventHandler<SelectionChangedEventArgs>), typeof(DataGrid));

    /// <summary>
    /// Identifies the Sorting routed event.
    /// </summary>
    public static readonly RoutedEvent SortingEvent =
        EventManager.RegisterRoutedEvent(nameof(Sorting), RoutingStrategy.Bubble,
            typeof(EventHandler<DataGridSortingEventArgs>), typeof(DataGrid));

    /// <summary>
    /// Occurs when the selection changes.
    /// </summary>
    public event EventHandler<SelectionChangedEventArgs> SelectionChanged
    {
        add => AddHandler(SelectionChangedEvent, value);
        remove => RemoveHandler(SelectionChangedEvent, value);
    }

    /// <summary>
    /// Occurs when a column is being sorted.
    /// </summary>
    public event EventHandler<DataGridSortingEventArgs> Sorting
    {
        add => AddHandler(SortingEvent, value);
        remove => RemoveHandler(SortingEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a collection used to generate the content of the DataGrid.
    /// </summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the currently selected item.
    /// </summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Gets or sets the index of the currently selected item.
    /// </summary>
    public int SelectedIndex
    {
        get => (int)(GetValue(SelectedIndexProperty) ?? -1);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether columns are automatically generated.
    /// </summary>
    public bool AutoGenerateColumns
    {
        get => (bool)(GetValue(AutoGenerateColumnsProperty) ?? true);
        set => SetValue(AutoGenerateColumnsProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the user can sort columns.
    /// </summary>
    public bool CanUserSortColumns
    {
        get => (bool)(GetValue(CanUserSortColumnsProperty) ?? true);
        set => SetValue(CanUserSortColumnsProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the user can resize columns.
    /// </summary>
    public bool CanUserResizeColumns
    {
        get => (bool)(GetValue(CanUserResizeColumnsProperty) ?? true);
        set => SetValue(CanUserResizeColumnsProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the user can reorder columns.
    /// </summary>
    public bool CanUserReorderColumns
    {
        get => (bool)(GetValue(CanUserReorderColumnsProperty) ?? false);
        set => SetValue(CanUserReorderColumnsProperty, value);
    }

    /// <summary>
    /// Gets or sets the selection mode.
    /// </summary>
    public DataGridSelectionMode SelectionMode
    {
        get => (DataGridSelectionMode)(GetValue(SelectionModeProperty) ?? DataGridSelectionMode.Single);
        set => SetValue(SelectionModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the selection unit.
    /// </summary>
    public DataGridSelectionUnit SelectionUnit
    {
        get => (DataGridSelectionUnit)(GetValue(SelectionUnitProperty) ?? DataGridSelectionUnit.FullRow);
        set => SetValue(SelectionUnitProperty, value);
    }

    /// <summary>
    /// Gets or sets which grid lines are shown.
    /// </summary>
    public DataGridGridLinesVisibility GridLinesVisibility
    {
        get => (DataGridGridLinesVisibility)(GetValue(GridLinesVisibilityProperty) ?? DataGridGridLinesVisibility.All);
        set => SetValue(GridLinesVisibilityProperty, value);
    }

    /// <summary>
    /// Gets or sets which headers are shown.
    /// </summary>
    public DataGridHeadersVisibility HeadersVisibility
    {
        get => (DataGridHeadersVisibility)(GetValue(HeadersVisibilityProperty) ?? DataGridHeadersVisibility.Column);
        set => SetValue(HeadersVisibilityProperty, value);
    }

    /// <summary>
    /// Gets or sets the standard height of rows.
    /// </summary>
    public double RowHeight
    {
        get => (double)(GetValue(RowHeightProperty) ?? 24.0);
        set => SetValue(RowHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the height of column headers.
    /// </summary>
    public double ColumnHeaderHeight
    {
        get => (double)(GetValue(ColumnHeaderHeightProperty) ?? 28.0);
        set => SetValue(ColumnHeaderHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the width of row headers.
    /// </summary>
    public double RowHeaderWidth
    {
        get => (double)(GetValue(RowHeaderWidthProperty) ?? 20.0);
        set => SetValue(RowHeaderWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the DataGrid is read-only.
    /// </summary>
    public bool IsReadOnly
    {
        get => (bool)(GetValue(IsReadOnlyProperty) ?? false);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for alternating rows.
    /// </summary>
    public Brush? AlternatingRowBackground
    {
        get => (Brush?)GetValue(AlternatingRowBackgroundProperty);
        set => SetValue(AlternatingRowBackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the default row background brush.
    /// </summary>
    public Brush? RowBackground
    {
        get => (Brush?)GetValue(RowBackgroundProperty);
        set => SetValue(RowBackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for horizontal grid lines.
    /// </summary>
    public Brush? HorizontalGridLinesBrush
    {
        get => (Brush?)GetValue(HorizontalGridLinesBrushProperty);
        set => SetValue(HorizontalGridLinesBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for vertical grid lines.
    /// </summary>
    public Brush? VerticalGridLinesBrush
    {
        get => (Brush?)GetValue(VerticalGridLinesBrushProperty);
        set => SetValue(VerticalGridLinesBrushProperty, value);
    }

    /// <summary>
    /// Gets the collection of columns.
    /// </summary>
    public ObservableCollection<DataGridColumn> Columns { get; }

    /// <summary>
    /// Gets the list of selected items.
    /// </summary>
    public IList<object> SelectedItems { get; }

    #endregion

    #region Private Fields

    private readonly List<object> _items = new();
    private readonly List<object> _selectedItems = new();
    private double _scrollOffsetX;
    private double _scrollOffsetY;
    private int _hoveredRowIndex = -1;
    private int _hoveredColumnIndex = -1;
    private int? _resizingColumnIndex;
    private double _resizeStartX;
    private double _resizeStartWidth;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="DataGrid"/> class.
    /// </summary>
    public DataGrid()
    {
        Focusable = true;
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        Foreground = new SolidColorBrush(Color.White);
        BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70));
        BorderThickness = new Thickness(1);

        Columns = new ObservableCollection<DataGridColumn>();
        Columns.CollectionChanged += OnColumnsCollectionChanged;
        SelectedItems = _selectedItems;

        HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(50, 50, 50));
        VerticalGridLinesBrush = new SolidColorBrush(Color.FromRgb(50, 50, 50));
        AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(35, 35, 35));

        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
        AddHandler(MouseMoveEvent, new RoutedEventHandler(OnMouseMoveHandler));
        AddHandler(MouseUpEvent, new RoutedEventHandler(OnMouseUpHandler));
        AddHandler(KeyDownEvent, new RoutedEventHandler(OnKeyDownHandler));
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            Focus();
            var position = mouseArgs.GetPosition(this);

            // Check for column resize
            if (CanUserResizeColumns && HeadersVisibility.HasFlag(DataGridHeadersVisibility.Column))
            {
                var headerHeight = ColumnHeaderHeight;
                if (position.Y < headerHeight)
                {
                    var resizeIndex = GetColumnResizeIndex(position.X);
                    if (resizeIndex.HasValue)
                    {
                        _resizingColumnIndex = resizeIndex;
                        _resizeStartX = position.X;
                        _resizeStartWidth = Columns[resizeIndex.Value].ActualWidth;
                        e.Handled = true;
                        return;
                    }
                }
            }

            // Check for column header click (sorting)
            if (CanUserSortColumns && HeadersVisibility.HasFlag(DataGridHeadersVisibility.Column))
            {
                var headerHeight = ColumnHeaderHeight;
                if (position.Y < headerHeight)
                {
                    var columnIndex = GetColumnIndexAtPosition(position.X);
                    if (columnIndex >= 0 && columnIndex < Columns.Count)
                    {
                        SortByColumn(Columns[columnIndex]);
                        e.Handled = true;
                        return;
                    }
                }
            }

            // Row selection
            var rowIndex = GetRowIndexAtPosition(position.Y);
            if (rowIndex >= 0 && rowIndex < _items.Count)
            {
                SelectRow(rowIndex, mouseArgs.KeyboardModifiers);
                e.Handled = true;
            }
        }
    }

    private void OnMouseMoveHandler(object sender, RoutedEventArgs e)
    {
        if (e is MouseEventArgs mouseArgs)
        {
            var position = mouseArgs.GetPosition(this);

            // Handle column resize dragging
            if (_resizingColumnIndex.HasValue)
            {
                var delta = position.X - _resizeStartX;
                var newWidth = Math.Max(20, _resizeStartWidth + delta);
                Columns[_resizingColumnIndex.Value].Width = newWidth;
                InvalidateVisual();
                return;
            }

            // Update hover state
            var rowIndex = GetRowIndexAtPosition(position.Y);
            var columnIndex = GetColumnIndexAtPosition(position.X);

            if (rowIndex != _hoveredRowIndex || columnIndex != _hoveredColumnIndex)
            {
                _hoveredRowIndex = rowIndex;
                _hoveredColumnIndex = columnIndex;
                InvalidateVisual();
            }
        }
    }

    private void OnMouseUpHandler(object sender, RoutedEventArgs e)
    {
        _resizingColumnIndex = null;
    }

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
            }
        }
    }

    private int? GetColumnResizeIndex(double x)
    {
        var currentX = HeadersVisibility.HasFlag(DataGridHeadersVisibility.Row) ? RowHeaderWidth : 0;
        currentX -= _scrollOffsetX;

        for (var i = 0; i < Columns.Count; i++)
        {
            currentX += Columns[i].ActualWidth;
            if (Math.Abs(x - currentX) < 5)
                return i;
        }
        return null;
    }

    private int GetColumnIndexAtPosition(double x)
    {
        var currentX = HeadersVisibility.HasFlag(DataGridHeadersVisibility.Row) ? RowHeaderWidth : 0;
        currentX -= _scrollOffsetX;

        for (var i = 0; i < Columns.Count; i++)
        {
            var nextX = currentX + Columns[i].ActualWidth;
            if (x >= currentX && x < nextX)
                return i;
            currentX = nextX;
        }
        return -1;
    }

    private int GetRowIndexAtPosition(double y)
    {
        var headerOffset = HeadersVisibility.HasFlag(DataGridHeadersVisibility.Column) ? ColumnHeaderHeight : 0;
        var adjustedY = y - headerOffset + _scrollOffsetY;

        if (adjustedY < 0) return -1;
        return (int)(adjustedY / RowHeight);
    }

    #endregion

    #region Selection

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

        SelectedItem = item;
        SelectedIndex = rowIndex;

        var args = new SelectionChangedEventArgs(SelectionChangedEvent, oldSelectedItems, _selectedItems.ToArray());
        RaiseEvent(args);

        InvalidateVisual();
    }

    /// <summary>
    /// Selects all items in the DataGrid.
    /// </summary>
    public void SelectAll()
    {
        if (SelectionMode != DataGridSelectionMode.Extended) return;

        var oldSelectedItems = _selectedItems.ToArray();
        _selectedItems.Clear();
        _selectedItems.AddRange(_items);

        var args = new SelectionChangedEventArgs(SelectionChangedEvent, oldSelectedItems, _selectedItems.ToArray());
        RaiseEvent(args);

        InvalidateVisual();
    }

    /// <summary>
    /// Clears the selection.
    /// </summary>
    public void UnselectAll()
    {
        var oldSelectedItems = _selectedItems.ToArray();
        _selectedItems.Clear();
        SelectedItem = null;
        SelectedIndex = -1;

        var args = new SelectionChangedEventArgs(SelectionChangedEvent, oldSelectedItems, Array.Empty<object>());
        RaiseEvent(args);

        InvalidateVisual();
    }

    #endregion

    #region Sorting

    private void SortByColumn(DataGridColumn column)
    {
        if (!column.CanUserSort) return;

        var sortingArgs = new DataGridSortingEventArgs(SortingEvent, column);
        RaiseEvent(sortingArgs);

        if (sortingArgs.Handled) return;

        // Toggle sort direction
        var newDirection = column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;

        // Clear other column sorts
        foreach (var col in Columns)
        {
            if (col != column)
                col.SortDirection = null;
        }

        column.SortDirection = newDirection;

        // Sort the items
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

        InvalidateVisual();
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

            var column = new DataGridTextColumn
            {
                Header = prop.Name,
                Binding = new Binding { Path = prop.Name },
                Width = 100
            };
            Columns.Add(column);
        }
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var headerHeight = HeadersVisibility.HasFlag(DataGridHeadersVisibility.Column) ? ColumnHeaderHeight : 0;
        var rowHeaderWidth = HeadersVisibility.HasFlag(DataGridHeadersVisibility.Row) ? RowHeaderWidth : 0;

        var totalColumnsWidth = rowHeaderWidth + Columns.Sum(c => c.ActualWidth);
        var totalRowsHeight = headerHeight + _items.Count * RowHeight;

        var width = double.IsPositiveInfinity(availableSize.Width) ? totalColumnsWidth : availableSize.Width;
        var height = double.IsPositiveInfinity(availableSize.Height) ? totalRowsHeight : availableSize.Height;

        return new Size(Math.Max(width, 100), Math.Max(height, 50));
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(RenderSize);
        var headerHeight = HeadersVisibility.HasFlag(DataGridHeadersVisibility.Column) ? ColumnHeaderHeight : 0;
        var rowHeaderWidth = HeadersVisibility.HasFlag(DataGridHeadersVisibility.Row) ? RowHeaderWidth : 0;

        // Draw background
        if (Background != null)
        {
            dc.DrawRectangle(Background, null, rect);
        }

        // Draw border
        if (BorderBrush != null && BorderThickness.TotalWidth > 0)
        {
            dc.DrawRectangle(null, new Pen(BorderBrush, BorderThickness.Left), rect);
        }

        // Draw column headers
        if (HeadersVisibility.HasFlag(DataGridHeadersVisibility.Column))
        {
            DrawColumnHeaders(dc, rowHeaderWidth, headerHeight);
        }

        // Draw rows
        DrawRows(dc, rowHeaderWidth, headerHeight);

        // Draw grid lines
        if (GridLinesVisibility != DataGridGridLinesVisibility.None)
        {
            DrawGridLines(dc, rowHeaderWidth, headerHeight);
        }
    }

    private void DrawColumnHeaders(DrawingContext dc, double rowHeaderWidth, double headerHeight)
    {
        var headerBg = new SolidColorBrush(Color.FromRgb(45, 45, 45));
        var headerRect = new Rect(0, 0, RenderSize.Width, headerHeight);
        dc.DrawRectangle(headerBg, null, headerRect);

        var x = rowHeaderWidth - _scrollOffsetX;
        foreach (var column in Columns)
        {
            if (x + column.ActualWidth > 0 && x < RenderSize.Width)
            {
                var cellRect = new Rect(x, 0, column.ActualWidth, headerHeight);

                // Draw header text
                var headerText = column.Header?.ToString() ?? "";
                var formattedText = new FormattedText(headerText, FontFamily ?? "Segoe UI", FontSize > 0 ? FontSize : 12)
                {
                    Foreground = Foreground ?? new SolidColorBrush(Color.White)
                };
                TextMeasurement.MeasureText(formattedText);

                var textX = x + 8;
                var textY = (headerHeight - formattedText.Height) / 2;
                dc.DrawText(formattedText, new Point(textX, textY));

                // Draw sort indicator
                if (column.SortDirection.HasValue)
                {
                    var sortIndicator = column.SortDirection == ListSortDirection.Ascending ? "▲" : "▼";
                    var sortText = new FormattedText(sortIndicator, FontFamily ?? "Segoe UI", 10)
                    {
                        Foreground = Foreground ?? new SolidColorBrush(Color.White)
                    };
                    TextMeasurement.MeasureText(sortText);
                    dc.DrawText(sortText, new Point(x + column.ActualWidth - sortText.Width - 8, textY));
                }
            }

            x += column.ActualWidth;
        }
    }

    private void DrawRows(DrawingContext dc, double rowHeaderWidth, double headerHeight)
    {
        var y = headerHeight - _scrollOffsetY;
        var selectionBrush = new SolidColorBrush(Color.FromRgb(0, 120, 215));
        var hoverBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60));

        for (var rowIndex = 0; rowIndex < _items.Count; rowIndex++)
        {
            if (y + RowHeight < 0)
            {
                y += RowHeight;
                continue;
            }
            if (y > RenderSize.Height) break;

            var item = _items[rowIndex];
            var rowRect = new Rect(0, y, RenderSize.Width, RowHeight);

            // Draw row background
            Brush? rowBg = null;
            if (_selectedItems.Contains(item))
            {
                rowBg = selectionBrush;
            }
            else if (rowIndex == _hoveredRowIndex)
            {
                rowBg = hoverBrush;
            }
            else if (rowIndex % 2 == 1 && AlternatingRowBackground != null)
            {
                rowBg = AlternatingRowBackground;
            }
            else if (RowBackground != null)
            {
                rowBg = RowBackground;
            }

            if (rowBg != null)
            {
                dc.DrawRectangle(rowBg, null, rowRect);
            }

            // Draw row header
            if (HeadersVisibility.HasFlag(DataGridHeadersVisibility.Row))
            {
                var rowHeaderRect = new Rect(0, y, rowHeaderWidth, RowHeight);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(45, 45, 45)), null, rowHeaderRect);

                var rowNumText = new FormattedText((rowIndex + 1).ToString(), FontFamily ?? "Segoe UI", 10)
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150))
                };
                TextMeasurement.MeasureText(rowNumText);
                dc.DrawText(rowNumText, new Point((rowHeaderWidth - rowNumText.Width) / 2, y + (RowHeight - rowNumText.Height) / 2));
            }

            // Draw cells
            var x = rowHeaderWidth - _scrollOffsetX;
            foreach (var column in Columns)
            {
                if (x + column.ActualWidth > 0 && x < RenderSize.Width)
                {
                    var cellValue = column.GetCellContent(item);
                    var cellText = cellValue?.ToString() ?? "";

                    var formattedText = new FormattedText(cellText, FontFamily ?? "Segoe UI", FontSize > 0 ? FontSize : 12)
                    {
                        Foreground = _selectedItems.Contains(item) ? new SolidColorBrush(Color.White) : (Foreground ?? new SolidColorBrush(Color.White))
                    };
                    TextMeasurement.MeasureText(formattedText);

                    var textX = x + 8;
                    var textY = y + (RowHeight - formattedText.Height) / 2;

                    // Clip text to cell bounds
                    dc.DrawText(formattedText, new Point(textX, textY));
                }

                x += column.ActualWidth;
            }

            y += RowHeight;
        }
    }

    private void DrawGridLines(DrawingContext dc, double rowHeaderWidth, double headerHeight)
    {
        var showHorizontal = GridLinesVisibility == DataGridGridLinesVisibility.All ||
                            GridLinesVisibility == DataGridGridLinesVisibility.Horizontal;
        var showVertical = GridLinesVisibility == DataGridGridLinesVisibility.All ||
                          GridLinesVisibility == DataGridGridLinesVisibility.Vertical;

        var horizontalPen = showHorizontal && HorizontalGridLinesBrush != null
            ? new Pen(HorizontalGridLinesBrush, 1)
            : null;
        var verticalPen = showVertical && VerticalGridLinesBrush != null
            ? new Pen(VerticalGridLinesBrush, 1)
            : null;

        // Draw horizontal lines
        if (horizontalPen != null)
        {
            var y = headerHeight;
            for (var i = 0; i <= _items.Count; i++)
            {
                if (y >= 0 && y <= RenderSize.Height)
                {
                    dc.DrawLine(horizontalPen, new Point(0, y), new Point(RenderSize.Width, y));
                }
                y += RowHeight;
            }
        }

        // Draw vertical lines
        if (verticalPen != null)
        {
            var x = rowHeaderWidth - _scrollOffsetX;
            foreach (var column in Columns)
            {
                x += column.ActualWidth;
                if (x >= 0 && x <= RenderSize.Width)
                {
                    dc.DrawLine(verticalPen, new Point(x, 0), new Point(x, RenderSize.Height));
                }
            }
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid)
        {
            // Unsubscribe from old collection
            if (e.OldValue is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= dataGrid.OnSourceCollectionChanged;
            }

            // Subscribe to new collection
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

        InvalidateMeasure();
        InvalidateVisual();
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid)
        {
            dataGrid.InvalidateVisual();
        }
    }

    private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid && e.NewValue is int newIndex)
        {
            if (newIndex >= 0 && newIndex < dataGrid._items.Count)
            {
                dataGrid.SelectedItem = dataGrid._items[newIndex];
            }
        }
    }

    private static void OnAutoGenerateColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid && (bool)e.NewValue)
        {
            dataGrid.AutoGenerateColumnsFromSource();
        }
    }

    private void OnColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid)
        {
            dataGrid.InvalidateMeasure();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid)
        {
            dataGrid.InvalidateVisual();
        }
    }

    #endregion

    #region Scrolling

    /// <summary>
    /// Scrolls to the specified item.
    /// </summary>
    public void ScrollIntoView(object item)
    {
        var index = _items.IndexOf(item);
        if (index < 0) return;

        var headerHeight = HeadersVisibility.HasFlag(DataGridHeadersVisibility.Column) ? ColumnHeaderHeight : 0;
        var itemTop = index * RowHeight;
        var itemBottom = itemTop + RowHeight;
        var viewportTop = _scrollOffsetY;
        var viewportBottom = viewportTop + RenderSize.Height - headerHeight;

        if (itemTop < viewportTop)
        {
            _scrollOffsetY = itemTop;
        }
        else if (itemBottom > viewportBottom)
        {
            _scrollOffsetY = itemBottom - (RenderSize.Height - headerHeight);
        }

        InvalidateVisual();
    }

    #endregion
}

#region Supporting Types

/// <summary>
/// Specifies the selection mode for a DataGrid.
/// </summary>
public enum DataGridSelectionMode
{
    /// <summary>
    /// Only one item can be selected at a time.
    /// </summary>
    Single,

    /// <summary>
    /// Multiple items can be selected using Shift and Ctrl keys.
    /// </summary>
    Extended
}

/// <summary>
/// Specifies the unit of selection for a DataGrid.
/// </summary>
public enum DataGridSelectionUnit
{
    /// <summary>
    /// Only cells can be selected.
    /// </summary>
    Cell,

    /// <summary>
    /// Only full rows can be selected.
    /// </summary>
    FullRow,

    /// <summary>
    /// Both cells and full rows can be selected.
    /// </summary>
    CellOrRowHeader
}

/// <summary>
/// Specifies which grid lines are shown.
/// </summary>
public enum DataGridGridLinesVisibility
{
    /// <summary>
    /// Both horizontal and vertical grid lines are shown.
    /// </summary>
    All,

    /// <summary>
    /// Only horizontal grid lines are shown.
    /// </summary>
    Horizontal,

    /// <summary>
    /// Only vertical grid lines are shown.
    /// </summary>
    Vertical,

    /// <summary>
    /// No grid lines are shown.
    /// </summary>
    None
}

/// <summary>
/// Specifies which headers are shown.
/// </summary>
[Flags]
public enum DataGridHeadersVisibility
{
    /// <summary>
    /// No headers are shown.
    /// </summary>
    None = 0,

    /// <summary>
    /// Column headers are shown.
    /// </summary>
    Column = 1,

    /// <summary>
    /// Row headers are shown.
    /// </summary>
    Row = 2,

    /// <summary>
    /// Both column and row headers are shown.
    /// </summary>
    All = Column | Row
}

/// <summary>
/// Provides data for the Sorting event.
/// </summary>
public class DataGridSortingEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the column being sorted.
    /// </summary>
    public DataGridColumn Column { get; }

    /// <summary>
    /// Creates a new instance of the DataGridSortingEventArgs class.
    /// </summary>
    public DataGridSortingEventArgs(RoutedEvent routedEvent, DataGridColumn column)
        : base(routedEvent)
    {
        Column = column;
    }
}

#endregion

#region Column Types

/// <summary>
/// Represents a column in a DataGrid.
/// </summary>
public abstract class DataGridColumn : DependencyObject
{
    /// <summary>
    /// Identifies the Header dependency property.
    /// </summary>
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(DataGridColumn),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Width dependency property.
    /// </summary>
    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.Register(nameof(Width), typeof(double), typeof(DataGridColumn),
            new PropertyMetadata(100.0));

    /// <summary>
    /// Identifies the MinWidth dependency property.
    /// </summary>
    public static readonly DependencyProperty MinWidthProperty =
        DependencyProperty.Register(nameof(MinWidth), typeof(double), typeof(DataGridColumn),
            new PropertyMetadata(20.0));

    /// <summary>
    /// Identifies the MaxWidth dependency property.
    /// </summary>
    public static readonly DependencyProperty MaxWidthProperty =
        DependencyProperty.Register(nameof(MaxWidth), typeof(double), typeof(DataGridColumn),
            new PropertyMetadata(double.PositiveInfinity));

    /// <summary>
    /// Gets or sets the column header content.
    /// </summary>
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets the column width.
    /// </summary>
    public double Width
    {
        get => (double)(GetValue(WidthProperty) ?? 100.0);
        set => SetValue(WidthProperty, Math.Max(MinWidth, Math.Min(MaxWidth, value)));
    }

    /// <summary>
    /// Gets or sets the minimum column width.
    /// </summary>
    public double MinWidth
    {
        get => (double)(GetValue(MinWidthProperty) ?? 20.0);
        set => SetValue(MinWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum column width.
    /// </summary>
    public double MaxWidth
    {
        get => (double)(GetValue(MaxWidthProperty) ?? double.PositiveInfinity);
        set => SetValue(MaxWidthProperty, value);
    }

    /// <summary>
    /// Gets the actual width of the column.
    /// </summary>
    public double ActualWidth => Width;

    /// <summary>
    /// Gets or sets whether the user can sort this column.
    /// </summary>
    public bool CanUserSort { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the user can resize this column.
    /// </summary>
    public bool CanUserResize { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the user can reorder this column.
    /// </summary>
    public bool CanUserReorder { get; set; } = true;

    /// <summary>
    /// Gets or sets the current sort direction.
    /// </summary>
    public ListSortDirection? SortDirection { get; set; }

    /// <summary>
    /// Gets or sets whether this column is read-only.
    /// </summary>
    public bool IsReadOnly { get; set; }

    /// <summary>
    /// Gets the cell content for the specified item.
    /// </summary>
    public abstract object? GetCellContent(object item);
}

/// <summary>
/// Represents a column that is bound to a property.
/// </summary>
public abstract class DataGridBoundColumn : DataGridColumn
{
    /// <summary>
    /// Gets or sets the binding for this column.
    /// </summary>
    public Binding? Binding { get; set; }

    /// <inheritdoc />
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
}

/// <summary>
/// Represents a text column in a DataGrid.
/// </summary>
public class DataGridTextColumn : DataGridBoundColumn
{
}

/// <summary>
/// Represents a checkbox column in a DataGrid.
/// </summary>
public class DataGridCheckBoxColumn : DataGridBoundColumn
{
}

/// <summary>
/// Represents a combo box column in a DataGrid.
/// </summary>
public class DataGridComboBoxColumn : DataGridBoundColumn
{
    /// <summary>
    /// Gets or sets the items source for the combo box.
    /// </summary>
    public IEnumerable? ItemsSource { get; set; }

    /// <summary>
    /// Gets or sets the display member path.
    /// </summary>
    public string? DisplayMemberPath { get; set; }

    /// <summary>
    /// Gets or sets the selected value path.
    /// </summary>
    public string? SelectedValuePath { get; set; }
}

/// <summary>
/// Represents a template column in a DataGrid.
/// </summary>
public class DataGridTemplateColumn : DataGridColumn
{
    /// <summary>
    /// Gets or sets the cell template.
    /// </summary>
    public DataTemplate? CellTemplate { get; set; }

    /// <summary>
    /// Gets or sets the cell editing template.
    /// </summary>
    public DataTemplate? CellEditingTemplate { get; set; }

    /// <inheritdoc />
    public override object? GetCellContent(object item)
    {
        // Template columns return the item itself for templating
        return item;
    }
}

/// <summary>
/// Represents a simple property binding.
/// </summary>
public class Binding
{
    /// <summary>
    /// Gets or sets the property path.
    /// </summary>
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets the string format.
    /// </summary>
    public string? StringFormat { get; set; }

    /// <summary>
    /// Gets or sets the binding mode.
    /// </summary>
    public BindingMode Mode { get; set; } = BindingMode.Default;
}

/// <summary>
/// Specifies the binding mode.
/// </summary>
public enum BindingMode
{
    /// <summary>
    /// Uses the default binding mode.
    /// </summary>
    Default,

    /// <summary>
    /// One-way binding from source to target.
    /// </summary>
    OneWay,

    /// <summary>
    /// Two-way binding between source and target.
    /// </summary>
    TwoWay,

    /// <summary>
    /// One-time binding from source to target.
    /// </summary>
    OneTime,

    /// <summary>
    /// One-way binding from target to source.
    /// </summary>
    OneWayToSource
}

#endregion
