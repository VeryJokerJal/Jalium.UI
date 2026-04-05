using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Abstract base class for charts that have X and Y axes (line, bar, scatter, etc.).
/// Handles axis rendering, grid lines, zoom, and pan.
/// </summary>
public abstract class AxisChartBase : ChartBase
{
    #region Static Brushes

    private static readonly SolidColorBrush s_defaultAxisBrush = new(Color.FromRgb(160, 160, 160));
    private static readonly SolidColorBrush s_defaultGridLineBrush = new(Color.FromArgb(40, 200, 200, 200));
    private static readonly SolidColorBrush s_defaultLabelBrush = new(Color.FromRgb(180, 180, 180));

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the XAxis dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty XAxisProperty =
        DependencyProperty.Register(nameof(XAxis), typeof(ChartAxis), typeof(AxisChartBase),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the YAxis dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty YAxisProperty =
        DependencyProperty.Register(nameof(YAxis), typeof(ChartAxis), typeof(AxisChartBase),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsGridLinesVisible dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty IsGridLinesVisibleProperty =
        DependencyProperty.Register(nameof(IsGridLinesVisible), typeof(bool), typeof(AxisChartBase),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the GridLineBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty GridLineBrushProperty =
        DependencyProperty.Register(nameof(GridLineBrush), typeof(Brush), typeof(AxisChartBase),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the GridLineDashArray dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty GridLineDashArrayProperty =
        DependencyProperty.Register(nameof(GridLineDashArray), typeof(DoubleCollection), typeof(AxisChartBase),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the AxisBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty AxisBrushProperty =
        DependencyProperty.Register(nameof(AxisBrush), typeof(Brush), typeof(AxisChartBase),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the AxisThickness dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty AxisThicknessProperty =
        DependencyProperty.Register(nameof(AxisThickness), typeof(double), typeof(AxisChartBase),
            new PropertyMetadata(1.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsZoomEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty IsZoomEnabledProperty =
        DependencyProperty.Register(nameof(IsZoomEnabled), typeof(bool), typeof(AxisChartBase),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsPanEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty IsPanEnabledProperty =
        DependencyProperty.Register(nameof(IsPanEnabled), typeof(bool), typeof(AxisChartBase),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the XAxisLabelRotation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty XAxisLabelRotationProperty =
        DependencyProperty.Register(nameof(XAxisLabelRotation), typeof(double), typeof(AxisChartBase),
            new PropertyMetadata(0.0, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the X axis.
    /// </summary>
    public ChartAxis? XAxis
    {
        get => (ChartAxis?)GetValue(XAxisProperty);
        set => SetValue(XAxisProperty, value);
    }

    /// <summary>
    /// Gets or sets the Y axis.
    /// </summary>
    public ChartAxis? YAxis
    {
        get => (ChartAxis?)GetValue(YAxisProperty);
        set => SetValue(YAxisProperty, value);
    }

    /// <summary>
    /// Gets or sets whether grid lines are visible.
    /// </summary>
    public bool IsGridLinesVisible
    {
        get => (bool)GetValue(IsGridLinesVisibleProperty)!;
        set => SetValue(IsGridLinesVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets the grid line brush.
    /// </summary>
    public Brush? GridLineBrush
    {
        get => (Brush?)GetValue(GridLineBrushProperty);
        set => SetValue(GridLineBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the grid line dash pattern.
    /// </summary>
    public DoubleCollection? GridLineDashArray
    {
        get => (DoubleCollection?)GetValue(GridLineDashArrayProperty);
        set => SetValue(GridLineDashArrayProperty, value);
    }

    /// <summary>
    /// Gets or sets the axis line brush.
    /// </summary>
    public Brush? AxisBrush
    {
        get => (Brush?)GetValue(AxisBrushProperty);
        set => SetValue(AxisBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the axis line thickness.
    /// </summary>
    public double AxisThickness
    {
        get => (double)GetValue(AxisThicknessProperty)!;
        set => SetValue(AxisThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets whether mouse-wheel zoom is enabled.
    /// </summary>
    public bool IsZoomEnabled
    {
        get => (bool)GetValue(IsZoomEnabledProperty)!;
        set => SetValue(IsZoomEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets whether middle-mouse-button panning is enabled.
    /// </summary>
    public bool IsPanEnabled
    {
        get => (bool)GetValue(IsPanEnabledProperty)!;
        set => SetValue(IsPanEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the rotation angle (in degrees) for X axis labels.
    /// </summary>
    public double XAxisLabelRotation
    {
        get => (double)GetValue(XAxisLabelRotationProperty)!;
        set => SetValue(XAxisLabelRotationProperty, value);
    }

    #endregion

    #region Internal Viewport State

    /// <summary>
    /// Viewport X minimum for zoom/pan.
    /// </summary>
    protected double _viewportMinX = double.NaN;

    /// <summary>
    /// Viewport X maximum for zoom/pan.
    /// </summary>
    protected double _viewportMaxX = double.NaN;

    /// <summary>
    /// Viewport Y minimum for zoom/pan.
    /// </summary>
    protected double _viewportMinY = double.NaN;

    /// <summary>
    /// Viewport Y maximum for zoom/pan.
    /// </summary>
    protected double _viewportMaxY = double.NaN;

    private bool _isPanning;
    private Point _panStartPoint;
    private double _panStartMinX, _panStartMaxX, _panStartMinY, _panStartMaxY;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="AxisChartBase"/> class.
    /// </summary>
    public AxisChartBase()
    {
        AddHandler(MouseWheelEvent, new MouseWheelEventHandler(OnMouseWheelHandler));
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
    }

    #endregion

    #region Abstract Methods

    /// <summary>
    /// When overridden, collects all Y values from the chart's series for auto-ranging.
    /// </summary>
    /// <returns>An enumerable of Y values.</returns>
    protected abstract IEnumerable<double> CollectYValues();

    /// <summary>
    /// When overridden, collects all X values from the chart's series for auto-ranging.
    /// </summary>
    /// <returns>An enumerable of X values.</returns>
    protected abstract IEnumerable<double> CollectXValues();

    /// <summary>
    /// When overridden, collects string category labels for the X axis.
    /// Returns null if X values are numeric (not categorical).
    /// </summary>
    protected virtual IList<string>? CollectXCategories() => null;

    /// <summary>
    /// When overridden, renders the actual plot content (series data) within the plot area.
    /// </summary>
    /// <param name="dc">The drawing context.</param>
    /// <param name="plotArea">The computed plot area rectangle.</param>
    /// <param name="xMin">The effective X minimum.</param>
    /// <param name="xMax">The effective X maximum.</param>
    /// <param name="yMin">The effective Y minimum.</param>
    /// <param name="yMax">The effective Y maximum.</param>
    protected abstract void RenderPlotContent(DrawingContext dc, Rect plotArea,
        double xMin, double xMax, double yMin, double yMax);

    #endregion

    #region RenderChart Implementation

    /// <inheritdoc />
    protected sealed override void RenderChart(DrawingContext dc, Rect plotArea)
    {
        var xAxis = XAxis;
        var yAxis = YAxis ?? new NumericAxis();

        // Auto-detect CategoryAxis when XValues are strings
        if (xAxis == null)
        {
            var categories = CollectXCategories();
            if (categories != null && categories.Count > 0)
            {
                xAxis = new CategoryAxis { Categories = categories };
            }
            else
            {
                xAxis = new NumericAxis();
            }
        }

        // Compute effective axis ranges
        double xMin, xMax, yMin, yMax;
        ComputeAxisRanges(xAxis, yAxis, out xMin, out xMax, out yMin, out yMax);

        // Apply viewport overrides for zoom/pan
        if (!double.IsNaN(_viewportMinX)) xMin = _viewportMinX;
        if (!double.IsNaN(_viewportMaxX)) xMax = _viewportMaxX;
        if (!double.IsNaN(_viewportMinY)) yMin = _viewportMinY;
        if (!double.IsNaN(_viewportMaxY)) yMax = _viewportMaxY;

        // Ensure valid ranges
        if (xMax <= xMin) { xMin = 0; xMax = 1; }
        if (yMax <= yMin) { yMin = 0; yMax = 1; }

        // Draw grid lines
        if (IsGridLinesVisible)
        {
            DrawGridLines(dc, plotArea, xAxis, yAxis, xMin, xMax, yMin, yMax);
        }

        // Draw axes
        DrawAxes(dc, plotArea, xAxis, yAxis, xMin, xMax, yMin, yMax);

        // Clip and render the plot content
        RenderPlotContent(dc, plotArea, xMin, xMax, yMin, yMax);
    }

    #endregion

    #region Axis Range Computation

    private void ComputeAxisRanges(ChartAxis xAxis, ChartAxis yAxis,
        out double xMin, out double xMax, out double yMin, out double yMax)
    {
        // X axis range
        if (xAxis.Minimum.HasValue && xAxis.Maximum.HasValue)
        {
            xMin = xAxis.Minimum.Value;
            xMax = xAxis.Maximum.Value;
        }
        else if (xAxis is CategoryAxis catAxis && catAxis.Categories != null && catAxis.Categories.Count > 0)
        {
            // For category axes, range is exactly [0, count-1] (no padding)
            xMin = -0.5;
            xMax = catAxis.Categories.Count - 0.5;
        }
        else
        {
            var xValues = CollectXValues().ToList();
            if (xValues.Count > 0)
            {
                ChartHelpers.ComputeAutoRange(xValues, out xMin, out xMax);
            }
            else
            {
                xMin = 0;
                xMax = 1;
            }

            if (xAxis.Minimum.HasValue) xMin = xAxis.Minimum.Value;
            if (xAxis.Maximum.HasValue) xMax = xAxis.Maximum.Value;
        }

        // Y axis range
        if (yAxis.Minimum.HasValue && yAxis.Maximum.HasValue)
        {
            yMin = yAxis.Minimum.Value;
            yMax = yAxis.Maximum.Value;
        }
        else
        {
            var yValues = CollectYValues().ToList();
            if (yValues.Count > 0)
            {
                ChartHelpers.ComputeAutoRange(yValues, out yMin, out yMax);
            }
            else
            {
                yMin = 0;
                yMax = 1;
            }

            if (yAxis.Minimum.HasValue) yMin = yAxis.Minimum.Value;
            if (yAxis.Maximum.HasValue) yMax = yAxis.Maximum.Value;
        }
    }

    #endregion

    #region Grid Lines

    private void DrawGridLines(DrawingContext dc, Rect plotArea,
        ChartAxis xAxis, ChartAxis yAxis,
        double xMin, double xMax, double yMin, double yMax)
    {
        var gridBrush = GridLineBrush ?? s_defaultGridLineBrush;
        var gridPen = new Pen(gridBrush, 0.5);
        if (GridLineDashArray is { Count: > 0 } dashArray)
        {
            gridPen.DashStyle = new DashStyle(dashArray, 0);
        }

        // Horizontal grid lines (Y axis ticks)
        if (yAxis.IsVisible)
        {
            var yTicks = yAxis.GenerateTicks(yMin, yMax, plotArea.Height);
            foreach (var tick in yTicks)
            {
                var py = plotArea.Bottom - yAxis.ValueToPixel(tick, yMin, yMax, plotArea.Height);
                if (py >= plotArea.Top && py <= plotArea.Bottom)
                {
                    dc.DrawLine(gridPen, new Point(plotArea.Left, py), new Point(plotArea.Right, py));
                }
            }
        }

        // Vertical grid lines (X axis ticks)
        if (xAxis.IsVisible)
        {
            var xTicks = xAxis.GenerateTicks(xMin, xMax, plotArea.Width);
            foreach (var tick in xTicks)
            {
                var px = plotArea.Left + xAxis.ValueToPixel(tick, xMin, xMax, plotArea.Width);
                if (px >= plotArea.Left && px <= plotArea.Right)
                {
                    dc.DrawLine(gridPen, new Point(px, plotArea.Top), new Point(px, plotArea.Bottom));
                }
            }
        }
    }

    #endregion

    #region Axis Drawing

    private void DrawAxes(DrawingContext dc, Rect plotArea,
        ChartAxis xAxis, ChartAxis yAxis,
        double xMin, double xMax, double yMin, double yMax)
    {
        var axisBrush = AxisBrush ?? s_defaultAxisBrush;
        var axisPen = new Pen(axisBrush, AxisThickness);
        var labelBrush = s_defaultLabelBrush;
        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;

        // Draw the X axis line (bottom)
        dc.DrawLine(axisPen, new Point(plotArea.Left, plotArea.Bottom), new Point(plotArea.Right, plotArea.Bottom));

        // Draw the Y axis line (left)
        dc.DrawLine(axisPen, new Point(plotArea.Left, plotArea.Top), new Point(plotArea.Left, plotArea.Bottom));

        // Draw X axis ticks and labels
        if (xAxis.IsVisible)
        {
            var xLabelBrush = xAxis.LabelForeground ?? labelBrush;
            var xTicks = xAxis.GenerateTicks(xMin, xMax, plotArea.Width);
            foreach (var tick in xTicks)
            {
                var px = plotArea.Left + xAxis.ValueToPixel(tick, xMin, xMax, plotArea.Width);
                if (px < plotArea.Left || px > plotArea.Right)
                    continue;

                // Draw tick mark
                dc.DrawLine(axisPen,
                    new Point(px, plotArea.Bottom),
                    new Point(px, plotArea.Bottom + xAxis.MajorTickLength));

                // Draw label
                var label = xAxis.FormatLabel(tick);
                var ft = new FormattedText(label, fontFamily, xAxis.LabelFontSize)
                {
                    Foreground = xLabelBrush
                };
                TextMeasurement.MeasureText(ft);

                var labelX = px - ft.Width / 2.0;
                var labelY = plotArea.Bottom + xAxis.MajorTickLength + 2;
                dc.DrawText(ft, new Point(labelX, labelY));
            }

            // Draw X axis title
            if (!string.IsNullOrEmpty(xAxis.Title))
            {
                var titleFt = new FormattedText(xAxis.Title, fontFamily, xAxis.LabelFontSize + 1)
                {
                    Foreground = xLabelBrush
                };
                TextMeasurement.MeasureText(titleFt);
                var titleX = plotArea.Left + (plotArea.Width - titleFt.Width) / 2.0;
                var titleY = plotArea.Bottom + xAxis.MajorTickLength + xAxis.LabelFontSize + 8;
                dc.DrawText(titleFt, new Point(titleX, titleY));
            }
        }

        // Draw Y axis ticks and labels
        if (yAxis.IsVisible)
        {
            var yLabelBrush = yAxis.LabelForeground ?? labelBrush;
            var yTicks = yAxis.GenerateTicks(yMin, yMax, plotArea.Height);
            foreach (var tick in yTicks)
            {
                var py = plotArea.Bottom - yAxis.ValueToPixel(tick, yMin, yMax, plotArea.Height);
                if (py < plotArea.Top || py > plotArea.Bottom)
                    continue;

                // Draw tick mark
                dc.DrawLine(axisPen,
                    new Point(plotArea.Left - yAxis.MajorTickLength, py),
                    new Point(plotArea.Left, py));

                // Draw label
                var label = yAxis.FormatLabel(tick);
                var ft = new FormattedText(label, fontFamily, yAxis.LabelFontSize)
                {
                    Foreground = yLabelBrush
                };
                TextMeasurement.MeasureText(ft);

                var labelX = plotArea.Left - yAxis.MajorTickLength - ft.Width - 4;
                var labelY = py - ft.Height / 2.0;
                dc.DrawText(ft, new Point(labelX, labelY));
            }

            // Draw Y axis title (rotated conceptually -- render at left side vertically)
            if (!string.IsNullOrEmpty(yAxis.Title))
            {
                var titleFt = new FormattedText(yAxis.Title, fontFamily, yAxis.LabelFontSize + 1)
                {
                    Foreground = yLabelBrush
                };
                TextMeasurement.MeasureText(titleFt);

                // For the Y axis title, draw each character vertically along the left edge
                // or simply draw it at the left-center for simplicity
                var titleX = 4.0;
                var titleY = plotArea.Top + (plotArea.Height - titleFt.Height) / 2.0;
                dc.DrawText(titleFt, new Point(titleX, titleY));
            }
        }
    }

    #endregion

    #region Zoom and Pan

    private void OnMouseWheelHandler(object sender, MouseWheelEventArgs e)
    {
        if (!IsZoomEnabled)
            return;

        var plotArea = GetPlotArea();
        var pos = e.GetPosition(this);

        if (!plotArea.Contains(pos))
            return;

        // Initialize viewport if first zoom
        if (double.IsNaN(_viewportMinX))
        {
            var xAxis = XAxis ?? new NumericAxis();
            var yAxis = YAxis ?? new NumericAxis();
            ComputeAxisRanges(xAxis, yAxis,
                out _viewportMinX, out _viewportMaxX,
                out _viewportMinY, out _viewportMaxY);
        }

        var zoomFactor = e.Delta > 0 ? 0.85 : 1.15;

        // Compute the mouse position as a fraction of the plot area
        var xFraction = (pos.X - plotArea.Left) / plotArea.Width;
        var yFraction = 1.0 - (pos.Y - plotArea.Top) / plotArea.Height;

        // Zoom around the mouse position
        var xRange = _viewportMaxX - _viewportMinX;
        var yRange = _viewportMaxY - _viewportMinY;

        var newXRange = xRange * zoomFactor;
        var newYRange = yRange * zoomFactor;

        _viewportMinX = _viewportMinX + xFraction * (xRange - newXRange);
        _viewportMaxX = _viewportMinX + newXRange;
        _viewportMinY = _viewportMinY + yFraction * (yRange - newYRange);
        _viewportMaxY = _viewportMinY + newYRange;

        e.Handled = true;
        InvalidateVisual();
    }

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsPanEnabled)
            return;

        // Use middle mouse button for panning
        if (e.ChangedButton == MouseButton.Middle)
        {
            var plotArea = GetPlotArea();
            var pos = e.GetPosition(this);

            if (!plotArea.Contains(pos))
                return;

            // Initialize viewport if first pan
            if (double.IsNaN(_viewportMinX))
            {
                var xAxis = XAxis ?? new NumericAxis();
                var yAxis = YAxis ?? new NumericAxis();
                ComputeAxisRanges(xAxis, yAxis,
                    out _viewportMinX, out _viewportMaxX,
                    out _viewportMinY, out _viewportMaxY);
            }

            _isPanning = true;
            _panStartPoint = pos;
            _panStartMinX = _viewportMinX;
            _panStartMaxX = _viewportMaxX;
            _panStartMinY = _viewportMinY;
            _panStartMaxY = _viewportMaxY;

            CaptureMouse();
            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning && e.ChangedButton == MouseButton.Middle)
        {
            _isPanning = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
            return;

        var plotArea = GetPlotArea();
        var pos = e.GetPosition(this);

        // Compute how far we have dragged in data units
        var xRange = _panStartMaxX - _panStartMinX;
        var yRange = _panStartMaxY - _panStartMinY;

        var dx = (pos.X - _panStartPoint.X) / plotArea.Width * xRange;
        var dy = (pos.Y - _panStartPoint.Y) / plotArea.Height * yRange;

        _viewportMinX = _panStartMinX - dx;
        _viewportMaxX = _panStartMaxX - dx;
        _viewportMinY = _panStartMinY + dy; // Y is inverted
        _viewportMaxY = _panStartMaxY + dy;

        InvalidateVisual();
    }

    /// <summary>
    /// Resets the viewport to auto-fit the data.
    /// </summary>
    public void ResetZoom()
    {
        _viewportMinX = double.NaN;
        _viewportMaxX = double.NaN;
        _viewportMinY = double.NaN;
        _viewportMaxY = double.NaN;
        InvalidateVisual();
    }

    #endregion
}
