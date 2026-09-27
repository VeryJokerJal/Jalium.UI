using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal static class CssCaretPainter
{
    internal static double NextAdvance(string text, int index, Func<int, double> position,
        double fallback)
    {
        fallback = double.IsFinite(fallback) && fallback > 0 ? fallback : 8;
        if (index < 0 || index >= text.Length) return fallback;

        var next = GraphemeClusters.NextBoundary(text, index);
        if (next <= index || next > text.Length) return fallback;
        var advance = position(next) - position(index);
        return double.IsFinite(advance) && advance > 0 ? advance : fallback;
    }

    internal static Rect Draw(DrawingContext dc, Brush brush, CssCaretShape shape,
        double x, double y, double height, double advance, double thickness)
    {
        height = Math.Max(1, height);
        advance = double.IsFinite(advance) && advance > 0 ? advance : 8;

        switch (shape)
        {
            case CssCaretShape.Block:
            {
                var bounds = new Rect(x, y, advance, height);
                // A translucent interior keeps the covered glyph legible. The edge
                // retains the authored caret brush at its original opacity.
                dc.PushOpacity(0.38);
                dc.DrawRectangle(brush, null, bounds);
                dc.Pop();
                dc.DrawRectangle(null, new Pen(brush, 1), bounds);
                return new Rect(x - 2, y - 2, advance + 4, height + 4);
            }
            case CssCaretShape.Underscore:
            {
                var baseline = y + height - Math.Max(1, thickness / 2);
                dc.DrawLine(new Pen(brush, thickness), new Point(x, baseline),
                    new Point(x + advance, baseline));
                return new Rect(x - 2, baseline - 2, advance + 4, 4);
            }
            default:
                dc.DrawLine(new Pen(brush, thickness), new Point(x, y),
                    new Point(x, y + height));
                return new Rect(x - 2, y - 1, 5, height + 2);
        }
    }
}
