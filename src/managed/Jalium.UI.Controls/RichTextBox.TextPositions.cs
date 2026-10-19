using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

public partial class RichTextBox
{
    private bool OwnsTextPosition(TextPointer position)
    {
        if (!ReferenceEquals(position.Document, _document) || !ReferenceEquals(_document.Parent, this)) return false;
        if (position.Parent is null) return position.Offset >= 0 && position.Offset <= _document.GetText().Length;
        return position.Parent is TextElement element && ReferenceEquals(element.GetFlowDocument(), _document);
    }

    internal bool HasValidTextPositionLayout(TextPointer position) =>
        OwnsTextPosition(position) && IsMeasureValid && IsArrangeValid && !_layoutDirty && _layoutCache is { } layout &&
        _layoutWidth == GetContentBounds().Width && ReferenceEquals(_layoutContext, RenderContext.Current) &&
        NativePresentationIsCurrent(layout.Blocks);

    private FlowDocumentLayoutInfo? GetTextPositionLayout(TextPointer position, out Rect bounds)
    {
        bounds = default;
        if (!OwnsTextPosition(position) || RenderSize.Width <= 0 || RenderSize.Height <= 0) return null;
        if (!IsMeasureValid || !IsArrangeValid) UpdateLayout();
        if (!OwnsTextPosition(position) || RenderSize.Width <= 0 || RenderSize.Height <= 0) return null;
        bounds = GetContentBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;
        var layout = EnsureLayout(bounds.Width);
        return OwnsTextPosition(position) && layout is { TextPositionLines.Count: > 0 } ? layout : null;
    }

    private static int FindTextPositionLine(List<(LineLayoutInfo line, double y, double x)> lines, int offset, bool backward)
    {
        int previous = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].line;
            if (offset >= line.StartOffset && offset <= line.EndOffset)
            {
                if (offset == line.EndOffset && !backward && i + 1 < lines.Count && lines[i + 1].line.StartOffset == offset) continue;
                if (offset == line.StartOffset && backward && i > 0 && lines[i - 1].line.EndOffset == offset) return i - 1;
                return i;
            }
            if (line.StartOffset > offset) return previous >= 0 ? previous : i;
            previous = i;
        }
        return lines.Count - 1;
    }

    internal Rect GetTextPositionCharacterRect(TextPointer position, LogicalDirection direction)
    {
        var layout = GetTextPositionLayout(position, out var bounds);
        if (layout is null) return Rect.Empty;
        var text = _document.GetText(); int offset = position.DocumentOffset;
        int start = offset, end = offset;
        bool emptyCell = position.Parent is TableCell { Blocks.Count: 0 };
        if (!emptyCell && direction == LogicalDirection.Backward && offset > 0)
        {
            start = GraphemeClusters.PreviousBoundary(text, offset);
            end = GraphemeClusters.IsBoundary(text, offset) ? offset : GraphemeClusters.NextBoundary(text, offset);
        }
        else if (!emptyCell && direction == LogicalDirection.Forward && offset < text.Length)
        {
            start = GraphemeClusters.IsBoundary(text, offset) ? offset : GraphemeClusters.PreviousBoundary(text, offset);
            end = GraphemeClusters.NextBoundary(text, start);
        }
        var entry = layout.TextPositionLines[FindTextPositionLine(layout.TextPositionLines, start, false)];
        var line = entry.line;
        double x = entry.x + bounds.Left - _horizontalOffset, y = entry.y + bounds.Top - _verticalOffset;
        bool hasContent = start < end && start < line.EndOffset && start < text.Length &&
            text[start] is not ('\r' or '\n' or '\u2029') && !emptyCell;
        double left = GetXOffsetInLine(line, Math.Min(start, line.EndOffset));
        double right = left;
        if (hasContent)
        {
            end = Math.Min(end, line.EndOffset);
            if (line.NativeLine is { } native)
            {
                var rectangles = native.Paragraph.Selection(native.Index, start - line.ParagraphOffset, end - start);
                if (rectangles.Length > 0)
                { left = rectangles.Min(r => r.X); right = rectangles.Max(r => r.X + r.Width); }
            }
            else right = GetXOffsetInLine(line, end, backward: true);
        }
        return ClipToCell(layout, line,
            new Rect(x + Math.Min(left, right), y, Math.Abs(right - left), Math.Max(1, line.Height)), bounds);
    }

    internal bool IsTextPositionAtLineStart(TextPointer position)
    {
        var layout = GetTextPositionLayout(position, out _);
        if (layout is null) return false;
        var line = layout.TextPositionLines[FindTextPositionLine(layout.TextPositionLines,
            position.DocumentOffset, position.LogicalDirection == LogicalDirection.Backward)].line;
        return position.DocumentOffset == line.StartOffset;
    }

    internal TextPointer? GetTextPositionLineStart(TextPointer position, int count, out int actualCount)
    {
        actualCount = 0;
        var layout = GetTextPositionLayout(position, out _);
        if (layout is null) return null;
        var lines = layout.TextPositionLines;
        int current = FindTextPositionLine(lines, position.DocumentOffset, position.LogicalDirection == LogicalDirection.Backward);
        int direction = Math.Sign(count); long remaining = Math.Abs((long)count);
        double x = lines[current].x; bool tables = ContainsTables(layout.Blocks);
        while (remaining-- > 0)
        {
            int next = FindAdjacentTextLine(lines, current, direction, x, tables, layout.CellClips);
            if (next < 0 || next >= lines.Count) break;
            current = next; actualCount += direction;
        }
        return _document.GetPositionAtOffset(lines[current].line.StartOffset, LogicalDirection.Forward);
    }

    private static int FindAdjacentTextLine(List<(LineLayoutInfo line, double y, double x)> lines,
        int current, int direction, double x, bool tables, Dictionary<LineLayoutInfo, Rect>? clips = null, double clipOffsetX = 0)
    {
        if (!tables) return current + direction;
        double y = lines[current].y;
        double Distance((LineLayoutInfo line, double y, double x) item)
        {
            double left = item.x, right = item.x + item.line.Width;
            if (clips is not null && clips.TryGetValue(item.line, out var clip))
            { left = clip.Left + clipOffsetX; right = clip.Right + clipOffsetX; }
            return Math.Max(0, Math.Max(left - x, x - right));
        }

        var next = lines.Select((item, index) => (item, index))
            .Where(value => direction * (value.item.y - y) > 0.01)
            .OrderBy(value => Math.Abs(value.item.y - y))
            .ThenBy(value => Distance(value.item)).FirstOrDefault();
        return next.item.line is null ? direction < 0 ? -1 : lines.Count : next.index;
    }
}
