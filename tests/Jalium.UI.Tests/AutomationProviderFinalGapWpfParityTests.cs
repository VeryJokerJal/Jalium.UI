using System.Reflection;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Raw = Jalium.UI.Automation.Provider;

namespace Jalium.UI.Tests;

[Collection("AutomationEventSink")]
public sealed class AutomationProviderFinalGapWpfParityTests
{
    [Fact]
    public void LegacyKindMismatchRows_SatisfyRequiredWpfTypeShapes()
    {
        AssertPeerShape<ButtonAutomationPeer>(
            typeof(ButtonBaseAutomationPeer), false, typeof(Raw.IInvokeProvider));
        AssertPeerShape<CheckBoxAutomationPeer>(
            typeof(ToggleButtonAutomationPeer), false, typeof(Raw.IToggleProvider));
        AssertPeerShape<ComboBoxAutomationPeer>(
            typeof(SelectorAutomationPeer), false,
            typeof(Raw.IExpandCollapseProvider), typeof(Raw.IValueProvider),
            typeof(Raw.ISelectionProvider), typeof(Raw.IItemContainerProvider));
        AssertPeerShape<DatePickerAutomationPeer>(
            typeof(FrameworkElementAutomationPeer), true,
            typeof(Raw.IExpandCollapseProvider), typeof(Raw.IValueProvider));
        AssertPeerShape<ListBoxAutomationPeer>(
            typeof(SelectorAutomationPeer), false,
            typeof(Raw.ISelectionProvider), typeof(Raw.IItemContainerProvider));
        AssertPeerShape<ListBoxItemAutomationPeer>(
            typeof(SelectorItemAutomationPeer), false,
            typeof(Raw.ISelectionItemProvider), typeof(Raw.IScrollItemProvider));
        AssertPeerShape<ListViewAutomationPeer>(
            typeof(ListBoxAutomationPeer), false,
            typeof(Raw.ISelectionProvider), typeof(Raw.IItemContainerProvider));
        AssertPeerShape<PasswordBoxAutomationPeer>(
            typeof(TextAutomationPeer), false, typeof(Raw.IValueProvider));
        AssertPeerShape<ProgressBarAutomationPeer>(
            typeof(RangeBaseAutomationPeer), false, typeof(Raw.IRangeValueProvider));
        AssertPeerShape<ScrollBarAutomationPeer>(
            typeof(RangeBaseAutomationPeer), false, typeof(Raw.IRangeValueProvider));
        AssertPeerShape<SliderAutomationPeer>(
            typeof(RangeBaseAutomationPeer), false, typeof(Raw.IRangeValueProvider));
        AssertPeerShape<TabControlAutomationPeer>(
            typeof(SelectorAutomationPeer), false,
            typeof(Raw.ISelectionProvider), typeof(Raw.IItemContainerProvider));
        AssertPeerShape<TabItemAutomationPeer>(
            typeof(SelectorItemAutomationPeer), false, typeof(Raw.ISelectionItemProvider));
    }

    [Fact]
    public void PeerDeclarations_UseCanonicalWpfProviderInterfaces()
    {
        AssertDeclaredInterfaces<ButtonAutomationPeer>(typeof(Raw.IInvokeProvider));
        AssertDeclaredInterfaces<ButtonBaseAutomationPeer>();
        AssertDeclaredInterfaces<CalendarAutomationPeer>(
            typeof(Raw.IGridProvider), typeof(Raw.IItemContainerProvider), typeof(Raw.IMultipleViewProvider),
            typeof(Raw.ISelectionProvider), typeof(Raw.ITableProvider));
        AssertDeclaredInterfaces<ComboBoxAutomationPeer>(typeof(Raw.IExpandCollapseProvider), typeof(Raw.IValueProvider));
        AssertDeclaredInterfaces<DataGridAutomationPeer>(typeof(Raw.IGridProvider), typeof(Raw.ISelectionProvider), typeof(Raw.ITableProvider));
        AssertDeclaredInterfaces<DatePickerAutomationPeer>(typeof(Raw.IExpandCollapseProvider), typeof(Raw.IValueProvider));
        AssertDeclaredInterfaces<ItemsControlAutomationPeer>(typeof(Raw.IItemContainerProvider));
        AssertDeclaredInterfaces<ListBoxAutomationPeer>();
        AssertDeclaredInterfaces<ListBoxItemAutomationPeer>(typeof(Raw.IScrollItemProvider));
        AssertDeclaredInterfaces<ListViewAutomationPeer>();
        AssertDeclaredInterfaces<PasswordBoxAutomationPeer>(typeof(Raw.IValueProvider));
        AssertDeclaredInterfaces<ProgressBarAutomationPeer>(typeof(Raw.IRangeValueProvider));
        AssertDeclaredInterfaces<RangeBaseAutomationPeer>(typeof(Raw.IRangeValueProvider));
        AssertDeclaredInterfaces<ScrollBarAutomationPeer>();
        AssertDeclaredInterfaces<SliderAutomationPeer>();
        AssertDeclaredInterfaces<TabControlAutomationPeer>(typeof(Raw.ISelectionProvider));
        AssertDeclaredInterfaces<TabItemAutomationPeer>(typeof(Raw.ISelectionItemProvider));
        AssertDeclaredInterfaces<ToggleButtonAutomationPeer>(typeof(Raw.IToggleProvider));
    }

    [Fact]
    public void ProviderContracts_UseExactWpfParameterNames()
    {
        AssertParameterNames(typeof(AutomationPeer), "PeerFromProvider", "provider");
        AssertParameterNames(typeof(AutomationPeer), "ProviderFromPeer", "peer");
        AssertParameterNames(typeof(AutomationPeer), nameof(AutomationPeer.RaiseAsyncContentLoadedEvent), "args");
        AssertParameterNames(
            typeof(AutomationPeer),
            nameof(AutomationPeer.RaiseNotificationEvent),
            "notificationKind", "notificationProcessing", "displayString", "activityId");

        MethodInfo comboSetValue = typeof(ComboBoxAutomationPeer)
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(method => method.Name.Contains(
                "Automation.Provider.IValueProvider.SetValue",
                StringComparison.Ordinal));
        Assert.Equal("val", Assert.Single(comboSetValue.GetParameters()).Name);

        Assert.NotNull(typeof(MenuItemAutomationPeer).GetMethod("GetAccessKeyCore", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.NotNull(typeof(MenuItemAutomationPeer).GetMethod("GetPositionInSetCore", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.NotNull(typeof(MenuItemAutomationPeer).GetMethod("GetSizeOfSetCore", BindingFlags.Instance | BindingFlags.NonPublic));
    }

    [Fact]
    public void OffscreenBehavior_UsesWpfValues()
    {
        Assert.Equal(0, (int)IsOffscreenBehavior.Default);
        Assert.Equal(1, (int)IsOffscreenBehavior.Onscreen);
        Assert.Equal(2, (int)IsOffscreenBehavior.Offscreen);
        Assert.Equal(3, (int)IsOffscreenBehavior.FromClip);
    }

    [Fact]
    public void CalendarProviders_AreStableAndDriveGridSelectionAndViews()
    {
        var calendar = new Calendar
        {
            DisplayDate = new DateTime(2026, 7, 12),
            FirstDayOfWeek = DayOfWeek.Monday,
            SelectionMode = CalendarSelectionMode.MultipleRange,
        };
        var peer = new CalendarAutomationPeer(calendar);
        var grid = Assert.IsAssignableFrom<Raw.IGridProvider>(peer.GetPattern(PatternInterface.Grid));
        var items = Assert.IsAssignableFrom<Raw.IItemContainerProvider>(peer.GetPattern(PatternInterface.ItemContainer));
        var selection = Assert.IsAssignableFrom<Raw.ISelectionProvider>(peer.GetPattern(PatternInterface.Selection));
        var table = Assert.IsAssignableFrom<Raw.ITableProvider>(peer.GetPattern(PatternInterface.Table));
        var views = Assert.IsAssignableFrom<Raw.IMultipleViewProvider>(peer.GetPattern(PatternInterface.MultipleView));

        Assert.Equal(6, grid.RowCount);
        Assert.Equal(7, grid.ColumnCount);
        Raw.IRawElementProviderSimple first = Assert.IsAssignableFrom<Raw.IRawElementProviderSimple>(grid.GetItem(0, 0));
        Assert.Same(first, grid.GetItem(0, 0));
        Assert.NotNull(items.FindItemByProperty(first, 0, null));
        Assert.Equal(7, table.GetColumnHeaders().Length);
        Assert.True(selection.CanSelectMultiple);

        calendar.SelectedDates.Add(new DateTime(2026, 7, 12));
        Assert.Single(selection.GetSelection());
        views.SetCurrentView((int)CalendarMode.Year);
        Assert.Equal((int)CalendarMode.Year, views.CurrentView);
        Assert.Equal(3, grid.RowCount);
        Assert.Equal(4, grid.ColumnCount);
    }

    [Fact]
    public void DataGridProviders_WorkForUnrealizedCellsHeadersAndSelection()
    {
        var grid = new DataGrid();
        var item = new Row("Jalium");
        grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Jalium.UI.Data.Binding(nameof(Row.Name)) });
        grid.Items.Add(item);
        grid.SelectedItems.Add(item);

        var peer = new DataGridAutomationPeer(grid);
        var gridProvider = Assert.IsAssignableFrom<Raw.IGridProvider>(peer.GetPattern(PatternInterface.Grid));
        var selection = Assert.IsAssignableFrom<Raw.ISelectionProvider>(peer.GetPattern(PatternInterface.Selection));
        var table = Assert.IsAssignableFrom<Raw.ITableProvider>(peer.GetPattern(PatternInterface.Table));

        Raw.IRawElementProviderSimple cell = Assert.IsAssignableFrom<Raw.IRawElementProviderSimple>(gridProvider.GetItem(0, 0));
        Assert.Same(cell, gridProvider.GetItem(0, 0));
        Assert.Single(selection.GetSelection());
        Assert.Single(table.GetColumnHeaders());
        Assert.Single(table.GetRowHeaders());
    }

    [Fact]
    public void ValueExpandAndMenuCoreBehaviors_AreFunctional()
    {
        var combo = new ComboBox { IsEditable = true, Text = "before" };
        var comboProvider = Assert.IsAssignableFrom<Raw.IValueProvider>(
            new ComboBoxAutomationPeer(combo).GetPattern(PatternInterface.Value));
        comboProvider.SetValue("after");
        Assert.Equal("after", combo.Text);

        var datePicker = new DatePicker();
        var dateProvider = Assert.IsAssignableFrom<Raw.IExpandCollapseProvider>(
            new DatePickerAutomationPeer(datePicker).GetPattern(PatternInterface.ExpandCollapse));
        dateProvider.Expand();
        Assert.True(datePicker.IsDropDownOpen);
        dateProvider.Collapse();
        Assert.False(datePicker.IsDropDownOpen);

        Assert.Equal("F", new MenuItemAutomationPeer(new MenuItem { Header = "_File" }).GetAccessKey());
    }

    [Fact]
    public void ProviderBridge_IsStableAndDetailedEventsPreservePayload()
    {
        var peer = new BridgePeer(new FrameworkElement());
        Raw.IRawElementProviderSimple provider = peer.ToProvider(peer);
        Assert.Same(provider, peer.ToProvider(peer));
        Assert.Same(peer, peer.FromProvider(provider));

        var sink = new DetailedSink();
        AutomationPeer.EventSink = sink;
        try
        {
            var asyncArgs = new AsyncContentLoadedEventArgs(AsyncContentLoadedState.Progress, 40);
            peer.RaiseAsyncContentLoadedEvent(asyncArgs);
            peer.RaiseNotificationEvent(
                AutomationNotificationKind.ActionCompleted,
                AutomationNotificationProcessing.MostRecent,
                "Saved",
                "save-1");

            Assert.Same(asyncArgs, sink.AsyncArgs);
            Assert.NotNull(sink.Notification);
            Assert.Equal(
                (AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.MostRecent, "Saved", "save-1"),
                sink.Notification.Value);
        }
        finally
        {
            AutomationPeer.EventSink = null;
        }
    }

    private static void AssertDeclaredInterfaces<T>(params Type[] expected)
    {
        Type type = typeof(T);
        var baseInterfaces = new HashSet<Type>(type.BaseType?.GetInterfaces() ?? Type.EmptyTypes);
        Type[] implemented = type.GetInterfaces();
        foreach (Type expectedInterface in expected)
        {
            Assert.Contains(expectedInterface, implemented);
        }

        Type[] unexpected = implemented
            .Where(candidate =>
                !baseInterfaces.Contains(candidate) &&
                candidate.Namespace == typeof(Raw.IInvokeProvider).Namespace &&
                !expected.Contains(candidate))
            .OrderBy(candidate => candidate.FullName)
            .ToArray();
        Assert.Empty(unexpected);
    }

    private static void AssertPeerShape<T>(Type expectedBaseType, bool isSealed, params Type[] requiredInterfaces)
    {
        Type type = typeof(T);
        Assert.True(type.IsClass);
        Assert.False(type.IsAbstract);
        Assert.Equal(isSealed, type.IsSealed);
        Assert.Equal(expectedBaseType, type.BaseType);

        Type[] actualInterfaces = type.GetInterfaces();
        Assert.All(requiredInterfaces, required => Assert.Contains(required, actualInterfaces));
    }

    private static void AssertParameterNames(Type type, string methodName, params string[] expected)
    {
        MethodInfo method = type.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method {type.FullName}.{methodName} was not found.");
        Assert.Equal(expected, method.GetParameters().Select(parameter => parameter.Name));
    }

    private sealed record Row(string Name);

    private sealed class BridgePeer : FrameworkElementAutomationPeer
    {
        internal BridgePeer(FrameworkElement owner) : base(owner) { }
        internal Raw.IRawElementProviderSimple ToProvider(AutomationPeer peer) => ProviderFromPeer(peer);
        internal AutomationPeer FromProvider(Raw.IRawElementProviderSimple provider) => PeerFromProvider(provider);
    }

    private sealed class DetailedSink : IAutomationEventSink
    {
        internal AsyncContentLoadedEventArgs? AsyncArgs { get; private set; }
        internal (AutomationNotificationKind, AutomationNotificationProcessing, string, string)? Notification { get; private set; }

        public void OnAutomationEventRaised(AutomationPeer peer, AutomationEvents eventId) { }
        public void OnPropertyChangedRaised(AutomationPeer peer, AutomationProperty property, object? oldValue, object? newValue) { }
        public void OnFocusChanged(AutomationPeer peer) { }
        public void OnAsyncContentLoadedRaised(AutomationPeer peer, AsyncContentLoadedEventArgs args) => AsyncArgs = args;
        public void OnNotificationRaised(
            AutomationPeer peer,
            AutomationNotificationKind notificationKind,
            AutomationNotificationProcessing notificationProcessing,
            string displayString,
            string activityId) =>
            Notification = (notificationKind, notificationProcessing, displayString, activityId);
    }
}
