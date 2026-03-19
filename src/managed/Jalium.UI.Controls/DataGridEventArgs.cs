using System.Collections;
using System.ComponentModel;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides data for the <see cref="DataGrid.AutoGeneratingColumn"/> event.
/// </summary>
public sealed class DataGridAutoGeneratingColumnEventArgs : EventArgs
{
    public DataGridAutoGeneratingColumnEventArgs(string propertyName, Type propertyType, DataGridColumn column)
    {
        PropertyName = propertyName;
        PropertyType = propertyType;
        Column = column;
    }

    /// <summary>Gets the name of the property bound to the generated column.</summary>
    public string PropertyName { get; }

    /// <summary>Gets the type of the property bound to the generated column.</summary>
    public Type PropertyType { get; }

    /// <summary>Gets or sets the generated column.</summary>
    public DataGridColumn Column { get; set; }

    /// <summary>Gets or sets a value indicating whether the event should be canceled.</summary>
    public bool Cancel { get; set; }

    /// <summary>Gets the PropertyDescriptor for the property bound to the generated column.</summary>
    public PropertyDescriptor? PropertyDescriptor { get; init; }
}

/// <summary>
/// Provides data for the <see cref="DataGrid.RowEditEnding"/> event.
/// </summary>
public sealed class DataGridRowEditEndingEventArgs : EventArgs
{
    public DataGridRowEditEndingEventArgs(DataGridRow row, DataGridEditAction editAction)
    {
        Row = row;
        EditAction = editAction;
    }

    /// <summary>Gets the row for which the event occurred.</summary>
    public DataGridRow Row { get; }

    /// <summary>Gets the edit action (Commit or Cancel).</summary>
    public DataGridEditAction EditAction { get; }

    /// <summary>Gets or sets a value indicating whether the event should be canceled.</summary>
    public bool Cancel { get; set; }
}

/// <summary>
/// Provides data for the <see cref="DataGrid.ColumnDisplayIndexChanged"/> event.
/// </summary>
public sealed class DataGridColumnEventArgs : EventArgs
{
    public DataGridColumnEventArgs(DataGridColumn column)
    {
        Column = column;
    }

    /// <summary>Gets the column associated with the event.</summary>
    public DataGridColumn Column { get; }
}

/// <summary>
/// Provides data for <see cref="DataGrid"/> row-related events.
/// </summary>
public sealed class DataGridRowEventArgs : EventArgs
{
    public DataGridRowEventArgs(DataGridRow row)
    {
        Row = row;
    }

    /// <summary>Gets the row associated with the event.</summary>
    public DataGridRow Row { get; }
}

/// <summary>
/// Represents a cell in a <see cref="DataGrid"/>.
/// </summary>
public readonly struct DataGridCellInfo : IEquatable<DataGridCellInfo>
{
    /// <summary>Initializes a new instance with the specified item and column.</summary>
    public DataGridCellInfo(object item, DataGridColumn column)
    {
        Item = item;
        Column = column;
    }

    /// <summary>Initializes a new instance from a DataGridCell.</summary>
    public DataGridCellInfo(DataGridCell cell)
    {
        Item = cell.DataContext;
        Column = cell.Column;
    }

    /// <summary>Gets the data item for the row that contains the cell.</summary>
    public object? Item { get; }

    /// <summary>Gets the column that contains the cell.</summary>
    public DataGridColumn? Column { get; }

    /// <summary>Gets a value indicating whether this instance is valid.</summary>
    public bool IsValid => Column != null;

    public bool Equals(DataGridCellInfo other) =>
        Equals(Item, other.Item) && Equals(Column, other.Column);

    public override bool Equals(object? obj) =>
        obj is DataGridCellInfo other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Item, Column);

    public static bool operator ==(DataGridCellInfo left, DataGridCellInfo right) => left.Equals(right);
    public static bool operator !=(DataGridCellInfo left, DataGridCellInfo right) => !left.Equals(right);
}

/// <summary>
/// Represents the content of a cell for clipboard operations.
/// </summary>
public readonly struct DataGridClipboardCellContent
{
    public DataGridClipboardCellContent(object item, DataGridColumn column, object? content)
    {
        Item = item;
        Column = column;
        Content = content;
    }

    /// <summary>Gets the data item for the row.</summary>
    public object Item { get; }

    /// <summary>Gets the column.</summary>
    public DataGridColumn Column { get; }

    /// <summary>Gets the text content of the cell.</summary>
    public object? Content { get; }
}

/// <summary>
/// Provides data for the <see cref="DataGrid.CopyingRowClipboardContent"/> event.
/// </summary>
public sealed class DataGridClipboardCopyingEventArgs : EventArgs
{
    public DataGridClipboardCopyingEventArgs(IList<DataGridClipboardCellContent> clipboardRowContent)
    {
        ClipboardRowContent = clipboardRowContent;
    }

    /// <summary>Gets the list of DataGridClipboardCellContent objects.</summary>
    public IList<DataGridClipboardCellContent> ClipboardRowContent { get; }

    /// <summary>Gets or sets a value indicating whether to cancel the default clipboard copying.</summary>
    public bool Cancel { get; set; }

    /// <summary>Gets or sets the first row index (for header row).</summary>
    public int StartColumnDisplayIndex { get; set; }

    /// <summary>Gets or sets the last row index (for header row).</summary>
    public int EndColumnDisplayIndex { get; set; }

    /// <summary>Gets or sets a value indicating whether this is the column headers row.</summary>
    public bool IsColumnHeadersRow { get; set; }

    /// <summary>Gets the item for this row.</summary>
    public object? Item { get; init; }
}

/// <summary>
/// Provides data for cell clipboard events.
/// </summary>
public sealed class DataGridCellClipboardEventArgs : EventArgs
{
    public DataGridCellClipboardEventArgs(object item, DataGridColumn column, object? content)
    {
        Item = item;
        Column = column;
        Content = content;
    }

    /// <summary>Gets the data item.</summary>
    public object Item { get; }

    /// <summary>Gets the column.</summary>
    public DataGridColumn Column { get; }

    /// <summary>Gets or sets the cell content.</summary>
    public object? Content { get; set; }
}

/// <summary>
/// Provides data for row clipboard events.
/// </summary>
public sealed class DataGridRowClipboardEventArgs : EventArgs
{
    public DataGridRowClipboardEventArgs(object item, int startColumnDisplayIndex, int endColumnDisplayIndex, bool isColumnHeadersRow)
    {
        Item = item;
        StartColumnDisplayIndex = startColumnDisplayIndex;
        EndColumnDisplayIndex = endColumnDisplayIndex;
        IsColumnHeadersRow = isColumnHeadersRow;
        ClipboardRowContent = new List<DataGridClipboardCellContent>();
    }

    /// <summary>Gets the data item for this row.</summary>
    public object Item { get; }

    /// <summary>Gets the starting column display index.</summary>
    public int StartColumnDisplayIndex { get; }

    /// <summary>Gets the ending column display index.</summary>
    public int EndColumnDisplayIndex { get; }

    /// <summary>Gets a value indicating whether this is a column headers row.</summary>
    public bool IsColumnHeadersRow { get; }

    /// <summary>Gets the list of cell content for clipboard operations.</summary>
    public IList<DataGridClipboardCellContent> ClipboardRowContent { get; }

    /// <summary>
    /// Formats the cell values into a string for the clipboard.
    /// </summary>
    public string FormatClipboardCellValues(string separator)
    {
        return string.Join(separator, ClipboardRowContent.Select(c => c.Content?.ToString() ?? string.Empty));
    }
}

/// <summary>
/// Provides data for the <see cref="DataGrid.InitializingNewItem"/> event.
/// </summary>
public sealed class InitializingNewItemEventArgs : EventArgs
{
    public InitializingNewItemEventArgs(object newItem)
    {
        NewItem = newItem;
    }

    /// <summary>Gets the new item being initialized.</summary>
    public object NewItem { get; }
}

