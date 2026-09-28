using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private bool TryGetTrimmedLine(in TextLayoutLine line, double contentWidth,
        out TrimmedLine trimmed)
    {
        trimmed = default;
        var containerWidth = contentWidth;
        contentWidth -= line.StartPadding + line.EndPadding;
        if (EffectiveTextWrapping() != TextWrapping.NoWrap || TextTrimming == TextTrimming.None ||
            line.Length == 0 || !double.IsFinite(contentWidth) || contentWidth <= 0 ||
            line.Width <= contentWidth + 0.001)
            return false;

        var fragmented = _usesInlineFontLayout || _hasWordSpacing || _hasLetterSpacing ||
            ContainsPreservedTab(line.StartIndex, line.Length) ||
            HasInlineForegroundOverrides(line);
        var tabOrigin = ResolveTextIndent(line.IndentApplies, containerWidth) +
            GetLineLeftPadding(line);
        var lineEnd = line.StartIndex + line.Length;
        var boundaries = GraphemeClusters.GetBoundaries(_visualText);
        var first = Array.BinarySearch(boundaries, line.StartIndex);
        var last = Array.BinarySearch(boundaries, lineEnd);
        if (first < 0) first = ~first;
        if (last < 0) last = ~last - 1;

        var bestLength = 0;
        var bestWidth = 0.0;
        var low = first + 1;
        var high = last;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var length = boundaries[middle] - line.StartIndex;
            var width = MeasureTrimmedPrefix(line.StartIndex, length, lineEnd, fragmented,
                tabOrigin);
            if (width <= contentWidth)
            {
                bestLength = length;
                var run = GetRunAtTrimBoundary(line.StartIndex, length, lineEnd);
                bestWidth = MeasureInlineRange(line.StartIndex, length,
                    includeTrailingWhitespace: true, forceFragmented: fragmented,
                    tabOrigin: tabOrigin) +
                    (_hasLetterSpacing
                        ? LetterSpacingBeforeEllipsis(line.StartIndex, length, run,
                            MeasureEllipsis(run).WidthIncludingTrailingWhitespace) : 0);
                low = middle + 1;
            }
            else
                high = middle - 1;
        }

        if (TextTrimming == TextTrimming.WordEllipsis && bestLength > 0)
        {
            var wordEnd = line.StartIndex + bestLength;
            while (wordEnd > line.StartIndex &&
                   !IsBreakableWhitespace(_visualText[wordEnd - 1]) &&
                   !IsStandardLineBreakOpportunity(wordEnd, line.StartIndex, lineEnd))
                wordEnd = GraphemeClusters.PreviousBoundary(_visualText, wordEnd);
            while (wordEnd > line.StartIndex &&
                   IsBreakableWhitespace(_visualText[wordEnd - 1])) wordEnd--;
            bestLength = wordEnd - line.StartIndex;
            var run = GetRunAtTrimBoundary(line.StartIndex, bestLength, lineEnd);
            bestWidth = MeasureInlineRange(line.StartIndex, bestLength,
                includeTrailingWhitespace: true, forceFragmented: fragmented,
                tabOrigin: tabOrigin) +
                (_hasLetterSpacing
                    ? LetterSpacingBeforeEllipsis(line.StartIndex, bestLength, run,
                        MeasureEllipsis(run).WidthIncludingTrailingWhitespace) : 0);
        }

        var ellipsisRun = GetRunAtTrimBoundary(line.StartIndex, bestLength, lineEnd);
        trimmed = new TrimmedLine(bestLength, bestWidth, ellipsisRun, fragmented);
        return true;
    }

    private bool HasInlineForegroundOverrides(in TextLayoutLine line)
    {
        if (_inlines is null) return false;
        var end = line.StartIndex + line.Length;
        foreach (var range in GetInlineTextRanges())
            if (range.End > line.StartIndex && range.Start < end &&
                !ReferenceEquals(range.Run.Foreground ?? Foreground, Foreground))
                return true;
        return false;
    }

    private double MeasureTrimmedPrefix(int start, int length, int lineEnd,
        bool fragmented, double tabOrigin)
    {
        if (!fragmented)
            return MeasureTextWidth(PaintSlice(start, length) + "…");
        var run = GetRunAtTrimBoundary(start, length, lineEnd);
        var ellipsisWidth = MeasureEllipsis(run).WidthIncludingTrailingWhitespace;
        return MeasureInlineRange(start, length,
            includeTrailingWhitespace: true, forceFragmented: true,
            tabOrigin: tabOrigin) +
            LetterSpacingBeforeEllipsis(start, length, run, ellipsisWidth) +
            ellipsisWidth;
    }

    private Run? GetRunAtTrimBoundary(int start, int prefixLength, int end)
    {
        var index = prefixLength > 0 ? start + prefixLength - 1 : start;
        if (index >= end) index = end - 1;
        foreach (var range in GetInlineTextRanges())
            if (range.Start <= index && index < range.End) return range.Run;
        return null;
    }

    private TextMeasurementCacheEntry MeasureEllipsis(Run? run)
    {
        if (run is null) return MeasureText("…");
        var size = GetRunFontSize(run);
        var formatted = new FormattedText("…", GetRunFontFamily(run), size)
        {
            FontWeight = run.FontWeight.ToOpenTypeWeight(),
            FontStyle = run.FontStyle.ToOpenTypeStyle(),
            FontStretch = run.FontStretch.ToOpenTypeStretch(),
        };
        var width = TextMeasurement.MeasureText(formatted) && formatted.IsMeasured
            ? formatted.WidthIncludingTrailingWhitespace
            : EstimateTextWidth("…", size);
        return new TextMeasurementCacheEntry(width, width, formatted);
    }

    private void DrawTrimmedLine(DrawingContext context, in TextLayoutLine line,
        double originX, double lineY, in TrimmedLine trimmed)
    {
        if (trimmed.Fragmented)
        {
            if (trimmed.PrefixLength > 0)
                DrawStyledInlineLine(context, line, originX, lineY,
                    line.StartIndex + trimmed.PrefixLength);

            var run = trimmed.EllipsisRun;
            var formatted = MeasureEllipsis(run).FormattedText!;
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
            formatted.ApplyTextOptionsFrom(this);
            context.DrawText(formatted, new Point(originX + trimmed.PrefixWidth,
                lineY + line.Baseline - ascent));
            return;
        }

        var text = PaintSlice(line.StartIndex, trimmed.PrefixLength) + "…";
        var formattedLine = MeasureText(text).FormattedText!;
        formattedLine.Foreground = Foreground;
        formattedLine.MaxTextWidth = double.MaxValue;
        formattedLine.MaxTextHeight = line.Height;
        formattedLine.ApplyTextOptionsFrom(this);
        context.DrawText(formattedLine, new Point(originX, lineY));
    }

    private readonly record struct TrimmedLine(int PrefixLength, double PrefixWidth,
        Run? EllipsisRun, bool Fragmented);
}
