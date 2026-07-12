using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

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
public abstract class WeakEventManager : Jalium.UI.Threading.DispatcherObject
{
    private static readonly ConcurrentDictionary<Type, WeakEventManager> s_managers = new();
    private static readonly object s_staticSource = new();

    private readonly ConditionalWeakTable<object, object> _sourceToData = new();
    private readonly List<WeakReference<object>> _knownSources = new();
    private readonly ReaderWriterLockSlim _tableLock = new(LockRecursionPolicy.SupportsRecursion);
    private int _cleanupRequests;

    /// <summary>
    /// Initializes a new weak event manager associated with the current dispatcher.
    /// </summary>
    protected WeakEventManager()
    {
    }

    /// <summary>
    /// Takes a read lock on the manager's source table.
    /// </summary>
    protected IDisposable ReadLock => LockToken.EnterRead(_tableLock);

    /// <summary>
    /// Takes a write lock on the manager's source table.
    /// </summary>
    protected IDisposable WriteLock => LockToken.EnterWrite(_tableLock);

    /// <summary>
    /// Gets or sets the manager-specific data associated with a source.
    /// </summary>
    protected object? this[object source]
    {
        get
        {
            object sourceKey = NormalizeSource(source);
            if (_tableLock.IsReadLockHeld || _tableLock.IsWriteLockHeld)
            {
                return GetDataNoLock(sourceKey);
            }

            using (ReadLock)
            {
                return GetDataNoLock(sourceKey);
            }
        }
        set
        {
            object sourceKey = NormalizeSource(source);
            if (_tableLock.IsWriteLockHeld)
            {
                SetDataNoLock(sourceKey, value);
                return;
            }

            using (WriteLock)
            {
                SetDataNoLock(sourceKey, value);
            }
        }
    }

    /// <summary>
    /// Returns a new list to hold listeners for a source.
    /// </summary>
    protected virtual ListenerList NewListenerList() => new();

    /// <summary>
    /// Gets the current manager for the specified manager type.
    /// </summary>
    protected static WeakEventManager? GetCurrentManager(Type managerType)
    {
        ArgumentNullException.ThrowIfNull(managerType);
        s_managers.TryGetValue(managerType, out var manager);
        return manager;
    }

    /// <summary>
    /// Sets the current manager for the specified manager type.
    /// </summary>
    protected static void SetCurrentManager(Type managerType, WeakEventManager manager)
    {
        ArgumentNullException.ThrowIfNull(managerType);
        ArgumentNullException.ThrowIfNull(manager);
        s_managers[managerType] = manager;
    }

    /// <summary>
    /// Discards the data associated with a source.
    /// </summary>
    protected void Remove(object source)
    {
        object sourceKey = NormalizeSource(source);
        if (_tableLock.IsWriteLockHeld)
        {
            RemoveDataNoLock(sourceKey);
            return;
        }

        using (WriteLock)
        {
            RemoveDataNoLock(sourceKey);
        }
    }

    /// <summary>
    /// Adds the specified listener to the list of listeners on the specified source.
    /// </summary>
    protected void ProtectedAddListener(object source, IWeakEventListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        AddListener(source, listener, null);
    }

    /// <summary>
    /// Removes the specified listener from the list of listeners on the specified source.
    /// </summary>
    protected void ProtectedRemoveListener(object source, IWeakEventListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        RemoveListener(source, listener, null);
    }

    /// <summary>
    /// Adds the specified event handler to the list on the specified source.
    /// </summary>
    protected void ProtectedAddHandler(object source, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        AddListener(source, null, handler);
    }

    /// <summary>
    /// Removes the specified event handler from the list on the specified source.
    /// </summary>
    protected void ProtectedRemoveHandler(object source, Delegate handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        RemoveListener(source, null, handler);
    }

    /// <summary>
    /// Delivers the event being managed to each listener registered for the sender.
    /// </summary>
    protected void DeliverEvent(object sender, EventArgs args)
    {
        ListenerList list;
        object sourceKey = NormalizeSource(sender);

        using (ReadLock)
        {
            list = (ListenerList?)GetDataNoLock(sourceKey) ?? ListenerList.Empty;
            list.BeginUse();
        }

        try
        {
            DeliverEventToList(sender, args, list);
        }
        finally
        {
            list.EndUse();
        }
    }

    /// <summary>
    /// Delivers an event to the listeners in an explicitly supplied list.
    /// </summary>
    protected void DeliverEventToList(object sender, EventArgs args, ListenerList list)
    {
        ArgumentNullException.ThrowIfNull(list);

        if (list.DeliverEvent(sender, args, GetType()))
        {
            ScheduleCleanup();
        }
    }

    /// <summary>
    /// Schedules a coalesced cleanup pass at dispatcher idle priority.
    /// </summary>
    protected void ScheduleCleanup()
    {
        if (Interlocked.Increment(ref _cleanupRequests) != 1)
        {
            return;
        }

        try
        {
            Dispatcher.BeginInvoke(Threading.DispatcherPriority.ContextIdle, CleanupOperation);
        }
        catch
        {
            Interlocked.Exchange(ref _cleanupRequests, 0);
            throw;
        }
    }

    /// <summary>
    /// Removes dead entries from the data for a source.
    /// </summary>
    /// <returns>true when stale data was removed; otherwise, false.</returns>
    protected virtual bool Purge(object source, object data, bool purgeAll)
    {
        bool foundDirt = false;
        bool removeList = purgeAll || source is null;

        if (!removeList)
        {
            var list = (ListenerList)data;
            if (ListenerList.PrepareForWriting(ref list))
            {
                this[source!] = list;
            }

            foundDirt = list.Purge();
            removeList = list.IsEmpty;
        }

        if (removeList && source is not null)
        {
            StopListening(source);
            if (!purgeAll)
            {
                Remove(source);
                foundDirt = true;
            }
        }

        return foundDirt;
    }

    /// <summary>
    /// When overridden in a derived class, starts listening for the event being managed.
    /// </summary>
    protected abstract void StartListening(object source);

    /// <summary>
    /// When overridden in a derived class, stops listening on the provided source.
    /// </summary>
    protected abstract void StopListening(object source);

    private void AddListener(object? source, IWeakEventListener? listener, Delegate? handler)
    {
        object sourceKey = NormalizeSource(source);

        using (WriteLock)
        {
            ListenerList? existingList = (ListenerList?)GetDataNoLock(sourceKey);
            ListenerList list;
            if (existingList is null)
            {
                list = NewListenerList()
                    ?? throw new InvalidOperationException("NewListenerList returned null.");
                SetDataNoLock(sourceKey, list);

                try
                {
                    StartListening(source!);
                }
                catch
                {
                    RemoveDataNoLock(sourceKey);
                    throw;
                }
            }
            else
            {
                list = existingList;
            }

            if (ListenerList.PrepareForWriting(ref list))
            {
                SetDataNoLock(sourceKey, list);
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

    private void RemoveListener(object? source, object? listener, Delegate? handler)
    {
        object sourceKey = NormalizeSource(source);

        using (WriteLock)
        {
            ListenerList? existingList = (ListenerList?)GetDataNoLock(sourceKey);
            if (existingList is null)
            {
                return;
            }

            ListenerList list = existingList;

            if (ListenerList.PrepareForWriting(ref list))
            {
                SetDataNoLock(sourceKey, list);
            }

            if (handler is not null)
            {
                list.RemoveHandler(handler);
            }
            else
            {
                list.Remove((IWeakEventListener)listener!);
            }

            if (list.IsEmpty)
            {
                RemoveDataNoLock(sourceKey);
                StopListening(source!);
            }
        }
    }

    private void CleanupOperation()
    {
        Interlocked.Exchange(ref _cleanupRequests, 0);

        using (WriteLock)
        {
            WeakReference<object>[] sources = _knownSources.ToArray();
            foreach (WeakReference<object> weakSource in sources)
            {
                if (!weakSource.TryGetTarget(out object? sourceKey))
                {
                    RemoveKnownSourceNoLock(weakSource);
                    continue;
                }

                object? data = GetDataNoLock(sourceKey);
                if (data is null)
                {
                    RemoveKnownSourceNoLock(weakSource);
                    continue;
                }

                object? source = ReferenceEquals(sourceKey, s_staticSource) ? null : sourceKey;
                Purge(source!, data, purgeAll: false);
            }
        }
    }

    private object? GetDataNoLock(object sourceKey) =>
        _sourceToData.TryGetValue(sourceKey, out object? data) ? data : null;

    private void SetDataNoLock(object sourceKey, object? data)
    {
        if (data is null)
        {
            RemoveDataNoLock(sourceKey);
            return;
        }

        bool isNewSource = !_sourceToData.TryGetValue(sourceKey, out _);
        _sourceToData.AddOrUpdate(sourceKey, data);
        if (isNewSource)
        {
            _knownSources.Add(new WeakReference<object>(sourceKey));
        }
    }

    private void RemoveDataNoLock(object sourceKey)
    {
        _sourceToData.Remove(sourceKey);

        for (int i = _knownSources.Count - 1; i >= 0; i--)
        {
            if (!_knownSources[i].TryGetTarget(out object? candidate) || ReferenceEquals(candidate, sourceKey))
            {
                _knownSources.RemoveAt(i);
            }
        }
    }

    private void RemoveKnownSourceNoLock(WeakReference<object> source)
    {
        for (int i = _knownSources.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_knownSources[i], source))
            {
                _knownSources.RemoveAt(i);
                return;
            }
        }
    }

    private static object NormalizeSource(object? source) => source ?? s_staticSource;

    private sealed class LockToken : IDisposable
    {
        private ReaderWriterLockSlim? _lock;
        private readonly bool _write;

        private LockToken(ReaderWriterLockSlim @lock, bool write)
        {
            _lock = @lock;
            _write = write;
        }

        public static LockToken EnterRead(ReaderWriterLockSlim @lock)
        {
            @lock.EnterReadLock();
            return new LockToken(@lock, write: false);
        }

        public static LockToken EnterWrite(ReaderWriterLockSlim @lock)
        {
            @lock.EnterWriteLock();
            return new LockToken(@lock, write: true);
        }

        public void Dispose()
        {
            ReaderWriterLockSlim? @lock = Interlocked.Exchange(ref _lock, null);
            if (@lock is null)
            {
                return;
            }

            if (_write)
            {
                @lock.ExitWriteLock();
            }
            else
            {
                @lock.ExitReadLock();
            }
        }
    }

    internal readonly struct Listener
    {
        private readonly WeakReference<object>? _target;
        private readonly WeakReference<Delegate>? _handler;

        public Listener(object? target)
        {
            _target = new WeakReference<object>(target ?? s_staticSource);
            _handler = null;
        }

        public Listener(object target, Delegate handler)
        {
            _target = new WeakReference<object>(target);
            _handler = new WeakReference<Delegate>(handler);
        }

        public object? Target =>
            _target is not null && _target.TryGetTarget(out object? target) ? target : null;

        public Delegate? Handler =>
            _handler is not null && _handler.TryGetTarget(out Delegate? handler) ? handler : null;

        public bool HasHandler => _handler is not null;

        public bool Matches(object target, Delegate handler) =>
            ReferenceEquals(target, Target) && Equals(handler, Handler);
    }

    /// <summary>
    /// Stores weak event listeners and handlers for a source.
    /// </summary>
    protected class ListenerList
    {
        private static readonly ListenerList s_empty = new();

        private readonly List<Listener> _listeners;
        private readonly ConditionalWeakTable<object, object> _handlerTable = new();
        private int _users;

        /// <summary>
        /// Initializes an empty listener list.
        /// </summary>
        public ListenerList()
        {
            _listeners = new List<Listener>();
        }

        /// <summary>
        /// Initializes an empty listener list with the specified capacity.
        /// </summary>
        public ListenerList(int capacity)
        {
            _listeners = new List<Listener>(capacity);
        }

        /// <summary>
        /// Gets the listener at the specified index.
        /// </summary>
        public IWeakEventListener this[int index] => (IWeakEventListener)_listeners[index].Target!;

        /// <summary>
        /// Gets the number of entries, including entries awaiting cleanup.
        /// </summary>
        public int Count => _listeners.Count;

        /// <summary>
        /// Gets a value indicating whether the list contains no entries.
        /// </summary>
        public bool IsEmpty => _listeners.Count == 0;

        /// <summary>
        /// Gets a shared empty listener list.
        /// </summary>
        public static ListenerList Empty => s_empty;

        /// <summary>
        /// Adds a weak event listener.
        /// </summary>
        public void Add(IWeakEventListener listener)
        {
            ArgumentNullException.ThrowIfNull(listener);
            EnsureNotInUse();
            _listeners.Add(new Listener(listener));
        }

        /// <summary>
        /// Removes one matching weak event listener.
        /// </summary>
        public void Remove(IWeakEventListener listener)
        {
            ArgumentNullException.ThrowIfNull(listener);
            EnsureNotInUse();

            for (int i = _listeners.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(_listeners[i].Target, listener))
                {
                    _listeners.RemoveAt(i);
                    break;
                }
            }
        }

        /// <summary>
        /// Adds an event handler without strongly retaining its target.
        /// </summary>
        public void AddHandler(Delegate handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            EnsureNotInUse();

            object target = handler.Target ?? s_staticSource;
            _listeners.Add(new Listener(target, handler));
            AddHandlerToTable(target, handler);
        }

        /// <summary>
        /// Removes one matching event handler.
        /// </summary>
        public void RemoveHandler(Delegate handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            EnsureNotInUse();

            object target = handler.Target ?? s_staticSource;
            for (int i = _listeners.Count - 1; i >= 0; i--)
            {
                if (_listeners[i].Matches(target, handler))
                {
                    _listeners.RemoveAt(i);
                    break;
                }
            }

            if (_handlerTable.TryGetValue(target, out object? value))
            {
                if (value is List<Delegate> handlers)
                {
                    handlers.Remove(handler);
                    if (handlers.Count == 0)
                    {
                        _handlerTable.Remove(target);
                    }
                }
                else
                {
                    _handlerTable.Remove(target);
                }
            }
        }

        /// <summary>
        /// Marks the list as in use.
        /// </summary>
        /// <returns>true if the list was already in use; otherwise, false.</returns>
        public bool BeginUse() => Interlocked.Increment(ref _users) != 1;

        /// <summary>
        /// Ends a use scope started by <see cref="BeginUse"/>.
        /// </summary>
        public void EndUse() => Interlocked.Decrement(ref _users);

        /// <summary>
        /// Clones a list that is currently in use before it is modified.
        /// </summary>
        public static bool PrepareForWriting(ref ListenerList list)
        {
            ArgumentNullException.ThrowIfNull(list);

            bool inUse = list.BeginUse();
            list.EndUse();
            if (inUse)
            {
                list = list.Clone();
            }

            return inUse;
        }

        /// <summary>
        /// Delivers an event to all live entries in registration order.
        /// </summary>
        /// <returns>true if stale entries were encountered; otherwise, false.</returns>
        public virtual bool DeliverEvent(object sender, EventArgs args, Type managerType)
        {
            bool foundStaleEntries = false;
            for (int i = 0, count = Count; i < count; i++)
            {
                Listener listener = GetListener(i);
                foundStaleEntries |= DeliverEvent(ref listener, sender, args, managerType);
            }

            return foundStaleEntries;
        }

        /// <summary>
        /// Removes entries whose targets have been collected.
        /// </summary>
        /// <returns>true if at least one entry was removed; otherwise, false.</returns>
        public bool Purge()
        {
            EnsureNotInUse();
            bool foundDirt = false;

            for (int i = _listeners.Count - 1; i >= 0; i--)
            {
                if (_listeners[i].Target is null)
                {
                    _listeners.RemoveAt(i);
                    foundDirt = true;
                }
            }

            return foundDirt;
        }

        /// <summary>
        /// Returns a modifiable copy of this listener list.
        /// </summary>
        public virtual ListenerList Clone()
        {
            var result = new ListenerList(Count);
            CopyTo(result);
            return result;
        }

        /// <summary>
        /// Copies all live entries to another listener list.
        /// </summary>
        protected void CopyTo(ListenerList newList)
        {
            ArgumentNullException.ThrowIfNull(newList);

            for (int i = 0, count = Count; i < count; i++)
            {
                Listener listener = GetListener(i);
                object? target = listener.Target;
                if (target is null)
                {
                    continue;
                }

                if (listener.HasHandler)
                {
                    if (listener.Handler is Delegate handler)
                    {
                        newList.AddHandler(handler);
                    }
                }
                else if (target is IWeakEventListener weakListener)
                {
                    newList.Add(weakListener);
                }
            }
        }

        internal Listener GetListener(int index) => _listeners[index];

        internal bool DeliverEvent(ref Listener listener, object sender, EventArgs args, Type managerType)
        {
            object? target = listener.Target;
            if (target is null)
            {
                return true;
            }

            if (listener.HasHandler)
            {
                Delegate? handler = listener.Handler;
                if (handler is EventHandler eventHandler)
                {
                    eventHandler(sender, args);
                }
                else
                {
                    handler?.DynamicInvoke(sender, args);
                }
            }
            else if (target is IWeakEventListener weakListener)
            {
                weakListener.ReceiveWeakEvent(managerType, sender, args);
            }

            return false;
        }

        private void AddHandlerToTable(object target, Delegate handler)
        {
            if (!_handlerTable.TryGetValue(target, out object? value))
            {
                _handlerTable.Add(target, handler);
                return;
            }

            if (value is not List<Delegate> handlers)
            {
                handlers = new List<Delegate> { (Delegate)value! };
                _handlerTable.Remove(target);
                _handlerTable.Add(target, handlers);
            }

            handlers.Add(handler);
        }

        private void EnsureNotInUse()
        {
            if (Volatile.Read(ref _users) != 0)
            {
                throw new InvalidOperationException("Cannot modify a ListenerList that is in use.");
            }
        }
    }

    /// <summary>
    /// Provides strongly typed delivery for event handlers with a specific event-argument type.
    /// </summary>
    protected class ListenerList<TEventArgs> : ListenerList
        where TEventArgs : EventArgs
    {
        public ListenerList()
        {
        }

        public ListenerList(int capacity)
            : base(capacity)
        {
        }

        public override bool DeliverEvent(object sender, EventArgs args, Type managerType)
        {
            var typedArgs = (TEventArgs)args;
            bool foundStaleEntries = false;

            for (int i = 0, count = Count; i < count; i++)
            {
                Listener listener = GetListener(i);
                if (listener.Target is null)
                {
                    foundStaleEntries = true;
                    continue;
                }

                if (listener.Handler is EventHandler<TEventArgs> handler)
                {
                    handler(sender, typedArgs);
                }
                else
                {
                    foundStaleEntries |= base.DeliverEvent(ref listener, sender, args, managerType);
                }
            }

            return foundStaleEntries;
        }

        public override ListenerList Clone()
        {
            var result = new ListenerList<TEventArgs>(Count);
            CopyTo(result);
            return result;
        }
    }
}

/// <summary>
/// Provides a type-safe WeakEventManager for a specific event source type and event args type.
/// </summary>
/// <typeparam name="TEventSource">The type that raises the event.</typeparam>
/// <typeparam name="TEventArgs">The type of event data.</typeparam>
public class WeakEventManager<
    [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicEvents)] TEventSource,
    TEventArgs> : WeakEventManager
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
        ArgumentNullException.ThrowIfNull(eventName);
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
        ArgumentNullException.ThrowIfNull(eventName);
        ArgumentNullException.ThrowIfNull(handler);

        if (s_perEventManagers.TryGetValue(eventName, out var manager))
        {
            manager.ProtectedRemoveHandler(source, handler);
        }
    }

    /// <inheritdoc />
    protected override ListenerList NewListenerList() => new ListenerList<TEventArgs>();

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
        if (source is TEventSource typedSource && _sourceToHandler.TryGetValue(source, out var handler))
        {
            var eventInfo = typeof(TEventSource).GetEvent(_eventName,
                BindingFlags.Public | BindingFlags.Instance);
            eventInfo?.RemoveEventHandler(typedSource, handler);
            _sourceToHandler.Remove(source);
        }
    }

    private static WeakEventManager<TEventSource, TEventArgs> GetOrCreateManager(string eventName)
    {
        return s_perEventManagers.GetOrAdd(eventName, name => new WeakEventManager<TEventSource, TEventArgs>(name));
    }
}
