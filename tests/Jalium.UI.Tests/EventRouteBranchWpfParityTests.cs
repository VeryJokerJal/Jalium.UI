using Xunit;

namespace Jalium.UI.Tests;

public sealed class EventRouteBranchWpfParityTests
{
    private static readonly RoutedEvent TestEvent = EventManager.RegisterRoutedEvent(
        "EventRouteBranchWpfParity",
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(EventRouteBranchWpfParityTests));

    [Fact]
    public void BranchNodesUseLifoOrderingAndKeepTheirSourcePaired()
    {
        var route = new EventRoute(TestEvent);
        var firstNode = new object();
        var firstSource = new object();
        var secondNode = new object();
        var secondSource = new object();

        Assert.Null(route.PeekBranchNode());
        Assert.Null(route.PeekBranchSource());
        Assert.Null(route.PopBranchNode());

        route.PushBranchNode(firstNode, firstSource);
        route.PushBranchNode(secondNode, secondSource);

        Assert.Same(secondNode, route.PeekBranchNode());
        Assert.Same(secondSource, route.PeekBranchSource());
        Assert.Same(secondNode, route.PopBranchNode());
        Assert.Same(firstNode, route.PeekBranchNode());
        Assert.Same(firstSource, route.PeekBranchSource());
    }
}
