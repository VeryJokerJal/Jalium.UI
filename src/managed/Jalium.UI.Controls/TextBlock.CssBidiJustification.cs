using System.Text;
using Jalium.UI.Interop;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private bool WantsCssBidiJustification => _cssRubyJustify ||
        CssFlowProperties.NativeLineTextAlignment(this, TextAlignmentProperty, false) == TextAlignment.Justify ||
        CssFlowProperties.NativeLineTextAlignment(this, TextAlignmentProperty, true) == TextAlignment.Justify;

    private readonly record struct CssBidiJustificationCell(int Unit, double Left,
        bool Rtl, Rune Rune, DependencyObject Owner);

    private sealed class CssBidiJustification
    {
        internal readonly HashSet<int> Expanded = [];
        internal readonly Dictionary<int, double> LeftOverflow = [];
    }

    private bool TryCssBidiJustificationOverflow(in TextLayoutLine line, out double overflow)
    {
        overflow = 0;
        if (!WantsCssBidiJustification) return false;
        if (_cssBidiParagraph is null && _cssBidiMeasurement is not null) TryCssBidiLine(line, out _);
        return _cssBidiParagraph?.Justification?.LeftOverflow.TryGetValue(line.StartIndex, out overflow) == true;
    }

    // Expand the final projected rows, after their tab and spacing advances.
    // The measurement paragraph remains intrinsic; every final glyph retains
    // its paragraph shaping, source ownership and original font snapshot.
    private void ApplyCssBidiJustification(CssBidiParagraph owner,
        NativeMethods.ParagraphAdvanceUnit[] units, CssBidiJustification result)
    {
        if (_layoutIntrinsic) return;
        double contentWidth = GetContentWidth(TextContentSize.Width);
        if (!double.IsFinite(contentWidth) || contentWidth <= 0) return;
        var paragraph = owner.Paragraph!;
        var source = BuildCssBidiSpacing(owner.Projection, usedLines: true).Sources;
        var unitIndex = units.Select((unit, index) => (unit.TextPosition, index))
            .ToDictionary(item => item.TextPosition, item => item.index);
        var ranges = GetInlineTextRanges();
        int firstSource = 0; uint row = 0;
        foreach (var line in _layoutLines)
        {
            int end = line.StartIndex + line.Length;
            while (firstSource < source.Length && source[firstSource].End <= line.StartIndex) firstSource++;
            if (line.Length == 0) continue;
            int nativeStart = owner.Projection.NativePosition(line.StartIndex);
            while (row + 1 < paragraph.Lines.Length &&
                nativeStart >= paragraph.Lines[row + 1].Metrics.Line.TextPosition) row++;
            double physical = paragraph.Lines[row].Metrics.Line.Width;
            int visibleEnd = end;
            while (visibleEnd > line.StartIndex && IsBreakableWhitespace(_visualText[visibleEnd - 1])) visibleEnd--;
            bool hangingSpaces = visibleEnd < end && _whiteSpaceMode == CssWhiteSpaceCollapse.Preserve &&
                EffectiveTextWrapping() != TextWrapping.NoWrap;
            double visiblePhysical = hangingSpaces
                ? paragraph.Selection(row, nativeStart, owner.Projection.NativePosition(visibleEnd) - nativeStart)
                    .Sum(rect => (double)rect.Width)
                : physical;
            double hanging = 0;
            bool trailingRtl = false;
            for (int at = firstSource; at < source.Length && source[at].First < end; at++)
            {
                var cell = source[at];
                if (cell.End != end || !Rune.IsWhiteSpace(cell.Rune)) continue;
                double a = paragraph.Caret(row, cell.NativeFirst, false).X;
                double b = paragraph.Caret(row, cell.NativeFirst + cell.NativeLength, true).X;
                if (b < a)
                {
                    trailingRtl = true;
                    hanging = Math.Max(0, physical - line.Width - line.HangingStart - line.HangingEnd);
                }
            }
            result.LeftOverflow[line.StartIndex] = hanging;
            if (!_cssRubyJustify && (CssFlowProperties.NativeLineTextAlignment(this, TextAlignmentProperty, !line.CanJustify) != TextAlignment.Justify ||
                !line.CanJustify && !CssFlowProperties.JustifiesLastLine(this, TextAlignmentProperty))) continue;
            double width = contentWidth - ResolveTextIndent(line.IndentApplies, contentWidth) -
                GetGroupAlignmentPadding(contentWidth) - line.StartPadding - line.EndPadding - CssClampAdvance(line);
            // Hanging whitespace is outside the used width. Its retained
            // advance is kept, so selection/editing can still expose it.
            // A transformed paint-wrap row was measured by the compatibility
            // formatter. Its aggregate advance need not equal retained native
            // shaping (notably HarfBuzz); distribute only the native remainder.
            double used = line.PaintWrap is not null ? visiblePhysical : Math.Min(line.Width, visiblePhysical);
            double slack = width - used;
            if (!double.IsFinite(slack) || slack <= .001) continue;
            int lastTab = _hasPreservedTabs ? _visualText.LastIndexOf('\t', end - 1, line.Length) : -1;
            var cells = new List<CssBidiJustificationCell>();
            for (int at = firstSource; at < source.Length && source[at].First < visibleEnd; at++)
            {
                var cell = source[at];
                if (cell.End > visibleEnd || cell.First <= lastTab) continue;
                int index = unitIndex[(uint)cell.NativeFirst];
                double a = paragraph.Caret(row, cell.NativeFirst, false).X;
                double b = paragraph.Caret(row, cell.NativeFirst + cell.NativeLength, true).X;
                if (Math.Abs(a - b) <= .000001) continue;
                var run = InlineRunAt(ranges, cell.First);
                cells.Add(new(index, Math.Min(a, b), b < a,
                    cell.Rune, run ?? (DependencyObject)this));
            }
            var visual = cells.OrderBy(cell => cell.Left).ToArray();
            var opportunities = new List<(int Left, int Right, bool Separator)>();
            for (int at = 0; at < visual.Length; at++)
            {
                var cell = visual[at];
                var own = (CssTextJustification)cell.Owner.GetValue(CssFlowProperties.TextJustifyProperty)!;
                if (at > 0 && at + 1 < visual.Length && IsWordSeparator(cell.Rune) &&
                    own is CssTextJustification.Auto or CssTextJustification.InterWord)
                    opportunities.Add((at, at, true));
                if (at == 0) continue;
                var previous = visual[at - 1];
                if (IsProhibitedLineEnd(previous.Rune) || IsProhibitedLineStart(cell.Rune)) continue;
                var common = CommonAutospaceOwner(previous.Owner, cell.Owner, this);
                var method = (CssTextJustification)common.GetValue(CssFlowProperties.TextJustifyProperty)!;
                if (method == CssTextJustification.InterCharacter && !IsCursiveJoiningPair(previous.Rune, cell.Rune) ||
                    method == CssTextJustification.Auto && !Rune.IsWhiteSpace(previous.Rune) && !Rune.IsWhiteSpace(cell.Rune) &&
                    (IsCjkLineBreakRune(previous.Rune) || IsCjkLineBreakRune(cell.Rune)) && RubyAutoOpportunity(previous.Rune, cell.Rune, this))
                    opportunities.Add((at - 1, at, false));
            }
            if (opportunities.Count == 0) continue;
            double extra = slack / opportunities.Count;
            if (extra > 1000000) continue;
            foreach (var opportunity in opportunities)
            {
                var left = visual[opportunity.Left]; var right = visual[opportunity.Right];
                if (opportunity.Separator)
                {
                    units[left.Unit].Leading += (float)(extra / 2);
                    units[left.Unit].Trailing += (float)(extra / 2);
                }
                else
                {
                    if (left.Rtl) units[left.Unit].Leading += (float)(extra / 2);
                    else units[left.Unit].Trailing += (float)(extra / 2);
                    if (right.Rtl) units[right.Unit].Trailing += (float)(extra / 2);
                    else units[right.Unit].Leading += (float)(extra / 2);
                }
            }
            if (hangingSpaces && trailingRtl)
                result.LeftOverflow[line.StartIndex] = Math.Max(0, physical - visiblePhysical);
            result.Expanded.Add(line.StartIndex);
        }
    }
}
