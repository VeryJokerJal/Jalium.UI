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
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty FillProperty =
        DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(Shape),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Stroke dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(Shape),
            new PropertyMetadata(null, OnPenChanged));

    /// <summary>
    /// Identifies the StrokeThickness dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(Shape),
            new PropertyMetadata(1.0, OnPenChanged));

    /// <summary>
    /// Identifies the Stretch dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(nameof(Stretch), typeof(Stretch), typeof(Shape),
            new PropertyMetadata(Stretch.Fill, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the StrokeStartLineCap dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StrokeStartLineCapProperty =
        DependencyProperty.Register(nameof(StrokeStartLineCap), typeof(PenLineCap), typeof(Shape),
            new PropertyMetadata(PenLineCap.Flat, OnPenChanged));

    /// <summary>
    /// Identifies the StrokeEndLineCap dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StrokeEndLineCapProperty =
        DependencyProperty.Register(nameof(StrokeEndLineCap), typeof(PenLineCap), typeof(Shape),
            new PropertyMetadata(PenLineCap.Flat, OnPenChanged));

    /// <summary>
    /// Identifies the StrokeDashCap dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StrokeDashCapProperty =
        DependencyProperty.Register(nameof(StrokeDashCap), typeof(PenLineCap), typeof(Shape),
            new PropertyMetadata(PenLineCap.Flat, OnPenChanged));

    /// <summary>
    /// Identifies the StrokeLineJoin dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StrokeLineJoinProperty =
        DependencyProperty.Register(nameof(StrokeLineJoin), typeof(PenLineJoin), typeof(Shape),
            new PropertyMetadata(PenLineJoin.Miter, OnPenChanged));

    /// <summary>
    /// Identifies the StrokeDashArray dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StrokeDashArrayProperty =
        DependencyProperty.Register(nameof(StrokeDashArray), typeof(List<double>), typeof(Shape),
            new PropertyMetadata(null, OnPenChanged));

    /// <summary>
    /// Identifies the StrokeDashOffset dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StrokeDashOffsetProperty =
        DependencyProperty.Register(nameof(StrokeDashOffset), typeof(double), typeof(Shape),
            new PropertyMetadata(0.0, OnPenChanged));

    /// <summary>
    /// Identifies the StrokeMiterLimit dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty StrokeMiterLimitProperty =
        DependencyProperty.Register(nameof(StrokeMiterLimit), typeof(double), typeof(Shape),
            new PropertyMetadata(10.0, OnPenChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the brush used to fill the shape.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used to stroke the shape.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>
    /// Gets or sets the stroke thickness.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty)!;
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets how the shape is stretched to fill its allocated space.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty)!;
        set => SetValue(StretchProperty, value);
    }

    /// <summary>
    /// Gets or sets the line cap style for the start of the stroke.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public PenLineCap StrokeStartLineCap
    {
        get => (PenLineCap)(GetValue(StrokeStartLineCapProperty) ?? PenLineCap.Flat);
        set => SetValue(StrokeStartLineCapProperty, value);
    }

    /// <summary>
    /// Gets or sets the line cap style for the end of the stroke.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public PenLineCap StrokeEndLineCap
    {
        get => (PenLineCap)(GetValue(StrokeEndLineCapProperty) ?? PenLineCap.Flat);
        set => SetValue(StrokeEndLineCapProperty, value);
    }

    /// <summary>
    /// Gets or sets the line cap style for dashes in the stroke.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public PenLineCap StrokeDashCap
    {
        get => (PenLineCap)(GetValue(StrokeDashCapProperty) ?? PenLineCap.Flat);
        set => SetValue(StrokeDashCapProperty, value);
    }

    /// <summary>
    /// Gets or sets the line join style for the stroke.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public PenLineJoin StrokeLineJoin
    {
        get => (PenLineJoin)(GetValue(StrokeLineJoinProperty) ?? PenLineJoin.Miter);
        set => SetValue(StrokeLineJoinProperty, value);
    }

    /// <summary>
    /// Gets or sets the dash pattern for the stroke.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public List<double>? StrokeDashArray
    {
        get => (List<double>?)GetValue(StrokeDashArrayProperty);
        set => SetValue(StrokeDashArrayProperty, value);
    }

    /// <summary>
    /// Gets or sets the distance within the dash pattern where a dash begins.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double StrokeDashOffset
    {
        get => (double)GetValue(StrokeDashOffsetProperty)!;
        set => SetValue(StrokeDashOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets the limit on the ratio of the miter length to half the StrokeThickness.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public double StrokeMiterLimit
    {
        get => (double)GetValue(StrokeMiterLimitProperty)!;
        set => SetValue(StrokeMiterLimitProperty, value);
    }

    #endregion

    #region Internal Helpers

    /// <summary>
    /// Returns true if Stroke is null or StrokeThickness is zero/NaN.
    /// </summary>
    internal bool IsPenNoOp
    {
        get
        {
            var strokeThickness = StrokeThickness;
            return Stroke == null || double.IsNaN(strokeThickness) || strokeThickness == 0;
        }
    }

    /// <summary>
    /// Returns the effective stroke thickness (0 if pen is no-op).
    /// </summary>
    internal double GetStrokeThickness()
    {
        return IsPenNoOp ? 0 : Math.Abs(StrokeThickness);
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

    private static void OnPenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Shape shape)
        {
            shape._pen = null;
            shape.InvalidateMeasure();
            shape.InvalidateVisual();
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Shape shape)
        {
            shape.InvalidateMeasure();
            shape.InvalidateVisual();
        }
    }

    #endregion

    #region Private Fields

    /// <summary>Cached pen, cleared when any stroke property changes.</summary>
    internal Pen? _pen;

    #endregion
}

