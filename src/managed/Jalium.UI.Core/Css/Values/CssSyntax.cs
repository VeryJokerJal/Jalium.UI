using System.Text;

namespace Jalium.UI.Styling;

internal static class CssSyntax
{
    public static bool IsNameStart(ReadOnlySpan<char> text, int position)
    {
        if (position >= text.Length) return false;
        var c = text[position];
        if (char.IsAsciiLetter(c) || c == '_' || c >= 0x80) return true;
        if (c == '\\') return position + 1 == text.Length || text[position + 1] is not ('\n' or '\r' or '\f');
        return c == '-' && position + 1 < text.Length && (text[position + 1] == '-' || IsNameStart(text, position + 1));
    }

    public static bool IsNameCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' || c >= 0x80;

    public static bool ReadIdentifier(ReadOnlySpan<char> text, scoped ref int position, out ReadOnlySpan<char> name, bool hash = false)
    {
        name = default;
        if (position >= text.Length || !(hash && IsNameCharacter(text[position])) && !IsNameStart(text, position)) return false;
        var start = position;
        StringBuilder? decoded = null;
        var segment = start;
        while (position < text.Length)
        {
            if (IsNameCharacter(text[position])) { position++; continue; }
            if (text[position] != '\\') break;
            var escape = position;
            if (!ReadEscape(text, ref position, out var value)) { position = escape; break; }
            decoded ??= new StringBuilder();
            decoded.Append(text[segment..escape]).Append(value);
            segment = position;
        }
        if (position == start) return false;
        if (decoded is null) name = text[start..position];
        else { decoded.Append(text[segment..position]); name = decoded.ToString().AsSpan(); }
        return true;
    }

    public static bool ReadEscape(ReadOnlySpan<char> text, scoped ref int position, out string value)
    {
        value = string.Empty;
        if (position >= text.Length || text[position] != '\\') return false;
        if (position + 1 < text.Length && text[position + 1] is '\n' or '\r' or '\f') return false;
        position++;
        if (position == text.Length) { value = "\uFFFD"; return true; }
        var codepoint = 0;
        var digits = 0;
        while (position < text.Length && digits < 6 && Hex(text[position]) is var hex && hex >= 0)
        { codepoint = codepoint * 16 + hex; position++; digits++; }
        if (digits == 0) { value = text[position++].ToString(); return true; }
        if (position < text.Length && CssTokenReader.IsCssWhitespace(text[position]))
        {
            var whitespace = text[position++];
            if (whitespace == '\r' && position < text.Length && text[position] == '\n') position++;
        }
        if (codepoint == 0 || codepoint > 0x10FFFF || codepoint is >= 0xD800 and <= 0xDFFF) codepoint = 0xFFFD;
        value = char.ConvertFromUtf32(codepoint);
        return true;
    }

    private static int Hex(char c) => c is >= '0' and <= '9' ? c - '0' : c is >= 'a' and <= 'f' ? c - 'a' + 10
        : c is >= 'A' and <= 'F' ? c - 'A' + 10 : -1;
}
