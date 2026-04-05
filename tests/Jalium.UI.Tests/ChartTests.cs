using System.Collections.ObjectModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Charts;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ChartTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);
        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    #region ChartHelpers

    [Fact]
    public void ChartHelpers_NiceNumber_Round_ReturnsNiceValue()
    {
        // Range 0.7 -> fraction 7.0 -> nice = 10.0 -> result = 10 * 10^(-1) = 1.0
        var result = ChartHelpers.NiceNumber(0.7, true);
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void ChartHelpers_NiceNumber_Ceiling_ReturnsNiceValue()
    {
        // Range 0.7 -> fraction 7.0 -> nice (ceiling) = 10 -> result = 1.0
        var result = ChartHelpers.NiceNumber(0.7, false);
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void ChartHelpers_NiceNumber_Zero_ReturnsOne()
    {
        var result = ChartHelpers.NiceNumber(0, true);
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void ChartHelpers_NiceNumber_Negative_ReturnsOne()
    {
        var result = ChartHelpers.NiceNumber(-5, true);
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void ChartHelpers_NiceNumber_LargeValue()
    {
        // 350 -> exponent=2, fraction=3.5 -> round: nice=5 -> result=500
        var result = ChartHelpers.NiceNumber(350, true);
        Assert.Equal(500.0, result);
    }

    [Fact]
    public void ChartHelpers_ComputeAutoRange_NormalValues()
    {
        ChartHelpers.ComputeAutoRange(new[] { 10.0, 20.0, 30.0 }, out double min, out double max);

        // Range = 30-10 = 20, padding = 20*0.05 = 1
        Assert.Equal(9.0, min, 5);
        Assert.Equal(31.0, max, 5);
    }

    [Fact]
    public void ChartHelpers_ComputeAutoRange_SingleValue()
    {
        ChartHelpers.ComputeAutoRange(new[] { 50.0 }, out double min, out double max);

        // All same: absVal=50, min=50-2.5=47.5, max=50+2.5=52.5
        Assert.True(min < 50.0);
        Assert.True(max > 50.0);
    }

    [Fact]
    public void ChartHelpers_ComputeAutoRange_EmptyValues()
    {
        ChartHelpers.ComputeAutoRange(Array.Empty<double>(), out double min, out double max);

        Assert.Equal(0.0, min);
        Assert.Equal(1.0, max);
    }

    [Fact]
    public void ChartHelpers_ComputeAutoRange_AllZero()
    {
        ChartHelpers.ComputeAutoRange(new[] { 0.0, 0.0 }, out double min, out double max);

        Assert.Equal(-1.0, min);
        Assert.Equal(1.0, max);
    }

    [Fact]
    public void ChartHelpers_MapValue_MapsCorrectly()
    {
        var result = ChartHelpers.MapValue(50, 0, 100, 0, 1000);
        Assert.Equal(500.0, result, 5);
    }

    [Fact]
    public void ChartHelpers_MapValue_ZeroRange_ReturnsMidpoint()
    {
        var result = ChartHelpers.MapValue(5, 5, 5, 0, 100);
        Assert.Equal(50.0, result, 5);
    }

    [Fact]
    public void ChartHelpers_Lerp_ZeroT_ReturnsA()
    {
        Assert.Equal(10.0, ChartHelpers.Lerp(10.0, 20.0, 0.0));
    }

    [Fact]
    public void ChartHelpers_Lerp_OneT_ReturnsB()
    {
        Assert.Equal(20.0, ChartHelpers.Lerp(10.0, 20.0, 1.0));
    }

    [Fact]
    public void ChartHelpers_Lerp_HalfT_ReturnsMidpoint()
    {
        Assert.Equal(15.0, ChartHelpers.Lerp(10.0, 20.0, 0.5));
    }

    [Fact]
    public void ChartHelpers_PointInPolygon_Inside()
    {
        var polygon = new Point[]
        {
            new(0, 0), new(10, 0), new(10, 10), new(0, 10)
        };
        Assert.True(ChartHelpers.PointInPolygon(new Point(5, 5), polygon));
    }

    [Fact]
    public void ChartHelpers_PointInPolygon_Outside()
    {
        var polygon = new Point[]
        {
            new(0, 0), new(10, 0), new(10, 10), new(0, 10)
        };
        Assert.False(ChartHelpers.PointInPolygon(new Point(15, 5), polygon));
    }

    [Fact]
    public void ChartHelpers_PointInPolygon_NullOrTooFewPoints()
    {
        Assert.False(ChartHelpers.PointInPolygon(new Point(5, 5), null!));
        Assert.False(ChartHelpers.PointInPolygon(new Point(5, 5), new Point[] { new(0, 0), new(1, 1) }));
    }

    [Fact]
    public void ChartHelpers_DistanceToSegment_PointOnSegment()
    {
        var dist = ChartHelpers.DistanceToSegment(new Point(5, 0), new Point(0, 0), new Point(10, 0));
        Assert.Equal(0.0, dist, 5);
    }

    [Fact]
    public void ChartHelpers_DistanceToSegment_PerpendicularDistance()
    {
        var dist = ChartHelpers.DistanceToSegment(new Point(5, 3), new Point(0, 0), new Point(10, 0));
        Assert.Equal(3.0, dist, 5);
    }

    [Fact]
    public void ChartHelpers_DistanceToSegment_ZeroLengthSegment()
    {
        var dist = ChartHelpers.DistanceToSegment(new Point(3, 4), new Point(0, 0), new Point(0, 0));
        Assert.Equal(5.0, dist, 5);
    }

    [Fact]
    public void ChartHelpers_SimplifyPoints_TooFewPoints_ReturnsSame()
    {
        var points = new Point[] { new(0, 0), new(1, 1) };
        var result = ChartHelpers.SimplifyPoints(points, 1.0);
        Assert.Equal(2, result.Length);
    }

    [Fact]
    public void ChartHelpers_SimplifyPoints_CollinearPoints_SimplifiesToEndpoints()
    {
        var points = new Point[] { new(0, 0), new(5, 0), new(10, 0) };
        var result = ChartHelpers.SimplifyPoints(points, 1.0);
        Assert.Equal(2, result.Length); // Only endpoints remain
    }

    [Fact]
    public void ChartHelpers_SimplifyPoints_LargeDeviation_KeepsAll()
    {
        var points = new Point[] { new(0, 0), new(5, 100), new(10, 0) };
        var result = ChartHelpers.SimplifyPoints(points, 1.0);
        Assert.Equal(3, result.Length); // Middle point has large deviation
    }

    [Fact]
    public void ChartHelpers_SimplifyPoints_Null_ReturnsEmpty()
    {
        var result = ChartHelpers.SimplifyPoints(null!, 1.0);
        Assert.Empty(result);
    }

    #endregion

    #region NumericAxis

    [Fact]
    public void NumericAxis_GenerateTicks_ProducesTicksInRange()
    {
        var axis = new NumericAxis();
        var ticks = axis.GenerateTicks(0, 100, 500);

        Assert.NotEmpty(ticks);
        Assert.True(ticks[0] >= 0);
        Assert.True(ticks[^1] <= 100 + 1);
    }

    [Fact]
    public void NumericAxis_GenerateTicks_InvalidRange_ReturnsEmpty()
    {
        var axis = new NumericAxis();
        var ticks = axis.GenerateTicks(100, 0, 500);
        Assert.Empty(ticks);
    }

    [Fact]
    public void NumericAxis_GenerateTicks_ExplicitInterval()
    {
        var axis = new NumericAxis { TickInterval = 25.0 };
        var ticks = axis.GenerateTicks(0, 100, 500);

        Assert.Contains(0.0, ticks);
        Assert.Contains(25.0, ticks);
        Assert.Contains(50.0, ticks);
        Assert.Contains(75.0, ticks);
        Assert.Contains(100.0, ticks);
    }

    [Fact]
    public void NumericAxis_ValueToPixel_MapsCorrectly()
    {
        var axis = new NumericAxis();
        var pixel = axis.ValueToPixel(50, 0, 100, 1000);
        Assert.Equal(500.0, pixel, 5);
    }

    [Fact]
    public void NumericAxis_PixelToValue_MapsCorrectly()
    {
        var axis = new NumericAxis();
        var value = axis.PixelToValue(500, 0, 100, 1000);
        Assert.Equal(50.0, value, 5);
    }

    [Fact]
    public void NumericAxis_ValueToPixel_Roundtrip()
    {
        var axis = new NumericAxis();
        double originalValue = 42.5;
        var pixel = axis.ValueToPixel(originalValue, 0, 100, 800);
        var roundtrip = axis.PixelToValue(pixel, 0, 100, 800);
        Assert.Equal(originalValue, roundtrip, 5);
    }

    #endregion

    #region CategoryAxis

    [Fact]
    public void CategoryAxis_ValueToPixel_CentersInBand()
    {
        var axis = new CategoryAxis
        {
            Categories = new List<string> { "A", "B", "C", "D" }
        };
        // 4 categories, 400px -> bandWidth=100
        // Category 0 -> 0*100 + 100/2 = 50
        var pixel = axis.ValueToPixel(0, 0, 3, 400);
        Assert.Equal(50.0, pixel, 5);
    }

    [Fact]
    public void CategoryAxis_PixelToValue_ReturnsCorrectIndex()
    {
        var axis = new CategoryAxis
        {
            Categories = new List<string> { "A", "B", "C", "D" }
        };
        // 4 categories, 400px -> bandWidth=100
        // pixel 150 -> floor(150/100) = 1
        var value = axis.PixelToValue(150, 0, 3, 400);
        Assert.Equal(1.0, value, 5);
    }

    [Fact]
    public void CategoryAxis_FormatLabel_ReturnsCategory()
    {
        var axis = new CategoryAxis
        {
            Categories = new List<string> { "Apple", "Banana", "Cherry" }
        };
        Assert.Equal("Banana", axis.FormatLabel(1));
    }

    [Fact]
    public void CategoryAxis_GenerateTicks_ReturnsIndices()
    {
        var axis = new CategoryAxis
        {
            Categories = new List<string> { "X", "Y", "Z" }
        };
        var ticks = axis.GenerateTicks(0, 2, 300);
        Assert.Equal(3, ticks.Length);
        Assert.Equal(0.0, ticks[0]);
        Assert.Equal(1.0, ticks[1]);
        Assert.Equal(2.0, ticks[2]);
    }

    [Fact]
    public void CategoryAxis_GenerateTicks_NoCategories_ReturnsEmpty()
    {
        var axis = new CategoryAxis();
        var ticks = axis.GenerateTicks(0, 10, 500);
        Assert.Empty(ticks);
    }

    #endregion

    #region DateTimeAxis

    [Fact]
    public void DateTimeAxis_FormatLabel_Day_ReturnsExpectedFormat()
    {
        var axis = new DateTimeAxis { IntervalType = DateTimeIntervalType.Day };
        var value = DateTimeAxis.DateTimeToDouble(new DateTime(2025, 3, 15, 0, 0, 0, DateTimeKind.Utc));
        var label = axis.FormatLabel(value);
        Assert.Contains("15", label); // Should contain the day
    }

    [Fact]
    public void DateTimeAxis_FormatLabel_Year_ReturnsYear()
    {
        var axis = new DateTimeAxis { IntervalType = DateTimeIntervalType.Year };
        var value = DateTimeAxis.DateTimeToDouble(new DateTime(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc));
        var label = axis.FormatLabel(value);
        Assert.Equal("2025", label);
    }

    [Fact]
    public void DateTimeAxis_FormatLabel_CustomFormat()
    {
        var axis = new DateTimeAxis { DateFormat = "yyyy/MM/dd" };
        var value = DateTimeAxis.DateTimeToDouble(new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc));
        var label = axis.FormatLabel(value);
        Assert.Equal("2025/01/15", label);
    }

    [Fact]
    public void DateTimeAxis_DateTimeToDouble_Roundtrip()
    {
        var original = new DateTime(2025, 6, 15, 12, 30, 0, DateTimeKind.Utc);
        var doubleVal = DateTimeAxis.DateTimeToDouble(original);
        var roundtrip = DateTimeAxis.DoubleToDateTime(doubleVal);
        Assert.Equal(original, roundtrip, TimeSpan.FromSeconds(1));
    }

    #endregion

    #region LogarithmicAxis

    [Fact]
    public void LogarithmicAxis_ValueToPixel_MapsLogarithmically()
    {
        var axis = new LogarithmicAxis { LogBase = 10 };
        // log10(10) = 1, log10(1) = 0, log10(100) = 2
        // range: log10(1)..log10(100) = 0..2
        // value 10 -> log10(10) = 1, pixel = (1-0)/2 * 1000 = 500
        var pixel = axis.ValueToPixel(10, 1, 100, 1000);
        Assert.Equal(500.0, pixel, 5);
    }

    [Fact]
    public void LogarithmicAxis_PixelToValue_MapsCorrectly()
    {
        var axis = new LogarithmicAxis { LogBase = 10 };
        var value = axis.PixelToValue(500, 1, 100, 1000);
        Assert.Equal(10.0, value, 3);
    }

    [Fact]
    public void LogarithmicAxis_ValueToPixel_Roundtrip()
    {
        var axis = new LogarithmicAxis { LogBase = 10 };
        double originalValue = 42.0;
        var pixel = axis.ValueToPixel(originalValue, 1, 1000, 600);
        var roundtrip = axis.PixelToValue(pixel, 1, 1000, 600);
        Assert.Equal(originalValue, roundtrip, 2);
    }

    [Fact]
    public void LogarithmicAxis_GenerateTicks_ProducesPowersOfBase()
    {
        var axis = new LogarithmicAxis { LogBase = 10 };
        var ticks = axis.GenerateTicks(1, 10000, 500);

        Assert.Contains(1.0, ticks);
        Assert.Contains(10.0, ticks);
        Assert.Contains(100.0, ticks);
        Assert.Contains(1000.0, ticks);
        Assert.Contains(10000.0, ticks);
    }

    [Fact]
    public void LogarithmicAxis_GenerateTicks_InvalidRange_ReturnsEmpty()
    {
        var axis = new LogarithmicAxis();
        var ticks = axis.GenerateTicks(-10, 100, 500); // min <= 0
        Assert.Empty(ticks);
    }

    #endregion

    #region LineChart

    [Fact]
    public void LineChart_DefaultProperties()
    {
        var chart = new LineChart();

        Assert.NotNull(chart.Series);
        Assert.Empty(chart.Series);
        Assert.False(chart.LineSmoothing);
        Assert.True(chart.ShowDataPoints);
        Assert.Equal(4.0, chart.DataPointRadius);
        Assert.False(chart.ShowArea);
        Assert.Equal(0.3, chart.AreaOpacity);
    }

    [Fact]
    public void LineChart_Series_CanAdd()
    {
        var chart = new LineChart();
        var series = new LineSeries { Name = "Test" };
        chart.Series.Add(series);

        Assert.Single(chart.Series);
        Assert.Equal("Test", chart.Series[0].Name);
    }

    #endregion

    #region BarChart

    [Fact]
    public void BarChart_DefaultProperties()
    {
        var chart = new BarChart();

        Assert.NotNull(chart.Series);
        Assert.Empty(chart.Series);
        Assert.Equal(BarMode.Grouped, chart.BarMode);
        Assert.Equal(Orientation.Vertical, chart.Orientation);
        Assert.Equal(2.0, chart.BarSpacing);
        Assert.Equal(8.0, chart.GroupSpacing);
        Assert.False(chart.ShowValueLabels);
    }

    [Fact]
    public void BarChart_BarMode_CanBeSet()
    {
        var chart = new BarChart();
        chart.BarMode = BarMode.Stacked;
        Assert.Equal(BarMode.Stacked, chart.BarMode);
    }

    [Fact]
    public void BarChart_BarMode_StackedPercentage()
    {
        var chart = new BarChart();
        chart.BarMode = BarMode.StackedPercentage;
        Assert.Equal(BarMode.StackedPercentage, chart.BarMode);
    }

    #endregion

    #region PieChart

    [Fact]
    public void PieChart_DefaultProperties()
    {
        var chart = new PieChart();

        Assert.NotNull(chart.Series);
        Assert.Equal(0.0, chart.InnerRadiusRatio);
        Assert.Equal(-90.0, chart.StartAngle);
        Assert.Equal(10.0, chart.ExplodeOffset);
        Assert.True(chart.ShowLabels);
        Assert.Equal(PieLabelPosition.Outside, chart.LabelPosition);
        Assert.Equal("{0}: {1:P0}", chart.LabelFormat);
    }

    [Fact]
    public void PieChart_InnerRadiusRatio_CanBeSet()
    {
        var chart = new PieChart();
        chart.InnerRadiusRatio = 0.5;
        Assert.Equal(0.5, chart.InnerRadiusRatio);
    }

    #endregion

    #region ScatterPlot

    [Fact]
    public void ScatterPlot_DefaultProperties()
    {
        var chart = new ScatterPlot();

        Assert.NotNull(chart.Series);
        Assert.Empty(chart.Series);
        Assert.False(chart.ShowTrendLine);
        Assert.Equal(TrendLineType.Linear, chart.TrendLineType);
        Assert.Equal(4.0, chart.MinPointSize);
        Assert.Equal(20.0, chart.MaxPointSize);
        Assert.Null(chart.SizeBindingPath);
    }

    #endregion

    #region Heatmap

    [Fact]
    public void Heatmap_DefaultProperties()
    {
        var chart = new Heatmap();

        Assert.Null(chart.Data);
        Assert.Null(chart.XLabels);
        Assert.Null(chart.YLabels);
        Assert.Equal(HeatmapColorScale.BlueToRed, chart.ColorScale);
        Assert.False(chart.ShowCellValues);
        Assert.Equal("F1", chart.CellValueFormat);
        Assert.Equal(0.5, chart.CellBorderThickness);
        Assert.Null(chart.DataMinimum);
        Assert.Null(chart.DataMaximum);
    }

    #endregion

    #region Sparkline

    [Fact]
    public void Sparkline_DefaultProperties()
    {
        var sparkline = new Sparkline();

        Assert.Null(sparkline.Values);
        Assert.Equal(SparklineType.Line, sparkline.SparklineType);
        Assert.Equal(1.5, sparkline.LineThickness);
    }

    [Fact]
    public void Sparkline_MeasureOverride_UnconstrainedSize()
    {
        var sparkline = new Sparkline();
        sparkline.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // When unconstrained, Sparkline returns 80x20
        Assert.Equal(80, sparkline.DesiredSize.Width);
        Assert.Equal(20, sparkline.DesiredSize.Height);
    }

    [Fact]
    public void Sparkline_MeasureOverride_ConstrainedSize()
    {
        var sparkline = new Sparkline();
        sparkline.Measure(new Size(200, 50));
        Assert.Equal(200, sparkline.DesiredSize.Width);
        Assert.Equal(50, sparkline.DesiredSize.Height);
    }

    #endregion

    #region GaugeChart

    [Fact]
    public void GaugeChart_DefaultProperties()
    {
        var chart = new GaugeChart();

        Assert.Equal(0.0, chart.Value);
        Assert.Equal(0.0, chart.Minimum);
        Assert.Equal(100.0, chart.Maximum);
        Assert.Equal(-225.0, chart.StartAngle);
        Assert.Equal(45.0, chart.EndAngle);
        Assert.Equal(0.8, chart.NeedleLength);
        Assert.Null(chart.NeedleBrush);
    }

    [Fact]
    public void GaugeChart_Value_CanBeSet()
    {
        var chart = new GaugeChart();
        chart.Value = 75.0;
        Assert.Equal(75.0, chart.Value);
    }

    [Fact]
    public void GaugeChart_Value_ClampedDuringRender()
    {
        // The Value DP itself doesn't clamp, but render clamps it.
        // We verify the property can hold out-of-range values.
        var chart = new GaugeChart();
        chart.Value = 150.0;
        Assert.Equal(150.0, chart.Value); // DP stores it; render clamps
    }

    [Fact]
    public void GaugeChart_MinMaxRange()
    {
        var chart = new GaugeChart();
        chart.Minimum = -50;
        chart.Maximum = 50;
        Assert.Equal(-50.0, chart.Minimum);
        Assert.Equal(50.0, chart.Maximum);
    }

    #endregion

    #region TreeMap

    [Fact]
    public void TreeMap_DefaultProperties()
    {
        var chart = new TreeMap();

        Assert.Null(chart.Items);
        Assert.Equal(TreeMapAlgorithm.Squarified, chart.Algorithm);
        Assert.Equal(20.0, chart.MinCellSize);
        Assert.True(chart.ShowLabels);
        Assert.Equal(8.0, chart.LabelMinFontSize);
        Assert.Equal(2.0, chart.CellPadding);
    }

    #endregion

    #region CandlestickChart

    [Fact]
    public void CandlestickChart_DefaultProperties()
    {
        var chart = new CandlestickChart();

        Assert.Null(chart.ItemsSource);
        Assert.False(chart.ShowVolume);
        Assert.Equal(0.2, chart.VolumeHeight);
        Assert.NotNull(chart);
    }

    #endregion

    #region NetworkGraph

    [Fact]
    public void NetworkGraph_DefaultProperties()
    {
        var chart = new NetworkGraph();

        Assert.Null(chart.Nodes);
        Assert.Null(chart.Links);
        Assert.Equal(NetworkLayoutAlgorithm.ForceDirected, chart.LayoutAlgorithm);
        Assert.Equal(15.0, chart.NodeRadius);
        Assert.Null(chart.NodeBrush);
    }

    #endregion

    #region GanttChart

    [Fact]
    public void GanttChart_DefaultProperties()
    {
        var chart = new GanttChart();

        Assert.Null(chart.Tasks);
        Assert.Equal(30.0, chart.RowHeight);
        Assert.Null(chart.TaskBrush);
        Assert.Null(chart.MilestoneBrush);
        Assert.Null(chart.DependencyLineBrush);
    }

    #endregion

    #region SankeyDiagram

    [Fact]
    public void SankeyDiagram_DefaultProperties()
    {
        var chart = new SankeyDiagram();

        Assert.Null(chart.Nodes);
        Assert.Null(chart.Links);
        Assert.Equal(20.0, chart.NodeWidth);
        Assert.Equal(10.0, chart.NodeSpacing);
    }

    #endregion
}
