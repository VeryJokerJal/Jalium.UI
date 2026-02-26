using System.Collections.ObjectModel;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents the abstract base class for objects that present row data in a GridView.
/// </summary>
public abstract class GridViewRowPresenterBase : FrameworkElement
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Columns dependency property.
    /// </summary>
    public static readonly DependencyProperty ColumnsProperty =
        DependencyProperty.Register(nameof(Columns), typeof(GridViewColumnCollection), typeof(GridViewRowPresenterBase),
            new PropertyMetadata(null, OnColumnsChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the collection of columns.
    /// </summary>
    public GridViewColumnCollection? Columns
    {
        get => (GridViewColumnCollection?)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    #endregion

    #region Column Change Handling

    private static void OnColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is GridViewRowPresenterBase presenter)
        {
            presenter.OnColumnsChanged(
                e.OldValue as GridViewColumnCollection,
                e.NewValue as GridViewColumnCollection);
        }
    }

    /// <summary>
    /// Called when the Columns property changes.
    /// </summary>
    /// <param name="oldColumns">The old columns collection.</param>
    /// <param name="newColumns">The new columns collection.</param>
    protected virtual void OnColumnsChanged(GridViewColumnCollection? oldColumns, GridViewColumnCollection? newColumns)
    {
        InvalidateMeasure();
    }

    #endregion

    #region Layout Helpers

    /// <summary>
    /// Gets the total width of all columns.
    /// </summary>
    protected double GetTotalColumnWidth()
    {
        if (Columns == null)
            return 0;

        double total = 0;
        foreach (var column in Columns)
        {
            total += column.ActualWidth;
        }
        return total;
    }

    /// <summary>
    /// Gets the column at the specified X position.
    /// </summary>
    /// <param name="x">The X position.</param>
    /// <returns>The column at the position, or null.</returns>
    protected GridViewColumn? GetColumnAtPosition(double x)
    {
        if (Columns == null)
            return null;

        double currentX = 0;
        foreach (var column in Columns)
        {
            if (x >= currentX && x < currentX + column.ActualWidth)
            {
                return column;
            }
            currentX += column.ActualWidth;
        }

        return null;
    }

    #endregion
}

/// <summary>
/// Represents a collection of GridView columns.
/// </summary>
public sealed class GridViewColumnCollection : ObservableCollection<GridViewColumn>
{
}

/// <summary>
/// Represents a column in a GridView.
/// </summary>
public class GridViewColumn
{
    /// <summary>
    /// Gets or sets the header content.
    /// </summary>
    public object? Header { get; set; }

    /// <summary>
    /// Gets or sets the width of the column.
    /// </summary>
    public double Width { get; set; } = double.NaN;

    /// <summary>
    /// Gets the actual width of the column.
    /// </summary>
    public double ActualWidth => double.IsNaN(Width) ? 100 : Width;

    /// <summary>
    /// Gets or sets the display member binding path.
    /// </summary>
    public string? DisplayMemberBinding { get; set; }

    /// <summary>
    /// Gets or sets the cell template.
    /// </summary>
    public DataTemplate? CellTemplate { get; set; }

    /// <summary>
    /// Gets or sets the header template.
    /// </summary>
    public DataTemplate? HeaderTemplate { get; set; }
}
