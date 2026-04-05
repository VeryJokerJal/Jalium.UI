using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// A lightweight inline chart control that renders a compact visualization (line, area, bar, or win/loss)
/// without axes, titles, or legends. Designed for embedding in grids, lists, and dashboards.
/// </summary>
public class Sparkline : Control
{
    #region Static Defaults

    private static readonly SolidColorBrush s_defaultLineBrush = new(Color.FromRgb(0x41, 0x7E, 0xE0));
    private static readonly SolidColorBrush s_defaultFillBrush = new(Color.FromArgb(60, 0x41, 0x7E, 0xE0));
    private static readonly SolidColorBrush s_defaultBarBrush = new(Color.FromRgb(0x41, 0x7E, 0xE0));
    private static readonly SolidColorBrush s_defaultNegativeBarBrush = new(Color.FromRgb(0xE0, 0x59, 0x3E));
    private static readonly SolidColorBrush s_defaultWinBrush = new(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly SolidColorBrush s_defaultLossBrush = new(Color.FromRgb(0xE0, 0x59, 0x3E));
    private static readonly SolidColorBrush s_defaultHighPointBrush = new(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly SolidColorBrush s_defaultLowPointBrush = new(Color.FromRgb(0xE0, 0x59, 0x3E));

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Values dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ValuesProperty =
        DependencyProperty.Register(nameof(Values), typeof(IList<double>), typeof(Sparkline),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the SparklineType dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SparklineTypeProperty =
        DependencyProperty.Register(nameof(SparklineType), typeof(SparklineType), typeof(Sparkline),
            new PropertyMetadata(SparklineType.Line, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the LineBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty LineBrushProperty =
        DependencyProperty.Register(nameof(LineBrush), typeof(Brush), typeof(Sparkline),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the LineThickness dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty LineThicknessProperty =
        DependencyProperty.Register(nameof(LineThickness), typeof(double), typeof(Sparkline),
            new PropertyMetadata(1.5, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the FillBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty FillBrushProperty =
        DependencyProperty.Register(nameof(FillBrush), typeof(Brush), typeof(Sparkline),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the HighPointBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty HighPointBrushProperty =
        DependencyProperty.Register(nameof(HighPointBrush), typeof(Brush), typeof(Sparkline),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the LowPointBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty LowPointBrushProperty =
        DependencyProperty.Register(nameof(LowPointBrush), typeof(Brush), typeof(Sparkline),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the FirstPointBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty FirstPointBrushProperty =
        DependencyProperty.Register(nameof(FirstPointBrush), typeof(Brush), typeof(Sparkline),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the LastPointBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty LastPointBrushProperty =
        DependencyProperty.Register(nameof(LastPointBrush), typeof(Brush), typeof(Sparkline),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the NegativeBarBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty NegativeBarBrushProperty =
        DependencyProperty.Register(nameof(NegativeBarBrush), typeof(Brush), typeof(Sparkline),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the BarSpacing dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty BarSpacingProperty =
        DependencyProperty.Register(nameof(BarSpacing), typeof(double), typeof(Sparkline),
            new PropertyMetadata(1.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowHighLowPoints dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowHighLowPointsProperty =
        DependencyProperty.Register(nameof(ShowHighLowPoints), typeof(bool), typeof(Sparkline),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the WinColor dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty WinColorProperty =
        DependencyProperty.Register(nameof(WinColor), typeof(Brush), typeof(Sparkline),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the LossColor dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty LossColorProperty =
        DependencyProperty.Register(nameof(LossColor), typeof(Brush), typeof(Sparkline),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the BaselineValue dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty BaselineValueProperty =
        DependencyProperty.Register(nameof(BaselineValue), typeof(double), typeof(Sparkline),
            new PropertyMetadata(0.0, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the data values to display.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public IList<double>? Values
    {
        get => (IList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    /// <summary>
    /// Gets or sets the sparkline visualization type.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public SparklineType SparklineType
    {
        get => (SparklineType)GetValue(SparklineTypeProperty)!;
        set => SetValue(SparklineTypeProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for the line stroke.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? LineBrush
    {
        get => (Brush?)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the thickness of the line stroke.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double LineThickness
    {
        get => (double)GetValue(LineThicknessProperty)!;
        set => SetValue(LineThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for the area fill beneath the line.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? FillBrush
    {
        get => (Brush?)GetValue(FillBrushProperty);
        set => SetValue(FillBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for the highest value point marker.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? HighPointBrush
    {
        get => (Brush?)GetValue(HighPointBrushProperty);
        set => SetValue(HighPointBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for the lowest value point marker.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? LowPointBrush
    {
        get => (Brush?)GetValue(LowPointBrushProperty);
        set => SetValue(LowPointBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for the first point marker.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? FirstPointBrush
    {
        get => (Brush?)GetValue(FirstPointBrushProperty);
        set => SetValue(FirstPointBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for the last point marker.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? LastPointBrush
    {
        get => (Brush?)GetValue(LastPointBrushProperty);
        set => SetValue(LastPointBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for negative value bars.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? NegativeBarBrush
    {
        get => (Brush?)GetValue(NegativeBarBrushProperty);
        set => SetValue(NegativeBarBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the spacing between bars in bar mode.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double BarSpacing
    {
        get => (double)GetValue(BarSpacingProperty)!;
        set => SetValue(BarSpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets whether special markers are drawn for the highest and lowest points.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowHighLowPoints
    {
        get => (bool)GetValue(ShowHighLowPointsProperty)!;
        set => SetValue(ShowHighLowPointsProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for win bars in win/loss mode.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? WinColor
    {
        get => (Brush?)GetValue(WinColorProperty);
        set => SetValue(WinColorProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for loss bars in win/loss mode.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? LossColor
    {
        get => (Brush?)GetValue(LossColorProperty);
        set => SetValue(LossColorProperty, value);
    }

    /// <summary>
    /// Gets or sets the baseline value for determining positive/negative classification.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public double BaselineValue
    {
        get => (double)GetValue(BaselineValueProperty)!;
        set => SetValue(BaselineValueProperty, value);
    }

    #endregion

    #region Automation

    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.SparklineAutomationPeer(this);
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? 80 : availableSize.Width;
        double h = double.IsInfinity(availableSize.Height) ? 20 : availableSize.Height;
        return new Size(w, h);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var values = Values;
        if (values == null || values.Count == 0)
            return;

        var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        // Draw background if set
        if (Background != null)
        {
            dc.DrawRectangle(Background, null, bounds);
        }

        switch (SparklineType)
        {
            case SparklineType.Line:
                RenderLine(dc, values, bounds, fillArea: false);
                break;
            case SparklineType.Area:
                RenderLine(dc, values, bounds, fillArea: true);
                break;
            case SparklineType.Bar:
                RenderBar(dc, values, bounds);
                break;
            case SparklineType.WinLoss:
                RenderWinLoss(dc, values, bounds);
                break;
        }
    }

    #endregion

    #region Line / Area Rendering

    private void RenderLine(DrawingContext dc, IList<double> values, Rect bounds, bool fillArea)
    {
        int count = values.Count;
        if (count < 2 && !fillArea)
        {
            // Single point: draw a dot
            if (count == 1)
            {
                var brush = LineBrush ?? s_defaultLineBrush;
                dc.DrawEllipse(brush, null, new Point(bounds.Width / 2, bounds.Height / 2), 2, 2);
            }
            return;
        }

        if (count < 1)
            return;

        // Compute min/max for scaling
        double minVal = double.MaxValue, maxVal = double.MinValue;
        int highIdx = 0, lowIdx = 0;
        for (int i = 0; i < count; i++)
        {
            var v = values[i];
            if (v < minVal) { minVal = v; lowIdx = i; }
            if (v > maxVal) { maxVal = v; highIdx = i; }
        }

        if (Math.Abs(maxVal - minVal) < 1e-15)
        {
            maxVal = minVal + 1;
        }

        // Add a small padding so points aren't right at edges
        double padY = bounds.Height * 0.05;
        double plotHeight = bounds.Height - padY * 2;

        // Compute pixel points
        var points = new List<Point>(count);
        for (int i = 0; i < count; i++)
        {
            double x = count > 1 ? bounds.X + (double)i / (count - 1) * bounds.Width : bounds.X + bounds.Width / 2;
            double t = (values[i] - minVal) / (maxVal - minVal);
            double y = bounds.Y + padY + (1.0 - t) * plotHeight;
            points.Add(new Point(x, y));
        }

        // Draw area fill
        if (fillArea)
        {
            var fillBrush = FillBrush ?? s_defaultFillBrush;
            var areaGeometry = new StreamGeometry();
            using (var ctx = areaGeometry.Open())
            {
                ctx.BeginFigure(new Point(points[0].X, bounds.Bottom), true, true);
                ctx.LineTo(points[0], true, false);
                for (int i = 1; i < points.Count; i++)
                {
                    ctx.LineTo(points[i], true, false);
                }
                ctx.LineTo(new Point(points[points.Count - 1].X, bounds.Bottom), true, false);
            }
            dc.DrawGeometry(fillBrush, null, areaGeometry);
        }

        // Draw line
        if (count >= 2)
        {
            var lineBrush = LineBrush ?? s_defaultLineBrush;
            var pen = new Pen(lineBrush, LineThickness);

            var lineGeometry = new StreamGeometry();
            using (var ctx = lineGeometry.Open())
            {
                ctx.BeginFigure(points[0], false, false);
                for (int i = 1; i < points.Count; i++)
                {
                    ctx.LineTo(points[i], true, false);
                }
            }
            dc.DrawGeometry(null, pen, lineGeometry);
        }

        // Draw special point markers
        DrawSpecialPointMarkers(dc, values, points, highIdx, lowIdx);
    }

    private void DrawSpecialPointMarkers(DrawingContext dc, IList<double> values, List<Point> points,
        int highIdx, int lowIdx)
    {
        const double markerRadius = 2.5;

        // First point
        if (FirstPointBrush != null && points.Count > 0)
        {
            dc.DrawEllipse(FirstPointBrush, null, points[0], markerRadius, markerRadius);
        }

        // Last point
        if (LastPointBrush != null && points.Count > 1)
        {
            dc.DrawEllipse(LastPointBrush, null, points[points.Count - 1], markerRadius, markerRadius);
        }

        // High/Low points
        if (ShowHighLowPoints && points.Count > 0)
        {
            var highBrush = HighPointBrush ?? s_defaultHighPointBrush;
            dc.DrawEllipse(highBrush, null, points[highIdx], markerRadius, markerRadius);

            var lowBrush = LowPointBrush ?? s_defaultLowPointBrush;
            dc.DrawEllipse(lowBrush, null, points[lowIdx], markerRadius, markerRadius);
        }
    }

    #endregion

    #region Bar Rendering

    private void RenderBar(DrawingContext dc, IList<double> values, Rect bounds)
    {
        int count = values.Count;
        if (count == 0)
            return;

        double spacing = BarSpacing;
        double totalSpacing = spacing * (count - 1);
        double barWidth = Math.Max(1, (bounds.Width - totalSpacing) / count);

        // Compute min/max
        double minVal = double.MaxValue, maxVal = double.MinValue;
        int highIdx = 0, lowIdx = 0;
        for (int i = 0; i < count; i++)
        {
            var v = values[i];
            if (v < minVal) { minVal = v; lowIdx = i; }
            if (v > maxVal) { maxVal = v; highIdx = i; }
        }

        double baseline = BaselineValue;
        double rangeMin = Math.Min(minVal, baseline);
        double rangeMax = Math.Max(maxVal, baseline);
        if (Math.Abs(rangeMax - rangeMin) < 1e-15)
            rangeMax = rangeMin + 1;

        double baselineY = bounds.Y + (1.0 - (baseline - rangeMin) / (rangeMax - rangeMin)) * bounds.Height;

        var positiveBrush = LineBrush ?? s_defaultBarBrush;
        var negativeBrush = NegativeBarBrush ?? s_defaultNegativeBarBrush;

        var markerPoints = new List<Point>(count);

        for (int i = 0; i < count; i++)
        {
            double x = bounds.X + i * (barWidth + spacing);
            double t = (values[i] - rangeMin) / (rangeMax - rangeMin);
            double valueY = bounds.Y + (1.0 - t) * bounds.Height;

            var barBrush = values[i] >= baseline ? positiveBrush : negativeBrush;

            double barTop = Math.Min(valueY, baselineY);
            double barBottom = Math.Max(valueY, baselineY);
            double barHeight = Math.Max(1, barBottom - barTop);

            dc.DrawRectangle(barBrush, null, new Rect(x, barTop, barWidth, barHeight));
            markerPoints.Add(new Point(x + barWidth / 2, valueY));
        }

        // Draw special point markers for bar mode
        DrawSpecialPointMarkers(dc, values, markerPoints, highIdx, lowIdx);
    }

    #endregion

    #region Win/Loss Rendering

    private void RenderWinLoss(DrawingContext dc, IList<double> values, Rect bounds)
    {
        int count = values.Count;
        if (count == 0)
            return;

        double spacing = BarSpacing;
        double totalSpacing = spacing * (count - 1);
        double barWidth = Math.Max(1, (bounds.Width - totalSpacing) / count);

        double baseline = BaselineValue;
        double midY = bounds.Y + bounds.Height / 2.0;
        double halfBarHeight = (bounds.Height / 2.0) * 0.8; // 80% of half height

        var winBrush = WinColor ?? s_defaultWinBrush;
        var lossBrush = LossColor ?? s_defaultLossBrush;

        for (int i = 0; i < count; i++)
        {
            double x = bounds.X + i * (barWidth + spacing);
            bool isWin = values[i] >= baseline;

            var brush = isWin ? winBrush : lossBrush;
            double barTop = isWin ? midY - halfBarHeight : midY;
            double barHeight = halfBarHeight;

            dc.DrawRectangle(brush, null, new Rect(x, barTop, barWidth, barHeight));
        }
    }

    #endregion

    #region Property Changed

    private new static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Control control)
        {
            control.InvalidateVisual();
        }
    }

    #endregion
}
