using Clipper2Lib;
using ClipFillRule = Clipper2Lib.FillRule;

namespace Jalium.UI.Media;

/// <summary>Polygon area operations after curves and child transforms are resolved.</summary>
internal static class GeometryBoolean
{
    internal static PathGeometry Combine(PathGeometry first, PathGeometry second, GeometryCombineMode mode, Transform? transform)
    {
        var subject = Contours(first); var clip = Contours(second);
        var frame = Prepare(subject, clip);
        var rule = Rule(first.FillRule);
        if (first.FillRule != second.FillRule)
        {
            subject = Execute(subject, [], ClipType.Union, rule, frame.Precision);
            clip = Execute(clip, [], ClipType.Union, Rule(second.FillRule), frame.Precision);
            rule = ClipFillRule.NonZero;
        }
        var operation = mode switch
        {
            GeometryCombineMode.Union => ClipType.Union,
            GeometryCombineMode.Intersect => ClipType.Intersection,
            GeometryCombineMode.Exclude => ClipType.Difference,
            GeometryCombineMode.Xor => ClipType.Xor,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        var solution = Execute(subject, clip, operation, rule, frame.Precision);
        var result = new PathGeometry { FillRule = FillRule.Nonzero };
        Point Restore(PointD point)
        {
            var restored = new Point(point.x + frame.X, point.y + frame.Y);
            return transform is null ? restored : transform.Value.Transform(restored);
        }
        foreach (var contour in solution)
        {
            if (contour.Count < 3) continue;
            var figure = new PathFigure { StartPoint = Restore(contour[0]), IsClosed = true, IsFilled = true };
            for (var index = 1; index < contour.Count; index++) figure.Segments.Add(new LineSegment(Restore(contour[index]), true));
            result.Figures.Add(figure);
        }
        return result;
    }

    internal static double Area(PathGeometry path)
    {
        var contours = Contours(path); var frame = Prepare(contours, []);
        var solution = Execute(contours, [], ClipType.Union, Rule(path.FillRule), frame.Precision);
        // Compute in the translated frame to avoid catastrophic cancellation at
        // large world offsets. Normalized holes carry the opposite winding.
        return Math.Abs(Clipper.Area(solution));
    }

    private static ClipFillRule Rule(FillRule rule) => rule == FillRule.EvenOdd ? ClipFillRule.EvenOdd : ClipFillRule.NonZero;

    private static PathsD Execute(PathsD subject, PathsD clip, ClipType operation, ClipFillRule rule, int precision)
    {
        if (subject.Count == 0 && clip.Count == 0) return [];
        var engine = new ClipperD(precision) { PreserveCollinear = true };
        engine.AddSubject(subject); engine.AddClip(clip);
        var result = new PathsD();
        if (!engine.Execute(operation, rule, result))
            throw new InvalidOperationException("Geometry polygon clipping failed.");
        return result;
    }

    private static PathsD Contours(PathGeometry path)
    {
        var result = new PathsD();
        foreach (var figure in path.Figures)
        {
            if (!figure.IsFilled) continue;
            var contour = new PathD { new PointD(figure.StartPoint.X, figure.StartPoint.Y) };
            foreach (var segment in figure.Segments)
            {
                if (segment is LineSegment line) contour.Add(new PointD(line.Point.X, line.Point.Y));
                else if (segment is PolyLineSegment poly)
                    foreach (var point in poly.Points) contour.Add(new PointD(point.X, point.Y));
            }
            if (contour.Count >= 3) result.Add(contour);
        }
        return result;
    }

    private static (double X, double Y, int Precision) Prepare(PathsD first, PathsD second)
    {
        var minX = double.PositiveInfinity; var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity; var maxY = double.NegativeInfinity;
        void Bounds(PathsD paths)
        {
            foreach (var contour in paths) foreach (var point in contour)
            {
                if (!double.IsFinite(point.x) || !double.IsFinite(point.y))
                    throw new ArgumentException("Geometry polygon coordinates must be finite.");
                minX = Math.Min(minX, point.x); minY = Math.Min(minY, point.y);
                maxX = Math.Max(maxX, point.x); maxY = Math.Max(maxY, point.y);
            }
        }
        Bounds(first); Bounds(second);
        if (double.IsPositiveInfinity(minX)) return (0, 0, 8);
        var x = minX / 2 + maxX / 2; var y = minY / 2 + maxY / 2;
        var extent = Math.Max(Math.Max(Math.Abs(minX - x), Math.Abs(maxX - x)),
            Math.Max(Math.Abs(minY - y), Math.Abs(maxY - y)));
        if (!double.IsFinite(extent) || extent > 1e14)
            throw new ArgumentOutOfRangeException(nameof(first), "Geometry polygon extent exceeds the supported clipping range.");
        // Keep integer working coordinates within 1e14 while retaining up to
        // eight decimal places for ordinary UI geometry. Clipper's integer
        // engine supplies the winding, holes, concavity and self intersections.
        var precision = Math.Clamp((int)Math.Floor(14 - Math.Log10(Math.Max(extent, 1))), 0, 8);
        void Shift(PathsD paths)
        {
            foreach (var contour in paths) for (var index = 0; index < contour.Count; index++)
                contour[index] = new PointD(contour[index].x - x, contour[index].y - y);
        }
        Shift(first); Shift(second);
        return (x, y, precision);
    }
}
