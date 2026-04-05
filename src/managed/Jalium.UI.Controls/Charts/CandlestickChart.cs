using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Specifies the type of moving average calculation.
/// </summary>
public enum MovingAverageType
{
    /// <summary>
    /// Simple Moving Average.
    /// </summary>
    SMA,

    /// <summary>
    /// Exponential Moving Average.
    /// </summary>
    EMA
}

/// <summary>
/// Configuration for a moving average overlay line on a candlestick chart.
/// </summary>
public class MovingAverageConfig : INotifyPropertyChanged
{
    private int _period = 20;
    private MovingAverageType _type = MovingAverageType.SMA;
    private Brush? _brush;
    private double _thickness = 1.5;

    /// <summary>
    /// Gets or sets the number of periods for the moving average calculation.
    /// </summary>
    public int Period
    {
        get => _period;
        set
        {
            if (_period != value)
            {
                _period = value;
                OnPropertyChanged(nameof(Period));
            }
        }
    }

    /// <summary>
    /// Gets or sets the type of moving average (SMA or EMA).
    /// </summary>
    public MovingAverageType Type
    {
        get => _type;
        set
        {
            if (_type != value)
            {
                _type = value;
                OnPropertyChanged(nameof(Type));
            }
        }
    }

    /// <summary>
    /// Gets or sets the brush used to render the moving average line.
    /// </summary>
    public Brush? Brush
    {
        get => _brush;
        set
        {
            if (_brush != value)
            {
                _brush = value;
                OnPropertyChanged(nameof(Brush));
            }
        }
    }

    /// <summary>
    /// Gets or sets the thickness of the moving average line.
    /// </summary>
    public double Thickness
    {
        get => _thickness;
        set
        {
            if (_thickness != value)
            {
                _thickness = value;
                OnPropertyChanged(nameof(Thickness));
            }
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event.
    /// </summary>
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Displays financial OHLC data as candlestick chart with optional volume bars and moving average overlays.
/// </summary>
public class CandlestickChart : AxisChartBase
{
    #region Static Brushes

    private static readonly SolidColorBrush s_defaultBullishBrush = new(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly SolidColorBrush s_defaultBearishBrush = new(Color.FromRgb(0xE0, 0x3E, 0x3E));
    private static readonly SolidColorBrush s_defaultVolumeBrush = new(Color.FromArgb(80, 0x60, 0x7D, 0x8B));
    private static readonly SolidColorBrush s_maColor1 = new(Color.FromRgb(0xFF, 0x9E, 0x22));
    private static readonly SolidColorBrush s_maColor2 = new(Color.FromRgb(0x41, 0x7E, 0xE0));
    private static readonly SolidColorBrush s_maColor3 = new(Color.FromRgb(0x9C, 0x5F, 0xC4));

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the ItemsSource dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(CandlestickChart),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the BullishBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BullishBrushProperty =
        DependencyProperty.Register(nameof(BullishBrush), typeof(Brush), typeof(CandlestickChart),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the BearishBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BearishBrushProperty =
        DependencyProperty.Register(nameof(BearishBrush), typeof(Brush), typeof(CandlestickChart),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the BullishStrokeBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BullishStrokeBrushProperty =
        DependencyProperty.Register(nameof(BullishStrokeBrush), typeof(Brush), typeof(CandlestickChart),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the BearishStrokeBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BearishStrokeBrushProperty =
        DependencyProperty.Register(nameof(BearishStrokeBrush), typeof(Brush), typeof(CandlestickChart),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the CandleWidth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CandleWidthProperty =
        DependencyProperty.Register(nameof(CandleWidth), typeof(double), typeof(CandlestickChart),
            new PropertyMetadata(0.8, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ShowVolume dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowVolumeProperty =
        DependencyProperty.Register(nameof(ShowVolume), typeof(bool), typeof(CandlestickChart),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the VolumeHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty VolumeHeightProperty =
        DependencyProperty.Register(nameof(VolumeHeight), typeof(double), typeof(CandlestickChart),
            new PropertyMetadata(0.2, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the VolumeBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty VolumeBrushProperty =
        DependencyProperty.Register(nameof(VolumeBrush), typeof(Brush), typeof(CandlestickChart),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the MovingAverages dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty MovingAveragesProperty =
        DependencyProperty.Register(nameof(MovingAverages), typeof(ObservableCollection<MovingAverageConfig>), typeof(CandlestickChart),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the data source. Cast to IEnumerable&lt;OhlcDataPoint&gt;.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for bullish (close &gt; open) candles.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? BullishBrush
    {
        get => (Brush?)GetValue(BullishBrushProperty);
        set => SetValue(BullishBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for bearish (close &lt; open) candles.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? BearishBrush
    {
        get => (Brush?)GetValue(BearishBrushProperty);
        set => SetValue(BearishBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the stroke brush for bullish candle outlines.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? BullishStrokeBrush
    {
        get => (Brush?)GetValue(BullishStrokeBrushProperty);
        set => SetValue(BullishStrokeBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the stroke brush for bearish candle outlines.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? BearishStrokeBrush
    {
        get => (Brush?)GetValue(BearishStrokeBrushProperty);
        set => SetValue(BearishStrokeBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the candle body width as a ratio (0..1) of the available per-candle slot.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double CandleWidth
    {
        get => (double)GetValue(CandleWidthProperty)!;
        set => SetValue(CandleWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the volume sub-chart is displayed.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowVolume
    {
        get => (bool)GetValue(ShowVolumeProperty)!;
        set => SetValue(ShowVolumeProperty, value);
    }

    /// <summary>
    /// Gets or sets the volume sub-chart height as a ratio (0..1) of the total plot area.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double VolumeHeight
    {
        get => (double)GetValue(VolumeHeightProperty)!;
        set => SetValue(VolumeHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for volume bars. Null uses a default translucent brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? VolumeBrush
    {
        get => (Brush?)GetValue(VolumeBrushProperty);
        set => SetValue(VolumeBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the collection of moving average overlay configurations.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public ObservableCollection<MovingAverageConfig> MovingAverages
    {
        get
        {
            var ma = (ObservableCollection<MovingAverageConfig>?)GetValue(MovingAveragesProperty);
            if (ma == null)
            {
                ma = new ObservableCollection<MovingAverageConfig>();
                SetValue(MovingAveragesProperty, ma);
            }
            return ma;
        }
        set => SetValue(MovingAveragesProperty, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="CandlestickChart"/> class.
    /// </summary>
    public CandlestickChart()
    {
        PlotAreaMargin = new Thickness(60, 20, 20, 40);
    }

    #endregion

    #region Automation

    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.CandlestickChartAutomationPeer(this);
    }

    #endregion

    #region Data Collection

    private List<OhlcDataPoint> GetDataPoints()
    {
        var result = new List<OhlcDataPoint>();
        var source = ItemsSource;
        if (source == null)
            return result;

        foreach (var item in source)
        {
            if (item is OhlcDataPoint dp)
                result.Add(dp);
        }
        return result;
    }

    /// <inheritdoc />
    protected override IEnumerable<double> CollectXValues()
    {
        var data = GetDataPoints();
        for (int i = 0; i < data.Count; i++)
        {
            yield return DateTimeAxis.DateTimeToDouble(data[i].Date);
        }
    }

    /// <inheritdoc />
    protected override IEnumerable<double> CollectYValues()
    {
        var data = GetDataPoints();
        foreach (var dp in data)
        {
            yield return dp.High;
            yield return dp.Low;
        }
    }

    #endregion

    /// <inheritdoc />
    protected override IList<ChartLegendItem>? CollectLegendItems()
    {
        var bullBrush = BullishBrush ?? s_defaultBullishBrush;
        var bearBrush = BearishBrush ?? s_defaultBearishBrush;
        return new List<ChartLegendItem>
        {
            new ChartLegendItem { Label = "Bullish", Brush = bullBrush },
            new ChartLegendItem { Label = "Bearish", Brush = bearBrush }
        };
    }

    #region Rendering

    /// <inheritdoc />
    protected override void RenderPlotContent(DrawingContext dc, Rect plotArea,
        double xMin, double xMax, double yMin, double yMax)
    {
        var data = GetDataPoints();
        if (data.Count == 0)
            return;

        var bullBrush = BullishBrush ?? s_defaultBullishBrush;
        var bearBrush = BearishBrush ?? s_defaultBearishBrush;
        var bullStroke = BullishStrokeBrush;
        var bearStroke = BearishStrokeBrush;

        // Determine plot sub-areas for candlestick and volume
        Rect candleArea = plotArea;
        Rect volumeArea = Rect.Empty;

        if (ShowVolume)
        {
            var volumeRatio = Math.Clamp(VolumeHeight, 0.05, 0.5);
            var volumeH = plotArea.Height * volumeRatio;
            var candleH = plotArea.Height - volumeH - 4; // 4px gap
            candleArea = new Rect(plotArea.Left, plotArea.Top, plotArea.Width, candleH);
            volumeArea = new Rect(plotArea.Left, plotArea.Top + candleH + 4, plotArea.Width, volumeH);
        }

        var xAxis = XAxis ?? new DateTimeAxis();
        var yAxis = YAxis ?? new NumericAxis();

        // Width per candle slot
        double slotWidth = candleArea.Width / data.Count;
        double bodyWidth = slotWidth * Math.Clamp(CandleWidth, 0.1, 1.0);

        // Draw candlesticks
        for (int i = 0; i < data.Count; i++)
        {
            var dp = data[i];
            bool isBullish = dp.Close >= dp.Open;
            var fillBrush = isBullish ? bullBrush : bearBrush;
            var strokeBrush = isBullish ? (bullStroke ?? fillBrush) : (bearStroke ?? fillBrush);
            var wickPen = new Pen(strokeBrush, 1.0);
            var bodyPen = new Pen(strokeBrush, 1.0);

            double centerX = candleArea.Left + (i + 0.5) * slotWidth;

            // Map Y values to pixels (inverted: high values at top)
            double highY = candleArea.Bottom - yAxis.ValueToPixel(dp.High, yMin, yMax, candleArea.Height);
            double lowY = candleArea.Bottom - yAxis.ValueToPixel(dp.Low, yMin, yMax, candleArea.Height);
            double openY = candleArea.Bottom - yAxis.ValueToPixel(dp.Open, yMin, yMax, candleArea.Height);
            double closeY = candleArea.Bottom - yAxis.ValueToPixel(dp.Close, yMin, yMax, candleArea.Height);

            // Draw wick (high-low line)
            dc.DrawLine(wickPen, new Point(centerX, highY), new Point(centerX, lowY));

            // Draw body rectangle
            double bodyTop = Math.Min(openY, closeY);
            double bodyBottom = Math.Max(openY, closeY);
            double bodyHeight = Math.Max(bodyBottom - bodyTop, 1.0);
            double bodyLeft = centerX - bodyWidth / 2.0;

            var bodyRect = new Rect(bodyLeft, bodyTop, bodyWidth, bodyHeight);
            dc.DrawRectangle(fillBrush, bodyPen, bodyRect);
        }

        // Draw volume bars
        if (ShowVolume && volumeArea != Rect.Empty)
        {
            RenderVolumeBars(dc, data, volumeArea, slotWidth, bullBrush, bearBrush);
        }

        // Draw moving average overlays
        var maConfigs = (ObservableCollection<MovingAverageConfig>?)GetValue(MovingAveragesProperty);
        if (maConfigs != null && maConfigs.Count > 0)
        {
            RenderMovingAverages(dc, data, candleArea, xAxis, yAxis, xMin, xMax, yMin, yMax, maConfigs);
        }
    }

    private void RenderVolumeBars(DrawingContext dc, List<OhlcDataPoint> data, Rect volumeArea,
        double slotWidth, Brush bullBrush, Brush bearBrush)
    {
        // Find max volume for scaling
        double maxVolume = 0;
        foreach (var dp in data)
        {
            if (dp.Volume > maxVolume)
                maxVolume = dp.Volume;
        }
        if (maxVolume <= 0)
            return;

        var defaultVolBrush = VolumeBrush ?? s_defaultVolumeBrush;
        double barWidth = slotWidth * Math.Clamp(CandleWidth, 0.1, 1.0);

        for (int i = 0; i < data.Count; i++)
        {
            var dp = data[i];
            if (dp.Volume <= 0)
                continue;

            double barHeight = (dp.Volume / maxVolume) * volumeArea.Height;
            double centerX = volumeArea.Left + (i + 0.5) * slotWidth;
            double barLeft = centerX - barWidth / 2.0;
            double barTop = volumeArea.Bottom - barHeight;

            Brush volBrush;
            if (VolumeBrush != null)
            {
                volBrush = defaultVolBrush;
            }
            else
            {
                // Color volume bars by candle direction with transparency
                bool isBullish = dp.Close >= dp.Open;
                var baseBrush = isBullish ? bullBrush : bearBrush;
                if (baseBrush is SolidColorBrush scb)
                {
                    volBrush = new SolidColorBrush(Color.FromArgb(80, scb.Color.R, scb.Color.G, scb.Color.B));
                }
                else
                {
                    volBrush = defaultVolBrush;
                }
            }

            var rect = new Rect(barLeft, barTop, barWidth, barHeight);
            dc.DrawRectangle(volBrush, null, rect);
        }
    }

    private void RenderMovingAverages(DrawingContext dc, List<OhlcDataPoint> data,
        Rect candleArea, ChartAxis xAxis, ChartAxis yAxis,
        double xMin, double xMax, double yMin, double yMax,
        ObservableCollection<MovingAverageConfig> configs)
    {
        var defaultMaBrushes = new Brush[] { s_maColor1, s_maColor2, s_maColor3 };
        double slotWidth = candleArea.Width / data.Count;

        for (int ci = 0; ci < configs.Count; ci++)
        {
            var config = configs[ci];
            if (config.Period <= 0 || config.Period > data.Count)
                continue;

            var maValues = config.Type == MovingAverageType.EMA
                ? ComputeEMA(data, config.Period)
                : ComputeSMA(data, config.Period);

            var maBrush = config.Brush ?? defaultMaBrushes[ci % defaultMaBrushes.Length];
            var maPen = new Pen(maBrush, config.Thickness);

            // Build line from MA values
            var points = new List<Point>();
            for (int i = 0; i < maValues.Length; i++)
            {
                if (double.IsNaN(maValues[i]))
                    continue;

                double px = candleArea.Left + (i + 0.5) * slotWidth;
                double py = candleArea.Bottom - yAxis.ValueToPixel(maValues[i], yMin, yMax, candleArea.Height);
                points.Add(new Point(px, py));
            }

            if (points.Count < 2)
                continue;

            // Draw the MA line
            var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = false };
            for (int i = 1; i < points.Count; i++)
            {
                figure.Segments.Add(new LineSegment(points[i], true));
            }
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            dc.DrawGeometry(null, maPen, geometry);
        }
    }

    private static double[] ComputeSMA(List<OhlcDataPoint> data, int period)
    {
        var result = new double[data.Count];
        double sum = 0;

        for (int i = 0; i < data.Count; i++)
        {
            sum += data[i].Close;
            if (i < period - 1)
            {
                result[i] = double.NaN;
            }
            else
            {
                if (i >= period)
                    sum -= data[i - period].Close;
                result[i] = sum / period;
            }
        }

        return result;
    }

    private static double[] ComputeEMA(List<OhlcDataPoint> data, int period)
    {
        var result = new double[data.Count];
        double multiplier = 2.0 / (period + 1);

        // Seed with SMA of first 'period' values
        double sum = 0;
        for (int i = 0; i < data.Count; i++)
        {
            if (i < period - 1)
            {
                sum += data[i].Close;
                result[i] = double.NaN;
            }
            else if (i == period - 1)
            {
                sum += data[i].Close;
                result[i] = sum / period;
            }
            else
            {
                result[i] = (data[i].Close - result[i - 1]) * multiplier + result[i - 1];
            }
        }

        return result;
    }

    #endregion
}
