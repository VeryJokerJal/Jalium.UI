using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

/// <summary>Paints CSS-originated text decoration lines for native text controls.</summary>
internal static class CssTextDecorationPainter
{
    internal static void Draw(DrawingContext context, DependencyObject source, UIElement visual, Brush brush,
        CssTextDecorationLine lines, double startX, double endX,
        double lineTop, double baseline, double fontSize, double textUnderEdge,
        double totalInlineSize = -1, bool firstFragment = true, bool lastFragment = true,
        double inlineSizeBefore = -1, double inlineSizeAfter = -1)
    {
        if (endX <= startX || lines == CssTextDecorationLine.None) return;

        var position = CssTextDecorationProperties.UnderlinePosition(source);
        var needsFontMetrics = CssTextDecorationProperties.ThicknessFromFont(source) ||
            (lines & CssTextDecorationLine.Underline) != 0 &&
            (position & CssTextUnderlinePosition.FromFont) != 0;
        var fontMetrics = needsFontMetrics ? GetSourceFontMetrics(source, fontSize) : default;
        var requested = CssTextDecorationProperties.Thickness(source);
        if (CssTextDecorationProperties.ThicknessFromFont(source) &&
            (fontMetrics.Available & 64) != 0 && fontMetrics.UnderlineThickness > 0)
            requested = fontMetrics.UnderlineThickness;
        var dpi = VisualTreeHelper.GetDpi(visual).DpiScaleY;
        var minimum = double.IsFinite(dpi) && dpi > 0 ? 1 / dpi : 1;
        var thickness = double.IsFinite(requested) ? Math.Max(minimum, requested) : minimum;
        var style = CssTextDecorationProperties.Style(source);

        if ((lines & (CssTextDecorationLine.SpellingError | CssTextDecorationLine.GrammarError)) != 0)
        {
            var errorBrush = (lines & CssTextDecorationLine.SpellingError) != 0
                ? Brushes.Red : Brushes.Green;
            var center = baseline + Math.Max(1, fontSize * 0.08) + Math.Max(thickness, 1) + thickness / 2;
            DrawStroke(context, errorBrush, thickness, CssTextDecorationStyle.Wavy,
                startX, endX, center);
            return;
        }

        var clone = CssBoxDecorationBreakProperties.Value(source) == CssBoxDecorationBreak.Clone;
        var basis = !clone && totalInlineSize >= 0 && double.IsFinite(totalInlineSize)
            ? totalInlineSize : endX - startX;
        if (CssTextDecorationProperties.SkipInset(source) == CssTextDecorationSkipInset.Auto)
        {
            var inset = Math.Min(thickness / 2, (endX - startX) / 4);
            startX += inset;
            endX -= inset;
        }

        var decorationInset = CssTextDecorationProperties.Inset(source);
        var autoInset = Math.Min(thickness, Math.Max(0, endX - startX) / 4);
        var logicalStart = decorationInset.Auto ? autoInset : decorationInset.Start.Resolve(basis, 0);
        var logicalEnd = decorationInset.Auto ? autoInset : decorationInset.End.Resolve(basis, 0);
        if (!clone)
        {
            // With slice, a positive inset can consume more than one line fragment.
            logicalStart = inlineSizeBefore >= 0 && logicalStart > 0
                ? Math.Max(0, logicalStart - inlineSizeBefore)
                : firstFragment ? logicalStart : 0;
            logicalEnd = inlineSizeAfter >= 0 && logicalEnd > 0
                ? Math.Max(0, logicalEnd - inlineSizeAfter)
                : lastFragment ? logicalEnd : 0;
        }
        var rightToLeft = source.GetValue(FrameworkElement.FlowDirectionProperty) is
            FlowDirection.RightToLeft;
        if (rightToLeft)
        {
            endX -= logicalStart;
            startX += logicalEnd;
        }
        else
        {
            startX += logicalStart;
            endX -= logicalEnd;
        }
        if (endX <= startX) return;

        if ((lines & CssTextDecorationLine.Overline) != 0)
            DrawStroke(context, brush, thickness, style, startX, endX, lineTop);
        if ((lines & CssTextDecorationLine.LineThrough) != 0)
            DrawStroke(context, brush, thickness, style, startX, endX,
                lineTop + (baseline - lineTop) * 0.55);
        if ((lines & CssTextDecorationLine.Underline) != 0)
        {
            var offset = CssTextDecorationProperties.ResolveUnderlineOffset(source, fontSize);
            var under = (position & CssTextUnderlinePosition.Under) != 0;
            var fromFont = (position & CssTextUnderlinePosition.FromFont) != 0 &&
                (fontMetrics.Available & 32) != 0 && double.IsFinite(fontMetrics.UnderlinePosition);
            var zero = fromFont ? baseline + fontMetrics.UnderlinePosition
                : under && double.IsFinite(textUnderEdge)
                    ? Math.Max(baseline, textUnderEdge) : baseline;
            var innerExtent = style switch
            {
                CssTextDecorationStyle.Double => thickness * 1.5,
                CssTextDecorationStyle.Wavy => Math.Max(thickness, 1) + thickness / 2,
                _ => thickness / 2,
            };
            var autoOffset = fromFont ? 0 : under ? Math.Max(1, fontSize * 0.04)
                : Math.Max(1, fontSize * 0.08);
            var center = double.IsNaN(offset)
                ? zero + (fromFont || under ? autoOffset + innerExtent : Math.Max(autoOffset, innerExtent))
                : zero + offset + innerExtent;
            DrawStroke(context, brush, thickness, style, startX, endX,
                center);
        }
    }

    internal static FontUnitMetrics GetSourceFontMetrics(DependencyObject source, double fallbackSize)
    {
        var fontSize = CssDependencyPropertyLookup.Find(source.GetType(), "FontSize") is { } sizeProperty &&
            source.GetValue(sizeProperty) is double size && double.IsFinite(size) && size > 0
                ? size : fallbackSize;
        var family = CssDependencyPropertyLookup.Find(source.GetType(), "FontFamily") is { } familyProperty
            ? source.GetValue(familyProperty) as FontFamily : null;
        var weight = CssDependencyPropertyLookup.Find(source.GetType(), "FontWeight") is { } weightProperty &&
            source.GetValue(weightProperty) is FontWeight fontWeight
                ? fontWeight.ToOpenTypeWeight() : 400;
        var style = CssDependencyPropertyLookup.Find(source.GetType(), "FontStyle") is { } styleProperty &&
            source.GetValue(styleProperty) is FontStyle fontStyle
                ? fontStyle.ToOpenTypeStyle() : 0;
        return TextMeasurement.GetFontUnitMetrics(
            family?.GetRenderingSource(source) ?? FrameworkElement.DefaultFontFamilyName,
            fontSize, weight, style);
    }

    private static void DrawStroke(DrawingContext context, Brush brush,
        double thickness, CssTextDecorationStyle style, double startX, double endX, double y)
    {
        if (style == CssTextDecorationStyle.Dotted)
        {
            var radius = thickness / 2;
            var first = startX + radius;
            var last = endX - radius;
            if (first > last)
            {
                context.DrawEllipse(brush, null, new Point((startX + endX) / 2, y),
                    Math.Min(radius, (endX - startX) / 2), radius);
                return;
            }
            for (var x = first; x <= last; x += thickness * 2)
                context.DrawEllipse(brush, null, new Point(x, y), radius, radius);
            return;
        }

        var pen = new Pen(brush, thickness);
        if (style == CssTextDecorationStyle.Double)
        {
            context.DrawLine(pen, new Point(startX, y - thickness),
                new Point(endX, y - thickness));
            context.DrawLine(pen, new Point(startX, y + thickness),
                new Point(endX, y + thickness));
            return;
        }
        if (style == CssTextDecorationStyle.Dashed) pen.DashStyle = DashStyles.Dash;
        else if (style == CssTextDecorationStyle.Wavy)
        {
            var length = endX - startX;
            var halfPeriod = Math.Max(thickness * 3, length / 2048);
            var amplitude = Math.Max(thickness, 1);
            var wave = new PathGeometry();
            var figure = new PathFigure
            {
                StartPoint = new Point(startX, y), IsClosed = false, IsFilled = false,
            };
            for (var index = 0; startX + index * halfPeriod < endX; index++)
            {
                var x = startX + index * halfPeriod;
                var nextX = Math.Min(endX, x + halfPeriod);
                figure.Segments.Add(new QuadraticBezierSegment(
                    new Point((x + nextX) / 2, y + (index % 2 == 0 ? -amplitude : amplitude)),
                    new Point(nextX, y), true));
            }
            wave.Figures.Add(figure);
            context.DrawGeometry(null, pen, wave);
            return;
        }
        context.DrawLine(pen, new Point(startX, y), new Point(endX, y));
    }
}
