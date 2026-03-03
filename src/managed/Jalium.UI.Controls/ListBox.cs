using System.Collections;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that displays a list of items and allows the user to select one or more.
/// </summary>
public class ListBox : Selector
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the SelectionMode dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectionModeProperty =
        DependencyProperty.Register(nameof(SelectionMode), typeof(SelectionMode), typeof(ListBox),
            new PropertyMetadata(SelectionMode.Single, OnSelectionModeChanged));

    #endregion

    #region Fields

    private readonly List<object> _selectedItems = new();

    /// <summary>
    /// The anchor index for Extended selection range operations.
    /// Set on regular click and Ctrl+Click; not changed on Shift+Click.
    /// </summary>
    private int _anchorIndex = -1;

    /// <summary>
    /// Tracks whether a mouse-drag selection is active (for Extended mode drag selection).
    /// </summary>
    private bool _isDragSelecting;

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the selection mode.
    /// </summary>
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
    public IList SelectedItems => _selectedItems;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ListBox"/> class.
    /// </summary>
    public ListBox()
    {
        if (ItemsPanel == null)
        {
            ItemsPanel = CreateItemsPanelTemplate(typeof(VirtualizingStackPanel));
        }

        // Register input event handlers
        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
        AddHandler(KeyDownEvent, new RoutedEventHandler(OnKeyDownHandler));
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
    protected override bool IsItemItsOwnContainer(object item)
    {
        return item is ListBoxItem;
    }

    /// <inheritdoc />
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);

        if (element is ListBoxItem listBoxItem)
        {
            listBoxItem.Content = item;
            listBoxItem.ContentTemplate = ItemTemplate;
            listBoxItem.ParentListBox = this;
            var logicalValue = GetSelectionValueFromLogicalItem(item);

            // Set selection state based on current selection mode
            if (SelectionMode == SelectionMode.Single)
            {
                listBoxItem.IsSelected = Equals(logicalValue, SelectedItem);
            }
            else
            {
                listBoxItem.IsSelected = logicalValue != null && _selectedItems.Contains(logicalValue);
            }
        }
    }

    #endregion

    #region Selection

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

    /// <summary>
    /// Single mode: deselect all others, select clicked item.
    /// </summary>
    private void SelectSingle(ListBoxItem item, object content)
    {
        var removedItems = new List<object>(_selectedItems);

        // Deselect all other items
        UnselectAllContainers(item);
        _selectedItems.Clear();

        // Select the clicked item
        item.IsSelected = true;
        _selectedItems.Add(content);
        _anchorIndex = GetItemIndex(item);

        SelectedItem = content;

        // Raise selection changed
        var addedItems = new List<object> { content };
        removedItems.Remove(content); // Don't report as removed if it was already selected
        if (removedItems.Count > 0 || addedItems.Count > 0)
        {
            var args = new SelectionChangedEventArgs(SelectionChangedEvent, removedItems.ToArray(), addedItems.ToArray());
            OnSelectionChanged(args);
        }
    }

    /// <summary>
    /// Multiple mode: click toggles item selection without modifier keys.
    /// </summary>
    private void SelectMultiple(ListBoxItem item, object content, int clickedIndex)
    {
        if (item.IsSelected)
        {
            // Deselect
            item.IsSelected = false;
            _selectedItems.Remove(content);

            // Update SelectedItem to last selected or null
            SelectedItem = _selectedItems.Count > 0 ? _selectedItems[^1] : null;
            _anchorIndex = clickedIndex;

            var args = new SelectionChangedEventArgs(SelectionChangedEvent, new object[] { content }, Array.Empty<object>());
            OnSelectionChanged(args);
        }
        else
        {
            // Select
            item.IsSelected = true;
            _selectedItems.Add(content);
            _anchorIndex = clickedIndex;

            SelectedItem = content;

            var args = new SelectionChangedEventArgs(SelectionChangedEvent, Array.Empty<object>(), new object[] { content });
            OnSelectionChanged(args);
        }
    }

    /// <summary>
    /// Extended mode: click-only = single select; Ctrl+Click = toggle; Shift+Click = range select.
    /// </summary>
    private void SelectExtended(ListBoxItem item, object content, int clickedIndex, bool isCtrlPressed, bool isShiftPressed)
    {
        if (isShiftPressed)
        {
            // Shift+Click: select range from anchor to clicked item
            var fromIndex = _anchorIndex >= 0 ? _anchorIndex : 0;
            var removedItems = new List<object>(_selectedItems);

            // If Ctrl is NOT held, clear existing selection first
            if (!isCtrlPressed)
            {
                UnselectAllContainers();
                _selectedItems.Clear();
            }

            // Select range
            var addedItems = SelectRange(fromIndex, clickedIndex);

            // Calculate actual changes
            foreach (var added in addedItems)
                removedItems.Remove(added);

            SelectedItem = content;

            // Don't update anchor on Shift+Click
            var args = new SelectionChangedEventArgs(SelectionChangedEvent, removedItems.ToArray(), addedItems.ToArray());
            OnSelectionChanged(args);
        }
        else if (isCtrlPressed)
        {
            // Ctrl+Click: toggle clicked item without affecting others
            if (item.IsSelected)
            {
                item.IsSelected = false;
                _selectedItems.Remove(content);
                SelectedItem = _selectedItems.Count > 0 ? _selectedItems[^1] : null;

                var args = new SelectionChangedEventArgs(SelectionChangedEvent, new object[] { content }, Array.Empty<object>());
                OnSelectionChanged(args);
            }
            else
            {
                item.IsSelected = true;
                _selectedItems.Add(content);
                SelectedItem = content;

                var args = new SelectionChangedEventArgs(SelectionChangedEvent, Array.Empty<object>(), new object[] { content });
                OnSelectionChanged(args);
            }

            _anchorIndex = clickedIndex;
        }
        else
        {
            // Click only: deselect all, select clicked (like Single)
            SelectSingle(item, content);
        }
    }

    /// <summary>
    /// Selects all items in the range [from, to] (inclusive, works in both directions).
    /// Returns the list of newly added items.
    /// </summary>
    private List<object> SelectRange(int from, int to)
    {
        var addedItems = new List<object>();
        var start = Math.Min(from, to);
        var end = Math.Max(from, to);

        for (var i = start; i <= end; i++)
        {
            var item = GetSelectionValueAtIndex(i);
            if (item != null)
            {
                if (!_selectedItems.Contains(item))
                {
                    _selectedItems.Add(item);
                    addedItems.Add(item);
                }

                var lbi = GetItemAtIndex(i);
                if (lbi != null)
                {
                    lbi.IsSelected = true;
                }
            }
        }

        return addedItems;
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
        if (ItemsHost == null)
        {
            yield break;
        }

        foreach (var child in ItemsHost.Children)
        {
            if (child is ListBoxItem item)
            {
                yield return item;
            }
        }
    }

    private object? GetSelectionValueAtIndex(int index)
    {
        var item = GetItemAt(index);
        return GetSelectionValueFromLogicalItem(item);
    }

    private static object? GetSelectionValueFromLogicalItem(object? item)
    {
        if (item is ListBoxItem listBoxItem)
        {
            return listBoxItem.Content ?? listBoxItem;
        }

        return item;
    }

    /// <summary>
    /// Deselects all items and clears the selection state.
    /// </summary>
    public void UnselectAll()
    {
        var removedItems = new List<object>(_selectedItems);

        UnselectAllContainers();
        _selectedItems.Clear();
        SelectedItem = null;

        if (removedItems.Count > 0)
        {
            var args = new SelectionChangedEventArgs(SelectionChangedEvent, removedItems.ToArray(), Array.Empty<object>());
            OnSelectionChanged(args);
        }
    }

    /// <summary>
    /// Deselects all ListBoxItem containers visually, optionally excluding one.
    /// </summary>
    private void UnselectAllContainers(ListBoxItem? except = null)
    {
        foreach (var child in EnumerateRealizedContainers())
        {
            if (child != except)
            {
                child.IsSelected = false;
            }
        }
    }

    /// <summary>
    /// Selects all items. Only applicable in Multiple and Extended modes.
    /// </summary>
    public void SelectAll()
    {
        if (SelectionMode == SelectionMode.Single) return;
        var addedItems = new List<object>();
        var count = GetItemCount();
        for (var i = 0; i < count; i++)
        {
            var item = GetSelectionValueAtIndex(i);
            if (item != null && !_selectedItems.Contains(item))
            {
                _selectedItems.Add(item);
                addedItems.Add(item);
            }

            var lbi = GetItemAtIndex(i);
            if (lbi != null)
            {
                lbi.IsSelected = item != null;
            }
        }

        if (_selectedItems.Count > 0)
            SelectedItem = _selectedItems[^1];

        if (addedItems.Count > 0)
        {
            var args = new SelectionChangedEventArgs(SelectionChangedEvent, Array.Empty<object>(), addedItems.ToArray());
            OnSelectionChanged(args);
        }
    }

    /// <inheritdoc />
    protected override void UpdateContainerSelection()
    {
        if (SelectionMode == SelectionMode.Single)
        {
            foreach (var child in EnumerateRealizedContainers())
            {
                var index = GetItemIndex(child);
                var value = GetSelectionValueAtIndex(index);
                child.IsSelected = (index == SelectedIndex) || Equals(value, SelectedItem);
            }
        }
        else
        {
            foreach (var child in EnumerateRealizedContainers())
            {
                var index = GetItemIndex(child);
                var value = GetSelectionValueAtIndex(index);
                child.IsSelected = value != null && _selectedItems.Contains(value);
            }
        }
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        Focus();

        // Begin drag-select tracking for Extended mode
        if (SelectionMode == SelectionMode.Extended && e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            _isDragSelecting = true;
        }
    }

    private void OnKeyDownHandler(object sender, RoutedEventArgs e)
    {
        if (e is KeyEventArgs keyArgs)
        {
            var handled = false;
            var itemCount = GetItemCount();
            var isCtrl = keyArgs.IsControlDown;
            var isShift = keyArgs.IsShiftDown;

            switch (keyArgs.Key)
            {
                case Key.Up:
                    handled = HandleArrowKey(-1, isCtrl, isShift, itemCount);
                    break;

                case Key.Down:
                    handled = HandleArrowKey(1, isCtrl, isShift, itemCount);
                    break;

                case Key.Home:
                    if (itemCount > 0)
                    {
                        if (SelectionMode == SelectionMode.Extended && isShift)
                        {
                            // Shift+Home: select from anchor to first item
                            var removedItems = new List<object>(_selectedItems);
                            if (!isCtrl)
                            {
                                UnselectAllContainers();
                                _selectedItems.Clear();
                            }
                            var fromIndex = _anchorIndex >= 0 ? _anchorIndex : SelectedIndex;
                            var addedItems = SelectRange(0, fromIndex);
                            foreach (var added in addedItems) removedItems.Remove(added);
                            SelectedIndex = 0;
                            if (addedItems.Count > 0 || removedItems.Count > 0)
                            {
                                var args = new SelectionChangedEventArgs(SelectionChangedEvent, removedItems.ToArray(), addedItems.ToArray());
                                OnSelectionChanged(args);
                            }
                        }
                        else
                        {
                            NavigateToIndex(0);
                        }
                        handled = true;
                    }
                    break;

                case Key.End:
                    if (itemCount > 0)
                    {
                        if (SelectionMode == SelectionMode.Extended && isShift)
                        {
                            // Shift+End: select from anchor to last item
                            var removedItems = new List<object>(_selectedItems);
                            if (!isCtrl)
                            {
                                UnselectAllContainers();
                                _selectedItems.Clear();
                            }
                            var fromIndex = _anchorIndex >= 0 ? _anchorIndex : SelectedIndex;
                            var addedItems = SelectRange(fromIndex, itemCount - 1);
                            foreach (var added in addedItems) removedItems.Remove(added);
                            SelectedIndex = itemCount - 1;
                            if (addedItems.Count > 0 || removedItems.Count > 0)
                            {
                                var args = new SelectionChangedEventArgs(SelectionChangedEvent, removedItems.ToArray(), addedItems.ToArray());
                                OnSelectionChanged(args);
                            }
                        }
                        else
                        {
                            NavigateToIndex(itemCount - 1);
                        }
                        handled = true;
                    }
                    break;

                case Key.Space:
                    handled = HandleSpaceKey(isCtrl);
                    break;

                case Key.A:
                    // Ctrl+A: select all (in Multiple/Extended mode)
                    if (isCtrl && SelectionMode != SelectionMode.Single)
                    {
                        SelectAll();
                        handled = true;
                    }
                    break;
            }

            if (handled)
            {
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Handles Up/Down arrow key navigation with modifier key support.
    /// </summary>
    /// <param name="direction">-1 for Up, +1 for Down.</param>
    /// <param name="isCtrl">Whether Ctrl is pressed.</param>
    /// <param name="isShift">Whether Shift is pressed.</param>
    /// <param name="itemCount">Total number of items.</param>
    /// <returns>True if the key was handled.</returns>
    private bool HandleArrowKey(int direction, bool isCtrl, bool isShift, int itemCount)
    {
        var currentIndex = SelectedIndex;
        var newIndex = currentIndex + direction;

        if (newIndex < 0 || newIndex >= itemCount)
            return false;

        switch (SelectionMode)
        {
            case SelectionMode.Single:
                SelectedIndex = newIndex;
                return true;

            case SelectionMode.Multiple:
                // In Multiple mode, arrow keys just move the focused item
                // Selection is only toggled with Space
                SelectedIndex = newIndex;
                return true;

            case SelectionMode.Extended:
                if (isShift)
                {
                    // Shift+Arrow: extend selection range from anchor
                    var fromIndex = _anchorIndex >= 0 ? _anchorIndex : currentIndex;
                    var removedItems = new List<object>(_selectedItems);

                    if (!isCtrl)
                    {
                        UnselectAllContainers();
                        _selectedItems.Clear();
                    }

                    var addedItems = SelectRange(fromIndex, newIndex);
                    foreach (var added in addedItems) removedItems.Remove(added);

                    SelectedIndex = newIndex;
                    SelectedItem = GetSelectionValueAtIndex(newIndex);

                    if (addedItems.Count > 0 || removedItems.Count > 0)
                    {
                        var args = new SelectionChangedEventArgs(SelectionChangedEvent, removedItems.ToArray(), addedItems.ToArray());
                        OnSelectionChanged(args);
                    }
                    // Don't update anchor on Shift navigation
                    return true;
                }
                else if (isCtrl)
                {
                    // Ctrl+Arrow: move focus without changing selection
                    SelectedIndex = newIndex;
                    // Don't change selection, just update the focus position
                    return true;
                }
                else
                {
                    // Plain arrow: deselect all, select new item
                    NavigateToIndex(newIndex);
                    return true;
                }
        }

        return false;
    }

    /// <summary>
    /// Handles Space key for toggling selection in Multiple and Extended modes.
    /// </summary>
    private bool HandleSpaceKey(bool isCtrl)
    {
        var currentIndex = SelectedIndex;
        if (currentIndex < 0) return false;

        var value = GetSelectionValueAtIndex(currentIndex);
        if (value == null) return false;
        var item = GetItemAtIndex(currentIndex);

        switch (SelectionMode)
        {
            case SelectionMode.Multiple:
                if (_selectedItems.Contains(value))
                {
                    _selectedItems.Remove(value);
                    if (item != null) item.IsSelected = false;
                    SelectedItem = _selectedItems.Count > 0 ? _selectedItems[^1] : null;
                    var removeArgs = new SelectionChangedEventArgs(SelectionChangedEvent, new object[] { value }, Array.Empty<object>());
                    OnSelectionChanged(removeArgs);
                }
                else
                {
                    _selectedItems.Add(value);
                    if (item != null) item.IsSelected = true;
                    SelectedItem = value;
                    var addArgs = new SelectionChangedEventArgs(SelectionChangedEvent, Array.Empty<object>(), new object[] { value });
                    OnSelectionChanged(addArgs);
                }
                return true;

            case SelectionMode.Extended:
                if (isCtrl)
                {
                    // Ctrl+Space: toggle current item without affecting others
                    if (_selectedItems.Contains(value))
                    {
                        if (item != null) item.IsSelected = false;
                        _selectedItems.Remove(value);
                        SelectedItem = _selectedItems.Count > 0 ? _selectedItems[^1] : null;

                        var args = new SelectionChangedEventArgs(SelectionChangedEvent, new object[] { value }, Array.Empty<object>());
                        OnSelectionChanged(args);
                    }
                    else
                    {
                        if (item != null) item.IsSelected = true;
                        _selectedItems.Add(value);
                        SelectedItem = value;

                        var args = new SelectionChangedEventArgs(SelectionChangedEvent, Array.Empty<object>(), new object[] { value });
                        OnSelectionChanged(args);
                    }
                    _anchorIndex = currentIndex;
                    return true;
                }
                break;
        }

        return false;
    }

    /// <summary>
    /// Navigates to the specified index, clearing multi-selection and selecting only that item.
    /// Used for plain arrow key navigation and Home/End without Shift.
    /// </summary>
    private void NavigateToIndex(int index)
    {
        var value = GetSelectionValueAtIndex(index);
        if (value == null) return;
        var item = GetItemAtIndex(index);

        var removedItems = new List<object>(_selectedItems);

        UnselectAllContainers();
        _selectedItems.Clear();

        if (item != null) item.IsSelected = true;
        _selectedItems.Add(value);
        removedItems.Remove(value);

        _anchorIndex = index;
        SelectedIndex = index;
        SelectedItem = value;

        if (removedItems.Count > 0)
        {
            var addedItems = new object[] { value };
            var args = new SelectionChangedEventArgs(SelectionChangedEvent, removedItems.ToArray(), addedItems);
            OnSelectionChanged(args);
        }
    }

    /// <summary>
    /// Handles drag selection when mouse enters a ListBoxItem while button is pressed.
    /// In Extended mode, this behaves like Shift+Click (range selection from anchor).
    /// In Multiple mode, this selects the item.
    /// In Single mode, this selects the item (existing behavior).
    /// </summary>
    internal void HandleDragSelect(ListBoxItem item)
    {
        switch (SelectionMode)
        {
            case SelectionMode.Single:
                SelectItem(item);
                break;

            case SelectionMode.Multiple:
                // In Multiple mode, drag-entering selects the item (if not already selected)
                if (!item.IsSelected)
                {
                    var index = GetItemIndex(item);
                    var content = GetSelectionValueAtIndex(index) ?? item.Content;
                    item.IsSelected = true;
                    if (content != null) _selectedItems.Add(content);
                    SelectedItem = content;

                    var args = new SelectionChangedEventArgs(SelectionChangedEvent, Array.Empty<object>(), new object[] { content! });
                    OnSelectionChanged(args);
                }
                break;

            case SelectionMode.Extended:
                // Extended drag: range-select from anchor to current item (like Shift+Click)
                if (_anchorIndex >= 0)
                {
                    var clickedIndex = GetItemIndex(item);
                    if (clickedIndex < 0)
                    {
                        return;
                    }
                    var removedItems = new List<object>(_selectedItems);

                    UnselectAllContainers();
                    _selectedItems.Clear();

                    var addedItems = SelectRange(_anchorIndex, clickedIndex);
                    foreach (var added in addedItems) removedItems.Remove(added);

                    SelectedItem = GetSelectionValueAtIndex(clickedIndex) ?? item.Content;

                    if (addedItems.Count > 0 || removedItems.Count > 0)
                    {
                        var args = new SelectionChangedEventArgs(SelectionChangedEvent, removedItems.ToArray(), addedItems.ToArray());
                        OnSelectionChanged(args);
                    }
                }
                else
                {
                    // No anchor yet, just select single item
                    SelectItem(item);
                }
                break;
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnSelectionModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ListBox listBox)
        {
            // When selection mode changes, clear multi-selection and keep only the current item
            var currentItem = listBox.SelectedItem;
            listBox.UnselectAllContainers();
            listBox._selectedItems.Clear();
            listBox._anchorIndex = -1;

            if (currentItem != null)
            {
                listBox._selectedItems.Add(currentItem);
                listBox.UpdateContainerSelection();
            }
        }
    }

    #endregion
}

/// <summary>
/// Represents a selectable item in a ListBox.
/// </summary>
public class ListBoxItem : ContentControl
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the IsSelected dependency property.
    /// </summary>
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(nameof(IsSelected), typeof(bool), typeof(ListBoxItem),
            new PropertyMetadata(false, OnIsSelectedChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets whether the item is selected.
    /// </summary>
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

        Focusable = true;

        // Register input event handlers
        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
        AddHandler(MouseEnterEvent, new RoutedEventHandler(OnMouseEnterHandler));
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            Focus();

            // Extract modifier key state from the event args
            bool ctrl = (mouseArgs.KeyboardModifiers & ModifierKeys.Control) != 0;
            bool shift = (mouseArgs.KeyboardModifiers & ModifierKeys.Shift) != 0;

            ParentListBox?.SelectItem(this, ctrl, shift);
            e.Handled = true;
        }
    }

    private void OnMouseEnterHandler(object sender, RoutedEventArgs e)
    {
        // If left mouse button is down while entering, perform drag selection
        if (e is MouseEventArgs mouseArgs && mouseArgs.LeftButton == MouseButtonState.Pressed)
        {
            ParentListBox?.HandleDragSelect(this);
        }
    }

    #endregion

    #region Property Changed

    private static void OnIsSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Triggers handle visual changes now
    }

    #endregion
}
