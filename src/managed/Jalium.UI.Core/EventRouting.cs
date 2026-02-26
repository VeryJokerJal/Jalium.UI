namespace Jalium.UI;

/// <summary>
/// Provides the container for the route to be followed by a routed event.
/// </summary>
public sealed class EventRoute
{
    private readonly List<RouteItem> _routeItems = new();

    public EventRoute(RoutedEvent routedEvent)
    {
        RoutedEvent = routedEvent ?? throw new ArgumentNullException(nameof(routedEvent));
    }

    public RoutedEvent RoutedEvent { get; }

    public void Add(object target, Delegate handler, bool handledEventsToo)
    {
        _routeItems.Add(new RouteItem(target, handler, handledEventsToo));
    }

    internal IReadOnlyList<RouteItem> Items => _routeItems;
}

/// <summary>
/// Represents an item in an event route.
/// </summary>
public readonly struct RouteItem
{
    public RouteItem(object target, Delegate handler, bool handledEventsToo)
    {
        Target = target;
        Handler = handler;
        HandledEventsToo = handledEventsToo;
    }

    public object Target { get; }
    public Delegate Handler { get; }
    public bool HandledEventsToo { get; }
}

/// <summary>
/// Delegate for validation of dependency property values.
/// </summary>
public delegate bool ValidateValueCallback(object? value);
