using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>Computed mask values; comparing an image never starts a resource fetch.</summary>
internal static class CssMaskStyleQuery
{
    internal static bool IsSupported(string name) => name is "mask" or "mask-border" or "mask-type" ||
        CssCoreProperties.MaskNames.Contains(name) || CssCoreProperties.MaskBorderNames.Contains(name);

    internal static bool Matches(string name, string? text, CssNode node, CssLengthContext lengths)
    {
        if (name is "mask" or "mask-border")
        {
            var parts = CssPropertyMetadata.Longhands(name);
            if (text is null) return parts.Any(part => Matches(part, null, node, lengths));
            if (CssPropertyMetadata.WideKeyword(text) is not null)
                return parts.All(part => Matches(part, text, node, lengths));
            var descriptor = CssPropertyRegistry.LookupForCompile(name, out _);
            var reader = new CssTokenReader(text, new CssNumericReadContext(lengths)); var declarations = new List<CssCompiledDeclaration>();
            if (descriptor?.Expand?.Invoke(ref reader, new CssCompileContext { NumericLengths = lengths }, declarations) != true || !reader.AtEnd) return false;
            foreach (var declaration in declarations)
                if (!MatchCompiled(declaration.Name, declaration.Value, node, lengths)) return false;
            return true;
        }
        var property = CssPropertyRegistry.LookupForCompile(name, out _);
        if (property?.StorageProperty is not { } storage) return false;
        var actual = node.GetValue(storage); var initial = storage.GetMetadata(node.Target.GetType()).DefaultValue;
        if (text is null) return !Equal(actual, initial);
        var keyword = CssPropertyMetadata.WideKeyword(text);
        if (keyword is "revert" or "revert-layer" or "revert-rule") return false;
        if (keyword is "initial" or "unset") return Equal(actual, initial);
        if (keyword == "inherit") return Equal(actual, CssMatcher.CssAncestor(node)?.GetValue(storage) ?? initial);
        var valueReader = new CssTokenReader(text, new CssNumericReadContext(lengths));
        var value = property.Parse?.Invoke(ref valueReader, new CssCompileContext { NumericLengths = lengths });
        return value is not null && valueReader.AtEnd && MatchCompiled(name, value, node, lengths);
    }

    private static bool MatchCompiled(string name, CssCompiledValue value, CssNode node, CssLengthContext lengths)
    {
        if (name == "mask-image")
            return CssCoreProperties.MaskImageDefinitions(value) is { } expected &&
                node.GetValue(CssMaskProperties.ImagesProperty) is CssMaskImages actual && ImagesEqual(actual.Definitions, expected);
        if (name == "mask-border-source")
            return node.GetValue(CssMaskBorderProperties.SourceProperty) is CssMaskBorderSource border &&
                ImagesEqual([border.Definition], [CssCoreProperties.MaskBorderDefinition(value)]);
        var descriptor = CssPropertyRegistry.LookupForCompile(name, out _);
        var sink = new CssEngine.CssSetterCollector();
        return descriptor?.StorageProperty is { } storage && value.TryApply(new CssApplyContext(node, lengths, new()), sink) &&
            sink.Values.TryGetValue(storage, out var expectedValue) && Equal(node.GetValue(storage), expectedValue.Value);
    }

    private static bool Equal(object? actual, object? expected)
    {
        if (actual is CssMaskImages a && expected is CssMaskImages b) return ImagesEqual(a.Definitions, b.Definitions);
        if (actual is CssMaskBorderSource ab && expected is CssMaskBorderSource bb) return ImagesEqual([ab.Definition], [bb.Definition]);
        if (actual is CssBorderImageMeasures am && expected is CssBorderImageMeasures bm)
            return am.Fill == bm.Fill && am.Sides.Length == bm.Sides.Length && am.Sides.Zip(bm.Sides).All(pair =>
                Measure(pair.First, am.Context, pair.Second, bm.Context));
        if (actual is CssMaskList<CssBackgroundPosition> ap && expected is CssMaskList<CssBackgroundPosition> bp)
            return ap.Values.Length == bp.Values.Length && ap.Values.Zip(bp.Values).All(pair =>
                Axis(pair.First.X, pair.First.Lengths, pair.Second.X, pair.Second.Lengths) &&
                Axis(pair.First.Y, pair.First.Lengths, pair.Second.Y, pair.Second.Lengths));
        if (actual is CssMaskList<CssBackgroundSize> az && expected is CssMaskList<CssBackgroundSize> bz)
            return az.Values.Length == bz.Values.Length && az.Values.Zip(bz.Values).All(pair =>
                Mode(pair.First) == Mode(pair.Second) && Length(pair.First.Width, pair.First.Lengths, pair.Second.Width, pair.Second.Lengths) &&
                Length(pair.First.Height, pair.First.Lengths, pair.Second.Height, pair.Second.Lengths));
        return Equals(actual, expected);
    }
    private static bool Measure(CssBorderImageMeasure a, CssLengthContext ac, CssBorderImageMeasure b, CssLengthContext bc)
    {
        if (a.Auto || b.Auto) return a.Auto == b.Auto;
        if (a.Number != b.Number) return false;
        return a.Number ? CssSizeStyleQuery.Equal(CssLayoutLength.Px(a.Resolve(ac, 0, 1, 0)), CssLayoutLength.Px(b.Resolve(bc, 0, 1, 0)))
            : CssSizeStyleQuery.Equal(Computed(a.Length, ac), Computed(b.Length, bc));
    }
    private static bool ImagesEqual(CssImageValue?[] a, CssImageValue?[] b) => a.Length == b.Length &&
        a.Zip(b).All(pair => pair.First is { } first && pair.Second is { } second
            ? first.EqualsForStyleQuery(second) : pair.First is null && pair.Second is null);
    private static CssBackgroundSizeMode Mode(CssBackgroundSize size) =>
        size.Mode == CssBackgroundSizeMode.Explicit && size.Width is null && size.Height is null ? CssBackgroundSizeMode.Auto : size.Mode;
    private static bool Length(CssLength? a, CssLengthContext ac, CssLength? b, CssLengthContext bc) =>
        a is { } first && b is { } second ? CssSizeStyleQuery.Equal(Computed(first, ac), Computed(second, bc)) : a is null && b is null;
    private static bool Axis(CssBackgroundPositionAxis a, CssLengthContext ac, CssBackgroundPositionAxis b, CssLengthContext bc)
        => CssSizeStyleQuery.Equal(Position(a, ac), Position(b, bc));
    internal static CssLayoutLength Computed(CssLength value, CssLengthContext context) => value.Expression is { } expression
        ? CssLayoutLength.Math(expression, context) : value.Unit == CssUnit.Percent ? CssLayoutLength.Percent(value.Value / 100)
        : value.TryResolve(context, CssPercentBasis.NotSupported, out var pixels) ? CssLayoutLength.Px(pixels) : default;
    internal static CssLayoutLength Position(CssBackgroundPositionAxis axis, CssLengthContext context)
    {
        if (!axis.FromFarEdge) return Computed(axis.Offset, context);
        var hundred = new CssMathExpression("value", CssNumericType.Of(CssNumericKind.Percent), new CssLength(100, CssUnit.Percent), []);
        var offset = axis.Offset.Expression ?? new CssMathExpression("value", CssNumericType.Of(axis.Offset.Unit == CssUnit.Percent ? CssNumericKind.Percent : CssNumericKind.Length), axis.Offset, []);
        return CssLayoutLength.Math(new("-", new CssNumericType(Length: 1, PercentHint: CssNumericKind.Length), default, [hundred, offset]), context);
    }
}
