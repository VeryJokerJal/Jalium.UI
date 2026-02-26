namespace Jalium.UI.Media;

/// <summary>
/// Defines a text effect that can be applied to text objects.
/// Enables per-character transforms, foreground changes, and other visual effects.
/// </summary>
public sealed class TextEffect
{
    /// <summary>
    /// Gets or sets the Transform to apply to the text effect.
    /// </summary>
    public Transform? Transform { get; set; }

    /// <summary>
    /// Gets or sets the Brush to apply to the text content.
    /// </summary>
    public Brush? Foreground { get; set; }

    /// <summary>
    /// Gets or sets the starting character position of the text effect.
    /// </summary>
    public int PositionStart { get; set; }

    /// <summary>
    /// Gets or sets the number of characters in the range.
    /// </summary>
    public int PositionCount { get; set; }

    /// <summary>
    /// Creates a copy of this TextEffect.
    /// </summary>
    public TextEffect Clone()
    {
        return new TextEffect
        {
            Transform = Transform,
            Foreground = Foreground,
            PositionStart = PositionStart,
            PositionCount = PositionCount
        };
    }
}

/// <summary>
/// Represents a collection of TextEffect objects.
/// </summary>
public sealed class TextEffectCollection : List<TextEffect>
{
    public TextEffectCollection() { }
    public TextEffectCollection(IEnumerable<TextEffect> collection) : base(collection) { }
}

/// <summary>
/// Provides attached properties and methods for performing OpenType typography on text elements.
/// </summary>
public static class Typography
{
    // Standard Ligatures
    public static readonly DependencyProperty StandardLigaturesProperty =
        DependencyProperty.RegisterAttached("StandardLigatures", typeof(bool), typeof(Typography),
            new PropertyMetadata(true));

    public static bool GetStandardLigatures(DependencyObject element) => (bool)element.GetValue(StandardLigaturesProperty);
    public static void SetStandardLigatures(DependencyObject element, bool value) => element.SetValue(StandardLigaturesProperty, value);

    // Contextual Ligatures
    public static readonly DependencyProperty ContextualLigaturesProperty =
        DependencyProperty.RegisterAttached("ContextualLigatures", typeof(bool), typeof(Typography),
            new PropertyMetadata(true));

    public static bool GetContextualLigatures(DependencyObject element) => (bool)element.GetValue(ContextualLigaturesProperty);
    public static void SetContextualLigatures(DependencyObject element, bool value) => element.SetValue(ContextualLigaturesProperty, value);

    // Discretionary Ligatures
    public static readonly DependencyProperty DiscretionaryLigaturesProperty =
        DependencyProperty.RegisterAttached("DiscretionaryLigatures", typeof(bool), typeof(Typography),
            new PropertyMetadata(false));

    public static bool GetDiscretionaryLigatures(DependencyObject element) => (bool)element.GetValue(DiscretionaryLigaturesProperty);
    public static void SetDiscretionaryLigatures(DependencyObject element, bool value) => element.SetValue(DiscretionaryLigaturesProperty, value);

    // Historical Ligatures
    public static readonly DependencyProperty HistoricalLigaturesProperty =
        DependencyProperty.RegisterAttached("HistoricalLigatures", typeof(bool), typeof(Typography),
            new PropertyMetadata(false));

    public static bool GetHistoricalLigatures(DependencyObject element) => (bool)element.GetValue(HistoricalLigaturesProperty);
    public static void SetHistoricalLigatures(DependencyObject element, bool value) => element.SetValue(HistoricalLigaturesProperty, value);

    // Kerning
    public static readonly DependencyProperty KerningProperty =
        DependencyProperty.RegisterAttached("Kerning", typeof(bool), typeof(Typography),
            new PropertyMetadata(true));

    public static bool GetKerning(DependencyObject element) => (bool)element.GetValue(KerningProperty);
    public static void SetKerning(DependencyObject element, bool value) => element.SetValue(KerningProperty, value);

    // Capital Spacing
    public static readonly DependencyProperty CapitalSpacingProperty =
        DependencyProperty.RegisterAttached("CapitalSpacing", typeof(bool), typeof(Typography),
            new PropertyMetadata(false));

    public static bool GetCapitalSpacing(DependencyObject element) => (bool)element.GetValue(CapitalSpacingProperty);
    public static void SetCapitalSpacing(DependencyObject element, bool value) => element.SetValue(CapitalSpacingProperty, value);

    // Numeral Style
    public static readonly DependencyProperty NumeralStyleProperty =
        DependencyProperty.RegisterAttached("NumeralStyle", typeof(FontNumeralStyle), typeof(Typography),
            new PropertyMetadata(FontNumeralStyle.Normal));

    public static FontNumeralStyle GetNumeralStyle(DependencyObject element) => (FontNumeralStyle)element.GetValue(NumeralStyleProperty);
    public static void SetNumeralStyle(DependencyObject element, FontNumeralStyle value) => element.SetValue(NumeralStyleProperty, value);

    // Numeral Alignment
    public static readonly DependencyProperty NumeralAlignmentProperty =
        DependencyProperty.RegisterAttached("NumeralAlignment", typeof(FontNumeralAlignment), typeof(Typography),
            new PropertyMetadata(FontNumeralAlignment.Normal));

    public static FontNumeralAlignment GetNumeralAlignment(DependencyObject element) => (FontNumeralAlignment)element.GetValue(NumeralAlignmentProperty);
    public static void SetNumeralAlignment(DependencyObject element, FontNumeralAlignment value) => element.SetValue(NumeralAlignmentProperty, value);

    // Variants
    public static readonly DependencyProperty VariantsProperty =
        DependencyProperty.RegisterAttached("Variants", typeof(FontVariants), typeof(Typography),
            new PropertyMetadata(FontVariants.Normal));

    public static FontVariants GetVariants(DependencyObject element) => (FontVariants)element.GetValue(VariantsProperty);
    public static void SetVariants(DependencyObject element, FontVariants value) => element.SetValue(VariantsProperty, value);

    // Capitals
    public static readonly DependencyProperty CapitalsProperty =
        DependencyProperty.RegisterAttached("Capitals", typeof(FontCapitals), typeof(Typography),
            new PropertyMetadata(FontCapitals.Normal));

    public static FontCapitals GetCapitals(DependencyObject element) => (FontCapitals)element.GetValue(CapitalsProperty);
    public static void SetCapitals(DependencyObject element, FontCapitals value) => element.SetValue(CapitalsProperty, value);

    // Slashed Zero
    public static readonly DependencyProperty SlashedZeroProperty =
        DependencyProperty.RegisterAttached("SlashedZero", typeof(bool), typeof(Typography),
            new PropertyMetadata(false));

    public static bool GetSlashedZero(DependencyObject element) => (bool)element.GetValue(SlashedZeroProperty);
    public static void SetSlashedZero(DependencyObject element, bool value) => element.SetValue(SlashedZeroProperty, value);
}

/// <summary>
/// Specifies the numeral style for a font.
/// </summary>
public enum FontNumeralStyle { Normal, Lining, OldStyle }

/// <summary>
/// Specifies the numeral alignment for a font.
/// </summary>
public enum FontNumeralAlignment { Normal, Proportional, Tabular }

/// <summary>
/// Specifies the font variant forms.
/// </summary>
public enum FontVariants { Normal, Superscript, Subscript, Ordinal, Inferior, Ruby }

/// <summary>
/// Specifies the capital letter style.
/// </summary>
public enum FontCapitals { Normal, AllSmallCaps, SmallCaps, AllPetiteCaps, PetiteCaps, Unicase, Titling }
