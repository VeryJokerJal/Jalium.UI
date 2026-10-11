using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    private bool _hasLinePaddingOverrides;

    private double GetLinePadding() => CssFlowProperties.UsedLinePadding(this);

    private void RebuildLinePadding()
    {
        var inherited = GetLinePadding();
        _hasLinePaddingOverrides = GetInlineTextRanges().Any(range =>
            CssFlowProperties.UsedLinePadding(range.Run) != inherited);
    }

    private bool HasAuthoredLinePaddingOnInlines()
    {
        foreach (var range in GetInlineTextRanges())
            if (HasAuthoredFlowValue(range.Run, CssFlowProperties.LinePaddingProperty))
                return true;
        return false;
    }

    private double GetLineStartPadding(int start, int end)
        => GetLineEdgePadding(start, end, true);

    private double GetLineEndPadding(int start, int end)
        => GetLineEdgePadding(start, end, false);

    private bool HasLinePaddingChange(int start, int end)
    {
        if (!_hasLinePaddingOverrides || end <= start) return false;
        var initial = GetLineStartPadding(start, end);
        foreach (var range in GetInlineTextRanges())
        {
            if (range.End <= start) continue;
            if (range.Start >= end) break;
            if (CssFlowProperties.UsedLinePadding(range.Run) != initial) return true;
        }
        return false;
    }

    private double GetLineEdgePadding(int start, int end, bool first)
    {
        if (!_hasLinePaddingOverrides || end <= start) return GetLinePadding();
        var index = GetLineEdgeSourceIndex(start, end, first);
        return index >= 0 ? GetLinePaddingAtSource(index) : GetLinePadding();
    }

    private int GetLineEdgeSourceIndex(int start, int end, bool first)
    {
        for (var index = first ? start : end - 1;
             first ? index < end : index >= start;
             index += first ? 1 : -1)
        {
            if (_paintOffsets is not null && _paintOffsets[index + 1] == _paintOffsets[index] &&
                _visualText[index] != '\u00AD') continue;
            return index;
        }
        return -1;
    }

    private double GetLinePaddingAtSource(int index)
    {
        var ranges = GetInlineTextRanges();
        var low = 0;
        var high = ranges.Count - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var range = ranges[middle];
            if (index < range.Start) high = middle - 1;
            else if (index >= range.End) low = middle + 1;
            else return CssFlowProperties.UsedLinePadding(range.Run);
        }
        return GetLinePadding();
    }

    private double GetLineLeftPadding(in TextLayoutLine line)
        => FlowDirection == FlowDirection.RightToLeft ? line.EndPadding : line.StartPadding;

    private double GetLineRightPadding(in TextLayoutLine line)
        => FlowDirection == FlowDirection.RightToLeft ? line.StartPadding : line.EndPadding;
}
