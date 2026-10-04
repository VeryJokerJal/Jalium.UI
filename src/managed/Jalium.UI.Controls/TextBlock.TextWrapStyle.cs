using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private void RebalanceLayoutLines(int lineStart, int lineLength, bool hasLineBreakAfter,
        double constraintWidth, bool firstLogicalLine, CssTextIndent indent, bool intrinsic,
        int groupStart)
    {
        var style = (CssTextWrapStyle)GetValue(CssFlowProperties.TextWrapStyleProperty)!;
        if (style is CssTextWrapStyle.Auto or CssTextWrapStyle.Stable || intrinsic ||
            lineLength <= 0 || !double.IsFinite(constraintWidth) || constraintWidth <= 1 ||
            EffectiveTextWrapping() == TextWrapping.NoWrap)
            return;

        var lineCount = _layoutLines.Count - groupStart;
        if (lineCount <= 1 || style == CssTextWrapStyle.Balance && lineCount > 10 ||
            lineCount > 24 || lineLength > 4096)
            return;

        var greedy = _layoutLines.GetRange(groupStart, lineCount);
        var greedyOverflow = MaxWrapOverflow(greedy, constraintWidth);
        var best = greedy;
        var bestScore = WrapStyleScore(greedy, constraintWidth, style);
        if (style == CssTextWrapStyle.AvoidShortLastLine &&
            OccupiedWidth(greedy[^1], constraintWidth) >= constraintWidth * 0.4)
            return;

        List<TextLayoutLine> Probe(double width)
        {
            var start = _layoutLines.Count;
            AppendLayoutLinesCore(lineStart, lineLength, hasLineBreakAfter,
                width, constraintWidth, firstLogicalLine, indent, intrinsic);
            var result = _layoutLines.GetRange(start, _layoutLines.Count - start);
            _layoutLines.RemoveRange(start, result.Count);
            return result;
        }

        // First find the narrowest width that retains the greedy line count.
        // Keep the actual container width as the basis for percentage indent.
        var low = 1.0;
        var high = constraintWidth;
        for (var attempt = 0; attempt < 12 && high - low > 0.25; attempt++)
        {
            var middle = (low + high) / 2;
            if (Probe(middle).Count <= lineCount) high = middle;
            else low = middle;
        }
        if (high >= constraintWidth - 0.25) return;

        // Different break thresholds within that interval can yield different
        // raggedness even with the same line count. Score their actual line
        // widths rather than the narrower probe width used to select breaks.
        for (var sample = 0; sample < 9; sample++)
        {
            var width = high + (constraintWidth - high) * sample / 9;
            var candidate = Probe(width);
            if (candidate.Count != lineCount ||
                MaxWrapOverflow(candidate, constraintWidth) > greedyOverflow + 0.5)
                continue;
            if (style == CssTextWrapStyle.AvoidShortLastLine)
            {
                if (OccupiedWidth(candidate[^1], constraintWidth) <=
                    OccupiedWidth(greedy[^1], constraintWidth) + 0.5 ||
                    candidate.Take(candidate.Count - 1).Any(line =>
                        OccupiedWidth(line, constraintWidth) < constraintWidth * 0.3))
                    continue;
            }
            var score = WrapStyleScore(candidate, constraintWidth, style);
            if (score >= bestScore - 0.001) continue;
            best = candidate;
            bestScore = score;
        }

        if (ReferenceEquals(best, greedy)) return;
        _layoutLines.RemoveRange(groupStart, lineCount);
        _layoutLines.AddRange(best);
    }

    private double OccupiedWidth(in TextLayoutLine line, double containerWidth)
        => line.Width + ResolveTextIndent(line.IndentApplies, containerWidth) +
            line.StartPadding + line.EndPadding;

    private double MaxWrapOverflow(List<TextLayoutLine> lines, double containerWidth)
    {
        var overflow = 0.0;
        foreach (var line in lines)
            overflow = Math.Max(overflow, OccupiedWidth(line, containerWidth) - containerWidth);
        return overflow;
    }

    private double WrapStyleScore(List<TextLayoutLine> lines, double containerWidth,
        CssTextWrapStyle style)
    {
        var average = lines.Average(line => OccupiedWidth(line, containerWidth));
        var variance = lines.Sum(line =>
        {
            var difference = OccupiedWidth(line, containerWidth) - average;
            return difference * difference;
        });
        if (style == CssTextWrapStyle.Balance) return variance;

        var shortfall = Math.Max(0, containerWidth * 0.4 -
            OccupiedWidth(lines[^1], containerWidth));
        var orphanPenalty = shortfall * shortfall;
        return style == CssTextWrapStyle.Pretty
            ? variance + orphanPenalty * 2
            : variance * 0.15 + orphanPenalty * 4;
    }
}
