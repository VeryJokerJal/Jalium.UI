using System.Collections.Specialized;

namespace Jalium.UI;

/// <summary>
/// Provides a WeakEventManager implementation for the INotifyCollectionChanged.CollectionChanged event.
/// </summary>
public sealed class CollectionChangedEventManager : WeakEventManager
{
    private CollectionChangedEventManager()
    {
    }

    /// <summary>
    /// Adds the specified handler to the CollectionChanged event of the specified source.
    /// </summary>
    public static void AddHandler(INotifyCollectionChanged source, EventHandler<NotifyCollectionChangedEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);

        var manager = GetManager();
        manager.ProtectedAddHandler(source, handler);
    }

    /// <summary>
    /// Removes the specified handler from the CollectionChanged event of the specified source.
    /// </summary>
    public static void RemoveHandler(INotifyCollectionChanged source, EventHandler<NotifyCollectionChangedEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);

        var manager = GetManager();
        manager.ProtectedRemoveHandler(source, handler);
    }

    /// <summary>
    /// Adds the specified listener to the CollectionChanged event of the specified source.
    /// </summary>
    public static void AddListener(INotifyCollectionChanged source, IWeakEventListener listener)
    {
        var manager = GetManager();
        manager.ProtectedAddListener(source, listener);
    }

    /// <summary>
    /// Removes the specified listener from the CollectionChanged event of the specified source.
    /// </summary>
    public static void RemoveListener(INotifyCollectionChanged source, IWeakEventListener listener)
    {
        var manager = GetManager();
        manager.ProtectedRemoveListener(source, listener);
    }

    /// <inheritdoc />
    protected override void StartListening(object source)
    {
        if (source is INotifyCollectionChanged incc)
        {
            incc.CollectionChanged += OnCollectionChanged;
        }
    }

    /// <inheritdoc />
    protected override void StopListening(object source)
    {
        if (source is INotifyCollectionChanged incc)
        {
            incc.CollectionChanged -= OnCollectionChanged;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (sender != null)
        {
            DeliverEvent(sender, e);
        }
    }

    private static CollectionChangedEventManager GetManager()
    {
        var manager = GetCurrentManager(typeof(CollectionChangedEventManager)) as CollectionChangedEventManager;
        if (manager == null)
        {
            manager = new CollectionChangedEventManager();
            SetCurrentManager(typeof(CollectionChangedEventManager), manager);
        }
        return manager;
    }
}
