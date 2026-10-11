using Jalium.UI.Interop;

namespace Jalium.UI.Controls;

/// <summary>Shared geometry queries over the same caret positions used by text rendering.</summary>
internal static class ImeTextGeometry
{
    internal static bool TryGetFormattedRange(string text, int start, int length, Point origin,
        string fontFamily, double fontSize, int fontWeight, int fontStyle, float maxWidth,
        out ImeTextRangeGeometry geometry)
    {
        geometry = default;
        if (!ImeTextEncoding.TryNormalizeUtf16Range(text, start, length, out start, out length) ||
            !TextMeasurement.HitTestTextRangeWrapped(text, fontFamily, fontSize, fontWeight, fontStyle,
                maxWidth, (uint)start, (uint)length, out var range) ||
            range.TextPosition != start || range.Length > length || (length > 0 && range.Length == 0)) return false;
        var rectangle = new Rect(origin.X + range.X, origin.Y + range.Y, range.Width, range.Height);
        if (!IsValid(rectangle) || !double.IsFinite(rectangle.Width) || rectangle.Width < 0) return false;
        geometry = new(rectangle, (int)range.TextPosition, (int)range.Length);
        return true;
    }

    internal static bool TryGetFirstLineRange(string text, int start, int length,
        Func<int, bool, Rect> caretRectangle, out ImeTextRangeGeometry geometry)
    {
        geometry = default;
        if (!ImeTextEncoding.TryNormalizeUtf16Range(text, start, length, out start, out length)) return false;
        Rect first = caretRectangle(start, false);
        if (!IsValid(first)) return false;
        if (length == 0)
        {
            geometry = new(new Rect(first.X, first.Y, 0, first.Height), start, 0);
            return true;
        }
        double left = first.X, right = first.X, bottom = first.Bottom;
        int end = start;
        while (end < start + length)
        {
            Rect leading = caretRectangle(end, false);
            if (!IsValid(leading) || Math.Abs(leading.Y - first.Y) > 0.5) break;
            int next = Math.Min(start + length, GraphemeClusters.NextBoundary(text, end));
            if (text[end] is '\r' or '\n') { end = next; break; }
            Rect trailing = caretRectangle(end, true);
            if (!IsValid(trailing) || Math.Abs(trailing.Y - first.Y) > 0.5) break;
            left = Math.Min(left, Math.Min(leading.X, trailing.X));
            right = Math.Max(right, Math.Max(leading.X, trailing.X));
            bottom = Math.Max(bottom, Math.Max(leading.Bottom, trailing.Bottom));
            end = next;
        }
        if (end == start) return false;
        geometry = new(new Rect(left, first.Y, right - left, bottom - first.Y), start, end - start);
        return true;
    }

    internal static Rect GetFormattedCaret(string text, int index, bool trailing, Point origin, double height,
        string fontFamily, double fontSize, int fontWeight, int fontStyle, float maxWidth,
        Func<string, double> measure)
    {
        if (text.Length > 0 && TextMeasurement.HitTestTextPositionWrapped(text, fontFamily, fontSize,
            fontWeight, fontStyle, maxWidth, (uint)index, trailing, out var hit))
            return new Rect(origin.X + hit.CaretX, origin.Y + hit.CaretY, 0,
                hit.CaretHeight > 0 ? hit.CaretHeight : Math.Max(1, height));
        int end = trailing ? GraphemeClusters.NextBoundary(text, index) : index;
        return new Rect(origin.X + measure(text[..end]), origin.Y, 0, Math.Max(1, height));
    }

    internal static bool TryHitTest(string text, Point point, Point origin, double height,
        string fontFamily, double fontSize, int fontWeight, int fontStyle, float maxWidth,
        Func<string, double> measure, bool requireTextBounds, out int index)
    {
        index = -1;
        if (TextMeasurement.HitTestPointWrapped(text, fontFamily, fontSize, fontWeight, fontStyle,
            maxWidth, (float)(point.X - origin.X), (float)(point.Y - origin.Y), out var hit))
        {
            int position = ImeTextEncoding.SnapToGraphemeBoundary(text,
                Math.Clamp((int)hit.TextPosition, 0, text.Length), forward: false);
            if (requireTextBounds)
            {
                if (position == text.Length || hit.IsInside == 0) return false;
                Rect leading = GetFormattedCaret(text, position, false, origin, height,
                    fontFamily, fontSize, fontWeight, fontStyle, maxWidth, measure);
                Rect trailing = GetFormattedCaret(text, position, true, origin, height,
                    fontFamily, fontSize, fontWeight, fontStyle, maxWidth, measure);
                if (point.Y < leading.Top || point.Y >= leading.Bottom ||
                    point.X < Math.Min(leading.X, trailing.X) || point.X > Math.Max(leading.X, trailing.X)) return false;
            }
            index = hit.IsTrailingHit != 0 ? GraphemeClusters.NextBoundary(text, position) : position;
            return true;
        }
        if (requireTextBounds)
        {
            if (text.Length == 0 || !TryGetFirstLineRange(text, 0, text.Length,
                (offset, trailing) => GetFormattedCaret(text, offset, trailing, origin, height,
                    fontFamily, fontSize, fontWeight, fontStyle, maxWidth, measure), out var bounds) ||
                point.Y < bounds.Rectangle.Top || point.Y >= bounds.Rectangle.Bottom ||
                point.X < bounds.Rectangle.Left || point.X > bounds.Rectangle.Right) return false;
        }
        double distance = double.PositiveInfinity;
        foreach (int boundary in GraphemeClusters.GetBoundaries(text))
        {
            double next = Math.Abs(point.X - origin.X - measure(text[..boundary]));
            if (next >= distance) continue;
            distance = next;
            index = boundary;
        }
        return index >= 0;
    }

    private static bool IsValid(Rect rectangle) => double.IsFinite(rectangle.X) &&
        double.IsFinite(rectangle.Y) && double.IsFinite(rectangle.Height) && rectangle.Height > 0;
}
