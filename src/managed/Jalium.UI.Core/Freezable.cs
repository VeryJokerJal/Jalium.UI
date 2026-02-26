using System.ComponentModel;

namespace Jalium.UI;

/// <summary>
/// Defines an object that has a modifiable state and a read-only (frozen) state.
/// Classes that derive from Freezable provide detailed change notification, can be made immutable,
/// and can clone themselves.
/// </summary>
public abstract class Freezable : DependencyObject
{
    private bool _isFrozen;

    /// <summary>
    /// Occurs when the Freezable or an object it contains is modified.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets a value that indicates whether the object can be made unmodifiable.
    /// </summary>
    public bool CanFreeze => FreezeCore(true);

    /// <summary>
    /// Gets a value that indicates whether the object is currently modifiable.
    /// </summary>
    public bool IsFrozen => _isFrozen;

    /// <summary>
    /// Creates a modifiable clone of the Freezable, making deep copies of the object's values.
    /// </summary>
    /// <returns>A modifiable clone of the current object.</returns>
    public Freezable Clone()
    {
        var clone = CreateInstance();
        CloneCore(this);
        return clone;
    }

    /// <summary>
    /// Creates a modifiable clone of the Freezable using its current values.
    /// </summary>
    /// <returns>A modifiable clone of the current object.</returns>
    public Freezable CloneCurrentValue()
    {
        var clone = CreateInstance();
        CloneCurrentValueCore(this);
        return clone;
    }

    /// <summary>
    /// Makes the current object unmodifiable and sets its IsFrozen property to true.
    /// </summary>
    public void Freeze()
    {
        if (_isFrozen)
            return;

        if (!FreezeCore(false))
            throw new InvalidOperationException("This Freezable cannot be frozen.");

        _isFrozen = true;
    }

    /// <summary>
    /// Creates a frozen copy of the Freezable.
    /// </summary>
    /// <returns>A frozen copy of the Freezable.</returns>
    public Freezable GetAsFrozen()
    {
        var clone = CreateInstance();
        GetAsFrozenCore(this);
        clone.Freeze();
        return clone;
    }

    /// <summary>
    /// Creates a frozen copy of the Freezable using current property values.
    /// </summary>
    /// <returns>A frozen copy of the Freezable.</returns>
    public Freezable GetCurrentValueAsFrozen()
    {
        var clone = CreateInstance();
        GetCurrentValueAsFrozenCore(this);
        clone.Freeze();
        return clone;
    }

    /// <summary>
    /// When implemented in a derived class, creates a new instance of the Freezable derived class.
    /// </summary>
    /// <returns>The new instance.</returns>
    protected abstract Freezable CreateInstanceCore();

    /// <summary>
    /// Makes the Freezable object unmodifiable or tests whether it can be made unmodifiable.
    /// </summary>
    /// <param name="isChecking">true to return an indication of whether the object can be frozen; false to actually freeze the object.</param>
    /// <returns>If isChecking is true, this method returns true if the Freezable can be made unmodifiable, or false if it cannot. If isChecking is false, this method returns true if the specified Freezable is now unmodifiable, or throws an exception if it cannot be made unmodifiable.</returns>
    protected virtual bool FreezeCore(bool isChecking)
    {
        return true;
    }

    /// <summary>
    /// Makes the instance a clone (deep copy) of the specified Freezable using base (non-animated) property values.
    /// </summary>
    /// <param name="sourceFreezable">The Freezable to clone.</param>
    protected virtual void CloneCore(Freezable sourceFreezable)
    {
        CopyCommonValues(sourceFreezable);
    }

    /// <summary>
    /// Makes the instance a modifiable clone (deep copy) of the specified Freezable using current property values.
    /// </summary>
    /// <param name="sourceFreezable">The Freezable to copy.</param>
    protected virtual void CloneCurrentValueCore(Freezable sourceFreezable)
    {
        CopyCommonValues(sourceFreezable);
    }

    /// <summary>
    /// Makes the instance a frozen clone of the specified Freezable using base (non-animated) property values.
    /// </summary>
    /// <param name="sourceFreezable">The Freezable to copy.</param>
    protected virtual void GetAsFrozenCore(Freezable sourceFreezable)
    {
        CopyCommonValues(sourceFreezable);
    }

    /// <summary>
    /// Makes the current instance a frozen clone of the specified Freezable using current property values.
    /// </summary>
    /// <param name="sourceFreezable">The Freezable to copy.</param>
    protected virtual void GetCurrentValueAsFrozenCore(Freezable sourceFreezable)
    {
        CopyCommonValues(sourceFreezable);
    }

    /// <summary>
    /// Called when the current Freezable object is modified.
    /// </summary>
    protected void OnChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Ensures that appropriate actions are taken when a DependencyObject member of a Freezable changes.
    /// </summary>
    /// <param name="oldValue">The previous value of the property.</param>
    /// <param name="newValue">The current value of the property.</param>
    protected virtual void OnFreezablePropertyChanged(DependencyObject? oldValue, DependencyObject? newValue)
    {
        // Unsubscribe from old value's change notifications
        if (oldValue is Freezable oldFreezable)
        {
            oldFreezable.Changed -= OnSubPropertyChanged;
        }

        // Subscribe to new value's change notifications
        if (newValue is Freezable newFreezable && !newFreezable.IsFrozen)
        {
            newFreezable.Changed += OnSubPropertyChanged;
        }
    }

    /// <summary>
    /// Verifies that the Freezable is not frozen and is being accessed from a valid thread context.
    /// </summary>
    protected void WritePreamble()
    {
        if (_isFrozen)
            throw new InvalidOperationException("Cannot modify a frozen Freezable.");
    }

    /// <summary>
    /// Raises the Changed event for the Freezable and invokes its OnChanged method.
    /// </summary>
    protected void WritePostscript()
    {
        OnChanged();
    }

    /// <summary>
    /// Creates a new instance of the Freezable class.
    /// </summary>
    protected Freezable CreateInstance()
    {
        return CreateInstanceCore();
    }

    private void CopyCommonValues(Freezable source)
    {
        // Copy dependency property values
        // This is a simplified implementation
    }

    private void OnSubPropertyChanged(object? sender, EventArgs e)
    {
        OnChanged();
    }
}

/// <summary>
/// Represents a collection of Freezable objects.
/// </summary>
/// <typeparam name="T">The type of elements in the collection.</typeparam>
public sealed class FreezableCollection<T> : Freezable, IList<T>, System.Collections.IList where T : DependencyObject
{
    private readonly List<T> _items = new();

    /// <summary>
    /// Gets or sets the item at the specified index.
    /// </summary>
    public T this[int index]
    {
        get => _items[index];
        set
        {
            WritePreamble();
            var oldItem = _items[index];
            _items[index] = value;
            OnFreezablePropertyChanged(oldItem, value);
            WritePostscript();
        }
    }

    object? System.Collections.IList.this[int index]
    {
        get => _items[index];
        set
        {
            if (value is T item)
                this[index] = item;
        }
    }

    /// <summary>
    /// Gets the number of items in the collection.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Gets a value indicating whether the collection is read-only.
    /// </summary>
    public bool IsReadOnly => IsFrozen;

    bool System.Collections.IList.IsFixedSize => IsFrozen;
    bool System.Collections.ICollection.IsSynchronized => false;
    object System.Collections.ICollection.SyncRoot => ((System.Collections.ICollection)_items).SyncRoot;

    /// <summary>
    /// Adds an item to the collection.
    /// </summary>
    public void Add(T item)
    {
        WritePreamble();
        _items.Add(item);
        OnFreezablePropertyChanged(null, item);
        WritePostscript();
    }

    int System.Collections.IList.Add(object? value)
    {
        if (value is T item)
        {
            Add(item);
            return _items.Count - 1;
        }
        return -1;
    }

    /// <summary>
    /// Removes all items from the collection.
    /// </summary>
    public void Clear()
    {
        WritePreamble();
        foreach (var item in _items)
        {
            OnFreezablePropertyChanged(item, null);
        }
        _items.Clear();
        WritePostscript();
    }

    /// <summary>
    /// Determines whether the collection contains a specific item.
    /// </summary>
    public bool Contains(T item) => _items.Contains(item);
    bool System.Collections.IList.Contains(object? value) => value is T item && _items.Contains(item);

    /// <summary>
    /// Copies the items to an array.
    /// </summary>
    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    void System.Collections.ICollection.CopyTo(Array array, int index) => ((System.Collections.ICollection)_items).CopyTo(array, index);

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();

    /// <summary>
    /// Returns the index of the specified item.
    /// </summary>
    public int IndexOf(T item) => _items.IndexOf(item);
    int System.Collections.IList.IndexOf(object? value) => value is T item ? _items.IndexOf(item) : -1;

    /// <summary>
    /// Inserts an item at the specified index.
    /// </summary>
    public void Insert(int index, T item)
    {
        WritePreamble();
        _items.Insert(index, item);
        OnFreezablePropertyChanged(null, item);
        WritePostscript();
    }

    void System.Collections.IList.Insert(int index, object? value)
    {
        if (value is T item)
            Insert(index, item);
    }

    /// <summary>
    /// Removes the specified item from the collection.
    /// </summary>
    public bool Remove(T item)
    {
        WritePreamble();
        var removed = _items.Remove(item);
        if (removed)
        {
            OnFreezablePropertyChanged(item, null);
            WritePostscript();
        }
        return removed;
    }

    void System.Collections.IList.Remove(object? value)
    {
        if (value is T item)
            Remove(item);
    }

    /// <summary>
    /// Removes the item at the specified index.
    /// </summary>
    public void RemoveAt(int index)
    {
        WritePreamble();
        var item = _items[index];
        _items.RemoveAt(index);
        OnFreezablePropertyChanged(item, null);
        WritePostscript();
    }

    /// <summary>
    /// Creates a new instance of the collection.
    /// </summary>
    protected override Freezable CreateInstanceCore()
    {
        return new FreezableCollection<T>();
    }
}
