using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents an icon that uses a bitmap image as its content.
/// Mirrors WinUI's Microsoft.UI.Xaml.Controls.BitmapIcon.
/// </summary>
public class BitmapIcon : IconElement
{
    private const double DefaultIconSize = 20.0;
    private BitmapImage? _cachedBitmap;

    /// <summary>
    /// Identifies the <see cref="UriSource"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public static readonly DependencyProperty UriSourceProperty =
        DependencyProperty.Register(nameof(UriSource), typeof(Uri), typeof(BitmapIcon),
            new PropertyMetadata(null, OnUriSourceChanged));

    /// <summary>
    /// Identifies the <see cref="ShowAsMonochrome"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowAsMonochromeProperty =
        DependencyProperty.Register(nameof(ShowAsMonochrome), typeof(bool), typeof(BitmapIcon),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    /// <summary>
    /// Gets or sets the <see cref="Uri"/> of the bitmap to display.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public Uri? UriSource
    {
        get => (Uri?)GetValue(UriSourceProperty);
        set => SetValue(UriSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the bitmap is shown as monochrome (tinted by <see cref="IconElement.Foreground"/>).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowAsMonochrome
    {
        get => (bool)GetValue(ShowAsMonochromeProperty)!;
        set => SetValue(ShowAsMonochromeProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BitmapIcon"/> class.
    /// </summary>
    public BitmapIcon()
    {
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        double width = !double.IsNaN(Width) ? Width : DefaultIconSize;
        double height = !double.IsNaN(Height) ? Height : DefaultIconSize;

        EnsureBitmap();
        if (_cachedBitmap != null && _cachedBitmap.Width > 0 && _cachedBitmap.Height > 0)
        {
            var aspectRatio = _cachedBitmap.Width / _cachedBitmap.Height;
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
        if (UriSource == null || RenderSize.Width <= 0 || RenderSize.Height <= 0)
        {
            return;
        }

        EnsureBitmap();
        if (_cachedBitmap != null)
        {
            IconRendering.DrawImageFit(drawingContext, _cachedBitmap, new Rect(RenderSize));
        }
    }

    private void EnsureBitmap()
    {
        if (_cachedBitmap == null && UriSource != null)
        {
            try
            {
                _cachedBitmap = new BitmapImage(UriSource);
            }
            catch
            {
                _cachedBitmap = null;
            }
        }
    }

    private static void OnUriSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BitmapIcon icon)
        {
            icon._cachedBitmap = null;
            icon.InvalidateMeasure();
            icon.InvalidateVisual();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BitmapIcon icon)
        {
            icon.InvalidateVisual();
        }
    }
}
