using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents an icon that uses an <see cref="ImageSource"/> as its content.
/// Mirrors WinUI's Microsoft.UI.Xaml.Controls.ImageIcon.
/// </summary>
public class ImageIcon : IconElement
{
    private const double DefaultIconSize = 20.0;

    /// <summary>
    /// Identifies the <see cref="Source"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(ImageSource), typeof(ImageIcon),
            new PropertyMetadata(null, OnSourceChanged));

    /// <summary>
    /// Gets or sets the <see cref="ImageSource"/> used as the icon's content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageIcon"/> class.
    /// </summary>
    public ImageIcon()
    {
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = !double.IsNaN(Width) ? Width : DefaultIconSize;
        double height = !double.IsNaN(Height) ? Height : DefaultIconSize;

        if (Source != null && Source.Width > 0 && Source.Height > 0)
        {
            var aspectRatio = Source.Width / Source.Height;
            if (!double.IsNaN(Width) && double.IsNaN(Height))
            {
                height = width / aspectRatio;
            }
            else if (double.IsNaN(Width) && !double.IsNaN(Height))
            {
                width = height * aspectRatio;
            }
        }

        return new Size(
            Math.Min(width, availableSize.Width),
            Math.Min(height, availableSize.Height));
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (Source == null || RenderSize.Width <= 0 || RenderSize.Height <= 0)
        {
            return;
        }

        IconRendering.DrawImageFit(drawingContext, Source, new Rect(RenderSize));
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ImageIcon icon)
        {
            icon.InvalidateMeasure();
            icon.InvalidateVisual();
        }
    }
}
