using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Jalium.UI.Threading;

namespace Jalium.UI.Data;

/// <summary>
/// Represents a view for grouping, sorting, filtering, and navigating a data collection.
/// </summary>
public class CollectionView : DispatcherObject, ICollectionView, INotifyPropertyChanged
{
    private static readonly object NullGroupName = new();
    private static readonly object NewItemPlaceholderValue = new NewItemPlaceholderMarker();

    private readonly IEnumerable _sourceCollection;
    private readonly ObservableCollection<System.ComponentModel.GroupDescription> _groupDescriptions = new();
    private readonly System.ComponentModel.SortDescriptionCollection _sortDescriptions = new();
    private readonly List<NotifyCollectionChangedEventArgs> _pendingChanges = new();
    private CultureInfo _culture = CultureInfo.CurrentCulture;
    private Predicate<object>? _filter;
    private List<object> _internalList = new();
    private object? _currentItem;
    private int _currentPosition = -1;
    private ReadOnlyObservableCollection<object>? _groups;
    private int _deferRefreshCount;
    private bool _needsRefresh;
    private bool _isDynamic;
    private bool _allowsCrossThreadChanges;
    private bool _updatedOutsideDispatcher;
    private bool _isDetached;
    private NotifyCollectionChangedEventHandler? _collectionChanged;
    private PropertyChangedEventHandler? _propertyChanged;
    private EventHandler? _currentChanged;
    private CurrentChangingEventHandler? _currentChanging;

    /// <summary>
    /// Initializes a collection view whose effective source is supplied by a derived class.
    /// </summary>
    protected CollectionView()
        : this(Array.Empty<object>())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CollectionView"/> class.
    /// </summary>
    public CollectionView(IEnumerable source)
    {
        _sourceCollection = source ?? throw new ArgumentNullException(nameof(source));
        BindingOperations.RegisterCollectionView(this, source);

        if (source is INotifyCollectionChanged notifyingCollection && this is not BindingListCollectionView)
        {
            notifyingCollection.CollectionChanged += OnCollectionChanged;
            _isDynamic = true;
        }

        ((INotifyCollectionChanged)_sortDescriptions).CollectionChanged += OnSortDescriptionsChanged;
        _groupDescriptions.CollectionChanged += OnGroupDescriptionsChanged;

        RebuildEffectiveItems();
        RestoreCurrencyAfterRefresh(oldCurrentItem: null, oldPosition: -1);
    }

    /// <summary>
    /// Occurs when the effective collection changes.
    /// </summary>
    protected virtual event NotifyCollectionChangedEventHandler? CollectionChanged
    {
        add => _collectionChanged += value;
        remove => _collectionChanged -= value;
    }

    event NotifyCollectionChangedEventHandler? INotifyCollectionChanged.CollectionChanged
    {
        add => _collectionChanged += value;
        remove => _collectionChanged -= value;
    }

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    protected virtual event PropertyChangedEventHandler? PropertyChanged
    {
        add => _propertyChanged += value;
        remove => _propertyChanged -= value;
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => _propertyChanged += value;
        remove => _propertyChanged -= value;
    }

    /// <summary>
    /// Occurs after the current item changes.
    /// </summary>
    public virtual event EventHandler? CurrentChanged
    {
        add => _currentChanged += value;
        remove => _currentChanged -= value;
    }

    /// <summary>
    /// Occurs before the current item changes.
    /// </summary>
    public virtual event CurrentChangingEventHandler? CurrentChanging
    {
        add => _currentChanging += value;
        remove => _currentChanging -= value;
    }

    /// <summary>
    /// Gets the singleton used by editable views to represent an insertion position.
    /// </summary>
    public static object NewItemPlaceholder => NewItemPlaceholderValue;

    /// <summary>
    /// Gets a value indicating whether filtering is supported.
    /// </summary>
    public virtual bool CanFilter => true;

    /// <summary>
    /// Gets a value indicating whether grouping is supported.
    /// </summary>
    public virtual bool CanGroup => true;

    /// <summary>
    /// Gets a value indicating whether sorting is supported.
    /// </summary>
    public virtual bool CanSort => true;

    /// <summary>
    /// Gets or sets the culture used by comparisons.
    /// </summary>
    public virtual CultureInfo Culture
    {
        get => _culture;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (Equals(_culture, value))
            {
                return;
            }

            _culture = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Culture)));
            RefreshOrDefer();
        }
    }

    /// <summary>
    /// Gets the current item.
    /// </summary>
    public virtual object? CurrentItem
    {
        get
        {
            VerifyRefreshNotDeferred();
            return _currentItem;
        }
    }

    /// <summary>
    /// Gets the current position.
    /// </summary>
    public virtual int CurrentPosition
    {
        get
        {
            VerifyRefreshNotDeferred();
            return _currentPosition;
        }
    }

    /// <summary>
    /// Gets or sets the view filter.
    /// </summary>
    public virtual Predicate<object>? Filter
    {
        get => _filter;
        set
        {
            if (value != null && !CanFilter)
            {
                throw new NotSupportedException("This collection view does not support filtering.");
            }

            if (ReferenceEquals(_filter, value))
            {
                return;
            }

            _filter = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(Filter)));
            RefreshOrDefer();
        }
    }

    /// <summary>
    /// Gets the grouping descriptions.
    /// </summary>
    public virtual ObservableCollection<System.ComponentModel.GroupDescription> GroupDescriptions => _groupDescriptions;

    /// <summary>
    /// Gets the top-level groups.
    /// </summary>
    public virtual ReadOnlyObservableCollection<object>? Groups => _groups;

    /// <summary>
    /// Gets whether currency is beyond the end of the view.
    /// </summary>
    public virtual bool IsCurrentAfterLast
    {
        get
        {
            VerifyRefreshNotDeferred();
            return Count > 0 && _currentPosition >= Count;
        }
    }

    /// <summary>
    /// Gets whether currency is before the beginning of the view.
    /// </summary>
    public virtual bool IsCurrentBeforeFirst
    {
        get
        {
            VerifyRefreshNotDeferred();
            return Count > 0 && _currentPosition < 0;
        }
    }

    /// <summary>
    /// Gets whether the view is empty.
    /// </summary>
    public virtual bool IsEmpty => Count == 0;

    /// <summary>
    /// Gets the sorting descriptions.
    /// </summary>
    public virtual System.ComponentModel.SortDescriptionCollection SortDescriptions => _sortDescriptions;

    /// <summary>
    /// Gets the source collection.
    /// </summary>
    public virtual IEnumerable SourceCollection => _sourceCollection;

    /// <summary>
    /// Gets the effective item count.
    /// </summary>
    public virtual int Count
    {
        get
        {
            VerifyRefreshNotDeferred();
            return _internalList.Count;
        }
    }

    /// <summary>
    /// Gets the comparer currently used by the view.
    /// </summary>
    public virtual IComparer? Comparer =>
        _sortDescriptions.Count == 0 ? null : new SortFieldComparer(_sortDescriptions, _culture);

    /// <summary>
    /// Gets whether the view has listeners or active currency handlers.
    /// </summary>
    public virtual bool IsInUse =>
        _collectionChanged != null || _propertyChanged != null || _currentChanged != null || _currentChanging != null;

    /// <summary>
    /// Gets whether a refresh is pending.
    /// </summary>
    public virtual bool NeedsRefresh => _needsRefresh;

    /// <summary>
    /// Gets whether cross-thread collection notifications are enabled.
    /// </summary>
    protected bool AllowsCrossThreadChanges => _allowsCrossThreadChanges;

    /// <summary>
    /// Gets whether currency agrees with the effective item at its recorded position.
    /// </summary>
    protected bool IsCurrentInSync =>
        _currentPosition < 0
            ? _currentItem == null
            : _currentPosition >= Count
                ? _currentItem == null
                : Equals(_currentItem, GetItemAt(_currentPosition));

    /// <summary>
    /// Gets whether the source sends dynamic collection notifications.
    /// </summary>
    protected bool IsDynamic => _isDynamic;

    /// <summary>
    /// Gets whether a refresh is currently deferred.
    /// </summary>
    protected bool IsRefreshDeferred => _deferRefreshCount > 0;

    /// <summary>
    /// Gets whether a collection notification arrived outside the owning dispatcher.
    /// </summary>
    protected bool UpdatedOutsideDispatcher => _updatedOutsideDispatcher;

    /// <summary>
    /// Returns whether an item is present in the effective view.
    /// </summary>
    public virtual bool Contains(object item)
    {
        VerifyRefreshNotDeferred();
        return IndexOf(item) >= 0;
    }

    /// <summary>
    /// Returns the item at a view index.
    /// </summary>
    public virtual object GetItemAt(int index)
    {
        VerifyRefreshNotDeferred();
        if ((uint)index >= (uint)_internalList.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _internalList[index];
    }

    /// <summary>
    /// Returns an item's effective view index.
    /// </summary>
    public virtual int IndexOf(object item)
    {
        VerifyRefreshNotDeferred();
        return _internalList.IndexOf(item);
    }

    /// <summary>
    /// Tests only the active filter.
    /// </summary>
    public virtual bool PassesFilter(object item) => !CanFilter || _filter == null || _filter(item);

    /// <summary>
    /// Defers automatic refresh work until the returned object is disposed.
    /// </summary>
    public virtual IDisposable DeferRefresh()
    {
        _deferRefreshCount++;
        return new DeferRefreshHelper(this);
    }

    /// <summary>
    /// Moves currency to an item, or before the first item when it is not present.
    /// </summary>
    public virtual bool MoveCurrentTo(object? item)
    {
        VerifyRefreshNotDeferred();
        if (ReferenceEquals(item, NewItemPlaceholder))
        {
            return IsCurrentInView;
        }

        if (Equals(CurrentItem, item) && (item != null || IsCurrentInView))
        {
            return IsCurrentInView;
        }

        return MoveCurrentToPosition(item == null ? -1 : IndexOf(item));
    }

    /// <summary>
    /// Moves currency to the first item.
    /// </summary>
    public virtual bool MoveCurrentToFirst() => MoveCurrentToPosition(0);

    /// <summary>
    /// Moves currency to the last item.
    /// </summary>
    public virtual bool MoveCurrentToLast() => MoveCurrentToPosition(Count - 1);

    /// <summary>
    /// Moves currency to the next item.
    /// </summary>
    public virtual bool MoveCurrentToNext()
    {
        var next = CurrentPosition + 1;
        return next <= Count && MoveCurrentToPosition(next);
    }

    /// <summary>
    /// Moves currency to a view position.
    /// </summary>
    public virtual bool MoveCurrentToPosition(int position)
    {
        VerifyRefreshNotDeferred();
        if (position < -1 || position > Count)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        if (position == CurrentPosition && IsCurrentInSync)
        {
            return IsCurrentInView;
        }

        if (!OKToChangeCurrent())
        {
            return IsCurrentInView;
        }

        var oldAfterLast = IsCurrentAfterLast;
        var oldBeforeFirst = IsCurrentBeforeFirst;
        SetCurrent(position >= 0 && position < Count ? GetItemAt(position) : null, position, Count);
        OnCurrentChanged();

        if (oldAfterLast != IsCurrentAfterLast)
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsCurrentAfterLast)));
        }

        if (oldBeforeFirst != IsCurrentBeforeFirst)
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsCurrentBeforeFirst)));
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(CurrentPosition)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(CurrentItem)));
        return IsCurrentInView;
    }

    /// <summary>
    /// Moves currency to the previous item.
    /// </summary>
    public virtual bool MoveCurrentToPrevious()
    {
        var previous = CurrentPosition - 1;
        return previous >= -1 && MoveCurrentToPosition(previous);
    }

    /// <summary>
    /// Refreshes the effective view.
    /// </summary>
    public virtual void Refresh()
    {
        if (this is IEditableCollectionView editable && (editable.IsAddingNew || editable.IsEditingItem))
        {
            throw new InvalidOperationException("Refresh is not permitted during an add or edit transaction.");
        }

        if (IsRefreshDeferred)
        {
            _needsRefresh = true;
            return;
        }

        RefreshOverride();
    }

    /// <summary>
    /// Stops observing the source collection.
    /// </summary>
    public virtual void DetachFromSourceCollection()
    {
        if (_isDetached)
        {
            return;
        }

        if (_sourceCollection is INotifyCollectionChanged notifyingCollection && this is not BindingListCollectionView)
        {
            notifyingCollection.CollectionChanged -= OnCollectionChanged;
        }

        _isDetached = true;
        _isDynamic = false;
        ClearPendingChanges();
    }

    /// <summary>
    /// Rebuilds the effective view.
    /// </summary>
    protected virtual void RefreshOverride()
    {
        var oldCurrentItem = _currentItem;
        var oldPosition = _currentPosition;
        OnCurrentChanging();
        RebuildEffectiveItems();
        RestoreCurrencyAfterRefresh(oldCurrentItem, oldPosition);
        _needsRefresh = false;
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        RaiseRefreshPropertyChanges();
        OnCurrentChanged();
    }

    /// <summary>
    /// Returns an enumerator over the effective items.
    /// </summary>
    protected virtual IEnumerator GetEnumerator()
    {
        VerifyRefreshNotDeferred();
        return _internalList.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Gets whether currency points at an effective item.
    /// </summary>
    protected bool IsCurrentInView => _currentPosition >= 0 && _currentPosition < Count;

    /// <summary>
    /// Clears collection changes queued for later processing.
    /// </summary>
    protected void ClearPendingChanges() => _pendingChanges.Clear();

    /// <summary>
    /// Compatibility alias for <see cref="ClearPendingChanges"/>.
    /// </summary>
    [Obsolete("Replaced by ClearPendingChanges")]
    protected void ClearChangeLog() => ClearPendingChanges();

    /// <summary>
    /// Raises a cancellable current-changing event and returns whether the move may continue.
    /// </summary>
    protected bool OKToChangeCurrent()
    {
        var args = new CurrentChangingEventArgs(isCancelable: true);
        OnCurrentChanging(args);
        return !args.Cancel;
    }

    /// <summary>
    /// Called when cross-thread collection access support changes.
    /// </summary>
    protected virtual void OnAllowsCrossThreadChangesChanged()
    {
    }

    /// <summary>
    /// Compatibility hook invoked before a change is logged.
    /// </summary>
    [Obsolete("Replaced by OnAllowsCrossThreadChangesChanged")]
    protected virtual void OnBeginChangeLogging(NotifyCollectionChangedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _pendingChanges.Add(args);
    }

    /// <summary>
    /// Handles source collection changes.
    /// </summary>
    protected void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (!CheckAccess())
        {
            _updatedOutsideDispatcher = true;
            if (!AllowsCrossThreadChanges)
            {
                throw new NotSupportedException(
                    "This CollectionView does not support changes to its SourceCollection from a thread different from the Dispatcher thread.");
            }

#pragma warning disable CS0618
            OnBeginChangeLogging(args);
#pragma warning restore CS0618
            Dispatcher.BeginInvoke(() => ProcessPendingChanges());
            return;
        }

        ProcessCollectionChanged(args);
    }

    /// <summary>
    /// Processes one source collection change.
    /// </summary>
    protected virtual void ProcessCollectionChanged(NotifyCollectionChangedEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (IsRefreshDeferred)
        {
            _needsRefresh = true;
            return;
        }

        var oldCurrentItem = _currentItem;
        var oldPosition = _currentPosition;
        var canForward = _filter == null && Comparer == null && !HasActiveGrouping;

        RebuildEffectiveItems();
        RestoreCurrencyAfterRefresh(oldCurrentItem, oldPosition);
        _needsRefresh = false;

        OnCollectionChanged(canForward
            ? args
            : new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        RaiseRefreshPropertyChanges();

        if (!Equals(oldCurrentItem, _currentItem) || oldPosition != _currentPosition)
        {
            OnCurrentChanged();
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(CurrentItem)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(CurrentPosition)));
        }
    }

    /// <summary>
    /// Processes all queued collection changes.
    /// </summary>
    protected void ProcessPendingChanges()
    {
        if (_pendingChanges.Count == 0)
        {
            return;
        }

        var pending = _pendingChanges.ToArray();
        _pendingChanges.Clear();
        _updatedOutsideDispatcher = false;
        foreach (var args in pending)
        {
            ProcessCollectionChanged(args);
        }
    }

    /// <summary>
    /// Refreshes immediately or records that a deferred refresh is necessary.
    /// </summary>
    protected void RefreshOrDefer()
    {
        if (IsRefreshDeferred)
        {
            _needsRefresh = true;
        }
        else
        {
            RefreshOverride();
        }
    }

    /// <summary>
    /// Sets currency using the current effective count.
    /// </summary>
    protected void SetCurrent(object? newItem, int newPosition) => SetCurrent(newItem, newPosition, Count);

    /// <summary>
    /// Sets currency using an explicitly supplied count.
    /// </summary>
    protected void SetCurrent(object? newItem, int newPosition, int count)
    {
        if (newPosition < -1 || newPosition > count)
        {
            throw new ArgumentOutOfRangeException(nameof(newPosition));
        }

        _currentItem = newPosition >= 0 && newPosition < count ? newItem : null;
        _currentPosition = newPosition;
    }

    /// <summary>
    /// Throws when stale view contents are inspected during a defer cycle.
    /// </summary>
    protected void VerifyRefreshNotDeferred()
    {
        if (IsRefreshDeferred && NeedsRefresh)
        {
            throw new InvalidOperationException(
                "Cannot change or inspect the contents or Current position of CollectionView while Refresh is being deferred.");
        }
    }

    /// <summary>
    /// Raises a property-change notification.
    /// </summary>
    protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        _propertyChanged?.Invoke(this, e);
    }

    /// <summary>
    /// Raises a property-change notification by name.
    /// </summary>
    protected void OnPropertyChanged(string propertyName) =>
        OnPropertyChanged(new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Raises a collection-change notification.
    /// </summary>
    protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        _collectionChanged?.Invoke(this, e);
    }

    /// <summary>
    /// Raises a collection-change notification while excluding one target.
    /// </summary>
    protected void OnCollectionChanged(NotifyCollectionChangedEventArgs e, object excludedHandlerTarget)
    {
        ArgumentNullException.ThrowIfNull(e);
        var handlers = _collectionChanged;
        if (handlers == null)
        {
            return;
        }

        foreach (NotifyCollectionChangedEventHandler handler in handlers.GetInvocationList())
        {
            if (!ReferenceEquals(handler.Target, excludedHandlerTarget))
            {
                handler(this, e);
            }
        }
    }

    /// <summary>
    /// Raises the current-changed event.
    /// </summary>
    protected virtual void OnCurrentChanged() => _currentChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Raises a noncancelable current-changing event.
    /// </summary>
    protected void OnCurrentChanging() => OnCurrentChanging(new CurrentChangingEventArgs(isCancelable: false));

    /// <summary>
    /// Raises a current-changing event.
    /// </summary>
    protected virtual void OnCurrentChanging(CurrentChangingEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _currentChanging?.Invoke(this, args);
    }

    /// <summary>
    /// Selects the group description for a parent group and level.
    /// </summary>
    protected virtual System.ComponentModel.GroupDescription? GetGroupDescription(CollectionViewGroup? parentGroup, int level) =>
        level >= 0 && level < _groupDescriptions.Count ? _groupDescriptions[level] : null;

    /// <summary>
    /// Gets a snapshot of the ordinary effective items, excluding editable placeholders.
    /// </summary>
    protected IList EffectiveItems => _internalList;

    /// <summary>
    /// Sets whether source changes may be processed through the owning dispatcher.
    /// </summary>
    internal void SetAllowsCrossThreadChanges(bool value)
    {
        if (_allowsCrossThreadChanges == value)
        {
            return;
        }

        _allowsCrossThreadChanges = value;
        OnAllowsCrossThreadChangesChanged();
    }

    private bool HasActiveGrouping => GetGroupDescription(parentGroup: null, level: 0) != null;

    private void RebuildEffectiveItems()
    {
        var items = new List<object>();
        foreach (var item in _sourceCollection)
        {
            if (item != null && PassesFilter(item))
            {
                items.Add(item);
            }
            else if (item == null && (_filter == null || _filter(null!)))
            {
                items.Add(null!);
            }
        }

        var comparer = Comparer;
        if (comparer != null)
        {
            items.Sort((left, right) => comparer.Compare(left, right));
        }

        _internalList = items;
        BuildGroups();
    }

    private void BuildGroups()
    {
        if (!HasActiveGrouping)
        {
            _groups = null;
            return;
        }

        var groups = new ObservableCollection<object>();
        BuildGroups(_internalList, parentGroup: null, level: 0, groups);
        _groups = new ReadOnlyObservableCollection<object>(groups);
    }

    private void BuildGroups(
        IReadOnlyList<object> items,
        CollectionViewGroup? parentGroup,
        int level,
        ObservableCollection<object> targetGroups)
    {
        var description = GetGroupDescription(parentGroup, level);
        if (description == null)
        {
            return;
        }

        var groupMap = new Dictionary<object, InternalCollectionViewGroup>();
        var groupOrder = new List<object>();
        foreach (var item in items)
        {
            var groupName = description.GroupNameFromItem(item, level, _culture);
            var key = groupName ?? NullGroupName;
            if (!groupMap.TryGetValue(key, out var group))
            {
                group = new InternalCollectionViewGroup(groupName!);
                groupMap.Add(key, group);
                groupOrder.Add(key);
            }

            group.AddItem(item);
        }

        foreach (var key in groupOrder)
        {
            var group = groupMap[key];
            if (GetGroupDescription(group, level + 1) != null)
            {
                var children = group.GetItemsSnapshot();
                group.Clear();
                var childGroups = new ObservableCollection<object>();
                BuildGroups(children, group, level + 1, childGroups);
                foreach (var childGroup in childGroups)
                {
                    group.AddItem(childGroup);
                }
            }

            targetGroups.Add(group);
        }
    }

    private void RestoreCurrencyAfterRefresh(object? oldCurrentItem, int oldPosition)
    {
        if (oldCurrentItem != null)
        {
            var newPosition = _internalList.IndexOf(oldCurrentItem);
            if (newPosition >= 0)
            {
                SetCurrent(oldCurrentItem, newPosition, _internalList.Count);
                return;
            }
        }

        if (oldPosition >= _internalList.Count && oldPosition >= 0)
        {
            SetCurrent(null, _internalList.Count, _internalList.Count);
        }
        else if (_internalList.Count > 0)
        {
            SetCurrent(_internalList[0], 0, _internalList.Count);
        }
        else
        {
            SetCurrent(null, -1, 0);
        }
    }

    private void RaiseRefreshPropertyChanges()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsEmpty)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Groups)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(NeedsRefresh)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsCurrentBeforeFirst)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsCurrentAfterLast)));
    }

    private void OnSortDescriptionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshOrDefer();

    private void OnGroupDescriptionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshOrDefer();

    private void EndDeferRefresh()
    {
        if (_deferRefreshCount <= 0)
        {
            return;
        }

        _deferRefreshCount--;
        if (_deferRefreshCount == 0 && _needsRefresh)
        {
            RefreshOverride();
        }
    }

    private sealed class DeferRefreshHelper : IDisposable
    {
        private CollectionView? _view;

        public DeferRefreshHelper(CollectionView view) => _view = view;

        public void Dispose()
        {
            var view = Interlocked.Exchange(ref _view, null);
            view?.EndDeferRefresh();
        }
    }

    private sealed class NewItemPlaceholderMarker
    {
        public override string ToString() => "{NewItemPlaceholder}";
    }

    private sealed class SortFieldComparer : IComparer, IComparer<object>
    {
        private readonly System.ComponentModel.SortDescriptionCollection _sortDescriptions;
        private readonly CultureInfo _culture;

        public SortFieldComparer(System.ComponentModel.SortDescriptionCollection sortDescriptions, CultureInfo culture)
        {
            _sortDescriptions = sortDescriptions;
            _culture = culture;
        }

        int IComparer.Compare(object? x, object? y) => Compare(x, y);

        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x == null)
            {
                return -1;
            }

            if (y == null)
            {
                return 1;
            }

            foreach (var description in _sortDescriptions)
            {
                var left = GetPropertyValue(x, description.PropertyName);
                var right = GetPropertyValue(y, description.PropertyName);
                var result = CompareValues(left, right);
                if (result != 0)
                {
                    return description.Direction == System.ComponentModel.ListSortDirection.Descending ? -result : result;
                }
            }

            return 0;
        }

        private int CompareValues(object? left, object? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return -1;
            }

            if (right == null)
            {
                return 1;
            }

            if (left is string leftText && right is string rightText)
            {
                return string.Compare(leftText, rightText, ignoreCase: false, _culture);
            }

            return global::System.Collections.Comparer.DefaultInvariant.Compare(left, right);
        }

        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
            "Trimming",
            "IL2075:UnrecognizedReflectionPattern",
            Justification = "Collection view sorting reflects user-selected property names; bound model properties must remain available under trimming.")]
        private static object? GetPropertyValue(object item, string? propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
            {
                return item;
            }

            return item.GetType().GetProperty(propertyName)?.GetValue(item);
        }
    }

    private sealed class InternalCollectionViewGroup : CollectionViewGroup
    {
        private bool _isBottomLevel = true;

        public InternalCollectionViewGroup(object name)
            : base(name)
        {
        }

        public override bool IsBottomLevel => _isBottomLevel;

        public void AddItem(object item)
        {
            if (item is CollectionViewGroup)
            {
                _isBottomLevel = false;
            }

            ProtectedItems.Add(item);
            ProtectedItemCount = ProtectedItems.Count;
        }

        public void Clear()
        {
            ProtectedItems.Clear();
            ProtectedItemCount = 0;
            _isBottomLevel = true;
        }

        public List<object> GetItemsSnapshot() => ProtectedItems.ToList();
    }
}
