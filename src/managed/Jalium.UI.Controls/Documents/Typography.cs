namespace Jalium.UI.Documents;

/// <summary>
/// Provides access to the OpenType typography settings stored on a dependency object.
/// </summary>
public sealed class Typography
{
    private const FrameworkPropertyMetadataOptions TypographyMetadataOptions =
        FrameworkPropertyMetadataOptions.AffectsMeasure |
        FrameworkPropertyMetadataOptions.AffectsRender |
        FrameworkPropertyMetadataOptions.Inherits;

    private readonly DependencyObject _owner;

    internal Typography(DependencyObject owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    public static readonly DependencyProperty StandardLigaturesProperty =
        RegisterTypographyProperty(nameof(StandardLigatures), true);

    public static readonly DependencyProperty ContextualLigaturesProperty =
        RegisterTypographyProperty(nameof(ContextualLigatures), true);

    public static readonly DependencyProperty DiscretionaryLigaturesProperty =
        RegisterTypographyProperty(nameof(DiscretionaryLigatures), false);

    public static readonly DependencyProperty HistoricalLigaturesProperty =
        RegisterTypographyProperty(nameof(HistoricalLigatures), false);

    public static readonly DependencyProperty AnnotationAlternatesProperty =
        RegisterTypographyProperty(nameof(AnnotationAlternates), 0);

    public static readonly DependencyProperty ContextualAlternatesProperty =
        RegisterTypographyProperty(nameof(ContextualAlternates), true);

    public static readonly DependencyProperty HistoricalFormsProperty =
        RegisterTypographyProperty(nameof(HistoricalForms), false);

    public static readonly DependencyProperty KerningProperty =
        RegisterTypographyProperty(nameof(Kerning), true);

    public static readonly DependencyProperty CapitalSpacingProperty =
        RegisterTypographyProperty(nameof(CapitalSpacing), false);

    public static readonly DependencyProperty CaseSensitiveFormsProperty =
        RegisterTypographyProperty(nameof(CaseSensitiveForms), false);

    public static readonly DependencyProperty StylisticSet1Property =
        RegisterTypographyProperty(nameof(StylisticSet1), false);

    public static readonly DependencyProperty StylisticSet2Property =
        RegisterTypographyProperty(nameof(StylisticSet2), false);

    public static readonly DependencyProperty StylisticSet3Property =
        RegisterTypographyProperty(nameof(StylisticSet3), false);

    public static readonly DependencyProperty StylisticSet4Property =
        RegisterTypographyProperty(nameof(StylisticSet4), false);

    public static readonly DependencyProperty StylisticSet5Property =
        RegisterTypographyProperty(nameof(StylisticSet5), false);

    public static readonly DependencyProperty StylisticSet6Property =
        RegisterTypographyProperty(nameof(StylisticSet6), false);

    public static readonly DependencyProperty StylisticSet7Property =
        RegisterTypographyProperty(nameof(StylisticSet7), false);

    public static readonly DependencyProperty StylisticSet8Property =
        RegisterTypographyProperty(nameof(StylisticSet8), false);

    public static readonly DependencyProperty StylisticSet9Property =
        RegisterTypographyProperty(nameof(StylisticSet9), false);

    public static readonly DependencyProperty StylisticSet10Property =
        RegisterTypographyProperty(nameof(StylisticSet10), false);

    public static readonly DependencyProperty StylisticSet11Property =
        RegisterTypographyProperty(nameof(StylisticSet11), false);

    public static readonly DependencyProperty StylisticSet12Property =
        RegisterTypographyProperty(nameof(StylisticSet12), false);

    public static readonly DependencyProperty StylisticSet13Property =
        RegisterTypographyProperty(nameof(StylisticSet13), false);

    public static readonly DependencyProperty StylisticSet14Property =
        RegisterTypographyProperty(nameof(StylisticSet14), false);

    public static readonly DependencyProperty StylisticSet15Property =
        RegisterTypographyProperty(nameof(StylisticSet15), false);

    public static readonly DependencyProperty StylisticSet16Property =
        RegisterTypographyProperty(nameof(StylisticSet16), false);

    public static readonly DependencyProperty StylisticSet17Property =
        RegisterTypographyProperty(nameof(StylisticSet17), false);

    public static readonly DependencyProperty StylisticSet18Property =
        RegisterTypographyProperty(nameof(StylisticSet18), false);

    public static readonly DependencyProperty StylisticSet19Property =
        RegisterTypographyProperty(nameof(StylisticSet19), false);

    public static readonly DependencyProperty StylisticSet20Property =
        RegisterTypographyProperty(nameof(StylisticSet20), false);

    public static readonly DependencyProperty FractionProperty =
        RegisterTypographyProperty(nameof(Fraction), FontFraction.Normal);

    public static readonly DependencyProperty SlashedZeroProperty =
        RegisterTypographyProperty(nameof(SlashedZero), false);

    public static readonly DependencyProperty MathematicalGreekProperty =
        RegisterTypographyProperty(nameof(MathematicalGreek), false);

    public static readonly DependencyProperty EastAsianExpertFormsProperty =
        RegisterTypographyProperty(nameof(EastAsianExpertForms), false);

    public static readonly DependencyProperty VariantsProperty =
        RegisterTypographyProperty(nameof(Variants), FontVariants.Normal);

    public static readonly DependencyProperty CapitalsProperty =
        RegisterTypographyProperty(nameof(Capitals), FontCapitals.Normal);

    public static readonly DependencyProperty NumeralStyleProperty =
        RegisterTypographyProperty(nameof(NumeralStyle), FontNumeralStyle.Normal);

    public static readonly DependencyProperty NumeralAlignmentProperty =
        RegisterTypographyProperty(nameof(NumeralAlignment), FontNumeralAlignment.Normal);

    public static readonly DependencyProperty EastAsianWidthsProperty =
        RegisterTypographyProperty(nameof(EastAsianWidths), FontEastAsianWidths.Normal);

    public static readonly DependencyProperty EastAsianLanguageProperty =
        RegisterTypographyProperty(nameof(EastAsianLanguage), FontEastAsianLanguage.Normal);

    public static readonly DependencyProperty StandardSwashesProperty =
        RegisterTypographyProperty(nameof(StandardSwashes), 0);

    public static readonly DependencyProperty ContextualSwashesProperty =
        RegisterTypographyProperty(nameof(ContextualSwashes), 0);

    public static readonly DependencyProperty StylisticAlternatesProperty =
        RegisterTypographyProperty(nameof(StylisticAlternates), 0);

    public bool StandardLigatures
    {
        get => GetValue<bool>(_owner, StandardLigaturesProperty);
        set => SetValue(_owner, StandardLigaturesProperty, value);
    }

    public bool ContextualLigatures
    {
        get => GetValue<bool>(_owner, ContextualLigaturesProperty);
        set => SetValue(_owner, ContextualLigaturesProperty, value);
    }

    public bool DiscretionaryLigatures
    {
        get => GetValue<bool>(_owner, DiscretionaryLigaturesProperty);
        set => SetValue(_owner, DiscretionaryLigaturesProperty, value);
    }

    public bool HistoricalLigatures
    {
        get => GetValue<bool>(_owner, HistoricalLigaturesProperty);
        set => SetValue(_owner, HistoricalLigaturesProperty, value);
    }

    public int AnnotationAlternates
    {
        get => GetValue<int>(_owner, AnnotationAlternatesProperty);
        set => SetValue(_owner, AnnotationAlternatesProperty, value);
    }

    public bool ContextualAlternates
    {
        get => GetValue<bool>(_owner, ContextualAlternatesProperty);
        set => SetValue(_owner, ContextualAlternatesProperty, value);
    }

    public bool HistoricalForms
    {
        get => GetValue<bool>(_owner, HistoricalFormsProperty);
        set => SetValue(_owner, HistoricalFormsProperty, value);
    }

    public bool Kerning
    {
        get => GetValue<bool>(_owner, KerningProperty);
        set => SetValue(_owner, KerningProperty, value);
    }

    public bool CapitalSpacing
    {
        get => GetValue<bool>(_owner, CapitalSpacingProperty);
        set => SetValue(_owner, CapitalSpacingProperty, value);
    }

    public bool CaseSensitiveForms
    {
        get => GetValue<bool>(_owner, CaseSensitiveFormsProperty);
        set => SetValue(_owner, CaseSensitiveFormsProperty, value);
    }

    public bool StylisticSet1
    {
        get => GetValue<bool>(_owner, StylisticSet1Property);
        set => SetValue(_owner, StylisticSet1Property, value);
    }

    public bool StylisticSet2
    {
        get => GetValue<bool>(_owner, StylisticSet2Property);
        set => SetValue(_owner, StylisticSet2Property, value);
    }

    public bool StylisticSet3
    {
        get => GetValue<bool>(_owner, StylisticSet3Property);
        set => SetValue(_owner, StylisticSet3Property, value);
    }

    public bool StylisticSet4
    {
        get => GetValue<bool>(_owner, StylisticSet4Property);
        set => SetValue(_owner, StylisticSet4Property, value);
    }

    public bool StylisticSet5
    {
        get => GetValue<bool>(_owner, StylisticSet5Property);
        set => SetValue(_owner, StylisticSet5Property, value);
    }

    public bool StylisticSet6
    {
        get => GetValue<bool>(_owner, StylisticSet6Property);
        set => SetValue(_owner, StylisticSet6Property, value);
    }

    public bool StylisticSet7
    {
        get => GetValue<bool>(_owner, StylisticSet7Property);
        set => SetValue(_owner, StylisticSet7Property, value);
    }

    public bool StylisticSet8
    {
        get => GetValue<bool>(_owner, StylisticSet8Property);
        set => SetValue(_owner, StylisticSet8Property, value);
    }

    public bool StylisticSet9
    {
        get => GetValue<bool>(_owner, StylisticSet9Property);
        set => SetValue(_owner, StylisticSet9Property, value);
    }

    public bool StylisticSet10
    {
        get => GetValue<bool>(_owner, StylisticSet10Property);
        set => SetValue(_owner, StylisticSet10Property, value);
    }

    public bool StylisticSet11
    {
        get => GetValue<bool>(_owner, StylisticSet11Property);
        set => SetValue(_owner, StylisticSet11Property, value);
    }

    public bool StylisticSet12
    {
        get => GetValue<bool>(_owner, StylisticSet12Property);
        set => SetValue(_owner, StylisticSet12Property, value);
    }

    public bool StylisticSet13
    {
        get => GetValue<bool>(_owner, StylisticSet13Property);
        set => SetValue(_owner, StylisticSet13Property, value);
    }

    public bool StylisticSet14
    {
        get => GetValue<bool>(_owner, StylisticSet14Property);
        set => SetValue(_owner, StylisticSet14Property, value);
    }

    public bool StylisticSet15
    {
        get => GetValue<bool>(_owner, StylisticSet15Property);
        set => SetValue(_owner, StylisticSet15Property, value);
    }

    public bool StylisticSet16
    {
        get => GetValue<bool>(_owner, StylisticSet16Property);
        set => SetValue(_owner, StylisticSet16Property, value);
    }

    public bool StylisticSet17
    {
        get => GetValue<bool>(_owner, StylisticSet17Property);
        set => SetValue(_owner, StylisticSet17Property, value);
    }

    public bool StylisticSet18
    {
        get => GetValue<bool>(_owner, StylisticSet18Property);
        set => SetValue(_owner, StylisticSet18Property, value);
    }

    public bool StylisticSet19
    {
        get => GetValue<bool>(_owner, StylisticSet19Property);
        set => SetValue(_owner, StylisticSet19Property, value);
    }

    public bool StylisticSet20
    {
        get => GetValue<bool>(_owner, StylisticSet20Property);
        set => SetValue(_owner, StylisticSet20Property, value);
    }

    public FontFraction Fraction
    {
        get => GetValue<FontFraction>(_owner, FractionProperty);
        set => SetValue(_owner, FractionProperty, value);
    }

    public bool SlashedZero
    {
        get => GetValue<bool>(_owner, SlashedZeroProperty);
        set => SetValue(_owner, SlashedZeroProperty, value);
    }

    public bool MathematicalGreek
    {
        get => GetValue<bool>(_owner, MathematicalGreekProperty);
        set => SetValue(_owner, MathematicalGreekProperty, value);
    }

    public bool EastAsianExpertForms
    {
        get => GetValue<bool>(_owner, EastAsianExpertFormsProperty);
        set => SetValue(_owner, EastAsianExpertFormsProperty, value);
    }

    public FontVariants Variants
    {
        get => GetValue<FontVariants>(_owner, VariantsProperty);
        set => SetValue(_owner, VariantsProperty, value);
    }

    public FontCapitals Capitals
    {
        get => GetValue<FontCapitals>(_owner, CapitalsProperty);
        set => SetValue(_owner, CapitalsProperty, value);
    }

    public FontNumeralStyle NumeralStyle
    {
        get => GetValue<FontNumeralStyle>(_owner, NumeralStyleProperty);
        set => SetValue(_owner, NumeralStyleProperty, value);
    }

    public FontNumeralAlignment NumeralAlignment
    {
        get => GetValue<FontNumeralAlignment>(_owner, NumeralAlignmentProperty);
        set => SetValue(_owner, NumeralAlignmentProperty, value);
    }

    public FontEastAsianWidths EastAsianWidths
    {
        get => GetValue<FontEastAsianWidths>(_owner, EastAsianWidthsProperty);
        set => SetValue(_owner, EastAsianWidthsProperty, value);
    }

    public FontEastAsianLanguage EastAsianLanguage
    {
        get => GetValue<FontEastAsianLanguage>(_owner, EastAsianLanguageProperty);
        set => SetValue(_owner, EastAsianLanguageProperty, value);
    }

    public int StandardSwashes
    {
        get => GetValue<int>(_owner, StandardSwashesProperty);
        set => SetValue(_owner, StandardSwashesProperty, value);
    }

    public int ContextualSwashes
    {
        get => GetValue<int>(_owner, ContextualSwashesProperty);
        set => SetValue(_owner, ContextualSwashesProperty, value);
    }

    public int StylisticAlternates
    {
        get => GetValue<int>(_owner, StylisticAlternatesProperty);
        set => SetValue(_owner, StylisticAlternatesProperty, value);
    }

    public static void SetStandardLigatures(DependencyObject element, bool value) =>
        SetValue(element, StandardLigaturesProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStandardLigatures(DependencyObject element) =>
        GetValue<bool>(element, StandardLigaturesProperty);

    public static void SetContextualLigatures(DependencyObject element, bool value) =>
        SetValue(element, ContextualLigaturesProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetContextualLigatures(DependencyObject element) =>
        GetValue<bool>(element, ContextualLigaturesProperty);

    public static void SetDiscretionaryLigatures(DependencyObject element, bool value) =>
        SetValue(element, DiscretionaryLigaturesProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetDiscretionaryLigatures(DependencyObject element) =>
        GetValue<bool>(element, DiscretionaryLigaturesProperty);

    public static void SetHistoricalLigatures(DependencyObject element, bool value) =>
        SetValue(element, HistoricalLigaturesProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetHistoricalLigatures(DependencyObject element) =>
        GetValue<bool>(element, HistoricalLigaturesProperty);

    public static void SetAnnotationAlternates(DependencyObject element, int value) =>
        SetValue(element, AnnotationAlternatesProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static int GetAnnotationAlternates(DependencyObject element) =>
        GetValue<int>(element, AnnotationAlternatesProperty);

    public static void SetContextualAlternates(DependencyObject element, bool value) =>
        SetValue(element, ContextualAlternatesProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetContextualAlternates(DependencyObject element) =>
        GetValue<bool>(element, ContextualAlternatesProperty);

    public static void SetHistoricalForms(DependencyObject element, bool value) =>
        SetValue(element, HistoricalFormsProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetHistoricalForms(DependencyObject element) =>
        GetValue<bool>(element, HistoricalFormsProperty);

    public static void SetKerning(DependencyObject element, bool value) =>
        SetValue(element, KerningProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetKerning(DependencyObject element) =>
        GetValue<bool>(element, KerningProperty);

    public static void SetCapitalSpacing(DependencyObject element, bool value) =>
        SetValue(element, CapitalSpacingProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetCapitalSpacing(DependencyObject element) =>
        GetValue<bool>(element, CapitalSpacingProperty);

    public static void SetCaseSensitiveForms(DependencyObject element, bool value) =>
        SetValue(element, CaseSensitiveFormsProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetCaseSensitiveForms(DependencyObject element) =>
        GetValue<bool>(element, CaseSensitiveFormsProperty);

    public static void SetStylisticSet1(DependencyObject element, bool value) => SetValue(element, StylisticSet1Property, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStylisticSet1(DependencyObject element) => GetValue<bool>(element, StylisticSet1Property);

    public static void SetStylisticSet2(DependencyObject element, bool value) => SetValue(element, StylisticSet2Property, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStylisticSet2(DependencyObject element) => GetValue<bool>(element, StylisticSet2Property);

    public static void SetStylisticSet3(DependencyObject element, bool value) => SetValue(element, StylisticSet3Property, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStylisticSet3(DependencyObject element) => GetValue<bool>(element, StylisticSet3Property);

    public static void SetStylisticSet4(DependencyObject element, bool value) => SetValue(element, StylisticSet4Property, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStylisticSet4(DependencyObject element) => GetValue<bool>(element, StylisticSet4Property);

    public static void SetStylisticSet5(DependencyObject element, bool value) => SetValue(element, StylisticSet5Property, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStylisticSet5(DependencyObject element) => GetValue<bool>(element, StylisticSet5Property);

    public static void SetStylisticSet6(DependencyObject element, bool value) => SetValue(element, StylisticSet6Property, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStylisticSet6(DependencyObject element) => GetValue<bool>(element, StylisticSet6Property);

    public static void SetStylisticSet7(DependencyObject element, bool value) => SetValue(element, StylisticSet7Property, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStylisticSet7(DependencyObject element) => GetValue<bool>(element, StylisticSet7Property);

    public static void SetStylisticSet8(DependencyObject element, bool value) => SetValue(element, StylisticSet8Property, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStylisticSet8(DependencyObject element) => GetValue<bool>(element, StylisticSet8Property);

    public static void SetStylisticSet9(DependencyObject element, bool value) => SetValue(element, StylisticSet9Property, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStylisticSet9(DependencyObject element) => GetValue<bool>(element, StylisticSet9Property);

    public static void SetStylisticSet10(DependencyObject element, bool value) => SetValue(element, StylisticSet10Property, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStylisticSet10(DependencyObject element) => GetValue<bool>(element, StylisticSet10Property);

    public static void SetStylisticSet11(DependencyObject element, bool value) => SetValue(element, StylisticSet11Property, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStylisticSet11(DependencyObject element) => GetValue<bool>(element, StylisticSet11Property);

    public static void SetStylisticSet12(DependencyObject element, bool value) => SetValue(element, StylisticSet12Property, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStylisticSet12(DependencyObject element) => GetValue<bool>(element, StylisticSet12Property);

    public static void SetStylisticSet13(DependencyObject element, bool value) => SetValue(element, StylisticSet13Property, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStylisticSet13(DependencyObject element) => GetValue<bool>(element, StylisticSet13Property);

    public static void SetStylisticSet14(DependencyObject element, bool value) => SetValue(element, StylisticSet14Property, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStylisticSet14(DependencyObject element) => GetValue<bool>(element, StylisticSet14Property);

    public static void SetStylisticSet15(DependencyObject element, bool value) => SetValue(element, StylisticSet15Property, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStylisticSet15(DependencyObject element) => GetValue<bool>(element, StylisticSet15Property);

    public static void SetStylisticSet16(DependencyObject element, bool value) => SetValue(element, StylisticSet16Property, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStylisticSet16(DependencyObject element) => GetValue<bool>(element, StylisticSet16Property);

    public static void SetStylisticSet17(DependencyObject element, bool value) => SetValue(element, StylisticSet17Property, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStylisticSet17(DependencyObject element) => GetValue<bool>(element, StylisticSet17Property);

    public static void SetStylisticSet18(DependencyObject element, bool value) => SetValue(element, StylisticSet18Property, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStylisticSet18(DependencyObject element) => GetValue<bool>(element, StylisticSet18Property);

    public static void SetStylisticSet19(DependencyObject element, bool value) => SetValue(element, StylisticSet19Property, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStylisticSet19(DependencyObject element) => GetValue<bool>(element, StylisticSet19Property);

    public static void SetStylisticSet20(DependencyObject element, bool value) => SetValue(element, StylisticSet20Property, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetStylisticSet20(DependencyObject element) => GetValue<bool>(element, StylisticSet20Property);

    public static void SetFraction(DependencyObject element, FontFraction value) =>
        SetValue(element, FractionProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static FontFraction GetFraction(DependencyObject element) =>
        GetValue<FontFraction>(element, FractionProperty);

    public static void SetSlashedZero(DependencyObject element, bool value) =>
        SetValue(element, SlashedZeroProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetSlashedZero(DependencyObject element) =>
        GetValue<bool>(element, SlashedZeroProperty);

    public static void SetMathematicalGreek(DependencyObject element, bool value) =>
        SetValue(element, MathematicalGreekProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetMathematicalGreek(DependencyObject element) =>
        GetValue<bool>(element, MathematicalGreekProperty);

    public static void SetEastAsianExpertForms(DependencyObject element, bool value) =>
        SetValue(element, EastAsianExpertFormsProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static bool GetEastAsianExpertForms(DependencyObject element) =>
        GetValue<bool>(element, EastAsianExpertFormsProperty);

    public static void SetVariants(DependencyObject element, FontVariants value) =>
        SetValue(element, VariantsProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static FontVariants GetVariants(DependencyObject element) =>
        GetValue<FontVariants>(element, VariantsProperty);

    public static void SetCapitals(DependencyObject element, FontCapitals value) =>
        SetValue(element, CapitalsProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static FontCapitals GetCapitals(DependencyObject element) =>
        GetValue<FontCapitals>(element, CapitalsProperty);

    public static void SetNumeralStyle(DependencyObject element, FontNumeralStyle value) =>
        SetValue(element, NumeralStyleProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static FontNumeralStyle GetNumeralStyle(DependencyObject element) =>
        GetValue<FontNumeralStyle>(element, NumeralStyleProperty);

    public static void SetNumeralAlignment(DependencyObject element, FontNumeralAlignment value) =>
        SetValue(element, NumeralAlignmentProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static FontNumeralAlignment GetNumeralAlignment(DependencyObject element) =>
        GetValue<FontNumeralAlignment>(element, NumeralAlignmentProperty);

    public static void SetEastAsianWidths(DependencyObject element, FontEastAsianWidths value) =>
        SetValue(element, EastAsianWidthsProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static FontEastAsianWidths GetEastAsianWidths(DependencyObject element) =>
        GetValue<FontEastAsianWidths>(element, EastAsianWidthsProperty);

    public static void SetEastAsianLanguage(DependencyObject element, FontEastAsianLanguage value) =>
        SetValue(element, EastAsianLanguageProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static FontEastAsianLanguage GetEastAsianLanguage(DependencyObject element) =>
        GetValue<FontEastAsianLanguage>(element, EastAsianLanguageProperty);

    public static void SetStandardSwashes(DependencyObject element, int value) =>
        SetValue(element, StandardSwashesProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static int GetStandardSwashes(DependencyObject element) =>
        GetValue<int>(element, StandardSwashesProperty);

    public static void SetContextualSwashes(DependencyObject element, int value) =>
        SetValue(element, ContextualSwashesProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static int GetContextualSwashes(DependencyObject element) =>
        GetValue<int>(element, ContextualSwashesProperty);

    public static void SetStylisticAlternates(DependencyObject element, int value) =>
        SetValue(element, StylisticAlternatesProperty, value);

    [AttachedPropertyBrowsableForType(typeof(DependencyObject))]
    public static int GetStylisticAlternates(DependencyObject element) =>
        GetValue<int>(element, StylisticAlternatesProperty);

    private static DependencyProperty RegisterTypographyProperty<T>(string name, T defaultValue)
        where T : struct
    {
        return DependencyProperty.RegisterAttached(
            name,
            typeof(T),
            typeof(Typography),
            new FrameworkPropertyMetadata(defaultValue, TypographyMetadataOptions),
            static value => value is T);
    }

    private static T GetValue<T>(DependencyObject element, DependencyProperty property)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(element);
        return (T)element.GetValue(property)!;
    }

    private static void SetValue<T>(DependencyObject element, DependencyProperty property, T value)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(property, value);
    }
}
