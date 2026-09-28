using Jalium.UI.Media;
using Jalium.UI.Styling;
using System.Text;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private readonly List<WordSpacingGap> _wordSpacingGaps = [];
    private double[] _wordSpacingPrefix = [];
    private bool _hasWordSpacing;

    private void RebuildWordSpacing()
    {
        _wordSpacingGaps.Clear();
        _hasWordSpacing = false;
        var blockValue = (CssLayoutLength)GetValue(CssFlowProperties.WordSpacingProperty)!;
        var blockSpacing = blockValue.Resolve(FontSize, 0);
        var ranges = GetInlineTextRanges();
        if (!double.IsFinite(blockSpacing) || Math.Abs(blockSpacing) <= 0.001)
        {
            var hasInlineSpacing = false;
            foreach (var range in ranges)
            {
                var runSpacing = ((CssLayoutLength)range.Run.GetValue(
                    CssFlowProperties.WordSpacingProperty)!).Resolve(GetRunFontSize(range.Run), 0);
                if (!double.IsFinite(runSpacing) || Math.Abs(runSpacing) <= 0.001) continue;
                hasInlineSpacing = true;
                break;
            }
            if (!hasInlineSpacing && _autospaceReplacementWidths.Count == 0)
            {
                _wordSpacingPrefix = [0];
                return;
            }
        }
        var rangeIndex = 0;
        for (var index = 0; index < _visualText.Length;)
        {
            if (!Rune.TryGetRuneAt(_visualText, index, out var rune))
            {
                index++;
                continue;
            }
            var length = rune.Utf16SequenceLength;
            if (IsWordSeparator(rune) || _autospaceReplacementWidths.ContainsKey(index))
            {
                var replacement = _autospaceReplacementWidths.TryGetValue(index,
                    out var autospace);
                while (rangeIndex < ranges.Count && ranges[rangeIndex].End <= index)
                    rangeIndex++;
                var run = rangeIndex < ranges.Count && ranges[rangeIndex].Start <= index
                    ? ranges[rangeIndex].Run : null;
                var spacing = run is null ? blockSpacing
                    : ((CssLayoutLength)run.GetValue(CssFlowProperties.WordSpacingProperty)!)
                        .Resolve(GetRunFontSize(run), 0);
                if (double.IsFinite(spacing) &&
                    (Math.Abs(spacing) > 0.001 || replacement))
                {
                    // A separator with no advance must not acquire spacing. Clamp
                    // negative spacing so its used advance never becomes negative.
                    var natural = MeasureInlineRangeBase(index, length,
                        includeTrailingWhitespace: true, forceFragmented: true);
                    if (natural > 0 || replacement)
                    {
                        var adjustment = replacement
                            ? Math.Max(-natural, autospace.Width + spacing - natural)
                            : Math.Max(-natural, spacing);
                        if (replacement)
                            _autospaceReplacementWidths[index] = autospace with
                            {
                                UsedWidth = Math.Max(0, natural + adjustment),
                            };
                        if (Math.Abs(adjustment) > 0.001 || replacement)
                            _wordSpacingGaps.Add(new WordSpacingGap(
                                index, length, adjustment, replacement));
                    }
                }
            }
            index += length;
        }

        _hasWordSpacing = _wordSpacingGaps.Count > 0;
        _wordSpacingPrefix = new double[_wordSpacingGaps.Count + 1];
        for (var index = 0; index < _wordSpacingGaps.Count; index++)
            _wordSpacingPrefix[index + 1] =
                _wordSpacingPrefix[index] + _wordSpacingGaps[index].Adjustment;
    }

    private static bool IsWordSeparator(Rune rune) => rune.Value is
        0x0020 or 0x00A0 or 0x1361 or 0x10100 or 0x10101 or 0x1039F or 0x1091F;

    private bool HasAuthoredWordSpacingOnInlines()
    {
        foreach (var range in GetInlineTextRanges())
            if (range.Run.GetValueSourceInternal(CssFlowProperties.WordSpacingProperty)
                    .BaseValueSource != BaseValueSource.Default)
                return true;
        return false;
    }

    private int FirstWordSpacingGapAtOrAfter(int position)
    {
        var low = 0;
        var high = _wordSpacingGaps.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_wordSpacingGaps[middle].Start < position) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private double WordSpacingInRange(int start, int end)
    {
        if (!_hasWordSpacing || end <= start) return 0;
        var first = FirstWordSpacingGapAtOrAfter(start);
        var last = FirstWordSpacingGapAtOrAfter(end);
        if (last > first && _wordSpacingGaps[last - 1].End > end) last--;
        return _wordSpacingPrefix[last] - _wordSpacingPrefix[first];
    }

    private double WordSpacingBeforeSeparator(int position)
    {
        if (!_hasWordSpacing) return 0;
        var index = FirstWordSpacingGapAtOrAfter(position);
        return index < _wordSpacingGaps.Count && _wordSpacingGaps[index].Start == position
            ? _wordSpacingGaps[index].Adjustment / 2 : 0;
    }

    private void DrawWordSpacedInlineRange(DrawingContext context,
        in TextLayoutLine line, int start, int end, ref double x, double lineY)
    {
        var cursor = start;
        for (var index = FirstWordSpacingGapAtOrAfter(start);
             index < _wordSpacingGaps.Count && _wordSpacingGaps[index].End <= end;
             index++)
        {
            var gap = _wordSpacingGaps[index];
            DrawStyledInlineRangeCore(context, line, cursor, gap.Start, ref x, lineY);
            if (gap.IsReplacement && IsEdgeAutospaceReplacement(gap.Start,
                    line.StartIndex, line.StartIndex + line.Length))
            {
                cursor = gap.End;
                continue;
            }
            x += gap.Adjustment / 2;
            DrawStyledInlineRangeCore(context, line, gap.Start, gap.End, ref x, lineY);
            x += gap.Adjustment / 2;
            cursor = gap.End;
        }
        DrawStyledInlineRangeCore(context, line, cursor, end, ref x, lineY);
    }

    private readonly record struct WordSpacingGap(int Start, int Length,
        double Adjustment, bool IsReplacement = false)
    {
        internal int End => Start + Length;
    }
}
