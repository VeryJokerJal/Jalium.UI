namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a control that displays the column headers in a DataGrid.
/// </summary>
public class DataGridColumnHeadersPresenter : Panel
{
    #region CLR Properties

    /// <summary>
    /// Gets or sets the DataGrid that owns this presenter.
    /// </summary>
    public DataGrid? DataGridOwner { get; internal set; }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var totalWidth = 0.0;
        var maxHeight = 0.0;

        foreach (var child in Children)
        {
            child.Measure(availableSize);
            totalWidth += child.DesiredSize.Width;
            maxHeight = Math.Max(maxHeight, child.DesiredSize.Height);
        }

        return new Size(totalWidth, maxHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;

        foreach (var child in Children)
        {
            var width = child.DesiredSize.Width;
            child.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }

    #endregion

    #region Column Header Management

    /// <summary>
    /// Creates a header for the specified column.
    /// </summary>
    /// <param name="column">The column to create a header for.</param>
    /// <returns>The created column header.</returns>
    internal DataGridColumnHeader CreateHeader(DataGridColumn column)
    {
        var header = new DataGridColumnHeader
        {
            Column = column,
            Content = column.Header,
            Width = column.Width
        };

        Children.Add(header);
        return header;
    }

    /// <summary>
    /// Clears all column headers.
    /// </summary>
    internal void ClearHeaders()
    {
        Children.Clear();
    }

    #endregion
}
