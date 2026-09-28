using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

/// <summary>Paints CSS border sides, or an opt-in native border, for direct renderers.</summary>
internal static class CssBorderAdornment
{
    internal static void Draw(FrameworkElement owner, DrawingContext drawingContext,
        ref Border? painter, Rect? bounds = null, bool drawNative = false)
    {
        var colors = CssBorderPaintProperties.Get(owner);
        if (colors is null && (!drawNative || owner is not Control { BorderBrush: not null }))
            return;
        var box = bounds ?? new Rect(owner.RenderSize);
        var border = CssBoxMetrics.BackgroundInsets(owner,
            owner.CssLayout?.ContainingWidthCache ?? owner.RenderSize.Width).Border;
        if (border.Left <= 0 && border.Top <= 0 && border.Right <= 0 && border.Bottom <= 0)
            return;

        if (colors is not null)
        {
            painter ??= new Border();
            painter.SetRootDpi(owner.DpiScale);
            painter.DrawCssSideColorsFor(drawingContext, colors,
                CssBorderStyleProperties.Get(owner), box, border,
                CssBorderRadiusProperties.Get(owner)?.Resolve(new Size(box.Width, box.Height)));
            return;
        }

        var control = (Control)owner;
        var radii = CssBorderRadiusProperties.Get(owner)?.Resolve(box.Size) ??
            CssBackgroundPainter.CircularRadii(control.CornerRadius).Normalize(box.Size);
        var ring = new PathGeometry { FillRule = FillRule.EvenOdd };
        ring.Figures.Add(CssRoundedRectangleGeometry.Figure(box, radii));
        var inner = ControlRenderGeometry.GetContentRect(box, border);
        if (inner.Width > 0 && inner.Height > 0)
            ring.Figures.Add(CssRoundedRectangleGeometry.Figure(inner,
                radii.Inset(border).Normalize(inner.Size)));
        ring.Freeze();
        drawingContext.DrawGeometry(control.BorderBrush, null, ring);
    }
}
