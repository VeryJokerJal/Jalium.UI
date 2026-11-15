using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using System.Runtime.CompilerServices;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.Media;

internal static partial class SvgParser
{
    internal sealed class MaskDefinition(XElement mask, XElement root, Size? styleViewport = null)
    {
        internal XElement Identity => mask;
        private sealed class Plans
        {
            internal Dictionary<(Rect, Size), DrawingGroup> Values = [];
            internal Dictionary<Size, SvgCssStyles> Styles = [];
        }
        private SvgCssStyles Styles(Size viewport)
        {
            viewport = styleViewport ?? viewport;
            var styles = _plans.GetOrCreateValue(Dispatcher.CurrentDispatcher).Styles;
            if (!styles.TryGetValue(viewport, out var value))
            { if (styles.Count >= 16) styles.Clear(); styles[viewport] = value = new(root, viewport); }
            return value;
        }
        internal CssMaskMode ModeFor(Size viewport) => StyleValue(viewport, "mask-type") == "alpha" ? CssMaskMode.Alpha : CssMaskMode.Luminance;
        internal bool LinearRgbFor(Size viewport) => StyleValue(viewport, "color-interpolation")?.Equals("linearRGB", StringComparison.OrdinalIgnoreCase) == true;
        private readonly ConditionalWeakTable<Dispatcher, Plans> _plans = new();
        private string? StyleValue(Size viewport, string name)
        {
            try { return Styles(viewport).Get(mask, name); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException or FormatException or OverflowException or InvalidDataException)
            { CssDiagnostics.Report("mask-image", CssDiagnosticReason.LossyConversion, null, "SVG mask styles cannot be resolved: " + error.Message); return null; }
        }


        internal DrawingGroup? Resolve(Rect objectBounds, Size viewport)
        {
            try { return ResolveCore(objectBounds, viewport); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException or FormatException or OverflowException or InvalidDataException)
            {
                CssDiagnostics.Report("mask-image", CssDiagnosticReason.LossyConversion, null, "SVG mask cannot be rendered: " + error.Message);
                return null;
            }
        }

        private DrawingGroup? ResolveCore(Rect objectBounds, Size viewport)
        {
            if (!Finite(objectBounds) || !double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height) ||
                viewport.Width < 0 || viewport.Height < 0) return null;
            var cache = _plans.GetOrCreateValue(Dispatcher.CurrentDispatcher).Values;
            if (cache.TryGetValue((objectBounds, viewport), out var previous)) return previous;
            var contentBox = mask.Attribute("maskContentUnits")?.Value == "objectBoundingBox";
            var regionBox = mask.Attribute("maskUnits")?.Value != "userSpaceOnUse";
            if ((contentBox || regionBox) && (objectBounds.Width == 0 || objectBounds.Height == 0)) return new DrawingGroup();
            var oldWidth = t_viewportWidth; var oldHeight = t_viewportHeight;
            var oldOutlines = t_rasterTextOutlines; var oldUse = t_useDepth;
            try
            {
                using var styleScope = EnterStyles(Styles(viewport));
                using var markerScope = EnterMarkers();
                t_rasterTextOutlines = true; t_useDepth = 0;
                t_viewportWidth = contentBox ? 1 : viewport.Width;
                t_viewportHeight = contentBox ? 1 : viewport.Height;
                var children = new DrawingGroup(); var defs = new Dictionary<string, XElement>(StringComparer.Ordinal);
                CollectDefs(root, defs); ParseChildren(mask, children, defs);
                // mask's opacity/display/filter do not apply. Its ancestors still
                // supply inherited presentation properties to its descendants.
                if (contentBox) children.Transform = new MatrixTransform(new(objectBounds.Width, 0, 0, objectBounds.Height, objectBounds.X, objectBounds.Y));
                var result = new DrawingGroup(); result.Children.Add(children);
                if (new[] { "x", "y", "width", "height" }.Any(name => mask.Attribute(name) is not null))
                {
                    var basis = regionBox ? objectBounds.Size : viewport;
                    if (!Length(mask, "x", -.1 * basis.Width, basis.Width, viewport, out var x) ||
                        !Length(mask, "y", -.1 * basis.Height, basis.Height, viewport, out var y) ||
                        !Length(mask, "width", 1.2 * basis.Width, basis.Width, viewport, out var width) ||
                        !Length(mask, "height", 1.2 * basis.Height, basis.Height, viewport, out var height)) return null;
                    if (regionBox) { x += objectBounds.X; y += objectBounds.Y; }
                    if (!(width > 0 && height > 0)) return new DrawingGroup();
                    result.ClipGeometry = new RectangleGeometry(new(x, y, width, height));
                }
                ResolveMarkerContextPaints(result);
                if (cache.Count >= 16) cache.Clear();
                cache[(objectBounds, viewport)] = result; return result;
            }
            finally { t_viewportWidth = oldWidth; t_viewportHeight = oldHeight; t_rasterTextOutlines = oldOutlines; t_useDepth = oldUse; }
        }

        private static bool Length(XElement owner, string name, double fallback, double basis, Size viewport, out double value)
        {
            value = fallback;
            if (owner.Attribute(name)?.Value is not { } text) return true;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                value = owner.Attribute("maskUnits")?.Value == "userSpaceOnUse" ? number : number * basis;
                return double.IsFinite(value);
            }
            double font = 16, rootFont = 16;
            foreach (var ancestor in owner.AncestorsAndSelf().Reverse())
            { font = ResolveSvgTextFontSize(ancestor, font, rootFont); if (ancestor.Parent is null) rootFont = font; }
            var lengths = new CssLengthContext(font, font, rootFont, viewport.Width, viewport.Height);
            var reader = new CssTokenReader(text, new CssNumericReadContext(lengths));
            if (!reader.TryReadLength(out var length) || !reader.AtEnd) return false;
            var box = owner.Attribute("maskUnits")?.Value != "userSpaceOnUse";
            var percentBasis = box ? 1 : basis;
            if (length.Expression is { } expression)
            { if (!expression.TryEvaluate(lengths, percentBasis, out value)) return false; }
            else if (length.Unit == CssUnit.Percent) value = length.Value * percentBasis / 100;
            else if (!length.TryResolve(lengths, CssPercentBasis.NotSupported, out value)) return false;
            // objectBoundingBox establishes a 1 x 1 user space; px and other
            // resolved lengths are transformed just like unitless coordinates.
            if (box) value *= basis;
            return double.IsFinite(value);
        }
    }

    internal static MaskDefinition? ParseMaskDefinition(byte[] content, string id)
    {
        if (content.Length == 0 || id.Length == 0) return null;
        using var input = new MemoryStream(content, writable: false);
        using var xml = content.Length >= 2 && content[0] == 0x1f && content[1] == 0x8b ? new GZipStream(input, CompressionMode.Decompress) : (Stream)input;
        using var reader = XmlReader.Create(xml, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 8_000_000 });
        var document = XDocument.Load(reader); var root = document.Root;
        if (root?.Name.LocalName != "svg") return null;
        // Bound XML traversal and later rendering recursion without a recursive
        // preflight walk over an adversarial document.
        var pending = new Stack<(XElement Node, int Depth)>(); pending.Push((root, 0)); var count = 0;
        while (pending.TryPop(out var item))
        {
            if (++count > 65_536 || item.Depth > 256) throw new InvalidDataException("SVG mask document exceeds the node/depth budget");
            foreach (var child in item.Node.Elements()) pending.Push((child, item.Depth + 1));
        }
        var match = root.DescendantsAndSelf().FirstOrDefault(element => element.Attribute("id")?.Value == id);
        return match?.Name.LocalName == "mask" ? new(match, root) : null;
    }

    private static SvgMaskBinding? ResolveLocalMask(XElement element, Dictionary<string, XElement> defs, Rect bounds)
    {
        var raw = t_styles is not null && !t_styles.IsSpecified(element, "mask-image")
            ? GetResolvedAttribute(element, "mask") : GetResolvedAttribute(element, "mask-image") ?? GetResolvedAttribute(element, "mask");
        var id = ReadLocalClipId(raw);
        if (id is null) return null;
        var definition = defs.TryGetValue(id, out var mask) && mask.Name.LocalName == "mask" && element.Document?.Root is { } root ? new MaskDefinition(mask, root, t_styles?.Viewport) : null;
        var mode = GetResolvedAttribute(element, "mask-mode")?.Trim().ToLowerInvariant() switch { "alpha" => CssMaskMode.Alpha, "luminance" => CssMaskMode.Luminance, _ => CssMaskMode.MatchSource };
        return new(definition, bounds.IsEmpty ? new Rect(0, 0, 0, 0) : bounds, new(t_viewportWidth, t_viewportHeight), mode);
    }

    private static bool Finite(Rect bounds) => !bounds.IsEmpty && double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) &&
        double.IsFinite(bounds.Width) && double.IsFinite(bounds.Height) && bounds.Width >= 0 && bounds.Height >= 0;
}
