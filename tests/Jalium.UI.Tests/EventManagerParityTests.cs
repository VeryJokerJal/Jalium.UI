namespace Jalium.UI.Tests;

public sealed class EventManagerParityTests
{
    [Fact]
    public void ClassHandlerResolutionIsCachedAndInvalidatedByRegistration()
    {
        var routedEvent = EventManager.RegisterRoutedEvent(
            $"CachedClassHandler{Guid.NewGuid():N}",
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(ClassHandlerBase));
        RoutedEventHandler firstHandler = static (_, _) => { };
        RoutedEventHandler secondHandler = static (_, _) => { };
        EventManager.RegisterClassHandler(typeof(ClassHandlerBase), routedEvent, firstHandler);

        var first = EventManager.GetClassHandlers(routedEvent, typeof(ClassHandlerDerived));
        var repeated = EventManager.GetClassHandlers(routedEvent, typeof(ClassHandlerDerived));

        Assert.Same(first, repeated);
        Assert.Single(first);
        Assert.Same(firstHandler, first[0].Handler);

        EventManager.RegisterClassHandler(typeof(ClassHandlerDerived), routedEvent, secondHandler);
        var refreshed = EventManager.GetClassHandlers(routedEvent, typeof(ClassHandlerDerived));

        Assert.NotSame(first, refreshed);
        Assert.Equal(2, refreshed.Length);
        Assert.Same(firstHandler, refreshed[0].Handler);
        Assert.Same(secondHandler, refreshed[1].Handler);
    }

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

    private class ClassHandlerBase;

    private sealed class ClassHandlerDerived : ClassHandlerBase;
}
