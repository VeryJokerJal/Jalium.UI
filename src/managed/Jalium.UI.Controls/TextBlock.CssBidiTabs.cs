using Jalium.UI.Interop;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private double CssBidiTabOverflow(in TextLayoutLine line)
    {
        int end=line.StartIndex+line.Length;
        if (!ContainsPreservedTab(line.StartIndex,line.Length) || end <= line.StartIndex ||
            _visualText[end-1] is not (' ' or '\t')) return 0;
        double width=GetContentWidth(TextContentSize.Width);
        double origin=ResolveTextIndent(line.IndentApplies,width)+GetLineStartPadding(line.StartIndex,end);
        if (!TryMeasureCssBidiRange(line.StartIndex,line.Length,true,out double physical,origin) ||
            _cssBidiSpacing?.TabsHangLeft(line.StartIndex,end) != true) return 0;
        return Math.Max(0,physical-line.Width-line.HangingStart-line.HangingEnd);
    }

    private sealed partial class CssBidiSpacingMetrics
    {
        private readonly TextBlock _block;
        private readonly bool _hasTabs;
        private readonly Dictionary<(int, int, double), double> _tabs = [];
        private sealed record TabCell(Cell Source, double Leading, double Trailing);
        private sealed record TabLayout(double Extra, TabCell[] Cells);

        internal bool TabsHangLeft(int start, int end)
        {
            for (int i=FirstAt(start); i<_cells.Length && _cells[i].First<end; i++)
                if (_cells[i].Tab) return _cells[i].Rtl;
            return false;
        }

        // Tabulation follows the paragraph direction (UAX #9 L1), including
        // tabs inside an override or isolate. Native tab edges retain that
        // direction while all glyphs remain shaped in paragraph context.
        private double TabExtra(int start, int end, double origin)
        {
            if (_tabs.TryGetValue((start, end, origin), out var cached)) return cached;
            double result = TabPlan(start, end, origin, capture: false).Extra;
            if (_tabs.Count >= 4096) _tabs.Clear();
            _tabs[(start,end,origin)] = result; return result;
        }

        private TabLayout TabPlan(int start, int end, double origin, bool capture = true)
        {
            var output = new List<TabCell>(); double extra = 0;
            int first = FirstAt(start), last = FirstAt(end);
            foreach (var row in _cells.AsSpan(first, last-first).ToArray().Where(cell => cell.End <= end).GroupBy(cell => cell.Row))
            {
                var all = row.OrderBy(cell => cell.Left).ToArray();
                var active = all.Where(cell => !cell.Tab || _block.GetPreservedTabAdvance(0, cell.First) > 0).ToArray();
                var left = new double[active.Length]; var right = new double[active.Length];
                for (int i = 0; i < active.Length; i++) left[i] = right[i] = active[i].Word/2;
                for (int i = 1; i < active.Length; i++)
                {
                    var a = active[i-1]; var b = active[i];
                    if (a.Cursive && b.Cursive) continue;
                    double requested = (a.Letter+b.Letter)/2;
                    // A tab is a positioning control. Bound negative tracking
                    // at this boundary to zero, keeping tab stops monotonic.
                    double limit = a.Tab || b.Tab ? 0 : -Math.Min(a.Natural+a.Word, b.Natural+b.Word);
                    double actual = Math.Max(limit, requested);
                    double scale = requested < 0 && actual != requested ? actual/requested : 1;
                    right[i-1] += a.Letter/2*scale; left[i] += b.Letter/2*scale;
                }
                bool rtl = all.FirstOrDefault(cell => cell.Tab) is { } tab && tab.Rtl;
                double position = origin;
                for (int logical = 0; logical < active.Length; logical++)
                {
                    int i = rtl ? active.Length-1-logical : logical;
                    var cell = active[i];
                    double before = rtl ? right[i] : left[i], after = rtl ? left[i] : right[i];
                    if (cell.Tab)
                    {
                        // Include the next glyph's own leading pad when
                        // choosing the stop, then absorb it into the tab.
                        int next = rtl ? i-1 : i+1;
                        double nextPad = next >= 0 && next < active.Length ? rtl ? right[next] : left[next] : 0;
                        double advance = _block.GetPreservedTabAdvance(position + before + after + nextPad, cell.First);
                        double delta = advance-cell.Natural;
                        if (rtl) left[i] += delta; else right[i] += delta;
                    }
                    position += cell.Natural+left[i]+right[i];
                    if (capture) output.Add(new(cell, cell.Rtl ? right[i] : left[i], cell.Rtl ? left[i] : right[i]));
                }
                if (capture)
                {
                    var activeSet = active.ToHashSet();
                    foreach (var cell in all.Where(cell => cell.Tab && !activeSet.Contains(cell)))
                        output.Add(new(cell, 0, -cell.Natural));
                }
                extra += position-origin-all.Sum(cell => cell.Natural);
            }
            return new(extra, output.ToArray());
        }

        internal NativeMethods.ParagraphAdvanceUnit[] TabUnits(NativeMethods.ParagraphAdvanceUnit[] units)
        {
            var indices = units.Select((unit, index) => (unit.TextPosition, index)).ToDictionary(pair => pair.TextPosition, pair => pair.index);
            foreach (var line in _block._layoutLines)
            {
                double width = _block.GetContentWidth(_block.TextContentSize.Width);
                double origin = _block.ResolveTextIndent(line.IndentApplies, width) + _block.GetLineStartPadding(line.StartIndex, line.StartIndex+line.Length);
                foreach (var cell in TabPlan(line.StartIndex, line.StartIndex+line.Length, origin).Cells)
                {
                    int index = indices[(uint)cell.Source.NativeFirst];
                    units[index].Spacing = 0;
                    units[index].Leading = (float)cell.Leading;
                    units[index].Trailing = (float)cell.Trailing;
                    units[index].Flags = 0;
                }
            }
            return units;
        }
    }
}
