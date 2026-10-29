namespace Jalium.UI.Controls;

/// <summary>Range geometry over the caret positions used by the editor's text layout.</summary>
internal static class ImeTextGeometry
{
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

    private static bool IsValid(Rect rectangle) => double.IsFinite(rectangle.X) &&
        double.IsFinite(rectangle.Y) && double.IsFinite(rectangle.Height) && rectangle.Height > 0;
}
