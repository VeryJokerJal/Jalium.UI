namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a control that displays a row of cells in a DataGrid.
/// </summary>
public class DataGridCellsPresenter : Panel
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Item dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(nameof(Item), typeof(object), typeof(DataGridCellsPresenter),
            new PropertyMetadata(null, OnItemChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the data item for this row.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public object? Item
    {
        get => GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

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

    #region Property Changed

    private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGridCellsPresenter presenter)
        {
            presenter.OnItemChanged(e.OldValue, e.NewValue);
        }
    }

    /// <summary>
    /// Called when the Item property changes.
    /// </summary>
    protected void OnItemChanged(object? oldItem, object? newItem)
    {
        InvalidateMeasure();
    }

    #endregion
}
