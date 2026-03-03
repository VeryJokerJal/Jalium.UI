using System.Collections;
using System.Reflection;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that displays hierarchical data in a tree structure.
/// </summary>
public class TreeView : ItemsControl
{
    private TreeViewItem? _selectedItem;

    #region Dependency Properties

    /// <summary>
    /// Identifies the SelectedItem dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(TreeView),
            new PropertyMetadata(null, OnSelectedItemChanged));

    /// <summary>
    /// Identifies the SelectedValue dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectedValueProperty =
        DependencyProperty.Register(nameof(SelectedValue), typeof(object), typeof(TreeView),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the currently selected item.
    /// </summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Gets or sets the value of the selected item.
    /// </summary>
    public object? SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Identifies the SelectedItemChanged routed event.
    /// </summary>
    public static readonly RoutedEvent SelectedItemChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SelectedItemChanged), RoutingStrategy.Bubble,
            typeof(RoutedPropertyChangedEventHandler<object>), typeof(TreeView));

    /// <summary>
    /// Occurs when the selected item changes.
    /// </summary>
    public event RoutedPropertyChangedEventHandler<object?>? SelectedItemChanged
    {
        add => AddHandler(SelectedItemChangedEvent, value);
        remove => RemoveHandler(SelectedItemChangedEvent, value);
    }

    #endregion

    public TreeView()
    {
        if (ItemsPanel == null)
        {
            ItemsPanel = CreateItemsPanelTemplate(typeof(VirtualizingStackPanel));
        }
    }

    #region Template

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
    }

    #endregion

    /// <inheritdoc />
    protected override Panel CreateItemsPanel()
    {
        return new VirtualizingStackPanel { Orientation = Orientation.Vertical };
    }

    /// <inheritdoc />
    protected override FrameworkElement GetContainerForItem(object item)
    {
        return WrapItemAsTreeViewItem(item) ?? new TreeViewItem();
    }

    /// <inheritdoc />
    protected override bool IsItemItsOwnContainer(object item)
    {
        return item is TreeViewItem;
    }

    /// <inheritdoc />
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);
        if (element is not TreeViewItem tvi)
        {
            return;
        }

        tvi.ParentTreeView = this;
        tvi.Level = 0;

        if (item is TreeViewItem)
        {
            return;
        }

        tvi.Header = item;
        tvi.DataContext = item;

        if (ItemTemplate is HierarchicalDataTemplate hdt)
        {
            TreeViewItem.ApplyHierarchicalDataTemplate(tvi, item, hdt);
        }
        else if (ItemTemplate != null)
        {
            tvi.HeaderTemplate = ItemTemplate;
        }
    }

    /// <summary>
    /// Wraps a data item as a TreeViewItem if it isn't one already.
    /// Applies HierarchicalDataTemplate to configure child items.
    /// </summary>
    private TreeViewItem? WrapItemAsTreeViewItem(object item)
    {
        if (item is TreeViewItem tvi)
            return tvi;

        // Create a container for the data item
        tvi = new TreeViewItem { Header = item, DataContext = item };

        // Apply ItemTemplate
        if (ItemTemplate is HierarchicalDataTemplate hdt)
        {
            TreeViewItem.ApplyHierarchicalDataTemplate(tvi, item, hdt);
        }
        else if (ItemTemplate != null)
        {
            tvi.HeaderTemplate = ItemTemplate;
        }

        return tvi;
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeView treeView)
        {
            var oldItem = e.OldValue as TreeViewItem;
            var newItem = e.NewValue as TreeViewItem;

            // Update selection state
            if (oldItem != null)
            {
                oldItem.IsSelected = false;
            }

            if (newItem != null)
            {
                newItem.IsSelected = true;
                treeView._selectedItem = newItem;
            }
            else
            {
                treeView._selectedItem = null;
            }

            // Raise event
            var args = new RoutedPropertyChangedEventArgs<object?>(
                e.OldValue, e.NewValue, SelectedItemChangedEvent);
            treeView.RaiseEvent(args);
        }
    }

    internal void SelectItem(TreeViewItem? item)
    {
        if (_selectedItem != item)
        {
            SelectedItem = item;
        }
    }
}

/// <summary>
/// Represents an item in a TreeView control.
/// </summary>
public class TreeViewItem : HeaderedItemsControl
{
    private const double IndentSize = 16;
    private const double ExpanderSize = 16;

    internal TreeView? ParentTreeView { get; set; }

    private int _level;

    #region Template Parts

    private Border? _headerBorder;
    private Border? _indentSpacer;
    private Border? _expanderBorder;
    private Shapes.Path? _expanderArrow;
    private StackPanel? _itemsHost;
    private Threading.DispatcherTimer? _expandAnimTimer;

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsExpanded dependency property.
    /// </summary>
    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(TreeViewItem),
            new PropertyMetadata(false, OnIsExpandedChanged));

    /// <summary>
    /// Identifies the IsSelected dependency property.
    /// </summary>
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(TreeViewItem),
            new PropertyMetadata(false, OnIsSelectedChanged));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets whether this item is expanded.
    /// </summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty)!;
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>
    /// Gets or sets whether this item is selected.
    /// </summary>
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty)!;
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>
    /// Gets whether this item has child items.
    /// </summary>
    public bool HasItems => Items.Count > 0;

    /// <summary>
    /// Gets or sets the indentation level.
    /// </summary>
    internal int Level
    {
        get => _level;
        set
        {
            _level = value;
            UpdateIndent();
            UpdateDescendantLevels();
        }
    }

    #endregion

    #region Events

    /// <summary>
    /// Identifies the Expanded routed event.
    /// </summary>
    public static readonly RoutedEvent ExpandedEvent =
        EventManager.RegisterRoutedEvent(nameof(Expanded), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(TreeViewItem));

    /// <summary>
    /// Identifies the Collapsed routed event.
    /// </summary>
    public static readonly RoutedEvent CollapsedEvent =
        EventManager.RegisterRoutedEvent(nameof(Collapsed), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(TreeViewItem));

    /// <summary>
    /// Occurs when the item is expanded.
    /// </summary>
    public event RoutedEventHandler Expanded
    {
        add => AddHandler(ExpandedEvent, value);
        remove => RemoveHandler(ExpandedEvent, value);
    }

    /// <summary>
    /// Occurs when the item is collapsed.
    /// </summary>
    public event RoutedEventHandler Collapsed
    {
        add => AddHandler(CollapsedEvent, value);
        remove => RemoveHandler(CollapsedEvent, value);
    }

    #endregion

    public TreeViewItem()
    {
        Padding = new Thickness(4, 2, 4, 2);

        Items.CollectionChanged += OnChildItemsChanged;
    }

    /// <summary>
    /// Override to prevent ItemsControl's fallback panel from interfering.
    /// TreeViewItem manages children via PART_ItemsHost in the template.
    /// </summary>
    protected override void RefreshItems()
    {
        // No-op: TreeViewItem manages children via _itemsHost (template part)
    }

    #region Template

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _headerBorder = GetTemplateChild("PART_HeaderBorder") as Border;
        _indentSpacer = GetTemplateChild("PART_IndentSpacer") as Border;
        _expanderBorder = GetTemplateChild("PART_ExpanderBorder") as Border;
        _expanderArrow = GetTemplateChild("PART_ExpanderArrow") as Shapes.Path;
        _itemsHost = GetTemplateChild("PART_ItemsHost") as StackPanel;

        // Attach click handler to header border only (not the whole item)
        // so child item clicks don't bubble up to parent items
        if (_headerBorder != null)
        {
            _headerBorder.AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler), true);
        }

        // Sync initial state
        UpdateIndent();
        UpdateExpanderVisibility();

        // Add existing items to the items host panel
        if (_itemsHost != null)
        {
            foreach (var item in Items)
            {
                if (item is TreeViewItem childTvi)
                {
                    childTvi.ParentTreeView = ParentTreeView;
                    childTvi.Level = Level + 1;
                    _itemsHost.Children.Add(childTvi);
                }
            }

            // Sync expanded visuals (IsExpanded may have been set before template was applied)
            SyncExpandedVisualState();
        }
        else
        {
            SyncExpandedVisualState();
        }
    }

    /// <inheritdoc />
    protected override void OnVisualParentChanged(Visual? oldParent)
    {
        base.OnVisualParentChanged(oldParent);

        // IsExpanded may be set before the item is attached to a window.
        // Re-sync once attached so layout/render is guaranteed to update.
        if (VisualParent != null)
        {
            SyncExpandedVisualState();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    #endregion

    private void OnChildItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        try
        {
            // Handle removed items
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item is TreeViewItem childTvi)
                    {
                        _itemsHost?.Children.Remove(childTvi);
                        childTvi.ParentTreeView = null;
                    }
                }
            }

            // Handle added items
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is TreeViewItem childTvi)
                    {
                        childTvi.ParentTreeView = ParentTreeView;
                        childTvi.Level = Level + 1;
                        _itemsHost?.Children.Add(childTvi);
                    }
                }
            }

            // Handle reset
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            {
                _itemsHost?.Children.Clear();
                foreach (var item in Items)
                {
                    if (item is TreeViewItem childTvi)
                    {
                        childTvi.ParentTreeView = ParentTreeView;
                        childTvi.Level = Level + 1;
                        _itemsHost?.Children.Add(childTvi);
                    }
                }
            }

            UpdateExpanderVisibility();
            UpdateDescendantLevels();
            InvalidateMeasure();
        }
        catch
        {
            // Ignored
        }
    }

    private void UpdateDescendantLevels()
    {
        foreach (var item in Items)
        {
            if (item is not TreeViewItem childTvi)
            {
                continue;
            }

            childTvi.ParentTreeView = ParentTreeView;
            childTvi.Level = _level + 1;
        }
    }

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        if (e is MouseButtonEventArgs mouseArgs)
        {
            // Check if clicked on expander area
            if (HasItems && _expanderBorder is { Visibility: Visibility.Visible } expander)
            {
                var pos = mouseArgs.GetPosition(expander);
                if (pos.X >= 0 && pos.X <= expander.ActualWidth &&
                    pos.Y >= 0 && pos.Y <= expander.ActualHeight)
                {
                    IsExpanded = !IsExpanded;
                    e.Handled = true;
                    return;
                }
            }

            // Select this item
            ParentTreeView?.SelectItem(this);
            e.Handled = true;
        }
    }

    #region State Updates

    private void UpdateIndent()
    {
        if (_indentSpacer != null)
        {
            _indentSpacer.Width = _level * IndentSize;
        }
    }

    private void UpdateExpanderVisibility()
    {
        if (_expanderBorder != null)
        {
            _expanderBorder.Visibility = HasItems ? Visibility.Visible : Visibility.Collapsed;
        }

        // When children are realized after template application, keep glyph direction
        // in sync with the current expanded state.
        if (_expanderArrow != null)
        {
            var rotateTransform = _expanderArrow.RenderTransform as RotateTransform ?? new RotateTransform();
            rotateTransform.Angle = IsExpanded ? 90 : 0;
            _expanderArrow.RenderTransform = rotateTransform;
            _expanderArrow.InvalidateVisual();
        }
    }

    private void SyncExpandedVisualState()
    {
        if (_itemsHost != null)
        {
            _itemsHost.Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_expanderArrow != null)
        {
            var rotateTransform = _expanderArrow.RenderTransform as RotateTransform ?? new RotateTransform();
            rotateTransform.Angle = IsExpanded ? 90 : 0;
            _expanderArrow.RenderTransform = rotateTransform;
            _expanderArrow.InvalidateVisual();
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeViewItem tvi)
        {
            var expanded = (bool)e.NewValue;

            if (expanded)
                tvi.RaiseEvent(new RoutedEventArgs(ExpandedEvent, tvi));
            else
                tvi.RaiseEvent(new RoutedEventArgs(CollapsedEvent, tvi));

            // Animate items host panel expand/collapse + arrow rotation
            if (tvi._itemsHost != null)
            {
                if (expanded)
                    tvi._expandAnimTimer = ExpandCollapseAnimator.AnimateExpand(tvi._itemsHost, tvi._expandAnimTimer, tvi._expanderArrow);
                else
                    tvi._expandAnimTimer = ExpandCollapseAnimator.AnimateCollapse(tvi._itemsHost, tvi._expandAnimTimer, tvi._expanderArrow);
            }

            tvi.InvalidateMeasure();
            tvi.ParentTreeView?.InvalidateMeasure();
        }
    }

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeViewItem tvi)
        {
            if ((bool)e.NewValue && tvi.ParentTreeView != null)
            {
                tvi.ParentTreeView.SelectItem(tvi);
            }
        }
    }

    #endregion

    #region HierarchicalDataTemplate Support

    /// <summary>
    /// Applies a HierarchicalDataTemplate to a TreeViewItem, setting up header template
    /// and populating child items from the ItemsSource binding.
    /// </summary>
    internal static void ApplyHierarchicalDataTemplate(TreeViewItem tvi, object dataItem, HierarchicalDataTemplate hdt)
    {
        // Set the header template
        tvi.Header = dataItem;
        tvi.HeaderTemplate = hdt;

        // Apply ItemTemplate for child items
        if (hdt.ItemTemplate != null)
            tvi.ItemTemplate = hdt.ItemTemplate;
        else
            tvi.ItemTemplate = hdt; // Recursive: same template for children

        // Resolve ItemsSource binding to populate children
        if (hdt.ItemsSource is Jalium.UI.Binding binding && !string.IsNullOrEmpty(binding.Path?.Path))
        {
            var childItems = ResolvePropertyPath(dataItem, binding.Path.Path);
            if (childItems is IEnumerable enumerable)
            {
                foreach (var childItem in enumerable)
                {
                    if (childItem is TreeViewItem childTvi)
                    {
                        tvi.Items.Add(childTvi);
                    }
                    else
                    {
                        var childContainer = new TreeViewItem
                        {
                            Header = childItem,
                            DataContext = childItem
                        };

                        // Recursively apply the template to children
                        var childTemplate = hdt.ItemTemplate as HierarchicalDataTemplate ?? hdt;
                        ApplyHierarchicalDataTemplate(childContainer, childItem, childTemplate);

                        tvi.Items.Add(childContainer);
                    }
                }
            }
        }
    }

    private static object? ResolvePropertyPath(object obj, string path)
    {
        var current = obj;
        foreach (var part in path.Split('.'))
        {
            if (current == null) return null;
            var prop = current.GetType().GetProperty(part, BindingFlags.Public | BindingFlags.Instance);
            if (prop == null) return null;
            current = prop.GetValue(current);
        }
        return current;
    }

    #endregion
}

// Note: HeaderedItemsControl is defined in Menu.cs
