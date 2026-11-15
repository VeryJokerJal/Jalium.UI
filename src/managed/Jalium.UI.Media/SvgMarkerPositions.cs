namespace Jalium.UI.Media;

/// <summary>SVG vertices and endpoint tangents, before flattening inserts curve samples.</summary>
internal static class SvgMarkerPositions
{
    internal enum Kind { Start, Mid, End }
    internal readonly record struct Placement(Point Point, double Angle, Kind Kind);
    private readonly record struct Edge(Point Start, Point End, Vector Incoming, Vector Outgoing);

    internal static IReadOnlyList<Placement> Get(Geometry geometry)
    {
        var path = geometry switch
        {
            PathGeometry value => value,
            RectangleGeometry value => value.GetPathGeometry(),
            EllipseGeometry value => value.GetPathGeometry(),
            _ => geometry.GetFlattenedPathGeometry(),
        };
        var result = new List<Placement>();
        foreach (var figure in path.Figures)
        {
            var edges = Edges(figure);
            // Basic shape equivalent paths omit a redundant zero-length close
            // vertex. An explicitly authored path retains all its vertices.
            if (figure.IsClosed && (geometry is PathGeometry || edges.Count == 0 || edges[^1].End != figure.StartPoint))
                edges.Add(Line(edges.Count == 0 ? figure.StartPoint : edges[^1].End, figure.StartPoint));
            if (edges.Count == 0)
            {
                result.Add(new(figure.StartPoint, 0, Kind.Mid));
                continue;
            }
            var starts = new Vector[edges.Count]; var ends = new Vector[edges.Count];
            var previous = new Vector();
            for (var i = 0; i < edges.Count; i++)
            {
                starts[i] = Nonzero(edges[i].Incoming) ? edges[i].Incoming : previous;
                if (Nonzero(edges[i].Outgoing)) previous = edges[i].Outgoing;
            }
            var following = new Vector();
            for (var i = edges.Count - 1; i >= 0; i--)
            {
                ends[i] = Nonzero(edges[i].Outgoing) ? edges[i].Outgoing : following;
                if (Nonzero(edges[i].Incoming)) following = edges[i].Incoming;
            }
            for (var i = 0; i < edges.Count; i++)
            {
                if (!Nonzero(starts[i])) starts[i] = Nonzero(ends[i]) ? ends[i] : new(1, 0);
                if (!Nonzero(ends[i])) ends[i] = starts[i];
            }
            var firstAngle = figure.IsClosed ? Bisect(ends[^1], starts[0]) : Angle(starts[0]);
            var lastAngle = figure.IsClosed ? firstAngle : Angle(ends[^1]);
            result.Add(new(figure.StartPoint, firstAngle, Kind.Mid));
            for (var i = 0; i < edges.Count - 1; i++)
                result.Add(new(edges[i].End, Bisect(ends[i], starts[i + 1]), Kind.Mid));
            result.Add(new(edges[^1].End, lastAngle, Kind.Mid));
        }
        if (result.Count > 0)
        {
            result[0] = result[0] with { Kind = Kind.Start };
            if (result.Count == 1) result.Add(result[0] with { Kind = Kind.End });
            else result[^1] = result[^1] with { Kind = Kind.End };
        }
        return result;
    }

    private static List<Edge> Edges(PathFigure figure)
    {
        var edges = new List<Edge>(); var point = figure.StartPoint;
        void Add(Edge edge) { edges.Add(edge); point = edge.End; }
        foreach (var segment in figure.Segments)
        {
            switch (segment)
            {
                case LineSegment line: Add(Line(point, line.Point)); break;
                case PolyLineSegment poly:
                    foreach (var end in poly.Points) Add(Line(point, end));
                    break;
                case BezierSegment curve: Add(Cubic(point, curve.Point1, curve.Point2, curve.Point3)); break;
                case PolyBezierSegment poly:
                    for (var i = 0; i + 2 < poly.Points.Count; i += 3) Add(Cubic(point, poly.Points[i], poly.Points[i + 1], poly.Points[i + 2]));
                    break;
                case QuadraticBezierSegment curve: Add(Quadratic(point, curve.Point1, curve.Point2)); break;
                case PolyQuadraticBezierSegment poly:
                    for (var i = 0; i + 1 < poly.Points.Count; i += 2) Add(Quadratic(point, poly.Points[i], poly.Points[i + 1]));
                    break;
                case ArcSegment arc: Add(Arc(point, arc)); break;
            }
        }
        return edges;
    }

    private static bool Nonzero(Vector vector) => (vector.X != 0 || vector.Y != 0) && double.IsFinite(vector.X) && double.IsFinite(vector.Y);
    private static Vector Difference(Point end, Point start) => new(end.X - start.X, end.Y - start.Y);
    private static Vector First(params Vector[] values) => values.FirstOrDefault(Nonzero);
    private static double Angle(Vector vector) => Math.Atan2(vector.Y, vector.X) * 180 / Math.PI;
    private static double Bisect(Vector incoming, Vector outgoing)
    {
        var first = Angle(incoming); var delta = Angle(outgoing) - first;
        if (delta <= -180) delta += 360;
        if (delta > 180) delta -= 360;
        return first + delta / 2;
    }
    private static Edge Line(Point start, Point end) => new(start, end, Difference(end, start), Difference(end, start));
    private static Edge Cubic(Point start, Point a, Point b, Point end) => new(start, end,
        First(Difference(a, start), Difference(b, start), Difference(end, start)),
        First(Difference(end, b), Difference(end, a), Difference(end, start)));
    private static Edge Quadratic(Point start, Point control, Point end) => new(start, end,
        First(Difference(control, start), Difference(end, start)), First(Difference(end, control), Difference(end, start)));

    private static Edge Arc(Point start, ArcSegment arc)
    {
        var rx = Math.Abs(arc.Size.Width); var ry = Math.Abs(arc.Size.Height);
        if (start == arc.Point || rx == 0 || ry == 0) return Line(start, arc.Point);
        var rotation = arc.RotationAngle * Math.PI / 180;
        var c = Math.Cos(rotation); var s = Math.Sin(rotation);
        var dx = (start.X - arc.Point.X) / 2; var dy = (start.Y - arc.Point.Y) / 2;
        var x = c * dx + s * dy; var y = -s * dx + c * dy;
        var lambda = x * x / (rx * rx) + y * y / (ry * ry);
        if (lambda > 1) { var scale = Math.Sqrt(lambda); rx *= scale; ry *= scale; }
        var denominator = rx * rx * y * y + ry * ry * x * x;
        var coefficient = Math.Sqrt(Math.Max(0, (rx * rx * ry * ry - denominator) / denominator));
        if (arc.IsLargeArc == (arc.SweepDirection == SweepDirection.Clockwise)) coefficient = -coefficient;
        var centerX = coefficient * rx * y / ry;
        var centerY = -coefficient * ry * x / rx;
        var direction = arc.SweepDirection == SweepDirection.Clockwise ? 1 : -1;
        Vector Tangent(double px, double py)
        {
            var sin = (py - centerY) / ry; var cos = (px - centerX) / rx;
            return new(direction * (-rx * sin * c - ry * cos * s), direction * (-rx * sin * s + ry * cos * c));
        }
        return new(start, arc.Point, Tangent(x, y), Tangent(-x, -y));
    }
}
