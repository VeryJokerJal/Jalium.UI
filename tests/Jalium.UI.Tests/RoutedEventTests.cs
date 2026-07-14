using Jalium.UI;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// 路由事件测试
/// </summary>
public class RoutedEventTests
{
    [Fact]
    public void RegisterRoutedEvent_ShouldCreateEvent()
    {
        // Arrange & Act
        var routedEvent = EventManager.RegisterRoutedEvent(
            "TestEvent",
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(TestElement));

        // Assert
        Assert.NotNull(routedEvent);
        Assert.Equal("TestEvent", routedEvent.Name);
        Assert.Equal(RoutingStrategy.Bubble, routedEvent.RoutingStrategy);
    }

    [Fact]
    public void BubblingEvent_ShouldBubbleUp()
    {
        // Arrange
        var parent = new TestElement { Name = "Parent" };
        var child = new TestElement { Name = "Child" };
        parent.AddVisualChild(child);

        var handledByParent = false;
        var handledByChild = false;

        parent.AddHandler(TestElement.TestBubblingEvent, new RoutedEventHandler((s, e) =>
        {
            handledByParent = true;
        }));

        child.AddHandler(TestElement.TestBubblingEvent, new RoutedEventHandler((s, e) =>
        {
            handledByChild = true;
        }));

        // Act
        child.RaiseEvent(new RoutedEventArgs(TestElement.TestBubblingEvent, child));

        // Assert
        Assert.True(handledByChild, "Child should handle event first");
        Assert.True(handledByParent, "Parent should receive bubbled event");
    }

    [Fact]
    public void TunnelingEvent_ShouldTunnelDown()
    {
        // Arrange
        var parent = new TestElement { Name = "Parent" };
        var child = new TestElement { Name = "Child" };
        parent.AddVisualChild(child);

        var parentHandledFirst = false;
        var childHandledSecond = false;
        var order = 0;

        parent.AddHandler(TestElement.TestTunnelingEvent, new RoutedEventHandler((s, e) =>
        {
            parentHandledFirst = order == 0;
            order++;
        }));

        child.AddHandler(TestElement.TestTunnelingEvent, new RoutedEventHandler((s, e) =>
        {
            childHandledSecond = order == 1;
            order++;
        }));

        // Act
        child.RaiseEvent(new RoutedEventArgs(TestElement.TestTunnelingEvent, child));

        // Assert
        Assert.True(parentHandledFirst, "Parent should handle tunneling event first");
        Assert.True(childHandledSecond, "Child should handle tunneling event second");
    }

    [Fact]
    public void HandledEvent_ShouldStopBubbling()
    {
        // Arrange
        var parent = new TestElement { Name = "Parent" };
        var child = new TestElement { Name = "Child" };
        parent.AddVisualChild(child);

        var handledByParent = false;

        child.AddHandler(TestElement.TestBubblingEvent, new RoutedEventHandler((s, e) =>
        {
            e.Handled = true;
        }));

        parent.AddHandler(TestElement.TestBubblingEvent, new RoutedEventHandler((s, e) =>
        {
            handledByParent = true;
        }));

        // Act
        child.RaiseEvent(new RoutedEventArgs(TestElement.TestBubblingEvent, child));

        // Assert
        Assert.False(handledByParent, "Parent should not receive handled event");
    }

    [Fact]
    public void DirectEvent_ShouldNotBubbleOrTunnel()
    {
        // Arrange
        var parent = new TestElement { Name = "Parent" };
        var child = new TestElement { Name = "Child" };
        parent.AddVisualChild(child);

        var handledByParent = false;
        var handledByChild = false;

        parent.AddHandler(TestElement.TestDirectEvent, new RoutedEventHandler((s, e) =>
        {
            handledByParent = true;
        }));

        child.AddHandler(TestElement.TestDirectEvent, new RoutedEventHandler((s, e) =>
        {
            handledByChild = true;
        }));

        // Act
        child.RaiseEvent(new RoutedEventArgs(TestElement.TestDirectEvent, child));

        // Assert
        Assert.True(handledByChild, "Child should handle direct event");
        Assert.False(handledByParent, "Parent should not receive direct event");
    }

    private class TestElement : FrameworkElement
    {
        public static readonly RoutedEvent TestBubblingEvent =
            EventManager.RegisterRoutedEvent("TestBubbling", RoutingStrategy.Bubble,
                typeof(RoutedEventHandler), typeof(TestElement));

        public static readonly RoutedEvent TestTunnelingEvent =
            EventManager.RegisterRoutedEvent("TestTunneling", RoutingStrategy.Tunnel,
                typeof(RoutedEventHandler), typeof(TestElement));

        public static readonly RoutedEvent TestDirectEvent =
            EventManager.RegisterRoutedEvent("TestDirect", RoutingStrategy.Direct,
                typeof(RoutedEventHandler), typeof(TestElement));

        public new void AddVisualChild(Visual child)
        {
            base.AddVisualChild(child);
        }
    }
}
