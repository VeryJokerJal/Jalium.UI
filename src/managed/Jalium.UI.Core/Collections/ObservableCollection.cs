using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Jalium.UI.Collections;

/// <summary>
/// Represents a dynamic data collection that provides notifications when items get added, removed, or when the whole list is refreshed.
/// </summary>
/// <typeparam name="T">The type of elements in the collection.</typeparam>
public class ObservableCollection<T> : IList<T>, IList, INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly List<T> _items;
    private SimpleMonitor? _monitor;
    private const string CountString = "Count";
    private const string IndexerName = "Item[]";

    /// <summary>
    /// Occurs when the collection changes.
    /// </summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="ObservableCollection{T}"/> class.
    /// </summary>
    public ObservableCollection()
    {
        _items = new List<T>();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ObservableCollection{T}"/> class that contains elements copied from the specified collection.
    /// </summary>
    /// <param name="collection">The collection from which the elements are copied.</param>
    public ObservableCollection(IEnumerable<T> collection)
    {
        _items = new List<T>(collection ?? throw new ArgumentNullException(nameof(collection)));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ObservableCollection{T}"/> class that contains elements copied from the specified list.
    /// </summary>
    /// <param name="list">The list from which the elements are copied.</param>
    public ObservableCollection(List<T> list)
    {
        _items = new List<T>(list ?? throw new ArgumentNullException(nameof(list)));
    }

    /// <summary>
    /// Gets the number of elements contained in the collection.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Gets a value indicating whether the collection is read-only.
    /// </summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// Gets a value indicating whether access to the collection is synchronized (thread safe).
    /// </summary>
    bool ICollection.IsSynchronized => false;

    /// <summary>
    /// Gets an object that can be used to synchronize access to the collection.
    /// </summary>
    object ICollection.SyncRoot => ((ICollection)_items).SyncRoot;

    /// <summary>
    /// Gets a value indicating whether the collection has a fixed size.
    /// </summary>
    bool IList.IsFixedSize => false;

    /// <summary>
    /// Gets or sets the element at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the element.</param>
    public T this[int index]
    {
        get => _items[index];
        set
        {
            CheckReentrancy();
            var oldItem = _items[index];
            _items[index] = value;
            OnPropertyChanged(IndexerName);
            OnCollectionChanged(NotifyCollectionChangedAction.Replace, value, oldItem, index);
        }
    }

    /// <summary>
    /// Gets or sets the element at the specified index.
    /// </summary>
    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = (T)value!;
    }

    /// <summary>
    /// Adds an item to the collection.
    /// </summary>
    /// <param name="item">The item to add.</param>
    public void Add(T item)
    {
        CheckReentrancy();
        var index = _items.Count;
        _items.Add(item);
        OnPropertyChanged(CountString);
        OnPropertyChanged(IndexerName);
        OnCollectionChanged(NotifyCollectionChangedAction.Add, item, index);
    }

    /// <summary>
    /// Adds an item to the collection.
    /// </summary>
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
        CheckReentrancy();
        _items.Clear();
        OnPropertyChanged(CountString);
        OnPropertyChanged(IndexerName);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Determines whether the collection contains a specific value.
    /// </summary>
    /// <param name="item">The object to locate in the collection.</param>
    public bool Contains(T item) => _items.Contains(item);

    /// <summary>
    /// Determines whether the collection contains a specific value.
    /// </summary>
    bool IList.Contains(object? value) => Contains((T)value!);

    /// <summary>
    /// Copies the elements of the collection to an array, starting at a particular array index.
    /// </summary>
    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    /// <summary>
    /// Copies the elements of the collection to an array, starting at a particular array index.
    /// </summary>
    void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Determines the index of a specific item in the collection.
    /// </summary>
    /// <param name="item">The object to locate in the collection.</param>
    public int IndexOf(T item) => _items.IndexOf(item);

    /// <summary>
    /// Determines the index of a specific item in the collection.
    /// </summary>
    int IList.IndexOf(object? value) => IndexOf((T)value!);

    /// <summary>
    /// Inserts an item to the collection at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index at which item should be inserted.</param>
    /// <param name="item">The object to insert into the collection.</param>
    public void Insert(int index, T item)
    {
        CheckReentrancy();
        _items.Insert(index, item);
        OnPropertyChanged(CountString);
        OnPropertyChanged(IndexerName);
        OnCollectionChanged(NotifyCollectionChangedAction.Add, item, index);
    }

    /// <summary>
    /// Inserts an item to the collection at the specified index.
    /// </summary>
    void IList.Insert(int index, object? value) => Insert(index, (T)value!);

    /// <summary>
    /// Moves the item at the specified index to a new location in the collection.
    /// </summary>
    /// <param name="oldIndex">The zero-based index specifying the location of the item to be moved.</param>
    /// <param name="newIndex">The zero-based index specifying the new location of the item.</param>
    public void Move(int oldIndex, int newIndex)
    {
        CheckReentrancy();
        var item = _items[oldIndex];
        _items.RemoveAt(oldIndex);
        _items.Insert(newIndex, item);
        OnPropertyChanged(IndexerName);
        OnCollectionChanged(NotifyCollectionChangedAction.Move, item, newIndex, oldIndex);
    }

    /// <summary>
    /// Removes the first occurrence of a specific object from the collection.
    /// </summary>
    /// <param name="item">The object to remove from the collection.</param>
    public bool Remove(T item)
    {
        CheckReentrancy();
        var index = _items.IndexOf(item);
        if (index < 0) return false;

        _items.RemoveAt(index);
        OnPropertyChanged(CountString);
        OnPropertyChanged(IndexerName);
        OnCollectionChanged(NotifyCollectionChangedAction.Remove, item, index);
        return true;
    }

    /// <summary>
    /// Removes the first occurrence of a specific object from the collection.
    /// </summary>
    void IList.Remove(object? value) => Remove((T)value!);

    /// <summary>
    /// Removes the element at the specified index of the collection.
    /// </summary>
    /// <param name="index">The zero-based index of the element to remove.</param>
    public void RemoveAt(int index)
    {
        CheckReentrancy();
        var item = _items[index];
        _items.RemoveAt(index);
        OnPropertyChanged(CountString);
        OnPropertyChanged(IndexerName);
        OnCollectionChanged(NotifyCollectionChangedAction.Remove, item, index);
    }

    /// <summary>
    /// Adds the elements of the specified collection to the end of the ObservableCollection.
    /// </summary>
    /// <param name="collection">The collection whose elements should be added.</param>
    public void AddRange(IEnumerable<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        CheckReentrancy();

        var startIndex = _items.Count;
        var items = collection.ToList();
        if (items.Count == 0) return;

        _items.AddRange(items);
        OnPropertyChanged(CountString);
        OnPropertyChanged(IndexerName);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add,
            items,
            startIndex));
    }

    /// <summary>
    /// Removes a range of elements from the ObservableCollection.
    /// </summary>
    /// <param name="index">The zero-based starting index of the range of elements to remove.</param>
    /// <param name="count">The number of elements to remove.</param>
    public void RemoveRange(int index, int count)
    {
        if (count == 0) return;
        CheckReentrancy();

        var items = _items.GetRange(index, count);
        _items.RemoveRange(index, count);
        OnPropertyChanged(CountString);
        OnPropertyChanged(IndexerName);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Remove,
            items,
            index));
    }

    /// <summary>
    /// Replaces all items in the collection with the items from the specified collection.
    /// </summary>
    /// <param name="collection">The new items.</param>
    public void ReplaceAll(IEnumerable<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        CheckReentrancy();

        _items.Clear();
        _items.AddRange(collection);
        OnPropertyChanged(CountString);
        OnPropertyChanged(IndexerName);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    /// <param name="propertyName">The name of the property that changed.</param>
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Raises the CollectionChanged event with the provided arguments.
    /// </summary>
    /// <param name="e">Arguments of the event being raised.</param>
    protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        var handler = CollectionChanged;
        if (handler != null)
        {
            using (BlockReentrancy())
            {
                handler(this, e);
            }
        }
    }

    private void OnCollectionChanged(NotifyCollectionChangedAction action, T item, int index)
    {
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(action, item, index));
    }

    private void OnCollectionChanged(NotifyCollectionChangedAction action, T newItem, T oldItem, int index)
    {
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(action, newItem, oldItem, index));
    }

    private void OnCollectionChanged(NotifyCollectionChangedAction action, T item, int newIndex, int oldIndex)
    {
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(action, item, newIndex, oldIndex));
    }

    /// <summary>
    /// Disallows reentrant attempts to change this collection.
    /// </summary>
    /// <returns>An IDisposable object that can be used to dispose of the object.</returns>
    protected IDisposable BlockReentrancy()
    {
        _monitor ??= new SimpleMonitor();
        _monitor.Enter();
        return _monitor;
    }

    /// <summary>
    /// Checks for reentrant attempts to change this collection.
    /// </summary>
    protected void CheckReentrancy()
    {
        if (_monitor != null && _monitor.Busy && CollectionChanged != null)
        {
            var invocationCount = CollectionChanged.GetInvocationList().Length;
            if (invocationCount > 1)
            {
                throw new InvalidOperationException("Cannot change ObservableCollection during a CollectionChanged event.");
            }
        }
    }

    private sealed class SimpleMonitor : IDisposable
    {
        private int _busyCount;

        public bool Busy => _busyCount > 0;

        public void Enter() => ++_busyCount;

        public void Dispose() => --_busyCount;
    }
}

/// <summary>
/// Represents a read-only wrapper around an ObservableCollection.
/// </summary>
/// <typeparam name="T">The type of elements in the collection.</typeparam>
public sealed class ReadOnlyObservableCollection<T> : IReadOnlyList<T>, INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly ObservableCollection<T> _collection;

    /// <summary>
    /// Occurs when the collection changes.
    /// </summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReadOnlyObservableCollection{T}"/> class.
    /// </summary>
    /// <param name="collection">The ObservableCollection to wrap.</param>
    public ReadOnlyObservableCollection(ObservableCollection<T> collection)
    {
        _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        _collection.CollectionChanged += (s, e) => CollectionChanged?.Invoke(this, e);
        _collection.PropertyChanged += (s, e) => PropertyChanged?.Invoke(this, e);
    }

    /// <summary>
    /// Gets the number of elements contained in the collection.
    /// </summary>
    public int Count => _collection.Count;

    /// <summary>
    /// Gets the element at the specified index.
    /// </summary>
    public T this[int index] => _collection[index];

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    public IEnumerator<T> GetEnumerator() => _collection.GetEnumerator();

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Determines whether the collection contains a specific value.
    /// </summary>
    public bool Contains(T item) => _collection.Contains(item);

    /// <summary>
    /// Determines the index of a specific item in the collection.
    /// </summary>
    public int IndexOf(T item) => _collection.IndexOf(item);

    /// <summary>
    /// Copies the elements of the collection to an array.
    /// </summary>
    public void CopyTo(T[] array, int arrayIndex) => _collection.CopyTo(array, arrayIndex);
}
