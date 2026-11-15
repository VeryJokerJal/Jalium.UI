using System.Text;
using Jalium.UI.Interop;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    internal sealed partial class CssTextFlowLayout
    {
        private bool JustifyFragments(List<CssInlineFragment> fragments, double available)
        {
            if (_intrinsic || !double.IsFinite(available) || available <= 0) return false;
            if (TryJustifyInlineBidiFragments(fragments,available,out var bidiExpanded))return bidiExpanded;
            if (TryJustifyShapedFragments(fragments, available, out var shaped)) return shaped;
            var joined = new StringBuilder();
            var offsets = new List<(CssInlineFragment Fragment, int Start, int End)>();
            foreach (var fragment in fragments)
            {
                var start = joined.Length;
                if (fragment.Source.Atomic) joined.Append('\ufffc');
                else joined.Append(fragment.Source.Text._visualText, fragment.Line.StartIndex, fragment.Line.Length);
                offsets.Add((fragment, start, joined.Length));
            }
            var text = joined.ToString(); var visible = text.Length;
            while (visible > 0 && IsBreakableWhitespace(text[visible - 1])) visible--;
            if (visible == 0) return false;
            // Tabs in later sources also anchor all earlier sources. The joined
            // line determines the last stop, not each TextBlock independently.
            var gaps = FindJustificationGaps(text, text.LastIndexOf('\t') + 1, visible, MethodAt);
            gaps.RemoveAll(gap => gap.Start == gap.End
                ? At(gap.Start - 1).Fragment.Source.Atomic && At(gap.Start).Fragment.Source.Atomic
                : SeparatorWidth(gap.Start, gap.End) <= .000001);
            if (gaps.Count == 0) return false;

            var mapped = new Dictionary<CssInlineFragment, List<(JustificationGap Gap, double Fraction)>>();
            foreach (var gap in gaps)
            {
                if (gap.Start == gap.End)
                {
                    var item = At(gap.Start - 1);
                    var line = item.Fragment.Line;
                    var at = line.StartIndex + gap.Start - item.Start;
                    Add(item.Fragment, new(at, at), 1); continue;
                }
                for (var index = AtIndex(gap.Start); index < offsets.Count && offsets[index].Start < gap.End; index++)
                {
                    var item = offsets[index]; var line = item.Fragment.Line;
                    var start = Math.Max(gap.Start, item.Start); var end = Math.Min(gap.End, item.End);
                    if (end > start) Add(item.Fragment, new(line.StartIndex + start - item.Start,
                        line.StartIndex + end - item.Start), (double)(end - start) / (gap.End - gap.Start));
                }
            }
            var widths = new Dictionary<CssInlineFragment, double>();
            foreach (var fragment in fragments)
            {
                if (!mapped.TryGetValue(fragment, out var local) || fragment.Source.Atomic)
                { widths[fragment] = fragment.Line.Width; continue; }
                var line = fragment.Line; var cursor = line.StartIndex; var width = 0d;
                foreach (var gap in local)
                {
                    width += fragment.Source.Text.MeasureJustifiedSegment(cursor, gap.Gap.Start, line.StartIndex, line.StartIndex + line.Length, fragment.TabOrigin);
                    width += fragment.Source.Text.MeasureJustifiedSegment(gap.Gap.Start, gap.Gap.End, line.StartIndex, line.StartIndex + line.Length, fragment.TabOrigin);
                    cursor = gap.Gap.End;
                }
                width += fragment.Source.Text.MeasureJustifiedSegment(cursor, line.StartIndex + line.Length, line.StartIndex, line.StartIndex + line.Length, fragment.TabOrigin);
                if (line.HyphenChange is { HasChanges: true })
                    width = fragment.Source.Text.MeasureLineWithHyphenChange(line.StartIndex, line.Length, line.HyphenChange, line.SoftHyphenVisible, fragment.TabOrigin, includeTrailingWhitespace: false);
                else if (line.SoftHyphenVisible)
                    width += fragment.Source.Text.MeasureSoftHyphenWidth(line.StartIndex + line.Length - 1);
                widths[fragment] = Math.Max(0, width - line.HangingStart - line.HangingEnd);
            }
            var natural = FragmentWidth(fragments) + fragments.Sum(fragment => widths[fragment] - fragment.Line.Width);
            var extra = available - natural;
            if (extra <= .000001) return false;
            var perGap = extra / gaps.Count;
            foreach (var fragment in fragments)
            {
                if (!mapped.TryGetValue(fragment, out var local)) continue;
                var line = fragment.Line; var expansion = local.Sum(gap => gap.Fraction) * perGap;
                var filled = widths[fragment] + expansion;
                fragment.Line = new(line.StartIndex, line.Length, filled, line.HasLineBreakAfter,
                    line.Height, line.Baseline, line.GlyphHeight, line.FormattedText, line.CanJustify, line.IndentApplies,
                    line.PaintWrap, line.SoftHyphenVisible, line.StartPadding, line.EndPadding, line.HangingStart, line.HangingEnd, line.HyphenChange);
                if (fragment.Source.Atomic) continue;
                var before = new double[local.Count]; var after = new double[local.Count]; var amounts = new double[local.Count];
                var cursor = line.StartIndex; var x = 0d;
                for (var i = 0; i < local.Count; i++)
                {
                    var gap = local[i];
                    x += fragment.Source.Text.MeasureJustifiedSegment(cursor, gap.Gap.Start, line.StartIndex, line.StartIndex + line.Length, fragment.TabOrigin);
                    before[i] = x; amounts[i] = gap.Fraction * perGap;
                    x += fragment.Source.Text.MeasureJustifiedSegment(gap.Gap.Start, gap.Gap.End, line.StartIndex, line.StartIndex + line.Length, fragment.TabOrigin) + amounts[i];
                    after[i] = x; cursor = gap.Gap.End;
                }
                fragment.Justification = new(line.StartIndex, line.StartIndex + line.Length, filled, perGap,
                    local.Select(gap => gap.Gap).ToArray(), before, after, amounts, fragment.TabOrigin);
            }
            return true;

            void Add(CssInlineFragment fragment, JustificationGap gap, double fraction)
            {
                if (!mapped.TryGetValue(fragment, out var local)) mapped[fragment] = local = [];
                local.Add((gap, fraction));
            }
            CssTextJustification MethodAt(int before, int after)
            {
                var first = At(before); var second = At(after);
                if (ReferenceEquals(first.Fragment, second.Fragment) && !first.Fragment.Source.Atomic)
                    return first.Fragment.Source.Text.JustificationMethod(first.Fragment.Line.StartIndex + before - first.Start,
                        first.Fragment.Line.StartIndex + after - first.Start);
                for (var frame = first.Fragment.Source.Frame; frame is not null; frame = frame.Parent)
                    for (var other = second.Fragment.Source.Frame; other is not null; other = other.Parent)
                        if (ReferenceEquals(frame, other)) return ContextMethod(frame.Element);
                return ContextMethod(owner);
                CssTextJustification ContextMethod(DependencyObject context)
                {
                    var method = (CssTextJustification)context.GetValue(CssFlowProperties.TextJustifyProperty)!;
                    return method == CssTextJustification.Auto && Rune.TryGetRuneAt(text, before, out var a) &&
                        Rune.TryGetRuneAt(text, after, out var b) && !RubyAutoOpportunity(a, b, context)
                        ? CssTextJustification.None : method;
                }
            }
            int AtIndex(int position)
            {
                var low = 0; var high = offsets.Count;
                while (low < high)
                {
                    var middle = (low + high) / 2;
                    if (offsets[middle].End <= position) low = middle + 1; else high = middle;
                }
                return low;
            }
            (CssInlineFragment Fragment, int Start, int End) At(int position) => offsets[AtIndex(position)];
            double SeparatorWidth(int start, int end)
            {
                var width = 0d;
                for (var index = AtIndex(start); index < offsets.Count && offsets[index].Start < end; index++)
                {
                    var item = offsets[index]; var fragment = item.Fragment; var line = fragment.Line;
                    if (fragment.Source.Atomic) continue;
                    width += fragment.Source.Text.MeasureJustifiedSegment(line.StartIndex + Math.Max(start, item.Start) - item.Start,
                        line.StartIndex + Math.Min(end, item.End) - item.Start, line.StartIndex, line.StartIndex + line.Length);
                }
                return width;
            }
        }

        private bool TryJustifyShapedFragments(List<CssInlineFragment> fragments, double available, out bool expanded)
        {
            expanded = false;
            if (!OperatingSystem.IsMacOS() || RenderContext.Current is not
                { IsValid: true, Backend: RenderBackend.Metal or RenderBackend.Software } ||
                fragments.Count == 0 || fragments.Any(fragment => fragment.Shape is not null || !CanShape(fragment))) return false;
            for (int i = 1; i < fragments.Count; i++) if (ShapeBoundary(fragments[i - 1], fragments[i])) return false;
            double edges = fragments.Sum(fragment => fragment.Width - fragment.Line.Width + fragment.MarginLeft + fragment.MarginRight);
            var request = new ShapingJustification(available - edges);
            var oldFrames = FramePieces(fragments).ToArray();
            if (request.Width <= 0 || TryShape(fragments, request) is not { } result) return false;
            foreach (var fragment in result) { fragment.OuterLeft = fragment.OuterRight = 0; fragment.FramePieces = null; }
            var frames = new List<CssInlineFramePiece>(); bool rtl = owner.FlowDirection == FlowDirection.RightToLeft;
            foreach (var old in oldFrames)
            {
                int first = result.FindIndex(fragment => BelongsTo(fragment.Source, old.Frame));
                int last = result.FindLastIndex(fragment => BelongsTo(fragment.Source, old.Frame));
                if (first < 0) continue;
                var piece = new CssInlineFramePiece(old.Frame, rtl ? last : first, rtl ? first : last, old.First, old.Last);
                result[piece.LeftIndex].OuterLeft += piece.Left + piece.MarginLeft;
                result[piece.RightIndex].OuterRight += piece.Right + piece.MarginRight; frames.Add(piece);
            }
            result[0].FramePieces = frames;
            fragments.Clear(); fragments.AddRange(result); expanded = request.Gaps.Length > 0; return true;
        }
    }
}
