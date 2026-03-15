using Jalium.UI.Media;

namespace Jalium.UI.Controls.Shapes;

/// <summary>
/// Draws a straight line between two points.
/// </summary>
public class Line : Shape
{
    /// <summary>
    /// Identifies the X1 dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty X1Property =
        DependencyProperty.Register(nameof(X1), typeof(double), typeof(Line),
            new PropertyMetadata(0.0, OnGeometryPropertyChanged));

    /// <summary>
    /// Identifies the Y1 dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty Y1Property =
        DependencyProperty.Register(nameof(Y1), typeof(double), typeof(Line),
            new PropertyMetadata(0.0, OnGeometryPropertyChanged));

    /// <summary>
    /// Identifies the X2 dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty X2Property =
        DependencyProperty.Register(nameof(X2), typeof(double), typeof(Line),
            new PropertyMetadata(0.0, OnGeometryPropertyChanged));

    /// <summary>
    /// Identifies the Y2 dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty Y2Property =
        DependencyProperty.Register(nameof(Y2), typeof(double), typeof(Line),
            new PropertyMetadata(0.0, OnGeometryPropertyChanged));

    /// <summary>
    /// Gets or sets the x-coordinate of the line start point.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double X1
    {
        get => (double)GetValue(X1Property)!;
        set => SetValue(X1Property, value);
    }

    /// <summary>
    /// Gets or sets the y-coordinate of the line start point.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double Y1
    {
        get => (double)GetValue(Y1Property)!;
        set => SetValue(Y1Property, value);
    }

    /// <summary>
    /// Gets or sets the x-coordinate of the line end point.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double X2
    {
        get => (double)GetValue(X2Property)!;
        set => SetValue(X2Property, value);
    }

    /// <summary>
    /// Gets or sets the y-coordinate of the line end point.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double Y2
    {
        get => (double)GetValue(Y2Property)!;
        set => SetValue(Y2Property, value);
    }

    /// <summary>
    /// Measures the shape to determine its desired size.
    /// </summary>
    protected override Size MeasureOverride(Size constraint)
    {
        var strokeThickness = StrokeThickness;
        var minX = Math.Min(X1, X2);
        var minY = Math.Min(Y1, Y2);
        var maxX = Math.Max(X1, X2);
        var maxY = Math.Max(Y1, Y2);

        var width = maxX - minX + strokeThickness;
        var height = maxY - minY + strokeThickness;

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
    /// Renders the line.
    /// </summary>
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var stroke = Stroke;
        if (stroke == null)
            return;

        var pen = new Pen(stroke, StrokeThickness)
        {
            StartLineCap = StrokeStartLineCap,
            EndLineCap = StrokeEndLineCap,
            LineJoin = StrokeLineJoin
        };

        dc.DrawLine(pen, new Point(X1, Y1), new Point(X2, Y2));
    }

    private static void OnGeometryPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Line line)
        {
            line.InvalidateMeasure();
            line.InvalidateVisual();
        }
    }
}
