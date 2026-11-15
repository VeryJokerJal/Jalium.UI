using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private readonly record struct CombinedTextUnit(int Start, int End, double Em, FlowDirection Direction);
    private CombinedTextUnit[] _combinedTextUnits = [];
    private int[]? _combinedTextBoundaries;
    private bool[]? _combinedWhiteSpaceMask;

    private void RebuildTextCombine()
    {
        _combinedTextUnits = []; _combinedTextBoundaries = null; _combinedWhiteSpaceMask = null;
        if (!OperatingSystem.IsMacOS() || !IsVerticalTextLayout ||
            TextWritingMode is not (CssWritingMode.VerticalRl or CssWritingMode.VerticalLr) || _displayText.Length == 0) return;
        var ranges = GetInlineTextRanges();
        // Projected paint leaves still look through the author's original
        // inline tree; splitting a decorated Span must not create new digit
        // sequence boundaries or lose the common ancestor's combine rule.
        var contextOwner = this is InlineSourceText projected ? projected.SourceOwner : this;
        int contextOffset = this is InlineSourceText leaf ? leaf.SourceOffset : 0;
        var contextRanges = contextOwner.GetInlineTextRanges();
        var contextText = contextOwner._displayText;
        var boundaries = GraphemeClusters.GetBoundaries(_displayText);
        var units = new List<CombinedTextUnit>();
        if (_inlines is null) Add(TextWritingSource, 0, _displayText.Length, null);
        else foreach (var range in ranges) Add(range.Run, range.Start, range.End, range.Run);
        _combinedTextUnits = units.ToArray();
        if (units.Count > 0)
        {
            int current = 0; var atomic = new List<int>(boundaries.Length);
            foreach (int at in boundaries)
            {
                while (current < units.Count && at >= units[current].End) current++;
                if (current == units.Count || at <= units[current].Start) atomic.Add(at);
            }
            _combinedTextBoundaries = atomic.ToArray();
        }

        void Add(DependencyObject owner, int first, int last, Run? run)
        {
            var combine = CssWritingModeProperties.GetTextCombineUpright(owner);
            if (!combine.IsEnabled || first >= last) return;
            foreach (var segment in CssWritingModeProperties.SegmentTateChuYoko(
                _displayText.Substring(first, last - first), TextWritingMode, combine))
                if (segment.IsCombined) Candidate(first + segment.StartIndex, first + segment.StartIndex + segment.Length);
            void Candidate(int start, int end)
            {
                if (Array.BinarySearch(boundaries, start) < 0 || Array.BinarySearch(boundaries, end) < 0) return;
                // Look through inline boundaries only when a common ancestor
                // enables combination. Never combine a partial inherited run.
                if (start == first && contextOffset + start > 0 && Continues(contextOffset + start - 1) ||
                    end == last && contextOffset + end < contextText.Length && Continues(contextOffset + end)) return;
                double em = run is null ? FontSize : GetRunFontSize(run);
                if (!double.IsFinite(em) || em <= 0 || em > 1000000) return;
                units.Add(new(start, end, em, run is null ? FlowDirection : (FlowDirection)run.GetValue(FlowDirectionProperty)!));
                // Whitespace at each composition's edges is processed as in a
                // horizontal inline-block, rather than as part of the column.
                var white = (CssWhiteSpaceCollapse)owner.GetValue(CssFlowProperties.WhiteSpaceProperty)!;
                if (white is CssWhiteSpaceCollapse.Collapse or CssWhiteSpaceCollapse.PreserveBreaks)
                {
                    _combinedWhiteSpaceMask ??= new bool[_displayText.Length];
                    for (int at = start; at < end && _displayText[at] is ' ' or '\t' or '\r' or '\n'; at++) _combinedWhiteSpaceMask[at] = true;
                    for (int at = end - 1; at >= start && _displayText[at] is ' ' or '\t' or '\r' or '\n'; at--) _combinedWhiteSpaceMask[at] = true;
                }
            }
            bool Continues(int position)
            {
                var neighbour = contextRanges.FirstOrDefault(range => position >= range.Start && position < range.End);
                if (neighbour.Run is null) return false; // A replaced inline or explicit break is a boundary.
                var mine = Ancestors(owner).ToArray();
                foreach (var ancestor in Ancestors(neighbour.Run))
                {
                    if (!mine.Contains(ancestor)) continue;
                    var common = CssWritingModeProperties.GetTextCombineUpright(ancestor);
                    if (common.IsEnabled && (common.Kind == CssTextCombineUprightKind.All || Digit(contextText[position]))) return true;
                }
                return false;
            }
        }
        IEnumerable<DependencyObject> Ancestors(DependencyObject value)
        {
            while (value is TextElement element)
            {
                yield return value;
                if (element.Parent is not { } parent) break;
                value = parent;
            }
            yield return contextOwner;
        }
        static bool Digit(char value) => value is >= '0' and <= '9';
    }

    private CombinedTextUnit? CombinedUnitAt(int position)
    {
        int low = 0, high = _combinedTextUnits.Length;
        while (low < high) { int middle = (low + high) / 2; if (_combinedTextUnits[middle].Start <= position) low = middle + 1; else high = middle; }
        return low > 0 && position < _combinedTextUnits[low - 1].End ? _combinedTextUnits[low - 1] : null;
    }
    private bool IsCombinedTextInterior(int position) => CombinedUnitAt(position) is { } unit && position > unit.Start;
    private int[] TextLayoutBoundaries() => _combinedTextBoundaries ?? GraphemeClusters.GetBoundaries(_visualText);
    private int NextTextLayoutBoundary(int position) => CombinedUnitAt(position) is { } unit ? unit.End : GraphemeClusters.NextBoundary(_visualText, position);
    private int PreviousTextLayoutBoundary(int position) => CombinedUnitAt(position - 1) is { } unit ? unit.Start : GraphemeClusters.PreviousBoundary(_visualText, position);

    private NativeMethods.ParagraphCombineUnit[] NativeCombinedUnits(CssBidiProjection projection)
        => _combinedTextUnits.Select(unit =>
        {
            var range = projection.NativeRange(unit.Start, unit.End - unit.Start);
            return new NativeMethods.ParagraphCombineUnit { TextPosition = (uint)range.Start, Length = (uint)range.Length,
                Em = (float)unit.Em, Direction = unit.Direction == FlowDirection.RightToLeft ? 1 : 0 };
        }).Where(unit => unit.Length > 0).ToArray();
}
