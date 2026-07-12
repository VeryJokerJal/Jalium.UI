namespace Jalium.UI;

/// <summary>
/// Provides the container for the route to be followed by a routed event.
/// </summary>
public sealed class EventRoute
{
    private readonly List<RouteItem> _routeItems = new();
    private Stack<BranchNode>? _branchNodes;

    public EventRoute(RoutedEvent routedEvent)
    {
        RoutedEvent = routedEvent ?? throw new ArgumentNullException(nameof(routedEvent));
    }

    public RoutedEvent RoutedEvent { get; }

    public void Add(object target, Delegate handler, bool handledEventsToo)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(handler);
        _routeItems.Add(new RouteItem(target, handler, handledEventsToo));
    }

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Advanced)]
    public void PushBranchNode(object node, object source)
    {
        _branchNodes ??= new Stack<BranchNode>(1);
        _branchNodes.Push(new BranchNode(node, source));
    }

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Advanced)]
    public object? PopBranchNode() =>
        _branchNodes is { Count: > 0 } ? _branchNodes.Pop().Node : null;

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Advanced)]
    public object? PeekBranchNode() =>
        _branchNodes is { Count: > 0 } ? _branchNodes.Peek().Node : null;

    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Advanced)]
    public object? PeekBranchSource() =>
        _branchNodes is { Count: > 0 } ? _branchNodes.Peek().Source : null;

    internal IReadOnlyList<RouteItem> Items => _routeItems;

    private readonly record struct BranchNode(object Node, object Source);
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
