namespace Jalium.UI.Styling;

internal readonly record struct CssMatchContext(CssNode? Scope, CssNode? SheetScope = null,
    CssScopeBinding? Bindings = null, CssNode? RelativeAnchor = null, CssMatchSession? Session = null, CssSelectorDependent? Observer = null)
{
    internal CssNode? ScopeFor(CssScopeRule? definition) => definition is null ? SheetScope : Bindings?.Find(definition);
    internal void Observe(CssNode node, string? property = null) => Observer?.Observe(node, property);
}

/// <summary>One matching pass memoizes the parent selector DAG instead of expanding nested selector lists.</summary>
internal sealed class CssMatchSession
{
    internal readonly record struct Key(CssNode Node, CssSelector Selector, bool States, CssNode? Scope,
        CssNode? SheetScope, CssScopeBinding? Bindings, CssNode? RelativeAnchor);
    internal readonly Dictionary<Key, bool> Results = [];
    internal readonly record struct ScopeKey(CssScopeRule Scope, CssNode Node, CssNode? SheetScope, bool States);
    internal readonly Dictionary<ScopeKey, IReadOnlyList<CssScopeBinding>> Scopes = [];
}

internal sealed record CssScopeBinding(CssScopeRule Definition, CssNode Root, CssScopeBinding? Parent)
{
    internal CssNode? Find(CssScopeRule definition)
    {
        for (var binding = this; binding is not null; binding = binding.Parent)
            if (ReferenceEquals(binding.Definition, definition)) return binding.Root;
        return null;
    }
}
