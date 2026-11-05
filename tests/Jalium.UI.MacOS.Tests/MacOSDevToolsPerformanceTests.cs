using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Diagnostics;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Diagnostics;
using Jalium.UI.Data;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDevToolsPerformanceTests
{
    [Fact]
    public void ActualWorkspaceKeepsRowsAndGeometryWhenSnapshotsAreUnchangedAndStopsOnLeave()
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application();
        bool enabled = RenderDiagnostics.ApiStatsEnabled; object? owner = RenderDiagnostics.StatsOwner;
        var gpuField = Field(typeof(RenderDiagnostics), "s_latestGpuSnapshot"); var apiField = Field(typeof(RenderDiagnostics), "s_latestDrawApiStats");
        object? gpu = gpuField.GetValue(null), api = apiField.GetValue(null);
        var target = new Window { Title = "Performance target", Content = new Button { Content = "Target" } };
        var window = new DevToolsWindow(target);
        try
        {
            target.FrameHistory.Push(new(1, 2, 3, 6, 0)); target.FrameHistory.Push(new(2, 3, 4, 9, 0));
            RenderDiagnostics.PublishGpuSnapshot(10, 100, 1024);
            var workspace = Assert.IsType<DevToolsWorkspace>(window.Content); workspace.SelectedPage = DevToolsPage.Performance; Layout(window);
            var view = Assert.IsType<DevToolsPerformanceView>(workspace.PerformanceContent);
            Assert.True(RenderDiagnostics.ApiStatsEnabled); Assert.Same(target, RenderDiagnostics.StatsOwner); Assert.True(Timer(window)!.IsEnabled);
            Assert.Equal(2, view.SamplesSource!.Count); Assert.True(view.FrameSummary.HasFrames); Assert.False(view.IsEngineSwitchEnabled);
            RenderDiagnostics.DiscardApiStats(); RenderDiagnostics.RecordApi("DrawText", Stopwatch.Frequency / 1000); RenderDiagnostics.RecordApi("FillRectangle", Stopwatch.Frequency / 500); RenderDiagnostics.PublishAndResetApiStats();
            Refresh(window); Layout(window);
            var metrics = ((IEnumerable)view.MetricsSource!).Cast<DevToolsPerformanceMetric>().ToArray(); var calls = ((IEnumerable)view.ApiSource!).Cast<DevToolsDrawApiItem>().ToArray();
            Assert.Contains(metrics, item => item.Label == "Glyph atlas" && item.ValueText.StartsWith("10/100", StringComparison.Ordinal)); Assert.Equal(2, calls.Length);
            var source = view.SamplesSource; var graph = Assert.IsType<DevToolsFrameGraph>(view.FindName("PART_Graph")); var geometry = graph.LayoutGeometry;
            var list = Assert.IsType<ListBox>(view.FindName("PART_MetricsList")); var firstContainer = list.ItemContainerGenerator.ContainerFromIndex(0);
            Refresh(window); Layout(window);
            Assert.Same(source, view.SamplesSource); Assert.Same(geometry, graph.LayoutGeometry); Assert.Same(metrics[0], view.MetricsSource!.First()); Assert.Same(calls[0], view.ApiSource!.First()); Assert.Same(firstContainer, list.ItemContainerGenerator.ContainerFromIndex(0));
            target.FrameHistory.Push(new(3, 4, 5, 12, 0)); Refresh(window); Layout(window);
            Assert.Equal(3, view.SamplesSource!.Count); Assert.NotSame(source, view.SamplesSource); Assert.Same(metrics[0], view.MetricsSource!.First());
            workspace.SelectedPage = DevToolsPage.Inspector; Assert.False(Timer(window)!.IsEnabled); Assert.False(RenderDiagnostics.ApiStatsEnabled); Assert.Null(RenderDiagnostics.StatsOwner);
            workspace.SelectedPage = DevToolsPage.Performance; Assert.True(Timer(window)!.IsEnabled);
            window.CloseDevTools(); Assert.False(Timer(window)!.IsEnabled); Assert.False(RenderDiagnostics.ApiStatsEnabled);
        }
        finally
        {
            window.CloseDevTools(); target.Close(); RenderDiagnostics.DiscardApiStats(); gpuField.SetValue(null, gpu); apiField.SetValue(null, api);
            RenderDiagnostics.ApiStatsEnabled = enabled; RenderDiagnostics.StatsOwner = owner;
        }
    }

    [Fact]
    public void LeavingAnOlderDashboardDoesNotDisableAnotherDiagnosticsOwner()
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application();
        bool enabled = RenderDiagnostics.ApiStatsEnabled; object? owner = RenderDiagnostics.StatsOwner;
        var target = new Window(); var window = new DevToolsWindow(target);
        try
        {
            var workspace = Assert.IsType<DevToolsWorkspace>(window.Content); workspace.SelectedPage = DevToolsPage.Performance;
            var currentOwner = new object(); RenderDiagnostics.StatsOwner = currentOwner; RenderDiagnostics.ApiStatsEnabled = true;
            workspace.SelectedPage = DevToolsPage.Inspector;
            Assert.False(Timer(window)!.IsEnabled); Assert.Same(currentOwner, RenderDiagnostics.StatsOwner); Assert.True(RenderDiagnostics.ApiStatsEnabled);
            window.CloseDevTools(); Assert.Same(currentOwner, RenderDiagnostics.StatsOwner); Assert.True(RenderDiagnostics.ApiStatsEnabled);
        }
        finally { window.CloseDevTools(); target.Close(); RenderDiagnostics.ApiStatsEnabled = enabled; RenderDiagnostics.StatsOwner = owner; }
    }

    [Fact]
    public void RepeatedApiReorderingKeepsBindingSubscriptionsBounded()
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application();
        var rows = new ObservableCollection<DevToolsDrawApiItem>(Enumerable.Range(0, 9).Select(i => new DevToolsDrawApiItem { Name = "API " + i, Fraction = .5 }));
        var view = new DevToolsPerformanceView { ApiSource = rows }; var window = new DisplayedTestWindow { Content = view };
        int Subscriptions(DependencyObject value) => DependencyObjectSubscriptionProbe.Count(value, "PropertyChangedInternal");
        try
        {
            window.SetLoadedState(true); Layout(window); int before = rows.Max(Subscriptions);
            for (int round = 0; round < 40; round++)
            {
                rows.Move(0, rows.Count - 1);
                foreach (var row in rows) { row.Fraction = (round % 10) / 10.0; row.TotalText = round.ToString(); }
                Layout(window);
            }
            // A logical test window never presents. Consume its frame invalidations
            // as a successful native frame would, so dirty-region bookkeeping does
            // not deliberately retain every retired visual during this GC check.
            typeof(Window).GetMethod("SwapDirtyForFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
            typeof(Window).GetMethod("DiscardFrameDirty", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [Field(typeof(Window), "_fullInvalidationSeq").GetValue(window)]);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            foreach (var row in rows) row.Fraction = .25;
            int after = rows.Max(Subscriptions);
            Assert.True(after <= before + 16, $"API model subscriptions grew from {before} to {after} across 40 reorders");
        }
        finally { window.SetLoadedState(false); window.Close(); }
    }
    private static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static DispatcherTimer? Timer(DevToolsWindow window) => (DispatcherTimer?)Field(typeof(DevToolsWindow), "_perfRefreshTimer").GetValue(window);
    private static void Refresh(DevToolsWindow window) => typeof(DevToolsWindow).GetMethod("RefreshPerfStats", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
    private static void Layout(UIElement element) { for (int i = 0; i < 3; i++) { element.InvalidateMeasure(); element.Measure(new Size(1040, 860)); element.Arrange(new Rect(0, 0, 1040, 860)); element.UpdateLayout(); } }
}
