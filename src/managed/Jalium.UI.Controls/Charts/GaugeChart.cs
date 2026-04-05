using System.Collections.ObjectModel;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Represents a colored range segment on a gauge chart.
/// </summary>
public class GaugeRange
{
    /// <summary>
    /// Gets or sets the minimum value of this range.
    /// </summary>
    public double Minimum { get; set; }

    /// <summary>
    /// Gets or sets the maximum value of this range.
    /// </summary>
    public double Maximum { get; set; }

    /// <summary>
    /// Gets or sets the brush used to draw this range.
    /// </summary>
    public Brush? Brush { get; set; }

    /// <summary>
    /// Gets or sets the inner radius ratio (0-1) of this range arc relative to the gauge radius.
    /// </summary>
    public double InnerRadiusRatio { get; set; } = 0.8;
}

/// <summary>
/// Displays a value on a radial gauge with colored ranges, tick marks, and a needle indicator.
/// </summary>
public class GaugeChart : ChartBase
{
    #region Static Defaults

    private static readonly SolidColorBrush s_defaultTrackBrush = new(Color.FromArgb(40, 200, 200, 200));
    private static readonly SolidColorBrush s_defaultNeedleBrush = new(Color.FromRgb(0xE0, 0x59, 0x3E));
    private static readonly SolidColorBrush s_defaultHubBrush = new(Color.FromRgb(200, 200, 200));
    private static readonly SolidColorBrush s_defaultTickBrush = new(Color.FromRgb(180, 180, 180));
    private static readonly SolidColorBrush s_defaultLabelBrush = new(Color.FromRgb(200, 200, 200));
    private static readonly SolidColorBrush s_defaultValueBrush = new(Color.FromRgb(240, 240, 240));

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Value dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(GaugeChart),
            new PropertyMetadata(0.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Minimum dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(GaugeChart),
            new PropertyMetadata(0.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Maximum dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(GaugeChart),
            new PropertyMetadata(100.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the StartAngle dependency property. The angle in degrees where the gauge arc begins
    /// (0 = right / 3 o'clock, angles increase clockwise).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StartAngleProperty =
        DependencyProperty.Register(nameof(StartAngle), typeof(double), typeof(GaugeChart),
            new PropertyMetadata(-225.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the EndAngle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty EndAngleProperty =
        DependencyProperty.Register(nameof(EndAngle), typeof(double), typeof(GaugeChart),
            new PropertyMetadata(45.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Ranges dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty RangesProperty =
        DependencyProperty.Register(nameof(Ranges), typeof(ObservableCollection<GaugeRange>), typeof(GaugeChart),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the NeedleBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty NeedleBrushProperty =
        DependencyProperty.Register(nameof(NeedleBrush), typeof(Brush), typeof(GaugeChart),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the NeedleLength dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty NeedleLengthProperty =
        DependencyProperty.Register(nameof(NeedleLength), typeof(double), typeof(GaugeChart),
            new PropertyMetadata(0.8, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the TrackThickness dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty TrackThicknessProperty =
        DependencyProperty.Register(nameof(TrackThickness), typeof(double), typeof(GaugeChart),
            new PropertyMetadata(20.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the TrackBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty TrackBrushProperty =
        DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(GaugeChart),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowValueText dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowValueTextProperty =
        DependencyProperty.Register(nameof(ShowValueText), typeof(bool), typeof(GaugeChart),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ValueFormat dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ValueFormatProperty =
        DependencyProperty.Register(nameof(ValueFormat), typeof(string), typeof(GaugeChart),
            new PropertyMetadata("F0", OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ValueFontSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty ValueFontSizeProperty =
        DependencyProperty.Register(nameof(ValueFontSize), typeof(double), typeof(GaugeChart),
            new PropertyMetadata(24.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowTickMarks dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowTickMarksProperty =
        DependencyProperty.Register(nameof(ShowTickMarks), typeof(bool), typeof(GaugeChart),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the MajorTickInterval dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty MajorTickIntervalProperty =
        DependencyProperty.Register(nameof(MajorTickInterval), typeof(double), typeof(GaugeChart),
            new PropertyMetadata(10.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the MinorTickInterval dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty MinorTickIntervalProperty =
        DependencyProperty.Register(nameof(MinorTickInterval), typeof(double), typeof(GaugeChart),
            new PropertyMetadata(2.0, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the current gauge value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public double Value
    {
        get => (double)GetValue(ValueProperty)!;
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum value of the gauge scale.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty)!;
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum value of the gauge scale.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty)!;
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>
    /// Gets or sets the start angle of the gauge arc in degrees.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double StartAngle
    {
        get => (double)GetValue(StartAngleProperty)!;
        set => SetValue(StartAngleProperty, value);
    }

    /// <summary>
    /// Gets or sets the end angle of the gauge arc in degrees.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double EndAngle
    {
        get => (double)GetValue(EndAngleProperty)!;
        set => SetValue(EndAngleProperty, value);
    }

    /// <summary>
    /// Gets or sets the collection of colored ranges on the gauge.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public ObservableCollection<GaugeRange> Ranges
    {
        get
        {
            var ranges = (ObservableCollection<GaugeRange>?)GetValue(RangesProperty);
            if (ranges == null)
            {
                ranges = new ObservableCollection<GaugeRange>();
                SetValue(RangesProperty, ranges);
            }
            return ranges;
        }
        set => SetValue(RangesProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for the needle.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? NeedleBrush
    {
        get => (Brush?)GetValue(NeedleBrushProperty);
        set => SetValue(NeedleBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the needle length as a fraction of the gauge radius (0-1).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double NeedleLength
    {
        get => (double)GetValue(NeedleLengthProperty)!;
        set => SetValue(NeedleLengthProperty, value);
    }

    /// <summary>
    /// Gets or sets the thickness of the background track arc in pixels.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double TrackThickness
    {
        get => (double)GetValue(TrackThicknessProperty)!;
        set => SetValue(TrackThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for the background track arc.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the value text is displayed below the needle hub.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowValueText
    {
        get => (bool)GetValue(ShowValueTextProperty)!;
        set => SetValue(ShowValueTextProperty, value);
    }

    /// <summary>
    /// Gets or sets the format string for the value text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string ValueFormat
    {
        get => (string)GetValue(ValueFormatProperty)!;
        set => SetValue(ValueFormatProperty, value);
    }

    /// <summary>
    /// Gets or sets the font size for the value text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public double ValueFontSize
    {
        get => (double)GetValue(ValueFontSizeProperty)!;
        set => SetValue(ValueFontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets whether tick marks are shown on the gauge.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowTickMarks
    {
        get => (bool)GetValue(ShowTickMarksProperty)!;
        set => SetValue(ShowTickMarksProperty, value);
    }

    /// <summary>
    /// Gets or sets the interval between major tick marks.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double MajorTickInterval
    {
        get => (double)GetValue(MajorTickIntervalProperty)!;
        set => SetValue(MajorTickIntervalProperty, value);
    }

    /// <summary>
    /// Gets or sets the interval between minor tick marks.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double MinorTickInterval
    {
        get => (double)GetValue(MinorTickIntervalProperty)!;
        set => SetValue(MinorTickIntervalProperty, value);
    }

    #endregion

    #region Automation

    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.GaugeChartAutomationPeer(this);
    }

    #endregion

    /// <inheritdoc />
    protected override IList<ChartLegendItem>? CollectLegendItems()
    {
        var ranges = (ObservableCollection<GaugeRange>?)GetValue(RangesProperty);
        if (ranges == null || ranges.Count == 0) return null;
        var items = new List<ChartLegendItem>();
        foreach (var range in ranges)
        {
            if (range.Brush != null)
            {
                var label = $"{range.Minimum:G6} – {range.Maximum:G6}";
                items.Add(new ChartLegendItem { Label = label, Brush = range.Brush });
            }
        }
        return items.Count > 0 ? items : null;
    }

    #region Rendering

    /// <inheritdoc />
    protected override void RenderChart(DrawingContext dc, Rect plotArea)
    {
        if (plotArea.Width <= 0 || plotArea.Height <= 0)
            return;

        double min = Minimum;
        double max = Maximum;
        if (max <= min) max = min + 100;

        double startAngle = StartAngle;
        double endAngle = EndAngle;
        double sweepAngle = endAngle - startAngle;
        if (Math.Abs(sweepAngle) < 1e-10)
            sweepAngle = 270;

        // Compute center and radius
        var centerX = plotArea.Left + plotArea.Width / 2.0;
        var centerY = plotArea.Top + plotArea.Height / 2.0;
        var radius = Math.Min(plotArea.Width, plotArea.Height) / 2.0 - TrackThickness / 2.0 - 20; // 20px for tick labels
        if (radius < 10)
            radius = 10;

        var center = new Point(centerX, centerY);

        // 1. Draw background track arc
        DrawTrackArc(dc, center, radius, startAngle, sweepAngle);

        // 2. Draw colored range arcs
        DrawRangeArcs(dc, center, radius, startAngle, sweepAngle, min, max);

        // 3. Draw tick marks and labels
        if (ShowTickMarks)
        {
            DrawTickMarks(dc, center, radius, startAngle, sweepAngle, min, max);
        }

        // 4. Draw needle
        DrawNeedle(dc, center, radius, startAngle, sweepAngle, min, max);

        // 5. Draw value text
        if (ShowValueText)
        {
            DrawValueText(dc, center, radius);
        }
    }

    #endregion

    #region Track Arc

    private void DrawTrackArc(DrawingContext dc, Point center, double radius, double startAngle, double sweepAngle)
    {
        var trackBrush = TrackBrush ?? s_defaultTrackBrush;
        var trackPen = new Pen(trackBrush, TrackThickness);

        var arcGeometry = BuildArcGeometry(center, radius, startAngle, sweepAngle);
        dc.DrawGeometry(null, trackPen, arcGeometry);
    }

    #endregion

    #region Range Arcs

    private void DrawRangeArcs(DrawingContext dc, Point center, double radius,
        double startAngle, double sweepAngle, double min, double max)
    {
        var ranges = (ObservableCollection<GaugeRange>?)GetValue(RangesProperty);
        if (ranges == null || ranges.Count == 0)
            return;

        double dataRange = max - min;
        if (dataRange < 1e-15)
            return;

        foreach (var range in ranges)
        {
            if (range.Brush == null)
                continue;

            double rangeStart = Math.Clamp(range.Minimum, min, max);
            double rangeEnd = Math.Clamp(range.Maximum, min, max);
            if (rangeEnd <= rangeStart)
                continue;

            double startFraction = (rangeStart - min) / dataRange;
            double endFraction = (rangeEnd - min) / dataRange;

            double arcStart = startAngle + startFraction * sweepAngle;
            double arcSweep = (endFraction - startFraction) * sweepAngle;

            double innerRadius = radius * Math.Clamp(range.InnerRadiusRatio, 0.1, 1.0);
            double thickness = radius - innerRadius;
            double arcRadius = innerRadius + thickness / 2.0;

            var pen = new Pen(range.Brush, thickness);
            var arcGeometry = BuildArcGeometry(center, arcRadius, arcStart, arcSweep);
            dc.DrawGeometry(null, pen, arcGeometry);
        }
    }

    #endregion

    #region Tick Marks

    private void DrawTickMarks(DrawingContext dc, Point center, double radius,
        double startAngle, double sweepAngle, double min, double max)
    {
        double dataRange = max - min;
        if (dataRange < 1e-15)
            return;

        var tickBrush = s_defaultTickBrush;
        var majorPen = new Pen(tickBrush, 2);
        var minorPen = new Pen(tickBrush, 1);
        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var labelBrush = Foreground ?? s_defaultLabelBrush;

        double outerRadius = radius + TrackThickness / 2.0;
        double majorInnerRadius = radius - TrackThickness / 2.0 - 6;
        double minorInnerRadius = radius - TrackThickness / 2.0 - 3;
        double labelRadius = outerRadius + 10;

        // Draw minor ticks first
        if (MinorTickInterval > 0)
        {
            for (double val = min; val <= max + 1e-10; val += MinorTickInterval)
            {
                double fraction = (val - min) / dataRange;
                double angle = startAngle + fraction * sweepAngle;
                double rad = angle * Math.PI / 180.0;

                var outer = new Point(center.X + Math.Cos(rad) * outerRadius,
                    center.Y + Math.Sin(rad) * outerRadius);
                var inner = new Point(center.X + Math.Cos(rad) * minorInnerRadius,
                    center.Y + Math.Sin(rad) * minorInnerRadius);

                dc.DrawLine(minorPen, inner, outer);
            }
        }

        // Draw major ticks and labels
        if (MajorTickInterval > 0)
        {
            for (double val = min; val <= max + 1e-10; val += MajorTickInterval)
            {
                double fraction = (val - min) / dataRange;
                double angle = startAngle + fraction * sweepAngle;
                double rad = angle * Math.PI / 180.0;

                var outer = new Point(center.X + Math.Cos(rad) * outerRadius,
                    center.Y + Math.Sin(rad) * outerRadius);
                var inner = new Point(center.X + Math.Cos(rad) * majorInnerRadius,
                    center.Y + Math.Sin(rad) * majorInnerRadius);

                dc.DrawLine(majorPen, inner, outer);

                // Draw label
                var labelText = val.ToString("G6");
                var ft = new FormattedText(labelText, fontFamily, 10)
                {
                    Foreground = labelBrush
                };
                TextMeasurement.MeasureText(ft);

                var labelPoint = new Point(
                    center.X + Math.Cos(rad) * labelRadius - ft.Width / 2.0,
                    center.Y + Math.Sin(rad) * labelRadius - ft.Height / 2.0);
                dc.DrawText(ft, labelPoint);
            }
        }
    }

    #endregion

    #region Needle

    private void DrawNeedle(DrawingContext dc, Point center, double radius,
        double startAngle, double sweepAngle, double min, double max)
    {
        double dataRange = max - min;
        if (dataRange < 1e-15)
            return;

        double clampedValue = Math.Clamp(Value, min, max);
        double fraction = (clampedValue - min) / dataRange;
        double angle = startAngle + fraction * sweepAngle;
        double rad = angle * Math.PI / 180.0;

        double needleLen = radius * Math.Clamp(NeedleLength, 0.1, 1.0);
        var needleBrush = NeedleBrush ?? s_defaultNeedleBrush;

        // Draw needle as a tapered triangle using PathGeometry
        double needleTipX = center.X + Math.Cos(rad) * needleLen;
        double needleTipY = center.Y + Math.Sin(rad) * needleLen;

        double perpRad = rad + Math.PI / 2.0;
        double halfBase = 3.5; // half-width of needle base
        double tailLen = 12; // needle extends slightly behind center

        var baseLeft = new Point(
            center.X + Math.Cos(perpRad) * halfBase - Math.Cos(rad) * tailLen,
            center.Y + Math.Sin(perpRad) * halfBase - Math.Sin(rad) * tailLen);
        var baseRight = new Point(
            center.X - Math.Cos(perpRad) * halfBase - Math.Cos(rad) * tailLen,
            center.Y - Math.Sin(perpRad) * halfBase - Math.Sin(rad) * tailLen);

        var figure = new PathFigure { IsClosed = true, IsFilled = true };
        figure.StartPoint = new Point(needleTipX, needleTipY);
        figure.Segments.Add(new LineSegment(baseLeft, true));
        figure.Segments.Add(new LineSegment(baseRight, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        dc.DrawGeometry(needleBrush, null, geometry);

        // Draw hub circle
        dc.DrawEllipse(s_defaultHubBrush, null, center, 6, 6);
        dc.DrawEllipse(needleBrush, null, center, 3, 3);
    }

    #endregion

    #region Value Text

    private void DrawValueText(DrawingContext dc, Point center, double radius)
    {
        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var valueBrush = Foreground ?? s_defaultValueBrush;

        var valueText = Value.ToString(ValueFormat);
        var ft = new FormattedText(valueText, fontFamily, ValueFontSize)
        {
            Foreground = valueBrush,
            FontWeight = 700
        };
        TextMeasurement.MeasureText(ft);

        var textX = center.X - ft.Width / 2.0;
        var textY = center.Y + radius * 0.3;
        dc.DrawText(ft, new Point(textX, textY));
    }

    #endregion

    #region Geometry Helpers

    /// <summary>
    /// Builds an arc geometry (unfilled stroke path) for the given center, radius, start angle and sweep.
    /// </summary>
    private static PathGeometry BuildArcGeometry(Point center, double radius, double startAngleDeg, double sweepAngleDeg)
    {
        var startRad = startAngleDeg * Math.PI / 180.0;
        var endRad = (startAngleDeg + sweepAngleDeg) * Math.PI / 180.0;

        var startPoint = new Point(
            center.X + Math.Cos(startRad) * radius,
            center.Y + Math.Sin(startRad) * radius);
        var endPoint = new Point(
            center.X + Math.Cos(endRad) * radius,
            center.Y + Math.Sin(endRad) * radius);

        bool isLargeArc = Math.Abs(sweepAngleDeg) > 180.0;
        var direction = sweepAngleDeg >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise;

        var figure = new PathFigure { IsClosed = false, IsFilled = false };
        figure.StartPoint = startPoint;

        // For arcs close to 360 degrees, split into two halves to avoid degenerate arc
        if (Math.Abs(sweepAngleDeg) > 359.5)
        {
            var midRad = startRad + (endRad - startRad) / 2.0;
            var midPoint = new Point(
                center.X + Math.Cos(midRad) * radius,
                center.Y + Math.Sin(midRad) * radius);

            figure.Segments.Add(new ArcSegment(midPoint, new Size(radius, radius),
                0, false, direction, true));
            figure.Segments.Add(new ArcSegment(endPoint, new Size(radius, radius),
                0, false, direction, true));
        }
        else
        {
            figure.Segments.Add(new ArcSegment(endPoint, new Size(radius, radius),
                0, isLargeArc, direction, true));
        }

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    #endregion
}
