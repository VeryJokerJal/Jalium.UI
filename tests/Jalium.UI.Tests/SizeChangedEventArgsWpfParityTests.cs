using System.Reflection;

namespace Jalium.UI.Tests;

public sealed class SizeChangedEventArgsWpfParityTests
{
    [Fact]
    public void InvokeEventHandlerIsProtectedOverrideAndInvokesTypedDelegate()
    {
        var method = typeof(SizeChangedEventArgs).GetMethod(
            "InvokeEventHandler",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.NotNull(method);
        Assert.True(method!.IsFamily);
        Assert.True(method.IsVirtual);

        var element = new FrameworkElement();
        SizeChangedEventArgs? received = null;
        element.AddHandler(
            FrameworkElement.SizeChangedEvent,
            new SizeChangedEventHandler((_, args) => received = args));
        var info = new SizeChangedInfo(element, new Size(10, 20), true, true);
        var eventArgs = new SizeChangedEventArgs(info)
        {
            RoutedEvent = FrameworkElement.SizeChangedEvent,
            Source = element,
        };

        element.RaiseEvent(eventArgs);

        Assert.Same(eventArgs, received);
    }
}
