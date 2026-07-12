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
