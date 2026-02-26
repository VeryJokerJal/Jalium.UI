using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Jalium.UI.Data;

/// <summary>
/// Defines the direction of a sort operation.
/// </summary>
public enum ListSortDirection
{
    /// <summary>
    /// Sort in ascending order.
    /// </summary>
    Ascending,

    /// <summary>
    /// Sort in descending order.
    /// </summary>
    Descending
}

/// <summary>
/// Defines a property and direction to sort a list by.
/// </summary>
public readonly struct SortDescription : IEquatable<SortDescription>
{
    /// <summary>
    /// Initializes a new instance of the SortDescription structure.
    /// </summary>
    /// <param name="propertyName">The name of the property to sort by.</param>
    /// <param name="direction">The direction of the sort.</param>
    public SortDescription(string propertyName, ListSortDirection direction)
    {
        PropertyName = propertyName;
        Direction = direction;
        IsSealed = true;
    }

    /// <summary>
    /// Gets the property name being used as the sorting criteria.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// Gets the direction of the sort operation.
    /// </summary>
    public ListSortDirection Direction { get; }

    /// <summary>
    /// Gets a value indicating whether this SortDescription is in use.
    /// </summary>
    public bool IsSealed { get; }

    /// <summary>
    /// Determines whether two SortDescription objects are equal.
    /// </summary>
    public static bool operator ==(SortDescription left, SortDescription right) => left.Equals(right);

    /// <summary>
    /// Determines whether two SortDescription objects are not equal.
    /// </summary>
    public static bool operator !=(SortDescription left, SortDescription right) => !left.Equals(right);

    /// <summary>
    /// Determines whether the specified SortDescription is equal to the current SortDescription.
    /// </summary>
    public bool Equals(SortDescription other)
    {
        return PropertyName == other.PropertyName && Direction == other.Direction;
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current object.
    /// </summary>
    public override bool Equals(object? obj)
    {
        return obj is SortDescription other && Equals(other);
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(PropertyName, Direction);
    }
}

/// <summary>
/// Represents a collection of SortDescription objects.
/// </summary>
public sealed class SortDescriptionCollection : IList<SortDescription>, IList, INotifyCollectionChanged
{
    private readonly List<SortDescription> _sortDescriptions = new();

    /// <summary>
    /// Gets an empty, non-modifiable instance of SortDescriptionCollection.
    /// </summary>
    public static readonly SortDescriptionCollection Empty = new(true);

    private readonly bool _isReadOnly;

    /// <summary>
    /// Initializes a new instance of the SortDescriptionCollection class.
    /// </summary>
    public SortDescriptionCollection()
    {
        _isReadOnly = false;
    }

    private SortDescriptionCollection(bool isReadOnly)
    {
        _isReadOnly = isReadOnly;
    }

    /// <summary>
    /// Occurs when the collection changes.
    /// </summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>
    /// Gets or sets the sort description at the specified index.
    /// </summary>
    public SortDescription this[int index]
    {
        get => _sortDescriptions[index];
        set
        {
            CheckReadOnly();
            var oldItem = _sortDescriptions[index];
            _sortDescriptions[index] = value;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, value, oldItem, index));
        }
    }

    object? IList.this[int index]
    {
        get => _sortDescriptions[index];
        set
        {
            CheckReadOnly();
            if (value is SortDescription sd)
                this[index] = sd;
        }
    }

    /// <summary>
    /// Gets the number of sort descriptions in the collection.
    /// </summary>
    public int Count => _sortDescriptions.Count;

    /// <summary>
    /// Gets a value indicating whether the collection is read-only.
    /// </summary>
    public bool IsReadOnly => _isReadOnly;

    bool IList.IsFixedSize => _isReadOnly;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => ((ICollection)_sortDescriptions).SyncRoot;

    /// <summary>
    /// Adds a sort description to the collection.
    /// </summary>
    public void Add(SortDescription item)
    {
        CheckReadOnly();
        _sortDescriptions.Add(item);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, _sortDescriptions.Count - 1));
    }

    int IList.Add(object? value)
    {
        CheckReadOnly();
        if (value is SortDescription sd)
        {
            Add(sd);
            return _sortDescriptions.Count - 1;
        }
        return -1;
    }

    /// <summary>
    /// Removes all sort descriptions from the collection.
    /// </summary>
    public void Clear()
    {
        CheckReadOnly();
        _sortDescriptions.Clear();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Determines whether the collection contains a specific sort description.
    /// </summary>
    public bool Contains(SortDescription item) => _sortDescriptions.Contains(item);
    bool IList.Contains(object? value) => value is SortDescription sd && _sortDescriptions.Contains(sd);

    /// <summary>
    /// Copies the sort descriptions to an array.
    /// </summary>
    public void CopyTo(SortDescription[] array, int arrayIndex) => _sortDescriptions.CopyTo(array, arrayIndex);
    void ICollection.CopyTo(Array array, int index) => ((ICollection)_sortDescriptions).CopyTo(array, index);

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    public IEnumerator<SortDescription> GetEnumerator() => _sortDescriptions.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _sortDescriptions.GetEnumerator();

    /// <summary>
    /// Returns the index of the specified sort description.
    /// </summary>
    public int IndexOf(SortDescription item) => _sortDescriptions.IndexOf(item);
    int IList.IndexOf(object? value) => value is SortDescription sd ? _sortDescriptions.IndexOf(sd) : -1;

    /// <summary>
    /// Inserts a sort description at the specified index.
    /// </summary>
    public void Insert(int index, SortDescription item)
    {
        CheckReadOnly();
        _sortDescriptions.Insert(index, item);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
    }

    void IList.Insert(int index, object? value)
    {
        CheckReadOnly();
        if (value is SortDescription sd)
            Insert(index, sd);
    }

    /// <summary>
    /// Removes the specified sort description from the collection.
    /// </summary>
    public bool Remove(SortDescription item)
    {
        CheckReadOnly();
        var index = _sortDescriptions.IndexOf(item);
        if (index >= 0)
        {
            _sortDescriptions.RemoveAt(index);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
            return true;
        }
        return false;
    }

    void IList.Remove(object? value)
    {
        CheckReadOnly();
        if (value is SortDescription sd)
            Remove(sd);
    }

    /// <summary>
    /// Removes the sort description at the specified index.
    /// </summary>
    public void RemoveAt(int index)
    {
        CheckReadOnly();
        var item = _sortDescriptions[index];
        _sortDescriptions.RemoveAt(index);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
    }

    /// <summary>
    /// Raises the CollectionChanged event.
    /// </summary>
    protected void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        CollectionChanged?.Invoke(this, e);
    }

    private void CheckReadOnly()
    {
        if (_isReadOnly)
            throw new NotSupportedException("Collection is read-only.");
    }
}
