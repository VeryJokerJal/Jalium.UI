using Jalium.UI.Media;

namespace Jalium.UI.Controls.Shapes;

/// <summary>
/// Draws an ellipse.
/// </summary>
public sealed class Ellipse : Shape
{
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

        // Calculate center and radii, accounting for stroke
        var centerX = width / 2;
        var centerY = height / 2;
        var radiusX = (width - strokeThickness) / 2;
        var radiusY = (height - strokeThickness) / 2;

        if (radiusX < 0) radiusX = 0;
        if (radiusY < 0) radiusY = 0;

        Pen? pen = null;
        if (Stroke != null && strokeThickness > 0)
        {
            pen = new Pen(Stroke, strokeThickness);
        }

        dc.DrawEllipse(Fill, pen, new Point(centerX, centerY), radiusX, radiusY);
    }

    #endregion
}
