namespace Jalium.UI.Styling;

/// <summary>
/// Selector matching. The CSS ancestor axis follows the logical tree (FrameworkParent) and
/// treats template-generated parts as transparent: they neither match page-level rules nor
/// appear as ancestors for combinators — matching the author's document-tree mental model
/// and the WPF implicit-style template boundary.
/// </summary>
internal static partial class CssMatcher
{
    /// <summary>Template-generated parts do not participate in page-level CSS matching.</summary>
    public static bool IsCssMatchable(CssNode element)
        => element.TemplatedParent is null;

    public static CssNode? CssAncestor(CssNode element)
    {
        var current = element.FrameworkParent;
        while (current is not null && current.TemplatedParent is not null)
        {
            current = current.FrameworkParent;
        }

        return current;
    }

    public static bool Matches(CssNode element, CssSelector selector, bool evaluateStates, CssNode? scope = null)
        => Matches(element, selector, evaluateStates, new CssMatchContext(scope, scope));

    internal static bool Matches(CssNode element, CssSelector selector, bool evaluateStates, CssMatchContext context)
    {
        context.Observe(element);
        if (selector.ContainsNesting && context.Session is null) context = context with { Session = new() };
        var key = new CssMatchSession.Key(element, selector, evaluateStates, context.Scope, context.SheetScope, context.Bindings, context.RelativeAnchor);
        if (context.Session?.Results.TryGetValue(key, out var cached) == true) return cached;
        var matches = MatchesCompound(element, selector.Compounds[0], evaluateStates, context) &&
            MatchesAncestors(element, selector, 1, evaluateStates, context);
        if (context.Session is { } session) session.Results[key] = matches;
        return matches;
    }

    private static bool MatchesAncestors(CssNode element, CssSelector selector, int index, bool evaluateStates, CssMatchContext context)
    {
        if (index >= selector.Compounds.Length)
        {
            return true;
        }

        var combinator = selector.Combinators[index - 1];
        if (CssAncestor(element) is { } observedParent) context.Observe(observedParent);
        if (combinator is CssCombinator.AdjacentSibling or CssCombinator.GeneralSibling)
        {
            var previous = PreviousSibling(element);
            while (previous is not null)
            {
                if (MatchesCompound(previous, selector.Compounds[index], evaluateStates, context) &&
                    MatchesAncestors(previous, selector, index + 1, evaluateStates, context)) return true;
                if (combinator == CssCombinator.AdjacentSibling) break;
                previous = PreviousSibling(previous);
            }
            return false;
        }
        var ancestor = CssAncestor(element);
        if (combinator == CssCombinator.Child)
        {
            return ancestor is not null &&
                   MatchesCompound(ancestor, selector.Compounds[index], evaluateStates, context) &&
                   MatchesAncestors(ancestor, selector, index + 1, evaluateStates, context);
        }

        while (ancestor is not null)
        {
            if (MatchesCompound(ancestor, selector.Compounds[index], evaluateStates, context) &&
                MatchesAncestors(ancestor, selector, index + 1, evaluateStates, context))
            {
                return true;
            }

            ancestor = CssAncestor(ancestor);
        }

        return false;
    }

    internal static bool MatchesCompound(CssNode element, CssCompound compound, bool evaluateStates, CssNode? scope = null)
        => MatchesCompound(element, compound, evaluateStates, new CssMatchContext(scope, scope));

    private static bool MatchesCompound(CssNode element, CssCompound compound, bool evaluateStates, CssMatchContext context)
    {
        context.Observe(element);
        if(compound.NamespaceUri is not null && element.ExpandedName.NamespaceUri!=compound.NamespaceUri) return false;
        if (compound.TypeName is not null)
        {
            if(compound.ExpandedTypeName)
            {
                if(element.ExpandedName.LocalName!=compound.TypeName) return false;
            }
            else if(element.ExpandedName.LocalName!=compound.TypeName)
            {
                var resolved = ResolveType(compound);
                if (resolved is null || !resolved.IsInstanceOfType(element.Target)) return false;
            }
        }

        if (compound.Id is not null)
        {
            context.Observe(element,"Name");
            if (!string.Equals(element.Name,compound.Id,StringComparison.Ordinal)) return false;
        }

        if (compound.Classes is { } classes)
        {
            context.Observe(element,"Class");
            var elementClasses = element.CssRuntimeState?.Classes;
            if (elementClasses is null || elementClasses.Length == 0)
            {
                return false;
            }

            foreach (var required in classes)
            {
                if (Array.IndexOf(elementClasses, required) < 0)
                {
                    return false;
                }
            }
        }

        if (compound.Pseudos is { } pseudos)
        {
            foreach (var pseudo in pseudos)
            {
                var property = pseudo switch
                {
                    CssPseudoClass.Hover => "IsMouseOver", CssPseudoClass.Active => "IsPressed", CssPseudoClass.Focus => "IsFocused",
                    CssPseudoClass.FocusVisible => "IsKeyboardFocused", CssPseudoClass.FocusWithin => "IsKeyboardFocusWithin",
                    CssPseudoClass.Enabled or CssPseudoClass.Disabled => "IsEnabled", CssPseudoClass.Checked or CssPseudoClass.Indeterminate => "IsChecked", _ => null,
                };
                if (property is not null) context.Observe(element,property);
                if (property == "IsEnabled")
                    for(var parent=CssAncestor(element);parent is not null;parent=CssAncestor(parent)) context.Observe(parent,property);
                if (!evaluateStates && CssCompound.ToStateMask(pseudo) != CssStateMask.None) continue;
                if (!(pseudo is CssPseudoClass.Scope or CssPseudoClass.NestingScope ? ReferenceEquals(element, context.Scope ?? Root(element)) :
                    pseudo == CssPseudoClass.RelativeAnchor ? ReferenceEquals(element, context.RelativeAnchor) :
                    pseudo >= CssPseudoClass.Empty ? EvaluateStructural(element, pseudo) : CssPseudoStates.Evaluate(element, pseudo)))
                {
                    return false;
                }
            }
        }

        if (compound.Attributes is { } attributes)
            foreach(var attribute in attributes)
            {
                context.Observe(element,attribute.Name); context.Observe(element,"Name"); context.Observe(element);
                if (!MatchesAttribute(element,attribute,context)) return false;
            }
        if (compound.Functions is { } functions)
            foreach (var function in functions)
            {
                if (!evaluateStates && function.Selectors.Any(s => s.HasAnyState)) continue;
                if (!MatchesFunction(element, function, evaluateStates, context)) return false;
            }

        return true;
    }

    private static Type? ResolveType(CssCompound compound)
    {
        var resolved = compound.ResolvedType;
        if (resolved is not null)
        {
            return ReferenceEquals(resolved, CssSelectorMatching.UnresolvableType) ? null : resolved;
        }

        var type = TypeResolver.ResolveTypeByName?.Invoke(compound.TypeName!);
        if (type is null)
        {
            CssDiagnostics.Report(
                compound.TypeName!, CssDiagnosticReason.UnknownProperty, null,
                "selector type name could not be resolved; rules using it never match");
            compound.ResolvedType = CssSelectorMatching.UnresolvableType;
            return null;
        }

        compound.ResolvedType = type;
        return type;
    }
}

/// <summary>Evaluates dynamic pseudo-class state against the element's live dependency properties.</summary>
internal static class CssPseudoStates
{
    public static bool Evaluate(CssNode element, CssPseudoClass pseudo) => pseudo switch
    {
        CssPseudoClass.Root => CssMatcher.CssAncestor(element) is null,
        CssPseudoClass.Hover => element.IsMouseOver,
        CssPseudoClass.Active => element.GetValue(UIElement.IsPressedProperty) is true,
        CssPseudoClass.Focus => element.IsFocused,
        CssPseudoClass.FocusVisible => element.IsKeyboardFocused,
        CssPseudoClass.FocusWithin => element.IsKeyboardFocusWithin,
        CssPseudoClass.Disabled => !element.IsEnabled,
        CssPseudoClass.Enabled => element.IsEnabled,
        CssPseudoClass.Checked => GetIsChecked(element) is true,
        CssPseudoClass.Indeterminate => GetIsChecked(element) is null,
        _ => false,
    };

    private static bool? GetIsChecked(CssNode element)
    {
        var dp = CssDependencyPropertyLookup.Find(element.GetType(), "IsChecked");
        if (dp is null)
        {
            return false;
        }

        return element.GetValue(dp) switch
        {
            true => true,
            false => false,
            _ => null,
        };
    }
}
