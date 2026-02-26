using System.Collections.ObjectModel;

namespace Jalium.UI.Interactivity;

/// <summary>
/// Represents a collection of actions with a shared AssociatedObject.
/// </summary>
public sealed class TriggerActionCollection : Collection<TriggerAction>
{
    private DependencyObject? _associatedObject;

    /// <summary>
    /// Gets the DependencyObject to which this collection of actions is attached.
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
            throw new InvalidOperationException("Cannot host TriggerActionCollection multiple times.");

        _associatedObject = dependencyObject;

        foreach (var action in this)
        {
            action.Attach(dependencyObject);
        }
    }

    /// <summary>
    /// Detaches this instance from its associated object.
    /// </summary>
    internal void Detach()
    {
        foreach (var action in this)
        {
            action.Detach();
        }

        _associatedObject = null;
    }

    /// <summary>
    /// Called when an item is inserted into the collection.
    /// </summary>
    protected override void InsertItem(int index, TriggerAction item)
    {
        if (_associatedObject != null)
        {
            item.Attach(_associatedObject);
        }

        item.IsHosted = true;
        base.InsertItem(index, item);
    }

    /// <summary>
    /// Called when an item is removed from the collection.
    /// </summary>
    protected override void RemoveItem(int index)
    {
        var action = this[index];
        action.Detach();
        action.IsHosted = false;
        base.RemoveItem(index);
    }

    /// <summary>
    /// Called when the collection is cleared.
    /// </summary>
    protected override void ClearItems()
    {
        foreach (var action in this)
        {
            action.Detach();
            action.IsHosted = false;
        }

        base.ClearItems();
    }

    /// <summary>
    /// Called when an item is set in the collection.
    /// </summary>
    protected override void SetItem(int index, TriggerAction item)
    {
        var oldAction = this[index];
        oldAction.Detach();
        oldAction.IsHosted = false;

        if (_associatedObject != null)
        {
            item.Attach(_associatedObject);
        }

        item.IsHosted = true;
        base.SetItem(index, item);
    }
}
