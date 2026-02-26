using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Jalium.UI;

/// <summary>
/// Provides an interface for classes that listen to events via the WeakEventManager pattern.
/// </summary>
public interface IWeakEventListener
{
    /// <summary>
    /// Receives events from the centralized event manager.
    /// </summary>
    /// <param name="managerType">The type of the WeakEventManager calling this method.</param>
    /// <param name="sender">Object that originated the event.</param>
    /// <param name="e">Event data.</param>
    /// <returns>true if the listener handled the event; false otherwise.</returns>
    bool ReceiveWeakEvent(Type managerType, object sender, EventArgs e);
}

/// <summary>
/// Provides a base class for the event manager that is used in the weak event pattern.
/// The manager adds and removes listeners for events (or callbacks) that also use the pattern.
/// </summary>
public abstract class WeakEventManager
{
    private static readonly ConcurrentDictionary<Type, WeakEventManager> s_managers = new();
    private readonly ConditionalWeakTable<object, ListenerList> _sourceToListeners = new();
    private readonly object _syncRoot = new();

    /// <summary>
    /// Gets the current manager for the specified manager type.
    /// </summary>
    protected static WeakEventManager? GetCurrentManager(Type managerType)
    {
        s_managers.TryGetValue(managerType, out var manager);
        return manager;
    }

    /// <summary>
    /// Sets the current manager for the specified manager type.
    /// </summary>
    protected static void SetCurrentManager(Type managerType, WeakEventManager manager)
    {
        s_managers[managerType] = manager;
    }

    /// <summary>
    /// Adds the specified listener to the list of listeners on the specified source.
    /// </summary>
    protected void ProtectedAddListener(object source, IWeakEventListener listener)
    {
        lock (_syncRoot)
        {
            var list = GetOrCreateListenerList(source);
            list.AddListener(listener);

            if (list.Count == 1)
            {
                StartListening(source);
            }
        }
    }

    /// <summary>
    /// Removes the specified listener from the list of listeners on the specified source.
    /// </summary>
    protected void ProtectedRemoveListener(object source, IWeakEventListener listener)
    {
        lock (_syncRoot)
        {
            if (!_sourceToListeners.TryGetValue(source, out var list))
                return;

            list.RemoveListener(listener);

            if (list.Count == 0)
            {
                StopListening(source);
            }
        }
    }

    /// <summary>
    /// Adds the specified event handler to the list on the specified source.
    /// </summary>
    protected void ProtectedAddHandler(object source, Delegate handler)
    {
        lock (_syncRoot)
        {
            var list = GetOrCreateListenerList(source);
            list.AddHandler(handler);

            if (list.Count == 1)
            {
                StartListening(source);
            }
        }
    }

    /// <summary>
    /// Removes the specified event handler from the list on the specified source.
    /// </summary>
    protected void ProtectedRemoveHandler(object source, Delegate handler)
    {
        lock (_syncRoot)
        {
            if (!_sourceToListeners.TryGetValue(source, out var list))
                return;

            list.RemoveHandler(handler);

            if (list.Count == 0)
            {
                StopListening(source);
            }
        }
    }

    /// <summary>
    /// Delivers the event being managed to each listener.
    /// </summary>
    protected void DeliverEvent(object sender, EventArgs args)
    {
        ListenerList? list;
        lock (_syncRoot)
        {
            if (!_sourceToListeners.TryGetValue(sender, out list))
                return;
        }

        list.DeliverEvent(sender, args, GetType());
    }

    /// <summary>
    /// When overridden in a derived class, starts listening for the event being managed.
    /// After StartListening is first called, the manager should be in the state of calling
    /// DeliverEvent whenever the relevant event from the provided source is handled.
    /// </summary>
    protected abstract void StartListening(object source);

    /// <summary>
    /// When overridden in a derived class, stops listening on the provided source for the event being managed.
    /// </summary>
    protected abstract void StopListening(object source);

    private ListenerList GetOrCreateListenerList(object source)
    {
        if (!_sourceToListeners.TryGetValue(source, out var list))
        {
            list = new ListenerList();
            _sourceToListeners.AddOrUpdate(source, list);
        }
        return list;
    }

    /// <summary>
    /// Internal list of listeners, using weak references to prevent memory leaks.
    /// </summary>
    private sealed class ListenerList
    {
        private readonly List<WeakReference<IWeakEventListener>> _listeners = new();
        private readonly List<WeakReference<Delegate>> _handlers = new();

        public int Count => _listeners.Count + _handlers.Count;

        public void AddListener(IWeakEventListener listener)
        {
            Purge();
            _listeners.Add(new WeakReference<IWeakEventListener>(listener));
        }

        public void RemoveListener(IWeakEventListener listener)
        {
            for (int i = _listeners.Count - 1; i >= 0; i--)
            {
                if (!_listeners[i].TryGetTarget(out var target) || ReferenceEquals(target, listener))
                {
                    _listeners.RemoveAt(i);
                }
            }
        }

        public void AddHandler(Delegate handler)
        {
            Purge();
            _handlers.Add(new WeakReference<Delegate>(handler));
        }

        public void RemoveHandler(Delegate handler)
        {
            for (int i = _handlers.Count - 1; i >= 0; i--)
            {
                if (!_handlers[i].TryGetTarget(out var target) || target == handler)
                {
                    _handlers.RemoveAt(i);
                }
            }
        }

        public void DeliverEvent(object sender, EventArgs args, Type managerType)
        {
            // Deliver to IWeakEventListener instances
            for (int i = _listeners.Count - 1; i >= 0; i--)
            {
                if (_listeners[i].TryGetTarget(out var listener))
                {
                    listener.ReceiveWeakEvent(managerType, sender, args);
                }
                else
                {
                    _listeners.RemoveAt(i);
                }
            }

            // Deliver to delegate handlers
            for (int i = _handlers.Count - 1; i >= 0; i--)
            {
                if (_handlers[i].TryGetTarget(out var handler))
                {
                    handler.DynamicInvoke(sender, args);
                }
                else
                {
                    _handlers.RemoveAt(i);
                }
            }
        }

        private void Purge()
        {
            _listeners.RemoveAll(wr => !wr.TryGetTarget(out _));
            _handlers.RemoveAll(wr => !wr.TryGetTarget(out _));
        }
    }
}

/// <summary>
/// Provides a type-safe WeakEventManager for a specific event source type and event args type.
/// </summary>
/// <typeparam name="TEventSource">The type that raises the event.</typeparam>
/// <typeparam name="TEventArgs">The type of event data.</typeparam>
public class WeakEventManager<TEventSource, TEventArgs> : WeakEventManager
    where TEventArgs : EventArgs
{
    private static readonly ConcurrentDictionary<string, WeakEventManager<TEventSource, TEventArgs>> s_perEventManagers = new();
    private readonly ConditionalWeakTable<object, EventHandler<TEventArgs>> _sourceToHandler = new();
    private readonly string _eventName;

    private WeakEventManager(string eventName)
    {
        _eventName = eventName;
    }

    /// <summary>
    /// Adds the specified event handler for the specified event on the specified source.
    /// </summary>
    public static void AddHandler(TEventSource source, string eventName, EventHandler<TEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);

        var manager = GetOrCreateManager(eventName);
        manager.ProtectedAddHandler(source, handler);
    }

    /// <summary>
    /// Removes the specified event handler from the specified source.
    /// </summary>
    public static void RemoveHandler(TEventSource source, string eventName, EventHandler<TEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(handler);

        if (s_perEventManagers.TryGetValue(eventName, out var manager))
        {
            manager.ProtectedRemoveHandler(source, handler);
        }
    }

    /// <inheritdoc />
    protected override void StartListening(object source)
    {
        if (source is TEventSource typedSource)
        {
            var eventInfo = typeof(TEventSource).GetEvent(_eventName,
                BindingFlags.Public | BindingFlags.Instance);
            if (eventInfo != null)
            {
                var handler = new EventHandler<TEventArgs>((s, e) => DeliverEvent(s!, e));
                _sourceToHandler.AddOrUpdate(source, handler);
                eventInfo.AddEventHandler(typedSource, handler);
            }
        }
    }

    /// <inheritdoc />
    protected override void StopListening(object source)
    {
        if (source is TEventSource typedSource)
        {
            if (_sourceToHandler.TryGetValue(source, out var handler))
            {
                var eventInfo = typeof(TEventSource).GetEvent(_eventName,
                    BindingFlags.Public | BindingFlags.Instance);
                eventInfo?.RemoveEventHandler(typedSource, handler);
                _sourceToHandler.Remove(source);
            }
        }
    }

    private static WeakEventManager<TEventSource, TEventArgs> GetOrCreateManager(string eventName)
    {
        return s_perEventManagers.GetOrAdd(eventName, name => new WeakEventManager<TEventSource, TEventArgs>(name));
    }
}
