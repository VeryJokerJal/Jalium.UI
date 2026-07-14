namespace Jalium.UI.Tests;

public sealed class RoutedPropertyChangedEventArgsParityTests
{
    [Fact]
    public void TwoArgumentConstructorInitializesValuesWithoutRoutedEvent()
    {
        var args = new RoutedPropertyChangedEventArgs<int>(1, 2);

        Assert.Equal(1, args.OldValue);
        Assert.Equal(2, args.NewValue);
        Assert.Null(args.RoutedEvent);
    }

    [Fact]
    public void RoutedDispatchUsesStronglyTypedHandler()
    {
        var routedEvent = EventManager.RegisterRoutedEvent(
            "WpfParityRoutedPropertyChanged",
            RoutingStrategy.Direct,
            typeof(RoutedPropertyChangedEventHandler<int>),
            typeof(RoutedPropertyChangedEventArgsParityTests));
        var element = new FrameworkElement();
        int observed = 0;
        element.AddHandler(
            routedEvent,
            new RoutedPropertyChangedEventHandler<int>((_, e) => observed = e.NewValue));

        element.RaiseEvent(new RoutedPropertyChangedEventArgs<int>(1, 7, routedEvent));

        Assert.Equal(7, observed);
    }
}
