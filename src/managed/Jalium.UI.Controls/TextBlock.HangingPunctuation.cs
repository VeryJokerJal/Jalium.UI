using System.Globalization;
using System.Text;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private bool _hasHangingPunctuation;

    private readonly record struct HangingPunctuationWidths(double Start, double End);

    private enum HangingEndKind { None, Always, Conditional }

    private void RebuildHangingPunctuation()
    {
        _hasHangingPunctuation =
            (CssHangingPunctuation)GetValue(CssFlowProperties.HangingPunctuationProperty)! !=
            CssHangingPunctuation.None || GetInlineTextRanges().Any(static range =>
                (CssHangingPunctuation)range.Run.GetValue(
                    CssFlowProperties.HangingPunctuationProperty)! !=
                CssHangingPunctuation.None);
    }

    private bool HasAuthoredHangingPunctuationOnInlines()
    {
        foreach (var range in GetInlineTextRanges())
            if (HasAuthoredFlowValue(range.Run, CssFlowProperties.HangingPunctuationProperty))
                return true;
        return false;
    }

    private CssHangingPunctuation HangingPunctuationAt(int sourceIndex)
    {
        var run = InlineRunAt(GetInlineTextRanges(), sourceIndex);
        return (CssHangingPunctuation)(run ?? (DependencyObject)this).GetValue(
            CssFlowProperties.HangingPunctuationProperty)!;
    }

    private (int Start, int End, Rune Rune) PaintedEdgeUnit(int start, int end, bool first)
    {
        if (end <= start) return default;
        var index = first ? start : end;
        while (first ? index < end : index > start)
        {
            var unitStart = first ? index : GraphemeClusters.PreviousBoundary(_visualText, index);
            var unitEnd = first ? GraphemeClusters.NextBoundary(_visualText, index) : index;
            if (unitEnd > end || unitStart < start) break;
            var paint = PaintSlice(unitStart, unitEnd - unitStart);
            if (paint.Length > 0 &&
                (first || _countTrailingWhitespace ||
                 !IsBreakableWhitespace(_visualText[unitStart])) &&
                Rune.TryGetRuneAt(paint, 0, out var rune))
                return (unitStart, unitEnd, rune);
            index = first ? unitEnd : unitStart;
        }
        return default;
    }

    private double HangingStartWidth(int start, int end)
    {
        var edge = PaintedEdgeUnit(start, end, first: true);
        if (edge.End <= edge.Start ||
            !StartsFirstFormattedLine(start, edge.Start, edge.End) ||
            (HangingPunctuationAt(edge.Start) & CssHangingPunctuation.First) == 0)
            return 0;
        var category = Rune.GetUnicodeCategory(edge.Rune);
        if (category is not (UnicodeCategory.OpenPunctuation or
            UnicodeCategory.InitialQuotePunctuation or
            UnicodeCategory.FinalQuotePunctuation) &&
            edge.Rune.Value is not ('\'' or '"' or 0x3000))
            return 0;
        return MeasureInlineRange(edge.Start, edge.End - edge.Start,
            includeTrailingWhitespace: true);
    }

    private (double Width, HangingEndKind Kind) HangingEndWidth(int start, int end,
        bool softHyphenVisible = false)
    {
        if (softHyphenVisible) return default;
        var edge = PaintedEdgeUnit(start, end, first: false);
        if (edge.End <= edge.Start) return default;
        var mode = HangingPunctuationAt(edge.Start);
        var category = Rune.GetUnicodeCategory(edge.Rune);
        if (EndsLastFormattedLine(end, edge.Start, edge.End) &&
            (mode & CssHangingPunctuation.Last) != 0 &&
            (category is UnicodeCategory.ClosePunctuation or
                UnicodeCategory.InitialQuotePunctuation or
                UnicodeCategory.FinalQuotePunctuation ||
             edge.Rune.Value is '\'' or '"'))
            return (MeasureInlineRange(edge.Start, edge.End - edge.Start,
                includeTrailingWhitespace: true), HangingEndKind.Always);

        if (!IsHangingStopOrComma(edge.Rune)) return default;
        var kind = (mode & CssHangingPunctuation.ForceEnd) != 0
            ? HangingEndKind.Always
            : (mode & CssHangingPunctuation.AllowEnd) != 0
                ? HangingEndKind.Conditional : HangingEndKind.None;
        return kind == HangingEndKind.None ? default
            : (MeasureInlineRange(edge.Start, edge.End - edge.Start,
                includeTrailingWhitespace: true), kind);
    }

    private bool StartsFirstFormattedLine(int lineStart, int edgeStart, int edgeEnd)
    {
        if (lineStart == 0) return true;
        foreach (var range in GetInlineTextRanges())
            if (range.Start >= lineStart && range.Start <= edgeStart &&
                edgeEnd <= range.End)
                return true;
        return false;
    }

    private bool EndsLastFormattedLine(int lineEnd, int edgeStart, int edgeEnd)
    {
        if (lineEnd == _visualText.Length) return true;
        foreach (var range in GetInlineTextRanges())
            if (range.Start <= edgeStart && edgeEnd <= range.End &&
                range.End <= lineEnd)
                return true;
        return false;
    }

    private static bool IsHangingStopOrComma(Rune rune) => rune.Value is
        ',' or '.' or 0x060C or 0x06D4 or 0x3001 or 0x3002 or 0xFF0C or
        0xFF0E or 0xFE50 or 0xFE51 or 0xFE52 or 0xFF61 or 0xFF64;

    private double HangingFitWidth(int start, int end, double naturalWidth)
    {
        if (!_hasHangingPunctuation) return naturalWidth;
        var leading = HangingStartWidth(start, end);
        var trailing = HangingEndWidth(start, end).Width;
        if (leading > 0 && PaintedEdgeUnit(start, end, first: true).Start ==
            PaintedEdgeUnit(start, end, first: false).Start)
            trailing = 0;
        return Math.Max(0, naturalWidth - leading - trailing);
    }

    private HangingPunctuationWidths UsedHangingPunctuation(int start, int end,
        double naturalWidth, double availableWidth, bool softHyphenVisible)
    {
        if (!_hasHangingPunctuation || end <= start) return default;
        var leading = HangingStartWidth(start, end);
        var (endWidth, kind) = HangingEndWidth(start, end, softHyphenVisible);
        if (leading > 0 && PaintedEdgeUnit(start, end, first: true).Start ==
            PaintedEdgeUnit(start, end, first: false).Start)
            endWidth = 0;
        if (kind == HangingEndKind.Conditional && double.IsFinite(availableWidth))
            endWidth = Math.Clamp(naturalWidth - leading - availableWidth, 0, endWidth);
        else if (kind == HangingEndKind.Conditional)
            endWidth = 0;
        return new HangingPunctuationWidths(leading, endWidth);
    }
}
