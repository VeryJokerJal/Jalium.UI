using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private readonly record struct InlineShadowSegment(int Start, int End, Effect? Effect);

    private bool TryDrawInlineTextShadows(DrawingContext context,
        in TextLayoutLine line, double originX, double lineY, Action drawLine)
    {
        if (_inlines is null || line.Length == 0 ||
            context is not IEffectDrawingContext
                { IsElementEffectCaptureEnabled: true, SupportsCssTextShadowsOnly: true } ||
            context is not IOffsetDrawingContext)
            return false;

        var rootEffect = CssTextShadowProperties.Value(this);
        var lineStart = line.StartIndex;
        var lineEnd = lineStart + line.Length;
        var segments = new List<InlineShadowSegment>();
        var hasOverride = false;
        void Add(int start, int end, Effect? effect)
        {
            if (end <= start) return;
            hasOverride |= !ReferenceEquals(effect, rootEffect);
            if (segments.Count > 0 && segments[^1] is var previous &&
                previous.End == start && ReferenceEquals(previous.Effect, effect))
                segments[^1] = previous with { End = end };
            else
                segments.Add(new(start, end, effect));
        }

        var cursor = lineStart;
        var ranges = GetInlineTextRanges();
        var low = 0;
        var high = ranges.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (ranges[middle].End <= lineStart) low = middle + 1;
            else high = middle;
        }
        for (var index = low; index < ranges.Count; index++)
        {
            var range = ranges[index];
            if (range.Start >= lineEnd) break;
            var start = Math.Max(range.Start, lineStart);
            var end = Math.Min(range.End, lineEnd);
            if (end <= start) continue;
            Add(cursor, start, rootEffect);
            Add(start, end, CssTextShadowProperties.Value(range.Run));
            cursor = end;
        }
        Add(cursor, lineEnd, rootEffect);
        if (!hasOverride) return false;

        var height = Math.Max(1, line.Height);
        var bounds = new Rect(0, lineY - height,
            Math.Max(1, RenderSize.Width), 3 * height);
        foreach (var segment in segments)
        {
            if (segment.Effect?.HasEffect != true) continue;
            var left = GetInlineBoundaryX(line, originX, segment.Start);
            var right = GetInlineBoundaryX(line, originX, segment.End);
            if (right <= left) continue;
            var capture = CssTextShadowPainter.Begin(context, segment.Effect,
                bounds, shadowsOnly: true);
            if (capture is null) continue;
            context.PushClip(new RectangleGeometry(new Rect(left,
                lineY - height, right - left, 3 * height)));
            try { drawLine(); }
            finally
            {
                context.Pop();
                capture.Value.End();
            }
        }
        // All adjacent glyphs and decorations must be above every shadow.
        drawLine();
        return true;
    }
}
