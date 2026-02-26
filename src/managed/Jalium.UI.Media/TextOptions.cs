namespace Jalium.UI.Media;

/// <summary>
/// Specifies the formatting method for text.
/// </summary>
public enum TextFormattingMode
{
    /// <summary>
    /// Text is displayed with resolution-independent glyph ideal metrics.
    /// </summary>
    Ideal = 0,

    /// <summary>
    /// Text is displayed with metrics that produce glyphs snapped to the pixel grid on screen.
    /// </summary>
    Display = 1
}

/// <summary>
/// Specifies the rendering mode for text.
/// </summary>
public enum TextRenderingMode
{
    /// <summary>
    /// Text is rendered with the most appropriate rendering algorithm automatically.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Text is rendered with bilevel anti-aliasing.
    /// </summary>
    Aliased = 1,

    /// <summary>
    /// Text is rendered with grayscale anti-aliasing.
    /// </summary>
    Grayscale = 2,

    /// <summary>
    /// Text is rendered with ClearType anti-aliasing.
    /// </summary>
    ClearType = 3
}

/// <summary>
/// Specifies whether text hinting is on or off.
/// </summary>
public enum TextHintingMode
{
    /// <summary>
    /// The text rendering engine determines the best hinting mode automatically.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Hinting is performed on the text using fixed-point hinting values.
    /// </summary>
    Fixed = 1,

    /// <summary>
    /// Hinting is performed using animated values.
    /// </summary>
    Animated = 2
}

/// <summary>
/// Provides a set of attached properties that affects text rendering in an element.
/// </summary>
public static class TextOptions
{
    /// <summary>
    /// Identifies the TextFormattingMode attached property.
    /// </summary>
    public static readonly DependencyProperty TextFormattingModeProperty =
        DependencyProperty.RegisterAttached("TextFormattingMode", typeof(TextFormattingMode), typeof(TextOptions),
            new PropertyMetadata(TextFormattingMode.Ideal));

    /// <summary>
    /// Identifies the TextRenderingMode attached property.
    /// </summary>
    public static readonly DependencyProperty TextRenderingModeProperty =
        DependencyProperty.RegisterAttached("TextRenderingMode", typeof(TextRenderingMode), typeof(TextOptions),
            new PropertyMetadata(TextRenderingMode.Auto));

    /// <summary>
    /// Identifies the TextHintingMode attached property.
    /// </summary>
    public static readonly DependencyProperty TextHintingModeProperty =
        DependencyProperty.RegisterAttached("TextHintingMode", typeof(TextHintingMode), typeof(TextOptions),
            new PropertyMetadata(TextHintingMode.Auto));

    /// <summary>
    /// Gets the TextFormattingMode for the specified element.
    /// </summary>
    public static TextFormattingMode GetTextFormattingMode(DependencyObject element)
    {
        return (TextFormattingMode)(element.GetValue(TextFormattingModeProperty) ?? TextFormattingMode.Ideal);
    }

    /// <summary>
    /// Sets the TextFormattingMode for the specified element.
    /// </summary>
    public static void SetTextFormattingMode(DependencyObject element, TextFormattingMode value)
    {
        element.SetValue(TextFormattingModeProperty, value);
    }

    /// <summary>
    /// Gets the TextRenderingMode for the specified element.
    /// </summary>
    public static TextRenderingMode GetTextRenderingMode(DependencyObject element)
    {
        return (TextRenderingMode)(element.GetValue(TextRenderingModeProperty) ?? TextRenderingMode.Auto);
    }

    /// <summary>
    /// Sets the TextRenderingMode for the specified element.
    /// </summary>
    public static void SetTextRenderingMode(DependencyObject element, TextRenderingMode value)
    {
        element.SetValue(TextRenderingModeProperty, value);
    }

    /// <summary>
    /// Gets the TextHintingMode for the specified element.
    /// </summary>
    public static TextHintingMode GetTextHintingMode(DependencyObject element)
    {
        return (TextHintingMode)(element.GetValue(TextHintingModeProperty) ?? TextHintingMode.Auto);
    }

    /// <summary>
    /// Sets the TextHintingMode for the specified element.
    /// </summary>
    public static void SetTextHintingMode(DependencyObject element, TextHintingMode value)
    {
        element.SetValue(TextHintingModeProperty, value);
    }
}
