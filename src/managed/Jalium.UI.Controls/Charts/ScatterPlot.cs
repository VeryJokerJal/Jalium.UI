using System.Collections.ObjectModel;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Displays data as a scatter plot with multiple point shapes, optional bubble sizing, and trend lines.
/// </summary>
public class ScatterPlot : AxisChartBase
{
    #region Private State

    /// <summary>
    /// Cached pixel positions and sizes for hit testing. Uses a spatial grid for large datasets.
    /// </summary>
    private readonly List<(ScatterSeries series, int index, Point pixel, double size)> _pointCache = new();

    /// <summary>
    /// Spatial grid for efficient hit testing on large datasets.
    /// Key = grid cell (row, col), Value = list of indices into _pointCache.
    /// </summary>
    private Dictionary<(int, int), List<int>>? _spatialGrid;
    private double _gridCellSize;
    private Rect _gridBounds;

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Series dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SeriesProperty =
        DependencyProperty.Register(nameof(Series), typeof(ObservableCollection<ScatterSeries>), typeof(ScatterPlot),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowTrendLine dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowTrendLineProperty =
        DependencyProperty.Register(nameof(ShowTrendLine), typeof(bool), typeof(ScatterPlot),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the TrendLineType dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty TrendLineTypeProperty =
        DependencyProperty.Register(nameof(TrendLineType), typeof(TrendLineType), typeof(ScatterPlot),
            new PropertyMetadata(TrendLineType.Linear, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the MinPointSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty MinPointSizeProperty =
        DependencyProperty.Register(nameof(MinPointSize), typeof(double), typeof(ScatterPlot),
            new PropertyMetadata(4.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the MaxPointSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty MaxPointSizeProperty =
        DependencyProperty.Register(nameof(MaxPointSize), typeof(double), typeof(ScatterPlot),
            new PropertyMetadata(20.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the SizeBindingPath dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SizeBindingPathProperty =
        DependencyProperty.Register(nameof(SizeBindingPath), typeof(string), typeof(ScatterPlot),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the collection of scatter series to render.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public ObservableCollection<ScatterSeries> Series
    {
        get
        {
            var s = (ObservableCollection<ScatterSeries>?)GetValue(SeriesProperty);
            if (s == null)
            {
                s = new ObservableCollection<ScatterSeries>();
                SetValue(SeriesProperty, s);
            }
            return s;
        }
        set => SetValue(SeriesProperty, value);
    }

    /// <summary>
    /// Gets or sets whether a trend line is displayed for each series.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowTrendLine
    {
        get => (bool)GetValue(ShowTrendLineProperty)!;
        set => SetValue(ShowTrendLineProperty, value);
    }

    /// <summary>
    /// Gets or sets the type of trend line to compute and display.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public TrendLineType TrendLineType
    {
        get => (TrendLineType)GetValue(TrendLineTypeProperty)!;
        set => SetValue(TrendLineTypeProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum point size for bubble mode rendering.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double MinPointSize
    {
        get => (double)GetValue(MinPointSizeProperty)!;
        set => SetValue(MinPointSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum point size for bubble mode rendering.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double MaxPointSize
    {
        get => (double)GetValue(MaxPointSizeProperty)!;
        set => SetValue(MaxPointSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the property path on data items to use for bubble sizing.
    /// When null, all points use MinPointSize.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string? SizeBindingPath
    {
        get => (string?)GetValue(SizeBindingPathProperty);
        set => SetValue(SizeBindingPathProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ScatterPlot"/> class.
    /// </summary>
    public ScatterPlot()
    {
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnScatterMouseMove));
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnScatterMouseLeave));
    }

    #endregion

    #region Automation

    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.ScatterPlotAutomationPeer(this);
    }

    #endregion

    #region Data Collection

    /// <inheritdoc />
    protected override IEnumerable<double> CollectXValues()
    {
        var series = (ObservableCollection<ScatterSeries>?)GetValue(SeriesProperty);
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
    protected override IEnumerable<double> CollectYValues()
    {
        var series = (ObservableCollection<ScatterSeries>?)GetValue(SeriesProperty);
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
        var series = (ObservableCollection<ScatterSeries>?)GetValue(SeriesProperty);
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
        var series = (ObservableCollection<ScatterSeries>?)GetValue(SeriesProperty);
        if (series == null || series.Count == 0)
            return;

        var xAxis = XAxis ?? new NumericAxis();
        var yAxis = YAxis ?? new NumericAxis();

        _pointCache.Clear();
        _spatialGrid = null;

        int totalPoints = 0;

        for (int si = 0; si < series.Count; si++)
        {
            var s = series[si];
            if (!s.IsVisible || s.DataPoints.Count == 0)
                continue;

            var seriesBrush = s.Brush ?? GetSeriesBrush(si);
            var strokeBrush = s.StrokeBrush;
            var strokePen = strokeBrush != null ? new Pen(strokeBrush, s.StrokeThickness) : null;

            // Resolve bubble sizes if SizeBindingPath is set
            var sizeValues = ResolveSizeValues(s);
            double sizeMin = double.MaxValue, sizeMax = double.MinValue;
            if (sizeValues != null)
            {
                foreach (var sv in sizeValues)
                {
                    if (sv < sizeMin) sizeMin = sv;
                    if (sv > sizeMax) sizeMax = sv;
                }
                if (Math.Abs(sizeMax - sizeMin) < 1e-15)
                {
                    sizeMin = 0;
                    sizeMax = sizeMax > 0 ? sizeMax : 1;
                }
            }

            // Collect data for trend line
            var trendXValues = new List<double>();
            var trendYValues = new List<double>();

            for (int i = 0; i < s.DataPoints.Count; i++)
            {
                var dp = s.DataPoints[i];
                double xVal;
                if (dp.XValue is double dv)
                    xVal = dv;
                else if (dp.XValue is string)
                    xVal = i;
                else if (dp.XValue is IConvertible cv)
                {
                    try { xVal = cv.ToDouble(System.Globalization.CultureInfo.InvariantCulture); }
                    catch { xVal = i; }
                }
                else
                    xVal = i;

                var px = plotArea.Left + xAxis.ValueToPixel(xVal, xMin, xMax, plotArea.Width);
                var py = plotArea.Bottom - yAxis.ValueToPixel(dp.YValue, yMin, yMax, plotArea.Height);

                // Determine point size
                double pointSize;
                if (sizeValues != null && i < sizeValues.Length)
                {
                    pointSize = ChartHelpers.MapValue(sizeValues[i], sizeMin, sizeMax, MinPointSize, MaxPointSize);
                }
                else
                {
                    pointSize = MinPointSize;
                }

                var pixel = new Point(px, py);

                // Draw the point
                DrawPoint(dc, pixel, pointSize, s.PointShape, seriesBrush, strokePen);

                _pointCache.Add((s, i, pixel, pointSize));
                totalPoints++;

                trendXValues.Add(xVal);
                trendYValues.Add(dp.YValue);
            }

            // Draw trend line if enabled
            if (ShowTrendLine && trendXValues.Count >= 2)
            {
                DrawTrendLine(dc, plotArea, trendXValues, trendYValues,
                    xAxis, yAxis, xMin, xMax, yMin, yMax, seriesBrush);
            }
        }

        // Build spatial grid for large datasets
        if (totalPoints > 500)
        {
            BuildSpatialGrid(plotArea);
        }
    }

    #endregion

    #region Point Drawing

    private static void DrawPoint(DrawingContext dc, Point center, double size,
        PointShape shape, Brush fill, Pen? stroke)
    {
        var halfSize = size / 2.0;

        switch (shape)
        {
            case PointShape.Circle:
                dc.DrawEllipse(fill, stroke, center, halfSize, halfSize);
                break;

            case PointShape.Square:
            {
                var rect = new Rect(center.X - halfSize, center.Y - halfSize, size, size);
                dc.DrawRectangle(fill, stroke, rect);
                break;
            }

            case PointShape.Diamond:
            {
                var figure = new PathFigure
                {
                    StartPoint = new Point(center.X, center.Y - halfSize),
                    IsClosed = true,
                    IsFilled = true
                };
                figure.Segments.Add(new LineSegment(new Point(center.X + halfSize, center.Y), true));
                figure.Segments.Add(new LineSegment(new Point(center.X, center.Y + halfSize), true));
                figure.Segments.Add(new LineSegment(new Point(center.X - halfSize, center.Y), true));

                var geometry = new PathGeometry();
                geometry.Figures.Add(figure);
                dc.DrawGeometry(fill, stroke, geometry);
                break;
            }

            case PointShape.Triangle:
            {
                var h = size * 0.866; // sqrt(3)/2 * size
                var figure = new PathFigure
                {
                    StartPoint = new Point(center.X, center.Y - h / 2.0),
                    IsClosed = true,
                    IsFilled = true
                };
                figure.Segments.Add(new LineSegment(new Point(center.X + halfSize, center.Y + h / 2.0), true));
                figure.Segments.Add(new LineSegment(new Point(center.X - halfSize, center.Y + h / 2.0), true));

                var geometry = new PathGeometry();
                geometry.Figures.Add(figure);
                dc.DrawGeometry(fill, stroke, geometry);
                break;
            }
        }
    }

    #endregion

    #region Trend Line

    private void DrawTrendLine(DrawingContext dc, Rect plotArea,
        List<double> xValues, List<double> yValues,
        ChartAxis xAxis, ChartAxis yAxis,
        double xMin, double xMax, double yMin, double yMax,
        Brush seriesBrush)
    {
        if (xValues.Count < 2)
            return;

        var trendLineType = TrendLineType;

        // Compute linear regression (least squares)
        double sumX = 0, sumY = 0, sumXX = 0, sumXY = 0;
        int n = xValues.Count;

        for (int i = 0; i < n; i++)
        {
            sumX += xValues[i];
            sumY += yValues[i];
            sumXX += xValues[i] * xValues[i];
            sumXY += xValues[i] * yValues[i];
        }

        double slope, intercept;

        if (trendLineType == TrendLineType.Linear)
        {
            var denom = n * sumXX - sumX * sumX;
            if (Math.Abs(denom) < 1e-15)
                return;

            slope = (n * sumXY - sumX * sumY) / denom;
            intercept = (sumY - slope * sumX) / n;
        }
        else if (trendLineType == TrendLineType.MovingAverage)
        {
            // Draw moving average as a polyline
            int window = Math.Max(2, n / 5);
            DrawMovingAverageLine(dc, plotArea, xValues, yValues, window,
                xAxis, yAxis, xMin, xMax, yMin, yMax, seriesBrush);
            return;
        }
        else
        {
            // For Polynomial and Exponential, fall back to linear for now
            // (a full polynomial fit requires matrix inversion which adds complexity)
            var denom = n * sumXX - sumX * sumX;
            if (Math.Abs(denom) < 1e-15)
                return;

            slope = (n * sumXY - sumX * sumY) / denom;
            intercept = (sumY - slope * sumX) / n;
        }

        // Dashed pen for the trend line
        var dashPen = new Pen(seriesBrush, 1.5);
        dashPen.DashStyle = new DashStyle(new DoubleCollection(new[] { 6.0, 3.0 }), 0);

        // Draw the linear regression line across the plot area
        var lineStartX = xMin;
        var lineEndX = xMax;
        var lineStartY = slope * lineStartX + intercept;
        var lineEndY = slope * lineEndX + intercept;

        var pxStart = new Point(
            plotArea.Left + xAxis.ValueToPixel(lineStartX, xMin, xMax, plotArea.Width),
            plotArea.Bottom - yAxis.ValueToPixel(lineStartY, yMin, yMax, plotArea.Height));
        var pxEnd = new Point(
            plotArea.Left + xAxis.ValueToPixel(lineEndX, xMin, xMax, plotArea.Width),
            plotArea.Bottom - yAxis.ValueToPixel(lineEndY, yMin, yMax, plotArea.Height));

        dc.DrawLine(dashPen, pxStart, pxEnd);
    }

    private void DrawMovingAverageLine(DrawingContext dc, Rect plotArea,
        List<double> xValues, List<double> yValues, int window,
        ChartAxis xAxis, ChartAxis yAxis,
        double xMin, double xMax, double yMin, double yMax,
        Brush seriesBrush)
    {
        // Sort by X values
        var indices = new int[xValues.Count];
        for (int i = 0; i < indices.Length; i++) indices[i] = i;
        Array.Sort(indices, (a, b) => xValues[a].CompareTo(xValues[b]));

        var sortedX = new double[xValues.Count];
        var sortedY = new double[yValues.Count];
        for (int i = 0; i < indices.Length; i++)
        {
            sortedX[i] = xValues[indices[i]];
            sortedY[i] = yValues[indices[i]];
        }

        var maPoints = new List<Point>();
        for (int i = 0; i < sortedX.Length; i++)
        {
            int start = Math.Max(0, i - window / 2);
            int end = Math.Min(sortedX.Length - 1, i + window / 2);
            double sum = 0;
            int count = 0;
            for (int j = start; j <= end; j++)
            {
                sum += sortedY[j];
                count++;
            }
            var avgY = sum / count;
            var px = plotArea.Left + xAxis.ValueToPixel(sortedX[i], xMin, xMax, plotArea.Width);
            var py = plotArea.Bottom - yAxis.ValueToPixel(avgY, yMin, yMax, plotArea.Height);
            maPoints.Add(new Point(px, py));
        }

        if (maPoints.Count < 2)
            return;

        var dashPen = new Pen(seriesBrush, 1.5);
        dashPen.DashStyle = new DashStyle(new DoubleCollection(new[] { 6.0, 3.0 }), 0);

        var figure = new PathFigure { StartPoint = maPoints[0], IsClosed = false, IsFilled = false };
        for (int i = 1; i < maPoints.Count; i++)
        {
            figure.Segments.Add(new LineSegment(maPoints[i], true));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        dc.DrawGeometry(null, dashPen, geometry);
    }

    #endregion

    #region Size Value Resolution

    private double[]? ResolveSizeValues(ScatterSeries s)
    {
        // Check series-level SizeBindingPath first, then chart-level
        var bindingPath = s.SizeBindingPath ?? SizeBindingPath;
        if (string.IsNullOrEmpty(bindingPath))
            return null;

        var source = s.ItemsSource;
        if (source == null)
        {
            // If no ItemsSource, try to use the Tag property of data points as size
            var sizes = new double[s.DataPoints.Count];
            bool hasAny = false;
            for (int i = 0; i < s.DataPoints.Count; i++)
            {
                var tag = s.DataPoints[i].Tag;
                if (tag is double dv)
                {
                    sizes[i] = dv;
                    hasAny = true;
                }
                else if (tag is IConvertible cv)
                {
                    try
                    {
                        sizes[i] = cv.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
                        hasAny = true;
                    }
                    catch
                    {
                        sizes[i] = 0;
                    }
                }
                else
                {
                    sizes[i] = 0;
                }
            }
            return hasAny ? sizes : null;
        }

        // Reflect the binding path on each item
        var result = new List<double>();
        System.Reflection.PropertyInfo? propInfo = null;
        bool propResolved = false;

        foreach (var item in source)
        {
            if (item == null)
            {
                result.Add(0);
                continue;
            }

            if (!propResolved)
            {
                propInfo = item.GetType().GetProperty(bindingPath);
                propResolved = true;
            }

            if (propInfo != null)
            {
                var val = propInfo.GetValue(item);
                if (val is double dv)
                    result.Add(dv);
                else if (val is IConvertible cv)
                {
                    try { result.Add(cv.ToDouble(System.Globalization.CultureInfo.InvariantCulture)); }
                    catch { result.Add(0); }
                }
                else
                    result.Add(0);
            }
            else
            {
                result.Add(0);
            }
        }

        return result.Count > 0 ? result.ToArray() : null;
    }

    #endregion

    #region Spatial Grid for Hit Testing

    private void BuildSpatialGrid(Rect plotArea)
    {
        _gridCellSize = Math.Max(MaxPointSize * 2, 30);
        _gridBounds = plotArea;
        _spatialGrid = new Dictionary<(int, int), List<int>>();

        for (int i = 0; i < _pointCache.Count; i++)
        {
            var (_, _, pixel, _) = _pointCache[i];
            var col = (int)((pixel.X - plotArea.Left) / _gridCellSize);
            var row = (int)((pixel.Y - plotArea.Top) / _gridCellSize);
            var key = (row, col);

            if (!_spatialGrid.TryGetValue(key, out var list))
            {
                list = new List<int>();
                _spatialGrid[key] = list;
            }
            list.Add(i);
        }
    }

    #endregion

    #region Hit Testing

    private void OnScatterMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsTooltipEnabled || _pointCache.Count == 0)
            return;

        var pos = e.GetPosition(this);
        int hitIndex = -1;

        if (_spatialGrid != null)
        {
            // Use spatial grid for efficient lookup
            var col = (int)((pos.X - _gridBounds.Left) / _gridCellSize);
            var row = (int)((pos.Y - _gridBounds.Top) / _gridCellSize);

            double bestDistSq = double.MaxValue;

            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc2 = -1; dc2 <= 1; dc2++)
                {
                    var key = (row + dr, col + dc2);
                    if (_spatialGrid.TryGetValue(key, out var indices))
                    {
                        foreach (var idx in indices)
                        {
                            var (_, _, pixel, size) = _pointCache[idx];
                            var ddx = pos.X - pixel.X;
                            var ddy = pos.Y - pixel.Y;
                            var distSq = ddx * ddx + ddy * ddy;
                            var hitRadius = size / 2.0 + 2;
                            if (distSq <= hitRadius * hitRadius && distSq < bestDistSq)
                            {
                                bestDistSq = distSq;
                                hitIndex = idx;
                            }
                        }
                    }
                }
            }
        }
        else
        {
            // Linear search for small datasets
            double bestDistSq = double.MaxValue;
            for (int i = 0; i < _pointCache.Count; i++)
            {
                var (_, _, pixel, size) = _pointCache[i];
                var ddx = pos.X - pixel.X;
                var ddy = pos.Y - pixel.Y;
                var distSq = ddx * ddx + ddy * ddy;
                var hitRadius = size / 2.0 + 2;
                if (distSq <= hitRadius * hitRadius && distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    hitIndex = i;
                }
            }
        }

        if (hitIndex >= 0)
        {
            var (series, dpIndex, pixel, _) = _pointCache[hitIndex];
            var dp = dpIndex < series.DataPoints.Count ? series.DataPoints[dpIndex] : null;
            var xLabel = dp?.XValue?.ToString() ?? dpIndex.ToString();
            var yLabel = dp?.YValue.ToString("G6") ?? "";
            ShowTooltip(pixel.X, pixel.Y, series, xLabel, yLabel);

            RaiseEvent(new ChartDataPointEventArgs(DataPointHoverEvent, series, dp, dpIndex));
        }
        else
        {
            HideTooltip();
        }
    }

    private void OnScatterMouseLeave(object sender, MouseEventArgs e)
    {
        HideTooltip();
    }

    #endregion
}
