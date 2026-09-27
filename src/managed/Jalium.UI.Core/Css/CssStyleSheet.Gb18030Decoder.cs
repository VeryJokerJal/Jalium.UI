using System.Text;

namespace Jalium.UI.Styling;

/// <summary>WHATWG GBK and gb18030 share this byte decoder.</summary>
internal static class CssStyleSheetGb18030Decoder
{
    internal static string Decode(ReadOnlySpan<byte> bytes)
    {
        var result = new StringBuilder(bytes.Length);
        int first = 0, second = 0, third = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            int current = bytes[i];
            if (third != 0)
            {
                if (current is < 0x30 or > 0x39)
                {
                    // Restore the second, third and current bytes after one error.
                    result.Append('\uFFFD');
                    first = second = third = 0;
                    i -= 3;
                    continue;
                }

                int pointer = (first - 0x81) * 12600 + (second - 0x30) * 1260 +
                    (third - 0x81) * 10 + current - 0x30;
                int codePoint = RangeCodePoint(pointer);
                if (codePoint < 0) result.Append('\uFFFD');
                else if (codePoint <= 0xFFFF) result.Append((char)codePoint);
                else result.Append(char.ConvertFromUtf32(codePoint));
                first = second = third = 0;
                continue;
            }

            if (second != 0)
            {
                if (current is >= 0x81 and <= 0xFE)
                {
                    third = current;
                    continue;
                }
                // Restore the second and current bytes after one error.
                result.Append('\uFFFD');
                first = second = 0;
                i -= 2;
                continue;
            }

            if (first != 0)
            {
                if (current is >= 0x30 and <= 0x39)
                {
                    second = current;
                    continue;
                }

                int leading = first;
                first = 0;
                if (current is >= 0x40 and <= 0x7E or >= 0x80 and <= 0xFE)
                {
                    int offset = current < 0x7F ? 0x40 : 0x41;
                    int pointer = (leading - 0x81) * 190 + current - offset;
                    result.Append(CssStyleSheetEncodingData.Gb18030Index[pointer]);
                }
                else
                {
                    result.Append('\uFFFD');
                    if (current < 0x80) i--;
                }
                continue;
            }

            if (current < 0x80) result.Append((char)current);
            else if (current == 0x80) result.Append('\u20AC');
            else if (current is >= 0x81 and <= 0xFE) first = current;
            else result.Append('\uFFFD');
        }

        if (first != 0 || second != 0 || third != 0) result.Append('\uFFFD');
        return result.ToString();
    }

    private static int RangeCodePoint(int pointer)
    {
        if (pointer is > 39419 and < 189000 or > 1237575) return -1;
        if (pointer == 7457) return 0xE7C7;

        var ranges = CssStyleSheetEncodingData.Gb18030Ranges;
        int lower = 0, upper = ranges.Length;
        while (lower < upper)
        {
            int middle = lower + (upper - lower) / 2;
            if (ranges[middle].Pointer <= pointer) lower = middle + 1;
            else upper = middle;
        }
        var range = ranges[lower - 1];
        return range.CodePoint + pointer - range.Pointer;
    }
}
