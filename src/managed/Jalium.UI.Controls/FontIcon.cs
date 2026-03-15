using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents an icon that uses a glyph from a specified font.
/// Mirrors WinUI's Microsoft.UI.Xaml.Controls.FontIcon.
/// </summary>
public class FontIcon : IconElement
{
    // Prefer MDL2 as a compatibility baseline; callers can still set FontFamily explicitly.
    private static readonly FontFamily DefaultFontFamily = new("Segoe MDL2 Assets");

    #region Dependency Properties

    /// <summary>
    /// Identifies the Glyph dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(FontIcon),
            new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the FontFamily dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(FontIcon),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the FontSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(FontIcon),
            new PropertyMetadata(20.0, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

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
    /// Gets or sets the font used to display the icon glyph.
    /// Defaults to Segoe MDL2 Assets if not specified.
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

    #endregion

    /// <summary>
    /// Initializes a new instance of the FontIcon class.
    /// </summary>
    public FontIcon()
    {
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        double size = FontSize > 0 ? FontSize : 20;
        return new Size(
            Math.Min(size, availableSize.Width),
            Math.Min(size, availableSize.Height));
    }

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc) return;
        if (string.IsNullOrEmpty(Glyph)) return;

        var foreground = GetEffectiveForeground();
        var fontFamily = FontFamily ?? DefaultFontFamily;
        double fontSize = FontSize > 0 ? FontSize : 16;

        var ft = new FormattedText(Glyph, fontFamily, fontSize)
        {
            Foreground = foreground,
            MaxTextWidth = RenderSize.Width,
            MaxTextHeight = RenderSize.Height
        };

        // Measure with DirectWrite to get accurate glyph dimensions
        TextMeasurement.MeasureText(ft);

        double x = (RenderSize.Width - ft.Width) / 2;
        double y = (RenderSize.Height - ft.Height) / 2;
        dc.DrawText(ft, new Point(x, y));
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FontIcon icon)
            icon.InvalidateVisual();
    }
}
