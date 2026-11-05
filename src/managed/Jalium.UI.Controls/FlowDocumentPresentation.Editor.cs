using Jalium.UI.Documents;

namespace Jalium.UI.Controls;

internal sealed partial class FlowDocumentPresentation
{
    // Editing keeps source offsets, while the text provider uses exposed offsets.
    // Both are views over the original document and the existing box geometry.
    internal string EditorExposedText => Exposure.Text;
    internal int EditorExposedOffset(int source) => Exposure.FromSource(source);
    internal int EditorSourceOffset(int exposed, bool forward = true) => Exposure.ToSource(exposed, forward);
    internal (int Start, int Length)[] EditorSourceRanges(int start, int length) => Exposure.Ranges(start, length).ToArray();
    internal IReadOnlyList<Rect> EditorTextBounds(int start, int length) => TextBounds(start, length, clip: false);
    internal IReadOnlyList<Rect> EditorExposedBounds(int start, int length) => Exposure.Ranges(start, length)
        .SelectMany(range => TextBounds(range.Start, range.Length, clip: false)).ToArray();

    internal TextPointer? EditorInsertionPosition(int source, bool forward = true)
    {
        if (_document is null) return null;
        var direction = forward ? LogicalDirection.Forward : LogicalDirection.Backward;
        var position = _document.GetPositionAtOffset(source, direction);
        if (position?.Parent is Run current && !Styling.CssDisplayProperties.SuppressesBoxTree(current) &&
            (forward ? position.Offset < current.Text.Length : position.Offset > 0)) return position;
        foreach (var paragraph in _paragraphs)
            foreach (var run in Runs(paragraph.Source.Inlines))
            {
                if (Styling.CssDisplayProperties.SuppressesBoxTree(run)) continue;
                var start = _document.GetDocumentOffset(run, 0);
                if (forward ? source >= start && source < start + run.Text.Length : source > start && source <= start + run.Text.Length)
                    return new(_document, run, source - start, direction);
            }
        return position;
        static IEnumerable<Run> Runs(IEnumerable<Inline> inlines)
        {
            foreach (var inline in inlines)
                if (inline is Run run) yield return run;
                else if (inline is Span span) foreach (var child in Runs(span.Inlines)) yield return child;
        }
    }

    internal TextPointer? EditorPosition(Point point)
    {
        var offset = OffsetFromPoint(point);
        var line = EditorLines.Where(line => offset >= line.Start && offset <= line.End)
            .OrderBy(line => Math.Max(line.Top - point.Y, Math.Max(0, point.Y - line.Top - line.Height)))
            .ThenBy(line => Math.Max(line.Left - point.X, Math.Max(0, point.X - line.Left - line.Width))).FirstOrDefault();
        return EditorInsertionPosition(offset, line.Height <= 0 || offset != line.End);
    }

    internal IReadOnlyList<Line> EditorLines
    {
        get
        {
            EnsureTextGeometry();
            var padding = _document?.PagePadding ?? default;
            var lines = HasColumns || _hasDocumentHeights ? _positionLines : _lines;
            return lines.Where(line => !line.IsSpace && line.Height > 0)
                .Select(line => line with { Top = line.Top + padding.Top - _pageTop, Left = line.Left + padding.Left })
                .OrderBy(line => line.Start).ThenBy(line => line.End).ToArray();
        }
    }

    internal Rect? EditorCaret(int source, bool backward)
    {
        EnsureTextGeometry();
        if (_document is null) return null;
        var text = _document.GetText();
        source = Math.Clamp(source, 0, text.Length);
        var lines = EditorLines;
        var candidates = lines.Where(line => source >= line.Start && source <= line.End).ToArray();
        var selected = backward
            ? candidates.FirstOrDefault(line => line.End == source, candidates.FirstOrDefault())
            : candidates.FirstOrDefault(line => line.Start == source, candidates.LastOrDefault());
        if (selected.Height <= 0)
            selected = lines.OrderBy(line => Math.Min(Math.Abs(line.Start - source), Math.Abs(line.End - source))).FirstOrDefault();
        if (selected.Height <= 0) return null;

        // A zero-length range at a seam can belong to either original source.
        // Choose a glyph on the requested visual line, then its leading/trailing
        // edge; this also handles the paragraph terminator and hidden sources.
        var before = backward || source >= selected.End;
        var start = before ? GraphemeClusters.PreviousBoundary(text, Math.Min(source, selected.End)) : Math.Max(source, selected.Start);
        var end = before ? Math.Min(source, selected.End) : GraphemeClusters.NextBoundary(text, start);
        var rectangles = EditorTextBounds(start, Math.Max(0, end - start));
        var rectangle = rectangles.Where(rect => rect.Bottom > selected.Top && rect.Top < selected.Top + selected.Height)
            .OrderBy(rect => Math.Abs(rect.Top - selected.Top)).FirstOrDefault();
        if (rectangle.Height <= 0)
            rectangle = EditorTextBounds(Math.Clamp(source, selected.Start, selected.End), 0)
                .OrderBy(rect => Math.Abs(rect.Top - selected.Top)).FirstOrDefault();
        if (rectangle.Height > 0)
        {
            var rtl = _document.FlowDirection == FlowDirection.RightToLeft;
            var x = before != rtl ? rectangle.Right : rectangle.Left;
            return new(x, rectangle.Top, 0, rectangle.Height);
        }
        return new(selected.Left + (before && double.IsFinite(selected.Width) ? selected.Width : 0), selected.Top, 0, selected.Height);
    }
}
