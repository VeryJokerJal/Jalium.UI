using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using Jalium.UI;

namespace System.ComponentModel;

/// <summary>
/// Provides information for an <see cref="ICollectionView.CurrentChanging"/> event.
/// </summary>
public class CurrentChangingEventArgs : EventArgs
{
    private bool _cancel;

    public CurrentChangingEventArgs()
        : this(true)
    {
    }

    public CurrentChangingEventArgs(bool isCancelable)
    {
        IsCancelable = isCancelable;
    }

    public bool IsCancelable { get; }

    public bool Cancel
    {
        get => _cancel;
        set
        {
            if (value && !IsCancelable)
            {
                throw new InvalidOperationException("This current-item change cannot be canceled.");
            }

            _cancel = value;
        }
    }
}

/// <summary>
/// Defines a property and direction used to sort a collection view.
/// </summary>
public struct SortDescription
{
    private string? _propertyName;
    private ListSortDirection _direction;
    private bool _isSealed;

    public SortDescription(string propertyName, ListSortDirection direction)
    {
        ValidateDirection(direction, nameof(direction));
        _propertyName = propertyName;
        _direction = direction;
        _isSealed = false;
    }

    public string? PropertyName
    {
        readonly get => _propertyName;
        set
        {
            VerifyNotSealed();
            _propertyName = value;
        }
    }

    public ListSortDirection Direction
    {
        readonly get => _direction;
        set
        {
            VerifyNotSealed();
            ValidateDirection(value, nameof(value));
            _direction = value;
        }
    }

    public readonly bool IsSealed => _isSealed;

    public static bool operator ==(SortDescription sd1, SortDescription sd2) =>
        sd1.PropertyName == sd2.PropertyName && sd1.Direction == sd2.Direction;

    public static bool operator !=(SortDescription sd1, SortDescription sd2) => !(sd1 == sd2);

    public override readonly bool Equals(object? obj) => obj is SortDescription other && this == other;

    public override readonly int GetHashCode() => HashCode.Combine(PropertyName, Direction);

    internal void Seal() => _isSealed = true;

    private readonly void VerifyNotSealed()
    {
        if (_isSealed)
        {
            throw new InvalidOperationException("A SortDescription cannot be changed after it has been added to a collection.");
        }
    }

    private static void ValidateDirection(ListSortDirection direction, string parameterName)
    {
        if (direction is not ListSortDirection.Ascending and not ListSortDirection.Descending)
        {
            throw new InvalidEnumArgumentException(parameterName, (int)direction, typeof(ListSortDirection));
        }
    }
}

/// <summary>
/// Represents an observable collection of sealed <see cref="SortDescription"/> values.
/// </summary>
public class SortDescriptionCollection : Collection<SortDescription>, INotifyCollectionChanged
{
    private sealed class EmptySortDescriptionCollection : SortDescriptionCollection
    {
        protected override void ClearItems() => throw new NotSupportedException("The collection is read-only.");
        protected override void RemoveItem(int index) => throw new NotSupportedException("The collection is read-only.");
        protected override void InsertItem(int index, SortDescription item) => throw new NotSupportedException("The collection is read-only.");
        protected override void SetItem(int index, SortDescription item) => throw new NotSupportedException("The collection is read-only.");
    }

    public static readonly SortDescriptionCollection Empty = new EmptySortDescriptionCollection();

    protected event NotifyCollectionChangedEventHandler? CollectionChanged;

    event NotifyCollectionChangedEventHandler? INotifyCollectionChanged.CollectionChanged
    {
        add => CollectionChanged += value;
        remove => CollectionChanged -= value;
    }

    protected override void ClearItems()
    {
        base.ClearItems();
        CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    protected override void RemoveItem(int index)
    {
        SortDescription oldItem = this[index];
        base.RemoveItem(index);
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, oldItem, index));
    }

    protected override void InsertItem(int index, SortDescription item)
    {
        item.Seal();
        base.InsertItem(index, item);
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
    }

    protected override void SetItem(int index, SortDescription item)
    {
        item.Seal();
        SortDescription oldItem = this[index];
        base.SetItem(index, item);
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, oldItem, index));
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
    }
}

/// <summary>
/// Describes how items in a collection view are divided into groups.
/// </summary>
public abstract class GroupDescription : INotifyPropertyChanged
{
    private readonly ObservableCollection<object> _groupNames = new();
    private SortDescriptionCollection? _sortDescriptions;
    private IComparer? _customSort;

    protected GroupDescription()
    {
        _groupNames.CollectionChanged += (_, _) =>
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(GroupNames)));
    }

    public ObservableCollection<object> GroupNames => _groupNames;

    public SortDescriptionCollection SortDescriptions
    {
        get
        {
            if (_sortDescriptions is null)
            {
                SetSortDescriptions(new SortDescriptionCollection());
            }

            return _sortDescriptions!;
        }
    }

    public IComparer? CustomSort
    {
        get => _customSort;
        set
        {
            if (ReferenceEquals(_customSort, value))
            {
                return;
            }

            _customSort = value;
            SetSortDescriptions(null);
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(CustomSort)));
        }
    }

    protected virtual event PropertyChangedEventHandler? PropertyChanged;

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => PropertyChanged += value;
        remove => PropertyChanged -= value;
    }

    public bool ShouldSerializeGroupNames() => _groupNames.Count > 0;

    public bool ShouldSerializeSortDescriptions() => _sortDescriptions is { Count: > 0 };

    public abstract object GroupNameFromItem(object item, int level, CultureInfo culture);

    public virtual bool NamesMatch(object groupName, object itemName) => Equals(groupName, itemName);

    protected virtual void OnPropertyChanged(PropertyChangedEventArgs e) => PropertyChanged?.Invoke(this, e);

    private void SetSortDescriptions(SortDescriptionCollection? descriptions)
    {
        if (_sortDescriptions is not null)
        {
            ((INotifyCollectionChanged)_sortDescriptions).CollectionChanged -= OnSortDescriptionsChanged;
        }

        bool changed = !ReferenceEquals(_sortDescriptions, descriptions);
        _sortDescriptions = descriptions;

        if (_sortDescriptions is not null)
        {
            ((INotifyCollectionChanged)_sortDescriptions).CollectionChanged += OnSortDescriptionsChanged;
        }

        if (changed)
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(SortDescriptions)));
        }
    }

    private void OnSortDescriptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_sortDescriptions is { Count: > 0 } && _customSort is not null)
        {
            _customSort = null;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(CustomSort)));
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(SortDescriptions)));
    }
}

/// <summary>Defines collection-view live-shaping controls.</summary>
public interface ICollectionViewLiveShaping
{
    bool CanChangeLiveSorting { get; }
    bool CanChangeLiveFiltering { get; }
    bool CanChangeLiveGrouping { get; }
    bool? IsLiveSorting { get; set; }
    bool? IsLiveFiltering { get; set; }
    bool? IsLiveGrouping { get; set; }
    ObservableCollection<string> LiveSortingProperties { get; }
    ObservableCollection<string> LiveFilteringProperties { get; }
    ObservableCollection<string> LiveGroupingProperties { get; }
}

/// <summary>Defines editable collection-view transactions.</summary>
public interface IEditableCollectionView
{
    NewItemPlaceholderPosition NewItemPlaceholderPosition { get; set; }
    bool CanAddNew { get; }
    bool IsAddingNew { get; }
    object? CurrentAddItem { get; }
    bool CanRemove { get; }
    bool CanCancelEdit { get; }
    bool IsEditingItem { get; }
    object? CurrentEditItem { get; }
    object? AddNew();
    void CommitNew();
    void CancelNew();
    void RemoveAt(int index);
    void Remove(object item);
    void EditItem(object item);
    void CommitEdit();
    void CancelEdit();
}

/// <summary>Extends editable collection views with caller-supplied new items.</summary>
public interface IEditableCollectionViewAddNewItem : IEditableCollectionView
{
    bool CanAddNewItem { get; }
    object AddNewItem(object newItem);
}

/// <summary>
/// Delivers <see cref="INotifyPropertyChanged.PropertyChanged"/> without strongly retaining listeners.
/// </summary>
public class PropertyChangedEventManager : WeakEventManager
{
    private static PropertyChangedEventManager CurrentManager
    {
        get
        {
            var manager = (PropertyChangedEventManager?)GetCurrentManager(typeof(PropertyChangedEventManager));
            if (manager is null)
            {
                manager = new PropertyChangedEventManager();
                SetCurrentManager(typeof(PropertyChangedEventManager), manager);
            }

            return manager;
        }
    }

    private PropertyChangedEventManager()
    {
    }

    public static void AddListener(
        INotifyPropertyChanged source,
        IWeakEventListener listener,
        string propertyName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(listener);
        CurrentManager.Add(source, propertyName, listener, null);
    }

    public static void RemoveListener(
        INotifyPropertyChanged source,
        IWeakEventListener listener,
        string propertyName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(listener);
        CurrentManager.Remove(source, propertyName, listener, null);
    }

    public static void AddHandler(
        INotifyPropertyChanged source,
        EventHandler<PropertyChangedEventArgs> handler,
        string propertyName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);
        CurrentManager.Add(source, propertyName, null, handler);
    }

    public static void RemoveHandler(
        INotifyPropertyChanged source,
        EventHandler<PropertyChangedEventArgs> handler,
        string propertyName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);
        CurrentManager.Remove(source, propertyName, null, handler);
    }

    protected override ListenerList NewListenerList() => new ListenerList<PropertyChangedEventArgs>();

    protected override void StartListening(object source) =>
        ((INotifyPropertyChanged)source).PropertyChanged += OnPropertyChanged;

    protected override void StopListening(object source) =>
        ((INotifyPropertyChanged)source).PropertyChanged -= OnPropertyChanged;

    protected override bool Purge(object source, object data, bool purgeAll)
    {
        var map = (Dictionary<string, ListenerList>)data;
        bool changed = false;

        foreach (string key in map.Keys.ToArray())
        {
            ListenerList list = map[key];
            if (ListenerList.PrepareForWriting(ref list))
            {
                map[key] = list;
            }

            changed |= list.Purge();
            if (purgeAll || list.IsEmpty)
            {
                map.Remove(key);
                changed = true;
            }
        }

        if (purgeAll || map.Count == 0)
        {
            StopListening(source);
            base.Remove(source);
            changed = true;
        }

        return changed;
    }

    private void Add(
        INotifyPropertyChanged source,
        string? propertyName,
        IWeakEventListener? listener,
        EventHandler<PropertyChangedEventArgs>? handler)
    {
        using (WriteLock)
        {
            var map = (Dictionary<string, ListenerList>?)this[source];
            if (map is null)
            {
                map = new Dictionary<string, ListenerList>(StringComparer.OrdinalIgnoreCase);
                this[source] = map;
                StartListening(source);
            }

            string key = propertyName ?? string.Empty;
            if (!map.TryGetValue(key, out ListenerList? list) || list is null)
            {
                list = NewListenerList();
                map.Add(key, list);
            }
            else if (ListenerList.PrepareForWriting(ref list))
            {
                map[key] = list;
            }

            if (handler is not null)
            {
                list.AddHandler(handler);
            }
            else
            {
                list.Add(listener!);
            }

            ScheduleCleanup();
        }
    }

    private void Remove(
        INotifyPropertyChanged source,
        string? propertyName,
        IWeakEventListener? listener,
        EventHandler<PropertyChangedEventArgs>? handler)
    {
        using (WriteLock)
        {
            var map = (Dictionary<string, ListenerList>?)this[source];
            string key = propertyName ?? string.Empty;
            if (map is null || !map.TryGetValue(key, out ListenerList? list) || list is null)
            {
                return;
            }

            if (ListenerList.PrepareForWriting(ref list))
            {
                map[key] = list;
            }

            if (handler is not null)
            {
                list.RemoveHandler(handler);
            }
            else
            {
                list.Remove(listener!);
            }

            if (list.IsEmpty)
            {
                map.Remove(key);
            }

            if (map.Count == 0)
            {
                StopListening(source);
                base.Remove(source);
            }
        }
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is null)
        {
            return;
        }

        List<ListenerList> lists = new();
        using (ReadLock)
        {
            var map = (Dictionary<string, ListenerList>?)this[sender];
            if (map is null)
            {
                return;
            }

            if (string.IsNullOrEmpty(e.PropertyName))
            {
                lists.AddRange(map.Values);
            }
            else
            {
                if (map.TryGetValue(e.PropertyName, out ListenerList? specific) && specific is not null)
                {
                    lists.Add(specific);
                }

                if (map.TryGetValue(string.Empty, out ListenerList? all) && all is not null)
                {
                    lists.Add(all);
                }
            }

            foreach (ListenerList list in lists)
            {
                list.BeginUse();
            }
        }

        try
        {
            foreach (ListenerList list in lists)
            {
                DeliverEventToList(sender, e, list);
            }
        }
        finally
        {
            foreach (ListenerList list in lists)
            {
                list.EndUse();
            }
        }
    }
}
