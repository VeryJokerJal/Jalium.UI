using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private sealed record InlineBackgroundRange(Inline Element, int Start, int End, int Depth)
    {
        internal readonly List<Rect> Rects = [];
        internal CssInlineBoxFragment[] Fragments = [];
    }

    private void DrawInlineBackgrounds(DrawingContext context)
    {
        foreach (var range in GetInlineBackgroundRanges())
        {
            if (range.Element.Background is { } brush)
            {
                context.PushClip(GetRenderClip());
                try
                {
                    foreach (var fragment in range.Fragments)
                    {
                        using var writing = PushTextWritingLine(context, fragment.Rect.Top, fragment.Rect.Height);
                        context.DrawRectangle(brush, null, fragment.Rect);
                    }
                }
                finally { context.Pop(); }
            }
            // Document inlines have no native border box. Explicit lengths,
            // percentages and auto image widths paint over their used text boxes;
            // numeric multipliers retain the initial zero ordinary-border width.
            foreach (var fragment in range.Fragments)
            {
                using var writing = PushTextWritingLine(context, fragment.Rect.Top, fragment.Rect.Height);
                CssBorderImagePainter.DecorationFragment(this, range.Element, context, fragment, default);
            }
        }
    }

    internal CssInlineBoxFragment[] GetDocumentDecorationFragments(Inline element)
    {
        EnsureLayout(GetLayoutConstraintWidth(TextContentSize.Width));
        var fragments = GetInlineBackgroundRanges().FirstOrDefault(range => ReferenceEquals(range.Element, element))?.Fragments ?? [];
        var offset = TextContentOffset;
        if (offset == default && !IsVerticalTextLayout) return fragments;
        return fragments.Select(fragment =>
        {
            var rect = TextWritingRect(fragment.Rect, fragment.Rect.Top, fragment.Rect.Height);
            rect.Offset(offset.X, offset.Y);
            return fragment with { Rect = rect };
        }).ToArray();
    }

    internal double DocumentBorderImageInkPadding()
    {
        if (_inlines is null) return 0;
        static double Ink(IEnumerable<Inline> inlines)
        {
            var result = 0d;
            foreach (var inline in inlines)
            {
                if(CssDisplayProperties.SuppressesBoxTree(inline))continue;
                var outset = CssBorderImageProperties.Outset(inline, default);
                result = Math.Max(result, Math.Max(Math.Max(outset.Left, outset.Right), Math.Max(outset.Top, outset.Bottom)));
                if (inline is Span span) result = Math.Max(result, Ink(span.Inlines));
            }
            return result;
        }
        return Ink(_inlines);
    }

    private List<InlineBackgroundRange> GetInlineBackgroundRanges()
    {
        var ranges = new List<InlineBackgroundRange>();
        if (_inlines is null) return ranges;
        var offset = 0;
        foreach (var inline in _inlines)
            CollectInlineBackgroundRanges(inline, ref offset, 0, ranges);
        if (ranges.Count == 0) return ranges;
        ranges.Sort(static (left, right) =>
        {
            var depth = left.Depth.CompareTo(right.Depth);
            return depth != 0 ? depth : left.Start.CompareTo(right.Start);
        });

        var renderWidth = GetContentWidth(TextContentSize.Width);
        var lineY = GetVerticalContentOffset(GetLineHeight());
        foreach (var line in _layoutLines)
        {
            var currentY = TextLineTop(line, lineY);
            lineY += line.Height;
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
                    if (right < left) (left, right) = (right, left);
                    if (ownsEllipsis) right = Math.Max(right, ellipsisRight);
                    if (range.Start <= leftEdge && leftEdge < range.End)
                        left -= GetLineLeftPadding(line);
                    if (range.Start <= rightEdge && rightEdge < range.End)
                        right += GetLineRightPadding(line);
                    if (right > left)
                        range.Rects.Add(new Rect(left, currentY, right - left, line.Height));
                }
            }
        }
        foreach (var range in ranges)
        {
            var clone = CssBoxDecorationBreakProperties.Value(range.Element) == CssBoxDecorationBreak.Clone;
            var rtl = CssNode.Get(range.Element).FrameworkParent?.GetValue(FrameworkElement.FlowDirectionProperty) is FlowDirection.RightToLeft;
            range.Fragments = CssInlineBoxFragment.Join(range.Rects.Select((rect, index) =>
                new CssInlineBoxFragment(rect, clone || (rtl ? index == range.Rects.Count - 1 : index == 0),
                    clone || (rtl ? index == 0 : index == range.Rects.Count - 1))).ToArray(), clone, rtl);
        }
        return ranges;
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
        if (offset > start && !CssDisplayProperties.SuppressesBoxTree(inline) && (inline.Background is not null || inline.GetValue(CssBorderImageProperties.SourceProperty) is not null))
            ranges.Add(new InlineBackgroundRange(inline, start, offset, depth));
    }
}
