using System.Collections.Specialized;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Tests;

public sealed class SharedBaseShapeFinalGapWpfParityTests
{
    [Fact]
    public void UiAndFreezableCollectionExposeTheWpfAnimatableContract()
    {
        var element = new FrameworkElement();
        Assert.IsAssignableFrom<IAnimatable>(element);
        Assert.False(element.HasAnimatedProperties);
        Assert.Equal(element.GetValue(FrameworkElement.TagProperty),
            element.GetAnimationBaseValue(FrameworkElement.TagProperty));

        Assert.Equal(typeof(Animatable), typeof(FreezableCollection<DependencyObject>).BaseType);
        Assert.True(typeof(IAnimatable).IsAssignableFrom(typeof(FreezableCollection<DependencyObject>)));
    }

    [Fact]
    public void SelectorItemsExposeAWorkingVirtualizedItemProvider()
    {
        var item = new ListBoxItem();
        var peer = new ListBoxItemAutomationPeer(item);
        var provider = Assert.IsAssignableFrom<IVirtualizedItemProvider>(peer);

        provider.Realize();

        Assert.Same(item, peer.Owner);
    }

    [Fact]
    public void ItemsControlStoresAndClearsVirtualizedItemValues()
    {
        var items = new ItemsControl();
        var storage = Assert.IsAssignableFrom<IContainItemStorage>(items);
        var item = new object();

        storage.StoreItemValue(item, FrameworkElement.TagProperty, "saved");
        Assert.Equal("saved", storage.ReadItemValue(item, FrameworkElement.TagProperty));

        storage.ClearItemValue(item, FrameworkElement.TagProperty);
        Assert.Same(DependencyProperty.UnsetValue,
            storage.ReadItemValue(item, FrameworkElement.TagProperty));
    }

    [Fact]
    public void ButtonAndGridViewBasesExposeTheirRequiredInfrastructureInterfaces()
    {
        Assert.IsAssignableFrom<ICommandSource>(new Button());

        var presenter = new GridViewRowPresenter();
        var listener = Assert.IsAssignableFrom<IWeakEventListener>(presenter);
        Assert.True(listener.ReceiveWeakEvent(
            typeof(CollectionChangedEventManager),
            presenter,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)));
    }
}
