namespace Jalium.UI.Styling;

/// <summary>Compares computed margin and padding edges before layout resolves percentages.</summary>
internal static class CssBoxSpacingStyleQuery
{
    private static readonly HashSet<string> s_longhands = new(StringComparer.OrdinalIgnoreCase)
    {
        "margin-top", "margin-right", "margin-bottom", "margin-left",
        "margin-inline-start", "margin-inline-end", "margin-block-start", "margin-block-end",
        "padding-top", "padding-right", "padding-bottom", "padding-left",
        "padding-inline-start", "padding-inline-end", "padding-block-start", "padding-block-end",
    };

    private static readonly HashSet<string> s_shorthands = new(StringComparer.OrdinalIgnoreCase)
    {
        "margin", "margin-inline", "margin-block",
        "padding", "padding-inline", "padding-block",
    };

    private static readonly CssLayoutLength s_initial = CssLayoutLength.Px(0);

    internal static bool IsSupported(string name) => Descriptor(name) is not null;

    internal static bool Matches(string name, string? text, CssNode container, CssLengthContext lengths)
    {
        var descriptor = Descriptor(name);
        if (descriptor is null || container.Target is not FrameworkElement) return false;
        return descriptor.Kind == CssPropertyKind.Shorthand
            ? MatchesShorthand(descriptor, text, container, lengths)
            : MatchesLonghand(descriptor, text, container, lengths);
    }

    internal static bool ComputedValueChanged(CssLayoutState? previous, CssLayoutState? current)
    {
        for (var edge = 0; edge < 4; edge++)
            if (!CssSizeStyleQuery.Equal(StateValue(previous, true, edge),
                    StateValue(current, true, edge)) ||
                !CssSizeStyleQuery.Equal(StateValue(previous, false, edge),
                    StateValue(current, false, edge))) return true;
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
        if (text is null) return !CssSizeStyleQuery.Equal(actual, s_initial);
        var keyword = CssPropertyMetadata.WideKeyword(text);
        if (keyword is "revert" or "revert-layer") return false;
        if (keyword is "initial" or "unset")
            return CssSizeStyleQuery.Equal(actual, s_initial);
        if (keyword == "inherit")
        {
            var parent = CssMatcher.CssAncestor(container);
            var inherited = parent is not null && ReadActual(parent, descriptor.Name, out var parentValue)
                ? parentValue : s_initial;
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
        var declarations = new List<CssCompiledDeclaration>(longhands.Length);
        if (!descriptor.Expand!(ref reader, new CssCompileContext { NumericLengths = lengths }, declarations) ||
            !reader.AtEnd || declarations.Count != longhands.Length) return false;
        var slots = NewSlots(container);
        var context = new CssApplyContext(container, lengths, slots);
        var sink = new CssEngine.CssSetterCollector();
        foreach (var declaration in declarations)
            if (!longhands.Contains(declaration.Name) || !declaration.Value.TryApply(context, sink)) return false;
        slots.Flush(context, sink);
        foreach (var name in longhands)
            if (!ReadActual(container, name, out var actual) ||
                !ReadExpected(sink, container, name, slots.LogicalRightToLeft, out var expected) ||
                !CssSizeStyleQuery.Equal(actual, expected)) return false;
        return true;
    }

    private static bool ReadActual(CssNode node, string name, out CssLayoutLength value)
    {
        value = default;
        if (node.Target is not FrameworkElement element) return false;
        var margin = IsMargin(name);
        var edge = CssLogicalBoxEdges.Edge(name, element.FlowDirection == FlowDirection.RightToLeft);
        var property = margin ? FrameworkElement.MarginProperty :
            CssDependencyPropertyLookup.Find(element.GetType(), "Padding");
        if (property is not null && element.HasLocalOrAnimatedValue(property) &&
            element.GetValue(property) is Thickness local)
        {
            value = NativeEdge(local, edge);
            return true;
        }
        var stored = RawStateValue(element.CssLayout, margin, edge);
        if (stored.IsSet)
        {
            value = stored;
            return true;
        }
        value = property is not null && element.GetValue(property) is Thickness native
            ? NativeEdge(native, edge) : s_initial;
        return true;
    }

    private static CssLayoutLength NativeEdge(Thickness thickness, int edge)
        => CssLayoutLength.Px(edge switch
        {
            0 => thickness.Left,
            1 => thickness.Top,
            2 => thickness.Right,
            _ => thickness.Bottom,
        });

    private static bool Compute(CssPropertyDescriptor descriptor, string text,
        CssNode container, CssLengthContext lengths, out CssLayoutLength value)
    {
        value = default;
        var reader = new CssTokenReader(text, new CssNumericReadContext(lengths));
        var compiled = descriptor.Parse!(ref reader, new CssCompileContext { NumericLengths = lengths });
        if (compiled is null || !reader.AtEnd) return false;
        var slots = NewSlots(container);
        var context = new CssApplyContext(container, lengths, slots);
        var sink = new CssEngine.CssSetterCollector();
        if (!compiled.TryApply(context, sink)) return false;
        slots.Flush(context, sink);
        return ReadExpected(sink, container, descriptor.Name, slots.LogicalRightToLeft, out value);
    }

    private static CssSlotAccumulator NewSlots(CssNode container)
        => new() { LogicalRightToLeft =
            container.GetValue(FrameworkElement.FlowDirectionProperty) is FlowDirection.RightToLeft };

    private static bool ReadExpected(CssEngine.CssSetterCollector sink, CssNode container,
        string name, bool rightToLeft,
        out CssLayoutLength value)
    {
        var margin = IsMargin(name);
        var edge = CssLogicalBoxEdges.Edge(name, rightToLeft);
        value = RawStateValue(sink.LayoutState, margin, edge);
        if (value.IsSet) return true;
        var property = margin ? FrameworkElement.MarginProperty :
            CssDependencyPropertyLookup.Find(container.GetType(), "Padding");
        if (property is null || !sink.Values.TryGetValue(property, out var applied) ||
            applied.Value is not Thickness thickness) return false;
        value = NativeEdge(thickness, edge);
        return true;
    }

    private static bool IsMargin(string name) => name.StartsWith("margin-", StringComparison.Ordinal);

    private static CssLayoutLength StateValue(CssLayoutState? state, bool margin, int edge)
    {
        var value = RawStateValue(state, margin, edge);
        return value.IsSet ? value : s_initial;
    }

    private static CssLayoutLength RawStateValue(CssLayoutState? state, bool margin, int edge)
        => (margin, edge) switch
        {
            (true, 0) => state?.MarginLeft ?? default,
            (true, 1) => state?.MarginTop ?? default,
            (true, 2) => state?.MarginRight ?? default,
            (true, _) => state?.MarginBottom ?? default,
            (false, 0) => state?.PaddingLeft ?? default,
            (false, 1) => state?.PaddingTop ?? default,
            (false, 2) => state?.PaddingRight ?? default,
            _ => state?.PaddingBottom ?? default,
        };
}
