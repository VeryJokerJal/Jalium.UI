namespace Jalium.UI.Media;

/// <summary>
/// Draws a Geometry using the specified Brush and Pen.
/// </summary>
public sealed class GeometryDrawing : Drawing
{
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
    public Brush? Brush { get; set; }

    /// <summary>
    /// Gets or sets the Pen used to stroke the geometry.
    /// </summary>
    public Pen? Pen { get; set; }

    /// <summary>
    /// Gets or sets the Geometry that describes the shape to draw.
    /// </summary>
    public Geometry? Geometry { get; set; }

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
            }

            return bounds;
        }
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
