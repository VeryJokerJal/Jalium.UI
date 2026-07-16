using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using DataGridColumnHeader = Jalium.UI.Controls.Primitives.DataGridColumnHeader;
using ListSortDirection = System.ComponentModel.ListSortDirection;
using WpfClipboard = global::Jalium.UI.Clipboard;

namespace Jalium.UI.Controls;

/// <summary>
/// Internal interface for controls that host DataGridColumnHeaders (DataGrid, TreeDataGrid).
/// Allows column headers to call back into their parent for resize, drag reorder, etc.
/// </summary>
internal interface IColumnHeaderHost
{
    bool CanUserResizeColumns { get; }
    bool CanUserReorderColumns { get; }
    bool IsColumnDragging { get; }
    void ResizeColumn(DataGridColumn column, double newWidth);
    void StartColumnDrag(DataGridColumnHeader sourceHeader, DataGridColumn column);
    void UpdateColumnDrag(Point positionInHost);
    void EndColumnDrag(Point positionInHost);
    void CancelColumnDrag();
}

/// <summary>
/// Internal WPF-compatible contract used by elements that expose their owning
/// DataGrid column without adding another public compatibility API.
/// </summary>
internal interface IProvideDataGridColumn
{
    DataGridColumn? Column { get; }
}

/// <summary>
/// Represents a control that displays data in a customizable grid using TemplatedControl pattern.
/// </summary>
public class DataGrid : MultiSelector, IColumnHeaderHost
{
    /// <summary>Begins editing the current cell.</summary>
    public static readonly RoutedCommand BeginEditCommand =
        new(nameof(BeginEditCommand), typeof(DataGrid));

    /// <summary>Cancels the current cell or row edit.</summary>
    public static readonly RoutedCommand CancelEditCommand =
        new(nameof(CancelEditCommand), typeof(DataGrid));

    /// <summary>Commits the current cell or row edit.</summary>
    public static readonly RoutedCommand CommitEditCommand =
        new(nameof(CommitEditCommand), typeof(DataGrid));

    private static readonly RoutedUICommand s_deleteCommand =
        new("Delete", nameof(DeleteCommand), typeof(DataGrid));
    private static readonly RoutedUICommand s_selectAllCommand =
        new("Select All", nameof(SelectAllCommand), typeof(DataGrid));
    private static readonly ComponentResourceKey s_focusBorderBrushKey =
        new(typeof(DataGrid), nameof(FocusBorderBrushKey));
    private static readonly IValueConverter s_headersVisibilityConverter =
        new DataGridHeadersVisibilityConverter();
    private static readonly IValueConverter s_rowDetailsScrollingConverter =
        new RowDetailsScrollingOrientationConverter();

    static DataGrid()
    {
        Selector.SelectedItemProperty.OverrideMetadata(
            typeof(DataGrid),
            new PropertyMetadata(null, OnSelectedItemChanged));
        Selector.SelectedIndexProperty.OverrideMetadata(
            typeof(DataGrid),
            new PropertyMetadata(-1, OnSelectedIndexChanged));
    }

    /// <summary>Gets the command that deletes the selected rows.</summary>
    public static RoutedUICommand DeleteCommand => s_deleteCommand;

    /// <summary>Gets the command that selects every row or cell.</summary>
    public static RoutedUICommand SelectAllCommand => s_selectAllCommand;

    /// <summary>Gets the resource key used by the current-cell focus border.</summary>
    public static ComponentResourceKey FocusBorderBrushKey => s_focusBorderBrushKey;

    /// <summary>Gets the converter used by the default header templates.</summary>
    public static IValueConverter HeadersVisibilityConverter => s_headersVisibilityConverter;

    /// <summary>Gets the converter used by the default row-details template.</summary>
    public static IValueConverter RowDetailsScrollingConverter => s_rowDetailsScrollingConverter;

    bool IColumnHeaderHost.IsColumnDragging => _isColumnDragging;
    void IColumnHeaderHost.ResizeColumn(DataGridColumn column, double newWidth) => ResizeColumn(column, newWidth);
    void IColumnHeaderHost.StartColumnDrag(DataGridColumnHeader sourceHeader, DataGridColumn column) => StartColumnDrag(sourceHeader, column);
    void IColumnHeaderHost.UpdateColumnDrag(Point positionInHost) => UpdateColumnDrag(positionInHost);
    void IColumnHeaderHost.EndColumnDrag(Point positionInHost) => EndColumnDrag(positionInHost);
    void IColumnHeaderHost.CancelColumnDrag() => CancelColumnDrag();

    /// <inheritdoc />
    protected internal override bool HandlesScrolling => true;

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.DataGridAutomationPeer(this);
    }

    #region Dependency Properties

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CurrentItemProperty =
        DependencyProperty.Register(nameof(CurrentItem), typeof(object), typeof(DataGrid),
            new PropertyMetadata(null, OnCurrentItemChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CurrentColumnProperty =
        DependencyProperty.Register(nameof(CurrentColumn), typeof(DataGridColumn), typeof(DataGrid),
            new PropertyMetadata(null, OnCurrentColumnChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CurrentCellProperty =
        DependencyProperty.Register(nameof(CurrentCell), typeof(DataGridCellInfo), typeof(DataGrid),
            new PropertyMetadata(default(DataGridCellInfo), OnCurrentCellPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty AutoGenerateColumnsProperty =
        DependencyProperty.Register(nameof(AutoGenerateColumns), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(true, OnAutoGenerateColumnsChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanUserSortColumnsProperty =
        DependencyProperty.Register(nameof(CanUserSortColumns), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(true));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanUserResizeColumnsProperty =
        DependencyProperty.Register(nameof(CanUserResizeColumns), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(true));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanUserReorderColumnsProperty =
        DependencyProperty.Register(nameof(CanUserReorderColumns), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(true));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanUserAddRowsProperty =
        DependencyProperty.Register(nameof(CanUserAddRows), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(true, OnCanUserModifyRowsChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanUserDeleteRowsProperty =
        DependencyProperty.Register(nameof(CanUserDeleteRows), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(true, OnCanUserModifyRowsChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty FrozenColumnCountProperty =
        DependencyProperty.Register(nameof(FrozenColumnCount), typeof(int), typeof(DataGrid),
            new PropertyMetadata(0, OnFrozenColumnCountChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty EnableRowVirtualizationProperty =
        DependencyProperty.Register(nameof(EnableRowVirtualization), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(true, OnVirtualizationSettingsChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty EnableColumnVirtualizationProperty =
        DependencyProperty.Register(nameof(EnableColumnVirtualization), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(false, OnVirtualizationSettingsChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectionModeProperty =
        DependencyProperty.Register(nameof(SelectionMode), typeof(DataGridSelectionMode), typeof(DataGrid),
            new PropertyMetadata(DataGridSelectionMode.Extended, OnSelectionModeChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SelectionUnitProperty =
        DependencyProperty.Register(nameof(SelectionUnit), typeof(DataGridSelectionUnit), typeof(DataGrid),
            new PropertyMetadata(DataGridSelectionUnit.FullRow, OnSelectionUnitChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty GridLinesVisibilityProperty =
        DependencyProperty.Register(nameof(GridLinesVisibility), typeof(DataGridGridLinesVisibility), typeof(DataGrid),
            new PropertyMetadata(DataGridGridLinesVisibility.All));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeadersVisibilityProperty =
        DependencyProperty.Register(nameof(HeadersVisibility), typeof(DataGridHeadersVisibility), typeof(DataGrid),
            new PropertyMetadata(DataGridHeadersVisibility.All, OnHeadersVisibilityChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RowHeightProperty =
        DependencyProperty.Register(nameof(RowHeight), typeof(double), typeof(DataGrid),
            new PropertyMetadata(double.NaN, OnRowHeightChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnHeaderHeightProperty =
        DependencyProperty.Register(nameof(ColumnHeaderHeight), typeof(double), typeof(DataGrid),
            new PropertyMetadata(double.NaN, OnColumnHeaderHeightChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RowHeaderWidthProperty =
        DependencyProperty.Register(nameof(RowHeaderWidth), typeof(double), typeof(DataGrid),
            new PropertyMetadata(double.NaN, OnRowHeaderWidthChanged));

    private static readonly DependencyPropertyKey RowHeaderActualWidthPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(RowHeaderActualWidth), typeof(double), typeof(DataGrid),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty RowHeaderActualWidthProperty =
        RowHeaderActualWidthPropertyKey.DependencyProperty;

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(false, OnDataGridIsReadOnlyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RowDetailsTemplateProperty =
        DependencyProperty.Register(nameof(RowDetailsTemplate), typeof(DataTemplate), typeof(DataGrid),
            new PropertyMetadata(null, OnRowDetailsConfigurationChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RowDetailsTemplateSelectorProperty =
        DependencyProperty.Register(nameof(RowDetailsTemplateSelector), typeof(DataTemplateSelector), typeof(DataGrid),
            new PropertyMetadata(null, OnRowDetailsConfigurationChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RowDetailsVisibilityModeProperty =
        DependencyProperty.Register(nameof(RowDetailsVisibilityMode), typeof(DataGridRowDetailsVisibilityMode), typeof(DataGrid),
            new PropertyMetadata(DataGridRowDetailsVisibilityMode.VisibleWhenSelected, OnRowDetailsConfigurationChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty AreRowDetailsFrozenProperty =
        DependencyProperty.Register(nameof(AreRowDetailsFrozen), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(false));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CanUserResizeRowsProperty =
        DependencyProperty.Register(nameof(CanUserResizeRows), typeof(bool), typeof(DataGrid),
            new PropertyMetadata(true));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty AlternatingRowBackgroundProperty =
        DependencyProperty.Register(nameof(AlternatingRowBackground), typeof(Brush), typeof(DataGrid),
            new PropertyMetadata(null));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty RowBackgroundProperty =
        DependencyProperty.Register(nameof(RowBackground), typeof(Brush), typeof(DataGrid),
            new PropertyMetadata(null));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty HorizontalGridLinesBrushProperty =
        DependencyProperty.Register(nameof(HorizontalGridLinesBrush), typeof(Brush), typeof(DataGrid),
            new PropertyMetadata(null));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty VerticalGridLinesBrushProperty =
        DependencyProperty.Register(nameof(VerticalGridLinesBrush), typeof(Brush), typeof(DataGrid),
            new PropertyMetadata(null));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CellStyleProperty =
        DependencyProperty.Register(nameof(CellStyle), typeof(Style), typeof(DataGrid),
            new PropertyMetadata(null, OnCellAppearanceChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ColumnHeaderStyleProperty =
        DependencyProperty.Register(nameof(ColumnHeaderStyle), typeof(Style), typeof(DataGrid),
            new PropertyMetadata(null, OnColumnHeaderAppearanceChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnWidthProperty =
        DependencyProperty.Register(nameof(ColumnWidth), typeof(DataGridLength), typeof(DataGrid),
            new PropertyMetadata(DataGridLength.SizeToHeader, OnColumnSizingChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinColumnWidthProperty =
        DependencyProperty.Register(nameof(MinColumnWidth), typeof(double), typeof(DataGrid),
            new PropertyMetadata(20.0, OnColumnSizingChanged), ValidateMinColumnWidth);

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaxColumnWidthProperty =
        DependencyProperty.Register(nameof(MaxColumnWidth), typeof(double), typeof(DataGrid),
            new PropertyMetadata(double.PositiveInfinity, OnColumnSizingChanged), ValidateMaxColumnWidth);

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinRowHeightProperty =
        DependencyProperty.Register(nameof(MinRowHeight), typeof(double), typeof(DataGrid),
            new PropertyMetadata(0.0, OnRowSizingChanged), ValidateMinRowHeight);

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty =
        ScrollViewer.HorizontalScrollBarVisibilityProperty.AddOwner(
            typeof(DataGrid), new PropertyMetadata(ScrollBarVisibility.Auto, OnScrollBarVisibilityChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty =
        ScrollViewer.VerticalScrollBarVisibilityProperty.AddOwner(
            typeof(DataGrid), new PropertyMetadata(ScrollBarVisibility.Auto, OnScrollBarVisibilityChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty RowStyleProperty =
        DependencyProperty.Register(nameof(RowStyle), typeof(Style), typeof(DataGrid),
            new PropertyMetadata(null, OnRowAppearanceChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty RowStyleSelectorProperty =
        DependencyProperty.Register(nameof(RowStyleSelector), typeof(StyleSelector), typeof(DataGrid),
            new PropertyMetadata(null, OnRowAppearanceChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty RowHeaderStyleProperty =
        DependencyProperty.Register(nameof(RowHeaderStyle), typeof(Style), typeof(DataGrid),
            new PropertyMetadata(null, OnRowAppearanceChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty RowHeaderTemplateProperty =
        DependencyProperty.Register(nameof(RowHeaderTemplate), typeof(DataTemplate), typeof(DataGrid),
            new PropertyMetadata(null, OnRowAppearanceChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty RowHeaderTemplateSelectorProperty =
        DependencyProperty.Register(nameof(RowHeaderTemplateSelector), typeof(DataTemplateSelector), typeof(DataGrid),
            new PropertyMetadata(null, OnRowAppearanceChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty RowValidationErrorTemplateProperty =
        DependencyProperty.Register(nameof(RowValidationErrorTemplate), typeof(ControlTemplate), typeof(DataGrid),
            new PropertyMetadata(null, OnRowValidationErrorTemplateChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ClipboardCopyModeProperty =
        DependencyProperty.Register(nameof(ClipboardCopyMode), typeof(DataGridClipboardCopyMode), typeof(DataGrid),
            new PropertyMetadata(DataGridClipboardCopyMode.ExcludeHeader),
            static value => value is DataGridClipboardCopyMode mode &&
                mode is DataGridClipboardCopyMode.None or
                    DataGridClipboardCopyMode.ExcludeHeader or
                    DataGridClipboardCopyMode.IncludeHeader);

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty DragIndicatorStyleProperty =
        DependencyProperty.Register(nameof(DragIndicatorStyle), typeof(Style), typeof(DataGrid),
            new PropertyMetadata(null));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty DropLocationIndicatorStyleProperty =
        DependencyProperty.Register(nameof(DropLocationIndicatorStyle), typeof(Style), typeof(DataGrid),
            new PropertyMetadata(null));

    private static readonly DependencyPropertyKey CellsPanelHorizontalOffsetPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CellsPanelHorizontalOffset), typeof(double), typeof(DataGrid),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty CellsPanelHorizontalOffsetProperty =
        CellsPanelHorizontalOffsetPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey NonFrozenColumnsViewportHorizontalOffsetPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(NonFrozenColumnsViewportHorizontalOffset), typeof(double), typeof(DataGrid),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty NonFrozenColumnsViewportHorizontalOffsetProperty =
        NonFrozenColumnsViewportHorizontalOffsetPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey NewItemMarginPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(NewItemMargin), typeof(Thickness), typeof(DataGrid),
            new PropertyMetadata(new Thickness(0)));

    public static readonly DependencyProperty NewItemMarginProperty =
        NewItemMarginPropertyKey.DependencyProperty;

    #endregion

    #region Routed Events

    public event DataGridSortingEventHandler? Sorting;

    public event EventHandler<DataGridBeginningEditEventArgs>? BeginningEdit;

    public event EventHandler<DataGridCellEditEndingEventArgs>? CellEditEnding;

    public event EventHandler<DataGridPreparingCellForEditEventArgs>? PreparingCellForEdit;

    public event EventHandler? AutoGeneratedColumns;

    public event EventHandler<DataGridAutoGeneratingColumnEventArgs>? AutoGeneratingColumn;

    public event EventHandler<DataGridColumnEventArgs>? ColumnDisplayIndexChanged;

    public event EventHandler<DataGridColumnReorderingEventArgs>? ColumnReordering;

    public event EventHandler<DataGridColumnEventArgs>? ColumnReordered;

    /// <summary>Occurs when a new item is requested from the data source.</summary>
    public event EventHandler<AddingNewItemEventArgs>? AddingNewItem;

    /// <summary>Occurs when a column-header drag operation starts.</summary>
    public event EventHandler<DragStartedEventArgs>? ColumnHeaderDragStarted;

    /// <summary>Occurs while a column header is being dragged.</summary>
    public event EventHandler<DragDeltaEventArgs>? ColumnHeaderDragDelta;

    /// <summary>Occurs when a column-header drag operation finishes.</summary>
    public event EventHandler<DragCompletedEventArgs>? ColumnHeaderDragCompleted;

    /// <summary>Occurs after clipboard content for a row has been assembled.</summary>
    public event EventHandler<DataGridRowClipboardEventArgs>? CopyingRowClipboardContent;

    /// <summary>
    /// Occurs when a newly created item is ready for application initialization.
    /// </summary>
    public event InitializingNewItemEventHandler? InitializingNewItem;

    /// <summary>
    /// Occurs when the collection of selected cells changes.
    /// </summary>
    public event SelectedCellsChangedEventHandler? SelectedCellsChanged;

    /// <summary>Occurs when the current cell changes.</summary>
    public event EventHandler<EventArgs>? CurrentCellChanged;

    /// <summary>Occurs after a row container is prepared for use.</summary>
    public event EventHandler<DataGridRowEventArgs>? LoadingRow;

    /// <summary>Occurs before a row container is released.</summary>
    public event EventHandler<DataGridRowEventArgs>? UnloadingRow;

    /// <summary>Occurs before visible row details are presented.</summary>
    public event EventHandler<DataGridRowDetailsEventArgs>? LoadingRowDetails;

    /// <summary>Occurs before loaded row details are released.</summary>
    public event EventHandler<DataGridRowDetailsEventArgs>? UnloadingRowDetails;

    /// <summary>Occurs when a row details visibility value changes.</summary>
    public event EventHandler<DataGridRowDetailsEventArgs>? RowDetailsVisibilityChanged;

    /// <summary>Occurs before a row edit is committed or canceled.</summary>
    public event EventHandler<DataGridRowEditEndingEventArgs>? RowEditEnding;

    /// <summary>Raises the <see cref="Sorting"/> event.</summary>
    protected virtual void OnSorting(DataGridSortingEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);
        Sorting?.Invoke(this, eventArgs);
    }

    /// <summary>Raises the <see cref="BeginningEdit"/> event.</summary>
    protected virtual void OnBeginningEdit(DataGridBeginningEditEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        BeginningEdit?.Invoke(this, e);
    }

    /// <summary>Raises the <see cref="CellEditEnding"/> event.</summary>
    protected virtual void OnCellEditEnding(DataGridCellEditEndingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        CellEditEnding?.Invoke(this, e);
    }

    /// <summary>Raises the <see cref="PreparingCellForEdit"/> event.</summary>
    protected internal virtual void OnPreparingCellForEdit(DataGridPreparingCellForEditEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        PreparingCellForEdit?.Invoke(this, e);
    }

    /// <summary>Raises the <see cref="InitializingNewItem"/> event.</summary>
    protected virtual void OnInitializingNewItem(InitializingNewItemEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        InitializingNewItem?.Invoke(this, e);
    }

    /// <summary>Raises the <see cref="SelectedCellsChanged"/> event.</summary>
    protected virtual void OnSelectedCellsChanged(SelectedCellsChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        SelectedCellsChanged?.Invoke(this, e);
    }

    /// <summary>Raises the <see cref="CurrentCellChanged"/> event.</summary>
    protected virtual void OnCurrentCellChanged(EventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        CurrentCellChanged?.Invoke(this, e);
    }

    /// <summary>Raises the <see cref="LoadingRow"/> event.</summary>
    protected virtual void OnLoadingRow(DataGridRowEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        LoadingRow?.Invoke(this, e);
        OnLoadingRowDetailsWrapper(e.Row);
    }

    /// <summary>Raises the <see cref="UnloadingRow"/> event.</summary>
    protected virtual void OnUnloadingRow(DataGridRowEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        UnloadingRow?.Invoke(this, e);
        OnUnloadingRowDetailsWrapper(e.Row);
    }

    /// <summary>Raises the <see cref="LoadingRowDetails"/> event.</summary>
    protected virtual void OnLoadingRowDetails(DataGridRowDetailsEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        LoadingRowDetails?.Invoke(this, e);
    }

    /// <summary>Raises the <see cref="UnloadingRowDetails"/> event.</summary>
    protected virtual void OnUnloadingRowDetails(DataGridRowDetailsEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        UnloadingRowDetails?.Invoke(this, e);
    }

    /// <summary>Raises the <see cref="RowDetailsVisibilityChanged"/> event.</summary>
    protected internal virtual void OnRowDetailsVisibilityChanged(DataGridRowDetailsEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RowDetailsVisibilityChanged?.Invoke(this, e);
        OnLoadingRowDetailsWrapper(e.Row);
    }

    /// <summary>Raises the <see cref="RowEditEnding"/> event.</summary>
    protected virtual void OnRowEditEnding(DataGridRowEditEndingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RowEditEnding?.Invoke(this, e);
    }

    private void OnLoadingRowDetailsWrapper(DataGridRow row)
    {
        row.EnsureDetailsElement();
        if (!row.DetailsLoaded && row.DetailsVisibility == Visibility.Visible && row.DetailsElement != null)
        {
            OnLoadingRowDetails(new DataGridRowDetailsEventArgs(row, row.DetailsElement));
            row.DetailsLoaded = true;
        }
    }

    private void OnUnloadingRowDetailsWrapper(DataGridRow row)
    {
        if (row.DetailsLoaded && row.DetailsElement != null)
        {
            OnUnloadingRowDetails(new DataGridRowDetailsEventArgs(row, row.DetailsElement));
        }

        row.DetailsLoaded = false;
    }

    #endregion

    #region CLR Properties

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public object? CurrentItem
    {
        get => GetValue(CurrentItemProperty);
        set => SetValue(CurrentItemProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public DataGridColumn? CurrentColumn
    {
        get => (DataGridColumn?)GetValue(CurrentColumnProperty);
        set => SetValue(CurrentColumnProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public DataGridCellInfo CurrentCell
    {
        get => (DataGridCellInfo)(GetValue(CurrentCellProperty) ?? default(DataGridCellInfo));
        set => SetValue(CurrentCellProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool AutoGenerateColumns
    {
        get => (bool)GetValue(AutoGenerateColumnsProperty)!;
        set => SetValue(AutoGenerateColumnsProperty, value);
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

    /// <summary>
    /// Gets or sets whether the user can add new rows. When enabled and the
    /// bound source is a mutable, non-fixed-size <see cref="IList"/> whose item
    /// type is constructible, an empty placeholder row is shown at the bottom
    /// of the grid; interacting with it appends a new item to the source.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool CanUserAddRows
    {
        get => (bool)GetValue(CanUserAddRowsProperty)!;
        set => SetValue(CanUserAddRowsProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the user can delete rows. When enabled and the
    /// bound source is a mutable, non-fixed-size <see cref="IList"/>, pressing
    /// <c>Delete</c> removes the selected row(s) from the source.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool CanUserDeleteRows
    {
        get => (bool)GetValue(CanUserDeleteRowsProperty)!;
        set => SetValue(CanUserDeleteRowsProperty, value);
    }

    /// <summary>
    /// Gets or sets the number of leading columns that stay pinned to the left
    /// edge while the remaining columns scroll horizontally. The count is
    /// measured in display order. The default is 0 (no frozen columns).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public int FrozenColumnCount
    {
        get => (int)GetValue(FrozenColumnCountProperty)!;
        set => SetValue(FrozenColumnCountProperty, value);
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

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public DataGridSelectionMode SelectionMode
    {
        get => (DataGridSelectionMode)GetValue(SelectionModeProperty)!;
        set => SetValue(SelectionModeProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DataGridSelectionUnit SelectionUnit
    {
        get => (DataGridSelectionUnit)GetValue(SelectionUnitProperty)!;
        set => SetValue(SelectionUnitProperty, value);
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
    public double RowHeaderWidth
    {
        get => (double)GetValue(RowHeaderWidthProperty)!;
        set => SetValue(RowHeaderWidthProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double RowHeaderActualWidth
    {
        get => (double)(GetValue(RowHeaderActualWidthProperty) ?? 0.0);
        internal set => SetValue(RowHeaderActualWidthPropertyKey, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty)!;
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>
    /// Gets or sets the template to use to display the details section of a row.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public DataTemplate? RowDetailsTemplate
    {
        get => (DataTemplate?)GetValue(RowDetailsTemplateProperty);
        set => SetValue(RowDetailsTemplateProperty, value);
    }

    /// <summary>Gets or sets the selector used to choose a row details template.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public DataTemplateSelector? RowDetailsTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(RowDetailsTemplateSelectorProperty);
        set => SetValue(RowDetailsTemplateSelectorProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates when the details section of a row is displayed.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public DataGridRowDetailsVisibilityMode RowDetailsVisibilityMode
    {
        get => (DataGridRowDetailsVisibilityMode)(GetValue(RowDetailsVisibilityModeProperty) ?? DataGridRowDetailsVisibilityMode.VisibleWhenSelected);
        set => SetValue(RowDetailsVisibilityModeProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public bool AreRowDetailsFrozen
    {
        get => (bool)(GetValue(AreRowDetailsFrozenProperty) ?? false);
        set => SetValue(AreRowDetailsFrozenProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool CanUserResizeRows
    {
        get => (bool)(GetValue(CanUserResizeRowsProperty) ?? true);
        set => SetValue(CanUserResizeRowsProperty, value);
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

    /// <summary>Gets or sets the style applied to every generated cell.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Style CellStyle
    {
        get => (Style)GetValue(CellStyleProperty)!;
        set => SetValue(CellStyleProperty, value);
    }

    /// <summary>Gets or sets the style applied to generated column headers.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Style ColumnHeaderStyle
    {
        get => (Style)GetValue(ColumnHeaderStyleProperty)!;
        set => SetValue(ColumnHeaderStyleProperty, value);
    }

    /// <summary>Gets or sets the default width used by columns without a local width.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public DataGridLength ColumnWidth
    {
        get => (DataGridLength)(GetValue(ColumnWidthProperty) ?? DataGridLength.SizeToHeader);
        set => SetValue(ColumnWidthProperty, value);
    }

    /// <summary>Gets or sets the minimum width constraint inherited by columns.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MinColumnWidth
    {
        get => (double)(GetValue(MinColumnWidthProperty) ?? 20.0);
        set
        {
            if (!ValidateMinColumnWidth(value))
            {
                throw new ArgumentException("MinColumnWidth must be finite and non-negative.", nameof(value));
            }

            SetValue(MinColumnWidthProperty, value);
        }
    }

    /// <summary>Gets or sets the maximum width constraint inherited by columns.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MaxColumnWidth
    {
        get => (double)(GetValue(MaxColumnWidthProperty) ?? double.PositiveInfinity);
        set
        {
            if (!ValidateMaxColumnWidth(value))
            {
                throw new ArgumentException("MaxColumnWidth must be non-negative and not NaN.", nameof(value));
            }

            SetValue(MaxColumnWidthProperty, value);
        }
    }

    /// <summary>Gets or sets the minimum height constraint inherited by rows.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MinRowHeight
    {
        get => (double)(GetValue(MinRowHeightProperty) ?? 0.0);
        set
        {
            if (!ValidateMinRowHeight(value))
            {
                throw new ArgumentException("MinRowHeight must be finite and non-negative.", nameof(value));
            }

            SetValue(MinRowHeightProperty, value);
        }
    }

    /// <summary>Gets or sets the horizontal scroll bar visibility.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get => (ScrollBarVisibility)(GetValue(HorizontalScrollBarVisibilityProperty) ?? ScrollBarVisibility.Auto);
        set => SetValue(HorizontalScrollBarVisibilityProperty, value);
    }

    /// <summary>Gets or sets the vertical scroll bar visibility.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => (ScrollBarVisibility)(GetValue(VerticalScrollBarVisibilityProperty) ?? ScrollBarVisibility.Auto);
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }

    /// <summary>Gets or sets the style applied to generated row containers.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Style RowStyle
    {
        get => (Style)GetValue(RowStyleProperty)!;
        set => SetValue(RowStyleProperty, value);
    }

    /// <summary>Gets or sets the selector used to choose a generated row's style.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public StyleSelector RowStyleSelector
    {
        get => (StyleSelector)GetValue(RowStyleSelectorProperty)!;
        set => SetValue(RowStyleSelectorProperty, value);
    }

    /// <summary>Gets or sets the style applied to generated row headers.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Style RowHeaderStyle
    {
        get => (Style)GetValue(RowHeaderStyleProperty)!;
        set => SetValue(RowHeaderStyleProperty, value);
    }

    /// <summary>Gets or sets the template applied to generated row headers.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public DataTemplate RowHeaderTemplate
    {
        get => (DataTemplate)GetValue(RowHeaderTemplateProperty)!;
        set => SetValue(RowHeaderTemplateProperty, value);
    }

    /// <summary>Gets or sets the selector used to choose a generated row header template.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public DataTemplateSelector RowHeaderTemplateSelector
    {
        get => (DataTemplateSelector)GetValue(RowHeaderTemplateSelectorProperty)!;
        set => SetValue(RowHeaderTemplateSelectorProperty, value);
    }

    /// <summary>Gets or sets the template used to visualize row validation errors.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public ControlTemplate RowValidationErrorTemplate
    {
        get => (ControlTemplate)GetValue(RowValidationErrorTemplateProperty)!;
        set => SetValue(RowValidationErrorTemplateProperty, value);
    }

    /// <summary>Gets or sets how selected cells are copied to the clipboard.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DataGridClipboardCopyMode ClipboardCopyMode
    {
        get => (DataGridClipboardCopyMode)(GetValue(ClipboardCopyModeProperty) ??
            DataGridClipboardCopyMode.ExcludeHeader);
        set => SetValue(ClipboardCopyModeProperty, value);
    }

    /// <summary>Gets or sets the style of the floating header shown during a reorder.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Style DragIndicatorStyle
    {
        get => (Style)GetValue(DragIndicatorStyleProperty)!;
        set => SetValue(DragIndicatorStyleProperty, value);
    }

    /// <summary>Gets or sets the style of the column drop-location indicator.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Style DropLocationIndicatorStyle
    {
        get => (Style)GetValue(DropLocationIndicatorStyleProperty)!;
        set => SetValue(DropLocationIndicatorStyleProperty, value);
    }

    /// <summary>Gets the validation rules evaluated when a row edit is committed.</summary>
    public ObservableCollection<ValidationRule> RowValidationRules => _rowValidationRules;

    /// <summary>Gets the horizontal offset between a row and its cells panel.</summary>
    public double CellsPanelHorizontalOffset =>
        (double)(GetValue(CellsPanelHorizontalOffsetProperty) ?? 0.0);

    /// <summary>Gets the viewport offset where non-frozen columns begin.</summary>
    public double NonFrozenColumnsViewportHorizontalOffset =>
        (double)(GetValue(NonFrozenColumnsViewportHorizontalOffsetProperty) ?? 0.0);

    /// <summary>Gets the computed margin used by the new-item row.</summary>
    public Thickness NewItemMargin =>
        (Thickness)(GetValue(NewItemMarginProperty) ?? new Thickness(0));

    public ObservableCollection<DataGridColumn> Columns { get; }

    /// <summary>Gets the cells currently selected in this grid.</summary>
    public IList<DataGridCellInfo> SelectedCells => _selectedCells;

    #endregion

    #region Private Fields

    private const double DefaultRowHeight = 28.0;
    private const double DefaultColumnHeaderHeight = 32.0;
    private const double DefaultRowHeaderWidth = 20.0;

    private readonly List<object> _items = new();
    private readonly ObservableCollection<object> _selectedItems;
    private readonly HashSet<object> _selectedItemsLookup = new();
    private object[] _selectedItemsSnapshot = [];
    private readonly SelectedCellCollection _selectedCells;
    private readonly Dictionary<object, Visibility> _detailsVisibilityByItem = new();
    private readonly ObservableCollection<ValidationRule> _rowValidationRules = new();

    private StackPanel? _columnHeadersHost;
    private StackPanel? _rowsHost;
    private Border? _columnHeadersBorder;
    private ScrollViewer? _columnHeadersScrollViewer;
    private ScrollViewer? _dataScrollViewer;
    private FrameworkElement? _rowHeaderCorner;
    private readonly Dictionary<int, DataGridRow> _realizedRows = new();
    private Border? _topSpacer;
    private Border? _bottomSpacer;
    private DataGridRow? _placeholderRow;
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
    private bool _selectionMutationOwnedByDataGrid;
    private bool _isSynchronizingCurrentCell;
    private bool _isSynchronizingColumnDisplayIndexes;
    private int _selectionAnchorIndex = -1;

    // Drag reorder state
    private Canvas? _dragOverlay;
    private Border? _dragGhost;
    private Border? _dropIndicator;
    private DataGridColumn? _dragColumn;
    private int _dragSourceIndex = -1;
    private Point _dragStartPoint;
    private Point _dragLastPoint;
    internal bool _isColumnDragging;
    private const double ScrollBarReservedWidth = 12.0;

    // Frozen columns are lifted above the scrolling cells so they paint on top
    // once they translate over them; a constant Z-index is enough since no
    // other DataGrid part competes for stacking order.
    private const int FrozenColumnZIndex = 10;

    private static readonly SolidColorBrush s_frozenCellFallbackBrush = new(ThemeColors.ControlBackground);

    #endregion

    #region Constructor

    public DataGrid()
    {
        Focusable = true;
        _selectedItems = SelectedItemsStorage;
        _selectedCells = new SelectedCellCollection(this);
        Columns = new ObservableCollection<DataGridColumn>();
        Columns.CollectionChanged += OnColumnsCollectionChanged;
        _rowValidationRules.CollectionChanged += OnRowValidationRulesChanged;

        AddHandler(RoutedCommand.CanExecuteEvent,
            new CanExecuteRoutedEventHandler(OnCanExecuteCommand), handledEventsToo: true);
        AddHandler(RoutedCommand.ExecutedEvent,
            new ExecutedRoutedEventHandler(OnExecutedCommand), handledEventsToo: true);
    }

    #endregion

    #region Template

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Unsubscribe from previous ScrollViewer
        if (_dataScrollViewer != null)
        {
            _dataScrollViewer.ScrollChanged -= OnDataScrollViewerScrollChanged;
            _dataScrollViewer.SizeChanged -= OnDataScrollViewerSizeChanged;
        }

        _columnHeadersHost = GetTemplateChild("PART_ColumnHeadersHost") as StackPanel;
        _rowsHost = GetTemplateChild("PART_RowsHost") as StackPanel;
        _columnHeadersBorder = GetTemplateChild("PART_ColumnHeadersBorder") as Border;
        _rowHeaderCorner = GetTemplateChild("PART_RowHeaderCorner") as FrameworkElement;
        _columnHeadersScrollViewer = GetTemplateChild("PART_ColumnHeadersScrollViewer") as ScrollViewer;
        _dataScrollViewer = GetTemplateChild("PART_DataScrollViewer") as ScrollViewer;
        _dragOverlay = GetTemplateChild("PART_DragOverlay") as Canvas;

        ApplyScrollBarVisibility();
        UpdateLayoutMetrics();

        // Sync column headers with horizontal scroll
        if (_dataScrollViewer != null)
        {
            _dataScrollViewer.ScrollChanged += OnDataScrollViewerScrollChanged;
            _dataScrollViewer.SizeChanged += OnDataScrollViewerSizeChanged;
        }

        UpdateHeadersVisibility();

        RefreshColumnHeaders();
        RefreshRows();
    }

    /// <inheritdoc />
    internal override void OnTemplateContentClearing()
    {
        base.OnTemplateContentClearing();

        // Release the cached template parts before the tree is discarded. The row-refresh and
        // column-header paths only guard against null, so a reference that survives the teardown
        // keeps resolving into the detached tree — rows silently realize into an invisible panel
        // once the template is cleared and never re-applied.
        if (_dataScrollViewer != null)
        {
            _dataScrollViewer.ScrollChanged -= OnDataScrollViewerScrollChanged;
            _dataScrollViewer.SizeChanged -= OnDataScrollViewerSizeChanged;
        }

        _columnHeadersHost = null;
        _rowsHost = null;
        _columnHeadersBorder = null;
        _rowHeaderCorner = null;
        _columnHeadersScrollViewer = null;
        _dataScrollViewer = null;
        _dragOverlay = null;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var measured = base.MeasureOverride(availableSize);
        UpdateLayoutMetrics();
        return measured;
    }

    /// <inheritdoc />
    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    {
        base.OnContextMenuOpening(e);
    }

    /// <inheritdoc />
    protected override void OnIsMouseCapturedChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsMouseCapturedChanged(e);
        if (e.NewValue is false && _isColumnDragging)
        {
            CancelColumnDrag();
        }
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_isColumnDragging)
        {
            UpdateColumnDrag(e.GetPosition(this));
        }
    }

    /// <inheritdoc />
    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0]))
        {
            return;
        }

        if (BeginEdit(e) && _currentEditingCell?.EditingElement is TextBox textBox)
        {
            textBox.Text = e.Text;
            textBox.CaretIndex = textBox.Text.Length;
            e.Handled = true;
        }
    }

    /// <summary>Called when the control template changes.</summary>
    protected override void OnTemplateChanged(ControlTemplate oldTemplate, ControlTemplate newTemplate)
    {
        base.OnTemplateChanged(oldTemplate, newTemplate);
        // The template property can change while the old visual tree is still attached.
        // Materializing rows here raises LoadingRow for containers that are immediately
        // discarded when OnApplyTemplate installs the new tree. Defer all realization to
        // OnApplyTemplate, which owns the active PART_RowsHost.
        InvalidateMeasure();
    }

    private void OnDataScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Sync column headers horizontal scroll with data scroll
        SyncColumnHeadersHorizontalScroll();

        if (EnableRowVirtualization || EnableColumnVirtualization)
        {
            UpdateRealizedRows();
        }

        // Counter-translate the frozen columns so they stay pinned to the left
        // edge as the rest of the grid scrolls horizontally.
        UpdateFrozenColumnOffsets();
    }

    /// <summary>
    /// Handles size changes of the data ScrollViewer to keep headers in sync.
    /// </summary>
    private void OnDataScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // When the ScrollViewer viewport resizes (e.g. DataGrid width changes),
        // re-sync headers to account for scrollbar visibility changes.
        SyncColumnHeadersHorizontalScroll();
        UpdateLayoutMetrics();
    }

    /// <summary>
    /// Syncs the column headers ScrollViewer horizontal offset with the data ScrollViewer,
    /// and adjusts right margin to account for the vertical scrollbar.
    /// </summary>
    private void SyncColumnHeadersHorizontalScroll()
    {
        if (_dataScrollViewer == null) return;

        // Sync horizontal offset
        _columnHeadersScrollViewer?.ScrollToHorizontalOffset(_dataScrollViewer.HorizontalOffset);

        // Adjust column headers border right margin to match vertical scrollbar space
        if (_columnHeadersBorder != null)
        {
            var needsVerticalScrollBar = VerticalScrollBarVisibility == ScrollBarVisibility.Visible ||
                (VerticalScrollBarVisibility == ScrollBarVisibility.Auto && _dataScrollViewer.ScrollableHeight > 0);
            var rightMargin = needsVerticalScrollBar ? ScrollBarReservedWidth : 0.0;
            _columnHeadersBorder.Margin = new Thickness(0, 0, rightMargin, 0);
        }
    }

    private void ApplyScrollBarVisibility()
    {
        if (_dataScrollViewer == null)
        {
            return;
        }

        _dataScrollViewer.HorizontalScrollBarVisibility = HorizontalScrollBarVisibility;
        _dataScrollViewer.VerticalScrollBarVisibility = VerticalScrollBarVisibility;
    }

    /// <summary>
    /// Recomputes the shared geometry used by row headers, cells, frozen columns,
    /// and the read-only WPF layout state properties.
    /// </summary>
    private void UpdateLayoutMetrics()
    {
        var cellsOffset = GetEffectiveRowHeaderWidth();
        SetValue(RowHeaderActualWidthPropertyKey, cellsOffset);
        SetValue(CellsPanelHorizontalOffsetPropertyKey, cellsOffset);

        var frozenWidth = 0.0;
        var frozenColumnCount = Math.Min(FrozenColumnCount, Columns.Count);
        for (var index = 0; index < frozenColumnCount; index++)
        {
            if (!IsColumnVisible(Columns[index]))
            {
                continue;
            }

            frozenWidth += GetRenderableColumnWidth(Columns[index]);
        }

        SetValue(NonFrozenColumnsViewportHorizontalOffsetPropertyKey, frozenWidth);

        // The custom DataGrid currently has no grouped-row indentation. Compute
        // the WPF new-item margin from that real layout fact instead of exposing
        // a writable or arbitrary placeholder value.
        SetValue(NewItemMarginPropertyKey, new Thickness(0));

        foreach (var row in _realizedRows.Values)
        {
            UpdateRowHeaderLayout(row);
        }

        if (_placeholderRow != null)
        {
            UpdateRowHeaderLayout(_placeholderRow);
        }

        if (_rowHeaderCorner != null)
        {
            _rowHeaderCorner.Width = cellsOffset;
            _rowHeaderCorner.Visibility = cellsOffset > 0 &&
                HeadersVisibility.HasFlag(DataGridHeadersVisibility.Column)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (_columnHeadersScrollViewer != null)
        {
            _columnHeadersScrollViewer.Margin = new Thickness(cellsOffset, 0, 0, 0);
        }

        UpdateFrozenColumnOffsets();
    }

    private static bool ValidateMinColumnWidth(object? value) =>
        value is double width && width >= 0.0 && !double.IsNaN(width) && !double.IsPositiveInfinity(width);

    private static bool ValidateMaxColumnWidth(object? value) =>
        value is double width && width >= 0.0 && !double.IsNaN(width);

    private static bool ValidateMinRowHeight(object? value) =>
        value is double height && height >= 0.0 && !double.IsNaN(height) && !double.IsInfinity(height);

    /// <summary>Raises <see cref="AddingNewItem"/>.</summary>
    protected virtual void OnAddingNewItem(AddingNewItemEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        AddingNewItem?.Invoke(this, e);
    }

    /// <summary>Raises <see cref="AutoGeneratingColumn"/>.</summary>
    protected virtual void OnAutoGeneratingColumn(DataGridAutoGeneratingColumnEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        AutoGeneratingColumn?.Invoke(this, e);
    }

    /// <summary>Raises <see cref="AutoGeneratedColumns"/>.</summary>
    protected virtual void OnAutoGeneratedColumns(EventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        AutoGeneratedColumns?.Invoke(this, e);
    }

    /// <summary>Raises <see cref="ColumnDisplayIndexChanged"/>.</summary>
    protected internal virtual void OnColumnDisplayIndexChanged(DataGridColumnEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        ColumnDisplayIndexChanged?.Invoke(this, e);
    }

    /// <summary>Raises <see cref="ColumnReordering"/>.</summary>
    protected internal virtual void OnColumnReordering(DataGridColumnReorderingEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        ColumnReordering?.Invoke(this, e);
    }

    /// <summary>Raises <see cref="ColumnReordered"/>.</summary>
    protected internal virtual void OnColumnReordered(DataGridColumnEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        ColumnReordered?.Invoke(this, e);
    }

    /// <summary>Raises <see cref="ColumnHeaderDragStarted"/>.</summary>
    protected internal virtual void OnColumnHeaderDragStarted(DragStartedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        ColumnHeaderDragStarted?.Invoke(this, e);
    }

    /// <summary>Raises <see cref="ColumnHeaderDragDelta"/>.</summary>
    protected internal virtual void OnColumnHeaderDragDelta(DragDeltaEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        ColumnHeaderDragDelta?.Invoke(this, e);
    }

    /// <summary>Raises <see cref="ColumnHeaderDragCompleted"/>.</summary>
    protected internal virtual void OnColumnHeaderDragCompleted(DragCompletedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        ColumnHeaderDragCompleted?.Invoke(this, e);
    }

    /// <summary>Raises <see cref="CopyingRowClipboardContent"/>.</summary>
    protected virtual void OnCopyingRowClipboardContent(DataGridRowClipboardEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        CopyingRowClipboardContent?.Invoke(this, args);
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

        UpdateLayoutMetrics();
    }

    private void RefreshColumnHeaders()
    {
        if (_columnHeadersHost == null) return;

        _columnHeadersHost.Children.Clear();

        for (var columnIndex = 0; columnIndex < Columns.Count; columnIndex++)
        {
            var column = Columns[columnIndex];
            if (!IsColumnVisible(column))
            {
                continue;
            }

            var header = new DataGridColumnHeader
            {
                Width = GetRenderableColumnWidth(column),
                Height = double.IsNaN(ColumnHeaderHeight) ? double.NaN : GetEffectiveColumnHeaderHeight()
            };
            header.PrepareColumnHeader(column.Header, this, this, column);

            var headerStyle = column.HeaderStyle ?? (Style?)GetValue(ColumnHeaderStyleProperty);
            if (headerStyle != null)
            {
                header.Style = headerStyle;
            }

            header.AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnColumnHeaderClick));
            header.UpdateSortIndicator(column.SortDirection);

            ApplyFrozenHeaderState(header, columnIndex);

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

    /// <inheritdoc />
    protected override bool IsItemItsOwnContainerOverride(object item) => item is DataGridRow;

    /// <inheritdoc />
    protected override DependencyObject GetContainerForItemOverride() => new DataGridRow();

    /// <inheritdoc />
    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        if (element is not DataGridRow row)
        {
            throw new InvalidOperationException("A DataGrid item container must be a DataGridRow.");
        }

        var originalStyle = row.ReadLocalValue(FrameworkElement.StyleProperty);
        base.PrepareContainerForItemOverride(element, item);

        row.DataItem = item;
        row.ParentDataGrid = this;
        SetAlternationIndex(row, row.RowIndex);
        if (!ReferenceEquals(row, item))
        {
            row.DataContext = item;
        }

        var selector = (StyleSelector?)GetValue(RowStyleSelectorProperty);
        var ownerStyle = selector?.SelectStyle(item, row) ?? (Style?)GetValue(RowStyleProperty);
        if (ownerStyle != null &&
            (!ReferenceEquals(row, item) || ReferenceEquals(originalStyle, DependencyProperty.UnsetValue)))
        {
            row.Style = ownerStyle;
            row.AppliedOwnerStyle = ownerStyle;
        }

        ApplyPreparedRowAppearance(row, item);
        LoadRow(row);
    }

    /// <inheritdoc />
    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        if (element is not DataGridRow row)
        {
            base.ClearContainerForItemOverride(element, item);
            return;
        }

        if (row.IsLoadedForDataGrid)
        {
            OnUnloadingRow(new DataGridRowEventArgs(row));
            row.IsLoadedForDataGrid = false;
        }

        Validation.ClearInvalid(row);
        DetachRowInput(row);

        if (row.AppliedOwnerStyle != null && ReferenceEquals(row.Style, row.AppliedOwnerStyle))
        {
            row.ClearValue(FrameworkElement.StyleProperty);
        }

        row.AppliedOwnerStyle = null;
        row.RowHeader = null;
        row.AlternatingBackground = null;
        row.Cells.Clear();
        row.CellsByColumn.Clear();
        row.VisibleColumnStart = -1;
        row.VisibleColumnEnd = -1;
        row.IsNewItemPlaceholder = false;
        row.IsEditing = false;
        row.ParentDataGrid = null;
        row.DataItem = null;
        if (!ReferenceEquals(row, item))
        {
            row.ClearValue(FrameworkElement.DataContextProperty);
        }

        base.ClearContainerForItemOverride(element, item);
    }

    private void ApplyPreparedRowAppearance(DataGridRow row, object item)
    {
        row.MinHeight = MinRowHeight;
        row.Height = double.IsNaN(RowHeight) ? double.NaN : GetEffectiveRowHeight();
        row.ApplyOwnerDetailsSettings(RowDetailsTemplate, RowDetailsTemplateSelector);
        ApplyRowDetailsVisibility(row, raiseVisibilityChanged: false);

        if (RowBackground != null)
        {
            row.Background = RowBackground;
        }

        if (row.RowIndex % 2 == 1 && AlternatingRowBackground != null)
        {
            row.AlternatingBackground = AlternatingRowBackground;
        }

        row.RowHeader = CreateRowHeader(row, item);
        ApplyRowValidationErrorTemplate(row);
        AttachRowInput(row);
    }

    private DataGridRowHeader? CreateRowHeader(DataGridRow row, object? item)
    {
        if (!HeadersVisibility.HasFlag(DataGridHeadersVisibility.Row))
        {
            return null;
        }

        var header = new DataGridRowHeader
        {
            Width = GetEffectiveRowHeaderWidth(),
            Height = GetEffectiveRowHeight(),
            RowIndex = row.RowIndex,
            IsRowSelected = row.IsSelected,
            Content = row.Header ?? item,
            RenderOffset = new Point(GetHorizontalScrollOffset(), 0)
        };

        var headerStyle = row.HeaderStyle;
        if (headerStyle != null)
        {
            header.Style = headerStyle;
        }

        var headerTemplateSelector = row.HeaderTemplateSelector;
        header.ContentTemplateSelector = headerTemplateSelector;
        header.ContentTemplate = headerTemplateSelector?.SelectTemplate(header.Content, header)
            ?? row.HeaderTemplate;

        Panel.SetZIndex(header, FrozenColumnZIndex + 1);
        return header;
    }

    private void UpdateRowHeaderLayout(DataGridRow row)
    {
        if (row.RowHeader == null)
        {
            return;
        }

        row.RowHeader.Width = GetEffectiveRowHeaderWidth();
        row.RowHeader.Height = GetEffectiveRowHeight();
        row.RowHeader.Visibility = HeadersVisibility.HasFlag(DataGridHeadersVisibility.Row)
            ? Visibility.Visible
            : Visibility.Collapsed;
        row.RowHeader.RenderOffset = new Point(GetHorizontalScrollOffset(), 0);
    }

    private void ApplyRowValidationErrorTemplate(DataGridRow row)
    {
        row.ApplyValidationErrorTemplate();
    }

    private void AttachRowInput(DataGridRow row)
    {
        row.AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnRowMouseDown), handledEventsToo: true);
        row.AddHandler(TouchDownEvent, new RoutedEventHandler(OnRowTouchDown), handledEventsToo: true);
        row.AddHandler(TouchMoveEvent, new RoutedEventHandler(OnRowTouchMove), handledEventsToo: true);
        row.AddHandler(TouchUpEvent, new RoutedEventHandler(OnRowTouchUp), handledEventsToo: true);
        TouchHelper.SetIsRippleEnabled(row, true);
    }

    private void DetachRowInput(DataGridRow row)
    {
        row.RemoveHandler(MouseDownEvent, new MouseButtonEventHandler(OnRowMouseDown));
        row.RemoveHandler(TouchDownEvent, new RoutedEventHandler(OnRowTouchDown));
        row.RemoveHandler(TouchMoveEvent, new RoutedEventHandler(OnRowTouchMove));
        row.RemoveHandler(TouchUpEvent, new RoutedEventHandler(OnRowTouchUp));
        TouchHelper.SetIsRippleEnabled(row, false);
    }

    private static bool IsColumnVisible(DataGridColumn column) =>
        column.Visibility == Visibility.Visible;

    private static double GetEffectiveLength(double value, double fallback) =>
        double.IsNaN(value) || double.IsInfinity(value) || value <= 0 ? fallback : value;

    private double GetEffectiveRowHeight() =>
        Math.Max(MinRowHeight, GetEffectiveLength(RowHeight, DefaultRowHeight));

    private double GetEffectiveColumnHeaderHeight() => GetEffectiveLength(ColumnHeaderHeight, DefaultColumnHeaderHeight);

    private double GetEffectiveRowHeaderWidth()
    {
        if (!HeadersVisibility.HasFlag(DataGridHeadersVisibility.Row))
        {
            return 0.0;
        }

        return GetEffectiveLength(RowHeaderWidth, DefaultRowHeaderWidth);
    }

    private double GetRenderableColumnWidth(DataGridColumn column)
    {
        if (!IsColumnVisible(column))
        {
            return 0.0;
        }

        var hasLocalWidth = !ReferenceEquals(
            column.ReadLocalValue(DataGridColumn.WidthProperty), DependencyProperty.UnsetValue);
        var requestedWidth = hasLocalWidth ? column.Width : ColumnWidth;
        var width = ResolveColumnWidth(column, requestedWidth);

        var hasLocalMinWidth = !ReferenceEquals(
            column.ReadLocalValue(DataGridColumn.MinWidthProperty), DependencyProperty.UnsetValue);
        var hasLocalMaxWidth = !ReferenceEquals(
            column.ReadLocalValue(DataGridColumn.MaxWidthProperty), DependencyProperty.UnsetValue);
        var minimum = hasLocalMinWidth ? column.MinWidth : MinColumnWidth;
        var maximum = hasLocalMaxWidth ? column.MaxWidth : MaxColumnWidth;
        if (maximum < minimum)
        {
            maximum = minimum;
        }

        var actualWidth = Math.Clamp(Math.Max(1.0, width), minimum, maximum);
        column.SetActualWidth(actualWidth);
        return actualWidth;
    }

    private double ResolveColumnWidth(DataGridColumn column, DataGridLength requestedWidth)
    {
        if (requestedWidth.IsAbsolute)
        {
            return requestedWidth.Value;
        }

        if (!double.IsNaN(requestedWidth.DisplayValue) && requestedWidth.DisplayValue > 0.0)
        {
            return requestedWidth.DisplayValue;
        }

        if (requestedWidth.IsStar)
        {
            var starWeight = requestedWidth.Value;
            var totalStarWeight = Columns
                .Where(IsColumnVisible)
                .Select(GetEffectiveColumnLength)
                .Where(length => length.IsStar)
                .Sum(length => length.Value);
            if (totalStarWeight > 0.0)
            {
                var viewportWidth = _dataScrollViewer?.ViewportWidth > 0
                    ? _dataScrollViewer.ViewportWidth
                    : _dataScrollViewer?.ActualWidth ?? 0.0;
                if (viewportWidth > 0)
                {
                    return Math.Max(
                        1.0,
                        (viewportWidth - CellsPanelHorizontalOffset) * starWeight / totalStarWeight);
                }
            }
        }

        if (!double.IsNaN(requestedWidth.DesiredValue) && requestedWidth.DesiredValue > 0.0)
        {
            return requestedWidth.DesiredValue;
        }

        return column.ActualWidth > 0.0 ? column.ActualWidth : 120.0;
    }

    private DataGridLength GetEffectiveColumnLength(DataGridColumn column) =>
        !ReferenceEquals(column.ReadLocalValue(DataGridColumn.WidthProperty), DependencyProperty.UnsetValue)
            ? column.Width
            : ColumnWidth;

    private void RefreshRows()
    {
        if (_rowsHost == null)
        {
            return;
        }

        UnloadRows(_realizedRows.Values.ToArray());
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
            UnloadRows(_realizedRows.Values.ToArray());
            _rowsHost.Children.Clear();
            _realizedRows.Clear();
            _realizedStartIndex = -1;
            _realizedEndIndex = -1;
            _realizedColumnStartIndex = -1;
            _realizedColumnEndIndex = -1;

            // An empty grid still shows the new-item placeholder so the user
            // has somewhere to start adding rows.
            if (ShowNewItemPlaceholder)
            {
                _placeholderRow = BuildPlaceholderRow(GetEffectiveRowHeight());
                _rowsHost.Children.Add(_placeholderRow);
            }
            else
            {
                _placeholderRow = null;
            }
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
            UnloadRow(_realizedRows[staleIndex]);
            _realizedRows.Remove(staleIndex);
        }

        for (var rowIndex = startIndex; rowIndex <= endIndex; rowIndex++)
        {
            if (!_realizedRows.TryGetValue(rowIndex, out var row) ||
                row.VisibleColumnStart != columnStart ||
                row.VisibleColumnEnd != columnEnd)
            {
                if (row != null)
                {
                    UnloadRow(row);
                }

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

            var bottomHeight = Math.Max(0, (_items.Count - endIndex - 1) * rowHeight);
            _bottomSpacer.Height = bottomHeight;
            _bottomSpacer.Visibility = bottomHeight > 0 ? Visibility.Visible : Visibility.Collapsed;
            _rowsHost.Children.Add(_bottomSpacer);

            // The new-item placeholder is appended after the bottom spacer so
            // it is always pinned to the very end of the scrollable content,
            // independent of which rows the virtualizer currently realizes.
            if (ShowNewItemPlaceholder)
            {
                _placeholderRow = BuildPlaceholderRow(rowHeight);
                _rowsHost.Children.Add(_placeholderRow);
            }
            else
            {
                _placeholderRow = null;
            }
        }
        finally
        {
            _rowsHost.Children.EndBatchUpdate();
        }
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

        // Frozen columns must remain realized at all horizontal offsets so they
        // can be pinned; horizontal column virtualization would recycle them
        // away once scrolled past, so it is disabled whenever any are frozen.
        if (!EnableColumnVirtualization || _dataScrollViewer == null || FrozenColumnCount > 0)
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

        viewportWidth = Math.Max(0.0, viewportWidth - CellsPanelHorizontalOffset);

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
        var row = IsItemItsOwnContainerOverride(item)
            ? (DataGridRow)item
            : (DataGridRow)GetContainerForItemOverride();

        row.DataItem = item;
        row.RowIndex = rowIndex;
        row.Height = double.IsNaN(RowHeight) ? double.NaN : rowHeight;
        row.MinHeight = MinRowHeight;
        row.IsSelected = IsItemSelected(item);
        row.ParentDataGrid = this;
        row.VisibleColumnStart = columnStart;
        row.VisibleColumnEnd = columnEnd;
        row.IsNewItemPlaceholder = false;
        row.Cells.Clear();
        row.CellsByColumn.Clear();

        if (columnStart < 0 || columnEnd < 0 || Columns.Count == 0)
        {
            PrepareContainerForItemOverride(row, item);
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
                Width = GetRenderableColumnWidth(column),
                Column = column
            };

            var cellStyle = column.CellStyle ?? (Style?)GetValue(CellStyleProperty);
            if (cellStyle != null)
            {
                cell.Style = cellStyle;
            }

            // Use the column's GenerateElement to create the display content
            var displayElement = column.BuildVisualTree(isEditing: false, item, cell);
            cell.Content = displayElement;

            row.Cells.Add(cell);
            row.CellsByColumn[colIndex] = cell;
            ApplyFrozenCellState(cell, colIndex);
        }

        PrepareContainerForItemOverride(row, item);
        return row;
    }

    private void LoadRow(DataGridRow row)
    {
        if (row.IsLoadedForDataGrid)
        {
            return;
        }

        row.IsLoadedForDataGrid = true;
        OnLoadingRow(new DataGridRowEventArgs(row));
    }

    private void UnloadRow(DataGridRow row)
    {
        if (!row.IsLoadedForDataGrid)
        {
            return;
        }

        ClearContainerForItemOverride(row, row.DataItem ?? row);
    }

    private void UnloadRows(IEnumerable<DataGridRow> rows)
    {
        foreach (var row in rows)
        {
            UnloadRow(row);
        }
    }

    private void OnRowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        if (sender is DataGridRow row && e.ChangedButton == MouseButton.Left)
        {
            Focus();
            if (row.IsNewItemPlaceholder)
            {
                CommitNewItem();
            }
            else
            {
                HandleRowSelectionInput(row, e.KeyboardModifiers, e.OriginalSource as Visual);
            }
            e.Handled = true;
        }
    }

    private const double RowTouchPanCancelThresholdDips = 8.0;

    private void OnRowTouchDown(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled || sender is not DataGridRow row || e is not TouchEventArgs touchArgs) return;
        if (!TouchHelper.GetIsTouchInteractive(row)) return;
        row.TouchActiveId = touchArgs.TouchDevice.Id;
        row.TouchDownPos = touchArgs.GetTouchPoint(row).Position;
        row.TouchClickCandidate = true;
        // Suppress mouse synthesis so OnRowMouseDown does not select the row
        // immediately. PointerDown still bubbles to ancestor ScrollViewer.
        e.Handled = true;
    }

    private void OnRowTouchMove(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGridRow row || !row.TouchClickCandidate || e is not TouchEventArgs touchArgs) return;
        if (touchArgs.TouchDevice.Id != row.TouchActiveId) return;
        var current = touchArgs.GetTouchPoint(row).Position;
        double dx = current.X - row.TouchDownPos.X;
        double dy = current.Y - row.TouchDownPos.Y;
        if (dx * dx + dy * dy > RowTouchPanCancelThresholdDips * RowTouchPanCancelThresholdDips)
        {
            row.TouchClickCandidate = false;
            row.TouchActiveId = -1;
        }
    }

    private void OnRowTouchUp(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGridRow row || e is not TouchEventArgs touchArgs) return;
        if (touchArgs.TouchDevice.Id != row.TouchActiveId) return;
        bool wasCandidate = row.TouchClickCandidate;
        row.TouchClickCandidate = false;
        row.TouchActiveId = -1;
        if (!wasCandidate) return;

        Focus();
        if (row.IsNewItemPlaceholder)
        {
            CommitNewItem();
        }
        else
        {
            HandleRowSelectionInput(row, ModifierKeys.None, e.OriginalSource as Visual);
        }
        e.Handled = true;
    }

    private void HandleRowSelectionInput(DataGridRow row, ModifierKeys modifiers, Visual? originalSource)
    {
        var cell = FindCellAncestor(originalSource, row);
        var columnIndex = cell == null ? -1 : GetColumnIndex(row, cell);

        if (SelectionUnit == DataGridSelectionUnit.FullRow ||
            (SelectionUnit == DataGridSelectionUnit.CellOrRowHeader && cell == null))
        {
            SelectRow(row.RowIndex, modifiers);
        }
        else if (cell != null && columnIndex >= 0)
        {
            SelectCell(row.RowIndex, columnIndex, modifiers);
        }

        if (cell != null && columnIndex >= 0)
        {
            CurrentCell = new DataGridCellInfo(row.DataItem!, Columns[columnIndex]);
        }
    }

    private static DataGridCell? FindCellAncestor(Visual? source, DataGridRow row)
    {
        for (var current = source; current != null && !ReferenceEquals(current, row); current = current.VisualParent)
        {
            if (current is DataGridCell cell)
            {
                return cell;
            }
        }

        return null;
    }

    private static int GetColumnIndex(DataGridRow row, DataGridCell cell)
    {
        foreach (var pair in row.CellsByColumn)
        {
            if (ReferenceEquals(pair.Value, cell))
            {
                return pair.Key;
            }
        }

        return -1;
    }

    #endregion

    #region Row Details

    /// <summary>Sets the details visibility associated with a data item.</summary>
    public void SetDetailsVisibilityForItem(object item, Visibility detailsVisibility)
    {
        ArgumentNullException.ThrowIfNull(item);
        _detailsVisibilityByItem[item] = detailsVisibility;

        var row = FindRealizedRow(item);
        if (row != null)
        {
            ApplyRowDetailsVisibility(row, raiseVisibilityChanged: true);
        }
    }

    /// <summary>Gets the details visibility associated with a data item.</summary>
    public Visibility GetDetailsVisibilityForItem(object item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (_detailsVisibilityByItem.TryGetValue(item, out var storedVisibility))
        {
            return storedVisibility;
        }

        var row = FindRealizedRow(item);
        if (row != null)
        {
            return row.DetailsVisibility;
        }

        return RowDetailsVisibilityMode switch
        {
            DataGridRowDetailsVisibilityMode.Visible => Visibility.Visible,
            DataGridRowDetailsVisibilityMode.VisibleWhenSelected when IsItemSelected(item) => Visibility.Visible,
            _ => Visibility.Collapsed
        };
    }

    /// <summary>Clears the item-specific details visibility value.</summary>
    public void ClearDetailsVisibilityForItem(object item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _detailsVisibilityByItem.Remove(item);

        var row = FindRealizedRow(item);
        if (row != null)
        {
            ApplyRowDetailsVisibility(row, raiseVisibilityChanged: true);
        }
    }

    internal DataGridRow? FindRealizedRow(object item) =>
        _realizedRows.Values.FirstOrDefault(row => Equals(row.DataItem, item));

    internal void RefreshColumnCellContent(DataGridColumn column, string propertyName)
    {
        var columnIndex = Columns.IndexOf(column);
        if (columnIndex < 0)
        {
            return;
        }

        foreach (var row in _realizedRows.Values)
        {
            if (row.CellsByColumn.TryGetValue(columnIndex, out var cell))
            {
                column.RefreshCellContent(cell, propertyName);
            }
        }
    }

    internal void RefreshColumnCellContent(DataGridColumn column, DataGridCell cell, string propertyName)
    {
        var row = _realizedRows.Values.FirstOrDefault(candidate => candidate.CellsByColumn.Values.Contains(cell));
        var item = row?.DataItem ?? cell.DataContext;
        if (item == null)
        {
            return;
        }

        var replacement = column.BuildVisualTree(cell.IsEditing, item, cell);
        replacement.DataContext = item;
        cell.Content = replacement;
        cell.InvalidateMeasure();
        cell.InvalidateVisual();
    }

    private Visibility GetEffectiveDetailsVisibility(DataGridRow row)
    {
        if (row.DataItem != null && _detailsVisibilityByItem.TryGetValue(row.DataItem, out var storedVisibility))
        {
            return storedVisibility;
        }

        if (row.DetailsTemplate == null && row.DetailsTemplateSelector == null)
        {
            return Visibility.Collapsed;
        }

        return RowDetailsVisibilityMode switch
        {
            DataGridRowDetailsVisibilityMode.Visible => Visibility.Visible,
            DataGridRowDetailsVisibilityMode.VisibleWhenSelected when row.IsSelected => Visibility.Visible,
            _ => Visibility.Collapsed
        };
    }

    internal void ApplyRowDetailsVisibility(DataGridRow row, bool raiseVisibilityChanged)
    {
        row.ApplyDetailsVisibility(GetEffectiveDetailsVisibility(row), raiseVisibilityChanged);
    }

    internal void OnRowDetailsVisibilityChangedFromRow(DataGridRow row, bool isOwnerValue)
    {
        if (!isOwnerValue && row.DataItem != null)
        {
            if (row.HasLocalValue(DataGridRow.DetailsVisibilityProperty))
            {
                _detailsVisibilityByItem[row.DataItem] = row.DetailsVisibility;
            }
            else
            {
                _detailsVisibilityByItem.Remove(row.DataItem);
                ApplyRowDetailsVisibility(row, raiseVisibilityChanged: false);
            }
        }

        row.EnsureDetailsElement();
        OnRowDetailsVisibilityChanged(new DataGridRowDetailsEventArgs(row, row.DetailsElement!));

        InvalidateMeasure();
    }

    #endregion

    #region Selection

    private void ClearSelection()
    {
        _selectionMutationOwnedByDataGrid = true;
        try
        {
            _selectedItems.Clear();
        }
        finally
        {
            _selectionMutationOwnedByDataGrid = false;
        }
        _selectedItemsLookup.Clear();
    }

    private void AddToSelection(object item)
    {
        _selectionMutationOwnedByDataGrid = true;
        try
        {
            _selectedItems.Add(item);
        }
        finally
        {
            _selectionMutationOwnedByDataGrid = false;
        }
        _selectedItemsLookup.Add(item);
    }

    private void RemoveFromSelection(object item)
    {
        _selectionMutationOwnedByDataGrid = true;
        try
        {
            _selectedItems.Remove(item);
        }
        finally
        {
            _selectionMutationOwnedByDataGrid = false;
        }
        _selectedItemsLookup.Remove(item);
    }

    private bool IsItemSelected(object item) => _selectedItemsLookup.Contains(item);

    internal bool AutomationSelectItem(object item, bool addToSelection)
    {
        int rowIndex = _items.IndexOf(item);
        if (rowIndex < 0)
            return false;

        var oldSelectedItems = _selectedItems.ToArray();
        if (!addToSelection || SelectionMode == DataGridSelectionMode.Single)
            ClearSelection();
        if (!_selectedItems.Contains(item))
            AddToSelection(item);
        else
            _selectedItemsLookup.Add(item);
        _selectionAnchorIndex = rowIndex;
        UpdateSelectionPropertiesFromSelectedItems(rowIndex);
        UpdateRowSelectionVisuals();
        RaiseSelectionChangedIfNeeded(oldSelectedItems);
        return true;
    }

    internal bool AutomationRemoveItemFromSelection(object item)
    {
        if (!_selectedItems.Contains(item))
            return true;

        var oldSelectedItems = _selectedItems.ToArray();
        RemoveFromSelection(item);
        UpdateSelectionPropertiesFromSelectedItems();
        UpdateRowSelectionVisuals();
        RaiseSelectionChangedIfNeeded(oldSelectedItems);
        return true;
    }

    private void UpdateSelectionPropertiesFromSelectedItems(int preferredIndex = -1)
    {
        object? selectedItem = null;
        var selectedIndex = -1;

        if (_selectedItems.Count > 0)
        {
            if (preferredIndex >= 0 && preferredIndex < _items.Count)
            {
                var preferredItem = _items[preferredIndex];
                if (IsItemSelected(preferredItem))
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
        var removed = oldSelection.Where(item => !IsItemSelected(item)).ToArray();
        var added = _selectedItems.Where(item => !oldSelection.Contains(item)).ToArray();
        if (removed.Length == 0 && added.Length == 0)
        {
            _selectedItemsSnapshot = _selectedItems.ToArray();
            return;
        }

        if (SelectionUnit != DataGridSelectionUnit.Cell)
        {
            _selectedCells.UpdateForRowSelection(added, removed);
        }

        OnSelectionChanged(new SelectionChangedEventArgs(SelectionChangedEvent, removed, added));
        _selectedItemsSnapshot = _selectedItems.ToArray();
    }

    /// <summary>Raises the routed <see cref="SelectionChanged"/> event.</summary>
    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        e.RoutedEvent ??= SelectionChangedEvent;
        RaiseEvent(e);
    }

    internal override bool HandleSelectedItemsCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (_selectionMutationOwnedByDataGrid)
        {
            return false;
        }

        object[] oldSelection = _selectedItemsSnapshot;
        _selectedItemsLookup.Clear();
        foreach (object item in _selectedItems)
        {
            _selectedItemsLookup.Add(item);
        }

        if (SelectionMode == DataGridSelectionMode.Single && _selectedItems.Count > 1)
        {
            object retained = e.NewItems?.Cast<object>().LastOrDefault() ?? _selectedItems[0];
            ClearSelection();
            AddToSelection(retained);
        }

        UpdateSelectionPropertiesFromSelectedItems();
        UpdateRowSelectionVisuals();
        RaiseSelectionChangedIfNeeded(oldSelection);
        return false;
    }

    private void SelectRow(int rowIndex, ModifierKeys modifiers)
    {
        if (rowIndex < 0 || rowIndex >= _items.Count) return;

        var item = _items[rowIndex];
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
                        AddToSelection(_items[i]);
                }
                // Do not update anchor on Shift+Click
            }
            else
            {
                ClearSelection();
                AddToSelection(item);
                _selectionAnchorIndex = rowIndex;
            }
        }

        UpdateSelectionPropertiesFromSelectedItems(rowIndex);

        // Update row visual states
        UpdateRowSelectionVisuals();

        RaiseSelectionChangedIfNeeded(oldSelectedItems);
    }

    private void SelectCell(int rowIndex, int columnIndex, ModifierKeys modifiers)
    {
        if (rowIndex < 0 || rowIndex >= _items.Count ||
            columnIndex < 0 || columnIndex >= Columns.Count)
        {
            return;
        }

        var cellInfo = new DataGridCellInfo(_items[rowIndex], Columns[columnIndex]);
        if (SelectionMode == DataGridSelectionMode.Single || modifiers == ModifierKeys.None)
        {
            _selectedCells.ReplaceWith([cellInfo]);
        }
        else if (modifiers.HasFlag(ModifierKeys.Control))
        {
            if (_selectedCells.Contains(cellInfo))
            {
                _selectedCells.Remove(cellInfo);
            }
            else
            {
                _selectedCells.Add(cellInfo);
            }
        }
        else if (modifiers.HasFlag(ModifierKeys.Shift) && CurrentCell.IsValid)
        {
            var anchorRow = _items.IndexOf(CurrentCell.Item!);
            var anchorColumn = CurrentCell.Column == null ? -1 : Columns.IndexOf(CurrentCell.Column);
            if (anchorRow >= 0 && anchorColumn >= 0)
            {
                var cells = new List<DataGridCellInfo>();
                for (var row = Math.Min(anchorRow, rowIndex); row <= Math.Max(anchorRow, rowIndex); row++)
                {
                    for (var column = Math.Min(anchorColumn, columnIndex); column <= Math.Max(anchorColumn, columnIndex); column++)
                    {
                        cells.Add(new DataGridCellInfo(_items[row], Columns[column]));
                    }
                }

                _selectedCells.ReplaceWith(cells);
            }
        }

        UpdateRowSelectionVisuals();
    }

    private void UpdateRowSelectionVisuals()
    {
        foreach (var row in _realizedRows.Values)
        {
            row.IsSelected = row.DataItem != null && IsItemSelected(row.DataItem);
            if (row.RowHeader != null)
            {
                row.RowHeader.IsRowSelected = row.IsSelected;
            }

            foreach (var pair in row.CellsByColumn)
            {
                pair.Value.IsSelected = row.DataItem != null &&
                    _selectedCells.Contains(new DataGridCellInfo(row.DataItem, Columns[pair.Key]));
            }
        }
    }

    internal override void SelectAllCore()
    {
        if (SelectionMode != DataGridSelectionMode.Extended) return;

        var oldSelectedItems = _selectedItems.ToArray();
        ClearSelection();
        foreach (var item in _items)
            AddToSelection(item);
        UpdateSelectionPropertiesFromSelectedItems(preferredIndex: SelectedIndex);

        UpdateRowSelectionVisuals();
        RaiseSelectionChangedIfNeeded(oldSelectedItems);
    }

    internal override void UnselectAllCore()
    {
        var oldSelectedItems = _selectedItems.ToArray();
        ClearSelection();
        UpdateSelectionPropertiesFromSelectedItems();

        UpdateRowSelectionVisuals();
        RaiseSelectionChangedIfNeeded(oldSelectedItems);
    }

    /// <summary>Selects every cell in the grid.</summary>
    public void SelectAllCells()
    {
        if (SelectionUnit == DataGridSelectionUnit.FullRow)
        {
            SelectAll();
            return;
        }

        if (_items.Count == 0 || Columns.Count == 0)
        {
            return;
        }

        var cells = new List<DataGridCellInfo>(_items.Count * Columns.Count);
        foreach (var item in _items)
        {
            foreach (var column in Columns)
            {
                cells.Add(new DataGridCellInfo(item, column));
            }
        }

        _selectedCells.ReplaceWith(cells);
        UpdateRowSelectionVisuals();
    }

    /// <summary>Clears all selected cells and, when applicable, selected rows.</summary>
    public void UnselectAllCells()
    {
        _selectedCells.ClearFromOwner();
        if (SelectionUnit != DataGridSelectionUnit.Cell)
        {
            UnselectAll();
        }
        else
        {
            UpdateRowSelectionVisuals();
        }
    }

    #endregion

    #region Sorting

    private void SortByColumn(DataGridColumn column)
    {
        if (!column.CanUserSort) return;

        var sortingArgs = new DataGridSortingEventArgs(column);
        OnSorting(sortingArgs);

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

        if (column is DataGridBoundColumn { Binding: Binding binding } && binding.Path != null)
        {
            var path = binding.Path.Path;
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

        // Reconcile SelectedIndex with the new item order
        if (SelectedItem != null)
        {
            var newIndex = _items.IndexOf(SelectedItem);
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

    private static readonly Dictionary<(Type, string), PropertyInfo?> s_propertyCache = new();

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2070:UnrecognizedReflectionPattern",
        Justification = "DataGrid is a data-bound control that resolves cell values by reflecting over user-defined item types. Every caller passes a runtime Type obtained from object.GetType() (the bound row item), which cannot carry DynamicallyAccessedMembers, so the property metadata cannot be flowed statically. Preserving the public properties of types bound to a DataGrid is the documented consumer responsibility under trimming/AOT (the same binding-reflection-fallback contract used across the framework).")]
    internal static PropertyInfo? GetCachedProperty(Type type, string name)
    {
        var key = (type, name);
        if (!s_propertyCache.TryGetValue(key, out var prop))
        {
            prop = type.GetProperty(name);
            s_propertyCache[key] = prop;
        }
        return prop;
    }

    private static object? GetPropertyValue(object obj, string propertyPath)
    {
        var current = obj;
        foreach (var part in propertyPath.Split('.'))
        {
            if (current == null) return null;
            var prop = GetCachedProperty(current.GetType(), part);
            current = prop?.GetValue(current);
        }
        return current;
    }

    #endregion

    #region Auto-Generate Columns

    /// <summary>Creates a default column for every property reported by an item-properties provider.</summary>
    public static Collection<DataGridColumn> GenerateColumns(IItemProperties itemProperties)
    {
        ArgumentNullException.ThrowIfNull(itemProperties);
        var result = new Collection<DataGridColumn>();
        var properties = itemProperties.ItemProperties;
        if (properties == null)
        {
            return result;
        }

        foreach (var property in properties)
        {
            var column = CreateAutoGeneratedColumn(property.Name, property.PropertyType);
            column.SetIsAutoGenerated(true);
            result.Add(column);
        }

        return result;
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075:UnrecognizedReflectionPattern",
        Justification = "Auto-generated columns are derived by reflecting over the public instance properties of the bound item type, obtained from firstItem.GetType(). object.GetType() cannot carry DynamicallyAccessedMembers, so the metadata cannot be flowed statically. AutoGenerateColumns is an opt-in convenience; consumers relying on it must preserve their item type's public properties under trimming/AOT, the documented binding-reflection contract for data-bound controls.")]
    private void AutoGenerateColumnsFromSource()
    {
        if (!AutoGenerateColumns || _items.Count == 0) return;

        var firstItem = _items[0];
        var itemType = firstItem.GetType();
        var properties = itemType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Columns.Clear();
        foreach (var prop in properties)
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;

            var eventArgs = new DataGridAutoGeneratingColumnEventArgs(
                CreateAutoGeneratedColumn(prop),
                prop.Name,
                prop.PropertyType,
                prop);

            OnAutoGeneratingColumn(eventArgs);
            if (eventArgs.Cancel || eventArgs.Column == null)
            {
                continue;
            }

            eventArgs.Column.SetIsAutoGenerated(true);
            Columns.Add(eventArgs.Column);
        }

        OnAutoGeneratedColumns(EventArgs.Empty);
    }

    private static DataGridColumn CreateAutoGeneratedColumn(PropertyInfo property)
        => CreateAutoGeneratedColumn(property.Name, property.PropertyType);

    private static DataGridColumn CreateAutoGeneratedColumn(string propertyName, Type propertyType)
    {
        var effectiveType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (effectiveType == typeof(bool))
        {
            return new DataGridCheckBoxColumn
            {
                Header = propertyName,
                Binding = new Binding(propertyName),
                IsThreeState = Nullable.GetUnderlyingType(propertyType) != null
            };
        }

        return new DataGridTextColumn
        {
            Header = propertyName,
            Binding = new Binding(propertyName)
        };
    }

    #endregion

    #region Input Handling

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

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
                if (SelectedIndex < _items.Count - 1)
                {
                    SelectedIndex++;
                    ScrollSelectedIntoView();
                    e.Handled = true;
                }
                break;
            case Key.Home:
                if (_items.Count > 0)
                {
                    SelectedIndex = 0;
                    ScrollSelectedIntoView();
                    e.Handled = true;
                }
                break;
            case Key.End:
                if (_items.Count > 0)
                {
                    SelectedIndex = _items.Count - 1;
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
            case Key.F2:
                BeginEdit();
                e.Handled = true;
                break;
            case Key.Delete:
                if (_currentEditingCell == null && TryDeleteSelectedRows())
                {
                    e.Handled = true;
                }
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

    #endregion

    #region Property Changed Callbacks

    private static void OnDataGridIsReadOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid dataGrid)
        {
            return;
        }

        if (e.NewValue is true && dataGrid._currentEditingCell != null)
        {
            dataGrid.CancelEdit();
        }

        foreach (var column in dataGrid.Columns)
        {
            column.CoerceValue(DataGridColumn.IsReadOnlyProperty);
        }

        foreach (var row in dataGrid._realizedRows.Values)
        {
            foreach (var cell in row.Cells)
            {
                cell.CoerceValue(DataGridCell.IsReadOnlyProperty);
            }
        }

        if (dataGrid._placeholderRow != null)
        {
            foreach (var cell in dataGrid._placeholderRow.Cells)
            {
                cell.CoerceValue(DataGridCell.IsReadOnlyProperty);
            }
        }

        dataGrid.RefreshRows();
    }

    /// <inheritdoc />
    protected override void OnItemsSourceChanged(IEnumerable? oldValue, IEnumerable? newValue)
    {
        base.OnItemsSourceChanged(oldValue, newValue);
        RefreshItems();
    }

    /// <inheritdoc />
    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems != null:
                HandleItemsAdded(e.NewStartingIndex, e.NewItems);
                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems != null:
                HandleItemsRemoved(e.OldStartingIndex, e.OldItems);
                break;
            case NotifyCollectionChangedAction.Replace when e.NewItems != null && e.OldItems != null:
                HandleItemsReplaced(e.OldStartingIndex, e.OldItems, e.NewItems);
                break;
            case NotifyCollectionChangedAction.Move when e.NewItems != null:
                HandleItemsMoved(e.OldStartingIndex, e.NewStartingIndex, e.NewItems.Count);
                break;
            default:
                // Reset or unknown action — full refresh
                RefreshItems();
                break;
        }
    }

    private void HandleItemsAdded(int startIndex, System.Collections.IList newItems)
    {
        var oldSelectedItems = _selectedItems.ToArray();
        var insertIndex = startIndex >= 0 ? startIndex : _items.Count;

        foreach (var item in newItems)
        {
            _items.Insert(insertIndex++, item);
        }

        if (AutoGenerateColumns && Columns.Count == 0)
        {
            AutoGenerateColumnsFromSource();
        }

        RefreshRows();
        InvalidateMeasure();
        RaiseSelectionChangedIfNeeded(oldSelectedItems);
    }

    private void HandleItemsRemoved(int startIndex, System.Collections.IList oldItems)
    {
        var oldSelectedItems = _selectedItems.ToArray();

        if (startIndex >= 0)
        {
            // Remove by index from end to start to preserve positions
            for (var i = oldItems.Count - 1; i >= 0; i--)
            {
                var removeIndex = startIndex + i;
                if (removeIndex < _items.Count)
                {
                    var item = _items[removeIndex];
                    _items.RemoveAt(removeIndex);
                    if (IsItemSelected(item))
                    {
                        RemoveFromSelection(item);
                    }
                }
            }
        }
        else
        {
            // Fallback: remove by reference when index is unknown
            foreach (var item in oldItems)
            {
                _items.Remove(item);
                if (item != null && IsItemSelected(item))
                {
                    RemoveFromSelection(item);
                }
            }
        }

        UpdateSelectionPropertiesFromSelectedItems(preferredIndex: SelectedIndex);
        ReconcileCellStateWithItems();
        RefreshRows();
        InvalidateMeasure();
        RaiseSelectionChangedIfNeeded(oldSelectedItems);
    }

    private void HandleItemsReplaced(int startIndex, System.Collections.IList oldItems, System.Collections.IList newItems)
    {
        var oldSelectedItems = _selectedItems.ToArray();

        for (var i = 0; i < oldItems.Count; i++)
        {
            var oldItem = oldItems[i]!;
            var newItem = newItems[i]!;
            var index = startIndex >= 0 ? startIndex + i : _items.IndexOf(oldItem);
            if (index >= 0)
            {
                _items[index] = newItem;
            }

            if (IsItemSelected(oldItem))
            {
                RemoveFromSelection(oldItem);
                AddToSelection(newItem);
            }
        }

        UpdateSelectionPropertiesFromSelectedItems(preferredIndex: SelectedIndex);
        ReconcileCellStateWithItems();
        RefreshRows();
        InvalidateMeasure();
        RaiseSelectionChangedIfNeeded(oldSelectedItems);
    }

    private void HandleItemsMoved(int oldStartIndex, int newStartIndex, int count)
    {
        if (oldStartIndex < 0 || newStartIndex < 0) { RefreshItems(); return; }

        var movedItems = _items.GetRange(oldStartIndex, count);
        _items.RemoveRange(oldStartIndex, count);
        _items.InsertRange(newStartIndex, movedItems);

        RefreshRows();
        InvalidateMeasure();
    }

    private new void RefreshItems()
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
        UpdateLayoutMetrics();
        RefreshColumnHeaders();
        RefreshRows();
        InvalidateMeasure();
        RaiseSelectionChangedIfNeeded(oldSelectedItems);
    }

    private void ReconcileSelectionWithItems()
    {
        var toRemove = _selectedItems.Where(item => !_items.Contains(item)).ToArray();
        foreach (var item in toRemove)
            RemoveFromSelection(item);

        if (SelectionMode == DataGridSelectionMode.Single && _selectedItems.Count > 1)
        {
            var retained = _selectedItems[0];
            ClearSelection();
            AddToSelection(retained);
        }

        UpdateSelectionPropertiesFromSelectedItems(preferredIndex: SelectedIndex);
        ReconcileCellStateWithItems();
    }

    private void ReconcileCellStateWithItems()
    {
        _selectedCells.ReplaceWith(_selectedCells.Where(cell =>
            cell.Item != null && _items.Contains(cell.Item) &&
            cell.Column != null && Columns.Contains(cell.Column)));

        if (CurrentCell.Item != null && !_items.Contains(CurrentCell.Item))
        {
            CurrentCell = default;
        }
    }

    /// <summary>Determines whether the begin-edit command can execute.</summary>
    protected virtual void OnCanExecuteBeginEdit(CanExecuteRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        e.CanExecute = IsEnabled && !IsReadOnly && _currentEditingCell == null &&
            (CurrentCell.IsValid || SelectedIndex >= 0);
        e.Handled = true;
    }

    /// <summary>Determines whether the cancel-edit command can execute.</summary>
    protected virtual void OnCanExecuteCancelEdit(CanExecuteRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        e.CanExecute = _currentEditingCell != null;
        e.Handled = true;
    }

    /// <summary>Determines whether the commit-edit command can execute.</summary>
    protected virtual void OnCanExecuteCommitEdit(CanExecuteRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        e.CanExecute = _currentEditingCell != null;
        e.Handled = true;
    }

    /// <summary>Determines whether the copy command can execute.</summary>
    protected virtual void OnCanExecuteCopy(CanExecuteRoutedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        args.CanExecute = ClipboardCopyMode != DataGridClipboardCopyMode.None &&
            (_selectedItems.Count > 0 || _selectedCells.Count > 0);
        args.Handled = true;
    }

    /// <summary>Determines whether the delete command can execute.</summary>
    protected virtual void OnCanExecuteDelete(CanExecuteRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        e.CanExecute = CanDeleteSelectedRows();
        e.Handled = true;
    }

    /// <summary>Executes the begin-edit command.</summary>
    protected virtual void OnExecutedBeginEdit(ExecutedRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        _ = BeginEdit(e);
        e.Handled = true;
    }

    /// <summary>Executes the cancel-edit command.</summary>
    protected virtual void OnExecutedCancelEdit(ExecutedRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var unit = e.Parameter is DataGridEditingUnit editingUnit
            ? editingUnit
            : DataGridEditingUnit.Cell;
        _ = CancelEdit(unit);
        e.Handled = true;
    }

    /// <summary>Executes the commit-edit command.</summary>
    protected virtual void OnExecutedCommitEdit(ExecutedRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var unit = e.Parameter is DataGridEditingUnit editingUnit
            ? editingUnit
            : DataGridEditingUnit.Cell;
        _ = CommitEdit(unit, exitEditingMode: true);
        e.Handled = true;
    }

    /// <summary>Executes the copy command.</summary>
    protected virtual void OnExecutedCopy(ExecutedRoutedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var text = BuildClipboardText();
        if (text.Length > 0)
        {
            WpfClipboard.SetText(text);
        }

        args.Handled = true;
    }

    /// <summary>Executes the delete command.</summary>
    protected virtual void OnExecutedDelete(ExecutedRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        _ = TryDeleteSelectedRows();
        e.Handled = true;
    }

    private void OnCanExecuteCommand(object sender, CanExecuteRoutedEventArgs e)
    {
        if (ReferenceEquals(e.Command, BeginEditCommand))
        {
            OnCanExecuteBeginEdit(e);
        }
        else if (ReferenceEquals(e.Command, CancelEditCommand))
        {
            OnCanExecuteCancelEdit(e);
        }
        else if (ReferenceEquals(e.Command, CommitEditCommand))
        {
            OnCanExecuteCommitEdit(e);
        }
        else if (ReferenceEquals(e.Command, DeleteCommand))
        {
            OnCanExecuteDelete(e);
        }
        else if (ReferenceEquals(e.Command, ApplicationCommands.Copy))
        {
            OnCanExecuteCopy(e);
        }
        else if (ReferenceEquals(e.Command, SelectAllCommand))
        {
            e.CanExecute = IsEnabled && _items.Count > 0;
            e.Handled = true;
        }
    }

    private void OnExecutedCommand(object sender, ExecutedRoutedEventArgs e)
    {
        if (ReferenceEquals(e.Command, BeginEditCommand))
        {
            OnExecutedBeginEdit(e);
        }
        else if (ReferenceEquals(e.Command, CancelEditCommand))
        {
            OnExecutedCancelEdit(e);
        }
        else if (ReferenceEquals(e.Command, CommitEditCommand))
        {
            OnExecutedCommitEdit(e);
        }
        else if (ReferenceEquals(e.Command, DeleteCommand))
        {
            OnExecutedDelete(e);
        }
        else if (ReferenceEquals(e.Command, ApplicationCommands.Copy))
        {
            OnExecutedCopy(e);
        }
        else if (ReferenceEquals(e.Command, SelectAllCommand))
        {
            SelectAll();
            e.Handled = true;
        }
    }

    private static void OnCurrentItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid dataGrid || dataGrid._isSynchronizingCurrentCell)
        {
            return;
        }

        dataGrid.CurrentCell = DataGridCellInfo.CreatePossiblyPartial(e.NewValue, dataGrid.CurrentColumn);
    }

    private static void OnCurrentColumnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid dataGrid || dataGrid._isSynchronizingCurrentCell)
        {
            return;
        }

        dataGrid.CurrentCell = DataGridCellInfo.CreatePossiblyPartial(
            dataGrid.CurrentItem, e.NewValue as DataGridColumn);
    }

    private static void OnCurrentCellPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid dataGrid)
        {
            return;
        }

        var oldCell = e.OldValue is DataGridCellInfo oldInfo ? oldInfo : default;
        var newCell = e.NewValue is DataGridCellInfo newInfo ? newInfo : default;
        if (oldCell == newCell)
        {
            return;
        }

        if (dataGrid._currentEditingCell != null &&
            (!Equals(oldCell.Item, newCell.Item) || !Equals(oldCell.Column, newCell.Column)))
        {
            dataGrid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
        }

        dataGrid._isSynchronizingCurrentCell = true;
        try
        {
            dataGrid.SetValue(CurrentItemProperty, newCell.Item);
            dataGrid.SetValue(CurrentColumnProperty, newCell.Column);
        }
        finally
        {
            dataGrid._isSynchronizingCurrentCell = false;
        }

        dataGrid.OnCurrentCellChanged(EventArgs.Empty);
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
                        dataGrid.ClearSelection();
                        dataGrid.AddToSelection(newItem);
                        dataGrid.SetValue(SelectedIndexProperty, index);
                    }
                    else
                    {
                        dataGrid.ClearSelection();
                        dataGrid.SetValue(SelectedIndexProperty, -1);
                        dataGrid.SetValue(SelectedItemProperty, null);
                    }
                }
                else
                {
                    dataGrid.ClearSelection();
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
                    dataGrid.ClearSelection();
                    dataGrid.AddToSelection(item);
                    dataGrid.SetValue(SelectedItemProperty, item);
                }
                else
                {
                    dataGrid.ClearSelection();
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

    private static void OnCellAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid)
        {
            dataGrid.RefreshRows();
        }
    }

    private static void OnColumnHeaderAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid)
        {
            dataGrid.RefreshColumnHeaders();
        }
    }

    private static void OnRowAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid)
        {
            dataGrid.RefreshRows();
        }
    }

    private static void OnColumnSizingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid)
        {
            dataGrid.UpdateLayoutMetrics();
            dataGrid.RefreshColumnHeaders();
            dataGrid.RefreshRows();
            dataGrid.InvalidateMeasure();
        }
    }

    private static void OnRowSizingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid)
        {
            dataGrid.RefreshRows();
            dataGrid.InvalidateMeasure();
        }
    }

    private static void OnScrollBarVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid)
        {
            dataGrid.ApplyScrollBarVisibility();
            dataGrid.SyncColumnHeadersHorizontalScroll();
            dataGrid.InvalidateMeasure();
        }
    }

    private static void OnRowValidationErrorTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid dataGrid)
        {
            return;
        }

        foreach (var row in dataGrid._realizedRows.Values)
        {
            dataGrid.ApplyRowValidationErrorTemplate(row);
        }

        if (dataGrid._placeholderRow != null)
        {
            dataGrid.ApplyRowValidationErrorTemplate(dataGrid._placeholderRow);
        }
    }

    private void OnRowValidationRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var row in _realizedRows.Values)
        {
            Validation.ClearInvalid(row);
        }
    }

    private static void OnSelectionModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid dataGrid || e.NewValue is not DataGridSelectionMode newMode)
        {
            return;
        }

        dataGrid.CanSelectMultipleItems = newMode != DataGridSelectionMode.Single;
        if (newMode == DataGridSelectionMode.Single)
        {
            if (dataGrid._selectedItems.Count > 1)
            {
                var oldSelectedItems = dataGrid._selectedItems.ToArray();
                var retainedItem = dataGrid.SelectedItem ?? dataGrid._selectedItems[0];
                dataGrid.ClearSelection();
                dataGrid.AddToSelection(retainedItem);
                dataGrid.UpdateSelectionPropertiesFromSelectedItems(dataGrid._items.IndexOf(retainedItem));
                dataGrid.UpdateRowSelectionVisuals();
                dataGrid.RaiseSelectionChangedIfNeeded(oldSelectedItems);
            }

            if (dataGrid._selectedCells.Count > 1 &&
                (dataGrid.SelectionUnit == DataGridSelectionUnit.Cell ||
                 (dataGrid.SelectionUnit == DataGridSelectionUnit.CellOrRowHeader &&
                  dataGrid._selectedItems.Count == 0)))
            {
                dataGrid._selectedCells.ReplaceWith([dataGrid._selectedCells[0]]);
            }
        }
    }

    private static void OnSelectionUnitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid && !Equals(e.OldValue, e.NewValue))
        {
            dataGrid._selectedCells.ClearFromOwner();
            dataGrid.UnselectAll();
        }
    }

    private static void OnHeadersVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid)
        {
            dataGrid.UpdateHeadersVisibility();
            dataGrid.RefreshColumnHeaders();
            dataGrid.RefreshRows();
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
            dataGrid.UpdateLayoutMetrics();
            dataGrid.RefreshColumnHeaders();
            dataGrid.RefreshRows();
            dataGrid.InvalidateMeasure();
        }
    }

    private static void OnRowDetailsConfigurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid dataGrid)
        {
            return;
        }

        var templateChanged = e.Property == RowDetailsTemplateProperty ||
            e.Property == RowDetailsTemplateSelectorProperty;
        foreach (var row in dataGrid._realizedRows.Values)
        {
            if (templateChanged)
            {
                dataGrid.OnUnloadingRowDetailsWrapper(row);
                row.ApplyOwnerDetailsSettings(dataGrid.RowDetailsTemplate, dataGrid.RowDetailsTemplateSelector);
            }

            dataGrid.ApplyRowDetailsVisibility(row, raiseVisibilityChanged: true);
            dataGrid.OnLoadingRowDetailsWrapper(row);
        }

        dataGrid.InvalidateMeasure();
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
                OnColumnDisplayIndexChanged(new DataGridColumnEventArgs(column));
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

    /// <summary>Returns the column whose display index equals <paramref name="displayIndex"/>.</summary>
    public DataGridColumn ColumnFromDisplayIndex(int displayIndex)
    {
        if (displayIndex < 0 || displayIndex >= Columns.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(displayIndex), displayIndex,
                "DisplayIndex must be greater than or equal to zero and less than Columns.Count.");
        }

        return Columns.First(column => column.DisplayIndex == displayIndex);
    }

    private void OnColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var oldItem in e.OldItems)
            {
                if (oldItem is DataGridColumn oldColumn && oldColumn.DataGridOwner == this)
                {
                    oldColumn.SetDataGridOwner(null);
                }
            }
        }

        if (e.NewItems != null)
        {
            foreach (var newItem in e.NewItems)
            {
                if (newItem is DataGridColumn newColumn)
                {
                    newColumn.SetDataGridOwner(this);
                }
            }
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var column in Columns)
            {
                column.SetDataGridOwner(this);
            }
        }

        foreach (var row in _realizedRows.Values)
        {
            row.OnColumnsChanged(Columns, e);
        }

        _placeholderRow?.OnColumnsChanged(Columns, e);

        var selectedCells = _selectedCells.Where(cell =>
            cell.Item != null && _items.Contains(cell.Item) &&
            cell.Column != null && Columns.Contains(cell.Column)).ToList();
        if (SelectionUnit != DataGridSelectionUnit.Cell)
        {
            foreach (var item in _selectedItems)
            {
                foreach (var column in Columns)
                {
                    var cell = new DataGridCellInfo(item, column);
                    if (!selectedCells.Contains(cell))
                    {
                        selectedCells.Add(cell);
                    }
                }
            }
        }

        _selectedCells.ReplaceWith(selectedCells);
        if (CurrentColumn != null && !Columns.Contains(CurrentColumn))
        {
            CurrentColumn = null;
        }

        SyncColumnDisplayIndexesWithCollection(raiseEvents: true);
        if (e.Action == NotifyCollectionChangedAction.Move && e.NewItems != null)
        {
            foreach (var movedItem in e.NewItems)
            {
                if (movedItem is DataGridColumn movedColumn)
                {
                    OnColumnReordered(new DataGridColumnEventArgs(movedColumn));
                }
            }
        }

        UpdateLayoutMetrics();
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

        UpdateLayoutMetrics();
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

        var effectiveWidth = GetRenderableColumnWidth(column);

        // Update column header width
        if (_columnHeadersHost != null)
        {
            for (var i = 0; i < _columnHeadersHost.Children.Count; i++)
            {
                if (_columnHeadersHost.Children[i] is DataGridColumnHeader header && header.Column == column)
                {
                    header.Width = effectiveWidth;
                }
            }
        }

        // Update cell widths in all realized rows
        foreach (var row in _realizedRows.Values)
        {
            if (row.CellsByColumn.TryGetValue(colIndex, out var cell))
            {
                cell.Width = effectiveWidth;
            }
        }

        UpdateLayoutMetrics();

        if (EnableColumnVirtualization)
        {
            UpdateRealizedRows(forceRefresh: true);
        }

        InvalidateMeasure();
    }

    #endregion

    #region Row Add / Delete and Frozen Columns

    private static void OnCanUserModifyRowsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // CanUserAddRows toggles the placeholder row; CanUserDeleteRows only
        // gates a key handler. A single refresh covers both — it is a no-op
        // before the template is applied.
        if (d is DataGrid dataGrid)
        {
            dataGrid.RefreshRows();
        }
    }

    private static void OnFrozenColumnCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGrid dataGrid)
        {
            dataGrid.UpdateLayoutMetrics();
            dataGrid.RefreshColumnHeaders();
            dataGrid.RefreshRows();
        }
    }

    /// <summary>
    /// Gets whether the empty new-item placeholder row should be shown: the
    /// feature must be enabled, the grid editable, and the bound source a
    /// mutable list whose item type can be constructed.
    /// </summary>
    private bool ShowNewItemPlaceholder => CanUserAddRows && !IsReadOnly && CanAddRowsToSource();

    /// <summary>
    /// Determines whether new items can be appended to the current
    /// <see cref="ItemsSource"/>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075:UnrecognizedReflectionPattern",
        Justification = "Constructibility of the bound item type is probed for the optional new-item placeholder. The Type comes from GetNewItemType(), which ultimately derives from object.GetType() on a bound row item (or a generic argument of the source's IEnumerable<T>), neither of which can carry DynamicallyAccessedMembers. CanUserAddRows is opt-in; a consumer enabling it must preserve the item type's parameterless constructor under trimming/AOT. The result is also defensively guarded: a missing/trimmed constructor returns false here and Activator.CreateInstance is wrapped in try/catch at CommitNewItem.")]
    private bool CanAddRowsToSource()
    {
        if (ItemsSource is not IList list || list.IsReadOnly || list.IsFixedSize)
        {
            return false;
        }

        var itemType = GetNewItemType();
        if (itemType == null || itemType.IsAbstract || itemType.IsInterface)
        {
            return false;
        }

        // Value types are always constructible; reference types need a public
        // parameterless constructor (this is why e.g. string is rejected).
        return itemType.IsValueType || itemType.GetConstructor(Type.EmptyTypes) != null;
    }

    /// <summary>
    /// Resolves the element type of the bound items so a new instance can be
    /// created for the placeholder row. An existing item is the most reliable
    /// source; otherwise the source's <see cref="IEnumerable{T}"/> argument is
    /// used.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075:UnrecognizedReflectionPattern",
        Justification = "The element type of the bound source is recovered by walking source.GetType().GetInterfaces() for IEnumerable<T>. object.GetType() cannot carry DynamicallyAccessedMembers, so the interface list cannot be flowed statically. This only resolves an open generic shape (IEnumerable<T>) so the placeholder row can construct a new item; it is part of the opt-in CanUserAddRows feature whose item type preservation is the documented consumer responsibility under trimming/AOT.")]
    private Type? GetNewItemType()
    {
        if (_items.Count > 0 && _items[0] != null)
        {
            return _items[0]!.GetType();
        }

        var source = ItemsSource;
        if (source == null)
        {
            return null;
        }

        foreach (var iface in source.GetType().GetInterfaces())
        {
            if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return iface.GetGenericArguments()[0];
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the empty placeholder row that sits at the bottom of the grid
    /// while <see cref="CanUserAddRows"/> is active.
    /// </summary>
    private DataGridRow BuildPlaceholderRow(double rowHeight)
    {
        var (columnStart, columnEnd) = GetVisibleColumnRange();

        var row = new DataGridRow
        {
            DataItem = null,
            RowIndex = -1,
            IsNewItemPlaceholder = true,
            Height = double.IsNaN(RowHeight) ? double.NaN : rowHeight,
            MinHeight = MinRowHeight,
            ParentDataGrid = this,
            VisibleColumnStart = columnStart,
            VisibleColumnEnd = columnEnd,
        };
        SetAlternationIndex(row, _items.Count);

        var rowSelector = (StyleSelector?)GetValue(RowStyleSelectorProperty);
        var rowStyle = rowSelector?.SelectStyle(row, row) ?? (Style?)GetValue(RowStyleProperty);
        if (rowStyle != null)
        {
            row.Style = rowStyle;
            row.AppliedOwnerStyle = rowStyle;
        }

        if (RowBackground != null)
        {
            row.Background = RowBackground;
        }

        row.RowHeader = CreateRowHeader(row, null);
        ApplyRowValidationErrorTemplate(row);

        if (columnStart >= 0 && columnEnd >= 0 && Columns.Count > 0)
        {
            for (var colIndex = columnStart; colIndex <= columnEnd && colIndex < Columns.Count; colIndex++)
            {
                var column = Columns[colIndex];
                if (!IsColumnVisible(column))
                {
                    continue;
                }

                var cell = new DataGridCell
                {
                    Width = GetRenderableColumnWidth(column),
                    Column = column,
                };

                var cellStyle = column.CellStyle ?? (Style?)GetValue(CellStyleProperty);
                if (cellStyle != null)
                {
                    cell.Style = cellStyle;
                }

                row.Cells.Add(cell);
                row.CellsByColumn[colIndex] = cell;
                ApplyFrozenCellState(cell, colIndex);
            }
        }

        AttachRowInput(row);
        return row;
    }

    /// <summary>
    /// Creates a new item, appends it to the bound source, then selects and
    /// edits the resulting row. Invoked when the user activates the placeholder.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2072:UnrecognizedReflectionPattern",
        Justification = "The new placeholder item is created via Activator.CreateInstance over the bound item type from GetNewItemType(), whose value derives from object.GetType() (or an IEnumerable<T> generic argument) and cannot carry DynamicallyAccessedMembers(PublicParameterlessConstructor). CanUserAddRows is opt-in and the item type's constructor preservation is the documented consumer responsibility under trimming/AOT; the call is additionally guarded by CanAddRowsToSource() and wrapped in try/catch so a trimmed/throwing constructor leaves the grid unchanged.")]
    private void CommitNewItem()
    {
        if (!ShowNewItemPlaceholder || ItemsSource is not IList list)
        {
            return;
        }

        var itemType = GetNewItemType();
        if (itemType == null)
        {
            return;
        }

        var addingArgs = new AddingNewItemEventArgs();
        OnAddingNewItem(addingArgs);

        object? newItem = addingArgs.NewItem;
        if (newItem == null)
        {
        try
        {
            newItem = Activator.CreateInstance(itemType);
        }
        catch (Exception)
        {
            // A type that looked constructible but throws from its constructor
            // simply cannot back a new row — leave the grid unchanged.
            return;
        }
        }

        if (newItem == null)
        {
            return;
        }

        list.Add(newItem);
        OnInitializingNewItem(new InitializingNewItemEventArgs(newItem));

        // An observable source has already funneled the insertion through
        // OnSourceCollectionChanged; a plain list has not, so sync manually.
        if (ItemsSource is not INotifyCollectionChanged)
        {
            _items.Add(newItem);
            RefreshRows();
            InvalidateMeasure();
        }

        var newIndex = _items.IndexOf(newItem);
        if (newIndex >= 0)
        {
            SelectedIndex = newIndex;
            ScrollIntoView(newItem);
            BeginEdit();
        }
    }

    /// <summary>
    /// Removes every selected row from the bound source when row deletion is
    /// permitted. Returns whether anything was deleted.
    /// </summary>
    private bool TryDeleteSelectedRows()
    {
        if (!CanDeleteSelectedRows() || ItemsSource is not IList list)
        {
            return false;
        }

        // Snapshot first: removing from the source mutates _selectedItems via
        // the collection-changed handler while we iterate.
        var toDelete = _selectedItems.ToArray();
        var firstIndex = int.MaxValue;
        foreach (var item in toDelete)
        {
            var index = _items.IndexOf(item);
            if (index >= 0 && index < firstIndex)
            {
                firstIndex = index;
            }
        }

        var observable = ItemsSource is INotifyCollectionChanged;
        foreach (var item in toDelete)
        {
            list.Remove(item);
            if (!observable)
            {
                _items.Remove(item);
                RemoveFromSelection(item);
            }
        }

        if (!observable)
        {
            RefreshRows();
            InvalidateMeasure();
        }

        // Move the selection to the row that fell into the deleted slot.
        if (_items.Count > 0)
        {
            SelectedIndex = Math.Clamp(firstIndex == int.MaxValue ? 0 : firstIndex, 0, _items.Count - 1);
        }
        else
        {
            SelectedIndex = -1;
        }

        return true;
    }

    /// <summary>
    /// Lifts a cell that belongs to a frozen column above the scrolling cells
    /// and counter-translates it to the current horizontal offset so it stays
    /// pinned to the left edge.
    /// </summary>
    private void ApplyFrozenCellState(DataGridCell cell, int columnIndex)
    {
        if (columnIndex >= FrozenColumnCount)
        {
            return;
        }

        Panel.SetZIndex(cell, FrozenColumnZIndex);
        // Frozen cells overlap scrolling cells once translated, so they need an
        // opaque fill to occlude what passes underneath.
        cell.Background = ResolveFrozenCellBackground();
        cell.RenderOffset = new Point(GetHorizontalScrollOffset(), 0);
    }

    /// <summary>
    /// Lifts a frozen column header above the scrolling headers and pins it to
    /// the current horizontal offset.
    /// </summary>
    private void ApplyFrozenHeaderState(DataGridColumnHeader header, int columnIndex)
    {
        if (columnIndex >= FrozenColumnCount)
        {
            return;
        }

        Panel.SetZIndex(header, FrozenColumnZIndex);
        header.RenderOffset = new Point(GetHorizontalScrollOffset(), 0);
    }

    /// <summary>
    /// Re-applies the pinning translation to every realized frozen cell and
    /// header. Called on each horizontal scroll change.
    /// </summary>
    private void UpdateFrozenColumnOffsets()
    {
        var offset = new Point(GetHorizontalScrollOffset(), 0);

        foreach (var row in _realizedRows.Values)
        {
            if (row.RowHeader != null)
            {
                row.RowHeader.RenderOffset = offset;
            }

            if (FrozenColumnCount > 0)
            {
                ApplyFrozenOffsetToRow(row, offset);
            }
        }

        if (_placeholderRow != null)
        {
            if (_placeholderRow.RowHeader != null)
            {
                _placeholderRow.RowHeader.RenderOffset = offset;
            }

            if (FrozenColumnCount > 0)
            {
                ApplyFrozenOffsetToRow(_placeholderRow, offset);
            }
        }

        if (FrozenColumnCount > 0 && _columnHeadersHost != null)
        {
            foreach (UIElement child in _columnHeadersHost.Children)
            {
                if (child is DataGridColumnHeader header &&
                    header.Column != null &&
                    Columns.IndexOf(header.Column) < FrozenColumnCount)
                {
                    header.RenderOffset = offset;
                }
            }
        }
    }

    private void ApplyFrozenOffsetToRow(DataGridRow row, Point offset)
    {
        foreach (var pair in row.CellsByColumn)
        {
            if (pair.Key < FrozenColumnCount)
            {
                pair.Value.RenderOffset = offset;
            }
        }
    }

    private double GetHorizontalScrollOffset() => _dataScrollViewer?.HorizontalOffset ?? 0.0;

    /// <summary>
    /// Resolves an opaque brush for frozen cells. The grid's own backgrounds are
    /// preferred so the pinned column blends in; a theme color is the fallback.
    /// </summary>
    private Brush ResolveFrozenCellBackground()
        => RowBackground
           ?? Background
           ?? TryFindResource("ControlBackground") as Brush
           ?? s_frozenCellFallbackBrush;

    #endregion

    #region Editing

    /// <summary>
    /// Causes the cell being edited to enter edit mode.
    /// </summary>
    public bool BeginEdit() => BeginEdit(null!);

    /// <summary>Begins editing and records the input event that initiated the edit.</summary>
    public bool BeginEdit(RoutedEventArgs editingEventArgs)
    {
        if (IsReadOnly || _currentEditingCell != null) return false;

        var currentRowIndex = CurrentCell.Item == null ? -1 : _items.IndexOf(CurrentCell.Item);
        var row = GetOrRealizeRow(currentRowIndex >= 0 ? currentRowIndex : SelectedIndex);
        if (row == null) return false;

        if (CurrentCell.IsValid && CurrentCell.Column != null)
        {
            var currentColumnIndex = Columns.IndexOf(CurrentCell.Column);
            if (currentColumnIndex >= 0 && !CurrentCell.Column.IsReadOnly)
            {
                if (!row.CellsByColumn.TryGetValue(currentColumnIndex, out var currentCell))
                {
                    EnsureColumnVisible(currentColumnIndex);
                    row = GetOrRealizeRow(currentRowIndex >= 0 ? currentRowIndex : SelectedIndex);
                }

                if (row != null && row.CellsByColumn.TryGetValue(currentColumnIndex, out currentCell))
                {
                    return BeginEditCell(row, currentCell, CurrentCell.Column, editingEventArgs);
                }
            }
        }

        // Find the first editable column
        for (int i = 0; i < Columns.Count; i++)
        {
            if (!Columns[i].IsReadOnly)
            {
                if (row is null || !row.CellsByColumn.TryGetValue(i, out var cell))
                {
                    EnsureColumnVisible(i);
                    row = GetOrRealizeRow(SelectedIndex);
                    if (row == null || !row.CellsByColumn.TryGetValue(i, out cell))
                    {
                        continue;
                    }
                }

                return BeginEditCell(row, cell, Columns[i], editingEventArgs);
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

        var endingArgs = new DataGridCellEditEndingEventArgs(
            _currentEditingColumn!,
            _currentEditingRow!,
            _currentEditingCell.EditingElement ?? _currentEditingCell,
            DataGridEditAction.Commit);
        OnCellEditEnding(endingArgs);

        if (endingArgs.Cancel) return false;

        if (editingUnit == DataGridEditingUnit.Row)
        {
            var rowEndingArgs = new DataGridRowEditEndingEventArgs(
                _currentEditingRow!, DataGridEditAction.Commit);
            OnRowEditEnding(rowEndingArgs);
            if (rowEndingArgs.Cancel) return false;
        }

        // Write back the edited value to the data item
        if (_currentEditingColumn != null && _currentEditingRow?.DataItem != null && _currentEditingCell.EditingElement != null)
        {
            if (!_currentEditingColumn.CommitCellEditInternal(
                    _currentEditingCell.EditingElement,
                    _currentEditingRow.DataItem))
            {
                return false;
            }
        }

        if (editingUnit == DataGridEditingUnit.Row &&
            _currentEditingRow != null &&
            !ValidateRow(_currentEditingRow))
        {
            return false;
        }

        if (exitEditingMode)
        {
            _currentEditingCell.IsEditing = false;
            _currentEditingRow!.IsEditing = false;

            // Refresh the display element to show the updated value
            RefreshCellDisplay(_currentEditingCell, _currentEditingColumn!, _currentEditingRow!);

            _currentEditingCell = null;
            _currentEditingColumn = null;
            _currentEditingRow = null;
        }

        return true;
    }

    private bool CanDeleteSelectedRows() =>
        CanUserDeleteRows && !IsReadOnly && _selectedItems.Count > 0 &&
        ItemsSource is IList { IsReadOnly: false, IsFixedSize: false };

    private string BuildClipboardText()
    {
        if (ClipboardCopyMode == DataGridClipboardCopyMode.None)
        {
            return string.Empty;
        }

        var selectedRows = _selectedItems
            .Select(item => (Item: item, Index: _items.IndexOf(item)))
            .Where(pair => pair.Index >= 0)
            .OrderBy(pair => pair.Index)
            .Select(pair => pair.Item)
            .ToList();
        if (selectedRows.Count == 0)
        {
            selectedRows = _selectedCells
                .Select(cell => cell.Item)
                .Where(item => item != null)
                .Distinct()
                .OrderBy(item => _items.IndexOf(item!))
                .Cast<object>()
                .ToList();
        }

        if (selectedRows.Count == 0)
        {
            return string.Empty;
        }

        var visibleColumns = Columns
            .Where(IsColumnVisible)
            .OrderBy(column => column.DisplayIndex)
            .ToList();
        if (visibleColumns.Count == 0)
        {
            return string.Empty;
        }

        var rows = new List<string>();
        if (ClipboardCopyMode == DataGridClipboardCopyMode.IncludeHeader)
        {
            var headerArgs = new DataGridRowClipboardEventArgs(
                selectedRows[0], 0, visibleColumns.Count - 1, isColumnHeadersRow: true);
            foreach (var column in visibleColumns)
            {
                headerArgs.ClipboardRowContent.Add(
                    new DataGridClipboardCellContent(selectedRows[0], column, column.Header));
            }

            OnCopyingRowClipboardContent(headerArgs);
            rows.Add(headerArgs.FormatClipboardCellValues("\t"));
        }

        foreach (var item in selectedRows)
        {
            var rowArgs = new DataGridRowClipboardEventArgs(
                item, 0, visibleColumns.Count - 1, isColumnHeadersRow: false);
            foreach (var column in visibleColumns)
            {
                rowArgs.ClipboardRowContent.Add(
                    new DataGridClipboardCellContent(item, column,
                        column.OnCopyingCellClipboardContent(item)));
            }

            OnCopyingRowClipboardContent(rowArgs);
            rows.Add(rowArgs.FormatClipboardCellValues("\t"));
        }

        return string.Join(Environment.NewLine, rows);
    }

    private bool ValidateRow(DataGridRow row)
    {
        Validation.ClearInvalid(row);
        ApplyRowValidationErrorTemplate(row);

        if (row.DataItem == null || _rowValidationRules.Count == 0)
        {
            return true;
        }

        var isValid = true;
        foreach (var rule in _rowValidationRules)
        {
            ValidationResult result;
            try
            {
                result = rule.Validate(row.DataItem, CultureInfo.CurrentCulture);
            }
            catch (Exception exception)
            {
                result = new ValidationResult(false, exception.Message);
                Validation.MarkInvalid(
                    row,
                    new ValidationError(rule, row.DataItem, result.ErrorContent, exception));
                isValid = false;
                continue;
            }

            if (!result.IsValid)
            {
                Validation.MarkInvalid(
                    row,
                    new ValidationError(rule, row.DataItem, result.ErrorContent, null));
                isValid = false;
            }
        }

        return isValid;
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

        var endingArgs = new DataGridCellEditEndingEventArgs(
            _currentEditingColumn!,
            _currentEditingRow!,
            _currentEditingCell.EditingElement ?? _currentEditingCell,
            DataGridEditAction.Cancel);
        OnCellEditEnding(endingArgs);

        if (endingArgs.Cancel) return false;

        if (editingUnit == DataGridEditingUnit.Row)
        {
            var rowEndingArgs = new DataGridRowEditEndingEventArgs(
                _currentEditingRow!, DataGridEditAction.Cancel);
            OnRowEditEnding(rowEndingArgs);
            if (rowEndingArgs.Cancel) return false;
        }

        // Cancel the edit on the column (restore the editing element to the original value)
        if (_currentEditingColumn != null && _currentEditingRow?.DataItem != null && _currentEditingCell.EditingElement != null)
        {
            _currentEditingColumn.CancelCellEditInternal(
                _currentEditingCell.EditingElement,
                _currentEditingCell.OriginalValue);
        }

        // Setting IsEditing to false will restore the display element via the property change callback
        _currentEditingCell.IsEditing = false;
        _currentEditingRow!.IsEditing = false;
        _currentEditingCell = null;
        _currentEditingColumn = null;
        _currentEditingRow = null;

        return true;
    }

    private bool BeginEditCell(
        DataGridRow row,
        DataGridCell cell,
        DataGridColumn column,
        RoutedEventArgs? editingEventArgs)
    {
        if (column.IsReadOnly || IsReadOnly) return false;

        if (row.DataItem != null)
        {
            CurrentCell = new DataGridCellInfo(row.DataItem, column);
        }

        var beginArgs = new DataGridBeginningEditEventArgs(column, row, editingEventArgs!);
        OnBeginningEdit(beginArgs);

        if (beginArgs.Cancel) return false;

        _currentEditingCell = cell;
        _currentEditingColumn = column;
        _currentEditingRow = row;
        row.IsEditing = true;
        cell.IsEditing = true;

        if (cell.EditingElement != null)
        {
            cell.OriginalValue = column.PrepareCellForEditInternal(
                cell.EditingElement, editingEventArgs ?? new RoutedEventArgs());
        }

        var prepArgs = new DataGridPreparingCellForEditEventArgs(
            column,
            row,
            editingEventArgs!,
            cell.EditingElement ?? cell);
        OnPreparingCellForEdit(prepArgs);

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
            var displayElement = column.BuildVisualTree(isEditing: false, row.DataItem, cell);
            cell.Content = displayElement;
        }
    }

    #endregion

    #region Scrolling

    private void ScrollSelectedIntoView()
    {
        if (SelectedIndex >= 0 && SelectedIndex < _items.Count)
        {
            ScrollIntoView(_items[SelectedIndex]);
        }
    }

    public void ScrollIntoView(object item)
    {
        var index = _items.IndexOf(item);
        if (index < 0)
        {
            return;
        }

        if (EnableRowVirtualization && _dataScrollViewer != null)
        {
            _dataScrollViewer.ScrollToVerticalOffset(index * GetEffectiveRowHeight());
            UpdateRealizedRows(forceRefresh: true);
        }

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

        viewportWidth = Math.Max(0.0, viewportWidth - CellsPanelHorizontalOffset);

        if (columnIndex < FrozenColumnCount)
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

    #region Column Drag Reorder

    /// <summary>
    /// Starts a column drag reorder operation, showing a semi-transparent ghost.
    /// </summary>
    internal void StartColumnDrag(DataGridColumnHeader sourceHeader, DataGridColumn column)
    {
        if (_dragOverlay == null || _columnHeadersHost == null || !CanUserReorderColumns || !column.CanUserReorder)
            return;

        var reorderingArgs = new DataGridColumnReorderingEventArgs(column);
        OnColumnReordering(reorderingArgs);
        if (reorderingArgs.Cancel)
        {
            return;
        }

        _dragColumn = column;
        _dragSourceIndex = Columns.IndexOf(column);
        _isColumnDragging = true;
        _dragStartPoint = new Point(GetColumnStartOffset(_dragSourceIndex), 0);
        _dragLastPoint = _dragStartPoint;

        // Create ghost visual
        var headerHeight = GetEffectiveColumnHeaderHeight();
        _dragGhost = new Border
        {
            Width = GetRenderableColumnWidth(column),
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

        // Create drop indicator (vertical line)
        var accentBrush = TryFindResource("AccentBrush") as Brush ?? new SolidColorBrush(ThemeColors.Accent);
        _dropIndicator = new Border
        {
            Width = 2,
            Height = headerHeight + 8,
            Background = accentBrush,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed
        };

        if (DragIndicatorStyle != null)
        {
            _dragGhost.Style = DragIndicatorStyle;
        }

        if (DropLocationIndicatorStyle != null)
        {
            _dropIndicator.Style = DropLocationIndicatorStyle;
        }

        _dragOverlay.Children.Clear();
        _dragOverlay.Children.Add(_dropIndicator);
        _dragOverlay.Children.Add(_dragGhost);
        _dragOverlay.Visibility = Visibility.Visible;
        OnColumnHeaderDragStarted(new DragStartedEventArgs(0, 0));
    }

    /// <summary>
    /// Updates the drag ghost position and drop indicator during a column drag.
    /// </summary>
    internal void UpdateColumnDrag(Point positionInDataGrid)
    {
        if (!_isColumnDragging || _dragGhost == null || _dropIndicator == null || _columnHeadersHost == null)
            return;

        OnColumnHeaderDragDelta(new DragDeltaEventArgs(
            positionInDataGrid.X - _dragLastPoint.X,
            positionInDataGrid.Y - _dragLastPoint.Y));
        _dragLastPoint = positionInDataGrid;

        // Position ghost centered on cursor
        var ghostX = positionInDataGrid.X - _dragGhost.Width / 2;
        var ghostY = 0.0;
        Canvas.SetLeft(_dragGhost, ghostX);
        Canvas.SetTop(_dragGhost, ghostY);

        // Calculate drop target
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

    /// <summary>
    /// Completes the column drag reorder, moving the column to the drop position.
    /// </summary>
    internal void EndColumnDrag(Point positionInDataGrid)
    {
        if (!_isColumnDragging || _dragColumn == null)
        {
            CancelColumnDrag();
            return;
        }

        var (targetIndex, _) = GetDropTargetIndex(positionInDataGrid.X);
        var sourceIndex = _dragSourceIndex;

        OnColumnHeaderDragCompleted(new DragCompletedEventArgs(
            positionInDataGrid.X - _dragStartPoint.X,
            positionInDataGrid.Y - _dragStartPoint.Y,
            canceled: false));

        CleanupDragVisuals();

        if (targetIndex >= 0 && targetIndex != sourceIndex && targetIndex != sourceIndex + 1)
        {
            // Adjust target: if dropping after the source, subtract 1 because source will be removed first
            var moveTarget = targetIndex > sourceIndex ? targetIndex - 1 : targetIndex;
            moveTarget = Math.Clamp(moveTarget, 0, Columns.Count - 1);
            if (moveTarget != sourceIndex)
            {
                Columns.Move(sourceIndex, moveTarget);
            }
        }
    }

    /// <summary>
    /// Cancels an in-progress column drag reorder.
    /// </summary>
    internal void CancelColumnDrag()
    {
        if (_isColumnDragging)
        {
            OnColumnHeaderDragCompleted(new DragCompletedEventArgs(
                _dragLastPoint.X - _dragStartPoint.X,
                _dragLastPoint.Y - _dragStartPoint.Y,
                canceled: true));
        }

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

    /// <summary>
    /// Given an X position in DataGrid coordinates, returns the drop target column index
    /// and the X position for the drop indicator.
    /// </summary>
    private (int targetIndex, double indicatorX) GetDropTargetIndex(double x)
    {
        if (_columnHeadersHost == null || Columns.Count == 0)
            return (-1, 0);

        // Account for horizontal scroll offset
        var scrollOffset = _dataScrollViewer?.HorizontalOffset ?? 0;
        var adjustedX = x - CellsPanelHorizontalOffset + scrollOffset;

        var cumulative = 0.0;
        for (var i = 0; i < Columns.Count; i++)
        {
            var colWidth = GetRenderableColumnWidth(Columns[i]);
            var colMid = cumulative + colWidth / 2;

            if (adjustedX < colMid)
            {
                return (i, CellsPanelHorizontalOffset + cumulative - scrollOffset);
            }

            cumulative += colWidth;
        }

        // Past all columns — drop at end
        return (Columns.Count, CellsPanelHorizontalOffset + cumulative - scrollOffset);
    }

    private sealed class DataGridHeadersVisibilityConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (targetType != typeof(Visibility))
            {
                return DependencyProperty.UnsetValue;
            }

            var visible = value is DataGridHeadersVisibility current &&
                parameter is DataGridHeadersVisibility requested &&
                (current == DataGridHeadersVisibility.All ||
                 current == requested ||
                 (requested == DataGridHeadersVisibility.None &&
                  current is DataGridHeadersVisibility.Column or DataGridHeadersVisibility.Row));
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }

    private sealed class RowDetailsScrollingOrientationConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool isFrozen && isFrozen &&
                parameter is SelectiveScrollingOrientation orientation)
            {
                return orientation;
            }

            return SelectiveScrollingOrientation.Both;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }

    private sealed class SelectedCellCollection : Collection<DataGridCellInfo>
    {
        private readonly DataGrid _owner;
        private bool _isOwnerUpdate;

        internal SelectedCellCollection(DataGrid owner)
        {
            _owner = owner;
        }

        internal void ReplaceWith(IEnumerable<DataGridCellInfo> cells)
        {
            var replacement = cells.Distinct().ToList();
            foreach (var cell in replacement)
            {
                ValidateCell(cell);
            }

            var oldCells = this.ToList();
            _isOwnerUpdate = true;
            try
            {
                Items.Clear();
                foreach (var cell in replacement)
                {
                    Items.Add(cell);
                }
            }
            finally
            {
                _isOwnerUpdate = false;
            }

            NotifyChanges(oldCells);
        }

        internal void ClearFromOwner() => ReplaceWith(Array.Empty<DataGridCellInfo>());

        internal void UpdateForRowSelection(IEnumerable<object> addedItems, IEnumerable<object> removedItems)
        {
            var removed = new HashSet<object>(removedItems);
            var replacement = this.Where(cell => cell.Item == null || !removed.Contains(cell.Item)).ToList();
            foreach (var item in addedItems)
            {
                foreach (var column in _owner.Columns)
                {
                    var cell = new DataGridCellInfo(item, column);
                    if (!replacement.Contains(cell))
                    {
                        replacement.Add(cell);
                    }
                }
            }

            ReplaceWith(replacement);
        }

        protected override void InsertItem(int index, DataGridCellInfo item)
        {
            EnsureCanMutate();
            ValidateCell(item);
            if (Contains(item))
            {
                return;
            }

            if (_owner.SelectionMode == DataGridSelectionMode.Single && Count > 0)
            {
                ReplaceWith([item]);
                return;
            }

            var oldCells = this.ToList();
            base.InsertItem(index, item);
            NotifyChanges(oldCells);
        }

        protected override void SetItem(int index, DataGridCellInfo item)
        {
            EnsureCanMutate();
            ValidateCell(item);
            var replacement = this.ToList();
            replacement[index] = item;
            ReplaceWith(replacement);
        }

        protected override void RemoveItem(int index)
        {
            EnsureCanMutate();
            var oldCells = this.ToList();
            base.RemoveItem(index);
            NotifyChanges(oldCells);
        }

        protected override void ClearItems()
        {
            EnsureCanMutate();
            if (Count == 0)
            {
                return;
            }

            var oldCells = this.ToList();
            base.ClearItems();
            NotifyChanges(oldCells);
        }

        private void EnsureCanMutate()
        {
            if (!_isOwnerUpdate && _owner.SelectionUnit == DataGridSelectionUnit.FullRow)
            {
                throw new InvalidOperationException("Individual cells cannot be selected when SelectionUnit is FullRow.");
            }
        }

        private void ValidateCell(DataGridCellInfo cell)
        {
            if (!cell.IsValid || cell.Column == null || !_owner.Columns.Contains(cell.Column) ||
                cell.Item == null || !_owner._items.Contains(cell.Item))
            {
                throw new ArgumentException("The cell does not belong to this DataGrid.", nameof(cell));
            }
        }

        private void NotifyChanges(IList<DataGridCellInfo> oldCells)
        {
            var added = this.Where(cell => !oldCells.Contains(cell)).ToArray();
            var removed = oldCells.Where(cell => !Contains(cell)).ToArray();
            if (added.Length == 0 && removed.Length == 0)
            {
                return;
            }

            _owner.OnSelectedCellsChanged(new SelectedCellsChangedEventArgs(added, removed));
            _owner.UpdateRowSelectionVisuals();
        }
    }

    /// <summary>Scrolls both the requested row and column into the viewport.</summary>
    public void ScrollIntoView(object item, DataGridColumn column)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(column);
        if (!Columns.Contains(column))
        {
            throw new ArgumentException("The column does not belong to this DataGrid.", nameof(column));
        }

        ScrollIntoView(item);
        EnsureColumnVisible(Columns.IndexOf(column));
    }

    #endregion
}

#region DataGridRow

/// <summary>
/// Represents a row in a DataGrid.
/// </summary>
public class DataGridRow : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.DataGridRowAutomationPeer(this);
    }

    private static readonly DependencyPropertyKey IsEditingPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsEditing), typeof(bool), typeof(DataGridRow),
            new PropertyMetadata(false));

    internal static readonly DependencyPropertyKey IsNewItemPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsNewItem), typeof(bool), typeof(DataGridRow),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(nameof(Item), typeof(object), typeof(DataGridRow),
            new PropertyMetadata(null, OnItemPropertyChanged));

    public static readonly DependencyProperty ItemsPanelProperty =
        ItemsControl.ItemsPanelProperty.AddOwner(
            typeof(DataGridRow),
            new PropertyMetadata(CreateDefaultCellsPanelTemplate()));

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(DataGridRow),
            new PropertyMetadata(null, OnHeaderPropertyChanged));

    public static readonly DependencyProperty HeaderStyleProperty =
        DependencyProperty.Register(nameof(HeaderStyle), typeof(Style), typeof(DataGridRow),
            new PropertyMetadata(null, OnHeaderAppearancePropertyChanged));

    public static readonly DependencyProperty HeaderTemplateProperty =
        DependencyProperty.Register(nameof(HeaderTemplate), typeof(DataTemplate), typeof(DataGridRow),
            new PropertyMetadata(null, OnHeaderAppearancePropertyChanged));

    public static readonly DependencyProperty HeaderTemplateSelectorProperty =
        DependencyProperty.Register(nameof(HeaderTemplateSelector), typeof(DataTemplateSelector), typeof(DataGridRow),
            new PropertyMetadata(null, OnHeaderAppearancePropertyChanged));

    public static readonly DependencyProperty ValidationErrorTemplateProperty =
        DependencyProperty.Register(nameof(ValidationErrorTemplate), typeof(ControlTemplate), typeof(DataGridRow),
            new PropertyMetadata(null, OnValidationErrorTemplateChanged));

    public static readonly DependencyProperty DetailsTemplateProperty =
        DependencyProperty.Register(nameof(DetailsTemplate), typeof(DataTemplate), typeof(DataGridRow),
            new PropertyMetadata(null, OnDetailsTemplateChanged));

    public static readonly DependencyProperty DetailsTemplateSelectorProperty =
        DependencyProperty.Register(nameof(DetailsTemplateSelector), typeof(DataTemplateSelector), typeof(DataGridRow),
            new PropertyMetadata(null, OnDetailsTemplateChanged));

    public static readonly DependencyProperty DetailsVisibilityProperty =
        DependencyProperty.Register(nameof(DetailsVisibility), typeof(Visibility), typeof(DataGridRow),
            new PropertyMetadata(Visibility.Collapsed, OnDetailsVisibilityChanged));

    public static readonly DependencyProperty AlternationIndexProperty =
        ItemsControl.AlternationIndexProperty.AddOwner(typeof(DataGridRow));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(DataGridRow),
            new PropertyMetadata(false, OnIsSelectedChanged));

    public static readonly DependencyProperty IsEditingProperty =
        IsEditingPropertyKey.DependencyProperty;

    public static readonly DependencyProperty IsNewItemProperty =
        IsNewItemPropertyKey.DependencyProperty;

    public static readonly RoutedEvent SelectedEvent =
        EventManager.RegisterRoutedEvent(nameof(Selected), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(DataGridRow));

    public static readonly RoutedEvent UnselectedEvent =
        EventManager.RegisterRoutedEvent(nameof(Unselected), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(DataGridRow));

    public object? Item
    {
        get => GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public ItemsPanelTemplate? ItemsPanel
    {
        get => (ItemsPanelTemplate?)GetValue(ItemsPanelProperty);
        set => SetValue(ItemsPanelProperty, value);
    }

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public Style? HeaderStyle
    {
        get => (Style?)GetValue(HeaderStyleProperty) ?? ParentDataGrid?.RowHeaderStyle;
        set => SetValue(HeaderStyleProperty, value);
    }

    public DataTemplate? HeaderTemplate
    {
        get => (DataTemplate?)GetValue(HeaderTemplateProperty) ?? ParentDataGrid?.RowHeaderTemplate;
        set => SetValue(HeaderTemplateProperty, value);
    }

    public DataTemplateSelector? HeaderTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(HeaderTemplateSelectorProperty) ??
            ParentDataGrid?.RowHeaderTemplateSelector;
        set => SetValue(HeaderTemplateSelectorProperty, value);
    }

    public ControlTemplate? ValidationErrorTemplate
    {
        get => (ControlTemplate?)GetValue(ValidationErrorTemplateProperty) ??
            ParentDataGrid?.RowValidationErrorTemplate;
        set => SetValue(ValidationErrorTemplateProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty)!;
        set => SetValue(IsSelectedProperty, value);
    }

    public DataTemplate? DetailsTemplate
    {
        get => (DataTemplate?)GetValue(DetailsTemplateProperty);
        set => SetValue(DetailsTemplateProperty, value);
    }

    public DataTemplateSelector? DetailsTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(DetailsTemplateSelectorProperty);
        set => SetValue(DetailsTemplateSelectorProperty, value);
    }

    public Visibility DetailsVisibility
    {
        get => (Visibility)(GetValue(DetailsVisibilityProperty) ?? Visibility.Collapsed);
        set => SetValue(DetailsVisibilityProperty, value);
    }

    public int AlternationIndex =>
        (int)(GetValue(AlternationIndexProperty) ?? 0);

    public bool IsEditing
    {
        get => (bool)(GetValue(IsEditingProperty) ?? false);
        internal set => SetValue(IsEditingPropertyKey, value);
    }

    public bool IsNewItem
    {
        get => (bool)(GetValue(IsNewItemProperty) ?? false);
        internal set => SetValue(IsNewItemPropertyKey, value);
    }

    public event RoutedEventHandler Selected
    {
        add => AddHandler(SelectedEvent, value);
        remove => RemoveHandler(SelectedEvent, value);
    }

    public event RoutedEventHandler Unselected
    {
        add => AddHandler(UnselectedEvent, value);
        remove => RemoveHandler(UnselectedEvent, value);
    }

    internal object? DataItem
    {
        get => Item;
        set => Item = value;
    }
    internal int RowIndex { get; set; }
    internal DataGrid? ParentDataGrid { get; set; }
    internal Brush? AlternatingBackground { get; set; }
    internal Style? AppliedOwnerStyle { get; set; }
    internal DataGridRowHeader? RowHeader { get; set; }
    internal int VisibleColumnStart { get; set; } = -1;
    internal int VisibleColumnEnd { get; set; } = -1;
    internal bool IsLoadedForDataGrid { get; set; }
    internal bool DetailsLoaded { get; set; }
    internal FrameworkElement? DetailsElement => _detailsElement;

    // Per-row touch-panning gate state. DataGrid is almost always inside a
    // ScrollViewer; deferring the row select to TouchUp + cancelling on drag
    // lets the scroller take over panning.
    internal int TouchActiveId { get; set; } = -1;
    internal Point TouchDownPos { get; set; }
    internal bool TouchClickCandidate { get; set; }

    /// <summary>
    /// Gets whether this row is the empty new-item placeholder shown at the
    /// bottom of the grid when <see cref="DataGrid.CanUserAddRows"/> is enabled.
    /// </summary>
    internal bool IsNewItemPlaceholder
    {
        get => IsNewItem;
        set => IsNewItem = value;
    }

    internal List<DataGridCell> Cells { get; } = new();
    internal Dictionary<int, DataGridCell> CellsByColumn { get; } = new();

    private StackPanel? _cellsPanel;
    private DataGridDetailsPresenter? _detailsPresenter;
    private FrameworkElement? _detailsElement;
    private bool _isApplyingOwnerDetailsSettings;
    private bool _isApplyingOwnerDetailsVisibility;
    private bool _suppressDetailsVisibilityNotification;

    private static ItemsPanelTemplate CreateDefaultCellsPanelTemplate()
    {
        var template = new ItemsPanelTemplate
        {
            PanelType = typeof(DataGridCellsPanel)
        };
        template.Seal();
        return template;
    }

    public DataGridRow()
    {
        Focusable = false;
    }

    /// <summary>
    /// Returns this row's current index in its owning grid.
    /// </summary>
    public int GetIndex()
    {
        if (ParentDataGrid == null)
        {
            return -1;
        }

        return RowIndex;
    }

    /// <summary>
    /// Finds the row that contains <paramref name="element"/> in the visual tree.
    /// </summary>
    public static DataGridRow? GetRowContainingElement(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        Visual? current = element;
        while (current != null)
        {
            if (current is DataGridRow row)
            {
                return row;
            }

            current = current.VisualParent;
        }

        return null;
    }

    /// <summary>
    /// Called when the owning grid's columns collection changes.
    /// </summary>
    protected internal virtual void OnColumnsChanged(
        ObservableCollection<DataGridColumn> columns,
        NotifyCollectionChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(e);
        InvalidateMeasure();
    }

    /// <summary>
    /// Called when <see cref="Header"/> changes.
    /// </summary>
    protected virtual void OnHeaderChanged(object? oldHeader, object? newHeader)
    {
        if (RowHeader != null)
        {
            RowHeader.Content = newHeader ?? Item;
        }
    }

    /// <summary>
    /// Called when <see cref="Item"/> changes.
    /// </summary>
    protected virtual void OnItemChanged(object? oldItem, object? newItem)
    {
        if (_detailsPresenter != null)
        {
            _detailsPresenter.RowItem = newItem;
        }

        if (_detailsElement != null)
        {
            _detailsElement.DataContext = newItem;
        }

        if (RowHeader != null && Header == null)
        {
            RowHeader.Content = newItem;
        }
    }

    /// <summary>
    /// Raises the <see cref="Selected"/> routed event.
    /// </summary>
    protected virtual void OnSelected(RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(e);
    }

    /// <summary>
    /// Raises the <see cref="Unselected"/> routed event.
    /// </summary>
    protected virtual void OnUnselected(RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(e);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _cellsPanel = GetTemplateChild("PART_CellsPanel") as StackPanel;

        if (_cellsPanel != null)
        {
            _cellsPanel.Orientation = Orientation.Vertical;
            _cellsPanel.Children.BeginBatchUpdate();
            try
            {
                var cellsPanel = new StackPanel { Orientation = Orientation.Horizontal };
                if (RowHeader != null)
                {
                    cellsPanel.Children.Add(RowHeader);
                }

                foreach (var cell in Cells)
                {
                    cellsPanel.Children.Add(cell);
                }

                _cellsPanel.Children.Add(cellsPanel);
                _detailsPresenter = new DataGridDetailsPresenter
                {
                    DataGridOwner = ParentDataGrid,
                    RowItem = DataItem,
                    Visibility = DetailsVisibility,
                    Content = _detailsElement
                };
                _cellsPanel.Children.Add(_detailsPresenter);
            }
            finally
            {
                _cellsPanel.Children.EndBatchUpdate();
            }
        }

        SyncRowHeaderAppearance();
        ApplyValidationErrorTemplate();
        RestoreNonSelectedBackground();
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGridRow row)
        {
            if (row.RowHeader != null)
            {
                row.RowHeader.IsRowSelected = row.IsSelected;
            }

            if (e.NewValue is false)
            {
                row.RestoreNonSelectedBackground();
            }

            row.ParentDataGrid?.ApplyRowDetailsVisibility(row, raiseVisibilityChanged: true);

            if (e.NewValue is true)
            {
                row.OnSelected(new RoutedEventArgs(SelectedEvent, row));
            }
            else
            {
                row.OnUnselected(new RoutedEventArgs(UnselectedEvent, row));
            }
        }
    }

    private static void OnItemPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGridRow row)
        {
            row.OnItemChanged(e.OldValue, e.NewValue);
        }
    }

    private static void OnHeaderPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGridRow row)
        {
            row.OnHeaderChanged(e.OldValue, e.NewValue);
        }
    }

    private static void OnHeaderAppearancePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGridRow row)
        {
            row.SyncRowHeaderAppearance();
        }
    }

    private static void OnValidationErrorTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGridRow row)
        {
            row.ApplyValidationErrorTemplate();
        }
    }

    private static void OnDetailsTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGridRow row)
        {
            row.RecreateDetailsElement();
            if (!row._isApplyingOwnerDetailsSettings)
            {
                row.ParentDataGrid?.ApplyRowDetailsVisibility(row, raiseVisibilityChanged: true);
            }
        }
    }

    private static void OnDetailsVisibilityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGridRow row)
        {
            return;
        }

        row.EnsureDetailsElement();
        if (row._detailsPresenter != null)
        {
            row._detailsPresenter.Visibility = row.DetailsVisibility;
            row._detailsPresenter.Content = row._detailsElement;
        }

        if (!row._suppressDetailsVisibilityNotification)
        {
            row.ParentDataGrid?.OnRowDetailsVisibilityChangedFromRow(
                row, row._isApplyingOwnerDetailsVisibility);
        }
    }

    internal void ApplyOwnerDetailsSettings(DataTemplate? template, DataTemplateSelector? selector)
    {
        _isApplyingOwnerDetailsSettings = true;
        try
        {
            DetailsTemplate = template;
            DetailsTemplateSelector = selector;
        }
        finally
        {
            _isApplyingOwnerDetailsSettings = false;
        }
    }

    internal void ApplyDetailsVisibility(Visibility visibility, bool raiseVisibilityChanged)
    {
        _isApplyingOwnerDetailsVisibility = true;
        _suppressDetailsVisibilityNotification = !raiseVisibilityChanged;
        try
        {
            DetailsVisibility = visibility;
        }
        finally
        {
            _suppressDetailsVisibilityNotification = false;
            _isApplyingOwnerDetailsVisibility = false;
        }

        EnsureDetailsElement();
        if (_detailsPresenter != null)
        {
            _detailsPresenter.Visibility = visibility;
            _detailsPresenter.Content = _detailsElement;
        }
    }

    internal void EnsureDetailsElement()
    {
        if (_detailsElement != null || DetailsVisibility != Visibility.Visible || DataItem == null)
        {
            return;
        }

        var template = DetailsTemplate ?? DetailsTemplateSelector?.SelectTemplate(DataItem, this);
        _detailsElement = template?.LoadContent();
        if (_detailsElement != null)
        {
            _detailsElement.DataContext = DataItem;
        }
    }

    private void RecreateDetailsElement()
    {
        if (_detailsPresenter != null)
        {
            _detailsPresenter.Content = null;
        }

        _detailsElement = null;
        EnsureDetailsElement();
        if (_detailsPresenter != null)
        {
            _detailsPresenter.Content = _detailsElement;
        }
    }

    internal void SyncRowHeaderAppearance()
    {
        if (RowHeader == null)
        {
            return;
        }

        var header = Header ?? Item;
        RowHeader.Content = header;

        var style = HeaderStyle;
        if (style != null)
        {
            RowHeader.Style = style;
        }
        else
        {
            RowHeader.ClearValue(FrameworkElement.StyleProperty);
        }

        var selector = HeaderTemplateSelector;
        RowHeader.ContentTemplateSelector = selector;
        RowHeader.ContentTemplate = selector?.SelectTemplate(header, RowHeader) ?? HeaderTemplate;
    }

    internal void ApplyValidationErrorTemplate()
    {
        var template = ValidationErrorTemplate;
        if (template != null)
        {
            Validation.SetErrorTemplate(this, template);
        }
        else
        {
            ClearValue(Validation.ErrorTemplateProperty);
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
                Background = ParentDataGrid?.RowBackground;
            }
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
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.DataGridCellAutomationPeer(this);
    }

    private static readonly DependencyPropertyKey ColumnPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(Column), typeof(DataGridColumn), typeof(DataGridCell),
            new PropertyMetadata(null, OnColumnPropertyChanged));

    private static readonly DependencyPropertyKey IsReadOnlyPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsReadOnly), typeof(bool), typeof(DataGridCell),
            new PropertyMetadata(false, OnIsReadOnlyPropertyChanged, CoerceIsReadOnly));

    public static readonly DependencyProperty ColumnProperty =
        ColumnPropertyKey.DependencyProperty;

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsEditingProperty =
        DependencyProperty.Register(nameof(IsEditing), typeof(bool), typeof(DataGridCell),
            new PropertyMetadata(false, OnIsEditingPropertyChanged));

    public static readonly DependencyProperty IsReadOnlyProperty =
        IsReadOnlyPropertyKey.DependencyProperty;

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(DataGridCell),
            new PropertyMetadata(false, OnIsSelectedPropertyChanged));

    public static readonly RoutedEvent SelectedEvent =
        EventManager.RegisterRoutedEvent(nameof(Selected), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(DataGridCell));

    public static readonly RoutedEvent UnselectedEvent =
        EventManager.RegisterRoutedEvent(nameof(Unselected), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(DataGridCell));

    /// <summary>
    /// Gets or sets whether this cell is in editing mode.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsEditing
    {
        get => (bool)GetValue(IsEditingProperty)!;
        set => SetValue(IsEditingProperty, value);
    }

    /// <summary>
    /// Gets whether this cell is read-only after applying its column and grid policies.
    /// </summary>
    public bool IsReadOnly
    {
        get
        {
            CoerceValue(IsReadOnlyProperty);
            return (bool)(GetValue(IsReadOnlyProperty) ?? false);
        }
    }

    /// <summary>Gets or sets whether this cell is selected.</summary>
    public bool IsSelected
    {
        get => (bool)(GetValue(IsSelectedProperty) ?? false);
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>
    /// Gets the column associated with this cell.
    /// </summary>
    public DataGridColumn Column
    {
        get => (DataGridColumn)GetValue(ColumnProperty)!;
        internal set => SetValue(ColumnPropertyKey, value);
    }

    public event RoutedEventHandler Selected
    {
        add => AddHandler(SelectedEvent, value);
        remove => RemoveHandler(SelectedEvent, value);
    }

    public event RoutedEventHandler Unselected
    {
        add => AddHandler(UnselectedEvent, value);
        remove => RemoveHandler(UnselectedEvent, value);
    }

    /// <summary>
    /// Stores the display element when the cell enters editing mode.
    /// </summary>
    internal FrameworkElement? DisplayElement { get; set; }

    /// <summary>
    /// Stores the editing element while the cell is in editing mode.
    /// </summary>
    internal FrameworkElement? EditingElement { get; set; }

    /// <summary>
    /// Stores the original cell value before editing began, for cancel/restore.
    /// </summary>
    internal object? OriginalValue { get; set; }

    public DataGridCell()
    {
        UseTemplateContentManagement();
        Focusable = false;
    }

    private static void OnColumnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGridCell cell)
        {
            cell.OnColumnChanged((DataGridColumn)e.OldValue!, (DataGridColumn)e.NewValue!);
        }
    }

    private static void OnIsEditingPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGridCell cell)
        {
            cell.OnIsEditingChanged(e.NewValue is true);
        }
    }

    private static void OnIsReadOnlyPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGridCell cell && e.NewValue is true && cell.IsEditing)
        {
            cell.OwningDataGrid?.CancelEdit();
        }
    }

    private static object? CoerceIsReadOnly(DependencyObject d, object? baseValue)
    {
        var cell = (DataGridCell)d;
        var column = cell.GetValue(ColumnProperty) as DataGridColumn;
        return baseValue is true || column?.IsReadOnly == true || cell.OwningDataGrid?.IsReadOnly == true;
    }

    private static void OnIsSelectedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGridCell cell)
        {
            return;
        }

        if (e.NewValue is true)
        {
            cell.OnSelected(new RoutedEventArgs(SelectedEvent, cell));
        }
        else
        {
            cell.OnUnselected(new RoutedEventArgs(UnselectedEvent, cell));
        }
    }

    /// <summary>
    /// Called when the associated column changes.
    /// </summary>
    protected virtual void OnColumnChanged(DataGridColumn oldColumn, DataGridColumn newColumn)
    {
        Content = null;
        CoerceValue(IsReadOnlyProperty);

        var row = FindParentRow(this);
        if (newColumn != null && row?.DataItem != null && !IsEditing)
        {
            var element = newColumn.BuildVisualTree(isEditing: false, row.DataItem, this);
            element.DataContext = row.DataItem;
            Content = element;
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// Called when <see cref="IsEditing"/> changes.
    /// </summary>
    protected virtual void OnIsEditingChanged(bool isEditing)
    {
        var column = GetValue(ColumnProperty) as DataGridColumn;
        if (column == null)
        {
            return;
        }

        if (isEditing)
        {
            DisplayElement = Content as FrameworkElement;

            var row = FindParentRow(this);
            if (row?.DataItem != null)
            {
                OriginalValue = column.GetCellValueInternal(row.DataItem);

                var editingElement = column.BuildVisualTree(isEditing: true, row.DataItem, this);
                editingElement.DataContext = row.DataItem;
                EditingElement = editingElement;
                Content = editingElement;

                editingElement.Focus();
                if (editingElement is TextBox textBox)
                {
                    textBox.SelectAll();
                }
            }
        }
        else
        {
            EditingElement = null;
            if (DisplayElement != null)
            {
                Content = DisplayElement;
                DisplayElement = null;
            }

            OriginalValue = null;
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// Raises the <see cref="Selected"/> routed event.
    /// </summary>
    protected virtual void OnSelected(RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(e);
    }

    /// <summary>
    /// Raises the <see cref="Unselected"/> routed event.
    /// </summary>
    protected virtual void OnUnselected(RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(e);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size constraint)
    {
        var horizontal = IsGridLineVisible(horizontal: true) ? 1.0 : 0.0;
        var vertical = IsGridLineVisible(horizontal: false) ? 1.0 : 0.0;
        var contentConstraint = SubtractGridLines(constraint, vertical, horizontal);
        var measured = base.MeasureOverride(contentConstraint);
        return new Size(measured.Width + vertical, measured.Height + horizontal);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var horizontal = IsGridLineVisible(horizontal: true) ? 1.0 : 0.0;
        var vertical = IsGridLineVisible(horizontal: false) ? 1.0 : 0.0;
        var contentSize = SubtractGridLines(arrangeSize, vertical, horizontal);
        var arranged = base.ArrangeOverride(contentSize);
        return new Size(arranged.Width + vertical, arranged.Height + horizontal);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var owner = OwningDataGrid;
        if (owner == null)
        {
            return;
        }

        if (IsGridLineVisible(horizontal: false) && RenderSize.Width >= 1.0)
        {
            drawingContext.DrawRectangle(
                owner.VerticalGridLinesBrush,
                null,
                new Rect(RenderSize.Width - 1.0, 0.0, 1.0, RenderSize.Height));
        }

        if (IsGridLineVisible(horizontal: true) && RenderSize.Height >= 1.0)
        {
            drawingContext.DrawRectangle(
                owner.HorizontalGridLinesBrush,
                null,
                new Rect(0.0, RenderSize.Height - 1.0, RenderSize.Width, 1.0));
        }
    }

    /// <inheritdoc />
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
    }

    /// <inheritdoc />
    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
    }

    private DataGrid? OwningDataGrid =>
        FindParentRow(this)?.ParentDataGrid ??
        (GetValue(ColumnProperty) as DataGridColumn)?.DataGridOwner;

    private bool IsGridLineVisible(bool horizontal)
    {
        var owner = OwningDataGrid;
        if (owner == null)
        {
            return false;
        }

        return owner.GridLinesVisibility == DataGridGridLinesVisibility.All ||
            (horizontal && owner.GridLinesVisibility == DataGridGridLinesVisibility.Horizontal) ||
            (!horizontal && owner.GridLinesVisibility == DataGridGridLinesVisibility.Vertical);
    }

    private static Size SubtractGridLines(Size size, double width, double height)
    {
        var contentWidth = double.IsPositiveInfinity(size.Width)
            ? size.Width
            : Math.Max(0.0, size.Width - width);
        var contentHeight = double.IsPositiveInfinity(size.Height)
            ? size.Height
            : Math.Max(0.0, size.Height - height);
        return new Size(contentWidth, contentHeight);
    }

    /// <summary>
    /// Walks up the visual tree to find the parent DataGridRow.
    /// </summary>
    private static DataGridRow? FindParentRow(DataGridCell cell)
    {
        Visual? parent = cell.InternalVisualParent;
        while (parent != null)
        {
            if (parent is DataGridRow row)
                return row;
            parent = parent.InternalVisualParent;
        }
        return null;
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
    All = 0,
    Horizontal = 1,
    None = 2,
    Vertical = 3
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
    Style,
    Visibility,
    SortDirection
}

public class DataGridSortingEventArgs : DataGridColumnEventArgs
{
    /// <summary>Gets or sets whether the application handled the sort operation.</summary>
    public bool Handled { get; set; }

    /// <summary>Initializes event data for the specified column.</summary>
    public DataGridSortingEventArgs(DataGridColumn column)
        : base(column)
    {
    }

    /// <summary>
    /// Initializes routed event data for the specified column.
    /// Retained for compatibility with Jalium's routed-event implementation.
    /// </summary>
    public DataGridSortingEventArgs(RoutedEvent routedEvent, DataGridColumn column)
        : this(column)
    {
    }
}

/// <summary>
/// Provides data for the BeginningEdit event.
/// </summary>
public class DataGridBeginningEditEventArgs : EventArgs
{
    public DataGridColumn Column { get; }
    public DataGridRow Row { get; }
    public DataGridCell? Cell { get; }

    /// <summary>Gets the event arguments that initiated editing, if available.</summary>
    public RoutedEventArgs? EditingEventArgs { get; }

    public bool Cancel { get; set; }

    /// <summary>Initializes event data for a cell that is about to enter edit mode.</summary>
    public DataGridBeginningEditEventArgs(
        DataGridColumn column,
        DataGridRow row,
        RoutedEventArgs editingEventArgs)
    {
        Column = column;
        Row = row;
        EditingEventArgs = editingEventArgs;
    }

    /// <summary>
    /// Initializes routed event data for a cell that is about to enter edit mode.
    /// Retained for compatibility with Jalium's routed-event implementation.
    /// </summary>
    public DataGridBeginningEditEventArgs(RoutedEvent routedEvent, DataGridColumn column, DataGridRow row, DataGridCell cell)
    {
        Column = column;
        Row = row;
        Cell = cell;
    }
}

/// <summary>
/// Provides data for the CellEditEnding event.
/// </summary>
public class DataGridCellEditEndingEventArgs : EventArgs
{
    public DataGridColumn Column { get; }
    public DataGridRow Row { get; }
    public DataGridCell? Cell { get; }

    /// <summary>Gets the editing element within the cell.</summary>
    public FrameworkElement EditingElement { get; }

    public DataGridEditAction EditAction { get; }
    public bool Cancel { get; set; }

    /// <summary>Initializes event data for a cell that is about to leave edit mode.</summary>
    public DataGridCellEditEndingEventArgs(
        DataGridColumn column,
        DataGridRow row,
        FrameworkElement editingElement,
        DataGridEditAction editAction)
    {
        Column = column;
        Row = row;
        EditingElement = editingElement;
        EditAction = editAction;
    }

    /// <summary>
    /// Initializes routed event data for a cell that is about to leave edit mode.
    /// Retained for compatibility with Jalium's routed-event implementation.
    /// </summary>
    public DataGridCellEditEndingEventArgs(RoutedEvent routedEvent, DataGridColumn column, DataGridRow row, DataGridCell cell, DataGridEditAction editAction)
    {
        Column = column;
        Row = row;
        Cell = cell;
        EditingElement = cell.EditingElement ?? cell;
        EditAction = editAction;
    }
}

/// <summary>
/// Provides data for the PreparingCellForEdit event.
/// </summary>
public class DataGridPreparingCellForEditEventArgs : EventArgs
{
    public DataGridColumn Column { get; }
    public DataGridRow Row { get; }
    public DataGridCell? Cell { get; }

    /// <summary>Gets the event arguments that initiated editing, if available.</summary>
    public RoutedEventArgs? EditingEventArgs { get; }

    /// <summary>Gets the editing element within the cell.</summary>
    public FrameworkElement EditingElement { get; }

    /// <summary>Initializes event data for a cell that has entered edit mode.</summary>
    public DataGridPreparingCellForEditEventArgs(
        DataGridColumn column,
        DataGridRow row,
        RoutedEventArgs editingEventArgs,
        FrameworkElement editingElement)
    {
        Column = column;
        Row = row;
        EditingEventArgs = editingEventArgs;
        EditingElement = editingElement;
    }

    /// <summary>
    /// Initializes routed event data for a cell that has entered edit mode.
    /// Retained for compatibility with Jalium's routed-event implementation.
    /// </summary>
    public DataGridPreparingCellForEditEventArgs(RoutedEvent routedEvent, DataGridColumn column, DataGridRow row, DataGridCell cell)
    {
        Column = column;
        Row = row;
        Cell = cell;
        EditingElement = cell.EditingElement ?? cell;
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

    private DataGrid? _dataGridOwner;
    private BindingBase? _clipboardContentBinding;
    private bool _isSettingDisplayIndex;

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(DataGridColumn),
            new PropertyMetadata(null, OnHeaderPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.Register(nameof(Width), typeof(DataGridLength), typeof(DataGridColumn),
            new PropertyMetadata(DataGridLength.Auto, OnWidthPropertyChanged, CoerceWidth));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinWidthProperty =
        DependencyProperty.Register(nameof(MinWidth), typeof(double), typeof(DataGridColumn),
            new PropertyMetadata(DefaultMinWidth, OnMinWidthPropertyChanged, CoerceMinWidth));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaxWidthProperty =
        DependencyProperty.Register(nameof(MaxWidth), typeof(double), typeof(DataGridColumn),
            new PropertyMetadata(double.PositiveInfinity, OnMaxWidthPropertyChanged, CoerceMaxWidth));

    private static readonly DependencyPropertyKey ActualWidthPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(ActualWidth), typeof(double), typeof(DataGridColumn),
            new PropertyMetadata(0.0));

    public static readonly DependencyProperty ActualWidthProperty =
        ActualWidthPropertyKey.DependencyProperty;

    public static readonly DependencyProperty CanUserReorderProperty =
        DependencyProperty.Register(nameof(CanUserReorder), typeof(bool), typeof(DataGridColumn),
            new PropertyMetadata(true, OnColumnPolicyPropertyChanged, CoerceCanUserReorder));

    public static readonly DependencyProperty CanUserResizeProperty =
        DependencyProperty.Register(nameof(CanUserResize), typeof(bool), typeof(DataGridColumn),
            new PropertyMetadata(true, OnColumnPolicyPropertyChanged, CoerceCanUserResize));

    public static readonly DependencyProperty CanUserSortProperty =
        DependencyProperty.Register(nameof(CanUserSort), typeof(bool), typeof(DataGridColumn),
            new PropertyMetadata(true, OnColumnPolicyPropertyChanged, CoerceCanUserSort));

    public static readonly DependencyProperty CellStyleProperty =
        DependencyProperty.Register(nameof(CellStyle), typeof(Style), typeof(DataGridColumn),
            new PropertyMetadata(null, OnCellStylePropertyChanged));

    public static readonly DependencyProperty DisplayIndexProperty =
        DependencyProperty.Register(nameof(DisplayIndex), typeof(int), typeof(DataGridColumn),
            new PropertyMetadata(-1, OnDisplayIndexPropertyChanged));

    public static readonly DependencyProperty DragIndicatorStyleProperty =
        DependencyProperty.Register(nameof(DragIndicatorStyle), typeof(Style), typeof(DataGridColumn),
            new PropertyMetadata(null, OnHeaderAppearancePropertyChanged));

    public static readonly DependencyProperty HeaderStringFormatProperty =
        DependencyProperty.Register(nameof(HeaderStringFormat), typeof(string), typeof(DataGridColumn),
            new PropertyMetadata(null, OnHeaderAppearancePropertyChanged));

    public static readonly DependencyProperty HeaderStyleProperty =
        DependencyProperty.Register(nameof(HeaderStyle), typeof(Style), typeof(DataGridColumn),
            new PropertyMetadata(null, OnHeaderAppearancePropertyChanged));

    public static readonly DependencyProperty HeaderTemplateProperty =
        DependencyProperty.Register(nameof(HeaderTemplate), typeof(DataTemplate), typeof(DataGridColumn),
            new PropertyMetadata(null, OnHeaderAppearancePropertyChanged));

    public static readonly DependencyProperty HeaderTemplateSelectorProperty =
        DependencyProperty.Register(nameof(HeaderTemplateSelector), typeof(DataTemplateSelector), typeof(DataGridColumn),
            new PropertyMetadata(null, OnHeaderAppearancePropertyChanged));

    private static readonly DependencyPropertyKey IsAutoGeneratedPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsAutoGenerated), typeof(bool), typeof(DataGridColumn),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsAutoGeneratedProperty =
        IsAutoGeneratedPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey IsFrozenPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(IsFrozen), typeof(bool), typeof(DataGridColumn),
            new PropertyMetadata(false, OnHeaderAppearancePropertyChanged));

    public static readonly DependencyProperty IsFrozenProperty =
        IsFrozenPropertyKey.DependencyProperty;

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(DataGridColumn),
            new PropertyMetadata(false, OnCellStylePropertyChanged, CoerceIsReadOnly));

    public static readonly DependencyProperty SortDirectionProperty =
        DependencyProperty.Register(nameof(SortDirection), typeof(ListSortDirection?), typeof(DataGridColumn),
            new PropertyMetadata(null, OnSortPropertyChanged));

    public static readonly DependencyProperty SortMemberPathProperty =
        DependencyProperty.Register(nameof(SortMemberPath), typeof(string), typeof(DataGridColumn),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty VisibilityProperty =
        DependencyProperty.Register(nameof(Visibility), typeof(Visibility), typeof(DataGridColumn),
            new PropertyMetadata(Visibility.Visible, OnVisibilityPropertyChanged));

    /// <summary>Gets the DataGrid that owns this column.</summary>
    protected internal DataGrid DataGridOwner => _dataGridOwner!;

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public Style HeaderStyle
    {
        get => (Style?)GetValue(HeaderStyleProperty) ?? _dataGridOwner?.ColumnHeaderStyle!;
        set => SetValue(HeaderStyleProperty, value);
    }

    public string HeaderStringFormat
    {
        get => (string)GetValue(HeaderStringFormatProperty)!;
        set => SetValue(HeaderStringFormatProperty, value);
    }

    public DataTemplate HeaderTemplate
    {
        get => (DataTemplate)GetValue(HeaderTemplateProperty)!;
        set => SetValue(HeaderTemplateProperty, value);
    }

    public DataTemplateSelector HeaderTemplateSelector
    {
        get => (DataTemplateSelector)GetValue(HeaderTemplateSelectorProperty)!;
        set => SetValue(HeaderTemplateSelectorProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public DataGridLength Width
    {
        get => (DataGridLength)(GetValue(WidthProperty) ?? DataGridLength.Auto);
        set => SetValue(WidthProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MinWidth
    {
        get => (double)GetValue(MinWidthProperty)!;
        set => SetValue(MinWidthProperty, value < 0 || double.IsNaN(value) || double.IsInfinity(value) ? 0.0 : value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MaxWidth
    {
        get => (double)GetValue(MaxWidthProperty)!;
        set => SetValue(MaxWidthProperty, value < 0 || double.IsNaN(value) ? double.PositiveInfinity : value);
    }

    public double ActualWidth => (double)(GetValue(ActualWidthProperty) ?? 0.0);

    public bool CanUserSort
    {
        get => (bool)(GetValue(CanUserSortProperty) ?? true) &&
            (_dataGridOwner?.CanUserSortColumns ?? true);
        set => SetValue(CanUserSortProperty, value);
    }

    public bool CanUserResize
    {
        get => (bool)(GetValue(CanUserResizeProperty) ?? true) &&
            (_dataGridOwner?.CanUserResizeColumns ?? true);
        set => SetValue(CanUserResizeProperty, value);
    }

    public bool CanUserReorder
    {
        get => (bool)(GetValue(CanUserReorderProperty) ?? true) &&
            (_dataGridOwner?.CanUserReorderColumns ?? true);
        set => SetValue(CanUserReorderProperty, value);
    }

    public ListSortDirection? SortDirection
    {
        get => (ListSortDirection?)GetValue(SortDirectionProperty);
        set => SetValue(SortDirectionProperty, value);
    }

    public string SortMemberPath
    {
        get => (string)(GetValue(SortMemberPathProperty) ?? string.Empty);
        set => SetValue(SortMemberPathProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public bool IsReadOnly
    {
        get => (bool)(GetValue(IsReadOnlyProperty) ?? false) ||
            (_dataGridOwner?.IsReadOnly ?? false);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool IsAutoGenerated => (bool)(GetValue(IsAutoGeneratedProperty) ?? false);

    public bool IsFrozen => (bool)(GetValue(IsFrozenProperty) ?? false);

    /// <summary>
    /// Gets or sets the style applied to cells in this column.
    /// </summary>
    public Style CellStyle
    {
        get => (Style?)GetValue(CellStyleProperty) ?? _dataGridOwner?.CellStyle!;
        set => SetValue(CellStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the style applied to the column header.
    /// </summary>
    public Style DragIndicatorStyle
    {
        get => (Style)GetValue(DragIndicatorStyleProperty)!;
        set => SetValue(DragIndicatorStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the Visibility of the column.
    /// </summary>
    public Visibility Visibility
    {
        get => (Visibility)(GetValue(VisibilityProperty) ?? Visibility.Visible);
        set => SetValue(VisibilityProperty, value);
    }

    public int DisplayIndex
    {
        get => (int)(GetValue(DisplayIndexProperty) ?? -1);
        set => SetValue(DisplayIndexProperty, value);
    }

    public virtual BindingBase ClipboardContentBinding
    {
        get => _clipboardContentBinding!;
        set => _clipboardContentBinding = value;
    }

    internal void SetDataGridOwner(DataGrid? owner)
    {
        if (ReferenceEquals(_dataGridOwner, owner))
        {
            return;
        }

        _dataGridOwner = owner;
        CoerceValue(IsReadOnlyProperty);
        CoerceValue(CanUserSortProperty);
        CoerceValue(CanUserResizeProperty);
        CoerceValue(CanUserReorderProperty);
        SetIsFrozen(owner != null && DisplayIndex >= 0 && DisplayIndex < owner.FrozenColumnCount);
    }

    internal void SetDisplayIndexSilently(int displayIndex)
    {
        _isSettingDisplayIndex = true;
        try
        {
            SetValue(DisplayIndexProperty, displayIndex);
        }
        finally
        {
            _isSettingDisplayIndex = false;
        }

        SetIsFrozen(_dataGridOwner != null && displayIndex >= 0 && displayIndex < _dataGridOwner.FrozenColumnCount);
    }

    internal void SetIsAutoGenerated(bool value) => SetValue(IsAutoGeneratedPropertyKey, value);

    internal void SetIsFrozen(bool value) => SetValue(IsFrozenPropertyKey, value);

    internal void SetActualWidth(double value)
    {
        var minimum = Math.Max(0.0, MinWidth);
        var maximum = Math.Max(minimum, MaxWidth);
        SetValue(ActualWidthPropertyKey, Math.Clamp(value, minimum, maximum));
    }

    private void NotifyOwner(DataGridColumnPropertyChange change) =>
        _dataGridOwner?.OnColumnPropertyChanged(this, change);

    private static void OnHeaderPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DataGridColumn)d).NotifyOwner(DataGridColumnPropertyChange.Header);

    private static void OnHeaderAppearancePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DataGridColumn)d).NotifyOwner(DataGridColumnPropertyChange.Header);

    private static void OnCellStylePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DataGridColumn)d).NotifyOwner(DataGridColumnPropertyChange.Style);

    private static void OnColumnPolicyPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DataGridColumn)d).NotifyOwner(DataGridColumnPropertyChange.Header);

    private static void OnSortPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DataGridColumn)d).NotifyOwner(DataGridColumnPropertyChange.SortDirection);

    private static void OnVisibilityPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DataGridColumn)d).NotifyOwner(DataGridColumnPropertyChange.Visibility);

    private static void OnDisplayIndexPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var column = (DataGridColumn)d;
        if (!column._isSettingDisplayIndex && e.NewValue is int requestedIndex && column._dataGridOwner != null)
        {
            column._dataGridOwner.RequestColumnDisplayIndex(column, requestedIndex);
        }

        column.SetIsFrozen(
            column._dataGridOwner != null && column.DisplayIndex >= 0 &&
            column.DisplayIndex < column._dataGridOwner.FrozenColumnCount);
    }

    private static void OnWidthPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var column = (DataGridColumn)d;
        if (e.NewValue is DataGridLength width)
        {
            var displayWidth = width.IsAbsolute
                ? width.Value
                : width.DisplayValue;
            if (!double.IsNaN(displayWidth) && !double.IsInfinity(displayWidth))
            {
                column.SetActualWidth(displayWidth);
            }
        }

        column.NotifyOwner(DataGridColumnPropertyChange.Layout);
    }

    private static void OnMinWidthPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var column = (DataGridColumn)d;
        column.CoerceValue(WidthProperty);
        column.SetActualWidth(column.ActualWidth);
        column.NotifyOwner(DataGridColumnPropertyChange.Layout);
    }

    private static void OnMaxWidthPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var column = (DataGridColumn)d;
        column.CoerceValue(WidthProperty);
        column.SetActualWidth(column.ActualWidth);
        column.NotifyOwner(DataGridColumnPropertyChange.Layout);
    }

    private static object? CoerceWidth(DependencyObject d, object? baseValue)
    {
        var width = baseValue is DataGridLength value ? value : DataGridLength.Auto;
        if (!width.IsAbsolute)
        {
            return width;
        }

        var column = (DataGridColumn)d;
        return new DataGridLength(Math.Clamp(width.Value, column.MinWidth, column.MaxWidth));
    }

    private static object? CoerceMinWidth(DependencyObject d, object? baseValue)
    {
        var minimum = baseValue is double value ? value : DefaultMinWidth;
        if (double.IsNaN(minimum) || double.IsInfinity(minimum) || minimum < 0.0)
        {
            minimum = 0.0;
        }

        return d is DataGridColumn column ? Math.Min(minimum, column.MaxWidth) : minimum;
    }

    private static object? CoerceMaxWidth(DependencyObject d, object? baseValue)
    {
        var maximum = baseValue is double value ? value : double.PositiveInfinity;
        if (double.IsNaN(maximum) || maximum < 0.0 || double.IsNegativeInfinity(maximum))
        {
            maximum = double.PositiveInfinity;
        }

        return d is DataGridColumn column ? Math.Max(maximum, column.MinWidth) : maximum;
    }

    private static object? CoerceIsReadOnly(DependencyObject d, object? baseValue) =>
        ((DataGridColumn)d).OnCoerceIsReadOnly(baseValue is true);

    private static object? CoerceCanUserSort(DependencyObject d, object? baseValue) =>
        baseValue is true && (((DataGridColumn)d)._dataGridOwner?.CanUserSortColumns ?? true);

    private static object? CoerceCanUserResize(DependencyObject d, object? baseValue) =>
        baseValue is true && (((DataGridColumn)d)._dataGridOwner?.CanUserResizeColumns ?? true);

    private static object? CoerceCanUserReorder(DependencyObject d, object? baseValue) =>
        baseValue is true && (((DataGridColumn)d)._dataGridOwner?.CanUserReorderColumns ?? true);

    /// <summary>Allows derived columns to force a read-only effective value.</summary>
    protected virtual bool OnCoerceIsReadOnly(bool baseValue) =>
        baseValue || (_dataGridOwner?.IsReadOnly ?? false);

    /// <summary>Notifies realized cells that a column-specific presentation property changed.</summary>
    protected void NotifyPropertyChanged(string propertyName) =>
        _dataGridOwner?.RefreshColumnCellContent(this, propertyName);

    /// <summary>Refreshes one realized cell after a column presentation property changes.</summary>
    protected internal virtual void RefreshCellContent(FrameworkElement element, string propertyName)
    {
        if (element is DataGridCell cell)
        {
            _dataGridOwner?.RefreshColumnCellContent(this, cell, propertyName);
        }
    }

    internal FrameworkElement BuildVisualTree(bool isEditing, object dataItem, DataGridCell cell) =>
        isEditing ? GenerateEditingElement(cell, dataItem) : GenerateElement(cell, dataItem);

    protected abstract FrameworkElement GenerateElement(DataGridCell cell, object dataItem);

    protected abstract FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem);

    protected virtual object? PrepareCellForEdit(FrameworkElement editingElement, RoutedEventArgs editingEventArgs) =>
        editingElement.DataContext == null ? null : GetCellValue(editingElement.DataContext);

    protected virtual void CancelCellEdit(FrameworkElement editingElement, object uneditedValue)
    {
    }

    protected virtual bool CommitCellEdit(FrameworkElement editingElement) => true;

    internal object? PrepareCellForEditInternal(FrameworkElement editingElement, RoutedEventArgs editingEventArgs) =>
        PrepareCellForEdit(editingElement, editingEventArgs);

    internal void CancelCellEditInternal(FrameworkElement editingElement, object? uneditedValue) =>
        CancelCellEdit(editingElement, uneditedValue!);

    internal bool CommitCellEditInternal(FrameworkElement editingElement, object dataItem)
    {
        editingElement.DataContext = dataItem;
        return CommitCellEdit(editingElement);
    }

    protected virtual object? GetCellValue(object item) => item;

    internal object? GetCellValueInternal(object item) => GetCellValue(item);

    public FrameworkElement GetCellContent(object dataItem)
    {
        if (_dataGridOwner == null)
        {
            return null!;
        }

        var row = _dataGridOwner.FindRealizedRow(dataItem);
        return row == null ? null! : GetCellContent(row);
    }

    public FrameworkElement GetCellContent(DataGridRow dataGridRow)
    {
        ArgumentNullException.ThrowIfNull(dataGridRow);
        var columnIndex = _dataGridOwner?.Columns.IndexOf(this) ?? -1;
        return columnIndex >= 0 && dataGridRow.CellsByColumn.TryGetValue(columnIndex, out var cell)
            ? cell.Content as FrameworkElement ?? cell
            : null!;
    }

    public virtual object OnCopyingCellClipboardContent(object item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var content = EvaluateClipboardBinding(item);
        if (CopyingCellClipboardContent != null)
        {
            var args = new DataGridCellClipboardEventArgs(item, this, content);
            CopyingCellClipboardContent(this, args);
            content = args.Content;
        }

        return content!;
    }

    public virtual void OnPastingCellClipboardContent(object item, object cellContent)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (ClipboardContentBinding == null)
        {
            return;
        }

        object? content = cellContent;
        if (PastingCellClipboardContent != null)
        {
            var args = new DataGridCellClipboardEventArgs(item, this, content);
            PastingCellClipboardContent(this, args);
            content = args.Content;
        }

        if (content != null)
        {
            SetClipboardCellValue(item, content);
        }
    }

    public event EventHandler<DataGridCellClipboardEventArgs> CopyingCellClipboardContent = null!;

    public event EventHandler<DataGridCellClipboardEventArgs> PastingCellClipboardContent = null!;

    protected static object? GetBindingValue(BindingBase? bindingBase, object item)
    {
        if (bindingBase is not Binding binding || binding.Path == null)
        {
            return null;
        }

        object? current = item;
        foreach (var part in binding.Path.PathSegments)
        {
            if (current == null)
            {
                return null;
            }

            current = DataGrid.GetCachedProperty(current.GetType(), part)?.GetValue(current);
        }

        return current;
    }

    protected static bool SetBindingValue(BindingBase? bindingBase, object item, object? value)
    {
        if (bindingBase is not Binding binding || binding.Path == null ||
            binding.Path.PathSegments.Length == 0)
        {
            return false;
        }

        object? target = item;
        var parts = binding.Path.PathSegments;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (target == null)
            {
                return false;
            }

            target = DataGrid.GetCachedProperty(target.GetType(), parts[index])?.GetValue(target);
        }

        if (target == null)
        {
            return false;
        }

        var property = DataGrid.GetCachedProperty(target.GetType(), parts[^1]);
        if (property?.CanWrite != true)
        {
            return false;
        }

        try
        {
            var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var converted = value == null || targetType.IsInstanceOfType(value)
                ? value
                : targetType.IsEnum
                    ? Enum.Parse(targetType, value.ToString()!, ignoreCase: true)
                    : Convert.ChangeType(value, targetType, CultureInfo.CurrentCulture);
            property.SetValue(target, converted);
            return true;
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    private object? EvaluateClipboardBinding(object item)
    {
        if (ClipboardContentBinding == null)
        {
            return GetCellValue(item);
        }

        return GetBindingValue(ClipboardContentBinding, item) ?? GetCellValue(item);
    }

    protected virtual void SetClipboardCellValue(object item, object value)
    {
    }
}

public abstract class DataGridBoundColumn : DataGridColumn
{
    private BindingBase? _binding;

    public static readonly DependencyProperty ElementStyleProperty =
        DependencyProperty.Register(nameof(ElementStyle), typeof(Style), typeof(DataGridBoundColumn),
            new PropertyMetadata(null, OnElementStyleChanged));

    public static readonly DependencyProperty EditingElementStyleProperty =
        DependencyProperty.Register(nameof(EditingElementStyle), typeof(Style), typeof(DataGridBoundColumn),
            new PropertyMetadata(null, OnEditingElementStyleChanged));

    public virtual BindingBase Binding
    {
        get => _binding!;
        set
        {
            if (ReferenceEquals(_binding, value))
            {
                return;
            }

            var oldBinding = _binding;
            _binding = value;
            OnBindingChanged(oldBinding!, value);
            NotifyPropertyChanged(nameof(Binding));
            CoerceValue(IsReadOnlyProperty);
        }
    }

    public Style ElementStyle
    {
        get => (Style)GetValue(ElementStyleProperty)!;
        set => SetValue(ElementStyleProperty, value);
    }

    public Style EditingElementStyle
    {
        get => (Style)GetValue(EditingElementStyleProperty)!;
        set => SetValue(EditingElementStyleProperty, value);
    }

    public override BindingBase ClipboardContentBinding
    {
        get => Binding!;
        set => Binding = value;
    }

    /// <summary>Called after the binding used by the column changes.</summary>
    protected virtual void OnBindingChanged(BindingBase oldBinding, BindingBase newBinding)
    {
    }

    protected override bool OnCoerceIsReadOnly(bool baseValue)
    {
        var oneWay = Binding is Binding binding &&
            binding.Mode is BindingMode.OneWay or BindingMode.OneTime;
        return base.OnCoerceIsReadOnly(baseValue) || oneWay;
    }

    protected internal override void RefreshCellContent(FrameworkElement element, string propertyName)
    {
        base.RefreshCellContent(element, propertyName);
    }

    protected override object? GetCellValue(object item)
    {
        if (Binding is not Binding binding || binding.Path == null) return null;

        var current = item;
        foreach (var part in binding.Path.PathSegments)
        {
            if (current == null) return null;
            var prop = DataGrid.GetCachedProperty(current.GetType(), part);
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
        if (Binding is not Binding binding || binding.Path == null) return;

        var parts = binding.Path.PathSegments;
        var target = dataItem;

        // Navigate to the parent object for nested paths
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (target == null) return;
            var prop = DataGrid.GetCachedProperty(target.GetType(), parts[i]);
            target = prop?.GetValue(target);
        }

        if (target == null) return;

        var finalProp = DataGrid.GetCachedProperty(target.GetType(), parts[^1]);
        if (finalProp?.CanWrite == true)
        {
            try
            {
                var targetType = Nullable.GetUnderlyingType(finalProp.PropertyType) ?? finalProp.PropertyType;
                var convertedValue = value == null || targetType.IsInstanceOfType(value)
                    ? value
                    : targetType.IsEnum
                        ? Enum.Parse(targetType, value.ToString()!, ignoreCase: true)
                        : Convert.ChangeType(value, targetType, CultureInfo.CurrentCulture);
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

    protected override void SetClipboardCellValue(object item, object value) =>
        SetCellValue(item, value);

    private static void OnElementStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DataGridBoundColumn)d).NotifyPropertyChanged(nameof(ElementStyle));

    private static void OnEditingElementStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DataGridBoundColumn)d).NotifyPropertyChanged(nameof(EditingElementStyle));
}

public class DataGridTextColumn : DataGridBoundColumn
{
    private static readonly Style s_defaultElementStyle = CreateDefaultElementStyle();
    private static readonly Style s_defaultEditingElementStyle = CreateDefaultEditingElementStyle();

    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(nameof(FontFamily), typeof(Jalium.UI.Media.FontFamily), typeof(DataGridTextColumn),
            new PropertyMetadata(SystemFonts.MessageFontFamily, OnTextAppearanceChanged));

    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(DataGridTextColumn),
            new PropertyMetadata(SystemFonts.MessageFontSize, OnTextAppearanceChanged));

    public static readonly DependencyProperty FontStyleProperty =
        DependencyProperty.Register(nameof(FontStyle), typeof(Jalium.UI.FontStyle), typeof(DataGridTextColumn),
            new PropertyMetadata(SystemFonts.MessageFontStyle, OnTextAppearanceChanged));

    public static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.Register(nameof(FontWeight), typeof(Jalium.UI.FontWeight), typeof(DataGridTextColumn),
            new PropertyMetadata(SystemFonts.MessageFontWeight, OnTextAppearanceChanged));

    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(DataGridTextColumn),
            new PropertyMetadata(SystemColors.ControlTextBrush, OnTextAppearanceChanged));

    public static Style DefaultElementStyle => s_defaultElementStyle;

    public static Style DefaultEditingElementStyle => s_defaultEditingElementStyle;

    public Jalium.UI.Media.FontFamily FontFamily
    {
        get => (Jalium.UI.Media.FontFamily)(GetValue(FontFamilyProperty) ?? SystemFonts.MessageFontFamily);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)(GetValue(FontSizeProperty) ?? SystemFonts.MessageFontSize);
        set => SetValue(FontSizeProperty, value);
    }

    public Jalium.UI.FontStyle FontStyle
    {
        get => (Jalium.UI.FontStyle)(GetValue(FontStyleProperty) ?? SystemFonts.MessageFontStyle);
        set => SetValue(FontStyleProperty, value);
    }

    public Jalium.UI.FontWeight FontWeight
    {
        get => (Jalium.UI.FontWeight)(GetValue(FontWeightProperty) ?? SystemFonts.MessageFontWeight);
        set => SetValue(FontWeightProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)(GetValue(ForegroundProperty) ?? SystemColors.ControlTextBrush);
        set => SetValue(ForegroundProperty, value);
    }

    protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
    {
        var value = GetCellValue(dataItem);
        var textBlock = new TextBlock
        {
            Text = value?.ToString() ?? "",
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = FontFamily,
            FontSize = FontSize,
            FontStyle = FontStyle,
            FontWeight = FontWeight,
            Foreground = Foreground
        };
        textBlock.Style = ElementStyle ?? DefaultElementStyle;
        return textBlock;
    }

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem)
    {
        var value = GetCellValue(dataItem);
        var textBox = new TextBox
        {
            Text = value?.ToString() ?? "",
            BorderThickness = new Thickness(0),
            Background = Jalium.UI.Media.Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = FontFamily,
            FontSize = FontSize,
            FontStyle = FontStyle,
            FontWeight = FontWeight,
            Foreground = Foreground
        };
        textBox.Style = EditingElementStyle ?? DefaultEditingElementStyle;
        return textBox;
    }

    protected override object? PrepareCellForEdit(
        FrameworkElement editingElement,
        RoutedEventArgs editingEventArgs)
    {
        if (editingElement is not TextBox textBox)
        {
            return base.PrepareCellForEdit(editingElement, editingEventArgs);
        }

        var original = textBox.Text;
        _ = textBox.Focus();
        textBox.SelectAll();
        return original;
    }

    protected internal override void RefreshCellContent(FrameworkElement element, string propertyName)
    {
        if (propertyName is nameof(FontFamily) or nameof(FontSize) or nameof(FontStyle) or
            nameof(FontWeight) or nameof(Foreground))
        {
            base.RefreshCellContent(element, propertyName);
            return;
        }

        base.RefreshCellContent(element, propertyName);
    }

    protected override bool CommitCellEdit(FrameworkElement editingElement)
    {
        if (editingElement is TextBox textBox && editingElement.DataContext != null)
        {
            SetCellValue(editingElement.DataContext, textBox.Text);
        }

        return true;
    }

    protected override void CancelCellEdit(FrameworkElement editingElement, object uneditedValue)
    {
        if (editingElement is TextBox textBox)
        {
            textBox.Text = uneditedValue?.ToString() ?? "";
        }
    }

    private static void OnTextAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DataGridTextColumn)d).NotifyPropertyChanged(e.Property.Name);

    private static Style CreateDefaultElementStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(2, 0, 2, 0)));
        style.Seal();
        return style;
    }

    private static Style CreateDefaultEditingElementStyle()
    {
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Seal();
        return style;
    }
}

public class DataGridCheckBoxColumn : DataGridBoundColumn
{
    private static readonly Style s_defaultElementStyle = CreateDefaultElementStyle();
    private static readonly Style s_defaultEditingElementStyle = CreateDefaultEditingElementStyle();

    public static readonly DependencyProperty IsThreeStateProperty =
        DependencyProperty.Register(nameof(IsThreeState), typeof(bool), typeof(DataGridCheckBoxColumn),
            new PropertyMetadata(false, OnIsThreeStateChanged));

    public static Style DefaultElementStyle => s_defaultElementStyle;

    public static Style DefaultEditingElementStyle => s_defaultEditingElementStyle;

    /// <summary>
    /// Gets or sets whether the CheckBox is a three-state checkbox.
    /// </summary>
    public bool IsThreeState
    {
        get => (bool)(GetValue(IsThreeStateProperty) ?? false);
        set => SetValue(IsThreeStateProperty, value);
    }

    protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
    {
        var value = GetCellValue(dataItem);
        var checkBox = new CheckBox
        {
            IsChecked = value as bool? ?? (value is bool b ? b : null),
            IsEnabled = false,
            IsThreeState = IsThreeState,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        checkBox.Style = ElementStyle ?? DefaultElementStyle;
        return checkBox;
    }

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem)
    {
        var value = GetCellValue(dataItem);
        var checkBox = new CheckBox
        {
            IsChecked = value as bool? ?? (value is bool b ? b : null),
            IsThreeState = IsThreeState,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        checkBox.Style = EditingElementStyle ?? DefaultEditingElementStyle;
        return checkBox;
    }

    protected override object? PrepareCellForEdit(
        FrameworkElement editingElement,
        RoutedEventArgs editingEventArgs)
    {
        if (editingElement is CheckBox checkBox)
        {
            _ = checkBox.Focus();
            return checkBox.IsChecked;
        }

        return base.PrepareCellForEdit(editingElement, editingEventArgs);
    }

    protected internal override void RefreshCellContent(FrameworkElement element, string propertyName)
    {
        base.RefreshCellContent(element, propertyName);
    }

    protected override bool CommitCellEdit(FrameworkElement editingElement)
    {
        if (editingElement is CheckBox checkBox && editingElement.DataContext != null)
        {
            SetCellValue(editingElement.DataContext, checkBox.IsChecked);
        }

        return true;
    }

    protected override void CancelCellEdit(FrameworkElement editingElement, object uneditedValue)
    {
        if (editingElement is CheckBox checkBox)
        {
            checkBox.IsChecked = uneditedValue as bool? ?? (uneditedValue is bool b ? b : null);
        }
    }

    private static void OnIsThreeStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DataGridCheckBoxColumn)d).NotifyPropertyChanged(nameof(IsThreeState));

    private static Style CreateDefaultElementStyle()
    {
        var style = new Style(typeof(CheckBox));
        style.Setters.Add(new Setter(UIElement.IsHitTestVisibleProperty, false));
        style.Setters.Add(new Setter(UIElement.FocusableProperty, false));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top));
        style.Seal();
        return style;
    }

    private static Style CreateDefaultEditingElementStyle()
    {
        var style = new Style(typeof(CheckBox));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top));
        style.Seal();
        return style;
    }
}

public class DataGridComboBoxColumn : DataGridColumn
{
    private static readonly Style s_defaultElementStyle = CreateDefaultComboBoxStyle();
    private static readonly Style s_defaultEditingElementStyle = CreateDefaultComboBoxStyle();
    private static readonly ComponentResourceKey s_textBlockComboBoxStyleKey =
        new(typeof(DataGridComboBoxColumn), nameof(TextBlockComboBoxStyleKey));

    private BindingBase? _selectedItemBinding;
    private BindingBase? _selectedValueBinding;
    private BindingBase? _textBinding;

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(DataGridComboBoxColumn),
            new PropertyMetadata(null, OnComboBoxPropertyChanged));

    public static readonly DependencyProperty DisplayMemberPathProperty =
        DependencyProperty.Register(nameof(DisplayMemberPath), typeof(string), typeof(DataGridComboBoxColumn),
            new PropertyMetadata(string.Empty, OnComboBoxPropertyChanged));

    public static readonly DependencyProperty SelectedValuePathProperty =
        DependencyProperty.Register(nameof(SelectedValuePath), typeof(string), typeof(DataGridComboBoxColumn),
            new PropertyMetadata(string.Empty, OnComboBoxPropertyChanged));

    public static readonly DependencyProperty ElementStyleProperty =
        DependencyProperty.Register(nameof(ElementStyle), typeof(Style), typeof(DataGridComboBoxColumn),
            new PropertyMetadata(s_defaultElementStyle, OnComboBoxPropertyChanged));

    public static readonly DependencyProperty EditingElementStyleProperty =
        DependencyProperty.Register(nameof(EditingElementStyle), typeof(Style), typeof(DataGridComboBoxColumn),
            new PropertyMetadata(s_defaultEditingElementStyle, OnComboBoxPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public IEnumerable ItemsSource
    {
        get => (IEnumerable)GetValue(ItemsSourceProperty)!;
        set => SetValue(ItemsSourceProperty, value);
    }

    public string DisplayMemberPath
    {
        get => (string)(GetValue(DisplayMemberPathProperty) ?? string.Empty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    public string SelectedValuePath
    {
        get => (string)(GetValue(SelectedValuePathProperty) ?? string.Empty);
        set => SetValue(SelectedValuePathProperty, value);
    }

    public Style ElementStyle
    {
        get => (Style)(GetValue(ElementStyleProperty) ?? DefaultElementStyle);
        set => SetValue(ElementStyleProperty, value);
    }

    public Style EditingElementStyle
    {
        get => (Style)(GetValue(EditingElementStyleProperty) ?? DefaultEditingElementStyle);
        set => SetValue(EditingElementStyleProperty, value);
    }

    public static Style DefaultElementStyle => s_defaultElementStyle;

    public static Style DefaultEditingElementStyle => s_defaultEditingElementStyle;

    public static ComponentResourceKey TextBlockComboBoxStyleKey => s_textBlockComboBoxStyleKey;

    public virtual BindingBase SelectedItemBinding
    {
        get => _selectedItemBinding!;
        set
        {
            var oldBinding = _selectedItemBinding;
            _selectedItemBinding = value;
            OnSelectedItemBindingChanged(oldBinding!, value);
            NotifyPropertyChanged(nameof(SelectedItemBinding));
            CoerceValue(IsReadOnlyProperty);
        }
    }

    public virtual BindingBase SelectedValueBinding
    {
        get => _selectedValueBinding!;
        set
        {
            var oldBinding = _selectedValueBinding;
            _selectedValueBinding = value;
            OnSelectedValueBindingChanged(oldBinding!, value);
            NotifyPropertyChanged(nameof(SelectedValueBinding));
            CoerceValue(IsReadOnlyProperty);
        }
    }

    public virtual BindingBase TextBinding
    {
        get => _textBinding!;
        set
        {
            var oldBinding = _textBinding;
            _textBinding = value;
            OnTextBindingChanged(oldBinding!, value);
            NotifyPropertyChanged(nameof(TextBinding));
            CoerceValue(IsReadOnlyProperty);
        }
    }

    public override BindingBase ClipboardContentBinding
    {
        get => base.ClipboardContentBinding ?? SelectedItemBinding ?? SelectedValueBinding ?? TextBinding;
        set => base.ClipboardContentBinding = value;
    }

    protected virtual void OnSelectedItemBindingChanged(BindingBase oldBinding, BindingBase newBinding)
    {
    }

    protected virtual void OnSelectedValueBindingChanged(BindingBase oldBinding, BindingBase newBinding)
    {
    }

    protected virtual void OnTextBindingChanged(BindingBase oldBinding, BindingBase newBinding)
    {
    }

    protected override bool OnCoerceIsReadOnly(bool baseValue)
    {
        var effectiveBinding = SelectedItemBinding ?? SelectedValueBinding ?? TextBinding;
        var oneWay = effectiveBinding is Binding binding &&
            binding.Mode is BindingMode.OneWay or BindingMode.OneTime;
        return base.OnCoerceIsReadOnly(baseValue) || oneWay;
    }

    protected override object? GetCellValue(object item) =>
        GetBindingValue(SelectedItemBinding ?? SelectedValueBinding ?? TextBinding, item);

    protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
    {
        var comboBox = CreateComboBox(dataItem, isEditing: false);
        comboBox.IsHitTestVisible = false;
        comboBox.Focusable = false;
        return comboBox;
    }

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem)
        => CreateComboBox(dataItem, isEditing: true);

    protected override object? PrepareCellForEdit(
        FrameworkElement editingElement,
        RoutedEventArgs editingEventArgs)
    {
        if (editingElement is ComboBox comboBox)
        {
            _ = comboBox.Focus();
            comboBox.IsDropDownOpen = true;
            return GetEditingValue(comboBox);
        }

        return base.PrepareCellForEdit(editingElement, editingEventArgs);
    }

    protected override bool CommitCellEdit(FrameworkElement editingElement)
    {
        if (editingElement is ComboBox comboBox && editingElement.DataContext != null)
        {
            if (SelectedItemBinding != null)
            {
                _ = SetBindingValue(SelectedItemBinding, editingElement.DataContext, comboBox.SelectedItem);
            }
            else if (SelectedValueBinding != null)
            {
                _ = SetBindingValue(SelectedValueBinding, editingElement.DataContext, comboBox.SelectedValue);
            }
            else if (TextBinding != null)
            {
                _ = SetBindingValue(TextBinding, editingElement.DataContext, comboBox.Text);
            }
        }

        return true;
    }

    protected override void CancelCellEdit(FrameworkElement editingElement, object uneditedValue)
    {
        if (editingElement is ComboBox comboBox)
        {
            if (SelectedItemBinding != null)
            {
                comboBox.SelectedItem = uneditedValue;
            }
            else if (SelectedValueBinding != null)
            {
                comboBox.SelectedValue = uneditedValue;
            }
            else
            {
                comboBox.Text = uneditedValue?.ToString() ?? string.Empty;
            }
        }
    }

    protected internal override void RefreshCellContent(FrameworkElement element, string propertyName)
    {
        base.RefreshCellContent(element, propertyName);
    }

    protected override void SetClipboardCellValue(object item, object value)
    {
        var binding = SelectedItemBinding ?? SelectedValueBinding ?? TextBinding;
        _ = SetBindingValue(binding, item, value);
    }

    private ComboBox CreateComboBox(object dataItem, bool isEditing)
    {
        var comboBox = new ComboBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            ItemsSource = ItemsSource,
            DisplayMemberPath = DisplayMemberPath,
            SelectedValuePath = SelectedValuePath,
            Style = isEditing ? EditingElementStyle : ElementStyle,
            IsSynchronizedWithCurrentItem = false
        };

        if (SelectedItemBinding != null)
        {
            comboBox.SelectedItem = GetBindingValue(SelectedItemBinding, dataItem);
        }
        else if (SelectedValueBinding != null)
        {
            comboBox.SelectedValue = GetBindingValue(SelectedValueBinding, dataItem);
        }
        else if (TextBinding != null)
        {
            comboBox.Text = GetBindingValue(TextBinding, dataItem)?.ToString() ?? string.Empty;
        }

        return comboBox;
    }

    private object? GetEditingValue(ComboBox comboBox) =>
        SelectedItemBinding != null
            ? comboBox.SelectedItem
            : SelectedValueBinding != null
                ? comboBox.SelectedValue
                : comboBox.Text;

    private static void OnComboBoxPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((DataGridComboBoxColumn)d).NotifyPropertyChanged(e.Property.Name);
    }

    private static Style CreateDefaultComboBoxStyle()
    {
        var style = new Style(typeof(ComboBox));
        style.Setters.Add(new Setter(Selector.IsSynchronizedWithCurrentItemProperty, false));
        style.Seal();
        return style;
    }
}

public class DataGridTemplateColumn : DataGridColumn
{
    public static readonly DependencyProperty CellTemplateProperty =
        DependencyProperty.Register(nameof(CellTemplate), typeof(DataTemplate), typeof(DataGridTemplateColumn),
            new PropertyMetadata(null, OnTemplatePropertyChanged));

    public static readonly DependencyProperty CellEditingTemplateProperty =
        DependencyProperty.Register(nameof(CellEditingTemplate), typeof(DataTemplate), typeof(DataGridTemplateColumn),
            new PropertyMetadata(null, OnTemplatePropertyChanged));

    public static readonly DependencyProperty CellTemplateSelectorProperty =
        DependencyProperty.Register(nameof(CellTemplateSelector), typeof(DataTemplateSelector),
            typeof(DataGridTemplateColumn), new PropertyMetadata(null, OnTemplatePropertyChanged));

    public static readonly DependencyProperty CellEditingTemplateSelectorProperty =
        DependencyProperty.Register(nameof(CellEditingTemplateSelector), typeof(DataTemplateSelector),
            typeof(DataGridTemplateColumn), new PropertyMetadata(null, OnTemplatePropertyChanged));

    public DataTemplate CellTemplate
    {
        get => (DataTemplate)GetValue(CellTemplateProperty)!;
        set => SetValue(CellTemplateProperty, value);
    }

    public DataTemplate CellEditingTemplate
    {
        get => (DataTemplate)GetValue(CellEditingTemplateProperty)!;
        set => SetValue(CellEditingTemplateProperty, value);
    }

    public DataTemplateSelector CellTemplateSelector
    {
        get => (DataTemplateSelector)GetValue(CellTemplateSelectorProperty)!;
        set => SetValue(CellTemplateSelectorProperty, value);
    }

    public DataTemplateSelector CellEditingTemplateSelector
    {
        get => (DataTemplateSelector)GetValue(CellEditingTemplateSelectorProperty)!;
        set => SetValue(CellEditingTemplateSelectorProperty, value);
    }

    protected override object? GetCellValue(object item)
    {
        return item;
    }

    protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
    {
        var template = CellTemplateSelector?.SelectTemplate(dataItem, cell) ?? CellTemplate;
        if (template != null)
        {
            var element = template.LoadContent();
            if (element != null)
            {
                element.DataContext = dataItem;
                return element;
            }
        }

        return new TextBlock { Text = dataItem?.ToString() ?? "" };
    }

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem)
    {
        var template = CellEditingTemplateSelector?.SelectTemplate(dataItem, cell) ??
            CellEditingTemplate ??
            CellTemplateSelector?.SelectTemplate(dataItem, cell) ??
            CellTemplate;
        if (template != null)
        {
            var element = template.LoadContent();
            if (element != null)
            {
                element.DataContext = dataItem;
                return element;
            }
        }

        return null!;
    }

    protected internal override void RefreshCellContent(FrameworkElement element, string propertyName)
    {
        base.RefreshCellContent(element, propertyName);
    }

    private static void OnTemplatePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DataGridTemplateColumn)d).NotifyPropertyChanged(e.Property.Name);
}

/// <summary>
/// Represents a DataGrid column that hosts URI elements in its cells.
/// </summary>
public class DataGridHyperlinkColumn : DataGridBoundColumn
{
    private static readonly Style s_defaultElementStyle = CreateDefaultElementStyle();
    private static readonly Style s_defaultEditingElementStyle = CreateDefaultEditingElementStyle();
    private BindingBase? _contentBinding;

    public static readonly DependencyProperty TargetNameProperty =
        DependencyProperty.Register(nameof(TargetName), typeof(string), typeof(DataGridHyperlinkColumn),
            new PropertyMetadata(string.Empty, OnTargetNameChanged));

    public static Style DefaultElementStyle => s_defaultElementStyle;

    public static Style DefaultEditingElementStyle => s_defaultEditingElementStyle;

    /// <summary>
    /// Gets or sets the binding for the text content of the hyperlink.
    /// </summary>
    public BindingBase ContentBinding
    {
        get => _contentBinding!;
        set
        {
            var oldBinding = _contentBinding;
            _contentBinding = value;
            OnContentBindingChanged(oldBinding!, value);
            NotifyPropertyChanged(nameof(ContentBinding));
        }
    }

    /// <summary>
    /// Gets or sets the name of a target window or frame for the hyperlink.
    /// </summary>
    public string TargetName
    {
        get => (string)(GetValue(TargetNameProperty) ?? string.Empty);
        set => SetValue(TargetNameProperty, value);
    }

    protected virtual void OnContentBindingChanged(BindingBase oldBinding, BindingBase newBinding)
    {
    }

    protected override FrameworkElement GenerateElement(DataGridCell cell, object dataItem)
    {
        var uri = GetCellValue(dataItem);
        var displayText = ContentBinding != null ? GetContentValue(dataItem) : uri;
        var button = new HyperlinkButton
        {
            Content = displayText?.ToString() ?? "",
            VerticalAlignment = VerticalAlignment.Center
        };

        if (uri is Uri uriValue)
        {
            button.NavigateUri = uriValue;
        }
        else if (uri is string uriString && Uri.TryCreate(uriString, UriKind.RelativeOrAbsolute, out var parsed))
        {
            button.NavigateUri = parsed;
        }

        if (ElementStyle != null)
            button.Style = ElementStyle;
        return button;
    }

    protected override FrameworkElement GenerateEditingElement(DataGridCell cell, object dataItem)
    {
        var value = GetCellValue(dataItem);
        var textBox = new TextBox
        {
            Text = value?.ToString() ?? "",
            BorderThickness = new Thickness(0),
            Background = Jalium.UI.Media.Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center
        };
        textBox.Style = EditingElementStyle ?? DefaultEditingElementStyle;
        return textBox;
    }

    protected override object? PrepareCellForEdit(
        FrameworkElement editingElement,
        RoutedEventArgs editingEventArgs)
    {
        if (editingElement is TextBox textBox)
        {
            var original = textBox.Text;
            _ = textBox.Focus();
            textBox.SelectAll();
            return original;
        }

        return base.PrepareCellForEdit(editingElement, editingEventArgs);
    }

    protected internal override void RefreshCellContent(FrameworkElement element, string propertyName)
    {
        base.RefreshCellContent(element, propertyName);
    }

    protected override bool CommitCellEdit(FrameworkElement editingElement)
    {
        if (editingElement is TextBox textBox && editingElement.DataContext != null)
        {
            SetCellValue(editingElement.DataContext, textBox.Text);
        }

        return true;
    }

    protected override void CancelCellEdit(FrameworkElement editingElement, object uneditedValue)
    {
        if (editingElement is TextBox textBox)
        {
            textBox.Text = uneditedValue?.ToString() ?? "";
        }
    }

    private object? GetContentValue(object item)
    {
        return GetBindingValue(ContentBinding, item);
    }

    private static void OnTargetNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DataGridHyperlinkColumn)d).NotifyPropertyChanged(nameof(TargetName));

    private static Style CreateDefaultElementStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(2, 0, 2, 0)));
        style.Seal();
        return style;
    }

    private static Style CreateDefaultEditingElementStyle()
    {
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Seal();
        return style;
    }
}

#endregion
