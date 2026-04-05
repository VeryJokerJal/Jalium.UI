using System.Collections.ObjectModel;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Displays data as connected line segments with optional smoothing, area fill, and data point markers.
/// </summary>
public class LineChart : AxisChartBase
{
    #region Private State

    /// <summary>
    /// Cached pixel positions for each series' data points (for hit testing).
    /// </summary>
    private readonly List<(LineSeries series, List<Point> pixels)> _pointCache = new();

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Series dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SeriesProperty =
        DependencyProperty.Register(nameof(Series), typeof(ObservableCollection<LineSeries>), typeof(LineChart),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the LineSmoothing dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty LineSmoothingProperty =
        DependencyProperty.Register(nameof(LineSmoothing), typeof(bool), typeof(LineChart),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowDataPoints dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowDataPointsProperty =
        DependencyProperty.Register(nameof(ShowDataPoints), typeof(bool), typeof(LineChart),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the DataPointRadius dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty DataPointRadiusProperty =
        DependencyProperty.Register(nameof(DataPointRadius), typeof(double), typeof(LineChart),
            new PropertyMetadata(4.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowArea dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowAreaProperty =
        DependencyProperty.Register(nameof(ShowArea), typeof(bool), typeof(LineChart),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the AreaOpacity dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty AreaOpacityProperty =
        DependencyProperty.Register(nameof(AreaOpacity), typeof(double), typeof(LineChart),
            new PropertyMetadata(0.3, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the collection of line series to render.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public ObservableCollection<LineSeries> Series
    {
        get
        {
            var s = (ObservableCollection<LineSeries>?)GetValue(SeriesProperty);
            if (s == null)
            {
                s = new ObservableCollection<LineSeries>();
                SetValue(SeriesProperty, s);
            }
            return s;
        }
        set => SetValue(SeriesProperty, value);
    }

    /// <summary>
    /// Gets or sets whether lines are drawn with Catmull-Rom spline smoothing.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool LineSmoothing
    {
        get => (bool)GetValue(LineSmoothingProperty)!;
        set => SetValue(LineSmoothingProperty, value);
    }

    /// <summary>
    /// Gets or sets whether data point markers are shown.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowDataPoints
    {
        get => (bool)GetValue(ShowDataPointsProperty)!;
        set => SetValue(ShowDataPointsProperty, value);
    }

    /// <summary>
    /// Gets or sets the radius of data point markers.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double DataPointRadius
    {
        get => (double)GetValue(DataPointRadiusProperty)!;
        set => SetValue(DataPointRadiusProperty, value);
    }

    /// <summary>
    /// Gets or sets whether a filled area is drawn beneath each line.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowArea
    {
        get => (bool)GetValue(ShowAreaProperty)!;
        set => SetValue(ShowAreaProperty, value);
    }

    /// <summary>
    /// Gets or sets the opacity of the area fill (0.0 to 1.0).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double AreaOpacity
    {
        get => (double)GetValue(AreaOpacityProperty)!;
        set => SetValue(AreaOpacityProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="LineChart"/> class.
    /// </summary>
    public LineChart()
    {
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnLineChartMouseMove));
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnLineChartMouseLeave));
    }

    #endregion

    #region Automation

    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.LineChartAutomationPeer(this);
    }

    #endregion

    #region Data Collection

    /// <inheritdoc />
    protected override IEnumerable<double> CollectXValues()
    {
        var series = (ObservableCollection<LineSeries>?)GetValue(SeriesProperty);
        if (series == null)
            yield break;

        foreach (var s in series)
        {
            if (!s.IsVisible)
                continue;
            for (int i = 0; i < s.DataPoints.Count; i++)
            {
                var dp = s.DataPoints[i];
                if (dp.XValue is double d)
                    yield return d;
                else if (dp.XValue is string)
                    yield return i;
                else if (dp.XValue is IConvertible c)
                {
                    double val;
                    try { val = c.ToDouble(System.Globalization.CultureInfo.InvariantCulture); }
                    catch { val = i; }
                    yield return val;
                }
                else
                    yield return i;
            }
        }
    }

    /// <inheritdoc />
    protected override IList<string>? CollectXCategories()
    {
        var series = (ObservableCollection<LineSeries>?)GetValue(SeriesProperty);
        if (series == null) return null;

        // Check if any series has string XValues
        foreach (var s in series)
        {
            if (!s.IsVisible) continue;
            foreach (var dp in s.DataPoints)
            {
                if (dp.XValue is string)
                {
                    // Collect unique category labels in order from the first series with string XValues
                    var categories = new List<string>();
                    foreach (var s2 in series)
                    {
                        if (!s2.IsVisible) continue;
                        foreach (var dp2 in s2.DataPoints)
                        {
                            if (dp2.XValue is string label && !categories.Contains(label))
                                categories.Add(label);
                        }
                    }
                    return categories;
                }
                return null; // First XValue is not string, assume numeric
            }
        }
        return null;
    }

    /// <inheritdoc />
    protected override IEnumerable<double> CollectYValues()
    {
        var series = (ObservableCollection<LineSeries>?)GetValue(SeriesProperty);
        if (series == null)
            yield break;

        foreach (var s in series)
        {
            if (!s.IsVisible)
                continue;
            foreach (var dp in s.DataPoints)
            {
                yield return dp.YValue;
            }
        }
    }

    #endregion

    /// <inheritdoc />
    protected override IList<ChartLegendItem>? CollectLegendItems()
    {
        var series = (ObservableCollection<LineSeries>?)GetValue(SeriesProperty);
        if (series == null || series.Count == 0) return null;
        var items = new List<ChartLegendItem>();
        for (int i = 0; i < series.Count; i++)
        {
            var s = series[i];
            if (!string.IsNullOrEmpty(s.Title))
                items.Add(new ChartLegendItem { Label = s.Title, Brush = s.Brush ?? GetSeriesBrush(i), IsVisible = s.IsVisible });
        }
        return items.Count > 0 ? items : null;
    }

    #region Rendering

    /// <inheritdoc />
    protected override void RenderPlotContent(DrawingContext dc, Rect plotArea,
        double xMin, double xMax, double yMin, double yMax)
    {
        var series = (ObservableCollection<LineSeries>?)GetValue(SeriesProperty);
        if (series == null || series.Count == 0)
            return;

        var xAxis = XAxis ?? new NumericAxis();
        var yAxis = YAxis ?? new NumericAxis();

        _pointCache.Clear();

        for (int si = 0; si < series.Count; si++)
        {
            var s = series[si];
            if (!s.IsVisible || s.DataPoints.Count == 0)
                continue;

            var seriesBrush = s.Brush ?? s.StrokeBrush ?? GetSeriesBrush(si);
            var strokeBrush = s.StrokeBrush ?? seriesBrush;
            var pen = new Pen(strokeBrush, s.StrokeThickness);
            if (s.DashArray is { Count: > 0 } dashArray)
            {
                pen.DashStyle = new DashStyle(dashArray, 0);
            }

            // Map data points to pixel coordinates
            var pixels = new List<Point>(s.DataPoints.Count);
            for (int idx = 0; idx < s.DataPoints.Count; idx++)
            {
                var dp = s.DataPoints[idx];
                double xVal;
                if (dp.XValue is double dv)
                    xVal = dv;
                else if (dp.XValue is string)
                    xVal = idx;
                else if (dp.XValue is IConvertible cv)
                {
                    try { xVal = cv.ToDouble(System.Globalization.CultureInfo.InvariantCulture); }
                    catch { xVal = idx; }
                }
                else
                    xVal = idx;

                var px = plotArea.Left + xAxis.ValueToPixel(xVal, xMin, xMax, plotArea.Width);
                var py = plotArea.Bottom - yAxis.ValueToPixel(dp.YValue, yMin, yMax, plotArea.Height);
                pixels.Add(new Point(px, py));
            }

            if (pixels.Count == 0)
                continue;

            _pointCache.Add((s, new List<Point>(pixels)));

            // Draw area fill if enabled
            if (ShowArea && pixels.Count >= 2)
            {
                RenderAreaFill(dc, plotArea, pixels, seriesBrush);
            }

            // Draw line path
            if (pixels.Count >= 2)
            {
                if (pixels.Count > 1000)
                {
                    RenderLineWithStreamGeometry(dc, pen, pixels);
                }
                else if (LineSmoothing && pixels.Count >= 3)
                {
                    RenderSmoothLine(dc, pen, pixels);
                }
                else
                {
                    RenderStraightLine(dc, pen, pixels);
                }
            }

            // Draw data point markers
            if (ShowDataPoints)
            {
                var radius = DataPointRadius;
                foreach (var pt in pixels)
                {
                    dc.DrawEllipse(seriesBrush, null, pt, radius, radius);
                }
            }
        }
    }

    #endregion

    #region Line Rendering Helpers

    private void RenderStraightLine(DrawingContext dc, Pen pen, List<Point> pixels)
    {
        var figure = new PathFigure { StartPoint = pixels[0], IsClosed = false, IsFilled = false };
        for (int i = 1; i < pixels.Count; i++)
        {
            figure.Segments.Add(new LineSegment(pixels[i], true));
        }
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, pen, geometry);
    }

    private void RenderSmoothLine(DrawingContext dc, Pen pen, List<Point> pixels)
    {
        var figure = new PathFigure { StartPoint = pixels[0], IsClosed = false, IsFilled = false };

        for (int i = 0; i < pixels.Count - 1; i++)
        {
            var p0 = pixels[Math.Max(i - 1, 0)];
            var p1 = pixels[i];
            var p2 = pixels[i + 1];
            var p3 = pixels[Math.Min(i + 2, pixels.Count - 1)];

            CatmullRomToBezier(p0, p1, p2, p3, out var cp1, out var cp2);
            figure.Segments.Add(new BezierSegment(cp1, cp2, p2, true));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, pen, geometry);
    }

    private void RenderLineWithStreamGeometry(DrawingContext dc, Pen pen, List<Point> pixels)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(pixels[0], false, false);

            if (LineSmoothing && pixels.Count >= 3)
            {
                for (int i = 0; i < pixels.Count - 1; i++)
                {
                    var p0 = pixels[Math.Max(i - 1, 0)];
                    var p1 = pixels[i];
                    var p2 = pixels[i + 1];
                    var p3 = pixels[Math.Min(i + 2, pixels.Count - 1)];

                    CatmullRomToBezier(p0, p1, p2, p3, out var cp1, out var cp2);
                    ctx.BezierTo(cp1, cp2, p2, true, false);
                }
            }
            else
            {
                for (int i = 1; i < pixels.Count; i++)
                {
                    ctx.LineTo(pixels[i], true, false);
                }
            }
        }

        dc.DrawGeometry(null, pen, geometry);
    }

    private void RenderAreaFill(DrawingContext dc, Rect plotArea, List<Point> pixels, Brush seriesBrush)
    {
        var opacity = Math.Clamp(AreaOpacity, 0.0, 1.0);
        Brush areaBrush;
        if (seriesBrush is SolidColorBrush scb)
        {
            var color = scb.Color;
            areaBrush = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B));
        }
        else
        {
            areaBrush = seriesBrush;
        }

        var figure = new PathFigure { StartPoint = new Point(pixels[0].X, plotArea.Bottom), IsClosed = true, IsFilled = true };

        // Line up to first data point
        figure.Segments.Add(new LineSegment(pixels[0], false));

        // Along the data points
        if (LineSmoothing && pixels.Count >= 3)
        {
            for (int i = 0; i < pixels.Count - 1; i++)
            {
                var p0 = pixels[Math.Max(i - 1, 0)];
                var p1 = pixels[i];
                var p2 = pixels[i + 1];
                var p3 = pixels[Math.Min(i + 2, pixels.Count - 1)];

                CatmullRomToBezier(p0, p1, p2, p3, out var cp1, out var cp2);
                figure.Segments.Add(new BezierSegment(cp1, cp2, p2, false));
            }
        }
        else
        {
            for (int i = 1; i < pixels.Count; i++)
            {
                figure.Segments.Add(new LineSegment(pixels[i], false));
            }
        }

        // Close back down to baseline
        figure.Segments.Add(new LineSegment(new Point(pixels[pixels.Count - 1].X, plotArea.Bottom), false));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(areaBrush, null, geometry);
    }

    /// <summary>
    /// Converts a Catmull-Rom spline segment (defined by 4 points) into a cubic Bezier control pair.
    /// Uses the standard Catmull-Rom to Bezier conversion with tension = 0 (uniform parameterisation).
    /// </summary>
    private static void CatmullRomToBezier(Point p0, Point p1, Point p2, Point p3,
        out Point controlPoint1, out Point controlPoint2)
    {
        const double alpha = 1.0 / 6.0;

        controlPoint1 = new Point(
            p1.X + alpha * (p2.X - p0.X),
            p1.Y + alpha * (p2.Y - p0.Y));

        controlPoint2 = new Point(
            p2.X - alpha * (p3.X - p1.X),
            p2.Y - alpha * (p3.Y - p1.Y));
    }

    #endregion

    #region Hit Testing

    private void OnLineChartMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsTooltipEnabled || _pointCache.Count == 0)
            return;

        var pos = e.GetPosition(this);
        var hitRadius = DataPointRadius + 4;

        foreach (var (series, pixels) in _pointCache)
        {
            for (int i = 0; i < pixels.Count; i++)
            {
                var pt = pixels[i];
                var dx = pos.X - pt.X;
                var dy = pos.Y - pt.Y;
                if (dx * dx + dy * dy <= hitRadius * hitRadius)
                {
                    var dp = i < series.DataPoints.Count ? series.DataPoints[i] : null;
                    var xLabel = dp?.XValue?.ToString() ?? i.ToString();
                    var yLabel = dp?.YValue.ToString("G6") ?? "";
                    ShowTooltip(pt.X, pt.Y, series, xLabel, yLabel);

                    RaiseEvent(new ChartDataPointEventArgs(DataPointHoverEvent, series, dp, i));
                    return;
                }
            }
        }

        HideTooltip();
    }

    private void OnLineChartMouseLeave(object sender, MouseEventArgs e)
    {
        HideTooltip();
    }

    #endregion
}
