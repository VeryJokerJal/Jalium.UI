using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Displays a 2D matrix of values as a color-coded grid with optional labels and a color scale bar.
/// </summary>
public class Heatmap : AxisChartBase
{
    #region Private State

    private static readonly SolidColorBrush s_defaultLabelBrush = new(Color.FromRgb(220, 220, 220));

    /// <summary>
    /// Cached cell rectangles for O(1) hit testing by grid position.
    /// </summary>
    private int _cachedRows;
    private int _cachedCols;
    private Rect _cachedPlotArea;

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Data dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(double[,]), typeof(Heatmap),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the XLabels dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty XLabelsProperty =
        DependencyProperty.Register(nameof(XLabels), typeof(IList<string>), typeof(Heatmap),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the YLabels dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty YLabelsProperty =
        DependencyProperty.Register(nameof(YLabels), typeof(IList<string>), typeof(Heatmap),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ColorScale dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ColorScaleProperty =
        DependencyProperty.Register(nameof(ColorScale), typeof(HeatmapColorScale), typeof(Heatmap),
            new PropertyMetadata(HeatmapColorScale.BlueToRed, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the MinColor dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty MinColorProperty =
        DependencyProperty.Register(nameof(MinColor), typeof(Color), typeof(Heatmap),
            new PropertyMetadata(Color.Blue, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the MaxColor dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty MaxColorProperty =
        DependencyProperty.Register(nameof(MaxColor), typeof(Color), typeof(Heatmap),
            new PropertyMetadata(Color.Red, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the MidColor dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty MidColorProperty =
        DependencyProperty.Register(nameof(MidColor), typeof(Color?), typeof(Heatmap),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowCellValues dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowCellValuesProperty =
        DependencyProperty.Register(nameof(ShowCellValues), typeof(bool), typeof(Heatmap),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the CellValueFormat dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty CellValueFormatProperty =
        DependencyProperty.Register(nameof(CellValueFormat), typeof(string), typeof(Heatmap),
            new PropertyMetadata("F1", OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the CellBorderBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CellBorderBrushProperty =
        DependencyProperty.Register(nameof(CellBorderBrush), typeof(Brush), typeof(Heatmap),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the CellBorderThickness dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CellBorderThicknessProperty =
        DependencyProperty.Register(nameof(CellBorderThickness), typeof(double), typeof(Heatmap),
            new PropertyMetadata(0.5, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the DataMinimum dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty DataMinimumProperty =
        DependencyProperty.Register(nameof(DataMinimum), typeof(double?), typeof(Heatmap),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the DataMaximum dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty DataMaximumProperty =
        DependencyProperty.Register(nameof(DataMaximum), typeof(double?), typeof(Heatmap),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the 2D data matrix. Rows are Y, columns are X.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public double[,]? Data
    {
        get => (double[,]?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>
    /// Gets or sets the labels for the X axis (columns).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public IList<string>? XLabels
    {
        get => (IList<string>?)GetValue(XLabelsProperty);
        set => SetValue(XLabelsProperty, value);
    }

    /// <summary>
    /// Gets or sets the labels for the Y axis (rows).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public IList<string>? YLabels
    {
        get => (IList<string>?)GetValue(YLabelsProperty);
        set => SetValue(YLabelsProperty, value);
    }

    /// <summary>
    /// Gets or sets the color scale mode.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public HeatmapColorScale ColorScale
    {
        get => (HeatmapColorScale)GetValue(ColorScaleProperty)!;
        set => SetValue(ColorScaleProperty, value);
    }

    /// <summary>
    /// Gets or sets the color representing the minimum value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Color MinColor
    {
        get => (Color)GetValue(MinColorProperty)!;
        set => SetValue(MinColorProperty, value);
    }

    /// <summary>
    /// Gets or sets the color representing the maximum value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Color MaxColor
    {
        get => (Color)GetValue(MaxColorProperty)!;
        set => SetValue(MaxColorProperty, value);
    }

    /// <summary>
    /// Gets or sets the optional midpoint color for three-stop gradients.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Color? MidColor
    {
        get => (Color?)GetValue(MidColorProperty);
        set => SetValue(MidColorProperty, value);
    }

    /// <summary>
    /// Gets or sets whether cell values are displayed as text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowCellValues
    {
        get => (bool)GetValue(ShowCellValuesProperty)!;
        set => SetValue(ShowCellValuesProperty, value);
    }

    /// <summary>
    /// Gets or sets the format string for cell values.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string CellValueFormat
    {
        get => (string)GetValue(CellValueFormatProperty)!;
        set => SetValue(CellValueFormatProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for cell borders.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? CellBorderBrush
    {
        get => (Brush?)GetValue(CellBorderBrushProperty);
        set => SetValue(CellBorderBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the thickness of cell borders.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double CellBorderThickness
    {
        get => (double)GetValue(CellBorderThicknessProperty)!;
        set => SetValue(CellBorderThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets an optional minimum data value override for color scaling.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public double? DataMinimum
    {
        get => (double?)GetValue(DataMinimumProperty);
        set => SetValue(DataMinimumProperty, value);
    }

    /// <summary>
    /// Gets or sets an optional maximum data value override for color scaling.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public double? DataMaximum
    {
        get => (double?)GetValue(DataMaximumProperty);
        set => SetValue(DataMaximumProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="Heatmap"/> class.
    /// </summary>
    public Heatmap()
    {
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnHeatmapMouseMove));
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnHeatmapMouseLeave));
    }

    #endregion

    #region Automation

    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.HeatmapAutomationPeer(this);
    }

    #endregion

    #region AxisChartBase Overrides

    /// <inheritdoc />
    protected override IEnumerable<double> CollectXValues()
    {
        var data = Data;
        if (data == null)
            yield break;

        int cols = data.GetLength(1);
        for (int c = 0; c < cols; c++)
            yield return c;
    }

    /// <inheritdoc />
    protected override IEnumerable<double> CollectYValues()
    {
        var data = Data;
        if (data == null)
            yield break;

        int rows = data.GetLength(0);
        for (int r = 0; r < rows; r++)
            yield return r;
    }

    /// <inheritdoc />
    protected override void RenderPlotContent(DrawingContext dc, Rect plotArea,
        double xMin, double xMax, double yMin, double yMax)
    {
        var data = Data;
        if (data == null)
            return;

        int rows = data.GetLength(0);
        int cols = data.GetLength(1);
        if (rows == 0 || cols == 0)
            return;

        // Reserve space for color scale bar on the right
        const double colorBarWidth = 20;
        const double colorBarGap = 10;
        var cellArea = new Rect(plotArea.X, plotArea.Y,
            Math.Max(0, plotArea.Width - colorBarWidth - colorBarGap), plotArea.Height);

        double cellWidth = cellArea.Width / cols;
        double cellHeight = cellArea.Height / rows;

        // Compute data range
        double dataMin, dataMax;
        ComputeDataRange(data, rows, cols, out dataMin, out dataMax);

        // Cache layout info for hit testing
        _cachedRows = rows;
        _cachedCols = cols;
        _cachedPlotArea = cellArea;

        var borderPen = CellBorderBrush != null && CellBorderThickness > 0
            ? new Pen(CellBorderBrush, CellBorderThickness)
            : null;

        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var cellValueFontSize = Math.Max(7, Math.Min(cellHeight * 0.4, cellWidth * 0.3));

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                double value = data[row, col];
                double t = (dataMax - dataMin) > 1e-15
                    ? Math.Clamp((value - dataMin) / (dataMax - dataMin), 0, 1)
                    : 0.5;

                var color = InterpolateColor(t);
                var brush = new SolidColorBrush(color);

                // Row 0 = top of grid, so draw from top
                var cellRect = new Rect(
                    cellArea.X + col * cellWidth,
                    cellArea.Y + row * cellHeight,
                    cellWidth,
                    cellHeight);

                dc.DrawRectangle(brush, borderPen, cellRect);

                // Draw cell value text
                if (ShowCellValues && cellWidth > 15 && cellHeight > 10)
                {
                    var text = value.ToString(CellValueFormat);
                    // Choose contrasting text color based on perceived luminance
                    var luminance = 0.299 * color.R + 0.587 * color.G + 0.114 * color.B;
                    var textBrush = luminance > 128
                        ? new SolidColorBrush(Color.FromRgb(20, 20, 20))
                        : new SolidColorBrush(Color.FromRgb(240, 240, 240));

                    var ft = new FormattedText(text, fontFamily, cellValueFontSize)
                    {
                        Foreground = textBrush
                    };
                    TextMeasurement.MeasureText(ft);

                    var textX = cellRect.X + (cellRect.Width - ft.Width) / 2.0;
                    var textY = cellRect.Y + (cellRect.Height - ft.Height) / 2.0;
                    dc.DrawText(ft, new Point(textX, textY));
                }
            }
        }

        // Draw color scale bar
        DrawColorScaleBar(dc, plotArea, cellArea, colorBarWidth, colorBarGap, dataMin, dataMax, fontFamily);
    }

    #endregion

    #region Color Scale Bar

    private void DrawColorScaleBar(DrawingContext dc, Rect plotArea, Rect cellArea,
        double barWidth, double gap, double dataMin, double dataMax, string fontFamily)
    {
        var barX = cellArea.Right + gap;
        var barY = plotArea.Y;
        var barHeight = plotArea.Height;

        if (barHeight <= 0 || barWidth <= 0)
            return;

        // Draw gradient as discrete bands
        int bands = Math.Max(1, (int)barHeight);
        double bandHeight = barHeight / bands;

        for (int i = 0; i < bands; i++)
        {
            // t=1 at top (max), t=0 at bottom (min)
            double t = 1.0 - (double)i / bands;
            var color = InterpolateColor(t);
            var brush = new SolidColorBrush(color);
            var bandRect = new Rect(barX, barY + i * bandHeight, barWidth, bandHeight + 1);
            dc.DrawRectangle(brush, null, bandRect);
        }

        // Draw border
        var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(100, 100, 100)), 1);
        dc.DrawRectangle(null, borderPen, new Rect(barX, barY, barWidth, barHeight));

        // Draw min/max labels
        var labelBrush = Foreground ?? s_defaultLabelBrush;
        var labelFontSize = 10.0;

        var maxFt = new FormattedText(dataMax.ToString("G4"), fontFamily, labelFontSize)
        {
            Foreground = labelBrush
        };
        TextMeasurement.MeasureText(maxFt);
        dc.DrawText(maxFt, new Point(barX + barWidth + 3, barY));

        var minFt = new FormattedText(dataMin.ToString("G4"), fontFamily, labelFontSize)
        {
            Foreground = labelBrush
        };
        TextMeasurement.MeasureText(minFt);
        dc.DrawText(minFt, new Point(barX + barWidth + 3, barY + barHeight - minFt.Height));
    }

    #endregion

    #region Color Interpolation

    private void ComputeDataRange(double[,] data, int rows, int cols, out double dataMin, out double dataMax)
    {
        if (DataMinimum.HasValue)
            dataMin = DataMinimum.Value;
        else
        {
            dataMin = double.MaxValue;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var v = data[r, c];
                    if (!double.IsNaN(v) && v < dataMin)
                        dataMin = v;
                }
            if (dataMin == double.MaxValue)
                dataMin = 0;
        }

        if (DataMaximum.HasValue)
            dataMax = DataMaximum.Value;
        else
        {
            dataMax = double.MinValue;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var v = data[r, c];
                    if (!double.IsNaN(v) && v > dataMax)
                        dataMax = v;
                }
            if (dataMax == double.MinValue)
                dataMax = 1;
        }

        if (dataMax <= dataMin)
            dataMax = dataMin + 1;
    }

    private Color InterpolateColor(double t)
    {
        t = Math.Clamp(t, 0, 1);

        switch (ColorScale)
        {
            case HeatmapColorScale.BlueToRed:
            {
                var midColor = MidColor;
                if (midColor.HasValue)
                {
                    // Three-stop gradient: MinColor -> MidColor -> MaxColor
                    if (t < 0.5)
                    {
                        double localT = t * 2.0;
                        return LerpColor(MinColor, midColor.Value, localT);
                    }
                    else
                    {
                        double localT = (t - 0.5) * 2.0;
                        return LerpColor(midColor.Value, MaxColor, localT);
                    }
                }
                return LerpColor(MinColor, MaxColor, t);
            }

            case HeatmapColorScale.Viridis:
                return ViridisColor(t);

            case HeatmapColorScale.Grayscale:
            {
                byte v = (byte)(t * 255);
                return Color.FromRgb(v, v, v);
            }

            case HeatmapColorScale.Custom:
            {
                var midColor = MidColor;
                if (midColor.HasValue)
                {
                    if (t < 0.5)
                        return LerpColor(MinColor, midColor.Value, t * 2.0);
                    else
                        return LerpColor(midColor.Value, MaxColor, (t - 0.5) * 2.0);
                }
                return LerpColor(MinColor, MaxColor, t);
            }

            default:
                return LerpColor(MinColor, MaxColor, t);
        }
    }

    private static Color LerpColor(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    /// <summary>
    /// Approximation of the Viridis colormap using 5 key stops.
    /// </summary>
    private static Color ViridisColor(double t)
    {
        // Viridis key colors (5 stops at t=0, 0.25, 0.5, 0.75, 1.0)
        ReadOnlySpan<byte> r = stackalloc byte[] { 68, 59, 33, 94, 253 };
        ReadOnlySpan<byte> g = stackalloc byte[] { 1, 82, 145, 201, 231 };
        ReadOnlySpan<byte> b = stackalloc byte[] { 84, 139, 140, 98, 37 };

        double scaledT = t * 4.0;
        int idx = Math.Min((int)scaledT, 3);
        double localT = scaledT - idx;

        return Color.FromRgb(
            (byte)(r[idx] + (r[idx + 1] - r[idx]) * localT),
            (byte)(g[idx] + (g[idx + 1] - g[idx]) * localT),
            (byte)(b[idx] + (b[idx + 1] - b[idx]) * localT));
    }

    #endregion

    #region Hit Testing

    private void OnHeatmapMouseMove(object sender, MouseEventArgs e)
    {
        if (!IsTooltipEnabled)
            return;

        var data = Data;
        if (data == null || _cachedRows == 0 || _cachedCols == 0)
            return;

        var pos = e.GetPosition(this);
        var area = _cachedPlotArea;

        if (!area.Contains(pos))
        {
            HideTooltip();
            return;
        }

        // O(1) cell lookup
        int col = (int)((pos.X - area.X) / (area.Width / _cachedCols));
        int row = (int)((pos.Y - area.Y) / (area.Height / _cachedRows));

        col = Math.Clamp(col, 0, _cachedCols - 1);
        row = Math.Clamp(row, 0, _cachedRows - 1);

        double value = data[row, col];

        var xLabel = XLabels != null && col < XLabels.Count ? XLabels[col] : col.ToString();
        var yLabel = YLabels != null && row < YLabels.Count ? YLabels[row] : row.ToString();

        ShowTooltip(pos.X, pos.Y, null, $"{yLabel}, {xLabel}", value.ToString(CellValueFormat));
    }

    private void OnHeatmapMouseLeave(object sender, MouseEventArgs e)
    {
        HideTooltip();
    }

    #endregion
}
