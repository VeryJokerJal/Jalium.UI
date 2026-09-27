namespace Jalium.UI.Styling;

/// <summary>Compares the inherited computed font width percentage.</summary>
internal static class CssFontStretchStyleQuery
{
    internal static bool IsSupported(string name) => name is "font-stretch" or "font-width";

    internal static bool Matches(string? text, CssNode container, CssLengthContext lengths)
    {
        var actual = Actual(container);
        const double initial = 100;
        if (text is null) return !Equal(actual, initial);

        var keyword = CssPropertyMetadata.WideKeyword(text);
        if (keyword is "revert" or "revert-layer") return false;
        if (keyword == "initial") return Equal(actual, initial);
        if (keyword is "inherit" or "unset")
            return Equal(actual, CssMatcher.CssAncestor(container) is { } parent ? Actual(parent) : initial);

        var reader = new CssTokenReader(text, new CssNumericReadContext(lengths));
        return CssFontStretchValue.TryRead(ref reader, out var expected) && Equal(actual, expected);
    }

    private static double Actual(CssNode node) => CssFontStretchValue.Computed(node);

    private static bool Equal(double actual, double expected)
        => Math.Abs(actual - expected) <= 1e-10 * Math.Max(1, Math.Max(Math.Abs(actual), Math.Abs(expected)));
}
