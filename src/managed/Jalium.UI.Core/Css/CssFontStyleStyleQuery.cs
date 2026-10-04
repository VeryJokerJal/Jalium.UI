namespace Jalium.UI.Styling;

/// <summary>Compares the inherited computed style and any explicit oblique angle.</summary>
internal static class CssFontStyleStyleQuery
{
    internal static bool IsSupported(string name) => name == "font-style";

    internal static bool Matches(string? text, CssNode container, CssLengthContext lengths)
    {
        var actual = CssFontStyleValue.Computed(container);
        var initial = CssComputedFontStyle.Normal;
        if (text is null) return !Equal(actual, initial);

        var keyword = CssPropertyMetadata.WideKeyword(text);
        if (keyword is "revert" or "revert-layer") return false;
        if (keyword == "initial") return Equal(actual, initial);
        if (keyword is "inherit" or "unset")
            return Equal(actual, CssMatcher.CssAncestor(container) is { } parent
                ? CssFontStyleValue.Computed(parent) : initial);

        var reader = new CssTokenReader(text, new CssNumericReadContext(lengths));
        return CssFontStyleValue.TryRead(ref reader, out var expected) && reader.AtEnd &&
            Equal(actual, expected);
    }

    private static bool Equal(CssComputedFontStyle actual, CssComputedFontStyle expected)
    {
        if (actual.Native != expected.Native) return false;
        if (actual.AngleDegrees is not { } angle) return expected.AngleDegrees is null;
        return expected.AngleDegrees is { } other &&
            Math.Abs(angle - other) <= 1e-10 * Math.Max(1, Math.Max(Math.Abs(angle), Math.Abs(other)));
    }
}
