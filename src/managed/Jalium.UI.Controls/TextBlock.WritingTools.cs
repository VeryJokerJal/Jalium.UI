using Jalium.UI.Media;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    internal virtual UIElement? DocumentWritingToolsOwner => this is InlineSourceText source ? source.SourceOwner.DocumentWritingToolsOwner : null;

    internal virtual IEnumerable<(int Start, int Length)> DocumentWritingToolsRanges(int start, int length) =>
        this is InlineSourceText source
            ? source.SourceOwner.DocumentWritingToolsRanges(start, length).Select(range => (range.Start - source.SourceOffset, range.Length))
            : [];

    private void DrawDocumentGlyphs(DrawingContext context, FormattedText text, Point origin,
        int start, int length, int? paintStart = null, bool generated = false)
    {
        if (DocumentWritingToolsOwner is not { } owner ||
            !MacOSWritingToolsPresentation.TryGetGlyphRanges(owner, out var preview, out var ranges))
        {
            context.DrawText(text, origin);
            return;
        }
        var painted = new List<uint>();
        int end = start + length;
        int first = paintStart ?? (_paintOffsets?[start] ?? start);
        foreach (var range in ranges)
            foreach (var source in DocumentWritingToolsRanges(range.Start, range.Length))
            {
                int from = Math.Max(start, source.Start), to = (int)Math.Min(end, (long)source.Start + source.Length);
                if (to <= from) continue;
                int left = generated ? 0 : Math.Clamp((_paintOffsets?[from] ?? from) - first, 0, text.Text.Length);
                int right = generated ? text.Text.Length : Math.Clamp((_paintOffsets?[to] ?? to) - first, 0, text.Text.Length);
                if (right > left) { painted.Add((uint)left); painted.Add((uint)(right - left)); }
            }
        if (!preview && painted.Count == 0)
        {
            context.DrawText(text, origin);
            return;
        }
        if (MacOSWritingToolsPresentation.UsesShapedGlyphRanges)
        {
            // Layout/cache objects also paint in later frames and previews.
            // Keep each presentation on a value snapshot of that shaped line.
            var copy = text.CreateRenderSnapshot(text.Foreground);
            copy.GlyphVisibility = preview ? 1 : 2;
            copy.GlyphRanges = painted.ToArray();
            context.DrawText(copy, origin);
        }
        else
        {
            using var clip = PushDocumentWritingToolsClip(context, preview, ranges);
            context.DrawText(text, origin);
        }
    }

    private MacOSWritingToolsPresentation.ClipScope PushDocumentWritingToolsDecorations(DrawingContext context, int start, int length)
    {
        if (DocumentWritingToolsOwner is not { } owner ||
            !MacOSWritingToolsPresentation.TryGetGlyphRanges(owner, out var preview, out var ranges)) return default;
        if (length <= 0)
        {
            if (!preview) return default;
            context.PushClip(Geometry.Empty);
            return new(context);
        }
        var pieces = ranges.SelectMany(range => DocumentWritingToolsRanges(range.Start, range.Length))
            .Where(range => range.Start < start + length && (long)range.Start + range.Length > start)
            .OrderBy(range => range.Start).ToArray();
        var end = start + length;
        var cursor = start;
        foreach (var piece in pieces)
        {
            if (piece.Start > cursor) break;
            cursor = (int)Math.Min(end, Math.Max(cursor, (long)piece.Start + piece.Length));
        }
        // A complete decoration includes its stroke caps and under/overline
        // ink outside the advance rectangle. Do not slice those endpoints.
        if (cursor == end || pieces.Length == 0)
        {
            var include = preview ? cursor == end : pieces.Length == 0;
            if (include) return default;
            context.PushClip(Geometry.Empty);
            return new(context);
        }
        return PushDocumentWritingToolsClip(context, preview, ranges);
    }

    private MacOSWritingToolsPresentation.ClipScope PushDocumentWritingToolsClip(DrawingContext context,
        bool preview, (int Start, int Length)[] ranges)
    {
        Geometry? included = null;
        foreach (var range in ranges)
            foreach (var source in DocumentWritingToolsRanges(range.Start, range.Length))
            {
                var start = Math.Max(0, source.Start);
                var end = (int)Math.Min(Text.Length, (long)source.Start + source.Length);
                if (end <= start) continue;
                foreach (var bounds in DocumentTextBounds(start, end - start))
                {
                    var rect = new RectangleGeometry(new Rect(bounds.X - TextContentOffset.X, bounds.Y - TextContentOffset.Y, bounds.Width, bounds.Height));
                    included = included is null ? rect : new CombinedGeometry(GeometryCombineMode.Union, included, rect);
                }
            }
        if (!preview && included is null) return default;
        var clip = preview ? included ?? Geometry.Empty : new CombinedGeometry(GeometryCombineMode.Exclude, GetRenderClip(), included!);
        context.PushClip(clip);
        return new(context);
    }
}
