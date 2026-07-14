namespace Jalium.UI.Tests;

public sealed class EventManagerParityTests
{
    [Fact]
    public void GetRoutedEventsReturnsDistinctSnapshotIncludingAddedOwners()
    {
        var name = $"ParityEvent{Guid.NewGuid():N}";
        var routedEvent = EventManager.RegisterRoutedEvent(
            name,
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(FirstOwner));

        Assert.Same(routedEvent, routedEvent.AddOwner(typeof(SecondOwner)));
        Assert.Same(routedEvent, routedEvent.AddOwner(typeof(SecondOwner)));

        Assert.Contains(routedEvent, EventManager.GetRoutedEventsForOwner(typeof(FirstOwner)));
        Assert.Contains(routedEvent, EventManager.GetRoutedEventsForOwner(typeof(SecondOwner)));

        var snapshot = EventManager.GetRoutedEvents();
        Assert.Equal(1, snapshot.Count(candidate => ReferenceEquals(candidate, routedEvent)));

        snapshot[Array.IndexOf(snapshot, routedEvent)] = null!;
        Assert.Contains(routedEvent, EventManager.GetRoutedEvents());
        Assert.Throws<ArgumentNullException>(() => routedEvent.AddOwner(null!));
    }

    private sealed class FirstOwner;

    private sealed class SecondOwner;
}
