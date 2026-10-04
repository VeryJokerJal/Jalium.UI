namespace Jalium.UI.Styling;

/// <summary>Computed layout keywords and ratios that differ from their used layout values.</summary>
internal static class CssLayoutStyleQuery
{
    private readonly record struct AspectValue(bool Auto, bool HasRatio, double Numerator, double Denominator);

    internal static bool IsSupported(string name) => Descriptor(name) is not null;

    internal static bool Matches(string name, string? value, CssNode container, CssLengthContext lengths)
    {
        var descriptor = Descriptor(name);
        if (descriptor is null || container.Target is not FrameworkElement) return false;
        var actualState = container.CssLayout;
        if (descriptor.Name == "box-sizing")
        {
            var actual = Box(actualState);
            const CssBoxSizing initial = CssBoxSizing.ContentBox;
            if (value is null) return actual != initial;
            var keyword = CssPropertyMetadata.WideKeyword(value);
            if (keyword is "revert" or "revert-layer") return false;
            if (keyword is "initial" or "unset") return actual == initial;
            if (keyword == "inherit")
                return actual == Box(CssMatcher.CssAncestor(container)?.CssLayout);
            return Compute(descriptor, value, container, lengths, out var expected) &&
                actual == Box(expected);
        }

        if (descriptor.Name == "position")
        {
            var actual = Position(actualState);
            if (value is null) return actual != CssPositionKeyword.Static;
            var keyword = CssPropertyMetadata.WideKeyword(value);
            if (keyword is "revert" or "revert-layer") return false;
            if (keyword is "initial" or "unset") return actual == CssPositionKeyword.Static;
            if (keyword == "inherit")
                return actual == Position(CssMatcher.CssAncestor(container)?.CssLayout);
            return Compute(descriptor, value, container, lengths, out var expected) &&
                actual == Position(expected);
        }

        var aspect = Aspect(actualState);
        var initialAspect = Aspect(null);
        if (value is null) return aspect != initialAspect;
        var wide = CssPropertyMetadata.WideKeyword(value);
        if (wide is "revert" or "revert-layer") return false;
        if (wide is "initial" or "unset") return aspect == initialAspect;
        if (wide == "inherit")
            return aspect == Aspect(CssMatcher.CssAncestor(container)?.CssLayout);
        return Compute(descriptor, value, container, lengths, out var expectedAspect) &&
            aspect == Aspect(expectedAspect);
    }

    internal static bool ComputedValueChanged(CssLayoutState? previous, CssLayoutState? current)
        => Box(previous) != Box(current) || Aspect(previous) != Aspect(current) ||
            Position(previous) != Position(current);

    private static CssBoxSizing Box(CssLayoutState? state)
        => state is { HasBoxSizing: true } ? state.BoxSizing : CssBoxSizing.ContentBox;

    private static CssPositionKeyword Position(CssLayoutState? state)
        => state?.ComputedPosition ?? CssPositionKeyword.Static;

    private static AspectValue Aspect(CssLayoutState? state)
        => state is { HasAspectRatio: true }
            ? new(state.AspectRatioAuto, true, state.AspectRatioNumerator, state.AspectRatioDenominator)
            : new(true, false, 0, 0);

    private static CssPropertyDescriptor? Descriptor(string name)
    {
        var descriptor = CssPropertyRegistry.LookupForCompile(name, out var canonicalName);
        return (canonicalName is "box-sizing" or "aspect-ratio" or "position") && descriptor is
            { Kind: CssPropertyKind.Longhand, Parse: not null } ? descriptor : null;
    }

    private static bool Compute(CssPropertyDescriptor descriptor, string text, CssNode container,
        CssLengthContext lengths, out CssLayoutState? state)
    {
        state = null;
        var reader = new CssTokenReader(text, new CssNumericReadContext(lengths));
        var compiled = descriptor.Parse!(ref reader, new CssCompileContext { NumericLengths = lengths });
        if (compiled is null || !reader.AtEnd) return false;
        var slots = new CssSlotAccumulator();
        var context = new CssApplyContext(container, lengths, slots);
        var sink = new CssEngine.CssSetterCollector();
        if (!compiled.TryApply(context, sink)) return false;
        slots.Flush(context, sink);
        state = sink.LayoutState;
        return true;
    }
}
