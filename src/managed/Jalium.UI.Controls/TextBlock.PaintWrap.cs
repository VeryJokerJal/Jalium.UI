using Jalium.UI.Documents;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using System.Text;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    // A transformed source grapheme can contain several painted graphemes.
    // These lines retain source indices for interaction and a separate painted
    // range for the part that actually appears on each visual row.
    private sealed record PaintWrapLine(PaintWrapUnit[] Units, int First, int End)
    {
        internal int PaintStart => Units[First].PaintStart;
        internal int PaintEnd => Units[End - 1].PaintEnd;
    }

    private sealed record PaintWrapUnit(int PaintStart, int PaintEnd,
        int SourceStart, int SourceEnd, Run? Run,
        TextMeasurementCacheEntry Measurement, bool Invisible, bool BreakableSpace,
        double WordSpacing);

    private readonly record struct PaintWrapJustification(
        int[] Boundaries, double ExtraPerGap, double FilledWidth);

    private readonly Dictionary<(PaintWrapLine Line, double Width,
        CssTextJustification Method), PaintWrapJustification> _paintWrapJustificationCache = new();

    private bool WantsPaintExpansionJustification()
    {
        if ((CssTextJustification)GetValue(CssFlowProperties.TextJustifyProperty)! ==
            CssTextJustification.None)
            return false;
        if (CssFlowProperties.JustifiesLastLine(this, TextAlignmentProperty))
            return true;
        return CssFlowProperties.NativeLineTextAlignment(this, TextAlignmentProperty,
                   isLast: false) == TextAlignment.Justify &&
               EffectiveTextWrapping() != TextWrapping.NoWrap;
    }

    private bool TryGetPaintWrapJustification(in TextLayoutLine line,
        double contentWidth, out PaintWrapJustification justified)
    {
        justified = default;
        var groupPadding = GetGroupAlignmentPadding(contentWidth);
        contentWidth -= ResolveTextIndent(line.IndentApplies, contentWidth) +
            groupPadding + line.StartPadding + line.EndPadding;
        if (CssFlowProperties.NativeLineTextAlignment(this, TextAlignmentProperty,
                isLast: !line.CanJustify) != TextAlignment.Justify ||
            (!line.CanJustify && !CssFlowProperties.JustifiesLastLine(this, TextAlignmentProperty)) ||
            !double.IsFinite(contentWidth) || contentWidth <= 0 || line.Length == 0)
            return false;

        var method = (CssTextJustification)GetValue(CssFlowProperties.TextJustifyProperty)!;
        if (method == CssTextJustification.None) return false;
        var paintWrap = line.PaintWrap!;
        var key = (paintWrap, contentWidth, method);
        if (_paintWrapJustificationCache.TryGetValue(key, out justified))
            return justified.Boundaries is { Length: > 0 };

        var units = paintWrap.Units;
        var visibleEnd = paintWrap.End;
        while (visibleEnd > paintWrap.First && units[visibleEnd - 1].BreakableSpace)
            visibleEnd--;
        var visibleSourceEnd = visibleEnd > paintWrap.First
            ? units[visibleEnd - 1].SourceEnd : units[paintWrap.First].SourceStart;
        var boundaries = new List<int>();
        if (method is CssTextJustification.Auto or CssTextJustification.InterWord)
        {
            for (var index = paintWrap.First; index < visibleEnd;)
            {
                if (!units[index].BreakableSpace)
                {
                    index++;
                    continue;
                }
                var start = index;
                while (index < visibleEnd && units[index].BreakableSpace) index++;
                if (start > paintWrap.First && index < visibleEnd)
                    boundaries.Add(index);
            }
        }
        if (method is CssTextJustification.Auto or CssTextJustification.InterCharacter)
        {
            for (var boundary = paintWrap.First + 1; boundary < visibleEnd; boundary++)
            {
                if (!Rune.TryGetRuneAt(_paintText, units[boundary - 1].PaintStart,
                        out var before) ||
                    !Rune.TryGetRuneAt(_paintText, units[boundary].PaintStart,
                        out var after) ||
                    IsProhibitedLineEnd(before) || IsProhibitedLineStart(after))
                    continue;
                if (method == CssTextJustification.Auto
                    ? IsCjkLineBreakRune(before) && IsCjkLineBreakRune(after)
                    : !IsCursiveJoiningPair(before, after))
                    boundaries.Add(boundary);
            }
        }

        boundaries.Sort();
        var naturalWidth = 0.0;
        if (_hasLetterSpacing || _hasWordSpacing)
            naturalWidth = MeasurePaintWrapRange(units, paintWrap.First, visibleEnd);
        else
        {
            var first = paintWrap.First;
            foreach (var boundary in boundaries)
            {
                naturalWidth += MeasurePaintWrapRange(units, first, boundary,
                    includeTrailingWhitespace: true,
                    lineSourceStart: units[paintWrap.First].SourceStart,
                    lineSourceEnd: visibleSourceEnd);
                first = boundary;
            }
            naturalWidth += MeasurePaintWrapRange(units, first, visibleEnd,
                includeTrailingWhitespace: true,
                lineSourceStart: units[paintWrap.First].SourceStart,
                lineSourceEnd: visibleSourceEnd);
        }
        var extra = contentWidth - naturalWidth;
        extra += line.HangingStart + line.HangingEnd;
        if (boundaries.Count > 0 && extra > 0.001)
            justified = new PaintWrapJustification(
                boundaries.ToArray(), extra / boundaries.Count, contentWidth);
        if (_paintWrapJustificationCache.Count >= MaxTextWidthCacheEntries)
            _paintWrapJustificationCache.Clear();
        _paintWrapJustificationCache[key] = justified;
        return justified.Boundaries is { Length: > 0 };
    }

    private bool TryAppendPaintWrapLines(int lineStart, int lineLength,
        bool hasLineBreakAfter, double constraintWidth, double indentPercentBasis, bool firstLogicalLine,
        CssTextIndent indent, bool intrinsic)
    {
        if (_paintOffsets is null || ContainsPreservedTab(lineStart, lineLength) ||
            _visualText.AsSpan(lineStart, lineLength).Contains('\u00AD')) return false;

        var lineEnd = lineStart + lineLength;
        var sourceBoundaries = GraphemeClusters.GetBoundaries(_visualText);
        var paintBoundaries = GraphemeClusters.GetBoundaries(_paintText);
        var firstSource = Array.BinarySearch(sourceBoundaries, lineStart);
        var lastSource = Array.BinarySearch(sourceBoundaries, lineEnd);
        var firstPaint = Array.BinarySearch(paintBoundaries, _paintOffsets[lineStart]);
        var lastPaint = Array.BinarySearch(paintBoundaries, _paintOffsets[lineEnd]);
        if (firstSource < 0 || lastSource < 0 || firstPaint < 0 || lastPaint < 0)
            return false;

        // Leave ordinary one-to-one text on the existing shaping and wrapping
        // path. An internal painted boundary is the reason this path exists.
        var hasInternalBoundary = false;
        for (var source = firstSource; source < lastSource; source++)
        {
            var paintStart = _paintOffsets[sourceBoundaries[source]];
            var paintEnd = _paintOffsets[sourceBoundaries[source + 1]];
            var boundary = Array.BinarySearch(paintBoundaries, paintStart);
            if (boundary < 0) boundary = ~boundary - 1;
            if (boundary + 1 < paintBoundaries.Length &&
                paintBoundaries[boundary + 1] < paintEnd)
            {
                hasInternalBoundary = true;
                break;
            }
        }
        if (!hasInternalBoundary) return false;

        var units = new PaintWrapUnit[lastPaint - firstPaint];
        var ranges = GetInlineTextRanges();
        var rangeIndex = 0;
        var sourceIndex = firstSource;
        for (var paintIndex = firstPaint; paintIndex < lastPaint; paintIndex++)
        {
            var paintStart = paintBoundaries[paintIndex];
            var paintEnd = paintBoundaries[paintIndex + 1];
            while (sourceIndex + 1 < lastSource &&
                   _paintOffsets[sourceBoundaries[sourceIndex + 1]] <= paintStart)
                sourceIndex++;
            var sourceStart = sourceBoundaries[sourceIndex];
            var endSource = sourceIndex + 1;
            while (endSource < lastSource &&
                   _paintOffsets[sourceBoundaries[endSource]] < paintEnd)
                endSource++;
            var sourceEnd = sourceBoundaries[endSource];
            while (rangeIndex < ranges.Count && ranges[rangeIndex].End <= sourceStart)
                rangeIndex++;
            var run = rangeIndex < ranges.Count && ranges[rangeIndex].Start <= sourceStart
                ? ranges[rangeIndex].Run : null;
            var text = _paintText.Substring(paintStart, paintEnd - paintStart);
            var measurement = _hasLetterSpacing || _hasWordSpacing
                ? (run is null ? MeasureText(text) : MeasureRunText(run, text))
                : default;
            units[paintIndex - firstPaint] = new PaintWrapUnit(
                paintStart, paintEnd, sourceStart, sourceEnd, run, measurement,
                IsInvisiblePaintUnit(text) || _autospaceReplacementWidths.ContainsKey(sourceStart),
                text.Length == 1 && IsBreakableWhitespace(text[0]),
                WordSpacingInRange(sourceStart, sourceEnd));
        }

        var firstIndentApplies = indent.Applies(firstLogicalLine, !firstLogicalLine);
        var canBreakInsidePaint = _layoutLineBreak == CssLineBreak.Anywhere ||
            _layoutWordBreak == CssWordBreak.BreakAll || AllowsEmergencyWrap(intrinsic);
        for (var first = 0; first < units.Length;)
        {
            var indentApplies = indent.Applies(firstLogicalLine && first == 0,
                !firstLogicalLine && first == 0);
            var availableWidth = constraintWidth - ResolveTextIndent(indentApplies, indentPercentBasis);
            int end;
            if (double.IsInfinity(constraintWidth))
                end = units.Length;
            else if (!canBreakInsidePaint)
            {
                // Justification still needs painted boundaries when wrapping
                // itself follows the established source-space break policy.
                var wrapSourceStart = units[first].SourceStart;
                var sourceLength = FindWrapLength(wrapSourceStart,
                    lineEnd - wrapSourceStart, availableWidth, intrinsic);
                var wrapSourceEnd = wrapSourceStart + Math.Max(1, sourceLength);
                end = first + 1;
                while (end < units.Length && units[end].SourceStart < wrapSourceEnd)
                    end++;
            }
            else
            {
                var best = first;
                var candidateSourceStart = units[first].SourceStart;
                var startPadding = GetLineStartPadding(candidateSourceStart, lineEnd);
                var variableEndPadding = HasLinePaddingChange(candidateSourceStart, lineEnd);
                for (var groupStart = first + 1; groupStart <= units.Length;)
                {
                    var endPadding = GetLineEndPadding(candidateSourceStart,
                        units[groupStart - 1].SourceEnd);
                    var groupEnd = groupStart;
                    if (variableEndPadding)
                    {
                        while (groupEnd < units.Length &&
                               GetLineEndPadding(candidateSourceStart,
                                   units[groupEnd].SourceEnd) == endPadding)
                            groupEnd++;
                    }
                    else groupEnd = units.Length;

                    var low = groupStart;
                    var high = groupEnd;
                    while (low <= high)
                    {
                        var middle = low + (high - low) / 2;
                        var measured = MeasurePaintWrapRange(units, first, middle);
                        if (HangingFitWidth(units[first].SourceStart,
                                units[middle - 1].SourceEnd, measured) +
                            startPadding + endPadding <= availableWidth)
                        {
                            best = Math.Max(best, middle);
                            low = middle + 1;
                        }
                        else high = middle - 1;
                    }
                    groupStart = groupEnd + 1;
                }
                end = ChoosePaintWrapEnd(units, first, best, lineEnd, intrinsic);
            }
            var last = end == units.Length;
            var sourceStart = units[first].SourceStart;
            var sourceEnd = units[end - 1].SourceEnd;
            var paintWrap = new PaintWrapLine(units, first, end);
            _layoutLines.Add(CreateTextLayoutLine(sourceStart, sourceEnd - sourceStart,
                MeasurePaintWrapRange(units, first, end),
                last && hasLineBreakAfter,
                canJustify: !last,
                indentApplies: first == 0 ? firstIndentApplies : indentApplies,
                paintWrap: paintWrap,
                availableWidth: availableWidth));
            first = end;
        }
        return true;
    }

    private int ChoosePaintWrapEnd(PaintWrapUnit[] units, int first, int fittingEnd,
        int paragraphEnd, bool intrinsic)
    {
        if (fittingEnd == units.Length) return fittingEnd;
        if (_layoutLineBreak == CssLineBreak.Anywhere)
            return Math.Max(first + 1, fittingEnd);

        for (var end = fittingEnd; end > first; end--)
            if (IsPaintWrapStandardBreak(units, first, end, paragraphEnd))
                return end;

        if (AllowsEmergencyWrap(intrinsic))
            return Math.Max(first + 1, fittingEnd);

        // A whole unbreakable word may overflow, matching the source-space
        // wrapping path. break-all can still find its next letter boundary.
        for (var end = fittingEnd + 1; end < units.Length; end++)
            if (IsPaintWrapStandardBreak(units, first, end, paragraphEnd))
                return end;
        return units.Length;
    }

    private bool IsPaintWrapStandardBreak(PaintWrapUnit[] units, int first,
        int end, int paragraphEnd)
    {
        if (end <= first || end >= units.Length) return false;
        var before = units[end - 1];
        var after = units[end];
        if (before.BreakableSpace) return true;
        if (before.SourceEnd == after.SourceStart)
            return IsStandardLineBreakOpportunity(before.SourceEnd,
                units[first].SourceStart, paragraphEnd);
        if (_layoutWordBreak == CssWordBreak.BreakAll &&
            Rune.TryGetRuneAt(_paintText, before.PaintStart, out var beforeRune) &&
            Rune.TryGetRuneAt(_paintText, after.PaintStart, out var afterRune) &&
            Rune.IsLetterOrDigit(beforeRune) && Rune.IsLetterOrDigit(afterRune))
            return true;
        return false;
    }

    private double MeasurePaintWrapRange(PaintWrapUnit[] units, int first, int end,
        bool includeTrailingWhitespace = false,
        int? lineSourceStart = null, int? lineSourceEnd = null)
    {
        if (!includeTrailingWhitespace && !_countTrailingWhitespace)
            while (end > first && units[end - 1].BreakableSpace) end--;
        if (end <= first) return 0;
        var sourceStart = lineSourceStart ?? units[first].SourceStart;
        var sourceEnd = lineSourceEnd ?? units[end - 1].SourceEnd;

        if (!_hasLetterSpacing && !_hasWordSpacing)
        {
            var width = 0.0;
            for (var index = first; index < end;)
            {
                var chunkEnd = index + 1;
                while (chunkEnd < end && ReferenceEquals(units[chunkEnd].Run, units[index].Run))
                    chunkEnd++;
                var text = _paintText.Substring(units[index].PaintStart,
                    units[chunkEnd - 1].PaintEnd - units[index].PaintStart);
                var measured = units[index].Run is { } run
                    ? MeasureRunText(run, text) : MeasureText(text);
                width += measured.WidthIncludingTrailingWhitespace;
                index = chunkEnd;
            }
            return width;
        }

        var result = 0.0;
        PaintWrapUnit? previousVisible = null;
        for (var index = first; index < end; index++)
        {
            var unit = units[index];
            if (IsEdgeAutospaceReplacement(unit.SourceStart, sourceStart, sourceEnd))
                continue;
            if (!unit.Invisible)
            {
                if (previousVisible is not null)
                    result += PaintWrapLetterGap(previousVisible, unit);
                previousVisible = unit;
            }
            result += unit.Measurement.WidthIncludingTrailingWhitespace + unit.WordSpacing;
        }
        return Math.Max(0, result);
    }

    private double PaintWrapLetterGap(PaintWrapUnit previous, PaintWrapUnit current)
    {
        var preceding = ComputedLetterSpacingAt(previous.SourceStart);
        var following = ComputedLetterSpacingAt(current.SourceStart);
        var requested = (preceding + following) / 2;
        if (previous.SourceEnd == current.SourceStart)
            requested += AutospaceAtBoundary(previous.SourceStart, current.SourceStart,
                previous.Run, current.Run).Width;
        if (!double.IsFinite(requested) || Math.Abs(requested) <= 0.001) return 0;
        return Math.Max(-Math.Min(previous.Measurement.WidthIncludingTrailingWhitespace,
            current.Measurement.WidthIncludingTrailingWhitespace), requested);
    }

    private void DrawPaintWrapLine(DrawingContext context, in TextLayoutLine line,
        double originX, double lineY)
    {
        var paintWrap = line.PaintWrap!;
        var units = paintWrap.Units;
        var x = originX;
        var hasJustification = TryGetPaintWrapJustification(line,
            GetContentWidth(RenderSize.Width), out var justified);
        var gaps = hasJustification ? justified.Boundaries : Array.Empty<int>();
        if (!_hasLetterSpacing && !_hasWordSpacing)
        {
            var first = paintWrap.First;
            foreach (var gap in gaps)
            {
                DrawPaintWrapChunks(context, line, units, first, gap, ref x, lineY);
                x += justified.ExtraPerGap;
                first = gap;
            }
            DrawPaintWrapChunks(context, line, units, first, paintWrap.End, ref x, lineY);
            return;
        }

        PaintWrapUnit? previousVisible = null;
        var gapIndex = 0;
        for (var index = paintWrap.First; index < paintWrap.End; index++)
        {
            var unit = units[index];
            if (IsEdgeAutospaceReplacement(unit.SourceStart,
                    line.StartIndex, line.StartIndex + line.Length))
                continue;
            if (!unit.Invisible)
            {
                if (previousVisible is not null)
                    x += PaintWrapLetterGap(previousVisible, unit);
                previousVisible = unit;
            }
            x += unit.WordSpacing / 2;
            DrawMeasuredFragment(context, unit.Run, unit.Measurement, ref x, lineY, line);
            x += unit.WordSpacing / 2;
            if (gapIndex < gaps.Length && gaps[gapIndex] == index + 1)
            {
                x += justified.ExtraPerGap;
                gapIndex++;
            }
        }
    }

    private void DrawPaintWrapChunks(DrawingContext context, in TextLayoutLine line,
        PaintWrapUnit[] units, int first, int end, ref double x, double lineY)
    {
        for (var index = first; index < end;)
        {
            var chunkEnd = index + 1;
            while (chunkEnd < end && ReferenceEquals(units[chunkEnd].Run, units[index].Run))
                chunkEnd++;
            var text = _paintText.Substring(units[index].PaintStart,
                units[chunkEnd - 1].PaintEnd - units[index].PaintStart);
            var measured = units[index].Run is { } run
                ? MeasureRunText(run, text) : MeasureText(text);
            DrawMeasuredFragment(context, units[index].Run, measured, ref x, lineY, line);
            index = chunkEnd;
        }
    }

    private double GetPaintWrapBoundaryX(in TextLayoutLine line, int sourceIndex)
    {
        var hasJustification = TryGetPaintWrapJustification(line,
            GetContentWidth(RenderSize.Width), out var justified);
        var fullWidth = (hasJustification ? justified.FilledWidth : line.Width) +
            line.HangingStart + line.HangingEnd;
        if (sourceIndex >= line.StartIndex + line.Length) return fullWidth;
        var paintWrap = line.PaintWrap!;
        var paintBoundary = Math.Clamp(_paintOffsets![sourceIndex],
            paintWrap.PaintStart, paintWrap.PaintEnd);
        var end = paintWrap.First;
        while (end < paintWrap.End && paintWrap.Units[end].PaintEnd <= paintBoundary)
            end++;
        var width = 0.0;
        if (hasJustification && !_hasLetterSpacing && !_hasWordSpacing)
        {
            var first = paintWrap.First;
            foreach (var gap in justified.Boundaries)
            {
                if (gap > end) break;
                width += MeasurePaintWrapRange(paintWrap.Units, first, gap,
                    includeTrailingWhitespace: true,
                    lineSourceStart: line.StartIndex,
                    lineSourceEnd: line.StartIndex + line.Length) +
                    justified.ExtraPerGap;
                first = gap;
            }
            width += MeasurePaintWrapRange(paintWrap.Units, first, end,
                includeTrailingWhitespace: true,
                lineSourceStart: line.StartIndex,
                lineSourceEnd: line.StartIndex + line.Length);
        }
        else
        {
            width = MeasurePaintWrapRange(paintWrap.Units, paintWrap.First, end,
                includeTrailingWhitespace: true,
                lineSourceStart: line.StartIndex,
                lineSourceEnd: line.StartIndex + line.Length);
            if (hasJustification)
                foreach (var gap in justified.Boundaries)
                    if (gap <= end) width += justified.ExtraPerGap;
        }
        return Math.Min(fullWidth, width);
    }
}
