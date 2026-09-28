using System.Text;

namespace Jalium.UI.Styling;

internal static class CssStyleSheetBig5Decoder
{
    internal static string Decode(ReadOnlySpan<byte> bytes)
    {
        var result = new StringBuilder(bytes.Length);
        int leading = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            int current = bytes[i];
            if (leading != 0)
            {
                int pointer = -1;
                if (current is >= 0x40 and <= 0x7E or >= 0xA1 and <= 0xFE)
                {
                    int offset = current < 0x7F ? 0x40 : 0x62;
                    pointer = (leading - 0x81) * 157 + current - offset;
                }
                leading = 0;

                // These four pointers decode to two code points in the standard.
                switch (pointer)
                {
                    case 1133: result.Append("\u00CA\u0304"); continue;
                    case 1135: result.Append("\u00CA\u030C"); continue;
                    case 1164: result.Append("\u00EA\u0304"); continue;
                    case 1166: result.Append("\u00EA\u030C"); continue;
                }

                int codePoint = pointer < 0 ? 0 : CssStyleSheetEncodingData.Big5Index[pointer];
                if (codePoint == 0)
                {
                    result.Append('\uFFFD');
                    if (current < 0x80) i--;
                }
                else if (codePoint <= 0xFFFF) result.Append((char)codePoint);
                else result.Append(char.ConvertFromUtf32(codePoint));
                continue;
            }

            if (current < 0x80) result.Append((char)current);
            else if (current is >= 0x81 and <= 0xFE) leading = current;
            else result.Append('\uFFFD');
        }

        if (leading != 0) result.Append('\uFFFD');
        return result.ToString();
    }
}
