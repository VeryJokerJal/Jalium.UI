using Jalium.UI.Media;

namespace Jalium.UI.Controls.Shapes;

/// <summary>
/// Draws a polygon, which is a connected series of lines that form a closed shape.
/// </summary>
public sealed class Polygon : Shape
{
    /// <summary>
    /// Identifies the Points dependency property.
    /// </summary>
    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(PointCollection), typeof(Polygon),
            new PropertyMetadata(null, OnGeometryPropertyChanged));

    /// <summary>
    /// Identifies the FillRule dependency property.
    /// </summary>
    public static readonly DependencyProperty FillRuleProperty =
        DependencyProperty.Register(nameof(FillRule), typeof(FillRule), typeof(Polygon),
            new PropertyMetadata(FillRule.EvenOdd, OnGeometryPropertyChanged));

    /// <summary>
    /// Gets or sets a collection that contains the vertex points of the polygon.
    /// </summary>
    public PointCollection? Points
    {
        get => (PointCollection?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that specifies how the interior fill of the shape is determined.
    /// </summary>
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
            return Size.Empty;

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

    /// <summary>
    /// Renders the polygon.
    /// </summary>
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var points = Points;
        if (points == null || points.Count < 3)
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
                LineJoin = StrokeLineJoin
            };
        }

        // Create geometry
        var geometry = new PathGeometry();
        var figure = new PathFigure
        {
            StartPoint = points[0],
            IsClosed = true,
            IsFilled = fill != null
        };

        for (int i = 1; i < points.Count; i++)
        {
            figure.Segments.Add(new LineSegment(points[i]));
        }

        geometry.Figures.Add(figure);
        geometry.FillRule = FillRule;

        dc.DrawGeometry(fill, pen, geometry);
    }

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
        if (d is Polygon polygon)
        {
            polygon.InvalidateMeasure();
            polygon.InvalidateVisual();
        }
    }
}
