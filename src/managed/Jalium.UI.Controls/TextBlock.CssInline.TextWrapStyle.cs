using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    internal sealed partial class CssTextFlowLayout
    {
        // Only the break selector uses this reduction. The containing block,
        // percentages, tab ruler, hanging whitespace and float placer keep
        // their actual widths throughout both probing and final placement.
        private double _wrapWidthReduction;

        private double ParagraphIndent(bool first, bool afterBreak, bool useIndent, double? basis)
        {
            var value = (CssTextIndent)owner.GetValue(CssFlowProperties.TextIndentProperty)!;
            var indent = useIndent && value.Applies(first, afterBreak) ? value.Resolve(basis ?? _containing.Width) : 0;
            return double.IsFinite(indent) ? indent : 0;
        }

        private readonly record struct WrapMeasure(double Occupied, double Available)
        {
            internal double Free => Available - Occupied;
        }

        private double ChooseWrapWidthReduction(List<CssInlineUnit> units, bool[] splitAfter, int start, int end,
            bool first, bool afterBreak, bool useIndent, double? indentBasis, bool noWrap)
        {
            var style = (CssTextWrapStyle)owner.GetValue(CssFlowProperties.TextWrapStyleProperty)!;
            if (CssLineClampLayout.Stable(owner) || style is CssTextWrapStyle.Auto or CssTextWrapStyle.Stable || _intrinsic || noWrap ||
                !double.IsFinite(_containing.Width) || _containing.Width <= 1 || end - start > 4096)
                return 0;
            var characters = 0;
            for (var index = start; index < end; index++)
                characters += units[index].Source.Atomic ? 1 : units[index].End - units[index].Start;
            if (characters > 4096) return 0;

            var maximumLines = style == CssTextWrapStyle.Balance ? 10 : 24;
            var greedy = Probe(0);
            if (greedy.Count <= 1 || greedy.Count > maximumLines) return 0;
            if (style == CssTextWrapStyle.AvoidShortLastLine && greedy[^1].Occupied >= greedy[^1].Available * .4)
                return 0;
            var overflow = MaxOverflow(greedy);
            var bestScore = Score(greedy, style);
            var bestReduction = 0d;
            var low = 1d; var high = _containing.Width;
            for (var attempt = 0; attempt < 12 && high - low > .25; attempt++)
            {
                var middle = (low + high) / 2;
                if (Probe(_containing.Width - middle).Count <= greedy.Count) high = middle;
                else low = middle;
            }
            if (high >= _containing.Width - .25) return 0;
            for (var sample = 0; sample < 9; sample++)
            {
                var reduction = (_containing.Width - high) * (1 - sample / 9d);
                var candidate = Probe(reduction);
                if (candidate.Count != greedy.Count || MaxOverflow(candidate) > overflow + .5) continue;
                if (style == CssTextWrapStyle.AvoidShortLastLine &&
                    (candidate[^1].Occupied <= greedy[^1].Occupied + .5 ||
                     candidate.Take(candidate.Count - 1).Any(line => line.Occupied < line.Available * .3))) continue;
                var score = Score(candidate, style);
                if (score >= bestScore - .001) continue;
                bestScore = score; bestReduction = reduction;
            }
            return bestReduction;

            List<WrapMeasure> Probe(double reduction)
            {
                var y = _y; var nextFloat = _nextFloat; var oldReduction = _wrapWidthReduction;
                var checkpoint = _floatSpace?.Capture();
                var sourceCounts = _sources.Select(static source => source.Fragments.Count).ToArray();
                var frameCounts = _frames.Select(static frame => frame.Fragments.Count).ToArray();
                var floatRects = _nestedFloatRects.ToArray();
                var result = new List<WrapMeasure>();
                var cursor = start; var firstLine = first; var hardBreak = afterBreak;
                _wrapWidthReduction = reduction;
                try
                {
                    while (cursor < end)
                    {
                        while (cursor < end && units[cursor].Collapse) cursor++;
                        if (cursor == end) break;
                        var indent = ParagraphIndent(firstLine, hardBreak, useIndent, indentBasis);
                        var plan = PlanLine(units, splitAfter, cursor, indent, noWrap, end);
                        var metrics = FragmentMetrics(plan.Fragments);
                        var band = FloatBand(metrics.Height);
                        if (!metrics.Empty)
                            result.Add(new(FragmentWidth(plan.Fragments) + indent, Math.Max(0, band.Right - band.Left)));
                        // Edge slicing depends on whether a source or frame has
                        // already participated. Advance that state without
                        // alignment, paint allocations or public-tree changes.
                        foreach (var fragment in plan.Fragments) fragment.Source.Fragments.Add(fragment);
                        foreach (var piece in FramePieces(plan.Fragments)) piece.Frame.Fragments.Add(piece);
                        _floatSpace?.NoteSourceTop(_y); _y += metrics.Height;
                        if (!metrics.Empty) firstLine = false;
                        hardBreak = plan.HardBreak;
                        cursor = Math.Max(cursor + 1, plan.Consumed);
                        if (result.Count > maximumLines) break;
                    }
                    return result;
                }
                finally
                {
                    for (var i = 0; i < _sources.Count; i++)
                        _sources[i].Fragments.RemoveRange(sourceCounts[i], _sources[i].Fragments.Count - sourceCounts[i]);
                    for (var i = 0; i < _frames.Count; i++)
                        _frames[i].Fragments.RemoveRange(frameCounts[i], _frames[i].Fragments.Count - frameCounts[i]);
                    if (checkpoint is { } saved) _floatSpace!.Restore(saved);
                    _nestedFloatRects.Clear();
                    foreach (var pair in floatRects) _nestedFloatRects.Add(pair.Key, pair.Value);
                    _y = y; _nextFloat = nextFloat; _wrapWidthReduction = oldReduction;
                    // PlanLine may update provisional float entry rectangles.
                    // The final pass replays every event in this same forced-
                    // break group, overwriting them against the restored space.
                }
            }
        }

        private static double MaxOverflow(List<WrapMeasure> lines) =>
            lines.Select(static line => -line.Free).Prepend(0).Max();

        private static double Score(List<WrapMeasure> lines, CssTextWrapStyle style)
        {
            var average = lines.Average(static line => line.Free);
            var variance = lines.Sum(line => Math.Pow(line.Free - average, 2));
            if (style == CssTextWrapStyle.Balance) return variance;
            var shortfall = Math.Max(0, lines[^1].Available * .4 - lines[^1].Occupied);
            return style == CssTextWrapStyle.Pretty ? variance + 2 * shortfall * shortfall
                : .15 * variance + 4 * shortfall * shortfall;
        }
    }
}
