using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Diagnostics;
using Jalium.UI.Input;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using ShapePath = Jalium.UI.Shapes.Path;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsEventsTemplateTests
{
    private static readonly RoutedEvent BubbleEvent = EventManager.RegisterRoutedEvent("TemplateBubble", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(DevToolsEventsTemplateTests));
    private static readonly RoutedEvent TunnelEvent = EventManager.RegisterRoutedEvent("TemplateTunnel", RoutingStrategy.Tunnel, typeof(RoutedEventHandler), typeof(DevToolsEventsTemplateTests));
    private static readonly RoutedEvent DirectEvent = EventManager.RegisterRoutedEvent("TemplateDirect", RoutingStrategy.Direct, typeof(RoutedEventHandler), typeof(DevToolsEventsTemplateTests));
    public DevToolsEventsTemplateTests() { ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }

    [Fact]
    public void EmptyRecordingAndCommandStatesSurviveTemplateReplacement()
    {
        int records = 0, resets = 0;
        var view = new DevToolsEventsView { ToggleRecordingCommand = new RelayCommand(() => records++), ResetCommand = new RelayCommand(() => resets++) };
        Layout(view);
        Assert.False(view.HasEvents); Assert.Equal(Visibility.Visible, Part<TextBlock>(view, "PART_Empty").Visibility);
        Assert.Equal("0", Part<DevToolsTag>(view, "PART_EventCount").Text);
        view.IsRecording = true; view.EventCount = 512;
        Assert.Equal("Stop recording", Part<DevToolsActionButton>(view, "PART_RecordButton").Label);
        Assert.Equal("512", Part<DevToolsTag>(view, "PART_EventCount").Text);
        Part<DevToolsActionButton>(view, "PART_RecordButton").PerformClick(); Part<DevToolsActionButton>(view, "PART_ResetButton").PerformClick();
        Assert.Equal((1, 1), (records, resets)); Assert.True(view.IsRecording);
        var original = view.Template; var empty = new ControlTemplate(typeof(DevToolsEventsView)); empty.SetVisualTree(() => new Border());
        view.Template = empty; Layout(view); view.IsRecording = false; view.FilterText = "Click";
        Assert.Null(view.FindName("PART_FilterTextBox"));
        view.Template = original; Layout(view);
        Assert.Equal("Start recording", Part<DevToolsActionButton>(view, "PART_RecordButton").Label);
        Assert.Equal("Click", Part<TextBox>(view, "PART_FilterTextBox").Text);
        view.IsEnabled = false; Assert.False(Part<DevToolsActionButton>(view, "PART_RecordButton").IsEnabled);
    }

    [Fact]
    public void FilterCommitsOnEnterAndLostFocusAndDetachesTheRetiredPart()
    {
        var values = new List<object?>(); var view = new DevToolsEventsView { ApplyFilterCommand = new RelayCommand(value => values.Add(value)) };
        Layout(view); var box = Part<TextBox>(view, "PART_FilterTextBox");
        box.Text = "Click; Loaded"; Assert.Equal("Click; Loaded", view.FilterText);
        var key = KeyInput(box, Key.Enter, UIElement.PreviewKeyDownEvent);
        box.RaiseEvent(key); Assert.True(key.Handled); Assert.Equal("Click; Loaded", Assert.Single(values));
        box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, box)); Assert.Equal(2, values.Count);
        var original = view.Template; var empty = new ControlTemplate(typeof(DevToolsEventsView)); empty.SetVisualTree(() => new Border());
        view.Template = empty; Layout(view); int before = values.Count;
        box.RaiseEvent(KeyInput(box, Key.Enter, UIElement.PreviewKeyDownEvent));
        box.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, box)); Assert.Equal(before, values.Count);
        view.Template = original; Layout(view);
        view.ApplyFilterCommand = new RelayCommand(value => values.Add(value), _ => false);
        var denied = KeyInput(Part<TextBox>(view, "PART_FilterTextBox"), Key.Enter, UIElement.PreviewKeyDownEvent);
        Part<TextBox>(view, "PART_FilterTextBox").RaiseEvent(denied); Assert.False(denied.Handled); Assert.Equal(before, values.Count);
        Assert.False(Part<DevToolsActionButton>(view, "PART_ApplyFilter").IsEnabled);
    }

    [Fact]
    public void SourceObserversDetachAndEmptyStatesFollowLiveCollections()
    {
        var old = new ObservedCollection(); var current = new ObservedCollection();
        var view = new DevToolsEventsView { EventsSource = old }; Assert.Equal(1, old.ViewSubscribers);
        view.EventsSource = current; Assert.Equal(0, old.ViewSubscribers); Assert.Equal(1, current.ViewSubscribers);
        Layout(view); current.Add(new DevToolsRoutedEventItem()); Assert.True(view.HasEvents);
        Assert.Equal(Visibility.Collapsed, Part<TextBlock>(view, "PART_Empty").Visibility);
        current.Clear(); Assert.False(view.HasEvents);
        view.EventsSource = null; Assert.Equal(0, current.ViewSubscribers); Assert.Null(Part<ListBox>(view, "PART_EventList").ItemsSource);
    }

    [Fact]
    public void NodeSourceNameStrategyAndDisabledStatesAreTemplateOwned()
    {
        var node = new DevToolsEventNode { TypeName = "Button", ElementName = "Leaf", Strategy = RoutingStrategy.Bubble, IsOriginalSource = true }; Layout(node, 180, 52);
        Assert.Equal("#Leaf", Part<TextBlock>(node, "PART_Name").Text);
        Assert.Equal(Visibility.Visible, Part<Border>(node, "PART_SourceFrame").Visibility);
        Assert.Equal(Color.FromRgb(0x5F, 0xB7, 0xCB), ColorOf(Part<Border>(node, "PART_SourceFrame").BorderBrush));
        node.Strategy = RoutingStrategy.Tunnel;
        Assert.Equal(Color.FromRgb(0xED, 0xA6, 0x3C), ColorOf(Part<Border>(node, "PART_SourceFrame").BorderBrush));
        node.ElementName = ""; node.IsOriginalSource = false;
        Assert.Equal(Visibility.Collapsed, Part<TextBlock>(node, "PART_Name").Visibility);
        Assert.Equal(Visibility.Collapsed, Part<Border>(node, "PART_SourceFrame").Visibility);
        var original = node.Template; var empty = new ControlTemplate(typeof(DevToolsEventNode)); empty.SetVisualTree(() => new Border());
        node.Template = empty; Layout(node); node.Strategy = RoutingStrategy.Direct; node.ElementName = "New"; node.IsOriginalSource = true;
        node.Template = original; Layout(node, 180, 52);
        Assert.Equal("#New", Part<TextBlock>(node, "PART_Name").Text);
        Assert.Equal(Color.FromRgb(0x56, 0xD9, 0x8D), ColorOf(Part<Border>(node, "PART_SourceFrame").BorderBrush));
        node.IsEnabled = false; Assert.Equal(.55, Assert.IsAssignableFrom<UIElement>(node.GetVisualChild(0)).Opacity, 3);
        Assert.Throws<ArgumentException>(() => node.Strategy = (RoutingStrategy)100);
    }

    [Theory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Space)]
    public void NodeKeyboardAndAutomationInvokeItsCurrentCommand(Key key)
    {
        int count = 0; object? actual = null; var parameter = new object();
        var node = new DevToolsEventNode { TypeName = "Button", ElementName = "Leaf", IsOriginalSource = true, Command = new RelayCommand(value => { count++; actual = value; }), CommandParameter = parameter };
        Layout(node, 180, 52);
        node.RaiseEvent(KeyInput(node, key, UIElement.KeyDownEvent));
        node.RaiseEvent(KeyInput(node, key, UIElement.KeyUpEvent));
        Assert.Equal(1, count); Assert.Same(parameter, actual);
        var peer = UIElementAutomationPeer.CreatePeerForElement(node)!;
        Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType()); Assert.Contains("Button #Leaf", peer.GetName()); Assert.Contains("original source", peer.GetName());
        var invoke = Assert.IsAssignableFrom<IInvokeProvider>(peer.GetPattern(PatternInterface.Invoke)); invoke.Invoke(); Assert.Equal(2, count);
        AutomationProperties.SetName(node, "Reveal leaf"); Assert.Equal("Reveal leaf", peer.GetName());
        node.IsEnabled = false; Assert.Throws<InvalidOperationException>(invoke.Invoke);
    }

    [Theory]
    [InlineData(RoutingStrategy.Bubble)]
    [InlineData(RoutingStrategy.Tunnel)]
    [InlineData(RoutingStrategy.Direct)]
    public void SnapshotNodesFollowDispatchOrderAndOnlyTheOriginalSourceIsMarked(RoutingStrategy strategy)
    {
        var capture = Capture(strategy, handled: true);
        var item = new DevToolsRoutedEventItem(); item.Update(capture.Entry);
        var nodes = item.Nodes.Cast<DevToolsEventNodeItem>().ToArray();
        Assert.True(item.Handled); Assert.True(item.HasNodes);
        Assert.Single(nodes, node => node.IsOriginalSource);
        Assert.True(nodes[^1].HasSuccessor == false); Assert.All(nodes[..^1], node => Assert.True(node.HasSuccessor));
        if (strategy == RoutingStrategy.Tunnel) { Assert.Equal("Root", nodes[0].ElementName); Assert.Equal("Leaf", nodes[^1].ElementName); }
        else { Assert.Equal("Leaf", nodes[0].ElementName); Assert.Equal(strategy == RoutingStrategy.Direct ? 1 : 4, nodes.Length); }
        var first = nodes[0]; item.Update(capture.Entry); Assert.Same(first, item.Nodes.Cast<DevToolsEventNodeItem>().First());
        first.VisualReference!.SetTarget(null!); item.Update(capture.Entry); Assert.False(first.CanReveal);
        GC.KeepAlive(capture);
    }

    [Theory]
    [InlineData(320)]
    [InlineData(480)]
    [InlineData(1040)]
    public void RouteTemplateShowsStrategyHandledAndCurrentNodeCommandsWithoutOverflow(double width)
    {
        var capture = Capture(RoutingStrategy.Tunnel, handled: true); var item = new DevToolsRoutedEventItem(); item.Update(capture.Entry);
        object? revealed = null; var view = new DevToolsEventsView { EventsSource = new[] { item }, EventCount = 1, RevealNodeCommand = new RelayCommand(value => revealed = value) };
        Layout(view, width);
        var filter = Part<TextBox>(view, "PART_FilterTextBox"); var filterPoint = filter.TransformToAncestor(view);
        Assert.True(filter.ActualWidth > 50); Assert.True(filterPoint.X + filter.ActualWidth <= width);
        var outer = Part<ListBox>(view, "PART_EventList"); Assert.True(outer.ActualWidth > width - 80);
        var row = Assert.IsType<ListBoxItem>(outer.ItemContainerGenerator.ContainerFromIndex(0));
        var tags = Descendants<DevToolsTag>(row).ToArray();
        var strategyTag = Assert.Single(tags, tag => tag.Text == "Tunnel"); Assert.Equal(Color.FromRgb(0xED, 0xA6, 0x3C), ColorOf(strategyTag.Foreground));
        var handled = Assert.Single(tags, tag => tag.Text == "Handled"); Assert.Equal(Visibility.Visible, handled.Visibility);
        var node = Descendants<DevToolsEventNode>(row).First(); Assert.Equal("Root", node.ElementName);
        var routePanel = Assert.Single(Descendants<VirtualizingStackPanel>(Assert.Single(Descendants<ListBox>(row))));
        Assert.Equal(Orientation.Horizontal, routePanel.Orientation);
        Assert.False(Assert.Single(Descendants<ScrollViewer>(Assert.Single(Descendants<ListBox>(row)))).IsScrollBarAutoHideEnabled);
        node.PerformClick(); Assert.Same(item.Nodes.Cast<DevToolsEventNodeItem>().First(), revealed);
        item.Handled = false; Assert.Equal(Visibility.Collapsed, handled.Visibility);
        Assert.True(row.ActualWidth <= outer.ActualWidth + .1);
        var connector = Descendants<ShapePath>(row).First(path => path.Name == "PART_Connector"); Assert.Equal(Color.FromRgb(0xED, 0xA6, 0x3C), ColorOf(connector.Stroke));
        GC.KeepAlive(capture);
    }

    [Fact]
    public void BothTimelineAndLongHorizontalRoutesVirtualizeTheirData()
    {
        var capture = Capture(RoutingStrategy.Bubble, depth: 65);
        var rows = new ObservableCollection<DevToolsRoutedEventItem>(Enumerable.Range(0, 80).Select(_ => { var item = new DevToolsRoutedEventItem(); item.Update(capture.Entry); return item; }));
        var view = new DevToolsEventsView { EventsSource = rows, EventCount = 512 }; Layout(view, 480, 430);
        var list = Part<ListBox>(view, "PART_EventList");
        Assert.InRange(Enumerable.Range(0, 80).Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) != null), 1, 79);
        var first = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
        var path = Assert.Single(Descendants<ListBox>(first)); Assert.Equal(64, path.Items.Count);
        Assert.Equal(Orientation.Horizontal, Assert.Single(Descendants<VirtualizingStackPanel>(path)).Orientation);
        Assert.InRange(Enumerable.Range(0, 64).Count(i => path.ItemContainerGenerator.ContainerFromIndex(i) != null), 1, 63);
        Assert.Equal("512", Part<DevToolsTag>(view, "PART_EventCount").Text);
        GC.KeepAlive(capture);
    }

    [Fact]
    public void MissingPathsCustomTemplatesAndExplicitNamesRemainSupported()
    {
        var view = new DevToolsEventsView { EventsSource = new[] { new DevToolsRoutedEventItem { EventName = "System event" } } }; Layout(view);
        Assert.Contains(Descendants<TextBlock>(view), text => text.Text == "No path captured." && text.Visibility == Visibility.Visible);
        var capture = Capture(RoutingStrategy.Direct); var item = new DevToolsRoutedEventItem(); item.Update(capture.Entry);
        view.EventsSource = new[] { item };
        var nodeTemplate = new DataTemplate(); nodeTemplate.SetVisualTree(() => new TextBlock { Text = "Custom node" }); view.NodeItemTemplate = nodeTemplate; Layout(view);
        Assert.Contains(Descendants<TextBlock>(view), text => text.Text == "Custom node");
        var rowTemplate = new DataTemplate(); rowTemplate.SetVisualTree(() => new TextBlock { Text = "Custom route" }); view.EventItemTemplate = rowTemplate; Layout(view);
        Assert.Contains(Descendants<TextBlock>(view), text => text.Text == "Custom route");
        var peer = UIElementAutomationPeer.CreatePeerForElement(view)!; Assert.Equal("Routed events", peer.GetName());
        AutomationProperties.SetName(view, "Project events"); Assert.Equal("Project events", peer.GetName());
        GC.KeepAlive(capture);
    }

    private static CaptureResult Capture(RoutingStrategy strategy, bool handled = false, int depth = 2)
    {
        var leaf = new Button { Name = "Leaf", Content = "Target" }; UIElement child = leaf;
        for (int i = 0; i < depth; i++) child = new Border { Name = "Parent" + i, Child = child };
        var root = new StackPanel { Name = "Root" }; root.Children.Add(child); Layout(root, 400, 200);
        var routedEvent = strategy switch { RoutingStrategy.Bubble => BubbleEvent, RoutingStrategy.Tunnel => TunnelEvent, _ => DirectEvent };
        leaf.AddHandler(routedEvent, new RoutedEventHandler((_, e) => e.Handled = handled));
        bool recording = RoutedEventDiagnostics.IsRecording; var filter = RoutedEventDiagnostics.GetFilter();
        RoutedEventDiagnostics.StopRecording(); RoutedEventDiagnostics.Reset(); RoutedEventDiagnostics.SetFilter(Array.Empty<string>());
        try
        {
            RoutedEventDiagnostics.StartRecording(); leaf.RaiseEvent(new RoutedEventArgs(routedEvent, leaf));
            return new CaptureResult(Assert.Single(RoutedEventDiagnostics.Snapshot()), root);
        }
        finally { RoutedEventDiagnostics.StopRecording(); RoutedEventDiagnostics.Reset(); RoutedEventDiagnostics.SetFilter(filter); if (recording) RoutedEventDiagnostics.StartRecording(); }
    }
    private static KeyEventArgs KeyInput(UIElement control, Key key, RoutedEvent routedEvent) =>
        new(Keyboard.PrimaryDevice, new InputSource { RootVisual = control }, 1, key) { RoutedEvent = routedEvent, Source = control };
    private sealed class InputSource : PresentationSource
    {
        public override Visual? RootVisual { get; set; }
        public override bool IsDisposed => false;
        protected override CompositionTarget? GetCompositionTargetCore() => null;
    }
    private sealed record CaptureResult(RoutedEventDiagnostics.RoutedEventEntry Entry, StackPanel Root);
    private static T Part<T>(FrameworkElement owner, string name) where T : class => Assert.IsAssignableFrom<T>(owner.FindName(name));
    private static Color ColorOf(Brush? brush) => Assert.IsType<SolidColorBrush>(brush).Color;
    private static void Layout(UIElement control, double width = 1040, double height = 760)
    {
        for (int pass = 0; pass < 3; pass++) { control.InvalidateMeasure(); control.Measure(new Size(width, height)); control.Arrange(new Rect(0, 0, width, height)); control.UpdateLayout(); }
    }
    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual
    {
        for (int i = 0; i < root.VisualChildrenCount; i++) if (root.GetVisualChild(i) is { } child) { if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    private sealed class ObservedCollection : System.Collections.ArrayList, INotifyCollectionChanged
    {
        private NotifyCollectionChangedEventHandler? _changed;
        public int ViewSubscribers => _changed?.GetInvocationList().Count(handler => handler.Method.DeclaringType?.FullName?.Contains(nameof(DevToolsEventsView), StringComparison.Ordinal) == true) ?? 0;
        public event NotifyCollectionChangedEventHandler? CollectionChanged { add => _changed += value; remove => _changed -= value; }
        public override int Add(object? value) { int index = base.Add(value); _changed?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, value, index)); return index; }
        public override void Clear() { base.Clear(); _changed?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)); }
    }
}
