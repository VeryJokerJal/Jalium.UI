using System.Collections;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.Charts;

/// <summary>
/// A control that renders chart legend items as colored rectangles with labels.
/// </summary>
public class ChartLegend : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Controls.Automation.GenericAutomationPeer(this, Jalium.UI.Automation.AutomationControlType.Pane);

    private static readonly SolidColorBrush s_defaultForeground = new(Color.FromRgb(220, 220, 220));

    #region Dependency Properties

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(ChartLegend),
            new PropertyMetadata(Orientation.Horizontal, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Items dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(nameof(Items), typeof(IEnumerable), typeof(ChartLegend),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the orientation of legend item layout.
    /// </summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets the legend items to display.
    /// </summary>
    public IEnumerable? Items
    {
        get => (IEnumerable?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var items = Items;
        if (items == null)
            return;

        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var fontSize = FontSize > 0 ? FontSize : 12.0;
        var foreground = Foreground ?? s_defaultForeground;

        const double markerSize = 12;
        const double markerTextGap = 4;
        const double itemSpacing = 16;

        double offsetX = Padding.Left;
        double offsetY = Padding.Top;

        foreach (var item in items)
        {
            if (item is not ChartLegendItem legendItem)
                continue;

            if (!legendItem.IsVisible)
                continue;

            // Draw the color marker rectangle
            var markerRect = new Rect(offsetX, offsetY + 1, markerSize, markerSize);
            dc.DrawRectangle(legendItem.Brush, null, markerRect);

            // Draw the label text
            var ft = new FormattedText(legendItem.Label, fontFamily, fontSize)
            {
                Foreground = foreground
            };
            TextMeasurement.MeasureText(ft);
            dc.DrawText(ft, new Point(offsetX + markerSize + markerTextGap, offsetY));

            if (Orientation == Orientation.Horizontal)
            {
                offsetX += markerSize + markerTextGap + ft.Width + itemSpacing;
            }
            else
            {
                offsetY += Math.Max(markerSize, ft.Height) + itemSpacing / 2.0;
            }
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var items = Items;
        if (items == null)
            return new Size(0, 0);

        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var fontSize = FontSize > 0 ? FontSize : 12.0;

        const double markerSize = 12;
        const double markerTextGap = 4;
        const double itemSpacing = 16;

        double totalWidth = 0;
        double totalHeight = 0;
        double lineWidth = 0;
        double maxLineHeight = 0;

        foreach (var item in items)
        {
            if (item is not ChartLegendItem legendItem || !legendItem.IsVisible)
                continue;

            var ft = new FormattedText(legendItem.Label, fontFamily, fontSize);
            TextMeasurement.MeasureText(ft);

            var itemWidth = markerSize + markerTextGap + ft.Width + itemSpacing;
            var itemHeight = Math.Max(markerSize, ft.Height);

            if (Orientation == Orientation.Horizontal)
            {
                lineWidth += itemWidth;
                totalWidth = Math.Max(totalWidth, lineWidth);
                maxLineHeight = Math.Max(maxLineHeight, itemHeight);
            }
            else
            {
                totalWidth = Math.Max(totalWidth, itemWidth);
                totalHeight += itemHeight + itemSpacing / 2.0;
            }
        }

        if (Orientation == Orientation.Horizontal)
        {
            totalHeight = maxLineHeight;
        }

        return new Size(
            totalWidth + Padding.Left + Padding.Right,
            totalHeight + Padding.Top + Padding.Bottom);
    }

    #endregion
}
