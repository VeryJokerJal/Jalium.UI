using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents an icon that uses a glyph from the Segoe icon font family as its content.
/// Mirrors WinUI's Microsoft.UI.Xaml.Controls.SymbolIcon.
/// </summary>
public class SymbolIcon : IconElement
{
    // Segoe MDL2 Assets is available on a wider range of Windows installations.
    // Our Symbol enum uses MDL2-compatible code points, so this keeps icons visible
    // even when Segoe Fluent Icons is missing.
    private static readonly FontFamily SymbolFontFamily = new("Segoe MDL2 Assets");

    /// <summary>
    /// Identifies the Symbol dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SymbolProperty =
        DependencyProperty.Register(nameof(Symbol), typeof(Symbol), typeof(SymbolIcon),
            new PropertyMetadata(Symbol.Cancel, OnSymbolChanged));

    /// <summary>
    /// Gets or sets the glyph code used as the icon content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Symbol Symbol
    {
        get => (Symbol)GetValue(SymbolProperty)!;
        set => SetValue(SymbolProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the SymbolIcon class.
    /// </summary>
    public SymbolIcon()
    {
    }

    /// <summary>
    /// Initializes a new instance of the SymbolIcon class using the specified symbol.
    /// </summary>
    public SymbolIcon(Symbol symbol)
    {
        Symbol = symbol;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // Default icon size: 20x20 (consistent with WinUI)
        double size = 20;
        return new Size(
            Math.Min(size, availableSize.Width),
            Math.Min(size, availableSize.Height));
    }

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc) return;

        var glyph = char.ConvertFromUtf32((int)Symbol);
        var foreground = GetEffectiveForeground();
        double fontSize = Math.Min(RenderSize.Width, RenderSize.Height);
        if (fontSize <= 0) fontSize = 16;

        var ft = new FormattedText(glyph, SymbolFontFamily, fontSize)
        {
            Foreground = foreground,
            MaxTextWidth = RenderSize.Width,
            MaxTextHeight = RenderSize.Height
        };

        // Measure with DirectWrite to get accurate glyph dimensions
        TextMeasurement.MeasureText(ft);

        // Center the glyph within the render area
        double x = (RenderSize.Width - ft.Width) / 2;
        double y = (RenderSize.Height - ft.Height) / 2;
        dc.DrawText(ft, new Point(x, y));
    }

    private static void OnSymbolChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SymbolIcon icon)
            icon.InvalidateVisual();
    }
}
