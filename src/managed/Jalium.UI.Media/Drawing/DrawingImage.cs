namespace Jalium.UI.Media;

/// <summary>
/// An ImageSource that uses a Drawing for its content.
/// </summary>
public sealed class DrawingImage : ImageSource
{
    public static readonly DependencyProperty DrawingProperty =
        DependencyProperty.Register(nameof(Drawing), typeof(Drawing), typeof(DrawingImage), new PropertyMetadata(null));

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingImage"/> class.
    /// </summary>
    public DrawingImage()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DrawingImage"/> class
    /// with the specified Drawing.
    /// </summary>
    /// <param name="drawing">The Drawing to use as the image content.</param>
    public DrawingImage(Drawing? drawing)
    {
        Drawing = drawing;
    }

    /// <summary>
    /// Gets or sets the Drawing that provides the content for this ImageSource.
    /// </summary>
    public Drawing? Drawing
    {
        get => (Drawing?)GetValue(DrawingProperty);
        set => SetValue(DrawingProperty, value);
    }

    public override ImageMetadata? Metadata => null;

    /// <summary>
    /// Gets the width of the DrawingImage.
    /// </summary>
    public override double Width => Drawing?.Bounds.Width ?? 0;

    /// <summary>
    /// Gets the height of the DrawingImage.
    /// </summary>
    public override double Height => Drawing?.Bounds.Height ?? 0;

    /// <summary>
    /// Gets the native handle. DrawingImage does not have a native handle.
    /// </summary>
    public override nint NativeHandle => 0;

    public new DrawingImage Clone() => (DrawingImage)base.Clone();
    public new DrawingImage CloneCurrentValue() => (DrawingImage)base.CloneCurrentValue();
    protected override Freezable CreateInstanceCore() => new DrawingImage();

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (ReferenceEquals(e.Property, DrawingProperty))
        {
            OnFreezablePropertyChanged(e.OldValue as DependencyObject, e.NewValue as DependencyObject, DrawingProperty);
            WritePostscript();
        }
    }
}
