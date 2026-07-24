namespace Jalium.UI;

/// <summary>
/// Provides static methods for registering and managing routed events.
/// </summary>
public static class EventManager
{
    private static readonly Dictionary<(Type, string), RoutedEvent> _registeredEvents = new();
    private static readonly Dictionary<RoutedEvent, List<ClassHandlerInfo>> _classHandlers = new();
    private static readonly Dictionary<(RoutedEvent Event, Type TargetType), ClassHandlerInfo[]> _resolvedClassHandlers = new();
    private static readonly object _lock = new();

    /// <summary>
    /// Registers a routed event.
    /// </summary>
    /// <param name="name">The name of the event.</param>
    /// <param name="routingStrategy">The routing strategy.</param>
    /// <param name="handlerType">The type of the event handler delegate.</param>
    /// <param name="ownerType">The owner type.</param>
    /// <returns>The registered routed event.</returns>
    public static RoutedEvent RegisterRoutedEvent(string name, RoutingStrategy routingStrategy, Type handlerType, Type ownerType)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(handlerType);
        ArgumentNullException.ThrowIfNull(ownerType);

        lock (_lock)
        {
            var key = (ownerType, name);

            // Return existing event if already registered (handles concurrent registration)
            if (_registeredEvents.TryGetValue(key, out var existingEvent))
            {
                return existingEvent;
            }

            var routedEvent = new RoutedEvent(name, routingStrategy, handlerType, ownerType);
            _registeredEvents[key] = routedEvent;
            return routedEvent;
        }
    }

    internal static void AddOwner(RoutedEvent routedEvent, Type ownerType)
    {
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(ownerType);

        lock (_lock)
        {
            var key = (ownerType, routedEvent.Name);
            if (_registeredEvents.TryGetValue(key, out var existing))
            {
                if (!ReferenceEquals(existing, routedEvent))
                {
                    throw new ArgumentException(
                        $"A routed event named '{routedEvent.Name}' is already registered for '{ownerType}'.",
                        nameof(ownerType));
                }

                return;
            }

            _registeredEvents[key] = routedEvent;
        }
    }

    /// <summary>
    /// Registers a class handler for a routed event.
    /// </summary>
    /// <param name="classType">The class type to register the handler for.</param>
    /// <param name="routedEvent">The routed event.</param>
    /// <param name="handler">The event handler.</param>
    public static void RegisterClassHandler(Type classType, RoutedEvent routedEvent, Delegate handler)
    {
        RegisterClassHandler(classType, routedEvent, handler, handledEventsToo: false);
    }

    /// <summary>
    /// Registers a class handler for a routed event.
    /// </summary>
    /// <param name="classType">The class type to register the handler for.</param>
    /// <param name="routedEvent">The routed event.</param>
    /// <param name="handler">The event handler.</param>
    /// <param name="handledEventsToo">Whether to invoke the handler even if the event is already handled.</param>
    public static void RegisterClassHandler(Type classType, RoutedEvent routedEvent, Delegate handler, bool handledEventsToo)
    {
        ArgumentNullException.ThrowIfNull(classType);
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(handler);

        lock (_lock)
        {
            if (!_classHandlers.TryGetValue(routedEvent, out var handlers))
            {
                handlers = new List<ClassHandlerInfo>();
                _classHandlers[routedEvent] = handlers;
            }

            handlers.Add(new ClassHandlerInfo(classType, handler, handledEventsToo));
            // Class handlers may be registered after a target type was first
            // queried. Registration is rare, so invalidating the small cache is
            // cheaper and simpler than finding every assignable target key.
            _resolvedClassHandlers.Clear();
        }
    }

    /// <summary>
    /// Gets the class handlers for a routed event.
    /// </summary>
    internal static ClassHandlerInfo[] GetClassHandlers(RoutedEvent routedEvent, Type targetType)
    {
        lock (_lock)
        {
            var key = (routedEvent, targetType);
            if (_resolvedClassHandlers.TryGetValue(key, out var cached))
            {
                return cached;
            }

            if (!_classHandlers.TryGetValue(routedEvent, out var handlers))
            {
                return Array.Empty<ClassHandlerInfo>();
            }

            int matchingCount = 0;
            foreach (var handler in handlers)
            {
                if (handler.ClassType.IsAssignableFrom(targetType))
                {
                    matchingCount++;
                }
            }

            if (matchingCount == 0)
            {
                return Array.Empty<ClassHandlerInfo>();
            }

            var resolved = new ClassHandlerInfo[matchingCount];
            int resolvedIndex = 0;
            foreach (var handler in handlers)
            {
                if (handler.ClassType.IsAssignableFrom(targetType))
                {
                    resolved[resolvedIndex++] = handler;
                }
            }

            _resolvedClassHandlers[key] = resolved;
            return resolved;
        }
    }

    /// <summary>
    /// Gets all routed events registered for a type.
    /// </summary>
    /// <param name="ownerType">The owner type.</param>
    /// <returns>An array of routed events.</returns>
    public static RoutedEvent[] GetRoutedEventsForOwner(Type ownerType)
    {
        ArgumentNullException.ThrowIfNull(ownerType);

        lock (_lock)
        {
            return _registeredEvents
                .Where(kvp => kvp.Key.Item1 == ownerType)
                .Select(kvp => kvp.Value)
                .ToArray();
        }
    }

    /// <summary>Gets a snapshot of every routed event currently registered.</summary>
    public static RoutedEvent[] GetRoutedEvents()
    {
        lock (_lock)
        {
            return _registeredEvents.Values
                .Distinct()
                .OrderBy(static routedEvent => routedEvent.GlobalIndex)
                .ToArray();
        }
    }
}

/// <summary>
/// Information about a class-level event handler.
/// </summary>
internal sealed class ClassHandlerInfo
{
    public Type ClassType { get; }
    public Delegate Handler { get; }
    public bool HandledEventsToo { get; }

    public ClassHandlerInfo(Type classType, Delegate handler, bool handledEventsToo)
    {
        ClassType = classType;
        Handler = handler;
        HandledEventsToo = handledEventsToo;
    }
}
