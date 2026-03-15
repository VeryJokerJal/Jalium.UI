using Jalium.UI.Media;

namespace Jalium.UI.Controls.Shapes;

/// <summary>
/// Draws a rectangle.
/// </summary>
public class Rectangle : Shape
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the RadiusX dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty RadiusXProperty =
        DependencyProperty.Register(nameof(RadiusX), typeof(double), typeof(Rectangle),
            new PropertyMetadata(0.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the RadiusY dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty RadiusYProperty =
        DependencyProperty.Register(nameof(RadiusY), typeof(double), typeof(Rectangle),
            new PropertyMetadata(0.0, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the x-axis radius of the ellipse used to round the corners.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double RadiusX
    {
        get => (double)GetValue(RadiusXProperty)!;
        set => SetValue(RadiusXProperty, value);
    }

    /// <summary>
    /// Gets or sets the y-axis radius of the ellipse used to round the corners.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double RadiusY
    {
        get => (double)GetValue(RadiusYProperty)!;
        set => SetValue(RadiusYProperty, value);
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (Stretch == Stretch.None)
        {
            return new Size(0, 0);
        }

        // Return available size, constrained by Width/Height if set
        var width = double.IsNaN(Width) ? availableSize.Width : Width;
        var height = double.IsNaN(Height) ? availableSize.Height : Height;

        if (double.IsPositiveInfinity(width)) width = 0;
        if (double.IsPositiveInfinity(height)) height = 0;

        return new Size(width, height);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var width = RenderSize.Width;
        var height = RenderSize.Height;

        if (width <= 0 || height <= 0)
            return;

        var strokeThickness = StrokeThickness;
        var halfStroke = strokeThickness / 2;

        // Create rect accounting for stroke
        var rect = new Rect(
            halfStroke,
            halfStroke,
            Math.Max(0, width - strokeThickness),
            Math.Max(0, height - strokeThickness));

        Pen? pen = null;
        if (Stroke != null && strokeThickness > 0)
        {
            pen = new Pen(Stroke, strokeThickness)
            {
                StartLineCap = StrokeStartLineCap,
                EndLineCap = StrokeEndLineCap,
                LineJoin = StrokeLineJoin
            };
        }

        if (RadiusX > 0 || RadiusY > 0)
        {
            dc.DrawRoundedRectangle(Fill, pen, rect, RadiusX, RadiusY);
        }
        else
        {
            dc.DrawRectangle(Fill, pen, rect);
        }
    }

    #endregion

    #region Property Changed

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Rectangle rect)
        {
            rect.InvalidateVisual();
        }
    }

    #endregion
}
