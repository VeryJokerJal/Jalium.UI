using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Compares the winning inherited font weight after relative weights are resolved.</summary>
internal static class CssFontWeightStyleQuery
{
    internal static bool IsSupported(string name) => name == "font-weight";

    internal static bool Matches(string? text, CssNode container, CssLengthContext lengths)
    {
        var actual = Actual(container);
        var initial = FontWeights.Normal;
        if (text is null) return actual != initial;

        var keyword = CssPropertyMetadata.WideKeyword(text);
        if (keyword is "revert" or "revert-layer") return false;
        if (keyword == "initial") return actual == initial;
        if (keyword is "inherit" or "unset")
            return actual == (CssMatcher.CssAncestor(container) is { } parent ? Actual(parent) : initial);

        var property = CssDependencyPropertyLookup.Find(container.Target.GetType(), "FontWeight");
        var descriptor = CssPropertyRegistry.LookupForCompile("font-weight", out _);
        if (property is null || descriptor?.Parse is null) return false;
        var reader = new CssTokenReader(text, new CssNumericReadContext(lengths));
        var compiled = descriptor.Parse(ref reader, new CssCompileContext { NumericLengths = lengths });
        if (compiled is null || !reader.AtEnd) return false;
        var sink = new CssEngine.CssSetterCollector();
        return compiled.TryApply(new CssApplyContext(container, lengths, new CssSlotAccumulator()), sink) &&
            sink.Values.TryGetValue(property, out var computed) &&
            computed.Value is FontWeight expected && actual == expected;
    }

    private static FontWeight Actual(CssNode node)
    {
        var property = CssDependencyPropertyLookup.Find(node.Target.GetType(), "FontWeight");
        return property is not null && node.GetValue(property) is FontWeight weight
            ? weight : FontWeights.Normal;
    }
}
