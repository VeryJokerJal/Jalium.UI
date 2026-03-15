using Jalium.UI.Media;

namespace Jalium.UI.Documents;

using TextDecorationCollection = Jalium.UI.Media.TextDecorationCollection;

/// <summary>
/// Abstract base class for all elements in a FlowDocument.
/// </summary>
public abstract class TextElement : DependencyObject
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the FontFamily dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(nameof(FontFamily), typeof(string), typeof(TextElement),
            new PropertyMetadata("Segoe UI"));

    /// <summary>
    /// Identifies the FontSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(TextElement),
            new PropertyMetadata(14.0));

    /// <summary>
    /// Identifies the FontWeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(TextElement),
            new PropertyMetadata(FontWeights.Normal));

    /// <summary>
    /// Identifies the FontStyle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontStyleProperty =
        DependencyProperty.Register(nameof(FontStyle), typeof(FontStyle), typeof(TextElement),
            new PropertyMetadata(FontStyles.Normal));

    /// <summary>
    /// Identifies the Foreground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(TextElement),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the Background dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(TextElement),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the TextDecorations dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextDecorationsProperty =
        DependencyProperty.Register(nameof(TextDecorations), typeof(TextDecorationCollection), typeof(TextElement),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the font family.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public string FontFamily
    {
        get => (string)(GetValue(FontFamilyProperty) ?? "Segoe UI");
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>
    /// Gets or sets the font size.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty)!;
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the font weight.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty) is FontWeight fw ? fw : FontWeights.Normal;
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the font style.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public FontStyle FontStyle
    {
        get => GetValue(FontStyleProperty) is FontStyle fs ? fs : FontStyles.Normal;
        set => SetValue(FontStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the foreground brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the background brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the text decorations.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public TextDecorationCollection? TextDecorations
    {
        get => (TextDecorationCollection?)GetValue(TextDecorationsProperty);
        set => SetValue(TextDecorationsProperty, value);
    }

    /// <summary>
    /// Gets the parent element.
    /// </summary>
    public TextElement? Parent { get; internal set; }

    #endregion

    #region Methods

    /// <summary>
    /// Gets the effective value of a property, considering inheritance.
    /// </summary>
    protected T GetEffectiveValue<T>(DependencyProperty property, T defaultValue)
    {
        var value = GetValue(property);
        if (value != null)
            return (T)value;

        if (Parent != null)
        {
            return (T)(Parent.GetValue(property) ?? defaultValue);
        }

        return defaultValue;
    }

    /// <summary>
    /// Gets the effective font family.
    /// </summary>
    public string GetEffectiveFontFamily() => GetEffectiveValue(FontFamilyProperty, "Segoe UI");

    /// <summary>
    /// Gets the effective font size.
    /// </summary>
    public double GetEffectiveFontSize() => GetEffectiveValue(FontSizeProperty, 14.0);

    /// <summary>
    /// Gets the effective font weight.
    /// </summary>
    public FontWeight GetEffectiveFontWeight() => GetEffectiveValue(FontWeightProperty, FontWeights.Normal);

    /// <summary>
    /// Gets the effective font style.
    /// </summary>
    public FontStyle GetEffectiveFontStyle() => GetEffectiveValue(FontStyleProperty, FontStyles.Normal);

    /// <summary>
    /// Gets the effective foreground brush.
    /// </summary>
    public Brush GetEffectiveForeground() =>
        GetEffectiveValue<Brush?>(ForegroundProperty, null) ?? new SolidColorBrush(Color.Black);

    /// <summary>
    /// Gets the effective background brush.
    /// </summary>
    public Brush? GetEffectiveBackground() => GetEffectiveValue<Brush?>(BackgroundProperty, null);

    /// <summary>
    /// Gets the effective text decorations.
    /// </summary>
    public TextDecorationCollection? GetEffectiveTextDecorations() => GetEffectiveValue<TextDecorationCollection?>(TextDecorationsProperty, null);

    #endregion
}

