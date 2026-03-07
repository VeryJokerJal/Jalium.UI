using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies that the text should be parsed for underscored access keys (mnemonics).
/// The first character following an underscore is used as the access key.
/// </summary>
public sealed class AccessText : FrameworkElement
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Text dependency property.
    /// </summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(AccessText),
            new PropertyMetadata(string.Empty, OnTextChanged));

    /// <summary>
    /// Identifies the FontFamily dependency property.
    /// </summary>
    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(AccessText),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the FontSize dependency property.
    /// </summary>
    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(AccessText),
            new PropertyMetadata(12.0));

    /// <summary>
    /// Identifies the FontWeight dependency property.
    /// </summary>
    public static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(AccessText),
            new PropertyMetadata(FontWeights.Normal));

    /// <summary>
    /// Identifies the FontStyle dependency property.
    /// </summary>
    public static readonly DependencyProperty FontStyleProperty =
        DependencyProperty.Register(nameof(FontStyle), typeof(FontStyle), typeof(AccessText),
            new PropertyMetadata(FontStyles.Normal));

    /// <summary>
    /// Identifies the Foreground dependency property.
    /// </summary>
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(AccessText),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the FontColor dependency property.
    /// </summary>
    public static readonly DependencyProperty FontColorProperty=
        DependencyProperty.Register(nameof(FontColor), typeof(Color), typeof(AccessText),
            new PropertyMetadata(Colors.Black));

    /// <summary>
    /// Identifies the TextWrapping dependency property.
    /// </summary>
    public static readonly DependencyProperty TextWrappingProperty =
        DependencyProperty.Register(nameof(TextWrapping), typeof(TextWrapping), typeof(AccessText),
            new PropertyMetadata(TextWrapping.NoWrap));

    /// <summary>
    /// Identifies the TextTrimming dependency property.
    /// </summary>
    public static readonly DependencyProperty TextTrimmingProperty =
        DependencyProperty.Register(nameof(TextTrimming), typeof(TextTrimming), typeof(AccessText),
            new PropertyMetadata(TextTrimming.None));

    #endregion

    /// <summary>
    /// Gets or sets the text that is displayed.
    /// </summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// Gets or sets the font family.
    /// </summary>
    public FontFamily? FontFamily
    {
        get => (FontFamily?)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>
    /// Gets or sets the font size.
    /// </summary>
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the font weight.
    /// </summary>
    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the font style.
    /// </summary>
    public FontStyle FontStyle
    {
        get => (FontStyle)GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the foreground brush.
    /// </summary>
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public Color? FontColor
    {
        get => (Color?)GetValue(FontColorProperty);
        set => SetValue(FontColorProperty, value);
    }

    /// <summary>
    /// Gets or sets the text wrapping.
    /// </summary>
    public TextWrapping TextWrapping
    {
        get => (TextWrapping)GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    /// <summary>
    /// Gets or sets the text trimming.
    /// </summary>
    public TextTrimming TextTrimming
    {
        get => (TextTrimming)GetValue(TextTrimmingProperty);
        set => SetValue(TextTrimmingProperty, value);
    }

    /// <summary>
    /// Gets the access key character, or '\0' if none.
    /// </summary>
    public char AccessKey { get; private set; }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AccessText at)
        {
            at.UpdateAccessKey();
            at.InvalidateMeasure();
        }
    }

    private void UpdateAccessKey()
    {
        AccessKey = '\0';
        var text = Text;
        if (string.IsNullOrEmpty(text)) return;

        for (int i = 0; i < text.Length - 1; i++)
        {
            if (text[i] == '_' && text[i + 1] != '_')
            {
                AccessKey = text[i + 1];
                return;
            }
            if (text[i] == '_' && text[i + 1] == '_')
            {
                i++; // Skip escaped underscore
            }
        }
    }

    /// <summary>
    /// Gets the display text with the underscore mnemonic removed.
    /// </summary>
    public string DisplayText
    {
        get
        {
            var text = Text;
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var result = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '_')
                {
                    if (i + 1 < text.Length && text[i + 1] == '_')
                    {
                        result.Append('_');
                        i++;
                    }
                    // Skip the single underscore (access key indicator)
                }
                else
                {
                    result.Append(text[i]);
                }
            }
            return result.ToString();
        }
    }
}
