using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that displays a list of data items with optional column view.
/// </summary>
public class ListView : ListBox
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.ListViewAutomationPeer(this);
    }

    private StackPanel? _columnHeadersHost;
    private Border? _columnHeadersBorder;

    /// <summary>
    /// Identifies the View dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public static readonly DependencyProperty ViewProperty =
        DependencyProperty.Register(nameof(View), typeof(ViewBase), typeof(ListView),
            new PropertyMetadata(null, OnViewChanged));

    /// <summary>
    /// Gets or sets an object that defines how the data is styled and organized in a ListView control.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public ViewBase? View
    {
        get => (ViewBase?)GetValue(ViewProperty);
        set => SetValue(ViewProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ListView"/> class.
    /// </summary>
    public ListView()
    {
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _columnHeadersHost = GetTemplateChild("PART_ColumnHeadersHost") as StackPanel;
        _columnHeadersBorder = GetTemplateChild("PART_ColumnHeadersBorder") as Border;

        RefreshColumnHeaders();
    }

    /// <inheritdoc />
    protected override FrameworkElement GetContainerForItem(object item)
    {
        return new ListViewItem();
    }

    /// <inheritdoc />
    protected override bool IsItemItsOwnContainer(object item)
    {
        return item is ListViewItem;
    }

    /// <inheritdoc />
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);

        if (element is ListViewItem listViewItem)
        {
            listViewItem.Content = item;
            listViewItem.ContentTemplate = ItemTemplate;
            listViewItem.ParentListBox = this;
            listViewItem.IsSelected = item == SelectedItem;

            // If using GridView, set up cells
            if (View is GridView gridView)
            {
                listViewItem.SetupGridViewCells(gridView, item);
            }
        }
    }

    private void RefreshColumnHeaders()
    {
        if (_columnHeadersHost == null) return;

        _columnHeadersHost.Children.Clear();

        if (View is GridView gridView && gridView.Columns.Count > 0)
        {
            // Show header border
            if (_columnHeadersBorder != null)
            {
                _columnHeadersBorder.Visibility = Visibility.Visible;
            }

            foreach (var column in gridView.Columns)
            {
                var header = new GridViewColumnHeader
                {
                    Column = column,
                    Content = column.Header,
                    Width = double.IsNaN(column.Width) ? 120 : column.Width
                };

                column.ActualWidth = header.Width;

                _columnHeadersHost.Children.Add(header);
            }
        }
        else
        {
            // Hide header border when not using GridView
            if (_columnHeadersBorder != null)
            {
                _columnHeadersBorder.Visibility = Visibility.Collapsed;
            }
        }
    }

    private static void OnViewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var listView = (ListView)d;
        var oldView = (ViewBase?)e.OldValue;
        var newView = (ViewBase?)e.NewValue;

        oldView?.OnViewDetached(listView);
        newView?.OnViewAttached(listView);

        listView.RefreshColumnHeaders();
        listView.InvalidateMeasure();
    }
}

/// <summary>
/// Represents an item in a ListView control.
/// </summary>
public class ListViewItem : ListBoxItem
{
    private StackPanel? _cellsPanel;

    static ListViewItem()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ListViewItem"/> class.
    /// </summary>
    public ListViewItem()
    {
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _cellsPanel = GetTemplateChild("PART_CellsPanel") as StackPanel;
        OnCellsPanelAttached(_cellsPanel);
    }

    /// <summary>
    /// Called when the cells panel template part is attached.
    /// </summary>
    /// <param name="cellsPanel">The cells panel, if present in template.</param>
    protected virtual void OnCellsPanelAttached(StackPanel? cellsPanel)
    {
    }

    /// <summary>
    /// Sets up cells for GridView column display.
    /// </summary>
    protected internal virtual void SetupGridViewCells(GridView gridView, object item)
    {
        if (_cellsPanel == null) return;

        // Remove any default ContentPresenter and replace with column cells
        _cellsPanel.Children.Clear();

        foreach (var column in gridView.Columns)
        {
            var cellWidth = double.IsNaN(column.Width) ? 120 : column.Width;

            // If the column has a CellTemplate, use ContentPresenter to render it
            if (column.CellTemplate != null)
            {
                var presenter = new ContentPresenter
                {
                    Content = item,
                    ContentTemplate = column.CellTemplate,
                    Width = cellWidth,
                    VerticalAlignment = VerticalAlignment.Center
                };
                _cellsPanel.Children.Add(presenter);
            }
            else
            {
                // Fall back to text rendering using DisplayMemberBinding or header name
                var cellValue = GetPropertyValue(item, column);
                var cellText = new TextBlock
                {
                    Text = cellValue?.ToString() ?? "",
                    Width = cellWidth,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 8, 0)
                };
                _cellsPanel.Children.Add(cellText);
            }
        }
    }

    private static object? GetPropertyValue(object item, GridViewColumn column)
    {
        // Try DisplayMemberBinding path
        if (column.DisplayMemberBinding is Jalium.UI.Binding binding && !string.IsNullOrEmpty(binding.Path?.Path))
        {
            return ResolvePropertyPath(item, binding.Path.Path);
        }

        // Try header name as property name fallback
        var headerText = column.Header?.ToString();
        if (!string.IsNullOrEmpty(headerText))
        {
            var value = ResolvePropertyPath(item, headerText);
            if (value != null) return value;
        }

        // If item is a string or simple type, return it directly
        if (item is string || item.GetType().IsPrimitive)
        {
            return item;
        }

        return null;
    }

    private static object? ResolvePropertyPath(object obj, string path)
    {
        var current = obj;
        foreach (var part in path.Split('.'))
        {
            if (current == null) return null;
            var prop = current.GetType().GetProperty(part, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (prop == null) return null;
            current = prop.GetValue(current);
        }
        return current;
    }
}

/// <summary>
/// Represents the base class for views that define the appearance of data in a ListView control.
/// </summary>
public abstract class ViewBase : DependencyObject
{
    /// <summary>
    /// Gets the default style key.
    /// </summary>
    protected internal virtual object DefaultStyleKey => typeof(ViewBase);

    /// <summary>
    /// Gets the item container default style key.
    /// </summary>
    protected internal virtual object ItemContainerDefaultStyleKey => typeof(ListViewItem);

    /// <summary>
    /// Called when the view is attached to a ListView.
    /// </summary>
    protected internal virtual void OnViewAttached(DependencyObject listView)
    {
    }

    /// <summary>
    /// Called when the view is detached from a ListView.
    /// </summary>
    protected internal virtual void OnViewDetached(DependencyObject listView)
    {
    }
}

/// <summary>
/// Represents a view mode that displays data items in columns for a ListView control.
/// </summary>
public sealed class GridView : ViewBase
{
    private readonly GridViewColumnCollection _columns;

    /// <summary>
    /// Identifies the ColumnHeaderContainerStyle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnHeaderContainerStyleProperty =
        DependencyProperty.Register(nameof(ColumnHeaderContainerStyle), typeof(Style), typeof(GridView),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the ColumnHeaderTemplate dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnHeaderTemplateProperty =
        DependencyProperty.Register(nameof(ColumnHeaderTemplate), typeof(DataTemplate), typeof(GridView),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the ColumnHeaderTemplateSelector dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnHeaderTemplateSelectorProperty =
        DependencyProperty.Register(nameof(ColumnHeaderTemplateSelector), typeof(DataTemplateSelector), typeof(GridView),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the ColumnHeaderContextMenu dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnHeaderContextMenuProperty =
        DependencyProperty.Register(nameof(ColumnHeaderContextMenu), typeof(ContextMenu), typeof(GridView),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the ColumnHeaderToolTip dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnHeaderToolTipProperty =
        DependencyProperty.Register(nameof(ColumnHeaderToolTip), typeof(object), typeof(GridView),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the AllowsColumnReorder dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty AllowsColumnReorderProperty =
        DependencyProperty.Register(nameof(AllowsColumnReorder), typeof(bool), typeof(GridView),
            new PropertyMetadata(true));

    /// <summary>
    /// Gets the collection of GridViewColumn objects that is defined for this GridView.
    /// </summary>
    public GridViewColumnCollection Columns => _columns;

    /// <summary>
    /// Gets or sets the style to apply to column headers.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Style? ColumnHeaderContainerStyle
    {
        get => (Style?)GetValue(ColumnHeaderContainerStyleProperty);
        set => SetValue(ColumnHeaderContainerStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the template to use to display the column headers.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public DataTemplate? ColumnHeaderTemplate
    {
        get => (DataTemplate?)GetValue(ColumnHeaderTemplateProperty);
        set => SetValue(ColumnHeaderTemplateProperty, value);
    }

    /// <summary>
    /// Gets or sets the selector object that provides logic for selecting a template to use for each column header.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public DataTemplateSelector? ColumnHeaderTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(ColumnHeaderTemplateSelectorProperty);
        set => SetValue(ColumnHeaderTemplateSelectorProperty, value);
    }

    /// <summary>
    /// Gets or sets a ContextMenu for the GridView column headers.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public ContextMenu? ColumnHeaderContextMenu
    {
        get => (ContextMenu?)GetValue(ColumnHeaderContextMenuProperty);
        set => SetValue(ColumnHeaderContextMenuProperty, value);
    }

    /// <summary>
    /// Gets or sets the content of a tooltip that appears when the mouse pointer pauses over one of the column headers.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public object? ColumnHeaderToolTip
    {
        get => GetValue(ColumnHeaderToolTipProperty);
        set => SetValue(ColumnHeaderToolTipProperty, value);
    }

    /// <summary>
    /// Gets or sets whether columns in a GridView can be reordered by a drag-and-drop operation.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool AllowsColumnReorder
    {
        get => (bool)GetValue(AllowsColumnReorderProperty)!;
        set => SetValue(AllowsColumnReorderProperty, value);
    }

    /// <inheritdoc />
    protected internal override object DefaultStyleKey => typeof(ListView);

    /// <inheritdoc />
    protected internal override object ItemContainerDefaultStyleKey => typeof(ListViewItem);

    /// <summary>
    /// Initializes a new instance of the <see cref="GridView"/> class.
    /// </summary>
    public GridView()
    {
        _columns = new GridViewColumnCollection();
    }
}

/// <summary>
/// Represents a column that displays data in a GridView.
/// </summary>
public sealed class GridViewColumn : DependencyObject
{
    /// <summary>
    /// Identifies the Header dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(GridViewColumn),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the HeaderContainerStyle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderContainerStyleProperty =
        DependencyProperty.Register(nameof(HeaderContainerStyle), typeof(Style), typeof(GridViewColumn),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the HeaderTemplate dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderTemplateProperty =
        DependencyProperty.Register(nameof(HeaderTemplate), typeof(DataTemplate), typeof(GridViewColumn),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the HeaderTemplateSelector dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderTemplateSelectorProperty =
        DependencyProperty.Register(nameof(HeaderTemplateSelector), typeof(DataTemplateSelector), typeof(GridViewColumn),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the HeaderStringFormat dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty HeaderStringFormatProperty =
        DependencyProperty.Register(nameof(HeaderStringFormat), typeof(string), typeof(GridViewColumn),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Width dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.Register(nameof(Width), typeof(double), typeof(GridViewColumn),
            new PropertyMetadata(double.NaN));

    /// <summary>
    /// Identifies the CellTemplate dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty CellTemplateProperty =
        DependencyProperty.Register(nameof(CellTemplate), typeof(DataTemplate), typeof(GridViewColumn),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the CellTemplateSelector dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty CellTemplateSelectorProperty =
        DependencyProperty.Register(nameof(CellTemplateSelector), typeof(DataTemplateSelector), typeof(GridViewColumn),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the DisplayMemberBinding dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public static readonly DependencyProperty DisplayMemberBindingProperty =
        DependencyProperty.Register(nameof(DisplayMemberBinding), typeof(BindingBase), typeof(GridViewColumn),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the content of the header of a GridViewColumn.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets the style to use for the header of the GridViewColumn.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public Style? HeaderContainerStyle
    {
        get => (Style?)GetValue(HeaderContainerStyleProperty);
        set => SetValue(HeaderContainerStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the template to use to display the content of the column header.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public DataTemplate? HeaderTemplate
    {
        get => (DataTemplate?)GetValue(HeaderTemplateProperty);
        set => SetValue(HeaderTemplateProperty, value);
    }

    /// <summary>
    /// Gets or sets a DataTemplateSelector that provides logic for choosing the template to use to display the column header.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public DataTemplateSelector? HeaderTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(HeaderTemplateSelectorProperty);
        set => SetValue(HeaderTemplateSelectorProperty, value);
    }

    /// <summary>
    /// Gets or sets a composite string that specifies how to format the Header property if it is displayed as a string.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string? HeaderStringFormat
    {
        get => (string?)GetValue(HeaderStringFormatProperty);
        set => SetValue(HeaderStringFormatProperty, value);
    }

    /// <summary>
    /// Gets or sets the width of the column.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double Width
    {
        get => (double)GetValue(WidthProperty)!;
        set => SetValue(WidthProperty, value);
    }

    /// <summary>
    /// Gets the actual width of a GridViewColumn.
    /// </summary>
    public double ActualWidth { get; internal set; }

    /// <summary>
    /// Gets or sets the template to use to display the contents of a column cell.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DataTemplate? CellTemplate
    {
        get => (DataTemplate?)GetValue(CellTemplateProperty);
        set => SetValue(CellTemplateProperty, value);
    }

    /// <summary>
    /// Gets or sets a DataTemplateSelector that determines the template to use to display cells in a column.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public DataTemplateSelector? CellTemplateSelector
    {
        get => (DataTemplateSelector?)GetValue(CellTemplateSelectorProperty);
        set => SetValue(CellTemplateSelectorProperty, value);
    }

    /// <summary>
    /// Gets or sets the data item to bind to for this column.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Items)]
    public BindingBase? DisplayMemberBinding
    {
        get => (BindingBase?)GetValue(DisplayMemberBindingProperty);
        set => SetValue(DisplayMemberBindingProperty, value);
    }
}

/// <summary>
/// Represents a collection of GridViewColumn objects.
/// </summary>
public sealed class GridViewColumnCollection : ObservableCollection<GridViewColumn>
{
}

/// <summary>
/// Represents the header for a GridViewColumn.
/// </summary>
public class GridViewColumnHeader : ContentControl
{
    /// <summary>
    /// Identifies the Column dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnProperty =
        DependencyProperty.Register(nameof(Column), typeof(GridViewColumn), typeof(GridViewColumnHeader),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the GridViewColumn that is associated with this header.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public GridViewColumn? Column
    {
        get => (GridViewColumn?)GetValue(ColumnProperty);
        set => SetValue(ColumnProperty, value);
    }

    /// <summary>
    /// Gets the role of the column header.
    /// </summary>
    public GridViewColumnHeaderRole Role { get; internal set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GridViewColumnHeader"/> class.
    /// </summary>
    public GridViewColumnHeader()
    {
        // Use template-based content management so the ControlTemplate's
        // ContentPresenter handles displaying column header text
        UseTemplateContentManagement();
        Focusable = false;
    }
}

/// <summary>
/// Defines the role of a GridViewColumnHeader control.
/// </summary>
public enum GridViewColumnHeaderRole
{
    /// <summary>
    /// The column header is not floating.
    /// </summary>
    Normal,

    /// <summary>
    /// The column header is floating.
    /// </summary>
    Floating,

    /// <summary>
    /// The column header displays a filler.
    /// </summary>
    Padding
}
