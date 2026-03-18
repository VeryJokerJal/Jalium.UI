using Jalium.UI.Media;

namespace Jalium.UI.Controls.Shapes;

/// <summary>
/// Draws a series of connected lines and curves.
/// </summary>
public class Path : Shape
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Data dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(string), typeof(Path),
            new PropertyMetadata(null, OnDataChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the geometry data in SVG path mini-language format.
    /// Supported commands: M/m, L/l, H/h, V/v, C/c (cubic bezier), Z/z
    /// Example: "M 0,5 L 3,8 L 8,2" draws a checkmark
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public string? Data
    {
        get => (string?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    #endregion

    #region Private Fields

    private PathGeometry? _pathGeometry;

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var geomBounds = _pathGeometry?.Bounds ?? Rect.Empty;
        var strokeThickness = Stroke != null && StrokeThickness > 0 ? StrokeThickness : 0;
        var naturalSize = GetNaturalSize(geomBounds, strokeThickness);

        if (Stretch == Stretch.None)
        {
            return naturalSize;
        }

        var hasExplicitWidth = !double.IsNaN(Width);
        var hasExplicitHeight = !double.IsNaN(Height);
        var aspectRatio = naturalSize.Height > 0 ? naturalSize.Width / naturalSize.Height : 1.0;

        double width, height;

        if (hasExplicitWidth && hasExplicitHeight)
        {
            width = Width;
            height = Height;
        }
        else if (hasExplicitWidth && !hasExplicitHeight)
        {
            width = Width;
            height = aspectRatio > 0 ? width / aspectRatio : width;
        }
        else if (!hasExplicitWidth && hasExplicitHeight)
        {
            height = Height;
            width = height * aspectRatio;
        }
        else
        {
            width = double.IsPositiveInfinity(availableSize.Width) ? naturalSize.Width : availableSize.Width;
            height = double.IsPositiveInfinity(availableSize.Height) ? naturalSize.Height : availableSize.Height;
        }

        return new Size(width, height);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc || _pathGeometry == null || _pathGeometry.Figures.Count == 0)
            return;

        var width = RenderSize.Width;
        var height = RenderSize.Height;

        if (width <= 0 || height <= 0)
            return;

        Pen? pen = null;
        if (Stroke != null && StrokeThickness > 0)
        {
            pen = new Pen(Stroke, StrokeThickness)
            {
                StartLineCap = StrokeStartLineCap,
                EndLineCap = StrokeEndLineCap,
                LineJoin = StrokeLineJoin
            };
        }

        // Calculate scaling based on stretch mode
        var bounds = _pathGeometry.Bounds;
        var strokeInset = pen?.Thickness > 0 ? pen.Thickness / 2 : 0.0;
        var targetRect = new Rect(
            strokeInset,
            strokeInset,
            Math.Max(0, width - strokeInset * 2),
            Math.Max(0, height - strokeInset * 2));
        ComputeStretchTransform(bounds, targetRect, Stretch, out var scaleX, out var scaleY, out var offsetX, out var offsetY);

        // Transform the geometry and render
        var transformed = TransformGeometry(_pathGeometry, scaleX, scaleY, offsetX, offsetY);

        // Apply the element-level RenderTransform separately so we preserve
        // arc and multi-point segment fidelity inside the geometry itself.
        if (RenderTransform is Transform renderXform)
        {
            var matrix = renderXform.Value;
            if (!matrix.IsIdentity)
            {
                dc.PushTransform(new MatrixTransform(CreateCenteredMatrix(matrix, width / 2, height / 2)));
                try
                {
                    dc.DrawGeometry(Fill, pen, transformed);
                }
                finally
                {
                    dc.Pop();
                }

                return;
            }
        }

        dc.DrawGeometry(Fill, pen, transformed);
    }

    private static PathGeometry TransformGeometry(PathGeometry source, double scaleX, double scaleY, double offsetX, double offsetY)
    {
        var result = new PathGeometry();
        result.FillRule = source.FillRule;

        foreach (var figure in source.Figures)
        {
            var newFigure = new PathFigure
            {
                StartPoint = TransformPoint(figure.StartPoint, scaleX, scaleY, offsetX, offsetY),
                IsClosed = figure.IsClosed,
                IsFilled = figure.IsFilled,
            };

            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                        newFigure.Segments.Add(new LineSegment(
                            TransformPoint(line.Point, scaleX, scaleY, offsetX, offsetY),
                            line.IsStroked));
                        break;

                    case PolyLineSegment polyLine:
                    {
                        var newSegment = new PolyLineSegment
                        {
                            IsStroked = polyLine.IsStroked
                        };

                        foreach (var point in polyLine.Points)
                        {
                            newSegment.Points.Add(TransformPoint(point, scaleX, scaleY, offsetX, offsetY));
                        }

                        newFigure.Segments.Add(newSegment);
                        break;
                    }

                    case BezierSegment bezier:
                        newFigure.Segments.Add(new BezierSegment(
                            TransformPoint(bezier.Point1, scaleX, scaleY, offsetX, offsetY),
                            TransformPoint(bezier.Point2, scaleX, scaleY, offsetX, offsetY),
                            TransformPoint(bezier.Point3, scaleX, scaleY, offsetX, offsetY),
                            bezier.IsStroked));
                        break;

                    case PolyBezierSegment polyBezier:
                    {
                        var newSegment = new PolyBezierSegment
                        {
                            IsStroked = polyBezier.IsStroked
                        };

                        foreach (var point in polyBezier.Points)
                        {
                            newSegment.Points.Add(TransformPoint(point, scaleX, scaleY, offsetX, offsetY));
                        }

                        newFigure.Segments.Add(newSegment);
                        break;
                    }

                    case QuadraticBezierSegment quad:
                        newFigure.Segments.Add(new QuadraticBezierSegment(
                            TransformPoint(quad.Point1, scaleX, scaleY, offsetX, offsetY),
                            TransformPoint(quad.Point2, scaleX, scaleY, offsetX, offsetY),
                            quad.IsStroked));
                        break;

                    case PolyQuadraticBezierSegment polyQuad:
                    {
                        var newSegment = new PolyQuadraticBezierSegment
                        {
                            IsStroked = polyQuad.IsStroked
                        };

                        foreach (var point in polyQuad.Points)
                        {
                            newSegment.Points.Add(TransformPoint(point, scaleX, scaleY, offsetX, offsetY));
                        }

                        newFigure.Segments.Add(newSegment);
                        break;
                    }

                    case ArcSegment arc:
                        newFigure.Segments.Add(new ArcSegment(
                            TransformPoint(arc.Point, scaleX, scaleY, offsetX, offsetY),
                            new Size(Math.Abs(arc.Size.Width * scaleX), Math.Abs(arc.Size.Height * scaleY)),
                            arc.RotationAngle,
                            arc.IsLargeArc,
                            scaleX * scaleY < 0 ? FlipSweep(arc.SweepDirection) : arc.SweepDirection,
                            arc.IsStroked));
                        break;
                }
            }

            result.Figures.Add(newFigure);
        }

        return result;
    }

    private static Size GetNaturalSize(Rect bounds, double strokeThickness)
    {
        var width = Math.Max(0, bounds.Width);
        var height = Math.Max(0, bounds.Height);

        if (strokeThickness > 0)
        {
            width += strokeThickness;
            height += strokeThickness;
        }

        return new Size(width, height);
    }

    private static void ComputeStretchTransform(
        Rect bounds,
        Rect targetRect,
        Stretch stretch,
        out double scaleX,
        out double scaleY,
        out double offsetX,
        out double offsetY)
    {
        scaleX = 1.0;
        scaleY = 1.0;
        offsetX = targetRect.X - bounds.X;
        offsetY = targetRect.Y - bounds.Y;

        var hasWidth = bounds.Width > 0;
        var hasHeight = bounds.Height > 0;

        if (stretch == Stretch.None)
        {
            if (hasWidth && !hasHeight)
            {
                if (bounds.Width > targetRect.Width && targetRect.Width > 0)
                {
                    scaleX = targetRect.Width / bounds.Width;
                    offsetX = targetRect.X - bounds.X * scaleX;
                }
                else
                {
                    offsetX = targetRect.X + (targetRect.Width - bounds.Width) / 2 - bounds.X;
                }

                offsetY = targetRect.Y + targetRect.Height / 2 - bounds.Y;
            }
            else if (!hasWidth && hasHeight)
            {
                if (bounds.Height > targetRect.Height && targetRect.Height > 0)
                {
                    scaleY = targetRect.Height / bounds.Height;
                    offsetY = targetRect.Y - bounds.Y * scaleY;
                }
                else
                {
                    offsetY = targetRect.Y + (targetRect.Height - bounds.Height) / 2 - bounds.Y;
                }

                offsetX = targetRect.X + targetRect.Width / 2 - bounds.X;
            }
            else if (!hasWidth && !hasHeight)
            {
                offsetX = targetRect.X + targetRect.Width / 2 - bounds.X;
                offsetY = targetRect.Y + targetRect.Height / 2 - bounds.Y;
            }

            return;
        }

        switch (stretch)
        {
            case Stretch.Fill:
                scaleX = hasWidth ? targetRect.Width / bounds.Width : 1.0;
                scaleY = hasHeight ? targetRect.Height / bounds.Height : 1.0;
                offsetX = hasWidth
                    ? targetRect.X - bounds.X * scaleX
                    : targetRect.X + targetRect.Width / 2 - bounds.X;
                offsetY = hasHeight
                    ? targetRect.Y - bounds.Y * scaleY
                    : targetRect.Y + targetRect.Height / 2 - bounds.Y;
                return;

            case Stretch.Uniform:
            case Stretch.UniformToFill:
            {
                if (hasWidth && hasHeight)
                {
                    var scale = stretch == Stretch.Uniform
                        ? Math.Min(targetRect.Width / bounds.Width, targetRect.Height / bounds.Height)
                        : Math.Max(targetRect.Width / bounds.Width, targetRect.Height / bounds.Height);
                    scaleX = scaleY = scale;
                    offsetX = targetRect.X + (targetRect.Width - bounds.Width * scale) / 2 - bounds.X * scale;
                    offsetY = targetRect.Y + (targetRect.Height - bounds.Height * scale) / 2 - bounds.Y * scale;
                    return;
                }

                if (hasWidth)
                {
                    var scale = targetRect.Width / bounds.Width;
                    scaleX = scaleY = scale;
                    offsetX = targetRect.X + (targetRect.Width - bounds.Width * scale) / 2 - bounds.X * scale;
                    offsetY = targetRect.Y + targetRect.Height / 2 - bounds.Y * scale;
                    return;
                }

                if (hasHeight)
                {
                    var scale = targetRect.Height / bounds.Height;
                    scaleX = scaleY = scale;
                    offsetX = targetRect.X + targetRect.Width / 2 - bounds.X * scale;
                    offsetY = targetRect.Y + (targetRect.Height - bounds.Height * scale) / 2 - bounds.Y * scale;
                    return;
                }

                offsetX = targetRect.X + targetRect.Width / 2 - bounds.X;
                offsetY = targetRect.Y + targetRect.Height / 2 - bounds.Y;
                return;
            }
        }
    }

    private static Point TransformPoint(Point p, double scaleX, double scaleY, double offsetX, double offsetY)
    {
        return new Point(p.X * scaleX + offsetX, p.Y * scaleY + offsetY);
    }

    private static SweepDirection FlipSweep(SweepDirection sweepDirection) =>
        sweepDirection == SweepDirection.Clockwise
            ? SweepDirection.Counterclockwise
            : SweepDirection.Clockwise;

    private static Matrix CreateCenteredMatrix(Matrix matrix, double centerX, double centerY)
    {
        return new Matrix(
            matrix.M11,
            matrix.M12,
            matrix.M21,
            matrix.M22,
            matrix.OffsetX + centerX - centerX * matrix.M11 - centerY * matrix.M21,
            matrix.OffsetY + centerY - centerX * matrix.M12 - centerY * matrix.M22);
    }

    #endregion

    #region Property Changed

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Path path)
        {
            var data = (string?)e.NewValue;
            path._pathGeometry = string.IsNullOrWhiteSpace(data)
                ? null
                : Geometry.Parse(data) as PathGeometry;
            path.InvalidateMeasure();
            path.InvalidateVisual();
        }
    }

    #endregion
}
