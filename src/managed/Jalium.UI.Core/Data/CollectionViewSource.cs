using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Jalium.UI.Data;

/// <summary>
/// Provides a declarative proxy that creates and configures an <see cref="ICollectionView"/>.
/// </summary>
public class CollectionViewSource : DependencyObject, ISupportInitialize, IWeakEventListener
{
    private sealed class DefaultViewCacheEntry
    {
        public WeakReference<ICollectionView>? View { get; set; }
    }

    private static readonly ConditionalWeakTable<object, DefaultViewCacheEntry> DefaultViews = new();

    private static readonly DependencyPropertyKey ViewPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(View),
            typeof(ICollectionView),
            typeof(CollectionViewSource),
            new PropertyMetadata(null));

    private static readonly DependencyPropertyKey CanChangeLiveFilteringPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(CanChangeLiveFiltering),
            typeof(bool),
            typeof(CollectionViewSource),
            new PropertyMetadata(false));

    private static readonly DependencyPropertyKey CanChangeLiveGroupingPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(CanChangeLiveGrouping),
            typeof(bool),
            typeof(CollectionViewSource),
            new PropertyMetadata(false));

    private static readonly DependencyPropertyKey CanChangeLiveSortingPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(CanChangeLiveSorting),
            typeof(bool),
            typeof(CollectionViewSource),
            new PropertyMetadata(false));

    private static readonly DependencyPropertyKey IsLiveFilteringPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsLiveFiltering),
            typeof(bool?),
            typeof(CollectionViewSource),
            new PropertyMetadata(null));

    private static readonly DependencyPropertyKey IsLiveGroupingPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsLiveGrouping),
            typeof(bool?),
            typeof(CollectionViewSource),
            new PropertyMetadata(null));

    private static readonly DependencyPropertyKey IsLiveSortingPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsLiveSorting),
            typeof(bool?),
            typeof(CollectionViewSource),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the source dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(
            nameof(Source),
            typeof(object),
            typeof(CollectionViewSource),
            new PropertyMetadata(null, OnSourcePropertyChanged),
            IsSourceValid);

    /// <summary>
    /// Identifies the read-only view dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public static readonly DependencyProperty ViewProperty = ViewPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the requested view type dependency property.
    /// </summary>
    public static readonly DependencyProperty CollectionViewTypeProperty =
        DependencyProperty.Register(
            nameof(CollectionViewType),
            typeof(Type),
            typeof(CollectionViewSource),
            new PropertyMetadata(null, OnCollectionViewTypePropertyChanged),
            IsCollectionViewTypeValid);

    /// <summary>
    /// Identifies the live-filtering request dependency property.
    /// </summary>
    public static readonly DependencyProperty IsLiveFilteringRequestedProperty =
        DependencyProperty.Register(
            nameof(IsLiveFilteringRequested),
            typeof(bool),
            typeof(CollectionViewSource),
            new PropertyMetadata(false, OnForwardedDependencyPropertyChanged));

    /// <summary>
    /// Identifies the live-grouping request dependency property.
    /// </summary>
    public static readonly DependencyProperty IsLiveGroupingRequestedProperty =
        DependencyProperty.Register(
            nameof(IsLiveGroupingRequested),
            typeof(bool),
            typeof(CollectionViewSource),
            new PropertyMetadata(false, OnForwardedDependencyPropertyChanged));

    /// <summary>
    /// Identifies the live-sorting request dependency property.
    /// </summary>
    public static readonly DependencyProperty IsLiveSortingRequestedProperty =
        DependencyProperty.Register(
            nameof(IsLiveSortingRequested),
            typeof(bool),
            typeof(CollectionViewSource),
            new PropertyMetadata(false, OnForwardedDependencyPropertyChanged));

    /// <summary>
    /// Identifies the read-only live-filtering capability dependency property.
    /// </summary>
    public static readonly DependencyProperty CanChangeLiveFilteringProperty =
        CanChangeLiveFilteringPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the read-only live-grouping capability dependency property.
    /// </summary>
    public static readonly DependencyProperty CanChangeLiveGroupingProperty =
        CanChangeLiveGroupingPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the read-only live-sorting capability dependency property.
    /// </summary>
    public static readonly DependencyProperty CanChangeLiveSortingProperty =
        CanChangeLiveSortingPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the read-only live-filtering state dependency property.
    /// </summary>
    public static readonly DependencyProperty IsLiveFilteringProperty =
        IsLiveFilteringPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the read-only live-grouping state dependency property.
    /// </summary>
    public static readonly DependencyProperty IsLiveGroupingProperty =
        IsLiveGroupingPropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the read-only live-sorting state dependency property.
    /// </summary>
    public static readonly DependencyProperty IsLiveSortingProperty =
        IsLiveSortingPropertyKey.DependencyProperty;

    private readonly System.ComponentModel.SortDescriptionCollection _sortDescriptions = new();
    private readonly ObservableCollection<System.ComponentModel.GroupDescription> _groupDescriptions = new();
    private readonly ObservableCollection<string> _liveFilteringProperties = new();
    private readonly ObservableCollection<string> _liveGroupingProperties = new();
    private readonly ObservableCollection<string> _liveSortingProperties = new();
    private CultureInfo _culture = CultureInfo.CurrentCulture;
    private FilterEventHandler? _filter;
    private bool _isInitializing;
    private int _deferLevel;
    private DataSourceProvider? _dataProvider;
    private INotifyPropertyChanged? _observedView;

    /// <summary>
    /// Initializes a new collection view source.
    /// </summary>
    public CollectionViewSource()
    {
        ((INotifyCollectionChanged)_sortDescriptions).CollectionChanged += OnForwardedCollectionChanged;
        _groupDescriptions.CollectionChanged += OnForwardedCollectionChanged;
        _liveFilteringProperties.CollectionChanged += OnForwardedCollectionChanged;
        _liveGroupingProperties.CollectionChanged += OnForwardedCollectionChanged;
        _liveSortingProperties.CollectionChanged += OnForwardedCollectionChanged;
    }

    /// <summary>
    /// Occurs when an item is tested by the view filter.
    /// </summary>
    public event FilterEventHandler? Filter
    {
        add
        {
            _filter += value;
            OnForwardedPropertyChanged();
        }
        remove
        {
            _filter -= value;
            OnForwardedPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the source object.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public object? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Gets the configured view.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public ICollectionView? View => (ICollectionView?)GetValue(ViewProperty);

    /// <summary>
    /// Gets or sets the desired collection-view type. This property is initialization-only.
    /// </summary>
    public Type? CollectionViewType
    {
        get => (Type?)GetValue(CollectionViewTypeProperty);
        set => SetValue(CollectionViewTypeProperty, value);
    }

    /// <summary>
    /// Gets or sets the culture forwarded to the view.
    /// </summary>
    public CultureInfo Culture
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
            OnForwardedPropertyChanged();
        }
    }

    /// <summary>
    /// Gets sort descriptions forwarded to the view.
    /// </summary>
    public System.ComponentModel.SortDescriptionCollection SortDescriptions => _sortDescriptions;

    /// <summary>
    /// Gets group descriptions forwarded to the view.
    /// </summary>
    public ObservableCollection<System.ComponentModel.GroupDescription> GroupDescriptions => _groupDescriptions;

    /// <summary>
    /// Gets or sets whether live filtering is requested.
    /// </summary>
    public bool IsLiveFilteringRequested
    {
        get => (bool)GetValue(IsLiveFilteringRequestedProperty)!;
        set => SetValue(IsLiveFilteringRequestedProperty, value);
    }

    /// <summary>
    /// Gets or sets whether live grouping is requested.
    /// </summary>
    public bool IsLiveGroupingRequested
    {
        get => (bool)GetValue(IsLiveGroupingRequestedProperty)!;
        set => SetValue(IsLiveGroupingRequestedProperty, value);
    }

    /// <summary>
    /// Gets or sets whether live sorting is requested.
    /// </summary>
    public bool IsLiveSortingRequested
    {
        get => (bool)GetValue(IsLiveSortingRequestedProperty)!;
        set => SetValue(IsLiveSortingRequestedProperty, value);
    }

    /// <summary>
    /// Gets whether the current view can change live filtering.
    /// </summary>
    [ReadOnly(true)]
    public bool CanChangeLiveFiltering => (bool)GetValue(CanChangeLiveFilteringProperty)!;

    /// <summary>
    /// Gets whether the current view can change live grouping.
    /// </summary>
    [ReadOnly(true)]
    public bool CanChangeLiveGrouping => (bool)GetValue(CanChangeLiveGroupingProperty)!;

    /// <summary>
    /// Gets whether the current view can change live sorting.
    /// </summary>
    [ReadOnly(true)]
    public bool CanChangeLiveSorting => (bool)GetValue(CanChangeLiveSortingProperty)!;

    /// <summary>
    /// Gets the current live-filtering state.
    /// </summary>
    [ReadOnly(true)]
    public bool? IsLiveFiltering => (bool?)GetValue(IsLiveFilteringProperty);

    /// <summary>
    /// Gets the current live-grouping state.
    /// </summary>
    [ReadOnly(true)]
    public bool? IsLiveGrouping => (bool?)GetValue(IsLiveGroupingProperty);

    /// <summary>
    /// Gets the current live-sorting state.
    /// </summary>
    [ReadOnly(true)]
    public bool? IsLiveSorting => (bool?)GetValue(IsLiveSortingProperty);

    /// <summary>
    /// Gets live-filtering property names forwarded to the view.
    /// </summary>
    public ObservableCollection<string> LiveFilteringProperties => _liveFilteringProperties;

    /// <summary>
    /// Gets live-grouping property names forwarded to the view.
    /// </summary>
    public ObservableCollection<string> LiveGroupingProperties => _liveGroupingProperties;

    /// <summary>
    /// Gets live-sorting property names forwarded to the view.
    /// </summary>
    public ObservableCollection<string> LiveSortingProperties => _liveSortingProperties;

    /// <summary>
    /// Returns the cached default view for a source, creating it when necessary.
    /// </summary>
    public static ICollectionView GetDefaultView(object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source is ICollectionView existingView)
        {
            return existingView;
        }

        if (DefaultViews.TryGetValue(source, out var entry) &&
            entry.View?.TryGetTarget(out var cachedView) == true)
        {
            return cachedView;
        }

        var view = CreateDefaultView(source)
            ?? throw new ArgumentException("Source must provide or contain an enumerable collection.", nameof(source));
        DefaultViews.GetOrCreateValue(source).View = new WeakReference<ICollectionView>(view);
        return view;
    }

    /// <summary>
    /// Returns whether a view is the cached default for its source.
    /// </summary>
    public static bool IsDefaultView(ICollectionView? view)
    {
        if (view == null)
        {
            return true;
        }

        return ReferenceEquals(view, GetDefaultView(view.SourceCollection));
    }

    /// <summary>
    /// Starts initialization, during which <see cref="CollectionViewType"/> can be set.
    /// </summary>
    public void BeginInit() => _isInitializing = true;

    /// <summary>
    /// Completes initialization and creates the view.
    /// </summary>
    public void EndInit()
    {
        _isInitializing = false;
        EnsureView();
    }

    /// <summary>
    /// Defers forwarding property changes until disposal.
    /// </summary>
    public IDisposable DeferRefresh()
    {
        _deferLevel++;
        return new DeferRefreshHelper(this);
    }

    /// <summary>
    /// Called when <see cref="Source"/> changes.
    /// </summary>
    protected virtual void OnSourceChanged(object? oldSource, object? newSource)
    {
    }

    /// <summary>
    /// Called when <see cref="CollectionViewType"/> changes.
    /// </summary>
    protected virtual void OnCollectionViewTypeChanged(Type? oldCollectionViewType, Type? newCollectionViewType)
    {
    }

    /// <summary>
    /// Handles weak events from the active data provider or view.
    /// </summary>
    protected virtual bool ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
    {
        if (managerType == typeof(DataChangedEventManager) && ReferenceEquals(sender, _dataProvider))
        {
            EnsureView();
            return true;
        }

        if (managerType == typeof(PropertyChangedEventManager) && ReferenceEquals(sender, _observedView))
        {
            UpdateLiveShapingState(View);
            return true;
        }

        return false;
    }

    bool IWeakEventListener.ReceiveWeakEvent(Type managerType, object sender, EventArgs e) =>
        ReceiveWeakEvent(managerType, sender, e);

    private static bool IsSourceValid(object? source) =>
        source == null ||
        source is IEnumerable ||
        source is IListSource ||
        source is DataSourceProvider ||
        source is ICollectionView ||
        source is System.ComponentModel.ICollectionViewFactory;

    private static bool IsCollectionViewTypeValid(object? value) =>
        value == null || value is Type type && typeof(ICollectionView).IsAssignableFrom(type);

    private static void OnSourcePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var source = (CollectionViewSource)d;
        source.OnSourceChanged(e.OldValue, e.NewValue);
        source.EnsureView();
    }

    private static void OnCollectionViewTypePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var source = (CollectionViewSource)d;
        if (!source._isInitializing)
        {
            throw new InvalidOperationException("CollectionViewType can only be set during initialization.");
        }

        source.OnCollectionViewTypeChanged((Type?)e.OldValue, (Type?)e.NewValue);
    }

    private static void OnForwardedDependencyPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((CollectionViewSource)d).OnForwardedPropertyChanged();

    private static ICollectionView? CreateDefaultView(object source)
    {
        if (source is System.ComponentModel.ICollectionViewFactory componentModelFactory)
        {
            return componentModelFactory.CreateView();
        }

        if (source is IBindingList bindingList)
        {
            return new BindingListCollectionView(bindingList);
        }

        if (source is IList list)
        {
            return new ListCollectionView(list);
        }

        if (source is IListSource listSource)
        {
            return CreateDefaultView(listSource.GetList());
        }

        return source is IEnumerable enumerable ? new CollectionView(enumerable) : null;
    }

    private void EnsureView()
    {
        if (_isInitializing || _deferLevel > 0)
        {
            return;
        }

        object? source = Source;
        var provider = source as DataSourceProvider;
        if (!ReferenceEquals(provider, _dataProvider))
        {
            if (_dataProvider != null)
            {
                DataChangedEventManager.RemoveListener(_dataProvider, this);
            }

            _dataProvider = provider;
            if (_dataProvider != null)
            {
                DataChangedEventManager.AddListener(_dataProvider, this);
            }
        }

        if (provider != null)
        {
            source = provider.Data;
        }

        ICollectionView? view = null;
        var requestedViewType = CollectionViewType;
        if (source is ICollectionView suppliedView)
        {
            view = suppliedView;
        }
        else if (source != null && requestedViewType != null)
        {
            view = CreateRequestedView(requestedViewType, source);
        }
        else if (source != null)
        {
            view = CreateDefaultView(source);
        }

        ObserveView(view);
        if (view != null)
        {
            ApplyPropertiesToView(view);
        }
        else
        {
            UpdateLiveShapingState(null);
        }

        SetValue(ViewPropertyKey, view);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2067:UnrecognizedReflectionPattern",
        Justification = "CollectionViewType is an explicit runtime extension point; consumers preserve the selected view constructor under trimming.")]
    private static ICollectionView CreateRequestedView(Type viewType, object source)
    {
        if (!typeof(ICollectionView).IsAssignableFrom(viewType))
        {
            throw new ArgumentException("CollectionViewType must implement ICollectionView.", nameof(viewType));
        }

        try
        {
            return (ICollectionView)(Activator.CreateInstance(viewType, source)
                ?? throw new InvalidOperationException($"Could not create collection view '{viewType}'."));
        }
        catch (MissingMethodException ex)
        {
            throw new InvalidOperationException(
                $"Collection view type '{viewType}' must expose a constructor accepting the source collection.",
                ex);
        }
    }

    private void ApplyPropertiesToView(ICollectionView view)
    {
        using (view.DeferRefresh())
        {
            view.Culture = _culture;

            if (!view.CanSort && _sortDescriptions.Count > 0)
            {
                throw new InvalidOperationException("The selected view does not support sorting.");
            }

            if (view.CanSort)
            {
                view.SortDescriptions.Clear();
                foreach (var sort in _sortDescriptions)
                {
                    view.SortDescriptions.Add(sort);
                }
            }

            if (_filter != null && !view.CanFilter)
            {
                throw new InvalidOperationException("The selected view does not support filtering.");
            }

            if (view.CanFilter)
            {
                view.Filter = _filter == null ? null : WrapFilter;
            }

            if (!view.CanGroup && _groupDescriptions.Count > 0)
            {
                throw new InvalidOperationException("The selected view does not support grouping.");
            }

            if (view.CanGroup)
            {
                view.GroupDescriptions.Clear();
                foreach (var group in _groupDescriptions)
                {
                    view.GroupDescriptions.Add(group);
                }
            }

            ApplyLiveShaping(view);
        }

        UpdateLiveShapingState(view);
    }

    private bool WrapFilter(object item)
    {
        var args = new FilterEventArgs(item);
        _filter?.Invoke(this, args);
        return args.Accepted;
    }

    private void ApplyLiveShaping(ICollectionView view)
    {
        if (view is not ICollectionViewLiveShaping liveView)
        {
            return;
        }

        if (liveView.CanChangeLiveSorting)
        {
            liveView.IsLiveSorting = IsLiveSortingRequested;
            SynchronizeStrings(liveView.LiveSortingProperties, _liveSortingProperties, IsLiveSortingRequested);
        }

        if (liveView.CanChangeLiveFiltering)
        {
            liveView.IsLiveFiltering = IsLiveFilteringRequested;
            SynchronizeStrings(liveView.LiveFilteringProperties, _liveFilteringProperties, IsLiveFilteringRequested);
        }

        if (liveView.CanChangeLiveGrouping)
        {
            liveView.IsLiveGrouping = IsLiveGroupingRequested;
            SynchronizeStrings(liveView.LiveGroupingProperties, _liveGroupingProperties, IsLiveGroupingRequested);
        }
    }

    private static void SynchronizeStrings(
        ObservableCollection<string> destination,
        ObservableCollection<string> source,
        bool enabled)
    {
        destination.Clear();
        if (!enabled)
        {
            return;
        }

        foreach (var propertyName in source)
        {
            destination.Add(propertyName);
        }
    }

    private void UpdateLiveShapingState(ICollectionView? view)
    {
        var liveView = view as ICollectionViewLiveShaping;
        SetValue(CanChangeLiveFilteringPropertyKey, liveView?.CanChangeLiveFiltering ?? false);
        SetValue(CanChangeLiveGroupingPropertyKey, liveView?.CanChangeLiveGrouping ?? false);
        SetValue(CanChangeLiveSortingPropertyKey, liveView?.CanChangeLiveSorting ?? false);
        SetValue(IsLiveFilteringPropertyKey, liveView?.IsLiveFiltering);
        SetValue(IsLiveGroupingPropertyKey, liveView?.IsLiveGrouping);
        SetValue(IsLiveSortingPropertyKey, liveView?.IsLiveSorting);
    }

    private void ObserveView(ICollectionView? view)
    {
        if (_observedView != null)
        {
            PropertyChangedEventManager.RemoveListener(_observedView, this);
        }

        _observedView = view as INotifyPropertyChanged;
        if (_observedView != null)
        {
            PropertyChangedEventManager.AddListener(_observedView, this);
        }
    }

    private void OnForwardedCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnForwardedPropertyChanged();

    private void OnForwardedPropertyChanged()
    {
        if (_isInitializing || _deferLevel > 0)
        {
            return;
        }

        if (View is { } view)
        {
            ApplyPropertiesToView(view);
        }
        else
        {
            EnsureView();
        }
    }

    private void EndDeferRefresh()
    {
        if (_deferLevel <= 0)
        {
            return;
        }

        _deferLevel--;
        if (_deferLevel == 0)
        {
            EnsureView();
            if (View is { } view)
            {
                ApplyPropertiesToView(view);
            }
        }
    }

    private sealed class DeferRefreshHelper : IDisposable
    {
        private CollectionViewSource? _source;

        public DeferRefreshHelper(CollectionViewSource source) => _source = source;

        public void Dispose()
        {
            var source = Interlocked.Exchange(ref _source, null);
            source?.EndDeferRefresh();
        }
    }
}

/// <summary>
/// Provides data for the <see cref="CollectionViewSource.Filter"/> event.
/// </summary>
public sealed class FilterEventArgs : EventArgs
{
    /// <summary>
    /// Initializes filter-event data for an item.
    /// </summary>
    public FilterEventArgs(object item)
    {
        Item = item;
        Accepted = true;
    }

    /// <summary>
    /// Gets the item being tested.
    /// </summary>
    public object Item { get; }

    /// <summary>
    /// Gets or sets whether the item is accepted.
    /// </summary>
    public bool Accepted { get; set; }
}

/// <summary>
/// Handles <see cref="CollectionViewSource.Filter"/>.
/// </summary>
public delegate void FilterEventHandler(object sender, FilterEventArgs e);
