namespace Jalium.UI.Media;

/// <summary>
/// SVG strokes are constructed in the furthest SVG viewport, not in device pixels.
/// Keep that space separate from a drawing context's DPI and outer transforms.
/// Scopes also cover bounds and the vector rasterizer, and restore on exceptions.
/// </summary>
internal static class SvgStrokeSpace
{
    [ThreadStatic] private static Matrix? t_world;
    [ThreadStatic] private static Matrix? t_viewport;
    [ThreadStatic] private static DrawingGroup? t_group;
    [ThreadStatic] private static Transform? t_transform;
    internal static Matrix World => t_world ?? Matrix.Identity;
    internal static bool IsActive => t_world is not null;
    internal readonly struct Scope(Matrix? world, Matrix? viewport, DrawingGroup? group, Transform? transform) : IDisposable
    {
        public void Dispose() { t_world = world; t_viewport = viewport; t_group = group; t_transform = transform; }
    }
    internal static Scope Preserve() => new(t_world, t_viewport, t_group, t_transform);
    internal static Scope Viewport(Size? size, Rect source)
    {
        var scope = Preserve();
        t_world = null;
        t_group = null;
        t_viewport = size is { Width: > 0, Height: > 0 } value && source.Width > 0 && source.Height > 0
            ? new Matrix(value.Width / source.Width, 0, 0, value.Height / source.Height, 0, 0) : null;
        return scope;
    }
    internal static Scope Enter(DrawingGroup group)
    {
        var scope = Preserve();
        if (ReferenceEquals(t_group, group)) return scope;
        if (group.IsSvgViewport) { t_world = t_viewport ?? Matrix.Identity; t_viewport = null; }
        t_transform = TransformFor(group);
        if (t_world is { } world) t_world = (t_transform?.Value ?? Matrix.Identity) * world;
        t_group = group;
        return scope;
    }

    internal sealed record Marker(Point Reference, Point Position, double Angle, double Width);
    internal static Transform? TransformFor(DrawingGroup group)
    {
        if (ReferenceEquals(t_group, group)) return t_transform;
        if (group.SvgStrokeMarker is not { } marker || t_world is not { } world) return group.Transform;
        if (!GradientPaintCoordinates.TryInvert(world, out var inverse)) return group.Transform;
        var angle = marker.Angle * Math.PI / 180;
        var x = Math.Cos(angle); var y = Math.Sin(angle);
        var hostAngle = Math.Atan2(x * world.M12 + y * world.M22, x * world.M11 + y * world.M21) * 180 / Math.PI;
        var position = world.Transform(marker.Position);
        var map = new TranslateTransform(-marker.Reference.X, -marker.Reference.Y).Value *
            new ScaleTransform(marker.Width, marker.Width).Value * new RotateTransform(hostAngle).Value *
            new TranslateTransform(position.X, position.Y).Value * inverse;
        return new MatrixTransform(map);
    }

    internal static bool TryStroke(Geometry geometry, Pen pen, out GeometryDrawing stroke, out Matrix inverse)
    {
        stroke = null!;
        var world = World;
        if (!GradientPaintCoordinates.TryInvert(world, out inverse)) return false;
        var map = (geometry.Transform?.Value ?? Matrix.Identity) * world;
        if (!GradientPaintCoordinates.TryInvert(map, out _)) return false;
        // Flatten in host units so large/non-uniform transforms cannot magnify
        // the chord error. Preserve subpaths, closure and IsStroked edges.
        var norm = Math.Max(Math.Abs(map.M11) + Math.Abs(map.M21), Math.Abs(map.M12) + Math.Abs(map.M22));
        var path = geometry.GetFlattenedPathGeometry(Math.Clamp(.001 / Math.Max(1, norm), 1e-9, .001), ToleranceType.Absolute);
        path = path.CloneCurrentValue();
        path.Transform = null;
        foreach (var figure in path.Figures)
        {
            figure.StartPoint = map.Transform(figure.StartPoint);
            foreach (var segment in figure.Segments)
            {
                if (segment is LineSegment line) line.Point = map.Transform(line.Point);
                else if (segment is PolyLineSegment poly)
                    for (var i = 0; i < poly.Points.Count; i++) poly.Points[i] = map.Transform(poly.Points[i]);
            }
        }
        var copy = pen.CloneCurrentValue();
        copy.SvgViewportStroke = true;
        copy.Brush = MapPaint(pen.Brush, geometry.Bounds, map);
        stroke = new GeometryDrawing(null, copy, path);
        return true;
    }

    private static Brush? MapPaint(Brush? brush, Rect bounds, Matrix map)
    {
        if (brush is not GradientBrush gradient) return brush;
        var paintMap = GradientPaintCoordinates.GradientToTarget(gradient, bounds, default) * map;
        if (!GradientPaintCoordinates.TryInvert(paintMap, out _)) return null;
        var copy = (GradientBrush)gradient.CloneCurrentValue();
        if (copy is LinearGradientBrush linear)
        {
            linear.StartPoint = GradientPaintCoordinates.MapPoint(gradient, linear.StartPoint, bounds, default);
            linear.EndPoint = GradientPaintCoordinates.MapPoint(gradient, linear.EndPoint, bounds, default);
        }
        if (copy is RadialGradientBrush radial)
        {
            radial.Center = GradientPaintCoordinates.MapPoint(gradient, radial.Center, bounds, default);
            radial.GradientOrigin = GradientPaintCoordinates.MapPoint(gradient, radial.GradientOrigin, bounds, default);
            radial.RadiusX = GradientPaintCoordinates.MapRadius(gradient, radial.RadiusX, bounds.Width);
            radial.RadiusY = GradientPaintCoordinates.MapRadius(gradient, radial.RadiusY, bounds.Height);
            radial.SvgFocalRadius = GradientPaintCoordinates.MapRadius(gradient, radial.SvgFocalRadius, bounds.Width);
        }
        copy.MappingMode = BrushMappingMode.Absolute;
        copy.UsesNormalizedInterpolation = false;
        copy.RelativeTransform = null;
        copy.Transform = new MatrixTransform(paintMap);
        return copy;
    }
}
