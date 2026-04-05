using System.Collections.ObjectModel;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Displays data as grouped, stacked, or percentage-stacked bars with optional value labels and rounded corners.
/// </summary>
public class BarChart : AxisChartBase
{
    #region Private State

    /// <summary>
    /// Cached bar rectangles for hit testing.
    /// </summary>
    private readonly List<(BarSeries series, int index, Rect rect)> _barRects = new();

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Series dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SeriesProperty =
        DependencyProperty.Register(nameof(Series), typeof(ObservableCollection<BarSeries>), typeof(BarChart),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(BarChart),
            new PropertyMetadata(Orientation.Vertical, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the BarMode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BarModeProperty =
        DependencyProperty.Register(nameof(BarMode), typeof(BarMode), typeof(BarChart),
            new PropertyMetadata(BarMode.Grouped, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the BarSpacing dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty BarSpacingProperty =
        DependencyProperty.Register(nameof(BarSpacing), typeof(double), typeof(BarChart),
            new PropertyMetadata(2.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the GroupSpacing dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty GroupSpacingProperty =
        DependencyProperty.Register(nameof(GroupSpacing), typeof(double), typeof(BarChart),
            new PropertyMetadata(8.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the BarCornerRadius dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BarCornerRadiusProperty =
        DependencyProperty.Register(nameof(BarCornerRadius), typeof(CornerRadius), typeof(BarChart),
            new PropertyMetadata(new CornerRadius(2, 2, 0, 0), OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowValueLabels dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowValueLabelsProperty =
        DependencyProperty.Register(nameof(ShowValueLabels), typeof(bool), typeof(BarChart),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the collection of bar series to render.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public ObservableCollection<BarSeries> Series
    {
        get
        {
            var s = (ObservableCollection<BarSeries>?)GetValue(SeriesProperty);
            if (s == null)
            {
                s = new ObservableCollection<BarSeries>();
                SetValue(SeriesProperty, s);
            }
            return s;
        }
        set => SetValue(SeriesProperty, value);
    }

    /// <summary>
    /// Gets or sets the bar orientation (Vertical or Horizontal).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets how multiple series bars are arranged (Grouped, Stacked, StackedPercentage).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public BarMode BarMode
    {
        get => (BarMode)GetValue(BarModeProperty)!;
        set => SetValue(BarModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the spacing in pixels between bars within a group.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double BarSpacing
    {
        get => (double)GetValue(BarSpacingProperty)!;
        set => SetValue(BarSpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets the spacing in pixels between groups of bars.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double GroupSpacing
    {
        get => (double)GetValue(GroupSpacingProperty)!;
        set => SetValue(GroupSpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets the corner radius for bar rendering.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public CornerRadius BarCornerRadius
    {
        get => (CornerRadius)GetValue(BarCornerRadiusProperty)!;
        set => SetValue(BarCornerRadiusProperty, value);
    }

    /// <summary>
    /// Gets or sets whether value labels are shown above each bar.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowValueLabels
    {
        get => (bool)GetValue(ShowValueLabelsProperty)!;
        set => SetValue(ShowValueLabelsProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="BarChart"/> class.
    /// </summary>
    public BarChart()
    {
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnBarChartMouseMove));
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnBarChartMouseLeave));
    }

    #endregion

    #region Automation

    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.BarChartAutomationPeer(this);
    }

    #endregion

    #region Data Collection

    /// <inheritdoc />
    protected override IEnumerable<double> CollectXValues()
    {
        var series = (ObservableCollection<BarSeries>?)GetValue(SeriesProperty);
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
        var series = (ObservableCollection<BarSeries>?)GetValue(SeriesProperty);
        if (series == null) return null;

        foreach (var s in series)
        {
            if (!s.IsVisible) continue;
            foreach (var dp in s.DataPoints)
            {
                if (dp.XValue is string)
                {
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
                return null;
            }
        }
        return null;
    }

    /// <inheritdoc />
    protected override IEnumerable<double> CollectYValues()
    {
        var series = (ObservableCollection<BarSeries>?)GetValue(SeriesProperty);
        if (series == null)
            yield break;

        var mode = BarMode;

        if (mode == BarMode.Stacked || mode == BarMode.StackedPercentage)
        {
            // For stacked, we need the accumulated totals
            var visibleSeries = new List<BarSeries>();
            foreach (var s in series)
            {
                if (s.IsVisible)
                    visibleSeries.Add(s);
            }

            if (visibleSeries.Count > 0)
            {
                int maxCount = 0;
                foreach (var s in visibleSeries)
                {
                    if (s.DataPoints.Count > maxCount)
                        maxCount = s.DataPoints.Count;
                }

                for (int ci = 0; ci < maxCount; ci++)
                {
                    double sum = 0;
                    foreach (var s in visibleSeries)
                    {
                        if (ci < s.DataPoints.Count)
                            sum += Math.Abs(s.DataPoints[ci].YValue);
                    }
                    yield return sum;
                }

                yield return 0; // Include zero baseline
            }
        }
        else
        {
            // Grouped: just collect all individual Y values
            yield return 0; // Include zero baseline
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
    }

    #endregion

    /// <inheritdoc />
    protected override IList<ChartLegendItem>? CollectLegendItems()
    {
        var series = (ObservableCollection<BarSeries>?)GetValue(SeriesProperty);
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
        var series = (ObservableCollection<BarSeries>?)GetValue(SeriesProperty);
        if (series == null || series.Count == 0)
            return;

        _barRects.Clear();

        var visibleSeries = new List<BarSeries>();
        var seriesIndices = new List<int>();
        for (int i = 0; i < series.Count; i++)
        {
            if (series[i].IsVisible && series[i].DataPoints.Count > 0)
            {
                visibleSeries.Add(series[i]);
                seriesIndices.Add(i);
            }
        }

        if (visibleSeries.Count == 0)
            return;

        var xAxis = XAxis ?? new NumericAxis();
        var yAxis = YAxis ?? new NumericAxis();
        var mode = BarMode;
        var isVertical = Orientation == Orientation.Vertical;
        var cornerRadius = BarCornerRadius;

        // Determine the maximum number of categories
        int categoryCount = 0;
        foreach (var s in visibleSeries)
        {
            if (s.DataPoints.Count > categoryCount)
                categoryCount = s.DataPoints.Count;
        }

        if (categoryCount == 0)
            return;

        // Compute band width for each category
        double totalCategoryAxis = isVertical ? plotArea.Width : plotArea.Height;
        double bandWidth = totalCategoryAxis / categoryCount;
        double usableBand = bandWidth - GroupSpacing;
        if (usableBand < 2) usableBand = 2;

        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var labelBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220));

        for (int ci = 0; ci < categoryCount; ci++)
        {
            double bandStart = ci * bandWidth + GroupSpacing / 2.0;

            if (mode == BarMode.Grouped)
            {
                RenderGroupedBars(dc, plotArea, visibleSeries, seriesIndices,
                    ci, bandStart, usableBand, xAxis, yAxis,
                    xMin, xMax, yMin, yMax, isVertical, cornerRadius, fontFamily, labelBrush);
            }
            else
            {
                RenderStackedBars(dc, plotArea, visibleSeries, seriesIndices,
                    ci, bandStart, usableBand, xAxis, yAxis,
                    xMin, xMax, yMin, yMax, isVertical, cornerRadius, fontFamily, labelBrush,
                    mode == BarMode.StackedPercentage);
            }
        }
    }

    private void RenderGroupedBars(DrawingContext dc, Rect plotArea,
        List<BarSeries> visibleSeries, List<int> seriesIndices,
        int categoryIndex, double bandStart, double usableBand,
        ChartAxis xAxis, ChartAxis yAxis,
        double xMin, double xMax, double yMin, double yMax,
        bool isVertical, CornerRadius cornerRadius,
        string fontFamily, Brush labelBrush)
    {
        int seriesCount = visibleSeries.Count;
        double barWidth = (usableBand - (seriesCount - 1) * BarSpacing) / seriesCount;
        if (barWidth < 1) barWidth = 1;

        for (int si = 0; si < seriesCount; si++)
        {
            var s = visibleSeries[si];
            if (categoryIndex >= s.DataPoints.Count)
                continue;

            var dp = s.DataPoints[categoryIndex];
            var value = dp.YValue;
            var seriesBrush = s.Brush ?? GetSeriesBrush(seriesIndices[si]);
            var strokeBrush = s.StrokeBrush;
            var strokePen = strokeBrush != null ? new Pen(strokeBrush, s.StrokeThickness) : null;

            double barOffset = bandStart + si * (barWidth + BarSpacing);

            Rect barRect;
            if (isVertical)
            {
                var baseline = plotArea.Bottom - yAxis.ValueToPixel(0, yMin, yMax, plotArea.Height);
                var valuePixel = plotArea.Bottom - yAxis.ValueToPixel(value, yMin, yMax, plotArea.Height);

                double top = Math.Min(baseline, valuePixel);
                double bottom = Math.Max(baseline, valuePixel);
                double height = bottom - top;
                if (height < 0.5) height = 0.5;

                barRect = new Rect(plotArea.Left + barOffset, top, barWidth, height);
            }
            else
            {
                var baseline = plotArea.Left + xAxis.ValueToPixel(0, xMin, xMax, plotArea.Width);
                var valuePixel = plotArea.Left + xAxis.ValueToPixel(value, xMin, xMax, plotArea.Width);

                double left = Math.Min(baseline, valuePixel);
                double right = Math.Max(baseline, valuePixel);
                double width = right - left;
                if (width < 0.5) width = 0.5;

                barRect = new Rect(left, plotArea.Top + barOffset, width, barWidth);
            }

            // Clamp bar rect to plot area
            barRect = ClampToPlotArea(barRect, plotArea);

            dc.DrawRoundedRectangle(seriesBrush, strokePen, barRect, cornerRadius);
            _barRects.Add((s, categoryIndex, barRect));

            if (ShowValueLabels)
            {
                DrawValueLabel(dc, barRect, value, isVertical, fontFamily, labelBrush);
            }
        }
    }

    private void RenderStackedBars(DrawingContext dc, Rect plotArea,
        List<BarSeries> visibleSeries, List<int> seriesIndices,
        int categoryIndex, double bandStart, double usableBand,
        ChartAxis xAxis, ChartAxis yAxis,
        double xMin, double xMax, double yMin, double yMax,
        bool isVertical, CornerRadius cornerRadius,
        string fontFamily, Brush labelBrush, bool isPercentage)
    {
        // Compute total for percentage mode
        double total = 0;
        if (isPercentage)
        {
            foreach (var s in visibleSeries)
            {
                if (categoryIndex < s.DataPoints.Count)
                    total += Math.Abs(s.DataPoints[categoryIndex].YValue);
            }
            if (total < 1e-15) total = 1;
        }

        double positiveAccum = 0;
        double negativeAccum = 0;

        for (int si = 0; si < visibleSeries.Count; si++)
        {
            var s = visibleSeries[si];
            if (categoryIndex >= s.DataPoints.Count)
                continue;

            var dp = s.DataPoints[categoryIndex];
            var rawValue = dp.YValue;
            var seriesBrush = s.Brush ?? GetSeriesBrush(seriesIndices[si]);
            var strokeBrush = s.StrokeBrush;
            var strokePen = strokeBrush != null ? new Pen(strokeBrush, s.StrokeThickness) : null;

            double value = isPercentage ? (rawValue / total * 100.0) : rawValue;

            double stackBase, stackTop;
            if (value >= 0)
            {
                stackBase = positiveAccum;
                stackTop = positiveAccum + value;
                positiveAccum = stackTop;
            }
            else
            {
                stackTop = negativeAccum;
                stackBase = negativeAccum + value;
                negativeAccum = stackBase;
            }

            double effectiveYMin = isPercentage ? 0 : yMin;
            double effectiveYMax = isPercentage ? 100 : yMax;

            Rect barRect;
            if (isVertical)
            {
                var basePixel = plotArea.Bottom - yAxis.ValueToPixel(stackBase, effectiveYMin, effectiveYMax, plotArea.Height);
                var topPixel = plotArea.Bottom - yAxis.ValueToPixel(stackTop, effectiveYMin, effectiveYMax, plotArea.Height);

                double top = Math.Min(basePixel, topPixel);
                double bottom = Math.Max(basePixel, topPixel);
                double height = bottom - top;
                if (height < 0.5) height = 0.5;

                barRect = new Rect(plotArea.Left + bandStart, top, usableBand, height);
            }
            else
            {
                var basePixel = plotArea.Left + xAxis.ValueToPixel(stackBase, effectiveYMin, effectiveYMax, plotArea.Width);
                var topPixel = plotArea.Left + xAxis.ValueToPixel(stackTop, effectiveYMin, effectiveYMax, plotArea.Width);

                double left = Math.Min(basePixel, topPixel);
                double right = Math.Max(basePixel, topPixel);
                double width = right - left;
                if (width < 0.5) width = 0.5;

                barRect = new Rect(left, plotArea.Top + bandStart, width, usableBand);
            }

            barRect = ClampToPlotArea(barRect, plotArea);

            // Only apply corner radius to the topmost bar in the stack
            bool isTopOfStack = (si == visibleSeries.Count - 1) ||
                                (categoryIndex >= visibleSeries[visibleSeries.Count - 1].DataPoints.Count);
            var cr = isTopOfStack ? cornerRadius : new CornerRadius(0);

            dc.DrawRoundedRectangle(seriesBrush, strokePen, barRect, cr);
            _barRects.Add((s, categoryIndex, barRect));

            if (ShowValueLabels)
            {
                DrawValueLabel(dc, barRect, rawValue, isVertical, fontFamily, labelBrush);
            }
        }
    }

    private void DrawValueLabel(DrawingContext dc, Rect barRect, double value,
        bool isVertical, string fontFamily, Brush labelBrush)
    {
        var text = value.ToString("G6");
        var ft = new FormattedText(text, fontFamily, 10.0)
        {
            Foreground = labelBrush
        };
        TextMeasurement.MeasureText(ft);

        double x, y;
        if (isVertical)
        {
            x = barRect.Left + (barRect.Width - ft.Width) / 2.0;
            y = barRect.Top - ft.Height - 2;
        }
        else
        {
            x = barRect.Right + 4;
            y = barRect.Top + (barRect.Height - ft.Height) / 2.0;
        }

        dc.DrawText(ft, new Point(x, y));
    }

    private static Rect ClampToPlotArea(Rect rect, Rect plotArea)
    {
        double left = Math.Max(rect.Left, plotArea.Left);
        double top = Math.Max(rect.Top, plotArea.Top);
        double right = Math.Min(rect.Right, plotArea.Right);
        double bottom = Math.Min(rect.Bottom, plotArea.Bottom);

        double w = Math.Max(0, right - left);
        double h = Math.Max(0, bottom - top);
        return new Rect(left, top, w, h);
    }

    #endregion

    #region Hit Testing

    private void OnBarChartMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsTooltipEnabled || _barRects.Count == 0)
            return;

        var pos = e.GetPosition(this);

        foreach (var (series, index, rect) in _barRects)
        {
            if (rect.Contains(pos))
            {
                var dp = index < series.DataPoints.Count ? series.DataPoints[index] : null;
                var xLabel = dp?.Label ?? dp?.XValue?.ToString() ?? index.ToString();
                var yLabel = dp?.YValue.ToString("G6") ?? "";
                ShowTooltip(pos.X, pos.Y, series, xLabel, yLabel);

                RaiseEvent(new ChartDataPointEventArgs(DataPointHoverEvent, series, dp, index));
                return;
            }
        }

        HideTooltip();
    }

    private void OnBarChartMouseLeave(object sender, MouseEventArgs e)
    {
        HideTooltip();
    }

    #endregion
}
