using System.Collections.ObjectModel;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// Event args for chart data point interaction events.
/// </summary>
public class ChartDataPointEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the series that the data point belongs to.
    /// </summary>
    public ChartSeries? Series { get; }

    /// <summary>
    /// Gets the data point that was interacted with.
    /// </summary>
    public object? DataPoint { get; }

    /// <summary>
    /// Gets the index of the data point within the series.
    /// </summary>
    public int DataPointIndex { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ChartDataPointEventArgs"/> class.
    /// </summary>
    public ChartDataPointEventArgs(RoutedEvent routedEvent, ChartSeries? series, object? dataPoint, int index)
        : base(routedEvent)
    {
        Series = series;
        DataPoint = dataPoint;
        DataPointIndex = index;
    }
}

/// <summary>
/// Delegate for chart data point events.
/// </summary>
public delegate void ChartDataPointEventHandler(object sender, ChartDataPointEventArgs e);

/// <summary>
/// Abstract base class for all chart controls.
/// </summary>
public abstract class ChartBase : Control
{
    #region Static Brushes

    private static readonly SolidColorBrush s_defaultTitleForeground = new(Color.FromRgb(240, 240, 240));
    private static readonly SolidColorBrush s_defaultBackground = new(Color.FromArgb(0, 0, 0, 0));

    /// <summary>
    /// Default chart color palette with 10 visually distinct colors.
    /// </summary>
    private static readonly IList<Brush> s_defaultPalette = new List<Brush>
    {
        new SolidColorBrush(Color.FromRgb(0x41, 0x7E, 0xE0)),   // Blue
        new SolidColorBrush(Color.FromRgb(0xE0, 0x59, 0x3E)),   // Red-Orange
        new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),   // Green
        new SolidColorBrush(Color.FromRgb(0xFF, 0x9E, 0x22)),   // Orange
        new SolidColorBrush(Color.FromRgb(0x9C, 0x5F, 0xC4)),   // Purple
        new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4)),   // Cyan
        new SolidColorBrush(Color.FromRgb(0xE9, 0x1E, 0x63)),   // Pink
        new SolidColorBrush(Color.FromRgb(0x8B, 0xC3, 0x4A)),   // Lime
        new SolidColorBrush(Color.FromRgb(0x79, 0x55, 0x48)),   // Brown
        new SolidColorBrush(Color.FromRgb(0x60, 0x7D, 0x8B)),   // Blue-Grey
    }.AsReadOnly();

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Title dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ChartBase),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the TitleFontSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TitleFontSizeProperty =
        DependencyProperty.Register(nameof(TitleFontSize), typeof(double), typeof(ChartBase),
            new PropertyMetadata(16.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the TitleForeground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty TitleForegroundProperty =
        DependencyProperty.Register(nameof(TitleForeground), typeof(Brush), typeof(ChartBase),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsLegendVisible dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty IsLegendVisibleProperty =
        DependencyProperty.Register(nameof(IsLegendVisible), typeof(bool), typeof(ChartBase),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the LegendPosition dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty LegendPositionProperty =
        DependencyProperty.Register(nameof(LegendPosition), typeof(LegendPosition), typeof(ChartBase),
            new PropertyMetadata(LegendPosition.Bottom, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsTooltipEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty IsTooltipEnabledProperty =
        DependencyProperty.Register(nameof(IsTooltipEnabled), typeof(bool), typeof(ChartBase),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the TooltipTemplate dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TooltipTemplateProperty =
        DependencyProperty.Register(nameof(TooltipTemplate), typeof(DataTemplate), typeof(ChartBase),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the AnimationDuration dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty AnimationDurationProperty =
        DependencyProperty.Register(nameof(AnimationDuration), typeof(TimeSpan), typeof(ChartBase),
            new PropertyMetadata(TimeSpan.FromMilliseconds(300)));

    /// <summary>
    /// Identifies the IsAnimationEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty IsAnimationEnabledProperty =
        DependencyProperty.Register(nameof(IsAnimationEnabled), typeof(bool), typeof(ChartBase),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the PlotAreaMargin dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PlotAreaMarginProperty =
        DependencyProperty.Register(nameof(PlotAreaMargin), typeof(Thickness), typeof(ChartBase),
            new PropertyMetadata(new Thickness(40, 20, 20, 40), OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the ChartPalette dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ChartPaletteProperty =
        DependencyProperty.Register(nameof(ChartPalette), typeof(IList<Brush>), typeof(ChartBase),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the DataPointClicked routed event.
    /// </summary>
    public static readonly RoutedEvent DataPointClickedEvent =
        EventManager.RegisterRoutedEvent(nameof(DataPointClicked), RoutingStrategy.Bubble,
            typeof(ChartDataPointEventHandler), typeof(ChartBase));

    /// <summary>
    /// Occurs when a data point is clicked.
    /// </summary>
    public event ChartDataPointEventHandler DataPointClicked
    {
        add => AddHandler(DataPointClickedEvent, value);
        remove => RemoveHandler(DataPointClickedEvent, value);
    }

    /// <summary>
    /// Identifies the DataPointHover routed event.
    /// </summary>
    public static readonly RoutedEvent DataPointHoverEvent =
        EventManager.RegisterRoutedEvent(nameof(DataPointHover), RoutingStrategy.Bubble,
            typeof(ChartDataPointEventHandler), typeof(ChartBase));

    /// <summary>
    /// Occurs when the pointer hovers over a data point.
    /// </summary>
    public event ChartDataPointEventHandler DataPointHover
    {
        add => AddHandler(DataPointHoverEvent, value);
        remove => RemoveHandler(DataPointHoverEvent, value);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the chart title.
    /// </summary>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the title font size.
    /// </summary>
    public double TitleFontSize
    {
        get => (double)GetValue(TitleFontSizeProperty)!;
        set => SetValue(TitleFontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the title foreground brush.
    /// </summary>
    public Brush? TitleForeground
    {
        get => (Brush?)GetValue(TitleForegroundProperty);
        set => SetValue(TitleForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the legend is visible.
    /// </summary>
    public bool IsLegendVisible
    {
        get => (bool)GetValue(IsLegendVisibleProperty)!;
        set => SetValue(IsLegendVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets the legend position.
    /// </summary>
    public LegendPosition LegendPosition
    {
        get => (LegendPosition)GetValue(LegendPositionProperty)!;
        set => SetValue(LegendPositionProperty, value);
    }

    /// <summary>
    /// Gets or sets whether tooltips are enabled.
    /// </summary>
    public bool IsTooltipEnabled
    {
        get => (bool)GetValue(IsTooltipEnabledProperty)!;
        set => SetValue(IsTooltipEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the tooltip data template.
    /// </summary>
    public DataTemplate? TooltipTemplate
    {
        get => (DataTemplate?)GetValue(TooltipTemplateProperty);
        set => SetValue(TooltipTemplateProperty, value);
    }

    /// <summary>
    /// Gets or sets the animation duration.
    /// </summary>
    public TimeSpan AnimationDuration
    {
        get => (TimeSpan)GetValue(AnimationDurationProperty)!;
        set => SetValue(AnimationDurationProperty, value);
    }

    /// <summary>
    /// Gets or sets whether animations are enabled.
    /// </summary>
    public bool IsAnimationEnabled
    {
        get => (bool)GetValue(IsAnimationEnabledProperty)!;
        set => SetValue(IsAnimationEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the margin around the plot area for axes and labels.
    /// </summary>
    public Thickness PlotAreaMargin
    {
        get => (Thickness)GetValue(PlotAreaMarginProperty)!;
        set => SetValue(PlotAreaMarginProperty, value);
    }

    /// <summary>
    /// Gets or sets the color palette for chart series.
    /// </summary>
    public IList<Brush>? ChartPalette
    {
        get => (IList<Brush>?)GetValue(ChartPaletteProperty);
        set => SetValue(ChartPaletteProperty, value);
    }

    #endregion

    #region Tooltip State

    private ChartTooltip? _chartTooltip;
    private bool _isTooltipShown;

    #endregion

    #region Methods

    /// <summary>
    /// Computes the plot area rectangle within the control bounds.
    /// </summary>
    protected Rect GetPlotArea()
    {
        var margin = PlotAreaMargin;
        var titleHeight = 0.0;

        if (!string.IsNullOrEmpty(Title))
        {
            titleHeight = TitleFontSize + 8;
        }

        var left = margin.Left;
        var top = margin.Top + titleHeight;
        var right = RenderSize.Width - margin.Right;
        var bottom = RenderSize.Height - margin.Bottom;

        // Reserve space for legend if visible
        if (IsLegendVisible)
        {
            switch (LegendPosition)
            {
                case LegendPosition.Top:
                    top += 24;
                    break;
                case LegendPosition.Bottom:
                    bottom -= 24;
                    break;
                case LegendPosition.Left:
                    left += 100;
                    break;
                case LegendPosition.Right:
                    right -= 100;
                    break;
            }
        }

        var w = Math.Max(0, right - left);
        var h = Math.Max(0, bottom - top);
        return new Rect(left, top, w, h);
    }

    /// <summary>
    /// Gets the brush for a series at the given index from the palette.
    /// </summary>
    /// <param name="index">The series index.</param>
    /// <returns>The brush from the palette.</returns>
    protected Brush GetSeriesBrush(int index)
    {
        var palette = ChartPalette ?? s_defaultPalette;
        if (palette.Count == 0)
            return new SolidColorBrush(Color.Gray);

        return palette[index % palette.Count];
    }

    /// <summary>
    /// Shows a tooltip at the specified position with the given content.
    /// </summary>
    protected void ShowTooltip(double x, double y, ChartSeries? series, string? xValue, string? yValue)
    {
        if (!IsTooltipEnabled)
            return;

        _chartTooltip ??= new ChartTooltip();
        _chartTooltip.SeriesTitle = series?.Title;
        _chartTooltip.XValue = xValue;
        _chartTooltip.YValue = yValue;
        _chartTooltip.SeriesBrush = series?.Brush;
        _isTooltipShown = true;

        InvalidateVisual();
    }

    /// <summary>
    /// Hides the chart tooltip.
    /// </summary>
    protected void HideTooltip()
    {
        if (_isTooltipShown)
        {
            _isTooltipShown = false;
            _chartTooltip = null;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Resolves a brush from the theme resource dictionary, with fallback.
    /// </summary>
    protected Brush ResolveChartThemeBrush(string resourceKey, Brush fallback, string? secondaryKey = null)
    {
        if (TryFindResource(resourceKey) is Brush b) return b;
        if (secondaryKey != null && TryFindResource(secondaryKey) is Brush sb) return sb;
        var app = Jalium.UI.Application.Current;
        if (app?.Resources != null)
        {
            if (app.Resources.TryGetValue(resourceKey, out var r) && r is Brush rb) return rb;
            if (secondaryKey != null && app.Resources.TryGetValue(secondaryKey, out var r2) && r2 is Brush rb2) return rb2;
        }
        return fallback;
    }

    /// <summary>
    /// When overridden, renders the chart content within the plot area.
    /// </summary>
    /// <param name="dc">The drawing context.</param>
    /// <param name="plotArea">The computed plot area rectangle.</param>
    protected abstract void RenderChart(DrawingContext dc, Rect plotArea);

    /// <summary>
    /// When overridden, returns the legend items for this chart.
    /// </summary>
    protected virtual IList<ChartLegendItem>? CollectLegendItems() => null;

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);

        // Draw background
        var bg = Background ?? s_defaultBackground;
        dc.DrawRectangle(bg, null, bounds);

        // Draw title
        double titleBottom = 0;
        if (!string.IsNullOrEmpty(Title))
        {
            var titleBrush = TitleForeground ?? Foreground ?? s_defaultTitleForeground;
            var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
            var ft = new FormattedText(Title, fontFamily, TitleFontSize)
            {
                Foreground = titleBrush,
                FontWeight = 700
            };
            TextMeasurement.MeasureText(ft);

            var titleX = (RenderSize.Width - ft.Width) / 2.0;
            var titleY = PlotAreaMargin.Top / 2.0;
            dc.DrawText(ft, new Point(titleX, titleY));
            titleBottom = titleY + ft.Height + 4;
        }

        // Compute plot area and delegate rendering
        var plotArea = GetPlotArea();
        if (plotArea.Width > 0 && plotArea.Height > 0)
        {
            RenderChart(dc, plotArea);
        }

        // Draw legend
        if (IsLegendVisible)
        {
            RenderLegend(dc, plotArea);
        }
    }

    private void RenderLegend(DrawingContext dc, Rect plotArea)
    {
        var items = CollectLegendItems();
        if (items == null || items.Count == 0) return;

        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var textBrush = Foreground ?? s_defaultTitleForeground;
        const double markerSize = 10;
        const double markerTextGap = 5;
        const double itemGap = 16;
        const double fontSize = 12;

        // Measure total legend width to center it
        double totalWidth = 0;
        var measurements = new List<(FormattedText ft, double w)>();
        foreach (var item in items)
        {
            var ft = new FormattedText(item.Label ?? "", fontFamily, fontSize) { Foreground = textBrush };
            TextMeasurement.MeasureText(ft);
            double itemW = markerSize + markerTextGap + ft.Width;
            measurements.Add((ft, itemW));
            totalWidth += itemW;
        }
        totalWidth += (items.Count - 1) * itemGap;

        // Position based on LegendPosition
        double legendX, legendY;
        switch (LegendPosition)
        {
            case LegendPosition.Top:
                legendX = (RenderSize.Width - totalWidth) / 2.0;
                legendY = plotArea.Top - 22;
                break;
            case LegendPosition.Bottom:
            default:
                legendX = (RenderSize.Width - totalWidth) / 2.0;
                legendY = RenderSize.Height - fontSize - 4;
                break;
            case LegendPosition.Left:
                legendX = 4;
                legendY = plotArea.Top + plotArea.Height / 2.0;
                break;
            case LegendPosition.Right:
                legendX = plotArea.Right + 8;
                legendY = plotArea.Top + plotArea.Height / 2.0;
                break;
        }

        // Draw items horizontally (for Top/Bottom)
        double x = legendX;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var (ft, itemW) = measurements[i];
            var markerBrush = item.Brush ?? GetSeriesBrush(i);

            // Draw color marker — vertically centered to the actual text height
            double markerY = legendY + (ft.Height - markerSize) / 2.0;
            var markerRect = new Rect(x, markerY, markerSize, markerSize);
            dc.DrawRoundedRectangle(markerBrush, null, markerRect, 2, 2);

            // Draw label
            dc.DrawText(ft, new Point(x + markerSize + markerTextGap, legendY));

            x += itemW + itemGap;
        }
    }

    #endregion
}
