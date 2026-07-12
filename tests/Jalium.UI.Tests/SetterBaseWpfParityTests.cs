using System.ComponentModel;
using Jalium.UI;

namespace Jalium.UI.Tests;

public class SetterBaseWpfParityTests
{
    private static readonly RoutedEvent TestEvent = EventManager.RegisterRoutedEvent(
        "SetterBaseParityEvent",
        RoutingStrategy.Direct,
        typeof(RoutedEventHandler),
        typeof(FrameworkElement));

    [Fact]
    public void SetterTypes_ExposeWpfInheritanceAndCollectionShapes()
    {
        Assert.True(typeof(SetterBase).IsAbstract);
        Assert.Equal(typeof(SetterBase), typeof(Setter).BaseType);
        Assert.Equal(typeof(SetterBase), typeof(EventSetter).BaseType);
        Assert.Equal(typeof(SetterBaseCollection), typeof(Style).GetProperty(nameof(Style.Setters))!.PropertyType);
        Assert.Equal(typeof(SetterBaseCollection), typeof(TriggerBase).GetProperty(nameof(TriggerBase.Setters))!.PropertyType);

        Assert.IsType<SetterBaseCollection>(new DataTrigger().Setters);
        Assert.IsType<SetterBaseCollection>(new MultiTrigger().Setters);
        Assert.IsType<SetterBaseCollection>(new MultiDataTrigger().Setters);
    }

    [Fact]
    public void StyleSetters_AcceptPropertyAndEventSetters_AndApplyBoth()
    {
        var raised = 0;
        RoutedEventHandler handler = (_, _) => raised++;
        var style = new Style(typeof(FrameworkElement));
        var propertySetter = new Setter(FrameworkElement.TagProperty, "styled");
        var eventSetter = new EventSetter(TestEvent, handler);

        style.Setters.Add(propertySetter);
        style.Setters.Add(eventSetter);
        style.Seal();

        Assert.True(style.Setters.IsSealed);
        Assert.True(propertySetter.IsSealed);
        Assert.True(eventSetter.IsSealed);

        var element = new FrameworkElement();
        style.Apply(element);
        element.RaiseEvent(new RoutedEventArgs(TestEvent, element));

        Assert.Equal("styled", element.Tag);
        Assert.Equal(1, raised);

        style.Remove(element);
        element.RaiseEvent(new RoutedEventArgs(TestEvent, element));
        Assert.Null(element.Tag);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void LegacyEventSettersView_IsBackedByStyleSettersAndSharesSealing()
    {
        RoutedEventHandler handler = (_, _) => { };
        var style = new Style(typeof(FrameworkElement));
        var eventSetter = new EventSetter(TestEvent, handler);

        style.EventSetters.Add(eventSetter);

        Assert.Same(eventSetter, Assert.Single(style.Setters));
        Assert.Same(eventSetter, Assert.Single(style.EventSetters));

        style.Seal();

        Assert.True(style.EventSetters.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => style.EventSetters.Add(new EventSetter(TestEvent, handler)));
    }

    [Fact]
    public void SetterInitialization_TracksState_ConvertsAndCannotOutliveSeal()
    {
        var setter = new Setter();
        var initialize = (ISupportInitialize)setter;

        Assert.Throws<InvalidOperationException>(initialize.EndInit);

        initialize.BeginInit();
        Assert.Throws<InvalidOperationException>(initialize.BeginInit);
        setter.Property = UIElement.IsEnabledProperty;
        setter.Value = "false";
        initialize.EndInit();

        Assert.Equal(false, setter.Value);

        var style = new Style(typeof(FrameworkElement));
        style.Setters.Add(setter);
        style.Seal();

        Assert.Throws<InvalidOperationException>(() => setter.Value = true);
        Assert.Throws<InvalidOperationException>(() => style.Setters.Clear());
        Assert.Throws<InvalidOperationException>(initialize.BeginInit);
    }

    [Fact]
    public void StyleSeal_SealsTriggerSetterCollections()
    {
        var style = new Style(typeof(FrameworkElement));
        var trigger = new DataTrigger();
        trigger.Setters.Add(new Setter(FrameworkElement.TagProperty, "active"));
        style.Triggers.Add(trigger);

        style.Seal();

        Assert.True(trigger.Setters.IsSealed);
        Assert.True(trigger.Setters[0].IsSealed);
        Assert.Throws<InvalidOperationException>(() => trigger.Setters.Add(new Setter(FrameworkElement.TagProperty, "other")));
    }
}
