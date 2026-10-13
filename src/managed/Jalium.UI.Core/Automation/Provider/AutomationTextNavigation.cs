using System.Globalization;

namespace Jalium.UI.Automation.Provider;

/// <summary>Shared UTF-16 navigation over the editor's current rendered layout.</summary>
internal static class AutomationTextNavigation
{
    internal static int LineFromIndex(IReadOnlyList<AutomationTextLine> lines, int index, int textLength, bool backwardAffinity = false)
    {
        if (index < 0 || index > textLength || lines.Count == 0) return -1;
        for (int line = lines.Count - 1; line >= 0; line--)
            if (index >= lines[line].Start)
                return backwardAffinity && line > 0 && index == lines[line].Start
                    && lines[line - 1].Start + lines[line - 1].Length == index ? line - 1 : line;
        return -1;
    }

    internal static bool TryGetCharacterRange(string text, int index, out AutomationTextSpan range)
    {
        range = default;
        if (index < 0 || index > text.Length) return false;
        if (index == text.Length) { range = new(index, 0); return true; }
        // AppKit treats each newline character as its own glyph, including CRLF.
        if (text[index] is '\r' or '\n') { range = new(index, 1); return true; }
        int[] boundaries = StringInfo.ParseCombiningCharacters(text);
        int position = Array.BinarySearch(boundaries, index);
        if (position < 0) position = ~position - 1;
        int start = boundaries[Math.Max(0, position)];
        int end = position + 1 < boundaries.Length ? boundaries[position + 1] : text.Length;
        range = new(start, end - start);
        return true;
    }

    internal static IReadOnlyList<AutomationTextSpan> VisibleRanges(
        IAutomationTextProviderSource source, IAutomationTextViewSource view)
    {
        Rect viewport = view.TextViewport;
        if (viewport.IsEmpty || viewport.Width <= 0 || viewport.Height <= 0) return [];
        string text = source.Text;
        var result = new List<AutomationTextSpan>();
        foreach (var line in view.GetTextLines())
        {
            if (line.Bounds.Bottom <= viewport.Top || line.Bounds.Top >= viewport.Bottom) continue;
            int start = Math.Clamp(line.Start, 0, text.Length), end = Math.Clamp(start + line.Length, start, text.Length);
            if (start == end)
            {
                if (Visible(start, 0)) Add(start, 0);
                continue;
            }
            var elements = StringInfo.GetTextElementEnumerator(text, start);
            int offset = start;
            while (elements.MoveNext() && offset < end)
            {
                // ElementIndex is relative to the sliced string on some
                // runtimes. Advance our document offset explicitly.
                int length = Math.Min(elements.GetTextElement().Length, end - offset);
                if (Visible(offset, length)) Add(offset, length);
                offset += length;
            }
        }
        return result;

        bool Visible(int start, int length) => source.GetBoundingRectangles(start, length).Any(rectangle =>
        {
            Rect clipped = Rect.Intersect(rectangle, viewport);
            return !clipped.IsEmpty && clipped.Width > 0 && clipped.Height > 0;
        });
        void Add(int start, int length)
        {
            if (result.Count > 0 && result[^1].Start + result[^1].Length == start)
                result[^1] = new(result[^1].Start, start + length - result[^1].Start);
            else result.Add(new(start, length));
        }
    }

    internal static bool TryGetRangeFromPoint(IAutomationTextProviderSource source, IAutomationTextViewSource view,
        Point point, out AutomationTextSpan range)
    {
        range = default;
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !view.TextViewport.Contains(point)
            || source is Peers.AutomationPeer { Owner: UIElement owner } && !new AutomationVisibility(owner).Contains(point)
            || !view.TryGetInsertionIndex(point, out int insertion)) return false;
        string text = source.Text;
        insertion = Math.Clamp(insertion, 0, text.Length);
        // Hit testing finds the nearest insertion edge. Check both adjacent
        // clusters to return the glyph on either half, including bidi text.
        foreach (int candidate in new[] { insertion, insertion - 1 })
        {
            if (!TryGetCharacterRange(text, candidate, out var glyph) || glyph.Length == 0) continue;
            if (source.GetBoundingRectangles(glyph.Start, glyph.Length).Any(rectangle => rectangle.Contains(point)))
            { range = glyph; return true; }
        }
        range = new(insertion, 0);
        return true;
    }
}
