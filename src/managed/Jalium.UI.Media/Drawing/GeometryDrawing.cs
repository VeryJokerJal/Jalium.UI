namespace Jalium.UI.Media;

/// <summary>
/// Draws a Geometry using the specified Brush and Pen.
/// </summary>
public sealed class GeometryDrawing : Drawing
{
    public static readonly DependencyProperty BrushProperty =
        DependencyProperty.Register(nameof(Brush), typeof(Brush), typeof(GeometryDrawing), new PropertyMetadata(null));
    public static readonly DependencyProperty PenProperty =
        DependencyProperty.Register(nameof(Pen), typeof(Pen), typeof(GeometryDrawing), new PropertyMetadata(null));
    public static readonly DependencyProperty GeometryProperty =
        DependencyProperty.Register(nameof(Geometry), typeof(Geometry), typeof(GeometryDrawing), new PropertyMetadata(null));

    /// <summary>
    /// Initializes a new instance of the <see cref="GeometryDrawing"/> class.
    /// </summary>
    public GeometryDrawing()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GeometryDrawing"/> class
    /// with the specified brush, pen, and geometry.
    /// </summary>
    /// <param name="brush">The brush to use to fill the geometry.</param>
    /// <param name="pen">The pen to use to stroke the geometry.</param>
    /// <param name="geometry">The geometry to draw.</param>
    public GeometryDrawing(Brush? brush, Pen? pen, Geometry? geometry)
    {
        Brush = brush;
        Pen = pen;
        Geometry = geometry;
    }

    /// <summary>
    /// Gets or sets the Brush used to fill the interior of the geometry.
    /// </summary>
    public Brush? Brush
    {
        get => (Brush?)GetValue(BrushProperty);
        set => SetValue(BrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the Pen used to stroke the geometry.
    /// </summary>
    public Pen? Pen
    {
        get => (Pen?)GetValue(PenProperty);
        set => SetValue(PenProperty, value);
    }

    /// <summary>
    /// Gets or sets the Geometry that describes the shape to draw.
    /// </summary>
    public Geometry? Geometry
    {
        get => (Geometry?)GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }

    public new GeometryDrawing Clone() => (GeometryDrawing)base.Clone();
    public new GeometryDrawing CloneCurrentValue() => (GeometryDrawing)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new GeometryDrawing();

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, BrushProperty)
            || ReferenceEquals(e.Property, PenProperty)
            || ReferenceEquals(e.Property, GeometryProperty))
        {
            OnFreezablePropertyChanged(e.OldValue as DependencyObject, e.NewValue as DependencyObject, e.Property);
            WritePostscript();
        }
    }

    /// <inheritdoc />
    public override Rect Bounds
    {
        get
        {
            if (Geometry == null)
            {
                return Rect.Empty;
            }

            var bounds = Geometry.Bounds;

            // Expand bounds for pen thickness
            if (Pen != null && Pen.Thickness > 0)
            {
                var halfThickness = Pen.Thickness / 2;
                bounds = new Rect(
                    bounds.X - halfThickness,
                    bounds.Y - halfThickness,
                    bounds.Width + Pen.Thickness,
                    bounds.Height + Pen.Thickness);
                if (Pen.LineJoin == PenLineJoin.Miter &&
                    double.IsFinite(Pen.MiterLimit) && Pen.MiterLimit >= 1)
                    bounds = IncludeMiterTips(bounds, Geometry, Pen);
            }

            return bounds;
        }
    }

    private static Rect IncludeMiterTips(Rect bounds, Geometry geometry, Pen pen)
    {
        var flattened = geometry.GetFlattenedPathGeometry();
        var transform = geometry.Transform?.Value;
        var half = pen.Thickness / 2;
        var left = bounds.X;
        var top = bounds.Y;
        var right = bounds.Right;
        var bottom = bounds.Bottom;

        Point Map(Point point) => transform is { } matrix ? matrix.Transform(point) : point;

        void IncludeJoin(Point previous, Point vertex, Point next)
        {
            var dx0 = vertex.X - previous.X;
            var dy0 = vertex.Y - previous.Y;
            var dx1 = next.X - vertex.X;
            var dy1 = next.Y - vertex.Y;
            var len0 = Math.Sqrt(dx0 * dx0 + dy0 * dy0);
            var len1 = Math.Sqrt(dx1 * dx1 + dy1 * dy1);
            if (len0 <= 1e-9 || len1 <= 1e-9) return;
            dx0 /= len0;
            dy0 /= len0;
            dx1 /= len1;
            dy1 /= len1;
            var cross = dx0 * dy1 - dy0 * dx1;
            if (Math.Abs(cross) <= 1e-9) return;
            var side = cross > 0 ? -half : half;
            var outer0X = vertex.X - dy0 * side;
            var outer0Y = vertex.Y + dx0 * side;
            var outer1X = vertex.X - dy1 * side;
            var outer1Y = vertex.Y + dx1 * side;
            var t = ((outer1X - outer0X) * dy1 -
                (outer1Y - outer0Y) * dx1) / cross;
            var tipX = outer0X + t * dx0;
            var tipY = outer0Y + t * dy0;
            var reachX = tipX - vertex.X;
            var reachY = tipY - vertex.Y;
            if (!double.IsFinite(tipX) || !double.IsFinite(tipY) ||
                reachX * reachX + reachY * reachY >
                half * half * pen.MiterLimit * pen.MiterLimit) return;
            left = Math.Min(left, tipX);
            top = Math.Min(top, tipY);
            right = Math.Max(right, tipX);
            bottom = Math.Max(bottom, tipY);
        }

        foreach (var figure in flattened.Figures)
        {
            var points = new List<Point> { Map(figure.StartPoint) };
            var strokedEdges = new List<bool>();
            foreach (var segment in figure.Segments)
            foreach (var point in segment.GetPoints())
            {
                points.Add(Map(point));
                strokedEdges.Add(segment.IsStroked);
            }
            var closingStroked = figure.IsClosed;
            if (figure.IsClosed && points.Count > 2 && points[0] == points[^1])
            {
                closingStroked = strokedEdges[^1];
                points.RemoveAt(points.Count - 1);
                strokedEdges.RemoveAt(strokedEdges.Count - 1);
            }
            for (var i = 1; i + 1 < points.Count; i++)
                if (strokedEdges[i - 1] && strokedEdges[i])
                    IncludeJoin(points[i - 1], points[i], points[i + 1]);
            if (figure.IsClosed && points.Count > 2)
            {
                if (closingStroked && strokedEdges[^1])
                    IncludeJoin(points[^2], points[^1], points[0]);
                if (closingStroked && strokedEdges[0])
                    IncludeJoin(points[^1], points[0], points[1]);
            }
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    /// <inheritdoc />
    public override void RenderTo(DrawingContext context)
    {
        if (Geometry != null)
        {
            context.DrawGeometry(Brush, Pen, Geometry);
        }
    }
}
