using Jalium.UI.Media;

namespace Jalium.UI.Controls.Shapes;

/// <summary>
/// Draws a series of connected straight lines.
/// </summary>
public class Polyline : Shape
{
    /// <summary>
    /// Identifies the Points dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(PointCollection), typeof(Polyline),
            new PropertyMetadata(null, OnGeometryPropertyChanged));

    /// <summary>
    /// Identifies the FillRule dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty FillRuleProperty =
        DependencyProperty.Register(nameof(FillRule), typeof(FillRule), typeof(Polyline),
            new PropertyMetadata(FillRule.EvenOdd, OnGeometryPropertyChanged));

    /// <summary>
    /// Gets or sets a collection that contains the vertex points of the polyline.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public PointCollection? Points
    {
        get => (PointCollection?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that specifies how the interior fill of the shape is determined.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public FillRule FillRule
    {
        get => (FillRule)(GetValue(FillRuleProperty) ?? FillRule.EvenOdd);
        set => SetValue(FillRuleProperty, value);
    }

    /// <summary>
    /// Measures the shape to determine its desired size.
    /// </summary>
    protected override Size MeasureOverride(Size constraint)
    {
        var points = Points;
        if (points == null || points.Count == 0)
            return default(Size);

        var bounds = GetBounds(points);
        var strokeThickness = StrokeThickness;

        var width = bounds.Width + strokeThickness;
        var height = bounds.Height + strokeThickness;

        return new Size(
            double.IsInfinity(constraint.Width) ? width : Math.Min(width, constraint.Width),
            double.IsInfinity(constraint.Height) ? height : Math.Min(height, constraint.Height));
    }

    /// <summary>
    /// Arranges the shape.
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

    private PathGeometry? _cachedGeometry;

    /// <summary>
    /// Renders the polyline.
    /// </summary>
    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;

        var points = Points;
        if (points == null || points.Count < 2)
            return;

        var fill = Fill;
        var stroke = Stroke;

        if (fill == null && stroke == null)
            return;

        Pen? pen = null;
        if (stroke != null)
        {
            pen = new Pen(stroke, StrokeThickness)
            {
                StartLineCap = StrokeStartLineCap,
                EndLineCap = StrokeEndLineCap,
                DashCap = StrokeDashCap,
                LineJoin = StrokeLineJoin,
                MiterLimit = StrokeMiterLimit
            };
            var dashArray = StrokeDashArray;
            if (dashArray is { Count: > 0 })
            {
                pen.DashStyle = new DashStyle(dashArray, StrokeDashOffset);
            }
        }

        var geometry = EnsureGeometry();
        if (geometry == null) return;
        dc.DrawGeometry(fill, pen, geometry);
    }

    /// <summary>
    /// Builds (and caches) the polyline's open <see cref="PathGeometry"/> from
    /// <see cref="Points"/>. IsFilled is left true unconditionally — the brush passed to
    /// DrawGeometry decides whether the interior is painted — so the same cached geometry
    /// serves both rendering and <see cref="Shape.DefiningGeometry"/>.
    /// </summary>
    private PathGeometry? EnsureGeometry()
    {
        if (_cachedGeometry != null) return _cachedGeometry;
        var points = Points;
        if (points == null || points.Count < 2) return null;

        var geometry = new PathGeometry { FillRule = FillRule };
        var figure = new PathFigure { StartPoint = points[0], IsClosed = false, IsFilled = true };
        for (int i = 1; i < points.Count; i++)
            figure.Segments.Add(new LineSegment(points[i]));
        geometry.Figures.Add(figure);
        _cachedGeometry = geometry;
        return geometry;
    }

    /// <inheritdoc />
    protected override Geometry? DefiningGeometry => EnsureGeometry();

    private static Rect GetBounds(PointCollection points)
    {
        if (points.Count == 0)
            return Rect.Empty;

        var minX = double.MaxValue;
        var minY = double.MaxValue;
        var maxX = double.MinValue;
        var maxY = double.MinValue;

        foreach (var point in points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static void OnGeometryPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Polyline polyline)
        {
            polyline._cachedGeometry = null;
            polyline.InvalidateMeasure();
            polyline.InvalidateVisual();
        }
    }
}
