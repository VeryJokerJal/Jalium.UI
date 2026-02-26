using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a horizontal line that separates items in a MenuFlyout.
/// </summary>
public sealed class MenuFlyoutSeparator : Control
{
    /// <summary>
    /// Initializes a new instance of the MenuFlyoutSeparator class.
    /// </summary>
    public MenuFlyoutSeparator()
    {
        Focusable = false;
        IsHitTestVisible = false;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(0, 9); // 4px margin top + 1px line + 4px margin bottom; width determined by siblings
    }

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc) return;
        base.OnRender(drawingContext);

        var brush = new Jalium.UI.Media.SolidColorBrush(
            Jalium.UI.Media.Color.FromRgb(67, 67, 70));
        var pen = new Jalium.UI.Media.Pen(brush, 1);

        dc.DrawLine(pen, new Point(12, 4.5), new Point(RenderSize.Width - 12, 4.5));
    }
}
