using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents an abstract base class for controls that support multiple item selection.
/// </summary>
public abstract class MultiSelector : Selector
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the CanSelectMultipleItems dependency property.
    /// </summary>
    public static readonly DependencyProperty CanSelectMultipleItemsProperty =
        DependencyProperty.Register(nameof(CanSelectMultipleItems), typeof(bool), typeof(MultiSelector),
            new PropertyMetadata(true));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets a value indicating whether multiple items can be selected.
    /// </summary>
    protected bool CanSelectMultipleItems
    {
        get => (bool)GetValue(CanSelectMultipleItemsProperty)!;
        set => SetValue(CanSelectMultipleItemsProperty, value);
    }

    /// <summary>
    /// Gets the collection of selected items.
    /// </summary>
    public ObservableCollection<object> SelectedItems { get; }

    /// <summary>
    /// Gets a value indicating whether a batch selection update is in progress.
    /// </summary>
    protected bool IsUpdatingSelectedItems => _isUpdatingSelectedItems;

    #endregion

    #region Private Fields

    private bool _isUpdatingSelectedItems;
    private readonly List<object> _pendingAdditions = new();
    private readonly List<object> _pendingRemovals = new();

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiSelector"/> class.
    /// </summary>
    protected MultiSelector()
    {
        SelectedItems = new ObservableCollection<object>();
        SelectedItems.CollectionChanged += OnSelectedItemsCollectionChanged;
    }

    #endregion

    #region Selection Methods

    /// <summary>
    /// Selects all items in the control.
    /// </summary>
    public void SelectAll()
    {
        if (!CanSelectMultipleItems)
            return;

        BeginUpdateSelectedItems();

        try
        {
            foreach (var item in Items)
            {
                if (!SelectedItems.Contains(item))
                {
                    SelectedItems.Add(item);
                }
            }
        }
        finally
        {
            EndUpdateSelectedItems();
        }
    }

    /// <summary>
    /// Clears all selections.
    /// </summary>
    public void UnselectAll()
    {
        BeginUpdateSelectedItems();

        try
        {
            SelectedItems.Clear();
        }
        finally
        {
            EndUpdateSelectedItems();
        }
    }

    /// <summary>
    /// Begins a batch update of selected items.
    /// </summary>
    /// <remarks>
    /// Call this method before making multiple selection changes to improve performance.
    /// Must be paired with a call to <see cref="EndUpdateSelectedItems"/>.
    /// </remarks>
    protected void BeginUpdateSelectedItems()
    {
        _isUpdatingSelectedItems = true;
        _pendingAdditions.Clear();
        _pendingRemovals.Clear();
    }

    /// <summary>
    /// Ends a batch update of selected items.
    /// </summary>
    /// <remarks>
    /// This method commits all pending selection changes and raises the appropriate events.
    /// </remarks>
    protected void EndUpdateSelectedItems()
    {
        _isUpdatingSelectedItems = false;

        if (_pendingAdditions.Count > 0 || _pendingRemovals.Count > 0)
        {
            OnSelectionChanged(_pendingRemovals.ToList(), _pendingAdditions.ToList());
            _pendingAdditions.Clear();
            _pendingRemovals.Clear();
        }
    }

    /// <summary>
    /// Selects or deselects a specific item.
    /// </summary>
    /// <param name="item">The item to select or deselect.</param>
    /// <param name="isSelected">True to select, false to deselect.</param>
    protected void SetItemSelected(object item, bool isSelected)
    {
        if (isSelected)
        {
            if (!CanSelectMultipleItems)
            {
                // Clear existing selection for single-select mode
                SelectedItems.Clear();
            }

            if (!SelectedItems.Contains(item))
            {
                SelectedItems.Add(item);
            }
        }
        else
        {
            SelectedItems.Remove(item);
        }
    }

    /// <summary>
    /// Toggles the selection state of an item.
    /// </summary>
    /// <param name="item">The item to toggle.</param>
    protected void ToggleItemSelection(object item)
    {
        if (SelectedItems.Contains(item))
        {
            SelectedItems.Remove(item);
        }
        else
        {
            if (!CanSelectMultipleItems)
            {
                SelectedItems.Clear();
            }
            SelectedItems.Add(item);
        }
    }

    /// <summary>
    /// Selects a range of items.
    /// </summary>
    /// <param name="startIndex">The starting index of the range.</param>
    /// <param name="endIndex">The ending index of the range.</param>
    protected void SelectRange(int startIndex, int endIndex)
    {
        if (!CanSelectMultipleItems)
            return;

        var minIndex = Math.Min(startIndex, endIndex);
        var maxIndex = Math.Max(startIndex, endIndex);

        BeginUpdateSelectedItems();

        try
        {
            for (var i = minIndex; i <= maxIndex; i++)
            {
                if (i >= 0 && i < Items.Count)
                {
                    var item = Items[i];
                    if (!SelectedItems.Contains(item))
                    {
                        SelectedItems.Add(item);
                    }
                }
            }
        }
        finally
        {
            EndUpdateSelectedItems();
        }
    }

    #endregion

    #region Selection Changed Handling

    private void OnSelectedItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_isUpdatingSelectedItems)
        {
            // Track changes for batch processing
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item != null)
                    {
                        _pendingAdditions.Add(item);
                        _pendingRemovals.Remove(item);
                    }
                }
            }

            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems)
                {
                    if (item != null)
                    {
                        _pendingRemovals.Add(item);
                        _pendingAdditions.Remove(item);
                    }
                }
            }
        }
        else
        {
            // Immediate processing
            var removed = e.OldItems?.Cast<object>().ToList() ?? new List<object>();
            var added = e.NewItems?.Cast<object>().ToList() ?? new List<object>();

            if (removed.Count > 0 || added.Count > 0)
            {
                OnSelectionChanged(removed, added);
            }
        }

        // Update SelectedItem and SelectedIndex to reflect first selected item
        if (SelectedItems.Count > 0)
        {
            var firstSelected = SelectedItems[0];
            if (!Equals(SelectedItem, firstSelected))
            {
                SelectedItem = firstSelected;
            }
        }
        else
        {
            SelectedItem = null;
            SelectedIndex = -1;
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Called when the selection changes.
    /// </summary>
    /// <param name="removedItems">Items that were removed from selection.</param>
    /// <param name="addedItems">Items that were added to selection.</param>
    protected virtual void OnSelectionChanged(IList<object> removedItems, IList<object> addedItems)
    {
        // Derived classes can override to handle selection changes
    }

    #endregion

    #region Container Management

    /// <summary>
    /// Gets a value indicating whether the specified item is selected.
    /// </summary>
    /// <param name="item">The item to check.</param>
    /// <returns>True if the item is selected; otherwise, false.</returns>
    public bool IsItemSelected(object item)
    {
        return SelectedItems.Contains(item);
    }

    /// <summary>
    /// Gets the selected items as an array.
    /// </summary>
    /// <returns>An array of selected items.</returns>
    public object[] GetSelectedItems()
    {
        return SelectedItems.ToArray();
    }

    #endregion
}
