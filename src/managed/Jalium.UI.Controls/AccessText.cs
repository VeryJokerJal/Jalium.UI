using System.Text;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies that the text should be parsed for underscored access keys (mnemonics).
/// The first character following an underscore is used as the access key.
/// </summary>
[Jalium.UI.Markup.ContentProperty(nameof(Text))]
public class AccessText : FrameworkElement, IAddChild
{
    private readonly TextBlock _textBlock;
    private int _accessKeyDisplayIndex = -1;

    #region Dependency Properties

    /// <summary>
    /// Identifies the Text dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(AccessText),
            new PropertyMetadata(string.Empty, OnTextChanged));

    /// <summary>
    /// Identifies the Background dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BackgroundProperty =
        TextBlock.BackgroundProperty.AddOwner(typeof(AccessText),
            new PropertyMetadata(null, OnForwardedPropertyChanged));

    /// <summary>
    /// Identifies the BaselineOffset dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty BaselineOffsetProperty =
        TextBlock.BaselineOffsetProperty.AddOwner(typeof(AccessText),
            new PropertyMetadata(double.NaN, OnForwardedPropertyChanged));

    /// <summary>
    /// Identifies the FontFamily dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(AccessText),
            new PropertyMetadata(null, OnForwardedPropertyChanged));

    /// <summary>
    /// Identifies the FontSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(AccessText),
            new PropertyMetadata(FrameworkElement.DefaultFontSize, OnForwardedPropertyChanged));

    /// <summary>
    /// Identifies the FontWeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(AccessText),
            new PropertyMetadata(FontWeights.Normal, OnForwardedPropertyChanged));

    /// <summary>
    /// Identifies the FontStyle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontStyleProperty =
        DependencyProperty.Register(nameof(FontStyle), typeof(FontStyle), typeof(AccessText),
            new PropertyMetadata(FontStyles.Normal, OnForwardedPropertyChanged));

    /// <summary>
    /// Identifies the FontStretch dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontStretchProperty =
        TextBlock.FontStretchProperty.AddOwner(typeof(AccessText),
            new PropertyMetadata(FontStretches.Normal, OnForwardedPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the Foreground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(AccessText),
            new PropertyMetadata(null, OnForwardedPropertyChanged));

    /// <summary>
    /// Identifies the TextWrapping dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextWrappingProperty =
        TextBlock.TextWrappingProperty.AddOwner(typeof(AccessText),
            new PropertyMetadata(TextWrapping.NoWrap, OnForwardedPropertyChanged));

    /// <summary>
    /// Identifies the TextTrimming dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextTrimmingProperty =
        TextBlock.TextTrimmingProperty.AddOwner(typeof(AccessText),
            new PropertyMetadata(TextTrimming.None, OnForwardedPropertyChanged));

    /// <summary>
    /// Identifies the LineHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty LineHeightProperty =
        TextBlock.LineHeightProperty.AddOwner(typeof(AccessText),
            new PropertyMetadata(double.NaN, OnForwardedPropertyChanged));

    /// <summary>
    /// Identifies the LineStackingStrategy dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty LineStackingStrategyProperty =
        TextBlock.LineStackingStrategyProperty.AddOwner(typeof(AccessText),
            new PropertyMetadata(LineStackingStrategy.MaxHeight, OnForwardedPropertyChanged));

    /// <summary>
    /// Identifies the TextAlignment dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextAlignmentProperty =
        TextBlock.TextAlignmentProperty.AddOwner(typeof(AccessText),
            new PropertyMetadata(TextAlignment.Left, OnForwardedPropertyChanged));

    /// <summary>
    /// Identifies the TextDecorations dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextDecorationsProperty =
        TextBlock.TextDecorationsProperty.AddOwner(typeof(AccessText),
            new PropertyMetadata(null, OnForwardedPropertyChanged));

    /// <summary>
    /// Identifies the TextEffects dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextEffectsProperty =
        TextBlock.TextEffectsProperty.AddOwner(typeof(AccessText),
            new PropertyMetadata(null, OnForwardedPropertyChanged));

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="AccessText"/> class.
    /// </summary>
    public AccessText()
    {
        _textBlock = new TextBlock
        {
            Focusable = false,
        };
        AddVisualChild(_textBlock);
        SynchronizeTextBlock();
    }

    /// <summary>
    /// Gets or sets the text that is displayed.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Text
    {
        get => (string)GetValue(TextProperty)!;
        set => SetValue(TextProperty, value);
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
    /// Gets or sets the baseline offset.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public double BaselineOffset
    {
        get => (double)GetValue(BaselineOffsetProperty)!;
        set => SetValue(BaselineOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets the font family.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public FontFamily? FontFamily
    {
        get => (FontFamily?)GetValue(FontFamilyProperty);
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
        get => (FontWeight)GetValue(FontWeightProperty)!;
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the font style.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public FontStyle FontStyle
    {
        get => (FontStyle)GetValue(FontStyleProperty)!;
        set => SetValue(FontStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the font stretch.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public FontStretch FontStretch
    {
        get => GetValue(FontStretchProperty) is FontStretch value ? value : FontStretches.Normal;
        set => SetValue(FontStretchProperty, value);
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
    /// Gets or sets the text wrapping.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public TextWrapping TextWrapping
    {
        get => (TextWrapping)GetValue(TextWrappingProperty)!;
        set => SetValue(TextWrappingProperty, value);
    }

    /// <summary>
    /// Gets or sets the text trimming.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public TextTrimming TextTrimming
    {
        get => (TextTrimming)GetValue(TextTrimmingProperty)!;
        set => SetValue(TextTrimmingProperty, value);
    }

    /// <summary>
    /// Gets or sets the line height.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public double LineHeight
    {
        get => (double)GetValue(LineHeightProperty)!;
        set => SetValue(LineHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the line stacking strategy.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public LineStackingStrategy LineStackingStrategy
    {
        get => (LineStackingStrategy)GetValue(LineStackingStrategyProperty)!;
        set => SetValue(LineStackingStrategyProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal alignment of the text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty)!;
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets decorations applied to the text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public TextDecorationCollection TextDecorations
    {
        get
        {
            if (GetValue(TextDecorationsProperty) is TextDecorationCollection value)
            {
                return value;
            }

            value = new TextDecorationCollection();
            SetCurrentValue(TextDecorationsProperty, value);
            return value;
        }
        set => SetValue(TextDecorationsProperty, value);
    }

    /// <summary>
    /// Gets or sets effects applied to the text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public TextEffectCollection TextEffects
    {
        get
        {
            if (GetValue(TextEffectsProperty) is TextEffectCollection value)
            {
                return value;
            }

            value = new TextEffectCollection();
            SetCurrentValue(TextEffectsProperty, value);
            return value;
        }
        set => SetValue(TextEffectsProperty, value);
    }

    /// <summary>
    /// Gets the access key character, or '\0' if none.
    /// </summary>
    public char AccessKey { get; private set; }

    /// <inheritdoc />
    protected override int VisualChildrenCount => 1;

    /// <inheritdoc />
    protected override Visual? GetVisualChild(int index)
    {
        if (index != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _textBlock;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        _textBlock.Measure(availableSize);
        return _textBlock.DesiredSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        _textBlock.Arrange(new Rect(finalSize));
        return finalSize;
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var foreground = Foreground ?? _textBlock.Foreground;
        var displayText = DisplayText;
        if (foreground == null || _accessKeyDisplayIndex < 0 || _accessKeyDisplayIndex >= displayText.Length)
        {
            return;
        }

        var family = FontFamily?.Source ?? FrameworkElement.DefaultFontFamilyName;
        var prefix = new FormattedText(displayText[.._accessKeyDisplayIndex], family, FontSize)
        {
            FontWeight = FontWeight.ToOpenTypeWeight(),
            FontStyle = FontStyle.ToOpenTypeStyle(),
            FontStretch = FontStretch.ToOpenTypeStretch(),
        };
        var key = new FormattedText(displayText.Substring(_accessKeyDisplayIndex, 1), family, FontSize)
        {
            FontWeight = FontWeight.ToOpenTypeWeight(),
            FontStyle = FontStyle.ToOpenTypeStyle(),
            FontStretch = FontStretch.ToOpenTypeStretch(),
        };
        var whole = new FormattedText(displayText, family, FontSize)
        {
            FontWeight = FontWeight.ToOpenTypeWeight(),
            FontStyle = FontStyle.ToOpenTypeStyle(),
            FontStretch = FontStretch.ToOpenTypeStretch(),
        };
        TextMeasurement.MeasureText(prefix);
        TextMeasurement.MeasureText(key);
        TextMeasurement.MeasureText(whole);

        var textX = TextAlignment switch
        {
            TextAlignment.Center => Math.Max(0, (RenderSize.Width - whole.Width) / 2),
            TextAlignment.Right => Math.Max(0, RenderSize.Width - whole.Width),
            _ => 0,
        };
        var textY = Math.Max(0, (RenderSize.Height - whole.Height) / 2);
        var underlineY = textY + whole.Height - 1;
        drawingContext.DrawLine(
            new Pen(foreground, 1),
            new Point(textX + prefix.Width, underlineY),
            new Point(textX + prefix.Width + Math.Max(1, key.Width), underlineY));
    }

    void IAddChild.AddChild(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        switch (value)
        {
            case string text:
                AppendText(text);
                return;
            case Inline inline when TryGetInlineText(inline, out var inlineText):
                AppendText(inlineText);
                return;
            default:
                throw new ArgumentException(
                    $"AccessText cannot represent child content of type {value.GetType().FullName} as text.",
                    nameof(value));
        }
    }

    void IAddChild.AddText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        AppendText(text);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AccessText at)
        {
            at.UpdateAccessKey();
            at.SynchronizeTextBlock();
            at.InvalidateMeasure();
            at.InvalidateVisual();
        }
    }

    private static void OnForwardedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AccessText accessText)
        {
            accessText.SynchronizeTextBlock();
            accessText.InvalidateMeasure();
            accessText.InvalidateVisual();
        }
    }

    private void AppendText(string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        Text = (Text ?? string.Empty) + text;
    }

    private static bool TryGetInlineText(Inline inline, out string text)
    {
        switch (inline)
        {
            case Run run:
                text = run.Text;
                return true;
            case LineBreak:
                text = "\n";
                return true;
            case Span span:
                var builder = new StringBuilder();
                foreach (var child in span.Inlines)
                {
                    if (!TryGetInlineText(child, out var childText))
                    {
                        text = string.Empty;
                        return false;
                    }

                    builder.Append(childText);
                }

                text = builder.ToString();
                return true;
            default:
                text = string.Empty;
                return false;
        }
    }

    private void SynchronizeTextBlock()
    {
        _textBlock.Text = DisplayText;
        _textBlock.FontFamily = FontFamily ?? new FontFamily(FrameworkElement.DefaultFontFamilyName);
        _textBlock.FontSize = FontSize;
        _textBlock.FontWeight = FontWeight;
        _textBlock.FontStyle = FontStyle;
        _textBlock.FontStretch = FontStretch;
        if (Foreground is Brush foreground)
        {
            _textBlock.Foreground = foreground;
        }
        else
        {
            _textBlock.ClearValue(TextBlock.ForegroundProperty);
        }
        _textBlock.Background = Background;
        _textBlock.TextWrapping = TextWrapping;
        _textBlock.TextTrimming = TextTrimming;
        _textBlock.BaselineOffset = BaselineOffset;
        _textBlock.LineHeight = LineHeight;
        _textBlock.LineStackingStrategy = LineStackingStrategy;
        _textBlock.TextAlignment = TextAlignment;

        if (GetValue(TextDecorationsProperty) is TextDecorationCollection decorations)
        {
            _textBlock.TextDecorations = decorations;
        }
        else
        {
            _textBlock.ClearValue(TextBlock.TextDecorationsProperty);
        }

        if (GetValue(TextEffectsProperty) is TextEffectCollection effects)
        {
            _textBlock.TextEffects = effects;
        }
        else
        {
            _textBlock.ClearValue(TextBlock.TextEffectsProperty);
        }
    }

    private void UpdateAccessKey()
    {
        AccessKey = '\0';
        _accessKeyDisplayIndex = -1;
        var text = Text;
        if (string.IsNullOrEmpty(text)) return;

        var displayIndex = 0;
        for (int i = 0; i < text.Length - 1; i++)
        {
            if (text[i] == '_' && text[i + 1] != '_')
            {
                AccessKey = text[i + 1];
                _accessKeyDisplayIndex = displayIndex;
                return;
            }
            if (text[i] == '_' && text[i + 1] == '_')
            {
                i++; // Skip escaped underscore
            }

            displayIndex++;
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
