namespace Jalium.UI.Styling;

internal enum CssCombinator : byte
{
    /// <summary>Whitespace combinator: any ancestor.</summary>
    Descendant,

    /// <summary>'>' combinator: direct parent.</summary>
    Child,
    AdjacentSibling,
    GeneralSibling,
}

/// <summary>
/// Native CSS pseudo-classes supported by the engine. Names follow CSS exactly
/// (:active maps to the framework's IsPressed state).
/// </summary>
internal enum CssPseudoClass : byte
{
    Hover,
    Active,
    Focus,
    FocusVisible,
    FocusWithin,
    Disabled,
    Enabled,
    Checked,
    Indeterminate,
    Root,
    Scope,
    Empty,
    FirstChild,
    LastChild,
    OnlyChild,
    FirstOfType,
    LastOfType,
    OnlyOfType,
    NestingScope,
    RelativeAnchor,
}

/// <summary>Bit set of dynamic state sources a selector depends on.</summary>
[Flags]
internal enum CssStateMask : ushort
{
    None = 0,
    Hover = 1 << 0,
    Active = 1 << 1,
    Focus = 1 << 2,
    KeyboardFocus = 1 << 3,
    FocusWithin = 1 << 4,
    Enabled = 1 << 5,
    Checked = 1 << 6,
}

/// <summary>A compound selector: one simple-selector sequence such as <c>Button.primary#ok:hover</c>.</summary>
internal sealed class CssCompound
{
    public string? TypeName;
    public bool Universal;
    public string? NamespaceUri;
    public bool ExpandedTypeName;
    public bool ExplicitTypeSelector;
    public bool DefaultNamespaceApplied;
    public string? Id;
    public string[]? Classes;
    public CssPseudoClass[]? Pseudos;
    public CssStateMask StateMask;
    public CssAttributeSelector[]? Attributes;
    public CssFunctionalPseudo[]? Functions;

    /// <summary>Lazily resolved element type; <see cref="CssSelectorMatching.UnresolvableType"/> marks a failed lookup.</summary>
    public Type? ResolvedType;

    public static CssStateMask ToStateMask(CssPseudoClass pseudo) => pseudo switch
    {
        CssPseudoClass.Hover => CssStateMask.Hover,
        CssPseudoClass.Active => CssStateMask.Active,
        CssPseudoClass.Focus => CssStateMask.Focus,
        CssPseudoClass.FocusVisible => CssStateMask.KeyboardFocus,
        CssPseudoClass.FocusWithin => CssStateMask.FocusWithin,
        CssPseudoClass.Disabled => CssStateMask.Enabled,
        CssPseudoClass.Enabled => CssStateMask.Enabled,
        CssPseudoClass.Checked => CssStateMask.Checked,
        CssPseudoClass.Indeterminate => CssStateMask.Checked,
        _ => CssStateMask.None,
    };
}

/// <summary>A complex selector: a chain of compounds joined by combinators. Compounds[0] is the rightmost.</summary>
internal sealed class CssSelector
{
    /// <summary>Index 0 is the subject (rightmost) compound; matching walks up from there.</summary>
    public required CssCompound[] Compounds;

    /// <summary>Combinators[i] joins Compounds[i] (right) to Compounds[i + 1] (left).</summary>
    public required CssCombinator[] Combinators;

    /// <summary>Packed specificity: (ids &lt;&lt; 20) | (classes+pseudos &lt;&lt; 10) | types, each saturated at 1023.</summary>
    public required int Specificity;

    public CssStateMask RightmostStates;
    public CssStateMask AncestorStates;
    internal bool ContainsNesting;
    internal bool ContainsScope;

    public bool HasCombinators => Compounds.Length > 1;
    public bool HasStructuralDependencies => Combinators.Any(c => c is CssCombinator.AdjacentSibling or CssCombinator.GeneralSibling) ||
        Compounds.Any(c => c.Attributes is { Length: > 0 } || c.Functions is { Length: > 0 } ||
            c.Pseudos?.Any(p => p >= CssPseudoClass.Scope) == true);

    public bool HasAnyState => (RightmostStates | AncestorStates) != CssStateMask.None;

    public IEnumerable<string> AttributeNames()
    {
        var pending = new Stack<CssSelector>(); pending.Push(this);
        var visited = new HashSet<CssSelector>();
        while (pending.TryPop(out var selector))
        {
            if (!visited.Add(selector)) continue;
            foreach (var compound in selector.Compounds)
            {
                if (compound.Attributes is { } attributes)
                    foreach (var attribute in attributes) yield return attribute.Name;
                if (compound.Functions is { } functions)
                    foreach (var function in functions)
                        foreach (var child in function.Selectors) pending.Push(child);
            }
        }
    }

    public static int PackSpecificity(int ids, int classesAndPseudos, int types)
        => (Math.Min(ids, 1023) << 20) | (Math.Min(classesAndPseudos, 1023) << 10) | Math.Min(types, 1023);
}

internal sealed record CssAttributeSelector(string Name, string Operator, string Value, bool IgnoreCase, string? NamespaceUri = "");
internal sealed record CssFunctionalPseudo(string Name, CssSelector[] Selectors, int A = 0, int B = 0, CssScopeRule? NestingScope = null);

internal enum CssRelativeSelectorMode { None, Nesting, Scope, Has }
internal sealed record CssNestingContext(CssSelector[] Selectors, CssScopeRule? Scope);

internal static class CssSelectorMatching
{
    /// <summary>Sentinel cached on <see cref="CssCompound.ResolvedType"/> when the type name cannot be resolved.</summary>
    internal sealed class Unresolvable { }

    internal static readonly Type UnresolvableType = typeof(Unresolvable);
}
