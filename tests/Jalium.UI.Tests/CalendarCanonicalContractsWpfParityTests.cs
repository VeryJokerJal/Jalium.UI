using System.Collections;
using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

public sealed class CalendarCanonicalContractsWpfParityTests
{
    [Fact]
    public void CalendarAndSelectionTypes_LiveOnlyInTheirCanonicalWpfNamespaces()
    {
        var assembly = typeof(Calendar).Assembly;

        Assert.Equal("Jalium.UI.Controls", typeof(CalendarMode).Namespace);
        Assert.Equal("Jalium.UI.Controls", typeof(SelectionChangedEventArgs).Namespace);
        Assert.Equal("Jalium.UI.Controls", typeof(SelectionChangedEventHandler).Namespace);
        Assert.Null(assembly.GetType("Jalium.UI.Controls.Primitives.CalendarMode", throwOnError: false));
        Assert.Null(assembly.GetType("Jalium.UI.Controls.Primitives.SelectionChangedEventArgs", throwOnError: false));
        Assert.Null(assembly.GetType("Jalium.UI.Controls.Primitives.SelectionChangedEventHandler", throwOnError: false));

        Assert.Equal(["Month", "Year", "Decade"], Enum.GetNames<CalendarMode>());
        Assert.Equal([0, 1, 2], Enum.GetValues<CalendarMode>().Select(static value => (int)value));

        Assert.Equal(typeof(RoutedEventArgs), typeof(SelectionChangedEventArgs).BaseType);
        Assert.False(typeof(SelectionChangedEventArgs).IsSealed);
        Assert.Equal(
            typeof(IList),
            typeof(SelectionChangedEventArgs).GetProperty(nameof(SelectionChangedEventArgs.RemovedItems))!.PropertyType);
        Assert.Equal(
            typeof(IList),
            typeof(SelectionChangedEventArgs).GetProperty(nameof(SelectionChangedEventArgs.AddedItems))!.PropertyType);

        var constructor = typeof(SelectionChangedEventArgs).GetConstructor(
            [typeof(RoutedEvent), typeof(IList), typeof(IList)]);
        Assert.NotNull(constructor);
        Assert.Equal(
            ["id", "removedItems", "addedItems"],
            constructor!.GetParameters().Select(static parameter => parameter.Name));

        var invoke = typeof(SelectionChangedEventHandler).GetMethod("Invoke")!;
        Assert.Equal(typeof(void), invoke.ReturnType);
        Assert.Equal(
            [typeof(object), typeof(SelectionChangedEventArgs)],
            invoke.GetParameters().Select(static parameter => parameter.ParameterType));

        var invokeEventHandler = typeof(SelectionChangedEventArgs).GetMethod(
            "InvokeEventHandler",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            binder: null,
            types: [typeof(Delegate), typeof(object)],
            modifiers: null);
        Assert.NotNull(invokeEventHandler);
        Assert.True(invokeEventHandler!.IsFamily);
        Assert.True(invokeEventHandler.IsVirtual);
        Assert.Equal(typeof(RoutedEventArgs), invokeEventHandler.GetBaseDefinition().DeclaringType);

        Assert.Equal(
            typeof(SelectionChangedEventArgs),
            typeof(Selector).GetMethod(
                "OnSelectionChanged",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!
                .GetParameters()[0].ParameterType);
        Assert.Equal(
            typeof(CalendarMode),
            CalendarItem.DisplayModeProperty.PropertyType);
    }

    [Fact]
    public void SelectionChangedEventArgs_ValidatesAndSnapshotsItsItemLists()
    {
        var removed = new ArrayList { "old" };
        var added = new ArrayList { "new" };
        var args = new SelectionChangedEventArgs(Selector.SelectionChangedEvent, removed, added);

        Assert.NotSame(removed, args.RemovedItems);
        Assert.NotSame(added, args.AddedItems);
        Assert.Equal(["old"], args.RemovedItems.Cast<object>());
        Assert.Equal(["new"], args.AddedItems.Cast<object>());

        removed.Add("later-old");
        added.Add("later-new");

        Assert.Equal(["old"], args.RemovedItems.Cast<object>());
        Assert.Equal(["new"], args.AddedItems.Cast<object>());
        Assert.Throws<NotSupportedException>(() => args.RemovedItems.Add("direct"));
        Assert.Throws<NotSupportedException>(() => args.AddedItems.Add("direct"));

        Assert.Throws<ArgumentNullException>(() =>
            new SelectionChangedEventArgs(null!, removed, added));
        Assert.Throws<ArgumentNullException>(() =>
            new SelectionChangedEventArgs(Selector.SelectionChangedEvent, null!, added));
        Assert.Throws<ArgumentNullException>(() =>
            new SelectionChangedEventArgs(Selector.SelectionChangedEvent, removed, null!));
    }

    [Fact]
    public void Calendar_DisplayModeAndSelectedDatesChangedUseCanonicalContractsAndRealHooks()
    {
        Assert.Equal(
            typeof(CalendarMode),
            typeof(Calendar).GetProperty(nameof(Calendar.DisplayMode))!.PropertyType);
        Assert.Equal(typeof(CalendarMode), Calendar.DisplayModeProperty.PropertyType);
        var metadata = Assert.IsType<FrameworkPropertyMetadata>(
            Calendar.DisplayModeProperty.GetMetadata(typeof(Calendar)));
        Assert.Equal(CalendarMode.Month, metadata.DefaultValue);
        Assert.True(metadata.BindsTwoWayByDefault);

        Assert.Equal(RoutingStrategy.Direct, Calendar.SelectedDatesChangedEvent.RoutingStrategy);
        Assert.Equal(
            typeof(EventHandler<SelectionChangedEventArgs>),
            Calendar.SelectedDatesChangedEvent.HandlerType);
        Assert.Equal(
            typeof(EventHandler<SelectionChangedEventArgs>),
            typeof(Calendar).GetEvent(nameof(Calendar.SelectedDatesChanged))!.EventHandlerType);

        var selectedDatesChanged = typeof(Calendar).GetMethod(
            "OnSelectedDatesChanged",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            binder: null,
            types: [typeof(SelectionChangedEventArgs)],
            modifiers: null);
        Assert.NotNull(selectedDatesChanged);
        Assert.True(selectedDatesChanged!.IsFamily);
        Assert.True(selectedDatesChanged.IsVirtual);
        Assert.False(selectedDatesChanged.IsFinal);

        var calendar = new ProbeCalendar();
        SelectionChangedEventArgs? eventArgs = null;
        calendar.SelectedDatesChanged += (_, e) =>
        {
            calendar.Notifications.Add("event");
            eventArgs = e;
        };

        var selectedDate = new DateTime(2026, 7, 11);
        calendar.SelectedDate = selectedDate;

        Assert.Equal(["hook", "event", "after-hook"], calendar.Notifications);
        Assert.Same(calendar.CapturedArgs, eventArgs);
        Assert.Equal([selectedDate], eventArgs!.AddedItems.Cast<DateTime>());
        Assert.Empty(eventArgs.RemovedItems);

        Assert.Throws<ArgumentException>(() => calendar.DisplayMode = (CalendarMode)99);
        Assert.Equal(CalendarMode.Month, calendar.DisplayMode);
    }

    [Fact]
    public void CalendarMode_XamlEnumConversionTargetsTheCanonicalPropertyType()
    {
        const string xaml = """
            <Calendar xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      DisplayMode="Year" />
            """;

        var calendar = Assert.IsType<Calendar>(XamlReader.Parse(xaml));

        Assert.Equal(CalendarMode.Year, calendar.DisplayMode);
        Assert.Equal(typeof(CalendarMode), Calendar.DisplayModeProperty.PropertyType);
    }

    private sealed class ProbeCalendar : Calendar
    {
        public List<string> Notifications { get; } = new();

        public SelectionChangedEventArgs? CapturedArgs { get; private set; }

        protected override void OnSelectedDatesChanged(SelectionChangedEventArgs e)
        {
            CapturedArgs = e;
            Notifications.Add("hook");
            base.OnSelectedDatesChanged(e);
            Notifications.Add("after-hook");
        }
    }
}
