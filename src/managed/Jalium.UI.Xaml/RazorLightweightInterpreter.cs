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
    StringLiteral, CharLiteral, True, False, Null,

    // Identifiers & keywords
    Identifier,
    Var, Int, Long, Double, Float, Decimal, Bool, String, Char, Object, Dynamic,
    If, Else, For, Foreach, In, While, Do, Switch, Case, Default, Break, Continue, Return,
    Try, Catch, Finally, Throw, New, Typeof, Is, As, Nameof,

    // Operators
    Plus, Minus, Star, Slash, Percent,
    Equals, NotEquals, Less, Greater, LessEquals, GreaterEquals,
    And, Or, Not, QuestionQuestion, QuestionDot,
    Assign, PlusAssign, MinusAssign, StarAssign, SlashAssign, PercentAssign,
    PlusPlus, MinusMinus,
    Question, Colon, Dot, Comma, Semicolon, Arrow,

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

        // String literals
        if (c == '"' || (c == '$' && _pos + 1 < _source.Length && _source[_pos + 1] == '"')
                     || (c == '@' && _pos + 1 < _source.Length && _source[_pos + 1] == '"'))
            return ReadStringLiteral(start);

        // Char literal
        if (c == '\'') return ReadCharLiteral(start);

        // Number
        if (char.IsDigit(c) || (c == '.' && _pos + 1 < _source.Length && char.IsDigit(_source[_pos + 1])))
            return ReadNumber(start);

        // Identifier or keyword
        if (char.IsLetter(c) || c == '_') return ReadIdentifierOrKeyword(start);

        // Operators & delimiters
        return ReadOperator(start);
    }

    private RazorToken ReadStringLiteral(int start)
    {
        var isInterpolated = _source[_pos] == '$';
        var isVerbatim = _source[_pos] == '@';
        if (isInterpolated || isVerbatim) _pos++;
        _pos++; // skip opening "

        var sb = new StringBuilder();
        while (_pos < _source.Length)
        {
            var c = _source[_pos];
            if (isVerbatim)
            {
                if (c == '"') { if (_pos + 1 < _source.Length && _source[_pos + 1] == '"') { sb.Append('"'); _pos += 2; continue; } _pos++; break; }
                sb.Append(c); _pos++;
            }
            else
            {
                if (c == '\\') { sb.Append(ReadEscapeSequence()); continue; }
                if (c == '"') { _pos++; break; }
                sb.Append(c); _pos++;
            }
        }
        return new RazorToken(RazorTokenKind.StringLiteral, sb.ToString(), start);
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
            _ => c
        };
    }

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
        var hasDecimal = false;
        while (_pos < _source.Length && (char.IsDigit(_source[_pos]) || _source[_pos] == '.'))
        {
            if (_source[_pos] == '.') { if (hasDecimal) break; hasDecimal = true; }
            _pos++;
        }

        var suffix = RazorTokenKind.IntLiteral;
        if (_pos < _source.Length)
        {
            var s = char.ToLower(_source[_pos]);
            if (s == 'l') { suffix = RazorTokenKind.LongLiteral; _pos++; }
            else if (s == 'f') { suffix = RazorTokenKind.FloatLiteral; _pos++; }
            else if (s == 'd') { suffix = RazorTokenKind.DoubleLiteral; _pos++; }
            else if (s == 'm') { suffix = RazorTokenKind.DecimalLiteral; _pos++; }
            else if (hasDecimal) suffix = RazorTokenKind.DoubleLiteral;
        }
        else if (hasDecimal) suffix = RazorTokenKind.DoubleLiteral;

        return new RazorToken(suffix, _source[start.._pos], start);
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
            _ => RazorTokenKind.Identifier
        };
        return new RazorToken(kind, text, start);
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
            '<' when next == '=' => Advance(RazorTokenKind.LessEquals, "<="),
            '<' => new RazorToken(RazorTokenKind.Less, "<", start),
            '>' when next == '=' => Advance(RazorTokenKind.GreaterEquals, ">="),
            '>' => new RazorToken(RazorTokenKind.Greater, ">", start),
            '&' when next == '&' => Advance(RazorTokenKind.And, "&&"),
            '|' when next == '|' => Advance(RazorTokenKind.Or, "||"),
            '?' when next == '?' => Advance(RazorTokenKind.QuestionQuestion, "??"),
            '?' when next == '.' => Advance(RazorTokenKind.QuestionDot, "?."),
            '?' => new RazorToken(RazorTokenKind.Question, "?", start),
            ':' => new RazorToken(RazorTokenKind.Colon, ":", start),
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
}

// ──────────────────────────── Expression Parser ────────────────────────────

internal sealed class RazorExpressionParser
{
    private readonly List<RazorToken> _tokens;
    private int _pos;

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
        var value = EvalExpression(resolver);
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
            if (value != null) { EvalOr(resolver); continue; } // short-circuit: skip but consume tokens
            value = EvalOr(resolver);
        }
        return value;
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
        var value = EvalEquality(resolver);
        while (Match(RazorTokenKind.And))
        { var right = EvalEquality(resolver); value = IsTruthy(value) && IsTruthy(right); }
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
        var value = EvalAdditive(resolver);
        while (true)
        {
            if (Match(RazorTokenKind.Less)) { var r = EvalAdditive(resolver); value = Compare(value, r) < 0; }
            else if (Match(RazorTokenKind.Greater)) { var r = EvalAdditive(resolver); value = Compare(value, r) > 0; }
            else if (Match(RazorTokenKind.LessEquals)) { var r = EvalAdditive(resolver); value = Compare(value, r) <= 0; }
            else if (Match(RazorTokenKind.GreaterEquals)) { var r = EvalAdditive(resolver); value = Compare(value, r) >= 0; }
            else if (Current.Kind == RazorTokenKind.Is) { Consume(); value = EvalIsExpression(value); }
            else if (Current.Kind == RazorTokenKind.As) { Consume(); var type = ResolveTypeName(); value = type != null && type.IsInstanceOfType(value!) ? value : null; }
            else break;
        }
        return value;
    }

    private object? EvalIsExpression(object? value)
    {
        var type = ResolveTypeName();
        return type != null && value != null && type.IsInstanceOfType(value);
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
        if (Match(RazorTokenKind.Not)) return !IsTruthy(EvalUnary(resolver));
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
                        value = EvalStaticMethodCall(staticType, member, resolver);
                    else
                        value = GetStaticMember(staticType, member);
                }
                else if (Current.Kind == RazorTokenKind.OpenParen)
                    value = EvalMethodCall(value, member, resolver);
                else
                    value = GetMember(value, member);
            }
            else if (Match(RazorTokenKind.QuestionDot))
            {
                if (value == null) { SkipMemberChain(); return null; }
                var member = Expect(RazorTokenKind.Identifier).Value;
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
            else break;
        }
        return value;
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
        if (Current.Kind != RazorTokenKind.CloseParen)
        {
            args.Add(EvalExpression(resolver));
            while (Match(RazorTokenKind.Comma))
                args.Add(EvalExpression(resolver));
        }
        Expect(RazorTokenKind.CloseParen);

        if (target == null)
            return null;

        return InvokeMethod(target, methodName, args.ToArray());
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
            case RazorTokenKind.CharLiteral: Consume(); return token.Value.Length > 0 ? token.Value[0] : '\0';
            case RazorTokenKind.True: Consume(); return true;
            case RazorTokenKind.False: Consume(); return false;
            case RazorTokenKind.Null: Consume(); return null;

            case RazorTokenKind.OpenParen:
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

        // new Type { Prop = val }
        if (Current.Kind == RazorTokenKind.OpenBrace && newType != null)
        {
            Consume();
            var obj = Activator.CreateInstance(newType);
            while (Current.Kind != RazorTokenKind.CloseBrace && Current.Kind != RazorTokenKind.Eof)
            {
                var prop = Expect(RazorTokenKind.Identifier).Value;
                Expect(RazorTokenKind.Assign);
                var propVal = EvalExpression(resolver);
                newType.GetProperty(prop)?.SetValue(obj, propVal);
                Match(RazorTokenKind.Comma);
            }
            Expect(RazorTokenKind.CloseBrace);
            return obj;
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

        // IO & environment
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

        // Fallback: check build-time registered namespace types, then runtime resolution
        _ => TryResolveTypeByName(name)
    };

    private static Type? TryResolveTypeByName(string name)
    {
        // First check types registered from @using directives at build time
        var registered = RazorExpressionRegistry.TryResolveRegisteredType(name);
        if (registered != null) return registered;

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

    private static object? ArithmeticOp(object? left, object? right, char op)
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
        var type = target.GetType();

        // Try exact match first (cached)
        var methods = ReflectionCache.GetMethods(type, methodName, args.Length, BindingFlags.Public | BindingFlags.Instance);

        foreach (var method in methods)
        {
            try
            {
                var parameters = method.GetParameters();
                var convertedArgs = new object?[args.Length];
                for (var i = 0; i < args.Length; i++)
                {
                    if (args[i] == null || parameters[i].ParameterType.IsInstanceOfType(args[i]!))
                        convertedArgs[i] = args[i];
                    else
                        convertedArgs[i] = Convert.ChangeType(args[i], parameters[i].ParameterType, CultureInfo.InvariantCulture);
                }
                return method.Invoke(target, convertedArgs);
            }
            catch { }
        }

        // Well-known methods fallback
        return methodName switch
        {
            "ToString" => target.ToString(),
            "GetType" => target.GetType(),
            "Equals" when args.Length == 1 => target.Equals(args[0]),
            "GetHashCode" => target.GetHashCode(),
            _ => null
        };
    }

    private static object? GetIndexer(object? target, object? index)
    {
        if (target == null) return null;
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
