namespace Jalium.UI.Styling;

internal static class CssTextSpacingStyleQuery
{
    internal static bool IsSupported(string name) => name is "letter-spacing" or "word-spacing";

    internal static bool Matches(string name, string? text, CssNode container, CssLengthContext lengths)
    {
        var descriptor = CssPropertyRegistry.LookupForCompile(name, out _);
        if (descriptor?.StorageProperty is not { } property || descriptor.Parse is null ||
            !CssTextSpacing.Length(container.GetValue(property), out var actual)) return false;
        if (text is null) return !CssSizeStyleQuery.Equal(actual, CssLayoutLength.Px(0));
        var keyword = CssPropertyMetadata.WideKeyword(text);
        if (keyword is "revert" or "revert-layer") return false;
        if (keyword == "initial") return CssSizeStyleQuery.Equal(actual, CssLayoutLength.Px(0));
        if (keyword is "inherit" or "unset")
        {
            var parent = CssMatcher.CssAncestor(container);
            var inherited = parent is not null && CssTextSpacing.Length(parent.GetValue(property), out var value)
                ? value : CssLayoutLength.Px(0);
            return CssSizeStyleQuery.Equal(actual, inherited);
        }
        var reader = new CssTokenReader(text, new CssNumericReadContext(lengths));
        var compiled = descriptor.Parse(ref reader, new CssCompileContext { NumericLengths = lengths });
        if (compiled is null || !reader.AtEnd) return false;
        var slots = new CssSlotAccumulator();
        var context = new CssApplyContext(container, lengths, slots);
        var sink = new CssEngine.CssSetterCollector();
        if (!compiled.TryApply(context, sink) || !sink.Values.TryGetValue(property, out var computed) ||
            !CssTextSpacing.Length(computed.Value, out var expected)) return false;
        return CssSizeStyleQuery.Equal(actual, expected);
    }
}
