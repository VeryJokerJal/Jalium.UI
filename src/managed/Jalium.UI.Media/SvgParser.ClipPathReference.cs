using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Jalium.UI.Media;

internal static partial class SvgParser
{
    /// <summary>An SVG clipPath definition; geometry is resolved in the referencing user space.</summary>
    internal sealed class ClipPathDefinition(XElement clipPath, XElement root, bool objectBoundingBox)
    {
        internal PathGeometry? Resolve(Size size) => Resolve(new Rect(size), size);

        internal PathGeometry? Resolve(Rect objectBounds, Size viewport)
            => Resolve(objectBounds, viewport, new HashSet<XElement>());

        internal PathGeometry? Resolve(Rect objectBounds, Size viewport, HashSet<XElement> active)
        {
            if (!double.IsFinite(objectBounds.X) || !double.IsFinite(objectBounds.Y) ||
                !double.IsFinite(objectBounds.Width) || !double.IsFinite(objectBounds.Height) ||
                objectBounds.Width < 0 || objectBounds.Height < 0 ||
                !double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height) ||
                viewport.Width < 0 || viewport.Height < 0) return null;
            if (!active.Add(clipPath)) return null;
            if (objectBoundingBox && (objectBounds.Width == 0 || objectBounds.Height == 0))
            {
                var empty = new PathGeometry();
                empty.Freeze();
                active.Remove(clipPath);
                return empty;
            }

            var oldWidth = t_viewportWidth;
            var oldHeight = t_viewportHeight;
            t_viewportWidth = objectBoundingBox ? 1 : viewport.Width;
            t_viewportHeight = objectBoundingBox ? 1 : viewport.Height;
            try
            {
                if (!TryParseClipChildren(clipPath, root, active, out var geometry)) return null;
                geometry ??= new PathGeometry();
                geometry = ApplyClipTransform(clipPath, geometry);
                if (objectBoundingBox)
                {
                    geometry = BakeTransform(geometry, new ScaleTransform
                    {
                        ScaleX = objectBounds.Width, ScaleY = objectBounds.Height,
                    });
                    geometry = BakeTransform(geometry, new TranslateTransform
                    {
                        X = objectBounds.X, Y = objectBounds.Y,
                    });
                }
                if (!TryIntersectClipSource(clipPath, root, objectBounds, viewport,
                    active, ref geometry)) return null;
                geometry.Freeze();
                return geometry;
            }
            finally
            {
                t_viewportWidth = oldWidth;
                t_viewportHeight = oldHeight;
                active.Remove(clipPath);
            }
        }
    }

    internal static ClipPathDefinition? ParseClipPathDefinition(byte[] content, string id)
    {
        if (content.Length == 0 || id.Length == 0) return null;
        using var input = new MemoryStream(content, writable: false);
        using var xml = content.Length >= 2 && content[0] == 0x1f && content[1] == 0x8b
            ? new GZipStream(input, CompressionMode.Decompress)
            : (Stream)input;
        using var reader = XmlReader.Create(xml, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 8_000_000,
        });
        var document = XDocument.Load(reader);
        var root = document.Root;
        if (root?.Name.LocalName != "svg") return null;
        var match = root.DescendantsAndSelf().FirstOrDefault(element =>
            element.Attribute("id")?.Value == id);
        if (match?.Name.LocalName != "clipPath") return null;
        return CreateClipPathDefinition(match, root);
    }

    private static ClipPathDefinition CreateClipPathDefinition(XElement clipPath, XElement root)
        => new(clipPath, root,
            clipPath.Attribute("clipPathUnits")?.Value == "objectBoundingBox");

    private static bool TryParseClipChildren(XElement parent, XElement root,
        HashSet<XElement> active, out PathGeometry? geometry)
    {
        geometry = null;
        var parts = new List<PathGeometry>();
        foreach (var child in parent.Elements())
        {
            if (child.Name.LocalName is "title" or "desc" or "metadata" or
                "animate" or "animateColor" or "animateMotion" or "animateTransform" or
                "set" or "script") continue;
            if (!TryParseClipNode(child, root, active, out var part)) return false;
            if (part is not null) parts.Add(part);
        }
        if (parts.Count == 0) return true;
        geometry = parts.Count == 1 ? parts[0] : UnionClipPaths(parts);
        return geometry is not null;
    }

    private static bool TryParseClipNode(XElement element, XElement root,
        HashSet<XElement> active, out PathGeometry? geometry)
    {
        geometry = null;
        if (GetResolvedAttribute(element, "display") == "none") return true;

        var kind = element.Name.LocalName;
        // A hidden container may have a visible child; descendants resolve
        // visibility independently. Text spans likewise retain advances while
        // only their visible glyphs contribute to the clip silhouette.
        if (kind is not ("g" or "text") &&
            (GetResolvedAttribute(element, "visibility") is "hidden" or "collapse")) return true;
        // A line has no filled silhouette, even when it has a visible stroke.
        if (kind == "line") return true;
        if (kind == "g")
        {
            if (!TryParseClipChildren(element, root, active, out geometry)) return false;
        }
        else if (kind == "use")
        {
            var href = element.Attribute("href")?.Value ??
                element.Attribute(XlinkNs + "href")?.Value;
            if (href is null || !href.StartsWith('#') || href.Length == 1) return false;
            var target = root.DescendantsAndSelf().FirstOrDefault(candidate =>
                candidate.Attribute("id")?.Value == href[1..]);
            if (target is null || target.Name.LocalName is "use" or "g" or "clipPath" ||
                !TryParseClipNode(target, root, active, out geometry)) return false;
            if (geometry is not null)
                geometry = BakeTransform(geometry, new TranslateTransform
                {
                    X = ParseDouble(element, "x"), Y = ParseDouble(element, "y"),
                });
        }
        else if (kind == "path")
        {
            var data = element.Attribute("d")?.Value;
            if (string.IsNullOrWhiteSpace(data)) return true;
            try
            {
                var path = PathMarkupParser.ParseSvgPathData(data);
                foreach (var figure in path.Figures) figure.IsClosed = true;
                path.FillRule = GetResolvedAttribute(element, "clip-rule") == "evenodd"
                    ? FillRule.EvenOdd : FillRule.Nonzero;
                geometry = path.GetFlattenedPathGeometry();
            }
            catch (FormatException) { return false; }
        }
        else if (kind == "text")
        {
            if (!TryParseClipText(element, out geometry)) return false;
        }
        else if (kind is "rect" or "circle" or "ellipse" or "polygon" or "polyline")
        {
            try
            {
                var source = kind == "polyline" ? ParseClipPolygon(element) : ParseClipGeometry(element);
                if (source is PathGeometry polygon)
                    polygon.FillRule = GetResolvedAttribute(element, "clip-rule") == "evenodd"
                        ? FillRule.EvenOdd : FillRule.Nonzero;
                geometry = source?.GetFlattenedPathGeometry();
            }
            catch (ArgumentException) { return false; }
        }
        else return false;

        if (geometry is not null)
        {
            var bounds = geometry.Bounds;
            if (!TryIntersectClipSource(element, root,
                bounds.IsEmpty ? new Rect(0, 0, 0, 0) : bounds,
                new Size(t_viewportWidth, t_viewportHeight), active, ref geometry)) return false;
            geometry = ApplyClipTransform(element, geometry);
        }
        return true;
    }

    private static bool TryIntersectClipSource(XElement element, XElement root,
        Rect objectBounds, Size viewport, HashSet<XElement> active, ref PathGeometry geometry)
    {
        var id = ReadLocalClipId(GetResolvedAttribute(element, "clip-path"));
        if (id is null) return true;
        var target = root.DescendantsAndSelf().FirstOrDefault(candidate =>
            candidate.Attribute("id")?.Value == id);
        if (target?.Name.LocalName != "clipPath") return true;
        var referenced = CreateClipPathDefinition(target, root)
            .Resolve(objectBounds, viewport, active);
        if (referenced is null) return false;
        var intersection = IntersectClipPaths(geometry, referenced);
        if (intersection is null) return false;
        geometry = intersection;
        return true;
    }

    private static PathGeometry ApplyClipTransform(XElement element, PathGeometry geometry)
        => ParseTransform(element) is { } transform ? BakeTransform(geometry, transform) : geometry;

    private static PathGeometry BakeTransform(PathGeometry geometry, Transform transform)
    {
        var matrix = transform.Value;
        foreach (var figure in geometry.Figures)
        {
            figure.StartPoint = Map(figure.StartPoint);
            foreach (var segment in figure.Segments)
            {
                if (segment is LineSegment line) line.Point = Map(line.Point);
                else if (segment is PolyLineSegment polyline)
                    for (var index = 0; index < polyline.Points.Count; index++)
                        polyline.Points[index] = Map(polyline.Points[index]);
            }
        }
        Point Map(Point point) => new(point.X * matrix.M11 + point.Y * matrix.M21 + matrix.OffsetX,
            point.X * matrix.M12 + point.Y * matrix.M22 + matrix.OffsetY);
        return geometry;
    }
}
