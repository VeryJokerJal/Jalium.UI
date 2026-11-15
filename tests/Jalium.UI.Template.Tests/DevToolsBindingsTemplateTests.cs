using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Data;
using Jalium.UI.Diagnostics;
using Jalium.UI.Input;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using ShapePath = Jalium.UI.Shapes.Path;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsBindingsTemplateTests
{
    public DevToolsBindingsTemplateTests() { ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }

    [Fact]
    public void ViewStatesAndCommandsSurviveMissingPartsAndTemplateRestoration()
    {
        int records = 0, resets = 0;
        var view = new DevToolsBindingsView { ToggleRecordingCommand = new RelayCommand(() => records++), ResetCommand = new RelayCommand(() => resets++) };
        Layout(view);
        Assert.Equal("Select an element in Inspector.", Part<TextBlock>(view, "PART_BindingsEmpty").Text);
        Assert.Equal(Visibility.Visible, Part<TextBlock>(view, "PART_EventsEmpty").Visibility);
        view.SelectedElementName = "TextBox #Editor";
        Assert.Equal("No active bindings.", Part<TextBlock>(view, "PART_BindingsEmpty").Text);
        view.IsRecording = true;
        Assert.Equal("Stop recording", Part<DevToolsActionButton>(view, "PART_RecordButton").Label);
        Part<DevToolsActionButton>(view, "PART_RecordButton").PerformClick();
        Part<DevToolsActionButton>(view, "PART_ResetButton").PerformClick();
        Assert.Equal((1, 1), (records, resets)); Assert.True(view.IsRecording);
        var original = view.Template;
        var empty = new ControlTemplate(typeof(DevToolsBindingsView)); empty.SetVisualTree(() => new Border());
        view.Template = empty; Layout(view); view.IsRecording = false; view.SelectedElementName = "";
        Assert.Null(view.FindName("PART_RecordButton"));
        view.Template = original; Layout(view);
        Assert.Equal("Start recording", Part<DevToolsActionButton>(view, "PART_RecordButton").Label);
        Assert.Equal("Select an element in Inspector.", Part<TextBlock>(view, "PART_BindingsEmpty").Text);
        view.IsEnabled = false;
        Assert.False(Part<DevToolsActionButton>(view, "PART_RecordButton").IsEnabled);
    }

    [Fact]
    public void SourceReplacementDetachesViewObserversAndTracksEmptyStates()
    {
        var old = new ObservedCollection(); var current = new ObservedCollection();
        var view = new DevToolsBindingsView { BindingsSource = old, EventsSource = old };
        Assert.Equal(2, old.ViewSubscribers);
        view.BindingsSource = current; view.EventsSource = current;
        Assert.Equal(0, old.ViewSubscribers); Assert.Equal(2, current.ViewSubscribers);
        Layout(view);
        current.Add(new DevToolsBindingItem()); Assert.True(view.HasBindings); Assert.True(view.HasEvents);
        current.Clear(); Assert.False(view.HasBindings); Assert.False(view.HasEvents);
        view.BindingsSource = null; view.EventsSource = null;
        Assert.Equal(0, current.ViewSubscribers);
        Assert.Null(Part<ListBox>(view, "PART_BindingsList").ItemsSource);
    }

    [Theory]
    [InlineData(1040)]
    [InlineData(480)]
    public void CardEndpointsHaveRealCommandsAndAdaptToTheirAvailableWidth(double width)
    {
        object? source = null, target = null;
        var item = Sample();
        var flow = new DevToolsBindingFlow { Item = item, RevealSourceCommand = new RelayCommand(value => source = value), RevealTargetCommand = new RelayCommand(value => target = value) };
        Layout(flow, width, 340);
        var from = Part<Button>(flow, "PART_SourceButton"); var to = Part<Button>(flow, "PART_TargetButton");
        Assert.True(from.ActualWidth > 100); Assert.True(to.ActualWidth > 100);
        from.PerformClick(); to.PerformClick(); Assert.Same(item, source); Assert.Same(item, target);
        Assert.Contains(Descendants<TextBlock>(flow), text => text.Text == "SourceEditor");
        Assert.Contains(Descendants<TextBlock>(flow), text => text.Text == "Text");
        var fromPoint = from.TransformToAncestor(flow); var toPoint = to.TransformToAncestor(flow);
        if (width < flow.CompactWidth)
        {
            Assert.True(flow.IsCompact); Assert.True(toPoint.Y >= fromPoint.Y + from.ActualHeight);
            Assert.Equal(fromPoint.X, toPoint.X, 2);
        }
        else { Assert.False(flow.IsCompact); Assert.True(toPoint.X >= fromPoint.X + from.ActualWidth); }
        Layout(flow, 1040, 340);
        Assert.False(flow.IsCompact); Assert.Equal(104, Part<ColumnDefinition>(flow, "PART_CenterColumn").Width.Value);
        Assert.Throws<ArgumentException>(() => flow.CompactWidth = double.NaN);
    }

    [Fact]
    public void CardTracksLiveStatusModeAndErrorsAndReleasesItsOldItem()
    {
        var item = Sample(); var flow = new DevToolsBindingFlow { Item = item }; Layout(flow);
        var accent = Part<Border>(flow, "PART_Accent"); var path = Part<ShapePath>(flow, "PART_FlowArrow"); var initialPath = path.Data;
        Assert.Equal(Color.FromRgb(0x56, 0xD9, 0x8D), Assert.IsType<SolidColorBrush>(accent.Background).Color);
        item.Status = BindingStatus.PathError; item.LastError = "A missing property"; item.Mode = BindingMode.TwoWay;
        Assert.Equal("PathError", Part<DevToolsTag>(flow, "PART_Status").Text);
        Assert.Equal(Color.FromRgb(0xF2, 0x65, 0x5C), Assert.IsType<SolidColorBrush>(accent.Background).Color);
        Assert.Equal(Visibility.Visible, Part<TextBlock>(flow, "PART_LastError").Visibility);
        Assert.NotSame(initialPath, path.Data);
        item.Mode = BindingMode.OneWay; item.Status = BindingStatus.Inactive; item.LastError = "";
        Assert.Same(initialPath, path.Data); Assert.Equal(Visibility.Collapsed, Part<TextBlock>(flow, "PART_LastError").Visibility);
        var replacement = Sample(); flow.Item = replacement;
        Assert.Equal(0, FlowSubscribers(item)); Assert.Equal(1, FlowSubscribers(replacement));
        var original = flow.Template;
        var empty = new ControlTemplate(typeof(DevToolsBindingFlow)); empty.SetVisualTree(() => new Border());
        flow.Template = empty; Layout(flow); replacement.Status = BindingStatus.Unattached;
        flow.Template = original; Layout(flow);
        Assert.Equal(Color.FromRgb(0xED, 0xA6, 0x3C), Assert.IsType<SolidColorBrush>(Part<Border>(flow, "PART_Accent").Background).Color);
        flow.Item = null; Assert.Equal(0, FlowSubscribers(replacement));
    }

    [Fact]
    public void TimelineVirtualizesAndUpdatesColorsAndMessagesInExistingRows()
    {
        var events = new ObservableCollection<DevToolsBindingEventItem>(Enumerable.Range(0, 120).Select(i => new DevToolsBindingEventItem { TimeText = "12:34:56.789", Kind = "UpdateTarget", TargetText = "TextBox.Text", SourceText = $"Model.Value{i}", Message = "" }));
        var view = new DevToolsBindingsView { EventsSource = events, EventCount = 512 }; Layout(view, 480);
        Assert.Equal("512", Part<DevToolsTag>(view, "PART_EventCount").Text);
        var list = Part<ListBox>(view, "PART_EventsList");
        Assert.True(list.ActualWidth > 400);
        Assert.InRange(Enumerable.Range(0, 120).Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) != null), 1, 90);
        var first = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
        var tag = Assert.Single(Descendants<DevToolsTag>(first));
        events[0].Kind = "Error"; events[0].Message = "Source property is missing"; Layout(view, 480);
        Assert.Same(first, list.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Equal(Color.FromRgb(0xF2, 0x65, 0x5C), Assert.IsType<SolidColorBrush>(tag.Foreground).Color);
        var message = Assert.Single(Descendants<TextBlock>(first), text => text.Text == "Source property is missing");
        Assert.Equal("Source property is missing", ToolTipService.GetToolTip(message));
        Assert.Equal(Visibility.Visible, message.Visibility);
        events[0].Message = ""; Layout(view, 480); Assert.Equal(Visibility.Collapsed, message.Visibility);
    }

    [Fact]
    public void RealBindingSnapshotsKeepWeakEndpointsAndActualCounters()
    {
        bool recording = BindingDiagnostics.IsRecording; BindingDiagnostics.Reset(); BindingDiagnostics.StartRecording();
        var source = new TextBlock { Name = "Source", Text = "First" }; var target = new TextBlock { Name = "Target" };
        try
        {
            var expression = target.SetBinding(TextBlock.TextProperty, new Binding("Text") { Source = source, Mode = BindingMode.OneWay });
            source.Text = "Second";
            var item = new DevToolsBindingItem(); item.Update(target, TextBlock.TextProperty, expression);
            Assert.Equal("TextBlock  #Target", item.TargetName); Assert.Equal("Text", item.TargetProperty);
            Assert.Equal("TextBlock  #Source", item.SourceName); Assert.Equal("Text", item.SourcePath);
            Assert.Equal(BindingMode.OneWay, item.Mode); Assert.Equal(BindingStatus.Active, item.Status);
            Assert.True(item.CanRevealSource); Assert.True(item.CanRevealTarget);
            Assert.True(item.TargetReference!.TryGetTarget(out var actualTarget)); Assert.Same(target, actualTarget);
            Assert.True(item.SourceReference!.TryGetTarget(out var actualSource)); Assert.Same(source, actualSource);
            Assert.NotEqual("T 0", item.TargetCountText);
            var entry = BindingDiagnostics.Snapshot().Last(); var row = new DevToolsBindingEventItem(); row.Update(entry);
            Assert.Equal(entry.Kind.ToString(), row.Kind); Assert.Equal("TextBlock.Text", row.TargetText);
            item.TargetReference.SetTarget(null!); item.SourceReference.SetTarget(null!);
            Assert.False(item.TargetReference.TryGetTarget(out _)); Assert.False(item.SourceReference.TryGetTarget(out _));
        }
        finally { target.ClearBinding(TextBlock.TextProperty); BindingDiagnostics.StopRecording(); BindingDiagnostics.Reset(); if (recording) BindingDiagnostics.StartRecording(); }
    }

    [Fact]
    public void ValidationErrorsRemainVisibleWhenRecordingIsOffAndRecoverWithANewBinding()
    {
        bool recording = BindingDiagnostics.IsRecording; BindingDiagnostics.StopRecording(); BindingDiagnostics.Reset();
        var target = new TextBlock();
        try
        {
            var expression = target.SetBinding(TextBlock.TextProperty, new Binding("Value") { Source = new InvalidSource(), ValidatesOnDataErrors = true });
            var item = new DevToolsBindingItem(); item.Update(target, TextBlock.TextProperty, expression);
            Assert.True(item.HasValidationError); Assert.Equal("Validation error", item.StatusText);
            Assert.Equal("Source validation failed", item.LastError); Assert.False(item.CanRevealSource);
            var flow = new DevToolsBindingFlow { Item = item }; Layout(flow);
            Assert.Equal("Validation error", Part<DevToolsTag>(flow, "PART_Status").Text);
            Assert.Equal(Color.FromRgb(0xF2, 0x65, 0x5C), Assert.IsType<SolidColorBrush>(Part<Border>(flow, "PART_Accent").Background).Color);
            var entry = Assert.Single(BindingDiagnostics.Snapshot(), row => row.Kind == BindingDiagnostics.BindingEventKind.Error);
            var eventItem = new DevToolsBindingEventItem(); eventItem.Update(entry);
            Assert.Equal("Source validation failed", eventItem.Message); Assert.Contains(eventItem.Message, eventItem.AutomationName);
            expression = target.SetBinding(TextBlock.TextProperty, new Binding("Text") { Source = new TextBlock { Text = "Valid" } });
            item.Update(target, TextBlock.TextProperty, expression);
            Assert.False(item.HasValidationError); Assert.Equal("Active", item.StatusText); Assert.Equal("", item.LastError);
            Assert.Equal(Color.FromRgb(0x56, 0xD9, 0x8D), Assert.IsType<SolidColorBrush>(Part<Border>(flow, "PART_Accent").Background).Color);
        }
        finally { target.ClearBinding(TextBlock.TextProperty); BindingDiagnostics.Reset(); if (recording) BindingDiagnostics.StartRecording(); }
    }

    [Fact]
    public void CustomRowsAndExplicitAutomationNamesRemainConsumerOwned()
    {
        var bindingTemplate = new DataTemplate(); bindingTemplate.SetVisualTree(() => new TextBlock { Text = "Custom binding" });
        var eventTemplate = new DataTemplate(); eventTemplate.SetVisualTree(() => new TextBlock { Text = "Custom event" });
        var view = new DevToolsBindingsView { BindingsSource = new[] { Sample() }, EventsSource = new[] { new DevToolsBindingEventItem() }, BindingItemTemplate = bindingTemplate, EventItemTemplate = eventTemplate };
        Layout(view);
        Assert.Contains(Descendants<TextBlock>(view), text => text.Text == "Custom binding");
        Assert.Contains(Descendants<TextBlock>(view), text => text.Text == "Custom event");
        var peer = UIElementAutomationPeer.CreatePeerForElement(view)!;
        Assert.Equal(AutomationControlType.Group, peer.GetAutomationControlType()); Assert.Equal("Binding diagnostics", peer.GetName());
        AutomationProperties.SetName(view, "Project bindings"); Assert.Equal("Project bindings", peer.GetName());
        var flow = new DevToolsBindingFlow { Item = Sample() }; Layout(flow);
        var flowPeer = UIElementAutomationPeer.CreatePeerForElement(flow)!;
        Assert.Equal(flow.Item.AutomationName, flowPeer.GetName()); AutomationProperties.SetName(flow, "Name binding"); Assert.Equal("Name binding", flowPeer.GetName());
    }

    private static DevToolsBindingItem Sample() => new() { SourceName = "SourceEditor", SourcePath = "Text", TargetName = "TextBox #Target", TargetProperty = "Text", Mode = BindingMode.OneWay, Status = BindingStatus.Active, CanRevealSource = true, CanRevealTarget = true };
    private sealed class InvalidSource : System.ComponentModel.IDataErrorInfo
    {
        public string Value => "Invalid";
        public string Error => "Source validation failed";
        public string this[string columnName] => Error;
    }
    private static int FlowSubscribers(DevToolsBindingItem item)
    {
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        object owner = item; var field = typeof(DependencyObject).GetField("PropertyChangedInternal", fields);
        if (field == null)
        {
            var optionalState = typeof(DependencyObject).GetField("_optionalState", fields); Assert.NotNull(optionalState);
            var state = optionalState.GetValue(item); if (state == null) return 0;
            owner = state; field = state.GetType().GetField("PropertyChanged", fields); Assert.NotNull(field);
        }
        return (field.GetValue(owner) as Delegate)?.GetInvocationList().Count(handler => handler.Method.DeclaringType?.FullName?.Contains(nameof(DevToolsBindingFlow), StringComparison.Ordinal) == true) ?? 0;
    }
    private static T Part<T>(FrameworkElement owner, string name) where T : class => Assert.IsAssignableFrom<T>(owner.FindName(name));
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
        public int ViewSubscribers => _changed?.GetInvocationList().Count(handler => handler.Method.DeclaringType?.FullName?.Contains(nameof(DevToolsBindingsView), StringComparison.Ordinal) == true) ?? 0;
        public event NotifyCollectionChangedEventHandler? CollectionChanged { add => _changed += value; remove => _changed -= value; }
        public override int Add(object? value) { int index = base.Add(value); _changed?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, value, index)); return index; }
        public override void Clear() { base.Clear(); _changed?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)); }
    }
}
