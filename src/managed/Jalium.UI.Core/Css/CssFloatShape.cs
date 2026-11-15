using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>
/// A margin-box-local, flattened contour. Edges include the filled portions of
/// the two clipping sides, so a shape enclosing the margin box still excludes it.
/// Offsetting every edge by a circular capsule implements Euclidean shape-margin,
/// including degenerate lines; it does not inflate a polygon's bounding box.
/// </summary>
internal sealed partial class CssFloatShape : IEquatable<CssFloatShape>
{
    // Curves and circular margin caps target .01 CSS px flattening tolerance.
    private const double Tolerance = .01;
    internal readonly record struct Edge(Point A, Point B)
    {
        internal double Top => Math.Min(A.Y, B.Y);
        internal double Bottom => Math.Max(A.Y, B.Y);
        internal double Slope => (B.X - A.X) / (B.Y - A.Y);
        internal double X(double y) => Math.Clamp(A.X + (B.X - A.X) * Math.Clamp((y - A.Y) / (B.Y - A.Y), 0, 1),
            Math.Min(A.X, B.X), Math.Max(A.X, B.X));
    }
    private Edge[]? _edges;
    internal Edge[] Edges => _edges ??= BuildRasterEdges();
    private readonly int _hash;
    private double _lastY = double.NaN, _lastHeight;
    private (double Left, double Right, bool Active) _lastBand;

    private CssFloatShape(Edge[] edges)
    {
        _edges = edges;
        var hash = new HashCode(); foreach (var edge in edges) hash.Add(edge); _hash = hash.ToHashCode();
    }
    public bool Equals(CssFloatShape? other) => ReferenceEquals(this, other) ||
        other is not null && _hash == other._hash && (_raster is { } raster
            ? other._raster is { } otherRaster && raster.Equals(otherRaster)
            : other._raster is null && Edges.AsSpan().SequenceEqual(other.Edges));
    public override bool Equals(object? other) => other is CssFloatShape shape && Equals(shape);
    public override int GetHashCode() => _hash;

    internal (double Left, double Right, bool Active) Band(double y, double height)
    {
        if (y == _lastY && height == _lastHeight) return _lastBand;
        if (_raster is not null)
        {
            _lastY = y; _lastHeight = height;
            return _lastBand = _raster.Band(y, height);
        }
        var left = double.PositiveInfinity; var right = double.NegativeInfinity;
        foreach (var edge in Edges)
        {
            if (edge.Bottom <= y || edge.Top >= y + height) continue;
            var a = edge.X(Math.Max(y, edge.Top)); var b = edge.X(Math.Min(y + height, edge.Bottom));
            left = Math.Min(left, Math.Min(a, b)); right = Math.Max(right, Math.Max(a, b));
        }
        _lastY = y; _lastHeight = height;
        return _lastBand = (left, right, left <= right);
    }

    internal static CssFloatShape Create(Geometry geometry, double offsetX, double offsetY,
        double width, double height, double margin)
    {
        var outline = new List<Edge>(); var original = new List<Edge>();
        if (width <= 0 || height <= 0) return new([]);
        Point Shift(Point point) => new(point.X + offsetX, point.Y + offsetY);
        var path = geometry.GetFlattenedPathGeometry(Tolerance, ToleranceType.Absolute);
        foreach (var figure in path.Figures)
        {
            if (!figure.IsFilled) continue;
            var start = Shift(figure.StartPoint); var current = start; var count = original.Count;
            void Add(Point next) { original.Add(new(current, next)); current = next; }
            foreach (var segment in figure.Segments)
            {
                if (segment is LineSegment line) Add(Shift(line.Point));
                else if (segment is PolyLineSegment poly)
                    foreach (var point in poly.Points) Add(Shift(point));
            }
            if (current != start || original.Count == count) Add(start);
        }
        // RoundedRectangleGeometry intentionally has no paint path when its
        // area is zero. CSS Shapes still uses its vertical edge (or margin cap).
        if (original.Count == 0 && geometry is CssRoundedRectangleGeometry rectangle)
        {
            var a = Shift(rectangle.Rect.TopLeft); var b = Shift(rectangle.Rect.BottomRight);
            original.Add(new(a, b)); original.Add(new(b, a));
        }
        // A single expanded vertex may already cover the entire clipping box.
        // Avoid tessellating huge margins whose result is simply that rectangle.
        if (margin > 0 && original.Any(edge => Covers(edge.A)))
            return new([new(new(0, 0), new(0, height)), new(new(width, 0), new(width, height))]);
        bool Covers(Point center)
        {
            if (!Finite(center)) return false;
            var x = Math.Max(Math.Abs(center.X), Math.Abs(width - center.X)) / margin;
            var y = Math.Max(Math.Abs(center.Y), Math.Abs(height - center.Y)) / margin;
            return x <= 1 && y <= 1 && x * x + y * y <= 1;
        }
        AddContour(original, path.FillRule);
        if (margin > 0)
            foreach (var edge in original.Where(edge => Finite(edge.A) && Finite(edge.B)).Distinct()) AddContour(Capsule(edge, margin), FillRule.Nonzero);
        return new(outline.Distinct().ToArray());

        void AddContour(IReadOnlyList<Edge> contour, FillRule fillRule)
        {
            foreach (var edge in contour)
            {
                if (!Finite(edge.A) || !Finite(edge.B) || edge.Top == edge.Bottom) continue;
                var t0 = 0d; var t1 = 1d;
                var dx = edge.B.X - edge.A.X; var dy = edge.B.Y - edge.A.Y;
                if (!Clip(-dx, edge.A.X) || !Clip(dx, width - edge.A.X) ||
                    !Clip(-dy, edge.A.Y) || !Clip(dy, height - edge.A.Y)) continue;
                var a = new Point(Math.Clamp(edge.A.X + dx * t0, 0, width), Math.Clamp(edge.A.Y + dy * t0, 0, height));
                var b = new Point(Math.Clamp(edge.A.X + dx * t1, 0, width), Math.Clamp(edge.A.Y + dy * t1, 0, height));
                if (a.Y != b.Y) outline.Add(new(a, b));
                bool Clip(double direction, double distance)
                {
                    if (direction == 0) return distance >= 0;
                    var t = distance / direction;
                    if (direction < 0) { if (t > t1) return false; t0 = Math.Max(t0, t); }
                    else { if (t < t0) return false; t1 = Math.Min(t1, t); }
                    return true;
                }
            }
            AddSide(0); AddSide(width);
            void AddSide(double x)
            {
                var crossings = new SortedSet<double> { 0, height };
                foreach (var edge in contour)
                {
                    if (!Finite(edge.A) || !Finite(edge.B)) continue;
                    if (edge.A.X == edge.B.X)
                    {
                        if (edge.A.X == x) { crossings.Add(Math.Clamp(edge.A.Y, 0, height)); crossings.Add(Math.Clamp(edge.B.Y, 0, height)); }
                        continue;
                    }
                    var t = (x - edge.A.X) / (edge.B.X - edge.A.X);
                    if (t is >= 0 and <= 1) crossings.Add(Math.Clamp(edge.A.Y + t * (edge.B.Y - edge.A.Y), 0, height));
                }
                var values = crossings.ToArray();
                for (var i = 1; i < values.Length; i++)
                {
                    var y = (values[i - 1] + values[i]) / 2; var winding = 0;
                    foreach (var edge in contour)
                    {
                        if (edge.Top > y || edge.Bottom <= y) continue;
                        if (edge.X(y) > x) winding += edge.B.Y > edge.A.Y ? 1 : -1;
                    }
                    if (fillRule == FillRule.EvenOdd ? (winding & 1) != 0 : winding != 0)
                        outline.Add(new(new(x, values[i - 1]), new(x, values[i])));
                }
            }
        }
    }

    private static bool Finite(Point point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

    private static Edge[] Capsule(Edge edge, double radius)
    {
        var angle = Math.Atan2(edge.B.Y - edge.A.Y, edge.B.X - edge.A.X) + Math.PI / 2;
        // Inscribed circular arcs; straight polygon edges remain exact.
        var steps = (int)Math.Clamp(Math.Ceiling(Math.PI / Math.Acos(Math.Clamp(1 - Tolerance / radius, -1, 1))), 2, 4096);
        steps = Math.Min(steps, 4096);
        var points = new List<Point>(steps * 2 + 2);
        void Arc(Point center, double first)
        {
            for (var i = 0; i <= steps; i++)
            {
                var a = first - Math.PI * i / steps;
                points.Add(new(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a)));
            }
        }
        Arc(edge.B, angle); Arc(edge.A, angle - Math.PI);
        return points.Select((point, i) => new Edge(point, points[(i + 1) % points.Count])).ToArray();
    }
}
