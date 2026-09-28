namespace Jalium.UI.Styling;

/// <summary>Compares the winning inherited font size after relative lengths resolve.</summary>
internal static class CssFontSizeStyleQuery
{
    internal static bool IsSupported(string name) => name == "font-size";

    internal static bool Matches(string? text, CssNode container, CssLengthContext lengths)
    {
        var actual = Actual(container);
        const double initial = CssLengthContext.DefaultFontSize;
        if (text is null) return !Equal(actual, initial);

        var keyword = CssPropertyMetadata.WideKeyword(text);
        if (keyword is "revert" or "revert-layer") return false;
        if (keyword == "initial") return Equal(actual, initial);
        if (keyword is "inherit" or "unset")
            return Equal(actual, CssMatcher.CssAncestor(container) is { } parent ? Actual(parent) : initial);

        var property = CssDependencyPropertyLookup.Find(container.Target.GetType(), "FontSize");
        var descriptor = CssPropertyRegistry.LookupForCompile("font-size", out _);
        if (property is null || descriptor?.Parse is null) return false;
        var reader = new CssTokenReader(text, new CssNumericReadContext(lengths));
        var compiled = descriptor.Parse(ref reader, new CssCompileContext { NumericLengths = lengths });
        if (compiled is null || !reader.AtEnd) return false;
        var sink = new CssEngine.CssSetterCollector();
        return compiled.TryApply(new CssApplyContext(container, lengths, new CssSlotAccumulator()), sink) &&
            sink.Values.TryGetValue(property, out var computed) &&
            computed.Value is double expected && Equal(actual, expected);
    }

    private static double Actual(CssNode node)
    {
        var property = CssDependencyPropertyLookup.Find(node.Target.GetType(), "FontSize");
        return property is not null && node.GetValue(property) is double size &&
            double.IsFinite(size) && size >= 0 ? size : CssLengthContext.DefaultFontSize;
    }

    private static bool Equal(double actual, double expected)
        => double.IsFinite(actual) && double.IsFinite(expected) &&
            Math.Abs(actual - expected) <= 1e-10 * Math.Max(1, Math.Max(Math.Abs(actual), Math.Abs(expected)));
}
