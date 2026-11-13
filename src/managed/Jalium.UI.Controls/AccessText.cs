using System.Text;
using Jalium.UI.Documents;
using Jalium.UI.Data;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies that the text should be parsed for underscored access keys (mnemonics).
/// The first character following an underscore is used as the access key.
/// </summary>
[Jalium.UI.Markup.ContentProperty(nameof(Text))]
[TemplatePart(Name = "PART_TextDisplay", Type = typeof(TextBlock))]
[TemplateVisualState(Name = "WithAccessKey", GroupName = "AccessKeyStates")]
[TemplateVisualState(Name = "WithoutAccessKey", GroupName = "AccessKeyStates")]
public class AccessText : Control, IAddChild
{
    private TextBlock? _textBlock;
    private static readonly DependencyPropertyKey DisplayTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayText), typeof(string), typeof(AccessText), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DisplayTextProperty = DisplayTextPropertyKey.DependencyProperty;
    public string DisplayText => (string)GetValue(DisplayTextProperty)!;
    private static readonly DependencyPropertyKey AccessKeyIndexPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(AccessKeyIndex), typeof(int), typeof(AccessText), new PropertyMetadata(-1));
    public static readonly DependencyProperty AccessKeyIndexProperty = AccessKeyIndexPropertyKey.DependencyProperty;
    public int AccessKeyIndex => (int)GetValue(AccessKeyIndexProperty)!;
    private static readonly DependencyPropertyKey ResolvedFontFamilyPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ResolvedFontFamily), typeof(FontFamily), typeof(AccessText), new PropertyMetadata(SystemFonts.MessageFontFamily));
    public static readonly DependencyProperty ResolvedFontFamilyProperty = ResolvedFontFamilyPropertyKey.DependencyProperty;
    public FontFamily ResolvedFontFamily => (FontFamily)GetValue(ResolvedFontFamilyProperty)!;
    private static readonly DependencyPropertyKey ResolvedTextAlignmentPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ResolvedTextAlignment), typeof(TextAlignment), typeof(AccessText), new PropertyMetadata(TextAlignment.Left));
    public static readonly DependencyProperty ResolvedTextAlignmentProperty = ResolvedTextAlignmentPropertyKey.DependencyProperty;
    public TextAlignment ResolvedTextAlignment => (TextAlignment)GetValue(ResolvedTextAlignmentProperty)!;

    private List<InheritedValueObserver>? _inheritedAncestors;
    private bool _refreshingInheritedValues;
    private object? _lastTextShadow;
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
    public new static readonly DependencyProperty BackgroundProperty =
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
    public new static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(AccessText),
            new PropertyMetadata(null, OnForwardedPropertyChanged));

    /// <summary>
    /// Identifies the FontSize dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public new static readonly DependencyProperty FontSizeProperty =
        Control.FontSizeProperty.AddOwner(typeof(AccessText),
            new PropertyMetadata(FrameworkElement.DefaultFontSize, OnForwardedPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the FontWeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public new static readonly DependencyProperty FontWeightProperty =
        Control.FontWeightProperty.AddOwner(typeof(AccessText),
            new PropertyMetadata(FontWeights.Normal, OnForwardedPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the FontStyle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public new static readonly DependencyProperty FontStyleProperty =
        Control.FontStyleProperty.AddOwner(typeof(AccessText),
            new PropertyMetadata(FontStyles.Normal, OnForwardedPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the FontStretch dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public new static readonly DependencyProperty FontStretchProperty =
        TextBlock.FontStretchProperty.AddOwner(typeof(AccessText),
            new PropertyMetadata(FontStretches.Normal, OnForwardedPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the Foreground dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public new static readonly DependencyProperty ForegroundProperty =
        Control.ForegroundProperty.AddOwner(typeof(AccessText),
            new PropertyMetadata(null, OnForwardedPropertyChanged, null, inherits: true));

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
        DefaultStyleKey = typeof(AccessText);
        Loaded += (_, _) => ObserveInheritedAncestors();
        Unloaded += (_, _) => StopObservingInheritedAncestors();
        UpdateTemplateMetrics();
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
    public new Brush? Background
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
    public new FontFamily? FontFamily
    {
        get => (FontFamily?)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>
    /// Gets or sets the font size.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public new double FontSize
    {
        get => (double)GetValue(FontSizeProperty)!;
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the font weight.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public new FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty)!;
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the font style.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public new FontStyle FontStyle
    {
        get => (FontStyle)GetValue(FontStyleProperty)!;
        set => SetValue(FontStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the font stretch.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public new FontStretch FontStretch
    {
        get => GetValue(FontStretchProperty) is FontStretch value ? value : FontStretches.Normal;
        set => SetValue(FontStretchProperty, value);
    }

    /// <summary>
    /// Gets or sets the foreground brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public new Brush? Foreground
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

    protected internal override void OnVisualParentChanged(DependencyObject? oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        ObserveInheritedAncestors();
    }

    private void ObserveInheritedAncestors()
    {
        StopObservingInheritedAncestors();
        for (Visual? parent = ParentVisual; parent is not null; parent = parent.VisualParent)
            (_inheritedAncestors ??= []).Add(new InheritedValueObserver(this, parent));
        RefreshInheritedTemplateValues();
    }

    private void StopObservingInheritedAncestors()
    {
        if (_inheritedAncestors is null) return;
        foreach (var observer in _inheritedAncestors) observer.Detach();
        _inheritedAncestors.Clear();
    }

    private sealed class InheritedValueObserver
    {
        private readonly WeakReference<AccessText> _target;
        private readonly WeakReference<DependencyObject> _source;
        internal InheritedValueObserver(AccessText target, DependencyObject source)
        { _target = new(target); _source = new(source); source.PropertyChangedInternal += OnPropertyChanged; }
        internal void Detach()
        { if (_source.TryGetTarget(out var source)) source.PropertyChangedInternal -= OnPropertyChanged; }
        private void OnPropertyChanged(DependencyProperty property, object? oldValue, object? newValue)
        {
            if (!_target.TryGetTarget(out var target)) { Detach(); return; }
            if (property == Control.ForegroundProperty || property == Control.FontFamilyProperty ||
                property == Control.FontSizeProperty || property == Control.FontWeightProperty ||
                property == Control.FontStyleProperty || property == TextBlock.FontStretchProperty ||
                property == FlowDirectionProperty)
                target.RefreshInheritedTemplateValues();
        }
    }

    private void RefreshInheritedTemplateValues()
    {
        if (_refreshingInheritedValues) return;
        _refreshingInheritedValues = true;
        try
        {
            UpdateTemplateMetrics();
            if (_textBlock is null) return;
            // Inherited properties resolve lazily. Re-evaluate their declared
            // bindings when an ancestor changes without replacing those bindings.
            foreach (var property in new[] { TextBlock.ForegroundProperty, TextBlock.FontFamilyProperty,
                TextBlock.FontSizeProperty, TextBlock.FontWeightProperty, TextBlock.FontStyleProperty,
                TextBlock.FontStretchProperty, TextBlock.TextAlignmentProperty })
                BindingOperations.GetBindingExpressionBase(_textBlock, property)?.UpdateTarget();
            _textBlock.InvalidateVisual();
            InvalidateVisual();
        }
        finally { _refreshingInheritedValues = false; }
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _textBlock = GetTemplateChild("PART_TextDisplay") as TextBlock;
        RefreshInheritedTemplateValues();
    }

    internal override void OnTemplateContentClearing()
    {
        _textBlock = null;
        base.OnTemplateContentClearing();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Ancestors can attach after this control's immediate presenter did.
        ObserveInheritedAncestors();
        UpdateTemplateMetrics();
        return base.MeasureOverride(availableSize);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_textBlock is null) { base.OnRender(drawingContext); return; }
        var textShadow = CssTextShadowProperties.Value(this);
        if (!ReferenceEquals(_lastTextShadow, textShadow))
        {
            _lastTextShadow = textShadow;
            _textBlock.InvalidateVisual();
        }
        var alignment = CssFlowProperties.NativeTextAlignment(this, TextAlignmentProperty);
        SetValue(ResolvedTextAlignmentPropertyKey, alignment);
        base.OnRender(drawingContext);

        var foreground = Foreground ?? _textBlock.Foreground;
        var displayText = DisplayText;
        if (foreground == null || _accessKeyDisplayIndex < 0 || _accessKeyDisplayIndex >= displayText.Length)
        {
            return;
        }

        var keyLength = GraphemeClusters.NextBoundary(displayText, _accessKeyDisplayIndex) -
            _accessKeyDisplayIndex;
        var segments = _textBlock.GetSourceRangeGeometry(_accessKeyDisplayIndex, keyLength);
        var pen = new Pen(foreground, 1);
        foreach (var segment in segments)
        {
            var dx = segment.End.X - segment.Start.X;
            var dy = segment.End.Y - segment.Start.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            var shift = Math.Max(1, FontSize * 0.08);
            var normalX = length > 0 ? -dy / length * shift : 0;
            var normalY = length > 0 ? dx / length * shift : shift;
            var transform = _textBlock.TransformToVisual(this);
            drawingContext.DrawLine(pen,
                transform.Transform(new Point(segment.Start.X + normalX, segment.Start.Y + normalY)),
                transform.Transform(new Point(segment.End.X + normalX, segment.End.Y + normalY)));
        }
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
            at.UpdateTemplateMetrics();
            at.InvalidateMeasure();
            at.InvalidateVisual();
        }
    }

    private static void OnForwardedPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AccessText accessText)
        {
            accessText.UpdateTemplateMetrics();
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

    private void UpdateTemplateMetrics()
    {
        SetValue(DisplayTextPropertyKey, ComputeDisplayText());
        SetValue(AccessKeyIndexPropertyKey, _accessKeyDisplayIndex);
        SetValue(ResolvedFontFamilyPropertyKey, FontFamily ?? (FontFamily?)GetValue(Control.FontFamilyProperty) ?? SystemFonts.MessageFontFamily);
        SetValue(ResolvedTextAlignmentPropertyKey, CssFlowProperties.NativeTextAlignment(this, TextAlignmentProperty));
        if (TemplateRootInternal is not null)
            VisualStateManager.GoToState(this, _accessKeyDisplayIndex >= 0 ? "WithAccessKey" : "WithoutAccessKey", false);
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
    private string ComputeDisplayText()
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
