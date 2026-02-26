using Jalium.UI.Media;

namespace Jalium.UI.Controls.Shapes;

/// <summary>
/// Provides a base class for shape elements.
/// </summary>
public abstract class Shape : FrameworkElement
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Fill dependency property.
    /// </summary>
    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(Shape),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Stroke dependency property.
    /// </summary>
    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(Shape),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the StrokeThickness dependency property.
    /// </summary>
    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(Shape),
            new PropertyMetadata(1.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Stretch dependency property.
    /// </summary>
    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(nameof(Stretch), typeof(Stretch), typeof(Shape),
            new PropertyMetadata(Stretch.Fill, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the StrokeStartLineCap dependency property.
    /// </summary>
    public static readonly DependencyProperty StrokeStartLineCapProperty =
        DependencyProperty.Register(nameof(StrokeStartLineCap), typeof(PenLineCap), typeof(Shape),
            new PropertyMetadata(PenLineCap.Flat, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the StrokeEndLineCap dependency property.
    /// </summary>
    public static readonly DependencyProperty StrokeEndLineCapProperty =
        DependencyProperty.Register(nameof(StrokeEndLineCap), typeof(PenLineCap), typeof(Shape),
            new PropertyMetadata(PenLineCap.Flat, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the StrokeLineJoin dependency property.
    /// </summary>
    public static readonly DependencyProperty StrokeLineJoinProperty =
        DependencyProperty.Register(nameof(StrokeLineJoin), typeof(PenLineJoin), typeof(Shape),
            new PropertyMetadata(PenLineJoin.Miter, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the brush used to fill the shape.
    /// </summary>
    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used to stroke the shape.
    /// </summary>
    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>
    /// Gets or sets the stroke thickness.
    /// </summary>
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty)!;
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets how the shape is stretched to fill its allocated space.
    /// </summary>
    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty)!;
        set => SetValue(StretchProperty, value);
    }

    /// <summary>
    /// Gets or sets the line cap style for the start of the stroke.
    /// </summary>
    public PenLineCap StrokeStartLineCap
    {
        get => (PenLineCap)(GetValue(StrokeStartLineCapProperty) ?? PenLineCap.Flat);
        set => SetValue(StrokeStartLineCapProperty, value);
    }

    /// <summary>
    /// Gets or sets the line cap style for the end of the stroke.
    /// </summary>
    public PenLineCap StrokeEndLineCap
    {
        get => (PenLineCap)(GetValue(StrokeEndLineCapProperty) ?? PenLineCap.Flat);
        set => SetValue(StrokeEndLineCapProperty, value);
    }

    /// <summary>
    /// Gets or sets the line join style for the stroke.
    /// </summary>
    public PenLineJoin StrokeLineJoin
    {
        get => (PenLineJoin)(GetValue(StrokeLineJoinProperty) ?? PenLineJoin.Miter);
        set => SetValue(StrokeLineJoinProperty, value);
    }

    #endregion

    #region Property Changed

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Shape shape)
        {
            shape.InvalidateVisual();
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Shape shape)
        {
            shape.InvalidateMeasure();
        }
    }

    #endregion
}

/// <summary>
/// Specifies how content is stretched to fill its allocated space.
/// </summary>
public enum Stretch
{
    /// <summary>
    /// The content preserves its original size.
    /// </summary>
    None,

    /// <summary>
    /// The content is resized to fill the destination dimensions.
    /// </summary>
    Fill,

    /// <summary>
    /// The content is resized to fit in the destination dimensions while preserving aspect ratio.
    /// </summary>
    Uniform,

    /// <summary>
    /// The content is resized to fill the destination dimensions while preserving aspect ratio.
    /// Source content is clipped if necessary.
    /// </summary>
    UniformToFill
}
