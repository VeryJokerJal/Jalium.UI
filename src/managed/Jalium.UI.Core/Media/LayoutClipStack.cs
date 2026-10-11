using System.Runtime.CompilerServices;

namespace Jalium.UI.Media;

// An intersection expressed as independent masks, not a polygon boolean operation.
// Keeping masks separate preserves concave figures, fill rules and holes.
internal sealed class LayoutClipStack : Geometry
{
    internal readonly List<(Geometry Geometry, Matrix Mapping)> Clips = new();
    private static readonly ConditionalWeakTable<Geometry, LocalClip> LocalClips = new();

    public override Rect Bounds
    {
        get
        {
            Rect? result = null;
            foreach (var (geometry, mapping) in Clips)
            {
                var matrix = Matrix.Multiply(geometry.Transform?.Value ?? Matrix.Identity, mapping);
                var b = geometry.Bounds;
                if (b.IsEmpty) return Rect.Empty;
                var points = new[] { matrix.Transform(b.TopLeft), matrix.Transform(new Point(b.Right, b.Top)),
                    matrix.Transform(new Point(b.Left, b.Bottom)), matrix.Transform(b.BottomRight) };
                var left = points.Min(p => p.X);
                var top = points.Min(p => p.Y);
                var bounds = new Rect(left, top, points.Max(p => p.X) - left, points.Max(p => p.Y) - top);
                result = result is Rect previous ? Rect.Intersect(previous, bounds) : bounds;
            }
            return result ?? Rect.Empty;
        }
    }

    public override bool FillContains(Point point)
    {
        foreach (var (geometry, mapping) in Clips)
        {
            var matrix = Matrix.Multiply(geometry.Transform?.Value ?? Matrix.Identity, mapping);
            if (!matrix.TryInvert(out var inverse) || !LocalClips.GetValue(geometry, g => new LocalClip(g)).Get(geometry).FillContains(inverse.Transform(point)))
                return false;
        }
        return Clips.Count > 0;
    }

    internal static int Push(DrawingContext context, IClipDrawingContext clips, Geometry geometry)
    {
        if (geometry is not LayoutClipStack stack)
        {
            clips.PushClip(geometry);
            return 1;
        }
        var depth = 0;
        try
        {
            foreach (var (source, mapping) in stack.Clips)
            {
                var matrix = Matrix.Multiply(source.Transform?.Value ?? Matrix.Identity, mapping);
                if (!matrix.TryInvert(out var inverse))
                {
                    clips.PushClip(Geometry.Empty);
                    depth++;
                    break;
                }
                // PushTransform is conjugated about the context's current Offset.
                // The inverse restores drawing coordinates while retaining the mask.
                context.PushTransform(ToTransform(matrix));
                depth++;
                clips.PushClip(LocalClips.GetValue(source, g => new LocalClip(g)).Get(source, matrix.M12 != 0 || matrix.M21 != 0));
                depth++;
                context.PushTransform(ToTransform(inverse));
                depth++;
            }
            return depth;
        }
        catch
        {
            while (depth-- > 0) context.Pop();
            throw;
        }
    }

    private static Transform ToTransform(Matrix matrix) =>
        matrix.M11 == 1 && matrix.M12 == 0 && matrix.M21 == 0 && matrix.M22 == 1
            ? new TranslateTransform(matrix.OffsetX, matrix.OffsetY)
            : new MatrixTransform(matrix);

    protected override Freezable CreateInstanceCore() => new LayoutClipStack();

    private sealed class LocalClip
    {
        private Geometry? _cached;
        private Geometry? _path;
        internal LocalClip(Geometry source) => source.Changed += (_, _) => { _cached = null; _path = null; };
        internal Geometry Get(Geometry g, bool rotated = false)
        {
            if (rotated && g is RectangleGeometry)
            {
                if (_path != null) return _path;
                var path = g.GetFlattenedPathGeometry(0.05, ToleranceType.Absolute);
                path.Transform = null;
                path.Freeze();
                return _path = path;
            }
            if (_cached != null) return _cached;
            // Paths retain curves and fill rules. Other shapes use their existing
            // path conversion; rectangles retain the native rounded-clip fast path.
            var local = g is RectangleGeometry || g is PathGeometry
                ? g.CloneCurrentValue() : g.GetFlattenedPathGeometry(0.05, ToleranceType.Absolute);
            local.Transform = null;
            local.Freeze();
            return _cached = local;
        }
    }
}
