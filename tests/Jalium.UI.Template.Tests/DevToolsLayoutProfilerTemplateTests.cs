using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Diagnostics;
using Jalium.UI.Input;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsLayoutProfilerTemplateTests
{
    public DevToolsLayoutProfilerTemplateTests() { ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }

    [Fact]
    public void EmptyRecordingSortAndFooterStatesAreLiveAndReturnToDefaults()
    {
        var view = new DevToolsLayoutProfiler(); Layout(view);
        Assert.False(view.HasStats); Assert.False(view.HasInvalidations);
        Assert.Equal(Visibility.Visible, Part<TextBlock>(view, "PART_StatsEmpty").Visibility);
        Assert.Equal(Visibility.Collapsed, Part<ListBox>(view, "PART_StatsList").Visibility);
        Assert.Equal("Start recording", Part<DevToolsActionButton>(view, "PART_RecordButton").Label);
        view.IsRecording = true; view.IsStackCaptureEnabled = true; view.FooterText = "+4 more elements tracked";
        view.SortMode = DevToolsLayoutSortMode.Arrange;
        Assert.Equal("Stop recording", Part<DevToolsActionButton>(view, "PART_RecordButton").Label);
        Assert.Equal("REC", Part<DevToolsTag>(view, "PART_RecordingTag").Text);
        Assert.True(Part<DevToolsActionButton>(view, "PART_StackButton").IsActive);
        Assert.Equal("Sort: Arrange µs", Part<DevToolsActionButton>(view, "PART_SortButton").Label);
        Assert.Equal(Visibility.Visible, Part<TextBlock>(view, "PART_Footer").Visibility);
        view.SortMode = DevToolsLayoutSortMode.Invalidations;
        Assert.Equal("Sort: Invalidations", Part<DevToolsActionButton>(view, "PART_SortButton").Label);
        view.SortMode = DevToolsLayoutSortMode.Measure; view.IsRecording = false; view.FooterText = "";
        Assert.Equal("Sort: Measure µs", Part<DevToolsActionButton>(view, "PART_SortButton").Label);
        Assert.Equal("Start recording", Part<DevToolsActionButton>(view, "PART_RecordButton").Label);
        Assert.Equal(Visibility.Collapsed, Part<TextBlock>(view, "PART_Footer").Visibility);
        Assert.Throws<ArgumentException>(() => view.SortMode = (DevToolsLayoutSortMode)100);
    }

    [Fact]
    public void SourcesDetachTheirOldObserversAndTrackCollectionMutations()
    {
        var old = new ObservedCollection(); var current = new ObservedCollection();
        var view = new DevToolsLayoutProfiler { StatsSource = old, InvalidationSource = old };
        Assert.Equal(2, old.Subscribers);
        view.StatsSource = current; view.InvalidationSource = current;
        Assert.Equal(0, old.Subscribers); Assert.Equal(2, current.Subscribers);
        Layout(view);
        current.Add(new DevToolsLayoutStatsItem { ElementName = "Button" });
        Assert.True(view.HasStats); Assert.True(view.HasInvalidations);
        Assert.Equal(Visibility.Collapsed, Part<TextBlock>(view, "PART_StatsEmpty").Visibility);
        current.Clear();
        Assert.False(view.HasStats); Assert.False(view.HasInvalidations);
        view.StatsSource = null; view.InvalidationSource = null;
        Assert.Null(Part<ListBox>(view, "PART_StatsList").ItemsSource);
        Assert.Null(Part<ListBox>(view, "PART_InvalidationList").ItemsSource);
        Assert.Equal(0, current.ProfilerSubscribers);
    }

    [Fact]
    public void CommandsAndSortNotificationsRemainHostOwnedAcrossRetemplating()
    {
        int records = 0, resets = 0, stacks = 0, sorts = 0;
        var view = new DevToolsLayoutProfiler
        {
            ToggleRecordingCommand = new RelayCommand(() => records++), ResetCommand = new RelayCommand(() => resets++),
            ToggleStackCaptureCommand = new RelayCommand(() => stacks++), CycleSortCommand = new RelayCommand(() => sorts++)
        };
        Layout(view);
        foreach (string part in new[] { "PART_RecordButton", "PART_ResetButton", "PART_StackButton", "PART_SortButton" }) Part<DevToolsActionButton>(view, part).PerformClick();
        Assert.Equal((1, 1, 1, 1), (records, resets, stacks, sorts));
        Assert.False(view.IsRecording);
        int changes = 0; view.SortModeChanged += (_, _) => changes++;
        var original = view.Template;
        var empty = new ControlTemplate(typeof(DevToolsLayoutProfiler)); empty.SetVisualTree(() => new Border());
        view.Template = empty; Layout(view);
        view.IsRecording = true; view.SortMode = DevToolsLayoutSortMode.Arrange; view.SortMode = DevToolsLayoutSortMode.Arrange;
        Assert.Equal(1, changes); Assert.Null(view.FindName("PART_RecordButton"));
        view.Template = original; Layout(view);
        Assert.Equal("Stop recording", Part<DevToolsActionButton>(view, "PART_RecordButton").Label);
        Part<DevToolsActionButton>(view, "PART_RecordButton").PerformClick(); Assert.Equal(2, records);
        view.IsEnabled = false; Assert.False(Part<DevToolsActionButton>(view, "PART_RecordButton").IsEnabled);
    }

    [Theory]
    [InlineData(1040)]
    [InlineData(480)]
    public void RealizedRowBindingsAndProportionalSegmentsUpdateWithoutReplacingContainers(double width)
    {
        var item = new DevToolsLayoutStatsItem { Rank = 1, ElementName = "Grid #Root", MeasureText = "2 × total 200 µs", MeasureFraction = .5, ArrangeFraction = .25, MeasureShare = .2, ArrangeShare = .3, VisualShare = .1, TrailingShare = .4 };
        object? revealed = null;
        var source = new ObservableCollection<DevToolsLayoutStatsItem> { item };
        var view = new DevToolsLayoutProfiler { StatsSource = source, RevealCommand = new RelayCommand(value => revealed = value) };
        Layout(view, width);
        var list = Part<ListBox>(view, "PART_StatsList");
        Assert.True(list.ActualWidth > width - 80, $"The ranking list should fill its card: {list.ActualWidth} / {width}");
        var container = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
        var reveal = Descendants<DevToolsActionButton>(container).Single(b => b.Label == "Reveal");
        var origin = reveal.TransformToAncestor(view);
        var point = new Point(origin.X + reveal.ActualWidth / 2, origin.Y + reveal.ActualHeight / 2);
        var hit = view.InputHitTest(point) as Visual;
        var hitPath = new List<Visual>();
        for (var current = hit; current != null; current = current.VisualParent) hitPath.Add(current);
        Assert.Contains(reveal, hitPath);
        reveal.PerformClick(); Assert.Same(item, revealed);
        var meters = Descendants<DevToolsMeter>(container).ToArray(); Assert.Equal(2, meters.Length);
        Assert.Equal(.5, meters[0].Value); Assert.Equal(.25, meters[1].Value);
        var strip = Assert.Single(Descendants<DevToolsProportionalPanel>(container));
        Assert.True(strip.ActualWidth > 0);
        Assert.Equal(strip.ActualWidth * .2, strip.Children[0].RenderSize.Width, 2);
        var texts = Descendants<TextBlock>(container).ToArray();
        Assert.Contains(texts, t => t.Text == "1");
        item.ElementName = "Button #Updated"; item.MeasureFraction = .8; item.MeasureShare = 0; item.ArrangeShare = 0; item.VisualShare = 0; item.TrailingShare = 1;
        Layout(view, width);
        Assert.Same(container, list.ItemContainerGenerator.ContainerFromIndex(0));
        Assert.Contains(texts, t => t.Text == "Button #Updated"); Assert.Equal(.8, meters[0].Value);
        Assert.Equal(0, strip.Children[0].RenderSize.Width); Assert.Equal(strip.ActualWidth, strip.Children[3].RenderSize.Width, 2);
        Assert.True(container.RenderSize.Width <= list.ActualWidth + .1);
    }

    [Fact]
    public void TimelineVirtualizesReusesRowsAndRestoresTriggersAfterReload()
    {
        var source = new ObservableCollection<DevToolsLayoutInvalidationItem>(Enumerable.Range(0, 120).Select(i => new DevToolsLayoutInvalidationItem { TimeText = "12:34:56.789", Kind = "Measure", ElementName = $"Grid #{i}", SourceText = "Target.Refresh" }));
        var view = new DevToolsLayoutProfiler { InvalidationSource = source, InvalidationCount = 512 };
        Layout(view);
        var list = Part<ListBox>(view, "PART_InvalidationList");
        Assert.True(list.ActualWidth > view.ActualWidth - 80);
        Assert.Equal("512", Part<DevToolsTag>(view, "PART_InvalidationCount").Text);
        var realized = Enumerable.Range(0, 120).Select(list.ItemContainerGenerator.ContainerFromIndex).OfType<ListBoxItem>().ToArray();
        Assert.InRange(realized.Length, 1, 90);
        var first = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(0));
        var tag = Assert.Single(Descendants<DevToolsTag>(first));
        Assert.Equal("Measure", tag.Text);
        source[0].Kind = "Visual"; Layout(view);
        Assert.Same(first, list.ItemContainerGenerator.ContainerFromIndex(0)); Assert.Equal("Visual", tag.Text);
        Assert.Equal(Color.FromRgb(0xF2, 0x65, 0x5C), Assert.IsType<SolidColorBrush>(tag.Foreground).Color);
        var root = Assert.IsAssignableFrom<FrameworkElement>(Descendants<ContentPresenter>(first).First().GetVisualChild(0));
        root.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, root));
        source[0].Kind = "Arrange";
        root.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, root));
        Assert.Equal(Color.FromRgb(0xED, 0xA6, 0x3C), Assert.IsType<SolidColorBrush>(tag.Foreground).Color);
        root.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent, root));
        root.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, root));
        Assert.Equal(Color.FromRgb(0xED, 0xA6, 0x3C), Assert.IsType<SolidColorBrush>(tag.Foreground).Color);
    }

    [Fact]
    public void PresentationSnapshotsKeepWeakTargetsAndNormalizeZeroAndSmallValues()
    {
        var target = new Button { Name = "Sample" };
        var stats = (LayoutDiagnostics.ElementStats)Activator.CreateInstance(typeof(LayoutDiagnostics.ElementStats), BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { target }, null)!;
        var item = new DevToolsLayoutStatsItem(); item.Update(stats, 1, DevToolsLayoutSortMode.Measure, 1000);
        Assert.Equal("Button  #Sample", item.ElementName); Assert.Equal("0 ×   —", item.MeasureText); Assert.Equal(1, item.TrailingShare);
        typeof(LayoutDiagnostics.ElementStats).GetMethod("RecordMeasure", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(stats, new object[] { 1d });
        typeof(LayoutDiagnostics.ElementStats).GetMethod("RecordInvalidation", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(stats, new object[] { LayoutDiagnostics.InvalidationKind.Visual });
        item.Update(stats, 2, DevToolsLayoutSortMode.Measure, 1000);
        Assert.Equal(.02, item.MeasureFraction); Assert.Equal(1, item.VisualShare); Assert.Equal(0, item.TrailingShare);
        item.Update(stats, 2, DevToolsLayoutSortMode.Invalidations, 4);
        Assert.Equal(.25, item.MeasureFraction); Assert.Equal(.25, item.VisualShare); Assert.Equal(.75, item.TrailingShare);
        Assert.True(item.IsInvalidationHighlighted); Assert.False(item.IsMeasureHighlighted);
        stats.ElementRef.SetTarget(null!); Assert.False(item.Source!.ElementRef.TryGetTarget(out _));
    }

    [Fact]
    public void CustomItemTemplatesAndExplicitAutomationNamesAreRespected()
    {
        var statsTemplate = new DataTemplate(); statsTemplate.SetVisualTree(() => new TextBlock { Text = "Custom cost row" });
        var invTemplate = new DataTemplate(); invTemplate.SetVisualTree(() => new TextBlock { Text = "Custom event row" });
        var view = new DevToolsLayoutProfiler { StatsSource = new[] { new DevToolsLayoutStatsItem() }, InvalidationSource = new[] { new DevToolsLayoutInvalidationItem() }, StatsItemTemplate = statsTemplate, InvalidationItemTemplate = invTemplate };
        Layout(view);
        Assert.Contains(Descendants<TextBlock>(view), t => t.Text == "Custom cost row");
        Assert.Contains(Descendants<TextBlock>(view), t => t.Text == "Custom event row");
        var peer = UIElementAutomationPeer.CreatePeerForElement(view)!;
        Assert.Equal(AutomationControlType.Group, peer.GetAutomationControlType()); Assert.Equal("Layout profiler", peer.GetName());
        AutomationProperties.SetName(view, "Project layout"); Assert.Equal("Project layout", peer.GetName());
    }

    [Fact]
    public void ExcludedItemHostsCoverContainerConstructionBeforeAttachment()
    {
        var ignored = new ProbeListBox(); DiagnosticsScope.ExcludeRoot(ignored);
        ignored.Items.Add("Snapshot"); Layout(ignored);
        Assert.NotEmpty(ignored.CreationFlags); Assert.All(ignored.CreationFlags, Assert.True);
        var ordinary = new ProbeListBox(); ordinary.Items.Add("Target"); Layout(ordinary);
        Assert.NotEmpty(ordinary.CreationFlags); Assert.All(ordinary.CreationFlags, Assert.False);
    }

    [Fact]
    public void ExcludedDataTemplatesCannotEvictTargetEventsFromTheBoundedDiagnosticsQueue()
    {
        bool wasRecording = LayoutDiagnostics.IsRecording, capturedStacks = LayoutDiagnostics.CaptureStackTraces;
        LayoutDiagnostics.StopRecording(); LayoutDiagnostics.Reset(); LayoutDiagnostics.CaptureStackTraces = false;
        var target = new TextBlock { Text = "Target" };
        var template = new DataTemplate();
        template.SetVisualTree(() =>
        {
            var panel = new StackPanel();
            for (int i = 0; i < 520; i++)
            {
                var text = new TextBlock { Text = "Excluded template child" };
                NotifyInvalidation(text); panel.Children.Add(text);
            }
            return panel;
        });
        var presenter = new ContentPresenter { ContentTemplate = template };
        DiagnosticsScope.ExcludeRoot(presenter);
        try
        {
            LayoutDiagnostics.StartRecording(); NotifyInvalidation(target);
            presenter.Content = new object(); Layout(presenter);
            Assert.All(Descendants<TextBlock>(presenter), text => Assert.True(DiagnosticsScope.ShouldIgnore(text)));
            var events = LayoutDiagnostics.SnapshotInvalidations();
            Assert.Contains(events, entry => entry.ElementRef.TryGetTarget(out var element) && ReferenceEquals(element, target));
            Assert.All(events, entry => { Assert.True(entry.ElementRef.TryGetTarget(out var element)); Assert.Same(target, element); });
        }
        finally
        {
            LayoutDiagnostics.StopRecording(); LayoutDiagnostics.Reset(); LayoutDiagnostics.CaptureStackTraces = capturedStacks;
            if (wasRecording) LayoutDiagnostics.StartRecording();
        }
    }

    private static void NotifyInvalidation(UIElement element) => typeof(LayoutDiagnostics).GetMethod("NotifyInvalidation", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { element, LayoutDiagnostics.InvalidationKind.Visual });
    [Fact]
    public void TooltipCreationUsesTheExcludedOwnerBeforeItsDelayTimerRuns()
    {
        var owner = new Border(); DiagnosticsScope.ExcludeRoot(owner);
        try
        {
            ToolTipService.ShowToolTip(owner, "Full metric value", new Point(20, 20));
            var tip = Assert.IsType<ToolTip>(typeof(ToolTipService).GetField("_currentToolTip", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
            Assert.True(DiagnosticsScope.ShouldIgnore(tip));
            Assert.True(DiagnosticsScope.ShouldIgnore(Assert.IsType<TextBlock>(tip.Content)));
            Assert.Same(owner, tip.PlacementTarget);
        }
        finally { ToolTipService.HideToolTip(owner); }
    }

    private sealed class ProbeListBox : ListBox
    {
        public List<bool> CreationFlags { get; } = new();
        protected override FrameworkElement GetContainerForItem(object item) => new ProbeContainer(CreationFlags);
    }
    private sealed class ProbeContainer : ListBoxItem
    {
        public ProbeContainer(List<bool> flags) => flags.Add(DiagnosticsScope.ShouldIgnore(this));
    }

    private static T Part<T>(FrameworkElement owner, string name) where T : class => Assert.IsAssignableFrom<T>(owner.FindName(name));
    private static void Layout(UIElement control, double width = 1040, double height = 760) { control.Measure(new Size(width, height)); control.Arrange(new Rect(0, 0, width, height)); control.UpdateLayout(); }
    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual
    {
        for (int i = 0; i < root.VisualChildrenCount; i++) { var child = root.GetVisualChild(i); if (child == null) continue; if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    private sealed class ObservedCollection : System.Collections.ArrayList, INotifyCollectionChanged
    {
        private NotifyCollectionChangedEventHandler? _changed;
        public int Subscribers { get; private set; }
        public int ProfilerSubscribers => _changed?.GetInvocationList().Count(handler => handler.Method.DeclaringType?.FullName?.Contains(nameof(DevToolsLayoutProfiler), StringComparison.Ordinal) == true) ?? 0;
        public event NotifyCollectionChangedEventHandler? CollectionChanged { add { _changed += value; Subscribers++; } remove { _changed -= value; Subscribers--; } }
        public override int Add(object? value) { int index = base.Add(value); _changed?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, value, index)); return index; }
        public override void Clear() { base.Clear(); _changed?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)); }
    }
}
