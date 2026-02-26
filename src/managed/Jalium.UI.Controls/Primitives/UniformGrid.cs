namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Provides a way to arrange content in a grid where all cells have the same size.
/// </summary>
public sealed class UniformGrid : Panel
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Rows dependency property.
    /// </summary>
    public static readonly DependencyProperty RowsProperty =
        DependencyProperty.Register(nameof(Rows), typeof(int), typeof(UniformGrid),
            new PropertyMetadata(0, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the Columns dependency property.
    /// </summary>
    public static readonly DependencyProperty ColumnsProperty =
        DependencyProperty.Register(nameof(Columns), typeof(int), typeof(UniformGrid),
            new PropertyMetadata(0, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the FirstColumn dependency property.
    /// </summary>
    public static readonly DependencyProperty FirstColumnProperty =
        DependencyProperty.Register(nameof(FirstColumn), typeof(int), typeof(UniformGrid),
            new PropertyMetadata(0, OnLayoutPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the number of rows in the grid.
    /// A value of 0 means the number is calculated automatically.
    /// </summary>
    public int Rows
    {
        get => (int)GetValue(RowsProperty)!;
        set => SetValue(RowsProperty, value);
    }

    /// <summary>
    /// Gets or sets the number of columns in the grid.
    /// A value of 0 means the number is calculated automatically.
    /// </summary>
    public int Columns
    {
        get => (int)GetValue(ColumnsProperty)!;
        set => SetValue(ColumnsProperty, value);
    }

    /// <summary>
    /// Gets or sets the number of leading blank cells in the first row.
    /// </summary>
    public int FirstColumn
    {
        get => (int)GetValue(FirstColumnProperty)!;
        set => SetValue(FirstColumnProperty, value);
    }

    #endregion

    #region Private Fields

    private int _computedRows;
    private int _computedColumns;

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        UpdateComputedValues();

        if (_computedRows == 0 || _computedColumns == 0)
            return Size.Empty;

        var childAvailableSize = new Size(
            availableSize.Width / _computedColumns,
            availableSize.Height / _computedRows);

        var maxChildWidth = 0.0;
        var maxChildHeight = 0.0;

        foreach (var child in Children)
        {
            child.Measure(childAvailableSize);
            maxChildWidth = Math.Max(maxChildWidth, child.DesiredSize.Width);
            maxChildHeight = Math.Max(maxChildHeight, child.DesiredSize.Height);
        }

        return new Size(
            maxChildWidth * _computedColumns,
            maxChildHeight * _computedRows);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        UpdateComputedValues();

        if (_computedRows == 0 || _computedColumns == 0)
            return finalSize;

        var cellWidth = finalSize.Width / _computedColumns;
        var cellHeight = finalSize.Height / _computedRows;

        var row = 0;
        var column = FirstColumn;

        foreach (var child in Children)
        {
            // Skip first column cells if specified
            while (row == 0 && column > 0 && column >= _computedColumns)
            {
                column = 0;
                row++;
            }

            var x = column * cellWidth;
            var y = row * cellHeight;

            child.Arrange(new Rect(x, y, cellWidth, cellHeight));

            column++;
            if (column >= _computedColumns)
            {
                column = 0;
                row++;
            }
        }

        return finalSize;
    }

    private void UpdateComputedValues()
    {
        var childCount = Children.Count;
        if (childCount == 0)
        {
            _computedRows = 0;
            _computedColumns = 0;
            return;
        }

        // Account for FirstColumn offset
        var totalCells = childCount + FirstColumn;

        if (Rows > 0 && Columns > 0)
        {
            _computedRows = Rows;
            _computedColumns = Columns;
        }
        else if (Rows > 0)
        {
            _computedRows = Rows;
            _computedColumns = (totalCells + Rows - 1) / Rows;
        }
        else if (Columns > 0)
        {
            _computedColumns = Columns;
            _computedRows = (totalCells + Columns - 1) / Columns;
        }
        else
        {
            // Auto-calculate to be as square as possible
            _computedColumns = (int)Math.Ceiling(Math.Sqrt(totalCells));
            _computedRows = (totalCells + _computedColumns - 1) / _computedColumns;
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UniformGrid grid)
        {
            grid.InvalidateMeasure();
        }
    }

    #endregion
}
