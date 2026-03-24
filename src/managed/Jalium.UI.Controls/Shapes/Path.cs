using Jalium.UI.Media;

namespace Jalium.UI.Controls.Shapes;

/// <summary>
/// Draws a series of connected lines and curves described by a <see cref="PathGeometry"/> or
/// SVG path mini-language string.
/// </summary>
public class Path : Shape
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the <see cref="Data"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(string), typeof(Path),
            new PropertyMetadata(null, OnDataChanged));

    /// <summary>
    /// Identifies the <see cref="Geometry"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public static readonly DependencyProperty GeometryProperty =
        DependencyProperty.Register(nameof(Geometry), typeof(Geometry), typeof(Path),
            new PropertyMetadata(null, OnGeometryChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the geometry data in SVG path mini-language format.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public string? Data
    {
        get => (string?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>
    /// Gets or sets the geometry directly.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public Geometry? Geometry
    {
        get => (Geometry?)GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }

    #endregion

    #region Private Fields

    private PathGeometry? _definingGeometry;
    private bool _suppressPropertySync;
    /// <summary>The rendered geometry with stretch transform applied.</summary>
    private PathGeometry? _renderedGeometry;
    /// <summary>The stretch matrix computed during Arrange.</summary>
    private Matrix _stretchMatrix = Matrix.Identity;
    private bool _hasStretchMatrix;

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_definingGeometry == null || _definingGeometry.Figures.Count == 0)
            return new Size(0, 0);

        var stretch = Stretch;
        Size newSize;

        if (stretch == Stretch.None)
        {
            newSize = GetNaturalSize();
        }
        else
        {
            var strokeThickness = GetStrokeThickness();
            var geometryBounds = _definingGeometry.Bounds;
            GetStretchMetrics(stretch, strokeThickness, availableSize, geometryBounds,
                out _, out _, out _, out _, out newSize);
        }

        if (SizeIsInvalidOrEmpty(newSize))
            return new Size(0, 0);

        return newSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_definingGeometry == null || _definingGeometry.Figures.Count == 0)
        {
            _renderedGeometry = null;
            return finalSize;
        }

        var stretch = Stretch;
        Size newSize;

        if (stretch == Stretch.None)
        {
            _hasStretchMatrix = false;
            _stretchMatrix = Matrix.Identity;
            _renderedGeometry = null;
            newSize = finalSize;
        }
        else
        {
            var strokeThickness = GetStrokeThickness();
            var geomBounds = _definingGeometry.Bounds;
            GetStretchMetrics(stretch, strokeThickness, finalSize, geomBounds,
                out var xScale, out var yScale, out var dX, out var dY, out newSize);

            if (stretch is Stretch.Uniform or Stretch.UniformToFill)
            {
                // Center aspect-ratio-preserving content within the allocated path box.
                // Without this, tall/narrow chevrons stay top-left aligned and appear to
                // change size once rotated between collapsed/expanded states.
                var contentWidth = geomBounds.Width * xScale;
                var contentHeight = geomBounds.Height * yScale;
                dX += (finalSize.Width - strokeThickness - contentWidth) / 2;
                dY += (finalSize.Height - strokeThickness - contentHeight) / 2;
            }

            // Build the stretch matrix: ScaleAt(sx, sy, bounds.X, bounds.Y) then Translate(dX, dY)
            var cx = geomBounds.X;
            var cy = geomBounds.Y;
            _stretchMatrix = new Matrix(
                xScale, 0,
                0, yScale,
                cx - cx * xScale + dX,
                cy - cy * yScale + dY);
            _hasStretchMatrix = true;
            _renderedGeometry = null; // Force re-creation on next render
        }

        if (SizeIsInvalidOrEmpty(newSize))
        {
            newSize = new Size(0, 0);
            _renderedGeometry = null;
            _definingGeometry = null;
        }

        return newSize;
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        EnsureRenderedGeometry();

        if (_renderedGeometry == null || _renderedGeometry.Figures.Count == 0)
            return;

        var width = RenderSize.Width;
        var height = RenderSize.Height;
        if (width <= 0 || height <= 0)
            return;

        var pen = GetOrCreatePen();

        if (RenderTransform is Transform renderXform)
        {
            var matrix = renderXform.Value;
            if (!matrix.IsIdentity && _definingGeometry != null)
            {
                var origin = RenderTransformOrigin;
                var cx = origin.X * width;
                var cy = origin.Y * height;
                var centeredRotate = MatrixHelper.CreateCenteredMatrix(matrix, cx, cy);

                // Compose: stretch first (raw→local), then rotate (in local space).
                // Draw _definingGeometry (raw) with combined transform to avoid
                // separate native push ordering issues.
                Matrix combined;
                if (_hasStretchMatrix && !_stretchMatrix.IsIdentity)
                    combined = Matrix.Multiply(_stretchMatrix, centeredRotate);
                else
                    combined = centeredRotate;

                dc.PushTransform(new MatrixTransform(combined));
                try
                {
                    dc.DrawGeometry(Fill, pen, _definingGeometry);
                }
                finally
                {
                    dc.Pop();
                }
                return;
            }
        }

        dc.DrawGeometry(Fill, pen, _renderedGeometry);
    }

    private Pen? GetOrCreatePen()
    {
        if (IsPenNoOp)
        {
            _pen = null;
            return null;
        }

        if (_pen != null)
            return _pen;

        var thickness = Math.Abs(StrokeThickness);
        var pen = new Pen(Stroke, thickness)
        {
            StartLineCap = StrokeStartLineCap,
            EndLineCap = StrokeEndLineCap,
            DashCap = StrokeDashCap,
            LineJoin = StrokeLineJoin,
            MiterLimit = StrokeMiterLimit
        };

        var dashArray = StrokeDashArray;
        var dashOffset = StrokeDashOffset;
        if (dashArray is { Count: > 0 } || dashOffset != 0.0)
        {
            pen.DashStyle = new DashStyle(dashArray, dashOffset);
        }

        _pen = pen;
        return pen;
    }

    /// <summary>
    /// Ensures _renderedGeometry is built with the stretch transform applied via Geometry.Transform.
    /// </summary>
    private void EnsureRenderedGeometry()
    {
        if (_renderedGeometry != null)
            return;

        if (_definingGeometry == null || _definingGeometry.Figures.Count == 0)
            return;

        if (!_hasStretchMatrix || _stretchMatrix.IsIdentity)
        {
            _renderedGeometry = _definingGeometry;
            return;
        }

        // Clone the geometry and set the stretch matrix as its Transform
        _renderedGeometry = ClonePathGeometry(_definingGeometry);

        var existingTransform = _definingGeometry.Transform;
        if (existingTransform == null || existingTransform.Value.IsIdentity)
        {
            _renderedGeometry.Transform = new MatrixTransform(_stretchMatrix);
        }
        else
        {
            _renderedGeometry.Transform = new MatrixTransform(
                Matrix.Multiply(existingTransform.Value, _stretchMatrix));
        }
    }

    #endregion

    #region Stretch Calculation (aligned with WPF Shape.GetStretchMetrics)

    private Size GetNaturalSize()
    {
        if (_definingGeometry == null)
            return new Size(0, 0);

        var bounds = _definingGeometry.Bounds;
        if (bounds.IsEmpty)
            return new Size(0, 0);

        var strokeThickness = GetStrokeThickness();
        var halfStroke = strokeThickness / 2;

        return new Size(
            Math.Max(bounds.Right + halfStroke, 0),
            Math.Max(bounds.Bottom + halfStroke, 0));
    }

    private static void GetStretchMetrics(
        Stretch mode, double strokeThickness, Size availableSize, Rect geometryBounds,
        out double xScale, out double yScale, out double dX, out double dY, out Size stretchedSize)
    {
        if (!geometryBounds.IsEmpty)
        {
            var margin = strokeThickness / 2;

            xScale = Math.Max(availableSize.Width - strokeThickness, 0);
            yScale = Math.Max(availableSize.Height - strokeThickness, 0);
            dX = margin - geometryBounds.X;
            dY = margin - geometryBounds.Y;

            var hasThinDimension = false;

            if (geometryBounds.Width > xScale * double.Epsilon)
            {
                xScale /= geometryBounds.Width;
            }
            else
            {
                xScale = 1;
                if (geometryBounds.Width == 0) hasThinDimension = true;
            }

            if (geometryBounds.Height > yScale * double.Epsilon)
            {
                yScale /= geometryBounds.Height;
            }
            else
            {
                yScale = 1;
                if (geometryBounds.Height == 0) hasThinDimension = true;
            }

            if (mode != Stretch.Fill && !hasThinDimension)
            {
                if (mode == Stretch.Uniform)
                {
                    if (yScale > xScale)
                        yScale = xScale;
                    else
                        xScale = yScale;
                }
                else // UniformToFill
                {
                    if (xScale > yScale)
                        yScale = xScale;
                    else
                        xScale = yScale;
                }
            }

            stretchedSize = new Size(
                geometryBounds.Width * xScale + strokeThickness,
                geometryBounds.Height * yScale + strokeThickness);
        }
        else
        {
            xScale = yScale = 1;
            dX = dY = 0;
            stretchedSize = new Size(0, 0);
        }
    }

    private static PathGeometry ClonePathGeometry(PathGeometry source)
    {
        var clone = new PathGeometry { FillRule = source.FillRule };

        foreach (var figure in source.Figures)
        {
            var newFigure = new PathFigure
            {
                StartPoint = figure.StartPoint,
                IsClosed = figure.IsClosed,
                IsFilled = figure.IsFilled,
            };

            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                        newFigure.Segments.Add(new LineSegment(line.Point, line.IsStroked));
                        break;
                    case PolyLineSegment polyLine:
                    {
                        var s = new PolyLineSegment { IsStroked = polyLine.IsStroked };
                        foreach (var p in polyLine.Points) s.Points.Add(p);
                        newFigure.Segments.Add(s);
                        break;
                    }
                    case BezierSegment bezier:
                        newFigure.Segments.Add(new BezierSegment(
                            bezier.Point1, bezier.Point2, bezier.Point3, bezier.IsStroked));
                        break;
                    case PolyBezierSegment polyBezier:
                    {
                        var s = new PolyBezierSegment { IsStroked = polyBezier.IsStroked };
                        foreach (var p in polyBezier.Points) s.Points.Add(p);
                        newFigure.Segments.Add(s);
                        break;
                    }
                    case QuadraticBezierSegment quad:
                        newFigure.Segments.Add(new QuadraticBezierSegment(
                            quad.Point1, quad.Point2, quad.IsStroked));
                        break;
                    case PolyQuadraticBezierSegment polyQuad:
                    {
                        var s = new PolyQuadraticBezierSegment { IsStroked = polyQuad.IsStroked };
                        foreach (var p in polyQuad.Points) s.Points.Add(p);
                        newFigure.Segments.Add(s);
                        break;
                    }
                    case ArcSegment arc:
                        newFigure.Segments.Add(new ArcSegment(
                            arc.Point, arc.Size, arc.RotationAngle,
                            arc.IsLargeArc, arc.SweepDirection, arc.IsStroked));
                        break;
                }
            }

            clone.Figures.Add(newFigure);
        }

        return clone;
    }

    private static bool SizeIsInvalidOrEmpty(Size size)
    {
        return double.IsNaN(size.Width) || double.IsNaN(size.Height) || size.IsEmpty;
    }

    #endregion

    #region Property Changed

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Path path) return;
        if (path._suppressPropertySync) return;

        var data = (string?)e.NewValue;
        if (string.IsNullOrWhiteSpace(data))
        {
            path._definingGeometry = null;
        }
        else
        {
            try
            {
                path._definingGeometry = Geometry.Parse(data) as PathGeometry;
            }
            catch (FormatException)
            {
                System.Diagnostics.Debug.WriteLine($"[Path] Failed to parse path data: {data}");
                path._definingGeometry = null;
            }
        }

        path._suppressPropertySync = true;
        try { path.Geometry = null; }
        finally { path._suppressPropertySync = false; }

        path._renderedGeometry = null;
        path.InvalidateMeasure();
        path.InvalidateVisual();
    }

    private static void OnGeometryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Path path) return;
        if (path._suppressPropertySync) return;

        var geometry = (Geometry?)e.NewValue;
        if (geometry == null)
        {
            path._definingGeometry = null;
        }
        else
        {
            path._definingGeometry = geometry switch
            {
                PathGeometry pg => pg,
                StreamGeometry sg => sg.GetPathGeometry(),
                _ => geometry.GetFlattenedPathGeometry()
            };
        }

        path._suppressPropertySync = true;
        try { path.Data = null; }
        finally { path._suppressPropertySync = false; }

        path._renderedGeometry = null;
        path.InvalidateMeasure();
        path.InvalidateVisual();
    }

    #endregion
}
