using System.Globalization;
using System.Xml.Linq;
using Jalium.UI.Styling;

namespace Jalium.UI.Media;

internal sealed partial class SvgCssStyles
{
    private static readonly Dictionary<string, Property> Properties = new(StringComparer.Ordinal)
    {
        ["fill"] = new("black", true), ["stroke"] = new("none", true), ["color"] = new("black", true),
        ["fill-opacity"] = new("1", true), ["stroke-opacity"] = new("1", true), ["opacity"] = new("1"),
        ["mix-blend-mode"] = new("normal"), ["isolation"] = new("auto"),
        ["paint-order"] = new("normal", true),
        ["vector-effect"] = new("none"),
        ["marker-start"] = new("none", true), ["marker-mid"] = new("none", true), ["marker-end"] = new("none", true),
        ["overflow"] = new("visible"),
        ["fill-rule"] = new("nonzero", true), ["clip-rule"] = new("nonzero", true),
        ["stroke-width"] = new("1", true), ["stroke-dashoffset"] = new("0", true), ["stroke-dasharray"] = new("none", true),
        ["stroke-linecap"] = new("butt", true), ["stroke-linejoin"] = new("miter", true), ["stroke-miterlimit"] = new("4", true),
        ["font-size"] = new("16", true), ["font-family"] = new("Segoe UI", true), ["font-weight"] = new("400", true),
        ["font-style"] = new("normal", true), ["text-anchor"] = new("start", true), ["direction"] = new("ltr", true),
        ["visibility"] = new("visible", true), ["display"] = new("inline"), ["color-interpolation"] = new("sRGB", true),
        ["stop-color"] = new("black"), ["stop-opacity"] = new("1"), ["clip-path"] = new("none"),
        ["mask"] = new("none"), ["mask-image"] = new("none"), ["mask-mode"] = new("match-source"), ["mask-type"] = new("luminance"),
        ["transform"] = new("none"), ["x"] = new("0"), ["y"] = new("0"), ["width"] = new("auto"), ["height"] = new("auto"),
        ["path-length"] = new("none"),
        ["cx"] = new("0"), ["cy"] = new("0"), ["r"] = new("0"), ["rx"] = new("auto"), ["ry"] = new("auto"),
    };
    private static readonly HashSet<string> Names = new(Properties.Keys, StringComparer.Ordinal);
    internal static bool ApplicableGeometry(XElement element, string name) => name switch
    {
        "x" or "y" or "width" or "height" => element.Name.LocalName is "rect" or "image" or "svg" or "symbol" or "use" or "foreignObject",
        "cx" or "cy" => element.Name.LocalName is "circle" or "ellipse",
        "r" => element.Name.LocalName == "circle",
        "rx" or "ry" => element.Name.LocalName is "rect" or "ellipse",
        "path-length" => element.Name.LocalName is "path" or "rect" or "circle" or "ellipse" or "line" or "polygon" or "polyline",
        _ => true,
    };
    private static bool Supports(string name, string value)
    {
        name = name.ToLowerInvariant(); value = value.Trim();
        if (name == "marker") return CssPropertyMetadata.WideKeyword(value) is not null ||
            CssCustomProperties.ContainsSubstitution(value) && CssDeclarationValueSyntax.IsValid(value) || Keyword(value, "none") || LocalUrl(value);
        if (!Properties.ContainsKey(name)) return name == "all" ? CssPropertyMetadata.WideKeyword(value) is not null : Css.Supports(name, value);
        if (CssPropertyMetadata.WideKeyword(value) is not null) return true;
        if (CssCustomProperties.ContainsSubstitution(value)) return CssDeclarationValueSyntax.IsValid(value);
        var reader = new CssTokenReader(value);
        switch (name)
        {
            case "fill": case "stroke":
                if (Keyword(value, "none", "context-fill", "context-stroke")) return true;
                if (LocalUrl(value)) return true;
                goto case "color";
            case "color": case "stop-color":
                return CssColorParser.TryParseContextual(ref reader, out _, out _, out _) && reader.AtEnd;
            case "fill-rule": case "clip-rule": return Keyword(value, "nonzero", "evenodd");
            case "stroke-linecap": return Keyword(value, "butt", "round", "square");
            case "stroke-linejoin": return Keyword(value, "miter", "round", "bevel", "miter-clip", "arcs");
            case "font-style": return Keyword(value, "normal", "italic", "oblique");
            case "font-weight": return Keyword(value, "normal", "bold", "bolder", "lighter") || reader.TryReadNumber(out var weight, out var weightUnit) && weightUnit == CssUnit.None && weight is >= 1 and <= 1000 && reader.AtEnd;
            case "font-family": return Css.Supports(name, value);
            case "text-anchor": return Keyword(value, "start", "middle", "end");
            case "direction": return Keyword(value, "ltr", "rtl");
            case "mix-blend-mode": return CssCompositingProperties.TryRead(ref reader, out _) && reader.AtEnd;
            case "isolation": return Keyword(value, "auto", "isolate");
            case "paint-order": return SvgPaintOrder.TryParse(value, out _);
            case "vector-effect": return Keyword(value, "none", "non-scaling-stroke");
            case "marker": case "marker-start": case "marker-mid": case "marker-end": return Keyword(value, "none") || LocalUrl(value);
            case "overflow": return Keyword(value, "visible", "hidden", "clip", "scroll", "auto");
            case "visibility": return Keyword(value, "visible", "hidden", "collapse");
            case "display": return Css.Supports(name, value);
            case "color-interpolation": return Keyword(value, "auto", "sRGB", "linearRGB");
            case "mask-type": return Keyword(value, "alpha", "luminance");
            case "mask-mode": return Keyword(value, "match-source", "alpha", "luminance");
            case "clip-path": case "mask-image": return Keyword(value, "none") || LocalUrl(value);
            case "mask":
                return Css.Supports(name, value) && ExpandMask(value) is { } mask &&
                    (mask["mask-image"] == "none" || LocalUrl(mask["mask-image"]));
            case "transform":
                return CssTransformParser.TryParseTransformList(ref reader, out _) && reader.AtEnd ||
                    SvgParser.TryParseLegacyTransform(value);
            case "opacity": case "fill-opacity": case "stroke-opacity": case "stop-opacity":
                return reader.TryReadNumber(out _, out var alphaUnit) && alphaUnit is CssUnit.None or CssUnit.Percent && reader.AtEnd;
            case "stroke-miterlimit": return reader.TryReadNumber(out var limit, out var limitUnit) && limitUnit == CssUnit.None && limit >= 1 && reader.AtEnd;
            case "stroke-dasharray":
                if (Keyword(value, "none")) return true;
                return TryReadStrokeLengths(value, out _);
            case "font-size":
                if (Keyword(value, "xx-small", "x-small", "small", "medium", "large", "x-large", "xx-large", "xxx-large", "smaller", "larger")) return true;
                break;
            case "path-length":
                if (Keyword(value, "none")) return true;
                return TryLength(value, out var pathLength) && !pathLength.UsesPercent &&
                    (pathLength.Expression is not null || pathLength.Value >= 0 && (pathLength.Unit != CssUnit.None || pathLength.Value == 0));
            case "width": case "height": case "rx": case "ry": if (Keyword(value, "auto")) return true; break;
        }
        return TryLength(value, out var length) && (name is "x" or "y" or "cx" or "cy" or "stroke-dashoffset" || length.Expression is not null || length.Value >= 0);
    }
    private static bool Keyword(string value, params string[] names) => names.Contains(value, StringComparer.OrdinalIgnoreCase);
    private static bool LocalUrl(string value)
    {
        var reader = new CssTokenReader(value);
        if (!reader.TryReadFunction(out var name, out var body) || !name.Equals("url", StringComparison.OrdinalIgnoreCase) || !reader.AtEnd) return false;
        var text = body.Remaining.ToString().Trim().Trim('\'', '"');
        return text.StartsWith('#') && text.Length > 1;
    }
    private static bool TryLength(string value, out CssLength length)
    { var reader = new CssTokenReader(value); return reader.TryReadLength(out length) && reader.AtEnd; }
    private static string ResolveLength(string value, CssLengthContext context, bool preservePercent)
    {
        var reader = new CssTokenReader(value, new CssNumericReadContext(context));
        if (!reader.TryReadLength(out var length) || !reader.AtEnd) return value;
        if (preservePercent && (length.Unit == CssUnit.Percent || length.Expression?.UsesPercent == true)) return value;
        return length.TryResolve(context, CssPercentBasis.NotSupported, out var number) && double.IsFinite(number) ? number.ToString("R", CultureInfo.InvariantCulture) : value;
    }
    private static string ResolveFontSize(string value, double parent, double root, CssLengthContext context)
    {
        var normalized = value.ToLowerInvariant();
        var keyword = normalized switch { "xx-small" => 9.6, "x-small" => 12, "small" => 16d * 8 / 9, "medium" => 16, "large" => 19.2, "x-large" => 24, "xx-large" => 32, "xxx-large" => 48, "smaller" => parent / 1.2, "larger" => parent * 1.2, _ => double.NaN };
        if (double.IsFinite(keyword)) return keyword.ToString("R", CultureInfo.InvariantCulture);
        var lengths = new CssLengthContext(parent, parent, root, context.ViewportWidth, context.ViewportHeight);
        var reader = new CssTokenReader(value, new CssNumericReadContext(lengths));
        if (!reader.TryReadLength(out var length) || !reader.AtEnd) return parent.ToString("R", CultureInfo.InvariantCulture);
        double size;
        var valid = length.Expression is { } expression ? expression.TryEvaluate(lengths, parent, out size)
            : length.Unit == CssUnit.Percent ? double.IsFinite(size = length.Value * parent / 100)
            : length.TryResolve(lengths, CssPercentBasis.NotSupported, out size);
        return (valid && double.IsFinite(size) && size >= 0 ? size : parent).ToString("R", CultureInfo.InvariantCulture);
    }
    private static string ResolveOpacity(string value, CssLengthContext context)
    {
        var reader = new CssTokenReader(value, new CssNumericReadContext(context));
        if (!reader.TryReadNumber(out var alpha, out var unit) || !reader.AtEnd) return value;
        if (unit == CssUnit.Percent) alpha /= 100;
        return Math.Clamp(alpha, 0, 1).ToString("R", CultureInfo.InvariantCulture);
    }
    private static string NormalizePaint(string value, string current, CssLengthContext lengths)
    {
        value = value.Trim();
        if (Keyword(value, "none", "context-fill", "context-stroke")) return value.ToLowerInvariant();
        if (value.Equals("currentcolor", StringComparison.OrdinalIgnoreCase)) return "currentColor";
        var reader = new CssTokenReader(value, new CssNumericReadContext(lengths));
        if (!CssColorParser.TryParseContextual(ref reader, out var color, out var currentColor, out var expression) || !reader.AtEnd) return value;
        if (currentColor) return "currentColor";
        if (expression is not null)
        {
            var currentReader = new CssTokenReader(current);
            if (CssColorParser.TryParse(ref currentReader, out var inherited, out _) && CssColorParser.TryResolveCurrentColor(expression, inherited, out var resolved)) color = resolved;
        }
        return $"rgba({color.R},{color.G},{color.B},{(color.A / 255d).ToString("R", CultureInfo.InvariantCulture)})";
    }
}
