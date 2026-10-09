using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private bool _usesInlineFontLayout;
    private readonly Dictionary<(int Start, int Length), TextMeasurementCacheEntry> _inlineFontMeasurements = new();

    private bool HasInlineFontOverrides()
    {
        if (_inlines is null) return false;
        var family = FontFamily.GetRenderingSource(this);
        var size = FontSize;
        var weight = FontWeight.ToOpenTypeWeight();
        var style = FontStyle.ToOpenTypeStyle();
        var stretch = FontStretch.ToOpenTypeStretch();
        foreach (var range in GetInlineTextRanges())
        {
            var run = range.Run;
            if (!string.Equals(GetRunFontFamily(run), family, StringComparison.Ordinal) ||
                Math.Abs(GetRunFontSize(run) - size) > 0.001 ||
                run.FontWeight.ToOpenTypeWeight() != weight ||
                run.FontStyle.ToOpenTypeStyle() != style ||
                run.FontStretch.ToOpenTypeStretch() != stretch)
                return true;
        }
        return false;
    }

    private string GetRunFontFamily(Run run)
        => run.FontFamily?.GetRenderingSource(run) ?? FontFamily.GetRenderingSource(this);

    private double GetRunFontSize(Run run)
        => run.FontSize;

    private TextMeasurementCacheEntry MeasureInlineFragment(Run run, int start, int length)
    {
        if (length <= 0) return default;
        var key = (start, length);
        if (_inlineFontMeasurements.TryGetValue(key, out var cached)) return cached;

        var text = PaintSlice(start, length);
        if (text.Length == 0) return default;
        cached = MeasureRunText(run, text);
        if (_inlineFontMeasurements.Count >= MaxTextWidthCacheEntries)
            _inlineFontMeasurements.Clear();
        _inlineFontMeasurements[key] = cached;
        return cached;
    }

    private TextMeasurementCacheEntry MeasureRunText(Run run, string text)
    {
        if (text.Length == 0) return default;
        var size = GetRunFontSize(run);
        var formatted = new FormattedText(text, GetRunFontFamily(run), size)
        {
            FontWeight = run.FontWeight.ToOpenTypeWeight(),
            FontStyle = run.FontStyle.ToOpenTypeStyle(),
            FontStretch = run.FontStretch.ToOpenTypeStretch(),
        };
        double width;
        double widthWithWhitespace;
        if (TextMeasurement.MeasureText(formatted) && formatted.IsMeasured)
        {
            width = formatted.Width;
            widthWithWhitespace = Math.Max(width, formatted.WidthIncludingTrailingWhitespace);
        }
        else
        {
            widthWithWhitespace = EstimateTextWidth(text, size);
            width = EstimateTextWidth(text, size,
                GetTextLengthWithoutTrailingLayoutWhitespace(text));
        }
        return new TextMeasurementCacheEntry(width, widthWithWhitespace, formatted);
    }

    private double MeasureInlineRange(int start, int length,
        bool includeTrailingWhitespace = false, bool forceFragmented = false,
        double tabOrigin = 0)
    {
        if (ContainsPreservedTab(start, length))
            return MeasureTabbedInlineRange(start, length, includeTrailingWhitespace,
                tabOrigin);
        return MeasureInlineRangeWithoutTabs(start, length, includeTrailingWhitespace,
            forceFragmented);
    }

    private double MeasureInlineRangeWithoutTabs(int start, int length,
        bool includeTrailingWhitespace, bool forceFragmented)
    {
        includeTrailingWhitespace |= _countTrailingWhitespace;
        if (_hasLetterSpacing)
        {
            var visibleEnd = start + length;
            if (!includeTrailingWhitespace)
                while (visibleEnd > start && IsBreakableWhitespace(_visualText[visibleEnd - 1]))
                    visibleEnd--;
            return MeasureLetterSpacedRange(start, visibleEnd);
        }
        var width = MeasureInlineRangeBase(start, length, includeTrailingWhitespace,
            forceFragmented || _hasWordSpacing);
        if (!_hasWordSpacing || length <= 0) return width;
        var end = start + length;
        if (!includeTrailingWhitespace)
            while (end > start && IsBreakableWhitespace(_visualText[end - 1])) end--;
        return Math.Max(0, width + WordSpacingInRange(start, end));
    }

    private double MeasureInlineRangeBase(int start, int length,
        bool includeTrailingWhitespace = false, bool forceFragmented = false)
    {
        includeTrailingWhitespace |= _countTrailingWhitespace;
        if (length <= 0) return 0;
        if (!_usesInlineFontLayout && !forceFragmented)
            return MeasureTextWidth(PaintSlice(start, length), includeTrailingWhitespace);

        var end = start + length;
        if (!includeTrailingWhitespace)
            while (end > start && IsBreakableWhitespace(_visualText[end - 1])) end--;
        var width = 0.0;
        var cursor = start;
        foreach (var range in GetInlineTextRanges())
        {
            if (range.End <= cursor) continue;
            if (range.Start >= end) break;
            if (range.Start > cursor)
            {
                width += MeasureText(PaintSlice(cursor, range.Start - cursor))
                    .WidthIncludingTrailingWhitespace;
                cursor = range.Start;
            }
            var fragmentEnd = Math.Min(end, range.End);
            width += MeasureInlineFragment(range.Run, cursor, fragmentEnd - cursor)
                .WidthIncludingTrailingWhitespace;
            cursor = fragmentEnd;
            if (cursor >= end) break;
        }
        if (cursor < end)
            width += MeasureText(PaintSlice(cursor, end - cursor))
                .WidthIncludingTrailingWhitespace;
        return width;
    }

    private InlineLineMetrics GetInlineLineMetrics(int start, int length)
    {
        var baseSize = FontSize;
        var baseMetrics = TextMeasurement.GetFontMetrics(FontFamily.GetRenderingSource(this),
            baseSize, FontWeight.ToOpenTypeWeight(), FontStyle.ToOpenTypeStyle());
        var ascent = baseMetrics.Ascent > 0 ? baseMetrics.Ascent : baseSize * 0.8;
        var descent = baseMetrics.Descent > 0 ? baseMetrics.Descent : baseSize * 0.2;
        var gap = Math.Max(0, GetLineHeight() - ascent - descent);
        if (_usesInlineFontLayout && length > 0)
        {
            var end = start + length;
            foreach (var range in GetInlineTextRanges())
            {
                if (range.End <= start || range.Start >= end) continue;
                var run = range.Run;
                var size = GetRunFontSize(run);
                var family = GetRunFontFamily(run);
                var weight = run.FontWeight.ToOpenTypeWeight();
                var style = run.FontStyle.ToOpenTypeStyle();
                var metrics = TextMeasurement.GetFontMetrics(family, size, weight, style);
                var runAscent = metrics.Ascent > 0 ? metrics.Ascent : size * 0.8;
                var runDescent = metrics.Descent > 0 ? metrics.Descent : size * 0.2;
                ascent = Math.Max(ascent, runAscent);
                descent = Math.Max(descent, runDescent);
                gap = Math.Max(gap, Math.Max(0,
                    TextMeasurement.GetLineHeight(family, size, weight, style) -
                    runAscent - runDescent));
            }
        }
        return new InlineLineMetrics(Math.Max(GetLineHeight(), ascent + descent + gap),
            ascent, ascent + descent);
    }

    private double GetInlineDecorationUnderEdge(in TextLayoutLine line, int start, int end,
        double baseline, double lineY)
    {
        if (!_usesInlineFontLayout) return lineY + line.GlyphHeight;
        var descent = 0.0;
        foreach (var range in GetInlineTextRanges())
        {
            if (range.End <= start || range.Start >= end) continue;
            var run = range.Run;
            var size = GetRunFontSize(run);
            var metrics = TextMeasurement.GetFontMetrics(GetRunFontFamily(run), size,
                run.FontWeight.ToOpenTypeWeight(), run.FontStyle.ToOpenTypeStyle());
            descent = Math.Max(descent, metrics.Descent > 0 ? metrics.Descent : size * 0.2);
        }
        return descent > 0 ? baseline + descent : lineY + line.GlyphHeight;
    }

    private void DrawStyledInlineLine(DrawingContext context, in TextLayoutLine line,
        double originX, double lineY, int visibleEnd = int.MaxValue)
    {
        var lineEnd = Math.Min(line.StartIndex + line.Length, visibleEnd);
        var x = originX;
        DrawStyledInlineRange(context, line, line.StartIndex, lineEnd, ref x, lineY);
        if (line.SoftHyphenVisible && lineEnd == line.StartIndex + line.Length)
            DrawVisibleSoftHyphen(context, line, ref x, lineY);
    }

    private void DrawStyledInlineRange(DrawingContext context, in TextLayoutLine line,
        int start, int end, ref double x, double lineY)
    {
        if (_hasLetterSpacing)
            DrawLetterSpacedInlineRange(context, line, start, end, ref x, lineY);
        else if (_hasWordSpacing)
            DrawWordSpacedInlineRange(context, line, start, end, ref x, lineY);
        else
            DrawStyledInlineRangeCore(context, line, start, end, ref x, lineY);
    }

    private void DrawStyledInlineRangeCore(DrawingContext context, in TextLayoutLine line,
        int start, int end, ref double x, double lineY)
    {
        var cursor = start;
        foreach (var range in GetInlineTextRanges())
        {
            if (range.End <= cursor) continue;
            if (range.Start >= end) break;
            if (range.Start > cursor)
            {
                DrawStyledFragment(context, null, cursor, Math.Min(end, range.Start) - cursor,
                    ref x, lineY, line);
                cursor = Math.Min(end, range.Start);
            }
            if (cursor >= end) break;
            var fragmentEnd = Math.Min(range.End, end);
            DrawStyledFragment(context, range.Run, cursor, fragmentEnd - cursor,
                ref x, lineY, line);
            cursor = fragmentEnd;
            if (cursor >= end) break;
        }
        if (cursor < end)
            DrawStyledFragment(context, null, cursor, end - cursor,
                ref x, lineY, line);
    }

    private void DrawStyledFragment(DrawingContext context, Run? run,
        int start, int length, ref double x, double lineY, in TextLayoutLine line)
    {
        if (length <= 0) return;
        if (_internalPaintSpacingUnits.Count == 0)
        {
            DrawStyledFragmentRaw(context, run, start, length, ref x, lineY, line);
            return;
        }

        var end = start + length;
        var cursor = start;
        for (var index = FirstInternalPaintUnitAtOrAfter(start);
             index < _internalPaintSpacingUnits.Count &&
             _internalPaintSpacingUnits[index].Start < end;
             index++)
        {
            var unit = _internalPaintSpacingUnits[index];
            if (unit.End <= cursor) continue;
            if (unit.Start < cursor || unit.End > end || !ReferenceEquals(unit.Run, run))
                continue;
            DrawStyledFragmentRaw(context, run, cursor, unit.Start - cursor,
                ref x, lineY, line);
            for (var piece = 0; piece < unit.Pieces.Length; piece++)
            {
                x += unit.GapsBefore[piece];
                DrawMeasuredFragment(context, run, unit.Pieces[piece], ref x, lineY, line);
            }
            cursor = unit.End;
        }
        DrawStyledFragmentRaw(context, run, cursor, end - cursor, ref x, lineY, line);
    }

    private void DrawStyledFragmentRaw(DrawingContext context, Run? run,
        int start, int length, ref double x, double lineY, in TextLayoutLine line)
    {
        if (length <= 0) return;
        if (ContainsPreservedTab(start, length))
        {
            DrawTabbedInlineFragment(context, run, start, length, ref x, lineY, line);
            return;
        }
        var measurement = run is null
            ? MeasureText(PaintSlice(start, length))
            : MeasureInlineFragment(run, start, length);
        DrawMeasuredFragment(context, run, measurement, ref x, lineY, line);
    }

    private void DrawMeasuredFragment(DrawingContext context, Run? run,
        TextMeasurementCacheEntry measurement, ref double x, double lineY,
        in TextLayoutLine line)
    {
        var formatted = measurement.FormattedText;
        if (formatted is null) return;

        var size = run is null ? FontSize : GetRunFontSize(run);
        var metrics = TextMeasurement.GetFontMetrics(
            run is null ? FontFamily.GetRenderingSource(this) : GetRunFontFamily(run),
            size,
            run?.FontWeight.ToOpenTypeWeight() ?? FontWeight.ToOpenTypeWeight(),
            run?.FontStyle.ToOpenTypeStyle() ?? FontStyle.ToOpenTypeStyle());
        var ascent = metrics.Ascent > 0 ? metrics.Ascent : size * 0.8;
        formatted.Foreground = run?.Foreground ?? Foreground;
        formatted.MaxTextWidth = double.MaxValue;
        formatted.MaxTextHeight = Math.Max(line.Height, size * 2);
        formatted.Trimming = TextTrimming.None;
        formatted.ApplyTextOptionsFrom(this);
        context.DrawText(formatted, new Point(x, lineY + line.Baseline - ascent));
        x += measurement.WidthIncludingTrailingWhitespace;
    }

    private readonly record struct InlineLineMetrics(double Height, double Baseline, double GlyphHeight);
}
