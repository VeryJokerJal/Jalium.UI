using System.Collections;
using System.Collections.Specialized;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Virtualization;

namespace Jalium.UI.Controls;

/// <summary>
/// Generates UI containers for items on behalf of a host such as ItemsControl.
/// Maintains the association between data items and their UI containers, and supports
/// container recycling for virtualization.
/// </summary>
public sealed class ItemContainerGenerator : IRecyclingItemContainerGenerator
{
    private readonly ItemsControl _host;
    private readonly LogicalItemAccessor _itemAccessor;
    private readonly Dictionary<int, ItemContainerMap> _realizedItems = new();
    private readonly Dictionary<DependencyObject, int> _containerToIndex = new(ReferenceEqualityComparer.Instance);
    private readonly ContainerRecyclePool _recyclePool = new();

    private GeneratorStatus _status = GeneratorStatus.NotStarted;
    private int _generatorCurrentIndex;
    private GeneratorDirection _generatorDirection;
    private bool _isGenerating;
    private bool _sortedIndexCacheDirty = true;
    private readonly List<int> _sortedIndices = new();
    private Type? _preferredContainerType;

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemContainerGenerator"/> class.
    /// </summary>
    internal ItemContainerGenerator(ItemsControl host)
    {
        _host = host;
        _itemAccessor = new LogicalItemAccessor(host);
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
    public int ItemCount => _itemAccessor.Count;

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
        return _realizedItems.TryGetValue(index, out var map) ? map.Container : null;
    }

    /// <summary>
    /// Returns the container for the specified item.
    /// </summary>
    public DependencyObject? ContainerFromItem(object item)
    {
        foreach (var map in _realizedItems.Values)
        {
            if (Equals(map.Item, item))
            {
                return map.Container;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the item that corresponds to the specified generated container.
    /// </summary>
    public object? ItemFromContainer(DependencyObject container)
    {
        if (_containerToIndex.TryGetValue(container, out var index) && _realizedItems.TryGetValue(index, out var map))
        {
            return map.Item;
        }

        return DependencyProperty.UnsetValue;
    }

    /// <summary>
    /// Returns the index to an item that corresponds to the specified generated container.
    /// </summary>
    public int IndexFromContainer(DependencyObject container)
    {
        return _containerToIndex.TryGetValue(container, out var index) ? index : -1;
    }

    #endregion

    #region IItemContainerGenerator Implementation

    /// <inheritdoc />
    public GeneratorPosition GeneratorPositionFromIndex(int itemIndex)
    {
        var sorted = GetSortedRealizedIndices();
        if (sorted.Count == 0)
        {
            return new GeneratorPosition(-1, itemIndex + 1);
        }

        var idx = sorted.BinarySearch(itemIndex);
        if (idx >= 0)
        {
            return new GeneratorPosition(idx, 0);
        }

        var insert = ~idx;
        var before = insert - 1;
        if (before >= 0)
        {
            return new GeneratorPosition(before, itemIndex - sorted[before]);
        }

        return new GeneratorPosition(-1, itemIndex + 1);
    }

    /// <inheritdoc />
    public int IndexFromGeneratorPosition(GeneratorPosition position)
    {
        if (position.Index == -1)
        {
            return position.Offset - 1;
        }

        var sorted = GetSortedRealizedIndices();
        if (position.Index >= 0 && position.Index < sorted.Count)
        {
            return sorted[position.Index] + position.Offset;
        }

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
        {
            return null;
        }

        var container = GetOrCreateContainerForIndex(_generatorCurrentIndex, out isNewlyRealized);
        AdvanceIndex();
        return container;
    }

    /// <inheritdoc />
    public void PrepareItemContainer(DependencyObject container)
    {
        if (container is not FrameworkElement element)
        {
            return;
        }

        var item = ItemFromContainer(container);
        if (item == null || item == DependencyProperty.UnsetValue)
        {
            return;
        }

        _host.PrepareContainerForItemInternal(element, item);
    }

    /// <inheritdoc />
    public void Remove(GeneratorPosition position, int count)
    {
        var startIndex = IndexFromGeneratorPosition(position);
        for (int i = 0; i < count; i++)
        {
            RemoveIndexInternal(startIndex + i, recycle: false);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Public full-teardown surface: drops the realized bookkeeping AND the recycle pool. This is
    /// the path RefreshItems (ItemsSource / ItemTemplate rebinds) uses to guarantee brand-new
    /// containers. A collection Reset no longer routes here — see <c>OnReset</c>, which recycles
    /// realized containers into the pool instead.
    /// </remarks>
    public void RemoveAll()
    {
        _realizedItems.Clear();
        _containerToIndex.Clear();
        _recyclePool.Clear();
        _sortedIndices.Clear();
        _sortedIndexCacheDirty = false;
        Status = GeneratorStatus.NotStarted;
    }

    /// <inheritdoc />
    public void Recycle(GeneratorPosition position, int count)
    {
        var startIndex = IndexFromGeneratorPosition(position);
        for (int i = 0; i < count; i++)
        {
            RemoveIndexInternal(startIndex + i, recycle: true);
        }
    }

    #endregion

    #region Internal API

    internal DependencyObject? GetOrCreateContainerForIndex(int index, out bool isNewlyRealized)
    {
        isNewlyRealized = false;
        if (index < 0 || index >= ItemCount)
        {
            return null;
        }

        if (_realizedItems.TryGetValue(index, out var existing))
        {
            return existing.Container;
        }

        var item = _itemAccessor.GetItemAt(index);
        if (item == null)
        {
            return null;
        }

        DependencyObject container;
        bool isOwnContainer = _host.IsItemItsOwnContainerPublic(item);
        if (isOwnContainer)
        {
            container = (DependencyObject)item;
            if (container is FrameworkElement ownContainer)
            {
                _host.PrepareContainerForItemInternal(ownContainer, item);
            }
            isNewlyRealized = true;
        }
        else if (_preferredContainerType != null && TryPopDetachedContainer(out var recycled))
        {
            container = recycled;

            // The container was content-cleared (ClearContainerForItem) before it entered the pool.
            // Flag as newly realized so the caller (VirtualizingStackPanel) invokes
            // PrepareItemContainer and rebinds it to the new item.
            isNewlyRealized = true;
        }
        else
        {
            var generatedContainer = _host.GetContainerForItemPublic(item);
            _preferredContainerType ??= generatedContainer.GetType();
            container = generatedContainer;
            isNewlyRealized = true;
        }

        MapContainer(index, item, container, isOwnContainer);
        return container;
    }

    /// <summary>
    /// Pops pool entries until one that is fully detached comes up. A container
    /// recycled by a mid-measure Reset can still be attached to its panel — the
    /// panel defers its own bookkeeping/Children update until after the current
    /// measure pass, so popping such a container here would steal a live child
    /// the panel still tracks under another key: force-detaching it forks the
    /// panel's Children/realized bookkeeping into zombie entries and mis-keyed
    /// rows. The pool therefore only supplies detached containers; still-attached
    /// ones are dropped (the panel owns them and detaches them when its deferred
    /// reset runs — they simply lose their pooling opportunity).
    /// </summary>
    private bool TryPopDetachedContainer([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out DependencyObject? container)
    {
        while (_recyclePool.TryPop(_preferredContainerType!, out container) && container != null)
        {
            if (container is not Visual visual || visual.VisualParent == null)
            {
                return true;
            }
        }

        container = null;
        return false;
    }

    internal bool RecycleIndex(int index)
    {
        return RemoveIndexInternal(index, recycle: true);
    }

    internal bool RemoveIndex(int index)
    {
        return RemoveIndexInternal(index, recycle: false);
    }

    internal IReadOnlyList<int> GetRealizedIndices()
    {
        return GetSortedRealizedIndices();
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
                OnItemReplaced(e.NewStartingIndex, e.NewItems?.Count ?? 1);
                break;

            case NotifyCollectionChangedAction.Move:
                OnItemMoved(e.OldStartingIndex, e.NewStartingIndex, e.NewItems?.Count ?? e.OldItems?.Count ?? 1);
                break;

            case NotifyCollectionChangedAction.Reset:
                OnReset();
                break;
        }
    }

    private void OnItemAdded(int index, int count)
    {
        if (count <= 0)
        {
            return;
        }

        // Capture generator position BEFORE index remapping so event subscribers
        // receive the position relative to the pre-mutation state.
        var position = GeneratorPositionFromIndex(index);

        var keys = _realizedItems.Keys.Where(k => k >= index).OrderByDescending(k => k).ToArray();
        foreach (var key in keys)
        {
            var map = _realizedItems[key];
            _realizedItems.Remove(key);
            var newIndex = key + count;
            map.ItemIndex = newIndex;
            _realizedItems[newIndex] = map;
            _containerToIndex[map.Container] = newIndex;
        }

        MarkSortedCacheDirty();
        ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(
            NotifyCollectionChangedAction.Add, position, count, 0, index));
    }

    private void OnItemRemoved(int index, int count)
    {
        if (count <= 0)
        {
            return;
        }

        // Capture generator position BEFORE index remapping so event subscribers
        // receive the position relative to the pre-mutation state.
        var position = GeneratorPositionFromIndex(index);

        var toRemove = _realizedItems.Keys.Where(k => k >= index && k < index + count).ToArray();
        foreach (var key in toRemove)
        {
            RemoveIndexInternal(key, recycle: true);
        }

        var keysToShift = _realizedItems.Keys.Where(k => k >= index + count).OrderBy(k => k).ToArray();
        foreach (var oldIndex in keysToShift)
        {
            var map = _realizedItems[oldIndex];
            _realizedItems.Remove(oldIndex);
            var newIndex = oldIndex - count;
            map.ItemIndex = newIndex;
            _realizedItems[newIndex] = map;
            _containerToIndex[map.Container] = newIndex;
        }

        MarkSortedCacheDirty();
        ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(
            NotifyCollectionChangedAction.Remove, position, count, count, index));
    }

    private void OnItemReplaced(int index, int count)
    {
        if (count <= 0)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            RemoveIndexInternal(index + i, recycle: true);
        }

        MarkSortedCacheDirty();
        var position = GeneratorPositionFromIndex(index);
        ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(
            NotifyCollectionChangedAction.Replace, position, count, count, index));
    }

    private void OnItemMoved(int oldIndex, int newIndex, int count)
    {
        if (oldIndex < 0 || newIndex < 0 || count <= 0 || oldIndex == newIndex)
        {
            // Malformed / degenerate move — fall back to a full reset for safety.
            OnReset();
            return;
        }

        var newPosition = GeneratorPositionFromIndex(newIndex);
        var oldPosition = GeneratorPositionFromIndex(oldIndex);

        // Recycle every realized container whose absolute index is affected by the move. Items
        // OUTSIDE this span keep their indices, so their realized entries stay valid; the affected
        // ones are dropped and re-realized at their new indices on the next measure.
        var lo = Math.Min(oldIndex, newIndex);
        var hi = Math.Max(oldIndex, newIndex) + count;
        var affected = _realizedItems.Keys.Where(k => k >= lo && k < hi).ToArray();
        foreach (var key in affected)
        {
            RemoveIndexInternal(key, recycle: true);
        }

        MarkSortedCacheDirty();
        ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(
            NotifyCollectionChangedAction.Move, newPosition, oldPosition, count, count, newIndex, oldIndex));
    }

    private void OnReset()
    {
        // Tiered reset: unlike RemoveAll (the public full-teardown surface used by the
        // RefreshItems / ItemsSource / ItemTemplate rebind paths), a collection Reset preserves
        // the recycle pool so the next measure pops reused containers instead of rebuilding them.
        // Each realized entry goes through RemoveIndexInternal(recycle: true), i.e.
        // stop animations -> ClearContainerForItem -> pool.
        if (_realizedItems.Count > 0)
        {
            // Local snapshot, not a shared buffer: ClearContainerForItem below runs
            // user-overridable code that can re-enter with another collection change,
            // and a nested Reset refilling a shared buffer would corrupt this loop.
            // Keys shifted by a re-entrant Add/Remove still go stale — those entries
            // are skipped by RemoveIndexInternal and swept by the fallback Clear
            // below (their containers detach via the panel + deferred detach stop,
            // at the cost of one lost pooling opportunity).
            var keys = _realizedItems.Keys.ToArray();
            for (int i = 0; i < keys.Length; i++)
            {
                RemoveIndexInternal(keys[i], recycle: true);
            }
        }

        // Fallback for any leftover bookkeeping (own-container entries only detach their map
        // entry above); keeps the state transition identical to the old RemoveAll-based path,
        // and the Reset event semantics/ordering that VSP/VWP rely on are unchanged.
        _realizedItems.Clear();
        _containerToIndex.Clear();
        _sortedIndices.Clear();
        _sortedIndexCacheDirty = false;
        Status = GeneratorStatus.NotStarted;
        ItemsChanged?.Invoke(this, new ItemsChangedEventArgs(
            NotifyCollectionChangedAction.Reset, new GeneratorPosition(-1, 0), 0, 0));
    }

    #endregion

    #region Private Helpers

    private void AdvanceIndex()
    {
        if (_generatorDirection == GeneratorDirection.Forward)
        {
            _generatorCurrentIndex++;
        }
        else
        {
            _generatorCurrentIndex--;
        }
    }

    private void EndGeneration()
    {
        _isGenerating = false;
        Status = GeneratorStatus.ContainersGenerated;
    }

    private void MapContainer(int index, object item, DependencyObject container, bool isOwnContainer)
    {
        _realizedItems[index] = new ItemContainerMap(index, item, container, isOwnContainer);
        _containerToIndex[container] = index;
        MarkSortedCacheDirty();
    }

    private bool RemoveIndexInternal(int index, bool recycle)
    {
        if (!_realizedItems.TryGetValue(index, out var map))
        {
            return false;
        }

        _realizedItems.Remove(index);
        _containerToIndex.Remove(map.Container);
        if (!map.IsOwnContainer)
        {
            // Recycle-time animation hygiene, and it must run BEFORE ClearContainerForItem so
            // overrides (ListBox reading IsSelected, etc.) observe base values. Synchronous on
            // purpose: a pooled container can be popped again within the same layout pass, so the
            // deferred NotifyDetached policy is too late. The discard (!recycle) path stops too —
            // otherwise a running animation keeps the dead container pinned via its
            // AnimationManager registration. Own containers are app-owned elements that may be
            // re-attached elsewhere right after removal; they are left to the deferred detach
            // policy instead.
            (map.Container as UIElement)?.StopAnimationsForRecycleRecursive();

            if (recycle)
            {
                // Clear FIRST, then pool, so the recycle pool only ever holds clean containers that
                // no longer alias the previous item's content/selection. This single choke point
                // covers RecycleIndex, OnItemRemoved/Replaced/Moved, OnReset and the
                // IRecyclingItemContainerGenerator.Recycle surface, for both VirtualizingStackPanel
                // and VirtualizingWrapPanel.
                if (map.Container is FrameworkElement containerElement)
                {
                    _host.ClearContainerForItemInternal(containerElement, map.Item);
                }

                _recyclePool.Push(map.Container);
            }
        }

        MarkSortedCacheDirty();
        return true;
    }

    private List<int> GetSortedRealizedIndices()
    {
        if (!_sortedIndexCacheDirty)
        {
            return _sortedIndices;
        }

        _sortedIndices.Clear();
        _sortedIndices.AddRange(_realizedItems.Keys);
        _sortedIndices.Sort();
        _sortedIndexCacheDirty = false;
        return _sortedIndices;
    }

    private void MarkSortedCacheDirty()
    {
        _sortedIndexCacheDirty = true;
    }

    #endregion

    #region Internal Types

    /// <summary>
    /// Maps an item index to its container and data item.
    /// </summary>
    internal struct ItemContainerMap
    {
        public ItemContainerMap(int itemIndex, object item, DependencyObject container, bool isOwnContainer)
        {
            ItemIndex = itemIndex;
            Item = item;
            Container = container;
            IsOwnContainer = isOwnContainer;
        }

        public int ItemIndex { get; set; }

        public object Item { get; set; }

        public DependencyObject Container { get; set; }

        public bool IsOwnContainer { get; set; }
    }

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
