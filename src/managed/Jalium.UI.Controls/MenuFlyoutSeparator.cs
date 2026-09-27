using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a horizontal line that separates items in a MenuFlyout.
/// </summary>
public class MenuFlyoutSeparator : Control
{
    private Border? _cssBorderPainter;

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
        => new Jalium.UI.Automation.Peers.GenericAutomationPeer(this, Jalium.UI.Automation.Peers.AutomationControlType.Separator);

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
        var insets = CssBoxMetrics.ContentInsets(this,
            CssLayout?.ContainingWidthCache ?? availableSize.Width);
        return new Size(
            ControlRenderGeometry.GetAvailableLength(insets.Left + insets.Right, availableSize.Width),
            ControlRenderGeometry.GetAvailableLength(9 + insets.Top + insets.Bottom, availableSize.Height));
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;
        base.OnRender(drawingContext);
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0)
            return;

        var rect = new Rect(RenderSize);
        if (Background is { } background)
        {
            var backgroundLayer = GetEffectiveValueLayer(BackgroundProperty);
            var cssRadius = CssBorderRadiusProperties.Get(this);
            if (cssRadius is null && backgroundLayer is not
                    (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState))
                dc.DrawRoundedRectangle(background, null, rect, CornerRadius);
            else
            {
                var radii = cssRadius?.Resolve(RenderSize) ??
                    CssBackgroundPainter.CircularRadii(CornerRadius).Normalize(RenderSize);
                var shape = new CssRoundedRectangleGeometry(rect, radii);
                var (border, padding) = CssBoxMetrics.BackgroundInsets(this,
                    CssLayout?.ContainingWidthCache ?? RenderSize.Width);
                if (!CssBackgroundPainter.TryDraw(this, BackgroundProperty, background, dc,
                        rect, radii, border, padding,
                        brush => dc.DrawGeometry(brush, null, shape)))
                    dc.DrawGeometry(background, null, shape);
            }
        }

        var content = ControlRenderGeometry.GetContentRect(rect,
            CssBoxMetrics.ContentInsets(this,
                CssLayout?.ContainingWidthCache ?? RenderSize.Width));
        if (content.Width <= 24 || content.Height <= 0)
            return;

        var brush = Foreground
            ?? TryFindResource("MenuFlyoutPresenterBorderBrush") as Brush
            ?? new Jalium.UI.Media.SolidColorBrush(Jalium.UI.Media.Color.FromRgb(67, 67, 70));
        var pen = new Jalium.UI.Media.Pen(brush, 1);

        dc.DrawLine(pen,
            new Point(content.X + 12, content.Y + content.Height / 2),
            new Point(content.Right - 12, content.Y + content.Height / 2));
    }

    /// <inheritdoc />
    protected override void OnPostRender(DrawingContext drawingContext)
    {
        base.OnPostRender(drawingContext);
        CssBorderAdornment.Draw(this, drawingContext, ref _cssBorderPainter, drawNative: true);
    }
}
