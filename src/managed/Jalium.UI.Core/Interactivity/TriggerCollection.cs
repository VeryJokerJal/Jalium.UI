using System.Collections.ObjectModel;

namespace Jalium.UI.Interactivity;

/// <summary>
/// Represents a collection of triggers with a shared AssociatedObject.
/// </summary>
public sealed class TriggerCollection : Collection<TriggerBase>
{
    private DependencyObject? _associatedObject;

    /// <summary>
    /// Gets the DependencyObject to which this collection of triggers is attached.
    /// </summary>
    public DependencyObject? AssociatedObject => _associatedObject;

    /// <summary>
    /// Attaches to the specified object.
    /// </summary>
    /// <param name="dependencyObject">The DependencyObject to attach to.</param>
    internal void Attach(DependencyObject dependencyObject)
    {
        if (_associatedObject == dependencyObject)
            return;

        if (_associatedObject != null)
            throw new InvalidOperationException("Cannot host TriggerCollection multiple times.");

        _associatedObject = dependencyObject;

        foreach (var trigger in this)
        {
            trigger.Attach(dependencyObject);
        }
    }

    /// <summary>
    /// Detaches this instance from its associated object.
    /// </summary>
    internal void Detach()
    {
        foreach (var trigger in this)
        {
            trigger.Detach();
        }

        _associatedObject = null;
    }

    /// <summary>
    /// Called when an item is inserted into the collection.
    /// </summary>
    protected override void InsertItem(int index, TriggerBase item)
    {
        if (_associatedObject != null)
        {
            item.Attach(_associatedObject);
        }

        base.InsertItem(index, item);
    }

    /// <summary>
    /// Called when an item is removed from the collection.
    /// </summary>
    protected override void RemoveItem(int index)
    {
        var trigger = this[index];
        trigger.Detach();
        base.RemoveItem(index);
    }

    /// <summary>
    /// Called when the collection is cleared.
    /// </summary>
    protected override void ClearItems()
    {
        foreach (var trigger in this)
        {
            trigger.Detach();
        }

        base.ClearItems();
    }

    /// <summary>
    /// Called when an item is set in the collection.
    /// </summary>
    protected override void SetItem(int index, TriggerBase item)
    {
        var oldTrigger = this[index];
        oldTrigger.Detach();

        if (_associatedObject != null)
        {
            item.Attach(_associatedObject);
        }

        base.SetItem(index, item);
    }
}
