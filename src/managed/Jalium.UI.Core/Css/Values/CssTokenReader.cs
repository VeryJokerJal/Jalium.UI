using System.Globalization;

namespace Jalium.UI.Styling;

/// <summary>
/// Forward-only reader over a single CSS declaration value. All TryRead* methods skip
/// leading whitespace and leave the position untouched on failure, so callers can probe
/// alternatives by copying the struct. No exceptions are used for control flow.
/// </summary>
internal ref struct CssTokenReader
{
    private readonly ReadOnlySpan<char> _text;
    private int _pos;

    public CssTokenReader(ReadOnlySpan<char> text)
    {
        _text = text;
        _pos = 0;
    }

    public bool AtEnd
    {
        get
        {
            SkipWhitespace();
            return _pos >= _text.Length;
        }
    }

    public ReadOnlySpan<char> Remaining
    {
        get
        {
            SkipWhitespace();
            return _text.Slice(_pos);
        }
    }

    public void SkipWhitespace()
    {
        while (_pos < _text.Length && IsCssWhitespace(_text[_pos]))
        {
            _pos++;
        }
    }

    public bool TryPeekChar(out char c)
    {
        SkipWhitespace();
        if (_pos < _text.Length)
        {
            c = _text[_pos];
            return true;
        }

        c = '\0';
        return false;
    }

    /// <summary>Reads a CSS identifier. A '-' start is only an ident when not followed by a digit or '.'.</summary>
    public bool TryReadIdent(out ReadOnlySpan<char> ident)
    {
        SkipWhitespace();
        var start = _pos;
        if (start >= _text.Length || !IsIdentStart(_text, start))
        {
            ident = default;
            return false;
        }

        var end = start;
        while (end < _text.Length && IsIdentChar(_text[end]))
        {
            end++;
        }

        // An ident immediately followed by '(' is a function token, not a plain ident.
        if (end < _text.Length && _text[end] == '(')
        {
            ident = default;
            return false;
        }

        ident = _text.Slice(start, end - start);
        _pos = end;
        return true;
    }

    /// <summary>Reads a number with an optional unit suffix ('%' or a known unit ident). Unknown unit ⇒ failure.</summary>
    public bool TryReadNumber(out double value, out CssUnit unit)
    {
        SkipWhitespace();
        var start = _pos;
        var end = start;

        if (end < _text.Length && (_text[end] == '+' || _text[end] == '-'))
        {
            end++;
        }

        var digitsBefore = 0;
        while (end < _text.Length && char.IsAsciiDigit(_text[end]))
        {
            end++;
            digitsBefore++;
        }

        var digitsAfter = 0;
        if (end < _text.Length && _text[end] == '.')
        {
            var afterDot = end + 1;
            while (afterDot < _text.Length && char.IsAsciiDigit(_text[afterDot]))
            {
                afterDot++;
                digitsAfter++;
            }

            if (digitsAfter > 0)
            {
                end = afterDot;
            }
        }

        if (digitsBefore == 0 && digitsAfter == 0)
        {
            value = 0;
            unit = CssUnit.None;
            return false;
        }

        if (end < _text.Length && (_text[end] == 'e' || _text[end] == 'E'))
        {
            var expEnd = end + 1;
            if (expEnd < _text.Length && (_text[expEnd] == '+' || _text[expEnd] == '-'))
            {
                expEnd++;
            }

            var expDigits = 0;
            while (expEnd < _text.Length && char.IsAsciiDigit(_text[expEnd]))
            {
                expEnd++;
                expDigits++;
            }

            if (expDigits > 0)
            {
                end = expEnd;
            }
        }

        if (!double.TryParse(_text.Slice(start, end - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            unit = CssUnit.None;
            return false;
        }

        if (end < _text.Length && _text[end] == '%')
        {
            unit = CssUnit.Percent;
            _pos = end + 1;
            return true;
        }

        var unitStart = end;
        while (end < _text.Length && IsIdentChar(_text[end]) && _text[end] != '-')
        {
            end++;
        }

        if (!CssUnitConversion.TryMapUnit(_text.Slice(unitStart, end - unitStart), out unit))
        {
            value = 0;
            return false;
        }

        _pos = end;
        return true;
    }

    public bool TryReadLength(out CssLength length)
    {
        var probe = this;
        if (probe.TryReadNumber(out var value, out var unit))
        {
            var candidate = new CssLength(value, unit);
            if (candidate.IsLengthUnit || unit == CssUnit.Percent)
            {
                length = candidate;
                this = probe;
                return true;
            }
        }

        length = default;
        return false;
    }

    /// <summary>Reads a function token: ident '(' … balanced … ')'. Args become a nested reader.</summary>
    public bool TryReadFunction(out ReadOnlySpan<char> name, out CssTokenReader arguments)
    {
        SkipWhitespace();
        var start = _pos;
        if (start >= _text.Length || !IsIdentStart(_text, start))
        {
            name = default;
            arguments = default;
            return false;
        }

        var nameEnd = start;
        while (nameEnd < _text.Length && IsIdentChar(_text[nameEnd]))
        {
            nameEnd++;
        }

        if (nameEnd >= _text.Length || _text[nameEnd] != '(')
        {
            name = default;
            arguments = default;
            return false;
        }

        var depth = 1;
        var i = nameEnd + 1;
        while (i < _text.Length && depth > 0)
        {
            var c = _text[i];
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
            }
            else if (c == '"' || c == '\'')
            {
                i = SkipString(_text, i);
                continue;
            }

            i++;
        }

        if (depth != 0)
        {
            name = default;
            arguments = default;
            return false;
        }

        name = _text.Slice(start, nameEnd - start);
        var argStart = nameEnd + 1;
        arguments = new CssTokenReader(_text.Slice(argStart, i - 1 - argStart));
        _pos = i;
        return true;
    }

    /// <summary>Reads a hash token ('#' followed by ident/hex characters).</summary>
    public bool TryReadHash(out ReadOnlySpan<char> digits)
    {
        SkipWhitespace();
        if (_pos >= _text.Length || _text[_pos] != '#')
        {
            digits = default;
            return false;
        }

        var start = _pos + 1;
        var end = start;
        while (end < _text.Length && IsIdentChar(_text[end]))
        {
            end++;
        }

        if (end == start)
        {
            digits = default;
            return false;
        }

        digits = _text.Slice(start, end - start);
        _pos = end;
        return true;
    }

    /// <summary>Reads a quoted string. Backslash escapes yield the escaped character literally.</summary>
    public bool TryReadString(out string value)
    {
        SkipWhitespace();
        if (_pos >= _text.Length || (_text[_pos] != '"' && _text[_pos] != '\''))
        {
            value = string.Empty;
            return false;
        }

        var quote = _text[_pos];
        var i = _pos + 1;
        System.Text.StringBuilder? sb = null;
        var segmentStart = i;
        while (i < _text.Length)
        {
            var c = _text[i];
            if (c == quote)
            {
                if (sb is null)
                {
                    value = _text.Slice(segmentStart, i - segmentStart).ToString();
                }
                else
                {
                    sb.Append(_text.Slice(segmentStart, i - segmentStart));
                    value = sb.ToString();
                }

                _pos = i + 1;
                return true;
            }

            if (c == '\\' && i + 1 < _text.Length)
            {
                sb ??= new System.Text.StringBuilder();
                sb.Append(_text.Slice(segmentStart, i - segmentStart));
                sb.Append(_text[i + 1]);
                i += 2;
                segmentStart = i;
                continue;
            }

            i++;
        }

        value = string.Empty;
        return false;
    }

    public bool TryReadComma()
    {
        SkipWhitespace();
        if (_pos < _text.Length && _text[_pos] == ',')
        {
            _pos++;
            return true;
        }

        return false;
    }

    public bool TryReadSlash()
    {
        SkipWhitespace();
        if (_pos < _text.Length && _text[_pos] == '/')
        {
            _pos++;
            return true;
        }

        return false;
    }

    /// <summary>Reads the raw text up to (excluding) the next top-level comma, honoring nesting and strings.</summary>
    public bool TryReadUntilTopLevelComma(out ReadOnlySpan<char> segment)
    {
        SkipWhitespace();
        if (_pos >= _text.Length)
        {
            segment = default;
            return false;
        }

        var start = _pos;
        var depth = 0;
        var i = start;
        while (i < _text.Length)
        {
            var c = _text[i];
            if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                depth--;
            }
            else if (c == '"' || c == '\'')
            {
                i = SkipString(_text, i);
                continue;
            }
            else if (c == ',' && depth == 0)
            {
                break;
            }

            i++;
        }

        segment = _text.Slice(start, i - start).TrimEnd();
        _pos = i;
        return true;
    }

    internal static bool IsCssWhitespace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    private static bool IsIdentStart(ReadOnlySpan<char> text, int pos)
    {
        var c = text[pos];
        if (char.IsAsciiLetter(c) || c == '_' || c > 0x7F)
        {
            return true;
        }

        if (c == '-')
        {
            var next = pos + 1;
            return next < text.Length && (char.IsAsciiLetter(text[next]) || text[next] == '_' || text[next] == '-' || text[next] > 0x7F);
        }

        return false;
    }

    private static bool IsIdentChar(char c)
        => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || c > 0x7F;

    /// <summary>Advances past a quoted string starting at <paramref name="quotePos"/>; returns the index after the closing quote.</summary>
    internal static int SkipString(ReadOnlySpan<char> text, int quotePos)
    {
        var quote = text[quotePos];
        var i = quotePos + 1;
        while (i < text.Length)
        {
            if (text[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (text[i] == quote)
            {
                return i + 1;
            }

            i++;
        }

        return i;
    }
}
