using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Jalium.UI.Markup;

/// <summary>
/// Caches reflection metadata (properties, fields, methods) to avoid repeated lookups.
/// </summary>
internal static class ReflectionCache
{
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> PropertyCache = new();
    private static readonly ConcurrentDictionary<(Type, string), FieldInfo?> FieldCache = new();
    private static readonly ConcurrentDictionary<(Type, string, int), MethodInfo[]> MethodCache = new();

    public static PropertyInfo? GetProperty(Type type, string name, BindingFlags flags)
        => PropertyCache.GetOrAdd((type, name), static (key, f) => key.Item1.GetProperty(key.Item2, f), flags);

    public static FieldInfo? GetField(Type type, string name, BindingFlags flags)
        => FieldCache.GetOrAdd((type, name), static (key, f) => key.Item1.GetField(key.Item2, f), flags);

    public static MethodInfo[] GetMethods(Type type, string name, int argCount, BindingFlags flags)
        => MethodCache.GetOrAdd((type, name, argCount), static (key, f) =>
            key.Item1.GetMethods(f)
                .Where(m => m.Name.Equals(key.Item2, StringComparison.Ordinal) && m.GetParameters().Length == key.Item3)
                .ToArray(), flags);
}

// ─────────────────────────────── Token Types ───────────────────────────────

internal enum RazorTokenKind
{
    // Literals
    IntLiteral, LongLiteral, DoubleLiteral, FloatLiteral, DecimalLiteral,
    StringLiteral, InterpolatedStringLiteral, CharLiteral, True, False, Null,

    // Identifiers & keywords
    Identifier,
    Var, Int, Long, Double, Float, Decimal, Bool, String, Char, Object, Dynamic,
    If, Else, For, Foreach, In, While, Do, Switch, Case, Default, Break, Continue, Return,
    Try, Catch, Finally, Throw, New, Typeof, Is, As, Nameof, Await, Async,

    // Operators
    Plus, Minus, Star, Slash, Percent,
    Equals, NotEquals, Less, Greater, LessEquals, GreaterEquals,
    And, Or, Not, QuestionQuestion, QuestionDot,
    BitwiseAnd, BitwiseOr, BitwiseXor, BitwiseNot, LeftShift, RightShift, UnsignedRightShift,
    Assign, PlusAssign, MinusAssign, StarAssign, SlashAssign, PercentAssign,
    BitwiseAndAssign, BitwiseOrAssign, BitwiseXorAssign, LeftShiftAssign, RightShiftAssign,
    QuestionQuestionAssign,
    PlusPlus, MinusMinus,
    Question, Colon, Dot, DotDot, Comma, Semicolon, Arrow, Hat,

    // Delimiters
    OpenParen, CloseParen, OpenBrace, CloseBrace, OpenBracket, CloseBracket,

    // Special
    Eof
}

internal readonly record struct RazorToken(RazorTokenKind Kind, string Value, int Position);

// ─────────────────────────────── Tokenizer ─────────────────────────────────

internal sealed class RazorTokenizer
{
    private readonly string _source;
    private int _pos;

    public RazorTokenizer(string source) { _source = source; _pos = 0; }

    public List<RazorToken> Tokenize()
    {
        var tokens = new List<RazorToken>();
        while (_pos < _source.Length)
        {
            SkipWhitespaceAndComments();
            if (_pos >= _source.Length) break;

            var token = ReadToken();
            tokens.Add(token);
        }
        tokens.Add(new RazorToken(RazorTokenKind.Eof, "", _pos));
        return tokens;
    }

    private void SkipWhitespaceAndComments()
    {
        while (_pos < _source.Length)
        {
            if (char.IsWhiteSpace(_source[_pos])) { _pos++; continue; }
            if (_pos + 1 < _source.Length && _source[_pos] == '/' && _source[_pos + 1] == '/')
            {
                _pos += 2;
                while (_pos < _source.Length && _source[_pos] != '\n') _pos++;
                continue;
            }
            if (_pos + 1 < _source.Length && _source[_pos] == '/' && _source[_pos + 1] == '*')
            {
                _pos += 2;
                while (_pos + 1 < _source.Length && !(_source[_pos] == '*' && _source[_pos + 1] == '/')) _pos++;
                if (_pos + 1 < _source.Length) _pos += 2;
                continue;
            }
            break;
        }
    }

    private RazorToken ReadToken()
    {
        var start = _pos;
        var c = _source[_pos];

        // String literals: "", $"", @"", $@"", @$"", """raw"""
        if (c == '"'
            || (c == '$' && _pos + 1 < _source.Length && (_source[_pos + 1] == '"' || _source[_pos + 1] == '@'))
            || (c == '@' && _pos + 1 < _source.Length && (_source[_pos + 1] == '"' || _source[_pos + 1] == '$')))
            return ReadStringLiteral(start);

        // Char literal
        if (c == '\'') return ReadCharLiteral(start);

        // Number
        if (char.IsDigit(c) || (c == '.' && _pos + 1 < _source.Length && char.IsDigit(_source[_pos + 1])))
            return ReadNumber(start);

        // @identifier (verbatim identifier: @class, @event)
        if (c == '@' && _pos < _source.Length && (char.IsLetter(_source[_pos]) || _source[_pos] == '_'))
            return ReadVerbatimIdentifier(start);

        // Identifier or keyword
        if (char.IsLetter(c) || c == '_') return ReadIdentifierOrKeyword(start);

        // Operators & delimiters
        return ReadOperator(start);
    }

    private RazorToken ReadStringLiteral(int start)
    {
        var isInterpolated = false;
        var isVerbatim = false;

        // Parse prefix characters: $, @, or combination $@ / @$
        while (_pos < _source.Length && _source[_pos] is '$' or '@')
        {
            if (_source[_pos] == '$') isInterpolated = true;
            else if (_source[_pos] == '@') isVerbatim = true;
            _pos++;
        }

        // Raw string literal: """..."""
        if (_pos + 1 < _source.Length && _source[_pos] == '"' && _source[_pos + 1] == '"')
        {
            _pos += 2; // skip the two extra "
            // Count opening quotes (already consumed one, plus these two = 3+)
            var quoteCount = 3;
            while (_pos < _source.Length && _source[_pos] == '"') { quoteCount++; _pos++; }
            // Skip optional newline after opening quotes
            if (_pos < _source.Length && _source[_pos] == '\r') _pos++;
            if (_pos < _source.Length && _source[_pos] == '\n') _pos++;

            var rawSb = new StringBuilder();
            while (_pos < _source.Length)
            {
                if (_source[_pos] == '"')
                {
                    // Check for closing quotes
                    var closeCount = 0;
                    var p = _pos;
                    while (p < _source.Length && _source[p] == '"') { closeCount++; p++; }
                    if (closeCount >= quoteCount) { _pos += quoteCount; break; }
                }
                if (isInterpolated && _source[_pos] == '{')
                {
                    if (_pos + 1 < _source.Length && _source[_pos + 1] == '{') { rawSb.Append('{'); _pos += 2; continue; }
                    rawSb.Append(_source[_pos]); _pos++;
                    var depth = 1;
                    while (_pos < _source.Length && depth > 0)
                    {
                        if (_source[_pos] == '{') depth++;
                        else if (_source[_pos] == '}') { depth--; if (depth == 0) { rawSb.Append('}'); _pos++; break; } }
                        rawSb.Append(_source[_pos]); _pos++;
                    }
                    continue;
                }
                rawSb.Append(_source[_pos]); _pos++;
            }
            // Trim trailing newline + indentation
            var raw = rawSb.ToString();
            if (raw.EndsWith('\n')) raw = raw[..^1];
            if (raw.EndsWith('\r')) raw = raw[..^1];
            return new RazorToken(isInterpolated ? RazorTokenKind.InterpolatedStringLiteral : RazorTokenKind.StringLiteral, raw, start);
        }

        _pos++; // skip opening "

        var sb = new StringBuilder();
        while (_pos < _source.Length)
        {
            var c = _source[_pos];
            if (isVerbatim && !isInterpolated)
            {
                if (c == '"') { if (_pos + 1 < _source.Length && _source[_pos + 1] == '"') { sb.Append('"'); _pos += 2; continue; } _pos++; break; }
                sb.Append(c); _pos++;
            }
            else if (isInterpolated)
            {
                if (c == '{')
                {
                    if (_pos + 1 < _source.Length && _source[_pos + 1] == '{') { sb.Append('{'); _pos += 2; continue; }
                    // Keep { and } as-is so they can be processed at evaluation time
                    sb.Append(c); _pos++;
                    var depth = 1;
                    while (_pos < _source.Length && depth > 0)
                    {
                        if (_source[_pos] == '{') depth++;
                        else if (_source[_pos] == '}') { depth--; if (depth == 0) { sb.Append('}'); _pos++; break; } }
                        else if (_source[_pos] == '"') { sb.Append('"'); _pos++; while (_pos < _source.Length && _source[_pos] != '"') { if (_source[_pos] == '\\') { sb.Append(_source[_pos]); _pos++; } sb.Append(_source[_pos]); _pos++; } if (_pos < _source.Length) { sb.Append('"'); _pos++; } continue; }
                        sb.Append(_source[_pos]); _pos++;
                    }
                }
                else if (c == '}' && _pos + 1 < _source.Length && _source[_pos + 1] == '}') { sb.Append('}'); _pos += 2; }
                else if (c == '"')
                {
                    if (isVerbatim && _pos + 1 < _source.Length && _source[_pos + 1] == '"') { sb.Append('"'); _pos += 2; continue; }
                    _pos++; break;
                }
                else if (!isVerbatim && c == '\\') { sb.Append(ReadEscapeSequence()); }
                else { sb.Append(c); _pos++; }
            }
            else
            {
                if (c == '\\') { sb.Append(ReadEscapeSequence()); continue; }
                if (c == '"') { _pos++; break; }
                sb.Append(c); _pos++;
            }
        }
        return new RazorToken(isInterpolated ? RazorTokenKind.InterpolatedStringLiteral : RazorTokenKind.StringLiteral, sb.ToString(), start);
    }

    private char ReadEscapeSequence()
    {
        _pos++; // skip backslash
        if (_pos >= _source.Length) return '\\';
        var c = _source[_pos++];
        return c switch
        {
            'n' => '\n', 'r' => '\r', 't' => '\t', '\\' => '\\',
            '"' => '"', '\'' => '\'', '0' => '\0', 'a' => '\a',
            'b' => '\b', 'f' => '\f', 'v' => '\v',
            'u' => ReadUnicodeEscape(4),
            'U' => ReadUnicodeEscape(8),
            'x' => ReadHexEscape(),
            _ => c
        };
    }

    private char ReadUnicodeEscape(int digits)
    {
        var hex = new char[digits];
        for (var i = 0; i < digits && _pos < _source.Length; i++)
            hex[i] = _source[_pos++];
        return (char)Convert.ToInt32(new string(hex), 16);
    }

    private char ReadHexEscape()
    {
        var sb = new StringBuilder();
        while (_pos < _source.Length && sb.Length < 4 && IsHexDigit(_source[_pos]))
            sb.Append(_source[_pos++]);
        return sb.Length > 0 ? (char)Convert.ToInt32(sb.ToString(), 16) : '\0';
    }

    private static bool IsHexDigit(char c) =>
        c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

    private RazorToken ReadCharLiteral(int start)
    {
        _pos++; // skip opening '
        char value;
        if (_pos < _source.Length && _source[_pos] == '\\')
            value = ReadEscapeSequence();
        else if (_pos < _source.Length)
        { value = _source[_pos]; _pos++; }
        else value = '\0';
        if (_pos < _source.Length && _source[_pos] == '\'') _pos++; // skip closing '
        return new RazorToken(RazorTokenKind.CharLiteral, value.ToString(), start);
    }

    private RazorToken ReadNumber(int start)
    {
        // Hex: 0x or 0X
        if (_source[_pos] == '0' && _pos + 1 < _source.Length && _source[_pos + 1] is 'x' or 'X')
        {
            _pos += 2;
            while (_pos < _source.Length && (IsHexDigit(_source[_pos]) || _source[_pos] == '_')) _pos++;
            var hexStr = _source[(start + 2).._pos].Replace("_", "");
            var suffix = ReadIntSuffix();
            var hexVal = Convert.ToInt64(hexStr, 16);
            return suffix == RazorTokenKind.LongLiteral
                ? new RazorToken(RazorTokenKind.LongLiteral, hexVal.ToString() + "L", start)
                : new RazorToken(RazorTokenKind.IntLiteral, ((int)hexVal).ToString(), start);
        }

        // Binary: 0b or 0B
        if (_source[_pos] == '0' && _pos + 1 < _source.Length && _source[_pos + 1] is 'b' or 'B')
        {
            _pos += 2;
            while (_pos < _source.Length && (_source[_pos] is '0' or '1' || _source[_pos] == '_')) _pos++;
            var binStr = _source[(start + 2).._pos].Replace("_", "");
            var suffix = ReadIntSuffix();
            var binVal = Convert.ToInt64(binStr, 2);
            return suffix == RazorTokenKind.LongLiteral
                ? new RazorToken(RazorTokenKind.LongLiteral, binVal.ToString() + "L", start)
                : new RazorToken(RazorTokenKind.IntLiteral, ((int)binVal).ToString(), start);
        }

        // Decimal number (with optional _, decimal point, exponent)
        var hasDecimal = false;
        while (_pos < _source.Length && (char.IsDigit(_source[_pos]) || _source[_pos] == '.' || _source[_pos] == '_'))
        {
            if (_source[_pos] == '_') { _pos++; continue; }
            if (_source[_pos] == '.')
            {
                // Check it's not .. (range operator)
                if (_pos + 1 < _source.Length && _source[_pos + 1] == '.') break;
                if (hasDecimal) break;
                hasDecimal = true;
            }
            _pos++;
        }

        // Exponent: e or E
        if (_pos < _source.Length && _source[_pos] is 'e' or 'E')
        {
            hasDecimal = true;
            _pos++;
            if (_pos < _source.Length && _source[_pos] is '+' or '-') _pos++;
            while (_pos < _source.Length && (char.IsDigit(_source[_pos]) || _source[_pos] == '_')) _pos++;
        }

        var numSuffix = RazorTokenKind.IntLiteral;
        if (_pos < _source.Length)
        {
            var s = char.ToLower(_source[_pos]);
            if (s == 'l') { numSuffix = RazorTokenKind.LongLiteral; _pos++; }
            else if (s == 'u') { _pos++; if (_pos < _source.Length && char.ToLower(_source[_pos]) == 'l') _pos++; } // ulong
            else if (s == 'f') { numSuffix = RazorTokenKind.FloatLiteral; _pos++; }
            else if (s == 'd') { numSuffix = RazorTokenKind.DoubleLiteral; _pos++; }
            else if (s == 'm') { numSuffix = RazorTokenKind.DecimalLiteral; _pos++; }
            else if (hasDecimal) numSuffix = RazorTokenKind.DoubleLiteral;
        }
        else if (hasDecimal) numSuffix = RazorTokenKind.DoubleLiteral;

        // Strip underscores from the value for parsing
        var numText = _source[start.._pos].Replace("_", "");
        return new RazorToken(numSuffix, numText, start);
    }

    private RazorTokenKind ReadIntSuffix()
    {
        if (_pos < _source.Length && char.ToLower(_source[_pos]) == 'l') { _pos++; return RazorTokenKind.LongLiteral; }
        if (_pos < _source.Length && char.ToLower(_source[_pos]) == 'u') { _pos++; if (_pos < _source.Length && char.ToLower(_source[_pos]) == 'l') _pos++; }
        return RazorTokenKind.IntLiteral;
    }

    private RazorToken ReadIdentifierOrKeyword(int start)
    {
        while (_pos < _source.Length && (char.IsLetterOrDigit(_source[_pos]) || _source[_pos] == '_')) _pos++;
        var text = _source[start.._pos];
        var kind = text switch
        {
            "true" => RazorTokenKind.True, "false" => RazorTokenKind.False, "null" => RazorTokenKind.Null,
            "var" => RazorTokenKind.Var, "int" => RazorTokenKind.Int, "long" => RazorTokenKind.Long,
            "double" => RazorTokenKind.Double, "float" => RazorTokenKind.Float, "decimal" => RazorTokenKind.Decimal,
            "bool" => RazorTokenKind.Bool, "string" => RazorTokenKind.String, "char" => RazorTokenKind.Char,
            "object" => RazorTokenKind.Object, "dynamic" => RazorTokenKind.Dynamic,
            "if" => RazorTokenKind.If, "else" => RazorTokenKind.Else,
            "for" => RazorTokenKind.For, "foreach" => RazorTokenKind.Foreach, "in" => RazorTokenKind.In,
            "while" => RazorTokenKind.While, "do" => RazorTokenKind.Do,
            "switch" => RazorTokenKind.Switch, "case" => RazorTokenKind.Case, "default" => RazorTokenKind.Default,
            "break" => RazorTokenKind.Break, "continue" => RazorTokenKind.Continue, "return" => RazorTokenKind.Return,
            "try" => RazorTokenKind.Try, "catch" => RazorTokenKind.Catch, "finally" => RazorTokenKind.Finally,
            "throw" => RazorTokenKind.Throw,
            "new" => RazorTokenKind.New, "typeof" => RazorTokenKind.Typeof,
            "is" => RazorTokenKind.Is, "as" => RazorTokenKind.As, "nameof" => RazorTokenKind.Nameof,
            "await" => RazorTokenKind.Await, "async" => RazorTokenKind.Async,
            _ => RazorTokenKind.Identifier
        };
        return new RazorToken(kind, text, start);
    }

    private RazorToken ReadVerbatimIdentifier(int start)
    {
        // _pos is already past '@', read the identifier
        while (_pos < _source.Length && (char.IsLetterOrDigit(_source[_pos]) || _source[_pos] == '_')) _pos++;
        var text = _source[(start + 1).._pos]; // strip the @
        return new RazorToken(RazorTokenKind.Identifier, text, start);
    }

    private RazorToken ReadOperator(int start)
    {
        var c = _source[_pos++];
        var next = _pos < _source.Length ? _source[_pos] : '\0';

        return c switch
        {
            '+' when next == '+' => Advance(RazorTokenKind.PlusPlus, "++"),
            '+' when next == '=' => Advance(RazorTokenKind.PlusAssign, "+="),
            '+' => new RazorToken(RazorTokenKind.Plus, "+", start),
            '-' when next == '-' => Advance(RazorTokenKind.MinusMinus, "--"),
            '-' when next == '=' => Advance(RazorTokenKind.MinusAssign, "-="),
            '-' when next == '>' => Advance(RazorTokenKind.Arrow, "=>"),
            '-' => new RazorToken(RazorTokenKind.Minus, "-", start),
            '*' when next == '=' => Advance(RazorTokenKind.StarAssign, "*="),
            '*' => new RazorToken(RazorTokenKind.Star, "*", start),
            '/' when next == '=' => Advance(RazorTokenKind.SlashAssign, "/="),
            '/' => new RazorToken(RazorTokenKind.Slash, "/", start),
            '%' when next == '=' => Advance(RazorTokenKind.PercentAssign, "%="),
            '%' => new RazorToken(RazorTokenKind.Percent, "%", start),
            '=' when next == '=' => Advance(RazorTokenKind.Equals, "=="),
            '=' when next == '>' => Advance(RazorTokenKind.Arrow, "=>"),
            '=' => new RazorToken(RazorTokenKind.Assign, "=", start),
            '!' when next == '=' => Advance(RazorTokenKind.NotEquals, "!="),
            '!' => new RazorToken(RazorTokenKind.Not, "!", start),
            '<' when next == '<' => AdvanceCheck(RazorTokenKind.LeftShift, RazorTokenKind.LeftShiftAssign, "<<", "<<=", start),
            '<' when next == '=' => Advance(RazorTokenKind.LessEquals, "<="),
            '<' => new RazorToken(RazorTokenKind.Less, "<", start),
            '>' when next == '>' && _pos + 1 < _source.Length && _source[_pos + 1] == '>' => AdvanceDouble(RazorTokenKind.UnsignedRightShift, ">>>", start),
            '>' when next == '>' => AdvanceCheck(RazorTokenKind.RightShift, RazorTokenKind.RightShiftAssign, ">>", ">>=", start),
            '>' when next == '=' => Advance(RazorTokenKind.GreaterEquals, ">="),
            '>' => new RazorToken(RazorTokenKind.Greater, ">", start),
            '&' when next == '&' => Advance(RazorTokenKind.And, "&&"),
            '&' when next == '=' => Advance(RazorTokenKind.BitwiseAndAssign, "&="),
            '&' => new RazorToken(RazorTokenKind.BitwiseAnd, "&", start),
            '|' when next == '|' => Advance(RazorTokenKind.Or, "||"),
            '|' when next == '=' => Advance(RazorTokenKind.BitwiseOrAssign, "|="),
            '|' => new RazorToken(RazorTokenKind.BitwiseOr, "|", start),
            '^' when next == '=' => Advance(RazorTokenKind.BitwiseXorAssign, "^="),
            '^' => new RazorToken(RazorTokenKind.Hat, "^", start),
            '~' => new RazorToken(RazorTokenKind.BitwiseNot, "~", start),
            '?' when next == '?' && _pos + 1 < _source.Length && _source[_pos + 1] == '=' => AdvanceDouble(RazorTokenKind.QuestionQuestionAssign, "??=", start),
            '?' when next == '?' => Advance(RazorTokenKind.QuestionQuestion, "??"),
            '?' when next == '.' => Advance(RazorTokenKind.QuestionDot, "?."),
            '?' => new RazorToken(RazorTokenKind.Question, "?", start),
            ':' => new RazorToken(RazorTokenKind.Colon, ":", start),
            '.' when next == '.' => Advance(RazorTokenKind.DotDot, ".."),
            '.' => new RazorToken(RazorTokenKind.Dot, ".", start),
            ',' => new RazorToken(RazorTokenKind.Comma, ",", start),
            ';' => new RazorToken(RazorTokenKind.Semicolon, ";", start),
            '(' => new RazorToken(RazorTokenKind.OpenParen, "(", start),
            ')' => new RazorToken(RazorTokenKind.CloseParen, ")", start),
            '{' => new RazorToken(RazorTokenKind.OpenBrace, "{", start),
            '}' => new RazorToken(RazorTokenKind.CloseBrace, "}", start),
            '[' => new RazorToken(RazorTokenKind.OpenBracket, "[", start),
            ']' => new RazorToken(RazorTokenKind.CloseBracket, "]", start),
            _ => new RazorToken(RazorTokenKind.Identifier, c.ToString(), start)
        };
    }

    private RazorToken Advance(RazorTokenKind kind, string text) { _pos++; return new RazorToken(kind, text, _pos - text.Length); }
    private RazorToken AdvanceDouble(RazorTokenKind kind, string text, int start) { _pos += 2; return new RazorToken(kind, text, start); }
    private RazorToken AdvanceCheck(RazorTokenKind kind2, RazorTokenKind kind3, string text2, string text3, int start)
    {
        _pos++; // consume second char
        if (_pos < _source.Length && _source[_pos] == '=') { _pos++; return new RazorToken(kind3, text3, start); }
        return new RazorToken(kind2, text2, start);
    }
}

// ──────────────────────────── Expression Parser ────────────────────────────

internal sealed class RazorExpressionParser
{
    private readonly List<RazorToken> _tokens;
    private int _pos;
    // Pattern variables from `is Type varName` — injected into resolver
    private Dictionary<string, object?>? _patternVars;

    public RazorExpressionParser(List<RazorToken> tokens, int startPos = 0)
    {
        _tokens = tokens;
        _pos = startPos;
    }

    public int Position => _pos;
    private RazorToken Current => _pos < _tokens.Count ? _tokens[_pos] : new RazorToken(RazorTokenKind.Eof, "", 0);
    private RazorToken Peek(int offset = 1) => _pos + offset < _tokens.Count ? _tokens[_pos + offset] : new RazorToken(RazorTokenKind.Eof, "", 0);
    private RazorToken Consume() => _pos < _tokens.Count ? _tokens[_pos++] : new RazorToken(RazorTokenKind.Eof, "", 0);
    private RazorToken Expect(RazorTokenKind kind)
    {
        if (Current.Kind != kind)
            throw new XamlParseException($"Expected {kind} but got {Current.Kind} '{Current.Value}' at position {Current.Position}");
        return Consume();
    }
    private bool Match(RazorTokenKind kind) { if (Current.Kind == kind) { _pos++; return true; } return false; }

    // ── Entry point ──

    public object? ParseAndEvaluate(Func<string, object?> resolver)
    {
        // Wrap resolver to include pattern variables (from `is Type varName`)
        object? WrappedResolver(string name)
        {
            if (_patternVars != null && _patternVars.TryGetValue(name, out var pv)) return pv;
            return resolver(name);
        }
        var value = EvalExpression(WrappedResolver);
        return value;
    }

    // ── Recursive descent expression parser with inline evaluation ──
    // Instead of building an AST, we evaluate inline for simplicity and performance.

    public object? EvalExpression(Func<string, object?> resolver) => EvalTernary(resolver);

    private object? EvalTernary(Func<string, object?> resolver)
    {
        var value = EvalNullCoalesce(resolver);
        if (Match(RazorTokenKind.Question))
        {
            var trueVal = EvalExpression(resolver);
            Expect(RazorTokenKind.Colon);
            var falseVal = EvalExpression(resolver);
            return IsTruthy(value) ? trueVal : falseVal;
        }
        return value;
    }

    private object? EvalNullCoalesce(Func<string, object?> resolver)
    {
        var value = EvalOr(resolver);
        while (Match(RazorTokenKind.QuestionQuestion))
        {
            // throw expression: x ?? throw new Exception(...)
            if (Current.Kind == RazorTokenKind.Throw)
            {
                if (value != null) { SkipThrowExpression(); continue; }
                EvalThrowExpression(resolver);
            }
            if (value != null) { EvalOr(resolver); continue; }
            value = EvalOr(resolver);
        }
        return value;
    }

    private void SkipThrowExpression()
    {
        Consume(); // throw
        // Skip to end of expression
        var depth = 0;
        while (Current.Kind != RazorTokenKind.Eof)
        {
            if (Current.Kind is RazorTokenKind.OpenParen or RazorTokenKind.OpenBracket) depth++;
            else if (Current.Kind is RazorTokenKind.CloseParen or RazorTokenKind.CloseBracket) { if (depth == 0) break; depth--; }
            else if (Current.Kind is RazorTokenKind.Comma or RazorTokenKind.Semicolon && depth == 0) break;
            Consume();
        }
    }

    private void EvalThrowExpression(Func<string, object?> resolver)
    {
        Consume(); // throw
        var ex = EvalExpression(resolver);
        if (ex is Exception exception) throw exception;
        throw new InvalidOperationException(ex?.ToString() ?? "throw expression evaluated to null");
    }

    private object? EvalOr(Func<string, object?> resolver)
    {
        var value = EvalAnd(resolver);
        while (Match(RazorTokenKind.Or))
        { var right = EvalAnd(resolver); value = IsTruthy(value) || IsTruthy(right); }
        return value;
    }

    private object? EvalAnd(Func<string, object?> resolver)
    {
        var value = EvalBitwiseOr(resolver);
        while (Match(RazorTokenKind.And))
        { var right = EvalBitwiseOr(resolver); value = IsTruthy(value) && IsTruthy(right); }
        return value;
    }

    private object? EvalBitwiseOr(Func<string, object?> resolver)
    {
        var value = EvalBitwiseXor(resolver);
        while (Match(RazorTokenKind.BitwiseOr))
        { var r = EvalBitwiseXor(resolver); value = BitwiseOp(value, r, '|'); }
        return value;
    }

    private object? EvalBitwiseXor(Func<string, object?> resolver)
    {
        var value = EvalBitwiseAnd(resolver);
        while (Match(RazorTokenKind.Hat))
        { var r = EvalBitwiseAnd(resolver); value = BitwiseOp(value, r, '^'); }
        return value;
    }

    private object? EvalBitwiseAnd(Func<string, object?> resolver)
    {
        var value = EvalEquality(resolver);
        while (Match(RazorTokenKind.BitwiseAnd))
        { var r = EvalEquality(resolver); value = BitwiseOp(value, r, '&'); }
        return value;
    }

    private object? EvalEquality(Func<string, object?> resolver)
    {
        var value = EvalComparison(resolver);
        while (true)
        {
            if (Match(RazorTokenKind.Equals)) { var r = EvalComparison(resolver); value = Equals(value, r); }
            else if (Match(RazorTokenKind.NotEquals)) { var r = EvalComparison(resolver); value = !Equals(value, r); }
            else break;
        }
        return value;
    }

    private object? EvalComparison(Func<string, object?> resolver)
    {
        var value = EvalShift(resolver);
        while (true)
        {
            if (Match(RazorTokenKind.Less)) { var r = EvalShift(resolver); value = Compare(value, r) < 0; }
            else if (Match(RazorTokenKind.Greater)) { var r = EvalShift(resolver); value = Compare(value, r) > 0; }
            else if (Match(RazorTokenKind.LessEquals)) { var r = EvalShift(resolver); value = Compare(value, r) <= 0; }
            else if (Match(RazorTokenKind.GreaterEquals)) { var r = EvalShift(resolver); value = Compare(value, r) >= 0; }
            else if (Current.Kind == RazorTokenKind.Is) { Consume(); value = EvalIsExpression(value, resolver); }
            else if (Current.Kind == RazorTokenKind.As) { Consume(); var type = ResolveTypeName(); value = type != null && type.IsInstanceOfType(value!) ? value : null; }
            else break;
        }
        return value;
    }

    private object? EvalShift(Func<string, object?> resolver)
    {
        var value = EvalAdditive(resolver);
        while (true)
        {
            if (Match(RazorTokenKind.LeftShift)) { var r = EvalAdditive(resolver); value = BitwiseOp(value, r, '<'); }
            else if (Match(RazorTokenKind.UnsignedRightShift)) { var r = EvalAdditive(resolver); value = UnsignedRightShiftOp(value, r); }
            else if (Match(RazorTokenKind.RightShift)) { var r = EvalAdditive(resolver); value = BitwiseOp(value, r, '>'); }
            else break;
        }
        return value;
    }

    private object? EvalIsExpression(object? value, Func<string, object?> resolver)
    {
        var result = EvalPatternOr(value, resolver);
        return result;
    }

    // Pattern combinators: or has lowest precedence, then and
    private bool EvalPatternOr(object? value, Func<string, object?> resolver)
    {
        var result = EvalPatternAnd(value, resolver);
        while (Current.Kind == RazorTokenKind.Identifier && Current.Value == "or")
        {
            Consume();
            var right = EvalPatternAnd(value, resolver);
            result = result || right;
        }
        return result;
    }

    private bool EvalPatternAnd(object? value, Func<string, object?> resolver)
    {
        var result = EvalPatternPrimary(value, resolver);
        while (Current.Kind == RazorTokenKind.Identifier && Current.Value == "and")
        {
            Consume();
            var right = EvalPatternPrimary(value, resolver);
            result = result && right;
        }
        return result;
    }

    private bool EvalPatternPrimary(object? value, Func<string, object?> resolver)
    {
        // not pattern
        if (Current.Kind == RazorTokenKind.Not || (Current.Kind == RazorTokenKind.Identifier && Current.Value == "not"))
        {
            Consume();
            return !EvalPatternPrimary(value, resolver);
        }

        // Parenthesized pattern: is (> 0 and < 10)
        if (Current.Kind == RazorTokenKind.OpenParen)
        {
            Consume();
            var result = EvalPatternOr(value, resolver);
            Expect(RazorTokenKind.CloseParen);
            return result;
        }

        // null pattern
        if (Current.Kind == RazorTokenKind.Null)
        { Consume(); return value == null; }

        // Relational patterns: > 5, >= 0, < 10, <= 100
        if (Current.Kind is RazorTokenKind.Less or RazorTokenKind.Greater or RazorTokenKind.LessEquals or RazorTokenKind.GreaterEquals)
        {
            var op = Consume().Kind;
            var r = EvalUnary(resolver);
            var cmp = Compare(value, r);
            return op switch
            {
                RazorTokenKind.Less => cmp < 0,
                RazorTokenKind.Greater => cmp > 0,
                RazorTokenKind.LessEquals => cmp <= 0,
                RazorTokenKind.GreaterEquals => cmp >= 0,
                _ => false
            };
        }

        // Property pattern: { Prop: value, Prop.Sub: value }
        if (Current.Kind == RazorTokenKind.OpenBrace)
        {
            Consume();
            var matches = true;
            while (Current.Kind != RazorTokenKind.CloseBrace && Current.Kind != RazorTokenKind.Eof)
            {
                // Read property path (supports Prop.Sub.Deep)
                var propPath = Expect(RazorTokenKind.Identifier).Value;
                while (Match(RazorTokenKind.Dot))
                    propPath += "." + Expect(RazorTokenKind.Identifier).Value;
                Expect(RazorTokenKind.Colon);

                // Navigate the property path
                var actual = value;
                foreach (var part in propPath.Split('.'))
                    actual = GetMember(actual, part);

                // The value side can itself be a pattern (relational, nested, etc.)
                if (Current.Kind is RazorTokenKind.Less or RazorTokenKind.Greater or RazorTokenKind.LessEquals or RazorTokenKind.GreaterEquals
                    or RazorTokenKind.Not || (Current.Kind == RazorTokenKind.Identifier && Current.Value is "not" or "and" or "or"))
                {
                    if (!EvalPatternOr(actual, resolver)) matches = false;
                }
                else
                {
                    var expected = EvalExpression(resolver);
                    if (!Equals(actual, expected)) matches = false;
                }
                Match(RazorTokenKind.Comma);
            }
            if (Current.Kind == RazorTokenKind.CloseBrace) Consume();
            return matches;
        }

        // List pattern: is [1, 2, ..]
        if (Current.Kind == RazorTokenKind.OpenBracket)
        {
            Consume(); // [
            if (value is not System.Collections.IEnumerable enumerable) { SkipToCloseBracket(); return false; }
            var items = new List<object?>();
            foreach (var item in enumerable) items.Add(item);
            var idx = 0;
            var listMatch = true;
            while (Current.Kind != RazorTokenKind.CloseBracket && Current.Kind != RazorTokenKind.Eof)
            {
                // Spread/slice pattern: ..
                if (Current.Kind == RazorTokenKind.DotDot)
                {
                    Consume();
                    // Optional variable name for slice
                    if (Current.Kind == RazorTokenKind.Identifier && !IsPatternKeyword(Current.Value))
                        Consume();
                    // Spread matches remaining elements — skip to end
                    idx = items.Count;
                    Match(RazorTokenKind.Comma);
                    continue;
                }
                // Discard: _
                if (Current.Kind == RazorTokenKind.Identifier && Current.Value == "_")
                {
                    Consume();
                    idx++;
                    Match(RazorTokenKind.Comma);
                    continue;
                }
                // Match element at position
                if (idx >= items.Count) { listMatch = false; idx++; }
                else
                {
                    var elemMatch = EvalPatternOr(items[idx], resolver);
                    if (!elemMatch) listMatch = false;
                    idx++;
                }
                Match(RazorTokenKind.Comma);
            }
            Expect(RazorTokenKind.CloseBracket);
            // If no spread, element count must match exactly
            return listMatch && idx == items.Count;
        }

        // Constant pattern (literal values): is "hello", is 42, is true
        if (Current.Kind is RazorTokenKind.IntLiteral or RazorTokenKind.LongLiteral or RazorTokenKind.DoubleLiteral
            or RazorTokenKind.StringLiteral or RazorTokenKind.CharLiteral or RazorTokenKind.True or RazorTokenKind.False)
        {
            var constant = EvalPrimary(resolver);
            return Equals(value, constant);
        }

        // var pattern: is var x — always matches, captures value
        if (Current.Kind == RazorTokenKind.Var)
        {
            Consume(); // var
            if (Current.Kind == RazorTokenKind.Identifier && !IsPatternKeyword(Current.Value))
            {
                var varName = Consume().Value;
                _patternVars ??= new Dictionary<string, object?>(StringComparer.Ordinal);
                _patternVars[varName] = value;
            }
            return true; // var pattern always matches
        }

        // Positional pattern: is (expr, expr) or is Type(expr, expr)
        if (Current.Kind == RazorTokenKind.OpenParen)
        {
            Consume(); // (
            var posIdx = 0;
            var posMatch = true;
            while (Current.Kind != RazorTokenKind.CloseParen && Current.Kind != RazorTokenKind.Eof)
            {
                var itemVal = GetTupleItem(value, posIdx);
                if (!EvalPatternOr(itemVal, resolver)) posMatch = false;
                posIdx++;
                Match(RazorTokenKind.Comma);
            }
            Expect(RazorTokenKind.CloseParen);
            return posMatch;
        }

        // Type pattern: is Type / is Type varName / is Type(positional)
        var type = ResolveTypeName();
        var isMatch = type != null && value != null && type.IsInstanceOfType(value);

        // Positional pattern on type: is Point(> 0, > 0)
        if (isMatch && Current.Kind == RazorTokenKind.OpenParen)
        {
            Consume();
            var posIdx2 = 0;
            while (Current.Kind != RazorTokenKind.CloseParen && Current.Kind != RazorTokenKind.Eof)
            {
                var itemVal = GetTupleItem(value, posIdx2);
                if (!EvalPatternOr(itemVal, resolver)) isMatch = false;
                posIdx2++;
                Match(RazorTokenKind.Comma);
            }
            Expect(RazorTokenKind.CloseParen);
        }

        // Recursive property pattern on type: is Type { Prop: val }
        if (isMatch && Current.Kind == RazorTokenKind.OpenBrace)
        {
            Consume();
            while (Current.Kind != RazorTokenKind.CloseBrace && Current.Kind != RazorTokenKind.Eof)
            {
                var propPath = Expect(RazorTokenKind.Identifier).Value;
                while (Match(RazorTokenKind.Dot))
                    propPath += "." + Expect(RazorTokenKind.Identifier).Value;
                Expect(RazorTokenKind.Colon);
                var actual = value;
                foreach (var part in propPath.Split('.'))
                    actual = GetMember(actual, part);
                if (!EvalPatternOr(actual, resolver)) isMatch = false;
                Match(RazorTokenKind.Comma);
            }
            if (Current.Kind == RazorTokenKind.CloseBrace) Consume();
        }

        // Pattern variable: is string s — store s in pattern vars
        if (Current.Kind == RazorTokenKind.Identifier && !IsPatternKeyword(Current.Value))
        {
            var varName = Consume().Value;
            if (isMatch)
            {
                _patternVars ??= new Dictionary<string, object?>(StringComparer.Ordinal);
                _patternVars[varName] = value;
            }
        }
        return isMatch;
    }

    private static bool IsPatternKeyword(string value) =>
        value is "and" or "or" or "not" or "when";

    private static object? GetTupleItem(object? tuple, int index)
    {
        if (tuple == null) return null;
        var type = tuple.GetType();
        // ValueTuple fields: Item1, Item2, ...
        var field = type.GetField($"Item{index + 1}");
        if (field != null) return field.GetValue(tuple);
        // IList/array indexer
        if (tuple is System.Collections.IList list && index < list.Count) return list[index];
        return null;
    }

    private void SkipToCloseBracket()
    {
        var depth = 1;
        while (_pos < _tokens.Count && depth > 0)
        {
            if (Current.Kind == RazorTokenKind.OpenBracket) depth++;
            else if (Current.Kind == RazorTokenKind.CloseBracket) { depth--; if (depth == 0) { Consume(); return; } }
            Consume();
        }
    }

    private object? EvalAdditive(Func<string, object?> resolver)
    {
        var value = EvalMultiplicative(resolver);
        while (true)
        {
            if (Match(RazorTokenKind.Plus))
            {
                var r = EvalMultiplicative(resolver);
                if (value is string || r is string) value = $"{value}{r}";
                else value = ArithmeticOp(value, r, '+');
            }
            else if (Match(RazorTokenKind.Minus)) { var r = EvalMultiplicative(resolver); value = ArithmeticOp(value, r, '-'); }
            else break;
        }
        return value;
    }

    private object? EvalMultiplicative(Func<string, object?> resolver)
    {
        var value = EvalUnary(resolver);
        while (true)
        {
            if (Match(RazorTokenKind.Star)) { var r = EvalUnary(resolver); value = ArithmeticOp(value, r, '*'); }
            else if (Match(RazorTokenKind.Slash)) { var r = EvalUnary(resolver); value = ArithmeticOp(value, r, '/'); }
            else if (Match(RazorTokenKind.Percent)) { var r = EvalUnary(resolver); value = ArithmeticOp(value, r, '%'); }
            else break;
        }
        return value;
    }

    private object? EvalUnary(Func<string, object?> resolver)
    {
        // await expression: synchronously unwrap Task/ValueTask
        if (Match(RazorTokenKind.Await))
        {
            var v = EvalUnary(resolver);
            return UnwrapAwaitable(v);
        }
        if (Match(RazorTokenKind.Not))
        {
            // null-forgiving (x!) — if next token is not an expression start, treat as postfix !
            // otherwise treat as logical NOT
            return !IsTruthy(EvalUnary(resolver));
        }
        if (Match(RazorTokenKind.Minus))
        {
            var v = EvalUnary(resolver);
            if (v is int i) return -i;
            if (v is long l) return -l;
            if (v is double d) return -d;
            if (v is float f) return -f;
            if (v is decimal m) return -m;
            return ArithmeticOp(0, v, '-');
        }
        if (Match(RazorTokenKind.Plus)) return EvalUnary(resolver); // unary +
        if (Match(RazorTokenKind.BitwiseNot))
        {
            var v = EvalUnary(resolver);
            if (v is int i) return ~i;
            if (v is long l) return ~l;
            return v;
        }
        // Index from end: ^expr
        if (Match(RazorTokenKind.Hat))
        {
            var v = EvalUnary(resolver);
            if (v is int idx) return new Index(idx, true);
            return v;
        }

        // Cast expression: (Type)expr
        if (Current.Kind == RazorTokenKind.OpenParen && IsTypeName(Peek()))
        {
            var saved = _pos;
            Consume(); // (
            var type = ResolveTypeName();
            if (Current.Kind == RazorTokenKind.CloseParen && type != null)
            {
                Consume(); // )
                var val = EvalUnary(resolver);
                try { return Convert.ChangeType(val, type, CultureInfo.InvariantCulture); }
                catch { return val; }
            }
            _pos = saved; // backtrack
        }

        return EvalPostfix(resolver);
    }

    private object? EvalPostfix(Func<string, object?> resolver)
    {
        var value = EvalPrimary(resolver);

        while (true)
        {
            if (Match(RazorTokenKind.Dot))
            {
                var member = Expect(RazorTokenKind.Identifier).Value;
                if (value is Type staticType)
                {
                    if (Current.Kind == RazorTokenKind.OpenParen)
                    {
                        // Try instance method first (e.g. Type.GetMethod()), then static
                        var instanceResult = EvalMethodCall(value, member, resolver);
                        value = instanceResult ?? EvalStaticMethodCall(staticType, member, resolver);
                    }
                    else
                    {
                        // Try instance member first (e.g. GetType().Name), then static
                        var instanceResult = GetMember(value, member);
                        value = instanceResult ?? GetStaticMember(staticType, member);
                    }
                }
                else if (Current.Kind == RazorTokenKind.OpenParen || Current.Kind == RazorTokenKind.Less)
                {
                    var beforeSkip = _pos;
                    SkipGenericArgs(); // skip <T, U> if present
                    if (Current.Kind == RazorTokenKind.OpenParen)
                        value = EvalMethodCall(value, member, resolver);
                    else
                    {
                        _pos = beforeSkip; // restore — was comparison, not generic
                        value = GetMember(value, member);
                    }
                }
                else
                    value = GetMember(value, member);
            }
            else if (Match(RazorTokenKind.QuestionDot))
            {
                if (value == null) { SkipMemberChain(); return null; }
                var member = Expect(RazorTokenKind.Identifier).Value;
                SkipGenericArgs();
                if (Current.Kind == RazorTokenKind.OpenParen)
                    value = EvalMethodCall(value, member, resolver);
                else
                    value = GetMember(value, member);
            }
            else if (Current.Kind == RazorTokenKind.OpenBracket)
            {
                Consume();
                var index = EvalExpression(resolver);
                Expect(RazorTokenKind.CloseBracket);
                value = GetIndexer(value, index);
            }
            else if (Current.Kind == RazorTokenKind.Question && Peek().Kind == RazorTokenKind.OpenBracket)
            {
                // Conditional indexer: x?[i]
                Consume(); // ?
                if (value == null) { Consume(); EvalExpression(resolver); Expect(RazorTokenKind.CloseBracket); SkipMemberChain(); return null; }
                Consume(); // [
                var index = EvalExpression(resolver);
                Expect(RazorTokenKind.CloseBracket);
                value = GetIndexer(value, index);
            }
            else if (Current.Kind == RazorTokenKind.PlusPlus)
            {
                Consume();
                // Post-increment: return current value, actual mutation handled by caller
                return value;
            }
            else if (Current.Kind == RazorTokenKind.MinusMinus)
            {
                Consume();
                return value;
            }
            else if (Current.Kind == RazorTokenKind.Not)
            {
                // Null-forgiving operator: x! — just consume and return value as-is
                Consume();
            }
            else if (Current.Kind == RazorTokenKind.DotDot)
            {
                // Range operator: start..end
                Consume();
                object? end = Current.Kind is RazorTokenKind.CloseBracket or RazorTokenKind.CloseParen or RazorTokenKind.Comma or RazorTokenKind.Eof
                    ? null
                    : EvalUnary(resolver);
                var startIdx = value is int si ? new Index(si) : Index.Start;
                var endIdx = end is int ei ? new Index(ei) : (end is Index endIndex ? endIndex : Index.End);
                return new Range(startIdx, endIdx);
            }
            else if (Current.Kind == RazorTokenKind.Switch)
            {
                // Switch expression: value switch { pattern => result, ... }
                Consume(); // switch
                value = EvalSwitchExpression(value, resolver);
            }
            else if (Current.Kind == RazorTokenKind.Identifier && Current.Value == "with" && Peek().Kind == RazorTokenKind.OpenBrace)
            {
                // with expression: record with { Prop = val }
                Consume(); // with
                Consume(); // {
                // Clone via MemberwiseClone (works for records and POCOs)
                var cloneMethod = value?.GetType().GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
                var clone = cloneMethod?.Invoke(value, null) ?? value;
                var cloneType = clone?.GetType();
                while (Current.Kind != RazorTokenKind.CloseBrace && Current.Kind != RazorTokenKind.Eof)
                {
                    var prop = Expect(RazorTokenKind.Identifier).Value;
                    Expect(RazorTokenKind.Assign);
                    var propVal = EvalExpression(resolver);
                    cloneType?.GetProperty(prop)?.SetValue(clone, propVal);
                    Match(RazorTokenKind.Comma);
                }
                Expect(RazorTokenKind.CloseBrace);
                value = clone;
            }
            else break;
        }
        return value;
    }

    private object? EvalSwitchExpression(object? input, Func<string, object?> resolver)
    {
        Expect(RazorTokenKind.OpenBrace);
        object? result = null;
        var matched = false;

        while (Current.Kind != RazorTokenKind.CloseBrace && Current.Kind != RazorTokenKind.Eof)
        {
            if (matched)
            {
                // Skip remaining arms
                SkipSwitchArm();
                Match(RazorTokenKind.Comma);
                continue;
            }

            // Discard pattern: _ => result / _ when cond => result
            if (Current.Kind == RazorTokenKind.Identifier && Current.Value == "_"
                && (Peek().Kind is RazorTokenKind.Arrow || Peek().Value == "when"))
            {
                Consume(); // _
                var guardOk = EvalWhenGuard(resolver);
                Expect(RazorTokenKind.Arrow);
                var armResult = EvalExpression(resolver);
                if (guardOk) { result = armResult; matched = true; }
                Match(RazorTokenKind.Comma);
                continue;
            }

            // null pattern
            if (Current.Kind == RazorTokenKind.Null
                && (Peek().Kind is RazorTokenKind.Arrow || Peek().Value == "when"))
            {
                Consume();
                var guardOk = EvalWhenGuard(resolver);
                Expect(RazorTokenKind.Arrow);
                var armResult = EvalExpression(resolver);
                if (input == null && guardOk) { result = armResult; matched = true; }
                Match(RazorTokenKind.Comma);
                continue;
            }

            // Pattern-based arms: relational, property, type, constant
            // Try to evaluate as pattern first
            bool armMatched;
            if (Current.Kind is RazorTokenKind.Less or RazorTokenKind.Greater or RazorTokenKind.LessEquals
                or RazorTokenKind.GreaterEquals or RazorTokenKind.OpenBrace or RazorTokenKind.Not
                || (Current.Kind == RazorTokenKind.Identifier && Current.Value is "not" or "and" or "or"))
            {
                armMatched = EvalPatternOr(input, resolver);
            }
            else
            {
                // Type or constant pattern
                var pattern = EvalExpression(resolver);
                armMatched = pattern is Type t ? (input != null && t.IsInstanceOfType(input)) : Equals(input, pattern);
            }

            // Check for optional when guard
            var guardResult = EvalWhenGuard(resolver);

            if (Current.Kind == RazorTokenKind.Arrow)
            {
                Consume(); // =>
                var armResult = EvalExpression(resolver);
                if (!matched && armMatched && guardResult)
                { result = armResult; matched = true; }
            }
            else
            {
                SkipSwitchArm();
            }
            Match(RazorTokenKind.Comma);
        }

        Expect(RazorTokenKind.CloseBrace);
        return result;
    }

    private bool EvalWhenGuard(Func<string, object?> resolver)
    {
        if (Current.Kind != RazorTokenKind.Identifier || Current.Value != "when") return true;
        Consume(); // when
        var cond = EvalExpression(resolver);
        return IsTruthy(cond);
    }

    private void SkipSwitchArm()
    {
        var depth = 0;
        while (Current.Kind != RazorTokenKind.Eof)
        {
            if (Current.Kind is RazorTokenKind.OpenParen or RazorTokenKind.OpenBracket or RazorTokenKind.OpenBrace) depth++;
            else if (Current.Kind is RazorTokenKind.CloseParen or RazorTokenKind.CloseBracket) depth--;
            else if (Current.Kind == RazorTokenKind.CloseBrace) { if (depth == 0) break; depth--; }
            else if (Current.Kind == RazorTokenKind.Comma && depth == 0) break;
            Consume();
        }
    }

    /// <summary>
    /// Look-ahead: returns true if tokens starting from current position match
    /// a lambda parameter list pattern: () => or (id) => or (id, id) => or (type id, ...) =>
    /// </summary>
    private bool IsLambdaParameterList()
    {
        if (Current.Kind != RazorTokenKind.OpenParen) return false;
        var saved = _pos;
        Consume(); // (
        // () => is a parameterless lambda
        if (Current.Kind == RazorTokenKind.CloseParen && Peek().Kind == RazorTokenKind.Arrow) { _pos = saved; return true; }
        // Check for identifier-only or type+identifier comma-separated list followed by ) =>
        while (true)
        {
            // Skip optional type keyword
            if (Current.Kind is >= RazorTokenKind.Var and <= RazorTokenKind.Dynamic) Consume();
            if (Current.Kind != RazorTokenKind.Identifier) { _pos = saved; return false; }
            Consume();
            if (Current.Kind == RazorTokenKind.CloseParen)
            {
                var isLambda = Peek().Kind == RazorTokenKind.Arrow;
                _pos = saved;
                return isLambda;
            }
            if (Current.Kind != RazorTokenKind.Comma) { _pos = saved; return false; }
            Consume();
        }
    }

    private object CreateLambda(string[] paramNames, Func<string, object?> outerResolver)
    {
        // Capture the remaining expression tokens for lazy evaluation
        var bodyStart = _pos;
        // Skip to end of expression (stop at comma at depth 0 or close paren at depth 0 or EOF)
        var depth = 0;
        while (_pos < _tokens.Count && Current.Kind != RazorTokenKind.Eof)
        {
            if (Current.Kind is RazorTokenKind.OpenParen or RazorTokenKind.OpenBracket) depth++;
            else if (Current.Kind is RazorTokenKind.CloseParen or RazorTokenKind.CloseBracket)
            {
                if (depth == 0) break;
                depth--;
            }
            else if (Current.Kind == RazorTokenKind.Comma && depth == 0) break;
            _pos++;
        }
        var bodyTokens = _tokens.GetRange(bodyStart, _pos - bodyStart);
        bodyTokens.Add(new RazorToken(RazorTokenKind.Eof, "", 0));

        return new Func<object?[], object?>(args =>
        {
            object? LambdaResolver(string n)
            {
                for (var i = 0; i < paramNames.Length; i++)
                    if (n == paramNames[i]) return i < args.Length ? args[i] : null;
                return outerResolver(n);
            }
            var parser = new RazorExpressionParser(bodyTokens);
            return parser.ParseAndEvaluate(LambdaResolver);
        });
    }

    /// <summary>Skips generic type arguments like &lt;T, U&gt; if present, only when followed by '('.</summary>
    private void SkipGenericArgs()
    {
        if (Current.Kind != RazorTokenKind.Less) return;
        // Look ahead: only treat as generic args if we find matching > followed by (
        var saved = _pos;
        Consume(); // <
        var depth = 1;
        while (_pos < _tokens.Count && depth > 0)
        {
            if (Current.Kind == RazorTokenKind.Less) depth++;
            else if (Current.Kind == RazorTokenKind.Greater)
            {
                depth--;
                if (depth == 0)
                {
                    Consume(); // >
                    if (Current.Kind == RazorTokenKind.OpenParen)
                        return; // confirmed generic args: .Method<T>(...)
                    _pos = saved; // not generic — restore
                    return;
                }
            }
            // Only allow identifiers, commas, dots, brackets, and type keywords in generic args
            else if (Current.Kind is not (RazorTokenKind.Identifier or RazorTokenKind.Comma or RazorTokenKind.Dot
                or RazorTokenKind.OpenBracket or RazorTokenKind.CloseBracket or RazorTokenKind.Question
                or >= RazorTokenKind.Var and <= RazorTokenKind.Dynamic))
            {
                _pos = saved; // not a valid generic arg list
                return;
            }
            Consume();
        }
        _pos = saved;
    }

    private void SkipMemberChain()
    {
        // Skip remaining member accesses after null-conditional
        while (Current.Kind == RazorTokenKind.Dot || Current.Kind == RazorTokenKind.QuestionDot)
        {
            Consume();
            if (Current.Kind == RazorTokenKind.Identifier) Consume();
            if (Current.Kind == RazorTokenKind.OpenParen) { Consume(); SkipBalanced(RazorTokenKind.OpenParen, RazorTokenKind.CloseParen); }
        }
        if (Current.Kind == RazorTokenKind.OpenBracket) { Consume(); SkipBalanced(RazorTokenKind.OpenBracket, RazorTokenKind.CloseBracket); }
    }

    private void SkipBalanced(RazorTokenKind open, RazorTokenKind close)
    {
        var depth = 1;
        while (_pos < _tokens.Count && depth > 0)
        {
            if (Current.Kind == open) depth++;
            else if (Current.Kind == close) depth--;
            if (depth > 0) Consume();
        }
        if (Current.Kind == close) Consume();
    }

    private object? EvalMethodCall(object? target, string methodName, Func<string, object?> resolver)
    {
        Consume(); // (
        var args = new List<object?>();
        var outVarNames = new List<(int index, string name)?>();
        if (Current.Kind != RazorTokenKind.CloseParen)
        {
            ParseMethodArg(resolver, args, outVarNames);
            while (Match(RazorTokenKind.Comma))
                ParseMethodArg(resolver, args, outVarNames);
        }
        Expect(RazorTokenKind.CloseParen);

        if (target == null)
            return null;

        var argsArray = args.ToArray();
        var result = InvokeMethodWithOutRef(target, methodName, argsArray);

        // Write back out/ref values to pattern variables
        foreach (var entry in outVarNames)
        {
            if (entry.HasValue && entry.Value.index < argsArray.Length)
            {
                _patternVars ??= new Dictionary<string, object?>(StringComparer.Ordinal);
                _patternVars[entry.Value.name] = argsArray[entry.Value.index];
            }
        }

        return result;
    }

    private void ParseMethodArg(Func<string, object?> resolver, List<object?> args, List<(int index, string name)?> outVarNames)
    {
        // Skip ref/in keyword — just evaluate the expression
        if (Current.Kind == RazorTokenKind.Identifier && Current.Value is "ref" or "in")
        {
            Consume();
            outVarNames.Add(null);
            args.Add(EvalExpression(resolver));
            return;
        }
        // out var name / out name
        if (Current.Kind == RazorTokenKind.Identifier && Current.Value == "out")
        {
            Consume(); // out
            // out var name or out Type name
            if (Current.Kind is RazorTokenKind.Var or >= RazorTokenKind.Int and <= RazorTokenKind.Dynamic)
                Consume(); // skip type
            if (Current.Kind == RazorTokenKind.Identifier)
            {
                var outName = Consume().Value;
                outVarNames.Add((args.Count, outName));
                args.Add(null); // placeholder for out parameter
                return;
            }
        }
        outVarNames.Add(null);
        args.Add(EvalExpression(resolver));
    }

    private static object? InvokeMethodWithOutRef(object? target, string methodName, object?[] args)
    {
        if (target == null) return null;

        // Well-known zero-arg methods
        if (args.Length == 0)
        {
            switch (methodName)
            {
                case "ToString": return target.ToString();
                case "GetType": return target.GetType();
                case "GetHashCode": return target.GetHashCode();
            }
        }
        else if (args.Length == 1 && methodName == "Equals")
        {
            return target.Equals(args[0]);
        }

        var type = target.GetType();
        var methods = ReflectionCache.GetMethods(type, methodName, args.Length, BindingFlags.Public | BindingFlags.Instance);

        foreach (var method in methods)
        {
            try
            {
                var parameters = method.GetParameters();
                var convertedArgs = new object?[args.Length];
                for (var i = 0; i < args.Length; i++)
                {
                    var paramType = parameters[i].ParameterType;
                    if (paramType.IsByRef) paramType = paramType.GetElementType()!;

                    if (args[i] == null && (parameters[i].IsOut || paramType.IsValueType))
                        convertedArgs[i] = paramType.IsValueType ? Activator.CreateInstance(paramType) : null;
                    else if (args[i] == null || paramType.IsInstanceOfType(args[i]!))
                        convertedArgs[i] = args[i];
                    else
                        convertedArgs[i] = Convert.ChangeType(args[i], paramType, CultureInfo.InvariantCulture);
                }

                var result = method.Invoke(target, convertedArgs);

                // Write back ref/out values
                for (var i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].IsOut || parameters[i].ParameterType.IsByRef)
                        args[i] = convertedArgs[i];
                }

                return result;
            }
            catch { }
        }

        // Fallback to standard InvokeMethod for non-out/ref cases
        return InvokeMethod(target, methodName, args);
    }

    private object? EvalPrimary(Func<string, object?> resolver)
    {
        var token = Current;
        switch (token.Kind)
        {
            case RazorTokenKind.IntLiteral: Consume(); return int.Parse(token.Value, CultureInfo.InvariantCulture);
            case RazorTokenKind.LongLiteral: Consume(); return long.Parse(token.Value.TrimEnd('l', 'L'), CultureInfo.InvariantCulture);
            case RazorTokenKind.DoubleLiteral: Consume(); return double.Parse(token.Value.TrimEnd('d', 'D'), CultureInfo.InvariantCulture);
            case RazorTokenKind.FloatLiteral: Consume(); return float.Parse(token.Value.TrimEnd('f', 'F'), CultureInfo.InvariantCulture);
            case RazorTokenKind.DecimalLiteral: Consume(); return decimal.Parse(token.Value.TrimEnd('m', 'M'), CultureInfo.InvariantCulture);
            case RazorTokenKind.StringLiteral: Consume(); return token.Value;
            case RazorTokenKind.InterpolatedStringLiteral: Consume(); return EvalInterpolatedString(token.Value, resolver);
            case RazorTokenKind.CharLiteral: Consume(); return token.Value.Length > 0 ? token.Value[0] : '\0';
            case RazorTokenKind.True: Consume(); return true;
            case RazorTokenKind.False: Consume(); return false;
            case RazorTokenKind.Null: Consume(); return null;
            case RazorTokenKind.Default:
                Consume();
                if (Current.Kind == RazorTokenKind.OpenParen)
                {
                    Consume();
                    var defType = ResolveTypeName();
                    Expect(RazorTokenKind.CloseParen);
                    if (defType != null && defType.IsValueType) return Activator.CreateInstance(defType);
                    return null;
                }
                return null; // default literal

            case RazorTokenKind.OpenParen:
                // Check for lambda: (x) => expr or (x, y) => expr
                if (IsLambdaParameterList())
                {
                    Consume(); // (
                    var lambdaParams = new List<string>();
                    if (Current.Kind != RazorTokenKind.CloseParen)
                    {
                        // Skip optional type keywords (e.g., int x, string y)
                        if (Current.Kind is >= RazorTokenKind.Var and <= RazorTokenKind.Dynamic) Consume();
                        lambdaParams.Add(Expect(RazorTokenKind.Identifier).Value);
                        while (Match(RazorTokenKind.Comma))
                        {
                            if (Current.Kind is >= RazorTokenKind.Var and <= RazorTokenKind.Dynamic) Consume();
                            lambdaParams.Add(Expect(RazorTokenKind.Identifier).Value);
                        }
                    }
                    Expect(RazorTokenKind.CloseParen);
                    Expect(RazorTokenKind.Arrow);
                    return CreateLambda(lambdaParams.ToArray(), resolver);
                }
                Consume();
                var val = EvalExpression(resolver);
                if (Current.Kind == RazorTokenKind.Comma)
                {
                    // Tuple literal: (a, b, ...)
                    var items = new List<object?> { val };
                    while (Match(RazorTokenKind.Comma))
                        items.Add(EvalExpression(resolver));
                    Expect(RazorTokenKind.CloseParen);
                    return items.Count switch
                    {
                        2 => (items[0], items[1]),
                        3 => (items[0], items[1], items[2]),
                        4 => (items[0], items[1], items[2], items[3]),
                        5 => (items[0], items[1], items[2], items[3], items[4]),
                        _ => items.ToArray()
                    };
                }
                Expect(RazorTokenKind.CloseParen);
                return val;

            case RazorTokenKind.New:
                return EvalNewExpression(resolver);

            // C# 12 collection expression: [1, 2, 3]
            case RazorTokenKind.OpenBracket:
                Consume();
                var collItems = new List<object?>();
                if (Current.Kind != RazorTokenKind.CloseBracket)
                {
                    collItems.Add(EvalExpression(resolver));
                    while (Match(RazorTokenKind.Comma))
                    {
                        if (Current.Kind == RazorTokenKind.CloseBracket) break;
                        // Spread element: ..expr
                        if (Current.Kind == RazorTokenKind.DotDot)
                        {
                            Consume();
                            var spread = EvalExpression(resolver);
                            if (spread is System.Collections.IEnumerable spreadEnum)
                                foreach (var s in spreadEnum) collItems.Add(s);
                            continue;
                        }
                        collItems.Add(EvalExpression(resolver));
                    }
                }
                Expect(RazorTokenKind.CloseBracket);
                return collItems.ToArray();

            // async lambda: async x => expr, async (x, y) => expr, async () => expr
            case RazorTokenKind.Async:
                Consume();
                if (Current.Kind == RazorTokenKind.Identifier && Peek().Kind == RazorTokenKind.Arrow)
                {
                    var asyncParam = Consume().Value;
                    Consume(); // =>
                    return CreateLambda(new[] { asyncParam }, resolver);
                }
                if (Current.Kind == RazorTokenKind.OpenParen && IsLambdaParameterList())
                {
                    Consume(); // (
                    var asyncParams = new List<string>();
                    if (Current.Kind != RazorTokenKind.CloseParen)
                    {
                        if (Current.Kind is >= RazorTokenKind.Var and <= RazorTokenKind.Dynamic) Consume();
                        asyncParams.Add(Expect(RazorTokenKind.Identifier).Value);
                        while (Match(RazorTokenKind.Comma))
                        {
                            if (Current.Kind is >= RazorTokenKind.Var and <= RazorTokenKind.Dynamic) Consume();
                            asyncParams.Add(Expect(RazorTokenKind.Identifier).Value);
                        }
                    }
                    Expect(RazorTokenKind.CloseParen);
                    Expect(RazorTokenKind.Arrow);
                    return CreateLambda(asyncParams.ToArray(), resolver);
                }
                // async used as identifier fallback
                return resolver("async");

            case RazorTokenKind.Typeof:
                Consume();
                Expect(RazorTokenKind.OpenParen);
                var type = ResolveTypeName();
                Expect(RazorTokenKind.CloseParen);
                return type;

            case RazorTokenKind.Nameof:
                Consume();
                Expect(RazorTokenKind.OpenParen);
                var nameofId = Expect(RazorTokenKind.Identifier).Value;
                while (Match(RazorTokenKind.Dot)) nameofId = Expect(RazorTokenKind.Identifier).Value;
                Expect(RazorTokenKind.CloseParen);
                return nameofId;

            case RazorTokenKind.Identifier:
                var name = Consume().Value;
                // sizeof(Type)
                if (name == "sizeof" && Current.Kind == RazorTokenKind.OpenParen)
                {
                    Consume();
                    var sizeType = ResolveTypeName();
                    Expect(RazorTokenKind.CloseParen);
                    if (sizeType == typeof(byte) || sizeType == typeof(sbyte) || sizeType == typeof(bool)) return 1;
                    if (sizeType == typeof(short) || sizeType == typeof(ushort) || sizeType == typeof(char)) return 2;
                    if (sizeType == typeof(int) || sizeType == typeof(uint) || sizeType == typeof(float)) return 4;
                    if (sizeType == typeof(long) || sizeType == typeof(ulong) || sizeType == typeof(double)) return 8;
                    if (sizeType == typeof(decimal)) return 16;
                    if (sizeType != null) return System.Runtime.InteropServices.Marshal.SizeOf(sizeType);
                    return 0;
                }
                // delegate { } / delegate(params) { } — anonymous delegate
                if (name == "delegate")
                {
                    // Skip optional parameter list
                    if (Current.Kind == RazorTokenKind.OpenParen)
                    {
                        var paramList = new List<string>();
                        Consume(); // (
                        while (Current.Kind != RazorTokenKind.CloseParen && Current.Kind != RazorTokenKind.Eof)
                        {
                            if (Current.Kind is >= RazorTokenKind.Var and <= RazorTokenKind.Dynamic) Consume(); // skip type
                            if (Current.Kind == RazorTokenKind.Identifier) paramList.Add(Consume().Value);
                            Match(RazorTokenKind.Comma);
                        }
                        Expect(RazorTokenKind.CloseParen);
                        // Read body block { ... } and create lambda
                        if (Current.Kind == RazorTokenKind.OpenBrace)
                        {
                            var bodyStart = _pos;
                            SkipBalanced(RazorTokenKind.OpenBrace, RazorTokenKind.CloseBrace);
                            var bodyTokens = _tokens.GetRange(bodyStart, _pos - bodyStart - 1);
                            bodyTokens.Add(new RazorToken(RazorTokenKind.Eof, "", 0));
                            return new Func<object?[], object?>(args =>
                            {
                                object? DelegateResolver(string n)
                                {
                                    for (var i = 0; i < paramList.Count; i++)
                                        if (n == paramList[i]) return i < args.Length ? args[i] : null;
                                    return resolver(n);
                                }
                                var p = new RazorExpressionParser(bodyTokens);
                                return p.ParseAndEvaluate(DelegateResolver);
                            });
                        }
                    }
                    return null;
                }
                // checked(expr) / unchecked(expr) — evaluate expr as-is (no overflow distinction in interpreter)
                if (name is "checked" or "unchecked" && Current.Kind == RazorTokenKind.OpenParen)
                {
                    Consume(); // (
                    var checkedVal = EvalExpression(resolver);
                    Expect(RazorTokenKind.CloseParen);
                    return checkedVal;
                }
                // Lambda: x => expr
                if (Current.Kind == RazorTokenKind.Arrow)
                {
                    Consume(); // =>
                    return CreateLambda(new[] { name }, resolver);
                }
                // Check for method call on resolved identifier
                if (Current.Kind == RazorTokenKind.OpenParen)
                {
                    var target = resolver(name);

                    // Local function (stored as Func<object?[], object?>)
                    if (target is Func<object?[], object?> localFunc)
                    {
                        Consume(); // (
                        var args = new List<object?>();
                        if (Current.Kind != RazorTokenKind.CloseParen)
                        {
                            args.Add(EvalExpression(resolver));
                            while (Match(RazorTokenKind.Comma))
                                args.Add(EvalExpression(resolver));
                        }
                        Expect(RazorTokenKind.CloseParen);
                        return localFunc(args.ToArray());
                    }

                    if (target == null)
                    {
                        var staticType = ResolveWellKnownType(name);
                        if (staticType != null)
                            return EvalStaticMethodCall(staticType, resolver);
                    }
                    return EvalMethodCall(target, name, resolver);
                }
                var resolved = resolver(name);
                // If unresolved and followed by '.', might be a type name (e.g., DateTime.Now)
                if (resolved == null && Current.Kind == RazorTokenKind.Dot)
                {
                    var staticType = ResolveWellKnownType(name);
                    if (staticType != null)
                        return staticType; // Return Type object; postfix handler does static access
                }
                return resolved;

            // Type keywords used as identifiers (e.g., string.Empty, int.MaxValue)
            case RazorTokenKind.String:
            case RazorTokenKind.Int:
            case RazorTokenKind.Long:
            case RazorTokenKind.Double:
            case RazorTokenKind.Float:
            case RazorTokenKind.Decimal:
            case RazorTokenKind.Bool:
            case RazorTokenKind.Char:
            case RazorTokenKind.Object:
                var typeName = Consume().Value;
                if (Match(RazorTokenKind.Dot))
                {
                    var member = Expect(RazorTokenKind.Identifier).Value;
                    var t = ResolveWellKnownType(typeName);
                    if (t != null) return GetStaticMember(t, member);
                }
                return null;

            default:
                throw new XamlParseException($"Unexpected token '{token.Value}' ({token.Kind}) at position {token.Position}");
        }
    }

    private object? EvalNewExpression(Func<string, object?> resolver)
    {
        Consume(); // new

        // new[] { ... } - array initializer
        if (Current.Kind == RazorTokenKind.OpenBracket && Peek().Kind == RazorTokenKind.CloseBracket)
        {
            Consume(); Consume(); // []
            Expect(RazorTokenKind.OpenBrace);
            var items = new List<object?>();
            if (Current.Kind != RazorTokenKind.CloseBrace)
            {
                items.Add(EvalExpression(resolver));
                while (Match(RazorTokenKind.Comma))
                {
                    if (Current.Kind == RazorTokenKind.CloseBrace) break; // trailing comma
                    items.Add(EvalExpression(resolver));
                }
            }
            Expect(RazorTokenKind.CloseBrace);

            if (items.Count == 0) return Array.Empty<object?>();
            var elementType = items[0]?.GetType() ?? typeof(object);
            var allSameType = items.All(i => i == null || i.GetType() == elementType);
            if (allSameType && elementType != typeof(object))
            {
                var arr = Array.CreateInstance(elementType, items.Count);
                for (var i = 0; i < items.Count; i++) arr.SetValue(items[i], i);
                return arr;
            }
            return items.ToArray();
        }

        // new Type(args) or new Type { ... }
        var typeNameBuilder = new StringBuilder();
        while (Current.Kind == RazorTokenKind.Identifier || Current.Kind is >= RazorTokenKind.Int and <= RazorTokenKind.Dynamic)
        {
            typeNameBuilder.Append(Consume().Value);
            if (Match(RazorTokenKind.Dot)) typeNameBuilder.Append('.');
            else break;
        }

        // Handle generic: skip <T> for now
        if (Current.Kind == RazorTokenKind.Less)
        {
            typeNameBuilder.Append('<');
            Consume();
            var depth = 1;
            while (_pos < _tokens.Count && depth > 0)
            {
                if (Current.Kind == RazorTokenKind.Less) depth++;
                else if (Current.Kind == RazorTokenKind.Greater) { depth--; if (depth == 0) { Consume(); break; } }
                typeNameBuilder.Append(Consume().Value);
            }
            typeNameBuilder.Append('>');
        }

        var newType = ResolveWellKnownType(typeNameBuilder.ToString());

        if (Current.Kind == RazorTokenKind.OpenParen)
        {
            Consume();
            var args = new List<object?>();
            if (Current.Kind != RazorTokenKind.CloseParen)
            {
                args.Add(EvalExpression(resolver));
                while (Match(RazorTokenKind.Comma))
                    args.Add(EvalExpression(resolver));
            }
            Expect(RazorTokenKind.CloseParen);

            if (newType != null)
                return Activator.CreateInstance(newType, args.ToArray());

            // Tuple shorthand: new (a, b) → ValueTuple
            if (args.Count == 2) return (args[0], args[1]);
            if (args.Count == 3) return (args[0], args[1], args[2]);
        }

        // new Type { ... } — object initializer, collection initializer, or dictionary initializer
        if (Current.Kind == RazorTokenKind.OpenBrace && newType != null)
        {
            Consume();
            var obj = Activator.CreateInstance(newType);

            if (Current.Kind != RazorTokenKind.CloseBrace)
            {
                // Detect initializer style by first element
                if (Current.Kind == RazorTokenKind.OpenBrace || Current.Kind == RazorTokenKind.OpenBracket)
                {
                    // Collection/Dictionary initializer: new Dict { ["key"] = val } or new List { { a, b } }
                    var addMethod = newType.GetMethod("Add");
                    while (Current.Kind != RazorTokenKind.CloseBrace && Current.Kind != RazorTokenKind.Eof)
                    {
                        if (Current.Kind == RazorTokenKind.OpenBracket)
                        {
                            // Dictionary: ["key"] = value
                            Consume(); // [
                            var key = EvalExpression(resolver);
                            Expect(RazorTokenKind.CloseBracket);
                            Expect(RazorTokenKind.Assign);
                            var dictVal = EvalExpression(resolver);
                            addMethod?.Invoke(obj, new[] { key, dictVal });
                        }
                        else
                        {
                            // Collection element
                            var elemVal = EvalExpression(resolver);
                            addMethod?.Invoke(obj, new[] { elemVal });
                        }
                        Match(RazorTokenKind.Comma);
                    }
                }
                else if (Current.Kind == RazorTokenKind.Identifier && Peek().Kind == RazorTokenKind.Assign)
                {
                    // Object initializer: { Prop = val, ... }
                    while (Current.Kind != RazorTokenKind.CloseBrace && Current.Kind != RazorTokenKind.Eof)
                    {
                        var prop = Expect(RazorTokenKind.Identifier).Value;
                        Expect(RazorTokenKind.Assign);
                        var propVal = EvalExpression(resolver);
                        newType.GetProperty(prop)?.SetValue(obj, propVal);
                        Match(RazorTokenKind.Comma);
                    }
                }
                else
                {
                    // Collection initializer with simple values: new List<int> { 1, 2, 3 }
                    var addMethod = newType.GetMethod("Add");
                    while (Current.Kind != RazorTokenKind.CloseBrace && Current.Kind != RazorTokenKind.Eof)
                    {
                        var elemVal = EvalExpression(resolver);
                        addMethod?.Invoke(obj, new[] { elemVal });
                        Match(RazorTokenKind.Comma);
                    }
                }
            }

            Expect(RazorTokenKind.CloseBrace);
            return obj;
        }

        // new { X = 1, Y = 2 } — anonymous type (return as ExpandoObject)
        if (Current.Kind == RazorTokenKind.OpenBrace && newType == null && typeNameBuilder.Length == 0)
        {
            Consume();
            var expando = new System.Dynamic.ExpandoObject();
            var dict = (IDictionary<string, object?>)expando;
            while (Current.Kind != RazorTokenKind.CloseBrace && Current.Kind != RazorTokenKind.Eof)
            {
                var propName = Expect(RazorTokenKind.Identifier).Value;
                Expect(RazorTokenKind.Assign);
                var propVal = EvalExpression(resolver);
                dict[propName] = propVal;
                Match(RazorTokenKind.Comma);
            }
            Expect(RazorTokenKind.CloseBrace);
            return expando;
        }

        return null;
    }

    private object? EvalStaticMethodCall(Type type, string methodName, Func<string, object?> resolver)
    {
        Consume(); // (
        var args = new List<object?>();
        if (Current.Kind != RazorTokenKind.CloseParen)
        {
            args.Add(EvalExpression(resolver));
            while (Match(RazorTokenKind.Comma))
                args.Add(EvalExpression(resolver));
        }
        Expect(RazorTokenKind.CloseParen);

        var methods = ReflectionCache.GetMethods(type, methodName, args.Count, BindingFlags.Public | BindingFlags.Static);
        foreach (var m in methods)
        {
            try { return m.Invoke(null, args.ToArray()); } catch { }
        }
        return null;
    }

    private object? EvalStaticMethodCall(Type type, Func<string, object?> resolver)
    {
        Consume(); // (
        var args = new List<object?>();
        if (Current.Kind != RazorTokenKind.CloseParen)
        {
            args.Add(EvalExpression(resolver));
            while (Match(RazorTokenKind.Comma))
                args.Add(EvalExpression(resolver));
        }
        Expect(RazorTokenKind.CloseParen);

        // No specific method name — try all static methods matching arg count
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
        foreach (var m in methods)
        {
            if (m.GetParameters().Length == args.Count)
            {
                try { return m.Invoke(null, args.ToArray()); } catch { }
            }
        }
        return null;
    }

    // ── Type resolution ──

    private bool IsTypeName(RazorToken token) =>
        token.Kind is RazorTokenKind.Int or RazorTokenKind.Long or RazorTokenKind.Double
            or RazorTokenKind.Float or RazorTokenKind.Decimal or RazorTokenKind.Bool
            or RazorTokenKind.String or RazorTokenKind.Char or RazorTokenKind.Object;

    private Type? ResolveTypeName()
    {
        var name = Current.Value;
        Consume();
        while (Match(RazorTokenKind.Dot))
            name += "." + Expect(RazorTokenKind.Identifier).Value;
        return ResolveWellKnownType(name);
    }

    internal static Type? ResolveWellKnownType(string name) => name switch
    {
        // Primitives
        "int" or "Int32" or "System.Int32" => typeof(int),
        "long" or "Int64" or "System.Int64" => typeof(long),
        "short" or "Int16" or "System.Int16" => typeof(short),
        "byte" or "Byte" or "System.Byte" => typeof(byte),
        "double" or "Double" or "System.Double" => typeof(double),
        "float" or "Single" or "System.Single" => typeof(float),
        "decimal" or "Decimal" or "System.Decimal" => typeof(decimal),
        "bool" or "Boolean" or "System.Boolean" => typeof(bool),
        "string" or "String" or "System.String" => typeof(string),
        "char" or "Char" or "System.Char" => typeof(char),
        "object" or "Object" or "System.Object" => typeof(object),
        "void" or "Void" or "System.Void" => typeof(void),

        // Common types
        "DateTime" or "System.DateTime" => typeof(DateTime),
        "DateTimeOffset" or "System.DateTimeOffset" => typeof(DateTimeOffset),
        "TimeSpan" or "System.TimeSpan" => typeof(TimeSpan),
        "Guid" or "System.Guid" => typeof(Guid),
        "Uri" or "System.Uri" => typeof(Uri),
        "Type" or "System.Type" => typeof(Type),
        "Nullable" or "System.Nullable" => typeof(Nullable),

        // Math & conversion
        "Math" or "System.Math" => typeof(Math),
        "MathF" or "System.MathF" => typeof(MathF),
        "Convert" or "System.Convert" => typeof(Convert),
        "BitConverter" or "System.BitConverter" => typeof(BitConverter),

        // String & text
        "StringComparison" or "System.StringComparison" => typeof(StringComparison),
        "StringBuilder" or "System.Text.StringBuilder" => typeof(StringBuilder),
        "Encoding" or "System.Text.Encoding" => typeof(Encoding),
        "CultureInfo" or "System.Globalization.CultureInfo" => typeof(CultureInfo),
        "Regex" or "System.Text.RegularExpressions.Regex" => typeof(Regex),

        // Collections
        "Enumerable" or "System.Linq.Enumerable" => typeof(System.Linq.Enumerable),
        "Array" or "System.Array" => typeof(Array),
        "List" or "List`1" or "System.Collections.Generic.List" => typeof(List<>),
        "Dictionary" or "Dictionary`2" or "System.Collections.Generic.Dictionary" => typeof(Dictionary<,>),
        "HashSet" or "HashSet`1" or "System.Collections.Generic.HashSet" => typeof(HashSet<>),
        "Stack" or "Stack`1" => typeof(Stack<>),
        "Queue" or "Queue`1" => typeof(Queue<>),
        "KeyValuePair" or "KeyValuePair`2" => typeof(KeyValuePair<,>),

        // IO & environment
        "StringReader" or "System.IO.StringReader" => typeof(System.IO.StringReader),
        "StringWriter" or "System.IO.StringWriter" => typeof(System.IO.StringWriter),
        "MemoryStream" or "System.IO.MemoryStream" => typeof(System.IO.MemoryStream),
        "Path" or "System.IO.Path" => typeof(System.IO.Path),
        "File" or "System.IO.File" => typeof(System.IO.File),
        "Directory" or "System.IO.Directory" => typeof(System.IO.Directory),
        "Environment" or "System.Environment" => typeof(Environment),
        "Console" or "System.Console" => typeof(Console),
        "RuntimeInformation" or "System.Runtime.InteropServices.RuntimeInformation" => typeof(System.Runtime.InteropServices.RuntimeInformation),

        // Exceptions
        "Exception" or "System.Exception" => typeof(Exception),
        "ArgumentException" or "System.ArgumentException" => typeof(ArgumentException),
        "InvalidOperationException" or "System.InvalidOperationException" => typeof(InvalidOperationException),
        "NotSupportedException" or "System.NotSupportedException" => typeof(NotSupportedException),
        "NotImplementedException" or "System.NotImplementedException" => typeof(NotImplementedException),
        "ArgumentNullException" or "System.ArgumentNullException" => typeof(ArgumentNullException),
        "ArgumentOutOfRangeException" or "System.ArgumentOutOfRangeException" => typeof(ArgumentOutOfRangeException),
        "FormatException" or "System.FormatException" => typeof(FormatException),
        "OverflowException" or "System.OverflowException" => typeof(OverflowException),

        // Random & Tuple
        "Random" or "System.Random" => typeof(Random),
        "Tuple" or "System.Tuple" => typeof(Tuple),
        "ValueTuple" or "System.ValueTuple" => typeof(ValueTuple),
        "Task" or "System.Threading.Tasks.Task" => typeof(System.Threading.Tasks.Task),

        // Fallback: check build-time registered namespace types, then runtime resolution
        _ => TryResolveTypeByName(name)
    };

    private static Type? TryResolveTypeByName(string name)
    {
        // First check types registered from @using directives at build time
        var registered = RazorExpressionRegistry.TryResolveRegisteredType(name);
        if (registered != null) return registered;

        // Handle generic types: List<int> → System.Collections.Generic.List`1[System.Int32]
        var genericStart = name.IndexOf('<');
        if (genericStart > 0 && name.EndsWith('>'))
        {
            var baseName = name[..genericStart];
            var argsPart = name[(genericStart + 1)..^1];
            var baseType = ResolveWellKnownType(baseName);
            if (baseType != null && baseType.IsGenericType)
            {
                var typeArgs = argsPart.Split(',').Select(a => ResolveWellKnownType(a.Trim())).ToArray();
                if (typeArgs.All(t => t != null))
                {
                    try { return baseType.GetGenericTypeDefinition().MakeGenericType(typeArgs!); }
                    catch { }
                }
            }
        }

        try { return Type.GetType(name) ?? Type.GetType($"System.{name}"); }
        catch { return null; }
    }

    // ── Helpers ──

    internal static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        int i => i != 0,
        long l => l != 0,
        double d => d != 0,
        float f => f != 0,
        decimal m => m != 0,
        string s => s.Length > 0,
        _ => true
    };

    internal static object? BitwiseOp(object? left, object? right, char op)
    {
        if (left == null || right == null) return null;
        var l = Convert.ToInt64(left, CultureInfo.InvariantCulture);
        var r = Convert.ToInt64(right, CultureInfo.InvariantCulture);
        long result = op switch
        {
            '&' => l & r, '|' => l | r, '^' => l ^ r,
            '<' => l << (int)r, // left shift
            '>' => l >> (int)r, // right shift
            _ => 0
        };
        if (left is int && right is int) return (int)result;
        return result;
    }

    private static object? UnsignedRightShiftOp(object? left, object? right)
    {
        if (left == null || right == null) return null;
        var shift = Convert.ToInt32(right, CultureInfo.InvariantCulture);
        if (left is int i) return (int)((uint)i >> shift);
        if (left is long l) return (long)((ulong)l >> shift);
        return Convert.ToInt64(left, CultureInfo.InvariantCulture) >> shift;
    }

    internal static object? ArithmeticOp(object? left, object? right, char op)
    {
        if (left == null || right == null) return null;

        var l = Convert.ToDouble(left, CultureInfo.InvariantCulture);
        var r = Convert.ToDouble(right, CultureInfo.InvariantCulture);
        double result = op switch
        {
            '+' => l + r, '-' => l - r, '*' => l * r,
            '/' => r != 0 ? l / r : 0, '%' => r != 0 ? l % r : 0,
            _ => 0
        };

        // Preserve integer type when both operands are integral
        if (left is int && right is int) return (int)result;
        if (left is long || right is long) return (long)result;
        return result;
    }

    private static int Compare(object? left, object? right)
    {
        if (left == null && right == null) return 0;
        if (left == null) return -1;
        if (right == null) return 1;
        if (left is IComparable c)
        {
            try { return c.CompareTo(Convert.ChangeType(right, left.GetType(), CultureInfo.InvariantCulture)); }
            catch { return string.Compare(left.ToString(), right?.ToString(), StringComparison.Ordinal); }
        }
        return 0;
    }

    private static bool Equals(object? left, object? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left == null || right == null) return left == null && right == null;
        if (left.GetType() == right.GetType()) return left.Equals(right);
        try { return Convert.ToDouble(left, CultureInfo.InvariantCulture) == Convert.ToDouble(right, CultureInfo.InvariantCulture); }
        catch { return left.Equals(right); }
    }

    private static string EvalInterpolatedString(string template, Func<string, object?> resolver)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < template.Length)
        {
            if (template[i] == '{' && i + 1 < template.Length && template[i + 1] != '{')
            {
                // Find matching }
                var start = i + 1;
                var depth = 1;
                var p = start;
                while (p < template.Length && depth > 0)
                {
                    if (template[p] == '{') depth++;
                    else if (template[p] == '}') { depth--; if (depth == 0) break; }
                    p++;
                }

                var expr = template[start..p];
                // Check for alignment: {expr,alignment} or {expr,alignment:format}
                int? alignment = null;
                string? format = null;
                var commaIdx = FindAlignmentComma(expr);
                if (commaIdx >= 0)
                {
                    var afterComma = expr[(commaIdx + 1)..];
                    expr = expr[..commaIdx];
                    var colonInAlign = FindFormatColon(afterComma);
                    if (colonInAlign >= 0)
                    {
                        format = afterComma[(colonInAlign + 1)..];
                        if (int.TryParse(afterComma[..colonInAlign].Trim(), out var a)) alignment = a;
                    }
                    else
                    {
                        if (int.TryParse(afterComma.Trim(), out var a)) alignment = a;
                    }
                }
                else
                {
                    var colonIdx = FindFormatColon(expr);
                    if (colonIdx >= 0)
                    {
                        format = expr[(colonIdx + 1)..];
                        expr = expr[..colonIdx];
                    }
                }

                var value = RazorLightweightExpressionEvaluator.Evaluate(expr.Trim(), resolver);
                string formatted;
                if (format != null && value is IFormattable formattable)
                    formatted = formattable.ToString(format, CultureInfo.CurrentCulture);
                else
                    formatted = value?.ToString() ?? "";
                if (alignment.HasValue)
                {
                    var w = Math.Abs(alignment.Value);
                    formatted = alignment.Value >= 0
                        ? formatted.PadLeft(w)
                        : formatted.PadRight(w);
                }
                sb.Append(formatted);
                i = p + 1;
            }
            else
            {
                sb.Append(template[i]);
                i++;
            }
        }
        return sb.ToString();
    }

    /// <summary>Finds the format-specifier colon in an interpolation expression, skipping colons inside ternary expressions.</summary>
    private static int FindFormatColon(string expr)
    {
        var depth = 0;
        for (var i = 0; i < expr.Length; i++)
        {
            switch (expr[i])
            {
                case '(' or '[': depth++; break;
                case ')' or ']': depth--; break;
                case '?' when depth == 0: // skip ternary — the ':' after '?' is not a format spec
                    // Find the matching ':' of the ternary
                    i++;
                    var ternaryDepth = 0;
                    while (i < expr.Length)
                    {
                        if (expr[i] is '(' or '[') ternaryDepth++;
                        else if (expr[i] is ')' or ']') ternaryDepth--;
                        else if (expr[i] == ':' && ternaryDepth == 0) break;
                        i++;
                    }
                    break;
                case ':' when depth == 0: return i;
            }
        }
        return -1;
    }

    /// <summary>Finds the alignment comma in an interpolation expression {expr,alignment}.</summary>
    private static int FindAlignmentComma(string expr)
    {
        var depth = 0;
        for (var i = 0; i < expr.Length; i++)
        {
            switch (expr[i])
            {
                case '(' or '[' or '{': depth++; break;
                case ')' or ']' or '}': depth--; break;
                case ',' when depth == 0: return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Synchronously unwraps Task, Task&lt;T&gt;, ValueTask, ValueTask&lt;T&gt;, and any custom
    /// awaitable (via GetAwaiter/GetResult pattern). Fully AOT-safe — uses only reflection
    /// and well-known type checks, no dynamic emit.
    /// </summary>
    internal static object? UnwrapAwaitable(object? value)
    {
        if (value == null) return null;

        // Fast path: well-known types
        if (value is Task task)
        {
            task.GetAwaiter().GetResult();
            // Extract result if Task<T>
            var type = task.GetType();
            if (type.IsGenericType)
            {
                var resultProp = type.GetProperty("Result");
                return resultProp?.GetValue(task);
            }
            return null; // void Task
        }

        // ValueTask (non-generic)
        if (value is ValueTask vt)
        {
            vt.GetAwaiter().GetResult();
            return null;
        }

        // ValueTask<T> — check via type name (AOT-safe, no generic constraint needed)
        var valueType = value.GetType();
        if (valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            // Call AsTask().GetAwaiter().GetResult() via reflection
            var asTaskMethod = valueType.GetMethod("AsTask");
            if (asTaskMethod != null)
            {
                var task2 = (Task)asTaskMethod.Invoke(value, null)!;
                task2.GetAwaiter().GetResult();
                var resultProp = task2.GetType().GetProperty("Result");
                return resultProp?.GetValue(task2);
            }
        }

        // Custom awaitable: duck-type GetAwaiter().GetResult()
        var getAwaiter = valueType.GetMethod("GetAwaiter", Type.EmptyTypes);
        if (getAwaiter != null)
        {
            var awaiter = getAwaiter.Invoke(value, null);
            if (awaiter != null)
            {
                var getResult = awaiter.GetType().GetMethod("GetResult", Type.EmptyTypes);
                if (getResult != null)
                {
                    var result = getResult.Invoke(awaiter, null);
                    return getResult.ReturnType == typeof(void) ? null : result;
                }
            }
        }

        // Not awaitable — return as-is
        return value;
    }

    internal static object? GetMember(object? target, string memberName)
    {
        if (target == null) return null;
        var type = target.GetType();

        // Property (cached)
        var prop = ReflectionCache.GetProperty(type, memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop != null) return prop.GetValue(target);

        // Field (cached)
        var field = ReflectionCache.GetField(type, memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (field != null) return field.GetValue(target);

        // Tuple Item access
        if (memberName.StartsWith("Item") && type.IsValueType && type.FullName?.StartsWith("System.ValueTuple") == true)
        {
            field = ReflectionCache.GetField(type, memberName, BindingFlags.Public | BindingFlags.Instance);
            if (field != null) return field.GetValue(target);
        }

        return null;
    }

    internal static object? GetStaticMember(Type type, string memberName)
    {
        var prop = ReflectionCache.GetProperty(type, memberName, BindingFlags.Public | BindingFlags.Static);
        if (prop != null) return prop.GetValue(null);
        var field = ReflectionCache.GetField(type, memberName, BindingFlags.Public | BindingFlags.Static);
        if (field != null) return field.GetValue(null);
        return null;
    }

    internal static object? InvokeMethod(object? target, string methodName, object?[] args)
    {
        if (target == null) return null;

        // Well-known zero-arg methods — handle directly to avoid reflection edge cases
        if (args.Length == 0)
        {
            switch (methodName)
            {
                case "ToString": return target.ToString();
                case "GetType": return target.GetType();
                case "GetHashCode": return target.GetHashCode();
            }
        }
        else if (args.Length == 1 && methodName == "Equals")
        {
            return target.Equals(args[0]);
        }

        var type = target.GetType();

        // Try exact match via reflection (cached)
        var methods = ReflectionCache.GetMethods(type, methodName, args.Length, BindingFlags.Public | BindingFlags.Instance);

        foreach (var method in methods)
        {
            try
            {
                var parameters = method.GetParameters();
                var convertedArgs = ConvertArgs(args, parameters);
                if (convertedArgs != null)
                    return method.Invoke(target, convertedArgs);
            }
            catch { }
        }

        // LINQ extension methods (Select, Where, OrderBy, etc.)
        if (target is System.Collections.IEnumerable enumerable)
        {
            var result = TryInvokeLinqMethod(enumerable, methodName, args);
            if (result != null) return result;
        }

        return null;
    }

    private static object?[]? ConvertArgs(object?[] args, ParameterInfo[] parameters)
    {
        if (args.Length != parameters.Length) return null;
        var convertedArgs = new object?[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == null || parameters[i].ParameterType.IsInstanceOfType(args[i]!))
                convertedArgs[i] = args[i];
            else if (args[i] is Func<object?[], object?> lambda && parameters[i].ParameterType.IsGenericType)
                convertedArgs[i] = WrapLambdaAsDelegate(lambda, parameters[i].ParameterType);
            else
            {
                try { convertedArgs[i] = Convert.ChangeType(args[i], parameters[i].ParameterType, CultureInfo.InvariantCulture); }
                catch { return null; }
            }
        }
        return convertedArgs;
    }

    private static object? WrapLambdaAsDelegate(Func<object?[], object?> lambda, Type delegateType)
    {
        if (!delegateType.IsGenericType) return null;
        var genArgs = delegateType.GetGenericArguments();
        var genDef = delegateType.GetGenericTypeDefinition();

        // Func<T, TResult>
        if (genDef == typeof(Func<,>))
            return CreateTypedFunc(lambda, genArgs[0], genArgs[1]);
        // Func<T1, T2, TResult>
        if (genDef == typeof(Func<,,>))
            return CreateTypedFunc2(lambda, genArgs[0], genArgs[1], genArgs[2]);
        // Func<T, bool> (Predicate-like)
        if (genDef == typeof(Predicate<>))
            return CreateTypedPredicate(lambda, genArgs[0]);

        return null;
    }

    private static object CreateTypedFunc(Func<object?[], object?> lambda, Type tIn, Type tOut)
    {
        Func<object?, object?> wrapper = x => lambda(new[] { x });
        try
        {
            var method = typeof(RazorExpressionParser).GetMethod(nameof(MakeFunc), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(tIn, tOut);
            return method.Invoke(null, new object[] { wrapper })!;
        }
        catch (InvalidOperationException) // AOT: generic instantiation not available
        {
            return wrapper; // fallback: return untyped wrapper
        }
    }

    // AOT hint: pre-instantiate common generic combinations
    private static Func<TIn, TOut> MakeFunc<TIn, TOut>(Func<object?, object?> f) =>
        x => { var r = f(x); return r is TOut t ? t : (TOut)(object)(r ?? default(TOut))!; };

    private static object CreateTypedFunc2(Func<object?[], object?> lambda, Type t1, Type t2, Type tOut)
    {
        Func<object?, object?, object?> wrapper = (a, b) => lambda(new[] { a, b });
        try
        {
            var method = typeof(RazorExpressionParser).GetMethod(nameof(MakeFunc2), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(t1, t2, tOut);
            return method.Invoke(null, new object[] { wrapper })!;
        }
        catch (InvalidOperationException)
        {
            return wrapper;
        }
    }

    private static Func<T1, T2, TOut> MakeFunc2<T1, T2, TOut>(Func<object?, object?, object?> f) =>
        (a, b) => { var r = f(a, b); return r is TOut t ? t : (TOut)(object)(r ?? default(TOut))!; };

    private static object CreateTypedPredicate(Func<object?[], object?> lambda, Type tIn)
    {
        Func<object?, object?> wrapper = x => lambda(new[] { x });
        try
        {
            var method = typeof(RazorExpressionParser).GetMethod(nameof(MakePredicate), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(tIn);
            return method.Invoke(null, new object[] { wrapper })!;
        }
        catch (InvalidOperationException)
        {
            return (Predicate<object?>)(x => wrapper(x) is true);
        }
    }

    private static Predicate<T> MakePredicate<T>(Func<object?, object?> f) =>
        x => f(x) is true;

    // Force AOT to preserve common MakeFunc instantiations
    [System.Diagnostics.CodeAnalysis.DynamicDependency(
        System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All, typeof(RazorExpressionParser))]
    private static void PreserveAotInstantiations()
    {
        // These calls are never executed but ensure the generic methods are compiled for AOT
        _ = MakeFunc<object, object>(null!);
        _ = MakeFunc<object, bool>(null!);
        _ = MakeFunc<object, int>(null!);
        _ = MakeFunc<object, string>(null!);
        _ = MakeFunc<object, double>(null!);
        _ = MakeFunc<int, object>(null!);
        _ = MakeFunc<int, bool>(null!);
        _ = MakeFunc<int, int>(null!);
        _ = MakeFunc<string, object>(null!);
        _ = MakeFunc<string, bool>(null!);
        _ = MakeFunc<string, string>(null!);
        _ = MakeFunc2<object, object, object>(null!);
        _ = MakePredicate<object>(null!);
        _ = MakePredicate<int>(null!);
        _ = MakePredicate<string>(null!);
    }

    private static object? TryInvokeLinqMethod(System.Collections.IEnumerable source, string methodName, object?[] args)
    {
        // Convert IEnumerable to object list for uniform handling
        var items = new List<object?>();
        foreach (var item in source) items.Add(item);

        if (args.Length == 1 && args[0] is Func<object?[], object?> lambda)
        {
            switch (methodName)
            {
                case "Select": return items.Select(x => lambda(new[] { x })).ToArray();
                case "Where": return items.Where(x => IsTruthy(lambda(new[] { x }))).ToArray();
                case "Any": return items.Any(x => IsTruthy(lambda(new[] { x })));
                case "All": return items.All(x => IsTruthy(lambda(new[] { x })));
                case "First": return items.First(x => IsTruthy(lambda(new[] { x })));
                case "FirstOrDefault": return items.FirstOrDefault(x => IsTruthy(lambda(new[] { x })));
                case "Last": return items.Last(x => IsTruthy(lambda(new[] { x })));
                case "LastOrDefault": return items.LastOrDefault(x => IsTruthy(lambda(new[] { x })));
                case "Count": return items.Count(x => IsTruthy(lambda(new[] { x })));
                case "OrderBy": return items.OrderBy(x => lambda(new[] { x })).ToArray();
                case "OrderByDescending": return items.OrderByDescending(x => lambda(new[] { x })).ToArray();
                case "Sum": return items.Sum(x => Convert.ToDouble(lambda(new[] { x }), CultureInfo.InvariantCulture));
                case "Min": return items.Min(x => lambda(new[] { x }));
                case "Max": return items.Max(x => lambda(new[] { x }));
                case "SkipWhile": return items.SkipWhile(x => IsTruthy(lambda(new[] { x }))).ToArray();
                case "TakeWhile": return items.TakeWhile(x => IsTruthy(lambda(new[] { x }))).ToArray();
                case "GroupBy": return items.GroupBy(x => lambda(new[] { x })).ToArray();
                case "Distinct": return items.Distinct().ToArray();
                case "SelectMany":
                    var flat = new List<object?>();
                    foreach (var item in items)
                    {
                        var sub = lambda(new[] { item });
                        if (sub is System.Collections.IEnumerable subEnum) foreach (var s in subEnum) flat.Add(s);
                        else flat.Add(sub);
                    }
                    return flat.ToArray();
                case "Aggregate" when items.Count > 0:
                    object? acc = items[0];
                    for (var idx = 1; idx < items.Count; idx++)
                        acc = lambda(new[] { acc, items[idx] });
                    return acc;
                case "Single": return items.Single(x => IsTruthy(lambda(new[] { x })));
                case "SingleOrDefault": return items.SingleOrDefault(x => IsTruthy(lambda(new[] { x })));
                case "Zip" when args.Length >= 1: break; // needs 2-arg lambda, handled below
            }
        }
        else if (args.Length == 0)
        {
            switch (methodName)
            {
                case "Any": return items.Any();
                case "Count": return items.Count;
                case "First": return items.First();
                case "FirstOrDefault": return items.FirstOrDefault();
                case "Last": return items.Last();
                case "LastOrDefault": return items.LastOrDefault();
                case "Single" when items.Count == 1: return items[0];
                case "SingleOrDefault" when items.Count <= 1: return items.Count == 1 ? items[0] : null;
                case "ToArray": return items.ToArray();
                case "ToList": return items;
                case "Reverse": items.Reverse(); return items.ToArray();
                case "Sum": return items.Sum(x => Convert.ToDouble(x, CultureInfo.InvariantCulture));
                case "Min": return items.Min();
                case "Max": return items.Max();
                case "Average": return items.Average(x => Convert.ToDouble(x, CultureInfo.InvariantCulture));
                case "Distinct": return items.Distinct().ToArray();
                case "Order": return items.OrderBy(x => x).ToArray();
                case "OrderDescending": return items.OrderByDescending(x => x).ToArray();
                case "AsEnumerable": return items.ToArray();
                case "Cast": return items.ToArray();
            }
        }
        else if (args.Length == 1)
        {
            switch (methodName)
            {
                case "Contains": return items.Contains(args[0]);
                case "Skip" when args[0] is int n: return items.Skip(n).ToArray();
                case "Take" when args[0] is int n: return items.Take(n).ToArray();
                case "ElementAt" when args[0] is int n: return items.ElementAt(n);
                case "ElementAtOrDefault" when args[0] is int n: return n >= 0 && n < items.Count ? items[n] : null;
                case "Append": return items.Append(args[0]).ToArray();
                case "Prepend": return items.Prepend(args[0]).ToArray();
                case "Concat" when args[0] is System.Collections.IEnumerable other:
                    var concatList = new List<object?>(items);
                    foreach (var o in other) concatList.Add(o);
                    return concatList.ToArray();
                case "Union" when args[0] is System.Collections.IEnumerable unionOther:
                    var unionSet = new List<object?>(items);
                    foreach (var o in unionOther) if (!unionSet.Contains(o)) unionSet.Add(o);
                    return unionSet.ToArray();
                case "Intersect" when args[0] is System.Collections.IEnumerable intersectOther:
                    var otherSet = new HashSet<object?>();
                    foreach (var o in intersectOther) otherSet.Add(o);
                    return items.Where(x => otherSet.Contains(x)).ToArray();
                case "Except" when args[0] is System.Collections.IEnumerable exceptOther:
                    var exceptSet = new HashSet<object?>();
                    foreach (var o in exceptOther) exceptSet.Add(o);
                    return items.Where(x => !exceptSet.Contains(x)).ToArray();
                case "SequenceEqual" when args[0] is System.Collections.IEnumerable seqOther:
                    var otherList = new List<object?>();
                    foreach (var o in seqOther) otherList.Add(o);
                    return items.Count == otherList.Count && !items.Where((t, i) => !Equals(t, otherList[i])).Any();
                case "DefaultIfEmpty": return items.Count == 0 ? new List<object?> { args[0] }.ToArray() : items.ToArray();
            }
        }
        // Two-arg: Aggregate with seed, Zip
        if (args.Length == 2 && args[1] is Func<object?[], object?> lambda2)
        {
            switch (methodName)
            {
                case "Aggregate":
                    var seed = args[0];
                    foreach (var item in items)
                        seed = lambda2(new[] { seed, item });
                    return seed;
                case "Zip" when args[0] is System.Collections.IEnumerable zipOther:
                    var zipItems = new List<object?>();
                    foreach (var o in zipOther) zipItems.Add(o);
                    return items.Select((item, i) => i < zipItems.Count ? lambda2(new[] { item, zipItems[i] }) : null)
                        .Where(x => x != null).ToArray();
            }
        }

        return null;
    }

    private static object? GetIndexer(object? target, object? index)
    {
        if (target == null) return null;

        // Index from end: ^n
        if (index is Index fromEnd && target is System.Collections.IList indexList)
            return indexList[fromEnd.GetOffset(indexList.Count)];

        // Range slicing: start..end
        if (index is Range range)
        {
            if (target is string str)
                return str[range];
            if (target is Array rangeArr)
            {
                var (offset, length) = range.GetOffsetAndLength(rangeArr.Length);
                var slice = Array.CreateInstance(rangeArr.GetType().GetElementType()!, length);
                Array.Copy(rangeArr, offset, slice, 0, length);
                return slice;
            }
            if (target is System.Collections.IList rangeList)
            {
                var (offset, length) = range.GetOffsetAndLength(rangeList.Count);
                var result = new object?[length];
                for (var i = 0; i < length; i++) result[i] = rangeList[offset + i];
                return result;
            }
        }

        if (target is Array arr && index is int idx) return arr.GetValue(idx);
        if (target is System.Collections.IList list && index is int listIdx) return list[listIdx];
        if (target is System.Collections.IDictionary dict) return dict[index!];

        // Try indexer property
        var prop = target.GetType().GetProperties().FirstOrDefault(p => p.GetIndexParameters().Length > 0);
        if (prop != null)
        {
            try { return prop.GetValue(target, new[] { index }); }
            catch { }
        }
        return null;
    }
}

// ────────────────────── Cached Expression Evaluator ────────────────────────

/// <summary>
/// AOT-safe expression evaluator that uses a recursive-descent parser with
/// <c>dynamic</c>-free reflection-based evaluation. No Roslyn dependency.
/// </summary>
internal static class RazorLightweightExpressionEvaluator
{
    // Two-generation cache: when current fills up, previous is discarded and current becomes previous.
    // This keeps hot entries alive across evictions instead of losing everything on Clear().
    private static ConcurrentDictionary<string, List<RazorToken>> _current = new(StringComparer.Ordinal);
    private static ConcurrentDictionary<string, List<RazorToken>> _previous = new(StringComparer.Ordinal);
    private const int MaxCacheSize = 2048;

    public static object? Evaluate(string expression, Func<string, object?> resolver)
    {
        if (!_current.TryGetValue(expression, out var tokens))
        {
            // Check previous generation and promote if found
            if (_previous.TryGetValue(expression, out tokens))
            {
                _current.TryAdd(expression, tokens);
            }
            else
            {
                tokens = new RazorTokenizer(expression).Tokenize();
                _current.TryAdd(expression, tokens);
            }
        }

        // Rotate generations when current is full
        if (_current.Count > MaxCacheSize)
        {
            _previous = _current;
            _current = new ConcurrentDictionary<string, List<RazorToken>>(StringComparer.Ordinal);
        }

        var parser = new RazorExpressionParser(tokens);
        return parser.ParseAndEvaluate(resolver);
    }
}
