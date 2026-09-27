namespace Jalium.UI.Styling;

/// <summary>Checks CSS declaration-value tokens and var() argument syntax.</summary>
internal static class CssDeclarationValueSyntax
{
    internal static bool IsValid(ReadOnlySpan<char> value)
    {
        List<char>? blocks = null;
        var position = 0;
        while (position < value.Length)
        {
            var c = value[position];
            if (c is '"' or '\'')
            {
                var end = CssTokenReader.SkipString(value, position);
                if (end < value.Length && value[end] == '\n') return false;
                position = end;
                continue;
            }
            if (c == '/' && position + 1 < value.Length && value[position + 1] == '*')
            {
                position = CssTokenReader.SkipComment(value, position);
                continue;
            }
            if (c is '#' or '@')
            {
                var nameEnd = position + 1;
                if (CssSyntax.ReadIdentifier(value, ref nameEnd, out _, hash: c == '#'))
                {
                    position = nameEnd;
                    continue;
                }
            }
            if (char.IsAsciiDigit(c))
            {
                position = SkipDimension(value, position);
                continue;
            }
            if (CssSyntax.IsNameStart(value, position))
            {
                var nameEnd = position;
                if (CssSyntax.ReadIdentifier(value, ref nameEnd, out var name))
                {
                    if (IsUrl(name) && nameEnd < value.Length && value[nameEnd] == '(' &&
                        !StartsQuotedUrlFunction(value, nameEnd + 1))
                    {
                        position = nameEnd;
                        if (!SkipUrl(value, ref position)) return false;
                    }
                    else
                    {
                        if (name.Equals("var", StringComparison.OrdinalIgnoreCase) &&
                            nameEnd < value.Length && value[nameEnd] == '(' &&
                            !HasValidVariableArguments(value, nameEnd + 1)) return false;
                        if (name.Equals("if", StringComparison.OrdinalIgnoreCase) &&
                            nameEnd < value.Length && value[nameEnd] == '(')
                        {
                            var functionReader = new CssTokenReader(value[position..]);
                            if (!functionReader.TryReadFunction(out _, out var arguments) ||
                                !CssCustomProperties.HasValidIfArguments(arguments.Remaining)) return false;
                        }
                        if (name.Equals("random-item", StringComparison.OrdinalIgnoreCase) &&
                            nameEnd < value.Length && value[nameEnd] == '(')
                        {
                            var functionReader = new CssTokenReader(value[position..]);
                            if (!functionReader.TryReadFunction(out _, out var arguments) ||
                                !CssCustomProperties.HasValidRandomItemArguments(arguments.Remaining)) return false;
                        }
                        position = nameEnd;
                    }
                    continue;
                }
            }
            if (c == '\\' && CssSyntax.ReadEscape(value, ref position, out _)) continue;
            if (c is '(' or '[' or '{') (blocks ??= []).Add(c);
            else if (c is ')' or ']' or '}')
            {
                if (blocks is not { Count: > 0 } || !Matches(blocks[^1], c)) return false;
                blocks.RemoveAt(blocks.Count - 1);
            }
            else if (blocks is not { Count: > 0 } && (c is '!' or ';')) return false;
            position++;
        }

        // CSS Syntax closes open functions and simple blocks at EOF.
        return true;
    }

    private static int SkipDimension(ReadOnlySpan<char> value, int position)
    {
        while (position < value.Length && (char.IsAsciiDigit(value[position]) || value[position] == '.')) position++;
        if (CssSyntax.IsNameStart(value, position)) CssSyntax.ReadIdentifier(value, ref position, out _);
        return position;
    }

    private static bool HasValidVariableArguments(ReadOnlySpan<char> value, int position)
    {
        // The first argument is a token sequence. Its custom-property name is parsed
        // only after nested substitutions have run at computed-value time.
        var depth = 0;
        var hasToken = false;
        while (position < value.Length)
        {
            var c = value[position];
            if (c == '/' && position + 1 < value.Length && value[position + 1] == '*')
            {
                position = CssTokenReader.SkipComment(value, position);
                continue;
            }
            if (c is '"' or '\'')
            {
                hasToken = true;
                position = CssTokenReader.SkipString(value, position);
                continue;
            }
            if (c == '\\' && CssSyntax.ReadEscape(value, ref position, out _))
            {
                hasToken = true;
                continue;
            }
            if (depth == 0 && c is ',' or ')') return hasToken;
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}')
            {
                if (depth == 0) return hasToken;
                depth--;
            }
            if (!CssTokenReader.IsCssWhitespace(c)) hasToken = true;
            position++;
        }
        return hasToken;
    }

    private static bool StartsQuotedUrlFunction(ReadOnlySpan<char> value, int position)
    {
        while (position < value.Length && CssTokenReader.IsCssWhitespace(value[position])) position++;
        return position < value.Length && (value[position] is '"' or '\'');
    }

    private static bool SkipUrl(ReadOnlySpan<char> value, ref int position)
    {
        position++; // consume '('
        while (position < value.Length && CssTokenReader.IsCssWhitespace(value[position])) position++;
        while (position < value.Length)
        {
            var c = value[position];
            if (c == ')') { position++; return true; }
            if (CssTokenReader.IsCssWhitespace(c))
            {
                while (position < value.Length && CssTokenReader.IsCssWhitespace(value[position])) position++;
                if (position == value.Length) return true;
                if (value[position] == ')') { position++; return true; }
                return false;
            }
            if (c is '"' or '\'' or '(' || IsNonPrintable(c)) return false;
            if (c == '\\')
            {
                if (!CssSyntax.ReadEscape(value, ref position, out _)) return false;
                continue;
            }
            position++;
        }
        return true;
    }

    private static bool IsUrl(ReadOnlySpan<char> name)
    {
        if (name.Length != 3) return false;
        return (name[0] is 'u' or 'U') && (name[1] is 'r' or 'R') && (name[2] is 'l' or 'L');
    }

    private static bool IsNonPrintable(char c)
        => c <= '\u0008' || c == '\u000B' || c is >= '\u000E' and <= '\u001F' || c == '\u007F';

    private static bool Matches(char open, char close)
        => open == '(' && close == ')' || open == '[' && close == ']' || open == '{' && close == '}';
}
