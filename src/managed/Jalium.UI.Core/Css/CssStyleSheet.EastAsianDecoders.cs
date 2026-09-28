using System.Text;

namespace Jalium.UI.Styling;

internal static class CssStyleSheetEastAsianDecoders
{
    internal static string DecodeEucKr(ReadOnlySpan<byte> bytes)
    {
        var result = new StringBuilder(bytes.Length);
        int leading = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            int current = bytes[i];
            if (leading != 0)
            {
                int pointer = current is >= 0x41 and <= 0xFE
                    ? (leading - 0x81) * 190 + current - 0x41 : -1;
                leading = 0;
                char value = pointer >= 0 && pointer < CssStyleSheetEncodingData.EucKrIndex.Length
                    ? CssStyleSheetEncodingData.EucKrIndex[pointer] : '\0';
                if (value != '\0') result.Append(value);
                else
                {
                    result.Append('\uFFFD');
                    if (current < 0x80) i--;
                }
                continue;
            }

            if (current < 0x80) result.Append((char)current);
            else if (current is >= 0x81 and <= 0xFE) leading = current;
            else result.Append('\uFFFD');
        }
        if (leading != 0) result.Append('\uFFFD');
        return result.ToString();
    }

    internal static string DecodeEucJp(ReadOnlySpan<byte> bytes)
    {
        var result = new StringBuilder(bytes.Length);
        int leading = 0;
        bool jis0212 = false;
        for (var i = 0; i < bytes.Length; i++)
        {
            int current = bytes[i];
            if (leading == 0x8E && current is >= 0xA1 and <= 0xDF)
            {
                leading = 0;
                result.Append((char)(0xFF61 - 0xA1 + current));
                continue;
            }
            if (leading == 0x8F && current is >= 0xA1 and <= 0xFE)
            {
                leading = current;
                jis0212 = true;
                continue;
            }
            if (leading != 0)
            {
                int previous = leading;
                leading = 0;
                char value = '\0';
                if (previous is >= 0xA1 and <= 0xFE && current is >= 0xA1 and <= 0xFE)
                {
                    int pointer = (previous - 0xA1) * 94 + current - 0xA1;
                    string table = jis0212
                        ? CssStyleSheetEncodingData.Jis0212Index
                        : CssStyleSheetEncodingData.Jis0208Index;
                    if (pointer < table.Length) value = table[pointer];
                }
                jis0212 = false;
                if (value != '\0') result.Append(value);
                else
                {
                    result.Append('\uFFFD');
                    if (current < 0x80) i--;
                }
                continue;
            }

            if (current < 0x80) result.Append((char)current);
            else if (current is 0x8E or 0x8F or >= 0xA1 and <= 0xFE) leading = current;
            else result.Append('\uFFFD');
        }
        if (leading != 0) result.Append('\uFFFD');
        return result.ToString();
    }

    internal static string DecodeShiftJis(ReadOnlySpan<byte> bytes)
    {
        var result = new StringBuilder(bytes.Length);
        int leading = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            int current = bytes[i];
            if (leading != 0)
            {
                int pointer = -1;
                if (current is >= 0x40 and <= 0x7E or >= 0x80 and <= 0xFC)
                {
                    int offset = current < 0x7F ? 0x40 : 0x41;
                    int leadingOffset = leading < 0xA0 ? 0x81 : 0xC1;
                    pointer = (leading - leadingOffset) * 188 + current - offset;
                }
                leading = 0;
                if (pointer is >= 8836 and <= 10715)
                {
                    result.Append((char)(0xE000 - 8836 + pointer));
                    continue;
                }
                char value = pointer >= 0 && pointer < CssStyleSheetEncodingData.Jis0208Index.Length
                    ? CssStyleSheetEncodingData.Jis0208Index[pointer] : '\0';
                if (value != '\0') result.Append(value);
                else
                {
                    result.Append('\uFFFD');
                    if (current < 0x80) i--;
                }
                continue;
            }

            if (current <= 0x80) result.Append((char)current);
            else if (current is >= 0xA1 and <= 0xDF)
                result.Append((char)(0xFF61 - 0xA1 + current));
            else if (current is >= 0x81 and <= 0x9F or >= 0xE0 and <= 0xFC)
                leading = current;
            else result.Append('\uFFFD');
        }
        if (leading != 0) result.Append('\uFFFD');
        return result.ToString();
    }
}
