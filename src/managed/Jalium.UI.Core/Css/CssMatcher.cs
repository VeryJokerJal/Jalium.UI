namespace Jalium.UI.Styling;

/// <summary>
/// Selector matching. The CSS ancestor axis follows the logical tree (FrameworkParent) and
/// treats template-generated parts as transparent: they neither match page-level rules nor
/// appear as ancestors for combinators — matching the author's document-tree mental model
/// and the WPF implicit-style template boundary.
/// </summary>
internal static class CssMatcher
{
    /// <summary>Template-generated parts do not participate in page-level CSS matching.</summary>
    public static bool IsCssMatchable(FrameworkElement element)
        => element.TemplatedParent is null;

    public static FrameworkElement? CssAncestor(FrameworkElement element)
    {
        var current = element.FrameworkParent;
        while (current is not null && current.TemplatedParent is not null)
        {
            current = current.FrameworkParent;
        }

        return current;
    }

    public static bool Matches(FrameworkElement element, CssSelector selector, bool evaluateStates)
    {
        if (!MatchesCompound(element, selector.Compounds[0], evaluateStates))
        {
            return false;
        }

        return MatchesAncestors(element, selector, 1, evaluateStates);
    }

    private static bool MatchesAncestors(FrameworkElement element, CssSelector selector, int index, bool evaluateStates)
    {
        if (index >= selector.Compounds.Length)
        {
            return true;
        }

        var combinator = selector.Combinators[index - 1];
        var ancestor = CssAncestor(element);
        if (combinator == CssCombinator.Child)
        {
            return ancestor is not null &&
                   MatchesCompound(ancestor, selector.Compounds[index], evaluateStates) &&
                   MatchesAncestors(ancestor, selector, index + 1, evaluateStates);
        }

        while (ancestor is not null)
        {
            if (MatchesCompound(ancestor, selector.Compounds[index], evaluateStates) &&
                MatchesAncestors(ancestor, selector, index + 1, evaluateStates))
            {
                return true;
            }

            ancestor = CssAncestor(ancestor);
        }

        return false;
    }

    internal static bool MatchesCompound(FrameworkElement element, CssCompound compound, bool evaluateStates)
    {
        if (compound.TypeName is not null)
        {
            var resolved = ResolveType(compound);
            if (resolved is null || !resolved.IsInstanceOfType(element))
            {
                return false;
            }
        }

        if (compound.Id is not null &&
            !string.Equals(element.Name, compound.Id, StringComparison.Ordinal))
        {
            return false;
        }

        if (compound.Classes is { } classes)
        {
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

        if (evaluateStates && compound.Pseudos is { } pseudos)
        {
            foreach (var pseudo in pseudos)
            {
                if (!CssPseudoStates.Evaluate(element, pseudo))
                {
                    return false;
                }
            }
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
    public static bool Evaluate(FrameworkElement element, CssPseudoClass pseudo) => pseudo switch
    {
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

    private static bool? GetIsChecked(FrameworkElement element)
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
