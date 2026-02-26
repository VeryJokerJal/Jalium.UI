using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI.Data;

/// <summary>
/// Represents a CollectionView for collections that implement IList.
/// </summary>
public sealed class ListCollectionView : CollectionView, IEditableCollectionView
{
    private readonly IList _internalList;
    private object? _editItem;
    private object? _newItem;
    private NewItemPlaceholderPosition _newItemPlaceholderPosition = NewItemPlaceholderPosition.None;

    /// <summary>
    /// Initializes a new instance of the ListCollectionView class.
    /// </summary>
    /// <param name="list">The list to wrap.</param>
    public ListCollectionView(IList list) : base(list)
    {
        _internalList = list ?? throw new ArgumentNullException(nameof(list));
    }

    /// <summary>
    /// Gets a value that indicates whether a new item can be added to the collection.
    /// </summary>
    public bool CanAddNew => !IsEditingItem && _internalList is { IsFixedSize: false, IsReadOnly: false };

    /// <summary>
    /// Gets a value that indicates whether the collection view can discard pending changes and restore the original values of an edited object.
    /// </summary>
    public bool CanCancelEdit => _editItem is IEditableObject;

    /// <summary>
    /// Gets a value that indicates whether an item can be removed from the collection.
    /// </summary>
    public bool CanRemove => !IsEditingItem && !IsAddingNew && _internalList is { IsFixedSize: false, IsReadOnly: false };

    /// <summary>
    /// Gets the item that is being added during the current add transaction.
    /// </summary>
    public object? CurrentAddItem => _newItem;

    /// <summary>
    /// Gets the item in the collection that is being edited.
    /// </summary>
    public object? CurrentEditItem => _editItem;

    /// <summary>
    /// Gets a value that indicates whether an add transaction is in progress.
    /// </summary>
    public bool IsAddingNew => _newItem != null;

    /// <summary>
    /// Gets a value that indicates whether an edit transaction is in progress.
    /// </summary>
    public bool IsEditingItem => _editItem != null;

    /// <summary>
    /// Gets or sets the position of the new item placeholder in the ListCollectionView.
    /// </summary>
    public NewItemPlaceholderPosition NewItemPlaceholderPosition
    {
        get => _newItemPlaceholderPosition;
        set => _newItemPlaceholderPosition = value;
    }

    /// <summary>
    /// Starts an add transaction and returns the pending new item.
    /// </summary>
    /// <returns>The pending new item.</returns>
    public object? AddNew()
    {
        if (!CanAddNew)
            throw new InvalidOperationException("Cannot add a new item.");

        CommitEdit();
        CommitNew();

        var itemType = GetItemType();
        if (itemType == null)
            throw new InvalidOperationException("Cannot determine item type.");

        _newItem = Activator.CreateInstance(itemType);
        if (_newItem != null)
        {
            _internalList.Add(_newItem);
            MoveCurrentTo(_newItem);
        }

        return _newItem;
    }

    /// <summary>
    /// Ends the edit transaction and discards the pending changes.
    /// </summary>
    public void CancelEdit()
    {
        if (_editItem == null)
            return;

        if (_editItem is IEditableObject editable)
        {
            editable.CancelEdit();
        }

        _editItem = null;
    }

    /// <summary>
    /// Ends the add transaction and discards the pending new item.
    /// </summary>
    public void CancelNew()
    {
        if (_newItem == null)
            return;

        _internalList.Remove(_newItem);
        _newItem = null;
        Refresh();
    }

    /// <summary>
    /// Ends the edit transaction and saves the pending changes.
    /// </summary>
    public void CommitEdit()
    {
        if (_editItem == null)
            return;

        if (_editItem is IEditableObject editable)
        {
            editable.EndEdit();
        }

        _editItem = null;
        Refresh();
    }

    /// <summary>
    /// Ends the add transaction and saves the pending new item.
    /// </summary>
    public void CommitNew()
    {
        if (_newItem == null)
            return;

        _newItem = null;
        Refresh();
    }

    /// <summary>
    /// Begins an edit transaction of the specified item.
    /// </summary>
    /// <param name="item">The item to edit.</param>
    public void EditItem(object item)
    {
        if (IsAddingNew)
            throw new InvalidOperationException("Cannot edit while adding a new item.");

        CommitEdit();

        _editItem = item;

        if (_editItem is IEditableObject editable)
        {
            editable.BeginEdit();
        }
    }

    /// <summary>
    /// Removes the specified item from the collection.
    /// </summary>
    /// <param name="item">The item to remove.</param>
    public void Remove(object item)
    {
        if (!CanRemove)
            throw new InvalidOperationException("Cannot remove items.");

        _internalList.Remove(item);
        Refresh();
    }

    /// <summary>
    /// Removes the item at the specified position from the collection.
    /// </summary>
    /// <param name="index">The index of the item to remove.</param>
    public void RemoveAt(int index)
    {
        if (!CanRemove)
            throw new InvalidOperationException("Cannot remove items.");

        _internalList.RemoveAt(index);
        Refresh();
    }

    private Type? GetItemType()
    {
        // Try to get item type from generic IList<T>
        var listType = _internalList.GetType();
        var ilistType = listType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IList<>));

        if (ilistType != null)
        {
            return ilistType.GetGenericArguments()[0];
        }

        // Try to get from first item
        if (_internalList.Count > 0)
        {
            return _internalList[0]?.GetType();
        }

        return null;
    }
}

/// <summary>
/// Specifies the position of the placeholder for a new item in a collection.
/// </summary>
public enum NewItemPlaceholderPosition
{
    /// <summary>
    /// The placeholder is not displayed.
    /// </summary>
    None,

    /// <summary>
    /// The placeholder is at the beginning of the collection.
    /// </summary>
    AtBeginning,

    /// <summary>
    /// The placeholder is at the end of the collection.
    /// </summary>
    AtEnd
}

/// <summary>
/// Defines properties and methods for editing collections.
/// </summary>
public interface IEditableCollectionView
{
    /// <summary>
    /// Gets a value that indicates whether a new item can be added to the collection.
    /// </summary>
    bool CanAddNew { get; }

    /// <summary>
    /// Gets a value that indicates whether the collection view can discard pending changes and restore the original values of an edited object.
    /// </summary>
    bool CanCancelEdit { get; }

    /// <summary>
    /// Gets a value that indicates whether an item can be removed from the collection.
    /// </summary>
    bool CanRemove { get; }

    /// <summary>
    /// Gets the item that is being added during the current add transaction.
    /// </summary>
    object? CurrentAddItem { get; }

    /// <summary>
    /// Gets the item in the collection that is being edited.
    /// </summary>
    object? CurrentEditItem { get; }

    /// <summary>
    /// Gets a value that indicates whether an add transaction is in progress.
    /// </summary>
    bool IsAddingNew { get; }

    /// <summary>
    /// Gets a value that indicates whether an edit transaction is in progress.
    /// </summary>
    bool IsEditingItem { get; }

    /// <summary>
    /// Gets or sets the position of the new item placeholder in the collection.
    /// </summary>
    NewItemPlaceholderPosition NewItemPlaceholderPosition { get; set; }

    /// <summary>
    /// Starts an add transaction and returns the pending new item.
    /// </summary>
    object? AddNew();

    /// <summary>
    /// Ends the edit transaction and discards the pending changes.
    /// </summary>
    void CancelEdit();

    /// <summary>
    /// Ends the add transaction and discards the pending new item.
    /// </summary>
    void CancelNew();

    /// <summary>
    /// Ends the edit transaction and saves the pending changes.
    /// </summary>
    void CommitEdit();

    /// <summary>
    /// Ends the add transaction and saves the pending new item.
    /// </summary>
    void CommitNew();

    /// <summary>
    /// Begins an edit transaction of the specified item.
    /// </summary>
    void EditItem(object item);

    /// <summary>
    /// Removes the specified item from the collection.
    /// </summary>
    void Remove(object item);

    /// <summary>
    /// Removes the item at the specified position from the collection.
    /// </summary>
    void RemoveAt(int index);
}
