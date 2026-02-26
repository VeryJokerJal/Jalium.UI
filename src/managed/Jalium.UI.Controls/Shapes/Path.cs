using Jalium.UI.Media;

namespace Jalium.UI.Controls.Shapes;

/// <summary>
/// Draws a series of connected lines and curves.
/// </summary>
public sealed class Path : Shape
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Data dependency property.
    /// </summary>
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
        // Get the natural bounds of the path geometry
        var geomBounds = _pathGeometry?.Bounds ?? Rect.Empty;
        var naturalSize = geomBounds.Width > 0 && geomBounds.Height > 0
            ? new Size(geomBounds.Width + geomBounds.X, geomBounds.Height + geomBounds.Y)
            : new Size(0, 0);

        // Account for stroke thickness in the natural size
        if (StrokeThickness > 0 && _pathGeometry != null)
        {
            naturalSize = new Size(
                naturalSize.Width + StrokeThickness,
                naturalSize.Height + StrokeThickness);
        }

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
        var scaleX = 1.0;
        var scaleY = 1.0;
        var offsetX = 0.0;
        var offsetY = 0.0;

        var bounds = _pathGeometry.Bounds;
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            switch (Stretch)
            {
                case Stretch.Fill:
                    scaleX = width / bounds.Width;
                    scaleY = height / bounds.Height;
                    offsetX = -bounds.X * scaleX;
                    offsetY = -bounds.Y * scaleY;
                    break;
                case Stretch.Uniform:
                    var scale = Math.Min(width / bounds.Width, height / bounds.Height);
                    scaleX = scaleY = scale;
                    offsetX = (width - bounds.Width * scale) / 2 - bounds.X * scale;
                    offsetY = (height - bounds.Height * scale) / 2 - bounds.Y * scale;
                    break;
                case Stretch.UniformToFill:
                    var scaleFill = Math.Max(width / bounds.Width, height / bounds.Height);
                    scaleX = scaleY = scaleFill;
                    offsetX = (width - bounds.Width * scaleFill) / 2 - bounds.X * scaleFill;
                    offsetY = (height - bounds.Height * scaleFill) / 2 - bounds.Y * scaleFill;
                    break;
            }
        }

        // Transform the geometry and render
        var transformed = TransformGeometry(_pathGeometry, scaleX, scaleY, offsetX, offsetY);

        // Apply RenderTransform (e.g. RotateTransform) if set
        if (RenderTransform is Transform renderXform)
        {
            var matrix = renderXform.Value;
            if (!matrix.IsIdentity)
            {
                transformed = ApplyMatrixTransform(transformed, matrix, width / 2, height / 2);
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
                            TransformPoint(line.Point, scaleX, scaleY, offsetX, offsetY)));
                        break;

                    case BezierSegment bezier:
                        newFigure.Segments.Add(new BezierSegment(
                            TransformPoint(bezier.Point1, scaleX, scaleY, offsetX, offsetY),
                            TransformPoint(bezier.Point2, scaleX, scaleY, offsetX, offsetY),
                            TransformPoint(bezier.Point3, scaleX, scaleY, offsetX, offsetY)));
                        break;

                    case QuadraticBezierSegment quad:
                        newFigure.Segments.Add(new QuadraticBezierSegment(
                            TransformPoint(quad.Point1, scaleX, scaleY, offsetX, offsetY),
                            TransformPoint(quad.Point2, scaleX, scaleY, offsetX, offsetY)));
                        break;
                }
            }

            result.Figures.Add(newFigure);
        }

        return result;
    }

    private static Point TransformPoint(Point p, double scaleX, double scaleY, double offsetX, double offsetY)
    {
        return new Point(p.X * scaleX + offsetX, p.Y * scaleY + offsetY);
    }

    private static PathGeometry ApplyMatrixTransform(PathGeometry source, Matrix matrix, double centerX, double centerY)
    {
        var result = new PathGeometry();
        result.FillRule = source.FillRule;

        foreach (var figure in source.Figures)
        {
            var newFigure = new PathFigure
            {
                StartPoint = ApplyMatrix(figure.StartPoint, matrix, centerX, centerY),
                IsClosed = figure.IsClosed,
                IsFilled = figure.IsFilled,
            };

            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                        newFigure.Segments.Add(new LineSegment(
                            ApplyMatrix(line.Point, matrix, centerX, centerY)));
                        break;

                    case BezierSegment bezier:
                        newFigure.Segments.Add(new BezierSegment(
                            ApplyMatrix(bezier.Point1, matrix, centerX, centerY),
                            ApplyMatrix(bezier.Point2, matrix, centerX, centerY),
                            ApplyMatrix(bezier.Point3, matrix, centerX, centerY)));
                        break;

                    case QuadraticBezierSegment quad:
                        newFigure.Segments.Add(new QuadraticBezierSegment(
                            ApplyMatrix(quad.Point1, matrix, centerX, centerY),
                            ApplyMatrix(quad.Point2, matrix, centerX, centerY)));
                        break;
                }
            }

            result.Figures.Add(newFigure);
        }

        return result;
    }

    private static Point ApplyMatrix(Point p, Matrix m, double cx, double cy)
    {
        var x = p.X - cx;
        var y = p.Y - cy;
        return new Point(
            x * m.M11 + y * m.M21 + m.OffsetX + cx,
            x * m.M12 + y * m.M22 + m.OffsetY + cy);
    }

    #endregion

    #region Property Changed

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Path path)
        {
            var data = (string?)e.NewValue;
            path._pathGeometry = string.IsNullOrWhiteSpace(data) ? null : ArrowIcons.ParseSvgPath(data);
            path.InvalidateMeasure();
            path.InvalidateVisual();
        }
    }

    #endregion
}
