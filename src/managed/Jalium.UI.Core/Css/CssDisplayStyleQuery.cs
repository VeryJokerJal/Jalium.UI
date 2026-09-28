namespace Jalium.UI.Styling;

/// <summary>Compares display's computed outer and inner types after blockification.</summary>
internal static class CssDisplayStyleQuery
{
    internal static bool IsSupported(string name)
        => name.Equals("display", StringComparison.OrdinalIgnoreCase);

    internal static bool Matches(string? text, CssNode container, CssLengthContext lengths)
    {
        var actual = CssDisplayProperties.Computed(container);
        if (text is null) return actual != CssDisplayProperties.Initial;
        var keyword = CssPropertyMetadata.WideKeyword(text);
        if (keyword is "revert" or "revert-layer") return false;
        if (keyword is "initial" or "unset")
            return actual == CssDisplayProperties.Compute(container, CssDisplayProperties.Initial);
        if (keyword == "inherit")
        {
            var parent = CssMatcher.CssAncestor(container);
            var inherited = parent is null
                ? CssDisplayProperties.Initial : CssDisplayProperties.Computed(parent);
            return actual == CssDisplayProperties.Compute(container, inherited);
        }

        var descriptor = CssPropertyRegistry.LookupForCompile("display", out _);
        if (descriptor?.Parse is null) return false;
        var reader = new CssTokenReader(text, new CssNumericReadContext(lengths));
        var compiled = descriptor.Parse(ref reader, new CssCompileContext { NumericLengths = lengths });
        if (compiled is null || !reader.AtEnd) return false;
        var sink = new CssEngine.CssSetterCollector();
        if (!compiled.TryApply(new CssApplyContext(container, lengths, new CssSlotAccumulator()), sink) ||
            !sink.Values.TryGetValue(CssDisplayProperties.SpecificationProperty, out var applied) ||
            applied.Value is not CssDisplaySpecification specified) return false;
        return actual == CssDisplayProperties.Compute(container, specified);
    }
}
