using Jalium.UI.Media;

namespace Jalium.UI.Controls.Shapes;

/// <summary>
/// Draws a series of connected lines and curves described by a <see cref="PathGeometry"/> or
/// SVG path mini-language string.
/// </summary>
public class Path : Shape
{
    static Path()
    {
        // WPF's Path default is Stretch.None.  Keeping Shape's historical
        // Stretch.Fill default here made every Path in a shared design-space
        // container independently normalize its own geometry bounds.  Icon
        // layers such as a 24x24 base outline plus an inner detail therefore
        // acquired unrelated scales and translations before the containing
        // Viewbox was applied.
        StretchProperty.OverrideMetadata(typeof(Path),
            new PropertyMetadata(Stretch.None, OnStretchChanged));
    }

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

        if (stretch == Stretch.None)
        {
            _hasStretchMatrix = false;
            _stretchMatrix = Matrix.Identity;
            _renderedGeometry = null;
            return finalSize;
        }

        var strokeThickness = GetStrokeThickness();
        var geomBounds = _definingGeometry.Bounds;
        GetStretchMetrics(stretch, strokeThickness, finalSize, geomBounds,
            out var xScale, out var yScale, out var dX, out var dY, out var stretchedSize);

        if (SizeIsInvalidOrEmpty(stretchedSize))
        {
            // Don't null out _definingGeometry here: a zero finalSize can occur transiently
            // (e.g. before the parent has allocated real space), and discarding the source
            // geometry is irreversible — the element could then never render again even once
            // it is given real space. Just skip producing rendered geometry this pass.
            _renderedGeometry = null;
            _hasStretchMatrix = false;
            return new Size(0, 0);
        }

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

        var cx = geomBounds.X;
        var cy = geomBounds.Y;
        _stretchMatrix = new Matrix(
            xScale, 0,
            0, yScale,
            cx - cx * xScale + dX,
            cy - cy * yScale + dY);
        _hasStretchMatrix = true;
        _renderedGeometry = null;

        // Return finalSize (WPF-aligned). The stretch matrix above already centers the
        // geometry inside the finalSize box, so RenderSize must match that box for
        // RenderTransformOrigin (e.g. (0.5, 0.5) on RotateTransform) to land on the
        // geometry's visual center. Returning the smaller stretched size shifts the
        // rotation pivot off-center and makes the rotated shape drift outside the slot.
        return finalSize;
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;

        EnsureRenderedGeometry();

        if (_renderedGeometry == null || _renderedGeometry.Figures.Count == 0)
            return;

        var width = RenderSize.Width;
        var height = RenderSize.Height;
        if (width <= 0 || height <= 0)
            return;

        var pen = GetOrCreatePen();

        // RenderTransform (rotate/skew/etc.) is now pushed generically by
        // Visual.RenderDirect around this element's draw pass. Path's only
        // job is to emit the already-stretched geometry; the framework-pushed
        // transform will rotate it around RenderTransformOrigin correctly.
        // Applying it again here would double-transform the path (the symptom
        // was TreeView chevrons appearing at 45° / off-center after a 90°
        // RotateTransform).
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

        if (Stroke is null)
            return null;

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
        if (dashArray is { Count: > 0 })
        {
            pen.DashStyle = new DashStyle(dashArray, dashOffset);
        }

        _pen = pen;
        return pen;
    }

    /// <summary>
    /// Ensures _renderedGeometry is built with the stretch transform baked into
    /// every point of every figure. We deliberately do NOT set it via
    /// Geometry.Transform — that gets pushed inside DrawGeometry, which would
    /// order it AFTER any RenderTransform pushed by the framework and invert
    /// the correct "stretch first, then rotate" composition, skewing the
    /// rotation anchor off-center.
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

        var existingTransform = _definingGeometry.Transform;
        Matrix bake = _stretchMatrix;
        if (existingTransform != null && !existingTransform.Value.IsIdentity)
        {
            // Preserve an explicit Geometry.Transform set on the source by
            // pre-multiplying it into the bake: raw -> existing -> stretch.
            bake = Matrix.Multiply(existingTransform.Value, _stretchMatrix);
        }

        _renderedGeometry = ClonePathGeometry(_definingGeometry, bake);
        _renderedGeometry.Transform = null;
    }

    /// <inheritdoc />
    protected override Geometry? DefiningGeometry => _definingGeometry;

    /// <inheritdoc />
    public override Transform GeometryTransform =>
        (_hasStretchMatrix && !_stretchMatrix.IsIdentity) ? new MatrixTransform(_stretchMatrix) : Transform.Identity;

    /// <inheritdoc />
    public override Geometry RenderedGeometry
    {
        get
        {
            EnsureRenderedGeometry();
            return (Geometry?)_renderedGeometry ?? _definingGeometry ?? Geometry.Empty;
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
        // Rect.IsEmpty is true when EITHER dimension is zero, which would wrongly reject a
        // single-axis geometry — a horizontal line has Height == 0, a vertical line has
        // Width == 0. The thin-dimension handling below is written precisely to stretch
        // those (scale the zero dimension by 1 and give it strokeThickness of extent), so
        // we must NOT bail out here just because one dimension is zero. Only a box with no
        // extent at all (a single point) or non-finite bounds is genuinely unstretchable.
        // Using Rect.IsEmpty here previously collapsed lines like "M2,8 L12,8" to a (0,0)
        // size and made them permanently invisible.
        bool boundsUsable =
            !double.IsNaN(geometryBounds.Width) && !double.IsNaN(geometryBounds.Height) &&
            !double.IsInfinity(geometryBounds.Width) && !double.IsInfinity(geometryBounds.Height) &&
            geometryBounds.Width >= 0 && geometryBounds.Height >= 0 &&
            (geometryBounds.Width > 0 || geometryBounds.Height > 0);

        if (boundsUsable)
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
        => ClonePathGeometry(source, Matrix.Identity);

    /// <summary>
    /// Clones <paramref name="source"/>, optionally baking <paramref name="bake"/>
    /// into every control point so the returned geometry can be drawn without
    /// relying on its <see cref="Geometry.Transform"/> (which would otherwise
    /// be pushed inside <c>DrawGeometry</c> and sort after an outer
    /// RenderTransform — breaking the "local first, then render-transform"
    /// composition order).
    /// </summary>
    private static PathGeometry ClonePathGeometry(PathGeometry source, Matrix bake)
    {
        var clone = new PathGeometry { FillRule = source.FillRule };
        var identity = bake.IsIdentity;

        Point T(Point p) => identity ? p : bake.Transform(p);

        foreach (var figure in source.Figures)
        {
            var newFigure = new PathFigure
            {
                StartPoint = T(figure.StartPoint),
                IsClosed = figure.IsClosed,
                IsFilled = figure.IsFilled,
            };

            foreach (var segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                        newFigure.Segments.Add(new LineSegment(T(line.Point), line.IsStroked));
                        break;
                    case PolyLineSegment polyLine:
                    {
                        var s = new PolyLineSegment { IsStroked = polyLine.IsStroked };
                        foreach (var p in polyLine.Points) s.Points.Add(T(p));
                        newFigure.Segments.Add(s);
                        break;
                    }
                    case BezierSegment bezier:
                        newFigure.Segments.Add(new BezierSegment(
                            T(bezier.Point1), T(bezier.Point2), T(bezier.Point3), bezier.IsStroked));
                        break;
                    case PolyBezierSegment polyBezier:
                    {
                        var s = new PolyBezierSegment { IsStroked = polyBezier.IsStroked };
                        foreach (var p in polyBezier.Points) s.Points.Add(T(p));
                        newFigure.Segments.Add(s);
                        break;
                    }
                    case QuadraticBezierSegment quad:
                        newFigure.Segments.Add(new QuadraticBezierSegment(
                            T(quad.Point1), T(quad.Point2), quad.IsStroked));
                        break;
                    case PolyQuadraticBezierSegment polyQuad:
                    {
                        var s = new PolyQuadraticBezierSegment { IsStroked = polyQuad.IsStroked };
                        foreach (var p in polyQuad.Points) s.Points.Add(T(p));
                        newFigure.Segments.Add(s);
                        break;
                    }
                    case ArcSegment arc:
                    {
                        // Path's stretch matrix is always pure scale+translate
                        // (no rotation/skew component — see ArrangeOverride),
                        // so the arc's radii just need the same scale factors
                        // applied componentwise; rotation/flags stay valid.
                        var arcSize = identity
                            ? arc.Size
                            : new Size(arc.Size.Width * bake.M11, arc.Size.Height * bake.M22);
                        newFigure.Segments.Add(new ArcSegment(
                            T(arc.Point), arcSize, arc.RotationAngle,
                            arc.IsLargeArc, arc.SweepDirection, arc.IsStroked));
                        break;
                    }
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

    private static void OnStretchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Path path) return;

        path._renderedGeometry = null;
        path.InvalidateMeasure();
        path.InvalidateVisual();
    }

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
