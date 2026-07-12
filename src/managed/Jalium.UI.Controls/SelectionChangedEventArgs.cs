using System.Collections;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents the method that handles a selection-changed routed event.
/// </summary>
public delegate void SelectionChangedEventHandler(object sender, SelectionChangedEventArgs e);

/// <summary>
/// Provides data for a selection-changed routed event.
/// </summary>
public class SelectionChangedEventArgs : RoutedEventArgs
{
    private readonly object?[] _removedItems;
    private readonly object?[] _addedItems;

    /// <summary>
    /// Initializes a new instance of the <see cref="SelectionChangedEventArgs"/> class.
    /// </summary>
    /// <param name="id">The routed event being raised.</param>
    /// <param name="removedItems">The items that were unselected.</param>
    /// <param name="addedItems">The items that were selected.</param>
    public SelectionChangedEventArgs(RoutedEvent id, IList removedItems, IList addedItems)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(removedItems);
        ArgumentNullException.ThrowIfNull(addedItems);

        RoutedEvent = id;
        _removedItems = CopyItems(removedItems);
        _addedItems = CopyItems(addedItems);
    }

    /// <summary>
    /// Gets the items that were unselected.
    /// </summary>
    public IList RemovedItems => _removedItems;

    /// <summary>
    /// Gets the items that were selected.
    /// </summary>
    public IList AddedItems => _addedItems;

    /// <inheritdoc />
    protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget)
    {
        switch (genericHandler)
        {
            case SelectionChangedEventHandler selectionHandler:
                selectionHandler(genericTarget, this);
                break;
            case EventHandler<SelectionChangedEventArgs> eventHandler:
                eventHandler(genericTarget, this);
                break;
            default:
                base.InvokeEventHandler(genericHandler, genericTarget);
                break;
        }
    }

    private static object?[] CopyItems(IList items)
    {
        var copy = new object?[items.Count];
        items.CopyTo(copy, 0);
        return copy;
    }
}
