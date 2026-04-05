using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Displays data as a pie or donut chart with optional slice explosion, labels, and connectors.
/// </summary>
public class PieChart : ChartBase
{
    #region Private State

    /// <summary>
    /// Cached slice geometry data for hit testing.
    /// </summary>
    private readonly List<SliceHitInfo> _sliceCache = new();

    private struct SliceHitInfo
    {
        public int Index;
        public double StartAngle;
        public double SweepAngle;
        public Point Center;
        public double OuterRadius;
        public double InnerRadius;
    }

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Series dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SeriesProperty =
        DependencyProperty.Register(nameof(Series), typeof(PieSeries), typeof(PieChart),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the InnerRadiusRatio dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty InnerRadiusRatioProperty =
        DependencyProperty.Register(nameof(InnerRadiusRatio), typeof(double), typeof(PieChart),
            new PropertyMetadata(0.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the StartAngle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StartAngleProperty =
        DependencyProperty.Register(nameof(StartAngle), typeof(double), typeof(PieChart),
            new PropertyMetadata(-90.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ExplodeOffset dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ExplodeOffsetProperty =
        DependencyProperty.Register(nameof(ExplodeOffset), typeof(double), typeof(PieChart),
            new PropertyMetadata(10.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowLabels dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowLabelsProperty =
        DependencyProperty.Register(nameof(ShowLabels), typeof(bool), typeof(PieChart),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the LabelPosition dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty LabelPositionProperty =
        DependencyProperty.Register(nameof(LabelPosition), typeof(PieLabelPosition), typeof(PieChart),
            new PropertyMetadata(PieLabelPosition.Outside, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the LabelFormat dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty LabelFormatProperty =
        DependencyProperty.Register(nameof(LabelFormat), typeof(string), typeof(PieChart),
            new PropertyMetadata("{0}: {1:P0}", OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the pie series containing the data points.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public PieSeries Series
    {
        get
        {
            var s = (PieSeries?)GetValue(SeriesProperty);
            if (s == null)
            {
                s = new PieSeries();
                SetValue(SeriesProperty, s);
            }
            return s;
        }
        set => SetValue(SeriesProperty, value);
    }

    /// <summary>
    /// Gets or sets the inner radius as a fraction of the outer radius (0.0 = pie, >0.0 = donut).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double InnerRadiusRatio
    {
        get => (double)GetValue(InnerRadiusRatioProperty)!;
        set => SetValue(InnerRadiusRatioProperty, value);
    }

    /// <summary>
    /// Gets or sets the angle in degrees at which the first slice starts (-90 = 12 o'clock).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double StartAngle
    {
        get => (double)GetValue(StartAngleProperty)!;
        set => SetValue(StartAngleProperty, value);
    }

    /// <summary>
    /// Gets or sets the distance in pixels that exploded slices are pulled from center.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double ExplodeOffset
    {
        get => (double)GetValue(ExplodeOffsetProperty)!;
        set => SetValue(ExplodeOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets whether slice labels are displayed.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowLabels
    {
        get => (bool)GetValue(ShowLabelsProperty)!;
        set => SetValue(ShowLabelsProperty, value);
    }

    /// <summary>
    /// Gets or sets the label position relative to slices.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public PieLabelPosition LabelPosition
    {
        get => (PieLabelPosition)GetValue(LabelPositionProperty)!;
        set => SetValue(LabelPositionProperty, value);
    }

    /// <summary>
    /// Gets or sets the format string for slice labels. {0} = label, {1} = percentage.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string LabelFormat
    {
        get => (string)GetValue(LabelFormatProperty)!;
        set => SetValue(LabelFormatProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="PieChart"/> class.
    /// </summary>
    public PieChart()
    {
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnPieChartMouseMove));
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnPieChartMouseLeave));
    }

    #endregion

    #region Automation

    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.PieChartAutomationPeer(this);
    }

    #endregion

    /// <inheritdoc />
    protected override IList<ChartLegendItem>? CollectLegendItems()
    {
        var pieSeries = Series;
        if (pieSeries?.DataPoints == null || pieSeries.DataPoints.Count == 0) return null;
        var items = new List<ChartLegendItem>();
        for (int i = 0; i < pieSeries.DataPoints.Count; i++)
        {
            var dp = pieSeries.DataPoints[i];
            if (!string.IsNullOrEmpty(dp.Label))
                items.Add(new ChartLegendItem { Label = dp.Label, Brush = dp.Brush ?? GetSeriesBrush(i) });
        }
        return items.Count > 0 ? items : null;
    }

    #region Rendering

    /// <inheritdoc />
    protected override void RenderChart(DrawingContext dc, Rect plotArea)
    {
        var series = (PieSeries?)GetValue(SeriesProperty);
        if (series == null || series.DataPoints.Count == 0)
            return;

        _sliceCache.Clear();

        var dataPoints = series.DataPoints;

        // Compute total value
        double total = 0;
        foreach (var dp in dataPoints)
        {
            if (dp.Value > 0)
                total += dp.Value;
        }

        if (total < 1e-15)
            return;

        // Compute center and radius from plot area
        var centerX = plotArea.Left + plotArea.Width / 2.0;
        var centerY = plotArea.Top + plotArea.Height / 2.0;
        var outerRadius = Math.Min(plotArea.Width, plotArea.Height) / 2.0;

        // Reserve space for labels if shown outside
        if (ShowLabels && LabelPosition == PieLabelPosition.Outside)
        {
            outerRadius = Math.Max(20, outerRadius - 40);
        }
        else if (ShowLabels && LabelPosition == PieLabelPosition.Connector)
        {
            outerRadius = Math.Max(20, outerRadius - 60);
        }

        var innerRadius = outerRadius * Math.Clamp(InnerRadiusRatio, 0.0, 0.95);
        var center = new Point(centerX, centerY);
        var borderPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), 1);

        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var labelFgBrush = Foreground ?? new SolidColorBrush(Color.FromRgb(220, 220, 220));

        double currentAngle = StartAngle;

        for (int i = 0; i < dataPoints.Count; i++)
        {
            var dp = dataPoints[i];
            if (dp.Value <= 0)
                continue;

            double sweepAngle = dp.Value / total * 360.0;
            var sliceBrush = dp.Brush ?? GetSeriesBrush(i);

            // Compute slice center offset for exploded slices
            var bisectorAngle = currentAngle + sweepAngle / 2.0;
            var bisectorRad = bisectorAngle * Math.PI / 180.0;
            var sliceCenter = center;

            if (dp.IsExploded)
            {
                var offset = ExplodeOffset;
                sliceCenter = new Point(
                    center.X + Math.Cos(bisectorRad) * offset,
                    center.Y + Math.Sin(bisectorRad) * offset);
            }

            // Build slice geometry
            var sliceGeometry = BuildSliceGeometry(sliceCenter, outerRadius, innerRadius, currentAngle, sweepAngle);
            dc.DrawGeometry(sliceBrush, borderPen, sliceGeometry);

            // Cache for hit testing
            _sliceCache.Add(new SliceHitInfo
            {
                Index = i,
                StartAngle = currentAngle,
                SweepAngle = sweepAngle,
                Center = sliceCenter,
                OuterRadius = outerRadius,
                InnerRadius = innerRadius
            });

            // Draw labels
            if (ShowLabels)
            {
                var label = dp.Label ?? $"Item {i + 1}";
                var percentage = dp.Value / total;
                string labelText;
                try
                {
                    labelText = string.Format(LabelFormat, label, percentage);
                }
                catch (FormatException)
                {
                    labelText = $"{label}: {percentage:P0}";
                }

                DrawSliceLabel(dc, sliceCenter, outerRadius, innerRadius,
                    currentAngle, sweepAngle, labelText, fontFamily, labelFgBrush);
            }

            currentAngle += sweepAngle;
        }
    }

    #endregion

    #region Geometry Helpers

    private static PathGeometry BuildSliceGeometry(Point center, double outerRadius, double innerRadius,
        double startAngleDeg, double sweepAngleDeg)
    {
        var startRad = startAngleDeg * Math.PI / 180.0;
        var endRad = (startAngleDeg + sweepAngleDeg) * Math.PI / 180.0;

        var outerStart = new Point(
            center.X + Math.Cos(startRad) * outerRadius,
            center.Y + Math.Sin(startRad) * outerRadius);
        var outerEnd = new Point(
            center.X + Math.Cos(endRad) * outerRadius,
            center.Y + Math.Sin(endRad) * outerRadius);

        bool isLargeArc = sweepAngleDeg > 180.0;
        var outerArcSize = new Size(outerRadius, outerRadius);

        var figure = new PathFigure { IsClosed = true, IsFilled = true };

        if (innerRadius > 0.5)
        {
            // Donut slice
            var innerStart = new Point(
                center.X + Math.Cos(startRad) * innerRadius,
                center.Y + Math.Sin(startRad) * innerRadius);
            var innerEnd = new Point(
                center.X + Math.Cos(endRad) * innerRadius,
                center.Y + Math.Sin(endRad) * innerRadius);
            var innerArcSize = new Size(innerRadius, innerRadius);

            figure.StartPoint = outerStart;
            // Outer arc from start to end
            figure.Segments.Add(new ArcSegment(outerEnd, outerArcSize, 0, isLargeArc, SweepDirection.Clockwise, true));
            // Line to inner end
            figure.Segments.Add(new LineSegment(innerEnd, true));
            // Inner arc from end back to start (counter-clockwise)
            figure.Segments.Add(new ArcSegment(innerStart, innerArcSize, 0, isLargeArc, SweepDirection.Counterclockwise, true));
            // Close back to outer start (implicit)
        }
        else
        {
            // Full pie slice (wedge)
            // Handle full circle (single slice = 100%)
            if (sweepAngleDeg >= 359.99)
            {
                // Draw as two semicircles to avoid degenerate arc
                var midRad = startRad + Math.PI;
                var outerMid = new Point(
                    center.X + Math.Cos(midRad) * outerRadius,
                    center.Y + Math.Sin(midRad) * outerRadius);

                figure.StartPoint = outerStart;
                figure.Segments.Add(new ArcSegment(outerMid, outerArcSize, 0, false, SweepDirection.Clockwise, true));
                figure.Segments.Add(new ArcSegment(outerEnd, outerArcSize, 0, false, SweepDirection.Clockwise, true));
            }
            else
            {
                figure.StartPoint = center;
                figure.Segments.Add(new LineSegment(outerStart, true));
                figure.Segments.Add(new ArcSegment(outerEnd, outerArcSize, 0, isLargeArc, SweepDirection.Clockwise, true));
            }
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private void DrawSliceLabel(DrawingContext dc, Point center, double outerRadius, double innerRadius,
        double startAngleDeg, double sweepAngleDeg, string text,
        string fontFamily, Brush foreground)
    {
        var bisectorDeg = startAngleDeg + sweepAngleDeg / 2.0;
        var bisectorRad = bisectorDeg * Math.PI / 180.0;

        var ft = new FormattedText(text, fontFamily, 11.0)
        {
            Foreground = foreground
        };
        TextMeasurement.MeasureText(ft);

        double labelX, labelY;
        var labelPos = LabelPosition;

        if (labelPos == PieLabelPosition.Inside)
        {
            var labelRadius = innerRadius > 0.5
                ? (innerRadius + outerRadius) / 2.0
                : outerRadius * 0.65;
            labelX = center.X + Math.Cos(bisectorRad) * labelRadius - ft.Width / 2.0;
            labelY = center.Y + Math.Sin(bisectorRad) * labelRadius - ft.Height / 2.0;
        }
        else if (labelPos == PieLabelPosition.Outside)
        {
            var labelRadius = outerRadius + 12;
            var px = center.X + Math.Cos(bisectorRad) * labelRadius;
            var py = center.Y + Math.Sin(bisectorRad) * labelRadius;

            // Adjust alignment based on which side of the pie
            if (Math.Cos(bisectorRad) < 0)
                labelX = px - ft.Width;
            else
                labelX = px;
            labelY = py - ft.Height / 2.0;
        }
        else // Connector
        {
            var connectorStartRadius = outerRadius + 4;
            var connectorEndRadius = outerRadius + 28;

            var startPt = new Point(
                center.X + Math.Cos(bisectorRad) * connectorStartRadius,
                center.Y + Math.Sin(bisectorRad) * connectorStartRadius);
            var endPt = new Point(
                center.X + Math.Cos(bisectorRad) * connectorEndRadius,
                center.Y + Math.Sin(bisectorRad) * connectorEndRadius);

            // Draw connector line
            var connectorPen = new Pen(foreground, 1);
            dc.DrawLine(connectorPen, startPt, endPt);

            // Draw horizontal tail
            double tailLength = 12;
            var tailEnd = new Point(
                Math.Cos(bisectorRad) < 0 ? endPt.X - tailLength : endPt.X + tailLength,
                endPt.Y);
            dc.DrawLine(connectorPen, endPt, tailEnd);

            // Position text at end of tail
            if (Math.Cos(bisectorRad) < 0)
                labelX = tailEnd.X - ft.Width - 2;
            else
                labelX = tailEnd.X + 2;
            labelY = tailEnd.Y - ft.Height / 2.0;
        }

        dc.DrawText(ft, new Point(labelX, labelY));
    }

    #endregion

    #region Hit Testing

    private void OnPieChartMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsTooltipEnabled || _sliceCache.Count == 0)
            return;

        var pos = e.GetPosition(this);
        var series = (PieSeries?)GetValue(SeriesProperty);
        if (series == null)
            return;

        foreach (var slice in _sliceCache)
        {
            if (IsPointInSlice(pos, slice))
            {
                var dp = slice.Index < series.DataPoints.Count ? series.DataPoints[slice.Index] : null;
                var label = dp?.Label ?? $"Item {slice.Index + 1}";
                var value = dp?.Value.ToString("G6") ?? "";
                ShowTooltip(pos.X, pos.Y, series, label, value);

                RaiseEvent(new ChartDataPointEventArgs(DataPointHoverEvent, series, dp, slice.Index));
                return;
            }
        }

        HideTooltip();
    }

    private void OnPieChartMouseLeave(object sender, MouseEventArgs e)
    {
        HideTooltip();
    }

    private static bool IsPointInSlice(Point p, SliceHitInfo slice)
    {
        var dx = p.X - slice.Center.X;
        var dy = p.Y - slice.Center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);

        // Check if within the ring
        if (distance < slice.InnerRadius || distance > slice.OuterRadius)
            return false;

        // Check if within the angle range
        var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;

        // Normalize angles to [0, 360)
        var normalizedAngle = ((angle % 360.0) + 360.0) % 360.0;
        var normalizedStart = ((slice.StartAngle % 360.0) + 360.0) % 360.0;
        var normalizedEnd = ((normalizedStart + slice.SweepAngle) % 360.0 + 360.0) % 360.0;

        if (slice.SweepAngle >= 360.0)
            return true;

        if (normalizedStart < normalizedEnd)
        {
            return normalizedAngle >= normalizedStart && normalizedAngle <= normalizedEnd;
        }
        else
        {
            // Wraps around 360
            return normalizedAngle >= normalizedStart || normalizedAngle <= normalizedEnd;
        }
    }

    #endregion
}
