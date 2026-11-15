using System.Globalization;
using System.Xml.Linq;
using Jalium.UI.Styling;

namespace Jalium.UI.Media;

/// <summary>
/// SVG 2 <c>vector-effect</c> coordinate space transform behaviors.
/// </summary>
public enum SvgVectorEffect
{
    None = 0,
    NonScalingStroke = 1,
    NonScalingSize = 2,
    NonRotation = 3,
    FixedPosition = 4,
}

/// <summary>One immutable SVG document styled through the shared CSS parser, matcher and cascade.</summary>
internal sealed partial class SvgCssStyles
{
    public static SvgVectorEffect ParseVectorEffect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return SvgVectorEffect.None;
        var span = value.AsSpan().Trim();
        if (span.Equals("non-scaling-stroke", StringComparison.OrdinalIgnoreCase))
            return SvgVectorEffect.NonScalingStroke;
        if (span.Equals("non-scaling-size", StringComparison.OrdinalIgnoreCase))
            return SvgVectorEffect.NonScalingSize;
        if (span.Equals("non-rotation", StringComparison.OrdinalIgnoreCase))
            return SvgVectorEffect.NonRotation;
        if (span.Equals("fixed-position", StringComparison.OrdinalIgnoreCase))
            return SvgVectorEffect.FixedPosition;
        return SvgVectorEffect.None;
    }

    internal SvgVectorEffect GetVectorEffect(XElement element)
        => ParseVectorEffect(Get(element, "vector-effect"));

    public static double CompensateStrokeWidth(double strokeWidth, Matrix transform, SvgVectorEffect effect)
    {
        if (effect != SvgVectorEffect.NonScalingStroke || !(strokeWidth > 0 && double.IsFinite(strokeWidth)))
            return strokeWidth;
        var sx = Math.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12);
        var sy = Math.Sqrt(transform.M21 * transform.M21 + transform.M22 * transform.M22);
        var scale = Math.Sqrt((sx * sx + sy * sy) * 0.5);
        return scale > 1e-6 && double.IsFinite(scale) ? strokeWidth / scale : strokeWidth;
    }

    public static Matrix CompensateTransform(Matrix transform, SvgVectorEffect effect, Point origin = default)
    {
        switch (effect)
        {
            case SvgVectorEffect.NonScalingSize:
            {
                var sx = Math.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12);
                var sy = Math.Sqrt(transform.M21 * transform.M21 + transform.M22 * transform.M22);
                if (sx <= 1e-6 || sy <= 1e-6) return transform;
                var invScale = Matrix.Multiply(
                    Matrix.Multiply(new Matrix(1, 0, 0, 1, -origin.X, -origin.Y), new Matrix(1 / sx, 0, 0, 1 / sy, 0, 0)),
                    new Matrix(1, 0, 0, 1, origin.X, origin.Y));
                return Matrix.Multiply(invScale, transform);
            }
            case SvgVectorEffect.NonRotation:
            {
                var angle = Math.Atan2(transform.M12, transform.M11);
                var cos = Math.Cos(-angle);
                var sin = Math.Sin(-angle);
                var invRot = Matrix.Multiply(
                    Matrix.Multiply(new Matrix(1, 0, 0, 1, -origin.X, -origin.Y), new Matrix(cos, sin, -sin, cos, 0, 0)),
                    new Matrix(1, 0, 0, 1, origin.X, origin.Y));
                return Matrix.Multiply(invRot, transform);
            }
            case SvgVectorEffect.FixedPosition:
                return new Matrix(transform.M11, transform.M12, transform.M21, transform.M22, 0, 0);
            default:
                return transform;
        }
    }
    private sealed record Property(string Initial, bool Inherited = false);
    private sealed class Instance(Instance? parent, XElement target, XElement use)
    { internal Instance? Parent = parent; internal XElement Target = target, Use = use; }
    private sealed record Style(Dictionary<string, string> Values, IReadOnlyDictionary<string, string>? Variables);
    private readonly Dictionary<XElement, CssXmlElement> _nodes = [];
    private readonly Dictionary<(XElement, Instance?), Style> _computed = [];
    private readonly List<CssStyleSheet> _sheets = [];
    private readonly XElement _root;
    private readonly Size _viewport;
    private Instance? _instance;
    internal SvgCssStyles(XElement root, Size viewport)
    {
        _root = root; _viewport = viewport;
        var pending = new Stack<(XElement Source, CssXmlElement? Parent, int Depth)>(); pending.Push((root, null, 0));
        while (pending.TryPop(out var item))
        {
            if (_nodes.Count >= 65_536 || item.Depth > 256) throw new InvalidDataException("SVG styling exceeds the XML node/depth budget");
            var node = new CssXmlElement(item.Source, item.Parent) { SupportsProperty = Supports, InitialFontSize = 16 };
            node.Lengths = new(16, 16, 16, viewport.Width, viewport.Height);
            var css = CssNode.Get(node); css.CssRuntimeState = new() { Classes = (item.Source.Attribute("class")?.Value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) };
            _nodes.Add(item.Source, node); item.Parent?.Children.Add(node);
            foreach (var child in item.Source.Elements().Reverse()) pending.Push((child, node, item.Depth + 1));
        }
        foreach (var element in root.DescendantsAndSelf().Where(e => e.Name.LocalName == "style" && (e.Name.Namespace == root.Name.Namespace || e.Name.NamespaceName == "http://www.w3.org/1999/xhtml")))
        {
            var type = element.Attribute("type")?.Value.Trim();
            if (!string.IsNullOrEmpty(type) && !type.Equals("text/css", StringComparison.OrdinalIgnoreCase)) continue;
            var sheet = CssStyleSheet.Parse(element.Value, "SVG style");
            if (element.Attribute("media")?.Value is { } media)
                foreach (var rule in sheet.Rules) rule.Condition = CssCondition.And(new("media", media), rule.Condition);
            _sheets.Add(sheet);
        }
    }
    internal XElement Root => _root;
    internal Size Viewport => _viewport;
    internal string? Get(XElement element, string property)
        => Properties.ContainsKey(property) && ApplicableGeometry(element, property) && _nodes.ContainsKey(element) ? Compute(element, _instance).Values[property] : null;
    internal bool IsSpecified(XElement element, string property)
        => _nodes.ContainsKey(element) && Specified(element, _instance).ContainsKey(property);
    internal CssLengthContext Lengths(XElement element)
    { _ = Compute(element, _instance); return _nodes[element].Lengths; }
    internal IDisposable EnterUse(XElement target, XElement use)
    {
        var previous = _instance; _instance = new(previous, target, use);
        return new UseScope(this, previous);
    }
    internal IDisposable EnterDefinition()
    {
        var previous = _instance; _instance = null;
        return new UseScope(this, previous);
    }
    private sealed class UseScope(SvgCssStyles styles, Instance? previous) : IDisposable
    { public void Dispose() => styles._instance = previous; }
    private readonly Dictionary<(XElement, Instance?), Dictionary<string, string>> _specified = [];
    private Dictionary<string, string> Specified(XElement element, Instance? instance)
    { _ = Compute(element, instance); return _specified[(element, instance)]; }
    private Style Compute(XElement element, Instance? instance)
    {
        if (_computed.TryGetValue((element, instance), out var cached))
        { Publish(_nodes[element], cached); return cached; }
        var parentElement = element.Parent; var parentInstance = instance;
        for (var current = instance; current is not null; current = current.Parent)
            if (ReferenceEquals(current.Target, element)) { parentElement = current.Use; parentInstance = current.Parent; break; }
        var parent = parentElement is not null && _nodes.ContainsKey(parentElement) ? Compute(parentElement, parentInstance) : null;
        var parentFont = Number(parent?.Values.GetValueOrDefault("font-size"), 16);
        var rootFont = ReferenceEquals(element, _root) ? 16 : Number(Compute(_root, null).Values["font-size"], 16);
        var node = _nodes[element];
        SetSnapshot(node, CssXmlElement.FontSizeProperty, parentFont);
        SetSnapshot(node, CssXmlElement.FontFamilyProperty, new FontFamily(parent?.Values["font-family"] ?? "Segoe UI"));
        SetSnapshot(node, CssXmlElement.FontWeightProperty, FontWeight.FromOpenTypeWeight(Math.Clamp((int)Number(parent?.Values["font-weight"], 400), 1, 999)));
        SetSnapshot(node, CssXmlElement.FontStyleProperty, parent?.Values["font-style"] == "italic" ? FontStyles.Italic : parent?.Values["font-style"] == "oblique" ? FontStyles.Oblique : FontStyles.Normal);
        var inheritedColor = new CssTokenReader(parent?.Values["color"] ?? "black");
        if (CssColorParser.TryParse(ref inheritedColor, out var color, out _)) SetSnapshot(node, CssXmlElement.ForegroundProperty, new SolidColorBrush(color));
        node.Lengths = new(parentFont, parentFont, rootFont, _viewport.Width, _viewport.Height);
        var presentation = element.Name.Namespace == _root.Name.Namespace ? element.Attributes().Where(attribute =>
            !attribute.IsNamespaceDeclaration && attribute.Name.Namespace == XNamespace.None).Select(attribute =>
                PresentationAttribute(attribute)).Where(declaration =>
                (Properties.ContainsKey(declaration.PropertyName) || declaration.PropertyName == "marker") && ApplicableGeometry(element, declaration.PropertyName)) : [];
        var inline = CssParser.ParseInlineDeclarations(element.Attribute("style")?.Value ?? "", null);
        var own = CssEngine.ComputeRawCascade(node, _sheets, Names, presentation, inline, Supports, parent?.Variables, Shorthands, ExpandShorthand);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, definition) in Properties)
        {
            var initial = definition.Initial;
            var inherited = parent?.Values.GetValueOrDefault(name) ?? initial;
            var value = own.GetValueOrDefault(name);
            var wide = value is null ? null : CssPropertyMetadata.WideKeyword(value);
            values[name] = value is null || wide == "unset" ? definition.Inherited ? inherited : initial
                : wide == "inherit" ? inherited : wide == "initial" ? initial : value;
        }
        if (element.Name.LocalName == "marker" && !own.ContainsKey("overflow")) values["overflow"] = "hidden";
        foreach (var name in new[] { "mask-type", "mask-mode", "display", "visibility", "color-interpolation", "text-anchor", "stroke-linecap", "stroke-linejoin", "fill-rule", "clip-rule", "font-style", "font-weight", "direction", "mix-blend-mode", "isolation", "paint-order", "overflow", "vector-effect" })
            values[name] = values[name].ToLowerInvariant();
        foreach (var name in new[] { "width", "height", "rx", "ry", "stroke-dasharray", "transform", "path-length" })
            if (Keyword(values[name], "none", "auto")) values[name] = values[name].ToLowerInvariant();
        values["font-size"] = ResolveFontSize(values["font-size"], parentFont, rootFont, node.Lengths);
        var font = Number(values["font-size"], parentFont);
        node.Lengths = new(font, parentFont, ReferenceEquals(element, _root) ? font : rootFont, _viewport.Width, _viewport.Height);
        foreach (var name in new[] { "stroke-width", "stroke-dashoffset", "path-length" })
            values[name] = ComputeStrokeLength(values[name], node.Lengths, nonnegative: name != "stroke-dashoffset", unitlessAbsolute: name != "path-length");
        values["stroke-dasharray"] = ComputeStrokeLengths(values["stroke-dasharray"], node.Lengths);
        foreach (var name in new[] { "marker-start", "marker-mid", "marker-end" })
            if (Keyword(values[name], "none")) values[name] = "none";
        foreach (var name in new[] { "fill-opacity", "stroke-opacity", "stop-opacity", "opacity" })
            values[name] = ResolveOpacity(values[name], node.Lengths);
        if (values["color"].Equals("currentcolor", StringComparison.OrdinalIgnoreCase)) values["color"] = parent?.Values["color"] ?? "black";
        foreach (var name in new[] { "fill", "stroke", "color", "stop-color" })
            values[name] = NormalizePaint(values[name], values["color"], node.Lengths);
        node.Computed = values;
        var result = new Style(values, CssNode.Get(node).CssRuntimeState?.CustomProperties);
        _specified[(element, instance)] = own; _computed[(element, instance)] = result; Publish(node, result);
        return result;
    }
    private static void SetSnapshot(CssXmlElement node, DependencyProperty property, object value)
        => node.SetLayerValue(property, value, DependencyObject.LayerValueSource.CssBase, allowAutoTransition: false);
    private static void Publish(CssXmlElement node, Style style)
    {
        node.Computed = style.Values; CssNode.Get(node).CssRuntimeState!.CustomProperties = style.Variables;
        SetSnapshot(node, CssXmlElement.FontSizeProperty, Number(style.Values["font-size"], 16));
        SetSnapshot(node, CssXmlElement.FontFamilyProperty, new FontFamily(style.Values["font-family"]));
        SetSnapshot(node, CssXmlElement.FontWeightProperty, FontWeight.FromOpenTypeWeight(Math.Clamp((int)Number(style.Values["font-weight"], 400), 1, 999)));
        SetSnapshot(node, CssXmlElement.FontStyleProperty, style.Values["font-style"] == "italic" ? FontStyles.Italic : style.Values["font-style"] == "oblique" ? FontStyles.Oblique : FontStyles.Normal);
        var reader = new CssTokenReader(style.Values["color"]);
        if (CssColorParser.TryParse(ref reader, out var color, out _)) SetSnapshot(node, CssXmlElement.ForegroundProperty, new SolidColorBrush(color));
    }
    private static double Number(string? value, double fallback)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) ? number : fallback;
}
