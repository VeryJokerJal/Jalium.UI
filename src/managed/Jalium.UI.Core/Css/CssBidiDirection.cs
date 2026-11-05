using System.Text;

namespace Jalium.UI.Styling;

internal static class CssBidiDirection
{
    internal static FlowDirection? Strong(int scalar)
    {
        if ((uint)scalar > 0x10FFFF) return null;
        var ranges = CssBidiDirectionData.Ranges;
        int first = 0, last = ranges.Length - 1;
        while (first < last)
        {
            int middle = first + (last - first) / 2;
            if ((uint)scalar <= ranges[middle] >> 2) last = middle;
            else first = middle + 1;
        }
        return (ranges[first] & 3) switch
        {
            1 => FlowDirection.LeftToRight,
            2 => FlowDirection.RightToLeft,
            _ => null,
        };
    }

    // UAX #9 P2: an isolate's content cannot establish the enclosing
    // paragraph's base direction, even if its initiator is unmatched.
    internal static FlowDirection? FirstStrong(ReadOnlySpan<char> text)
    {
        int isolates = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value is >= 0x2066 and <= 0x2068) { isolates++; continue; }
            if (rune.Value == 0x2069) { if (isolates > 0) isolates--; continue; }
            if (isolates == 0 && Strong(rune.Value) is { } direction) return direction;
        }
        return null;
    }
}
