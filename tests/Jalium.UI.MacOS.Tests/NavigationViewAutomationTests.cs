using Jalium.UI;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class NavigationViewAutomationTests
{
    [Fact]
    public void ClearingSelectedItemAlsoClearsVisualAndAccessibleSelection()
    {
        var first = new NavigationViewItem { Content = "Page" };
        var navigation = Create(first);
        navigation.SelectedItem = first;
        NavigationViewSelectionChangedEventArgs? change = null;
        navigation.SelectionChanged += (_, args) => change = args;
        navigation.SelectedItem = null;
        Assert.False(first.IsSelected);
        Assert.False(((ISelectionItemProvider)first.GetAutomationPeer()!.GetPattern(PatternInterface.SelectionItem)!).IsSelected);
        Assert.Null(navigation.SelectedItem);
        Assert.Null(change!.SelectedItem);
        Assert.Same(first, change.PreviousSelectedItem);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AccessibleActivationUpdatesTheOwningNavigationAndDeselectsTheOldItem(bool invoke)
    {
        var first = new NavigationViewItem { Content = "First" };
        var second = new NavigationViewItem { Content = "Second" };
        var navigation = Create(first, second);
        navigation.SelectedItem = first;
        var selectionChanged = 0;
        navigation.SelectionChanged += (_, _) => selectionChanged++;
        var peer = second.GetAutomationPeer()!;
        if (invoke) ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
        else ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)!).Select();
        Assert.Same(second, navigation.SelectedItem);
        Assert.True(second.IsSelected);
        Assert.False(first.IsSelected);
        Assert.Equal(1, selectionChanged);
    }

    [Fact]
    public void InvocationOfAnActionItemPreservesTheSelectedPage()
    {
        var first = new NavigationViewItem { Content = "Page" };
        var action = new NavigationViewItem { Content = "Action", SelectsOnInvoked = false };
        var navigation = Create(first, action);
        navigation.SelectedItem = first;
        var invoked = 0;
        navigation.ItemInvoked += (_, _) => invoked++;
        ((IInvokeProvider)action.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
        Assert.Same(first, navigation.SelectedItem);
        Assert.False(action.IsSelected);
        Assert.Equal(1, invoked);
    }

    [Fact]
    public void DisabledNavigationCannotBeInvokedOrSelected()
    {
        var first = new NavigationViewItem { Content = "Page" };
        var second = new NavigationViewItem { Content = "Disabled", IsEnabled = false };
        var navigation = Create(first, second);
        navigation.SelectedItem = first;
        var peer = second.GetAutomationPeer()!;
        Assert.Throws<InvalidOperationException>(() => ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke());
        Assert.Throws<InvalidOperationException>(() => ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)!).Select());
        Assert.Same(first, navigation.SelectedItem);
    }

    private static NavigationView Create(params NavigationViewItem[] items)
    {
        var template = new ControlTemplate(typeof(NavigationView));
        template.SetVisualTree(() => new StackPanel { Name = "PART_MenuItemsHost" });
        var navigation = new NavigationView { Template = template };
        foreach (var item in items) navigation.MenuItems.Add(item);
        navigation.Measure(new Size(400, 300));
        navigation.Arrange(new Rect(0, 0, 400, 300));
        return navigation;
    }
}
