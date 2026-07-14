namespace Jalium.UI.Tests;

public sealed class ParityEventHandlerTypesTests
{
    [Fact]
    public void FrameworkElementDataContextChangedUsesWpfDelegateType()
    {
        var eventInfo = typeof(FrameworkElement).GetEvent(nameof(FrameworkElement.DataContextChanged));

        Assert.NotNull(eventInfo);
        Assert.Equal(typeof(DependencyPropertyChangedEventHandler), eventInfo.EventHandlerType);
    }

    [Fact]
    public void ApplicationLifecycleEventsUseWpfDelegateTypes()
    {
        Assert.Equal(
            typeof(StartupEventHandler),
            typeof(Application).GetEvent(nameof(Application.Startup))!.EventHandlerType);
        Assert.Equal(
            typeof(ExitEventHandler),
            typeof(Application).GetEvent(nameof(Application.Exit))!.EventHandlerType);
        Assert.Equal(
            typeof(SessionEndingCancelEventHandler),
            typeof(Application).GetEvent(nameof(Application.SessionEnding))!.EventHandlerType);
    }
}
