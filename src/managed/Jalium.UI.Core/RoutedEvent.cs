namespace Jalium.UI;

/// <summary>
/// Specifies the routing strategy for a routed event.
/// </summary>
public enum RoutingStrategy
{
    /// <summary>
    /// The event is raised only on the source element.
    /// </summary>
    Direct = 2,

    /// <summary>
    /// The event is raised on the source element and then bubbles up the visual tree.
    /// </summary>
    Bubble = 1,

    /// <summary>
    /// The event tunnels down the visual tree from the root to the source element.
    /// </summary>
    Tunnel = 0,
}

/// <summary>
/// Represents a routed event that can be raised and handled along the visual tree.
/// </summary>
public sealed class RoutedEvent
{
    private static int _globalIndex;

    /// <summary>
    /// Gets the name of the routed event.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the routing strategy for this event.
    /// </summary>
    public RoutingStrategy RoutingStrategy { get; }

    /// <summary>
    /// Gets the type of the event handler delegate.
    /// </summary>
    public Type HandlerType { get; }

    /// <summary>
    /// Gets the type that owns this routed event.
    /// </summary>
    public Type OwnerType { get; }

    /// <summary>
    /// Gets the global index for this event (used for fast lookup).
    /// </summary>
    public int GlobalIndex { get; }

    internal RoutedEvent(string name, RoutingStrategy routingStrategy, Type handlerType, Type ownerType)
    {
        Name = name;
        RoutingStrategy = routingStrategy;
        HandlerType = handlerType;
        OwnerType = ownerType;
        GlobalIndex = Interlocked.Increment(ref _globalIndex);
    }

    /// <summary>
    /// Adds another owner type for this routed event.
    /// </summary>
    /// <param name="ownerType">The type to add as an owner.</param>
    /// <returns>This routed event instance.</returns>
    public RoutedEvent AddOwner(Type ownerType)
    {
        ArgumentNullException.ThrowIfNull(ownerType);
        EventManager.AddOwner(this, ownerType);
        return this;
    }

    /// <inheritdoc />
    public override string ToString() => $"{OwnerType.Name}.{Name}";

    /// <inheritdoc />
    public override int GetHashCode() => GlobalIndex;
}
