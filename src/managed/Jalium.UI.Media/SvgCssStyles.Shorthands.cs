using System.Globalization;
using Jalium.UI.Styling;

namespace Jalium.UI.Media;

internal sealed partial class SvgCssStyles
{
    private static readonly IReadOnlyDictionary<string, string[]> Shorthands = new Dictionary<string, string[]>
    { ["font"] = ["font-style", "font-weight", "font-size", "font-family"], ["mask"] = ["mask-image", "mask-mode"],
      ["marker"] = ["marker-start", "marker-mid", "marker-end"] };

    private static IReadOnlyDictionary<string, string>? ExpandShorthand(CssXmlElement target, string name, string text)
    {
        if (name == "marker") return new Dictionary<string, string>
        { ["marker-start"] = text, ["marker-mid"] = text, ["marker-end"] = text };
        if (name == "mask") return ExpandMask(text);
        if (name != "font") return null;
        var node = CssNode.Get(target);
        var declarations = CssEngine.CompileDeclarations([new CssDeclaration { PropertyName = name, RawValue = text }],
            new CssCompileContext { NumericLengths = target.Lengths, InitialFontSize = target.InitialFontSize });
        if (declarations.Length == 0) return null;
        var sink = new CssEngine.CssSetterCollector();
        var context = new CssApplyContext(node, target.Lengths, new CssSlotAccumulator());
        foreach (var declaration in declarations)
            if (Shorthands["font"].Contains(declaration.Name) && !declaration.Value.TryApply(context, sink)) return null;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (sink.Values.TryGetValue(CssXmlElement.FontSizeProperty, out var size) && size.Value is double fontSize)
            result["font-size"] = fontSize.ToString("R", CultureInfo.InvariantCulture);
        if (sink.Values.TryGetValue(CssXmlElement.FontFamilyProperty, out var family) && family.Value is FontFamily fontFamily)
            result["font-family"] = fontFamily.Source;
        if (sink.Values.TryGetValue(CssXmlElement.FontWeightProperty, out var weight) && weight.Value is FontWeight fontWeight)
            result["font-weight"] = fontWeight.ToOpenTypeWeight().ToString(CultureInfo.InvariantCulture);
        if (sink.Values.TryGetValue(CssXmlElement.FontStyleProperty, out var style) && style.Value is FontStyle fontStyle)
            result["font-style"] = fontStyle.ToString().ToLowerInvariant();
        return result.Count == 4 ? result : null;
    }
    private static IReadOnlyDictionary<string, string>? ExpandMask(string text)
    {
        if (text.Equals("none", StringComparison.OrdinalIgnoreCase)) return new Dictionary<string, string> { ["mask-image"] = "none", ["mask-mode"] = "match-source" };
        var reader = new CssTokenReader(text); string? image = null; var mode = "match-source";
        while (!reader.AtEnd)
        {
            var start = reader.Position;
            var probe = reader;
            if (probe.TryReadFunction(out var function, out _) && function.Equals("url", StringComparison.OrdinalIgnoreCase))
            { image = text[start..probe.Position]; reader = probe; continue; }
            if (reader.TryReadIdent(out var word))
            { if (Keyword(word.ToString(), "alpha", "luminance", "match-source")) mode = word.ToString().ToLowerInvariant(); continue; }
            if (reader.TryReadLength(out _) || reader.TryReadSlash()) continue;
            return null;
        }
        return image is null ? null : new Dictionary<string, string> { ["mask-image"] = image, ["mask-mode"] = mode };
    }
}
