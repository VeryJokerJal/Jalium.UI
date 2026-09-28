using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private readonly record struct InlineBackgroundRange(Brush Brush,
        int Start, int End, int Depth);

    private void DrawInlineBackgrounds(DrawingContext context)
    {
        if (_inlines is null) return;
        var ranges = new List<InlineBackgroundRange>();
        var offset = 0;
        foreach (var inline in _inlines)
            CollectInlineBackgroundRanges(inline, ref offset, 0, ranges);
        if (ranges.Count == 0) return;
        ranges.Sort(static (left, right) =>
        {
            var depth = left.Depth.CompareTo(right.Depth);
            return depth != 0 ? depth : left.Start.CompareTo(right.Start);
        });

        var renderWidth = GetContentWidth(RenderSize.Width);
        Rect? clipBounds = null;
        var offsetY = 0d;
        if (context is IClipBoundsDrawingContext { CurrentClipBounds: Rect clip } &&
            context is IOffsetDrawingContext offsetContext)
        {
            clipBounds = clip;
            offsetY = offsetContext.Offset.Y;
        }
        var lineY = GetVerticalContentOffset(GetLineHeight());
        foreach (var line in _layoutLines)
        {
            var currentY = lineY;
            lineY += line.Height;
            if (clipBounds is Rect visible &&
                (offsetY + currentY + line.Height <= visible.Y ||
                 offsetY + currentY >= visible.Bottom)) continue;
            var sourceEnd = line.StartIndex + line.Length;
            var trimmedLine = TryGetTrimmedLine(line, renderWidth, out var trimmed);
            var visibleEnd = trimmedLine
                ? Math.Min(sourceEnd, line.StartIndex + trimmed.PrefixLength) : sourceEnd;
            var ellipsisSource = trimmedLine
                ? (trimmed.PrefixLength > 0 ? visibleEnd - 1 : line.StartIndex) : -1;
            if (visibleEnd > line.StartIndex || ellipsisSource >= 0)
            {
                var first = GetLineEdgeSourceIndex(line.StartIndex, visibleEnd, true);
                var last = GetLineEdgeSourceIndex(line.StartIndex, visibleEnd, false);
                if (first < 0) first = ellipsisSource;
                if (trimmedLine) last = ellipsisSource;
                var leftEdge = FlowDirection == FlowDirection.RightToLeft ? last : first;
                var rightEdge = FlowDirection == FlowDirection.RightToLeft ? first : last;
                var originX = GetLineOriginX(line, renderWidth);
                var ellipsisRight = 0d;
                if (trimmedLine)
                {
                    var tabOrigin = ResolveTextIndent(line.IndentApplies, renderWidth) +
                        GetLineLeftPadding(line);
                    ellipsisRight = originX + MeasureTrimmedPrefix(line.StartIndex,
                        trimmed.PrefixLength, sourceEnd, trimmed.Fragmented, tabOrigin);
                }
                foreach (var range in ranges)
                {
                    var start = Math.Max(range.Start, line.StartIndex);
                    var end = Math.Min(range.End, visibleEnd);
                    var ownsEllipsis = range.Start <= ellipsisSource && ellipsisSource < range.End;
                    if (end <= start && !ownsEllipsis) continue;
                    var left = end > start ? GetInlineBoundaryX(line, originX, start) : originX;
                    var right = end > start ? GetInlineBoundaryX(line, originX, end) : originX;
                    if (ownsEllipsis) right = Math.Max(right, ellipsisRight);
                    if (range.Start <= leftEdge && leftEdge < range.End)
                        left -= GetLineLeftPadding(line);
                    if (range.Start <= rightEdge && rightEdge < range.End)
                        right += GetLineRightPadding(line);
                    if (right > left)
                        context.DrawRectangle(range.Brush, null,
                            new Rect(left, currentY, right - left, line.Height));
                }
            }
        }
    }

    private static void CollectInlineBackgroundRanges(Inline inline, ref int offset,
        int depth, List<InlineBackgroundRange> ranges)
    {
        var start = offset;
        switch (inline)
        {
            case Run run:
                offset += run.Text.Length;
                break;
            case LineBreak:
                offset++;
                break;
            case Span span:
                foreach (var child in span.Inlines)
                    CollectInlineBackgroundRanges(child, ref offset, depth + 1, ranges);
                break;
        }
        if (offset > start && inline.Background is { } brush)
            ranges.Add(new InlineBackgroundRange(brush, start, offset, depth));
    }
}
