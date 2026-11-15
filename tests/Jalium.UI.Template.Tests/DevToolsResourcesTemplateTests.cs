using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Input;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsResourcesTemplateTests
{
    public DevToolsResourcesTemplateTests() { ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }

    [Fact]
    public void OptionalToggleActionsExposeStateAndPreserveOrdinaryButtonBehavior()
    {
        int actions = 0;
        var button = new DevToolsActionButton { Label = "Option", Command = new RelayCommand(() => actions++) };
        Layout(button); var peer = UIElementAutomationPeer.CreatePeerForElement(button)!;
        Assert.Null(peer.GetPattern(PatternInterface.Toggle));
        button.IsToggle = true; var toggle = Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle));
        Assert.Equal(ToggleState.Off, toggle.ToggleState); toggle.Toggle(); Assert.Equal(ToggleState.On, toggle.ToggleState); Assert.Equal(1, actions);
        Assert.IsAssignableFrom<IInvokeProvider>(peer.GetPattern(PatternInterface.Invoke)).Invoke(); Assert.Equal(ToggleState.Off, toggle.ToggleState); Assert.Equal(2, actions);
        button.IsActive = true; Assert.Equal(ToggleState.On, toggle.ToggleState);
        AutomationProperties.SetName(button, "Project option"); Assert.Equal("Project option", peer.GetName());
        button.IsEnabled = false; Assert.Throws<InvalidOperationException>(() => toggle.Toggle()); Assert.Equal(2, actions);
        button.IsEnabled = true; button.IsToggle = false; Assert.Null(peer.GetPattern(PatternInterface.Toggle));
        button.PerformClick(); Assert.True(button.IsActive); Assert.Equal(3, actions);
    }

    [Fact]
    public void WinnersToggleUsesTheViewStateWithoutDoubleToggling()
    {
        int commands = 0; var view = new DevToolsResourcesView();
        view.ToggleWinnersOnlyCommand = new RelayCommand(() => { commands++; view.IsWinnersOnly = !view.IsWinnersOnly; });
        Layout(view); var button = Part<DevToolsActionButton>(view, "PART_WinnersOnly");
        var normalBackground = ColorOf(Part<Border>(button, "PART_Surface").Background);
        var toggle = Assert.IsAssignableFrom<IToggleProvider>(UIElementAutomationPeer.CreatePeerForElement(button)!.GetPattern(PatternInterface.Toggle));
        toggle.Toggle(); Assert.True(view.IsWinnersOnly); Assert.True(button.IsActive); Assert.Equal(ToggleState.On, toggle.ToggleState); Assert.Equal(1, commands);
        toggle.Toggle(); Assert.False(view.IsWinnersOnly); Assert.False(button.IsActive); Assert.Equal(ToggleState.Off, toggle.ToggleState); Assert.Equal(2, commands);
        Assert.Equal(normalBackground, ColorOf(Part<Border>(button, "PART_Surface").Background));
        view.IsWinnersOnly = true; Assert.Equal(ToggleState.On, toggle.ToggleState);
        view.IsEnabled = false; Assert.Throws<InvalidOperationException>(() => toggle.Toggle()); Assert.Equal(2, commands);
    }

    [Fact]
    public void EmptyCountersCommandsAndFiltersSurviveTemplateReplacement()
    {
        int refreshes = 0, toggles = 0;
        var view = new DevToolsResourcesView { ScopeCount = 3, DeclarationCount = 2000, WinnerCount = 700,
            RefreshCommand = new RelayCommand(() => refreshes++), ToggleWinnersOnlyCommand = new RelayCommand(() => toggles++) };
        Layout(view);
        Assert.False(view.HasRows); Assert.Equal(Visibility.Visible, Part<TextBlock>(view, "PART_Empty").Visibility);
        Assert.Equal("3", Part<DevToolsTag>(view, "PART_ScopeCount").Text);
        Assert.Equal("2000", Part<DevToolsTag>(view, "PART_KeyCount").Text);
        Assert.Equal("700", Part<DevToolsTag>(view, "PART_WinnerCount").Text);
        Part<DevToolsActionButton>(view, "PART_Refresh").PerformClick(); Part<DevToolsActionButton>(view, "PART_WinnersOnly").PerformClick();
        Assert.Equal((1, 1), (refreshes, toggles));
        view.IsWinnersOnly = true; Assert.True(Part<DevToolsActionButton>(view, "PART_WinnersOnly").IsActive);
        Assert.Equal("No matching keys.", Part<TextBlock>(view, "PART_Empty").Text);
        var original = view.Template; view.Template = EmptyTemplate(typeof(DevToolsResourcesView)); Layout(view);
        view.FilterText = "Accent"; view.IsWinnersOnly = false; view.ItemsSource = new[] { Item() };
        Assert.Null(view.FindName("PART_ResourceList")); Assert.True(view.HasRows);
        view.Template = original; Layout(view);
        Assert.Equal("Accent", Part<TextBox>(view, "PART_FilterTextBox").Text);
        Assert.Equal(Visibility.Visible, Part<ListBox>(view, "PART_ResourceList").Visibility);
        view.IsEnabled = false; Assert.False(Part<DevToolsActionButton>(view, "PART_Refresh").IsEnabled);
        Assert.Throws<InvalidOperationException>(() => view.SetValue(DevToolsResourcesView.HasRowsProperty, false));
    }

    [Fact]
    public void FilterPartsCommitOnlyWhenCurrentEnabledAndExecutable()
    {
        var commits = new List<object?>(); bool executable = true; int changes = 0;
        var view = new DevToolsResourcesView { RefreshCommand = new RelayCommand(value => commits.Add(value), _ => executable) };
        view.FilterChanged += (_, _) => changes++;
        Layout(view); var box = Part<TextBox>(view, "PART_FilterTextBox");
        box.Text = "Accent"; Assert.Equal("Accent", view.FilterText); Assert.Equal(1, changes);
        var enter = KeyInput(box); box.RaiseEvent(enter); Assert.True(enter.Handled); Assert.Equal("Accent", Assert.Single(commits));
        box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, box)); Assert.Equal(2, commits.Count);
        view.Template = EmptyTemplate(typeof(DevToolsResourcesView)); Layout(view);
        box.RaiseEvent(KeyInput(box)); box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, box)); Assert.Equal(2, commits.Count);
        view.ClearValue(Control.TemplateProperty); Layout(view); box = Part<TextBox>(view, "PART_FilterTextBox");
        executable = false; enter = KeyInput(box); box.RaiseEvent(enter); Assert.False(enter.Handled); Assert.Equal(2, commits.Count);
        executable = true; view.IsEnabled = false; enter = KeyInput(box); box.RaiseEvent(enter); Assert.False(enter.Handled); Assert.Equal(2, commits.Count);
    }

    [Fact]
    public void ReplacingObservableSourcesDetachesTheRetiredViewObserver()
    {
        var old = new ObservedCollection(); var current = new ObservedCollection();
        var view = new DevToolsResourcesView { ItemsSource = old }; Layout(view);
        Assert.Equal(1, old.ViewSubscribers); old.Add(Item()); Assert.True(view.HasRows);
        view.ItemsSource = current; Assert.Equal(0, old.ViewSubscribers); Assert.Equal(1, current.ViewSubscribers); Assert.False(view.HasRows);
        old.Add(Item()); Assert.False(view.HasRows); current.Add(Item()); Assert.True(view.HasRows);
        current.Clear(); Assert.False(view.HasRows); view.ItemsSource = null; Assert.Equal(0, current.ViewSubscribers);
    }

    [Theory]
    [InlineData(DevToolsResourceRowKind.Resource)]
    [InlineData(DevToolsResourceRowKind.Scope)]
    [InlineData(DevToolsResourceRowKind.Dictionary)]
    [InlineData(DevToolsResourceRowKind.Note)]
    public void RowKindsResolutionAndReplacementHaveStableVisualStates(DevToolsResourceRowKind kind)
    {
        var row = new DevToolsResourceRow { RowKind = kind, KeyText = "Key", ValueText = "Value", IsWinner = true }; Layout(row, 700, 100);
        Assert.Equal(kind == DevToolsResourceRowKind.Resource ? Visibility.Visible : Visibility.Collapsed, Part<Border>(row, "PART_ResourceBorder").Visibility);
        Assert.Equal(kind is DevToolsResourceRowKind.Scope or DevToolsResourceRowKind.Dictionary ? Visibility.Visible : Visibility.Collapsed, Part<Border>(row, "PART_Header").Visibility);
        Assert.Equal(kind == DevToolsResourceRowKind.Note ? Visibility.Visible : Visibility.Collapsed, Part<TextBlock>(row, "PART_Note").Visibility);
        Assert.Equal(Color.FromRgb(0x56, 0xD9, 0x8D), ColorOf(Part<Border>(row, "PART_ResourceBorder").BorderBrush));
        row.IsWinner = false; Assert.Equal(1, Part<Grid>(row, "PART_EntryGrid").Opacity);
        Assert.Equal(Color.FromRgb(0x94, 0x9C, 0xA8), ColorOf(Part<TextBlock>(row, "PART_Key").Foreground));
        row.IsInactive = true; Assert.Equal(Visibility.Visible, Part<DevToolsTag>(row, "PART_InactiveTag").Visibility);
        var template = row.Template; row.Template = EmptyTemplate(typeof(DevToolsResourceRow)); Layout(row);
        row.IsWinner = true; row.IsInactive = false; row.RowKind = DevToolsResourceRowKind.Resource;
        row.Template = template; Layout(row, 700, 100); Assert.Equal(1, Part<Grid>(row, "PART_EntryGrid").Opacity);
        Assert.Equal(Visibility.Collapsed, Part<DevToolsTag>(row, "PART_InactiveTag").Visibility);
    }

    [Fact]
    public void SemanticValuesSwatchesDetailsAndAutomationRemainExtensible()
    {
        var row = new DevToolsResourceRow { KeyText = "Tint", ValueText = "#FF0A141E", ValueKind = DevToolsResourceValueKind.Color, IsWinner = true,
            SwatchBrush = new SolidColorBrush(Color.FromRgb(10, 20, 30)), DetailText = "SolidColorBrush" }; Layout(row);
        Assert.Equal(Visibility.Visible, Part<Border>(row, "PART_Swatch").Visibility);
        Assert.Equal(Color.FromRgb(10, 20, 30), ColorOf(Part<Border>(row, "PART_Swatch").Background));
        Assert.Equal(Visibility.Collapsed, Part<DevToolsTag>(row, "PART_Type").Visibility);
        Assert.Equal(Visibility.Collapsed, Part<TextBlock>(row, "PART_Detail").Visibility);
        row.ShowDetails = true; Assert.Equal(Visibility.Visible, Part<TextBlock>(row, "PART_Detail").Visibility);
        row.SwatchBrush = null; row.TypeLabel = "#"; row.ValueKind = DevToolsResourceValueKind.Number;
        Assert.Equal(Visibility.Visible, Part<DevToolsTag>(row, "PART_Type").Visibility);
        Assert.Equal(Color.FromRgb(0x8F, 0xD6, 0x9E), ColorOf(Part<TextBlock>(row, "PART_Value").Foreground));
        row.DetailText = ""; Assert.Equal(Visibility.Collapsed, Part<TextBlock>(row, "PART_Detail").Visibility);
        var peer = UIElementAutomationPeer.CreatePeerForElement(row)!; Assert.Contains("Tint = #FF0A141E", peer.GetName()); Assert.Contains("winner", peer.GetName());
        AutomationProperties.SetName(row, "Project tint"); Assert.Equal("Project tint", peer.GetName());
        Assert.Throws<ArgumentException>(() => row.ValueKind = (DevToolsResourceValueKind)100);
    }

    [Theory]
    [InlineData(320)]
    [InlineData(480)]
    [InlineData(1040)]
    public void ToolbarAndValueRowsFitNarrowAndWideLayouts(double width)
    {
        var view = new DevToolsResourcesView { ItemsSource = new[] { Item() } }; Layout(view, width, 430);
        var field = Part<TextBox>(view, "PART_FilterTextBox"); var actions = Part<StackPanel>(view, "PART_Actions");
        Assert.True(field.ActualWidth > 40); Assert.True(actions.TranslatePoint(new Point(actions.ActualWidth, 0), view).X <= width + .5);
        if (width == 320) Assert.True(actions.TranslatePoint(new Point(), view).Y >= field.TranslatePoint(new Point(0, field.ActualHeight), view).Y - .5);
        var row = Assert.Single(Descendants<DevToolsResourceRow>(view));
        Assert.False(row.ShowDetails); view.ShowDetails = true; Assert.True(row.ShowDetails);
        var key = Part<TextBlock>(row, "PART_Key"); var value = Part<TextBlock>(row, "PART_Value");
        if (width <= 480) Assert.True(value.TranslatePoint(new Point(), row).Y >= key.TranslatePoint(new Point(0, key.ActualHeight), row).Y);
        else Assert.True(value.TranslatePoint(new Point(), row).X >= key.TranslatePoint(new Point(key.ActualWidth, 0), row).X - .5);
        Layout(view, 1040, 430); Assert.Equal(0, Grid.GetRow(value)); Assert.Equal(2, Grid.GetColumn(value)); Assert.Equal(1, Grid.GetColumnSpan(key));
    }

    [Fact]
    public void CompactRowsReportTheirFinalHeightOnTheFirstMeasure()
    {
        var row = new DevToolsResourceRow { KeyText = "TerminalSelectionBackground", ValueText = "#594A90E2" };
        row.Measure(new Size(438, double.PositiveInfinity));
        double initialHeight = row.DesiredSize.Height;
        row.Arrange(new Rect(0, 0, 438, initialHeight));
        row.InvalidateMeasure(); row.Measure(new Size(438, double.PositiveInfinity));
        Assert.Equal(initialHeight, row.DesiredSize.Height);
        var key = Part<TextBlock>(row, "PART_Key"); var value = Part<TextBlock>(row, "PART_Value");
        Assert.Equal(1, Grid.GetRow(value)); Assert.Equal(0, Grid.GetRow(key));
    }

    [Fact]
    public void LargeDictionariesVirtualizeAndCustomTemplatesRemainSupported()
    {
        var source = new ObservableCollection<DevToolsResourceItem>(Enumerable.Range(0, 2000).Select(i => new DevToolsResourceItem { KeyText = "Key" + i, ValueText = "Value" + i, IsWinner = true }));
        var view = new DevToolsResourcesView { ItemsSource = source, DeclarationCount = 2000 }; Layout(view, 480, 430);
        var list = Part<ListBox>(view, "PART_ResourceList");
        Assert.InRange(Enumerable.Range(0, 2000).Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) != null), 1, 100);
        list.ScrollIntoView(source[1500]); Layout(view, 480, 430); Assert.NotNull(list.ItemContainerGenerator.ContainerFromIndex(1500));
        var custom = new DataTemplate(); custom.SetVisualTree(() => new TextBlock { Text = "Custom resource" });
        view.ItemTemplate = custom; Layout(view); Assert.Contains(Descendants<TextBlock>(view), text => text.Text == "Custom resource");
        var peer = UIElementAutomationPeer.CreatePeerForElement(view)!; Assert.Equal("Resource resolution", peer.GetName());
        AutomationProperties.SetName(view, "Project resources"); Assert.Equal("Project resources", peer.GetName());
    }

    private static DevToolsResourceItem Item() => new() { KeyText = "A.Long.Resource.Key", ValueText = "#FF123456", TypeLabel = "C", DetailText = "SolidColorBrush", IsWinner = true, ValueKind = DevToolsResourceValueKind.Color };
    private static ControlTemplate EmptyTemplate(Type type) { var template = new ControlTemplate(type); template.SetVisualTree(() => new Border()); return template; }
    private static T Part<T>(FrameworkElement owner, string name) where T : class => Assert.IsAssignableFrom<T>(owner.FindName(name));
    private static Color ColorOf(Brush? brush) => Assert.IsType<SolidColorBrush>(brush).Color;
    private static void Layout(UIElement control, double width = 1040, double height = 760) { for (int pass = 0; pass < 3; pass++) { control.InvalidateMeasure(); control.Measure(new Size(width, height)); control.Arrange(new Rect(0, 0, width, height)); control.UpdateLayout(); } }
    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual { for (int i = 0; i < root.VisualChildrenCount; i++) if (root.GetVisualChild(i) is { } child) { if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private static KeyEventArgs KeyInput(UIElement control) => new(Keyboard.PrimaryDevice, new InputSource { RootVisual = control }, 1, Key.Enter) { RoutedEvent = UIElement.PreviewKeyDownEvent, Source = control };
    private sealed class InputSource : PresentationSource { public override Visual? RootVisual { get; set; } public override bool IsDisposed => false; protected override CompositionTarget? GetCompositionTargetCore() => null; }
    private sealed class ObservedCollection : System.Collections.ArrayList, INotifyCollectionChanged
    {
        private NotifyCollectionChangedEventHandler? _changed;
        public int ViewSubscribers => _changed?.GetInvocationList().Count(handler => handler.Method.DeclaringType?.FullName?.Contains(nameof(DevToolsResourcesView), StringComparison.Ordinal) == true) ?? 0;
        public event NotifyCollectionChangedEventHandler? CollectionChanged { add => _changed += value; remove => _changed -= value; }
        public override int Add(object? value) { int index = base.Add(value); _changed?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, value, index)); return index; }
        public override void Clear() { base.Clear(); _changed?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)); }
    }
}
