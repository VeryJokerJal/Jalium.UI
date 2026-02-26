using System.Collections;
using System.Collections.Specialized;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Controls;

/// <summary>
/// Generates UI containers for items on behalf of a host such as ItemsControl.
/// Maintains the association between data items and their UI containers, and supports
/// container recycling for virtualization.
/// </summary>
public sealed class ItemContainerGenerator : IRecyclingItemContainerGenerator
{
    private readonly ItemsControl _host;
    private readonly List<ItemContainerMap> _realizedItems = new();
    private readonly Queue<DependencyObject> _recycleQueue = new();
    private GeneratorStatus _status = GeneratorStatus.NotStarted;

    // Generator state during StartAt...GenerateNext session
    private int _generatorCurrentIndex;
    private GeneratorDirection _generatorDirection;
    private bool _isGenerating;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemContainerGenerator"/> class.
    /// </summary>
    internal ItemContainerGenerator(ItemsControl host)
    {
        _host = host;
    }

    #region Public Properties

    /// <summary>
    /// Gets the status of the generator.
    /// </summary>
    public GeneratorStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                StatusChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Gets the number of items in the host's items collection.
    /// </summary>
    public int ItemCount
    {
        get
        {
            var source = _host.ItemsSource ?? (IEnumerable)_host.Items;
            if (source is ICollection collection)
                return collection.Count;

            int count = 0;
            foreach (var _ in source)
                count++;
            return count;
        }
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the Items collection associated with this generator has changed.
    /// </summary>
    public event ItemsChangedEventHandler? ItemsChanged;

    /// <summary>
    /// Occurs when the status of the generator changes.
    /// </summary>
    public event EventHandler? StatusChanged;

    #endregion

    #region Container-Item Mapping

    /// <summary>
    /// Returns the element that corresponds to the item at the given index.
    /// </summary>
    public DependencyObject? ContainerFromIndex(int index)
    {
        foreach (var map in _realizedItems)
        {
            if (map.ItemIndex == index)
                return map.Container;
        }
        return null;
    }

    /// <summary>
    /// Returns the container for the specified item.
    /// </summary>
    public DependencyObject? ContainerFromItem(object item)
    {
        foreach (var map in _realizedItems)
        {
            if (Equals(map.Item, item))
                return map.Container;
        }
        return null;
    }

    /// <summary>
    /// Returns the item that corresponds to the specified generated container.
    /// </summary>
    public object? ItemFromContainer(DependencyObject container)
    {
        foreach (var map in _realizedItems)
        {
            if (ReferenceEquals(map.Container, container))
                return map.Item;
        }
        return DependencyProperty.UnsetValue;
    }

    /// <summary>
    /// Returns the index to an item that corresponds to the specified generated container.
    /// </summary>
    public int IndexFromContainer(DependencyObject container)
    {
        foreach (var map in _realizedItems)
        {
            if (ReferenceEquals(map.Container, container))
                return map.ItemIndex;
        }
        return -1;
    }

    #endregion

    #region IItemContainerGenerator Implementation

    /// <inheritdoc />
    public GeneratorPosition GeneratorPositionFromIndex(int itemIndex)
    {
        // Find if this item is already realized
        for (int i = 0; i < _realizedItems.Count; i++)
        {
            if (_realizedItems[i].ItemIndex == itemIndex)
                return new GeneratorPosition(i, 0);
        }

        // Not realized - find the nearest realized item
        if (_realizedItems.Count == 0)
            return new GeneratorPosition(-1, itemIndex + 1);

        // Find closest realized item before this index
        int closestBefore = -1;
        for (int i = 0; i < _realizedItems.Count; i++)
        {
            if (_realizedItems[i].ItemIndex < itemIndex)
                closestBefore = i;
        }

        if (closestBefore >= 0)
            return new GeneratorPosition(closestBefore, itemIndex - _realizedItems[closestBefore].ItemIndex);

        return new GeneratorPosition(-1, itemIndex + 1);
    }

    /// <inheritdoc />
    public int IndexFromGeneratorPosition(GeneratorPosition position)
    {
        if (position.Index == -1)
            return position.Offset - 1;

        if (position.Index >= 0 && position.Index < _realizedItems.Count)
            return _realizedItems[position.Index].ItemIndex + position.Offset;

        return -1;
    }

    /// <inheritdoc />
    public IDisposable StartAt(GeneratorPosition position, GeneratorDirection direction)
    {
        return StartAt(position, direction, false);
    }

    /// <inheritdoc />
    public IDisposable StartAt(GeneratorPosition position, GeneratorDirection direction, bool allowStartAtRealizedItem)
    {
        _generatorCurrentIndex = IndexFromGeneratorPosition(position);
        _generatorDirection = direction;
        _isGenerating = true;

        Status = GeneratorStatus.GeneratingContainers;

        return new GeneratorSession(this);
    }

    /// <inheritdoc />
    public DependencyObject? GenerateNext(out bool isNewlyRealized)
    {
        isNewlyRealized = false;

        if (!_isGenerating || _generatorCurrentIndex < 0 || _generatorCurrentIndex >= ItemCount)
            return null;

        // Get the item at current index
        var item = GetItemAt(_generatorCurrentIndex);
        if (item == null)
            return null;

        // Check if already realized
        var existing = ContainerFromIndex(_generatorCurrentIndex);
        if (existing != null)
        {
            isNewlyRealized = false;
            AdvanceIndex();
            return existing;
        }

        // Try to get a recycled container
        DependencyObject container;
        if (_recycleQueue.TryDequeue(out var recycled))
        {
            container = recycled;
            isNewlyRealized = false;
        }
        else
        {
            // Create new container
            if (_host.IsItemItsOwnContainerPublic(item))
            {
                container = (DependencyObject)item;
            }
            else
            {
                container = _host.GetContainerForItemPublic(item);
            }
            isNewlyRealized = true;
        }

        // Track the mapping
        _realizedItems.Add(new ItemContainerMap(_generatorCurrentIndex, item, container));

        AdvanceIndex();
        return container;
    }

    /// <inheritdoc />
    public void PrepareItemContainer(DependencyObject container)
    {
        // Find the item for this container
        var item = ItemFromContainer(container);
        if (item != null && item != DependencyProperty.UnsetValue && container is FrameworkElement element)
        {
            _host.PrepareContainerForItemInternal(element, item);
        }
    }

    /// <inheritdoc />
    public void Remove(GeneratorPosition position, int count)
    {
        int startIndex = IndexFromGeneratorPosition(position);
        for (int i = count - 1; i >= 0; i--)
        {
            int removeAt = -1;
            for (int j = 0; j < _realizedItems.Count; j++)
            {
                if (_realizedItems[j].ItemIndex == startIndex + i)
                {
                    removeAt = j;
                    break;
                }
            }
            if (removeAt >= 0)
            {
                _realizedItems.RemoveAt(removeAt);
            }
        }
    }

    /// <inheritdoc />
    public void RemoveAll()
    {
        _realizedItems.Clear();
        _recycleQueue.Clear();
        Status = GeneratorStatus.NotStarted;
    }

    /// <inheritdoc />
    public void Recycle(GeneratorPosition position, int count)
    {
        int startIndex = IndexFromGeneratorPosition(position);
        for (int i = count - 1; i >= 0; i--)
        {
            int removeAt = -1;
            for (int j = 0; j < _realizedItems.Count; j++)
            {
                if (_realizedItems[j].ItemIndex == startIndex + i)
                {
                    removeAt = j;
                    break;
                }
            }
            if (removeAt >= 0)
            {
                _recycleQueue.Enqueue(_realizedItems[removeAt].Container);
                _realizedItems.RemoveAt(removeAt);
            }
        }
    }

    #endregion

    #region Internal - Collection Change Handling

    /// <summary>
    /// Called by ItemsControl when the underlying collection changes.
    /// Updates realized item indices and notifies listeners.
    /// </summary>
    internal void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                OnItemAdded(e.NewStartingIndex, e.NewItems?.Count ?? 1);
                break;

            case NotifyCollectionChangedAction.Remove:
                OnItemRemoved(e.OldStartingIndex, e.OldItems?.Count ?? 1);
                break;

            case NotifyCollectionChangedAction.Replace:
                OnItemReplaced(e.NewStartingIndex);
                break;

            case NotifyCollectionChangedAction.Move:
                OnItemMoved(e.OldStartingIndex, e.NewStartingIndex);
                break;

            case NotifyCollectionChangedAction.Reset:
                OnReset();
                break;
        }
    }

    private void OnItemAdded(int index, int count)
    {
        // Shift realized items after the insertion point
        for (int i = 0; i < _realizedItems.Count; i++)
        {
            if (_realizedItems[i].ItemIndex >= index)
            {
                _realizedItems[i] = _realizedItems[i] with { ItemIndex = _realizedItems[i].ItemIndex + count };
            }
        }

        var position = GeneratorPositionFromIndex(index);
        ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(
            NotifyCollectionChangedAction.Add, position, count, 0));
    }

    private void OnItemRemoved(int index, int count)
    {
        // Remove realized items at the removed indices
        for (int i = _realizedItems.Count - 1; i >= 0; i--)
        {
            var itemIndex = _realizedItems[i].ItemIndex;
            if (itemIndex >= index && itemIndex < index + count)
            {
                _realizedItems.RemoveAt(i);
            }
        }

        // Shift remaining realized items
        for (int i = 0; i < _realizedItems.Count; i++)
        {
            if (_realizedItems[i].ItemIndex >= index + count)
            {
                _realizedItems[i] = _realizedItems[i] with { ItemIndex = _realizedItems[i].ItemIndex - count };
            }
        }

        var position = GeneratorPositionFromIndex(index);
        ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(
            NotifyCollectionChangedAction.Remove, position, count, count));
    }

    private void OnItemReplaced(int index)
    {
        // Remove old mapping
        for (int i = _realizedItems.Count - 1; i >= 0; i--)
        {
            if (_realizedItems[i].ItemIndex == index)
            {
                _realizedItems.RemoveAt(i);
                break;
            }
        }

        var position = GeneratorPositionFromIndex(index);
        ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(
            NotifyCollectionChangedAction.Replace, position, 1, 1));
    }

    private void OnItemMoved(int oldIndex, int newIndex)
    {
        var oldPosition = GeneratorPositionFromIndex(oldIndex);
        var newPosition = GeneratorPositionFromIndex(newIndex);

        // Update mapping
        for (int i = 0; i < _realizedItems.Count; i++)
        {
            if (_realizedItems[i].ItemIndex == oldIndex)
            {
                _realizedItems[i] = _realizedItems[i] with { ItemIndex = newIndex };
            }
            else if (oldIndex < newIndex)
            {
                // Item moved forward: shift items in between backward
                if (_realizedItems[i].ItemIndex > oldIndex && _realizedItems[i].ItemIndex <= newIndex)
                    _realizedItems[i] = _realizedItems[i] with { ItemIndex = _realizedItems[i].ItemIndex - 1 };
            }
            else
            {
                // Item moved backward: shift items in between forward
                if (_realizedItems[i].ItemIndex >= newIndex && _realizedItems[i].ItemIndex < oldIndex)
                    _realizedItems[i] = _realizedItems[i] with { ItemIndex = _realizedItems[i].ItemIndex + 1 };
            }
        }

        ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(
            NotifyCollectionChangedAction.Move, newPosition, oldPosition, 1, 1));
    }

    private void OnReset()
    {
        _realizedItems.Clear();
        _recycleQueue.Clear();
        Status = GeneratorStatus.NotStarted;

        ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(
            NotifyCollectionChangedAction.Reset, new GeneratorPosition(-1, 0), 0, 0));
    }

    #endregion

    #region Private Helpers

    private object? GetItemAt(int index)
    {
        var source = _host.ItemsSource ?? (IEnumerable)_host.Items;
        if (source is IList list)
            return list[index];

        int i = 0;
        foreach (var item in source)
        {
            if (i == index) return item;
            i++;
        }
        return null;
    }

    private void AdvanceIndex()
    {
        if (_generatorDirection == GeneratorDirection.Forward)
            _generatorCurrentIndex++;
        else
            _generatorCurrentIndex--;
    }

    private void EndGeneration()
    {
        _isGenerating = false;
        Status = GeneratorStatus.ContainersGenerated;
    }

    #endregion

    #region Internal Types

    /// <summary>
    /// Maps an item index to its container and data item.
    /// </summary>
    internal record struct ItemContainerMap(int ItemIndex, object Item, DependencyObject Container);

    /// <summary>
    /// Disposable that ends the generation session.
    /// </summary>
    private sealed class GeneratorSession : IDisposable
    {
        private readonly ItemContainerGenerator _generator;

        public GeneratorSession(ItemContainerGenerator generator)
        {
            _generator = generator;
        }

        public void Dispose()
        {
            _generator.EndGeneration();
        }
    }

    #endregion
}
