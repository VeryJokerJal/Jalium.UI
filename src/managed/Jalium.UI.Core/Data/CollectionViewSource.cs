using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;

namespace Jalium.UI.Data;

/// <summary>
/// The XAML proxy of a CollectionView class.
/// </summary>
public sealed class CollectionViewSource : DependencyObject, ISupportInitialize
{
    private static readonly Dictionary<object, WeakReference<ICollectionView>> _defaultViews = new();

    private bool _isInitializing;
    private bool _deferRefresh;

    /// <summary>
    /// Identifies the Source dependency property.
    /// </summary>
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(object), typeof(CollectionViewSource),
            new PropertyMetadata(null, OnSourceChanged));

    /// <summary>
    /// Identifies the View read-only dependency property key.
    /// </summary>
    private static readonly DependencyPropertyKey ViewPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(View), typeof(ICollectionView), typeof(CollectionViewSource),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the View dependency property.
    /// </summary>
    public static readonly DependencyProperty ViewProperty = ViewPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the IsLiveFilteringRequested dependency property.
    /// </summary>
    public static readonly DependencyProperty IsLiveFilteringRequestedProperty =
        DependencyProperty.Register(nameof(IsLiveFilteringRequested), typeof(bool), typeof(CollectionViewSource),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsLiveSortingRequested dependency property.
    /// </summary>
    public static readonly DependencyProperty IsLiveSortingRequestedProperty =
        DependencyProperty.Register(nameof(IsLiveSortingRequested), typeof(bool), typeof(CollectionViewSource),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsLiveGroupingRequested dependency property.
    /// </summary>
    public static readonly DependencyProperty IsLiveGroupingRequestedProperty =
        DependencyProperty.Register(nameof(IsLiveGroupingRequested), typeof(bool), typeof(CollectionViewSource),
            new PropertyMetadata(false));

    /// <summary>
    /// Initializes a new instance of the CollectionViewSource class.
    /// </summary>
    public CollectionViewSource()
    {
        _sortDescriptions = new SortDescriptionCollection();
        _groupDescriptions = new ObservableCollection<GroupDescription>();
        _sortDescriptions.CollectionChanged += OnSortDescriptionsChanged;
        _groupDescriptions.CollectionChanged += OnGroupDescriptionsChanged;
    }

    private readonly SortDescriptionCollection _sortDescriptions;
    private readonly ObservableCollection<GroupDescription> _groupDescriptions;
    private CultureInfo _culture = CultureInfo.CurrentCulture;

    /// <summary>
    /// Occurs when the Filter event is raised.
    /// </summary>
    public event FilterEventHandler? Filter;

    /// <summary>
    /// Gets or sets the collection object from which to create this view.
    /// </summary>
    public object? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Gets the view object that is currently associated with this instance of CollectionViewSource.
    /// </summary>
    public ICollectionView? View => (ICollectionView?)GetValue(ViewProperty);

    /// <summary>
    /// Gets or sets the culture that is used for operations such as sorting and comparisons.
    /// </summary>
    public CultureInfo Culture
    {
        get => _culture;
        set
        {
            _culture = value ?? CultureInfo.CurrentCulture;
            if (View != null)
            {
                View.Culture = _culture;
            }
        }
    }

    /// <summary>
    /// Gets a collection of SortDescription objects that describe how the items in the collection are sorted in the view.
    /// </summary>
    public SortDescriptionCollection SortDescriptions => _sortDescriptions;

    /// <summary>
    /// Gets a collection of GroupDescription objects that describe how the items in the collection are grouped in the view.
    /// </summary>
    public ObservableCollection<GroupDescription> GroupDescriptions => _groupDescriptions;

    /// <summary>
    /// Gets or sets a value that indicates whether live filtering is requested.
    /// </summary>
    public bool IsLiveFilteringRequested
    {
        get => (bool)GetValue(IsLiveFilteringRequestedProperty)!;
        set => SetValue(IsLiveFilteringRequestedProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether live sorting is requested.
    /// </summary>
    public bool IsLiveSortingRequested
    {
        get => (bool)GetValue(IsLiveSortingRequestedProperty)!;
        set => SetValue(IsLiveSortingRequestedProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether live grouping is requested.
    /// </summary>
    public bool IsLiveGroupingRequested
    {
        get => (bool)GetValue(IsLiveGroupingRequestedProperty)!;
        set => SetValue(IsLiveGroupingRequestedProperty, value);
    }

    /// <summary>
    /// Returns the default view for the given source.
    /// </summary>
    /// <param name="source">The source collection.</param>
    /// <returns>The default ICollectionView for the given source.</returns>
    public static ICollectionView GetDefaultView(object source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // Check cache
        if (_defaultViews.TryGetValue(source, out var weakRef) && weakRef.TryGetTarget(out var existingView))
        {
            return existingView;
        }

        // Create new view
        ICollectionView view;
        if (source is IList list)
        {
            view = new ListCollectionView(list);
        }
        else if (source is IEnumerable enumerable)
        {
            view = new CollectionView(enumerable);
        }
        else
        {
            throw new ArgumentException("Source must be IEnumerable.", nameof(source));
        }

        // Cache the view
        _defaultViews[source] = new WeakReference<ICollectionView>(view);

        return view;
    }

    /// <summary>
    /// Signals the object that initialization is starting.
    /// </summary>
    public void BeginInit()
    {
        _isInitializing = true;
        _deferRefresh = true;
    }

    /// <summary>
    /// Signals the object that initialization is complete.
    /// </summary>
    public void EndInit()
    {
        _isInitializing = false;
        _deferRefresh = false;
        EnsureView();
    }

    /// <summary>
    /// Enters a defer cycle that you can use to merge changes to the view and delay automatic refresh.
    /// </summary>
    /// <returns>An IDisposable object that you can use to dispose of the calling object.</returns>
    public IDisposable DeferRefresh()
    {
        return View?.DeferRefresh() ?? new DeferRefreshHelper(this);
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CollectionViewSource cvs)
        {
            cvs.OnSourceChanged(e.OldValue, e.NewValue);
        }
    }

    private void OnSourceChanged(object? oldValue, object? newValue)
    {
        if (_isInitializing)
            return;

        EnsureView();
    }

    private void EnsureView()
    {
        var source = Source;
        if (source == null)
        {
            SetValue(ViewPropertyKey.DependencyProperty, null);
            return;
        }

        ICollectionView view;
        if (source is ICollectionView cv)
        {
            view = cv;
        }
        else if (source is IList list)
        {
            view = new ListCollectionView(list);
        }
        else if (source is IEnumerable enumerable)
        {
            view = new CollectionView(enumerable);
        }
        else
        {
            SetValue(ViewPropertyKey.DependencyProperty, null);
            return;
        }

        // Apply culture
        view.Culture = _culture;

        // Apply sort descriptions
        view.SortDescriptions.Clear();
        foreach (var sd in _sortDescriptions)
        {
            view.SortDescriptions.Add(sd);
        }

        // Apply group descriptions
        view.GroupDescriptions.Clear();
        foreach (var gd in _groupDescriptions)
        {
            view.GroupDescriptions.Add(gd);
        }

        // Apply filter
        if (Filter != null)
        {
            view.Filter = item =>
            {
                var args = new FilterEventArgs(item);
                Filter(this, args);
                return args.Accepted;
            };
        }

        SetValue(ViewPropertyKey.DependencyProperty, view);
    }

    private void OnSortDescriptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_deferRefresh || View == null)
            return;

        View.SortDescriptions.Clear();
        foreach (var sd in _sortDescriptions)
        {
            View.SortDescriptions.Add(sd);
        }
    }

    private void OnGroupDescriptionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_deferRefresh || View == null)
            return;

        View.GroupDescriptions.Clear();
        foreach (var gd in _groupDescriptions)
        {
            View.GroupDescriptions.Add(gd);
        }
    }

    private sealed class DeferRefreshHelper : IDisposable
    {
        private CollectionViewSource? _source;

        public DeferRefreshHelper(CollectionViewSource source)
        {
            _source = source;
            _source._deferRefresh = true;
        }

        public void Dispose()
        {
            if (_source != null)
            {
                _source._deferRefresh = false;
                _source.EnsureView();
                _source = null;
            }
        }
    }
}

/// <summary>
/// Provides data for the Filter event.
/// </summary>
public sealed class FilterEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the FilterEventArgs class.
    /// </summary>
    /// <param name="item">The item to filter.</param>
    public FilterEventArgs(object item)
    {
        Item = item;
        Accepted = true;
    }

    /// <summary>
    /// Gets the object that the filter should test.
    /// </summary>
    public object Item { get; }

    /// <summary>
    /// Gets or sets a value that indicates whether the item passes the filter.
    /// </summary>
    public bool Accepted { get; set; }
}

/// <summary>
/// Represents the method that will handle the Filter event.
/// </summary>
public delegate void FilterEventHandler(object sender, FilterEventArgs e);
