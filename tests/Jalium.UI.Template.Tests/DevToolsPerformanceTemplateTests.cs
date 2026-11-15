using System.Collections.ObjectModel;
using System.Windows.Input;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsPerformanceTemplateTests
{
    public DevToolsPerformanceTemplateTests() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }

    [Fact]
    public void DefaultDashboardExposesEmptyStatesAndItsEngineCommands()
    {
        var view = new DevToolsPerformanceView(); Layout(view);
        Assert.NotNull(view.Template); Assert.False(view.HasMetrics); Assert.False(view.HasApiItems);
        Assert.Equal(Visibility.Visible, Part<TextBlock>(view, "PART_NoMetrics").Visibility); Assert.Equal(Visibility.Visible, Part<TextBlock>(view, "PART_NoApi").Visibility);
        Assert.Equal("—", Part<TextBlock>(view, "PART_Rate").Text); Assert.True(Part<DevToolsActionButton>(view, "PART_Auto").IsActive);
        Assert.Equal("Selected", Part<DevToolsActionButton>(view, "PART_Auto").GetAutomationPeer()!.GetItemStatus());
        Assert.NotNull(Part<DevToolsFrameGraph>(view, "PART_Graph").Template);
        view.SetValue(DevToolsPerformanceView.FrameSummaryProperty, null); Assert.False(view.FrameSummary.HasFrames);
        AutomationProperties.SetName(view, "Target profiler"); Assert.Equal("Target profiler", view.GetAutomationPeer()!.GetName());
    }

    [Theory]
    [InlineData(320, 420)] [InlineData(480, 760)] [InlineData(1040, 760)]
    public void ResponsivePanelsAndTheirListsFitTheViewport(double width, double height)
    {
        var view = Filled(); Layout(view, width, height);
        var metrics = Part<DevToolsSurface>(view, "PART_MetricsSurface"); var api = Part<DevToolsSurface>(view, "PART_ApiSurface");
        var metricsEnd = metrics.TranslatePoint(new Point(metrics.ActualWidth, metrics.ActualHeight), view); var apiStart = api.TranslatePoint(new Point(), view);
        if (width <= 780) Assert.True(apiStart.Y >= metricsEnd.Y); else Assert.True(apiStart.X >= metricsEnd.X);
        foreach (string name in new[] { "PART_MetricsSurface", "PART_ApiSurface", "PART_MetricsList", "PART_ApiList", "PART_Graph", "PART_Auto", "PART_Impeller" })
        {
            var element = Part<FrameworkElement>(view, name); var end = element.TranslatePoint(new Point(element.ActualWidth, element.ActualHeight), view);
            Assert.True(element.ActualWidth > 0 && element.ActualHeight > 0, name);
            Assert.InRange(end.X, 0, width + .5); Assert.InRange(end.Y, 0, height + .5);
        }
        var list = Part<ListBox>(view, "PART_ApiList"); Assert.InRange(list.ActualWidth, api.ActualWidth - 27, api.ActualWidth - 19);
    }

    [Fact]
    public void SplitterSizesAreReleasedAcrossResponsiveAxes()
    {
        var view = Filled(); Layout(view, 480); var body = Part<Grid>(view, "PART_Body");
        body.RowDefinitions[0].Height = new GridLength(100); body.RowDefinitions[2].Height = new GridLength(280);
        Layout(view, 480); Layout(view, 1040);
        Assert.True(Part<DevToolsSurface>(view, "PART_MetricsSurface").ActualHeight >= body.ActualHeight - .5);
        Assert.True(Part<DevToolsSurface>(view, "PART_ApiSurface").ActualHeight >= body.ActualHeight - .5);
        body.ColumnDefinitions[0].Width = new GridLength(180); body.ColumnDefinitions[2].Width = new GridLength(700);
        Layout(view, 1040); Layout(view, 480);
        Assert.True(Part<DevToolsSurface>(view, "PART_MetricsSurface").ActualWidth >= body.ActualWidth - .5);
        Assert.True(Part<DevToolsSurface>(view, "PART_ApiSurface").ActualWidth >= body.ActualWidth - .5);
    }

    [Fact]
    public void EngineSelectionKeepsBindingsRejectsReentryAndReportsErrors()
    {
        var source = new DevToolsPerformanceView(); var view = new DevToolsPerformanceView();
        view.SetBinding(DevToolsPerformanceView.SelectedEngineProperty, new Binding(nameof(DevToolsPerformanceView.SelectedEngine)) { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        int calls = 0; view.EngineCommand = new RelayCommand(value => { calls++; Assert.Equal(RenderingEngine.Vello, value); Assert.True(view.IsSwitching); Assert.False(view.RequestEngine(RenderingEngine.Auto)); });
        Layout(view); Part<DevToolsActionButton>(view, "PART_Vello").PerformClick();
        Assert.Equal(1, calls); Assert.Equal(RenderingEngine.Vello, source.SelectedEngine); Assert.True(Part<DevToolsActionButton>(view, "PART_Vello").IsActive); Assert.False(Part<DevToolsActionButton>(view, "PART_Auto").IsActive);
        Assert.Equal("Selected", Part<DevToolsActionButton>(view, "PART_Vello").GetAutomationPeer()!.GetItemStatus()); Assert.Equal("Not selected", Part<DevToolsActionButton>(view, "PART_Auto").GetAutomationPeer()!.GetItemStatus());
        view.EngineCommand = new RelayCommand(() => throw new InvalidOperationException("Renderer unavailable"));
        Assert.False(view.RequestEngine(RenderingEngine.Impeller)); Assert.Equal(RenderingEngine.Vello, view.SelectedEngine); Assert.False(view.IsSwitching);
        Assert.Equal("Renderer unavailable", view.EngineErrorText); Assert.Equal(Visibility.Visible, Part<DevToolsTag>(view, "PART_EngineError").Visibility);
        view.IsEngineSwitchEnabled = false; Assert.False(view.AutoEngineCommand.CanExecute(null)); Assert.False(view.RequestEngine(RenderingEngine.Auto));
        view.IsEngineSwitchEnabled = true; view.IsEnabled = false; Assert.False(view.RequestEngine(RenderingEngine.Auto));
    }

    [Fact]
    public void CommandReplacementDetachesObserversAndRefreshesButtonAvailability()
    {
        var old = new ObservedCommand(); var current = new ObservedCommand(); var view = new DevToolsPerformanceView { EngineCommand = old }; Layout(view);
        Assert.Equal(1, old.Observers); old.Available = false; old.Notify(); Assert.False(Part<DevToolsActionButton>(view, "PART_Vello").IsEnabled);
        view.EngineCommand = current; Assert.Equal(0, old.Observers); Assert.Equal(1, current.Observers); Assert.True(Part<DevToolsActionButton>(view, "PART_Vello").IsEnabled);
        view.EngineCommand = null; Assert.Equal(0, current.Observers);
    }

    [Fact]
    public void DataTemplatesAndSummaryStatesUpdateWithoutReplacingTheView()
    {
        var metrics = new ObservableCollection<DevToolsPerformanceMetric>(); var view = new DevToolsPerformanceView { MetricsSource = metrics }; Layout(view);
        var metric = new DevToolsPerformanceMetric { Kind = DevToolsPerformanceMetricKind.Meter, Label = "Cache", ValueText = "1 / 2", Fraction = .5, Tone = DevToolsPerformanceTone.Warning };
        metrics.Add(metric); Layout(view); Assert.True(view.HasMetrics);
        var meter = Assert.Single(Descendants<DevToolsMeterRow>(view), element => element.Visibility == Visibility.Visible);
        Assert.Equal(.5, meter.Fraction); Assert.Equal(Color.FromRgb(0xED, 0xA6, 0x3C), Assert.IsType<SolidColorBrush>(meter.Foreground).Color);
        metric.ValueText = "2 / 2"; metric.Fraction = 1; Assert.Equal("2 / 2", meter.ValueText); Assert.Equal(1, meter.Fraction);
        view.FrameSummary = new(true, false, 60, 2, 3, 0, 3, DevToolsPerformanceTone.Success);
        Assert.Equal("60", Part<TextBlock>(view, "PART_Rate").Text); Assert.Equal(Color.FromRgb(0x56, 0xD9, 0x8D), Assert.IsType<SolidColorBrush>(Part<TextBlock>(view, "PART_Rate").Foreground).Color);
        view.FrameSummary = new(true, true, 0, 0, 0, 3, 2, DevToolsPerformanceTone.Muted); Assert.Contains("Idle 3.0", Part<TextBlock>(view, "PART_FrameDetail").Text);
        view.HasAdapterWarning = true; Assert.Equal(Color.FromRgb(0xF2, 0x65, 0x5C), Assert.IsType<SolidColorBrush>(Part<TextBlock>(view, "PART_Adapter").Foreground).Color);
        var custom = new DataTemplate(); custom.SetVisualTree(() => new TextBlock { Text = "Custom metric" }); view.MetricTemplate = custom; Layout(view);
        Assert.Contains(Descendants<TextBlock>(view), text => text.Text == "Custom metric");
    }

    [Fact]
    public void RetemplatingDetachesListsAndAllowsOptionalParts()
    {
        var metrics = new ObservableCollection<DevToolsPerformanceMetric> { new() { Label = "One", ValueText = "1" } };
        var view = new DevToolsPerformanceView { MetricsSource = metrics }; Layout(view); var original = view.Template;
        var oldList = Part<ListBox>(view, "PART_MetricsList"); var blank = new ControlTemplate(typeof(DevToolsPerformanceView)); blank.SetVisualTree(() => new Border());
        view.Template = blank; Layout(view); Assert.Null(oldList.ItemsSource); Assert.Null(view.FindName("PART_ApiList"));
        metrics.Clear(); Assert.False(view.HasMetrics); Assert.True(view.RequestEngine(RenderingEngine.Impeller));
        metrics.Add(new() { Label = "New" }); view.Template = original; Layout(view); Assert.True(view.HasMetrics); Assert.Same(metrics, Part<ListBox>(view, "PART_MetricsList").ItemsSource);
        Assert.Throws<InvalidOperationException>(() => view.SetValue(DevToolsPerformanceView.HasMetricsProperty, true));
        Assert.Throws<ArgumentException>(() => view.CompactWidth = double.NaN);
    }

    [Fact]
    public void InitialHeaderKeepsItsLabelsWhenTemplateBindingsRefresh()
    {
        var view = new DevToolsPerformanceView(); Layout(view);
        var header = Part<DevToolsDrawApiRow>(view, "PART_ApiHeader");
        Assert.True(header.IsHeader);
        Assert.Equal("API", Part<TextBlock>(header, "PART_Name").Text);
        view.DataContext = new object(); Layout(view);
        Assert.Equal("API", Part<TextBlock>(header, "PART_Name").Text);
        Assert.Equal("CALLS", Part<TextBlock>(header, "PART_Calls").Text);
        var item = Api("Updated operation"); header.Item = item; item.Name = "Changed while header";
        Assert.Equal("API", Part<TextBlock>(header, "PART_Name").Text);
        header.IsHeader = false;
        Assert.Equal(item.Name, Part<TextBlock>(header, "PART_Name").Text);
    }

    [Fact]
    public void DrawApiRowsRestoreTheirWideColumnsAndHeaderLabels()
    {
        var item = Api("A long draw API operation name"); var row = new DevToolsDrawApiRow { Item = item }; Layout(row, 400, 80);
        Assert.Equal(4, Grid.GetColumnSpan(Part<TextBlock>(row, "PART_Name"))); Assert.Equal(1, Grid.GetRow(Part<TextBlock>(row, "PART_Calls")));
        Layout(row, 800, 80); Assert.Equal(1, Grid.GetColumnSpan(Part<TextBlock>(row, "PART_Name"))); Assert.Equal(0, Grid.GetRow(Part<TextBlock>(row, "PART_Calls")));
        row.IsHeader = true; Assert.Equal("API", Part<TextBlock>(row, "PART_Name").Text); Assert.Equal("CALLS", Part<TextBlock>(row, "PART_Calls").Text);
        row.IsHeader = false; Assert.Equal(item.Name, Part<TextBlock>(row, "PART_Name").Text);
        item.Tone = DevToolsPerformanceTone.Accent; Assert.Equal(Color.FromRgb(0xFF, 0xB2, 0x2E), Assert.IsType<SolidColorBrush>(Part<TextBlock>(row, "PART_Total").Foreground).Color);
        Assert.Contains(item.Name, row.GetAutomationPeer()!.GetName());
    }

    [Theory]
    [InlineData(480, true)] [InlineData(480, false)]
    [InlineData(1040, true)] [InlineData(1040, false)]
    public void ScrollRailsLeaveMetricValuesAndApiColumnsUnobscured(double width, bool overlay)
    {
        var view = new DevToolsPerformanceView
        {
            MetricsSource = Enumerable.Range(0, 200).Select(i => new DevToolsPerformanceMetric { Label = "Metric " + i, ValueText = i + " units" }).ToArray(),
            ApiSource = Enumerable.Range(0, 2000).Select(i => Api("API " + i)).ToArray(),
        };
        Layout(view, width, 860);
        var metrics = Part<ListBox>(view, "PART_MetricsList"); var api = Part<ListBox>(view, "PART_ApiList");
        foreach (var list in new[] { metrics, api })
            foreach (var scroll in Descendants<ScrollViewer>(list)) scroll.IsOverlayScrollBarEnabled = overlay;
        Layout(view, width, 860);
        var metricContainer = Assert.IsType<ListBoxItem>(metrics.ItemContainerGenerator.ContainerFromIndex(0));
        var readout = Descendants<DevToolsReadout>(metricContainer).First(control => control.Visibility == Visibility.Visible);
        var value = Part<TextBlock>(readout, "PART_Value");
        double valueRight = value.TranslatePoint(new Point(value.ActualWidth, 0), metrics).X;
        Assert.True(valueRight <= metrics.ActualWidth - 10, $"metric right={valueRight}; viewport={metrics.ActualWidth}");
        var apiContainer = Assert.IsType<ListBoxItem>(api.ItemContainerGenerator.ContainerFromIndex(0));
        var row = Descendants<DevToolsDrawApiRow>(apiContainer).Single();
        var average = Part<TextBlock>(row, "PART_Average");
        double averageRight = average.TranslatePoint(new Point(average.ActualWidth, 0), api).X;
        Assert.True(averageRight <= api.ActualWidth - 10, $"average right={averageRight}; viewport={api.ActualWidth}");
        var heading = Part<DevToolsDrawApiRow>(view, "PART_ApiHeader");
        var headingAverage = Part<TextBlock>(heading, "PART_Average");
        Assert.Equal(average.TranslatePoint(new Point(average.ActualWidth, 0), view).X,
            headingAverage.TranslatePoint(new Point(headingAverage.ActualWidth, 0), view).X, 2);
    }

    [Fact]
    public void ProportionalBarsUseThemeSegmentsAndIndexedRowsRemainVirtualized()
    {
        var bar = new DevToolsPerformanceBar { ItemsSource = new[] { new DevToolsPerformanceSegment(1, DevToolsPerformanceTone.Info), new DevToolsPerformanceSegment(3, DevToolsPerformanceTone.Accent) } }; Layout(bar, 400, 20);
        var first = Assert.IsAssignableFrom<FrameworkElement>(bar.ItemContainerGenerator.ContainerFromIndex(0)); var second = Assert.IsAssignableFrom<FrameworkElement>(bar.ItemContainerGenerator.ContainerFromIndex(1));
        Assert.Equal(100, first.ActualWidth, 3); Assert.Equal(300, second.ActualWidth, 3);
        Assert.Contains(Descendants<Border>(first), element => element.Background is SolidColorBrush brush && brush.Color == Color.FromRgb(0x5F, 0xB7, 0xCB));
        var rows = Enumerable.Range(0, 500).Select(i => Api("API " + i)).ToArray(); var view = Filled(); view.ApiSource = rows; Layout(view, 480);
        var list = Part<ListBox>(view, "PART_ApiList"); Assert.Equal(500, list.Items.Count);
        Assert.InRange(Enumerable.Range(0, rows.Length).Count(index => list.ItemContainerGenerator.ContainerFromIndex(index) != null), 1, 50);
        list.ScrollIntoView(rows[^1]); Layout(view, 480);
        var last = Assert.IsAssignableFrom<FrameworkElement>(list.ItemContainerGenerator.ContainerFromIndex(499)); double top = last.TranslatePoint(new Point(), list).Y;
        Assert.True(top >= -.5 && top + last.ActualHeight <= list.ActualHeight + .5, $"last={top}/{last.ActualHeight}; viewport={list.ActualHeight}");
    }

    private static DevToolsPerformanceView Filled() => new() { MetricsSource = new[] { new DevToolsPerformanceMetric { Label = "Frame", ValueText = "2 ms" } }, ApiSource = new[] { Api("DrawText") } };
    private static DevToolsDrawApiItem Api(string name) => new() { Name = name, CallsText = "12", TotalText = "1.20", AverageText = "100.0", Fraction = .5 };
    private static T Part<T>(Control view, string name) where T : class => Assert.IsAssignableFrom<T>(view.FindName(name));
    private static void Layout(UIElement element, double width = 1040, double height = 760) { for (int pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout(); } }
    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual { for (int i = 0; i < root.VisualChildrenCount; i++) if (root.GetVisualChild(i) is { } child) { if (child is T match) yield return match; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private sealed class ObservedCommand : ICommand
    {
        private EventHandler? _changed; public bool Available = true; public int Observers => _changed?.GetInvocationList().Length ?? 0;
        public event EventHandler? CanExecuteChanged { add => _changed += value; remove => _changed -= value; }
        public bool CanExecute(object? parameter) => Available; public void Execute(object? parameter) { } public void Notify() => _changed?.Invoke(this, EventArgs.Empty);
    }
}
