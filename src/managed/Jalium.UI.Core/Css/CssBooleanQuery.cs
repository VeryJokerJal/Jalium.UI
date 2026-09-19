namespace Jalium.UI.Styling;

internal enum CssQueryResult { False, True, Unknown }

/// <summary>Token-aware boolean grammar shared by media and feature-support conditions.</summary>
internal abstract record CssBooleanQuery
{
    internal abstract CssQueryResult Evaluate(Func<string, CssQueryResult> feature);
    internal sealed record Constant(CssQueryResult Value) : CssBooleanQuery
    { internal override CssQueryResult Evaluate(Func<string, CssQueryResult> feature) => Value; }
    internal sealed record Feature(string Text) : CssBooleanQuery
    { internal override CssQueryResult Evaluate(Func<string, CssQueryResult> feature) => feature(Text); }
    internal sealed record Operation(string Name, CssBooleanQuery Left, CssBooleanQuery? Right = null) : CssBooleanQuery
    {
        internal override CssQueryResult Evaluate(Func<string, CssQueryResult> feature)
        {
            var left = Left.Evaluate(feature);
            if (Name == "not") return left == CssQueryResult.Unknown ? left : left == CssQueryResult.True ? CssQueryResult.False : CssQueryResult.True;
            var right = Right!.Evaluate(feature);
            if (Name == "and") return left == CssQueryResult.False || right == CssQueryResult.False ? CssQueryResult.False
                : left == CssQueryResult.Unknown || right == CssQueryResult.Unknown ? CssQueryResult.Unknown : CssQueryResult.True;
            return left == CssQueryResult.True || right == CssQueryResult.True ? CssQueryResult.True
                : left == CssQueryResult.Unknown || right == CssQueryResult.Unknown ? CssQueryResult.Unknown : CssQueryResult.False;
        }
    }

    internal static CssBooleanQuery? Parse(string text, int depth = 0)
    {
        if (depth > 64) return null;
        var reader = new CssTokenReader(text);
        var probe = reader;
        if (probe.TryReadIdent(out var keyword) && keyword.Equals("not",StringComparison.OrdinalIgnoreCase))
        {
            reader = probe;
            var operand = ReadAtom(ref reader, depth + 1);
            return operand is not null && reader.AtEnd ? new Operation("not",operand) : null;
        }
        var result = ReadAtom(ref reader,depth + 1);
        if (result is null) return null;
        string? operation = null;
        while (!reader.AtEnd)
        {
            if (!reader.TryReadIdent(out var op)) return null;
            var current = op.ToString().ToLowerInvariant();
            if (current is not ("and" or "or") || operation is not null && current != operation) return null;
            operation = current;
            var right = ReadAtom(ref reader,depth + 1);
            if (right is null) return null;
            result = new Operation(operation,result,right);
        }
        return result;
    }

    private static CssBooleanQuery? ReadAtom(ref CssTokenReader reader, int depth)
    {
        if (depth > 64) return null;
        if (reader.TryReadFunction(out var name,out var arguments))
            return new Feature(name.ToString() + "(" + arguments.Remaining.ToString() + ")");
        var text = reader.Remaining;
        if (text.IsEmpty || text[0] != '(') return null;
        var nesting = 1; var end = 1;
        for (;end < text.Length;end++)
        {
            if(text[end]=='/' && end+1<text.Length && text[end+1]=='*') {end=CssTokenReader.SkipComment(text,end)-1; continue;}
            if (text[end] is '\'' or '"') { end = CssTokenReader.SkipString(text,end)-1; continue; }
            if (text[end] == '\\') { if (!CssSyntax.ReadEscape(text,ref end,out _)) return null; end--; continue; }
            if (text[end] == '(') nesting++;
            else if (text[end] == ')' && --nesting == 0) break;
        }
        if (nesting != 0) return null;
        var inner = text[1..end].Trim().ToString();
        if (inner.Length == 0 || !CssDeclarationValue.IsValid("("+inner+")")) return null;
        reader = new CssTokenReader(text[(end+1)..]);
        return Parse(inner,depth + 1) ?? new Feature(inner);
    }
}

internal sealed class CssSupportsQuery(CssBooleanQuery expression,CssNamespaceContext? namespaces)
{
    internal static CssSupportsQuery? Parse(string text, bool allowDeclaration = false,CssNamespaceContext? namespaces = null)
    {
        text = text.Trim();
        if (allowDeclaration && IsDeclaration(text)) return new(new CssBooleanQuery.Feature(text),namespaces);
        var expression = CssBooleanQuery.Parse(text);
        return expression is null ? null : new(expression,namespaces);
    }

    private static bool IsDeclaration(string text)
    {
        var reader = new CssTokenReader(text);
        if (!reader.TryReadIdent(out _) || !reader.TryReadDelimiter(':')) return false;
        var value=reader.Remaining;
        CssParser.TryStripImportant(value,out value);
        return CssDeclarationValue.IsValid(value.ToString());
    }

    internal bool Evaluate()
    {
        return expression.Evaluate(Feature)==CssQueryResult.True;
    }

    private CssQueryResult Feature(string text)
    {
        var reader = new CssTokenReader(text);
        if (reader.TryReadFunction(out var name,out var args) && reader.AtEnd)
        {
            var supported=false;
            if(name.Equals("selector",StringComparison.OrdinalIgnoreCase))
            {
                supported=CssSelectorParser.IsSupported(args.Remaining,namespaces,out _);
            }
            if (name.Equals("at-rule",StringComparison.OrdinalIgnoreCase))
                supported=args.TryReadDelimiter('@') && args.TryReadIdent(out var rule) && args.AtEnd && CssParser.SupportsAtRule(rule.ToString());
            return supported ? CssQueryResult.True : CssQueryResult.False;
        }
        reader = new CssTokenReader(text);
        if (!reader.TryReadIdent(out var property) || !reader.TryReadDelimiter(':')) return CssQueryResult.False;
        var value=reader.Remaining;
        CssParser.TryStripImportant(value,out value);
        return CssDeclarationValue.IsValid(value.ToString()) && Css.Supports(property.ToString(),value.ToString()) ? CssQueryResult.True : CssQueryResult.False;
    }
}
