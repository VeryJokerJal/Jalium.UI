using System.Collections;
using System.Collections.Specialized;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that displays a list of items and allows the user to select one or more.
/// </summary>
public partial class ListBox : Selector
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.ListBoxAutomationPeer(this);
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the SelectionMode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty SelectionModeProperty =
        DependencyProperty.Register(nameof(SelectionMode), typeof(SelectionMode), typeof(ListBox),
            new PropertyMetadata(SelectionMode.Single, OnSelectionModeChanged));

    /// <summary>
    /// Identifies the read-only <see cref="SelectedItems"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectedItemsProperty =
        Selector.SelectedItemsImplProperty;

    #endregion

    #region Fields

    private readonly List<object> _selectedItems;
    private readonly SelectedItemsCollection _selectedItemsView;
    private readonly Dictionary<ListBoxItem, int> _explicitItemMemberships = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The anchor index for Extended selection range operations.
    /// Set on regular click and Ctrl+Click; not changed on Shift+Click.
    /// </summary>
    private int _anchorIndex = -1;
    private object? _anchorItem;
    private bool _isApplyingSelectedItems;
    private IInputElement? _pendingFocusOrigin;
    private bool _focusRequestQueued;

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the selection mode.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public SelectionMode SelectionMode
    {
        get => (SelectionMode)GetValue(SelectionModeProperty)!;
        set => SetValue(SelectionModeProperty, value);
    }

    /// <summary>
    /// Gets the currently selected items.
    /// In Single mode, this contains at most one item.
    /// In Multiple and Extended modes, this contains all selected items.
    /// </summary>
    public IList SelectedItems => _selectedItemsView;

    /// <summary>
    /// Gets or sets the anchor item used for extended range selection.
    /// </summary>
    protected object? AnchorItem
    {
        get => ResolveAnchorIndex() >= 0 ? _anchorItem : null;
        set
        {
            if (value is null || ReferenceEquals(value, DependencyProperty.UnsetValue))
            {
                ClearAnchor();
                return;
            }

            int index = ResolveSelectionOccurrence(value, preferred: _primaryOccurrence)?.Index ?? -1;
            if (index < 0)
            {
                throw new InvalidOperationException("The anchor item must belong to this ListBox.");
            }

            SetAnchorIndex(index);
        }
    }

    /// <inheritdoc />
    protected internal override bool HandlesScrolling => true;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ListBox"/> class.
    /// </summary>
    public ListBox()
    {
        _selectedItems = (List<object>)SelectedItemsImpl;
        _selectedItemsView = new SelectedItemsCollection(this, _selectedItems);
        SetSelectedItemsCollectionView(_selectedItemsView);
        SetCurrentValue(UIElement.TransitionPropertyProperty, TransitionPropertyCollection.None());

        if (ItemsPanel == null)
        {
            ItemsPanel = CreateItemsPanelTemplate(typeof(VirtualizingStackPanel));
        }

        // Register input event handlers
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
    }

    /// <summary>
    /// Brings the specified item into the visible viewport.
    /// </summary>
    public void ScrollIntoView(object item)
    {
        if (item is null)
        {
            return;
        }

        int index = Items.FindOccurrence(item)?.Index ?? -1;
        if (index < 0)
        {
            return;
        }

        if (ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement realizedContainer)
        {
            realizedContainer.BringIntoView();
            return;
        }

        if (ItemsHost is VirtualizingPanel virtualizingPanel)
        {
            virtualizingPanel.BringIndexIntoView(index);
            return;
        }

        if (ItemsHost is { } itemsHost && index < itemsHost.Children.Count &&
            itemsHost.Children[index] is FrameworkElement container)
        {
            container.BringIntoView();
        }
    }

    #endregion

    #region Item Container

    /// <inheritdoc />
    protected override Panel CreateItemsPanel()
    {
        return new VirtualizingStackPanel { Orientation = Orientation.Vertical };
    }

    /// <inheritdoc />
    protected override FrameworkElement GetContainerForItem(object item)
    {
        return new ListBoxItem();
    }

    /// <inheritdoc />
    protected override bool IsItemItsOwnContainerOverride(object item)
    {
        return item is ListBoxItem;
    }

    /// <inheritdoc />
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);

        if (element is ListBoxItem listBoxItem)
        {
            // When the item IS its own container, do not assign it as its own
            // Content — the template's ContentPresenter would try to parent the
            // element that is already in the items panel, causing a
            // "Visual already has a parent" exception.
            if (!ReferenceEquals(element, item))
            {
                listBoxItem.Content = item;
            }

            listBoxItem.ParentListBox = this;
            int index = ItemContainerGenerator.IndexFromContainer(element);
            if (index < 0) index = Items.FindOccurrence(item)?.Index ?? -1;
            listBoxItem.IsSelected = IsOccurrenceSelected(index);
        }
    }

    /// <inheritdoc />
    protected override void ClearContainerForItem(FrameworkElement element, object item)
    {
        base.ClearContainerForItem(element, item);

        // Reset per-container selection/owner state so a recycled-then-orphaned container does not
        // alias the previous item's selection. The steady-state re-pop path re-derives these in
        // PrepareContainerForItem, so this only changes observable behavior for orphaned containers.
        if (!ReferenceEquals(element, item) && element is ListBoxItem listBoxItem)
        {
            listBoxItem.ClearValue(ListBoxItem.IsSelectedProperty);
            listBoxItem.ParentListBox = null;
        }
    }

    /// <inheritdoc />
    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        // Membership and occurrence selection were reconciled at the collection
        // boundary before any subscribers could query, reenter or throw.
        base.OnItemsChanged(e);
    }

    private void SynchronizeExplicitItemOwnership(NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Move) return;

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            var current = new Dictionary<ListBoxItem, int>(ReferenceEqualityComparer.Instance);
            foreach (var item in Items)
            {
                if (item is ListBoxItem row)
                {
                    current.TryGetValue(row, out int count);
                    current[row] = count + 1;
                }
            }
            foreach (var row in _explicitItemMemberships.Keys)
            {
                if (!current.ContainsKey(row) && ReferenceEquals(row.ParentListBox, this))
                    row.ParentListBox = null;
            }
            _explicitItemMemberships.Clear();
            foreach (var entry in current)
            {
                _explicitItemMemberships.Add(entry.Key, entry.Value);
                entry.Key.ParentListBox = this;
            }
            return;
        }

        if (e.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace)
        {
            if (e.OldItems != null)
            {
                foreach (var oldItem in e.OldItems)
                {
                    if (oldItem is ListBoxItem row)
                    {
                        _explicitItemMemberships.TryGetValue(row, out int count);
                        if (count > 1) _explicitItemMemberships[row] = count - 1;
                        else
                        {
                            _explicitItemMemberships.Remove(row);
                            if (ReferenceEquals(row.ParentListBox, this)) row.ParentListBox = null;
                        }
                    }
                }
            }
        }
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace)
        {
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is ListBoxItem row)
                    {
                        _explicitItemMemberships.TryGetValue(row, out int count);
                        _explicitItemMemberships[row] = count + 1;
                        row.ParentListBox = this;
                    }
                }
            }
        }
    }

    protected override void SynchronizeSelectionWithItemsChange(NotifyCollectionChangedEventArgs e)
    {
        // ItemCollection has already reconciled occurrences before publishing
        // either direct-item or source/view notifications.
    }

    #endregion

    #region Selection

    /// <summary>
    /// Replaces the current selection with the supplied items as one atomic operation.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when every requested item belongs to this ListBox and the
    /// selection was applied; otherwise <see langword="false"/> and the old selection is retained.
    /// </returns>
    protected bool SetSelectedItems(IEnumerable selectedItems) =>
        ApplySelectedItems(selectedItems, preferredIndex: null, publishToSource: false);

    private bool ApplySelectedItems(IEnumerable selectedItems, int? preferredIndex, bool publishToSource, bool preserveItemAliases = false)
    {
        if (_isApplyingSelectedItems)
        {
            return false;
        }

        var resolved = new List<RowSelection>();
        var used = new HashSet<ItemOccurrence>();
        ItemOccurrence? preferred = preferredIndex.HasValue ? Items.GetOccurrenceAt(preferredIndex.Value) : null;
        if (selectedItems != null)
        {
            foreach (object? requested in selectedItems)
            {
                if (requested is null || ResolveSelectionOccurrence(requested, used, preferred) is not { } row) return false;
                if (!used.Add(row)) continue;
                resolved.Add(new(row, preserveItemAliases ? requested : GetSelectionValueFromLogicalItem(row.Item)!));
            }
        }
        return CommitOccurrenceSelection(resolved, preferred ?? resolved.FirstOrDefault().Row, publishToSource);
    }

    private void ApplyInputSelection(IEnumerable selectedItems, object? primaryItem)
    {
        var row = primaryItem is null ? null : ResolveSelectionOccurrence(primaryItem, preferred: _primaryOccurrence);
        ApplySelectedItems(selectedItems, row?.Index, publishToSource: true);
    }

    private void PublishSelectionToSource()
    {
        // Internal SetCurrentValue calls preserve expressions but suppress transfer.
        // Publish the final transaction through each binding's existing mode/trigger.
        GetBindingExpression(SelectedItemProperty)?.OnTargetValueChanged();
        GetBindingExpression(SelectedIndexProperty)?.OnTargetValueChanged();
        GetBindingExpression(SelectedValueProperty)?.OnTargetValueChanged();
    }

    /// <summary>
    /// Selects the specified item, respecting the current selection mode and modifier keys.
    /// </summary>
    /// <param name="item">The ListBoxItem to select.</param>
    /// <param name="isCtrlPressed">Whether the Ctrl key is pressed.</param>
    /// <param name="isShiftPressed">Whether the Shift key is pressed.</param>
    internal void SelectItem(ListBoxItem item, bool isCtrlPressed = false, bool isShiftPressed = false)
    {
        var clickedIndex = GetItemIndex(item);
        if (clickedIndex < 0)
        {
            return;
        }

        var content = GetSelectionValueAtIndex(clickedIndex) ?? item.Content;
        if (content is null) return;
        ClearPendingFocus();
        _focusedOccurrence = Items.GetOccurrenceAt(clickedIndex);

        switch (SelectionMode)
        {
            case SelectionMode.Single:
                SelectSingle(item, content);
                break;

            case SelectionMode.Multiple:
                SelectMultiple(item, content, clickedIndex);
                break;

            case SelectionMode.Extended:
                SelectExtended(item, content, clickedIndex, isCtrlPressed, isShiftPressed);
                break;
        }
    }

    internal bool SetAutomationSelectedItems(IEnumerable<object> items) =>
        ApplySelectedItems(items, preferredIndex: null, publishToSource: true);

    // Automation Select replaces the selection even in a multiple-selection
    // list. Use the existing atomic batch so collections, scalar properties,
    // realized containers and SelectionChanged stay synchronized.
    internal void SetAutomationSelection(object item, bool selected, bool exclusive)
    {
        int index = item is ListBoxItem container ? GetItemIndex(container) : ResolveSelectionOccurrence(item)?.Index ?? -1;
        if (index >= 0) SetAutomationSelectionAt(index, selected, exclusive);
    }

    /// <summary>
    /// Single mode: deselect all others, select clicked item.
    /// </summary>
    private void SelectSingle(ListBoxItem item, object content)
    {
        int index = GetItemIndex(item);
        SetAnchorIndex(index);
        ApplyIndexSelection([index], index);
    }

    private void SelectMultiple(ListBoxItem item, object content, int clickedIndex)
    {
        ToggleSelection(clickedIndex);
        SetAnchorIndex(clickedIndex);
    }

    private void ToggleSelection(int index)
    {
        var row = Items.GetOccurrenceAt(index);
        if (row is null) return;
        var entries = new List<RowSelection>(_selectionRows);
        bool removed = entries.RemoveAll(entry => ReferenceEquals(entry.Row, row)) != 0;
        if (!removed) entries.Add(SelectionAt(index));
        entries = entries.Select(entry => new RowSelection(entry.Row, GetSelectionValueFromLogicalItem(entry.Row.Item)!)).ToList();
        CommitOccurrenceSelection(entries, removed ? entries.LastOrDefault().Row : row, publishToSource: true);
    }

    private void SelectExtended(ListBoxItem item, object content, int clickedIndex, bool isCtrlPressed, bool isShiftPressed)
    {
        if (isShiftPressed)
        {
            int anchor = ResolveAnchorIndex();
            ApplyRangeSelection(anchor >= 0 ? anchor : 0, clickedIndex, isCtrlPressed);
        }
        else if (isCtrlPressed)
        {
            ToggleSelection(clickedIndex);
            SetAnchorIndex(clickedIndex);
        }
        else
        {
            SelectSingle(item, content);
        }
    }

    private void ApplyRangeSelection(int from, int to, bool additive)
    {
        var indices = additive ? GetSelectedOccurrenceIndices().ToList() : new List<int>();
        for (int i = Math.Max(0, Math.Min(from, to)); i <= Math.Max(from, to); i++)
            if (!indices.Contains(i)) indices.Add(i);
        ApplyIndexSelection(indices, to);
    }

    /// <summary>
    /// Gets the index of a ListBoxItem within the ItemsHost children.
    /// </summary>
    /// <returns>The zero-based index, or -1 if not found.</returns>
    private int GetItemIndex(ListBoxItem item)
    {
        var indexFromGenerator = ItemContainerGenerator.IndexFromContainer(item);
        if (indexFromGenerator >= 0)
        {
            return indexFromGenerator;
        }

        var indexOfItem = Items.FindOccurrence(item)?.Index ?? -1;
        if (indexOfItem >= 0)
        {
            return indexOfItem;
        }

        if (ItemsHost == null) return -1;

        for (var i = 0; i < ItemsHost.Children.Count; i++)
        {
            if (ItemsHost.Children[i] == item)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Gets the ListBoxItem at the specified index in the ItemsHost children.
    /// </summary>
    /// <returns>The ListBoxItem, or null if index is out of range.</returns>
    private ListBoxItem? GetItemAtIndex(int index)
    {
        if (index < 0)
        {
            return null;
        }

        return ItemContainerGenerator.ContainerFromIndex(index) as ListBoxItem;
    }

    private IEnumerable<ListBoxItem> EnumerateRealizedContainers()
    {
        var visited = new HashSet<ListBoxItem>(ReferenceEqualityComparer.Instance);
        if (ItemsHost != null)
            foreach (var child in ItemsHost.Children.OfType<ListBoxItem>().ToArray()) if (visited.Add(child)) yield return child;
        foreach (int index in ItemContainerGenerator.GetRealizedIndices().ToArray())
            if (ItemContainerGenerator.ContainerFromIndex(index) is ListBoxItem row && visited.Add(row)) yield return row;
        foreach (var own in _explicitItemMemberships.Keys.ToArray()) if (visited.Add(own)) yield return own;
    }

    private object? GetSelectionValueAtIndex(int index)
    {
        var item = GetItemAt(index);
        return GetSelectionValueFromLogicalItem(item);
    }

    private static bool SelectionValuesEqual(object left, object right) =>
        Equals(GetSelectionValueFromLogicalItem(left), GetSelectionValueFromLogicalItem(right));

    private static object? GetSelectionValueFromLogicalItem(object? item)
    {
        if (item is ListBoxItem listBoxItem)
        {
            if (listBoxItem.Content is UIElement || listBoxItem.Tag is not null)
            {
                return listBoxItem;
            }

            return listBoxItem.Content ?? listBoxItem;
        }

        return item;
    }

    private bool TryResolveSelectionItem(
        object requestedItem,
        out object? selectionValue,
        out int index,
        out object? item)
    {
        var row = ResolveSelectionOccurrence(requestedItem);
        selectionValue = row is null ? null : GetSelectionValueFromLogicalItem(row.Item);
        index = row?.Index ?? -1;
        item = row?.Item;
        return row != null && selectionValue != null;
    }

    private void SetAnchorIndex(int index)
    {
        object? item = GetItemAt(index);
        if (index < 0 || item is null)
        {
            ClearAnchor();
            return;
        }

        _anchorIndex = index;
        _anchorItem = item;
        _anchorOccurrence = Items.GetOccurrenceAt(index);
    }

    private int ResolveAnchorIndex()
    {
        if (_anchorOccurrence is not { IsAlive: true }) { ClearAnchor(); return -1; }
        _anchorIndex = _anchorOccurrence.Index;
        _anchorItem = _anchorOccurrence.Item;
        return _anchorIndex;
    }

    private void ClearAnchor()
    {
        _anchorIndex = -1;
        _anchorItem = null;
        _anchorOccurrence = null;
    }

    /// <summary>
    /// Deselects all items and clears the selection state.
    /// </summary>
    public void UnselectAll() => ApplyInputSelection(Array.Empty<object>(), null);

    /// <summary>
    /// Selects all items. Only applicable in Multiple and Extended modes.
    /// </summary>
    public void SelectAll()
    {
        if (SelectionMode == SelectionMode.Single) return;
        var indices = GetSelectedOccurrenceIndices().ToList();
        for (int i = 0; i < Items.Count; i++) if (!indices.Contains(i)) indices.Add(i);
        ApplyIndexSelection(indices, indices.LastOrDefault(-1));
    }

    /// <inheritdoc />
    protected override void UpdateContainerSelection()
    {
        ulong version = Items.OccurrenceVersion;
        foreach (var child in EnumerateRealizedContainers())
        {
            if (version != Items.OccurrenceVersion) break;
            child.IsSelected = IsOccurrenceSelected(GetItemIndex(child));
        }
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        Focus();

        // Begin drag-select tracking for Extended mode
        if (SelectionMode == SelectionMode.Extended && e.ChangedButton == MouseButton.Left)
        {
        }
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        int count = GetItemCount();
        bool isCtrl = PointerSelectionModifiers.IsToggle(e.KeyboardModifiers);
        bool isShift = e.IsShiftDown;
        bool handled = e.Key switch
        {
            Key.Up => HandleArrowKey(-1, isCtrl, isShift, count),
            Key.Down => HandleArrowKey(1, isCtrl, isShift, count),
            Key.Home when count > 0 => HandleNavigationTo(0, isCtrl, isShift),
            Key.End when count > 0 => HandleNavigationTo(count - 1, isCtrl, isShift),
            Key.Space => HandleSpaceKey(isCtrl),
            Key.A when isCtrl && SelectionMode != SelectionMode.Single => HandleSelectAll(),
            _ => false
        };
        if (handled) e.Handled = true;
    }

    private bool HandleSelectAll()
    {
        SelectAll();
        return true;
    }

    private int GetNavigationIndex()
    {
        if (PendingFocusStillApplies())
            return _pendingFocusOccurrence!.Index;
        // Focus and selection are independent in Multiple and Ctrl-navigation.
        // Read actual focus first, including focus within an item template.
        for (var visual = Keyboard.FocusedElement as Visual; visual is not null; visual = visual.VisualParent)
        {
            if (visual is ListBoxItem item && ReferenceEquals(item.ParentListBox, this))
                return GetItemIndex(item);
        }
        int remembered = _focusedOccurrence is { IsAlive: true } ? _focusedOccurrence.Index : -1;
        return remembered >= 0 ? remembered : SelectedIndex;
    }

    private void ClearPendingFocus()
    {
        _pendingFocusOccurrence = null;
        _pendingFocusOrigin = null;
    }

    private bool PendingFocusStillApplies()
    {
        if (_pendingFocusOccurrence is null) return false;
        var current = Keyboard.FocusedElement;
        if (!_pendingFocusOccurrence.IsAlive ||
            (current is not null && !ReferenceEquals(current, _pendingFocusOrigin) && !ReferenceEquals(current, this)))
        {
            // A later user focus request wins over deferred virtualization.
            ClearPendingFocus();
            return false;
        }
        return true;
    }

    private void FocusItem(int index)
    {
        if (GetItemAt(index) is not { } item) return;
        _focusedOccurrence = Items.GetOccurrenceAt(index);
        _pendingFocusOccurrence = _focusedOccurrence;
        _pendingFocusOrigin = Keyboard.FocusedElement;
        ScrollOccurrenceIntoView(index);
        if (GetItemAtIndex(index) is { } realized && realized.Focus())
        {
            ClearPendingFocus();
            return;
        }
        if (_focusRequestQueued) return;
        _focusRequestQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            _focusRequestQueued = false;
            if (!PendingFocusStillApplies()) return;
            // Complete layout outside item arrangement before calling user focus
            // handlers. Coalesced keys use the latest logical pending target.
            UpdateLayout();
            if (!PendingFocusStillApplies()) return;
            int target = _pendingFocusOccurrence!.Index;
            if (GetItemAtIndex(target) is { } container && container.Focus())
                ClearPendingFocus();
        });
    }

    private bool HandleArrowKey(int direction, bool isCtrl, bool isShift, int itemCount)
    {
        int current = GetNavigationIndex();
        int target = current < 0 ? (direction > 0 ? 0 : itemCount - 1) : current + direction;
        return target >= 0 && target < itemCount && HandleNavigationTo(target, isCtrl, isShift);
    }

    private bool HandleNavigationTo(int index, bool isCtrl, bool isShift)
    {
        int current = GetNavigationIndex();
        if (SelectionMode == SelectionMode.Multiple ||
            (SelectionMode == SelectionMode.Extended && isCtrl && !isShift))
        {
            FocusItem(index);
        }
        else if (SelectionMode == SelectionMode.Extended && isShift)
        {
            int anchor = ResolveAnchorIndex();
            if (anchor < 0)
            {
                anchor = current >= 0 ? current : 0;
                SetAnchorIndex(anchor);
            }
            ApplyRangeSelection(anchor, index, isCtrl);
            FocusItem(index);
        }
        else
        {
            NavigateToIndex(index);
        }
        return true;
    }

    private bool HandleSpaceKey(bool isCtrl)
    {
        int index = GetNavigationIndex();
        if (index < 0 || GetSelectionValueAtIndex(index) is not { } value) return false;
        if (SelectionMode == SelectionMode.Multiple || (SelectionMode == SelectionMode.Extended && isCtrl))
        {
            ToggleSelection(index);
            if (SelectionMode == SelectionMode.Extended) SetAnchorIndex(index);
            return true;
        }
        return false;
    }

    private void NavigateToIndex(int index)
    {
        if (GetSelectionValueAtIndex(index) is not { } value) return;
        SetAnchorIndex(index);
        ApplyIndexSelection([index], index);
        FocusItem(index);
    }

    /// <summary>
    /// Extends a drag from the stable anchor, or adds an item in Multiple mode.
    /// </summary>
    internal void HandleDragSelect(ListBoxItem item)
    {
        int index = GetItemIndex(item);
        if (index < 0 || GetSelectionValueAtIndex(index) is not { } value) return;
        switch (SelectionMode)
        {
            case SelectionMode.Multiple:
                if (!IsOccurrenceSelected(index))
                    ApplyIndexSelection(GetSelectedOccurrenceIndices().Append(index), index);
                break;
            case SelectionMode.Extended when ResolveAnchorIndex() is var anchor && anchor >= 0:
                ApplyRangeSelection(anchor, index, additive: false);
                break;
            default:
                SelectItem(item);
                break;
        }
        FocusItem(index);
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnSelectionModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ListBox listBox)
        {
            // When selection mode changes, clear multi-selection and keep only the current item
            var currentItem = listBox.SelectedItem;
            listBox.ClearAnchor();
            listBox.ApplyInputSelection(currentItem is null ? Array.Empty<object>() : new[] { currentItem }, currentItem);
        }
    }

    #endregion
}

/// <summary>
/// Represents a selectable item in a ListBox.
/// </summary>
public class ListBoxItem : ContentControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.ListBoxItemAutomationPeer(this);
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsSelected dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSelectedProperty =
        Selector.IsSelectedProperty.AddOwner(
            typeof(ListBoxItem),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault |
                FrameworkPropertyMetadataOptions.Journal,
                OnIsSelectedChanged));

    #endregion

    #region Routed Events

    /// <summary>Identifies the Selected routed event.</summary>
    public static readonly RoutedEvent SelectedEvent =
        Selector.SelectedEvent.AddOwner(typeof(ListBoxItem));

    /// <summary>Identifies the Unselected routed event.</summary>
    public static readonly RoutedEvent UnselectedEvent =
        Selector.UnselectedEvent.AddOwner(typeof(ListBoxItem));

    /// <summary>Occurs when this item becomes selected.</summary>
    public event RoutedEventHandler Selected
    {
        add => AddHandler(SelectedEvent, value);
        remove => RemoveHandler(SelectedEvent, value);
    }

    /// <summary>Occurs when this item becomes unselected.</summary>
    public event RoutedEventHandler Unselected
    {
        add => AddHandler(UnselectedEvent, value);
        remove => RemoveHandler(UnselectedEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets whether the item is selected.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty)!;
        set => SetValue(IsSelectedProperty, value);
    }

    /// <summary>
    /// Gets or sets the parent ListBox.
    /// </summary>
    internal ListBox? ParentListBox { get; set; }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ListBoxItem"/> class.
    /// </summary>
    public ListBoxItem()
    {
        // Use template-based content management so the ControlTemplate's
        // ContentPresenter handles displaying string/object content
        UseTemplateContentManagement();

        SetCurrentValue(UIElement.TransitionPropertyProperty, TransitionPropertyCollection.None());
        Focusable = true;
        // Register input event handlers
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseEnterEvent, new MouseEventHandler(OnMouseEnterHandler));
        AddHandler(TouchDownEvent, new RoutedEventHandler(OnTouchDownHandler));
        AddHandler(TouchMoveEvent, new RoutedEventHandler(OnTouchMoveHandler));
        AddHandler(TouchUpEvent, new RoutedEventHandler(OnTouchUpHandler));

        // Touch ripple by default for list items so taps get visual feedback.
        TouchHelper.SetIsRippleEnabled(this, true);
    }

    // Panning gate — same pattern as ButtonBase: defer the selection to TouchUp,
    // cancel if the contact drags more than the threshold so the ancestor
    // ScrollViewer can take over panning.
    private const double TouchPanCancelThresholdDips = 8.0;
    private int _activeTouchId = -1;
    private Point _activeTouchDownPos;
    private bool _touchClickCandidate;

    private void OnTouchDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled || e is not TouchEventArgs touchArgs) return;
        if (!TouchHelper.GetIsTouchInteractive(this)) return;
        _activeTouchId = touchArgs.TouchDevice.Id;
        _activeTouchDownPos = touchArgs.GetTouchPoint(this).Position;
        _touchClickCandidate = true;
        // Set Handled to suppress mouse synthesis (which would otherwise call
        // OnMouseDown → SelectItem instantly, bypassing the panning gate).
        // PointerDown is raised unconditionally by the dispatcher and still
        // reaches an ancestor ScrollViewer.
        e.Handled = true;
    }

    private void OnTouchMoveHandler(object sender, RoutedEventArgs e)
    {
        if (!_touchClickCandidate || e is not TouchEventArgs touchArgs) return;
        if (touchArgs.TouchDevice.Id != _activeTouchId) return;
        var current = touchArgs.GetTouchPoint(this).Position;
        double dx = current.X - _activeTouchDownPos.X;
        double dy = current.Y - _activeTouchDownPos.Y;
        if (dx * dx + dy * dy > TouchPanCancelThresholdDips * TouchPanCancelThresholdDips)
        {
            _touchClickCandidate = false;
        }
    }

    private void OnTouchUpHandler(object sender, RoutedEventArgs e)
    {
        if (e is not TouchEventArgs touchArgs) return;
        if (touchArgs.TouchDevice.Id != _activeTouchId) return;
        bool wasCandidate = _touchClickCandidate;
        _activeTouchId = -1;
        _touchClickCandidate = false;
        if (wasCandidate)
        {
            Focus();
            ParentListBox?.SelectItem(this, isCtrlPressed: false, isShiftPressed: false);
            e.Handled = true;
        }
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            Focus();

            // Extract modifier key state from the event args
            bool ctrl = PointerSelectionModifiers.IsToggle(e.KeyboardModifiers);
            bool shift = (e.KeyboardModifiers & ModifierKeys.Shift) != 0;

            ParentListBox?.SelectItem(this, ctrl, shift);
            e.Handled = true;
        }
    }

    private void OnMouseEnterHandler(object sender, MouseEventArgs e)
    {
        // If left mouse button is down while entering, perform drag selection
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            ParentListBox?.HandleDragSelect(this);
        }
    }

    #endregion

    #region Property Changed

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ListBoxItem item)
        {
            var selected = (bool)(e.NewValue ?? false);
            if (selected)
            {
                item.OnSelected(new RoutedEventArgs(SelectedEvent, item));
            }
            else
            {
                item.OnUnselected(new RoutedEventArgs(UnselectedEvent, item));
            }
        }
    }

    #endregion

    /// <summary>Raises the <see cref="Selected"/> routed event.</summary>
    protected virtual void OnSelected(RoutedEventArgs e) => RaiseEvent(e);

    /// <summary>Raises the <see cref="Unselected"/> routed event.</summary>
    protected virtual void OnUnselected(RoutedEventArgs e) => RaiseEvent(e);

}
