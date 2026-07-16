using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Bakes uniform scale and rotation operations into path coordinates so small
/// filled glyphs do not depend on native rotation-matrix rasterization.
/// </summary>
internal static class PathGeometryTransformHelper
{
    public static PathGeometry CloneWithTransformBaked(Geometry source)
    {
        var sourceTransform = source.Transform;
        if (sourceTransform == null || sourceTransform.Value.IsIdentity)
        {
            var clone = source is PathGeometry path
                ? path.ClonePathGeometry()
                : source.GetFlattenedPathGeometry();
            clone.Transform = null;
            return clone;
        }

        // An arbitrary affine transform cannot be represented by ArcSegment's
        // radius/rotation tuple in every case (for example, skew). Flatten first
        // so applying the matrix to every point is exact for the resulting path.
        var flattened = source.GetFlattenedPathGeometry();
        var matrix = sourceTransform.Value;
        return Transform(
            flattened,
            matrix.Transform,
            size => size,
            rotationAngle => rotationAngle);
    }

    public static PathGeometry StretchUniform(PathGeometry source, double boxWidth, double boxHeight)
    {
        var bounds = source.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return source.ClonePathGeometry();
        }

        var scale = Math.Min(boxWidth / bounds.Width, boxHeight / bounds.Height);
        var contentWidth = bounds.Width * scale;
        var contentHeight = bounds.Height * scale;
        var offsetX = (boxWidth - contentWidth) / 2.0 - bounds.X * scale;
        var offsetY = (boxHeight - contentHeight) / 2.0 - bounds.Y * scale;

        return Transform(
            source,
            point => new Point(point.X * scale + offsetX, point.Y * scale + offsetY),
            size => new Size(size.Width * scale, size.Height * scale),
            rotationAngle => rotationAngle);
    }

    public static PathGeometry Rotate(PathGeometry source, double angle, double centerX, double centerY)
    {
        var radians = angle * Math.PI / 180.0;
        var cosine = Math.Cos(radians);
        var sine = Math.Sin(radians);

        Point RotatePoint(Point point)
        {
            var deltaX = point.X - centerX;
            var deltaY = point.Y - centerY;
            return new Point(
                centerX + deltaX * cosine - deltaY * sine,
                centerY + deltaX * sine + deltaY * cosine);
        }

        return Transform(
            source,
            RotatePoint,
            size => size,
            rotationAngle => rotationAngle + angle);
    }

    private static PathGeometry Transform(
        PathGeometry source,
        Func<Point, Point> transformPoint,
        Func<Size, Size> transformSize,
        Func<double, double> transformArcRotation)
    {
        var result = new PathGeometry { FillRule = source.FillRule };

        foreach (var figure in source.Figures)
        {
            var transformedFigure = new PathFigure
            {
                StartPoint = transformPoint(figure.StartPoint),
                IsClosed = figure.IsClosed,
                IsFilled = figure.IsFilled,
            };

            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                        transformedFigure.Segments.Add(new LineSegment(transformPoint(line.Point), line.IsStroked));
                        break;
                    case PolyLineSegment polyLine:
                    {
                        var transformed = new PolyLineSegment { IsStroked = polyLine.IsStroked };
                        foreach (var point in polyLine.Points)
                        {
                            transformed.Points.Add(transformPoint(point));
                        }
                        transformedFigure.Segments.Add(transformed);
                        break;
                    }
                    case ArcSegment arc:
                        transformedFigure.Segments.Add(new ArcSegment(
                            transformPoint(arc.Point),
                            transformSize(arc.Size),
                            transformArcRotation(arc.RotationAngle),
                            arc.IsLargeArc,
                            arc.SweepDirection,
                            arc.IsStroked));
                        break;
                    case BezierSegment bezier:
                        transformedFigure.Segments.Add(new BezierSegment(
                            transformPoint(bezier.Point1),
                            transformPoint(bezier.Point2),
                            transformPoint(bezier.Point3),
                            bezier.IsStroked));
                        break;
                    case PolyBezierSegment polyBezier:
                    {
                        var transformed = new PolyBezierSegment { IsStroked = polyBezier.IsStroked };
                        foreach (var point in polyBezier.Points)
                        {
                            transformed.Points.Add(transformPoint(point));
                        }
                        transformedFigure.Segments.Add(transformed);
                        break;
                    }
                    case QuadraticBezierSegment quadratic:
                        transformedFigure.Segments.Add(new QuadraticBezierSegment(
                            transformPoint(quadratic.Point1),
                            transformPoint(quadratic.Point2),
                            quadratic.IsStroked));
                        break;
                    case PolyQuadraticBezierSegment polyQuadratic:
                    {
                        var transformed = new PolyQuadraticBezierSegment { IsStroked = polyQuadratic.IsStroked };
                        foreach (var point in polyQuadratic.Points)
                        {
                            transformed.Points.Add(transformPoint(point));
                        }
                        transformedFigure.Segments.Add(transformed);
                        break;
                    }
                }
            }

            result.Figures.Add(transformedFigure);
        }

        return result;
    }
}
