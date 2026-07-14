using Jalium.UI.Controls;
using Jalium.UI.Controls.Charts;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

/// <summary>
/// Charts section. Each chart is populated with a small in-memory sample series so
/// the gallery shows real, GPU-rendered visualizations. Series collections are filled
/// with explicit <c>.Add(...)</c> calls (rather than collection initializers) to stay
/// robust against per-type series shapes.
/// </summary>
internal static partial class GalleryWindow
{
    public static UIElement BuildChartsSection() => Section(
        "Charts",
        "GPU-rendered data visualizations driven by small sample series.",
        Card("BarChart", BarChartDemo(), width: 480),
        Card("LineChart", LineChartDemo(), width: 480),
        Card("PieChart", PieChartDemo(), width: 340),
        Card("ScatterPlot", ScatterPlotDemo(), width: 480),
        Card("GaugeChart", GaugeChartDemo(), width: 300),
        Card("Sparkline", SparklineDemo(), width: 280),
        Card("CandlestickChart", CandlestickDemo(), width: 480),
        Card("Heatmap", HeatmapDemo(), width: 360),
        Card("TreeMap", TreeMapDemo(), width: 360));

    private static UIElement BarChartDemo()
    {
        var chart = new BarChart { Title = "Quarterly revenue", Width = 440, Height = 240 };
        var series = new BarSeries { Title = "2026" };
        string[] labels = { "Q1", "Q2", "Q3", "Q4" };
        double[] values = { 42, 58, 51, 70 };
        for (int i = 0; i < labels.Length; i++)
            series.DataPoints.Add(new ChartDataPoint { XValue = labels[i], YValue = values[i], Label = labels[i] });
        chart.Series.Add(series);
        return chart;
    }

    private static UIElement LineChartDemo()
    {
        var chart = new LineChart { Title = "Active users", Width = 440, Height = 240, ShowArea = true, ShowDataPoints = true };
        var series = new LineSeries { Title = "This week" };
        string[] days = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        double[] values = { 12, 19, 15, 27, 24, 33, 38 };
        for (int i = 0; i < days.Length; i++)
            series.DataPoints.Add(new ChartDataPoint { XValue = days[i], YValue = values[i], Label = days[i] });
        chart.Series.Add(series);
        return chart;
    }

    private static UIElement PieChartDemo()
    {
        var chart = new PieChart { Title = "Browser share", Width = 300, Height = 240 };
        var series = new PieSeries();
        series.DataPoints.Add(new PieDataPoint { Value = 41, Label = "Chrome" });
        series.DataPoints.Add(new PieDataPoint { Value = 24, Label = "Edge" });
        series.DataPoints.Add(new PieDataPoint { Value = 19, Label = "Safari" });
        series.DataPoints.Add(new PieDataPoint { Value = 16, Label = "Other" });
        chart.Series = series;
        return chart;
    }

    private static UIElement ScatterPlotDemo()
    {
        var chart = new ScatterPlot { Title = "Height vs. weight", Width = 440, Height = 240 };
        var series = new ScatterSeries { Title = "Samples" };
        double[,] pts = { { 1, 2.2 }, { 2, 3.1 }, { 3, 2.7 }, { 4, 4.4 }, { 5, 3.9 }, { 6, 5.2 }, { 7, 4.8 }, { 8, 6.1 } };
        for (int i = 0; i < pts.GetLength(0); i++)
            series.DataPoints.Add(new ChartDataPoint { XValue = pts[i, 0], YValue = pts[i, 1] });
        chart.Series.Add(series);
        return chart;
    }

    private static UIElement GaugeChartDemo()
    {
        return new GaugeChart
        {
            Title = "CPU load",
            Minimum = 0,
            Maximum = 100,
            Value = 72,
            Width = 260,
            Height = 200,
        };
    }

    private static UIElement SparklineDemo()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 10 };
        stack.Children.Add(new Sparkline
        {
            Values = new List<double> { 4, 7, 5, 9, 6, 11, 8, 13, 12, 16 },
            SparklineType = SparklineType.Line,
            Width = 240,
            Height = 56,
        });
        stack.Children.Add(new Sparkline
        {
            Values = new List<double> { 3, 6, 4, 8, 5, 9, 7, 10 },
            SparklineType = SparklineType.Bar,
            Width = 240,
            Height = 56,
        });
        return stack;
    }

    private static UIElement CandlestickDemo()
    {
        var data = new List<OhlcDataPoint>();
        double[,] ohlc =
        {
            { 20, 24, 19, 23 }, { 23, 26, 22, 22 }, { 22, 25, 21, 24 },
            { 24, 28, 23, 27 }, { 27, 29, 25, 26 }, { 26, 30, 25, 29 },
            { 29, 33, 28, 31 }, { 31, 32, 28, 29 },
        };
        var start = new DateTime(2026, 1, 1);
        for (int i = 0; i < ohlc.GetLength(0); i++)
        {
            data.Add(new OhlcDataPoint
            {
                Date = start.AddDays(i),
                Open = ohlc[i, 0],
                High = ohlc[i, 1],
                Low = ohlc[i, 2],
                Close = ohlc[i, 3],
            });
        }
        return new CandlestickChart { Title = "JAL/USD", ItemsSource = data, Width = 440, Height = 240 };
    }

    private static UIElement HeatmapDemo()
    {
        var heatmap = new Heatmap
        {
            Title = "Activity",
            Width = 320,
            Height = 220,
            Data = new double[,]
            {
                { 1, 3, 5, 8, 6 },
                { 2, 6, 9, 4, 7 },
                { 5, 8, 3, 7, 9 },
            },
            XLabels = new List<string> { "Mon", "Tue", "Wed", "Thu", "Fri" },
            YLabels = new List<string> { "AM", "Noon", "PM" },
            ShowCellValues = true,
        };
        return heatmap;
    }

    private static UIElement TreeMapDemo()
    {
        var treeMap = new TreeMap { Title = "Disk usage", Width = 320, Height = 220 };
        treeMap.Items.Add(new TreeMapItem { Value = 48, Label = "Media" });
        treeMap.Items.Add(new TreeMapItem { Value = 27, Label = "Apps" });
        treeMap.Items.Add(new TreeMapItem { Value = 15, Label = "System" });
        treeMap.Items.Add(new TreeMapItem { Value = 10, Label = "Other" });
        return treeMap;
    }
}
