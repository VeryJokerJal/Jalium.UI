using System.Collections.ObjectModel;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Charts;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildChartsSection() => new GallerySampleChartsSection();
}

/// <summary>Templated charts with independently replaceable sample data.</summary>
[TemplatePart(Name = "PART_BarChart", Type = typeof(BarChart))]
[TemplatePart(Name = "PART_LineChart", Type = typeof(LineChart))]
[TemplatePart(Name = "PART_PieChart", Type = typeof(PieChart))]
[TemplatePart(Name = "PART_ScatterPlot", Type = typeof(ScatterPlot))]
[TemplatePart(Name = "PART_CandlestickChart", Type = typeof(CandlestickChart))]
[TemplatePart(Name = "PART_Heatmap", Type = typeof(Heatmap))]
[TemplatePart(Name = "PART_TreeMap", Type = typeof(TreeMap))]
public class GallerySampleChartsSection : GallerySampleHeader
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(nameof(Data), typeof(GallerySampleChartsData), typeof(GallerySampleChartsSection), new PropertyMetadata(null));
    public GallerySampleChartsData? Data { get => (GallerySampleChartsData?)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public GallerySampleChartsSection() { DefaultStyleKey = typeof(GallerySampleChartsSection); SetCurrentValue(DataProperty, new GallerySampleChartsData()); }
}

/// <summary>Small editable models used by the chart examples; contains no visual elements.</summary>
public sealed class GallerySampleChartsData
{
    public ObservableCollection<BarSeries> Bars { get; } = new();
    public ObservableCollection<LineSeries> Lines { get; } = new();
    public PieSeries Pie { get; } = new();
    public ObservableCollection<ScatterSeries> Scatter { get; } = new();
    public IList<double> LineValues { get; } = new List<double> { 4, 7, 5, 9, 6, 11, 8, 13, 12, 16 };
    public IList<double> BarValues { get; } = new List<double> { 3, 6, 4, 8, 5, 9, 7, 10 };
    public ObservableCollection<OhlcDataPoint> Candles { get; } = new();
    public double[,] Heat { get; } = new double[,] { { 1, 3, 5, 8, 6 }, { 2, 6, 9, 4, 7 }, { 5, 8, 3, 7, 9 } };
    public IList<string> XLabels { get; } = new List<string> { "Mon", "Tue", "Wed", "Thu", "Fri" };
    public IList<string> YLabels { get; } = new List<string> { "AM", "Noon", "PM" };
    public ObservableCollection<TreeMapItem> DiskUsage { get; } = new();
    public GallerySampleChartsData()
    {
        var bars = new BarSeries { Title = "2026" };
        string[] quarters = { "Q1", "Q2", "Q3", "Q4" }; double[] revenue = { 42, 58, 51, 70 };
        for (var i = 0; i < quarters.Length; i++) bars.DataPoints.Add(new ChartDataPoint { XValue = quarters[i], YValue = revenue[i], Label = quarters[i] });
        Bars.Add(bars);
        var line = new LineSeries { Title = "This week" };
        string[] days = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" }; double[] users = { 12, 19, 15, 27, 24, 33, 38 };
        for (var i = 0; i < days.Length; i++) line.DataPoints.Add(new ChartDataPoint { XValue = days[i], YValue = users[i], Label = days[i] });
        Lines.Add(line);
        Pie.DataPoints.Add(new PieDataPoint { Value = 41, Label = "Chrome" }); Pie.DataPoints.Add(new PieDataPoint { Value = 24, Label = "Edge" });
        Pie.DataPoints.Add(new PieDataPoint { Value = 19, Label = "Safari" }); Pie.DataPoints.Add(new PieDataPoint { Value = 16, Label = "Other" });
        var scatter = new ScatterSeries { Title = "Samples" };
        double[,] points = { { 1, 2.2 }, { 2, 3.1 }, { 3, 2.7 }, { 4, 4.4 }, { 5, 3.9 }, { 6, 5.2 }, { 7, 4.8 }, { 8, 6.1 } };
        for (var i = 0; i < points.GetLength(0); i++) scatter.DataPoints.Add(new ChartDataPoint { XValue = points[i, 0], YValue = points[i, 1] });
        Scatter.Add(scatter);
        double[,] ohlc = { { 20, 24, 19, 23 }, { 23, 26, 22, 22 }, { 22, 25, 21, 24 }, { 24, 28, 23, 27 }, { 27, 29, 25, 26 }, { 26, 30, 25, 29 }, { 29, 33, 28, 31 }, { 31, 32, 28, 29 } };
        var start = new DateTime(2026, 1, 1);
        for (var i = 0; i < ohlc.GetLength(0); i++) Candles.Add(new OhlcDataPoint { Date = start.AddDays(i), Open = ohlc[i, 0], High = ohlc[i, 1], Low = ohlc[i, 2], Close = ohlc[i, 3] });
        DiskUsage.Add(new TreeMapItem { Value = 48, Label = "Media" }); DiskUsage.Add(new TreeMapItem { Value = 27, Label = "Apps" });
        DiskUsage.Add(new TreeMapItem { Value = 15, Label = "System" }); DiskUsage.Add(new TreeMapItem { Value = 10, Label = "Other" });
    }
}
