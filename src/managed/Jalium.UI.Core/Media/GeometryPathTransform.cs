namespace Jalium.UI.Media;

/// <summary>Copies path coordinates through an affine transform without flattening curves.</summary>
internal static class GeometryPathTransform
{
    internal static void Append(PathGeometry target, PathGeometry source, Matrix matrix)
    {
        foreach (var figure in source.Figures)
        {
            var output = new PathFigure
            {
                StartPoint = matrix.Transform(figure.StartPoint), IsClosed = figure.IsClosed, IsFilled = figure.IsFilled
            };
            var current = figure.StartPoint;
            foreach (var segment in figure.Segments)
            {
                PathSegment? copy = segment switch
                {
                    LineSegment line => new LineSegment(matrix.Transform(line.Point), line.IsStroked),
                    PolyLineSegment poly => new PolyLineSegment(poly.Points.Select(matrix.Transform), poly.IsStroked),
                    BezierSegment cubic => new BezierSegment(matrix.Transform(cubic.Point1), matrix.Transform(cubic.Point2),
                        matrix.Transform(cubic.Point3), cubic.IsStroked),
                    PolyBezierSegment poly => new PolyBezierSegment(poly.Points.Select(matrix.Transform), poly.IsStroked),
                    QuadraticBezierSegment quad => new QuadraticBezierSegment(matrix.Transform(quad.Point1),
                        matrix.Transform(quad.Point2), quad.IsStroked),
                    PolyQuadraticBezierSegment poly => new PolyQuadraticBezierSegment(poly.Points.Select(matrix.Transform), poly.IsStroked),
                    ArcSegment arc => TransformArc(arc, matrix),
                    _ => throw new NotSupportedException($"Unsupported path segment {segment.GetType().Name}."),
                };
                if (copy is null)
                {
                    // A singular affine map collapses an ellipse to a line, but
                    // the projected arc can turn back beyond its endpoints.
                    // Retain those extrema using a tolerance-scaled polyline.
                    var arcPath = new PathGeometry([new PathFigure(current, [segment.Clone()], false)]);
                    var scale = Math.Max(GeometryComposition.Scale(matrix), 0.01);
                    var flat = arcPath.GetFlattenedPathGeometry(0.001 / scale, ToleranceType.Absolute);
                    foreach (var line in flat.Figures[0].Segments)
                    {
                        var mapped = new LineSegment(matrix.Transform(line.GetEndPoint(current)), segment.IsStroked)
                            { IsSmoothJoin = line.IsSmoothJoin };
                        output.Segments.Add(mapped);
                    }
                }
                else
                {
                    copy.IsSmoothJoin = segment.IsSmoothJoin;
                    output.Segments.Add(copy);
                }
                current = segment.GetEndPoint(current);
            }
            target.Figures.Add(output);
        }
    }

    private static ArcSegment? TransformArc(ArcSegment arc, Matrix matrix)
    {
        if (matrix.IsIdentity) return arc.Clone();
        var angle = arc.RotationAngle * Math.PI / 180;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        // Columns of the transformed ellipse basis. Its singular values are
        // the new radii; an eigenvector of B*B^T supplies the major-axis angle.
        var a = (matrix.M11 * cosine + matrix.M21 * sine) * arc.Size.Width;
        var b = (-matrix.M11 * sine + matrix.M21 * cosine) * arc.Size.Height;
        var c = (matrix.M12 * cosine + matrix.M22 * sine) * arc.Size.Width;
        var d = (-matrix.M12 * sine + matrix.M22 * cosine) * arc.Size.Height;
        var scale = Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), Math.Max(Math.Abs(c), Math.Abs(d)));
        if (!(scale > 0) || !double.IsFinite(scale)) return null;
        a /= scale; b /= scale; c /= scale; d /= scale;
        var determinant = a * d - b * c;
        if (Math.Abs(determinant) < 1e-15) return null;
        var x = a * a + b * b;
        var y = c * c + d * d;
        var cross = a * c + b * d;
        var radius = Math.Sqrt((x + y + Math.Sqrt((x - y) * (x - y) + 4 * cross * cross)) / 2);
        var rotation = Math.Atan2(2 * cross, x - y) / 2 * 180 / Math.PI;
        var sweep = determinant < 0
            ? (arc.SweepDirection == SweepDirection.Clockwise ? SweepDirection.Counterclockwise : SweepDirection.Clockwise)
            : arc.SweepDirection;
        return new ArcSegment(matrix.Transform(arc.Point),
            new Size(scale * radius, scale * Math.Abs(determinant) / radius), rotation,
            arc.IsLargeArc, sweep, arc.IsStroked);
    }
}
