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
                    CssPseudoClass.Enabled or CssPseudoClass.Disabled or CssPseudoClass.ReadOnly or CssPseudoClass.ReadWrite => "IsEnabled",
                    _ => null,
                };
                if (property is not null) context.Observe(element,property);
                if (pseudo == CssPseudoClass.PlaceholderShown)
                {
                    foreach (var name in CssPlaceholderState.ObservedProperties(element.Target))
                        context.Observe(element, name);
                }
                if (pseudo is CssPseudoClass.ReadOnly or CssPseudoClass.ReadWrite)
                {
                    foreach (var name in CssMutabilityState.ObservedProperties(element.Target))
                        context.Observe(element, name);
                }
                if (pseudo is CssPseudoClass.Valid or CssPseudoClass.Invalid)
                {
                    context.Observe(element, "HasError");
                    context.Observe(element, "Errors");
                }
                if (pseudo is CssPseudoClass.Checked or CssPseudoClass.Unchecked or CssPseudoClass.Indeterminate)
                {
                    foreach (var name in CssChoiceState.ObservedProperties(element))
                        context.Observe(element, name);
                }
                if (pseudo == CssPseudoClass.Open && CssOpenState.ObservedProperty(element) is { } openProperty)
                    context.Observe(element, openProperty);
                if (pseudo == CssPseudoClass.Default)
                    context.Observe(element, "IsDefault");
                if (pseudo is CssPseudoClass.Required or CssPseudoClass.Optional)
                    context.Observe(element, "IsRequiredForForm");
                if (pseudo is CssPseudoClass.InRange or CssPseudoClass.OutOfRange)
                    foreach (var name in CssRangeState.ObservedProperties(element))
                        context.Observe(element, name);
                if (pseudo is CssPseudoClass.Modal or CssPseudoClass.Fullscreen)
                {
                    context.Observe(element, "IsModal");
                    context.Observe(element, "WindowState");
                }
                if (pseudo is CssPseudoClass.Playing or CssPseudoClass.Paused or CssPseudoClass.Buffering)
                    context.Observe(element, "IsPlaying");
                if (pseudo == CssPseudoClass.Buffering)
                    context.Observe(element, "IsBuffering");
                if (pseudo == CssPseudoClass.Muted)
                    context.Observe(element, "IsMuted");
                if (property == "IsEnabled")
                    for(var parent=CssAncestor(element);parent is not null;parent=CssAncestor(parent)) context.Observe(parent,property);
                if (!evaluateStates && CssCompound.ToStateMask(pseudo) != CssStateMask.None) continue;
                if (!(pseudo is CssPseudoClass.Scope or CssPseudoClass.NestingScope ? ReferenceEquals(element, context.Scope ?? Root(element)) :
                    pseudo == CssPseudoClass.RelativeAnchor ? ReferenceEquals(element, context.RelativeAnchor) :
                    pseudo is >= CssPseudoClass.Empty and <= CssPseudoClass.OnlyOfType ? EvaluateStructural(element, pseudo) : CssPseudoStates.Evaluate(element, pseudo)))
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
        CssPseudoClass.Checked => CssChoiceState.GetState(element) == CssChoiceValue.Checked,
        CssPseudoClass.Unchecked => CssChoiceState.GetState(element) == CssChoiceValue.Unchecked,
        CssPseudoClass.Indeterminate => CssChoiceState.GetState(element) == CssChoiceValue.Indeterminate,
        CssPseudoClass.PlaceholderShown => CssPlaceholderState.IsShown(element.Target),
        CssPseudoClass.ReadOnly => !CssMutabilityState.IsReadWrite(element),
        CssPseudoClass.ReadWrite => CssMutabilityState.IsReadWrite(element),
        CssPseudoClass.Valid => CssValidityState.GetValidity(element) is true,
        CssPseudoClass.Invalid => CssValidityState.GetValidity(element) is false,
        CssPseudoClass.Open => CssOpenState.IsOpen(element),
        CssPseudoClass.Default => element.Target is Jalium.UI.Controls.Button { IsDefault: true },
        CssPseudoClass.Required => CssOptionalityState.GetRequired(element) is true,
        CssPseudoClass.Optional => CssOptionalityState.GetRequired(element) is false,
        CssPseudoClass.InRange => CssRangeState.GetInRange(element) is true,
        CssPseudoClass.OutOfRange => CssRangeState.GetInRange(element) is false,
        CssPseudoClass.Modal => CssPresentationState.IsModal(element),
        CssPseudoClass.Fullscreen => CssPresentationState.IsFullscreen(element),
        CssPseudoClass.Playing => CssMediaPlaybackState.IsPlaying(element),
        CssPseudoClass.Paused => CssMediaPlaybackState.IsPaused(element),
        CssPseudoClass.Buffering => CssMediaPlaybackState.IsBuffering(element),
        CssPseudoClass.Muted => CssMediaPlaybackState.IsMuted(element),
        _ => false,
    };

}
