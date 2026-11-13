using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

/// <summary>Box decoration follows each line fragment of a native content host.</summary>
internal static class CssInlineBoxPainter
{
    internal static IReadOnlyList<CssInlineBoxFragment> Fragments(FrameworkElement owner) =>
        CssFirstLinePainter.Fragments(owner,
            owner is { IsCssColumnFrame: true, CssColumnBox.InlineFragments: { } projected } ? projected : owner.CssInlineBox!.Fragments);

    internal static bool Background(FrameworkElement owner, DependencyProperty property, Brush? brush, DrawingContext context)
    {
        if (!owner.IsCssInlineFrame) return false;
        var (border, padding) = CssBoxMetrics.BackgroundInsets(owner, owner.CssLayout?.ContainingWidthCache ?? owner.RenderSize.Width);
        foreach (var fragment in Fragments(owner))
        {
            var radii = fragment.Radii(owner);
            var geometry = new CssRoundedRectangleGeometry(fragment.Rect, radii);
            if (owner is Border { BackdropEffect: { HasEffect: true } backdrop })
            {
                context.PushClip(geometry);
                context.DrawBackdropEffect(fragment.Rect, backdrop, new CornerRadius(0));
                context.Pop();
            }
            if (brush is null) continue;
            void Shape(Brush fill) => context.DrawGeometry(fill, null, geometry);
            if (!CssBackgroundPainter.TryDraw(owner, property, brush, context, fragment.Rect,
                    radii, fragment.Edges(border), fragment.Edges(padding), Shape)) Shape(brush);
        }
        return true;
    }

    internal static bool Border(FrameworkElement owner, DrawingContext context, ref Border? painter)
    {
        if (!owner.IsCssInlineFrame) return false;
        var border = CssBoxMetrics.BackgroundInsets(owner, owner.CssLayout?.ContainingWidthCache ?? owner.RenderSize.Width).Border;
        foreach (var fragment in Fragments(owner))
        {
            var edges = fragment.Edges(border);
            if (CssBorderImagePainter.Active(owner, fragment.Rect, edges)) continue;
            if (owner is Border { BorderBrush: { } brush } && CssBorderPaintProperties.Get(owner) is null)
            {
                var ring = new PathGeometry { FillRule = FillRule.EvenOdd };
                var radii = fragment.Radii(owner);
                ring.Figures.Add(CssRoundedRectangleGeometry.Figure(fragment.Rect, radii));
                var inner = ControlRenderGeometry.GetContentRect(fragment.Rect, edges);
                if (inner.Width > 0 && inner.Height > 0)
                    ring.Figures.Add(CssRoundedRectangleGeometry.Figure(inner, radii.Inset(edges).Normalize(inner.Size)));
                ring.Freeze(); context.DrawGeometry(brush, null, ring);
            }
            else CssBorderAdornment.Draw(owner, context, ref painter, fragment.Rect,
                borderThickness: edges, fragmentRadii: fragment.Radii(owner));
        }
        return true;
    }
}
