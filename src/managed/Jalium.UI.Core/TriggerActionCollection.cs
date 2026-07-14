using System.Collections;

namespace Jalium.UI;

/// <summary>Represents the ordered collection of actions owned by an <see cref="EventTrigger"/>.</summary>
public sealed class TriggerActionCollection : IList<TriggerAction>, IList
{
    private readonly List<TriggerAction> _items;
    private readonly TriggerBase? _owner;
    private bool _isSealed;

    /// <summary>Initializes an empty action collection.</summary>
    public TriggerActionCollection()
        : this(initialSize: 0)
    {
    }

    /// <summary>Initializes an action collection with the requested storage capacity.</summary>
    public TriggerActionCollection(int initialSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialSize);
        _items = new List<TriggerAction>(initialSize);
    }

    internal TriggerActionCollection(TriggerBase owner)
        : this()
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>Gets the number of actions in the collection.</summary>
    public int Count => _items.Count;

    /// <summary>Gets whether the collection can no longer be changed.</summary>
    public bool IsReadOnly => _isSealed;

    /// <summary>Gets or replaces the action at the specified index.</summary>
    public TriggerAction this[int index]
    {
        get => _items[index];
        set
        {
            CheckSealed();
            ArgumentNullException.ThrowIfNull(value);
            TriggerAction previous = _items[index];
            if (ReferenceEquals(previous, value))
                return;

            value.AttachOwner(_owner);
            try
            {
                _items[index] = value;
            }
            catch
            {
                value.DetachOwner(_owner);
                throw;
            }

            previous.DetachOwner(_owner);
        }
    }

    internal TriggerBase? Owner => _owner;

    /// <summary>Adds an action to the collection.</summary>
    public void Add(TriggerAction value) => Insert(Count, value);

    /// <summary>Removes all actions from the collection.</summary>
    public void Clear()
    {
        CheckSealed();
        TriggerAction[] previous = _items.ToArray();
        _items.Clear();
        foreach (TriggerAction action in previous)
            action.DetachOwner(_owner);
    }

    /// <summary>Determines whether the collection contains an action.</summary>
    public bool Contains(TriggerAction value) => _items.Contains(value);

    /// <summary>Copies the actions to an array.</summary>
    public void CopyTo(TriggerAction[] array, int index) => _items.CopyTo(array, index);

    /// <summary>Returns an enumerator over the actions.</summary>
    public IEnumerator<TriggerAction> GetEnumerator() => _items.GetEnumerator();

    /// <summary>Returns the zero-based index of an action.</summary>
    public int IndexOf(TriggerAction value) => _items.IndexOf(value);

    /// <summary>Inserts an action at the specified index.</summary>
    public void Insert(int index, TriggerAction value)
    {
        CheckSealed();
        ArgumentNullException.ThrowIfNull(value);
        value.AttachOwner(_owner);
        try
        {
            _items.Insert(index, value);
        }
        catch
        {
            value.DetachOwner(_owner);
            throw;
        }
    }

    /// <summary>Removes the first occurrence of an action.</summary>
    public bool Remove(TriggerAction value)
    {
        CheckSealed();
        int index = _items.IndexOf(value);
        if (index < 0)
            return false;

        RemoveAt(index);
        return true;
    }

    /// <summary>Removes the action at the specified index.</summary>
    public void RemoveAt(int index)
    {
        CheckSealed();
        TriggerAction previous = _items[index];
        _items.RemoveAt(index);
        previous.DetachOwner(_owner);
    }

    internal void Seal(TriggerBase containingTrigger)
    {
        ArgumentNullException.ThrowIfNull(containingTrigger);
        if (_isSealed)
            return;

        if (_owner is not null && !ReferenceEquals(_owner, containingTrigger))
            throw new InvalidOperationException("A TriggerActionCollection cannot be sealed by a different trigger.");

        foreach (TriggerAction action in _items)
            action.Seal(containingTrigger);

        _isSealed = true;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    bool IList.IsFixedSize => false;
    bool IList.IsReadOnly => IsReadOnly;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => ((ICollection)_items).SyncRoot;

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = ValidateValue(value);
    }

    int IList.Add(object? value)
    {
        Add(ValidateValue(value));
        return Count - 1;
    }

    bool IList.Contains(object? value) => value is TriggerAction action && Contains(action);

    int IList.IndexOf(object? value) => value is TriggerAction action ? IndexOf(action) : -1;

    void IList.Insert(int index, object? value) => Insert(index, ValidateValue(value));

    void IList.Remove(object? value)
    {
        if (value is TriggerAction action)
            Remove(action);
    }

    void ICollection.CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);

    private static TriggerAction ValidateValue(object? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value as TriggerAction
            ?? throw new ArgumentException($"Value must be a {nameof(TriggerAction)}.", nameof(value));
    }

    private void CheckSealed()
    {
        if (_isSealed)
            throw new InvalidOperationException("A sealed TriggerActionCollection cannot be changed.");
    }
}
