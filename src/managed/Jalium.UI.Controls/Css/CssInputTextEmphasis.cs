using System.Globalization;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

/// <summary>
/// Adds annotation space around input rows while retaining the complete source
/// layout for shaping, bidi edges, glyph visibility, and recorded drawings.
/// </summary>
internal readonly struct CssInputTextEmphasis
{
    internal delegate bool RowQuery(uint position, bool backward, out TextLineMetrics row);
    internal delegate bool RangeQuery(uint start, uint length, out TextRangeMetrics range);

    private readonly CssTextEmphasisPainter.Mark? _mark;
    internal double NaturalHeight { get; }
    internal double Above { get; }
    internal double Below { get; }
    internal double Height => Math.Round(NaturalHeight) + Above + Below;
    internal bool Active => _mark is not null;
    internal double Ascent { get; }

    internal CssInputTextEmphasis(Control owner)
    {
        var metrics = TextMeasurement.GetFontMetrics(
            owner.FontFamily?.GetRenderingSource(owner) ?? FrameworkElement.DefaultFontFamilyName,
            owner.FontSize, owner.FontWeight.ToOpenTypeWeight(), owner.FontStyle.ToOpenTypeStyle());
        NaturalHeight = metrics.LineHeight;
        Ascent = metrics.Ascent;
        _mark = CssTextEmphasisPainter.Create(owner, owner.FontSize, owner.Foreground);
        Above = _mark is { Under: false } ? Math.Ceiling(_mark.Height) : 0;
        Below = _mark is { Under: true } ? Math.Ceiling(_mark.Height) : 0;
    }

    internal double RowY(double nativeY, double nativeHeight) => Active
        ? Math.Round(nativeY / Math.Max(1, nativeHeight)) * Height : nativeY;

    internal double BodyY(double nativeY, double nativeHeight) => RowY(nativeY, nativeHeight) + Above;

    internal double NativeY(double annotatedY, double nativeHeight)
    {
        if (!Active) return annotatedY;
        var row = Math.Max(0, Math.Floor(annotatedY / Math.Max(1, Height)));
        return row * nativeHeight + Math.Clamp(annotatedY - row * Height - Above,
            0, Math.Max(0, nativeHeight - .01));
    }

    internal bool Draw(DrawingContext context, FormattedText text, Point origin,
        RowQuery? rowQuery = null, RangeQuery? rangeQuery = null, bool opaqueCharacters = false,
        Action<DrawingContext, FormattedText, Point>? drawText = null)
    {
        if (_mark is not { } mark || text.Text.Length == 0) return false;
        var width = text.NativeNoWrap || text.MaxTextWidth >= float.MaxValue
            ? float.PositiveInfinity : (float)Math.Max(1, text.MaxTextWidth);
        rowQuery ??= (uint position, bool backward, out TextLineMetrics row) =>
            TextMeasurement.TryGetVisualLineMetrics(text.Text, text.FontFamily, text.FontSize,
                text.FontWeight, text.FontStyle, width, position, backward, out row);
        rangeQuery ??= (uint start, uint length, out TextRangeMetrics range) =>
            TextMeasurement.HitTestTextRangeWrapped(text.Text, text.FontFamily, text.FontSize,
                text.FontWeight, text.FontStyle, width, start, length, out range);

        var rows = new List<TextLineMetrics>();
        uint position = 0;
        do
        {
            if (!rowQuery(position, false, out var row) || row.Length == 0) break;
            rows.Add(row);
            var next = row.TextPosition + row.Length;
            if (next <= position) break;
            position = next;
        } while (position < text.Text.Length);
        if (rows.Count == 0) return false;

        var filtered = OperatingSystem.IsMacOS() &&
            RenderContext.Current?.Backend is RenderBackend.Metal or RenderBackend.Software;
        foreach (var row in rows)
        {
            var snapshot = text.CreateRenderSnapshot(text.Foreground);
            snapshot.GlyphRanges = VisibleRanges(text, row.TextPosition, row.Length);
            snapshot.GlyphVisibility = 1;
            if (snapshot.GlyphRanges.Length == 0) continue;
            var top = origin.Y + BodyY(row.Y, row.Height);
            // Backends without source glyph masks still retain whole-paragraph
            // shaping. Their native row band separates neighboring row ink.
            if (!filtered) context.PushClip(new RectangleGeometry(
                new Rect(origin.X - text.FontSize, top,
                    Math.Max(row.X + row.Width, text.MaxTextWidth < float.MaxValue ? text.MaxTextWidth : row.Width) + 2 * text.FontSize,
                    Math.Max(1, row.Height))));
            try
            {
                var point = new Point(origin.X, top - row.Y);
                if (drawText is not null) drawText(context, snapshot, point); else context.DrawText(snapshot, point);
            }
            finally { if (!filtered) context.Pop(); }
        }

        var boundaries = StringInfo.ParseCombiningCharacters(text.Text);
        for (var index = 0; index < boundaries.Length; index++)
        {
            int start = boundaries[index], end = index + 1 < boundaries.Length ? boundaries[index + 1] : text.Text.Length;
            if ((!opaqueCharacters && !CssTextEmphasis.ShouldMark(text.Text.AsSpan(start, end - start))) ||
                VisibleRanges(text, (uint)start, (uint)(end - start)).Length == 0 ||
                !rangeQuery((uint)start, (uint)(end - start), out var range)) continue;
            var row = rows.FirstOrDefault(item => start >= item.TextPosition && start < item.TextPosition + item.Length);
            if (row.Length == 0) continue;
            CssTextEmphasisPainter.Draw(context, mark, origin.X + range.X + range.Width / 2,
                origin.Y + BodyY(row.Y, row.Height) + Ascent);
        }
        return true;
    }

    private static uint[] VisibleRanges(FormattedText text, uint start, uint length)
    {
        uint end = start + length;
        if (text.GlyphVisibility == 0 || text.GlyphRanges is not { } ranges) return [start, length];
        var visible = new List<uint>();
        if (text.GlyphVisibility == 1)
        {
            for (var index = 0; index + 1 < ranges.Length; index += 2)
            {
                uint left = Math.Max(start, ranges[index]), right = Math.Min(end, ranges[index] + ranges[index + 1]);
                if (right > left) { visible.Add(left); visible.Add(right - left); }
            }
        }
        else
        {
            var cursor = start;
            var excluded = Enumerable.Range(0, ranges.Length / 2)
                .Select(index => (Start: ranges[index * 2], End: ranges[index * 2] + ranges[index * 2 + 1]))
                .OrderBy(range => range.Start);
            foreach (var range in excluded)
            {
                if (range.End <= cursor || range.Start >= end) continue;
                if (range.Start > cursor) { visible.Add(cursor); visible.Add(range.Start - cursor); }
                cursor = Math.Max(cursor, Math.Min(end, range.End));
            }
            if (cursor < end) { visible.Add(cursor); visible.Add(end - cursor); }
        }
        return visible.ToArray();
    }
}
