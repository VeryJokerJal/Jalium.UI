namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a control that displays the rows in a DataGrid.
/// Uses virtualization for efficient rendering of large data sets.
/// </summary>
public sealed class DataGridRowsPresenter : VirtualizingStackPanel
{
    #region CLR Properties

    /// <summary>
    /// Gets or sets the DataGrid that owns this presenter.
    /// </summary>
    public DataGrid? DataGridOwner { get; internal set; }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="DataGridRowsPresenter"/> class.
    /// </summary>
    public DataGridRowsPresenter()
    {
        Orientation = Orientation.Vertical;
    }

    #endregion

    #region Row Management

    /// <summary>
    /// Scrolls to make the specified row visible.
    /// </summary>
    /// <param name="rowIndex">The index of the row to scroll to.</param>
    public void ScrollIntoView(int rowIndex)
    {
        // Calculate the offset needed to show the row
        // This would coordinate with the parent ScrollViewer
        BringIndexIntoView(rowIndex);
    }

    /// <summary>
    /// Gets the row container at the specified index.
    /// </summary>
    /// <param name="index">The index of the row.</param>
    /// <returns>The row container, or null if not realized.</returns>
    public FrameworkElement? GetRowContainer(int index)
    {
        if (index < 0 || index >= Children.Count)
            return null;

        return Children[index] as FrameworkElement;
    }

    #endregion
}
