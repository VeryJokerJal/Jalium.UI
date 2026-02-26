using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI.Data;

/// <summary>
/// Represents a view for grouping, sorting, filtering, and navigating a data collection.
/// </summary>
public class CollectionView : ICollectionView, INotifyPropertyChanged
{
    private readonly IEnumerable _sourceCollection;
    private readonly ObservableCollection<GroupDescription> _groupDescriptions = new();
    private readonly SortDescriptionCollection _sortDescriptions = new();
    private CultureInfo _culture = CultureInfo.CurrentCulture;
    private Predicate<object>? _filter;
    private List<object>? _internalList;
    private object? _currentItem;
    private int _currentPosition = -1;
    private ReadOnlyObservableCollection<object>? _groups;
    private ObservableCollection<object>? _groupsInternal;
    private int _deferRefreshCount;
    private bool _needsRefresh;

    /// <summary>
    /// Initializes a new instance of the CollectionView class.
    /// </summary>
    /// <param name="source">The source collection.</param>
    public CollectionView(IEnumerable source)
    {
        _sourceCollection = source ?? throw new ArgumentNullException(nameof(source));

        if (source is INotifyCollectionChanged ncc)
        {
            ncc.CollectionChanged += OnSourceCollectionChanged;
        }

        _sortDescriptions.CollectionChanged += OnSortDescriptionsChanged;
        _groupDescriptions.CollectionChanged += OnGroupDescriptionsChanged;

        RefreshInternal();
        MoveCurrentToFirst();
    }

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Occurs when the collection changes.
    /// </summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    /// <summary>
    /// Occurs after the current item has been changed.
    /// </summary>
    public event EventHandler? CurrentChanged;

    /// <summary>
    /// Occurs when a property value is about to change.
    /// </summary>
    public event CurrentChangingEventHandler? CurrentChanging;

    /// <summary>
    /// Gets a value that indicates whether this view supports filtering via the Filter property.
    /// </summary>
    public virtual bool CanFilter => true;

    /// <summary>
    /// Gets a value that indicates whether this view supports grouping via GroupDescriptions.
    /// </summary>
    public virtual bool CanGroup => true;

    /// <summary>
    /// Gets a value that indicates whether this view supports sorting via SortDescriptions.
    /// </summary>
    public virtual bool CanSort => true;

    /// <summary>
    /// Gets or sets the culture information for any operations of the view that may differ by culture.
    /// </summary>
    public CultureInfo Culture
    {
        get => _culture;
        set
        {
            if (_culture != value)
            {
                _culture = value ?? CultureInfo.CurrentCulture;
                OnPropertyChanged(nameof(Culture));
                Refresh();
            }
        }
    }

    /// <summary>
    /// Gets the current item in the view.
    /// </summary>
    public object? CurrentItem => _currentItem;

    /// <summary>
    /// Gets the ordinal position of the CurrentItem within the view.
    /// </summary>
    public int CurrentPosition => _currentPosition;

    /// <summary>
    /// Gets or sets a callback used to determine if an item is suitable for inclusion in the view.
    /// </summary>
    public Predicate<object>? Filter
    {
        get => _filter;
        set
        {
            _filter = value;
            Refresh();
        }
    }

    /// <summary>
    /// Gets a collection of GroupDescription objects that describe how the items in the collection are grouped in the view.
    /// </summary>
    public ObservableCollection<GroupDescription> GroupDescriptions => _groupDescriptions;

    /// <summary>
    /// Gets the top-level groups.
    /// </summary>
    public ReadOnlyObservableCollection<object>? Groups => _groups;

    /// <summary>
    /// Gets a value that indicates whether the CurrentItem of the view is beyond the end of the collection.
    /// </summary>
    public bool IsCurrentAfterLast => _currentPosition >= Count;

    /// <summary>
    /// Gets a value that indicates whether the CurrentItem of the view is before the beginning of the collection.
    /// </summary>
    public bool IsCurrentBeforeFirst => _currentPosition < 0;

    /// <summary>
    /// Gets a value that indicates whether the view is empty.
    /// </summary>
    public bool IsEmpty => Count == 0;

    /// <summary>
    /// Gets a collection of SortDescription objects that describe how the items in the collection are sorted in the view.
    /// </summary>
    public SortDescriptionCollection SortDescriptions => _sortDescriptions;

    /// <summary>
    /// Returns the underlying collection.
    /// </summary>
    public IEnumerable SourceCollection => _sourceCollection;

    /// <summary>
    /// Gets the number of items in the view.
    /// </summary>
    public int Count => _internalList?.Count ?? 0;

    /// <summary>
    /// Returns a value that indicates whether a given item belongs to this collection view.
    /// </summary>
    public bool Contains(object item)
    {
        return _internalList?.Contains(item) ?? false;
    }

    /// <summary>
    /// Enters a defer cycle that you can use to merge changes to the view and delay automatic refresh.
    /// </summary>
    public IDisposable DeferRefresh()
    {
        _deferRefreshCount++;
        return new DeferRefreshHelper(this);
    }

    /// <summary>
    /// Sets the specified item to be the CurrentItem in the view.
    /// </summary>
    public bool MoveCurrentTo(object? item)
    {
        if (item == null)
        {
            return MoveCurrentToPosition(-1);
        }

        var index = _internalList?.IndexOf(item) ?? -1;
        return MoveCurrentToPosition(index);
    }

    /// <summary>
    /// Sets the first item in the view as the CurrentItem.
    /// </summary>
    public bool MoveCurrentToFirst()
    {
        return MoveCurrentToPosition(0);
    }

    /// <summary>
    /// Sets the last item in the view as the CurrentItem.
    /// </summary>
    public bool MoveCurrentToLast()
    {
        return MoveCurrentToPosition(Count - 1);
    }

    /// <summary>
    /// Sets the item after the CurrentItem in the view as the CurrentItem.
    /// </summary>
    public bool MoveCurrentToNext()
    {
        return MoveCurrentToPosition(_currentPosition + 1);
    }

    /// <summary>
    /// Sets the item at the specified index to be the CurrentItem in the view.
    /// </summary>
    public bool MoveCurrentToPosition(int position)
    {
        if (position < -1 || position > Count)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        if (position == _currentPosition)
        {
            return IsCurrentInView;
        }

        var args = new CurrentChangingEventArgs();
        OnCurrentChanging(args);

        if (args.Cancel)
        {
            return IsCurrentInView;
        }

        var oldCurrentItem = _currentItem;
        var oldCurrentPosition = _currentPosition;

        _currentPosition = position;
        _currentItem = (position >= 0 && position < Count) ? _internalList![position] : null;

        if (_currentItem != oldCurrentItem || _currentPosition != oldCurrentPosition)
        {
            OnCurrentChanged();
        }

        return IsCurrentInView;
    }

    /// <summary>
    /// Sets the item before the CurrentItem in the view as the CurrentItem.
    /// </summary>
    public bool MoveCurrentToPrevious()
    {
        return MoveCurrentToPosition(_currentPosition - 1);
    }

    /// <summary>
    /// Recreates the view.
    /// </summary>
    public void Refresh()
    {
        if (_deferRefreshCount > 0)
        {
            _needsRefresh = true;
            return;
        }

        RefreshInternal();
    }

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    public IEnumerator GetEnumerator()
    {
        return (_internalList ?? Enumerable.Empty<object>()).GetEnumerator();
    }

    /// <summary>
    /// Gets a value indicating whether the current item is within the view.
    /// </summary>
    protected bool IsCurrentInView => _currentPosition >= 0 && _currentPosition < Count;

    /// <summary>
    /// Raises the PropertyChanged event.
    /// </summary>
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Raises the CollectionChanged event.
    /// </summary>
    protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        CollectionChanged?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the CurrentChanged event.
    /// </summary>
    protected virtual void OnCurrentChanged()
    {
        CurrentChanged?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(CurrentItem));
        OnPropertyChanged(nameof(CurrentPosition));
        OnPropertyChanged(nameof(IsCurrentAfterLast));
        OnPropertyChanged(nameof(IsCurrentBeforeFirst));
    }

    /// <summary>
    /// Raises the CurrentChanging event.
    /// </summary>
    protected virtual void OnCurrentChanging(CurrentChangingEventArgs args)
    {
        CurrentChanging?.Invoke(this, args);
    }

    private void RefreshInternal()
    {
        var oldCurrentItem = _currentItem;

        _internalList = new List<object>();

        foreach (var item in _sourceCollection)
        {
            if (_filter == null || _filter(item))
            {
                _internalList.Add(item);
            }
        }

        // Apply sorting
        if (_sortDescriptions.Count > 0)
        {
            _internalList.Sort(new SortFieldComparer(_sortDescriptions, _culture));
        }

        // Apply grouping
        if (_groupDescriptions.Count > 0)
        {
            _groupsInternal = new ObservableCollection<object>();
            BuildGroups(_internalList, _groupDescriptions, 0, _groupsInternal);
            _groups = new ReadOnlyObservableCollection<object>(_groupsInternal);
        }
        else
        {
            _groupsInternal = null;
            _groups = null;
        }

        // Try to restore current item
        if (oldCurrentItem != null && _internalList.Contains(oldCurrentItem))
        {
            _currentPosition = _internalList.IndexOf(oldCurrentItem);
            _currentItem = oldCurrentItem;
        }
        else if (_internalList.Count > 0)
        {
            _currentPosition = 0;
            _currentItem = _internalList[0];
        }
        else
        {
            _currentPosition = -1;
            _currentItem = null;
        }

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsEmpty));
        OnCurrentChanged();
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Refresh();
    }

    private void OnSortDescriptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Refresh();
    }

    private void OnGroupDescriptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Refresh();
    }

    private void BuildGroups(List<object> items, ObservableCollection<GroupDescription> groupDescriptions, int level, ObservableCollection<object> targetGroups)
    {
        if (level >= groupDescriptions.Count) return;

        var description = groupDescriptions[level];
        var groupMap = new Dictionary<object, InternalCollectionViewGroup>();
        var groupOrder = new List<object>();

        foreach (var item in items)
        {
            var groupName = description.GroupNameFromItem(item, level, _culture) ?? "(null)";

            if (!groupMap.TryGetValue(groupName, out var group))
            {
                group = new InternalCollectionViewGroup(groupName);
                groupMap[groupName] = group;
                groupOrder.Add(groupName);
            }
            group.AddItem(item);
        }

        foreach (var key in groupOrder)
        {
            var group = groupMap[key];
            // Recursively group sub-levels
            if (level + 1 < groupDescriptions.Count)
            {
                var subItems = group.GetItemsSnapshot();
                group.Clear();
                var subGroups = new ObservableCollection<object>();
                BuildGroups(subItems, groupDescriptions, level + 1, subGroups);
                foreach (var subGroup in subGroups)
                    group.AddItem(subGroup);
            }
            targetGroups.Add(group);
        }
    }

    private void EndDeferRefresh()
    {
        _deferRefreshCount--;
        if (_deferRefreshCount == 0 && _needsRefresh)
        {
            _needsRefresh = false;
            RefreshInternal();
        }
    }

    private sealed class DeferRefreshHelper : IDisposable
    {
        private CollectionView? _view;

        public DeferRefreshHelper(CollectionView view)
        {
            _view = view;
        }

        public void Dispose()
        {
            if (_view != null)
            {
                _view.EndDeferRefresh();
                _view = null;
            }
        }
    }

    private sealed class SortFieldComparer : IComparer<object>
    {
        private readonly SortDescriptionCollection _sortDescriptions;
        private readonly CultureInfo _culture;

        public SortFieldComparer(SortDescriptionCollection sortDescriptions, CultureInfo culture)
        {
            _sortDescriptions = sortDescriptions;
            _culture = culture;
        }

        public int Compare(object? x, object? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            foreach (var sd in _sortDescriptions)
            {
                var valueX = GetPropertyValue(x, sd.PropertyName);
                var valueY = GetPropertyValue(y, sd.PropertyName);

                int result;
                if (valueX is IComparable comparableX)
                {
                    result = comparableX.CompareTo(valueY);
                }
                else if (valueX is string strX && valueY is string strY)
                {
                    result = string.Compare(strX, strY, _culture, CompareOptions.None);
                }
                else
                {
                    result = Comparer.Default.Compare(valueX, valueY);
                }

                if (result != 0)
                {
                    return sd.Direction == ListSortDirection.Descending ? -result : result;
                }
            }

            return 0;
        }

        private static object? GetPropertyValue(object item, string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
                return item;

            var type = item.GetType();
            var property = type.GetProperty(propertyName);
            return property?.GetValue(item);
        }
    }

    /// <summary>
    /// Internal concrete CollectionViewGroup used by BuildGroups.
    /// Can hold either data items (leaf level) or sub-groups (non-leaf level).
    /// </summary>
    private sealed class InternalCollectionViewGroup : CollectionViewGroup
    {
        private bool _isBottomLevel = true;

        public InternalCollectionViewGroup(object name) : base(name)
        {
        }

        public override bool IsBottomLevel => _isBottomLevel;

        public void AddItem(object item)
        {
            if (item is CollectionViewGroup)
                _isBottomLevel = false;
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
