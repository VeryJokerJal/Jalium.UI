using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Displays text content.
/// </summary>
public sealed class TextBlock : FrameworkElement
{
    #region Cached FormattedText

    private FormattedText? _cachedFormattedText;
    private Size _cachedRenderSize;

    private FormattedText? GetOrCreateFormattedText()
    {
        if (string.IsNullOrEmpty(Text) || Foreground == null)
            return null;

        // Check if cache is still valid
        if (_cachedFormattedText != null &&
            _cachedRenderSize.Width == RenderSize.Width &&
            _cachedRenderSize.Height == RenderSize.Height)
        {
            return _cachedFormattedText;
        }

        // Create new FormattedText
        _cachedFormattedText = new FormattedText(Text, FontFamily, FontSize)
        {
            Foreground = Foreground,
            MaxTextWidth = RenderSize.Width,
            MaxTextHeight = RenderSize.Height,
            FontWeight = FontWeight.ToOpenTypeWeight(),
            Trimming = TextTrimming
        };
        _cachedRenderSize = RenderSize;

        return _cachedFormattedText;
    }

    private void InvalidateFormattedTextCache()
    {
        _cachedFormattedText = null;
    }

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Text dependency property.
    /// </summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(TextBlock),
            new PropertyMetadata(string.Empty, OnTextChanged));

    /// <summary>
    /// Identifies the Foreground dependency property.
    /// </summary>
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(TextBlock),
            new PropertyMetadata(new SolidColorBrush(Color.Black), OnVisualPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the FontFamily dependency property.
    /// </summary>
    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(nameof(FontFamily), typeof(string), typeof(TextBlock),
            new PropertyMetadata("Segoe UI", OnTextChanged));

    /// <summary>
    /// Identifies the FontSize dependency property.
    /// </summary>
    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(TextBlock),
            new PropertyMetadata(14.0, OnTextChanged));

    /// <summary>
    /// Identifies the FontStyle dependency property.
    /// </summary>
    public static readonly DependencyProperty FontStyleProperty =
        DependencyProperty.Register(nameof(FontStyle), typeof(FontStyle), typeof(TextBlock),
            new PropertyMetadata(FontStyles.Normal, OnTextChanged));

    /// <summary>
    /// Identifies the FontWeight dependency property.
    /// </summary>
    public static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.Register(nameof(FontWeight), typeof(FontWeight), typeof(TextBlock),
            new PropertyMetadata(FontWeights.Normal, OnTextChanged));

    /// <summary>
    /// Identifies the TextWrapping dependency property.
    /// </summary>
    public static readonly DependencyProperty TextWrappingProperty =
        DependencyProperty.Register(nameof(TextWrapping), typeof(TextWrapping), typeof(TextBlock),
            new PropertyMetadata(TextWrapping.NoWrap, OnTextChanged));

    /// <summary>
    /// Identifies the TextAlignment dependency property.
    /// </summary>
    public static readonly DependencyProperty TextAlignmentProperty =
        DependencyProperty.Register(nameof(TextAlignment), typeof(TextAlignment), typeof(TextBlock),
            new PropertyMetadata(TextAlignment.Left, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the TextTrimming dependency property.
    /// </summary>
    public static readonly DependencyProperty TextTrimmingProperty =
        DependencyProperty.Register(nameof(TextTrimming), typeof(TextTrimming), typeof(TextBlock),
            new PropertyMetadata(TextTrimming.None, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    public string Text
    {
        get => (string)(GetValue(TextProperty) ?? string.Empty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// Gets or sets the foreground brush.
    /// </summary>
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the font family.
    /// </summary>
    public string FontFamily
    {
        get => (string)(GetValue(FontFamilyProperty) ?? "Segoe UI");
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>
    /// Gets or sets the font size.
    /// </summary>
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty)!;
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the font style.
    /// </summary>
    public FontStyle FontStyle
    {
        get => GetValue(FontStyleProperty) is FontStyle fs ? fs : FontStyles.Normal;
        set => SetValue(FontStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the font weight.
    /// </summary>
    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty) is FontWeight fw ? fw : FontWeights.Normal;
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the text wrapping mode.
    /// </summary>
    public TextWrapping TextWrapping
    {
        get => (TextWrapping)GetValue(TextWrappingProperty)!;
        set => SetValue(TextWrappingProperty, value);
    }

    /// <summary>
    /// Gets or sets the text alignment.
    /// </summary>
    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty)!;
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets the text trimming mode.
    /// </summary>
    public TextTrimming TextTrimming
    {
        get => (TextTrimming)GetValue(TextTrimmingProperty)!;
        set => SetValue(TextTrimmingProperty, value);
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var text = Text;
        if (string.IsNullOrEmpty(text))
        {
            return Size.Empty;
        }

        // Determine max width for measurement
        double maxWidth;
        if (TextWrapping != TextWrapping.NoWrap)
        {
            maxWidth = double.IsInfinity(availableSize.Width) ? 100000 : availableSize.Width;
        }
        else if (TextTrimming != TextTrimming.None && !double.IsInfinity(availableSize.Width))
        {
            // When trimming is enabled and we have a finite constraint,
            // measure with that constraint so DesiredSize doesn't exceed available space.
            maxWidth = availableSize.Width;
        }
        else
        {
            maxWidth = double.MaxValue;
        }

        // Create FormattedText for measurement
        var formattedText = new FormattedText(text, FontFamily, FontSize)
        {
            MaxTextWidth = maxWidth,
            MaxTextHeight = double.MaxValue,
            FontWeight = FontWeight.ToOpenTypeWeight(),
            Trimming = TextTrimming
        };

        // Use native text measurement if available
        TextMeasurement.MeasureText(formattedText);

        // Get the measured size
        // Add small buffer to prevent edge-case overflow
        var measuredWidth = formattedText.Width + 2;
        var measuredHeight = formattedText.Height + 2;

        // When trimming, clamp width to available space
        if (TextTrimming != TextTrimming.None && !double.IsInfinity(availableSize.Width))
        {
            measuredWidth = Math.Min(measuredWidth, availableSize.Width);
        }

        return new Size(measuredWidth, measuredHeight);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        base.OnRender(drawingContext);

        if (drawingContext is DrawingContext dc)
        {
            var formattedText = GetOrCreateFormattedText();
            if (formattedText != null)
            {
                dc.DrawText(formattedText, Point.Zero);
            }
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock)
        {
            textBlock.InvalidateFormattedTextCache();

            // Skip layout invalidation if text is empty and property isn't Text itself
            // (font changes on empty text don't affect layout)
            if (e.Property != TextProperty && string.IsNullOrEmpty(textBlock.Text))
            {
                return;
            }
            textBlock.InvalidateMeasure();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock)
        {
            // Skip if value didn't change or text is empty (nothing to render)
            if (Equals(e.OldValue, e.NewValue)) return;

            textBlock.InvalidateFormattedTextCache();
            textBlock.InvalidateVisual();
        }
    }

    #endregion
}

/// <summary>
/// Specifies text wrapping behavior.
/// </summary>
public enum TextWrapping
{
    NoWrap,
    Wrap,
    WrapWithOverflow
}

/// <summary>
/// Specifies text alignment.
/// </summary>
public enum TextAlignment
{
    Left,
    Center,
    Right,
    Justify
}
