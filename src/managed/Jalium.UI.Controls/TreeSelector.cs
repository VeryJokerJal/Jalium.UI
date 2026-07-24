using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies how check-box state propagates between a TreeSelectorItem and its descendants.
/// </summary>
public enum TreeSelectorCheckCascadeMode
{
    /// <summary>Each item's check state is independent — toggling a parent does not affect children.</summary>
    None,
    /// <summary>Toggling a parent applies the same checked state to all descendants;
    /// the parent reflects mixed (indeterminate) when descendants disagree.</summary>
    Cascade
}

/// <summary>
/// A drop-down hierarchical selector (similar to Ant Design's TreeSelect): the trigger shows
/// the current selection — as a path string in single-select, or as a chip list in multi-select —
/// and clicking it opens a popup containing a checkable tree. An optional search box filters
/// the tree as the user types.
/// </summary>
public class TreeSelector : ItemsControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.TreeSelectorAutomationPeer(this);
    }

    private readonly ObservableCollection<object> _selectedItems = new();
    private readonly ObservableCollection<object> _checkedItems = new();
    private bool _isSyncingSelection;
    private bool _isSyncingCheck;
    internal Style? _cachedItemStyle;

    // Trigger / dropdown template parts
    private Border? _triggerBorder;
    private TextBlock? _displayText;
    private Panel? _tagsPanel;
    private TextBox? _searchTextBox;
    private TextBlock? _placeholderText;
    private Button? _clearButton;
    private Shapes.Path? _dropDownArrow;
    private Popup? _popup;
    private bool _isUpdatingSearchTextFromCode;
    private bool _searchHandlersAttached;

    #region Dependency Properties

    /// <summary>Identifies the SelectionMode dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectionModeProperty =
        DependencyProperty.Register(nameof(SelectionMode), typeof(SelectionMode), typeof(TreeSelector),
            new PropertyMetadata(SelectionMode.Single, OnSelectionModeChanged));

    /// <summary>Identifies the SelectedItem dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectedItemProperty =
        DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(TreeSelector),
            new PropertyMetadata(null, OnSelectedItemChanged));

    /// <summary>Identifies the ShowCheckBoxes dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowCheckBoxesProperty =
        DependencyProperty.Register(nameof(ShowCheckBoxes), typeof(bool), typeof(TreeSelector),
            new PropertyMetadata(false, OnShowCheckBoxesChanged));

    /// <summary>Identifies the CheckCascadeMode dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty CheckCascadeModeProperty =
        DependencyProperty.Register(nameof(CheckCascadeMode), typeof(TreeSelectorCheckCascadeMode), typeof(TreeSelector),
            new PropertyMetadata(TreeSelectorCheckCascadeMode.None));

    /// <summary>Identifies the AutoExpandSelected dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty AutoExpandSelectedProperty =
        DependencyProperty.Register(nameof(AutoExpandSelected), typeof(bool), typeof(TreeSelector),
            new PropertyMetadata(false));

    /// <summary>Identifies the IsDropDownOpen dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsDropDownOpenProperty =
        DependencyProperty.Register(nameof(IsDropDownOpen), typeof(bool), typeof(TreeSelector),
            new PropertyMetadata(false, OnIsDropDownOpenChanged));

    /// <summary>Identifies the PlaceholderText dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.Register(nameof(PlaceholderText), typeof(string), typeof(TreeSelector),
            new PropertyMetadata(string.Empty));

    /// <summary>Identifies the IsSearchEnabled dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSearchEnabledProperty =
        DependencyProperty.Register(nameof(IsSearchEnabled), typeof(bool), typeof(TreeSelector),
            new PropertyMetadata(false, OnIsSearchEnabledChanged));

    /// <summary>Identifies the SearchText dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(TreeSelector),
            new PropertyMetadata(string.Empty, OnSearchTextChanged));

    /// <summary>Identifies the MaxDropDownHeight dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaxDropDownHeightProperty =
        DependencyProperty.Register(nameof(MaxDropDownHeight), typeof(double), typeof(TreeSelector),
            new PropertyMetadata(320.0));

    /// <summary>Identifies the PathSeparator dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty PathSeparatorProperty =
        DependencyProperty.Register(nameof(PathSeparator), typeof(string), typeof(TreeSelector),
            new PropertyMetadata(" / "));

    #endregion

    #region Routed Events

    /// <summary>Identifies the SelectionChanged routed event.</summary>
    public static readonly RoutedEvent SelectionChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SelectionChanged), RoutingStrategy.Bubble,
            typeof(SelectionChangedEventHandler), typeof(TreeSelector));

    /// <summary>Identifies the ItemCheckStateChanged routed event.</summary>
    public static readonly RoutedEvent ItemCheckStateChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(ItemCheckStateChanged), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(TreeSelector));

    /// <summary>Identifies the DropDownOpened routed event.</summary>
    public static readonly RoutedEvent DropDownOpenedEvent =
        EventManager.RegisterRoutedEvent(nameof(DropDownOpened), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(TreeSelector));

    /// <summary>Identifies the DropDownClosed routed event.</summary>
    public static readonly RoutedEvent DropDownClosedEvent =
        EventManager.RegisterRoutedEvent(nameof(DropDownClosed), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(TreeSelector));

    /// <summary>Occurs when the selection changes.</summary>
    public event SelectionChangedEventHandler SelectionChanged
    {
        add => AddHandler(SelectionChangedEvent, value);
        remove => RemoveHandler(SelectionChangedEvent, value);
    }

    /// <summary>Occurs when the IsChecked state of any item changes.</summary>
    public event RoutedEventHandler ItemCheckStateChanged
    {
        add => AddHandler(ItemCheckStateChangedEvent, value);
        remove => RemoveHandler(ItemCheckStateChangedEvent, value);
    }

    /// <summary>Occurs when the dropdown opens.</summary>
    public event RoutedEventHandler DropDownOpened
    {
        add => AddHandler(DropDownOpenedEvent, value);
        remove => RemoveHandler(DropDownOpenedEvent, value);
    }

    /// <summary>Occurs when the dropdown closes.</summary>
    public event RoutedEventHandler DropDownClosed
    {
        add => AddHandler(DropDownClosedEvent, value);
        remove => RemoveHandler(DropDownClosedEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>Gets or sets the selection mode.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public SelectionMode SelectionMode
    {
        get => (SelectionMode)GetValue(SelectionModeProperty)!;
        set => SetValue(SelectionModeProperty, value);
    }

    /// <summary>Gets or sets the focused (currently selected) item. In Multiple mode this is the most recently selected.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>Gets the currently selected items. In Single selection mode the collection holds at most one item.</summary>
    public IList SelectedItems => _selectedItems;

    /// <summary>Gets the currently checked items (only meaningful when ShowCheckBoxes is true).</summary>
    public IList CheckedItems => _checkedItems;

    /// <summary>Gets or sets whether checkboxes are displayed for each item.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowCheckBoxes
    {
        get => (bool)GetValue(ShowCheckBoxesProperty)!;
        set => SetValue(ShowCheckBoxesProperty, value);
    }

    /// <summary>Gets or sets how check-state cascades between an item and its descendants.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public TreeSelectorCheckCascadeMode CheckCascadeMode
    {
        get => (TreeSelectorCheckCascadeMode)GetValue(CheckCascadeModeProperty)!;
        set => SetValue(CheckCascadeModeProperty, value);
    }

    /// <summary>Gets or sets whether ancestors of a newly-selected item should be auto-expanded.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool AutoExpandSelected
    {
        get => (bool)GetValue(AutoExpandSelectedProperty)!;
        set => SetValue(AutoExpandSelectedProperty, value);
    }

    /// <summary>Gets or sets whether the dropdown popup is open.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsDropDownOpen
    {
        get => (bool)GetValue(IsDropDownOpenProperty)!;
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>Gets or sets the placeholder text shown when nothing is selected.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string PlaceholderText
    {
        get => (string)(GetValue(PlaceholderTextProperty) ?? string.Empty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>Gets or sets whether the trigger shows a text box that filters the tree as the user types.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSearchEnabled
    {
        get => (bool)GetValue(IsSearchEnabledProperty)!;
        set => SetValue(IsSearchEnabledProperty, value);
    }

    /// <summary>Gets or sets the current search text. Setting this filters the tree.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string SearchText
    {
        get => (string)(GetValue(SearchTextProperty) ?? string.Empty);
        set => SetValue(SearchTextProperty, value);
    }

    /// <summary>Gets or sets the maximum height of the dropdown popup.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MaxDropDownHeight
    {
        get => (double)GetValue(MaxDropDownHeightProperty)!;
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    /// <summary>Gets or sets the separator used to join ancestor labels in the single-select trigger display.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string PathSeparator
    {
        get => (string)(GetValue(PathSeparatorProperty) ?? " / ");
        set => SetValue(PathSeparatorProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="TreeSelector"/> class.
    /// </summary>
    public TreeSelector()
    {
        Focusable = true;
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");

        if (ItemsPanel == null)
        {
            ItemsPanel = CreateItemsPanelTemplate(typeof(VirtualizingStackPanel));
        }

        _selectedItems.CollectionChanged += OnSelectedItemsCollectionChanged;
        _checkedItems.CollectionChanged += OnCheckedItemsCollectionChanged;

        // The dropdown popup is closed by default, so the standard ItemsControl path through
        // PrepareContainerForItem does not run until first expansion. Wire the root Items
        // collection eagerly so cascade / selection / navigation logic can rely on
        // ParentSelector + ParentItem regardless of whether the popup has been realized.
        ((System.Collections.Specialized.INotifyCollectionChanged)Items).CollectionChanged += OnRootItemsChanged;

        AddHandler(KeyDownEvent, new KeyEventHandler(OnTreeKeyDown));
    }

    private void OnRootItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var old in e.OldItems)
            {
                if (old is TreeSelectorItem child)
                {
                    child.ParentSelector = null;
                    child.ParentItem = null;
                }
            }
        }

        if (e.NewItems != null)
        {
            foreach (var added in e.NewItems)
            {
                if (added is TreeSelectorItem child)
                {
                    AttachRootContainer(child);
                }
            }
        }

        // Refresh trigger display whenever the source set of items changes (e.g. binding swap).
        UpdateTriggerDisplay();
    }

    private void AttachRootContainer(TreeSelectorItem container)
    {
        if (container.Style == null)
        {
            _cachedItemStyle ??= container.TryFindResource(typeof(TreeSelectorItem)) as Style;
            if (_cachedItemStyle != null)
            {
                container.Style = _cachedItemStyle;
            }
        }

        container.ParentSelector = this;
        container.ParentItem = null;
        container.Level = 0;
        container.UpdateDescendantLevels();

        var data = ResolveDataItem(container);
        container.IsSelected = _selectedItems.Contains(data);
        if (ShowCheckBoxes)
        {
            container.IsChecked = _checkedItems.Contains(data);
        }
    }

    #endregion

    #region Container Handling

    /// <inheritdoc />
    protected override Panel CreateItemsPanel()
    {
        return new VirtualizingStackPanel { Orientation = Orientation.Vertical };
    }

    /// <inheritdoc />
    protected override FrameworkElement GetContainerForItem(object item)
    {
        return item is TreeSelectorItem existing ? existing : new TreeSelectorItem();
    }

    /// <inheritdoc />
    protected override bool IsItemItsOwnContainerOverride(object item)
    {
        return item is TreeSelectorItem;
    }

    /// <inheritdoc />
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);

        if (element is not TreeSelectorItem container)
        {
            return;
        }

        if (container.Style == null)
        {
            _cachedItemStyle ??= container.TryFindResource(typeof(TreeSelectorItem)) as Style;
            if (_cachedItemStyle != null)
            {
                container.Style = _cachedItemStyle;
            }
        }

        container.ParentSelector = this;
        container.ParentItem = null;
        container.Level = 0;
        container.UpdateDescendantLevels();

        if (item is TreeSelectorItem)
        {
            return;
        }

        container.Header = item;
        container.DataContext = item;

        if (ItemTemplate != null)
        {
            container.HeaderTemplate = ItemTemplate;
        }

        container.IsSelected = _selectedItems.Contains(item);
        if (ShowCheckBoxes)
        {
            container.IsChecked = _checkedItems.Contains(item);
        }
    }

    #endregion

    #region Selection

    internal void HandleItemActivated(TreeSelectorItem item, bool isCtrlPressed, bool isShiftPressed)
    {
        var dataItem = ResolveDataItem(item);

        switch (SelectionMode)
        {
            case SelectionMode.Single:
                ApplySingleSelection(item, dataItem);
                break;

            case SelectionMode.Multiple:
                ApplyMultipleSelection(item, dataItem, toggle: true);
                break;

            case SelectionMode.Extended:
                if (isShiftPressed)
                {
                    ApplyRangeSelection(item, dataItem, additive: isCtrlPressed);
                }
                else if (isCtrlPressed)
                {
                    ApplyMultipleSelection(item, dataItem, toggle: true);
                }
                else
                {
                    ApplySingleSelection(item, dataItem);
                }
                break;
        }

        if (AutoExpandSelected)
        {
            ExpandAncestors(item);
        }
    }

    private void ApplySingleSelection(TreeSelectorItem container, object dataItem)
    {
        var removed = _selectedItems.Cast<object>().ToList();
        BeginSyncSelection();
        try
        {
            UnselectAllContainers(except: container);
            _selectedItems.Clear();
            _selectedItems.Add(dataItem);
            container.IsSelected = true;
        }
        finally
        {
            EndSyncSelection();
        }

        SelectedItem = dataItem;
        var added = new[] { dataItem };
        var trulyRemoved = removed.Where(o => !ReferenceEquals(o, dataItem)).ToArray();
        RaiseSelectionChanged(trulyRemoved, added);
        UpdateTriggerDisplay();

        // Single-select dropdowns close on commit, mirroring ComboBox behaviour.
        if (IsDropDownOpen)
        {
            IsDropDownOpen = false;
        }
    }

    private void ApplyMultipleSelection(TreeSelectorItem container, object dataItem, bool toggle)
    {
        BeginSyncSelection();
        try
        {
            if (toggle && _selectedItems.Contains(dataItem))
            {
                _selectedItems.Remove(dataItem);
                container.IsSelected = false;
                SelectedItem = _selectedItems.Count > 0 ? _selectedItems[^1] : null;
                RaiseSelectionChanged(new[] { dataItem }, Array.Empty<object>());
            }
            else if (!_selectedItems.Contains(dataItem))
            {
                _selectedItems.Add(dataItem);
                container.IsSelected = true;
                SelectedItem = dataItem;
                RaiseSelectionChanged(Array.Empty<object>(), new[] { dataItem });
            }
        }
        finally
        {
            EndSyncSelection();
        }

        UpdateTriggerDisplay();
    }

    private void ApplyRangeSelection(TreeSelectorItem container, object dataItem, bool additive)
    {
        var visible = GetVisibleItems();
        var anchor = SelectedItem != null
            ? visible.FirstOrDefault(i => Equals(ResolveDataItem(i), SelectedItem))
            : null;
        var anchorIndex = anchor != null ? visible.IndexOf(anchor) : -1;
        var targetIndex = visible.IndexOf(container);

        if (anchorIndex < 0 || targetIndex < 0)
        {
            ApplySingleSelection(container, dataItem);
            return;
        }

        var lo = Math.Min(anchorIndex, targetIndex);
        var hi = Math.Max(anchorIndex, targetIndex);

        var removed = _selectedItems.Cast<object>().ToList();
        BeginSyncSelection();
        try
        {
            if (!additive)
            {
                UnselectAllContainers();
                _selectedItems.Clear();
            }

            var added = new List<object>();
            for (int i = lo; i <= hi; i++)
            {
                var item = visible[i];
                var data = ResolveDataItem(item);
                if (!_selectedItems.Contains(data))
                {
                    _selectedItems.Add(data);
                    item.IsSelected = true;
                    added.Add(data);
                }
            }

            SelectedItem = dataItem;
            var actuallyRemoved = removed.Where(o => !_selectedItems.Contains(o)).ToArray();
            RaiseSelectionChanged(actuallyRemoved, added.ToArray());
        }
        finally
        {
            EndSyncSelection();
        }

        UpdateTriggerDisplay();
    }

    private void UnselectAllContainers(TreeSelectorItem? except = null)
    {
        WalkContainers(c =>
        {
            if (!ReferenceEquals(c, except))
            {
                c.IsSelected = false;
            }
        });
    }

    private void ExpandAncestors(TreeSelectorItem item)
    {
        var current = item.ParentItem;
        while (current != null)
        {
            current.IsExpanded = true;
            current = current.ParentItem;
        }
    }

    private static object ResolveDataItem(TreeSelectorItem container)
    {
        if (container.Header != null && !ReferenceEquals(container.Header, container))
        {
            return container.Header;
        }
        return container;
    }

    private void BeginSyncSelection() => _isSyncingSelection = true;
    private void EndSyncSelection() => _isSyncingSelection = false;
    private void BeginSyncCheck() => _isSyncingCheck = true;
    private void EndSyncCheck() => _isSyncingCheck = false;

    /// <summary>Selects all items in the tree (Multiple/Extended modes only).</summary>
    public void SelectAll()
    {
        if (SelectionMode == SelectionMode.Single) return;

        var added = new List<object>();
        BeginSyncSelection();
        try
        {
            // Walk every logical TreeSelectorItem so collapsed branches and
            // popup-not-yet-realized containers all get added to the selection.
            WalkContainers(item =>
            {
                var data = ResolveDataItem(item);
                if (!_selectedItems.Contains(data))
                {
                    _selectedItems.Add(data);
                    item.IsSelected = true;
                    added.Add(data);
                }
            });
        }
        finally
        {
            EndSyncSelection();
        }

        if (added.Count > 0)
        {
            RaiseSelectionChanged(Array.Empty<object>(), added.ToArray());
        }
        UpdateTriggerDisplay();
    }

    /// <summary>Clears the current selection.</summary>
    public void UnselectAll()
    {
        if (_selectedItems.Count == 0) return;

        var removed = _selectedItems.Cast<object>().ToArray();
        BeginSyncSelection();
        try
        {
            UnselectAllContainers();
            _selectedItems.Clear();
        }
        finally
        {
            EndSyncSelection();
        }

        SelectedItem = null;
        RaiseSelectionChanged(removed, Array.Empty<object>());
        UpdateTriggerDisplay();
    }

    /// <summary>
    /// Removes a single item from the current selection — used by the chip × button.
    /// </summary>
    public void RemoveFromSelection(object item)
    {
        if (item == null || !_selectedItems.Contains(item))
        {
            return;
        }

        BeginSyncSelection();
        try
        {
            _selectedItems.Remove(item);
            WalkContainers(c =>
            {
                if (Equals(ResolveDataItem(c), item))
                {
                    c.IsSelected = false;
                }
            });
        }
        finally
        {
            EndSyncSelection();
        }

        SelectedItem = _selectedItems.Count > 0 ? _selectedItems[^1] : null;
        RaiseSelectionChanged(new[] { item }, Array.Empty<object>());
        UpdateTriggerDisplay();
    }

    private void RaiseSelectionChanged(object[] removed, object[] added)
    {
        var args = new SelectionChangedEventArgs(SelectionChangedEvent, removed, added);
        RaiseEvent(args);
    }

    private void OnSelectedItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isSyncingSelection) return;

        WalkContainers(c =>
        {
            var data = ResolveDataItem(c);
            c.IsSelected = _selectedItems.Contains(data);
        });

        if (_selectedItems.Count > 0)
        {
            SelectedItem = _selectedItems[^1];
        }
        else
        {
            SelectedItem = null;
        }

        UpdateTriggerDisplay();
    }

    private void OnCheckedItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isSyncingCheck) return;

        WalkContainers(c =>
        {
            var data = ResolveDataItem(c);
            c.IsChecked = _checkedItems.Contains(data);
        });
    }

    #endregion

    #region Check-state cascade

    internal void HandleItemCheckStateChanged(TreeSelectorItem item, bool? oldState, bool? newState)
    {
        var dataItem = ResolveDataItem(item);

        BeginSyncCheck();
        try
        {
            if (newState == true)
            {
                if (!_checkedItems.Contains(dataItem)) _checkedItems.Add(dataItem);
            }
            else
            {
                _checkedItems.Remove(dataItem);
            }
        }
        finally
        {
            EndSyncCheck();
        }

        if (CheckCascadeMode == TreeSelectorCheckCascadeMode.Cascade && newState.HasValue && !_isSyncingCheck)
        {
            ApplyCascadeDown(item, newState.Value);

            var parent = item.ParentItem;
            while (parent != null)
            {
                RecomputeParentCheckState(parent);
                parent = parent.ParentItem;
            }
        }

        RaiseEvent(new RoutedEventArgs(ItemCheckStateChangedEvent, item));
    }

    private void ApplyCascadeDown(TreeSelectorItem parent, bool checkedValue)
    {
        foreach (var child in parent.Items)
        {
            if (child is TreeSelectorItem tsi)
            {
                tsi.SetIsCheckedSilently(checkedValue);
                ApplyCascadeDown(tsi, checkedValue);

                BeginSyncCheck();
                try
                {
                    var data = ResolveDataItem(tsi);
                    if (checkedValue)
                    {
                        if (!_checkedItems.Contains(data)) _checkedItems.Add(data);
                    }
                    else
                    {
                        _checkedItems.Remove(data);
                    }
                }
                finally
                {
                    EndSyncCheck();
                }
            }
        }
    }

    private void RecomputeParentCheckState(TreeSelectorItem parent)
    {
        bool anyChecked = false;
        bool anyUnchecked = false;
        bool anyMixed = false;

        foreach (var child in parent.Items)
        {
            if (child is TreeSelectorItem tsi)
            {
                if (tsi.IsChecked == null)
                {
                    anyMixed = true;
                }
                else if (tsi.IsChecked == true)
                {
                    anyChecked = true;
                }
                else
                {
                    anyUnchecked = true;
                }
            }
        }

        bool? newState;
        if (anyMixed || (anyChecked && anyUnchecked))
        {
            newState = null;
        }
        else if (anyChecked)
        {
            newState = true;
        }
        else
        {
            newState = false;
        }

        parent.SetIsCheckedSilently(newState);

        BeginSyncCheck();
        try
        {
            var data = ResolveDataItem(parent);
            if (newState == true)
            {
                if (!_checkedItems.Contains(data)) _checkedItems.Add(data);
            }
            else
            {
                _checkedItems.Remove(data);
            }
        }
        finally
        {
            EndSyncCheck();
        }
    }

    #endregion

    #region Keyboard Navigation

    private void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || Keyboard.FocusedElement is TreeSelectorItem)
        {
            return;
        }

        // Esc closes the popup.
        if (e.Key == Key.Escape && IsDropDownOpen)
        {
            IsDropDownOpen = false;
            e.Handled = true;
            return;
        }

        var visible = GetVisibleItems();
        if (visible.Count == 0) return;

        var handled = e.Key switch
        {
            Key.Down or Key.Right or Key.Home => visible[0].Focus(),
            Key.Up or Key.Left or Key.End => visible[^1].Focus(),
            _ => false
        };

        if (handled)
        {
            e.Handled = true;
        }
    }

    internal bool FocusAdjacent(TreeSelectorItem current, int direction)
    {
        var visible = GetVisibleItems();
        var index = visible.IndexOf(current);
        if (index < 0) return false;
        var next = index + direction;
        if (next < 0 || next >= visible.Count) return false;
        return visible[next].Focus();
    }

    internal bool FocusBoundary(bool last)
    {
        var visible = GetVisibleItems();
        if (visible.Count == 0) return false;
        return (last ? visible[^1] : visible[0]).Focus();
    }

    private List<TreeSelectorItem> GetVisibleItems()
    {
        var result = new List<TreeSelectorItem>();
        if (ItemsHost != null)
        {
            CollectVisible(ItemsHost, result);
        }
        return result;
    }

    private static void CollectVisible(Panel panel, List<TreeSelectorItem> result)
    {
        foreach (UIElement child in panel.Children)
        {
            if (child is not TreeSelectorItem item || item.Visibility != Visibility.Visible)
            {
                continue;
            }

            result.Add(item);

            if (!item.IsExpanded) continue;

            var subPanel = item.GetItemsHostPanel();
            if (subPanel != null)
            {
                CollectVisible(subPanel, result);
            }
        }
    }

    private void WalkContainers(Action<TreeSelectorItem> action)
    {
        // Walk the logical Items collection in addition to the realized panel — child containers
        // for collapsed nodes do not appear in ItemsHost yet, but our cascade / selection sync
        // logic still needs to reach them.
        WalkLogicalItems(Items, action);
    }

    private static void WalkLogicalItems(IEnumerable items, Action<TreeSelectorItem> action)
    {
        foreach (var item in items)
        {
            if (item is TreeSelectorItem container)
            {
                action(container);
                WalkLogicalItems(container.Items, action);
            }
        }
    }

    #endregion

    #region Template / Trigger / Dropdown

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        DetachTriggerHandlers();

        _triggerBorder = GetTemplateChild("PART_TriggerBorder") as Border;
        _displayText = GetTemplateChild("PART_DisplayText") as TextBlock;
        _tagsPanel = GetTemplateChild("PART_TagsPanel") as Panel;
        _searchTextBox = GetTemplateChild("PART_SearchTextBox") as TextBox;
        _placeholderText = GetTemplateChild("PART_PlaceholderText") as TextBlock;
        _clearButton = GetTemplateChild("PART_ClearButton") as Button;
        _dropDownArrow = GetTemplateChild("PART_DropDownArrow") as Shapes.Path;
        _popup = GetTemplateChild("PART_Popup") as Popup;

        AttachTriggerHandlers();
        UpdateTriggerDisplay();
        SetDropDownArrowAngle(IsDropDownOpen ? 180 : 0);
    }

    /// <inheritdoc />
    internal override void OnTemplateContentClearing()
    {
        // base clears ItemsControl's ItemsPresenter; then release our own parts. UpdateDisplay
        // otherwise keeps realizing tag chips into the detached _tagsPanel and writing display /
        // placeholder text and the dropdown open state into the discarded tree.
        base.OnTemplateContentClearing();

        DetachTriggerHandlers();

        _triggerBorder = null;
        _displayText = null;
        _tagsPanel = null;
        _searchTextBox = null;
        _placeholderText = null;
        _clearButton = null;
        _dropDownArrow = null;
        _popup = null;
    }

    private void AttachTriggerHandlers()
    {
        if (_triggerBorder != null)
        {
            _triggerBorder.AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnTriggerMouseDown), true);
        }
        if (_clearButton != null)
        {
            _clearButton.Click += OnClearButtonClick;
        }
        if (_searchTextBox != null && !_searchHandlersAttached)
        {
            _searchTextBox.TextChanged += OnSearchTextBoxTextChanged;
            _searchHandlersAttached = true;
        }
    }

    private void DetachTriggerHandlers()
    {
        if (_triggerBorder != null)
        {
            _triggerBorder.RemoveHandler(MouseDownEvent, new MouseButtonEventHandler(OnTriggerMouseDown));
        }
        if (_clearButton != null)
        {
            _clearButton.Click -= OnClearButtonClick;
        }
        if (_searchTextBox != null && _searchHandlersAttached)
        {
            _searchTextBox.TextChanged -= OnSearchTextBoxTextChanged;
            _searchHandlersAttached = false;
        }
    }

    private void OnTriggerMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        // Clicks on the clear button (or anything inside it) should fire the button instead.
        if (e.OriginalSource is DependencyObject src && IsAncestorOrSelf(src, _clearButton))
        {
            return;
        }

        // Clicks inside an existing chip target the chip's own × button — do not toggle the popup.
        if (_tagsPanel != null && e.OriginalSource is DependencyObject src2 && IsAncestorOrSelf(src2, _tagsPanel))
        {
            // If the click landed on the chip body but not the close button, still allow popup toggle.
            // Chip close buttons swallow the click via e.Handled = true in their own handler, so anything
            // reaching here is the chip body — fall through and toggle the popup.
        }

        // Clicks inside the search text box should not steal focus / toggle.
        if (_searchTextBox != null && e.OriginalSource is DependencyObject src3 && IsAncestorOrSelf(src3, _searchTextBox))
        {
            return;
        }

        Focus();
        IsDropDownOpen = !IsDropDownOpen;
        if (IsDropDownOpen && IsSearchEnabled && _searchTextBox != null)
        {
            _searchTextBox.Focus();
        }
        e.Handled = true;
    }

    private void OnClearButtonClick(object sender, RoutedEventArgs e)
    {
        UnselectAll();
        if (IsSearchEnabled)
        {
            ClearSearchTextSilently();
        }
        e.Handled = true;
    }

    private void OnSearchTextBoxTextChanged(object sender, RoutedEventArgs e)
    {
        if (_searchTextBox == null || _isUpdatingSearchTextFromCode) return;
        SearchText = _searchTextBox.Text ?? string.Empty;
        if (!string.IsNullOrEmpty(SearchText) && !IsDropDownOpen)
        {
            IsDropDownOpen = true;
        }
    }

    private void ClearSearchTextSilently()
    {
        _isUpdatingSearchTextFromCode = true;
        try
        {
            SearchText = string.Empty;
            if (_searchTextBox != null) _searchTextBox.Text = string.Empty;
        }
        finally
        {
            _isUpdatingSearchTextFromCode = false;
        }
        ApplySearchFilter();
    }

    private void UpdateTriggerDisplay()
    {
        bool hasSelection = _selectedItems.Count > 0;
        bool isMulti = SelectionMode != SelectionMode.Single;
        bool hasSearchText = !string.IsNullOrEmpty(SearchText);

        if (_displayText != null)
        {
            // Single-select shows the path string; hide while the user is typing in the search box.
            bool show = !isMulti && hasSelection && !IsSearchEnabled;
            _displayText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show)
            {
                _displayText.Text = BuildPathString(SelectedItem);
            }
        }

        if (_tagsPanel != null)
        {
            bool show = isMulti && hasSelection;
            _tagsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show)
            {
                RebuildTagsPanel();
            }
            else
            {
                _tagsPanel.Children.Clear();
            }
        }

        if (_searchTextBox != null)
        {
            _searchTextBox.Visibility = IsSearchEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_placeholderText != null)
        {
            // Placeholder shows when nothing is selected and the search box (if any) is empty.
            bool textBoxEmpty = _searchTextBox == null || string.IsNullOrEmpty(_searchTextBox.Text);
            bool show = !hasSelection && !hasSearchText && textBoxEmpty;
            _placeholderText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_clearButton != null)
        {
            _clearButton.Visibility = (hasSelection || hasSearchText) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void RebuildTagsPanel()
    {
        if (_tagsPanel == null) return;
        _tagsPanel.Children.Clear();
        foreach (var data in _selectedItems)
        {
            _tagsPanel.Children.Add(CreateTagChip(data));
        }
    }

    private FrameworkElement CreateTagChip(object data)
    {
        var label = ResolveDisplayLabel(data);
        var chipBorder = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 2, 1),
            Margin = new Thickness(0, 2, 4, 2),
            Background = TryFindResource("ControlBackgroundHover") as Brush
                       ?? new SolidColorBrush(Color.FromRgb(60, 60, 65)),
            BorderBrush = TryFindResource("ControlBorder") as Brush
                       ?? new SolidColorBrush(Color.FromRgb(80, 80, 80)),
            BorderThickness = new Thickness(1)
        };

        var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        });

        var closeButton = new Button
        {
            Content = "×",
            Width = 16,
            Height = 16,
            Padding = new Thickness(0),
            FontSize = 12,
            Background = null,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = TryFindResource("TextSecondary") as Brush
                       ?? new SolidColorBrush(Color.FromRgb(180, 180, 180))
        };
        closeButton.Click += (_, ev) =>
        {
            RemoveFromSelection(data);
            ev.Handled = true;
        };
        stack.Children.Add(closeButton);

        chipBorder.Child = stack;
        return chipBorder;
    }

    private string BuildPathString(object? selectedData)
    {
        if (selectedData == null) return string.Empty;
        var container = FindContainerForData(selectedData);
        if (container == null) return ResolveDisplayLabel(selectedData);

        var parts = new List<string>();
        var cur = container;
        while (cur != null)
        {
            parts.Insert(0, ResolveDisplayLabel(cur.Header ?? string.Empty));
            cur = cur.ParentItem;
        }
        return string.Join(PathSeparator, parts);
    }

    private TreeSelectorItem? FindContainerForData(object data)
    {
        TreeSelectorItem? result = null;
        WalkContainers(c =>
        {
            if (result == null && Equals(ResolveDataItem(c), data))
            {
                result = c;
            }
        });
        return result;
    }

    private static string ResolveDisplayLabel(object? data) => data?.ToString() ?? string.Empty;

    private static bool IsAncestorOrSelf(DependencyObject? candidate, FrameworkElement? ancestor)
    {
        if (ancestor == null || candidate == null) return false;
        for (var current = candidate; current != null; current = (current as UIElement)?.VisualParent as DependencyObject)
        {
            if (ReferenceEquals(current, ancestor)) return true;
        }
        return false;
    }

    private void ApplySearchFilter()
    {
        var query = SearchText?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(query))
        {
            // Restore visibility for everything.
            WalkContainers(c => c.Visibility = Visibility.Visible);
            return;
        }

        foreach (var item in Items)
        {
            if (item is TreeSelectorItem tsi)
            {
                FilterRecurse(tsi, query);
            }
        }
    }

    private static bool FilterRecurse(TreeSelectorItem container, string query)
    {
        bool selfMatch = ResolveDisplayLabel(container.Header).Contains(query, StringComparison.OrdinalIgnoreCase);

        bool childMatch = false;
        foreach (var child in container.Items)
        {
            if (child is TreeSelectorItem ts)
            {
                childMatch |= FilterRecurse(ts, query);
            }
        }

        bool anyMatch = selfMatch || childMatch;
        container.Visibility = anyMatch ? Visibility.Visible : Visibility.Collapsed;
        if (anyMatch && childMatch)
        {
            container.IsExpanded = true;
        }
        return anyMatch;
    }

    private void SetDropDownArrowAngle(double angle)
    {
        if (_dropDownArrow == null) return;
        var rotate = _dropDownArrow.RenderTransform as RotateTransform ?? new RotateTransform();
        rotate.Angle = angle;
        _dropDownArrow.RenderTransformOrigin = new Point(0.5, 0.5);
        _dropDownArrow.RenderTransform = rotate;
        _dropDownArrow.InvalidateVisual();
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnSelectionModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeSelector selector)
        {
            if ((SelectionMode)(e.NewValue ?? SelectionMode.Single) == SelectionMode.Single
                && selector._selectedItems.Count > 1)
            {
                var keep = selector._selectedItems[^1];
                selector.UnselectAll();
                selector._selectedItems.Add(keep);
                selector.SelectedItem = keep;
                selector.WalkContainers(c => c.IsSelected = ReferenceEquals(selector.ResolveDataItemForContainer(c), keep));
            }
            selector.UpdateTriggerDisplay();
        }
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeSelector selector && !selector._isSyncingSelection)
        {
            var newItem = e.NewValue;
            if (selector.SelectionMode == SelectionMode.Single)
            {
                selector.BeginSyncSelection();
                try
                {
                    selector._selectedItems.Clear();
                    if (newItem != null) selector._selectedItems.Add(newItem);
                    selector.WalkContainers(c =>
                        c.IsSelected = newItem != null && Equals(ResolveDataItem(c), newItem));
                }
                finally
                {
                    selector.EndSyncSelection();
                }
            }
            selector.UpdateTriggerDisplay();
        }
    }

    private static void OnShowCheckBoxesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeSelector selector)
        {
            selector.WalkContainers(c => c.UpdateCheckBoxVisibility());
        }
    }

    private static void OnIsDropDownOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeSelector selector)
        {
            bool open = (bool)(e.NewValue ?? false);
            selector.SetDropDownArrowAngle(open ? 180 : 0);
            if (open)
            {
                selector.RaiseEvent(new RoutedEventArgs(DropDownOpenedEvent, selector));
            }
            else
            {
                selector.RaiseEvent(new RoutedEventArgs(DropDownClosedEvent, selector));
            }
        }
    }

    private static void OnIsSearchEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeSelector selector)
        {
            selector.UpdateTriggerDisplay();
            if (!(bool)(e.NewValue ?? false))
            {
                // Search disabled — drop any active filter.
                selector.ClearSearchTextSilently();
            }
        }
    }

    private static void OnSearchTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TreeSelector selector) return;

        // Mirror externally-set SearchText into the search box (if present).
        if (selector._searchTextBox != null && !selector._isUpdatingSearchTextFromCode)
        {
            var newText = (string)(e.NewValue ?? string.Empty);
            if (selector._searchTextBox.Text != newText)
            {
                selector._isUpdatingSearchTextFromCode = true;
                try
                {
                    selector._searchTextBox.Text = newText;
                }
                finally
                {
                    selector._isUpdatingSearchTextFromCode = false;
                }
            }
        }

        selector.ApplySearchFilter();
        selector.UpdateTriggerDisplay();
    }

    private object ResolveDataItemForContainer(TreeSelectorItem container) => ResolveDataItem(container);

    #endregion
}

/// <summary>
/// Represents an item in a <see cref="TreeSelector"/> control.
/// </summary>
public class TreeSelectorItem : HeaderedItemsControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.TreeSelectorItemAutomationPeer(this);
    }

    private const double IndentSize = 16;


    private TreeSelector? _parentSelector;
    /// <summary>
    /// The owning TreeSelector. Setting this also walks the descendant tree so newly-attached
    /// subtrees inherit the same parent reference even before a layout pass realises them.
    /// </summary>
    internal TreeSelector? ParentSelector
    {
        get => _parentSelector;
        set
        {
            if (ReferenceEquals(_parentSelector, value)) return;
            _parentSelector = value;
            foreach (var child in Items)
            {
                if (child is TreeSelectorItem c)
                {
                    c.ParentSelector = value;
                }
            }
        }
    }
    internal TreeSelectorItem? ParentItem { get; set; }

    private int _level;
    private bool _suppressCheckCallback;

    #region Template Parts

    private Border? _headerBorder;
    private FrameworkElement? _indentSpacer;
    private FrameworkElement? _expanderBorder;
    private Shapes.Path? _expanderArrow;
    private CheckBox? _checkBox;
    private FrameworkElement? _itemsHost;

    #endregion

    #region Dependency Properties

    /// <summary>Identifies the IsExpanded dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(TreeSelectorItem),
            new PropertyMetadata(false, OnIsExpandedChanged));

    /// <summary>Identifies the IsSelected dependency property.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(TreeSelectorItem),
            new PropertyMetadata(false));

    /// <summary>Identifies the IsChecked dependency property (supports tri-state).</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(nameof(IsChecked), typeof(bool?), typeof(TreeSelectorItem),
            new PropertyMetadata(false, OnIsCheckedChanged));

    #endregion

    #region CLR Properties

    /// <summary>Gets or sets whether this item is expanded.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty)!;
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>Gets or sets whether this item is selected.</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty)!;
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>Gets or sets the check-state of this item (true / false / null for indeterminate).</summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool? IsChecked
    {
        get => (bool?)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>Gets the depth of this item in the tree (0 for root-level items).</summary>
    public int Level
    {
        get => _level;
        internal set
        {
            if (_level == value) return;
            _level = value;
            UpdateIndent();
            UpdateDescendantLevels();
        }
    }

    /// <summary>Gets whether the item has any child items.</summary>
    public new bool HasItems => base.HasItems;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="TreeSelectorItem"/> class.
    /// </summary>
    public TreeSelectorItem()
    {
        Focusable = true;
        SetCurrentValue(UIElement.TransitionPropertyProperty, "None");

        ((System.Collections.Specialized.INotifyCollectionChanged)Items).CollectionChanged += OnChildItemsChanged;
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
    }

    #endregion

    #region Container Plumbing

    /// <inheritdoc />
    protected override FrameworkElement GetContainerForItem(object item)
    {
        return item is TreeSelectorItem ? (FrameworkElement)item : new TreeSelectorItem();
    }

    /// <inheritdoc />
    protected override bool IsItemItsOwnContainerOverride(object item)
    {
        return item is TreeSelectorItem;
    }

    /// <inheritdoc />
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);

        if (element is not TreeSelectorItem child)
        {
            return;
        }

        child.ParentSelector = ParentSelector;
        child.ParentItem = this;
        child.Level = Level + 1;

        if (child.Style == null && ParentSelector?._cachedItemStyle != null)
        {
            child.Style = ParentSelector._cachedItemStyle;
        }

        if (item is TreeSelectorItem)
        {
            return;
        }

        child.Header = item;
        child.DataContext = item;

        var template = ParentSelector?.ItemTemplate ?? ItemTemplate;
        if (template != null)
        {
            child.HeaderTemplate = template;
        }
    }

    private void OnChildItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var old in e.OldItems)
            {
                if (old is TreeSelectorItem child)
                {
                    child.ParentSelector = null;
                    child.ParentItem = null;
                }
            }
        }

        if (e.NewItems != null)
        {
            foreach (var added in e.NewItems)
            {
                if (added is TreeSelectorItem child)
                {
                    child.ParentSelector = ParentSelector;
                    child.ParentItem = this;
                    child.Level = Level + 1;
                }
            }
        }

        UpdateExpanderVisibility();
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            UpdateDescendantLevels();
        }
    }

    /// <summary>
    /// Walks the descendant tree assigning ParentSelector/ParentItem/Level to every TreeSelectorItem.
    /// Must run after the root container's ParentSelector is set (otherwise the ancestor chain is null).
    /// </summary>
    internal void UpdateDescendantLevels()
    {
        foreach (var item in Items)
        {
            if (item is TreeSelectorItem child)
            {
                child.ParentSelector = ParentSelector;
                child.ParentItem = this;
                child.Level = _level + 1;
            }
        }
    }

    #endregion

    #region Template

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        if (_headerBorder != null)
        {
            _headerBorder.RemoveHandler(MouseDownEvent, new MouseButtonEventHandler(OnHeaderMouseDown));
        }
        if (_checkBox != null)
        {
            _checkBox.Click -= OnCheckBoxClick;
        }

        _headerBorder = GetTemplateChild("PART_HeaderBorder") as Border;
        _indentSpacer = GetTemplateChild("PART_IndentSpacer") as FrameworkElement;
        _expanderBorder = GetTemplateChild("PART_ExpanderBorder") as FrameworkElement;
        _expanderArrow = GetTemplateChild("PART_ExpanderArrow") as Shapes.Path;
        _checkBox = GetTemplateChild("PART_CheckBox") as CheckBox;
        _itemsHost = GetTemplateChild("PART_ItemsHost") as FrameworkElement;

        if (_headerBorder != null)
        {
            _headerBorder.AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnHeaderMouseDown), true);
        }
        if (_checkBox != null)
        {
            _checkBox.Click += OnCheckBoxClick;
        }

        UpdateIndent();
        UpdateExpanderVisibility();
        UpdateExpandedVisualState();
        UpdateCheckBoxVisibility();
        SyncCheckBoxFromState();
    }

    /// <inheritdoc />
    internal override void OnTemplateContentClearing()
    {
        // base clears ItemsControl's ItemsPresenter registration; then release our own parts so
        // the header/expander state no longer resolves into the discarded template tree.
        base.OnTemplateContentClearing();

        if (_headerBorder != null)
        {
            _headerBorder.RemoveHandler(MouseDownEvent, new MouseButtonEventHandler(OnHeaderMouseDown));
        }
        if (_checkBox != null)
        {
            _checkBox.Click -= OnCheckBoxClick;
        }

        _headerBorder = null;
        _indentSpacer = null;
        _expanderBorder = null;
        _expanderArrow = null;
        _checkBox = null;
        _itemsHost = null;
    }

    internal Panel? GetItemsHostPanel()
    {
        if (ItemsHost == null)
        {
            ApplyTemplate();
        }
        return ItemsHost;
    }

    #endregion

    #region Visual Updates

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
        SetExpanderAngle(IsExpanded ? 0 : -90);
    }

    private void UpdateExpandedVisualState()
    {
        if (_itemsHost != null)
        {
            _itemsHost.Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        }
        SetExpanderAngle(IsExpanded ? 0 : -90);
    }

    private void SetExpanderAngle(double angle)
    {
        if (_expanderArrow == null) return;
        var rotate = _expanderArrow.RenderTransform as RotateTransform ?? new RotateTransform();
        rotate.Angle = angle;
        _expanderArrow.RenderTransformOrigin = new Point(0.5, 0.5);
        _expanderArrow.RenderTransform = rotate;
        _expanderArrow.InvalidateVisual();
    }

    internal void UpdateCheckBoxVisibility()
    {
        if (_checkBox == null) return;
        _checkBox.Visibility = (ParentSelector?.ShowCheckBoxes ?? false)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SyncCheckBoxFromState()
    {
        if (_checkBox == null) return;
        _suppressCheckCallback = true;
        try
        {
            _checkBox.IsChecked = IsChecked;
        }
        finally
        {
            _suppressCheckCallback = false;
        }
    }

    /// <summary>Sets the IsChecked DP without raising the cascade callback (used by the parent selector).</summary>
    internal void SetIsCheckedSilently(bool? value)
    {
        _suppressCheckCallback = true;
        try
        {
            IsChecked = value;
        }
        finally
        {
            _suppressCheckCallback = false;
        }
    }

    #endregion

    #region Input Handling

    private void OnHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (HasItems && _expanderBorder is { Visibility: Visibility.Visible } expander)
        {
            var pos = e.GetPosition(expander);
            if (pos.X >= 0 && pos.X <= expander.ActualWidth &&
                pos.Y >= 0 && pos.Y <= expander.ActualHeight)
            {
                IsExpanded = !IsExpanded;
                e.Handled = true;
                return;
            }
        }

        if (e.OriginalSource is DependencyObject src && IsInsideInteractiveHeaderElement(src))
        {
            return;
        }

        Focus();

        var modifiers = Keyboard.Modifiers;
        bool isCtrl = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        bool isShift = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        ParentSelector?.HandleItemActivated(this, isCtrl, isShift);
        e.Handled = true;
    }

    private void OnCheckBoxClick(object sender, RoutedEventArgs e)
    {
        if (_checkBox == null || _suppressCheckCallback) return;
        IsChecked = _checkBox.IsChecked;
    }

    private bool IsInsideInteractiveHeaderElement(DependencyObject element)
    {
        for (var current = element; current != null; current = (current as UIElement)?.VisualParent as DependencyObject)
        {
            if (ReferenceEquals(current, this)) break;
            if (current is UIElement ui && ui.Focusable
                && current is not TextBlock && current is not Label
                && !ReferenceEquals(current, _checkBox))
            {
                return true;
            }
        }
        return false;
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;

        var handled = e.Key switch
        {
            Key.Up => ParentSelector?.FocusAdjacent(this, -1) == true,
            Key.Down => ParentSelector?.FocusAdjacent(this, 1) == true,
            Key.Home => ParentSelector?.FocusBoundary(last: false) == true,
            Key.End => ParentSelector?.FocusBoundary(last: true) == true,
            Key.Right => HandleRightArrow(),
            Key.Left => HandleLeftArrow(),
            Key.Enter or Key.Space => HandleSelectionKey(),
            _ => false
        };

        if (handled)
        {
            e.Handled = true;
        }
    }

    private bool HandleRightArrow()
    {
        if (HasItems && !IsExpanded)
        {
            IsExpanded = true;
            return true;
        }
        if (!IsExpanded) return false;

        foreach (var item in Items)
        {
            if (item is TreeSelectorItem child &&
                child.Visibility == Visibility.Visible &&
                child.Focus())
            {
                return true;
            }
        }
        return false;
    }

    private bool HandleLeftArrow()
    {
        if (HasItems && IsExpanded)
        {
            IsExpanded = false;
            return true;
        }
        return ParentItem != null && ParentItem.Focus();
    }

    private bool HandleSelectionKey()
    {
        if (ParentSelector == null) return false;
        var modifiers = Keyboard.Modifiers;
        bool isCtrl = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        bool isShift = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        ParentSelector.HandleItemActivated(this, isCtrl, isShift);
        return true;
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeSelectorItem item)
        {
            item.UpdateExpandedVisualState();
            item.InvalidateMeasure();
            item.ParentSelector?.InvalidateMeasure();
        }
    }

    private static void OnIsCheckedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TreeSelectorItem item)
        {
            item.SyncCheckBoxFromState();
            if (item._suppressCheckCallback) return;

            var oldVal = e.OldValue as bool?;
            var newVal = e.NewValue as bool?;
            item.ParentSelector?.HandleItemCheckStateChanged(item, oldVal, newVal);
        }
    }

    /// <inheritdoc />
    #endregion
}
