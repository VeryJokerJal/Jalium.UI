using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using ListSortDirection = Jalium.UI.Data.ListSortDirection;

namespace Jalium.UI.Controls;

/// <summary>
/// Displays hierarchical data in a flat, column-based grid with expand/collapse support.
/// Combines TreeView-style hierarchy with DataGrid-style columns.
/// </summary>
public class TreeDataGrid : Control, IColumnHeaderHost
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Controls.Automation.GenericAutomationPeer(this, Jalium.UI.Automation.AutomationControlType.DataGrid);

    bool IColumnHeaderHost.IsColumnDragging => _isColumnDragging;
    void IColumnHeaderHost.ResizeColumn(DataGridColumn column, double newWidth) => ResizeColumn(column, newWidth);
    void IColumnHeaderHost.StartColumnDrag(DataGridColumnHeader sourceHeader, DataGridColumn column) => StartColumnDrag(sourceHeader, column);
    void IColumnHeaderHost.UpdateColumnDrag(Point positionInHost) => UpdateColumnDrag(positionInHost);
    void IColumnHeaderHost.EndColumnDrag(Point positionInHost) => EndColumnDrag(positionInHost);
    void IColumnHeaderHost.CancelColumnDrag() => CancelColumnDrag();

    #region Dependency Properties

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(TreeDataGrid),
            new PropertyMetadata(null, OnItemsSourceChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public static readonly DependencyProperty ChildrenPropertyPathProperty =
        DependencyProperty.Register(nameof(ChildrenPropertyPath), typeof(string), typeof(TreeDataGrid),
            new PropertyMetadata(null, OnChildrenPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(TreeDataGrid),
            new PropertyMetadata(null, OnSelectedItemChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(TreeDataGrid),
            new PropertyMetadata(-1, OnSelectedIndexChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectionModeProperty =
        DependencyProperty.Register(nameof(SelectionMode), typeof(DataGridSelectionMode), typeof(TreeDataGrid),
            new PropertyMetadata(DataGridSelectionMode.Extended, OnSelectionModeChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanUserSortColumnsProperty =
        DependencyProperty.Register(nameof(CanUserSortColumns), typeof(bool), typeof(TreeDataGrid),
            new PropertyMetadata(true));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanUserResizeColumnsProperty =
        DependencyProperty.Register(nameof(CanUserResizeColumns), typeof(bool), typeof(TreeDataGrid),
            new PropertyMetadata(true));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanUserReorderColumnsProperty =
        DependencyProperty.Register(nameof(CanUserReorderColumns), typeof(bool), typeof(TreeDataGrid),
            new PropertyMetadata(true));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty EnableRowVirtualizationProperty =
        DependencyProperty.Register(nameof(EnableRowVirtualization), typeof(bool), typeof(TreeDataGrid),
            new PropertyMetadata(true, OnVirtualizationSettingsChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty EnableColumnVirtualizationProperty =
        DependencyProperty.Register(nameof(EnableColumnVirtualization), typeof(bool), typeof(TreeDataGrid),
            new PropertyMetadata(false, OnVirtualizationSettingsChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty GridLinesVisibilityProperty =
        DependencyProperty.Register(nameof(GridLinesVisibility), typeof(DataGridGridLinesVisibility), typeof(TreeDataGrid),
            new PropertyMetadata(DataGridGridLinesVisibility.All));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeadersVisibilityProperty =
        DependencyProperty.Register(nameof(HeadersVisibility), typeof(DataGridHeadersVisibility), typeof(TreeDataGrid),
            new PropertyMetadata(DataGridHeadersVisibility.Column, OnHeadersVisibilityChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RowHeightProperty =
        DependencyProperty.Register(nameof(RowHeight), typeof(double), typeof(TreeDataGrid),
            new PropertyMetadata(double.NaN, OnRowHeightChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnHeaderHeightProperty =
        DependencyProperty.Register(nameof(ColumnHeaderHeight), typeof(double), typeof(TreeDataGrid),
            new PropertyMetadata(double.NaN, OnColumnHeaderHeightChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty IndentSizeProperty =
        DependencyProperty.Register(nameof(IndentSize), typeof(double), typeof(TreeDataGrid),
            new PropertyMetadata(16.0, OnIndentSizeChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TreeColumnIndexProperty =
        DependencyProperty.Register(nameof(TreeColumnIndex), typeof(int), typeof(TreeDataGrid),
            new PropertyMetadata(0, OnTreeColumnIndexChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(TreeDataGrid),
            new PropertyMetadata(false));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty AlternatingRowBackgroundProperty =
        DependencyProperty.Register(nameof(AlternatingRowBackground), typeof(Brush), typeof(TreeDataGrid),
            new PropertyMetadata(null));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RowBackgroundProperty =
        DependencyProperty.Register(nameof(RowBackground), typeof(Brush), typeof(TreeDataGrid),
            new PropertyMetadata(null));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty HorizontalGridLinesBrushProperty =
        DependencyProperty.Register(nameof(HorizontalGridLinesBrush), typeof(Brush), typeof(TreeDataGrid),
            new PropertyMetadata(null));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty VerticalGridLinesBrushProperty =
        DependencyProperty.Register(nameof(VerticalGridLinesBrush), typeof(Brush), typeof(TreeDataGrid),
            new PropertyMetadata(null));

    #endregion

    #region Routed Events

    public static readonly RoutedEvent SelectionChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SelectionChanged), RoutingStrategy.Bubble,
            typeof(EventHandler<SelectionChangedEventArgs>), typeof(TreeDataGrid));

    public static readonly RoutedEvent NodeExpandingEvent =
        EventManager.RegisterRoutedEvent(nameof(NodeExpanding), RoutingStrategy.Bubble,
            typeof(EventHandler<TreeDataGridNodeExpandingEventArgs>), typeof(TreeDataGrid));

    public static readonly RoutedEvent NodeExpandedEvent =
        EventManager.RegisterRoutedEvent(nameof(NodeExpanded), RoutingStrategy.Bubble,
            typeof(EventHandler<TreeDataGridNodeEventArgs>), typeof(TreeDataGrid));

    public static readonly RoutedEvent NodeCollapsedEvent =
        EventManager.RegisterRoutedEvent(nameof(NodeCollapsed), RoutingStrategy.Bubble,
            typeof(EventHandler<TreeDataGridNodeEventArgs>), typeof(TreeDataGrid));

    public event EventHandler<SelectionChangedEventArgs> SelectionChanged
    {
        add => AddHandler(SelectionChangedEvent, value);
        remove => RemoveHandler(SelectionChangedEvent, value);
    }

    public event EventHandler<TreeDataGridNodeExpandingEventArgs> NodeExpanding
    {
        add => AddHandler(NodeExpandingEvent, value);
        remove => RemoveHandler(NodeExpandingEvent, value);
    }

    public event EventHandler<TreeDataGridNodeEventArgs> NodeExpanded
    {
        add => AddHandler(NodeExpandedEvent, value);
        remove => RemoveHandler(NodeExpandedEvent, value);
    }

    public event EventHandler<TreeDataGridNodeEventArgs> NodeCollapsed
    {
        add => AddHandler(NodeCollapsedEvent, value);
        remove => RemoveHandler(NodeCollapsedEvent, value);
    }

    #endregion

    #region CLR Properties

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public string? ChildrenPropertyPath
    {
        get => (string?)GetValue(ChildrenPropertyPathProperty);
        set => SetValue(ChildrenPropertyPathProperty, value);
    }

    /// <summary>
    /// Gets or sets a delegate that returns child items for a given data item.
    /// Takes priority over <see cref="ChildrenPropertyPath"/> when both are set.
    /// </summary>
    public Func<object, IEnumerable?>? ChildrenSelector { get; set; }

    /// <summary>
    /// Gets or sets a delegate that determines if a data item has children without loading them.
    /// Used to show the expander arrow before children are resolved.
    /// </summary>
    public Func<object, bool>? HasChildrenSelector { get; set; }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty)!;
        set => SetValue(SelectedIndexProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public DataGridSelectionMode SelectionMode
    {
        get => (DataGridSelectionMode)GetValue(SelectionModeProperty)!;
        set => SetValue(SelectionModeProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool CanUserSortColumns
    {
        get => (bool)GetValue(CanUserSortColumnsProperty)!;
        set => SetValue(CanUserSortColumnsProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool CanUserResizeColumns
    {
        get => (bool)GetValue(CanUserResizeColumnsProperty)!;
        set => SetValue(CanUserResizeColumnsProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool CanUserReorderColumns
    {
        get => (bool)GetValue(CanUserReorderColumnsProperty)!;
        set => SetValue(CanUserReorderColumnsProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool EnableRowVirtualization
    {
        get => (bool)GetValue(EnableRowVirtualizationProperty)!;
        set => SetValue(EnableRowVirtualizationProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool EnableColumnVirtualization
    {
        get => (bool)GetValue(EnableColumnVirtualizationProperty)!;
        set => SetValue(EnableColumnVirtualizationProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DataGridGridLinesVisibility GridLinesVisibility
    {
        get => (DataGridGridLinesVisibility)GetValue(GridLinesVisibilityProperty)!;
        set => SetValue(GridLinesVisibilityProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public DataGridHeadersVisibility HeadersVisibility
    {
        get => (DataGridHeadersVisibility)GetValue(HeadersVisibilityProperty)!;
        set => SetValue(HeadersVisibilityProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double RowHeight
    {
        get => (double)GetValue(RowHeightProperty)!;
        set => SetValue(RowHeightProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double ColumnHeaderHeight
    {
        get => (double)GetValue(ColumnHeaderHeightProperty)!;
        set => SetValue(ColumnHeaderHeightProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double IndentSize
    {
        get => (double)GetValue(IndentSizeProperty)!;
        set => SetValue(IndentSizeProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public int TreeColumnIndex
    {
        get => (int)GetValue(TreeColumnIndexProperty)!;
        set => SetValue(TreeColumnIndexProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty)!;
        set => SetValue(IsReadOnlyProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Brush? AlternatingRowBackground
    {
        get => (Brush?)GetValue(AlternatingRowBackgroundProperty);
        set => SetValue(AlternatingRowBackgroundProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Brush? RowBackground
    {
        get => (Brush?)GetValue(RowBackgroundProperty);
        set => SetValue(RowBackgroundProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? HorizontalGridLinesBrush
    {
        get => (Brush?)GetValue(HorizontalGridLinesBrushProperty);
        set => SetValue(HorizontalGridLinesBrushProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
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
    private const double ExpanderSize = 16.0;
    private const double ExpandArrowDurationMs = 260;
    private const double CollapseArrowDurationMs = 180;
    private const string ExpanderArrowPathData = "M 841.90,426.67 L 273.07,0 A 170.67,170.67,0,0,0,0,136.53 V 989.87 A 170.67,170.67,0,0,0,273.07,1126.40 L 841.90,699.73 A 170.67,170.67,0,0,0,841.90,426.67 Z";
    private static readonly CubicEase s_arrowExpandEase = new() { EasingMode = EasingMode.EaseOut };
    private static readonly CubicEase s_arrowCollapseEase = new() { EasingMode = EasingMode.EaseInOut };
    private const double ScrollBarReservedWidth = 12.0;

    private readonly List<TreeDataGridNode> _flattenedNodes = new();
    private readonly List<TreeDataGridNode> _rootNodes = new();
    private readonly List<object> _selectedItems = new();
    private readonly HashSet<object> _selectedItemsLookup = new();

    private StackPanel? _columnHeadersHost;
    private StackPanel? _rowsHost;
    private Border? _columnHeadersBorder;
    private ScrollViewer? _columnHeadersScrollViewer;
    private ScrollViewer? _dataScrollViewer;
    private readonly Dictionary<int, TreeDataGridRow> _realizedRows = new();
    private Border? _topSpacer;
    private Border? _bottomSpacer;
    private int _realizedStartIndex = -1;
    private int _realizedEndIndex = -1;
    private int _realizedColumnStartIndex = -1;
    private int _realizedColumnEndIndex = -1;

    private bool _isSynchronizingSelection;
    private int _selectionAnchorIndex = -1;

    // Drag reorder state
    private Canvas? _dragOverlay;
    private Border? _dragGhost;
    private Border? _dropIndicator;
    private DataGridColumn? _dragColumn;
    private int _dragSourceIndex = -1;
    internal bool _isColumnDragging;
    private bool _isUpdatingColumnWidthFromResize;

    // Tracks nodes whose expander arrow needs to animate after RefreshRows rebuilds the visual tree.
    // Key = node, Value = true means expanding (0→90), false means collapsing (90→0).
    private readonly Dictionary<TreeDataGridNode, bool> _pendingArrowAnimations = new();

    #endregion

    #region Constructor

    public TreeDataGrid()
    {
        Focusable = true;
        Columns = new ObservableCollection<DataGridColumn>();
        Columns.CollectionChanged += OnColumnsCollectionChanged;
        SelectedItems = _selectedItems;

        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
    }

    #endregion

    #region Template

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_dataScrollViewer != null)
        {
            _dataScrollViewer.ScrollChanged -= OnDataScrollViewerScrollChanged;
            _dataScrollViewer.SizeChanged -= OnDataScrollViewerSizeChanged;
        }

        _columnHeadersHost = GetTemplateChild("PART_ColumnHeadersHost") as StackPanel;
        _rowsHost = GetTemplateChild("PART_RowsHost") as StackPanel;
        _columnHeadersBorder = GetTemplateChild("PART_ColumnHeadersBorder") as Border;
        _columnHeadersScrollViewer = GetTemplateChild("PART_ColumnHeadersScrollViewer") as ScrollViewer;
        _dataScrollViewer = GetTemplateChild("PART_DataScrollViewer") as ScrollViewer;
        _dragOverlay = GetTemplateChild("PART_DragOverlay") as Canvas;

        if (_dataScrollViewer != null)
        {
            _dataScrollViewer.ScrollChanged += OnDataScrollViewerScrollChanged;
            _dataScrollViewer.SizeChanged += OnDataScrollViewerSizeChanged;
        }

        UpdateHeadersVisibility();
        RefreshColumnHeaders();
        RefreshRows();
    }

    private void OnDataScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        SyncColumnHeadersHorizontalScroll();

        if (EnableRowVirtualization || EnableColumnVirtualization)
        {
            UpdateRealizedRows();
        }
    }

    private void OnDataScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        SyncColumnHeadersHorizontalScroll();
    }

    private void SyncColumnHeadersHorizontalScroll()
    {
        if (_dataScrollViewer == null) return;

        _columnHeadersScrollViewer?.ScrollToHorizontalOffset(_dataScrollViewer.HorizontalOffset);

        if (_columnHeadersBorder != null)
        {
            var needsVerticalScrollBar = _dataScrollViewer.ScrollableHeight > 0;
            var rightMargin = needsVerticalScrollBar ? ScrollBarReservedWidth : 0.0;
            _columnHeadersBorder.Margin = new Thickness(0, 0, rightMargin, 0);
        }
    }

    #endregion

    #region Tree Flattening

    /// <summary>
    /// Rebuilds the entire flattened node list from the current ItemsSource.
    /// </summary>
    private void BuildFlattenedList()
    {
        _flattenedNodes.Clear();
        _rootNodes.Clear();

        if (ItemsSource == null) return;

        foreach (var item in ItemsSource)
        {
            var node = new TreeDataGridNode(item, 0, null);
            node.HasChildren = ResolveHasChildren(node);
            _rootNodes.Add(node);
            _flattenedNodes.Add(node);
        }
    }

    /// <summary>
    /// Expands or collapses a node, updating the flattened list.
    /// </summary>
    internal void ToggleExpand(TreeDataGridNode node)
    {
        if (node.IsExpanded)
        {
            CollapseNode(node);
        }
        else
        {
            ExpandNode(node);
        }
    }

    private void ExpandNode(TreeDataGridNode node)
    {
        if (!node.HasChildren) return;

        // Fire cancelable NodeExpanding event
        var expandingArgs = new TreeDataGridNodeExpandingEventArgs(NodeExpandingEvent, node.Item, node.Level);
        RaiseEvent(expandingArgs);
        if (expandingArgs.Cancel) return;

        // Lazily resolve children
        if (node.Children == null)
        {
            var childItems = ResolveChildren(node.Item);
            if (childItems != null)
            {
                node.Children = new List<TreeDataGridNode>();
                foreach (var childItem in childItems)
                {
                    var childNode = new TreeDataGridNode(childItem, node.Level + 1, node);
                    childNode.HasChildren = ResolveHasChildren(childNode);
                    node.Children.Add(childNode);
                }
            }
        }

        if (node.Children == null || node.Children.Count == 0)
        {
            node.HasChildren = false;
            RefreshRows();
            return;
        }

        node.IsExpanded = true;

        var index = _flattenedNodes.IndexOf(node);
        if (index < 0) return;

        var insertIndex = index + 1;
        InsertVisibleDescendants(node, ref insertIndex);

        RefreshRows();

        RaiseEvent(new TreeDataGridNodeEventArgs(NodeExpandedEvent, node.Item, node.Level));
    }

    private void InsertVisibleDescendants(TreeDataGridNode node, ref int insertIndex)
    {
        if (node.Children == null) return;

        foreach (var child in node.Children)
        {
            _flattenedNodes.Insert(insertIndex++, child);
            if (child.IsExpanded && child.Children != null)
            {
                InsertVisibleDescendants(child, ref insertIndex);
            }
        }
    }

    private void CollapseNode(TreeDataGridNode node)
    {
        if (!node.IsExpanded) return;

        node.IsExpanded = false;

        var index = _flattenedNodes.IndexOf(node);
        if (index < 0) return;

        var removeCount = CountVisibleDescendants(node);
        if (removeCount > 0)
        {
            _flattenedNodes.RemoveRange(index + 1, removeCount);
        }

        RefreshRows();

        RaiseEvent(new TreeDataGridNodeEventArgs(NodeCollapsedEvent, node.Item, node.Level));
    }

    private static int CountVisibleDescendants(TreeDataGridNode node)
    {
        if (node.Children == null) return 0;

        var count = 0;
        foreach (var child in node.Children)
        {
            count++;
            if (child.IsExpanded)
            {
                count += CountVisibleDescendants(child);
            }
        }
        return count;
    }

    #endregion

    #region Children Resolution

    private IEnumerable? ResolveChildren(object item)
    {
        if (ChildrenSelector != null)
        {
            return ChildrenSelector(item);
        }

        if (ChildrenPropertyPath != null)
        {
            var prop = DataGrid.GetCachedProperty(item.GetType(), ChildrenPropertyPath);
            return prop?.GetValue(item) as IEnumerable;
        }

        return null;
    }

    private bool ResolveHasChildren(TreeDataGridNode node)
    {
        if (HasChildrenSelector != null)
        {
            return HasChildrenSelector(node.Item);
        }

        var children = ResolveChildren(node.Item);
        if (children == null) return false;

        var enumerator = children.GetEnumerator();
        try
        {
            return enumerator.MoveNext();
        }
        finally
        {
            (enumerator as IDisposable)?.Dispose();
        }
    }

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
            if (!IsColumnVisible(column)) continue;

            var header = new DataGridColumnHeader
            {
                Content = column.Header,
                Width = column.ActualWidth,
                Height = double.IsNaN(ColumnHeaderHeight) ? double.NaN : GetEffectiveColumnHeaderHeight(),
                ColumnHost = this,
                Column = column
            };

            header.AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnColumnHeaderClick));
            header.UpdateSortIndicator(column.SortDirection);

            _columnHeadersHost.Children.Add(header);
        }
    }

    private void OnColumnHeaderClick(object sender, MouseButtonEventArgs e)
    {
        if (!CanUserSortColumns) return;

        if (sender is DataGridColumnHeader header && header.Column != null && e.ChangedButton == MouseButton.Left)
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

    private static double GetRenderableColumnWidth(DataGridColumn column) =>
        IsColumnVisible(column) ? Math.Max(1.0, column.ActualWidth) : 0.0;

    private void RefreshRows()
    {
        if (_rowsHost == null) return;

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
        if (_rowsHost == null) return;

        if (_flattenedNodes.Count == 0)
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
            endIndex = Math.Min(_flattenedNodes.Count - 1, firstVisible + visibleCount + cacheCount);
        }
        else
        {
            startIndex = 0;
            endIndex = _flattenedNodes.Count - 1;
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
                row = CreateRow(_flattenedNodes[rowIndex], rowIndex, columnStart, columnEnd, rowHeight);
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
        if (_rowsHost == null) return;

        _topSpacer ??= new Border();
        _bottomSpacer ??= new Border();

        _rowsHost.Children.Clear();

        _rowsHost.Children.BeginBatchUpdate();
        try
        {
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

            var bottomHeight = Math.Max(0, (_flattenedNodes.Count - endIndex - 1) * rowHeight);
            _bottomSpacer.Height = bottomHeight;
            _bottomSpacer.Visibility = bottomHeight > 0 ? Visibility.Visible : Visibility.Collapsed;
            _rowsHost.Children.Add(_bottomSpacer);
        }
        finally
        {
            _rowsHost.Children.EndBatchUpdate();
        }
    }

    private (int start, int end) GetVisibleColumnRange()
    {
        if (Columns.Count == 0) return (-1, -1);

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

        if (firstVisibleColumn == -1) return (-1, -1);

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
            if (columnWidth <= 0) continue;

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

    private TreeDataGridRow CreateRow(TreeDataGridNode node, int rowIndex, int columnStart, int columnEnd, double rowHeight)
    {
        var row = new TreeDataGridRow
        {
            DataItem = node.Item,
            RowIndex = rowIndex,
            Height = double.IsNaN(RowHeight) ? double.NaN : rowHeight,
            IsSelected = IsItemSelected(node.Item),
            ParentTreeDataGrid = this,
            Node = node,
            Level = node.Level,
            IsNodeExpanded = node.IsExpanded,
            HasChildren = node.HasChildren,
            VisibleColumnStart = columnStart,
            VisibleColumnEnd = columnEnd
        };

        if (RowBackground != null)
        {
            row.Background = RowBackground;
        }

        if (rowIndex % 2 == 1 && AlternatingRowBackground != null)
        {
            row.AlternatingBackground = AlternatingRowBackground;
        }

        if (columnStart < 0 || columnEnd < 0 || Columns.Count == 0)
        {
            row.AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnRowMouseDown));
            return row;
        }

        var treeColIndex = TreeColumnIndex;

        for (var colIndex = columnStart; colIndex <= columnEnd && colIndex < Columns.Count; colIndex++)
        {
            var column = Columns[colIndex];
            if (!IsColumnVisible(column)) continue;

            var cell = new DataGridCell
            {
                Width = column.ActualWidth,
                Column = column
            };

            var displayElement = column.GenerateElement(cell, node.Item);

            if (colIndex == treeColIndex)
            {
                cell.Content = WrapWithTreeIndentation(displayElement, node);
            }
            else
            {
                cell.Content = displayElement;
            }

            row.Cells.Add(cell);
            row.CellsByColumn[colIndex] = cell;
        }

        row.AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnRowMouseDown));

        return row;
    }

    /// <summary>
    /// Wraps cell content with tree indentation spacer and expand/collapse toggle.
    /// </summary>
    private FrameworkElement WrapWithTreeIndentation(FrameworkElement? cellContent, TreeDataGridNode node)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(node.Level * IndentSize) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ExpanderSize, GridUnitType.Pixel) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Indent spacer
        var indent = new Border();
        Grid.SetColumn(indent, 0);
        grid.Children.Add(indent);

        // Expander toggle
        if (node.HasChildren)
        {
            // If there's a pending animation for this node, start from the pre-toggle angle
            var hasPendingAnim = _pendingArrowAnimations.TryGetValue(node, out var pendingExpanding);
            var initialAngle = hasPendingAnim
                ? (pendingExpanding ? 0.0 : 90.0)
                : (node.IsExpanded ? 90.0 : 0.0);

            var expanderArrow = new Shapes.Path
            {
                Data = ExpanderArrowPathData,
                Fill = TryFindResource("TextSecondary") as Brush ?? Brushes.Gray,
                Width = 8,
                Height = 8,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform { Angle = initialAngle }
            };

            if (hasPendingAnim)
            {
                _pendingArrowAnimations.Remove(node);
                AnimateExpanderArrow(expanderArrow, pendingExpanding);
            }

            var expanderBorder = new Border
            {
                Width = ExpanderSize,
                Height = ExpanderSize,
                Background = Brushes.Transparent,
                Child = expanderArrow
            };

            expanderBorder.AddHandler(MouseDownEvent, new MouseButtonEventHandler((s, e) =>
            {
                _pendingArrowAnimations[node] = !node.IsExpanded;
                ToggleExpand(node);
                e.Handled = true;
            }));

            Grid.SetColumn(expanderBorder, 1);
            grid.Children.Add(expanderBorder);
        }

        // Cell content
        if (cellContent != null)
        {
            Grid.SetColumn(cellContent, 2);
            grid.Children.Add(cellContent);
        }

        return grid;
    }

    /// <summary>
    /// Animates the expander arrow rotation with non-linear easing.
    /// </summary>
    private void AnimateExpanderArrow(Shapes.Path arrow, bool expanding)
    {
        var fromAngle = expanding ? 0.0 : 90.0;
        var toAngle = expanding ? 90.0 : 0.0;
        var durationMs = expanding ? ExpandArrowDurationMs : CollapseArrowDurationMs;
        var ease = expanding ? s_arrowExpandEase : s_arrowCollapseEase;
        var startTick = Environment.TickCount64;

        var timer = new Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(1, CompositionTarget.FrameIntervalMs))
        };

        timer.Tick += (_, _) =>
        {
            var elapsed = Environment.TickCount64 - startTick;
            var progress = Math.Clamp(elapsed / durationMs, 0.0, 1.0);
            var easedProgress = ease.Ease(progress);
            var angle = fromAngle + (toAngle - fromAngle) * easedProgress;

            if (arrow.RenderTransform is RotateTransform rt)
            {
                rt.Angle = angle;
            }
            else
            {
                arrow.RenderTransform = new RotateTransform { Angle = angle };
            }

            arrow.InvalidateVisual();

            if (progress >= 1.0)
            {
                timer.Stop();
            }
        };

        timer.Start();
    }

    #endregion

    #region Selection

    private void ClearSelection()
    {
        _selectedItems.Clear();
        _selectedItemsLookup.Clear();
    }

    private void AddToSelection(object item)
    {
        _selectedItems.Add(item);
        _selectedItemsLookup.Add(item);
    }

    private void RemoveFromSelection(object item)
    {
        _selectedItems.Remove(item);
        _selectedItemsLookup.Remove(item);
    }

    private bool IsItemSelected(object item) => _selectedItemsLookup.Contains(item);

    private void UpdateSelectionPropertiesFromSelectedItems(int preferredIndex = -1)
    {
        object? selectedItem = null;
        var selectedIndex = -1;

        if (_selectedItems.Count > 0)
        {
            if (preferredIndex >= 0 && preferredIndex < _flattenedNodes.Count)
            {
                var preferredItem = _flattenedNodes[preferredIndex].Item;
                if (IsItemSelected(preferredItem))
                {
                    selectedItem = preferredItem;
                    selectedIndex = preferredIndex;
                }
            }

            if (selectedItem == null)
            {
                selectedItem = _selectedItems[0];
                selectedIndex = FindNodeIndex(selectedItem);
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

    private int FindNodeIndex(object item)
    {
        for (var i = 0; i < _flattenedNodes.Count; i++)
        {
            if (Equals(_flattenedNodes[i].Item, item))
                return i;
        }
        return -1;
    }

    private void RaiseSelectionChangedIfNeeded(IList<object> oldSelection)
    {
        var removed = oldSelection.Where(item => !IsItemSelected(item)).ToArray();
        var added = _selectedItems.Where(item => !oldSelection.Contains(item)).ToArray();
        if (removed.Length == 0 && added.Length == 0) return;

        RaiseEvent(new SelectionChangedEventArgs(SelectionChangedEvent, removed, added));
    }

    private void OnRowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        if (sender is TreeDataGridRow row && e.ChangedButton == MouseButton.Left)
        {
            Focus();
            SelectRow(row.RowIndex, e.KeyboardModifiers);
            e.Handled = true;
        }
    }

    private void SelectRow(int rowIndex, ModifierKeys modifiers)
    {
        if (rowIndex < 0 || rowIndex >= _flattenedNodes.Count) return;

        var item = _flattenedNodes[rowIndex].Item;
        var oldSelectedItems = _selectedItems.ToArray();

        if (SelectionMode == DataGridSelectionMode.Single)
        {
            ClearSelection();
            AddToSelection(item);
            _selectionAnchorIndex = rowIndex;
        }
        else if (SelectionMode == DataGridSelectionMode.Extended)
        {
            if (modifiers.HasFlag(ModifierKeys.Control))
            {
                if (IsItemSelected(item))
                    RemoveFromSelection(item);
                else
                    AddToSelection(item);
                _selectionAnchorIndex = rowIndex;
            }
            else if (modifiers.HasFlag(ModifierKeys.Shift))
            {
                var anchor = _selectionAnchorIndex >= 0 ? _selectionAnchorIndex : SelectedIndex;
                if (anchor >= 0)
                {
                    var start = Math.Min(anchor, rowIndex);
                    var end = Math.Max(anchor, rowIndex);
                    ClearSelection();
                    for (var i = start; i <= end; i++)
                        AddToSelection(_flattenedNodes[i].Item);
                }
            }
            else
            {
                ClearSelection();
                AddToSelection(item);
                _selectionAnchorIndex = rowIndex;
            }
        }

        UpdateSelectionPropertiesFromSelectedItems(rowIndex);
        UpdateRowSelectionVisuals();
        RaiseSelectionChangedIfNeeded(oldSelectedItems);
    }

    private void UpdateRowSelectionVisuals()
    {
        foreach (var row in _realizedRows.Values)
        {
            row.IsSelected = row.DataItem != null && IsItemSelected(row.DataItem);
        }
    }

    public void SelectAll()
    {
        if (SelectionMode != DataGridSelectionMode.Extended) return;

        var oldSelectedItems = _selectedItems.ToArray();
        ClearSelection();
        foreach (var node in _flattenedNodes)
            AddToSelection(node.Item);
        UpdateSelectionPropertiesFromSelectedItems(preferredIndex: SelectedIndex);
        UpdateRowSelectionVisuals();
        RaiseSelectionChangedIfNeeded(oldSelectedItems);
    }

    public void UnselectAll()
    {
        var oldSelectedItems = _selectedItems.ToArray();
        ClearSelection();
        UpdateSelectionPropertiesFromSelectedItems();
        UpdateRowSelectionVisuals();
        RaiseSelectionChangedIfNeeded(oldSelectedItems);
    }

    #endregion

    #region Sorting

    private void SortByColumn(DataGridColumn column)
    {
        if (!column.CanUserSort) return;

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
            var path = boundColumn.Binding.Path.Path;
            SortNodesRecursive(_rootNodes, path, newDirection);
        }

        // Rebuild flattened list from sorted tree
        _flattenedNodes.Clear();
        foreach (var rootNode in _rootNodes)
        {
            _flattenedNodes.Add(rootNode);
            if (rootNode.IsExpanded)
            {
                var insertIndex = _flattenedNodes.Count;
                InsertVisibleDescendants(rootNode, ref insertIndex);
            }
        }

        UpdateColumnHeaderSortIndicators();

        if (SelectedItem != null)
        {
            var newIndex = FindNodeIndex(SelectedItem);
            if (newIndex >= 0 && newIndex != SelectedIndex)
            {
                _isSynchronizingSelection = true;
                try
                {
                    SetValue(SelectedIndexProperty, newIndex);
                }
                finally
                {
                    _isSynchronizingSelection = false;
                }
            }
        }

        RefreshRows();
    }

    private static void SortNodesRecursive(List<TreeDataGridNode> nodes, string path, ListSortDirection direction)
    {
        nodes.Sort((a, b) =>
        {
            var valueA = GetPropertyValue(a.Item, path);
            var valueB = GetPropertyValue(b.Item, path);
            var result = Comparer.Default.Compare(valueA, valueB);
            return direction == ListSortDirection.Descending ? -result : result;
        });

        foreach (var node in nodes)
        {
            if (node.Children != null && node.Children.Count > 0)
            {
                SortNodesRecursive(node.Children, path, direction);
            }
        }
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
            var prop = DataGrid.GetCachedProperty(current.GetType(), part);
            current = prop?.GetValue(current);
        }
        return current;
    }

    #endregion

    #region Input Handling

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (!IsEnabled) return;

        switch (e.Key)
        {
            case Key.Up:
                if (SelectedIndex > 0)
                {
                    SelectedIndex--;
                    ScrollSelectedIntoView();
                    e.Handled = true;
                }
                break;
            case Key.Down:
                if (SelectedIndex < _flattenedNodes.Count - 1)
                {
                    SelectedIndex++;
                    ScrollSelectedIntoView();
                    e.Handled = true;
                }
                break;
            case Key.Right:
                HandleExpandKey();
                e.Handled = true;
                break;
            case Key.Left:
                HandleCollapseKey();
                e.Handled = true;
                break;
            case Key.Home:
                if (_flattenedNodes.Count > 0)
                {
                    SelectedIndex = 0;
                    ScrollSelectedIntoView();
                    e.Handled = true;
                }
                break;
            case Key.End:
                if (_flattenedNodes.Count > 0)
                {
                    SelectedIndex = _flattenedNodes.Count - 1;
                    ScrollSelectedIntoView();
                    e.Handled = true;
                }
                break;
            case Key.A when e.KeyboardModifiers.HasFlag(ModifierKeys.Control):
                if (SelectionMode == DataGridSelectionMode.Extended)
                {
                    SelectAll();
                    e.Handled = true;
                }
                break;
        }
    }

    private void HandleExpandKey()
    {
        if (SelectedIndex < 0 || SelectedIndex >= _flattenedNodes.Count) return;

        var node = _flattenedNodes[SelectedIndex];
        if (node.HasChildren && !node.IsExpanded)
        {
            ExpandNode(node);
        }
        else if (node.IsExpanded && SelectedIndex < _flattenedNodes.Count - 1)
        {
            // Move to first child
            SelectedIndex++;
            ScrollSelectedIntoView();
        }
    }

    private void HandleCollapseKey()
    {
        if (SelectedIndex < 0 || SelectedIndex >= _flattenedNodes.Count) return;

        var node = _flattenedNodes[SelectedIndex];
        if (node.IsExpanded)
        {
            CollapseNode(node);
        }
        else if (node.Parent != null)
        {
            // Move to parent
            var parentIndex = _flattenedNodes.IndexOf(node.Parent);
            if (parentIndex >= 0)
            {
                SelectedIndex = parentIndex;
                ScrollSelectedIntoView();
            }
        }
    }

    /// <summary>
    /// Scrolls the currently selected row into the viewport.
    /// </summary>
    public void ScrollSelectedIntoView()
    {
        ScrollIntoView(SelectedIndex);
    }

    /// <summary>
    /// Scrolls the row at the given flattened index into the viewport.
    /// </summary>
    public void ScrollIntoView(int rowIndex)
    {
        if (_dataScrollViewer == null || rowIndex < 0 || rowIndex >= _flattenedNodes.Count) return;

        var rowHeight = GetEffectiveRowHeight();
        var rowTop = rowIndex * rowHeight;
        var rowBottom = rowTop + rowHeight;
        var viewportTop = _dataScrollViewer.VerticalOffset;
        var viewportBottom = viewportTop + _dataScrollViewer.ViewportHeight;

        if (rowTop < viewportTop)
        {
            _dataScrollViewer.ScrollToVerticalOffset(rowTop);
        }
        else if (rowBottom > viewportBottom)
        {
            _dataScrollViewer.ScrollToVerticalOffset(Math.Max(0, rowBottom - _dataScrollViewer.ViewportHeight));
        }
    }

    /// <summary>
    /// Scrolls the row containing the specified data item into the viewport.
    /// </summary>
    public void ScrollIntoView(object item)
    {
        var index = FindNodeIndex(item);
        if (index >= 0)
        {
            ScrollIntoView(index);
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeDataGrid grid)
        {
            if (e.OldValue is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= grid.OnSourceCollectionChanged;
            }

            if (e.NewValue is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += grid.OnSourceCollectionChanged;
            }

            grid.RefreshItemsFromSource();
        }
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // For simplicity, do a full rebuild on any source change.
        // Tree structure makes incremental updates complex.
        RefreshItemsFromSource();
    }

    private void RefreshItemsFromSource()
    {
        var oldSelectedItems = _selectedItems.ToArray();
        BuildFlattenedList();
        ReconcileSelectionWithItems();
        RefreshColumnHeaders();
        RefreshRows();
        InvalidateMeasure();
        RaiseSelectionChangedIfNeeded(oldSelectedItems);
    }

    private void ReconcileSelectionWithItems()
    {
        var itemSet = new HashSet<object>(_flattenedNodes.Select(n => n.Item));
        var toRemove = _selectedItems.Where(item => !itemSet.Contains(item)).ToArray();
        foreach (var item in toRemove)
            RemoveFromSelection(item);

        if (SelectionMode == DataGridSelectionMode.Single && _selectedItems.Count > 1)
        {
            var retained = _selectedItems[0];
            ClearSelection();
            AddToSelection(retained);
        }

        UpdateSelectionPropertiesFromSelectedItems(preferredIndex: SelectedIndex);
    }

    private static void OnChildrenPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeDataGrid grid)
        {
            grid.RefreshItemsFromSource();
        }
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeDataGrid grid && !grid._isSynchronizingSelection)
        {
            var oldSelectedItems = grid._selectedItems.ToArray();
            var newItem = e.NewValue;

            grid._isSynchronizingSelection = true;
            try
            {
                if (newItem != null)
                {
                    var index = grid.FindNodeIndex(newItem);
                    if (index >= 0)
                    {
                        grid.ClearSelection();
                        grid.AddToSelection(newItem);
                        grid.SetValue(SelectedIndexProperty, index);
                    }
                    else
                    {
                        grid.ClearSelection();
                        grid.SetValue(SelectedIndexProperty, -1);
                        grid.SetValue(SelectedItemProperty, null);
                    }
                }
                else
                {
                    grid.ClearSelection();
                    grid.SetValue(SelectedIndexProperty, -1);
                }
            }
            finally
            {
                grid._isSynchronizingSelection = false;
            }

            grid.UpdateRowSelectionVisuals();
            grid.RaiseSelectionChangedIfNeeded(oldSelectedItems);
        }
    }

    private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeDataGrid grid && e.NewValue is int newIndex && !grid._isSynchronizingSelection)
        {
            var oldSelectedItems = grid._selectedItems.ToArray();
            grid._isSynchronizingSelection = true;
            try
            {
                if (newIndex >= 0 && newIndex < grid._flattenedNodes.Count)
                {
                    var item = grid._flattenedNodes[newIndex].Item;
                    grid.ClearSelection();
                    grid.AddToSelection(item);
                    grid.SetValue(SelectedItemProperty, item);
                }
                else
                {
                    grid.ClearSelection();
                    grid.SetValue(SelectedItemProperty, null);
                    grid.SetValue(SelectedIndexProperty, -1);
                }
            }
            finally
            {
                grid._isSynchronizingSelection = false;
            }

            grid.UpdateRowSelectionVisuals();
            grid.RaiseSelectionChangedIfNeeded(oldSelectedItems);
        }
    }

    private static void OnSelectionModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeDataGrid grid &&
            e.NewValue is DataGridSelectionMode newMode &&
            newMode == DataGridSelectionMode.Single &&
            grid._selectedItems.Count > 1)
        {
            var oldSelectedItems = grid._selectedItems.ToArray();
            var retainedItem = grid.SelectedItem ?? grid._selectedItems[0];
            grid.ClearSelection();
            grid.AddToSelection(retainedItem);
            grid.UpdateSelectionPropertiesFromSelectedItems(grid.FindNodeIndex(retainedItem));
            grid.UpdateRowSelectionVisuals();
            grid.RaiseSelectionChangedIfNeeded(oldSelectedItems);
        }
    }

    private static void OnVirtualizationSettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeDataGrid grid)
        {
            grid.RefreshRows();
        }
    }

    private static void OnHeadersVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeDataGrid grid)
        {
            grid.UpdateHeadersVisibility();
            grid.InvalidateMeasure();
        }
    }

    private static void OnRowHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeDataGrid grid)
        {
            grid.RefreshRows();
            grid.InvalidateMeasure();
        }
    }

    private static void OnColumnHeaderHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeDataGrid grid)
        {
            grid.RefreshColumnHeaders();
            grid.InvalidateMeasure();
        }
    }

    private static void OnIndentSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeDataGrid grid)
        {
            grid.RefreshRows();
            grid.InvalidateMeasure();
        }
    }

    private static void OnTreeColumnIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeDataGrid grid)
        {
            grid.RefreshRows();
            grid.InvalidateMeasure();
        }
    }

    #endregion

    #region Columns Collection Changed

    private void OnColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshColumnHeaders();
        RefreshRows();
        InvalidateMeasure();
    }

    #endregion

    #region Column Resizing

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

    #region Column Drag Reorder

    internal void StartColumnDrag(DataGridColumnHeader sourceHeader, DataGridColumn column)
    {
        if (_dragOverlay == null || _columnHeadersHost == null || !CanUserReorderColumns || !column.CanUserReorder)
            return;

        _dragColumn = column;
        _dragSourceIndex = Columns.IndexOf(column);
        _isColumnDragging = true;

        var headerHeight = GetEffectiveColumnHeaderHeight();
        _dragGhost = new Border
        {
            Width = column.ActualWidth,
            Height = headerHeight,
            Background = new SolidColorBrush(Color.FromArgb(180, 60, 60, 60)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(120, 120, 120)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Opacity = 0.85,
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = column.Header?.ToString() ?? "",
                Foreground = Media.Brushes.White,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            }
        };

        var accentBrush = TryFindResource("AccentBrush") as Brush ?? new SolidColorBrush(ThemeColors.Accent);
        _dropIndicator = new Border
        {
            Width = 2,
            Height = headerHeight + 8,
            Background = accentBrush,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

        _dragOverlay.Children.Clear();
        _dragOverlay.Children.Add(_dropIndicator);
        _dragOverlay.Children.Add(_dragGhost);
        _dragOverlay.Visibility = Visibility.Visible;
    }

    internal void UpdateColumnDrag(Point positionInDataGrid)
    {
        if (!_isColumnDragging || _dragGhost == null || _dropIndicator == null || _columnHeadersHost == null)
            return;

        var ghostX = positionInDataGrid.X - _dragGhost.Width / 2;
        Canvas.SetLeft(_dragGhost, ghostX);
        Canvas.SetTop(_dragGhost, 0.0);

        var (targetIndex, indicatorX) = GetDropTargetIndex(positionInDataGrid.X);
        if (targetIndex >= 0 && targetIndex != _dragSourceIndex && targetIndex != _dragSourceIndex + 1)
        {
            Canvas.SetLeft(_dropIndicator, indicatorX - 1);
            Canvas.SetTop(_dropIndicator, 0);
            _dropIndicator.Visibility = Visibility.Visible;
        }
        else
        {
            _dropIndicator.Visibility = Visibility.Collapsed;
        }
    }

    internal void EndColumnDrag(Point positionInDataGrid)
    {
        if (!_isColumnDragging || _dragColumn == null)
        {
            CancelColumnDrag();
            return;
        }

        var (targetIndex, _) = GetDropTargetIndex(positionInDataGrid.X);
        var sourceIndex = _dragSourceIndex;

        CleanupDragVisuals();

        if (targetIndex >= 0 && targetIndex != sourceIndex && targetIndex != sourceIndex + 1)
        {
            var moveTarget = targetIndex > sourceIndex ? targetIndex - 1 : targetIndex;
            moveTarget = Math.Clamp(moveTarget, 0, Columns.Count - 1);
            if (moveTarget != sourceIndex)
            {
                Columns.Move(sourceIndex, moveTarget);
            }
        }
    }

    internal void CancelColumnDrag()
    {
        CleanupDragVisuals();
    }

    private void CleanupDragVisuals()
    {
        _isColumnDragging = false;
        _dragColumn = null;
        _dragSourceIndex = -1;

        if (_dragOverlay != null)
        {
            _dragOverlay.Children.Clear();
            _dragOverlay.Visibility = Visibility.Collapsed;
        }

        _dragGhost = null;
        _dropIndicator = null;
    }

    private (int targetIndex, double indicatorX) GetDropTargetIndex(double x)
    {
        if (_columnHeadersHost == null || Columns.Count == 0)
            return (-1, 0);

        var scrollOffset = _dataScrollViewer?.HorizontalOffset ?? 0;
        var adjustedX = x + scrollOffset;

        var cumulative = 0.0;
        for (var i = 0; i < Columns.Count; i++)
        {
            var colWidth = GetRenderableColumnWidth(Columns[i]);
            var colMid = cumulative + colWidth / 2;

            if (adjustedX < colMid)
            {
                return (i, cumulative - scrollOffset);
            }

            cumulative += colWidth;
        }

        return (Columns.Count, cumulative - scrollOffset);
    }

    #endregion

    #region Public API

    /// <summary>
    /// Expands all nodes in the tree.
    /// </summary>
    public void ExpandAll()
    {
        ExpandAllRecursive(_rootNodes);
        _flattenedNodes.Clear();
        RebuildFlattenedFromTree();
        RefreshRows();
    }

    /// <summary>
    /// Collapses all nodes in the tree.
    /// </summary>
    public void CollapseAll()
    {
        CollapseAllRecursive(_rootNodes);
        _flattenedNodes.Clear();
        RebuildFlattenedFromTree();
        RefreshRows();
    }

    private void ExpandAllRecursive(List<TreeDataGridNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.HasChildren)
            {
                if (node.Children == null)
                {
                    var childItems = ResolveChildren(node.Item);
                    if (childItems != null)
                    {
                        node.Children = new List<TreeDataGridNode>();
                        foreach (var childItem in childItems)
                        {
                            var childNode = new TreeDataGridNode(childItem, node.Level + 1, node);
                            childNode.HasChildren = ResolveHasChildren(childNode);
                            node.Children.Add(childNode);
                        }
                    }
                }

                node.IsExpanded = true;
                if (node.Children != null)
                {
                    ExpandAllRecursive(node.Children);
                }
            }
        }
    }

    private static void CollapseAllRecursive(List<TreeDataGridNode> nodes)
    {
        foreach (var node in nodes)
        {
            node.IsExpanded = false;
            if (node.Children != null)
            {
                CollapseAllRecursive(node.Children);
            }
        }
    }

    private void RebuildFlattenedFromTree()
    {
        foreach (var rootNode in _rootNodes)
        {
            _flattenedNodes.Add(rootNode);
            if (rootNode.IsExpanded)
            {
                AddExpandedDescendantsToFlattened(rootNode);
            }
        }
    }

    private void AddExpandedDescendantsToFlattened(TreeDataGridNode node)
    {
        if (node.Children == null) return;

        foreach (var child in node.Children)
        {
            _flattenedNodes.Add(child);
            if (child.IsExpanded)
            {
                AddExpandedDescendantsToFlattened(child);
            }
        }
    }

    /// <summary>
    /// Gets the flattened node count (visible rows).
    /// </summary>
    public int FlattenedCount => _flattenedNodes.Count;

    /// <summary>
    /// Gets the data item at the specified flattened index.
    /// </summary>
    public object? GetItemAt(int flattenedIndex)
    {
        if (flattenedIndex < 0 || flattenedIndex >= _flattenedNodes.Count) return null;
        return _flattenedNodes[flattenedIndex].Item;
    }

    /// <summary>
    /// Checks whether the item at the specified flattened index is expanded.
    /// </summary>
    public bool IsExpanded(int flattenedIndex)
    {
        if (flattenedIndex < 0 || flattenedIndex >= _flattenedNodes.Count) return false;
        return _flattenedNodes[flattenedIndex].IsExpanded;
    }

    /// <summary>
    /// Gets the depth level of the item at the specified flattened index.
    /// </summary>
    public int GetLevel(int flattenedIndex)
    {
        if (flattenedIndex < 0 || flattenedIndex >= _flattenedNodes.Count) return -1;
        return _flattenedNodes[flattenedIndex].Level;
    }

    #endregion
}

#region TreeDataGridNode

/// <summary>
/// Represents a node in the flattened tree model. Not a UI element.
/// </summary>
internal sealed class TreeDataGridNode
{
    public TreeDataGridNode(object item, int level, TreeDataGridNode? parent)
    {
        Item = item;
        Level = level;
        Parent = parent;
    }

    public object Item { get; }
    public int Level { get; }
    public TreeDataGridNode? Parent { get; }
    public bool IsExpanded { get; set; }
    public bool HasChildren { get; set; }
    public List<TreeDataGridNode>? Children { get; set; }
}

#endregion

#region TreeDataGridRow

/// <summary>
/// Represents a row in a TreeDataGrid.
/// </summary>
public class TreeDataGridRow : Control
{
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(TreeDataGridRow),
            new PropertyMetadata(false, OnIsSelectedChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty)!;
        set => SetValue(IsSelectedProperty, value);
    }

    internal object? DataItem { get; set; }
    internal int RowIndex { get; set; }
    internal int Level { get; set; }
    internal bool IsNodeExpanded { get; set; }
    internal bool HasChildren { get; set; }
    internal TreeDataGrid? ParentTreeDataGrid { get; set; }
    internal TreeDataGridNode? Node { get; set; }
    internal Brush? AlternatingBackground { get; set; }
    internal int VisibleColumnStart { get; set; } = -1;
    internal int VisibleColumnEnd { get; set; } = -1;
    internal List<DataGridCell> Cells { get; } = new();
    internal Dictionary<int, DataGridCell> CellsByColumn { get; } = new();

    private StackPanel? _cellsPanel;

    public TreeDataGridRow()
    {
        Focusable = false;
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _cellsPanel = GetTemplateChild("PART_CellsPanel") as StackPanel;

        if (_cellsPanel != null && Cells.Count > 0)
        {
            _cellsPanel.Children.BeginBatchUpdate();
            try
            {
                foreach (var cell in Cells)
                {
                    _cellsPanel.Children.Add(cell);
                }
            }
            finally
            {
                _cellsPanel.Children.EndBatchUpdate();
            }
        }

        RestoreNonSelectedBackground();
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeDataGridRow row && e.NewValue is false)
        {
            row.RestoreNonSelectedBackground();
        }
    }

    private void RestoreNonSelectedBackground()
    {
        if (!IsSelected)
        {
            if (AlternatingBackground != null)
            {
                Background = AlternatingBackground;
            }
            else
            {
                Background = ParentTreeDataGrid?.RowBackground;
            }
        }
    }
}

#endregion

#region Event Args

/// <summary>
/// Event data for TreeDataGrid node events.
/// </summary>
public class TreeDataGridNodeEventArgs : RoutedEventArgs
{
    public TreeDataGridNodeEventArgs(RoutedEvent routedEvent, object item, int level)
        : base(routedEvent)
    {
        Item = item;
        Level = level;
    }

    public object Item { get; }
    public int Level { get; }
}

/// <summary>
/// Event data for the NodeExpanding event, which is cancelable.
/// </summary>
public class TreeDataGridNodeExpandingEventArgs : TreeDataGridNodeEventArgs
{
    public TreeDataGridNodeExpandingEventArgs(RoutedEvent routedEvent, object item, int level)
        : base(routedEvent, item, level)
    {
    }

    public bool Cancel { get; set; }
}

#endregion
