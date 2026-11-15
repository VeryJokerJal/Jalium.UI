using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents the base class for an icon source definition.
/// Mirrors WinUI's Microsoft.UI.Xaml.Controls.IconSource.
/// </summary>
public abstract class IconSource : DependencyObject
{
    /// <summary>
    /// Identifies the <see cref="Foreground"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(IconSource),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the foreground brush for the icon.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Occurs when a property of this icon source changes.
    /// </summary>
    internal event Action? Changed;

    /// <inheritdoc />
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        Changed?.Invoke();
    }

    /// <summary>
    /// Creates a concrete <see cref="IconElement"/> from this source definition.
    /// </summary>
    public abstract IconElement CreateIconElement();
}

/// <summary>
/// Represents an icon source that uses a font glyph.
/// </summary>
public class FontIconSource : IconSource
{
    /// <summary>
    /// Identifies the <see cref="Glyph"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(FontIconSource),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the <see cref="FontFamily"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(FontIconSource),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="FontSize"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(FontIconSource),
            new PropertyMetadata(20.0));

    /// <summary>
    /// Gets or sets the character code that identifies the icon glyph.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string Glyph
    {
        get => (string?)GetValue(GlyphProperty) ?? string.Empty;
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>
    /// Gets or sets the font family used to display the icon glyph.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public FontFamily? FontFamily
    {
        get => (FontFamily?)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>
    /// Gets or sets the size of the icon glyph.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty)!;
        set => SetValue(FontSizeProperty, value);
    }

    /// <inheritdoc />
    public override IconElement CreateIconElement()
    {
        return new FontIcon
        {
            Glyph = Glyph,
            FontFamily = FontFamily,
            FontSize = FontSize,
            Foreground = Foreground
        };
    }
}

/// <summary>
/// Represents an icon source that uses a vector path geometry.
/// </summary>
public class PathIconSource : IconSource
{
    /// <summary>
    /// Identifies the <see cref="Data"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(Geometry), typeof(PathIconSource),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the geometry that defines the path icon.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <inheritdoc />
    public override IconElement CreateIconElement()
    {
        return new PathIcon
        {
            Data = Data,
            Foreground = Foreground
        };
    }
}

/// <summary>
/// Represents an icon source that uses a symbol from the Segoe font family.
/// </summary>
public class SymbolIconSource : IconSource
{
    /// <summary>
    /// Identifies the <see cref="Symbol"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SymbolProperty =
        DependencyProperty.Register(nameof(Symbol), typeof(Symbol), typeof(SymbolIconSource),
            new PropertyMetadata(Symbol.Cancel));

    /// <summary>
    /// Gets or sets the symbol.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Symbol Symbol
    {
        get => (Symbol)GetValue(SymbolProperty)!;
        set => SetValue(SymbolProperty, value);
    }

    /// <inheritdoc />
    public override IconElement CreateIconElement()
    {
        return new SymbolIcon(Symbol)
        {
            Foreground = Foreground
        };
    }
}

/// <summary>
/// Represents an icon source that uses a bitmap image.
/// </summary>
public class BitmapIconSource : IconSource
{
    /// <summary>
    /// Identifies the <see cref="UriSource"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public static readonly DependencyProperty UriSourceProperty =
        DependencyProperty.Register(nameof(UriSource), typeof(Uri), typeof(BitmapIconSource),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the <see cref="ShowAsMonochrome"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ShowAsMonochromeProperty =
        DependencyProperty.Register(nameof(ShowAsMonochrome), typeof(bool), typeof(BitmapIconSource),
            new PropertyMetadata(true));

    /// <summary>
    /// Gets or sets the URI of the bitmap.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public Uri? UriSource
    {
        get => (Uri?)GetValue(UriSourceProperty);
        set => SetValue(UriSourceProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the bitmap is displayed as monochrome.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool ShowAsMonochrome
    {
        get => (bool)GetValue(ShowAsMonochromeProperty)!;
        set => SetValue(ShowAsMonochromeProperty, value);
    }

    /// <inheritdoc />
    public override IconElement CreateIconElement()
    {
        return new BitmapIcon
        {
            UriSource = UriSource,
            ShowAsMonochrome = ShowAsMonochrome,
            Foreground = Foreground
        };
    }
}

/// <summary>
/// Represents an icon source that uses an <see cref="ImageSource"/>.
/// </summary>
public class ImageIconSource : IconSource
{
    /// <summary>
    /// Identifies the <see cref="ImageSource"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public static readonly DependencyProperty ImageSourceProperty =
        DependencyProperty.Register(nameof(ImageSource), typeof(ImageSource), typeof(ImageIconSource),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the image source.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public ImageSource? ImageSource
    {
        get => (ImageSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    /// <inheritdoc />
    public override IconElement CreateIconElement()
    {
        return new ImageIcon
        {
            Source = ImageSource,
            Foreground = Foreground
        };
    }
}

/// <summary>
/// Represents an icon element that uses an <see cref="IconSource"/> as its content.
/// Mirrors WinUI's Microsoft.UI.Xaml.Controls.IconSourceElement.
/// </summary>
public class IconSourceElement : IconElement
{
    /// <summary>
    /// Identifies the <see cref="IconSource"/> dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public static readonly DependencyProperty IconSourceProperty =
        DependencyProperty.Register(nameof(IconSource), typeof(IconSource), typeof(IconSourceElement),
            new PropertyMetadata(null, OnIconSourceChanged));

    /// <summary>
    /// Gets or sets the icon source.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Data)]
    public IconSource? IconSource
    {
        get => (IconSource?)GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    private IconElement? _realizedElement;

    /// <summary>
    /// Gets the internally realized <see cref="IconElement"/>.
    /// </summary>
    public IconElement? RealizedElement => _realizedElement;

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_realizedElement != null)
        {
            _realizedElement.Measure(availableSize);
            return _realizedElement.DesiredSize;
        }

        return base.MeasureOverride(availableSize);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        _realizedElement?.Arrange(new Rect(finalSize));
        return base.ArrangeOverride(finalSize);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_realizedElement != null)
        {
            IconRendering.DrawIcon(
                drawingContext,
                _realizedElement,
                new Rect(RenderSize),
                Foreground ?? GetEffectiveForeground());
        }
    }

    private void UpdateRealizedElement()
    {
        _realizedElement = IconSource?.CreateIconElement();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnSourceChanged()
    {
        UpdateRealizedElement();
    }

    private static void OnIconSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is IconSourceElement element)
        {
            if (e.OldValue is IconSource oldSource)
            {
                oldSource.Changed -= element.OnSourceChanged;
            }
            if (e.NewValue is IconSource newSource)
            {
                newSource.Changed += element.OnSourceChanged;
            }
            element.UpdateRealizedElement();
        }
    }
}
