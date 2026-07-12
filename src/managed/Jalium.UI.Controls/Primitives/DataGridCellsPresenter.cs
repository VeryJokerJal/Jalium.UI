using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Generates the cells for one <see cref="DataGridRow"/> and presents them through a
/// <see cref="DataGridCellsPanel"/>.
/// </summary>
public class DataGridCellsPresenter : ItemsControl
{
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(nameof(Item), typeof(object), typeof(DataGridCellsPresenter),
            new PropertyMetadata(null, OnItemPropertyChanged));

    /// <summary>Gets the data item represented by this row of cells.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public object Item
    {
        get => GetValue(ItemProperty)!;
        internal set => SetValue(ItemProperty, value);
    }

    /// <summary>Gets or sets the grid that owns the presenter.</summary>
    public DataGrid? DataGridOwner { get; internal set; }

    public DataGridCellsPresenter()
    {
        var panelTemplate = new ItemsPanelTemplate { PanelType = typeof(DataGridCellsPanel) };
        panelTemplate.Seal();
        ItemsPanel = panelTemplate;
    }

    protected override bool IsItemItsOwnContainerOverride(object item) => item is DataGridCell;

    protected override DependencyObject GetContainerForItemOverride() => new DataGridCell();

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is not DataGridCell cell)
        {
            return;
        }

        var column = item as Jalium.UI.Controls.DataGridColumn;
        if (column == null && DataGridOwner != null)
        {
            var index = ItemContainerGenerator.IndexFromContainer(cell);
            if (index >= 0 && index < DataGridOwner.Columns.Count)
            {
                column = DataGridOwner.Columns[index];
            }
        }

        if (column == null)
        {
            return;
        }

        cell.Column = column;
        cell.DataContext = Item;
        if (Item != null)
        {
            var elementContent = column.BuildVisualTree(isEditing: false, Item, cell);
            elementContent.DataContext = Item;
            cell.Content = elementContent;
        }
    }

    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        if (element is DataGridCell cell)
        {
            cell.Content = null;
            cell.DataContext = null;
        }

        base.ClearContainerForItemOverride(element, item);
    }

    /// <summary>Synchronizes the generated cell containers after the column collection changes.</summary>
    protected internal virtual void OnColumnsChanged(
        ObservableCollection<Jalium.UI.Controls.DataGridColumn> columns,
        NotifyCollectionChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(e);

        Items.Clear();
        foreach (var column in columns)
        {
            Items.Add(column);
        }

        InvalidateMeasure();
    }

    /// <summary>Refreshes realized cells after the represented data item changes.</summary>
    protected virtual void OnItemChanged(object? oldItem, object? newItem)
    {
        for (var index = 0; index < Items.Count; index++)
        {
            if (ItemContainerGenerator.ContainerFromIndex(index) is DependencyObject container)
            {
                PrepareContainerForItemOverride(container, Items[index]);
            }
        }

        InvalidateMeasure();
    }

    private static void OnItemPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((DataGridCellsPresenter)d).OnItemChanged(e.OldValue, e.NewValue);
}
