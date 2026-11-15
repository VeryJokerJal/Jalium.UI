using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    internal sealed partial class CssTextFlowLayout
    {
        private readonly record struct FloatEvent(int Position, UIElement Element);
        private readonly List<FloatEvent> _floatEvents = [];
        private CssFloatSpace? _floatSpace;
        private Func<UIElement, double, double, double, Rect>? _placeFloat;
        private readonly Dictionary<UIElement, Rect> _nestedFloatRects = [];
        private int _nextFloat;

        private sealed record LinePlan(int End, int Consumed, bool HardBreak, List<CssInlineFragment> Fragments);

        private void BeginFloats(CssFloatSpace? space, Func<UIElement, double, double, double, Rect>? place)
        {
            _floatSpace = space; _placeFloat = place; _nextFloat = 0; _floatEvents.Clear();
            _nestedFloatRects.Clear();
        }

        private (double Left, double Right, bool Obstructed) FloatBand(double height, int count = int.MaxValue) =>
            (_floatSpace ?? _initialEmptySpace).Band(_containing.Width, _y, height, count, useShapes: true, additional: _initialLetter?.Region);

        private void PlaceFloatsThrough(int position, double occupied, double height)
        {
            while (_nextFloat < _floatEvents.Count && _floatEvents[_nextFloat].Position <= position)
            {
                var item = _floatEvents[_nextFloat++];
                if (_placeFloat is { } place)
                {
                    using var initial = _floatSpace?.Temporary(_initialLetter?.Region);
                    var rect = place(item.Element, _y, Math.Max(0, occupied), Math.Max(0, height));
                    if (!ReferenceEquals(item.Element.VisualParent, owner)) _nestedFloatRects[item.Element] = rect;
                }
            }
        }

        private void DrainFloats() => PlaceFloatsThrough(int.MaxValue, 0, 0);

        private List<CssInlineFragment> FragmentsFor(List<CssInlineUnit> units, int start, int end,
            double indent, int consumed, double minimumHeight, bool includeTrailingHyphen = true)
        {
            if (_floatSpace is null && _initialLetter?.Region is null)
                return Pieces(units, start, end, indent, consumed, includeTrailingHyphen: includeTrailingHyphen);
            var band = FloatBand(Math.Max(minimumHeight, CssFlowProperties.LineMetrics(Owner).Height));
            List<CssInlineFragment> fragments = [];
            for (var pass = 0; pass < 6; pass++)
            {
                fragments = Pieces(units, start, end, band.Left + indent, consumed,
                    Math.Max(0, band.Right - band.Left) - indent,
                    tabRulerOrigin: ((_inlineBidi?.DirectionAt(start)??owner.FlowDirection)==FlowDirection.RightToLeft
                        ? _containing.Width-band.Right : band.Left)+indent,
                    includeTrailingHyphen: includeTrailingHyphen);
                var actual = FloatBandFor(fragments, Math.Max(minimumHeight, FragmentMetrics(fragments).Height));
                if (band.Left == actual.Left && band.Right == actual.Right) break;
                band = actual;
            }
            return fragments;
        }

        private static double FragmentWidth(List<CssInlineFragment> fragments) =>
            fragments.Sum(static fragment => fragment.Width + fragment.MarginLeft + fragment.MarginRight - fragment.HangingWhitespace);

        private bool BreakAfter(List<CssInlineUnit> units, int index, int start, int limit)
        {
            var previous = index; var next = index + 1;
            while (previous >= start && !units[previous].Source.Atomic && !units[previous].HardBreak && units[previous].Start == units[previous].End) previous--;
            while (next < limit && !units[next].Source.Atomic && !units[next].HardBreak && units[next].Start == units[next].End) next++;
            return previous >= start && next < limit && BreakBetween(units[previous], units[next]);
        }

        private LinePlan PlanLine(List<CssInlineUnit> units, bool[] splitAfter, int start, double indent, bool noWrap, int paragraphEnd)
        {
            paragraphEnd = FirstLineBoundary(units, start, paragraphEnd);
            if (_floatSpace is null && _initialLetter?.Region is null) return SelectLine(units, splitAfter, start, paragraphEnd, indent, noWrap, 0, 0);
            var space = _floatSpace ?? _initialEmptySpace;
            // Floats preceding this line have a stable source anchor even when
            // the first unbreakable word has to move below their bottom edge.
            PlaceFloatsThrough(start, 0, 0);
            var checkpoint = space.Capture(); var firstEvent = _nextFloat;
            var y = _y; var limit = paragraphEnd; var reserve = 0d; var minimumHeight = 0d;
            while (true)
            {
                space.Restore(checkpoint); _nextFloat = firstEvent; _y = y;
                var plan = SelectLine(units, splitAfter, start, limit, indent, noWrap, reserve, minimumHeight);
                // Looking ahead into a word must not pull a later float above
                // that word when the whole word moves to the following line.
                if (_nextFloat > firstEvent && _floatEvents[_nextFloat - 1].Position > plan.Consumed)
                {
                    limit = Math.Min(limit, plan.Consumed);
                    continue;
                }
                var height = FragmentMetrics(plan.Fragments).Height;
                var range = InitialAvoidanceBounds(plan.Fragments, height);
                if (range.Active && _y + range.Top < -.000001)
                {
                    // Raised initials move their originating line down to keep
                    // the margin box inside the containing block. Replan at
                    // that position before choosing text or placing floats.
                    y = -range.Top; continue;
                }
                var width = FragmentWidth(plan.Fragments) + indent;
                var band = FloatBandFor(plan.Fragments, height);
                if (height > 0 && band.Obstructed && width > band.Right - band.Left + .000001)
                {
                    var prior = FloatBandFor(plan.Fragments, height, checkpoint.Count);
                    var fitting = _initialLetter?.Region is { } initial
                        ? new CssFloatSpace(new([.. space.At(0, 0).Regions.Take(checkpoint.Count), initial])) : space;
                    var next = fitting.NextFit(_containing.Width, _y + range.Top, range.Bottom - range.Top,
                        width, _initialLetter?.Region is null ? checkpoint.Count : int.MaxValue,
                        useShapes: !range.Active) - range.Top;
                    if (prior.Obstructed && width > prior.Right - prior.Left + .000001 && double.IsFinite(next))
                    {
                        y = next; limit = paragraphEnd; reserve = minimumHeight = 0;
                        continue;
                    }
                    if (_nextFloat > firstEvent && (band.Left != prior.Left || band.Right != prior.Right) &&
                        (width > reserve + .000001 || height > minimumHeight + .000001))
                    {
                        // A float in this very line follows its preceding text.
                        // Reserve the unbreakable word and final line height so
                        // the ordinary float placer can move the float down.
                        reserve = Math.Max(reserve, width); minimumHeight = Math.Max(minimumHeight, range.Bottom);
                        continue;
                    }
                }
                return plan;
            }
        }

        private LinePlan SelectLine(List<CssInlineUnit> units, bool[] splitAfter, int start, int limit,
            double indent, bool noWrap, double minimumReserve, double minimumHeight)
        {
            void FloatsAt(int position)
            {
                if (_nextFloat >= _floatEvents.Count || _floatEvents[_nextFloat].Position > position) return;
                var visible = position;
                while (visible > start && units[visible - 1].Collapse) visible--;
                var prefix = FragmentsFor(units, start, visible, indent, position, minimumHeight);
                var height = Math.Max(minimumHeight, FragmentMetrics(prefix).Height);
                PlaceFloatsThrough(position, Math.Max(minimumReserve, FragmentWidth(prefix) + indent),
                    InitialAvoidanceBounds(prefix, height).Bottom);
            }
            var zone = ((CssLayoutLength)owner.GetValue(CssFlowProperties.HyphenateLimitZoneProperty)!)
                .Resolve(_containing.Width, 0);
            var end = start; var lastBreak = -1; var lastPlainBreak = -1; var lastPlainWidth = 0d; var hardBreak = false; var overflowingToken = false;
            for (; end < limit; end++)
            {
                FloatsAt(end);
                if (units[end].HardBreak) { hardBreak = true; break; }
                if (noWrap || double.IsPositiveInfinity(_containing.Width)) continue;
                if (overflowingToken)
                {
                    // An unbreakable final word can overflow the initial's own
                    // width. Its bounded box still leaves room for body text.
                    if (units[end].Source.Initial is { Inline: true, Frame: { } initialFrame } &&
                        end >= initialFrame.LastUnit)
                    {
                        var prefix = FragmentsFor(units, start, end + 1, indent, end + 1, minimumHeight);
                        var prefixBand = FloatBandFor(prefix, Math.Max(minimumHeight, FragmentMetrics(prefix).Height));
                        if (FragmentWidth(prefix) <= prefixBand.Right - prefixBand.Left - indent + .000001)
                        { overflowingToken = false; lastBreak = end + 1; continue; }
                    }
                    if (end + 1 < limit && splitAfter[end] && BreakAfter(units, end, start, limit))
                    { end++; break; }
                    continue;
                }
                var fitEnd = end + 1;
                while (fitEnd > start && units[fitEnd - 1].Collapse) fitEnd--;
                var candidate = FragmentsFor(units, start, fitEnd, indent, end + 1, minimumHeight, includeTrailingHyphen: false);
                var band = FloatBandFor(candidate, Math.Max(minimumHeight, FragmentMetrics(candidate).Height));
                var available = Math.Max(0, band.Right - band.Left - _wrapWidthReduction) - indent;
                var candidateWidth = FragmentWidth(candidate);
                var outerOverflow = double.IsFinite(available) && candidateWidth > available + .000001;
                var ownOverflow = FramePieces(candidate).Any(static piece => piece.Initial is { Overflow: true });
                if (ownOverflow && !outerOverflow && lastBreak <= start && units[end].Source.Initial is { Frame: { } finalFrame } &&
                    end >= finalFrame.LastUnit) ownOverflow = false;
                var overflow = outerOverflow || ownOverflow;
                var canBreakAfter = end + 1 < limit && splitAfter[end] && BreakAfter(units, end, start, limit);
                var isHyphenBreak = canBreakAfter && IsHyphenBreakAfter(units, end, start);
                if (isHyphenBreak && zone > 0 && double.IsFinite(available) &&
                    (zone >= available - .000001 || (lastPlainBreak > start && available - lastPlainWidth <= zone + .000001)))
                {
                    canBreakAfter = false;
                    isHyphenBreak = false;
                }
                if (isHyphenBreak)
                {
                    var hyphenated = FragmentsFor(units, start, fitEnd, indent, end + 1, minimumHeight, includeTrailingHyphen: true);
                    var hyphenOwnOverflow = FramePieces(hyphenated).Any(static piece => piece.Initial is { Overflow: true });
                    var hyphenFits = (!double.IsFinite(available) || FragmentWidth(hyphenated) <= available + .000001) && !hyphenOwnOverflow;
                    if (hyphenFits || lastBreak <= start)
                        lastBreak = end + 1;
                }
                else if (canBreakAfter && !overflow)
                {
                    lastBreak = end + 1;
                    lastPlainBreak = end + 1;
                    lastPlainWidth = candidateWidth;
                }
                if (overflow && lastBreak > start) { end = lastBreak; break; }
                if (overflow && !units[end].Source.Atomic && units[end].Source.Wraps && units[end].Source.Text.AllowsEmergencyWrap(_intrinsic))
                {
                    if (end > start && splitAfter[end - 1]) break;
                    if (splitAfter[end]) { end++; break; }
                }
                if (canBreakAfter && overflow)
                {
                    end++; break;
                }
                if (overflow) overflowingToken = true;
            }
            FloatsAt(end);
            var consumed = hardBreak ? end + 1 : end;
            var visibleEnd = end;
            while (visibleEnd > start && units[visibleEnd - 1].Collapse) visibleEnd--;
            var fragments = FragmentsFor(units, start, visibleEnd, indent, consumed, minimumHeight, includeTrailingHyphen: true);
            if (hardBreak)
            {
                var breaker = units[end];
                if (fragments.Count == 0 || fragments[^1].Source != breaker.Source)
                {
                    if (fragments.Count > 0)
                    {
                        var previous = fragments[^1]; var line = previous.Line;
                        previous.Line = new TextLayoutLine(line.StartIndex, line.Length,
                            line.Width + line.HangingEnd, line.HasLineBreakAfter,
                            line.Height, line.Baseline, line.GlyphHeight, line.FormattedText,
                            line.CanJustify, line.IndentApplies, line.PaintWrap, line.SoftHyphenVisible,
                            startPadding: line.StartPadding, hangingStart: line.HangingStart,
                            hyphenChange: line.HyphenChange);
                    }
                    fragments.Add(new(breaker.Source, breaker.Source.Text.CreateTextLayoutLine(breaker.Start, 0, 0, true,
                        lineStart: fragments.Count == 0)));
                }
                else
                {
                    var fragment = fragments[^1]; var line = fragment.Line;
                    var band = FloatBandFor(fragments, FragmentMetrics(fragments).Height);
                    fragment.Line = fragment.Source.Text.CreateTextLayoutLine(line.StartIndex, line.Length,
                        line.Width + line.HangingStart + line.HangingEnd, true,
                        softHyphenVisible: line.SoftHyphenVisible, availableWidth: band.Right - band.Left - indent,
                        lineStart: fragments.Count == 1, hyphenChange: line.HyphenChange);
                }
                DecorateInlineFrames(fragments, consumed);
            }
            return new(end, consumed, hardBreak, fragments);
        }
    }
}
