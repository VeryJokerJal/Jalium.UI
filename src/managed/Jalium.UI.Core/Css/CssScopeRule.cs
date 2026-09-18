namespace Jalium.UI.Styling;

/// <summary>Immutable scope syntax; roots and limits are matched against the native tree at evaluation time.</summary>
internal sealed class CssScopeRule(CssSelector[]? starts, CssSelector[]? limits, CssScopeRule? parent)
{
    internal CssSelector[]? Starts { get; } = starts;
    internal CssSelector[]? Limits { get; private set; } = limits;
    internal CssScopeRule? Parent { get; } = parent;

    internal IEnumerable<CssSelector> Selectors()
    {
        for (var scope = this; scope is not null; scope = scope.Parent)
        {
            if (scope.Starts is { } startSelectors) foreach (var selector in startSelectors) yield return selector;
            if (scope.Limits is { } limitSelectors) foreach (var selector in limitSelectors) yield return selector;
        }
    }

    internal static CssScopeRule? Parse(string header, CssNestingContext? nesting, CssScopeRule? parent,CssNamespaceContext? namespaces = null)
    {
        var reader = new CssTokenReader(header);
        CssSelector[]? starts = null;
        if (reader.TryPeekChar(out var first) && first == '(')
        {
            if (!ReadParentheses(ref reader, out var start)) return null;
            var mode = nesting is not null ? CssRelativeSelectorMode.Nesting : parent is not null ? CssRelativeSelectorMode.Scope : CssRelativeSelectorMode.None;
            var selectors = CssSelectorParser.ParseGroup(start, out _, nesting, mode,namespaces);
            if (selectors is null) return null;
            starts = selectors.ToArray();
        }
        else if (nesting is not null)
            starts = CssSelectorParser.ParseGroup("&", out _, nesting,namespaces:namespaces)!.ToArray();
        var definition = new CssScopeRule(starts, null, parent);
        if (!reader.AtEnd)
        {
            if (!reader.TryReadIdent(out var to) || !to.Equals("to", StringComparison.OrdinalIgnoreCase) ||
                !ReadParentheses(ref reader, out var end) || !reader.AtEnd) return null;
            var selectors = CssSelectorParser.ParseGroup(end, out _, relative: CssRelativeSelectorMode.Scope,namespaces:namespaces);
            if (selectors is null) return null;
            definition.Limits = selectors.ToArray();
        }
        return definition;
    }

    private static bool ReadParentheses(ref CssTokenReader reader, out string value)
    {
        value = string.Empty;
        var text = reader.Remaining;
        if (text.IsEmpty || text[0] != '(') return false;
        var depth = 1; var position = 1;
        for (; position < text.Length; position++)
        {
            if(text[position]=='/' && position+1<text.Length && text[position+1]=='*') {position=CssTokenReader.SkipComment(text,position)-1; continue;}
            if (text[position] is '\'' or '"') { position = CssTokenReader.SkipString(text, position) - 1; continue; }
            if (text[position] == '\\') { if (!CssSyntax.ReadEscape(text, ref position, out _)) return false; position--; continue; }
            if (text[position] == '(') depth++;
            else if (text[position] == ')' && --depth == 0) break;
        }
        if (depth != 0) return false;
        value = text[1..position].ToString();
        reader = new CssTokenReader(text[(position + 1)..]);
        return value.Trim().Length > 0;
    }

    internal IReadOnlyList<CssScopeBinding> Bindings(CssNode element, CssNode? sheetScope, bool states, CssMatchSession session, CssSelectorDependent? observer = null)
    {
        var key = new CssMatchSession.ScopeKey(this, element, sheetScope, states);
        if (session.Scopes.TryGetValue(key, out var cached)) return cached;
        var result = new List<CssScopeBinding>();
        var roots = new HashSet<CssNode>();
        IEnumerable<CssScopeBinding?> parents;
        if (Parent is null) parents = new CssScopeBinding?[] { null };
        else parents = Parent.Bindings(element, sheetScope, states, session, observer);
        foreach (var outer in parents)
        {
            var context = new CssMatchContext(outer?.Root ?? sheetScope, sheetScope, outer, Session: session, Observer: observer);
            var implicitRoot = sheetScope ?? CssMatcher.Root(element);
            var depth = 0;
            for (var root = element; root is not null && depth++ < 4096; root = CssMatcher.CssAncestor(root))
            {
                if (roots.Contains(root)) { if (ReferenceEquals(root, outer?.Root)) break; continue; }
                var matches = Starts is null ? ReferenceEquals(root, implicitRoot) : Starts.Any(selector => CssMatcher.Matches(root, selector, states, context));
                if (matches)
                {
                    var binding = new CssScopeBinding(this, root, outer);
                    var scoped = context with { Scope = root, Bindings = binding };
                    var limited = false;
                    if (Limits is { Length: > 0 })
                    {
                        for (var current = element; current is not null; current = CssMatcher.CssAncestor(current))
                        {
                            if (Limits.Any(selector => CssMatcher.Matches(current, selector, states, scoped))) { limited = true; break; }
                            if (ReferenceEquals(current, root)) break;
                        }
                    }
                    if (!limited)
                    {
                        // A scope resets the body nesting context. One successful outer
                        // chain per innermost root suffices; do not enumerate combinations.
                        roots.Add(root); result.Add(binding);
                    }
                }
                if (ReferenceEquals(root, outer?.Root)) break;
            }
        }
        session.Scopes[key] = result;
        return result;
    }

    internal static int Distance(CssNode element, CssNode root)
    {
        var hops = 0;
        for (var current = element; current is not null && hops < 4096; current = CssMatcher.CssAncestor(current), hops++)
            if (ReferenceEquals(current, root)) return hops;
        return int.MaxValue;
    }
}
