using System.ComponentModel;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public sealed class ParityCoreOverloadsTests
{
    private static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        "WpfParityCoreOverloadValue",
        typeof(string),
        typeof(ParityCoreOverloadsTests),
        new PropertyMetadata(string.Empty));

    [Fact]
    public void DataObjectAdditionalConstructorsStoreData()
    {
        var byName = new DataObject("custom", "value", autoConvert: false);
        var byType = new DataObject(typeof(int), 42);

        Assert.Equal("value", byName.GetData("custom", autoConvert: false));
        Assert.Equal(42, byType.GetData(typeof(int)));
    }

    [Fact]
    public void SetterThreeArgumentConstructorInitializesAllMembers()
    {
        var setter = new Setter(ValueProperty, "value", "PART_Target");

        Assert.Same(ValueProperty, setter.Property);
        Assert.Equal("value", setter.Value);
        Assert.Equal("PART_Target", setter.TargetName);
    }

    [Fact]
    public void EventTriggerRoutedEventConstructorAndSourceNameRoundTrip()
    {
        var routedEvent = EventManager.RegisterRoutedEvent(
            "WpfParityEventTriggerEvent",
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(ParityCoreOverloadsTests));

        var trigger = new EventTrigger(routedEvent) { SourceName = "PART_Source" };

        Assert.Same(routedEvent, trigger.RoutedEvent);
        Assert.Equal("PART_Source", trigger.SourceName);
    }

    [Fact]
    public void CalendarDateRangeDefaultsAndCoercesEndLikeWpf()
    {
        var defaultRange = new CalendarDateRange();
        Assert.Equal(DateTime.MinValue, defaultRange.Start);
        Assert.Equal(DateTime.MaxValue, defaultRange.End);

        var start = new DateTime(2026, 7, 10);
        var range = new CalendarDateRange(start, start.AddDays(-2));
        Assert.Equal(start, range.Start);
        Assert.Equal(start, range.End);

        var changes = new List<string?>();
        ((INotifyPropertyChanged)range).PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        range.Start = start.AddDays(1);

        Assert.Contains(nameof(CalendarDateRange.Start), changes);
        Assert.Contains(nameof(CalendarDateRange.End), changes);
    }
}
