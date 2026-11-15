using System.Globalization;
using System.Text;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private readonly record struct CssBidiSpacingSource(int First, int End, int NativeFirst, int NativeLength,
        double Letter, double Word, bool Cursive, Rune Rune);

    private bool SupportedCssBidiSpacing()
    {
        if (!_hasLetterSpacing && !_hasWordSpacing) return true;
        bool Valid(DependencyObject owner, double size)
        {
            double letter = CssTextSpacing.Letter(owner);
            double word = ((CssLayoutLength)owner.GetValue(CssFlowProperties.WordSpacingProperty)!).Resolve(size,0);
            return double.IsFinite(letter) && Math.Abs(letter) <= 1000000 && double.IsFinite(word) && Math.Abs(word) <= 1000000;
        }
        return Valid(this,FontSize) && GetInlineTextRanges().All(range => Valid(range.Run,GetRunFontSize(range.Run)));
    }

    private (NativeMethods.ParagraphAdvanceUnit[] Units, CssBidiSpacingSource[] Sources) BuildCssBidiSpacing(CssBidiProjection projection,
        bool usedLines = false)
    {
        var units = new List<NativeMethods.ParagraphAdvanceUnit>();
        var sources = new List<CssBidiSpacingSource>();
        var boundaries = TextLayoutBoundaries();
        var displayedBoundaries = GraphemeClusters.GetBoundaries(projection.Text);
        var ranges = GetInlineTextRanges(); int rangeIndex = 0, cursor = 0;
        int lineIndex = 0;
        bool collapseEnds = usedLines && HasAuthoredWhiteSpace() &&
            _whiteSpaceMode is CssWhiteSpaceCollapse.Collapse or CssWhiteSpaceCollapse.PreserveBreaks;
        var usedEnds = collapseEnds ? _layoutLines.Select(line =>
        {
            int end = line.StartIndex+line.Length;
            while (end > line.StartIndex && projection.Source[end-1] is ' ' or '\t') end--;
            return end;
        }).ToArray() : [];
        for (int i = 0; i + 1 < boundaries.Length; i++)
        {
            int first = boundaries[i], last = boundaries[i + 1];
            var native = projection.NativeRange(first, last - first);
            if (native.Start > cursor) Add(cursor, native.Start, 0, 0, 1);
            while (rangeIndex < ranges.Count && ranges[rangeIndex].End <= first) rangeIndex++;
            var run = rangeIndex < ranges.Count && ranges[rangeIndex].Start <= first ? ranges[rangeIndex].Run : null;
            double letter = CssTextSpacing.Letter(run ?? (DependencyObject)this);
            double word = ((CssLayoutLength)(run ?? (DependencyObject)this).GetValue(CssFlowProperties.WordSpacingProperty)!)
                .Resolve(run is null ? FontSize : GetRunFontSize(run), 0);
            if (!double.IsFinite(letter) || Math.Abs(letter) > 1000000 || !double.IsFinite(word) || Math.Abs(word) > 1000000)
                return ([], []);
            if (native.Length == 0) { cursor = native.Start; continue; }
            if (CombinedUnitAt(first) is { } combined && combined.Start == first && combined.End == last)
            {
                // The composition is one spacing cell. Its horizontal shaping
                // ignores internal tracking and word gaps, while neighbouring
                // vertical cells still receive ordinary inter-character spacing.
                Add(native.Start, native.Start + native.Length, letter, 0, 0);
                sources.Add(new(first, last, native.Start, native.Length, letter, 0, false, new Rune(0xfffc)));
                cursor = native.Start + native.Length;
                continue;
            }
            bool ignored = true;
            foreach (var value in projection.Source.AsSpan(first, last-first).EnumerateRunes())
                if (Rune.GetUnicodeCategory(value) != UnicodeCategory.Format && value.Value is not
                    ('\r' or '\n' or 0x85 or 0x2028 or 0x2029 or 0x1c or 0x1d or 0x1e)) { ignored = false; break; }
            if (collapseEnds)
            {
                while (lineIndex+1 < _layoutLines.Count && first >= _layoutLines[lineIndex+1].StartIndex) lineIndex++;
                ignored |= first >= usedEnds[lineIndex] && first < _layoutLines[lineIndex].StartIndex+_layoutLines[lineIndex].Length;
            }
            int display = Array.BinarySearch(displayedBoundaries, native.Start);
            display = display >= 0 ? display : ~display - 1;
            for (; display + 1 < displayedBoundaries.Length && displayedBoundaries[display] < native.Start + native.Length; display++)
            {
                int a = Math.Max(native.Start, displayedBoundaries[display]), b = Math.Min(native.Start + native.Length, displayedBoundaries[display + 1]);
                Rune.TryGetRuneAt(projection.Text, a, out var shown);
                bool hidden = ignored || Rune.GetUnicodeCategory(shown) == UnicodeCategory.Format ||
                    shown.Value is '\r' or '\n' or 0x85 or 0x2028 or 0x2029 or 0x1c or 0x1d or 0x1e;
                bool cursive = Rune.IsLetter(shown) && IsCursiveJoiningPair(shown, shown);
                bool separator = IsWordSeparator(shown) && shown.Value != '\t'; double gap = separator ? word : 0;
                Add(a, b, letter, gap, hidden ? 1u : (cursive ? 2u : 0u) | (separator ? 4u : 0u));
                if (!hidden) sources.Add(new(first, last, a, b - a, letter, gap, cursive, shown));
            }
            cursor = native.Start + native.Length;
        }
        if (cursor < projection.Text.Length) Add(cursor, projection.Text.Length, 0, 0, 1);
        return (units.ToArray(), sources.ToArray());

        void Add(int first, int last, double letter, double word, uint flags)
        {
            if (last > first) units.Add(new() { TextPosition = (uint)first, Length = (uint)(last-first),
                Spacing = (float)letter, Leading = (float)(word/2), Trailing = (float)(word/2), Flags = flags });
        }
    }

    // Measure candidate lines using the already resolved paragraph. Tracking
    // belongs only between visual neighbours on that candidate line, so a
    // prefix of a fully spaced paragraph would incorrectly keep edge halves.
    private sealed partial class CssBidiSpacingMetrics
    {
        private sealed record Cell(int First, int End, uint Row, double Left, double Natural,
            double Letter, double Word, bool Cursive, int NativeFirst, int NativeLength, bool Tab, bool Rtl,
            double Right, double RequestedWord, AutospaceUnit Autospace);
        private readonly Cell[] _cells;
        private readonly int[] _counts;
        private readonly double[] _words;
        private readonly double? _uniform;
        private readonly Dictionary<(int, int), double> _extra = [];

        internal CssBidiSpacingMetrics(CssBidiParagraph owner, CssBidiSpacingSource[] sources, double[] natural, TextBlock block)
        {
            _block = block;
            var paragraph = owner.Paragraph!; uint row = 0;
            _cells = new Cell[sources.Length]; _counts = new int[natural.Length]; _words = new double[natural.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                while (row + 1 < paragraph.Lines.Length && source.NativeFirst >= paragraph.Lines[row].Metrics.Line.TextPosition + paragraph.Lines[row].Metrics.Line.Length) row++;
                double a = paragraph.Caret(row, source.NativeFirst, false).X;
                double b = paragraph.Caret(row, source.NativeFirst + source.NativeLength, true).X;
                double width = paragraph.Selection(row, source.NativeFirst, source.NativeLength).Sum(rect => (double)rect.Width);
                double word = width > .000001 ? Math.Max(-width, source.Word) : 0;
                _cells[i] = new(source.First,source.End,row,Math.Min(a,b),width,source.Letter,word,source.Cursive,
                    source.NativeFirst, source.NativeLength, block._hasPreservedTabs && source.Rune.Value == '\t', b < a,
                    Math.Max(a,b),source.Word,block.AutospaceMetadata(source.First,source.Rune));
                _counts[source.End]++; _words[source.End] += word;
            }
            for (int i = 1; i < _counts.Length; i++) { _counts[i] += _counts[i-1]; _words[i] += _words[i-1]; }
            _hasTabs = _cells.Any(cell => cell.Tab);
            if (!block._usesAutospaceShaping && _cells.Length > 0 && _cells[0].Letter >= 0 && _cells.All(cell => !cell.Cursive && cell.Letter == _cells[0].Letter))
                _uniform = _cells[0].Letter;
        }

        internal double Extra(int start, int end, double tabOrigin = 0)
        {
            if (_block._usesAutospaceShaping) return AutospaceExtra(start,end,tabOrigin);
            if (_hasTabs) return TabExtra(start, end, tabOrigin);
            int first = FirstAt(start), last = FirstAt(end);
            if (first == last) return 0;
            if (_uniform is { } uniform && _cells[first].Row == _cells[last-1].Row)
                return _words[end]-_words[start] + uniform*Math.Max(0,_counts[end]-_counts[start]-1);
            if (_extra.TryGetValue((start,end), out double cached)) return cached;
            double extra = 0;
            foreach (var row in _cells.AsSpan(first,last-first).ToArray().Where(cell => cell.End <= end).GroupBy(cell => cell.Row))
            {
                var visual = row.OrderBy(cell => cell.Left).ToArray();
                extra += visual.Sum(cell => cell.Word);
                for (int i = 1; i < visual.Length; i++)
                {
                    var a = visual[i-1]; var b = visual[i];
                    if (a.Cursive && b.Cursive) continue;
                    extra += Math.Max(-Math.Min(a.Natural+a.Word,b.Natural+b.Word),(a.Letter+b.Letter)/2);
                }
            }
            if (_extra.Count >= 4096) _extra.Clear();
            _extra[(start,end)] = extra; return extra;
        }

        private int FirstAt(int position)
        {
            int left = 0, right = _cells.Length;
            while (left < right) { int middle = left+(right-left)/2; if (_cells[middle].First < position) left=middle+1; else right=middle; }
            return left;
        }
    }
}
