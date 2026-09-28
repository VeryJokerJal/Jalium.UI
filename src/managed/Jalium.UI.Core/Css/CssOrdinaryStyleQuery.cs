namespace Jalium.UI.Styling;

/// <summary>
/// Compares ordinary properties with exact computed-value storage. The declaration
/// parser supplies the expected value; some properties also emit native mirror setters.
/// </summary>
internal static class CssOrdinaryStyleQuery
{
    // These longhands have a one-to-one, value-preserving storage mapping. Properties
    // with used-value resolution, composite slots, or inherited sentinels need a
    // property-specific computed-value projection before they can join this set.
    private static readonly HashSet<string> s_properties = new(StringComparer.OrdinalIgnoreCase)
    {
        "opacity", "direction", "float", "clear", "container-type", "container-name",
        "user-select", "image-rendering", "object-fit", "text-justify", "text-group-align",
        "overflow-wrap", "word-break", "line-break", "text-autospace",
        "hanging-punctuation", "hyphens", "text-transform", "white-space-collapse",
        "text-wrap-mode", "text-wrap-style", "white-space-trim", "caret-animation",
        "caret-shape", "box-decoration-break", "math-depth", "math-style",
    };
    private static readonly HashSet<string> s_shorthands = new(StringComparer.OrdinalIgnoreCase)
    {
        "container", "text-wrap", "white-space",
    };

    internal static bool IsSupported(string name)
    {
        name = name.ToLowerInvariant();
        return Descriptor(name) is not null || CssDisplayStyleQuery.IsSupported(name) ||
            CssVisibilityStyleQuery.IsSupported(name) || CssColorStyleQuery.IsSupported(name) ||
            CssFontWeightStyleQuery.IsSupported(name) || CssFontSizeStyleQuery.IsSupported(name) ||
            CssFontStretchStyleQuery.IsSupported(name) ||
            CssFontStyleStyleQuery.IsSupported(name) ||
            CssFontFamilyStyleQuery.IsSupported(name) ||
            CssSizeStyleQuery.IsSupported(name) ||
            CssLayoutStyleQuery.IsSupported(name) || CssInsetStyleQuery.IsSupported(name) ||
            CssBoxSpacingStyleQuery.IsSupported(name) || CssOverflowStyleQuery.IsSupported(name);
    }

    internal static bool Matches(string name, string? value, CssNode container, CssLengthContext lengths)
    {
        name = name.ToLowerInvariant();
        if (CssDisplayStyleQuery.IsSupported(name))
            return CssDisplayStyleQuery.Matches(value, container, lengths);
        if (CssVisibilityStyleQuery.IsSupported(name))
            return CssVisibilityStyleQuery.Matches(value, container, lengths);
        if (CssColorStyleQuery.IsSupported(name))
            return CssColorStyleQuery.Matches(name, value, container, lengths);
        if (CssFontWeightStyleQuery.IsSupported(name))
            return CssFontWeightStyleQuery.Matches(value, container, lengths);
        if (CssFontSizeStyleQuery.IsSupported(name))
            return CssFontSizeStyleQuery.Matches(value, container, lengths);
        if (CssFontStretchStyleQuery.IsSupported(name))
            return CssFontStretchStyleQuery.Matches(value, container, lengths);
        if (CssFontStyleStyleQuery.IsSupported(name))
            return CssFontStyleStyleQuery.Matches(value, container, lengths);
        if (CssFontFamilyStyleQuery.IsSupported(name))
            return CssFontFamilyStyleQuery.Matches(value, container, lengths);
        if (CssSizeStyleQuery.IsSupported(name))
            return CssSizeStyleQuery.Matches(name, value, container, lengths);
        if (CssLayoutStyleQuery.IsSupported(name))
            return CssLayoutStyleQuery.Matches(name, value, container, lengths);
        if (CssInsetStyleQuery.IsSupported(name))
            return CssInsetStyleQuery.Matches(name, value, container, lengths);
        if (CssBoxSpacingStyleQuery.IsSupported(name))
            return CssBoxSpacingStyleQuery.Matches(name, value, container, lengths);
        if (CssOverflowStyleQuery.IsSupported(name))
            return CssOverflowStyleQuery.Matches(name, value, container, lengths);
        var descriptor = Descriptor(name);
        if (descriptor?.Kind == CssPropertyKind.Shorthand)
            return MatchesShorthand(descriptor, value, container, lengths);
        if (descriptor?.StorageProperty is not { } storage ||
            CssPropertyMetadata.Initial(descriptor.Name) is not { } initialText ||
            !Compute(descriptor, initialText, storage, container, lengths, out var initial)) return false;

        var actual = Actual(descriptor.Name, container, storage, initial);
        if (value is null) return !Equals(actual, initial);

        var keyword = CssPropertyMetadata.WideKeyword(value);
        if (keyword is "revert" or "revert-layer") return false;
        if (keyword is "initial" || keyword is "unset" && !CssPropertyMetadata.IsInherited(descriptor.Name))
            return Equals(actual, initial);
        if (keyword is "inherit" or "unset")
        {
            var parent = CssMatcher.CssAncestor(container);
            return Equals(actual, parent is null ? initial : Actual(descriptor.Name, parent, storage, initial));
        }
        return Compute(descriptor, value, storage, container, lengths, out var expected) &&
            Equals(actual, expected);
    }

    private static object? Actual(string name, CssNode node, DependencyProperty storage, object? initial)
        => !CssPropertyMetadata.IsInherited(name) && node.Target.GetEffectiveValueLayer(storage) is null
            ? initial : node.GetValue(storage);

    private static CssPropertyDescriptor? Descriptor(string name)
    {
        var descriptor = CssPropertyRegistry.LookupForCompile(name, out var canonicalName);
        if (s_properties.Contains(canonicalName) && descriptor is
            { Kind: CssPropertyKind.Longhand, StorageProperty: not null, Parse: not null }) return descriptor;
        return s_shorthands.Contains(canonicalName) && descriptor is
            { Kind: CssPropertyKind.Shorthand, Expand: not null } &&
            CssPropertyMetadata.Longhands(canonicalName).All(s_properties.Contains)
            ? descriptor : null;
    }

    private static bool MatchesShorthand(CssPropertyDescriptor descriptor, string? value,
        CssNode container, CssLengthContext lengths)
    {
        var longhands = CssPropertyMetadata.Longhands(descriptor.Name).ToArray();
        if (value is null)
            return longhands.Any(name => Matches(name, null, container, lengths));
        var keyword = CssPropertyMetadata.WideKeyword(value);
        if (keyword is "revert" or "revert-layer") return false;
        if (keyword is "initial" or "inherit" or "unset")
            return longhands.All(name => Matches(name, keyword, container, lengths));

        var reader = new CssTokenReader(value, new CssNumericReadContext(lengths));
        var expanded = new List<CssCompiledDeclaration>(longhands.Length);
        if (!descriptor.Expand!(ref reader, new CssCompileContext { NumericLengths = lengths }, expanded) ||
            !reader.AtEnd || expanded.Count != longhands.Length) return false;
        foreach (var name in longhands)
        {
            var storage = Descriptor(name)?.StorageProperty;
            var declaration = expanded.FirstOrDefault(item => item.Name == name);
            if (storage is null || declaration.Value is null ||
                !Compute(declaration.Value, storage, container, lengths, out var expected) ||
                !Equals(Actual(name, container, storage, Initial(name, container, lengths)), expected)) return false;
        }
        return true;
    }

    private static object? Initial(string name, CssNode container, CssLengthContext lengths)
    {
        var descriptor = Descriptor(name);
        return descriptor?.StorageProperty is { } storage &&
            CssPropertyMetadata.Initial(descriptor.Name) is { } initialText &&
            Compute(descriptor, initialText, storage, container, lengths, out var initial)
            ? initial : null;
    }

    private static bool Compute(CssPropertyDescriptor descriptor, string text, DependencyProperty storage,
        CssNode container, CssLengthContext lengths, out object? value)
    {
        value = null;
        var reader = new CssTokenReader(text, new CssNumericReadContext(lengths));
        var compiled = descriptor.Parse!(ref reader, new CssCompileContext { NumericLengths = lengths });
        if (compiled is null || !reader.AtEnd) return false;
        return Compute(compiled, storage, container, lengths, out value);
    }

    private static bool Compute(CssCompiledValue compiled, DependencyProperty storage,
        CssNode container, CssLengthContext lengths, out object? value)
    {
        value = null;
        var sink = new CssEngine.CssSetterCollector();
        if (!compiled.TryApply(new CssApplyContext(container, lengths, new CssSlotAccumulator()), sink) ||
            !sink.Values.TryGetValue(storage, out var applied)) return false;
        value = applied.Value;
        return true;
    }
}
