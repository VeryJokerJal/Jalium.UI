namespace Jalium.UI.Styling;

/// <summary>Compares the inherited CSS visibility keyword, including native local precedence.</summary>
internal static class CssVisibilityStyleQuery
{
    internal static bool IsSupported(string name)
        => name.Equals("visibility", StringComparison.OrdinalIgnoreCase);

    internal static bool Matches(string? text, CssNode container, CssLengthContext lengths)
    {
        var actual = CssDisplayProperties.ComputedVisibility(container);
        if (text is null) return actual != CssVisibilityMode.Visible;
        var keyword = CssPropertyMetadata.WideKeyword(text);
        if (keyword is "revert" or "revert-layer") return false;
        if (keyword == "initial") return actual == CssVisibilityMode.Visible;
        if (keyword is "inherit" or "unset")
        {
            var parent = CssMatcher.CssAncestor(container);
            return actual == (parent is null
                ? CssVisibilityMode.Visible : CssDisplayProperties.ComputedVisibility(parent));
        }

        var descriptor = CssPropertyRegistry.LookupForCompile("visibility", out _);
        if (descriptor?.Parse is null) return false;
        var reader = new CssTokenReader(text, new CssNumericReadContext(lengths));
        var compiled = descriptor.Parse(ref reader, new CssCompileContext { NumericLengths = lengths });
        if (compiled is null || !reader.AtEnd) return false;
        var sink = new CssEngine.CssSetterCollector();
        if (!compiled.TryApply(new CssApplyContext(container, lengths, new CssSlotAccumulator()), sink) ||
            !sink.Values.TryGetValue(CssDisplayProperties.VisibilityProperty, out var applied) ||
            applied.Value is not CssVisibilityMode expected) return false;
        return actual == expected;
    }
}
