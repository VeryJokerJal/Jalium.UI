using Jalium.UI.Controls;
using Jalium.UI.Controls.Charts;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class FlowchartThemeTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void EdgeLabelsRenderWithTheirOwnForegroundAndBackground()
    {
        var flow = Create(); flow.EdgeLabelForeground = Brushes.Green; flow.EdgeLabelBackground = Brushes.Purple;
        Layout(flow); var ink = new Ink(); flow.Render(ink);
        Assert.Same(Brushes.Green, Assert.Single(ink.Text, text => text.Text == "ok").Foreground);
        Assert.Contains(ink.RoundedFills, brush => ReferenceEquals(brush, Brushes.Purple));
        Assert.IsType<Grid>(flow.GetVisualChild(0));
        Assert.Same(flow, Assert.IsType<ChartPlotView>(flow.FindName("PART_PlotView")).Owner);
    }

    [Fact]
    public void PaletteUsesScopedResourcesAndKeepsCallerOverrides()
    {
        var flow = Create(); flow.Resources["LayerFillColorDefaultBrush"] = Brushes.Green; Layout(flow);
        Assert.Same(Brushes.Green, flow.EdgeLabelBackground);
        flow.Resources["LayerFillColorDefaultBrush"] = Brushes.Blue; Layout(flow); Assert.Same(Brushes.Blue, flow.EdgeLabelBackground);
        flow.EdgeLabelBackground = Brushes.Purple; flow.Resources["LayerFillColorDefaultBrush"] = Brushes.Red; Layout(flow);
        Assert.Same(Brushes.Purple, flow.EdgeLabelBackground);
    }

    private static FlowchartDiagram Create()
    {
        var flow = new FlowchartDiagram(); flow.Resources.MergedDictionaries.Add(Defaults.Value);
        flow.Nodes.Add(new FlowchartNode("start", "Start")); flow.Nodes.Add(new FlowchartNode("end", "Done"));
        flow.Edges.Add(new FlowchartEdge("start", "end", "ok")); return flow;
    }
    private static void Layout(FrameworkElement element)
    {
        element.InvalidateMeasure(); element.Measure(new Size(400, 260)); element.Arrange(new Rect(0, 0, 400, 260)); element.UpdateLayout();
    }
    private sealed class Ink : DrawingContextAdapter
    {
        public List<FormattedText> Text { get; } = new();
        public List<Brush?> RoundedFills { get; } = new();
        public override void DrawText(FormattedText text, Point point) => Text.Add(text);
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) => RoundedFills.Add(brush);
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) { }
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) { }
        public override void DrawImage(ImageSource image, Rect rect) { }
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) { }
        public override void PushTransform(Transform transform) { }
        public override void PushClip(Geometry geometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }
}
