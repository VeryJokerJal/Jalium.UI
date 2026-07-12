using Jalium.UI;

namespace System.Collections.Specialized;

/// <summary>
/// Delivers <see cref="INotifyCollectionChanged.CollectionChanged"/> without strongly retaining listeners.
/// </summary>
public class CollectionChangedEventManager : WeakEventManager
{
    private static CollectionChangedEventManager CurrentManager
    {
        get
        {
            var manager = (CollectionChangedEventManager?)GetCurrentManager(typeof(CollectionChangedEventManager));
            if (manager is null)
            {
                manager = new CollectionChangedEventManager();
                SetCurrentManager(typeof(CollectionChangedEventManager), manager);
            }

            return manager;
        }
    }

    private CollectionChangedEventManager()
    {
    }

    public static void AddListener(INotifyCollectionChanged source, IWeakEventListener listener)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(listener);
        CurrentManager.ProtectedAddListener(source, listener);
    }

    public static void RemoveListener(INotifyCollectionChanged source, IWeakEventListener listener)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(listener);
        CurrentManager.ProtectedRemoveListener(source, listener);
    }

    public static void AddHandler(
        INotifyCollectionChanged source,
        EventHandler<NotifyCollectionChangedEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);
        CurrentManager.ProtectedAddHandler(source, handler);
    }

    public static void RemoveHandler(
        INotifyCollectionChanged source,
        EventHandler<NotifyCollectionChangedEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);
        CurrentManager.ProtectedRemoveHandler(source, handler);
    }

    protected override ListenerList NewListenerList() => new ListenerList<NotifyCollectionChangedEventArgs>();

    protected override void StartListening(object source) =>
        ((INotifyCollectionChanged)source).CollectionChanged += OnCollectionChanged;

    protected override void StopListening(object source) =>
        ((INotifyCollectionChanged)source).CollectionChanged -= OnCollectionChanged;

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (sender is not null)
        {
            DeliverEvent(sender, e);
        }
    }
}
