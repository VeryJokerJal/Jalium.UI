using System.Collections;
using System.Reflection;
using Jalium.UI.Markup;
using Jalium.UI.Navigation;

namespace Jalium.UI.Tests;

public sealed class NavigationWindowParityTests
{
    [Fact]
    public void JournalDependencyPropertiesAndDedicatedEventsMatchWpfShape()
    {
        var window = new ProbeNavigationWindow();

        Assert.Equal(typeof(IEnumerable), typeof(NavigationWindow).GetProperty(nameof(NavigationWindow.BackStack))!.PropertyType);
        Assert.Equal(typeof(IEnumerable), typeof(NavigationWindow).GetProperty(nameof(NavigationWindow.ForwardStack))!.PropertyType);
        Assert.Equal(typeof(NavigatingCancelEventHandler), typeof(NavigationWindow).GetEvent(nameof(NavigationWindow.Navigating))!.EventHandlerType);
        Assert.Equal(typeof(NavigatedEventHandler), typeof(NavigationWindow).GetEvent(nameof(NavigationWindow.Navigated))!.EventHandlerType);
        Assert.Equal(typeof(NavigationFailedEventHandler), typeof(NavigationWindow).GetEvent(nameof(NavigationWindow.NavigationFailed))!.EventHandlerType);

        Assert.False(window.CanGoBack);
        Assert.True(window.Navigate(new object()));
        Assert.True(window.Navigate(new object()));
        Assert.True(window.CanGoBack);
        Assert.Same(window.BackStack, window.GetValue(NavigationWindow.BackStackProperty));
        Assert.True((bool)window.GetValue(NavigationWindow.CanGoBackProperty)!);
    }

    [Fact]
    public void UriContextAndXamlContentHooksHaveObservableBehavior()
    {
        var window = new ProbeNavigationWindow();
        var baseUri = new Uri("https://example.test/navigation/");
        ((IUriContext)window).BaseUri = baseUri;

        window.AddTextForTest("  ");
        window.AddChildForTest("content");

        Assert.Equal(baseUri, ((IUriContext)window).BaseUri);
        Assert.Equal("content", window.Content);
        Assert.True(window.ShouldSerializeContent());
        Assert.Throws<ArgumentException>(() => window.AddTextForTest("invalid"));

        const BindingFlags protectedDeclared = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        Assert.NotNull(typeof(NavigationWindow).GetMethod("AddChild", protectedDeclared));
        Assert.NotNull(typeof(NavigationWindow).GetMethod("AddText", protectedDeclared));
    }

    private sealed class ProbeNavigationWindow : NavigationWindow
    {
        public void AddChildForTest(object value) => AddChild(value);
        public void AddTextForTest(string text) => AddText(text);
    }
}
