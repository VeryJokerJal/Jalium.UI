using Jalium.UI.Controls;
using Jalium.UI.Controls.Charts;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
namespace Jalium.UI.Tests;
[Collection("macOS Window globals")]
public sealed class ChartBaseTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults=new(()=>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize();using var stream=new MemoryStream();
        return ThemeManager.XamlLoader!(stream,"Themes/Generic.jalxaml",typeof(Control).Assembly)!;
    });
    [Fact]
    public void BaseTemplateSeparatesPlotPixelsFromTheTitleAndLegend()
    {
        var chart=Create();Layout(chart);var ink=new Ink();chart.Render(ink);Layout(chart);
        Assert.Same(chart,Part<ChartPlotView>(chart,"PART_PlotView").Owner);Assert.Equal("来源数据",Part<TextBlock>(chart,"PART_Title").Text);Assert.NotNull(chart.ChartContentClip);
        Assert.Same(chart.LegendItems,Part<ChartLegend>(chart,"PART_Legend").Items);Assert.Equal(2,chart.LegendItems!.Cast<ChartLegendItem>().Count());Assert.True(chart.RenderCount>0);Assert.Contains(ink.Text,text=>text=="来源数据");
        chart.LegendPosition=LegendPosition.Left;Layout(chart);Assert.Equal(Orientation.Vertical,Part<ChartLegend>(chart,"PART_Legend").Orientation);Assert.Equal("LegendVertical",VisualStateManager.GetCurrentStateName(chart,"LegendStates"));
        chart.IsLegendVisible=false;Assert.Equal(Visibility.Collapsed,chart.LegendVisibility);chart.IsEnabled=false;Assert.Equal(.65,chart.Opacity);
    }
    [Fact]
    public void TooltipUsesTheTemplateAndFitsWithinTheChart()
    {
        var chart=Create();Layout(chart);chart.Present(355,235);var ink=new Ink();chart.Render(ink);Layout(chart);chart.Render(ink);
        var tooltip=Part<ChartTooltip>(chart,"PART_Tooltip");Assert.Equal(Visibility.Visible,tooltip.Visibility);Assert.Equal("42",tooltip.YValue);Assert.Equal("A",tooltip.XValue);
        Assert.InRange(chart.TooltipBounds.Right,0,360);Assert.InRange(chart.TooltipBounds.Bottom,0,240);Assert.True(chart.TooltipBounds.Width>0);Assert.True(chart.TooltipBounds.Height>0);
        chart.IsTooltipEnabled=false;chart.Render(new Ink());Assert.Equal(Visibility.Collapsed,tooltip.Visibility);
    }
    [Fact]
    public void RetiredPartsReleaseOwnersAndLegendModelsWithoutReplacingTheData()
    {
        var chart=Create();Layout(chart);chart.Render(new Ink());var view=Part<ChartPlotView>(chart,"PART_PlotView");var legend=Part<ChartLegend>(chart,"PART_Legend");var rows=chart.Rows;
        var blank=new ControlTemplate(typeof(ChartBase));blank.SetVisualTree(()=>new Border());chart.Template=blank;Layout(chart);Assert.Null(view.Owner);Assert.Null(legend.Items);Assert.Same(rows,chart.Rows);
        chart.ClearValue(Control.TemplateProperty);Layout(chart);chart.Render(new Ink());Assert.Same(chart,Part<ChartPlotView>(chart,"PART_PlotView").Owner);Assert.NotSame(view,chart.FindName("PART_PlotView"));
    }
    [Fact]
    public void SparklineTemplateOwnsItsFrameAndTracksEmptyData()
    {
        var spark=new Sparkline{Values=new double[]{1,3,2,5}};spark.Resources.MergedDictionaries.Add(Defaults.Value);Layout(spark);var view=Part<SparklinePlotView>(spark,"PART_PlotView");Assert.Same(spark,view.Owner);Assert.True(spark.HasData);Assert.NotNull(spark.SparklineContentClip);
        spark.Values=Array.Empty<double>();Layout(spark);Assert.False(spark.HasData);Assert.Equal("Empty",VisualStateManager.GetCurrentStateName(spark,"DataStates"));
        var blank=new ControlTemplate(typeof(Sparkline));blank.SetVisualTree(()=>new Border());spark.Template=blank;Layout(spark);Assert.Null(view.Owner);Layout(new SparklinePlotView());Layout(new ChartPlotView());
    }
    private static Probe Create(){var chart=new Probe{Title="来源数据",Background=Brushes.White,Foreground=Brushes.Black};chart.Resources.MergedDictionaries.Add(Defaults.Value);return chart;}
    [Fact]
    public void NarrowCategoryAxisKeepsLabelsApartWithoutDroppingDataPoints()
    {
        string[] months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
        var chart = new LineChart { Foreground = Brushes.Purple };
        var series = new LineSeries();
        for (var i = 0; i < months.Length; i++)
            series.DataPoints.Add(new ChartDataPoint { XValue = months[i], YValue = 10 + i });
        chart.Series.Add(series);
        chart.Resources.MergedDictionaries.Add(Defaults.Value);
        Layout(chart);
        var ink = new Ink();
        Part<ChartPlotView>(chart, "PART_PlotView").Render(ink);
        var labels = ink.Labels.Where(p => months.Contains(p.Text)).OrderBy(p => p.X).ToArray();
        Assert.InRange(labels.Length, 2, months.Length - 1);
        Assert.All(labels, label => Assert.Same(Brushes.Purple, label.Foreground));
        for (var i = 1; i < labels.Length; i++)
            Assert.True(labels[i].X >= labels[i - 1].X + labels[i - 1].Width + 3.9);
        Assert.Equal(months.Length, series.DataPoints.Count);
    }
    private sealed class Probe:ChartBase
    {
        internal readonly List<ChartLegendItem> Rows=[new(){Label="第一组",Brush=Brushes.Red},new(){Label="第二组",Brush=Brushes.Blue}];
        internal int RenderCount;
        protected override IList<ChartLegendItem>? CollectLegendItems()=>Rows;
        protected override void RenderChart(DrawingContext dc,Rect area){RenderCount++;dc.DrawRectangle(Brushes.Blue,null,new Rect(area.X,area.Y,20,20));}
        internal void Present(double x,double y)=>ShowTooltip(x,y,null,"A","42");
    }
    private static T Part<T>(Control owner,string name)where T:class=>Assert.IsAssignableFrom<T>(owner.FindName(name));
    private static void Layout(FrameworkElement element){for(var i=0;i<4;i++){element.InvalidateMeasure();element.Measure(new Size(360,240));element.Arrange(new Rect(0,0,360,240));element.UpdateLayout();}}
    private sealed class Ink : DrawingContextAdapter
    {
        internal readonly List<string> Text = new();
        internal readonly List<(string Text, double X, double Width, Brush? Foreground)> Labels = new();
        public override void DrawText(FormattedText text, Point origin)
        {
            Text.Add(text.Text);
            Labels.Add((text.Text, origin.X, text.Width, text.Foreground));
        }
        public override void DrawLine(Pen pen, Point a, Point b) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) { }
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
