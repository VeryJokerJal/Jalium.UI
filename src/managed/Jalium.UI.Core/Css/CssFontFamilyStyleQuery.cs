namespace Jalium.UI.Styling;

/// <summary>Compares ordered computed family names and generic keywords.</summary>
internal static class CssFontFamilyStyleQuery
{
    internal static bool IsSupported(string name) => name == "font-family";

    internal static bool Matches(string? text, CssNode container, CssLengthContext lengths)
    {
        var actual = CssFontFamilyValue.Computed(container);
        var initial = CssComputedFontFamily.Initial;
        if (text is null) return !Equal(actual, initial);

        var keyword = CssPropertyMetadata.WideKeyword(text);
        if (keyword is "revert" or "revert-layer") return false;
        if (keyword == "initial") return Equal(actual, initial);
        if (keyword is "inherit" or "unset")
            return Equal(actual, CssMatcher.CssAncestor(container) is { } parent
                ? CssFontFamilyValue.Computed(parent) : initial);

        var reader = new CssTokenReader(text);
        return CssFontFamilyValue.TryRead(ref reader, out var expected) && reader.AtEnd &&
            Equal(actual, expected);
    }

    private static bool Equal(CssComputedFontFamily actual, CssComputedFontFamily expected)
    {
        var left = actual.Items;
        var right = expected.Items;
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
            if (left[i].IsGeneric != right[i].IsGeneric ||
                !string.Equals(left[i].Name, right[i].Name, StringComparison.Ordinal))
                return false;
        return true;
    }
}
