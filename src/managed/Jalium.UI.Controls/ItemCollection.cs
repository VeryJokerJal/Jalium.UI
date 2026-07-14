using CurrentChangingEventArgs = System.ComponentModel.CurrentChangingEventArgs;
using ICollectionView = System.ComponentModel.ICollectionView;
using IEditableCollectionView = System.ComponentModel.IEditableCollectionView;
using IEditableCollectionViewAddNewItem = System.ComponentModel.IEditableCollectionViewAddNewItem;
using ICollectionViewLiveShaping = System.ComponentModel.ICollectionViewLiveShaping;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Data;
using Jalium.UI.Media;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Collections;
using System.ComponentModel;

namespace Jalium.UI.Controls;

/// <summary>
/// WPF-compatible collection-view behavior for <see cref="ItemCollection"/>.
/// </summary>
public sealed partial class ItemCollection : Jalium.UI.Data.CollectionView, IList, IEditableCollectionView, IEditableCollectionViewAddNewItem, ICollectionViewLiveShaping, IItemProperties, IWeakEventListener
{
    private readonly System.ComponentModel.SortDescriptionCollection _itemSortDescriptions = new();
    private readonly ObservableCollection<System.ComponentModel.GroupDescription> _itemGroupDescriptions = new();
    private readonly ObservableCollection<string> _liveSortingProperties = new();
    private readonly ObservableCollection<string> _liveFilteringProperties = new();
    private readonly ObservableCollection<string> _liveGroupingProperties = new();
    private readonly HashSet<INotifyPropertyChanged> _liveItems = new();

    private CollectionView _directView = null!;
    private ICollectionView _activeView = null!;
    private IEnumerable? _itemsSource;
    private Predicate<object>? _itemFilter;
    private bool _isUsingItemsSource;
    private bool _isApplyingShaping;
    private int _suppressActiveEvents;
    private int _deferLevel;
    private bool _itemNeedsRefresh;
    private bool _deferredViewChanged;
    private IDisposable? _activeDefer;
    private bool? _requestedLiveSorting;
    private bool? _requestedLiveFiltering;
    private bool? _requestedLiveGrouping;
    private object? _fallbackCurrentAddItem;
    private NewItemPlaceholderPosition _fallbackNewItemPlaceholderPosition;

    private void InitializeCollectionView()
    {
        ((INotifyCollectionChanged)_itemSortDescriptions).CollectionChanged += OnShapingCollectionChanged;
        _itemGroupDescriptions.CollectionChanged += OnShapingCollectionChanged;
        _liveSortingProperties.CollectionChanged += OnLiveShapingPropertiesChanged;
        _liveFilteringProperties.CollectionChanged += OnLiveShapingPropertiesChanged;
        _liveGroupingProperties.CollectionChanged += OnLiveShapingPropertiesChanged;

        _directView = new CollectionView(_items);
        _directView.MoveCurrentToPosition(-1);
        _activeView = _directView;
        HookActiveView(_activeView);
    }

    /// <summary>
    /// Gets a value indicating whether the active view supports filtering.
    /// </summary>
    public override bool CanFilter => _activeView.CanFilter;

    /// <summary>
    /// Gets a value indicating whether the active view supports grouping.
    /// </summary>
    public override bool CanGroup => _isUsingItemsSource && _activeView.CanGroup;

    /// <summary>
    /// Gets a value indicating whether the active view supports sorting.
    /// </summary>
    public override bool CanSort => _activeView.CanSort;

    /// <inheritdoc />
    public override object? CurrentItem => _activeView.CurrentItem;

    /// <inheritdoc />
    public override int CurrentPosition => _activeView.CurrentPosition;

    /// <inheritdoc />
    public override Predicate<object>? Filter
    {
        get => _itemFilter;
        set
        {
            if (ReferenceEquals(_itemFilter, value))
            {
                return;
            }

            if (value != null && !CanFilter)
            {
                throw new NotSupportedException("The active collection view does not support filtering.");
            }

            _itemFilter = value;
            RequestShapingRefresh();
            OnPropertyChanged(nameof(Filter));
        }
    }

    /// <inheritdoc />
    public override ObservableCollection<System.ComponentModel.GroupDescription> GroupDescriptions =>
        _itemGroupDescriptions;

    /// <inheritdoc />
    public override ReadOnlyObservableCollection<object>? Groups =>
        CanGroup ? _activeView.Groups : null;

    /// <inheritdoc />
    public override bool IsCurrentAfterLast => !IsEmpty && _activeView.IsCurrentAfterLast;

    /// <inheritdoc />
    public override bool IsCurrentBeforeFirst => !IsEmpty && _activeView.IsCurrentBeforeFirst;

    /// <inheritdoc />
    public override bool IsEmpty => GetViewCount() == 0;

    /// <inheritdoc />
    public override System.ComponentModel.SortDescriptionCollection SortDescriptions =>
        _itemSortDescriptions;

    /// <inheritdoc />
    public override IEnumerable SourceCollection =>
        _isUsingItemsSource ? _activeView.SourceCollection : this;

    /// <inheritdoc />
    public override bool NeedsRefresh =>
        _itemNeedsRefresh || (_activeView is CollectionView view && view.NeedsRefresh);

    /// <summary>
    /// Gets a value indicating whether live filtering can be enabled.
    /// </summary>
    public bool CanChangeLiveFiltering => GetLiveShapingCapability(
        static live => live.CanChangeLiveFiltering);

    /// <summary>
    /// Gets a value indicating whether live grouping can be enabled.
    /// </summary>
    public bool CanChangeLiveGrouping => GetLiveShapingCapability(
        static live => live.CanChangeLiveGrouping);

    /// <summary>
    /// Gets a value indicating whether live sorting can be enabled.
    /// </summary>
    public bool CanChangeLiveSorting => GetLiveShapingCapability(
        static live => live.CanChangeLiveSorting);

    /// <summary>
    /// Gets or sets whether live filtering is enabled.
    /// </summary>
    public bool? IsLiveFiltering
    {
        get => GetLiveShapingValue(_requestedLiveFiltering, CanChangeLiveFiltering);
        set => SetLiveShapingValue(
            value,
            CanChangeLiveFiltering,
            ref _requestedLiveFiltering,
            nameof(IsLiveFiltering));
    }

    /// <summary>
    /// Gets or sets whether live grouping is enabled.
    /// </summary>
    public bool? IsLiveGrouping
    {
        get => GetLiveShapingValue(_requestedLiveGrouping, CanChangeLiveGrouping);
        set => SetLiveShapingValue(
            value,
            CanChangeLiveGrouping,
            ref _requestedLiveGrouping,
            nameof(IsLiveGrouping));
    }

    /// <summary>
    /// Gets or sets whether live sorting is enabled.
    /// </summary>
    public bool? IsLiveSorting
    {
        get => GetLiveShapingValue(_requestedLiveSorting, CanChangeLiveSorting);
        set => SetLiveShapingValue(
            value,
            CanChangeLiveSorting,
            ref _requestedLiveSorting,
            nameof(IsLiveSorting));
    }

    /// <summary>
    /// Gets the property names participating in live filtering.
    /// </summary>
    public ObservableCollection<string> LiveFilteringProperties => _liveFilteringProperties;

    /// <summary>
    /// Gets the property names participating in live grouping.
    /// </summary>
    public ObservableCollection<string> LiveGroupingProperties => _liveGroupingProperties;

    /// <summary>
    /// Gets the property names participating in live sorting.
    /// </summary>
    public ObservableCollection<string> LiveSortingProperties => _liveSortingProperties;

    /// <inheritdoc />
    public override IDisposable DeferRefresh()
    {
        _deferLevel++;
        if (_deferLevel == 1)
        {
            _activeDefer = _activeView.DeferRefresh();
        }

        return new ItemCollectionDeferHelper(this);
    }

    /// <inheritdoc />
    public override object GetItemAt(int index)
    {
        VerifyItemRefreshNotDeferred();
        ArgumentOutOfRangeException.ThrowIfNegative(index);

        if (_activeView is CollectionView collectionView)
        {
            return collectionView.GetItemAt(index);
        }

        var current = 0;
        foreach (var item in _activeView)
        {
            if (current++ == index)
            {
                return item!;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }

    /// <inheritdoc />
    public override bool MoveCurrentTo(object? item)
    {
        VerifyItemRefreshNotDeferred();
        return _activeView.MoveCurrentTo(item);
    }

    /// <inheritdoc />
    public override bool MoveCurrentToFirst()
    {
        VerifyItemRefreshNotDeferred();
        return _activeView.MoveCurrentToFirst();
    }

    /// <inheritdoc />
    public override bool MoveCurrentToLast()
    {
        VerifyItemRefreshNotDeferred();
        return _activeView.MoveCurrentToLast();
    }

    /// <inheritdoc />
    public override bool MoveCurrentToNext()
    {
        VerifyItemRefreshNotDeferred();
        return _activeView.MoveCurrentToNext();
    }

    /// <inheritdoc />
    public override bool MoveCurrentToPosition(int position)
    {
        VerifyItemRefreshNotDeferred();
        return _activeView.MoveCurrentToPosition(position);
    }

    /// <inheritdoc />
    public override bool MoveCurrentToPrevious()
    {
        VerifyItemRefreshNotDeferred();
        return _activeView.MoveCurrentToPrevious();
    }

    /// <inheritdoc />
    public override bool PassesFilter(object item)
    {
        VerifyItemRefreshNotDeferred();
        return _activeView is CollectionView collectionView
            ? collectionView.PassesFilter(item)
            : _activeView.Filter?.Invoke(item) ?? true;
    }

    /// <inheritdoc />
    protected override void RefreshOverride()
    {
        if (_activeView == null)
        {
            return;
        }

        if (_deferLevel > 0)
        {
            _itemNeedsRefresh = true;
            return;
        }

        var oldCurrentItem = _activeView.CurrentItem;
        var oldCurrentPosition = _activeView.CurrentPosition;
        _suppressActiveEvents++;
        try
        {
            if (!Equals(_activeView.Culture, base.Culture))
            {
                _activeView.Culture = base.Culture;
            }

            _activeView.Refresh();
            RestoreDirectCurrency(oldCurrentItem, oldCurrentPosition);
        }
        finally
        {
            _suppressActiveEvents--;
        }

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        RaiseViewPropertyChanges();
        RebuildLiveItemSubscriptions();
    }

    /// <inheritdoc />
    protected override IEnumerator GetEnumerator()
    {
        VerifyItemRefreshNotDeferred();
        return ((IEnumerable)_activeView).GetEnumerator();
    }

    internal void SetItemsSource(IEnumerable? source)
    {
        if (source != null && _items.Count != 0)
        {
            throw new InvalidOperationException(
                "Items collection must be empty before using ItemsSource.");
        }

        if (ReferenceEquals(_itemsSource, source) && _isUsingItemsSource == (source != null))
        {
            return;
        }

        UnhookActiveView(_activeView);
        _itemsSource = source;
        _isUsingItemsSource = source != null;

        if (source == null)
        {
            _activeView = _directView;
        }
        else if (ReferenceEquals(source, this))
        {
            throw new InvalidOperationException("An ItemCollection cannot use itself as ItemsSource.");
        }
        else
        {
            _activeView = source as ICollectionView ?? CollectionViewSource.GetDefaultView(source);
        }

        ApplyShapingToActiveView();
        HookActiveView(_activeView);
        RebuildLiveItemSubscriptions();

        // ItemsControl already performs its own Reset work in the dependency-property
        // callback. Other observers still need the ItemCollection view notification.
        OnCollectionChanged(
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset),
            _owner);
        RaiseViewPropertyChanges();
    }

    private int GetViewCount()
    {
        VerifyItemRefreshNotDeferred();
        if (_activeView is CollectionView collectionView)
        {
            return collectionView.Count;
        }

        if (_activeView is ICollection collection)
        {
            return collection.Count;
        }

        var count = 0;
        foreach (var _ in _activeView)
        {
            count++;
        }

        return count;
    }

    private bool ViewContains(object item)
    {
        VerifyItemRefreshNotDeferred();
        return _activeView.Contains(item);
    }

    private int ViewIndexOf(object item)
    {
        VerifyItemRefreshNotDeferred();
        if (_activeView is CollectionView collectionView)
        {
            return collectionView.IndexOf(item);
        }

        var index = 0;
        foreach (var candidate in _activeView)
        {
            if (Equals(candidate, item))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    private int GetDirectSourceIndex(int viewIndex)
    {
        VerifyWritable();
        var viewItem = GetItemAt(viewIndex);
        for (var index = 0; index < _items.Count; index++)
        {
            if (ReferenceEquals(_items[index], viewItem))
            {
                return index;
            }
        }

        var equalIndex = _items.IndexOf(viewItem);
        if (equalIndex < 0)
        {
            throw new InvalidOperationException("The view item is not present in the direct-items collection.");
        }

        return equalIndex;
    }

    private void CopyViewTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (array.Rank != 1)
        {
            throw new ArgumentException("Only single-dimensional arrays are supported.", nameof(array));
        }

        if (array.GetLowerBound(0) != 0)
        {
            throw new ArgumentException("Non-zero lower-bound arrays are not supported.", nameof(array));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(index);
        var count = GetViewCount();
        if (array.Length - index < count)
        {
            throw new ArgumentException("The destination array is too small.", nameof(array));
        }

        var destination = index;
        foreach (var item in _activeView)
        {
            array.SetValue(item, destination++);
        }
    }

    private IEnumerator<object> GetTypedViewEnumerator() => EnumerateView().GetEnumerator();

    private IEnumerable<object> EnumerateView()
    {
        VerifyItemRefreshNotDeferred();
        foreach (var item in _activeView)
        {
            yield return item!;
        }
    }

    private void OnDirectItemsChanged(NotifyCollectionChangedEventArgs eventArgs)
    {
        var oldCurrentItem = _directView.CurrentItem;
        var oldCurrentPosition = _directView.CurrentPosition;
        var wasBeforeFirst = oldCurrentPosition < 0;

        _suppressActiveEvents++;
        try
        {
            _directView.Refresh();
            if (wasBeforeFirst && _directView.Count > 0)
            {
                _directView.MoveCurrentToPosition(-1);
            }
        }
        finally
        {
            _suppressActiveEvents--;
        }

        var visibleShapeChanged = _itemFilter != null || _itemSortDescriptions.Count > 0;
        OnCollectionChanged(visibleShapeChanged
            ? new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)
            : eventArgs);

        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsEmpty));
        if (!ReferenceEquals(oldCurrentItem, _directView.CurrentItem) ||
            oldCurrentPosition != _directView.CurrentPosition)
        {
            OnCurrentChanged();
        }
        else
        {
            OnPropertyChanged(nameof(CurrentPosition));
            OnPropertyChanged(nameof(IsCurrentAfterLast));
            OnPropertyChanged(nameof(IsCurrentBeforeFirst));
        }

        RebuildLiveItemSubscriptions();
    }

    private void RequestShapingRefresh()
    {
        if (_isApplyingShaping)
        {
            return;
        }

        if (_deferLevel > 0)
        {
            _itemNeedsRefresh = true;
            return;
        }

        ApplyShapingAndNotify();
    }

    private void ApplyShapingAndNotify()
    {
        var oldCurrentItem = _activeView.CurrentItem;
        var oldCurrentPosition = _activeView.CurrentPosition;
        _suppressActiveEvents++;
        try
        {
            using (_activeView.DeferRefresh())
            {
                ApplyShapingToActiveView();
            }

            RestoreDirectCurrency(oldCurrentItem, oldCurrentPosition);
        }
        finally
        {
            _suppressActiveEvents--;
        }

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        RaiseViewPropertyChanges();
        RebuildLiveItemSubscriptions();
    }

    private void ApplyShapingToActiveView()
    {
        if (_isApplyingShaping)
        {
            return;
        }

        _isApplyingShaping = true;
        try
        {
            if (!Equals(_activeView.Culture, base.Culture))
            {
                _activeView.Culture = base.Culture;
            }

            if (_itemFilter != null && !_activeView.CanFilter)
            {
                throw new NotSupportedException("The active collection view does not support filtering.");
            }

            if (!ReferenceEquals(_activeView.Filter, _itemFilter))
            {
                _activeView.Filter = _itemFilter;
            }

            if (_itemSortDescriptions.Count > 0 && !_activeView.CanSort)
            {
                throw new NotSupportedException("The active collection view does not support sorting.");
            }

            SynchronizeSortDescriptions(_activeView.SortDescriptions, _itemSortDescriptions);

            if (_isUsingItemsSource && _activeView.CanGroup)
            {
                SynchronizeGroupDescriptions(_activeView.GroupDescriptions, _itemGroupDescriptions);
            }
            else if (_activeView.GroupDescriptions.Count > 0)
            {
                _activeView.GroupDescriptions.Clear();
            }

            ApplyLiveShapingToActiveView();
        }
        finally
        {
            _isApplyingShaping = false;
        }
    }

    private static void SynchronizeSortDescriptions(
        System.ComponentModel.SortDescriptionCollection destination,
        System.ComponentModel.SortDescriptionCollection source)
    {
        if (destination.Count == source.Count)
        {
            var equal = true;
            for (var index = 0; index < source.Count; index++)
            {
                if (destination[index].PropertyName != source[index].PropertyName ||
                    destination[index].Direction != source[index].Direction)
                {
                    equal = false;
                    break;
                }
            }

            if (equal)
            {
                return;
            }
        }

        destination.Clear();
        foreach (var description in source)
        {
            destination.Add(description);
        }
    }

    private static void SynchronizeGroupDescriptions(
        ObservableCollection<System.ComponentModel.GroupDescription> destination,
        ObservableCollection<System.ComponentModel.GroupDescription> source)
    {
        if (destination.Count == source.Count && destination.SequenceEqual(source))
        {
            return;
        }

        destination.Clear();
        foreach (var description in source)
        {
            destination.Add(description);
        }
    }

    private void OnShapingCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RequestShapingRefresh();

    private void OnLiveShapingPropertiesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_deferLevel > 0)
        {
            _itemNeedsRefresh = true;
            return;
        }

        ApplyLiveShapingToActiveView();
        RebuildLiveItemSubscriptions();
    }

    private bool GetLiveShapingCapability(Func<ICollectionViewLiveShaping, bool> selector)
    {
        if (!_isUsingItemsSource)
        {
            return false;
        }

        if (_activeView is ICollectionViewLiveShaping liveShaping)
        {
            return selector(liveShaping);
        }

        return _itemsSource is IList;
    }

    private bool? GetLiveShapingValue(bool? requestedValue, bool canChange)
    {
        if (!_isUsingItemsSource || !canChange)
        {
            return null;
        }

        return requestedValue ?? false;
    }

    private void SetLiveShapingValue(
        bool? value,
        bool canChange,
        ref bool? storage,
        string propertyName)
    {
        if (!_isUsingItemsSource || !canChange)
        {
            return;
        }

        if (storage == value)
        {
            return;
        }

        storage = value;
        ApplyLiveShapingToActiveView();
        RebuildLiveItemSubscriptions();
        OnPropertyChanged(propertyName);
    }

    private void ApplyLiveShapingToActiveView()
    {
        if (_activeView is not ICollectionViewLiveShaping liveShaping)
        {
            return;
        }

        SynchronizeStrings(liveShaping.LiveSortingProperties, _liveSortingProperties);
        SynchronizeStrings(liveShaping.LiveFilteringProperties, _liveFilteringProperties);
        SynchronizeStrings(liveShaping.LiveGroupingProperties, _liveGroupingProperties);

        if (liveShaping.CanChangeLiveSorting)
        {
            liveShaping.IsLiveSorting = _requestedLiveSorting ?? false;
        }

        if (liveShaping.CanChangeLiveFiltering)
        {
            liveShaping.IsLiveFiltering = _requestedLiveFiltering ?? false;
        }

        if (liveShaping.CanChangeLiveGrouping)
        {
            liveShaping.IsLiveGrouping = _requestedLiveGrouping ?? false;
        }
    }

    private static void SynchronizeStrings(
        ObservableCollection<string> destination,
        ObservableCollection<string> source)
    {
        if (destination.SequenceEqual(source, StringComparer.Ordinal))
        {
            return;
        }

        destination.Clear();
        foreach (var propertyName in source)
        {
            destination.Add(propertyName);
        }
    }

    private void HookActiveView(ICollectionView view)
    {
        view.CollectionChanged += OnActiveViewCollectionChanged;
        view.CurrentChanging += OnActiveViewCurrentChanging;
        view.CurrentChanged += OnActiveViewCurrentChanged;
        if (view is INotifyPropertyChanged propertyChanged)
        {
            propertyChanged.PropertyChanged += OnActiveViewPropertyChanged;
        }
    }

    private void UnhookActiveView(ICollectionView view)
    {
        view.CollectionChanged -= OnActiveViewCollectionChanged;
        view.CurrentChanging -= OnActiveViewCurrentChanging;
        view.CurrentChanged -= OnActiveViewCurrentChanged;
        if (view is INotifyPropertyChanged propertyChanged)
        {
            propertyChanged.PropertyChanged -= OnActiveViewPropertyChanged;
        }

        ClearLiveItemSubscriptions();
    }

    private void OnActiveViewCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_suppressActiveEvents > 0)
        {
            _deferredViewChanged = true;
            return;
        }

        RebuildLiveItemSubscriptions();
        OnCollectionChanged(e, _owner);
        RaiseViewPropertyChanges();
    }

    private void OnActiveViewCurrentChanging(object? sender, CurrentChangingEventArgs e)
    {
        if (_suppressActiveEvents == 0)
        {
            OnCurrentChanging(e);
        }
    }

    private void OnActiveViewCurrentChanged(object? sender, EventArgs e)
    {
        if (_suppressActiveEvents == 0)
        {
            OnCurrentChanged();
        }
    }

    private void OnActiveViewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressActiveEvents == 0 && !string.IsNullOrEmpty(e.PropertyName))
        {
            OnPropertyChanged(e.PropertyName);
        }
    }

    private void RaiseViewPropertyChanges()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CurrentItem));
        OnPropertyChanged(nameof(CurrentPosition));
        OnPropertyChanged(nameof(IsCurrentAfterLast));
        OnPropertyChanged(nameof(IsCurrentBeforeFirst));
        OnPropertyChanged(nameof(Groups));
        OnPropertyChanged(nameof(NeedsRefresh));
        OnPropertyChanged(nameof(SourceCollection));
        OnPropertyChanged(nameof(CanFilter));
        OnPropertyChanged(nameof(CanGroup));
        OnPropertyChanged(nameof(CanSort));
        OnPropertyChanged(nameof(CanChangeLiveFiltering));
        OnPropertyChanged(nameof(CanChangeLiveGrouping));
        OnPropertyChanged(nameof(CanChangeLiveSorting));
        OnPropertyChanged(nameof(IsLiveFiltering));
        OnPropertyChanged(nameof(IsLiveGrouping));
        OnPropertyChanged(nameof(IsLiveSorting));
    }

    private void RebuildLiveItemSubscriptions()
    {
        ClearLiveItemSubscriptions();
        if (_activeView is ICollectionViewLiveShaping ||
            (!(_requestedLiveSorting ?? false) &&
             !(_requestedLiveFiltering ?? false) &&
             !(_requestedLiveGrouping ?? false)))
        {
            return;
        }

        foreach (var item in _activeView)
        {
            if (item is INotifyPropertyChanged notify && _liveItems.Add(notify))
            {
                notify.PropertyChanged += OnLiveItemPropertyChanged;
            }
        }
    }

    private void ClearLiveItemSubscriptions()
    {
        foreach (var item in _liveItems)
        {
            item.PropertyChanged -= OnLiveItemPropertyChanged;
        }

        _liveItems.Clear();
    }

    private void OnLiveItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var propertyName = e.PropertyName;
        if (ShouldRefreshLiveProperty(propertyName, _requestedLiveSorting, _liveSortingProperties) ||
            ShouldRefreshLiveProperty(propertyName, _requestedLiveFiltering, _liveFilteringProperties) ||
            ShouldRefreshLiveProperty(propertyName, _requestedLiveGrouping, _liveGroupingProperties))
        {
            RefreshOverride();
        }
    }

    private static bool ShouldRefreshLiveProperty(
        string? propertyName,
        bool? enabled,
        ObservableCollection<string> propertyNames)
    {
        if (enabled != true)
        {
            return false;
        }

        return string.IsNullOrEmpty(propertyName) ||
               propertyNames.Count == 0 ||
               propertyNames.Contains(propertyName);
    }

    private void VerifyItemRefreshNotDeferred()
    {
        if (_deferLevel > 0 && NeedsRefresh)
        {
            throw new InvalidOperationException(
                "Cannot change or check the contents or Current position of CollectionView while Refresh is being deferred.");
        }
    }

    private void EndDeferRefresh()
    {
        if (_deferLevel <= 0)
        {
            return;
        }

        _deferLevel--;
        if (_deferLevel != 0)
        {
            return;
        }

        var shouldNotify = _itemNeedsRefresh || _deferredViewChanged;
        var oldCurrentItem = _activeView.CurrentItem;
        var oldCurrentPosition = _activeView.CurrentPosition;
        _suppressActiveEvents++;
        try
        {
            if (_itemNeedsRefresh)
            {
                ApplyShapingToActiveView();
            }

            _activeDefer?.Dispose();
            RestoreDirectCurrency(oldCurrentItem, oldCurrentPosition);
        }
        finally
        {
            _activeDefer = null;
            _suppressActiveEvents--;
            shouldNotify |= _deferredViewChanged;
            _itemNeedsRefresh = false;
            _deferredViewChanged = false;
        }

        if (shouldNotify)
        {
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            RaiseViewPropertyChanges();
            RebuildLiveItemSubscriptions();
        }
    }

    private void RestoreDirectCurrency(object? oldCurrentItem, int oldCurrentPosition)
    {
        if (_isUsingItemsSource || oldCurrentItem != null)
        {
            return;
        }

        if (oldCurrentPosition < 0 && _directView.Count > 0)
        {
            _directView.MoveCurrentToPosition(-1);
        }
        else if (oldCurrentPosition >= 0 && _directView.Count > 0)
        {
            _directView.MoveCurrentToPosition(_directView.Count);
        }
    }

    private IEditableCollectionView? EditableView =>
        _isUsingItemsSource ? _activeView as IEditableCollectionView : null;

    private IList? MutableItemsSource => _itemsSource as IList;

    bool IEditableCollectionView.CanAddNew => EditableView?.CanAddNew ?? false;

    bool IEditableCollectionView.CanCancelEdit => EditableView?.CanCancelEdit ?? false;

    bool IEditableCollectionView.CanRemove => EditableView?.CanRemove ?? false;

    object? IEditableCollectionView.CurrentAddItem =>
        _fallbackCurrentAddItem ?? EditableView?.CurrentAddItem;

    object? IEditableCollectionView.CurrentEditItem => EditableView?.CurrentEditItem;

    bool IEditableCollectionView.IsAddingNew =>
        _fallbackCurrentAddItem != null || EditableView?.IsAddingNew == true;

    bool IEditableCollectionView.IsEditingItem => EditableView?.IsEditingItem == true;

    NewItemPlaceholderPosition IEditableCollectionView.NewItemPlaceholderPosition
    {
        get => EditableView?.NewItemPlaceholderPosition ?? _fallbackNewItemPlaceholderPosition;
        set
        {
            if (EditableView != null)
            {
                EditableView.NewItemPlaceholderPosition = value;
            }
            else if (_isUsingItemsSource)
            {
                _fallbackNewItemPlaceholderPosition = value;
            }
            else
            {
                throw new InvalidOperationException("The direct-items view is not editable.");
            }
        }
    }

    object? IEditableCollectionView.AddNew() =>
        EditableView?.AddNew() ?? throw new InvalidOperationException("The active view cannot add new items.");

    void IEditableCollectionView.CancelEdit() => EditableView?.CancelEdit();

    void IEditableCollectionView.CancelNew()
    {
        if (_fallbackCurrentAddItem != null && MutableItemsSource != null)
        {
            MutableItemsSource.Remove(_fallbackCurrentAddItem);
            _fallbackCurrentAddItem = null;
            RefreshPlainItemsSourceIfNeeded();
            return;
        }

        EditableView?.CancelNew();
    }

    void IEditableCollectionView.CommitEdit() => EditableView?.CommitEdit();

    void IEditableCollectionView.CommitNew()
    {
        if (_fallbackCurrentAddItem != null)
        {
            _fallbackCurrentAddItem = null;
            return;
        }

        EditableView?.CommitNew();
    }

    void IEditableCollectionView.EditItem(object item)
    {
        if (EditableView == null)
        {
            throw new InvalidOperationException("The active view cannot edit items.");
        }

        EditableView.EditItem(item);
    }

    void IEditableCollectionView.Remove(object item)
    {
        if (EditableView == null)
        {
            throw new InvalidOperationException("The active view cannot remove items.");
        }

        EditableView.Remove(item);
    }

    void IEditableCollectionView.RemoveAt(int index)
    {
        if (EditableView == null)
        {
            throw new InvalidOperationException("The active view cannot remove items.");
        }

        var item = GetItemAt(index);
        EditableView.Remove(item);
    }

    bool System.ComponentModel.IEditableCollectionViewAddNewItem.CanAddNewItem
    {
        get
        {
            if (!_isUsingItemsSource)
            {
                return false;
            }

            if (_activeView is System.ComponentModel.IEditableCollectionViewAddNewItem addNewItemView)
            {
                return addNewItemView.CanAddNewItem;
            }

            return MutableItemsSource is { IsReadOnly: false, IsFixedSize: false };
        }
    }

    object System.ComponentModel.IEditableCollectionViewAddNewItem.AddNewItem(object newItem)
    {
        ArgumentNullException.ThrowIfNull(newItem);
        if (_activeView is System.ComponentModel.IEditableCollectionViewAddNewItem addNewItemView)
        {
            return addNewItemView.AddNewItem(newItem);
        }

        if (MutableItemsSource is not { IsReadOnly: false, IsFixedSize: false } list)
        {
            throw new InvalidOperationException("The active view cannot add the specified item.");
        }

        EditableView?.CommitEdit();
        EditableView?.CommitNew();
        list.Add(newItem);
        _fallbackCurrentAddItem = newItem;
        RefreshPlainItemsSourceIfNeeded();
        _activeView.MoveCurrentTo(newItem);
        return newItem;
    }

    private void RefreshPlainItemsSourceIfNeeded()
    {
        if (_itemsSource is not INotifyCollectionChanged)
        {
            RefreshOverride();
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "IItemProperties intentionally obtains runtime property descriptors for consumer model types, matching the data-binding reflection contract.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2072:UnrecognizedReflectionPattern",
        Justification = "The item type is supplied by the collection consumer; preserving bindable item properties is part of the binding contract.")]
    ReadOnlyCollection<ItemPropertyInfo>? IItemProperties.ItemProperties
    {
        get
        {
            if (_activeView is IItemProperties itemProperties)
            {
                return itemProperties.ItemProperties;
            }

            var itemType = GetItemType();
            if (itemType == null)
            {
                return null;
            }

            var descriptors = TypeDescriptor.GetProperties(itemType);
            var properties = new List<ItemPropertyInfo>(descriptors.Count);
            foreach (PropertyDescriptor descriptor in descriptors)
            {
                properties.Add(new ItemPropertyInfo(
                    descriptor.Name,
                    descriptor.PropertyType,
                    descriptor));
            }

            return new ReadOnlyCollection<ItemPropertyInfo>(properties);
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2075:UnrecognizedReflectionPattern",
        Justification = "The source collection's runtime IEnumerable<T> contract is inspected only to report item metadata through IItemProperties.")]
    private Type? GetItemType()
    {
        var source = _itemsSource ?? _items;
        var enumerableInterface = source.GetType().GetInterfaces()
            .FirstOrDefault(type =>
                type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumerableInterface != null)
        {
            var declaredType = enumerableInterface.GetGenericArguments()[0];
            if (declaredType != typeof(object))
            {
                return declaredType;
            }
        }

        foreach (var item in _activeView)
        {
            if (item != null)
            {
                return item.GetType();
            }
        }

        return null;
    }

    bool IWeakEventListener.ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
    {
        switch (e)
        {
            case NotifyCollectionChangedEventArgs collectionChanged:
                OnActiveViewCollectionChanged(sender, collectionChanged);
                return true;
            case PropertyChangedEventArgs propertyChanged:
                OnActiveViewPropertyChanged(sender, propertyChanged);
                return true;
            default:
                return false;
        }
    }

    bool IList.IsFixedSize => _isUsingItemsSource;

    bool IList.IsReadOnly => _owner.ItemsSource != null;

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = value!;
    }

    int IList.Add(object? value) => Add(value!);

    bool IList.Contains(object? value) => Contains(value!);

    int IList.IndexOf(object? value) => IndexOf(value!);

    void IList.Insert(int index, object? value) => Insert(index, value!);

    void IList.Remove(object? value) => Remove(value!);

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => null!;

    private sealed class ItemCollectionDeferHelper : IDisposable
    {
        private ItemCollection? _owner;

        public ItemCollectionDeferHelper(ItemCollection owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            _owner?.EndDeferRefresh();
            _owner = null;
        }
    }

    // --- from ItemsControl.cs ---
    private readonly List<object> _items = new();
    private readonly ItemsControl _owner;

    internal ItemCollection(ItemsControl owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        InitializeCollectionView();
    }

    /// <summary>
    /// Gets or sets the item at the specified index.
    /// </summary>
    public object this[int index]
    {
        get => GetItemAt(index);
        set
        {
            VerifyWritable();
            var sourceIndex = GetDirectSourceIndex(index);
            var oldItem = _items[sourceIndex];
            _items[sourceIndex] = value;
            OnDirectItemsChanged(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Replace, value, oldItem, index));
        }
    }

    /// <summary>
    /// Gets the number of items in the collection.
    /// </summary>
    public override int Count => GetViewCount();

    /// <summary>
    /// Adds an item to the collection.
    /// </summary>
    public int Add(object item)
    {
        VerifyWritable();
        _items.Add(item);
        var sourceIndex = _items.Count - 1;
        OnDirectItemsChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, item, sourceIndex));
        return sourceIndex;
    }

    /// <summary>
    /// Clears all items from the collection.
    /// </summary>
    public void Clear()
    {
        VerifyWritable();
        _items.Clear();
        OnDirectItemsChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Determines whether the collection contains a specific item.
    /// </summary>
    public override bool Contains(object item) => ViewContains(item);

    /// <summary>
    /// Copies the effective view to the specified array.
    /// </summary>
    public void CopyTo(Array array, int index) => CopyViewTo(array, index);

    /// <summary>
    /// Determines the index of a specific item in the collection.
    /// </summary>
    public override int IndexOf(object item) => ViewIndexOf(item);

    /// <summary>
    /// Inserts an item at the specified index.
    /// </summary>
    public void Insert(int index, object item)
    {
        VerifyWritable();
        _items.Insert(index, item);
        OnDirectItemsChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Add, item, index));
    }

    /// <summary>
    /// Removes the first occurrence of a specific item from the collection.
    /// </summary>
    public void Remove(object item)
    {
        RemoveDirectItem(item);
    }

    /// <summary>
    /// Removes the item at the specified index.
    /// </summary>
    public void RemoveAt(int index)
    {
        VerifyWritable();
        var sourceIndex = GetDirectSourceIndex(index);
        var item = _items[sourceIndex];
        _items.RemoveAt(sourceIndex);
        OnDirectItemsChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Remove, item, index));
    }

    /// <summary>
    /// Adds multiple items to the collection, firing a single Reset notification.
    /// </summary>
    internal void AddRange(IList<object> items)
    {
        VerifyWritable();
        if (items.Count == 0)
        {
            return;
        }

        _items.AddRange(items);
        OnDirectItemsChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }

    private bool RemoveDirectItem(object item)
    {
        VerifyWritable();
        var index = _items.IndexOf(item);
        if (index < 0)
        {
            return false;
        }

        _items.RemoveAt(index);
        OnDirectItemsChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Remove, item, index));
        return true;
    }

    private void VerifyWritable()
    {
        if (_owner.ItemsSource != null)
        {
            throw new InvalidOperationException(
                "Items cannot be changed while ItemsSource is in use.");
        }
    }
}
