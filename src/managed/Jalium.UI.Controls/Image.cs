using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a control that displays an image.
/// Uses ControlTemplate for visual customization (border, corner radius, etc.)
/// and an internal ImageHost element for actual bitmap rendering.
/// </summary>
public class Image : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.ImageAutomationPeer(this);
    }

    private ImageHost? _imageHost;
    private Border? _container;

    #region Dependency Properties

    /// <summary>
    /// Identifies the Source dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(nameof(Source), typeof(ImageSource), typeof(Image),
            new PropertyMetadata(null, OnSourceChanged));

    /// <summary>
    /// Identifies the Stretch dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(nameof(Stretch), typeof(Stretch), typeof(Image),
            new PropertyMetadata(Stretch.Uniform, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the StretchDirection dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty StretchDirectionProperty =
        DependencyProperty.Register(nameof(StretchDirection), typeof(StretchDirection), typeof(Image),
            new PropertyMetadata(StretchDirection.Both, OnLayoutPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the image source.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// Gets or sets how the image is stretched to fill its allocated space.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty)!;
        set => SetValue(StretchProperty, value);
    }

    /// <summary>
    /// Gets or sets the direction to stretch the image.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public StretchDirection StretchDirection
    {
        get => (StretchDirection)GetValue(StretchDirectionProperty)!;
        set => SetValue(StretchDirectionProperty, value);
    }

    #endregion

    public Image()
    {
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _container = GetTemplateChild("PART_Container") as Border;

        if (_container != null)
        {
            _imageHost = new ImageHost { Owner = this };
            _container.Child = _imageHost;
        }
    }

    #region Property Changed Callbacks

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Image image)
        {
            image._imageHost?.InvalidateMeasure();
            image._imageHost?.InvalidateVisual();
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Image image)
        {
            image._imageHost?.InvalidateMeasure();
        }
    }

    #endregion
}

/// <summary>
/// Internal element that handles actual image bitmap rendering inside the Image control's template.
/// </summary>
internal sealed class ImageHost : FrameworkElement
{
    internal Image? Owner { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var source = Owner?.Source;
        if (source == null || source.Width <= 0 || source.Height <= 0)
            return Size.Empty;

        var imageSize = new Size(source.Width, source.Height);
        return CalculateStretchSize(imageSize, availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        var source = Owner?.Source;
        if (source == null || source.Width <= 0 || source.Height <= 0)
            return;

        var imageSize = new Size(source.Width, source.Height);
        var stretchedSize = CalculateStretchSize(imageSize, RenderSize);

        // Center the image in the render area
        var x = (RenderSize.Width - stretchedSize.Width) / 2;
        var y = (RenderSize.Height - stretchedSize.Height) / 2;

        dc.DrawImage(source, new Rect(x, y, stretchedSize.Width, stretchedSize.Height));
    }

    private Size CalculateStretchSize(Size imageSize, Size availableSize)
    {
        if (imageSize.Width <= 0 || imageSize.Height <= 0)
            return Size.Empty;

        var stretch = Owner?.Stretch ?? Stretch.Uniform;
        var stretchDirection = Owner?.StretchDirection ?? StretchDirection.Both;

        var width = imageSize.Width;
        var height = imageSize.Height;
        var maxWidth = availableSize.Width;
        var maxHeight = availableSize.Height;

        // Handle explicit width/height on owner
        if (Owner != null)
        {
            if (!double.IsNaN(Owner.Width) && Owner.Width > 0)
                width = Owner.Width;
            if (!double.IsNaN(Owner.Height) && Owner.Height > 0)
                height = Owner.Height;
        }

        switch (stretch)
        {
            case Stretch.None:
                break;

            case Stretch.Fill:
                if (!double.IsInfinity(maxWidth))
                    width = maxWidth;
                if (!double.IsInfinity(maxHeight))
                    height = maxHeight;
                break;

            case Stretch.Uniform:
            {
                var scaleX = double.IsInfinity(maxWidth) ? double.MaxValue : maxWidth / imageSize.Width;
                var scaleY = double.IsInfinity(maxHeight) ? double.MaxValue : maxHeight / imageSize.Height;
                var scale = Math.Min(scaleX, scaleY);
                scale = ApplyStretchDirection(scale, stretchDirection);
                width = imageSize.Width * scale;
                height = imageSize.Height * scale;
                break;
            }

            case Stretch.UniformToFill:
            {
                if (!double.IsInfinity(maxWidth) && !double.IsInfinity(maxHeight))
                {
                    var scaleX = maxWidth / imageSize.Width;
                    var scaleY = maxHeight / imageSize.Height;
                    var scale = Math.Max(scaleX, scaleY);
                    scale = ApplyStretchDirection(scale, stretchDirection);
                    width = imageSize.Width * scale;
                    height = imageSize.Height * scale;
                }
                break;
            }
        }

        return new Size(width, height);
    }

    private static double ApplyStretchDirection(double scale, StretchDirection direction)
    {
        return direction switch
        {
            StretchDirection.UpOnly => Math.Max(1.0, scale),
            StretchDirection.DownOnly => Math.Min(1.0, scale),
            _ => scale
        };
    }
}
