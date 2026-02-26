using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Jalium.UI;

/// <summary>
/// Provides a WeakEventManager implementation for the INotifyPropertyChanged.PropertyChanged event.
/// Supports per-property filtering so that handlers registered for a specific property name
/// only receive notifications when that property changes.
/// </summary>
public sealed class PropertyChangedEventManager : WeakEventManager
{
    // Per-source: propertyName -> list of weak handler references
    // Empty string key "" means "all properties"
    private readonly ConditionalWeakTable<object, PropertyHandlerMap> _sourceHandlers = new();
    private readonly object _syncRoot = new();

    private PropertyChangedEventManager() { }

    public static void AddHandler(INotifyPropertyChanged source, EventHandler<PropertyChangedEventArgs> handler, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);

        var manager = GetManager();
        lock (manager._syncRoot)
        {
            var map = manager.GetOrCreateMap(source);
            var key = propertyName ?? "";
            map.Add(key, handler);

            if (map.TotalCount == 1)
            {
                // First handler for this source - start listening
                manager.StartListening(source);
            }
        }
    }

    public static void RemoveHandler(INotifyPropertyChanged source, EventHandler<PropertyChangedEventArgs> handler, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);

        var manager = GetManager();
        lock (manager._syncRoot)
        {
            if (!manager._sourceHandlers.TryGetValue(source, out var map))
                return;

            var key = propertyName ?? "";
            map.Remove(key, handler);

            if (map.TotalCount == 0)
            {
                manager.StopListening(source);
            }
        }
    }

    public static void AddListener(INotifyPropertyChanged source, IWeakEventListener listener)
    {
        var manager = GetManager();
        manager.ProtectedAddListener(source, listener);
    }

    public static void RemoveListener(INotifyPropertyChanged source, IWeakEventListener listener)
    {
        var manager = GetManager();
        manager.ProtectedRemoveListener(source, listener);
    }

    protected override void StartListening(object source)
    {
        if (source is INotifyPropertyChanged inpc)
            inpc.PropertyChanged += OnPropertyChanged;
    }

    protected override void StopListening(object source)
    {
        if (source is INotifyPropertyChanged inpc)
            inpc.PropertyChanged -= OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == null) return;

        // Deliver to IWeakEventListener instances (base class handles these)
        DeliverEvent(sender, e);

        // Deliver to per-property handlers
        lock (_syncRoot)
        {
            if (!_sourceHandlers.TryGetValue(sender, out var map))
                return;

            var changedProp = e.PropertyName ?? "";

            // Deliver to handlers registered for this specific property
            if (!string.IsNullOrEmpty(changedProp))
            {
                map.Deliver(changedProp, sender, e);
            }

            // Always deliver to handlers registered for "" (all properties)
            map.Deliver("", sender, e);
        }
    }

    private PropertyHandlerMap GetOrCreateMap(object source)
    {
        if (!_sourceHandlers.TryGetValue(source, out var map))
        {
            map = new PropertyHandlerMap();
            _sourceHandlers.AddOrUpdate(source, map);
        }
        return map;
    }

    private static PropertyChangedEventManager GetManager()
    {
        var manager = GetCurrentManager(typeof(PropertyChangedEventManager)) as PropertyChangedEventManager;
        if (manager == null)
        {
            manager = new PropertyChangedEventManager();
            SetCurrentManager(typeof(PropertyChangedEventManager), manager);
        }
        return manager;
    }

    private sealed class PropertyHandlerMap
    {
        private readonly Dictionary<string, List<WeakReference<EventHandler<PropertyChangedEventArgs>>>> _map = new();

        public int TotalCount
        {
            get
            {
                int count = 0;
                foreach (var list in _map.Values)
                    count += list.Count;
                return count;
            }
        }

        public void Add(string key, EventHandler<PropertyChangedEventArgs> handler)
        {
            if (!_map.TryGetValue(key, out var list))
            {
                list = new List<WeakReference<EventHandler<PropertyChangedEventArgs>>>();
                _map[key] = list;
            }
            list.Add(new WeakReference<EventHandler<PropertyChangedEventArgs>>(handler));
        }

        public void Remove(string key, EventHandler<PropertyChangedEventArgs> handler)
        {
            if (!_map.TryGetValue(key, out var list))
                return;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!list[i].TryGetTarget(out var target) || target == handler)
                    list.RemoveAt(i);
            }

            if (list.Count == 0)
                _map.Remove(key);
        }

        public void Deliver(string key, object sender, PropertyChangedEventArgs e)
        {
            if (!_map.TryGetValue(key, out var list))
                return;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].TryGetTarget(out var handler))
                {
                    handler(sender, e);
                }
                else
                {
                    list.RemoveAt(i);
                }
            }
        }
    }
}
