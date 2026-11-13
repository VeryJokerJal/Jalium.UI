namespace Jalium.UI.Media;

/// <summary>Composes child geometry in its parent's local coordinate system.</summary>
internal static class GeometryComposition
{
    internal static IEnumerable<(Geometry Geometry, Matrix Matrix)> Leaves(GeometryGroup group)
    {
        var pending = new Stack<(Geometry Geometry, Matrix Parent)>();
        Push(group, Matrix.Identity);
        while (pending.TryPop(out var item))
        {
            var matrix = item.Geometry.Transform is { } transform
                ? Matrix.Multiply(transform.Value, item.Parent) : item.Parent;
            if (item.Geometry is GeometryGroup nested) Push(nested, matrix);
            else yield return (item.Geometry, matrix);
        }
        void Push(GeometryGroup parent, Matrix matrix)
        {
            for (var index = parent.Children.Count - 1; index >= 0; index--)
                pending.Push((parent.Children[index], matrix));
        }
    }

    internal static double Scale(Matrix matrix)
    {
        var maximum = Math.Max(Math.Max(Math.Abs(matrix.M11), Math.Abs(matrix.M12)),
            Math.Max(Math.Abs(matrix.M21), Math.Abs(matrix.M22)));
        if (maximum == 0) return 0;
        // The Frobenius norm bounds the largest singular value, including skew.
        // Normalize first so finite, large matrix entries do not overflow early.
        return maximum * Math.Sqrt(Math.Pow(matrix.M11 / maximum, 2) + Math.Pow(matrix.M12 / maximum, 2) +
            Math.Pow(matrix.M21 / maximum, 2) + Math.Pow(matrix.M22 / maximum, 2));
    }

    internal static PathGeometry FlattenChild(Geometry geometry, double tolerance)
    {
        var matrix = geometry.Transform?.Value ?? Matrix.Identity;
        var result = new PathGeometry();
        var flat = geometry.GetFlattenedPathGeometry(tolerance / Math.Max(Scale(matrix), .01), ToleranceType.Absolute);
        result.FillRule = flat.FillRule;
        Append(result, flat, matrix);
        return result;
    }

    internal static void Append(PathGeometry target, PathGeometry source, Matrix matrix)
    {
        foreach (var figure in source.Figures)
        {
            var output = new PathFigure { StartPoint = matrix.Transform(figure.StartPoint),
                IsClosed = figure.IsClosed, IsFilled = figure.IsFilled };
            foreach (var segment in figure.Segments)
            {
                if (segment is LineSegment line)
                    output.Segments.Add(new LineSegment(matrix.Transform(line.Point), line.IsStroked)
                        { IsSmoothJoin = line.IsSmoothJoin });
                else if (segment is PolyLineSegment poly)
                {
                    var transformed = new PolyLineSegment { IsStroked = poly.IsStroked, IsSmoothJoin = poly.IsSmoothJoin };
                    foreach (var point in poly.Points) transformed.Points.Add(matrix.Transform(point));
                    output.Segments.Add(transformed);
                }
            }
            target.Figures.Add(output);
        }
    }

    internal static Rect GroupBounds(GeometryGroup group)
    {
        var bounds = Rect.Empty;
        foreach (var (geometry, matrix) in Leaves(group))
        {
            if (matrix.IsIdentity) { bounds.Union(geometry.Bounds); continue; }
            // Preserve analytic Bezier/ellipse extrema under affine transforms;
            // bounds must not shrink to a coarsely flattened polygon.
            var path = geometry switch
            {
                PathGeometry value => value,
                RectangleGeometry value => value.GetPathGeometry(),
                EllipseGeometry value => value.GetPathGeometry(),
                _ => geometry.GetFlattenedPathGeometry(),
            };
            foreach (var figure in path.Figures) bounds.Union(PathGeometry.GetFigureBounds(figure, matrix));
        }
        return bounds;
    }
}
