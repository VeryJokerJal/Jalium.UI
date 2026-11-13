using System.Collections;
using System.Collections.ObjectModel;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Diagnostics;
using Jalium.UI.Media;
using PlotPath = Jalium.UI.Shapes.Path;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsFrameGraphTemplateTests
{
    public DevToolsFrameGraphTemplateTests() { Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application(); }

    [Fact]
    public void EmptyTemplateProvidesNamedVectorPathsAndReadableState()
    {
        Assert.True(Jalium.UI.Markup.XamlPrebuiltDictionaryRegistry.TryGet("Jalium.UI.Managed.Themes.Controls.DevToolsPerformance.jalxaml", out _));
        var graph = new DevToolsFrameGraph(); Layout(graph);
        Assert.NotNull(graph.Template); Assert.Equal(0, graph.SampleCount);
        Assert.Equal(Visibility.Visible, Part<TextBlock>(graph, "PART_Empty").Visibility);
        Assert.Equal(3, Geometry(graph.GridGeometry).GetPathGeometry()!.Figures.Count);
        Assert.Empty(Geometry(graph.BudgetGeometry).GetPathGeometry()!.Figures);
        Assert.Same(graph.LayoutGeometry, Part<PlotPath>(graph, "PART_Layout").Data);
        Assert.Equal("Frame timings", graph.GetAutomationPeer()!.GetName());
        AutomationProperties.SetName(graph, "Target frame graph"); Assert.Equal("Target frame graph", graph.GetAutomationPeer()!.GetName());
    }

    [Fact]
    public void SingleSampleStaysInItsRightmostSlotAndStacksSegmentsInOrder()
    {
        var graph = new DevToolsFrameGraph { SamplesSource = new[] { Sample(2, 3, 1) } }; Layout(graph);
        var plot = Part<Grid>(graph, "PART_Plot");
        var layout = Geometry(graph.LayoutGeometry).Bounds; var render = Geometry(graph.RenderGeometry).Bounds; var present = Geometry(graph.PresentGeometry).Bounds;
        Assert.Equal(1, graph.SampleCount); Assert.Equal(16, graph.MaximumMilliseconds);
        Assert.InRange(layout.X, plot.ActualWidth * .99, plot.ActualWidth);
        Assert.InRange(layout.Width, plot.ActualWidth / 301, plot.ActualWidth / 299);
        Assert.Equal(layout.Y, render.Bottom, 6); Assert.Equal(render.Y, present.Bottom, 6);
        Assert.Equal(plot.ActualHeight, layout.Bottom, 6);
        Assert.Equal(Visibility.Collapsed, Part<TextBlock>(graph, "PART_Empty").Visibility);
        Assert.Equal(1, Geometry(graph.BudgetGeometry).GetPathGeometry()!.Figures.Count);
    }

    [Fact]
    public void CapacityUsesOnlyNewestSamplesAndBrushChangesKeepTheirGeometry()
    {
        var graph = new DevToolsFrameGraph { DisplayCapacity = 3, SamplesSource = new[] { Sample(1000), Sample(1), Sample(2), Sample(3) } }; Layout(graph);
        Assert.Equal(3, graph.SampleCount); Assert.Equal(16, graph.MaximumMilliseconds);
        var geometry = graph.LayoutGeometry; var brush = new SolidColorBrush(Color.FromRgb(12, 34, 56)); graph.LayoutBrush = brush;
        Assert.Same(geometry, graph.LayoutGeometry); Assert.Same(brush, Part<PlotPath>(graph, "PART_Layout").Fill);
        graph.FrameBudgetMs = 8; Assert.Equal(8, graph.MaximumMilliseconds); Assert.Equal("8 ms", graph.ScaleText);
        double previousWidth = Geometry(graph.LayoutGeometry).Bounds.Width; Layout(graph, 480);
        Assert.True(Geometry(graph.LayoutGeometry).Bounds.Width < previousWidth);
        Assert.Throws<ArgumentException>(() => graph.DisplayCapacity = 0); Assert.Throws<ArgumentException>(() => graph.DisplayCapacity = 4097);
        Assert.Throws<ArgumentException>(() => graph.FrameBudgetMs = double.NaN); Assert.Throws<ArgumentException>(() => graph.FrameBudgetMs = 0);
        Assert.Throws<InvalidOperationException>(() => graph.SetValue(DevToolsFrameGraph.SampleCountProperty, 12));
    }

    [Fact]
    public void ObservableSourcesAndRetiredTemplatePartsDetach()
    {
        var old = new ObservableCollection<FrameHistory.Sample>(); var current = new ObservableCollection<FrameHistory.Sample> { Sample(2) };
        var graph = new DevToolsFrameGraph { SamplesSource = old }; Layout(graph);
        old.Add(Sample(1)); Assert.Equal(1, graph.SampleCount); graph.SamplesSource = current;
        var unchanged = graph.LayoutGeometry; old.Add(Sample(100)); Assert.Same(unchanged, graph.LayoutGeometry);
        var original = graph.Template; var retired = Part<Grid>(graph, "PART_Plot");
        var empty = new ControlTemplate(typeof(DevToolsFrameGraph)); empty.SetVisualTree(() => new Border()); graph.Template = empty; Layout(graph);
        current.Add(Sample(3)); Assert.Equal(2, graph.SampleCount); Assert.Null(graph.LayoutGeometry);
        graph.Template = original; Layout(graph); var geometry = graph.LayoutGeometry;
        Layout(retired, 300); Assert.Same(geometry, graph.LayoutGeometry);
        current.Clear(); Assert.Equal(0, graph.SampleCount); Assert.Equal(Visibility.Visible, Part<TextBlock>(graph, "PART_Empty").Visibility);
    }

    [Fact]
    public void InvalidDurationsAndOverflowCannotCreateNonfiniteCoordinates()
    {
        var graph = new DevToolsFrameGraph { SamplesSource = new[] { new FrameHistory.Sample(double.NaN, double.PositiveInfinity, -2, double.NaN, 0), Sample(double.MaxValue, double.MaxValue, double.MaxValue) } }; Layout(graph);
        Assert.Equal(double.MaxValue, graph.MaximumMilliseconds);
        foreach (var data in new[] { graph.LayoutGeometry, graph.RenderGeometry, graph.PresentGeometry, graph.GridGeometry, graph.BudgetGeometry })
        {
            var value = Geometry(data); if (value.GetPathGeometry()!.Figures.Count == 0) continue;
            var bounds = value.Bounds; Assert.True(double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) && double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height));
        }
    }

    [Fact]
    public void HugeIndexedSourcesRequireOnlyBoundedWork()
    {
        var graph = new DevToolsFrameGraph(); Layout(graph); var source = new IndexedSource(); graph.SamplesSource = source;
        Assert.Equal(FrameHistory.Capacity, graph.SampleCount); Assert.InRange(source.Reads, 1, 2 * FrameHistory.Capacity);
        Assert.Equal(source.Count - FrameHistory.Capacity, source.FirstRead);
    }

    private static FrameHistory.Sample Sample(double layout, double render = 0, double present = 0) => new(layout, render, present, layout + render + present, 0);
    private static StreamGeometry Geometry(Geometry? value) => Assert.IsType<StreamGeometry>(value);
    private static T Part<T>(Control graph, string name) where T : class => Assert.IsAssignableFrom<T>(graph.FindName(name));
    private static void Layout(UIElement element, double width = 1040) { for (int pass = 0; pass < 3; pass++) { element.InvalidateMeasure(); element.Measure(new Size(width, 200)); element.Arrange(new Rect(0, 0, width, 200)); element.UpdateLayout(); } }
    private sealed class IndexedSource : IReadOnlyList<FrameHistory.Sample>
    {
        public int Count => 1_000_000_000; public int Reads; public int FirstRead = -1;
        public FrameHistory.Sample this[int index] { get { if (FirstRead < 0) FirstRead = index; Reads++; return Sample(1); } }
        public IEnumerator<FrameHistory.Sample> GetEnumerator() => throw new InvalidOperationException("Graph must use bounded indexed access");
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
