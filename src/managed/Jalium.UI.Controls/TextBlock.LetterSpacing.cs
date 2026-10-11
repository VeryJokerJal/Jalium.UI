using Jalium.UI.Media;
using Jalium.UI.Styling;
using System.Globalization;
using System.Text;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private readonly List<LetterSpacingGap> _letterSpacingGaps = [];
    private readonly List<InternalPaintSpacingUnit> _internalPaintSpacingUnits = [];
    private bool _hasLetterSpacing;

    private void RebuildLetterSpacing()
    {
        _letterSpacingGaps.Clear();
        _internalPaintSpacingUnits.Clear();
        _hasLetterSpacing = false;
        var blockSpacing = (double)GetValue(CssFlowProperties.LetterSpacingProperty)!;
        var blockAutospace = (CssTextAutospace)GetValue(CssFlowProperties.TextAutospaceProperty)!;
        var ranges = GetInlineTextRanges();
        if (!double.IsFinite(blockSpacing) || Math.Abs(blockSpacing) <= 0.001)
        {
            var hasInlineSpacing = false;
            foreach (var range in ranges)
            {
                var value = (double)range.Run.GetValue(CssFlowProperties.LetterSpacingProperty)!;
                if (!double.IsFinite(value) || Math.Abs(value) <= 0.001) continue;
                hasInlineSpacing = true;
                break;
            }
            if (!hasInlineSpacing)
            {
                var hasAutospace = blockAutospace != CssTextAutospace.None ||
                    ranges.Any(static range =>
                        (CssTextAutospace)range.Run.GetValue(
                            CssFlowProperties.TextAutospaceProperty)! != CssTextAutospace.None);
                if (!hasAutospace || !HasAutospaceCandidate()) return;
            }
        }

        var boundaries = GraphemeClusters.GetBoundaries(_visualText);
        var rangeIndex = 0;
        var previousStart = -1;
        var previousEnd = -1;
        var previousSpacing = 0.0;
        Jalium.UI.Documents.Run? previousRun = null;
        for (var unit = 0; unit + 1 < boundaries.Length; unit++)
        {
            var start = boundaries[unit];
            var end = boundaries[unit + 1];
            if (_visualText[start] is '\r' or '\n')
            {
                previousStart = -1;
                previousRun = null;
                continue;
            }
            if (IsInvisibleLetterSpacingUnit(start, end)) continue;

            while (rangeIndex < ranges.Count && ranges[rangeIndex].End <= start)
                rangeIndex++;
            var run = rangeIndex < ranges.Count && ranges[rangeIndex].Start <= start
                ? ranges[rangeIndex].Run : null;
            var crossesRunBoundary = rangeIndex < ranges.Count &&
                (run is null ? ranges[rangeIndex].Start < end : ranges[rangeIndex].End < end);
            var spacing = run is null ? blockSpacing
                : (double)run.GetValue(CssFlowProperties.LetterSpacingProperty)!;
            if (!double.IsFinite(spacing)) spacing = 0;
            if (Math.Abs(spacing) > 0.001 && _hasPaintTextChanges)
                AddInternalPaintSpacingUnit(start, end, run, spacing, crossesRunBoundary);

            if (previousStart >= 0)
            {
                var requested = (previousSpacing + spacing) / 2 +
                    (previousEnd == start
                        ? AutospaceAtBoundary(previousStart, start, previousRun, run).Width
                        : 0);
                if (Math.Abs(requested) > 0.001)
                {
                    // Preserve nonnegative advances for width fitting while
                    // allowing glyphs to overlap under negative tracking.
                    var natural = MeasureInternallySpacedRange(start, end);
                    var limit = natural;
                    if (requested < 0)
                        limit = Math.Min(limit,
                            MeasureInternallySpacedRange(previousStart, previousEnd));
                    var adjustment = Math.Max(-limit, requested);
                    if (Math.Abs(adjustment) > 0.001)
                        _letterSpacingGaps.Add(new LetterSpacingGap(
                            start, end, previousStart, adjustment));
                }
            }
            previousStart = start;
            previousEnd = end;
            previousSpacing = spacing;
            previousRun = run;
        }
        _hasLetterSpacing = _letterSpacingGaps.Count > 0 ||
            _internalPaintSpacingUnits.Count > 0;
    }

    private void AddInternalPaintSpacingUnit(int start, int end,
        Jalium.UI.Documents.Run? run, double spacing,
        bool crossesRunBoundary)
    {
        var paint = PaintSlice(start, end - start);
        var boundaries = GraphemeClusters.GetBoundaries(paint);
        if (boundaries.Length <= 2 || crossesRunBoundary)
            return;

        var pieces = new TextMeasurementCacheEntry[boundaries.Length - 1];
        var gapsBefore = new double[pieces.Length];
        var width = 0.0;
        var previousVisibleWidth = 0.0;
        var hasPreviousVisible = false;
        var hasGap = false;
        for (var piece = 0; piece < pieces.Length; piece++)
        {
            var text = paint.Substring(boundaries[piece],
                boundaries[piece + 1] - boundaries[piece]);
            var measurement = run is null ? MeasureText(text) : MeasureRunText(run, text);
            pieces[piece] = measurement;
            var visible = !IsInvisiblePaintUnit(text);
            if (visible && hasPreviousVisible)
            {
                var adjustment = Math.Max(
                    -Math.Min(previousVisibleWidth, measurement.WidthIncludingTrailingWhitespace),
                    spacing);
                gapsBefore[piece] = adjustment;
                width += adjustment;
                hasGap |= Math.Abs(adjustment) > 0.001;
            }
            if (visible)
            {
                previousVisibleWidth = measurement.WidthIncludingTrailingWhitespace;
                hasPreviousVisible = true;
            }
            width += measurement.WidthIncludingTrailingWhitespace;
        }
        if (hasGap)
            _internalPaintSpacingUnits.Add(new InternalPaintSpacingUnit(
                start, end, run, pieces, gapsBefore, Math.Max(0, width)));
    }

    private static bool IsInvisiblePaintUnit(string text)
    {
        foreach (var rune in text.EnumerateRunes())
            if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format)
                return false;
        return true;
    }

    private double MeasureInternallySpacedRange(int start, int end)
    {
        if (end <= start) return 0;
        var width = 0.0;
        var cursor = start;
        for (var index = FirstInternalPaintUnitAtOrAfter(start);
             index < _internalPaintSpacingUnits.Count &&
             _internalPaintSpacingUnits[index].Start < end;
             index++)
        {
            var unit = _internalPaintSpacingUnits[index];
            if (unit.End <= cursor) continue;
            if (unit.Start < cursor || unit.End > end) continue;
            width += MeasureInlineRangeBase(cursor, unit.Start - cursor,
                includeTrailingWhitespace: true, forceFragmented: true);
            width += unit.Width;
            cursor = unit.End;
        }
        return width + MeasureInlineRangeBase(cursor, end - cursor,
            includeTrailingWhitespace: true, forceFragmented: true);
    }

    private int FirstInternalPaintUnitAtOrAfter(int position)
    {
        var low = 0;
        var high = _internalPaintSpacingUnits.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_internalPaintSpacingUnits[middle].Start < position) low = middle + 1;
            else high = middle;
        }
        return low > 0 && _internalPaintSpacingUnits[low - 1].End > position
            ? low - 1 : low;
    }

    private bool IsInvisibleLetterSpacingUnit(int start, int end)
    {
        if (end == start + 1 && _autospaceReplacementWidths.ContainsKey(start))
            return true;
        foreach (var rune in _visualText.AsSpan(start, end - start).EnumerateRunes())
            if (Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format)
                return false;
        return true;
    }

    private bool HasAuthoredLetterSpacingOnInlines()
    {
        foreach (var range in GetInlineTextRanges())
            if (HasAuthoredFlowValue(range.Run, CssFlowProperties.LetterSpacingProperty))
                return true;
        return false;
    }

    private int FirstLetterSpacingGapAtOrAfter(int position)
    {
        var low = 0;
        var high = _letterSpacingGaps.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_letterSpacingGaps[middle].Start < position) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private double LetterSpacingAtBoundary(int position, int lineStart, int lineEnd)
    {
        if (!_hasLetterSpacing || position <= lineStart || position >= lineEnd)
            return 0;
        var index = FirstLetterSpacingGapAtOrAfter(position);
        if (index >= _letterSpacingGaps.Count) return 0;
        var gap = _letterSpacingGaps[index];
        return gap.Start == position && gap.PreviousStart >= lineStart && gap.End <= lineEnd
            ? gap.Adjustment : 0;
    }

    private double LetterSpacingBeforeEllipsis(int start, int length,
        Jalium.UI.Documents.Run? ellipsisRun, double ellipsisWidth)
    {
        if (!_hasLetterSpacing || length <= 0) return 0;
        var boundaries = GraphemeClusters.GetBoundaries(_visualText);
        var end = start + length;
        var index = Array.BinarySearch(boundaries, end);
        if (index < 0) index = ~index;
        for (index--; index >= 0 && boundaries[index] >= start; index--)
        {
            var unitStart = boundaries[index];
            var unitEnd = boundaries[index + 1];
            if (IsInvisibleLetterSpacingUnit(unitStart, unitEnd)) continue;
            var preceding = ComputedLetterSpacingAt(unitStart);
            var following = ellipsisRun is null
                ? (double)GetValue(CssFlowProperties.LetterSpacingProperty)!
                : (double)ellipsisRun.GetValue(CssFlowProperties.LetterSpacingProperty)!;
            var requested = (preceding + following) / 2;
            if (!double.IsFinite(requested) || Math.Abs(requested) <= 0.001) return 0;
            var limit = Math.Min(
                MeasureInternallySpacedRange(unitStart, unitEnd),
                ellipsisWidth);
            return Math.Max(-limit, requested);
        }
        return 0;
    }

    private double ComputedLetterSpacingAt(int position)
    {
        foreach (var range in GetInlineTextRanges())
            if (range.Start <= position && position < range.End)
                return (double)range.Run.GetValue(CssFlowProperties.LetterSpacingProperty)!;
        return (double)GetValue(CssFlowProperties.LetterSpacingProperty)!;
    }

    private double MeasureLetterSpacedRange(int start, int end)
    {
        var cursor = start;
        var width = 0.0;
        for (var index = FirstLetterSpacingGapAtOrAfter(start);
             index < _letterSpacingGaps.Count && _letterSpacingGaps[index].Start < end;
             index++)
        {
            var gap = _letterSpacingGaps[index];
            if (gap.PreviousStart < start || gap.End > end) continue;
            width += MeasureWordSpacedFragment(cursor, gap.Start);
            width += gap.Adjustment;
            cursor = gap.Start;
        }
        return Math.Max(0, width + MeasureWordSpacedFragment(cursor, end));
    }

    private double MeasureWordSpacedFragment(int start, int end)
    {
        if (end <= start) return 0;
        if (!_hasWordSpacing)
            return MeasureInternallySpacedRange(start, end);

        var width = 0.0;
        var cursor = start;
        for (var index = FirstWordSpacingGapAtOrAfter(start);
             index < _wordSpacingGaps.Count && _wordSpacingGaps[index].End <= end;
             index++)
        {
            var gap = _wordSpacingGaps[index];
            width += MeasureInternallySpacedRange(cursor, gap.Start);
            width += MeasureInternallySpacedRange(gap.Start, gap.End);
            width += gap.Adjustment;
            cursor = gap.End;
        }
        return width + MeasureInternallySpacedRange(cursor, end);
    }

    private void DrawLetterSpacedInlineRange(DrawingContext context,
        in TextLayoutLine line, int start, int end, ref double x, double lineY)
    {
        var cursor = start;
        var lineEnd = line.StartIndex + line.Length;
        for (var index = FirstLetterSpacingGapAtOrAfter(start);
             index < _letterSpacingGaps.Count && _letterSpacingGaps[index].Start < end;
             index++)
        {
            var gap = _letterSpacingGaps[index];
            if (gap.PreviousStart < line.StartIndex || gap.End > lineEnd) continue;
            DrawWordSpacedOrStyledRange(context, line, cursor, gap.Start, ref x, lineY);
            x += gap.Adjustment;
            cursor = gap.Start;
        }
        DrawWordSpacedOrStyledRange(context, line, cursor, end, ref x, lineY);
    }

    private void DrawWordSpacedOrStyledRange(DrawingContext context,
        in TextLayoutLine line, int start, int end, ref double x, double lineY)
    {
        if (_hasWordSpacing)
            DrawWordSpacedInlineRange(context, line, start, end, ref x, lineY);
        else
            DrawStyledInlineRangeCore(context, line, start, end, ref x, lineY);
    }

    private readonly record struct LetterSpacingGap(
        int Start, int End, int PreviousStart, double Adjustment);

    private sealed record InternalPaintSpacingUnit(
        int Start, int End, Jalium.UI.Documents.Run? Run,
        TextMeasurementCacheEntry[] Pieces, double[] GapsBefore, double Width);
}
