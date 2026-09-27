namespace Jalium.UI.Styling;

/// <summary>Compares computed overflow axes, including cross-axis keyword promotion.</summary>
internal static class CssOverflowStyleQuery
{
    private static readonly HashSet<string> s_longhands = new(StringComparer.OrdinalIgnoreCase)
    {
        "overflow-x", "overflow-y", "overflow-inline", "overflow-block",
    };

    private static readonly CssOverflowValue s_initial =
        new(CssOverflowMode.Visible, CssOverflowMode.Visible);

    internal static bool IsSupported(string name) => Descriptor(name) is not null;

    internal static bool Matches(string name, string? text, CssNode container, CssLengthContext lengths)
    {
        var descriptor = Descriptor(name);
        if (descriptor is null || container.Target is not UIElement) return false;
        var actual = Actual(container);
        if (descriptor.Kind == CssPropertyKind.Shorthand)
            return MatchesShorthand(descriptor, text, container, lengths, actual);

        var horizontal = Horizontal(descriptor.Name);
        var actualAxis = Axis(actual, horizontal);
        if (text is null) return actualAxis != CssOverflowMode.Visible;
        var keyword = CssPropertyMetadata.WideKeyword(text);
        if (keyword is "revert" or "revert-layer") return false;
        if (keyword is "initial" or "unset")
            return actualAxis == ComputedAxis(CssOverflowMode.Visible, actual, horizontal);
        if (keyword == "inherit")
        {
            var parent = CssMatcher.CssAncestor(container);
            var inherited = Axis(parent is null ? s_initial : Actual(parent), horizontal);
            return actualAxis == ComputedAxis(inherited, actual, horizontal);
        }
        return ComputeLonghand(descriptor, text, container, lengths, actual, horizontal,
            out var expected) && actualAxis == expected;
    }

    private static CssPropertyDescriptor? Descriptor(string name)
    {
        var descriptor = CssPropertyRegistry.LookupForCompile(name, out var canonicalName);
        if (s_longhands.Contains(canonicalName) && descriptor is
            { Kind: CssPropertyKind.Longhand, Parse: not null }) return descriptor;
        return canonicalName.Equals("overflow", StringComparison.OrdinalIgnoreCase) && descriptor is
            { Kind: CssPropertyKind.Shorthand, Expand: not null } ? descriptor : null;
    }

    private static CssOverflowValue Actual(CssNode node)
        => CssOverflowProperties.Get(node.Target) ?? s_initial;

    private static bool Horizontal(string name) => name is "overflow-x" or "overflow-inline";

    private static CssOverflowMode Axis(CssOverflowValue value, bool horizontal)
        => horizontal ? value.X : value.Y;

    private static CssOverflowMode ComputedAxis(CssOverflowMode mode,
        CssOverflowValue current, bool horizontal)
    {
        var pair = horizontal
            ? CssOverflowValue.Compute(mode, current.Y)
            : CssOverflowValue.Compute(current.X, mode);
        return Axis(pair, horizontal);
    }

    private static bool ComputeLonghand(CssPropertyDescriptor descriptor, string text,
        CssNode container, CssLengthContext lengths, CssOverflowValue actual,
        bool horizontal, out CssOverflowMode expected)
    {
        expected = default;
        var reader = new CssTokenReader(text, new CssNumericReadContext(lengths));
        var compiled = descriptor.Parse!(ref reader, new CssCompileContext { NumericLengths = lengths });
        if (compiled is null || !reader.AtEnd) return false;
        var slots = new CssSlotAccumulator();
        slots.SetOverflow(!horizontal, Axis(actual, !horizontal));
        var context = new CssApplyContext(container, lengths, slots);
        var sink = new CssEngine.CssSetterCollector();
        if (!compiled.TryApply(context, sink)) return false;
        slots.Flush(context, sink);
        if (!ReadComputed(sink, out var pair)) return false;
        expected = Axis(pair, horizontal);
        return true;
    }

    private static bool MatchesShorthand(CssPropertyDescriptor descriptor, string? text,
        CssNode container, CssLengthContext lengths, CssOverflowValue actual)
    {
        if (text is null) return actual != s_initial;
        var keyword = CssPropertyMetadata.WideKeyword(text);
        if (keyword is "revert" or "revert-layer") return false;
        if (keyword is "initial" or "unset") return actual == s_initial;
        if (keyword == "inherit")
        {
            var parent = CssMatcher.CssAncestor(container);
            return actual == (parent is null ? s_initial : Actual(parent));
        }
        var reader = new CssTokenReader(text, new CssNumericReadContext(lengths));
        var declarations = new List<CssCompiledDeclaration>(2);
        if (!descriptor.Expand!(ref reader, new CssCompileContext { NumericLengths = lengths }, declarations) ||
            !reader.AtEnd || declarations.Count != 2) return false;
        var slots = new CssSlotAccumulator();
        var context = new CssApplyContext(container, lengths, slots);
        var sink = new CssEngine.CssSetterCollector();
        foreach (var declaration in declarations)
            if (declaration.Name is not ("overflow-x" or "overflow-y") ||
                !declaration.Value.TryApply(context, sink)) return false;
        slots.Flush(context, sink);
        return ReadComputed(sink, out var expected) && actual == expected;
    }

    private static bool ReadComputed(CssEngine.CssSetterCollector sink, out CssOverflowValue value)
    {
        if (sink.Values.TryGetValue(CssOverflowProperties.ValueProperty, out var applied) &&
            applied.Value is CssOverflowValue computed)
        {
            value = computed;
            return true;
        }
        value = s_initial;
        return false;
    }
}
