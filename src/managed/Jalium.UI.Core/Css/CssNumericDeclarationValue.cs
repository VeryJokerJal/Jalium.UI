namespace Jalium.UI.Styling;

// Reuses the registered property parsers, including shorthand expansion and
// aliases, after the actual font/viewport/container context becomes available.
internal sealed class CssNumericDeclarationValue(
    string property, string rawValue, string longhand, Uri? baseUri) : CssCompiledValue
{
    public override bool TryApply(in CssApplyContext context, ICssSetterSink sink)
    {
        var compiled = CssEngine.CompileDeclarations([new CssDeclaration { PropertyName = property, RawValue = rawValue, Important = false }],
            new CssCompileContext { BaseUri = baseUri, NumericLengths = property.StartsWith("font", StringComparison.Ordinal)
                ? context.Lengths.ForFontProperty() : property == "line-height" ? context.Lengths.ForLineHeight() : context.Lengths });
        foreach (var declaration in compiled)
            if (declaration.Name == longhand) return declaration.Value.TryApply(context, sink);
        return new CssWideValue(longhand, "unset").TryApply(context, sink);
    }
}
