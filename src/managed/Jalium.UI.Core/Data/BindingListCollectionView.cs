using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Xml;

namespace Jalium.UI.Data;

/// <summary>
/// Implements a CollectionView for collections that implement IBindingList.
/// </summary>
public sealed class BindingListCollectionView : CollectionView,
    IComparer,
    IEditableCollectionView,
    ICollectionViewLiveShaping,
    IItemProperties
{
    private readonly IBindingList _list;
    private readonly IBindingListView? _listView;
    private readonly ObservableCollection<string> _liveSortingProperties = new();
    private readonly ObservableCollection<string> _liveFilteringProperties = new();
    private readonly ObservableCollection<string> _liveGroupingProperties = new();
    private readonly CollectionViewGroup _selectorRoot = new SelectorRootGroup();
    private ArrayList _cachedList;
    private System.ComponentModel.SortDescriptionCollection? _sortDescriptions;
    private string? _customFilter;
    private GroupDescriptionSelectorCallback? _groupBySelector;
    private object? _newItem;
    private int _newItemIndex = -1;
    private object? _editItem;
    private NewItemPlaceholderPosition _newItemPlaceholderPosition;
    private bool _isDataInGroupOrder;
    private bool? _isLiveGrouping = false;
    private bool _isApplyingSourceShaping;

    /// <summary>
    /// Initializes a new instance of the BindingListCollectionView class.
    /// </summary>
    public BindingListCollectionView(IBindingList list)
        : base(list)
    {
        _list = list ?? throw new ArgumentNullException(nameof(list));
        _listView = list as IBindingListView;
        _cachedList = new ArrayList(list);
        if (list.SupportsChangeNotification)
        {
            list.ListChanged += OnListChanged;
        }

        if (CanSort)
        {
            _sortDescriptions = base.SortDescriptions;
            ((INotifyCollectionChanged)_sortDescriptions).CollectionChanged += OnSortDescriptionsChanged;
        }

        _liveGroupingProperties.CollectionChanged += OnLiveGroupingPropertiesChanged;
        RefreshOverride();
    }

    /// <summary>
    /// Gets a value that indicates whether this view supports filtering.
    /// </summary>
    public override bool CanFilter => false;

    /// <summary>
    /// Gets a value that indicates whether this view supports sorting.
    /// </summary>
    public override bool CanSort => _list?.SupportsSorting == true;

    /// <summary>
    /// Gets a value that indicates whether this view supports grouping.
    /// </summary>
    public override bool CanGroup => true;

    /// <inheritdoc />
    public override System.ComponentModel.SortDescriptionCollection SortDescriptions =>
        CanSort
            ? (_sortDescriptions ??= base.SortDescriptions)
            : System.ComponentModel.SortDescriptionCollection.Empty;

    /// <inheritdoc />
    public override int Count
    {
        get
        {
            VerifyRefreshNotDeferred();
            return GetDisplayItems().Count;
        }
    }

    /// <inheritdoc />
    public override bool IsEmpty => Count == 0;

    /// <summary>
    /// Gets or sets the custom filter string.
    /// </summary>
    public string? CustomFilter
    {
        get => _customFilter;
        set
        {
            if (!CanCustomFilter)
            {
                throw new NotSupportedException("The source binding list does not support custom filtering.");
            }

            EnsureNotAddingOrEditing(nameof(CustomFilter));
            if (_customFilter == value)
            {
                return;
            }

            _customFilter = value;
            RefreshOrDefer();
            OnPropertyChanged(nameof(CustomFilter));
        }
    }

    /// <summary>
    /// Gets whether the underlying <see cref="IBindingListView"/> supports custom filtering.
    /// </summary>
    public bool CanCustomFilter => _listView?.SupportsFiltering == true;

    /// <summary>
    /// Gets or sets the grouping selector.
    /// </summary>
    [DefaultValue(null)]
    public GroupDescriptionSelectorCallback? GroupBySelector
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
    /// Gets or sets whether input items are already ordered for grouping.
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

    /// <summary>
    /// Gets a value that indicates whether a new item can be added to the collection.
    /// </summary>
    public bool CanAddNew => !IsEditingItem && _list.AllowNew;

    /// <summary>
    /// Gets a value that indicates whether an item can be removed from the collection.
    /// </summary>
    public bool CanRemove => !IsEditingItem && !IsAddingNew && _list.AllowRemove;

    /// <summary>
    /// Gets a value that indicates whether the collection view supports canceling changes to an edit item.
    /// </summary>
    public bool CanCancelEdit => _editItem is IEditableObject;

    /// <summary>
    /// Gets the item that is being added during the current add transaction.
    /// </summary>
    public object? CurrentAddItem => _newItem;

    /// <summary>
    /// Gets the item that is being edited during the current edit transaction.
    /// </summary>
    public object? CurrentEditItem => _editItem;

    /// <summary>
    /// Gets a value that indicates whether an add transaction is in progress.
    /// </summary>
    public bool IsAddingNew => _newItem != null;

    /// <summary>
    /// Gets a value that indicates whether an edit transaction is in progress.
    /// </summary>
    public bool IsEditingItem => _editItem != null;

    /// <summary>
    /// Gets or sets the new-item placeholder position.
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
                    "NewItemPlaceholderPosition cannot change during an add transaction.");
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
    public bool CanChangeLiveSorting => false;

    /// <summary>
    /// Gets whether live filtering can be toggled.
    /// </summary>
    public bool CanChangeLiveFiltering => false;

    /// <summary>
    /// Gets whether live grouping can be toggled.
    /// </summary>
    public bool CanChangeLiveGrouping => true;

    /// <summary>
    /// Gets live-sorting state; ordinary binding lists cannot report or change it.
    /// </summary>
    public bool? IsLiveSorting
    {
        get => null;
        set => throw new InvalidOperationException("This binding-list view cannot change live sorting.");
    }

    /// <summary>
    /// Gets live-filtering state; ordinary binding lists cannot report or change it.
    /// </summary>
    public bool? IsLiveFiltering
    {
        get => null;
        set => throw new InvalidOperationException("This binding-list view cannot change live filtering.");
    }

    /// <summary>
    /// Gets or sets whether grouping responds to item changes reported by the binding list.
    /// </summary>
    public bool? IsLiveGrouping
    {
        get => _isLiveGrouping;
        set
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (_isLiveGrouping == value)
            {
                return;
            }

            _isLiveGrouping = value;
            RefreshOrDefer();
            OnPropertyChanged(nameof(IsLiveGrouping));
        }
    }

    /// <summary>
    /// Gets live-sorting property names.
    /// </summary>
    public ObservableCollection<string> LiveSortingProperties => _liveSortingProperties;

    /// <summary>
    /// Gets live-filtering property names.
    /// </summary>
    public ObservableCollection<string> LiveFilteringProperties => _liveFilteringProperties;

    /// <summary>
    /// Gets live-grouping property names.
    /// </summary>
    public ObservableCollection<string> LiveGroupingProperties => _liveGroupingProperties;

    /// <summary>
    /// Gets item metadata supplied by the binding list or its item type.
    /// </summary>
    public ReadOnlyCollection<ItemPropertyInfo>? ItemProperties => GetItemProperties();

    /// <summary>
    /// Starts an add transaction and returns the pending new item.
    /// </summary>
    public object AddNew()
    {
        VerifyRefreshNotDeferred();
        if (IsEditingItem)
        {
            CommitEdit();
        }

        CommitNew();
        if (!CanAddNew)
        {
            throw new InvalidOperationException("The binding list does not allow new items.");
        }

        var newItem = _list.AddNew() ?? throw new InvalidOperationException("IBindingList.AddNew returned null.");
        _newItem = newItem;
        _newItemIndex = _list.IndexOf(newItem);
        if (newItem is IEditableObject editable)
        {
            editable.BeginEdit();
        }

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        MoveCurrentTo(newItem);
        RaiseTransactionProperties();
        return newItem;
    }

    /// <summary>
    /// Ends the add transaction and saves the pending new item.
    /// </summary>
    public void CommitNew()
    {
        if (_newItem == null)
        {
            return;
        }

        var item = _newItem;
        var index = _newItemIndex;
        _newItem = null;
        _newItemIndex = -1;
        if (_list is ICancelAddNew cancelAddNew && index >= 0)
        {
            cancelAddNew.EndNew(index);
        }

        if (item is IEditableObject editable)
        {
            editable.EndEdit();
        }

        RefreshOrDefer();
        RaiseTransactionProperties();
    }

    /// <summary>
    /// Ends the add transaction and discards the pending new item.
    /// </summary>
    public void CancelNew()
    {
        if (_newItem == null)
        {
            return;
        }

        var item = _newItem;
        var index = _newItemIndex;
        _newItem = null;
        _newItemIndex = -1;
        if (item is IEditableObject editable)
        {
            editable.CancelEdit();
        }

        if (_list is ICancelAddNew cancelAddNew && index >= 0)
        {
            cancelAddNew.CancelNew(index);
        }
        else
        {
            _list.Remove(item);
        }

        RefreshOrDefer();
        RaiseTransactionProperties();
    }

    /// <summary>
    /// Begins an edit transaction on the specified item.
    /// </summary>
    public void EditItem(object item)
    {
        ArgumentNullException.ThrowIfNull(item);
        VerifyRefreshNotDeferred();
        if (ReferenceEquals(item, NewItemPlaceholder) || IndexOf(item) < 0)
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
    /// Ends the edit transaction and saves the pending changes.
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
    /// Ends the edit transaction and discards the pending changes.
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
    /// Removes the specified item from the collection.
    /// </summary>
    public void Remove(object item)
    {
        if (!CanRemove)
        {
            throw new InvalidOperationException("The binding list does not allow item removal.");
        }

        if (ReferenceEquals(item, NewItemPlaceholder))
        {
            throw new InvalidOperationException("The new-item placeholder cannot be removed.");
        }

        _list.Remove(item);
    }

    /// <summary>
    /// Removes the item at the specified position from the collection.
    /// </summary>
    public void RemoveAt(int index)
    {
        Remove(GetItemAt(index));
    }

    /// <inheritdoc />
    public override bool Contains(object item)
    {
        VerifyRefreshNotDeferred();
        return GetDisplayItems().Contains(item);
    }

    /// <inheritdoc />
    public override object GetItemAt(int index)
    {
        VerifyRefreshNotDeferred();
        var items = GetDisplayItems();
        if ((uint)index >= (uint)items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return items[index]!;
    }

    /// <inheritdoc />
    public override int IndexOf(object item)
    {
        VerifyRefreshNotDeferred();
        return GetDisplayItems().IndexOf(item);
    }

    /// <inheritdoc />
    public override bool PassesFilter(object item) => true;

    /// <inheritdoc />
    public override void DetachFromSourceCollection()
    {
        if (_list.SupportsChangeNotification)
        {
            _list.ListChanged -= OnListChanged;
        }

        base.DetachFromSourceCollection();
    }

    /// <inheritdoc />
    protected override void OnAllowsCrossThreadChangesChanged()
    {
        base.OnAllowsCrossThreadChangesChanged();
        _cachedList = new ArrayList(_list);
    }

    /// <inheritdoc />
    [Obsolete("Replaced by OnAllowsCrossThreadChangesChanged")]
    protected override void OnBeginChangeLogging(NotifyCollectionChangedEventArgs args)
    {
#pragma warning disable CS0618
        base.OnBeginChangeLogging(args);
#pragma warning restore CS0618
    }

    /// <inheritdoc />
    protected override void ProcessCollectionChanged(NotifyCollectionChangedEventArgs args) =>
        ProcessCollectionChangedCore(args);

    /// <inheritdoc />
    protected override void RefreshOverride()
    {
        ApplySourceShaping();
        base.RefreshOverride();
        SynchronizeCurrencyWithDisplay();
        _cachedList = new ArrayList(_list);
    }

    /// <inheritdoc />
    protected override IEnumerator GetEnumerator()
    {
        VerifyRefreshNotDeferred();
        return GetDisplayItems().GetEnumerator();
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

    int IComparer.Compare(object? x, object? y) => IndexOf(x!) - IndexOf(y!);

    private ArrayList GetDisplayItems()
    {
        var items = new ArrayList(EffectiveItems);
        if (_newItem != null)
        {
            items.Remove(_newItem);
        }

        if (NewItemPlaceholderPosition == NewItemPlaceholderPosition.AtBeginning)
        {
            items.Insert(0, NewItemPlaceholder);
            if (_newItem != null)
            {
                items.Insert(1, _newItem);
            }
        }
        else if (NewItemPlaceholderPosition == NewItemPlaceholderPosition.AtEnd)
        {
            if (_newItem != null)
            {
                items.Add(_newItem);
            }
            items.Add(NewItemPlaceholder);
        }
        else if (_newItem != null && !items.Contains(_newItem))
        {
            items.Add(_newItem);
        }

        return items;
    }

    private void ProcessCollectionChangedCore(NotifyCollectionChangedEventArgs args)
    {
        base.ProcessCollectionChanged(args);
        SynchronizeCurrencyWithDisplay();
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
            throw new InvalidOperationException($"{memberName} cannot change during an add or edit transaction.");
        }
    }

    private void ApplySourceShaping()
    {
        if (_isApplyingSourceShaping)
        {
            return;
        }

        try
        {
            _isApplyingSourceShaping = true;
            if (_listView?.SupportsFiltering == true && _listView.Filter != _customFilter)
            {
                SetListFilter(_listView, _customFilter);
            }

            if (!CanSort)
            {
                return;
            }

            var sorts = _sortDescriptions ?? base.SortDescriptions;
            if (sorts.Count == 0)
            {
                if (_list.IsSorted)
                {
                    _list.RemoveSort();
                }
                return;
            }

            if (_listView?.SupportsAdvancedSorting == true)
            {
                var sourceSorts = sorts.Select(sort => new ListSortDescription(
                    GetPropertyDescriptor(sort.PropertyName),
                    sort.Direction == System.ComponentModel.ListSortDirection.Descending
                        ? System.ComponentModel.ListSortDirection.Descending
                        : System.ComponentModel.ListSortDirection.Ascending)).ToArray();
                _listView.ApplySort(new ListSortDescriptionCollection(sourceSorts));
                return;
            }

            if (sorts.Count > 1)
            {
                throw new InvalidOperationException("This binding list supports only one sort description.");
            }

            var sort = sorts[0];
            _list.ApplySort(
                GetPropertyDescriptor(sort.PropertyName),
                sort.Direction == System.ComponentModel.ListSortDirection.Descending
                    ? System.ComponentModel.ListSortDirection.Descending
                    : System.ComponentModel.ListSortDirection.Ascending);
        }
        finally
        {
            _isApplyingSourceShaping = false;
        }
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "IBindingListView.Filter is an explicit runtime data-binding contract; the source implementation resolves the caller-provided filter against item metadata that consumers must preserve under trimming.")]
    private static void SetListFilter(IBindingListView listView, string? filter) =>
        listView.Filter = filter;

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "Binding-list sorting resolves descriptors from the source's runtime item schema, which is part of the consumer's binding contract.")]
    private PropertyDescriptor GetPropertyDescriptor(string? propertyName)
    {
        var properties = _list is ITypedList typedList
            ? typedList.GetItemProperties(null)
            : GetItemType() is { } itemType
                ? TypeDescriptor.GetProperties(itemType)
                : null;
        return properties?[propertyName ?? string.Empty]
            ?? throw new InvalidOperationException($"The item property '{propertyName}' was not found.");
    }

    private void OnSortDescriptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        EnsureNotAddingOrEditing(nameof(SortDescriptions));
        RefreshOrDefer();
    }

    private void OnLiveGroupingPropertiesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (IsLiveGrouping == true)
        {
            RefreshOrDefer();
        }
    }

    private void OnListChanged(object? sender, ListChangedEventArgs e)
    {
        if (_isApplyingSourceShaping)
        {
            return;
        }

        NotifyCollectionChangedEventArgs args;
        switch (e.ListChangedType)
        {
            case ListChangedType.ItemAdded when e.NewIndex >= 0 && e.NewIndex < _list.Count:
                args = new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Add,
                    _list[e.NewIndex],
                    e.NewIndex);
                break;

            case ListChangedType.ItemDeleted when e.NewIndex >= 0 && e.NewIndex < _cachedList.Count:
                args = new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Remove,
                    _cachedList[e.NewIndex],
                    e.NewIndex);
                break;

            case ListChangedType.ItemMoved when e.NewIndex >= 0 && e.NewIndex < _list.Count:
                args = new NotifyCollectionChangedEventArgs(
                    NotifyCollectionChangedAction.Move,
                    _list[e.NewIndex],
                    e.NewIndex,
                    e.OldIndex);
                break;

            case ListChangedType.ItemChanged when e.NewIndex >= 0 && e.NewIndex < _list.Count:
                var item = _list[e.NewIndex];
                args = IsLiveGrouping == true
                    ? new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)
                    : new NotifyCollectionChangedEventArgs(
                        NotifyCollectionChangedAction.Replace,
                        item,
                        item,
                        e.NewIndex);
                break;

            default:
                args = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset);
                break;
        }

        ProcessCollectionChanged(args);
        _cachedList = new ArrayList(_list);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "Binding-list item metadata is part of the runtime data-binding contract.")]
    private ReadOnlyCollection<ItemPropertyInfo>? GetItemProperties()
    {
        var descriptors = _list is ITypedList typedList
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

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075:UnrecognizedReflectionPattern",
        Justification = "The binding-list view discovers IList<T> on the runtime source collection; bound source types preserve their interfaces under trimming.")]
    private Type? GetItemType()
    {
        var listType = _list.GetType();
        var genericList = listType.GetInterfaces()
            .Append(listType)
            .FirstOrDefault(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>));
        if (genericList != null)
        {
            return genericList.GetGenericArguments()[0];
        }

        foreach (var item in _list)
        {
            if (item != null)
            {
                return item.GetType();
            }
        }

        return null;
    }

    private void RaiseTransactionProperties()
    {
        OnPropertyChanged(nameof(CurrentAddItem));
        OnPropertyChanged(nameof(CurrentEditItem));
        OnPropertyChanged(nameof(IsAddingNew));
        OnPropertyChanged(nameof(IsEditingItem));
        OnPropertyChanged(nameof(CanAddNew));
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
/// Maps an XML namespace URI to a prefix for use with XmlDataProvider.
/// </summary>
public class XmlNamespaceMapping : ISupportInitialize
{
    private bool _isInitializing;

    /// <summary>
    /// Initializes a new instance of the XmlNamespaceMapping class.
    /// </summary>
    public XmlNamespaceMapping()
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified prefix and URI.
    /// </summary>
    public XmlNamespaceMapping(string prefix, Uri uri)
    {
        Prefix = prefix ?? throw new ArgumentNullException(nameof(prefix));
        Uri = uri ?? throw new ArgumentNullException(nameof(uri));
        ValidateState();
    }

    /// <summary>
    /// Gets or sets the prefix for the namespace.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the URI of the namespace.
    /// </summary>
    public Uri? Uri { get; set; }

    /// <inheritdoc />
    public void BeginInit() => _isInitializing = true;

    /// <inheritdoc />
    public void EndInit()
    {
        _isInitializing = false;
        ValidateState();
    }

    public override bool Equals(object? obj) =>
        obj is XmlNamespaceMapping other && Prefix == other.Prefix && Uri == other.Uri;

    public override int GetHashCode() => HashCode.Combine(Prefix, Uri);

    public static bool operator ==(XmlNamespaceMapping? mappingA, XmlNamespaceMapping? mappingB) =>
        Equals(mappingA, mappingB);

    public static bool operator !=(XmlNamespaceMapping? mappingA, XmlNamespaceMapping? mappingB) =>
        !Equals(mappingA, mappingB);

    private void ValidateState()
    {
        if (_isInitializing)
        {
            return;
        }

        if (Prefix.Contains(':'))
        {
            throw new ArgumentException("An XML namespace prefix cannot contain a colon.", nameof(Prefix));
        }

        if (Uri == null)
        {
            throw new InvalidOperationException("An XML namespace mapping requires a URI.");
        }
    }
}

/// <summary>
/// A collection of XmlNamespaceMapping objects that provides support for
/// adding and managing XML namespace mappings for use with XmlDataProvider.
/// </summary>
public class XmlNamespaceMappingCollection :
    XmlNamespaceManager,
    ICollection<XmlNamespaceMapping>,
    Jalium.UI.Markup.IAddChild
{
    private readonly List<XmlNamespaceMapping> _mappings = new();

    /// <summary>
    /// Initializes a new instance of the XmlNamespaceMappingCollection class.
    /// </summary>
    public XmlNamespaceMappingCollection()
        : base(new NameTable())
    {
    }

    /// <inheritdoc />
    public int Count => _mappings.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public void Add(XmlNamespaceMapping item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Uri == null)
        {
            throw new ArgumentException("The mapping must have a namespace URI.", nameof(item));
        }

        int existingIndex = _mappings.FindIndex(mapping =>
            string.Equals(mapping.Prefix, item.Prefix, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            _mappings[existingIndex] = item;
        }
        else
        {
            _mappings.Add(item);
        }

        AddNamespace(item.Prefix, item.Uri.ToString());
    }

    /// <inheritdoc />
    public void Clear()
    {
        foreach (XmlNamespaceMapping mapping in _mappings)
        {
            RemoveNamespace(mapping.Prefix, mapping.Uri!.ToString());
        }

        _mappings.Clear();
    }

    /// <inheritdoc />
    public bool Contains(XmlNamespaceMapping item) => _mappings.Contains(item);

    /// <inheritdoc />
    public void CopyTo(XmlNamespaceMapping[] array, int arrayIndex) =>
        _mappings.CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public bool Remove(XmlNamespaceMapping item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!_mappings.Remove(item))
        {
            return false;
        }

        if (item.Uri != null)
        {
            RemoveNamespace(item.Prefix, item.Uri.ToString());
        }

        return true;
    }

    /// <inheritdoc />
    public override IEnumerator GetEnumerator() => ProtectedGetEnumerator();

    IEnumerator<XmlNamespaceMapping> IEnumerable<XmlNamespaceMapping>.GetEnumerator() =>
        ProtectedGetEnumerator();

    /// <summary>
    /// Adds the specified object as a child. If the object is an XmlNamespaceMapping, it is added to the collection.
    /// </summary>
    /// <param name="value">The object to add as a child.</param>
    protected virtual void AddChild(object value)
    {
        if (value is XmlNamespaceMapping mapping)
        {
            Add(mapping);
        }
        else
        {
            throw new ArgumentException(
                $"Cannot add object of type '{value?.GetType().Name ?? "null"}' to XmlNamespaceMappingCollection. " +
                "Only XmlNamespaceMapping objects are accepted.",
                nameof(value));
        }
    }

    /// <summary>
    /// Adds the specified text content. This operation is not supported for XmlNamespaceMappingCollection
    /// and is silently ignored for whitespace text.
    /// </summary>
    /// <param name="text">The text to add.</param>
    protected virtual void AddText(string text)
    {
        // Ignore whitespace text; throw for non-whitespace
        if (!string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("XmlNamespaceMappingCollection does not support adding text content.");
        }
    }

    /// <summary>
    /// Returns a strongly typed enumerator over the current mappings.
    /// </summary>
    protected IEnumerator<XmlNamespaceMapping> ProtectedGetEnumerator() => _mappings.GetEnumerator();

    void Jalium.UI.Markup.IAddChild.AddChild(object value) => AddChild(value);

    void Jalium.UI.Markup.IAddChild.AddText(string text) => AddText(text);
}
