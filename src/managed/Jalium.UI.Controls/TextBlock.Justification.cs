using Jalium.UI.Media;
using Jalium.UI.Styling;
using System.Text;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private readonly Dictionary<(int Start, int Length, double Width, CssTextJustification Method), JustifiedLine>
        _justifiedLineCache = new();

    private bool TryGetJustifiedLine(in TextLayoutLine line, double contentWidth,
        out JustifiedLine justified)
    {
        justified = default;
        if (line.PaintWrap is not null || ContainsPreservedTab(line.StartIndex, line.Length))
            return false;
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

        var key = (line.StartIndex, line.Length, contentWidth, method);
        if (_justifiedLineCache.TryGetValue(key, out justified))
            return justified.Gaps is { Length: > 0 };

        var visibleEnd = line.StartIndex + line.Length;
        while (visibleEnd > line.StartIndex &&
               IsBreakableWhitespace(_visualText[visibleEnd - 1])) visibleEnd--;
        var gaps = new List<JustificationGap>();
        if (method is CssTextJustification.Auto or CssTextJustification.InterWord)
        {
            var index = line.StartIndex;
            while (index < visibleEnd)
            {
                if (!IsBreakableWhitespace(_visualText[index]))
                {
                    index++;
                    continue;
                }
                var start = index;
                while (index < visibleEnd && IsBreakableWhitespace(_visualText[index])) index++;
                if (start > line.StartIndex && index < visibleEnd)
                    gaps.Add(new JustificationGap(start, index));
            }
        }

        if (method is CssTextJustification.Auto or CssTextJustification.InterCharacter)
        {
            var boundaries = GraphemeClusters.GetBoundaries(_visualText);
            foreach (var boundary in boundaries)
            {
                if (boundary <= line.StartIndex || boundary >= visibleEnd) continue;
                var previous = GraphemeClusters.PreviousBoundary(_visualText, boundary);
                if (Rune.TryGetRuneAt(_visualText, previous, out var before) &&
                    Rune.TryGetRuneAt(_visualText, boundary, out var after) &&
                    !IsProhibitedLineEnd(before) && !IsProhibitedLineStart(after) &&
                    (method == CssTextJustification.Auto
                        ? IsCjkLineBreakRune(before) && IsCjkLineBreakRune(after)
                        : !IsCursiveJoiningPair(before, after)))
                    gaps.Add(new JustificationGap(boundary, boundary));
            }
        }

        gaps.Sort(static (a, b) => a.Start.CompareTo(b.Start));

        var naturalWidth = 0.0;
        var cursor = line.StartIndex;
        foreach (var gap in gaps)
        {
            naturalWidth += MeasureJustifiedSegment(cursor, gap.Start,
                line.StartIndex, visibleEnd);
            naturalWidth += MeasureJustifiedSegment(gap.Start, gap.End,
                line.StartIndex, visibleEnd);
            cursor = gap.End;
        }
        naturalWidth += MeasureJustifiedSegment(cursor, visibleEnd,
            line.StartIndex, visibleEnd);
        if (line.SoftHyphenVisible)
            naturalWidth += MeasureSoftHyphenWidth(line.StartIndex + line.Length - 1);
        naturalWidth = Math.Max(0,
            naturalWidth - line.HangingStart - line.HangingEnd);

        var extra = contentWidth - naturalWidth;
        if (gaps.Count > 0 && extra > 0.001)
        {
            var extraPerGap = extra / gaps.Count;
            var before = new double[gaps.Count];
            var after = new double[gaps.Count];
            var x = 0.0;
            cursor = line.StartIndex;
            for (var i = 0; i < gaps.Count; i++)
            {
                var gap = gaps[i];
                x += MeasureJustifiedSegment(cursor, gap.Start,
                    line.StartIndex, visibleEnd);
                before[i] = x;
                x += MeasureJustifiedSegment(gap.Start, gap.End,
                    line.StartIndex, visibleEnd) + extraPerGap;
                after[i] = x;
                cursor = gap.End;
            }
            justified = new JustifiedLine(line.StartIndex, visibleEnd, contentWidth,
                extraPerGap, gaps.ToArray(), before, after);
        }
        if (_justifiedLineCache.Count >= MaxTextWidthCacheEntries)
            _justifiedLineCache.Clear();
        _justifiedLineCache[key] = justified;
        return justified.Gaps is { Length: > 0 };
    }

    private void DrawJustifiedLine(DrawingContext context, in TextLayoutLine line,
        double originX, double lineY, in JustifiedLine justified)
    {
        var x = originX;
        var cursor = line.StartIndex;
        foreach (var gap in justified.Gaps)
        {
            DrawStyledInlineRange(context, line, cursor, gap.Start, ref x, lineY);
            x += MeasureJustifiedSegment(gap.Start, gap.End,
                line.StartIndex, justified.VisibleEnd) +
                justified.ExtraPerGap;
            cursor = gap.End;
        }
        DrawStyledInlineRange(context, line, cursor, justified.VisibleEnd, ref x, lineY);
        if (line.SoftHyphenVisible)
            DrawVisibleSoftHyphen(context, line, ref x, lineY);
    }

    private double GetJustifiedBoundaryOffset(in JustifiedLine justified, int index)
    {
        if (index >= justified.VisibleEnd) return justified.FilledWidth;
        var lower = 0;
        var upper = justified.Gaps.Length;
        while (lower < upper)
        {
            var middle = lower + (upper - lower) / 2;
            if (justified.Gaps[middle].Start <= index) lower = middle + 1;
            else upper = middle;
        }
        var gapIndex = lower - 1;
        if (gapIndex < 0)
            return MeasureInlineRange(justified.LineStart, index - justified.LineStart,
                includeTrailingWhitespace: true, forceFragmented: true);

        var gap = justified.Gaps[gapIndex];
        if (index < gap.End)
            return justified.BeforeGap[gapIndex] +
                (index > gap.Start
                    ? LetterSpacingAtBoundary(gap.Start, justified.LineStart, justified.VisibleEnd)
                    : 0) +
                MeasureInlineRange(gap.Start, index - gap.Start,
                    includeTrailingWhitespace: true, forceFragmented: true) +
                justified.ExtraPerGap * (index - gap.Start) / (gap.End - gap.Start);

        return justified.AfterGap[gapIndex] +
            (index > gap.End
                ? LetterSpacingAtBoundary(gap.End, justified.LineStart, justified.VisibleEnd)
                : 0) +
            MeasureInlineRange(gap.End, index - gap.End,
                includeTrailingWhitespace: true, forceFragmented: true);
    }

    private double MeasureJustifiedSegment(int start, int end, int lineStart, int lineEnd)
        => start >= end ? 0 :
            MeasureInlineRange(start, end - start,
                includeTrailingWhitespace: true, forceFragmented: true) +
            LetterSpacingAtBoundary(start, lineStart, lineEnd);

    private static bool IsCursiveJoiningPair(Rune before, Rune after)
    {
        static bool Joins(Rune rune) => rune.Value is >= 0x0600 and <= 0x08FF or
            >= 0x1800 and <= 0x18AF or >= 0xA840 and <= 0xA87F or
            >= 0xFB50 and <= 0xFDFF or >= 0xFE70 and <= 0xFEFF or
            >= 0x1E900 and <= 0x1E95F;
        return Joins(before) && Joins(after);
    }

    private readonly record struct JustificationGap(int Start, int End);
    private readonly record struct JustifiedLine(int LineStart, int VisibleEnd,
        double FilledWidth, double ExtraPerGap, JustificationGap[] Gaps,
        double[] BeforeGap, double[] AfterGap);
}
