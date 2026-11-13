using Jalium.UI.Media;
using Jalium.UI.Interop;
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
        if (HasCssColumnText && _cssColumnJustifiedLines?.TryGetValue(line.StartIndex, out justified) == true)
        {
            if (justified.Shaped is { IsDisposed: true } &&
                TryBuildShapedJustification(line, justified.VisibleEnd, justified.FilledWidth, justified.TabOrigin, out var refreshed))
                _cssColumnJustifiedLines[line.StartIndex] = justified = refreshed;
            return justified.Gaps is { Length: > 0 };
        }
        if (HasCssInlineFragments)
        {
            justified = InlineFragment(line)?.Justification ?? default;
            return justified.Gaps is { Length: > 0 };
        }
        if (line.PaintWrap is not null)
            return false;
        var groupPadding = GetGroupAlignmentPadding(contentWidth);
        var tabOrigin = ResolveTextIndent(line.IndentApplies, contentWidth) +
            CssFlowProperties.GroupStartPadding(this, groupPadding) + GetLineLeftPadding(line);
        contentWidth -= ResolveTextIndent(line.IndentApplies, contentWidth) +
            groupPadding + line.StartPadding + line.EndPadding + CssClampAdvance(line);
        if (!_cssRubyJustify && (CssFlowProperties.NativeLineTextAlignment(this, TextAlignmentProperty,
                isLast: !line.CanJustify) != TextAlignment.Justify ||
            (!line.CanJustify && !CssFlowProperties.JustifiesLastLine(this, TextAlignmentProperty))) ||
            !double.IsFinite(contentWidth) || contentWidth <= 0 || line.Length == 0)
            return false;

        var method = (CssTextJustification)GetValue(CssFlowProperties.TextJustifyProperty)!;
        var key = (line.StartIndex, line.Length, contentWidth, method);
        if (_justifiedLineCache.TryGetValue(key, out justified) && justified.Shaped is not { IsDisposed: true })
            return justified.Gaps is { Length: > 0 };

        var visibleEnd = line.StartIndex + line.Length;
        while (visibleEnd > line.StartIndex &&
               IsBreakableWhitespace(_visualText[visibleEnd - 1])) visibleEnd--;
        if (TryBuildShapedJustification(line, visibleEnd, contentWidth, tabOrigin, out justified))
        {
            if (_justifiedLineCache.Count >= MaxTextWidthCacheEntries)
                _justifiedLineCache.Clear();
            _justifiedLineCache[key] = justified;
            return justified.Gaps.Length > 0;
        }
        // Earlier fields keep the positions established by the tab ruler. Only
        // the suffix after the last preserved tab can consume the line's slack.
        var lastTab = line.Length > 0 && _hasPreservedTabs
            ? _visualText.LastIndexOf('\t', line.StartIndex + line.Length - 1, line.Length) : -1;
        var gaps = FindJustificationGaps(_visualText, Math.Max(line.StartIndex, lastTab + 1), visibleEnd, JustificationMethod);
        var startIndex = line.StartIndex;
        gaps.RemoveAll(gap => gap.Start != gap.End &&
            MeasureJustifiedSegment(gap.Start, gap.End, startIndex, visibleEnd) <= .000001);

        var naturalWidth = 0.0;
        var cursor = line.StartIndex;
        foreach (var gap in gaps)
        {
            naturalWidth += MeasureJustifiedSegment(cursor, gap.Start,
                line.StartIndex, visibleEnd, tabOrigin);
            naturalWidth += MeasureJustifiedSegment(gap.Start, gap.End,
                line.StartIndex, visibleEnd, tabOrigin);
            cursor = gap.End;
        }
        naturalWidth += MeasureJustifiedSegment(cursor, visibleEnd,
            line.StartIndex, visibleEnd, tabOrigin);
        if (line.HyphenChange is { HasChanges: true })
        {
            naturalWidth = MeasureLineWithHyphenChange(line.StartIndex, visibleEnd - line.StartIndex,
                line.HyphenChange, line.SoftHyphenVisible, tabOrigin, includeTrailingWhitespace: false);
        }
        else if (line.SoftHyphenVisible)
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
                    line.StartIndex, visibleEnd, tabOrigin);
                before[i] = x;
                x += MeasureJustifiedSegment(gap.Start, gap.End,
                    line.StartIndex, visibleEnd, tabOrigin) + extraPerGap;
                after[i] = x;
                cursor = gap.End;
            }
            justified = new JustifiedLine(line.StartIndex, visibleEnd, contentWidth,
                extraPerGap, gaps.ToArray(), before, after, TabOrigin: tabOrigin);
        }
        if (_justifiedLineCache.Count >= MaxTextWidthCacheEntries)
            _justifiedLineCache.Clear();
        _justifiedLineCache[key] = justified;
        return justified.Gaps is { Length: > 0 };
    }

    private void DrawJustifiedLine(DrawingContext context, in TextLayoutLine line,
        double originX, double lineY, in JustifiedLine justified)
    {
        if (justified.Shaped is { } shaped)
        { DrawContextSpacedLine(context, line, shaped, line.StartIndex, line.StartIndex + line.Length, originX, lineY); return; }
        var x = originX;
        var cursor = line.StartIndex;
        for (var index = 0; index < justified.Gaps.Length; index++)
        {
            var gap = justified.Gaps[index];
            DrawStyledInlineRangeWithHyphenChange(context, line, cursor, gap.Start, ref x, lineY);
            x += MeasureJustifiedSegment(gap.Start, gap.End,
                line.StartIndex, justified.VisibleEnd) +
                justified.Expansion(index);
            cursor = gap.End;
        }
        DrawStyledInlineRangeWithHyphenChange(context, line, cursor, justified.VisibleEnd, ref x, lineY);
        if (line.SoftHyphenVisible)
            DrawVisibleSoftHyphen(context, line, ref x, lineY);
    }

    private double GetJustifiedBoundaryOffset(in JustifiedLine justified, int index)
    {
        if (justified.Shaped is { } shaped)
            return shaped.Caret(0, Math.Clamp(index - justified.LineStart, 0, shaped.Text.Length), false).X - shaped.Lines[0].Metrics.Line.X;
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
                includeTrailingWhitespace: true, forceFragmented: true, tabOrigin: justified.TabOrigin);

        var gap = justified.Gaps[gapIndex];
        if (index < gap.End)
            return justified.BeforeGap[gapIndex] +
                (index > gap.Start
                    ? LetterSpacingAtBoundary(gap.Start, justified.LineStart, justified.VisibleEnd)
                    : 0) +
                MeasureInlineRange(gap.Start, index - gap.Start,
                    includeTrailingWhitespace: true, forceFragmented: true) +
                justified.Expansion(gapIndex) * (index - gap.Start) / (gap.End - gap.Start);

        return justified.AfterGap[gapIndex] +
            (index > gap.End
                ? LetterSpacingAtBoundary(gap.End, justified.LineStart, justified.VisibleEnd)
                : 0) +
            MeasureInlineRange(gap.End, index - gap.End,
                includeTrailingWhitespace: true, forceFragmented: true);
    }

    private double MeasureJustifiedSegment(int start, int end, int lineStart, int lineEnd, double tabOrigin = 0)
        => start >= end ? 0 :
            MeasureInlineRange(start, end - start,
                includeTrailingWhitespace: true, forceFragmented: true, tabOrigin: tabOrigin) +
            LetterSpacingAtBoundary(start, lineStart, lineEnd);

    private CssTextJustification JustificationMethod(int before, int after)
    {
        var ranges = GetInlineTextRanges();
        var method = (CssTextJustification)AutospaceOwner(InlineRunAt(ranges, before),
            InlineRunAt(ranges, after)).GetValue(CssFlowProperties.TextJustifyProperty)!;
        return method == CssTextJustification.Auto && System.Text.Rune.TryGetRuneAt(_visualText, before, out var a) &&
            System.Text.Rune.TryGetRuneAt(_visualText, after, out var b) && !RubyAutoOpportunity(a, b, this)
            ? CssTextJustification.None : method;
    }

    private static List<JustificationGap> FindJustificationGaps(string text, int start, int end,
        Func<int, int, CssTextJustification> methodAt)
    {
        var gaps = new List<JustificationGap>();
        var boundaries = GraphemeClusters.GetBoundaries(text);
        foreach (var boundary in boundaries)
        {
            if (boundary < start || boundary >= end || !Rune.TryGetRuneAt(text, boundary, out var after)) continue;
            var next = GraphemeClusters.NextBoundary(text, boundary);
            var own = methodAt(boundary, boundary);
            if (boundary > start && next < end && IsWordSeparator(after) &&
                own is CssTextJustification.Auto or CssTextJustification.InterWord)
                gaps.Add(new(boundary, next));
            if (boundary <= start) continue;
            var previous = GraphemeClusters.PreviousBoundary(text, boundary);
            if (!Rune.TryGetRuneAt(text, previous, out var before) ||
                IsProhibitedLineEnd(before) || IsProhibitedLineStart(after)) continue;
            var common = methodAt(previous, boundary);
            if (common == CssTextJustification.InterCharacter && !IsCursiveJoiningPair(before, after) ||
                common == CssTextJustification.Auto && !Rune.IsWhiteSpace(before) && !Rune.IsWhiteSpace(after) &&
                (IsCjkLineBreakRune(before) || IsCjkLineBreakRune(after)))
                gaps.Add(new(boundary, boundary));
        }
        gaps.Sort(static (a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));
        return gaps;
    }

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
        double[] BeforeGap, double[] AfterGap, double[]? Expansions = null, double TabOrigin = 0, NativeTextParagraph? Shaped = null)
    {
        internal double Expansion(int index) => Expansions is null ? ExtraPerGap : Expansions[index];
    }
}
