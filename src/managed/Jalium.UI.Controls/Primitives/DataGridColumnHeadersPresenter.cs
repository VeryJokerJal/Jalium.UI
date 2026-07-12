namespace Jalium.UI.Controls.Primitives;

/// <summary>Generates a <see cref="DataGridColumnHeader"/> for each grid column.</summary>
public class DataGridColumnHeadersPresenter : ItemsControl
{
    public DataGrid? DataGridOwner { get; internal set; }

    public DataGridColumnHeadersPresenter()
    {
        var panelTemplate = new ItemsPanelTemplate { PanelType = typeof(DataGridCellsPanel) };
        panelTemplate.Seal();
        ItemsPanel = panelTemplate;
    }

    protected override bool IsItemItsOwnContainerOverride(object item) => item is DataGridColumnHeader;

    protected override DependencyObject GetContainerForItemOverride() => new DataGridColumnHeader();

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is not DataGridColumnHeader header)
        {
            return;
        }

        var column = item as Jalium.UI.Controls.DataGridColumn ?? header.Column;
        if (column == null)
        {
            return;
        }

        header.Column = column;
        header.Content = column.Header;
        var width = column.ActualWidth;
        if (!(width > 0))
        {
            width = column.Width.DisplayValue;
        }
        header.Width = double.IsFinite(width) && width >= 0 ? width : 100;
        header.SortDirection = column.SortDirection switch
        {
            Jalium.UI.Data.ListSortDirection.Ascending => System.ComponentModel.ListSortDirection.Ascending,
            Jalium.UI.Data.ListSortDirection.Descending => System.ComponentModel.ListSortDirection.Descending,
            _ => null
        };
        header.CanUserSort = column.CanUserSort;
        header.DisplayIndex = column.DisplayIndex;
        header.IsFrozen = column.IsFrozen;
        if (column.HeaderStyle != null)
        {
            header.Style = column.HeaderStyle;
        }
    }

    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        if (element is DataGridColumnHeader header)
        {
            header.Content = null;
            header.Column = null;
        }

        base.ClearContainerForItemOverride(element, item);
    }

    internal DataGridColumnHeader CreateHeader(Jalium.UI.Controls.DataGridColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);
        var header = new DataGridColumnHeader { Column = column };
        PrepareContainerForItemOverride(header, column);
        Items.Add(header);
        return header;
    }

    internal void ClearHeaders() => Items.Clear();
}
