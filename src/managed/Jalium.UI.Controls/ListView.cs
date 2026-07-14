using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that displays a list of data items with optional column view.
/// </summary>
public class ListView : ListBox
{
    private Jalium.UI.Automation.Peers.ListViewAutomationPeer? _listViewAutomationPeer;

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        _listViewAutomationPeer = new Jalium.UI.Automation.Peers.ListViewAutomationPeer(this);
        return _listViewAutomationPeer;
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
    public override void OnApplyTemplate()
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
    protected override bool IsItemItsOwnContainerOverride(object item)
    {
        return item is ListViewItem;
    }

    /// <inheritdoc />
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);

        if (element is ListViewItem listViewItem)
        {
            // When the item IS its own container, do not assign it as its own
            // Content — the template's ContentPresenter would try to parent the
            // element that is already in the items panel, causing a
            // "Visual already has a parent" exception.
            if (!ReferenceEquals(element, item))
            {
                listViewItem.Content = item;
                listViewItem.ContentTemplate = ItemTemplate;
            }

            listViewItem.ParentListBox = this;
            listViewItem.IsSelected = item == SelectedItem;

            // If using GridView, set up cells
            if (View is GridView gridView)
            {
                gridView.PrepareItem(listViewItem);
                listViewItem.SetupGridViewCells(gridView, item);
            }
        }
    }

    /// <inheritdoc />
    protected override void ClearContainerForItem(FrameworkElement element, object item)
    {
        if (element is ListViewItem listViewItem)
        {
            View?.ClearItem(listViewItem);
        }

        base.ClearContainerForItem(element, item);
    }

    /// <inheritdoc />
    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        _listViewAutomationPeer?.NotifyItemsChanged(e);
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
                var headerTemplate = column.HeaderTemplate ?? gridView.ColumnHeaderTemplate;
                var headerTemplateSelector = column.HeaderTemplateSelector ?? gridView.ColumnHeaderTemplateSelector;
                var header = new GridViewColumnHeader
                {
                    Column = column,
                    Content = FormatHeaderContent(
                        column.Header,
                        column.HeaderStringFormat ?? gridView.ColumnHeaderStringFormat,
                        headerTemplate,
                        headerTemplateSelector),
                    ContentTemplate = headerTemplate,
                    ContentTemplateSelector = headerTemplateSelector,
                    ContextMenu = gridView.ColumnHeaderContextMenu,
                    ToolTip = gridView.ColumnHeaderToolTip,
                    Style = column.HeaderContainerStyle ?? gridView.ColumnHeaderContainerStyle,
                    Width = double.IsNaN(column.Width) ? Math.Max(120, column.ActualWidth) : column.Width
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

    private static object? FormatHeaderContent(
        object? header,
        string? format,
        DataTemplate? template,
        DataTemplateSelector? templateSelector)
    {
        if (header == null || string.IsNullOrEmpty(format) || template != null || templateSelector != null)
        {
            return header;
        }

        try
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, format, header);
        }
        catch (FormatException)
        {
            return header;
        }
    }

    private static void OnViewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var listView = (ListView)d;
        var oldView = (ViewBase?)e.OldValue;
        var newView = (ViewBase?)e.NewValue;

        if (oldView is GridView oldGridView)
        {
            oldGridView.ViewChanged -= listView.OnGridViewChanged;
        }

        oldView?.OnViewDetached(listView);
        newView?.OnViewAttached(listView);

        if (listView._listViewAutomationPeer is { } automationPeer)
        {
            automationPeer.ViewAutomationPeer?.ViewDetached();
            automationPeer.ViewAutomationPeer = newView?.GetAutomationPeer(listView);
        }

        if (newView is GridView newGridView)
        {
            newGridView.ViewChanged += listView.OnGridViewChanged;
        }

        listView.RefreshColumnHeaders();
        listView.InvalidateMeasure();
    }

    private void OnGridViewChanged(object? sender, EventArgs e)
    {
        RefreshColumnHeaders();

        if (View is GridView gridView && ItemsHost != null)
        {
            foreach (UIElement child in ItemsHost.Children)
            {
                if (child is ListViewItem listViewItem && listViewItem.Content != null)
                {
                    listViewItem.SetupGridViewCells(gridView, listViewItem.Content);
                }
            }
        }

        InvalidateMeasure();
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
    public override void OnApplyTemplate()
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
        if (column.DisplayMemberBinding is Jalium.UI.Data.Binding binding && !string.IsNullOrEmpty(binding.Path?.Path))
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

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075:RequiresUnreferencedCode",
        Justification = "Property-path resolution here implements GridViewColumn.DisplayMemberBinding (and the header-name fallback), the data-binding reflection fallback over a user-supplied data item whose concrete Type is only known at runtime via object.GetType(). Consumers that bind a GridView column to a property of a user-defined data type must keep that type's public instance properties preserved — the same documented prerequisite that applies to the data-binding surface under trimming/AOT — so this leaf is not a defect of this site.")]
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
    /// <summary>Creates the view-specific automation peer used by a parent ListView.</summary>
    protected internal virtual IViewAutomationPeer? GetAutomationPeer(ListView parent) => null;

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

    /// <summary>
    /// Applies view-specific state to a generated ListViewItem.
    /// </summary>
    protected internal virtual void PrepareItem(ListViewItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
    }

    /// <summary>
    /// Removes view-specific state from a recycled ListViewItem.
    /// </summary>
    protected internal virtual void ClearItem(ListViewItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
    }
}

/// <summary>
/// Represents a view mode that displays data items in columns for a ListView control.
/// </summary>
[Jalium.UI.Markup.ContentProperty(nameof(Columns))]
[StyleTypedProperty(Property = nameof(ColumnHeaderContainerStyle), StyleTargetType = typeof(GridViewColumnHeader))]
public class GridView : ViewBase, Jalium.UI.Markup.IAddChild
{
    private readonly GridViewColumnCollection _columns;
    private readonly HashSet<GridViewColumn> _subscribedColumns = [];

    /// <inheritdoc />
    protected internal override IViewAutomationPeer GetAutomationPeer(ListView parent) =>
        new GridViewAutomationPeer(this, parent);

    private static readonly ResourceKey s_gridViewScrollViewerStyleKey =
        new ComponentResourceKey(typeof(GridView), nameof(GridViewScrollViewerStyleKey));
    private static readonly ResourceKey s_gridViewStyleKey =
        new ComponentResourceKey(typeof(GridView), nameof(GridViewStyleKey));
    private static readonly ResourceKey s_gridViewItemContainerStyleKey =
        new ComponentResourceKey(typeof(GridView), nameof(GridViewItemContainerStyleKey));

    /// <summary>
    /// Identifies the ColumnCollection attached dependency property.
    /// </summary>
    public static readonly DependencyProperty ColumnCollectionProperty =
        DependencyProperty.RegisterAttached(
            "ColumnCollection",
            typeof(GridViewColumnCollection),
            typeof(GridView),
            new PropertyMetadata(null));

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
    /// Identifies the ColumnHeaderStringFormat dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnHeaderStringFormatProperty =
        DependencyProperty.Register(nameof(ColumnHeaderStringFormat), typeof(string), typeof(GridView),
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
    /// Gets the key for the ScrollViewer style used by a GridView.
    /// </summary>
    public static ResourceKey GridViewScrollViewerStyleKey => s_gridViewScrollViewerStyleKey;

    /// <summary>
    /// Gets the key for the default GridView ListView style.
    /// </summary>
    public static ResourceKey GridViewStyleKey => s_gridViewStyleKey;

    /// <summary>
    /// Gets the key for the default GridView item-container style.
    /// </summary>
    public static ResourceKey GridViewItemContainerStyleKey => s_gridViewItemContainerStyleKey;

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
    /// Gets or sets the composite format string used for textual column headers.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public string? ColumnHeaderStringFormat
    {
        get => (string?)GetValue(ColumnHeaderStringFormatProperty);
        set => SetValue(ColumnHeaderStringFormatProperty, value);
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
    protected internal override object DefaultStyleKey => GridViewStyleKey;

    /// <inheritdoc />
    protected internal override object ItemContainerDefaultStyleKey => GridViewItemContainerStyleKey;

    internal event EventHandler? ViewChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="GridView"/> class.
    /// </summary>
    public GridView()
    {
        _columns = new GridViewColumnCollection();
        _columns.CollectionChanged += OnColumnsCollectionChanged;
    }

    /// <summary>
    /// Gets the GridView column collection attached to an element.
    /// </summary>
    public static GridViewColumnCollection? GetColumnCollection(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (GridViewColumnCollection?)element.GetValue(ColumnCollectionProperty);
    }

    /// <summary>
    /// Attaches a GridView column collection to an element.
    /// </summary>
    public static void SetColumnCollection(DependencyObject element, GridViewColumnCollection? collection)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ColumnCollectionProperty, collection);
    }

    /// <summary>
    /// Determines whether the attached column collection should be serialized.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static bool ShouldSerializeColumnCollection(DependencyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        if (obj is ListViewItem { ParentListBox: ListView { View: GridView view } } item)
        {
            return !ReferenceEquals(item.ReadLocalValue(ColumnCollectionProperty), view.Columns);
        }

        return true;
    }

    /// <summary>
    /// Adds a GridViewColumn child declared in markup.
    /// </summary>
    protected virtual void AddChild(object column)
    {
        if (column is GridViewColumn gridViewColumn)
        {
            Columns.Add(gridViewColumn);
            return;
        }

        throw new InvalidOperationException("Only GridViewColumn children can be added to a GridView.");
    }

    /// <summary>
    /// Adds text content. GridView accepts only GridViewColumn children.
    /// </summary>
    protected virtual void AddText(string text) => AddChild(text);

    void Jalium.UI.Markup.IAddChild.AddChild(object value) => AddChild(value);

    void Jalium.UI.Markup.IAddChild.AddText(string text) => AddText(text);

    /// <inheritdoc />
    protected internal override void PrepareItem(ListViewItem item)
    {
        base.PrepareItem(item);
        SetColumnCollection(item, Columns);
    }

    /// <inheritdoc />
    protected internal override void ClearItem(ListViewItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.ClearValue(ColumnCollectionProperty);
        base.ClearItem(item);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var column in _subscribedColumns.ToArray())
            {
                UnsubscribeColumn(column);
            }

            foreach (var column in _columns)
            {
                SubscribeColumn(column);
            }
        }
        else
        {
            if (e.OldItems != null)
            {
                foreach (GridViewColumn column in e.OldItems)
                {
                    UnsubscribeColumn(column);
                }
            }

            if (e.NewItems != null)
            {
                foreach (GridViewColumn column in e.NewItems)
                {
                    SubscribeColumn(column);
                }
            }
        }

        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SubscribeColumn(GridViewColumn column)
    {
        if (_subscribedColumns.Add(column))
        {
            ((System.ComponentModel.INotifyPropertyChanged)column).PropertyChanged += OnColumnPropertyChanged;
        }
    }

    private void UnsubscribeColumn(GridViewColumn column)
    {
        if (_subscribedColumns.Remove(column))
        {
            ((System.ComponentModel.INotifyPropertyChanged)column).PropertyChanged -= OnColumnPropertyChanged;
        }
    }

    private void OnColumnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public override string ToString() => $"{GetType()} Columns.Count:{Columns.Count}";
}

/// <summary>
/// Represents a column that displays data in a GridView.
/// </summary>
public class GridViewColumn : DependencyObject, System.ComponentModel.INotifyPropertyChanged
{
    private double _actualWidth;
    private event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

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
    public double ActualWidth
    {
        get => _actualWidth;
        internal set
        {
            if (_actualWidth.Equals(value))
            {
                return;
            }

            _actualWidth = value;
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(ActualWidth)));
        }
    }

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

    event System.ComponentModel.PropertyChangedEventHandler?
        System.ComponentModel.INotifyPropertyChanged.PropertyChanged
    {
        add => PropertyChanged += value;
        remove => PropertyChanged -= value;
    }

    /// <summary>
    /// Called when the header string format changes.
    /// </summary>
    protected virtual void OnHeaderStringFormatChanged(
        string? oldHeaderStringFormat,
        string? newHeaderStringFormat)
    {
    }

    /// <summary>
    /// Raises the property changed notification.
    /// </summary>
    protected virtual void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        PropertyChanged?.Invoke(this, e);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property == HeaderStringFormatProperty)
        {
            OnHeaderStringFormatChanged(e.OldValue as string, e.NewValue as string);
        }
        else if (e.Property == WidthProperty && e.NewValue is double width && !double.IsNaN(width))
        {
            ActualWidth = Math.Max(0, width);
        }

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(e.Property.Name));
    }

    /// <inheritdoc />
    public override string ToString() => Header?.ToString() ?? base.ToString()!;
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
public class GridViewColumnHeader : Primitives.ButtonBase
{
    private static readonly DependencyPropertyKey ColumnPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(Column),
            typeof(GridViewColumn),
            typeof(GridViewColumnHeader),
            new PropertyMetadata(null));

    private static readonly DependencyPropertyKey RolePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(Role),
            typeof(GridViewColumnHeaderRole),
            typeof(GridViewColumnHeader),
            new PropertyMetadata(GridViewColumnHeaderRole.Normal));

    /// <summary>
    /// Identifies the Column dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ColumnProperty = ColumnPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the read-only Role dependency property.
    /// </summary>
    public static readonly DependencyProperty RoleProperty = RolePropertyKey.DependencyProperty;

    /// <summary>
    /// Gets or sets the GridViewColumn that is associated with this header.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public GridViewColumn? Column
    {
        get => (GridViewColumn?)GetValue(ColumnProperty);
        internal set => SetValue(ColumnPropertyKey, value);
    }

    /// <summary>
    /// Gets the role of the column header.
    /// </summary>
    public GridViewColumnHeaderRole Role =>
        (GridViewColumnHeaderRole)GetValue(RoleProperty)!;

    internal void SetRole(GridViewColumnHeaderRole role) => SetValue(RolePropertyKey, role);

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

    /// <inheritdoc />
    protected override void OnAccessKey(Jalium.UI.Input.AccessKeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnAccessKey(e);
        OnClick();
    }

    /// <inheritdoc />
    protected override void OnClick()
    {
        if (Role == GridViewColumnHeaderRole.Normal)
        {
            base.OnClick();
        }
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
