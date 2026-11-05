using System.Xml.Linq;
using Jalium.UI.Styling;

namespace Jalium.UI.Media;

internal static partial class SvgParser
{
    [ThreadStatic] private static MarkerState? t_markers;
    private sealed class MarkerContext(Brush? fill, Brush? stroke, Rect bounds)
    {
        internal Brush? Fill = fill, Stroke = stroke;
        internal Rect Bounds = bounds;
        internal Matrix World = Matrix.Identity;
        internal bool NonScalingStroke;
    }
    private sealed record ContextPaint(MarkerContext Origin);
    private sealed class MarkerState
    {
        internal MarkerContext? Current;
        internal readonly HashSet<XElement> Active = [];
        internal readonly Dictionary<Drawing, MarkerContext> Contexts = [];
        internal readonly Dictionary<Drawing, Rect> ObjectBounds = [];
        internal readonly Dictionary<Brush, ContextPaint> Paints = [];
        internal int Instances;
    }
    private sealed class MarkerScope(MarkerState? previous) : IDisposable
    { public void Dispose() => t_markers = previous; }
    private static IDisposable EnterMarkers()
    {
        var previous = t_markers; t_markers = new();
        return new MarkerScope(previous);
    }
    private sealed class PaintScope(MarkerState state, MarkerContext? previous) : IDisposable
    { public void Dispose() => state.Current = previous; }
    private static IDisposable? EnterUseContext(XElement element, DrawingGroup drawing, Dictionary<string, XElement> defs)
    {
        if (t_markers is not { } state) return null;
        var context = new MarkerContext(
            ResolveBrush(ResolveCurrentColor(element, GetResolvedAttribute(element, "fill") ?? "black"), defs),
            ResolveBrush(ResolveCurrentColor(element, GetResolvedAttribute(element, "stroke") ?? "none"), defs), Rect.Empty);
        var previous = state.Current; state.Current = context; state.Contexts.Add(drawing, context);
        return new PaintScope(state, previous);
    }

    private static MarkerContext? CreateMarkerContext(XElement element, Geometry geometry, Dictionary<string, XElement> defs)
    {
        if (t_markers is null || (GetResolvedAttribute(element, "marker-start") is null or "none") &&
            (GetResolvedAttribute(element, "marker-mid") is null or "none") && (GetResolvedAttribute(element, "marker-end") is null or "none")) return null;
        return new(ResolveBrush(ResolveCurrentColor(element, GetResolvedAttribute(element, "fill") ?? "black"), defs),
            ResolveBrush(ResolveCurrentColor(element, GetResolvedAttribute(element, "stroke") ?? "none"), defs), geometry.Bounds)
            { NonScalingStroke = GetResolvedAttribute(element, "vector-effect") == "non-scaling-stroke" };
    }

    private static Brush? ResolveMarkerContextBrush(string value)
    {
        if (t_markers?.Current is not { } context) return null;
        var source = value == "context-fill" ? context.Fill : context.Stroke;
        if (source is null) return null;
        var brush = (Brush)source.CloneCurrentValue();
        t_markers.Paints.Add(brush, t_markers.Paints.GetValueOrDefault(source) ?? new(context));
        return brush;
    }

    private static Drawing? CreateMarkers(XElement element, Geometry geometry, Dictionary<string, XElement> defs, MarkerContext? context)
    {
        if (context is null) return null;
        var references = new Dictionary<SvgMarkerPositions.Kind, XElement>();
        foreach (var (kind, name) in new[] { (SvgMarkerPositions.Kind.Start, "marker-start"), (SvgMarkerPositions.Kind.Mid, "marker-mid"), (SvgMarkerPositions.Kind.End, "marker-end") })
        {
            var id = ReadLocalClipId(GetResolvedAttribute(element, name));
            if (id is not null && defs.TryGetValue(id, out var definition) && definition.Name.LocalName == "marker" &&
                definition.Name.Namespace == element.Name.Namespace) references[kind] = definition;
        }
        if (references.Count == 0 || t_markers is null) return null;
        var group = new DrawingGroup();
        foreach (var placement in SvgMarkerPositions.Get(geometry))
            if (references.TryGetValue(placement.Kind, out var definition) && ParseMarker(definition, placement,
                ResolveStrokeLength(element, "stroke-width", 1), defs, context) is { } marker) group.Children.Add(marker);
        return group.Children.Count == 0 ? null : group;
    }

    private static Drawing? ParseMarker(XElement marker, SvgMarkerPositions.Placement placement, double strokeWidth,
        Dictionary<string, XElement> defs, MarkerContext context)
    {
        var state = t_markers!;
        if (!state.Active.Add(marker)) return null;
        var previous = state.Current; var oldWidth = t_viewportWidth; var oldHeight = t_viewportHeight;
        using var definitionStyle = t_styles?.EnterDefinition();
        try
        {
            if (++state.Instances > 65_536) throw new InvalidDataException("SVG marker instances exceed the rendering budget");
            var width = MarkerLength(marker, "markerWidth", oldWidth, 3);
            var height = MarkerLength(marker, "markerHeight", oldHeight, 3);
            var units = marker.Attribute("markerUnits")?.Value == "userSpaceOnUse" ? 1 : strokeWidth;
            if (width <= 0 || height <= 0 || units <= 0 || !double.IsFinite(width * units) || !double.IsFinite(height * units)) return null;
            Rect? viewBox = null;
            if (marker.Attribute("viewBox")?.Value is { } box)
            {
                var values = ParseNumberList(box);
                if (values.Count == 4 && values.All(double.IsFinite))
                {
                    if (values[2] <= 0 || values[3] <= 0) return null;
                    viewBox = new(values[0], values[1], values[2], values[3]);
                }
            }
            t_viewportWidth = viewBox?.Width ?? width; t_viewportHeight = viewBox?.Height ?? height;
            var view = MarkerViewBox(marker.Attribute("preserveAspectRatio")?.Value, viewBox, width, height);
            var reference = view.Transform(new Point(MarkerReference(marker, "refX", t_viewportWidth), MarkerReference(marker, "refY", t_viewportHeight)));
            var angle = MarkerAngle(marker.Attribute("orient")?.Value, placement);
            var position = Matrix.Multiply(new TranslateTransform(-reference.X, -reference.Y).Value, new ScaleTransform(units, units).Value);
            position = Matrix.Multiply(position, new RotateTransform(angle).Value);
            position = Matrix.Multiply(position, new TranslateTransform(placement.Point.X, placement.Point.Y).Value);
            var contents = new DrawingGroup { Transform = new MatrixTransform(view) };
            state.Current = context;
            ParseChildren(marker, contents, defs);
            if (contents.Children.Count == 0) return null;
            ApplyClipPath(marker, contents, defs);
            var objectBounds = Rect.Empty;
            foreach (var child in contents.Children) objectBounds.Union(SvgObjectBounds(child));
            contents.SvgMask = ResolveLocalMask(marker, defs, objectBounds);
            var viewport = new DrawingGroup { Transform = new MatrixTransform(position) };
            if (units == strokeWidth && marker.Attribute("markerUnits")?.Value != "userSpaceOnUse" &&
                context is not null && context.NonScalingStroke)
                viewport.SvgStrokeMarker = new(reference, placement.Point, angle, strokeWidth);
            viewport.Children.Add(contents);
            if (GetResolvedAttribute(marker, "overflow") != "visible")
                viewport.ClipGeometry = new RectangleGeometry(new Rect(0, 0, width, height)).GetPathGeometry();
            ApplyOpacity(marker, viewport);
            return viewport;
        }
        finally
        {
            state.Current = previous; state.Active.Remove(marker);
            t_viewportWidth = oldWidth; t_viewportHeight = oldHeight;
        }
    }

    private static double MarkerLength(XElement marker, string name, double basis, double fallback)
    {
        var value = marker.Attribute(name)?.Value;
        if (value is null) return fallback;
        var reader = new CssTokenReader(value);
        if (!reader.TryReadLength(out var length) || !reader.AtEnd) return fallback;
        var context = StrokeLengths(marker);
        var valid = length.Expression is { } expression ? expression.TryEvaluate(context, basis, out var result)
            : length.Unit == CssUnit.Percent ? double.IsFinite(result = length.Value * basis / 100)
            : length.TryResolve(context, CssPercentBasis.NotSupported, out result);
        return valid && double.IsFinite(result) ? result : fallback;
    }

    private static double MarkerReference(XElement marker, string name, double basis)
    {
        var keyword = marker.Attribute(name)?.Value;
        if (keyword == "center") return basis / 2;
        if (keyword == (name == "refX" ? "right" : "bottom")) return basis;
        if (keyword == (name == "refX" ? "left" : "top")) return 0;
        return MarkerLength(marker, name, basis, 0);
    }

    private static double MarkerAngle(string? value, SvgMarkerPositions.Placement placement)
    {
        if (value is "auto" or "auto-start-reverse")
            return placement.Angle + (value == "auto-start-reverse" && placement.Kind == SvgMarkerPositions.Kind.Start ? 180 : 0);
        var reader = new CssTokenReader(value ?? "0");
        if (!reader.TryReadNumber(out var number, out var unit) || !reader.AtEnd) return 0;
        var result = unit switch { CssUnit.None or CssUnit.Deg => number, CssUnit.Rad => number * 180 / Math.PI,
            CssUnit.Grad => number * .9, CssUnit.Turn => number * 360, _ => 0 };
        return double.IsFinite(result) ? result : 0;
    }

    private static Matrix MarkerViewBox(string? value, Rect? box, double width, double height)
    {
        if (box is not { } view) return Matrix.Identity;
        var words = (value ?? "xMidYMid meet").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Where(word => word != "defer").ToArray();
        var alignment = words.FirstOrDefault() ?? "xMidYMid";
        var sx = width / view.Width; var sy = height / view.Height;
        if (alignment == "none") return new(sx, 0, 0, sy, -view.X * sx, -view.Y * sy);
        if (!new[] { "xMinYMin", "xMidYMin", "xMaxYMin", "xMinYMid", "xMidYMid", "xMaxYMid", "xMinYMax", "xMidYMax", "xMaxYMax" }.Contains(alignment)) alignment = "xMidYMid";
        var scale = words.ElementAtOrDefault(1) == "slice" ? Math.Max(sx, sy) : Math.Min(sx, sy);
        var ax = alignment.StartsWith("xMin") ? 0 : alignment.StartsWith("xMax") ? 1 : .5;
        var ay = alignment.EndsWith("YMin") ? 0 : alignment.EndsWith("YMax") ? 1 : .5;
        return new(scale, 0, 0, scale, (width - view.Width * scale) * ax - view.X * scale, (height - view.Height * scale) * ay - view.Y * scale);
    }

    private static void ResolveMarkerContextPaints(Drawing root)
    {
        if (t_markers is not { Paints.Count: > 0 } state) return;
        var drawings = new List<(Drawing Drawing, Matrix World)>();
        void Visit(Drawing drawing, Matrix parent)
        {
            using var svgSpace = drawing is DrawingGroup svgGroup ? SvgStrokeSpace.Enter(svgGroup) : SvgStrokeSpace.Preserve();
            var world = SvgStrokeSpace.World;
            if (state.Contexts.TryGetValue(drawing, out var context)) context.World = world;
            drawings.Add((drawing, world));
            if (drawing is DrawingGroup group) foreach (var child in group.Children) Visit(child, world);
        }
        Visit(root, Matrix.Identity);
        foreach (var (drawing, world) in drawings)
        {
            if (drawing is GeometryDrawing geometry)
            {
                var bounds = geometry.Bounds;
                geometry.Brush = ContextBrush(geometry.Brush, geometry.Geometry?.Bounds ?? bounds, world, state);
                if (geometry.Pen is { Brush: { } penBrush } pen && state.Paints.ContainsKey(penBrush))
                { var copy = pen.CloneCurrentValue(); copy.Brush = ContextBrush(pen.Brush, bounds, world, state); geometry.Pen = copy; }
            }
            else if (drawing is GlyphRunDrawing glyph && glyph.ForegroundBrush is { } brush)
                glyph.ForegroundBrush = ContextBrush(brush, glyph.Bounds, world, state);
        }
    }

    private static Brush? ContextBrush(Brush? brush, Rect bounds, Matrix world, MarkerState state)
    {
        if (brush is not GradientBrush gradient || !state.Paints.TryGetValue(brush, out var paint)) return brush;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0 || !world.TryInvert(out var inverse)) return null;
        var sourceToLocal = Matrix.Multiply(paint.Origin.World, inverse);
        var source = (GradientBrush)gradient.CloneCurrentValue(); source.Opacity = 1;
        var box = paint.Origin.Bounds;
        var paintToLocal = GradientPaintCoordinates.GradientToTarget(source, box, default) * sourceToLocal;
        if (!GradientPaintCoordinates.TryInvert(paintToLocal, out _)) return null;
        if (source is LinearGradientBrush linear)
        {
            linear.StartPoint = GradientPaintCoordinates.MapPoint(source, linear.StartPoint, box, default);
            linear.EndPoint = GradientPaintCoordinates.MapPoint(source, linear.EndPoint, box, default);
        }
        if (source is RadialGradientBrush radial)
        {
            radial.Center = GradientPaintCoordinates.MapPoint(source, radial.Center, box, default);
            radial.GradientOrigin = GradientPaintCoordinates.MapPoint(source, radial.GradientOrigin, box, default);
            radial.RadiusX = GradientPaintCoordinates.MapRadius(source, radial.RadiusX, box.Width);
            radial.RadiusY = GradientPaintCoordinates.MapRadius(source, radial.RadiusY, box.Height);
            radial.SvgFocalRadius = GradientPaintCoordinates.MapRadius(source, radial.SvgFocalRadius, box.Width);
        }
        source.MappingMode = BrushMappingMode.Absolute;
        source.UsesNormalizedInterpolation = false;
        source.RelativeTransform = null;
        source.Transform = new MatrixTransform(paintToLocal);
        source.Opacity = gradient.Opacity;
        return source;
    }
}
