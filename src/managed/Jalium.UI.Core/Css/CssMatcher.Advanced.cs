using System.Globalization;

namespace Jalium.UI.Styling;

internal static partial class CssMatcher
{
    internal static CssNode Root(CssNode element)
    { while (CssAncestor(element) is { } parent) element = parent; return element; }

    internal static IEnumerable<CssNode> Children(CssNode element)
    {
        var children = element.LogicalChildren;
        while (children.MoveNext())
        {
            if (children.Current is not CssNode child) continue;
            if (IsCssMatchable(child)) yield return child;
            else foreach (var content in Children(child)) yield return content;
        }
    }

    private static List<CssNode> Siblings(CssNode element)
        => CssAncestor(element) is { } parent ? Children(parent).ToList() : [element];

    private static CssNode? PreviousSibling(CssNode element)
    {
        var siblings = Siblings(element);
        var index = siblings.IndexOf(element);
        return index > 0 ? siblings[index - 1] : null;
    }

    private static bool EvaluateStructural(CssNode element, CssPseudoClass pseudo)
    {
        if (pseudo == CssPseudoClass.Empty)
        {
            if (Children(element).Any()) return false;
            foreach (var name in new[] { "Text", "Content" })
                if (CssDependencyPropertyLookup.Find(element.GetType(), name) is { } dp &&
                    element.GetValue(dp) is string { Length: > 0 }) return false;
            return true;
        }
        var siblings = Siblings(element);
        if (pseudo is CssPseudoClass.FirstOfType or CssPseudoClass.LastOfType or CssPseudoClass.OnlyOfType)
            siblings.RemoveAll(s => s.ExpandedName != element.ExpandedName);
        var index = siblings.IndexOf(element);
        if (index < 0) return false;
        return pseudo switch
        {
            CssPseudoClass.FirstChild or CssPseudoClass.FirstOfType => index == 0,
            CssPseudoClass.LastChild or CssPseudoClass.LastOfType => index == siblings.Count - 1,
            CssPseudoClass.OnlyChild or CssPseudoClass.OnlyOfType => siblings.Count == 1,
            _ => false,
        };
    }

    private static bool MatchesAttribute(CssNode element, CssAttributeSelector selector,CssMatchContext context)
    {
        var seen=new HashSet<CssExpandedName>();
        foreach(var attribute in Css.AttributeValues(element.Target,selector.NamespaceUri,selector.Name))
        {
            seen.Add(attribute.Name);
            if(CompareAttribute(attribute.Value,selector)) return true;
        }
        foreach(var attribute in CssXmlIdentity.Attributes(element.Target,selector.NamespaceUri,selector.Name))
        {
            if(attribute.Property is { } property) context.Observe(element,property);
            if(seen.Add(attribute.Name) && attribute.Value is not null && CompareAttribute(attribute.Value,selector)) return true;
        }
        if(selector.NamespaceUri is not (null or "") || seen.Contains(new(string.Empty,selector.Name)) || CssXmlIdentity.HasQualifiedAttribute(element.Target,selector.Name)) return false;
        string? value=null;
        if(selector.Name=="id") value=string.IsNullOrEmpty(element.Name)?null:element.Name;
        else if(selector.Name=="class") value=string.IsNullOrEmpty(Css.GetClass(element.Target))?null:Css.GetClass(element.Target);
        else if(CssDependencyPropertyLookup.Find(element.GetType(),selector.Name) is { } dp && element.HasLocalValue(dp))
            value=Convert.ToString(element.GetValue(dp),CultureInfo.InvariantCulture);
        return value is not null && CompareAttribute(value,selector);
    }

    private static bool CompareAttribute(string value,CssAttributeSelector selector)
    {
        if (selector.Operator.Length == 0) return true;
        var comparison = selector.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return selector.Operator switch
        {
            "=" => value.Equals(selector.Value, comparison),
            "~=" => selector.Value.Length > 0 && value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Any(v => v.Equals(selector.Value, comparison)),
            "|=" => value.Equals(selector.Value, comparison) || value.StartsWith(selector.Value + "-", comparison),
            "^=" => selector.Value.Length > 0 && value.StartsWith(selector.Value, comparison),
            "$=" => selector.Value.Length > 0 && value.EndsWith(selector.Value, comparison),
            "*=" => selector.Value.Length > 0 && value.Contains(selector.Value, comparison), _ => false,
        };
    }

    private static bool MatchesFunction(CssNode element, CssFunctionalPseudo function, bool evaluateStates, CssMatchContext context)
    {
        if (function.Name is "is" or "where" or "not" or "nesting")
        {
            if (function.Name == "nesting") context = context with { Scope = context.ScopeFor(function.NestingScope) };
            var matches = function.Selectors.Any(s => Matches(element, s, evaluateStates, context));
            return function.Name == "not" ? !matches : matches;
        }
        if (function.Name == "has")
        {
            // Relative selectors are anchored with :scope. Search forward; their anchor
            // excludes unrelated nodes, including descendants that are before the anchor.
            var searchRoot=CssAncestor(element) ?? element;
            context.Observe(searchRoot);
            var candidates = Descendants(searchRoot);
            return candidates.Any(candidate => !ReferenceEquals(candidate, element) &&
                function.Selectors.Any(s => Matches(candidate, s, evaluateStates, context with { RelativeAnchor = element })));
        }
        var siblings = Siblings(element);
        if (CssAncestor(element) is { } parent) context.Observe(parent);
        if (function.Name.EndsWith("of-type", StringComparison.Ordinal)) siblings.RemoveAll(s => s.ExpandedName != element.ExpandedName);
        if (function.Selectors.Length > 0) siblings.RemoveAll(s => !function.Selectors.Any(selector => Matches(s, selector, evaluateStates, context)));
        var index = siblings.IndexOf(element);
        if (index < 0) return false;
        var position = function.Name.StartsWith("nth-last-", StringComparison.Ordinal) ? siblings.Count - index : index + 1;
        var difference = (long)position - function.B;
        return function.A == 0 ? difference == 0 : difference % function.A == 0 && difference / function.A >= 0;
    }

    private static IEnumerable<CssNode> Descendants(CssNode element)
    {
        foreach (var child in Children(element))
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
