using System.Collections;
using System.Collections.Specialized;

namespace Jalium.UI.Documents;

/// <summary>
/// A generic collection of <see cref="TextElement"/>-derived objects that provides
/// collection change notifications and maintains parent-child relationships.
/// </summary>
/// <typeparam name="T">The type of elements in the collection. Must be a class type.</typeparam>
public sealed class TextElementCollection<T> : IList<T>, IList, INotifyCollectionChanged where T : class
{
    private readonly List<T> _items = new();

    /// <summary>
    /// Occurs when an item is added, removed, changed, moved, or the entire list is refreshed.
    /// </summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>
    /// Gets or sets the element at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the element.</param>
    /// <returns>The element at the specified index.</returns>
    public T this[int index]
    {
        get => _items[index];
        set
        {
            var old = _items[index];
            _items[index] = value;
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Replace, value, old, index));
        }
    }

    /// <inheritdoc />
    object? IList.this[int index]
    {
        get => _items[index];
        set => this[index] = (T)value!;
    }

    /// <summary>
    /// Gets the number of elements in the collection.
    /// </summary>
    public int Count => _items.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    bool IList.IsFixedSize => false;

    /// <inheritdoc />
    bool ICollection.IsSynchronized => false;

    /// <inheritdoc />
    object ICollection.SyncRoot => ((ICollection)_items).SyncRoot;

    /// <summary>
    /// Adds an item to the end of the collection.
    /// </summary>
    /// <param name="item">The item to add.</param>
    public void Add(T item)
    {
        _items.Add(item);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, item, _items.Count - 1));
    }

    /// <inheritdoc />
    int IList.Add(object? value)
    {
        Add((T)value!);
        return Count - 1;
    }

    /// <summary>
    /// Removes all items from the collection.
    /// </summary>
    public void Clear()
    {
        _items.Clear();
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Determines whether the collection contains the specified item.
    /// </summary>
    /// <param name="item">The item to locate.</param>
    /// <returns>true if the item is found; otherwise false.</returns>
    public bool Contains(T item) => _items.Contains(item);

    /// <inheritdoc />
    bool IList.Contains(object? value) => Contains((T)value!);

    /// <summary>
    /// Copies the elements of the collection to an array, starting at the specified index.
    /// </summary>
    /// <param name="array">The destination array.</param>
    /// <param name="arrayIndex">The zero-based index in array at which copying begins.</param>
    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <inheritdoc />
    void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    /// <returns>An enumerator for the collection.</returns>
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Returns the zero-based index of the specified item.
    /// </summary>
    /// <param name="item">The item to locate.</param>
    /// <returns>The index of the item, or -1 if not found.</returns>
    public int IndexOf(T item) => _items.IndexOf(item);

    /// <inheritdoc />
    int IList.IndexOf(object? value) => IndexOf((T)value!);

    /// <summary>
    /// Inserts an item at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index at which to insert.</param>
    /// <param name="item">The item to insert.</param>
    public void Insert(int index, T item)
    {
        _items.Insert(index, item);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, item, index));
    }

    /// <inheritdoc />
    void IList.Insert(int index, object? value) => Insert(index, (T)value!);

    /// <summary>
    /// Removes the first occurrence of the specified item from the collection.
    /// </summary>
    /// <param name="item">The item to remove.</param>
    /// <returns>true if the item was found and removed; otherwise false.</returns>
    public bool Remove(T item)
    {
        var index = _items.IndexOf(item);
        if (index < 0) return false;
        _items.RemoveAt(index);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Remove, item, index));
        return true;
    }

    /// <inheritdoc />
    void IList.Remove(object? value) => Remove((T)value!);

    /// <summary>
    /// Removes the item at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the item to remove.</param>
    public void RemoveAt(int index)
    {
        var item = _items[index];
        _items.RemoveAt(index);
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Remove, item, index));
    }

    /// <summary>
    /// Inserts an item immediately after the specified sibling element.
    /// If <paramref name="previousSibling"/> is null, the item is inserted at position 0.
    /// </summary>
    /// <param name="previousSibling">The element after which to insert, or null to insert at the beginning.</param>
    /// <param name="item">The item to insert.</param>
    public void InsertAfter(T? previousSibling, T item)
    {
        var index = previousSibling != null ? _items.IndexOf(previousSibling) + 1 : 0;
        Insert(index, item);
    }

    /// <summary>
    /// Inserts an item immediately before the specified sibling element.
    /// If <paramref name="nextSibling"/> is null, the item is inserted at the end.
    /// </summary>
    /// <param name="nextSibling">The element before which to insert, or null to insert at the end.</param>
    /// <param name="item">The item to insert.</param>
    public void InsertBefore(T? nextSibling, T item)
    {
        var index = nextSibling != null ? _items.IndexOf(nextSibling) : _items.Count;
        Insert(index, item);
    }

    /// <summary>
    /// Adds a range of items to the end of the collection.
    /// </summary>
    /// <param name="range">The items to add.</param>
    public void AddRange(IEnumerable<T> range)
    {
        foreach (var item in range)
            Add(item);
    }
}
