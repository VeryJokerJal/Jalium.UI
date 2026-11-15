using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Diagnostics;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDevToolsLayoutProfilerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeMousePressInvokesARealizedRowActionOnItsFirstClick(bool scrolled)
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application();
        Mouse.Capture(null); Mouse.UpdateState(default, null, MouseButtonStates.AllReleased);
        var items = Enumerable.Range(1, 30).Select(i => new DevToolsLayoutStatsItem { Rank = i, ElementName = $"Target{i}", MeasureText = "2 × 240 µs", ArrangeText = "1 × 120 µs" }).ToArray();
        int count = 0; object? parameter = null;
        var view = new DevToolsLayoutProfiler { StatsSource = items, RevealCommand = new RelayCommand(value => { count++; parameter = value; }) };
        var window = new DisplayedTestWindow { Width = 1040, Height = 860, TitleBarStyle = WindowTitleBarStyle.Native, Content = view };
        try
        {
            Layout(window);
            var list = Part<ListBox>(view, "PART_StatsList");
            int index = scrolled ? 28 : 0;
            if (scrolled)
            {
                // The headless display has no compositor layout tick. Apply the
                // panel's queued viewport measure before dispatching native input.
                var panel = Descendants<VirtualizingStackPanel>(list).Single();
                panel.SetVerticalOffset(double.MaxValue);
                var viewport = new Size(panel.ViewportWidth, panel.ViewportHeight);
                for (int pass = 0; pass < 3; pass++)
                {
                    panel.InvalidateMeasure(); panel.Measure(viewport); panel.Arrange(new Rect(new Point(), viewport));
                }
                Assert.True(list.ItemContainerGenerator.ContainerFromIndex(index) != null,
                    $"offset={panel.VerticalOffset}; extent={panel.ExtentHeight}; viewport={panel.ViewportHeight}; realized={string.Join(',', Enumerable.Range(0, 30).Where(i => list.ItemContainerGenerator.ContainerFromIndex(i) != null))}");
            }
            var container = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(index));
            var action = Descendants<DevToolsActionButton>(container).Single(button => button.Label == "Reveal");
            var point = action.TranslatePoint(new Point(action.ActualWidth / 2, action.ActualHeight / 2), window);
            var hitPath = new List<Visual>();
            for (var current = window.HitTest(point)?.VisualHit as Visual; current != null; current = current.VisualParent) hitPath.Add(current);
            Assert.Contains(action, hitPath);
            foreach (var type in new[] { PlatformEventType.MouseDown, PlatformEventType.MouseUp })
            {
                var input = new PlatformEvent { Type = type, MouseX = (int)Math.Round(point.X * window.DpiScale), MouseY = (int)Math.Round(point.Y * window.DpiScale), Button = 0, ClickCount = 1, HasMouseButtonStates = true, MouseButtons = type == PlatformEventType.MouseDown ? 1u : 0u };
                typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { input });
            }
            Assert.Equal(1, count); Assert.Same(items[index], parameter);
        }
        finally { Mouse.Capture(null); Keyboard.ClearFocus(); window.Close(); }
    }

    [Fact]
    public void ActualProfilerKeepsTopThirtyReusesModelsAndRevealsTheirCurrentTarget()
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        _ = Application.Current ?? new Application();
        bool wasRecording = LayoutDiagnostics.IsRecording;
        bool capturedStacks = LayoutDiagnostics.CaptureStackTraces;
        var previousEnricher = LayoutDiagnostics.StackCaptureEnricher;
        LayoutDiagnostics.StopRecording(); LayoutDiagnostics.Reset();
        LayoutDiagnostics.CaptureStackTraces = false;
        var panel = new StackPanel();
        var targets = Enumerable.Range(0, 40).Select(i => new Border { Name = $"Target{i}", Width = 200, Height = 10 }).ToArray();
        foreach (var target in targets) panel.Children.Add(target);
        var host = new Window { Title = "Layout target", Content = panel };
        var window = new DevToolsWindow(host);
        try
        {
            typeof(Window).GetMethod("SetMacOSVisibilityForDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { Visibility.Visible });
            var workspace = Assert.IsType<DevToolsWorkspace>(window.Content);
            workspace.SelectedPage = DevToolsPage.Layout;
            var view = Assert.IsType<DevToolsLayoutProfiler>(workspace.LayoutContent);
            Assert.NotNull(LayoutDiagnostics.StackCaptureEnricher);
            Layout(window);
            Part<DevToolsActionButton>(view, "PART_RecordButton").PerformClick();
            Assert.True(LayoutDiagnostics.IsRecording); Assert.True(view.IsRecording);
            for (int i = 0; i < targets.Length; i++)
            {
                Notify("NotifyMeasure", targets[i], (double)i + 1);
                Notify("NotifyArrange", targets[i], (double)(40 - i));
                for (int j = 0; j < 4; j++) Notify("NotifyInvalidation", targets[i], LayoutDiagnostics.InvalidationKind.Measure);
            }
            Refresh(window); Layout(window);
            var stats = ((IEnumerable)view.StatsSource!).Cast<DevToolsLayoutStatsItem>().ToArray();
            var invalidations = ((IEnumerable)view.InvalidationSource!).Cast<DevToolsLayoutInvalidationItem>().ToArray();
            Assert.Equal(30, stats.Length); Assert.Equal(120, invalidations.Length); Assert.Equal(160, view.InvalidationCount);
            Assert.Equal("Border  #Target39", stats[0].ElementName); Assert.Equal("Border  #Target39", invalidations[0].ElementName);
            Assert.Equal("+10 more elements tracked", view.FooterText);
            var list = Part<ListBox>(view, "PART_StatsList");
            var firstContainer = list.ItemContainerGenerator.ContainerFromIndex(0);
            Part<DevToolsActionButton>(view, "PART_SortButton").PerformClick();
            Assert.Equal(DevToolsLayoutSortMode.Arrange, view.SortMode);
            Layout(window);
            Assert.Same(stats[0], ((IEnumerable)view.StatsSource!).Cast<DevToolsLayoutStatsItem>().First());
            Assert.Same(firstContainer, list.ItemContainerGenerator.ContainerFromIndex(0));
            Assert.Equal("Border  #Target0", stats[0].ElementName);
            var snapshot = LayoutDiagnostics.SnapshotStats();
            Assert.True(snapshot.Count == 40, string.Join("\n", snapshot.Where(sample => !sample.ElementRef.TryGetTarget(out var element) || !targets.Contains(element)).Select(sample =>
            {
                sample.ElementRef.TryGetTarget(out var element);
                var parents = new List<string>();
                for (Jalium.UI.Media.Visual? parent = element; parent != null; parent = parent.VisualParent) parents.Add(parent.GetType().Name);
                return $"Unexpected sample {sample.TypeName} #{sample.ElementName}; ignored={element != null && DiagnosticsScope.ShouldIgnore(element)}; chain={string.Join('/', parents)}";
            })));
            Assert.All(snapshot, sample => { Assert.True(sample.ElementRef.TryGetTarget(out var element)); Assert.Contains(element, targets); });
            var retainedEvent = LayoutDiagnostics.SnapshotInvalidations().First();
            var viewportPanel = Descendants<VirtualizingStackPanel>(list).Single();
            viewportPanel.SetVerticalOffset(double.MaxValue);
            var viewportSize = new Size(viewportPanel.ViewportWidth, viewportPanel.ViewportHeight);
            for (int pass = 0; pass < 3; pass++) { viewportPanel.InvalidateMeasure(); viewportPanel.Measure(viewportSize); viewportPanel.Arrange(new Rect(new Point(), viewportSize)); }
            var lastRow = Assert.IsType<ListBoxItem>(list.ItemContainerGenerator.ContainerFromIndex(28));
            var reveal = Descendants<DevToolsActionButton>(lastRow).Single(button => button.Label == "Reveal");
            var position = reveal.TranslatePoint(new Point(reveal.ActualWidth / 2, reveal.ActualHeight / 2), window);
            foreach (var type in new[] { PlatformEventType.MouseDown, PlatformEventType.MouseUp })
            {
                var input = new PlatformEvent { Type = type, MouseX = (int)Math.Round(position.X * window.DpiScale), MouseY = (int)Math.Round(position.Y * window.DpiScale), Button = 0, ClickCount = 1, HasMouseButtonStates = true, MouseButtons = type == PlatformEventType.MouseDown ? 1u : 0u };
                typeof(Window).GetMethod("OnPlatformEvent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { input });
            }
            Assert.Contains(retainedEvent, LayoutDiagnostics.SnapshotInvalidations());
            Assert.Equal(DevToolsPage.Inspector, workspace.SelectedPage);
            Assert.Same(targets[28], typeof(DevToolsWindow).GetField("_selectedVisual", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window));
            workspace.SelectedPage = DevToolsPage.Layout; Layout(window);
            Part<DevToolsActionButton>(view, "PART_StackButton").PerformClick();
            Assert.True(LayoutDiagnostics.CaptureStackTraces); Assert.True(view.IsStackCaptureEnabled);
            Notify("NotifyInvalidation", targets[0], LayoutDiagnostics.InvalidationKind.Visual); Refresh(window);
            Assert.NotEmpty(((IEnumerable)view.InvalidationSource!).Cast<DevToolsLayoutInvalidationItem>().First().SourceText);
            Part<DevToolsActionButton>(view, "PART_RecordButton").PerformClick(); Assert.False(LayoutDiagnostics.IsRecording);
            Part<DevToolsActionButton>(view, "PART_ResetButton").PerformClick();
            Assert.False(view.HasStats); Assert.False(view.HasInvalidations); Assert.Equal(0, view.InvalidationCount); Assert.Equal("", view.FooterText);
        }
        finally
        {
            window.CloseDevTools(); host.Close();
            LayoutDiagnostics.StopRecording(); LayoutDiagnostics.Reset(); LayoutDiagnostics.CaptureStackTraces = capturedStacks;
            LayoutDiagnostics.StackCaptureEnricher = previousEnricher;
            if (wasRecording) LayoutDiagnostics.StartRecording();
        }
    }

    private static void Notify(string name, UIElement target, object value) => typeof(LayoutDiagnostics).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { target, value });
    private static void Refresh(DevToolsWindow window)
    {
        foreach (var name in new[] { "RefreshLayoutStats", "RefreshInvalidations" }) typeof(DevToolsWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
    }
    private static T Part<T>(FrameworkElement owner, string name) where T : class => Assert.IsAssignableFrom<T>(owner.FindName(name));
    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual
    {
        for (int i = 0; i < root.VisualChildrenCount; i++) if (root.GetVisualChild(i) is { } child) { if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    private static void Layout(UIElement control) { control.Measure(new Size(1040, 860)); control.Arrange(new Rect(0, 0, 1040, 860)); control.UpdateLayout(); }
}
