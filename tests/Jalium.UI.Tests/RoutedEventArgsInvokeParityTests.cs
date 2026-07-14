using System.Reflection;

namespace Jalium.UI.Tests;

public sealed class RoutedEventArgsInvokeParityTests
{
    private static readonly RoutedEvent ProbeEvent = EventManager.RegisterRoutedEvent(
        "RoutedEventArgsInvokeProbe",
        RoutingStrategy.Bubble,
        typeof(RoutedEventHandler),
        typeof(RoutedEventArgsInvokeParityTests));

    [Fact]
    public void InvokeEventHandlerIsProtectedVirtualAndUsedByRouting()
    {
        var method = typeof(RoutedEventArgs).GetMethod(
            "InvokeEventHandler",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            [typeof(Delegate), typeof(object)],
            null);

        Assert.NotNull(method);
        Assert.True(method!.IsFamily);
        Assert.True(method.IsVirtual);
        Assert.False(method.IsFinal);

        var source = new DependencyObjectHost();
        var handled = false;
        source.AddHandler(ProbeEvent, new RoutedEventHandler((_, _) => handled = true));
        var args = new ProbeRoutedEventArgs(ProbeEvent, source);

        source.RaiseEvent(args);

        Assert.True(args.OverrideInvoked);
        Assert.True(handled);
    }

    private sealed class DependencyObjectHost : UIElement;

    private sealed class ProbeRoutedEventArgs : RoutedEventArgs
    {
        public ProbeRoutedEventArgs(RoutedEvent routedEvent, object source)
            : base(routedEvent, source)
        {
        }

        public bool OverrideInvoked { get; private set; }

        protected override void InvokeEventHandler(Delegate handler, object target)
        {
            OverrideInvoked = true;
            base.InvokeEventHandler(handler, target);
        }
    }
}
