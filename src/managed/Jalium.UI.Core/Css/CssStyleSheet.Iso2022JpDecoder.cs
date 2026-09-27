using System.Text;

namespace Jalium.UI.Styling;

internal static class CssStyleSheetIso2022JpDecoder
{
    private enum State { Ascii, Roman, Katakana, Leading, Trailing, EscapeStart, Escape }

    internal static string Decode(ReadOnlySpan<byte> bytes)
    {
        var result = new StringBuilder(bytes.Length);
        var state = State.Ascii;
        var outputState = State.Ascii;
        int leading = 0;
        bool output = false;

        // The final iteration supplies end-of-queue. Invalid escape sequences can
        // restore earlier bytes, including one before end-of-queue, to the input.
        for (var i = 0; i <= bytes.Length;)
        {
            int current = i == bytes.Length ? -1 : bytes[i];
            i++;
            switch (state)
            {
                case State.Ascii:
                case State.Roman:
                    if (current < 0) return result.ToString();
                    if (current == 0x1B) { state = State.EscapeStart; continue; }
                    if (current < 0x80 && current is not 0x0E and not 0x0F)
                    {
                        output = false;
                        result.Append(current == 0x5C && state == State.Roman ? '\u00A5' :
                            current == 0x7E && state == State.Roman ? '\u203E' : (char)current);
                    }
                    else { output = false; result.Append('\uFFFD'); }
                    continue;

                case State.Katakana:
                    if (current < 0) return result.ToString();
                    if (current == 0x1B) { state = State.EscapeStart; continue; }
                    output = false;
                    result.Append(current is >= 0x21 and <= 0x5F
                        ? (char)(0xFF61 - 0x21 + current) : '\uFFFD');
                    continue;

                case State.Leading:
                    if (current < 0) return result.ToString();
                    if (current == 0x1B) { state = State.EscapeStart; continue; }
                    output = false;
                    if (current is >= 0x21 and <= 0x7E)
                    {
                        leading = current;
                        state = State.Trailing;
                    }
                    else result.Append('\uFFFD');
                    continue;

                case State.Trailing:
                    if (current == 0x1B)
                    {
                        state = State.EscapeStart;
                        result.Append('\uFFFD');
                        continue;
                    }
                    state = State.Leading;
                    if (current is >= 0x21 and <= 0x7E)
                    {
                        int pointer = (leading - 0x21) * 94 + current - 0x21;
                        char value = CssStyleSheetEncodingData.Jis0208Index[pointer];
                        result.Append(value == '\0' ? '\uFFFD' : value);
                    }
                    else result.Append('\uFFFD');
                    if (current < 0) i--; // Finish after reporting the partial pair.
                    continue;

                case State.EscapeStart:
                    if (current is 0x24 or 0x28)
                    {
                        leading = current;
                        state = State.Escape;
                        continue;
                    }
                    i--; // Restore the byte after ESC, or reprocess end-of-queue.
                    output = false;
                    state = outputState;
                    result.Append('\uFFFD');
                    continue;

                case State.Escape:
                    State? next = (leading, current) switch
                    {
                        (0x28, 0x42) => State.Ascii,
                        (0x28, 0x4A) => State.Roman,
                        (0x28, 0x49) => State.Katakana,
                        (0x24, 0x40 or 0x42) => State.Leading,
                        _ => null,
                    };
                    if (next is { } selected)
                    {
                        state = outputState = selected;
                        if (output) result.Append('\uFFFD');
                        output = true;
                    }
                    else
                    {
                        // Restore the byte after ESC and then the current byte.
                        i -= 2;
                        state = outputState;
                        output = false;
                        result.Append('\uFFFD');
                    }
                    leading = 0;
                    continue;
            }
        }
        return result.ToString();
    }
}
