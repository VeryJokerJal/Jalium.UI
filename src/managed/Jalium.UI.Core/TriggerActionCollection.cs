using System.Collections.ObjectModel;

namespace Jalium.UI;

/// <summary>Represents the ordered collection of actions owned by an EventTrigger.</summary>
public sealed class TriggerActionCollection : Collection<TriggerAction>
{
    private readonly TriggerBase? _owner;
    private bool _isSealed;

    /// <summary>Initializes an empty action collection.</summary>
    public TriggerActionCollection()
        : base(new List<TriggerAction>())
    {
    }

    /// <summary>Initializes an action collection with the requested storage capacity.</summary>
    public TriggerActionCollection(int initialSize)
        : base(new List<TriggerAction>(initialSize >= 0
            ? initialSize
            : throw new ArgumentOutOfRangeException(nameof(initialSize))))
    {
    }

    internal TriggerActionCollection(TriggerBase owner)
        : this()
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>Gets whether the collection can no longer be changed.</summary>
    public bool IsReadOnly => _isSealed;

    internal TriggerBase? Owner => _owner;

    internal void Seal(TriggerBase containingTrigger)
    {
        ArgumentNullException.ThrowIfNull(containingTrigger);
        if (_isSealed)
        {
            return;
        }

        if (_owner is not null && !ReferenceEquals(_owner, containingTrigger))
        {
            throw new InvalidOperationException("A TriggerActionCollection cannot be sealed by a different trigger.");
        }

        foreach (TriggerAction action in this)
        {
            action.Seal(containingTrigger);
        }

        _isSealed = true;
    }

    protected override void InsertItem(int index, TriggerAction item)
    {
        CheckSealed();
        ArgumentNullException.ThrowIfNull(item);
        item.AttachOwner(_owner);
        try
        {
            base.InsertItem(index, item);
        }
        catch
        {
            item.DetachOwner(_owner);
            throw;
        }
    }

    protected override void SetItem(int index, TriggerAction item)
    {
        CheckSealed();
        ArgumentNullException.ThrowIfNull(item);
        TriggerAction previous = this[index];
        if (ReferenceEquals(previous, item))
        {
            return;
        }

        item.AttachOwner(_owner);
        try
        {
            base.SetItem(index, item);
        }
        catch
        {
            item.DetachOwner(_owner);
            throw;
        }

        previous.DetachOwner(_owner);
    }

    protected override void RemoveItem(int index)
    {
        CheckSealed();
        TriggerAction previous = this[index];
        base.RemoveItem(index);
        previous.DetachOwner(_owner);
    }

    protected override void ClearItems()
    {
        CheckSealed();
        TriggerAction[] previous = this.ToArray();
        base.ClearItems();
        foreach (TriggerAction action in previous)
        {
            action.DetachOwner(_owner);
        }
    }

    private void CheckSealed()
    {
        if (_isSealed)
        {
            throw new InvalidOperationException("A sealed TriggerActionCollection cannot be changed.");
        }
    }
}
