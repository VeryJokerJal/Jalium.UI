namespace Jalium.UI.Styling;

/// <summary>Compares computed inset lengths before position resolves them for layout.</summary>
internal static class CssInsetStyleQuery
{
    private static readonly HashSet<string> s_longhands = new(StringComparer.OrdinalIgnoreCase)
    {
        "top", "right", "bottom", "left",
        "inset-inline-start", "inset-inline-end", "inset-block-start", "inset-block-end",
    };

    private static readonly HashSet<string> s_shorthands = new(StringComparer.OrdinalIgnoreCase)
    {
        "inset", "inset-inline", "inset-block",
    };

    internal static bool IsSupported(string name) => Descriptor(name) is not null;

    internal static bool Matches(string name, string? value, CssNode container, CssLengthContext lengths)
    {
        var descriptor = Descriptor(name);
        if (descriptor is null || container.Target is not FrameworkElement) return false;
        if (descriptor.Kind == CssPropertyKind.Shorthand)
            return MatchesShorthand(descriptor, value, container, lengths);
        return MatchesLonghand(descriptor, value, container, lengths);
    }

    internal static bool ComputedValueChanged(CssLayoutState? previous, CssLayoutState? current)
    {
        for (var edge = 0; edge < 4; edge++)
            if (!CssSizeStyleQuery.Equal(StateValue(previous, edge), StateValue(current, edge)))
                return true;
        return false;
    }

    private static CssPropertyDescriptor? Descriptor(string name)
    {
        var descriptor = CssPropertyRegistry.LookupForCompile(name, out var canonicalName);
        if (s_longhands.Contains(canonicalName) && descriptor is
            { Kind: CssPropertyKind.Longhand, Parse: not null }) return descriptor;
        return s_shorthands.Contains(canonicalName) && descriptor is
            { Kind: CssPropertyKind.Shorthand, Expand: not null } &&
            CssPropertyMetadata.Longhands(canonicalName).All(s_longhands.Contains)
            ? descriptor : null;
    }

    private static bool MatchesLonghand(CssPropertyDescriptor descriptor, string? text,
        CssNode container, CssLengthContext lengths)
    {
        if (!ReadActual(container, descriptor.Name, out var actual)) return false;
        if (text is null) return !CssSizeStyleQuery.Equal(actual, CssLayoutLength.Auto);
        var keyword = CssPropertyMetadata.WideKeyword(text);
        if (keyword is "revert" or "revert-layer") return false;
        if (keyword is "initial" or "unset")
            return CssSizeStyleQuery.Equal(actual, CssLayoutLength.Auto);
        if (keyword == "inherit")
        {
            var parent = CssMatcher.CssAncestor(container);
            var inherited = parent is not null && ReadActual(parent, descriptor.Name, out var parentValue)
                ? parentValue : CssLayoutLength.Auto;
            return CssSizeStyleQuery.Equal(actual, inherited);
        }
        return Compute(descriptor, text, container, lengths, out var expected) &&
            CssSizeStyleQuery.Equal(actual, expected);
    }

    private static bool MatchesShorthand(CssPropertyDescriptor descriptor, string? text,
        CssNode container, CssLengthContext lengths)
    {
        var longhands = CssPropertyMetadata.Longhands(descriptor.Name).ToArray();
        if (text is null)
            return longhands.Any(name => Descriptor(name) is { } longhand &&
                MatchesLonghand(longhand, null, container, lengths));
        var keyword = CssPropertyMetadata.WideKeyword(text);
        if (keyword is "revert" or "revert-layer") return false;
        if (keyword is "initial" or "inherit" or "unset")
            return longhands.All(name => Descriptor(name) is { } longhand &&
                MatchesLonghand(longhand, keyword, container, lengths));

        var reader = new CssTokenReader(text, new CssNumericReadContext(lengths));
        var expanded = new List<CssCompiledDeclaration>(longhands.Length);
        if (!descriptor.Expand!(ref reader, new CssCompileContext { NumericLengths = lengths }, expanded) ||
            !reader.AtEnd || expanded.Count != longhands.Length) return false;
        foreach (var name in longhands)
        {
            var declaration = expanded.FirstOrDefault(item => item.Name == name);
            if (declaration.Value is null ||
                !ReadActual(container, name, out var actual) ||
                !Compute(declaration.Value, name, container, lengths, out var expected) ||
                !CssSizeStyleQuery.Equal(actual, expected)) return false;
        }
        return true;
    }

    private static bool ReadActual(CssNode node, string name, out CssLayoutLength value)
    {
        value = default;
        if (node.Target is not FrameworkElement element) return false;
        var edge = Edge(name, element.FlowDirection == FlowDirection.RightToLeft);
        var property = CssSlotAccumulator.InsetCompatibilityProperty?.Invoke(edge);
        if (property is not null && node.HasLocalOrAnimatedValue(property))
        {
            value = node.GetValue(property) is double pixels && double.IsFinite(pixels)
                ? CssLayoutLength.Px(pixels) : CssLayoutLength.Auto;
            return true;
        }
        value = StateValue(element.CssLayout, edge);
        return true;
    }

    private static CssLayoutLength StateValue(CssLayoutState? state, int edge)
    {
        var value = RawStateValue(state, edge);
        return value.IsSet ? value : CssLayoutLength.Auto;
    }

    private static CssLayoutLength RawStateValue(CssLayoutState? state, int edge)
        => edge switch
        {
            0 => state?.InsetLeft ?? default,
            1 => state?.InsetTop ?? default,
            2 => state?.InsetRight ?? default,
            _ => state?.InsetBottom ?? default,
        };

    private static int Edge(string name, bool rightToLeft) => CssLogicalBoxEdges.Edge(name, rightToLeft);

    private static bool Compute(CssPropertyDescriptor descriptor, string text, CssNode container,
        CssLengthContext lengths, out CssLayoutLength value)
    {
        var reader = new CssTokenReader(text, new CssNumericReadContext(lengths));
        var compiled = descriptor.Parse!(ref reader, new CssCompileContext { NumericLengths = lengths });
        value = default;
        return compiled is not null && reader.AtEnd &&
            Compute(compiled, descriptor.Name, container, lengths, out value);
    }

    private static bool Compute(CssCompiledValue compiled, string name, CssNode container,
        CssLengthContext lengths, out CssLayoutLength value)
    {
        value = default;
        var rightToLeft = container.GetValue(FrameworkElement.FlowDirectionProperty) is FlowDirection.RightToLeft;
        var slots = new CssSlotAccumulator { LogicalRightToLeft = rightToLeft };
        var context = new CssApplyContext(container, lengths, slots);
        var sink = new CssEngine.CssSetterCollector();
        if (!compiled.TryApply(context, sink)) return false;
        slots.Flush(context, sink);
        value = RawStateValue(sink.LayoutState, Edge(name, rightToLeft));
        return value.IsSet;
    }
}
