using System.Collections.ObjectModel;

namespace Jalium.UI;

/// <summary>Represents the collection of triggers owned by a style or template.</summary>
public sealed class TriggerCollection : Collection<TriggerBase>
{
    private readonly FrameworkElement? _owner;

    internal TriggerCollection()
    {
    }

    internal TriggerCollection(FrameworkElement owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    protected override void InsertItem(int index, TriggerBase item)
    {
        ArgumentNullException.ThrowIfNull(item);
        base.InsertItem(index, item);

        if (_owner is not null)
        {
            try
            {
                item.Attach(_owner);
            }
            catch
            {
                base.RemoveItem(index);
                throw;
            }
        }
    }

    protected override void SetItem(int index, TriggerBase item)
    {
        ArgumentNullException.ThrowIfNull(item);
        TriggerBase previous = this[index];
        if (ReferenceEquals(previous, item))
        {
            return;
        }

        if (_owner is not null)
        {
            previous.Detach(_owner);
        }

        base.SetItem(index, item);
        try
        {
            if (_owner is not null)
            {
                item.Attach(_owner);
            }
        }
        catch
        {
            base.SetItem(index, previous);
            if (_owner is not null)
            {
                previous.Attach(_owner);
            }

            throw;
        }
    }

    protected override void RemoveItem(int index)
    {
        if (_owner is not null)
        {
            this[index].Detach(_owner);
        }

        base.RemoveItem(index);
    }

    protected override void ClearItems()
    {
        if (_owner is not null)
        {
            foreach (TriggerBase trigger in this)
            {
                trigger.Detach(_owner);
            }
        }

        base.ClearItems();
    }
}
