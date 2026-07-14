using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace Jalium.UI.Data;

/// <summary>
/// Represents a collection view over an <see cref="IList"/>.
/// </summary>
public class ListCollectionView : CollectionView,
    IComparer,
    IEditableCollectionView,
    IEditableCollectionViewAddNewItem,
    ICollectionViewLiveShaping,
    IItemProperties
{
    private readonly IList _sourceList;
    private readonly ObservableCollection<string> _liveSortingProperties = new();
    private readonly ObservableCollection<string> _liveFilteringProperties = new();
    private readonly ObservableCollection<string> _liveGroupingProperties = new();
    private readonly HashSet<INotifyPropertyChanged> _liveItems = new();
    private readonly CollectionViewGroup _selectorRoot = new SelectorRootGroup();
    private object? _editItem;
    private object? _newItem;
    private NewItemPlaceholderPosition _newItemPlaceholderPosition;
    private IComparer? _customSort;
    private GroupDescriptionSelectorCallback? _groupBySelector;
    private IComparer? _activeComparer;
    private Predicate<object>? _activeFilter;
    private bool _isDataInGroupOrder;
    private bool? _isLiveSorting = false;
    private bool? _isLiveFiltering = false;
    private bool? _isLiveGrouping = false;

    /// <summary>
    /// Initializes a new list collection view.
    /// </summary>
    public ListCollectionView(IList list)
        : base(list)
    {
        _sourceList = list ?? throw new ArgumentNullException(nameof(list));
        ((INotifyCollectionChanged)SortDescriptions).CollectionChanged += OnListSortDescriptionsChanged;
        _liveSortingProperties.CollectionChanged += OnLivePropertyListChanged;
        _liveFilteringProperties.CollectionChanged += OnLivePropertyListChanged;
        _liveGroupingProperties.CollectionChanged += OnLivePropertyListChanged;
        RefreshOverride();
    }

    /// <inheritdoc />
    public override bool CanFilter => true;

    /// <inheritdoc />
    public override bool CanGroup => true;

    /// <inheritdoc />
    public override bool CanSort => true;

    /// <inheritdoc />
    public override Predicate<object>? Filter
    {
        get => base.Filter;
        set
        {
            EnsureNotAddingOrEditing(nameof(Filter));
            base.Filter = value;
        }
    }

    /// <inheritdoc />
    public override IComparer? Comparer => ActiveComparer;

    /// <summary>
    /// Gets or sets a custom comparer. Setting it clears <see cref="CollectionView.SortDescriptions"/>.
    /// </summary>
    public IComparer? CustomSort
    {
        get => _customSort;
        set
        {
            EnsureNotAddingOrEditing(nameof(CustomSort));
            if (ReferenceEquals(_customSort, value))
            {
                return;
            }

            _customSort = value;
            if (SortDescriptions.Count > 0)
            {
                SortDescriptions.Clear();
            }

            RefreshOrDefer();
            OnPropertyChanged(nameof(CustomSort));
        }
    }

    /// <summary>
    /// Gets or sets a callback that selects a grouping description for each level.
    /// </summary>
    [DefaultValue(null)]
    public virtual GroupDescriptionSelectorCallback? GroupBySelector
    {
        get => _groupBySelector;
        set
        {
            EnsureNotAddingOrEditing(nameof(GroupBySelector));
            if (ReferenceEquals(_groupBySelector, value))
            {
                return;
            }

            _groupBySelector = value;
            RefreshOrDefer();
            OnPropertyChanged(nameof(GroupBySelector));
        }
    }

    /// <summary>
    /// Gets or sets whether the input is already ordered for grouping.
    /// </summary>
    public bool IsDataInGroupOrder
    {
        get => _isDataInGroupOrder;
        set
        {
            if (_isDataInGroupOrder == value)
            {
                return;
            }

            _isDataInGroupOrder = value;
            OnPropertyChanged(nameof(IsDataInGroupOrder));
        }
    }

    /// <inheritdoc />
    public override int Count
    {
        get
        {
            VerifyRefreshNotDeferred();
            return InternalCount;
        }
    }

    /// <inheritdoc />
    public override bool IsEmpty => InternalCount == 0;

    /// <summary>
    /// Gets whether a new instance of the item type can be constructed and added.
    /// </summary>
    public bool CanAddNew => !IsEditingItem && CanAddNewItem && GetItemConstructor() != null;

    /// <summary>
    /// Gets whether a supplied item can be added.
    /// </summary>
    public bool CanAddNewItem => !IsEditingItem && !_sourceList.IsFixedSize && !_sourceList.IsReadOnly;

    /// <summary>
    /// Gets whether an edit can be canceled.
    /// </summary>
    public bool CanCancelEdit => _editItem is IEditableObject;

    /// <summary>
    /// Gets whether items can be removed.
    /// </summary>
    public bool CanRemove =>
        !IsAddingNew && !IsEditingItem && !_sourceList.IsFixedSize && !_sourceList.IsReadOnly;

    /// <summary>
    /// Gets the pending new item.
    /// </summary>
    public object? CurrentAddItem => _newItem;

    /// <summary>
    /// Gets the item in the current edit transaction.
    /// </summary>
    public object? CurrentEditItem => _editItem;

    /// <summary>
    /// Gets whether an add transaction is active.
    /// </summary>
    public bool IsAddingNew => _newItem != null;

    /// <summary>
    /// Gets whether an edit transaction is active.
    /// </summary>
    public bool IsEditingItem => _editItem != null;

    /// <summary>
    /// Gets or sets the insertion placeholder position.
    /// </summary>
    public NewItemPlaceholderPosition NewItemPlaceholderPosition
    {
        get => _newItemPlaceholderPosition;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            VerifyRefreshNotDeferred();
            if (IsAddingNew && value != _newItemPlaceholderPosition)
            {
                throw new InvalidOperationException(
                    "NewItemPlaceholderPosition cannot be changed during an add transaction.");
            }

            if (value == _newItemPlaceholderPosition)
            {
                return;
            }

            _newItemPlaceholderPosition = value;
            SynchronizeCurrencyWithDisplay();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            OnPropertyChanged(nameof(NewItemPlaceholderPosition));
            OnPropertyChanged(nameof(Count));
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>
    /// Gets whether live sorting can be toggled.
    /// </summary>
    public bool CanChangeLiveSorting => true;

    /// <summary>
    /// Gets whether live filtering can be toggled.
    /// </summary>
    public bool CanChangeLiveFiltering => true;

    /// <summary>
    /// Gets whether live grouping can be toggled.
    /// </summary>
    public bool CanChangeLiveGrouping => true;

    /// <summary>
    /// Gets or sets whether sorting reacts to item property changes.
    /// </summary>
    public bool? IsLiveSorting
    {
        get => _isLiveSorting;
        set => SetLiveShapingValue(ref _isLiveSorting, value, nameof(IsLiveSorting));
    }

    /// <summary>
    /// Gets or sets whether filtering reacts to item property changes.
    /// </summary>
    public bool? IsLiveFiltering
    {
        get => _isLiveFiltering;
        set => SetLiveShapingValue(ref _isLiveFiltering, value, nameof(IsLiveFiltering));
    }

    /// <summary>
    /// Gets or sets whether grouping reacts to item property changes.
    /// </summary>
    public bool? IsLiveGrouping
    {
        get => _isLiveGrouping;
        set => SetLiveShapingValue(ref _isLiveGrouping, value, nameof(IsLiveGrouping));
    }

    /// <summary>
    /// Gets the property names used for live sorting.
    /// </summary>
    public ObservableCollection<string> LiveSortingProperties => _liveSortingProperties;

    /// <summary>
    /// Gets the property names used for live filtering.
    /// </summary>
    public ObservableCollection<string> LiveFilteringProperties => _liveFilteringProperties;

    /// <summary>
    /// Gets the property names used for live grouping.
    /// </summary>
    public ObservableCollection<string> LiveGroupingProperties => _liveGroupingProperties;

    /// <summary>
    /// Gets metadata for properties available on source items.
    /// </summary>
    public ReadOnlyCollection<ItemPropertyInfo>? ItemProperties => GetItemProperties();

    /// <inheritdoc />
    public override bool Contains(object item)
    {
        VerifyRefreshNotDeferred();
        return InternalContains(item);
    }

    /// <inheritdoc />
    public override int IndexOf(object item)
    {
        VerifyRefreshNotDeferred();
        return InternalIndexOf(item);
    }

    /// <inheritdoc />
    public override object GetItemAt(int index)
    {
        VerifyRefreshNotDeferred();
        return InternalItemAt(index);
    }

    /// <inheritdoc />
    public override bool PassesFilter(object item) => ActiveFilter == null || ActiveFilter(item);

    /// <inheritdoc />
    public override bool MoveCurrentToFirst() =>
        MoveCurrentToPosition(NewItemPlaceholderPosition == NewItemPlaceholderPosition.AtBeginning ? 1 : 0);

    /// <inheritdoc />
    public override bool MoveCurrentToLast() =>
        MoveCurrentToPosition(NewItemPlaceholderPosition == NewItemPlaceholderPosition.AtEnd ? Count - 2 : Count - 1);

    /// <inheritdoc />
    public override bool MoveCurrentToNext()
    {
        var position = CurrentPosition + 1;
        if (position == 0 && NewItemPlaceholderPosition == NewItemPlaceholderPosition.AtBeginning)
        {
            position++;
        }

        if (position == Count - 1 && NewItemPlaceholderPosition == NewItemPlaceholderPosition.AtEnd)
        {
            position = Count;
        }

        return position <= Count && MoveCurrentToPosition(position);
    }

    /// <inheritdoc />
    public override bool MoveCurrentToPrevious()
    {
        var position = CurrentPosition - 1;
        if (position == Count - 1 && NewItemPlaceholderPosition == NewItemPlaceholderPosition.AtEnd)
        {
            position--;
        }

        if (position == 0 && NewItemPlaceholderPosition == NewItemPlaceholderPosition.AtBeginning)
        {
            position = -1;
        }

        return position >= -1 && MoveCurrentToPosition(position);
    }

    /// <inheritdoc />
    public override bool MoveCurrentToPosition(int position)
    {
        if (position >= 0 && position < Count && ReferenceEquals(InternalItemAt(position), NewItemPlaceholder))
        {
            return CurrentPosition >= 0 && CurrentPosition < Count;
        }

        return base.MoveCurrentToPosition(position);
    }

    /// <summary>
    /// Creates and begins editing a new item.
    /// </summary>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2072:RequiresUnreferencedCode",
        Justification = "The source list's runtime item type defines the editable collection contract; consumers preserve its parameterless constructor under trimming.")]
    public object? AddNew()
    {
        VerifyRefreshNotDeferred();
        if (!CanAddNew)
        {
            throw new InvalidOperationException("Cannot construct a new item for this view.");
        }

        return AddNewItem(GetItemConstructor()!.Invoke(null));
    }

    /// <summary>
    /// Adds a supplied item and begins an add transaction.
    /// </summary>
    public object AddNewItem(object newItem)
    {
        ArgumentNullException.ThrowIfNull(newItem);
        VerifyRefreshNotDeferred();

        if (IsEditingItem)
        {
            CommitEdit();
        }

        CommitNew();
        if (!CanAddNewItem)
        {
            throw new InvalidOperationException("Items cannot be added to this view.");
        }

        _newItem = newItem;
        try
        {
            _sourceList.Add(newItem);
        }
        catch
        {
            _newItem = null;
            throw;
        }

        if (newItem is ISupportInitialize initialize)
        {
            initialize.BeginInit();
        }

        if (newItem is IEditableObject editable)
        {
            editable.BeginEdit();
        }

        if (_sourceList is not INotifyCollectionChanged)
        {
            RefreshOverride();
        }

        MoveCurrentTo(newItem);
        RaiseTransactionProperties();
        return newItem;
    }

    /// <summary>
    /// Cancels the current edit transaction.
    /// </summary>
    public void CancelEdit()
    {
        if (_editItem == null)
        {
            return;
        }

        if (_editItem is not IEditableObject editable)
        {
            throw new InvalidOperationException("The current item does not support canceling edits.");
        }

        editable.CancelEdit();
        _editItem = null;
        RaiseTransactionProperties();
    }

    /// <summary>
    /// Cancels and removes the pending new item.
    /// </summary>
    public void CancelNew()
    {
        if (_newItem == null)
        {
            return;
        }

        var item = _newItem;
        _newItem = null;
        if (item is IEditableObject editable)
        {
            editable.CancelEdit();
        }

        _sourceList.Remove(item);
        if (_sourceList is not INotifyCollectionChanged)
        {
            RefreshOverride();
        }

        RaiseTransactionProperties();
    }

    /// <summary>
    /// Commits the current edit transaction.
    /// </summary>
    public void CommitEdit()
    {
        if (_editItem == null)
        {
            return;
        }

        if (_editItem is IEditableObject editable)
        {
            editable.EndEdit();
        }

        _editItem = null;
        RefreshOrDefer();
        RaiseTransactionProperties();
    }

    /// <summary>
    /// Commits the pending new item.
    /// </summary>
    public void CommitNew()
    {
        if (_newItem == null)
        {
            return;
        }

        var item = _newItem;
        _newItem = null;
        if (item is IEditableObject editable)
        {
            editable.EndEdit();
        }

        if (item is ISupportInitialize initialize)
        {
            initialize.EndInit();
        }

        RefreshOrDefer();
        RaiseTransactionProperties();
    }

    /// <summary>
    /// Begins editing an item.
    /// </summary>
    public void EditItem(object item)
    {
        ArgumentNullException.ThrowIfNull(item);
        VerifyRefreshNotDeferred();
        if (ReferenceEquals(item, NewItemPlaceholder) || !InternalContains(item))
        {
            throw new ArgumentException("The item does not belong to this view.", nameof(item));
        }

        CommitNew();
        CommitEdit();
        _editItem = item;
        if (item is IEditableObject editable)
        {
            editable.BeginEdit();
        }

        RaiseTransactionProperties();
    }

    /// <summary>
    /// Removes an item from the source list.
    /// </summary>
    public void Remove(object item)
    {
        if (!CanRemove)
        {
            throw new InvalidOperationException("Items cannot be removed during the current transaction.");
        }

        if (ReferenceEquals(item, NewItemPlaceholder))
        {
            throw new InvalidOperationException("The new-item placeholder cannot be removed.");
        }

        _sourceList.Remove(item);
        if (_sourceList is not INotifyCollectionChanged)
        {
            RefreshOverride();
        }
    }

    /// <summary>
    /// Removes the item at an effective view index.
    /// </summary>
    public void RemoveAt(int index) => Remove(GetItemAt(index));

    /// <summary>
    /// Compares two items using the active sort, or their effective order.
    /// </summary>
    protected virtual int Compare(object? o1, object? o2)
    {
        if (ActiveComparer != null)
        {
            return ActiveComparer.Compare(o1, o2);
        }

        return InternalIndexOf(o1!) - InternalIndexOf(o2!);
    }

    int IComparer.Compare(object? o1, object? o2) => Compare(o1, o2);

    /// <summary>
    /// Gets whether an item is in the effective view, including the placeholder.
    /// </summary>
    protected bool InternalContains(object item) => InternalIndexOf(item) >= 0;

    /// <summary>
    /// Returns an enumerator over effective items, including the placeholder.
    /// </summary>
    protected IEnumerator InternalGetEnumerator() => GetDisplayItems().GetEnumerator();

    /// <summary>
    /// Gets an item's effective index, including the placeholder.
    /// </summary>
    protected int InternalIndexOf(object item) => GetDisplayItems().IndexOf(item);

    /// <summary>
    /// Gets an effective item, including the placeholder.
    /// </summary>
    protected object InternalItemAt(int index)
    {
        var displayItems = GetDisplayItems();
        if ((uint)index >= (uint)displayItems.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return displayItems[index]!;
    }

    /// <summary>
    /// Gets whether sorting, filtering, or live grouping requires an effective local array.
    /// </summary>
    protected bool UsesLocalArray => ActiveComparer != null || ActiveFilter != null || (IsGrouping && IsLiveGrouping == true);

    /// <summary>
    /// Gets the effective ordinary item list.
    /// </summary>
    protected IList InternalList => EffectiveItems;

    /// <summary>
    /// Gets or sets the active comparer used during refresh.
    /// </summary>
    protected IComparer? ActiveComparer
    {
        get => _activeComparer;
        set => _activeComparer = value;
    }

    /// <summary>
    /// Gets or sets the active filter used during refresh.
    /// </summary>
    protected Predicate<object>? ActiveFilter
    {
        get => _activeFilter;
        set => _activeFilter = value;
    }

    /// <summary>
    /// Gets whether grouping is active.
    /// </summary>
    protected bool IsGrouping => GroupBySelector != null || GroupDescriptions.Count > 0;

    /// <summary>
    /// Gets the effective count, including the placeholder.
    /// </summary>
    protected int InternalCount => GetDisplayItems().Count;

    /// <inheritdoc />
    protected override IEnumerator GetEnumerator()
    {
        VerifyRefreshNotDeferred();
        return InternalGetEnumerator();
    }

    /// <inheritdoc />
    protected override void RefreshOverride()
    {
        ActiveFilter = base.Filter;
        ActiveComparer = _customSort ?? base.Comparer;
        base.RefreshOverride();
        SynchronizeCurrencyWithDisplay();
        RebuildLiveItemSubscriptions();
    }

    /// <inheritdoc />
    protected override void ProcessCollectionChanged(NotifyCollectionChangedEventArgs args)
    {
        base.ProcessCollectionChanged(args);
        SynchronizeCurrencyWithDisplay();
        RebuildLiveItemSubscriptions();
    }

    /// <inheritdoc />
    protected override System.ComponentModel.GroupDescription? GetGroupDescription(CollectionViewGroup? parentGroup, int level)
    {
        if (_groupBySelector != null)
        {
            return _groupBySelector(parentGroup ?? _selectorRoot, level);
        }

        return base.GetGroupDescription(parentGroup, level);
    }

    private ArrayList GetDisplayItems()
    {
        var items = new ArrayList(InternalList);
        if (_newItem != null)
        {
            items.Remove(_newItem);
        }

        switch (NewItemPlaceholderPosition)
        {
            case NewItemPlaceholderPosition.AtBeginning:
                items.Insert(0, NewItemPlaceholder);
                if (_newItem != null)
                {
                    items.Insert(1, _newItem);
                }
                break;

            case NewItemPlaceholderPosition.AtEnd:
                if (_newItem != null)
                {
                    items.Add(_newItem);
                }
                items.Add(NewItemPlaceholder);
                break;

            default:
                if (_newItem != null && !items.Contains(_newItem))
                {
                    items.Add(_newItem);
                }
                break;
        }

        return items;
    }

    private void SynchronizeCurrencyWithDisplay()
    {
        var items = GetDisplayItems();
        var currentItem = base.CurrentItem;
        if (currentItem != null)
        {
            var position = items.IndexOf(currentItem);
            SetCurrent(position >= 0 ? currentItem : null, position, items.Count);
        }
        else
        {
            SetCurrent(null, base.CurrentPosition >= EffectiveItems.Count ? items.Count : -1, items.Count);
        }
    }

    private void EnsureNotAddingOrEditing(string memberName)
    {
        if (IsAddingNew || IsEditingItem)
        {
            throw new InvalidOperationException($"{memberName} cannot be changed during an add or edit transaction.");
        }
    }

    private void SetLiveShapingValue(ref bool? storage, bool? value, string propertyName)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        if (storage == value)
        {
            return;
        }

        storage = value;
        RefreshOrDefer();
        OnPropertyChanged(propertyName);
    }

    private void OnListSortDescriptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (SortDescriptions.Count > 0 && _customSort != null)
        {
            _customSort = null;
            OnPropertyChanged(nameof(CustomSort));
            RefreshOrDefer();
        }
    }

    private void OnLivePropertyListChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (IsLiveSorting == true || IsLiveFiltering == true || IsLiveGrouping == true)
        {
            RefreshOrDefer();
        }
    }

    private void RebuildLiveItemSubscriptions()
    {
        foreach (var item in _liveItems)
        {
            PropertyChangedEventManager.RemoveHandler(item, OnLiveItemPropertyChanged, string.Empty);
        }

        _liveItems.Clear();
        if (IsLiveSorting != true && IsLiveFiltering != true && IsLiveGrouping != true)
        {
            return;
        }

        foreach (var item in _sourceList)
        {
            if (item is INotifyPropertyChanged notifying && _liveItems.Add(notifying))
            {
                PropertyChangedEventManager.AddHandler(notifying, OnLiveItemPropertyChanged, string.Empty);
            }
        }
    }

    private void OnLiveItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ShouldRefreshForLiveProperty(e.PropertyName))
        {
            RefreshOrDefer();
        }
    }

    private bool ShouldRefreshForLiveProperty(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return IsLiveSorting == true || IsLiveFiltering == true || IsLiveGrouping == true;
        }

        if (IsLiveFiltering == true &&
            (_liveFilteringProperties.Count == 0 || _liveFilteringProperties.Contains(propertyName)))
        {
            return true;
        }

        if (IsLiveSorting == true)
        {
            if (_liveSortingProperties.Contains(propertyName) ||
                (_liveSortingProperties.Count == 0 &&
                    (CustomSort != null || SortDescriptions.Any(sort => sort.PropertyName == propertyName))))
            {
                return true;
            }
        }

        if (IsLiveGrouping == true)
        {
            if (_liveGroupingProperties.Contains(propertyName) ||
                (_liveGroupingProperties.Count == 0 &&
                    (GroupBySelector != null || GroupDescriptions
                        .OfType<PropertyGroupDescription>()
                        .Any(group => group.PropertyName == propertyName))))
            {
                return true;
            }
        }

        return false;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075:UnrecognizedReflectionPattern",
        Justification = "Editable collection views discover the runtime element type and constructor supplied by the source list.")]
    private System.Reflection.ConstructorInfo? GetItemConstructor() =>
        GetItemType()?.GetConstructor(Type.EmptyTypes);

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075:UnrecognizedReflectionPattern",
        Justification = "The editable view discovers IList<T> on the runtime source collection; bound source types preserve their interfaces under trimming.")]
    private Type? GetItemType()
    {
        var listType = _sourceList.GetType();
        var genericList = listType.GetInterfaces()
            .Append(listType)
            .FirstOrDefault(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>));
        if (genericList != null)
        {
            return genericList.GetGenericArguments()[0];
        }

        foreach (var item in _sourceList)
        {
            if (item != null)
            {
                return item.GetType();
            }
        }

        return null;
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "Item metadata is a runtime data-binding contract; source item properties must be preserved by consumers under trimming.")]
    private ReadOnlyCollection<ItemPropertyInfo>? GetItemProperties()
    {
        PropertyDescriptorCollection? descriptors = _sourceList is ITypedList typedList
            ? typedList.GetItemProperties(null)
            : GetItemType() is { } itemType
                ? TypeDescriptor.GetProperties(itemType)
                : null;

        if (descriptors == null)
        {
            return null;
        }

        return new ReadOnlyCollection<ItemPropertyInfo>(descriptors
            .Cast<PropertyDescriptor>()
            .Select(descriptor => new ItemPropertyInfo(descriptor.Name, descriptor.PropertyType, descriptor))
            .ToList());
    }

    private void RaiseTransactionProperties()
    {
        OnPropertyChanged(nameof(CurrentAddItem));
        OnPropertyChanged(nameof(CurrentEditItem));
        OnPropertyChanged(nameof(IsAddingNew));
        OnPropertyChanged(nameof(IsEditingItem));
        OnPropertyChanged(nameof(CanAddNew));
        OnPropertyChanged(nameof(CanAddNewItem));
        OnPropertyChanged(nameof(CanCancelEdit));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private sealed class SelectorRootGroup : CollectionViewGroup
    {
        public SelectorRootGroup()
            : base(name: null!)
        {
        }

        public override bool IsBottomLevel => false;
    }
}

/// <summary>
/// Selects a grouping description as a function of the parent group and level.
/// </summary>
public delegate GroupDescription? GroupDescriptionSelectorCallback(CollectionViewGroup group, int level);
