namespace Jalium.UI.Controls;

public partial class TextBlock
{
    // Jalium's Unicode text source treats NEL as a newline sequence. CSS
    // whitespace processing may collapse it; LS and PS force line boxes.
    // Keep the original characters: LS retains the bidi paragraph while
    // PS/NEL start a paragraph, as resolved by CssBidiProjection/native text.
    private static bool IsSourceSegmentBreak(char value)
        => value is '\r' or '\n' or '\f' or '\u0085';

    private static bool IsUnicodeForcedLineBreak(char value)
        => value is '\u2028' or '\u2029';

    private static bool IsTextLineBreak(char value)
        => value is '\r' or '\n' or '\u0085' || IsUnicodeForcedLineBreak(value);
}
