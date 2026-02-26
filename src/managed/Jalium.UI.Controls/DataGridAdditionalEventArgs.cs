namespace Jalium.UI.Controls;

/// <summary>
/// Provides data for the <see cref="DataGrid.SelectedCellsChanged"/> event.
/// </summary>
public sealed class SelectedCellsChangedEventArgs : EventArgs
{
    public SelectedCellsChangedEventArgs(IList<DataGridCellInfo> addedCells, IList<DataGridCellInfo> removedCells)
    {
        AddedCells = addedCells ?? throw new ArgumentNullException(nameof(addedCells));
        RemovedCells = removedCells ?? throw new ArgumentNullException(nameof(removedCells));
    }

    /// <summary>Gets the cells that were added to the selection.</summary>
    public IList<DataGridCellInfo> AddedCells { get; }

    /// <summary>Gets the cells that were removed from the selection.</summary>
    public IList<DataGridCellInfo> RemovedCells { get; }
}

/// <summary>
/// Provides data for the <see cref="DataGrid.RowDetailsVisibilityChanged"/> event.
/// </summary>
public sealed class DataGridRowDetailsEventArgs : EventArgs
{
    public DataGridRowDetailsEventArgs(FrameworkElement detailsElement, DataGridRow row)
    {
        DetailsElement = detailsElement;
        Row = row;
    }

    /// <summary>Gets the details element for the row.</summary>
    public FrameworkElement DetailsElement { get; }

    /// <summary>Gets the row that the details are displayed for.</summary>
    public DataGridRow Row { get; }
}

/// <summary>
/// Provides data for the column reordering event.
/// </summary>
public sealed class DataGridColumnReorderingEventArgs : EventArgs
{
    public DataGridColumnReorderingEventArgs(DataGridColumn column)
    {
        Column = column;
    }

    /// <summary>Gets the column being reordered.</summary>
    public DataGridColumn Column { get; }

    /// <summary>Gets or sets a value indicating whether the reorder should be canceled.</summary>
    public bool Cancel { get; set; }

    /// <summary>Gets or sets the drop location indicator column.</summary>
    public DataGridColumn? DropLocationIndicator { get; set; }

    /// <summary>Gets or sets the drag indicator column.</summary>
    public DataGridColumn? DragIndicator { get; set; }
}

/// <summary>
/// Provides data for the <see cref="DataGrid.AddingNewItem"/> event.
/// </summary>
public sealed class AddingNewItemEventArgs : EventArgs
{
    /// <summary>Gets or sets the new item to be added.</summary>
    public object? NewItem { get; set; }
}

/// <summary>
/// Specifies the copy behavior for the <see cref="DataGrid"/> clipboard operations.
/// </summary>
public enum DataGridClipboardCopyMode
{
    /// <summary>Clipboard support is disabled.</summary>
    None,

    /// <summary>Column headers are not included in the clipboard content.</summary>
    ExcludeHeader,

    /// <summary>Column headers are included in the clipboard content.</summary>
    IncludeHeader
}
