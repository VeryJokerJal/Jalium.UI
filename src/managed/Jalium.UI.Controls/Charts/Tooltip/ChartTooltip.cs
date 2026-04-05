using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// A tooltip control for displaying chart data point information.
/// </summary>
public class ChartTooltip : ContentControl
{
    private static readonly SolidColorBrush s_defaultBackground = new(Color.FromArgb(230, 40, 40, 40));
    private static readonly SolidColorBrush s_defaultBorder = new(Color.FromRgb(80, 80, 80));
    private static readonly SolidColorBrush s_defaultForeground = new(Color.FromRgb(240, 240, 240));

    #region Dependency Properties

    /// <summary>
    /// Identifies the SeriesTitle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SeriesTitleProperty =
        DependencyProperty.Register(nameof(SeriesTitle), typeof(string), typeof(ChartTooltip),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the XValue dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty XValueProperty =
        DependencyProperty.Register(nameof(XValue), typeof(string), typeof(ChartTooltip),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the YValue dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty YValueProperty =
        DependencyProperty.Register(nameof(YValue), typeof(string), typeof(ChartTooltip),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the SeriesBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SeriesBrushProperty =
        DependencyProperty.Register(nameof(SeriesBrush), typeof(Brush), typeof(ChartTooltip),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the series title to display.
    /// </summary>
    public string? SeriesTitle
    {
        get => (string?)GetValue(SeriesTitleProperty);
        set => SetValue(SeriesTitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the formatted X value to display.
    /// </summary>
    public string? XValue
    {
        get => (string?)GetValue(XValueProperty);
        set => SetValue(XValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the formatted Y value to display.
    /// </summary>
    public string? YValue
    {
        get => (string?)GetValue(YValueProperty);
        set => SetValue(YValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the series color brush.
    /// </summary>
    public Brush? SeriesBrush
    {
        get => (Brush?)GetValue(SeriesBrushProperty);
        set => SetValue(SeriesBrushProperty, value);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var bgBrush = Background ?? s_defaultBackground;
        var borderBrush = BorderBrush ?? s_defaultBorder;
        var fgBrush = Foreground ?? s_defaultForeground;
        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var fontSize = FontSize > 0 ? FontSize : 12.0;

        var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);
        var borderPen = new Pen(borderBrush, 1);

        // Draw background and border
        dc.DrawRoundedRectangle(bgBrush, borderPen, bounds, new CornerRadius(4));

        double x = 8;
        double y = 6;

        // Draw series color marker
        if (SeriesBrush != null)
        {
            dc.DrawRectangle(SeriesBrush, null, new Rect(x, y + 2, 8, 8));
            x += 14;
        }

        // Draw series title
        if (!string.IsNullOrEmpty(SeriesTitle))
        {
            var titleFt = new FormattedText(SeriesTitle, fontFamily, fontSize)
            {
                Foreground = fgBrush,
                FontWeight = 700
            };
            TextMeasurement.MeasureText(titleFt);
            dc.DrawText(titleFt, new Point(x, y));
            y += titleFt.Height + 4;
        }

        // Draw X value
        if (!string.IsNullOrEmpty(XValue))
        {
            var xFt = new FormattedText($"X: {XValue}", fontFamily, fontSize)
            {
                Foreground = fgBrush
            };
            TextMeasurement.MeasureText(xFt);
            dc.DrawText(xFt, new Point(8, y));
            y += xFt.Height + 2;
        }

        // Draw Y value
        if (!string.IsNullOrEmpty(YValue))
        {
            var yFt = new FormattedText($"Y: {YValue}", fontFamily, fontSize)
            {
                Foreground = fgBrush
            };
            TextMeasurement.MeasureText(yFt);
            dc.DrawText(yFt, new Point(8, y));
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var fontSize = FontSize > 0 ? FontSize : 12.0;

        double maxWidth = 0;
        double totalHeight = 12; // top + bottom padding

        if (!string.IsNullOrEmpty(SeriesTitle))
        {
            var ft = new FormattedText(SeriesTitle, fontFamily, fontSize);
            TextMeasurement.MeasureText(ft);
            maxWidth = Math.Max(maxWidth, ft.Width + (SeriesBrush != null ? 14 : 0));
            totalHeight += ft.Height + 4;
        }

        if (!string.IsNullOrEmpty(XValue))
        {
            var ft = new FormattedText($"X: {XValue}", fontFamily, fontSize);
            TextMeasurement.MeasureText(ft);
            maxWidth = Math.Max(maxWidth, ft.Width);
            totalHeight += ft.Height + 2;
        }

        if (!string.IsNullOrEmpty(YValue))
        {
            var ft = new FormattedText($"Y: {YValue}", fontFamily, fontSize);
            TextMeasurement.MeasureText(ft);
            maxWidth = Math.Max(maxWidth, ft.Width);
            totalHeight += ft.Height + 2;
        }

        return new Size(maxWidth + 16, totalHeight);
    }

    #endregion
}
