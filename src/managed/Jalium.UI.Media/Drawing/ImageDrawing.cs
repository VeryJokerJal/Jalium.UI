namespace Jalium.UI.Media;

/// <summary>
/// Draws an image within a region defined by a Rect.
/// </summary>
public sealed class ImageDrawing : Drawing
{
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
    public ImageSource? ImageSource { get; set; }

    /// <summary>
    /// Gets or sets the region in which to draw the image.
    /// </summary>
    public Rect Rect { get; set; }

    /// <inheritdoc />
    public override Rect Bounds => Rect;

    /// <inheritdoc />
    public override void RenderTo(DrawingContext context)
    {
        if (ImageSource != null && !Rect.IsEmpty)
        {
            context.DrawImage(ImageSource, Rect);
        }
    }
}
