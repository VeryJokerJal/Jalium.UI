namespace Jalium.UI.Media;

/// <summary>
/// Draws an image within a region defined by a Rect.
/// </summary>
public sealed class ImageDrawing : Drawing
{
    public static readonly DependencyProperty ImageSourceProperty =
        DependencyProperty.Register(nameof(ImageSource), typeof(ImageSource), typeof(ImageDrawing), new PropertyMetadata(null));
    public static readonly DependencyProperty RectProperty =
        DependencyProperty.Register(nameof(Rect), typeof(Rect), typeof(ImageDrawing), new PropertyMetadata(Rect.Empty));
    private static readonly DependencyProperty ScalingModeProperty =
        DependencyProperty.Register(nameof(ScalingMode), typeof(BitmapScalingMode), typeof(ImageDrawing), new PropertyMetadata(BitmapScalingMode.Unspecified));

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageDrawing"/> class.
    /// </summary>
    public ImageDrawing()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageDrawing"/> class
    /// with the specified image source and destination rectangle.
    /// </summary>
    /// <param name="imageSource">The image to draw.</param>
    /// <param name="rect">The region in which to draw the image.</param>
    public ImageDrawing(ImageSource? imageSource, Rect rect)
    {
        ImageSource = imageSource;
        Rect = rect;
    }

    /// <summary>
    /// Gets or sets the source of the image to draw.
    /// </summary>
    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets the region in which to draw the image.
    /// </summary>
    public Rect Rect
    {
        get => (Rect)(GetValue(RectProperty) ?? Rect.Empty);
        set => SetValue(RectProperty, value);
    }

    /// <summary>
    /// Gets or sets the algorithm used to scale the bitmap when its source pixel size
    /// differs from <see cref="Rect"/>.
    /// </summary>
    public BitmapScalingMode ScalingMode
    {
        get => (BitmapScalingMode)(GetValue(ScalingModeProperty) ?? BitmapScalingMode.Unspecified);
        set => SetValue(ScalingModeProperty, value);
    }

    public new ImageDrawing Clone() => (ImageDrawing)base.Clone();
    public new ImageDrawing CloneCurrentValue() => (ImageDrawing)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new ImageDrawing();

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, ImageSourceProperty))
        {
            OnFreezablePropertyChanged(e.OldValue as DependencyObject, e.NewValue as DependencyObject, ImageSourceProperty);
        }

        if (ReferenceEquals(e.Property, ImageSourceProperty)
            || ReferenceEquals(e.Property, RectProperty)
            || ReferenceEquals(e.Property, ScalingModeProperty))
        {
            WritePostscript();
        }
    }

    /// <inheritdoc />
    public override Rect Bounds => Rect;

    /// <inheritdoc />
    public override void RenderTo(DrawingContext context)
    {
        if (ImageSource != null && !Rect.IsEmpty)
        {
            context.DrawImage(ImageSource, Rect, ScalingMode);
        }
    }
}
