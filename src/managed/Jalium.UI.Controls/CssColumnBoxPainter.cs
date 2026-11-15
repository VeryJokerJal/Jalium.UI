using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal static class CssColumnBoxPainter
{
    internal static bool PaintOwn(Visual visual, DrawingContext context)
    {
        if (visual is not FrameworkElement { IsCssColumnFrame: true, CssColumnBox: { } box } source) return false;
        if (TextBlock.PaintColumnInline(source, context, border: false)) return true;
        var property = CssDependencyPropertyLookup.Find(source.GetType(), "Background");
        var brush = property is null ? null : source.GetValue(property) as Brush;
        if (source.IsCssInlineFrame && box.InlineFragments is not null)
        {
            CssBorderImagePainter.EmptyBackground(source, context);
            if (property is not null) CssInlineBoxPainter.Background(source, property, brush, context);
            return true;
        }
        var clone = CssBoxDecorationBreakProperties.Value(source) == CssBoxDecorationBreak.Clone;
        foreach (var piece in box.Pieces)
        {
            context.PushClip(new RectangleGeometry(piece.Destination));
            context.PushTransform(new TranslateTransform(piece.Destination.X - piece.Source.X, piece.Destination.Y - piece.Source.Y));
            try
            {
                if (property is not null && !CssInlineBoxPainter.Background(source, property, brush, context))
                {
                    var bounds = clone ? new Rect(piece.Source.Location, piece.Destination.Size) : new Rect(box.NaturalSize);
                    var radii = CssReplacedContent.Radii(source, bounds.Size);
                    var (border, padding) = CssBoxMetrics.BackgroundInsets(source, source.CssLayout?.ContainingWidthCache ?? bounds.Width);
                    if (brush is not null)
                    {
                        var geometry = new CssRoundedRectangleGeometry(bounds, radii);
                        void Shape(Brush fill) => context.DrawGeometry(fill, null, geometry);
                        if (!CssBackgroundPainter.TryDraw(source, property, brush, context, bounds, radii, border, padding, Shape)) Shape(brush);
                    }
                    else CssBorderImagePainter.Decoration(source, context, bounds, border);
                }
            }
            finally { context.Pop(); context.Pop(); }
        }
        TextBlock.PaintColumnText(source, context);
        return true;
    }

    internal static bool PaintBorder(Visual visual, DrawingContext context)
    {
        if (visual is not FrameworkElement { IsCssColumnFrame: true, CssColumnBox: { } box } source) return false;
        if (TextBlock.PaintColumnInline(source, context, border: true)) return true;
        Border? painter = null;
        if (source.IsCssInlineFrame && box.InlineFragments is not null)
        {
            CssInlineBoxPainter.Border(source, context, ref painter);
            return true;
        }
        var clone = CssBoxDecorationBreakProperties.Value(source) == CssBoxDecorationBreak.Clone;
        foreach (var piece in box.Pieces)
        {
            context.PushClip(new RectangleGeometry(piece.Destination));
            context.PushTransform(new TranslateTransform(piece.Destination.X - piece.Source.X, piece.Destination.Y - piece.Source.Y));
            try
            {
                if (CssInlineBoxPainter.Border(source, context, ref painter)) continue;
                var bounds = clone ? new Rect(piece.Source.Location, piece.Destination.Size) : new Rect(box.NaturalSize);
                var border = CssBoxMetrics.BackgroundInsets(source, source.CssLayout?.ContainingWidthCache ?? bounds.Width).Border;
                if (CssBorderImagePainter.Active(source, bounds, border)) continue;
                var colors = CssBorderPaintProperties.Get(source);
                if (colors is null && CssDependencyPropertyLookup.Find(source.GetType(), "BorderBrush") is { } property && source.GetValue(property) is Brush brush)
                    colors = new(brush, brush, brush, brush);
                if (colors is null) continue;
                painter ??= new Border(); painter.SetRootDpi(source.DpiScale);
                painter.DrawCssSideColorsFor(context, colors, CssBorderStyleProperties.Get(source), bounds, border, CssReplacedContent.Radii(source, bounds.Size));
            }
            finally { context.Pop(); context.Pop(); }
        }
        return true;
    }

    internal static void PaintRules(Visual visual, DrawingContext context)
    {
        if (visual is not FrameworkElement source || !CssMultiColumnLayout.Try(source, out var layout) || layout?.IsApplied != true) return;
        var style = (CssBorderLineStyle)source.GetValue(CssMultiColumnProperties.RuleStyleProperty)!;
        if (style is CssBorderLineStyle.None or CssBorderLineStyle.Hidden) return;
        var width = (double)source.GetValue(CssMultiColumnProperties.RuleWidthProperty)!;
        if (!(width > 0)) return;
        var brush = source.GetValue(CssMultiColumnProperties.RuleColorProperty) as Brush ?? (Brush)source.GetValue(TextBlock.ForegroundProperty)!;
        PaintRules(context, source, layout.Rules, width, style, brush);
    }

    internal static void PaintRules(DrawingContext context, FrameworkElement source, IEnumerable<Rect> rules,
        double width, CssBorderLineStyle style, Brush brush)
    {
        if (width <= 0 || style is CssBorderLineStyle.None or CssBorderLineStyle.Hidden) return;
        if (style == CssBorderLineStyle.Inset) style = CssBorderLineStyle.Ridge;
        if (style == CssBorderLineStyle.Outset) style = CssBorderLineStyle.Groove;
        var painter = new Border(); painter.SetRootDpi(source.DpiScale);
        foreach (var rule in rules)
            painter.DrawCssSideColorsFor(context, new(brush, brush, brush, brush), new(style, style, style, style),
                new(rule.X - width / 2, rule.Y, width, rule.Height), new(width, 0, 0, 0), default);
    }
}
