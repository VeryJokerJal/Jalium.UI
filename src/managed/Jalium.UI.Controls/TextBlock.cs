using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using WpfClipboard = global::Jalium.UI.Clipboard;

namespace Jalium.UI.Controls;

/// <summary>
/// Displays text content.
/// </summary>
[Jalium.UI.Markup.ContentProperty(nameof(Inlines))]
public partial class TextBlock : FrameworkElement, IAddChild, IServiceProvider, IContentHost
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.TextBlockAutomationPeer(this);
    }

    private const int MaxTextWidthCacheEntries = 256;
    private static readonly SolidColorBrush s_defaultSelectionBrush = new(Color.FromArgb(180, 0x1E, 0x79, 0x3F));

    private readonly Dictionary<string, TextMeasurementCacheEntry> _textWidthCache = new(StringComparer.Ordinal);
    private InlineCollection? _inlines;
    private List<InlineDecorationRange>? _inlineDecorationRanges;
    private List<InlineTextRange>? _inlineTextRanges;
    private string _displayText = string.Empty;
    private bool _synchronizingTextAndInlines;
    private bool _inlinesExplicitlyModified;

    private List<TextLayoutLine> _layoutLines = new();
    private bool _layoutDirty = true;
    private bool _layoutRubyFontArea;
    private double _layoutConstraintWidth = double.NaN;
    private bool _layoutIntrinsic;
    private CssWordBreak _layoutWordBreak;
    private CssLineBreak _layoutLineBreak;
    private string? _layoutLanguageTag;
    private bool _layoutUsesCjkWritingSystem;
    private string? _layoutText;
    private List<FormattedText>? _cachedFormattedLines;
    private bool _formattedLinesCacheDirty = true;
    private RectangleGeometry? _renderClipCache;
    // Foreground 是“绘制属性”而非“布局属性”：变了不需要重 layout，但必须同步到
    // _cachedFormattedLines 里每个 FormattedText.Foreground，否则 OnRender 用旧 brush。
    // 用单独的 dirty 标志驱动，平时 OnRender 0 开销，只在 Foreground 真变后下一帧同步一次。
    private bool _foregroundCacheNeedsSync;

    private string? _cachedFontFamily;
    private double _cachedFontSize;
    private int _cachedFontWeight;
    private int _cachedFontStyle;
    private int _cachedFontStretch;
    private double? _cachedLineHeight;
    private int _lineHeightContextGeneration;
    private long _lineHeightMetricsEpoch;
    private (long Family, long Size, long Weight, long Style, long Height, long Stacking, long CssHeight, long RubyContext, long Tree) _lineHeightInputVersions;

    private int _selectionStart;
    private int _selectionLength;
    private int _selectionAnchor;
    private bool _isSelecting;
    private bool _ownsSelectionCursor;
    private bool _isWordSelecting;
    private int _wordSelectionAnchorStart;
    private int _wordSelectionAnchorEnd;
    private bool _isRenderingText;
    private Border? _cssBorderPainter;
    private FlowDocument? _contentPointerDocument;
    private string? _contentPointerText;

    /// <summary>
    /// Identifies the Text dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(TextBlock),
            new PropertyMetadata(string.Empty, OnTextChanged));

    /// <summary>
    /// Identifies the Foreground dependency property.
    /// Shared with Control and TextElement via AddOwner for cross-tree property inheritance.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ForegroundProperty =
        TextElement.ForegroundProperty.AddOwner(typeof(TextBlock),
            new PropertyMetadata(new SolidColorBrush(Themes.ThemeColors.TextPrimary), OnVisualPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the Background dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BackgroundProperty =
        TextElement.BackgroundProperty.AddOwner(typeof(TextBlock),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the BaselineOffset attached dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty BaselineOffsetProperty =
        DependencyProperty.RegisterAttached(nameof(BaselineOffset), typeof(double), typeof(TextBlock),
            new PropertyMetadata(double.NaN, OnBaselineOffsetChanged));

    /// <summary>
    /// Identifies the FontFamily dependency property.
    /// Shared with Control and TextElement via AddOwner.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontFamilyProperty =
        TextElement.FontFamilyProperty.AddOwner(typeof(TextBlock),
            new FrameworkPropertyMetadata(
                SystemFonts.MessageFontFamily,
                FrameworkPropertyMetadataOptions.Inherits,
                OnTextChanged));

    /// <summary>
    /// Identifies the FontSize dependency property.
    /// Shared with Control and TextElement via AddOwner.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontSizeProperty =
        TextElement.FontSizeProperty.AddOwner(typeof(TextBlock),
            new PropertyMetadata(14.0, OnTextChanged, null, inherits: true));

    /// <summary>
    /// Identifies the FontStyle dependency property.
    /// Shared with Control and TextElement via AddOwner.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontStyleProperty =
        TextElement.FontStyleProperty.AddOwner(typeof(TextBlock),
            new PropertyMetadata(FontStyles.Normal, OnTextChanged, null, inherits: true));

    /// <summary>
    /// Identifies the FontWeight dependency property.
    /// Shared with Control and TextElement via AddOwner.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontWeightProperty =
        TextElement.FontWeightProperty.AddOwner(typeof(TextBlock),
            new PropertyMetadata(FontWeights.Normal, OnTextChanged, null, inherits: true));

    /// <summary>
    /// Identifies the FontStretch dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontStretchProperty =
        Control.FontStretchProperty.AddOwner(typeof(TextBlock),
            new PropertyMetadata(FontStretches.Normal, OnTextChanged, null, inherits: true));

    /// <summary>
    /// Identifies the LineHeight attached dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty LineHeightProperty =
        Block.LineHeightProperty.AddOwner(typeof(TextBlock),
            new PropertyMetadata(double.NaN, OnTextChanged));

    /// <summary>
    /// Identifies the LineStackingStrategy attached dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty LineStackingStrategyProperty =
        DependencyProperty.RegisterAttached(nameof(LineStackingStrategy), typeof(LineStackingStrategy), typeof(TextBlock),
            new PropertyMetadata(LineStackingStrategy.MaxHeight, OnTextChanged));

    /// <summary>
    /// Identifies the Padding dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PaddingProperty =
        Block.PaddingProperty.AddOwner(typeof(TextBlock),
            new PropertyMetadata(new Thickness(0), OnTextChanged));

    /// <summary>
    /// Identifies the TextDecorations dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextDecorationsProperty =
        TextElement.TextDecorationsProperty.AddOwner(typeof(TextBlock),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the TextEffects dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextEffectsProperty =
        DependencyProperty.Register(nameof(TextEffects), typeof(TextEffectCollection), typeof(TextBlock),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsHyphenationEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty IsHyphenationEnabledProperty =
        FlowDocument.IsHyphenationEnabledProperty.AddOwner(typeof(TextBlock),
            new PropertyMetadata(false, OnTextChanged));

    /// <summary>
    /// Identifies the TextWrapping dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextWrappingProperty =
        DependencyProperty.Register(nameof(TextWrapping), typeof(TextWrapping), typeof(TextBlock),
            new PropertyMetadata(TextWrapping.NoWrap, OnTextChanged));

    /// <summary>
    /// Identifies the TextAlignment dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextAlignmentProperty =
        DependencyProperty.Register(nameof(TextAlignment), typeof(TextAlignment), typeof(TextBlock),
            new PropertyMetadata(TextAlignment.Left, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the TextTrimming dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextTrimmingProperty =
        DependencyProperty.Register(nameof(TextTrimming), typeof(TextTrimming), typeof(TextBlock),
            new PropertyMetadata(TextTrimming.None, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsTextSelectionEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsTextSelectionEnabledProperty =
        DependencyProperty.Register(nameof(IsTextSelectionEnabled), typeof(bool), typeof(TextBlock),
            new PropertyMetadata(false, OnIsTextSelectionEnabledChanged));

    /// <summary>
    /// Identifies the SelectionBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SelectionBrushProperty =
        DependencyProperty.Register(nameof(SelectionBrush), typeof(Brush), typeof(TextBlock),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the SelectionChanged routed event.
    /// </summary>
    public static readonly RoutedEvent SelectionChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SelectionChanged), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(TextBlock));

    /// <summary>
    /// Initializes a new instance of the <see cref="TextBlock"/> class.
    /// </summary>
    public TextBlock()
    {
        Focusable = true;
        KeyboardNavigation.SetIsTabStop(this, false);

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseEnterEvent, new MouseEventHandler(OnMouseEnterHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnMouseLeaveHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnKeyboardFocusChanged));
        AddHandler(LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnKeyboardFocusChanged));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TextBlock"/> class with an initial inline.
    /// </summary>
    public TextBlock(Inline inline)
        : this()
    {
        ArgumentNullException.ThrowIfNull(inline);
        Inlines.Add(inline);
    }

    /// <summary>
    /// Occurs when the selection changes.
    /// </summary>
    public event RoutedEventHandler SelectionChanged
    {
        add => AddHandler(SelectionChangedEvent, value);
        remove => RemoveHandler(SelectionChangedEvent, value);
    }

    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Text
    {
        get => (string)(GetValue(TextProperty) ?? string.Empty);
        set
        {
            value ??= string.Empty;
            if (string.Equals(Text, value, StringComparison.Ordinal) &&
                !string.Equals(_displayText, value, StringComparison.Ordinal))
            {
                SynchronizeInlinesFromText(value);
                InvalidateTextContent();
            }

            SetValue(TextProperty, value);
        }
    }

    /// <summary>
    /// Gets the inline content displayed by this text block.
    /// </summary>
    public InlineCollection Inlines
    {
        get
        {
            if (_inlines is null)
            {
                var inlines = new InlineCollection(OnInlinesChanged, this);
                _inlines = inlines;
                if (!_inlinesExplicitlyModified && _displayText.Length > 0)
                {
                    _synchronizingTextAndInlines = true;
                    try
                    {
                        inlines.Add(new Run(_displayText));
                    }
                    finally
                    {
                        _synchronizingTextAndInlines = false;
                    }
                }
            }

            return _inlines;
        }
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
    /// Gets or sets the brush used to paint the text block background.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the distance from the top of the text block to its baseline.
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
    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty)!;
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
    /// Gets or sets the font style.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public FontStyle FontStyle
    {
        get => GetValue(FontStyleProperty) is FontStyle fs ? fs : FontStyles.Normal;
        set => SetValue(FontStyleProperty, value);
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
    /// Gets or sets the font stretch.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public FontStretch FontStretch
    {
        get => GetValue(FontStretchProperty) is FontStretch stretch ? stretch : FontStretches.Normal;
        set => SetValue(FontStretchProperty, value);
    }

    /// <summary>
    /// Gets or sets the requested line height. <see cref="double.NaN"/> selects the font's natural line height.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public double LineHeight
    {
        get => (double)GetValue(LineHeightProperty)!;
        set => SetValue(LineHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets how explicit and natural line heights are combined.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public LineStackingStrategy LineStackingStrategy
    {
        get => (LineStackingStrategy)GetValue(LineStackingStrategyProperty)!;
        set => SetValue(LineStackingStrategyProperty, value);
    }

    /// <summary>
    /// Gets or sets the space between the text block edge and its text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty)!;
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>
    /// Gets or sets decorations applied to the displayed text.
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
    /// Gets or sets effects applied to the displayed text.
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
    /// Gets or sets whether the formatter may hyphenate words when wrapping.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public bool IsHyphenationEnabled
    {
        get => (bool)GetValue(IsHyphenationEnabledProperty)!;
        set => SetValue(IsHyphenationEnabledProperty, value);
    }

    /// <summary>
    /// Gets the preferred line break behavior before this element.
    /// </summary>
    public LineBreakCondition BreakBefore => LineBreakCondition.BreakDesired;

    /// <summary>
    /// Gets the preferred line break behavior after this element.
    /// </summary>
    public LineBreakCondition BreakAfter => LineBreakCondition.BreakDesired;

    /// <summary>
    /// Gets instance access to the OpenType typography properties on this element.
    /// </summary>
    public Documents.Typography Typography => new(this);

    /// <summary>Gets the start of the text content hosted by this TextBlock.</summary>
    public TextPointer ContentStart => EnsureContentPointerDocument().ContentStart;

    /// <summary>Gets the end of the text content hosted by this TextBlock.</summary>
    public TextPointer ContentEnd => EnsureContentPointerDocument().ContentEnd;

    /// <summary>Gets the input elements hosted by inline UI containers.</summary>
    protected virtual IEnumerator<IInputElement> HostedElementsCore =>
        EnumerateHostedElements().GetEnumerator();

    /// <summary>
    /// Gets or sets the text wrapping mode.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public TextWrapping TextWrapping
    {
        get => (TextWrapping)GetValue(TextWrappingProperty)!;
        set => SetValue(TextWrappingProperty, value);
    }

    /// <summary>
    /// Gets or sets the text alignment.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty)!;
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets the text trimming mode.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public TextTrimming TextTrimming
    {
        get => (TextTrimming)GetValue(TextTrimmingProperty)!;
        set => SetValue(TextTrimmingProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the text can be selected with the mouse.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsTextSelectionEnabled
    {
        get => (bool)GetValue(IsTextSelectionEnabledProperty)!;
        set => SetValue(IsTextSelectionEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used to paint the selected text background.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? SelectionBrush
    {
        get => (Brush?)GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    public static void SetBaselineOffset(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(BaselineOffsetProperty, value);
    }

    public static double GetBaselineOffset(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)element.GetValue(BaselineOffsetProperty)!;
    }

    public static void SetFontFamily(DependencyObject element, FontFamily value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FontFamilyProperty, value);
    }

    public static FontFamily GetFontFamily(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (FontFamily)element.GetValue(FontFamilyProperty)!;
    }

    public static void SetFontSize(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FontSizeProperty, value);
    }

    public static double GetFontSize(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)element.GetValue(FontSizeProperty)!;
    }

    public static void SetFontStretch(DependencyObject element, FontStretch value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FontStretchProperty, value);
    }

    public static FontStretch GetFontStretch(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(FontStretchProperty) is FontStretch value ? value : FontStretches.Normal;
    }

    public static void SetFontStyle(DependencyObject element, FontStyle value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FontStyleProperty, value);
    }

    public static FontStyle GetFontStyle(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(FontStyleProperty) is FontStyle value ? value : FontStyles.Normal;
    }

    public static void SetFontWeight(DependencyObject element, FontWeight value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FontWeightProperty, value);
    }

    public static FontWeight GetFontWeight(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(FontWeightProperty) is FontWeight value ? value : FontWeights.Normal;
    }

    public static void SetForeground(DependencyObject element, Brush? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(ForegroundProperty, value);
    }

    public static Brush? GetForeground(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (Brush?)element.GetValue(ForegroundProperty);
    }

    public static void SetLineHeight(DependencyObject element, double value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(LineHeightProperty, value);
    }

    public static double GetLineHeight(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (double)element.GetValue(LineHeightProperty)!;
    }

    public static void SetLineStackingStrategy(DependencyObject element, LineStackingStrategy value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(LineStackingStrategyProperty, value);
    }

    public static LineStackingStrategy GetLineStackingStrategy(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (LineStackingStrategy)element.GetValue(LineStackingStrategyProperty)!;
    }

    public static void SetTextAlignment(DependencyObject element, TextAlignment value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TextAlignmentProperty, value);
    }

    public static TextAlignment GetTextAlignment(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (TextAlignment)element.GetValue(TextAlignmentProperty)!;
    }

    /// <summary>
    /// Returns whether a non-NaN local baseline offset should be serialized.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public bool ShouldSerializeBaselineOffset()
    {
        var localValue = ReadLocalValue(BaselineOffsetProperty);
        return localValue is double value && !double.IsNaN(value);
    }

    /// <summary>
    /// Returns whether the simple Text property should be serialized.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public bool ShouldSerializeText()
    {
        var localValue = ReadLocalValue(TextProperty);
        return !_inlinesExplicitlyModified && localValue is string value && value.Length > 0;
    }

    /// <summary>Indicates whether inline content should be serialized.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public bool ShouldSerializeInlines(XamlDesignerSerializationManager manager)
    {
        return _inlinesExplicitlyModified && manager is not null && manager.XmlWriter is null;
    }

    /// <summary>Returns the text position nearest to a point in control coordinates.</summary>
    public TextPointer? GetPositionFromPoint(Point point, bool snapToText)
    {
        if (CssContainmentProperties.HidesContentsFromUserAgent(this)) return null;
        if (!snapToText && !TextContentContains(point))
            return null;

        var document = EnsureContentPointerDocument();
        var index = Math.Clamp(GetCharacterIndexFromPosition(point), 0, _displayText.Length);
        return document.GetPositionAtOffset(index, LogicalDirection.Forward) ?? document.ContentEnd;
    }

    /// <summary>Returns layout rectangles occupied by a hosted content element.</summary>
    protected virtual ReadOnlyCollection<Rect> GetRectanglesCore(ContentElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return Array.AsReadOnly(Array.Empty<Rect>());
    }

    /// <summary>Performs content-host input hit testing.</summary>
    protected virtual IInputElement? InputHitTestCore(Point point)
    {
        return new Rect(RenderSize).Contains(point) ? this : null;
    }

    /// <summary>Responds when a hosted child changes its desired size.</summary>
    protected virtual void OnChildDesiredSizeChangedCore(UIElement child)
    {
        ArgumentNullException.ThrowIfNull(child);
        InvalidateMeasure();
    }

    IEnumerator<IInputElement> IContentHost.HostedElements => HostedElementsCore;

    ReadOnlyCollection<Rect> IContentHost.GetRectangles(ContentElement child) =>
        GetRectanglesCore(child);

    IInputElement IContentHost.InputHitTest(Point point) =>
        InputHitTestCore(point) ?? this;

    void IContentHost.OnChildDesiredSizeChanged(UIElement child) =>
        OnChildDesiredSizeChangedCore(child);

    void IAddChild.AddChild(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        switch (value)
        {
            case Inline inline:
                Inlines.Add(inline);
                break;
            default:
                throw new ArgumentException($"A TextBlock can contain only Inline children, not {value.GetType().FullName}.", nameof(value));
        }
    }

    void IAddChild.AddText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_inlinesExplicitlyModified)
        {
            Inlines.Add(text);
        }
        else
        {
            Text += text;
        }
    }

    object? IServiceProvider.GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return null;
    }

    /// <summary>
    /// Gets the start index of the current selection.
    /// </summary>
    public int SelectionStart => _selectionStart;

    /// <summary>
    /// Gets the length of the current selection.
    /// </summary>
    public int SelectionLength => _selectionLength;

    /// <summary>
    /// Gets the selected text.
    /// </summary>
    public string SelectedText
    {
        get
        {
            var text = _displayText;
            if (_selectionLength == 0 || string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var start = Math.Clamp(_selectionStart, 0, text.Length);
            var length = Math.Clamp(_selectionLength, 0, text.Length - start);
            return text.Substring(start, length);
        }
    }

    /// <summary>
    /// Selects the entire text.
    /// </summary>
    public void SelectAll()
    {
        if (string.IsNullOrEmpty(_displayText))
        {
            return;
        }

        _selectionAnchor = 0;
        ApplySelection(0, _displayText.Length);
    }

    /// <summary>
    /// Selects a specific range of text.
    /// </summary>
    public void Select(int start, int length)
    {
        if (string.IsNullOrEmpty(_displayText))
        {
            ClearSelection();
            return;
        }

        var clampedStart = Math.Clamp(start, 0, _displayText.Length);
        var clampedLength = Math.Clamp(length, 0, _displayText.Length - clampedStart);
        _selectionAnchor = clampedStart;
        ApplySelection(clampedStart, clampedLength);
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    public void ClearSelection()
    {
        ApplySelection(_selectionStart, 0);
    }

    /// <summary>
    /// Copies the current selection to the clipboard.
    /// </summary>
    public void Copy()
    {
        if (!string.IsNullOrEmpty(SelectedText))
        {
            WpfClipboard.SetText(SelectedText);
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        if (CssPlaceholderPainter.MeasureAlias(this, availableSize, out var placeholderSize)) return placeholderSize;
        if (this is CssGeneratedText { IsImageReplacement: true } image) return image.MeasureImageReplacement(availableSize);
        if (this is CssGeneratedText && TryCssInlineAllocation(out var allocation)) return allocation;
        if (_cssTextLayoutActive)
        {
            _cssTextLayoutActive = false;
            InvalidateCaches();
        }
        if (!HasCssInlineBoxes) ClearInlineSourceBoxes();
        if (_cssTextBody is not null) _cssTextBody.Visibility = Visibility.Collapsed;
        return MeasureTextContent(availableSize);
    }

    private Size MeasureTextContent(Size availableSize, bool cssBody = false)
    {
        availableSize = LogicalTextSize(availableSize);
        ClearCssInlineFragments();
        // Explicit invalidation can reflect a changed coercion input; only
        // constraint-only remeasures may reuse the resolved line metrics.
        if (!IsMeasureValid) _cachedLineHeight = null;

        // Negative Padding is legal; the Size constructor is not — clamp both the
        // empty-text fast path and the measured-text sink below.
        var insets = GetTextInsets();
        var horizontalPadding = insets.Left + insets.Right;
        var verticalPadding = insets.Top + insets.Bottom;
        if (string.IsNullOrEmpty(_displayText))
        {
            return PhysicalTextSize(new Size(Math.Max(0, horizontalPadding), Math.Max(0, verticalPadding)));
        }

        EnsureLayout(GetLayoutConstraintWidth(availableSize.Width),
            intrinsic: availableSize.Width == 0 && (UsesCssWrapMode() || CssIntrinsicMeasure || cssBody));
        if (CssLineClampLayout.Limit(this) is { Count: 0 }) return default;

        var maxLineWidth = 0.0;
        var measuredLineHeight = 0.0;
        for (int i = 0; i < _layoutLines.Count; i++)
        {
            var line = _layoutLines[i];
            var indent = ResolveTextIndent(line.IndentApplies, GetContentWidth(availableSize.Width));
            maxLineWidth = Math.Max(maxLineWidth,
                line.Width + Math.Max(0, indent) + line.StartPadding + line.EndPadding);
            measuredLineHeight += line.Height;
        }

        // CSS bodies and document formatting use typographic boxes. The native
        // control's optical ink allowance is paint overflow, not layout space.
        var measurementSlack = CssIntrinsicMeasure || cssBody || IsDocumentBoxContent || maxLineWidth == 0 && measuredLineHeight == 0 ? 0 : 2;
        var measuredWidth = maxLineWidth + horizontalPadding + measurementSlack;
        var measuredHeight = Math.Max(measuredLineHeight, GetLineHeight()) + verticalPadding + measurementSlack;

        if (TextTrimming != TextTrimming.None &&
            EffectiveTextWrapping() == TextWrapping.NoWrap &&
            !double.IsInfinity(availableSize.Width))
        {
            measuredWidth = Math.Min(measuredWidth, availableSize.Width);
        }

        if (!double.IsInfinity(availableSize.Height))
        {
            measuredHeight = Math.Min(measuredHeight, availableSize.Height);
        }

        return PhysicalTextSize(new Size(Math.Max(0, measuredWidth), Math.Max(0, measuredHeight)));
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (CssBackgroundTextClip.Active)
        {
            if (!UsesCssTextBody && this is not CssGeneratedText { HasImageContent: true }) RenderTextContent(drawingContext);
            return;
        }
        if (CssPlaceholderPainter.DrawAlias(this, drawingContext)) return;
        if (_isRenderingText)
        {
            return;
        }

        _isRenderingText = true;
        try
        {
            base.OnRender(drawingContext);

            if (Background != null && !CssInlineBoxPainter.Background(this, BackgroundProperty, Background, drawingContext))
            {
                if (IsCssInlineBox)
                    foreach (var fragment in CssInlineFragmentValues)
                        DrawTextBackground(drawingContext, fragment.Rect, fragment);
                else DrawTextBackground(drawingContext, new Rect(RenderSize));
            }

            if (!UsesCssTextBody && this is not CssGeneratedText { HasImageContent: true }) RenderTextContent(drawingContext);
        }
        finally
        {
            _isRenderingText = false;
        }
    }

    private void RenderTextContent(DrawingContext dc)
    {
        if (HasCssInlineBoxes) return;
        if (string.IsNullOrEmpty(_displayText)) return;
        EnsureLayout(GetLayoutConstraintWidth(TextContentSize.Width));
        DrawInlineBackgrounds(dc);
        dc.PushClip(GetRenderClip());
        try
        {
            if (CssBackgroundTextClip.Active || !DrawCssSelection(dc))
            {
                if (!CssBackgroundTextClip.Active && _selectionLength > 0) DrawSelection(dc);
                if (Foreground is not null || CssBackgroundTextClip.Active) DrawTextLines(dc);
            }
            PaintDocumentSharedMarker(dc);
        }
        finally { dc.Pop(); }
    }

    /// <inheritdoc />
    protected override void OnPostRender(DrawingContext drawingContext)
    {
        base.OnPostRender(drawingContext);
        if (CssInlineBoxPainter.Border(this, drawingContext, ref _cssBorderPainter)) return;
        if (IsCssInlineBox)
            foreach (var fragment in CssInlineFragmentValues)
                CssBorderAdornment.Draw(this, drawingContext, ref _cssBorderPainter, fragment.Rect,
                    borderThickness: InlineFragmentEdges(fragment, CssBoxMetrics.BackgroundInsets(this,
                        CssLayout?.ContainingWidthCache ?? RenderSize.Width).Border),
                    fragmentRadii: InlineFragmentRadii(fragment));
        else CssBorderAdornment.Draw(this, drawingContext, ref _cssBorderPainter);
    }

    /// <inheritdoc />
    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();
        _isSelecting = false;
        _isWordSelecting = false;
    }

    private void DrawTextLines(DrawingContext dc)
    {
        if (CssSelectionPainter.Unwrap(dc) is MacOSWritingToolsPresentation.CssTextContext && DocumentWritingToolsOwner is null) return;
        using var writingToolsInk = MacOSWritingToolsPresentation.BeginCssTextInk(CssSelectionPainter.Unwrap(dc));
        // A zero-sized base font has no glyph ink. Keep inline fragments alive
        // when a Run overrides the size with a nonzero value.
        if (FontSize == 0 && !_usesInlineFontLayout) return;
        var lineHeight = GetLineHeight();
        var renderWidth = GetContentWidth(TextContentSize.Width);
        var verticalOffset = GetVerticalContentOffset(lineHeight);


        // Rebuild formatted text cache only when layout or text properties changed
        if (_formattedLinesCacheDirty || _cachedFormattedLines == null ||
            _cachedFormattedLines.Count != _layoutLines.Count)
        {
            var fontFamily = FontFamily.GetRenderingSource(this);
            var fontSize = FontSize;
            var fontWeight = FontWeight.ToOpenTypeWeight();
            var fontStyle = FontStyle.ToOpenTypeStyle();
            var fontStretch = FontStretch.ToOpenTypeStretch();

            _cachedFormattedLines ??= new List<FormattedText>(_layoutLines.Count);
            _cachedFormattedLines.Clear();

            for (int i = 0; i < _layoutLines.Count; i++)
            {
                var line = _layoutLines[i];
                if (line.Length == 0)
                {
                    _cachedFormattedLines.Add(null!);
                    continue;
                }

                if (_usesInlineFontLayout || _hasWordSpacing || _hasLetterSpacing || _usesCapsShaping || _usesAutospaceShaping ||
                    ContainsPreservedTab(line.StartIndex, line.Length) ||
                    _hasPaintTextChanges || line.SoftHyphenVisible || line.HyphenChange is { HasChanges: true })
                {
                    _cachedFormattedLines.Add(null!);
                    continue;
                }

                var formattedText = line.FormattedText ?? new FormattedText(
                    PaintSlice(line.StartIndex, line.Length),
                    fontFamily,
                    fontSize)
                {
                    Foreground = Foreground,
                    // Each _layoutLines entry is already a final, pre-broken visual
                    // line (wrapping was decided in FindWrapLength). Render it with
                    // no wrapping constraint so DirectWrite can never re-break the
                    // fragment onto a hidden second row that MaxTextHeight = lineHeight
                    // would clip — the symptom behind "viewer with" collapsing to a
                    // clipped first line. Horizontal alignment is applied separately
                    // via GetLineOriginX, so MaxTextWidth only governs wrapping here;
                    // genuine overflow (the rare single-word-too-long fallback) is
                    // clipped at the edge by the OnRender PushClip — visible, not
                    // silently swallowed.
                    MaxTextWidth = double.MaxValue,
                    MaxTextHeight = TextPaintHeight(line),
                    FontWeight = fontWeight,
                    FontStyle = fontStyle,
                    FontStretch = fontStretch,
                    Trimming = EffectiveTextWrapping() == TextWrapping.NoWrap ? TextTrimming : TextTrimming.None
                };
                // Layout already measured this exact fragment. Reuse that object
                // and add only the drawing-specific constraints here.
                formattedText.Foreground = Foreground;
                formattedText.MaxTextWidth = double.MaxValue;
                formattedText.MaxTextHeight = TextPaintHeight(line);
                formattedText.Trimming = EffectiveTextWrapping() == TextWrapping.NoWrap ? TextTrimming : TextTrimming.None;
                // Pull TextOptions.{TextRenderingMode,TextFormattingMode,TextHintingMode}
                // off the TextBlock so the native glyph atlas can honour per-element
                // overrides. Defaults are Auto/Ideal/Auto — same effective behaviour
                // as before for elements that didn't set any of the attached properties.
                formattedText.ApplyTextOptionsFrom(this);
                _cachedFormattedLines.Add(formattedText);
            }
            _formattedLinesCacheDirty = false;
            // 整体重建时新 FormattedText 已经用最新 Foreground 创建，下一帧无需再同步。
            _foregroundCacheNeedsSync = false;
        }
        else if (_foregroundCacheNeedsSync)
        {
            // 仅在 Foreground 真变化后这一帧执行同步——见字段注释。
            // OnVisualPropertyChanged 早就过滤过 Equals(old, new)，进到这里 brush 必然不同；
            // 仍用 ReferenceEquals 二次保护，避免 cache 重建后第一帧多余写。
            var current = Foreground;
            for (int i = 0; i < _cachedFormattedLines.Count; i++)
            {
                var ft = _cachedFormattedLines[i];
                if (ft != null && !ReferenceEquals(ft.Foreground, current))
                {
                    ft.Foreground = current;
                }
            }
            _foregroundCacheNeedsSync = false;
        }

        // Viewport culling: when the context can report its effective clip bounds
        // (e.g. a ScrollViewer viewport, or this element's own RenderSize clip) and
        // the element's drawing offset, skip lines whose vertical band lies entirely
        // outside the clip. CurrentClipBounds is in absolute drawing coordinates, so
        // a line's absolute band is [Offset.Y + lineY, Offset.Y + lineY + line.Height].
        // lineY advances by each preceding line's height. Skipping a long off-screen block (e.g. a 10 000-line TextBlock
        // scrolled to the middle) turns O(lines) DrawText calls into O(visible lines).
        Rect? clipBounds = null;
        var offsetY = 0.0;
        if (!IsVerticalTextLayout && dc is IClipBoundsDrawingContext { CurrentClipBounds: Rect cb } &&
            dc is IOffsetDrawingContext offsetContext)
        {
            clipBounds = cb;
            offsetY = offsetContext.Offset.Y;
        }

        var lineY = verticalOffset;
        var firstDecoratedLine = -1;
        var lastDecoratedLine = -1;
        var ownDecorationInset = CssTextDecorationProperties.Inset(this);
        var accessDecorationInset = VisualParent is AccessText accessText
            ? CssTextDecorationProperties.Inset(accessText) : CssTextDecorationInset.Zero;
        var hasDecorationInset = ownDecorationInset != CssTextDecorationInset.Zero ||
            accessDecorationInset != CssTextDecorationInset.Zero;
        var decorationSizePrefix = hasDecorationInset
            ? new double[_layoutLines.Count + 1] : null;
        if (hasDecorationInset)
        {
            for (var lineIndex = 0; lineIndex < _layoutLines.Count; lineIndex++)
            {
                decorationSizePrefix![lineIndex + 1] = decorationSizePrefix[lineIndex];
                var decorationLine = _layoutLines[lineIndex];
                if (decorationLine.Length > 0)
                {
                    if (firstDecoratedLine < 0) firstDecoratedLine = lineIndex;
                    lastDecoratedLine = lineIndex;
                    decorationSizePrefix[lineIndex + 1] += GetTextDecorationWidth(decorationLine);
                }
            }
        }
        var totalDecorationInlineSize = decorationSizePrefix?[^1] ?? -1;
        var inlineInsetWidths = new Dictionary<TextElement, double[]>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < _cachedFormattedLines.Count; i++)
        {
            var line = _layoutLines[i];
            var ft = _cachedFormattedLines[i];
            var currentY = TextLineTop(line, lineY);
            lineY += line.Height;
            var inlineSizeBefore = decorationSizePrefix?[i] ?? -1;
            var inlineSizeAfter = decorationSizePrefix is null ? -1 : Math.Max(0,
                totalDecorationInlineSize - decorationSizePrefix[i + 1]);
            if (CssClampAdvance(line) == 0 && (line.Length == 0 || !_usesInlineFontLayout && !_hasWordSpacing &&
                !_hasLetterSpacing && !_usesCapsShaping && !_usesAutospaceShaping && !ContainsPreservedTab(line.StartIndex, line.Length) &&
                !_hasPaintTextChanges && !line.SoftHyphenVisible && line.HyphenChange is not { HasChanges: true } && ft == null)) continue;

            if (clipBounds is Rect clip)
            {
                var top = offsetY + currentY - _cssLineInkPadding;
                var bottom = offsetY + currentY + line.Height + _cssLineInkPadding;
                // Cull when the band is fully above or fully below the clip. Touching
                // edges (bottom == clip top, or top == clip bottom) contribute no
                // visible pixels, so they are culled too.
                if (bottom <= clip.Y || top >= clip.Y + clip.Height)
                {
                    continue;
                }
            }

            var lineOriginX = GetLineOriginX(line, renderWidth);
            using var selectionLine = CssSelectionPainter.BeginLine(dc, currentY + line.Baseline);
            using var writingLine = PushTextWritingLine(dc, currentY, line.Height);
            void DrawLineContent()
            {
                if (!DrawCssClampedLine(dc, line, lineOriginX, currentY) && !DrawCssBidiLine(dc,line,lineOriginX,currentY) && !DrawSharedShape(dc, line, lineOriginX, currentY) && !DrawNativeBidiLine(dc, line, lineOriginX, currentY))
                {
                    if (TryGetTrimmedLine(line, renderWidth, out var trimmed))
                        DrawTrimmedLine(dc, line, lineOriginX, currentY, trimmed);
                    else if (line.PaintWrap is not null)
                        DrawPaintWrapLine(dc, line, lineOriginX, currentY);
                    else if (TryGetJustifiedLine(line, renderWidth, out var justified))
                        DrawJustifiedLine(dc, line, lineOriginX, currentY, justified);
                    else if (_usesInlineFontLayout || _hasWordSpacing || _hasLetterSpacing || _usesCapsShaping || _usesAutospaceShaping ||
                             ContainsPreservedTab(line.StartIndex, line.Length) ||
                             _hasPaintTextChanges || line.SoftHyphenVisible || line.HyphenChange is { HasChanges: true })
                        DrawStyledInlineLine(dc, line, lineOriginX, currentY);
                    else if (!DrawInlineForegroundText(dc, ft!, line, lineOriginX, currentY, line.Height))
                        DrawDocumentGlyphs(dc, ft!, TextPaintOrigin(ft!, line, lineOriginX, currentY), line.StartIndex, line.Length);
                }
                DrawTextDecorations(dc, line, lineOriginX, currentY, line.Height,
                    totalDecorationInlineSize,
                    i == firstDecoratedLine, i == lastDecoratedLine,
                    inlineSizeBefore, inlineSizeAfter);
                DrawInlineTextDecorations(dc, line, lineOriginX, currentY, line.Height,
                    i, inlineInsetWidths);
                DrawTextEmphasis(dc,line,lineOriginX,currentY);
            }
            if (TryDrawInlineTextShadows(dc, line, lineOriginX, currentY,
                    DrawLineContent))
                continue;
            var shadowCapture = CssTextShadowPainter.Begin(dc, this,
                new Rect(0, currentY - line.Height, Math.Max(1, TextContentSize.Width),
                    3 * Math.Max(1, line.Height)));
            try
            {
                DrawLineContent();
            }
            finally
            {
                shadowCapture?.End();
            }
        }
    }

    private bool DrawInlineForegroundText(DrawingContext context, FormattedText formattedText,
        in TextLayoutLine line, double originX, double lineY, double lineHeight)
    {
        if (_inlines is null || line.Length == 0) return false;
        var ranges = GetInlineTextRanges();
        var lineEnd = line.StartIndex + line.Length;
        var hasDifferentForeground = false;
        foreach (var range in ranges)
        {
            if (range.End <= line.StartIndex || range.Start >= lineEnd) continue;
            if (!ReferenceEquals(range.Run.Foreground ?? Foreground, Foreground))
            {
                hasDifferentForeground = true;
                break;
            }
        }
        if (!hasDifferentForeground) return false;

        var originalForeground = formattedText.Foreground;
        var cursor = line.StartIndex;
        try
        {
            foreach (var range in ranges)
            {
                var start = Math.Max(range.Start, line.StartIndex);
                var end = Math.Min(range.End, lineEnd);
                if (end <= start) continue;
                if (start > cursor)
                    DrawForegroundRange(context, formattedText, line, originX, lineY,
                        lineHeight, cursor, start, Foreground);
                DrawForegroundRange(context, formattedText, line, originX, lineY,
                    lineHeight, start, end, range.Run.Foreground ?? Foreground);
                cursor = end;
            }
            if (cursor < lineEnd)
                DrawForegroundRange(context, formattedText, line, originX, lineY,
                    lineHeight, cursor, lineEnd, Foreground);
        }
        finally
        {
            formattedText.Foreground = originalForeground;
        }
        return true;
    }

    private void DrawForegroundRange(DrawingContext context, FormattedText formattedText,
        in TextLayoutLine line, double originX, double lineY, double lineHeight,
        int start, int end, Brush? foreground)
    {
        if (foreground is null || end <= start) return;
        var left = GetInlineBoundaryX(line, originX, start);
        var right = GetInlineBoundaryX(line, originX, end);
        if (right <= left) return;

        // Keep the original shaped line so color boundaries do not reflow glyphs.
        // The vertical clip spans ink overhang above and below the line box.
        var inkHeight = UsesCssLineMetrics ? Math.Max(lineHeight, line.GlyphHeight + Math.Abs(line.Baseline)) : lineHeight;
        context.PushClip(new RectangleGeometry(new Rect(left, lineY - inkHeight,
            right - left, inkHeight * 3)));
        try
        {
            formattedText.Foreground = foreground;
            DrawDocumentGlyphs(context, formattedText, TextPaintOrigin(formattedText, line, originX, lineY), line.StartIndex, line.Length);
        }
        finally
        {
            context.Pop();
        }
    }

    private List<InlineTextRange> GetInlineTextRanges()
    {
        if (_inlineTextRanges is not null) return _inlineTextRanges;
        var ranges = new List<InlineTextRange>();
        var offset = 0;
        if (_inlines is not null)
            foreach (var inline in _inlines)
                CollectInlineTextRanges(inline, ref offset, ranges);
        return _inlineTextRanges = ranges;
    }

    private static void CollectInlineTextRanges(Inline inline, ref int offset,
        List<InlineTextRange> ranges)
    {
        switch (inline)
        {
            case Run run:
                var start = offset;
                offset += run.Text.Length;
                if (offset > start && !CssDisplayProperties.SuppressesBoxTree(run)) ranges.Add(new InlineTextRange(run, start, offset));
                break;
            case LineBreak:
                offset++;
                break;
            case Span span:
                foreach (var child in span.Inlines)
                    CollectInlineTextRanges(child, ref offset, ranges);
                break;
        }
    }

    private readonly record struct InlineTextRange(Run Run, int Start, int End);

    private void DrawTextDecorations(
        DrawingContext drawingContext,
        in TextLayoutLine line,
        double lineOriginX,
        double lineY,
        double lineHeight,
        double totalInlineSize,
        bool firstFragment,
        bool lastFragment,
        double inlineSizeBefore,
        double inlineSizeAfter)
    {
        var decorationWidth = GetTextDecorationWidth(line);
        DrawDocumentOwnerDecorations(drawingContext, line, lineOriginX, lineY, decorationWidth,
            totalInlineSize, firstFragment, lastFragment, inlineSizeBefore, inlineSizeAfter);
        UIElement cssOwner = this is InlineSourceText projected ? projected.SourceOwner : this;
        var cssOwnsDecorations = cssOwner.GetEffectiveValueLayer(TextDecorationsProperty) is
            (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState);
        if (!cssOwnsDecorations && cssOwner.VisualParent is AccessText accessText &&
            accessText.GetEffectiveValueLayer(AccessText.TextDecorationsProperty) is
                (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState))
        {
            // AccessText forwards its CSS collection as a local value to the
            // internal TextBlock. Its own CSS longhands still define the paint.
            cssOwner = accessText;
            cssOwnsDecorations = true;
        }
        var decorations = cssOwnsDecorations
            ? null : cssOwner.GetValue(TextDecorationsProperty) as TextDecorationCollection;
        var cssLines = cssOwnsDecorations
            ? CssTextDecorationProperties.Line(cssOwner) : CssTextDecorationLine.None;
        if (decorationWidth <= 0 || decorations is null && cssLines == CssTextDecorationLine.None ||
            decorations is { Count: 0 })
        {
            return;
        }

        var baseline = lineY + line.Baseline;

        using var writingToolsClip = PushDocumentWritingToolsDecorations(drawingContext, line.StartIndex, line.Length);
        if (decorations is null)
        {
            var brush = CssTextDecorationProperties.Color(cssOwner) ?? cssOwner.GetValue(ForegroundProperty) as Brush ?? Foreground;
            if (brush is null) return;
            CssTextDecorationPainter.Draw(drawingContext, cssOwner, this, brush, cssLines,
                lineOriginX, lineOriginX + decorationWidth, lineY, baseline, FontSize,
                lineY + line.GlyphHeight, totalInlineSize, firstFragment, lastFragment,
                inlineSizeBefore, inlineSizeAfter);
            return;
        }

        foreach (var decoration in decorations)
        {
            var brush = decoration.Brush ?? cssOwner.GetValue(ForegroundProperty) as Brush ?? Foreground;
            if (brush == null)
            {
                continue;
            }

            var thickness = decoration.Thickness > 0 ? decoration.Thickness : 1.0;
            var offset = decoration.OffsetUnit == TextDecorationUnit.Pixel
                ? decoration.Offset
                : decoration.Offset * FontSize;
            var y = decoration.Location switch
            {
                TextDecorationLocation.OverLine => lineY + offset,
                TextDecorationLocation.Strikethrough => lineY + (baseline - lineY) * 0.55 + offset,
                TextDecorationLocation.Baseline => baseline + offset,
                _ => baseline + Math.Max(1, FontSize * 0.08) + offset,
            };

            drawingContext.DrawLine(
                new Pen(brush, thickness),
                new Point(lineOriginX, y),
                new Point(lineOriginX + decorationWidth, y));
        }
    }

    private double GetTextDecorationWidth(in TextLayoutLine line)
    {
        var contentWidth = GetContentWidth(TextContentSize.Width);
        if (ContainsPreservedTab(line.StartIndex, line.Length))
        {
            var originX = GetLineOriginX(line, contentWidth);
            return GetInlineBoundaryX(line, originX, line.StartIndex + line.Length) -
                originX;
        }
        var logicalWidth = line.PaintWrap is not null
            ? TryGetPaintWrapJustification(line, contentWidth, out var paintJustified)
                ? paintJustified.FilledWidth : line.Width
            : TryGetJustifiedLine(line, contentWidth, out var justified)
                ? justified.FilledWidth : line.Width;
        return logicalWidth + line.HangingStart + line.HangingEnd;
    }

    private void DrawInlineTextDecorations(DrawingContext context, in TextLayoutLine line,
        double lineOriginX, double lineY, double lineHeight,
        int lineIndex, Dictionary<TextElement, double[]> insetWidthCache)
    {
        if ((_inlines is null && this is not InlineSourceText) || line.Length == 0 ||
            line.Width + line.HangingStart + line.HangingEnd <= 0) return;
        var ranges = GetInlineDecorationRanges();
        if (ranges.Count == 0) return;

        var lineEnd = line.StartIndex + line.Length;
        var baseline = lineY + line.Baseline;
        foreach (var range in ranges)
        {
            var start = Math.Max(range.Start, line.StartIndex);
            var end = Math.Min(range.End, lineEnd);
            if (end <= start) continue;

            using var writingToolsClip = PushDocumentWritingToolsDecorations(context, start, end - start);

            var segments = TryCssBidiLine(line, out var cssBidi)
                ? cssBidi.Selection(start - line.StartIndex, end - start).Select(rect => (Start: lineOriginX + rect.X, End: lineOriginX + rect.X + rect.Width)).ToArray()
                : SharedShape(line) is { } shared
                    ? shared.Selection(start, end - start).Select(rect => (Start: lineOriginX + rect.X, End: lineOriginX + rect.X + rect.Width)).ToArray()
                    : [(Start: GetInlineBoundaryX(line, lineOriginX, start), End: GetInlineBoundaryX(line, lineOriginX, end))];
            foreach (var (startX, endX) in segments)
            {
                if (endX <= startX) continue;

                var sourceFontSize = _usesInlineFontLayout || CssFirstLine.IsProjection(CssNode.Get(range.Source)) ||
                    CssFirstLetter.Origin(range.Source) is not null ? range.Source.FontSize : FontSize;
                var sourceLineTop = lineY;
                if (_usesInlineFontLayout)
                {
                    var sourceMetrics = TextMeasurement.GetFontMetrics(
                        range.Source.FontFamily.GetRenderingSource(range.Source), sourceFontSize,
                        range.Source.FontWeight.ToOpenTypeWeight(),
                        range.Source.FontStyle.ToOpenTypeStyle());
                    sourceLineTop = baseline - (sourceMetrics.Ascent > 0
                        ? sourceMetrics.Ascent : sourceFontSize * 0.8);
                }
                var foreground = range.Source.Foreground ?? Foreground;
                if (range.CssLines != CssTextDecorationLine.None)
                {
                    var brush = CssTextDecorationProperties.Color(range.Source) ?? foreground;
                    if (brush is not null)
                    {
                        var underEdge = (range.CssLines & CssTextDecorationLine.Underline) != 0 &&
                            (CssTextDecorationProperties.UnderlinePosition(range.Source) & CssTextUnderlinePosition.Under) != 0
                                ? GetInlineDecorationUnderEdge(line, start, end, baseline, lineY)
                                : lineY + line.GlyphHeight;
                        var prefix = GetInlineDecorationSizePrefix(range, insetWidthCache);
                        CssTextDecorationPainter.Draw(context, range.Source, this, brush,
                            range.CssLines, startX, endX, sourceLineTop, baseline, sourceFontSize,
                            underEdge, prefix?[^1] ?? -1,
                            start == range.Start, end == range.End,
                            prefix?[lineIndex] ?? -1,
                            prefix is null ? -1 : prefix[^1] - prefix[lineIndex + 1]);
                    }
                    continue;
                }

                foreach (var decoration in range.Native!)
                {
                    var brush = decoration.Brush ?? foreground;
                    if (brush is null) continue;
                    var thickness = decoration.Thickness > 0 ? decoration.Thickness : 1;
                    var offset = decoration.OffsetUnit == TextDecorationUnit.Pixel
                        ? decoration.Offset : decoration.Offset * sourceFontSize;
                    var y = decoration.Location switch
                    {
                        TextDecorationLocation.OverLine => sourceLineTop + offset,
                        TextDecorationLocation.Strikethrough => sourceLineTop + (baseline - sourceLineTop) * 0.55 + offset,
                        TextDecorationLocation.Baseline => baseline + offset,
                        _ => baseline + Math.Max(1, sourceFontSize * 0.08) + offset,
                    };
                    context.DrawLine(new Pen(brush, thickness),
                        new Point(startX, y), new Point(endX, y));
                }
            }
        }
    }

    private double[]? GetInlineDecorationSizePrefix(in InlineDecorationRange range,
        Dictionary<TextElement, double[]> cache)
    {
        if (CssTextDecorationProperties.Inset(range.Source) == CssTextDecorationInset.Zero)
            return null;
        if (cache.TryGetValue(range.Source, out var prefix)) return prefix;
        prefix = new double[_layoutLines.Count + 1];
        for (var index = 0; index < _layoutLines.Count; index++)
        {
            var fragment = _layoutLines[index];
            prefix[index + 1] = prefix[index];
            var start = Math.Max(range.Start, fragment.StartIndex);
            var end = Math.Min(range.End, fragment.StartIndex + fragment.Length);
            if (end <= start) continue;
            prefix[index + 1] += SharedShape(fragment) is { } shared
                ? shared.Selection(start, end - start).Sum(static rect => rect.Width)
                : Math.Max(0, GetInlineBoundaryX(fragment, 0, end) - GetInlineBoundaryX(fragment, 0, start));
        }
        cache[range.Source] = prefix;
        return prefix;
    }

    private double GetInlineBoundaryX(in TextLayoutLine line, double originX, int index)
    {
        if (TryCssBidiLine(line,out var cssBidi)) return originX+cssBidi.Boundary(Math.Clamp(index-line.StartIndex,0,line.Length),index>=line.StartIndex+line.Length);
        if (SharedShape(line) is { } shared) return originX + shared.Boundary(index);
        if (TryNativeBidiLine(line, out var paragraph))
            return originX + paragraph.Caret(0, Math.Clamp(index - line.StartIndex, 0, line.Length), false).X - paragraph.Lines[0].Metrics.Line.X;
        if (index <= line.StartIndex) return originX;
        if (line.PaintWrap is not null)
            return originX + GetPaintWrapBoundaryX(line, index);
        var contentWidth = GetContentWidth(TextContentSize.Width);
        var physicalWidth = line.Width + line.HangingStart + line.HangingEnd;
        if (TryGetJustifiedLine(line, contentWidth, out var justified))
        {
            var justifiedPhysical = justified.FilledWidth +
                line.HangingStart + line.HangingEnd;
            if (index >= justified.VisibleEnd) return originX + justifiedPhysical;
            return originX + Math.Min(justifiedPhysical,
                GetJustifiedBoundaryOffset(justified, index) + WordSpacingBeforeSeparator(index) +
                LetterSpacingAtBoundary(index, line.StartIndex, justified.VisibleEnd));
        }
        if (ContainsPreservedTab(line.StartIndex, line.Length))
        {
            var lineEnd = line.StartIndex + line.Length;
            var boundary = Math.Min(index, lineEnd);
            var width = MeasureInlineRange(line.StartIndex, boundary - line.StartIndex,
                includeTrailingWhitespace: true,
                tabOrigin: InlineFragment(line)?.TabOrigin ?? originX - GetTextInsets().Left);
            if (boundary < lineEnd)
                width += WordSpacingBeforeSeparator(boundary) +
                    LetterSpacingAtBoundary(boundary, line.StartIndex, lineEnd);
            return originX + width;
        }
        if (index >= line.StartIndex + line.Length) return originX + physicalWidth;
        if (line.HyphenChange is { HasChanges: true } || line.SoftHyphenVisible)
        {
            var tabOrigin = InlineFragment(line)?.TabOrigin ??
                (ResolveTextIndent(line.IndentApplies, contentWidth) + GetLineLeftPadding(line));
            return originX + GetHyphenChangeBoundaryOffset(line, index, tabOrigin);
        }
        if (_usesInlineFontLayout || _hasWordSpacing || _hasLetterSpacing || _usesCapsShaping || _usesAutospaceShaping ||
            _hasPaintTextChanges)
            return originX + Math.Min(physicalWidth,
                MeasureInlineRange(line.StartIndex, index - line.StartIndex,
                    includeTrailingWhitespace: true,
                    tabOrigin: ResolveTextIndent(line.IndentApplies, contentWidth) +
                        GetLineLeftPadding(line)) +
                    WordSpacingBeforeSeparator(index) +
                    LetterSpacingAtBoundary(index, line.StartIndex, line.StartIndex + line.Length));
        var prefix = PaintSlice(line.StartIndex, index - line.StartIndex);
        return originX + Math.Min(physicalWidth,
            MeasureText(prefix).WidthIncludingTrailingWhitespace);
    }

    private IReadOnlyList<(double StartX, double EndX, double Baseline)>
        GetLogicalSourceRangeSegments(int start, int length)
    {
        if (DocumentDisplaySuppressed || CssRubyVisibility.Suppressed(this) || CssLineClampLayout.SuppressedInTree(this)) return [];
        if (length <= 0) return [];
        if (HasCssInlineBoxes) return InlineSourceSegments(start,length);
        EnsureLayout(GetLayoutConstraintWidth(TextContentSize.Width));
        var rangeStart = Math.Clamp(start, 0, _visualText.Length);
        var rangeEnd = (int)Math.Clamp((long)start + length, 0, _visualText.Length);
        if (rangeEnd <= rangeStart) return [];

        var segments = new List<(double StartX, double EndX, double Baseline)>();
        var renderWidth = GetContentWidth(TextContentSize.Width);
        var y = GetVerticalContentOffset(GetLineHeight());
        foreach (var line in _layoutLines)
        {
            var visibleEnd = line.StartIndex + line.Length;
            if (TryGetTrimmedLine(line, renderWidth, out var trimmed))
                visibleEnd = Math.Min(visibleEnd, line.StartIndex + trimmed.PrefixLength);
            var selectedStart = Math.Max(rangeStart, line.StartIndex);
            var selectedEnd = Math.Min(rangeEnd, visibleEnd);
            if (selectedEnd > selectedStart && HasDisplayText(selectedStart,selectedEnd-selectedStart))
            {
                var originX = GetLineOriginX(line, renderWidth);
                if (TryCssBidiLine(line,out var cssBidi))
                {
                    foreach(var rect in cssBidi.Selection(selectedStart-line.StartIndex,selectedEnd-selectedStart))
                        segments.Add((originX+rect.X+TextContentOffset.X,originX+rect.X+rect.Width+TextContentOffset.X,TextLineTop(line,y)+line.Baseline+TextContentOffset.Y));
                    y+=line.Height;continue;
                }
                if (SharedShape(line) is { } shared)
                {
                    foreach (var rect in shared.Selection(selectedStart, selectedEnd - selectedStart))
                        segments.Add((originX + rect.X + TextContentOffset.X, originX + rect.X + rect.Width + TextContentOffset.X,
                            TextLineTop(line, y) + line.Baseline + TextContentOffset.Y));
                    y += line.Height; continue;
                }
                if (TryNativeBidiLine(line, out var paragraph))
                {
                    originX -= paragraph.Lines[0].Metrics.Line.X;
                    foreach (var rect in paragraph.Selection(0, selectedStart - line.StartIndex, selectedEnd - selectedStart))
                        segments.Add((originX + rect.X + TextContentOffset.X, originX + rect.X + rect.Width + TextContentOffset.X,
                            TextLineTop(line, y) + line.Baseline + TextContentOffset.Y));
                    y += line.Height;
                    continue;
                }
                var left = GetInlineBoundaryX(line, originX, selectedStart);
                var right = GetInlineBoundaryX(line, originX, selectedEnd);
                if (right > left)
                    segments.Add((left + TextContentOffset.X, right + TextContentOffset.X,
                        TextLineTop(line, y) + line.Baseline + TextContentOffset.Y));
            }
            y += line.Height;
        }
        return HasSharedBidiSourceParts ? JoinSharedBidiSourceSegments(segments) : segments;
    }

    private List<InlineDecorationRange> GetInlineDecorationRanges()
    {
        if (_inlineDecorationRanges is not null) return _inlineDecorationRanges;
        if (this is InlineSourceText projected)
        {
            var borrowed = projected.SourceOwner.GetInlineDecorationRanges()
                .Where(range => range.End > projected.SourceOffset && range.Start < projected.SourceOffset + Text.Length)
                .Select(range => range with { Start = range.Start - projected.SourceOffset, End = range.End - projected.SourceOffset }).ToList();
            var styles = new List<TextElement>();
            for (var node = projected.ProjectedStyleSource is { } style ? CssNode.Get(style) : null;
                 node is not null && (CssFirstLine.IsProjection(node) || CssFirstLetter.Origin(node.Target) is not null);
                 node = node.FrameworkParent)
                if (node.Target is TextElement element &&
                    (CssFirstLine.Kind(element) == CssPseudoElement.FirstLine || CssFirstLetter.Origin(element) is not null)) styles.Add(element);
            if (styles.Any(element => CssFirstLine.Kind(element) == CssPseudoElement.FirstLine) && projected.FirstLineContext is { } firstLine)
                borrowed = borrowed.Select(range => firstLine.Contains(range.Source)
                    ? range with { Source = firstLine.StyleFor(range.Source) } : range).ToList();
            styles.Reverse();
            for (var index = 0; index < styles.Count; index++)
            {
                var element = styles[index];
                var css = element.GetEffectiveValueLayer(TextElement.TextDecorationsProperty) is
                    DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState;
                var lines = css ? CssTextDecorationProperties.Line(element) : CssTextDecorationLine.None;
                var native = css ? null : element.TextDecorations;
                if (lines != CssTextDecorationLine.None || native is { Count: > 0 })
                    borrowed.Add(new(element, 0, Text.Length,
                        (CssFirstLine.Kind(element) == CssPseudoElement.FirstLine ? -1000 : 1000) + index, lines, native));
            }
            return _inlineDecorationRanges = borrowed.OrderBy(range => range.Depth).ThenBy(range => range.Start).ToList();
        }
        var ranges = new List<InlineDecorationRange>();
        var offset = 0;
        if (_inlines is not null)
            foreach (var inline in _inlines)
                CollectInlineDecorationRanges(inline, ref offset, 0, ranges);
        ranges.Sort(static (left, right) =>
        {
            var depth = left.Depth.CompareTo(right.Depth);
            return depth != 0 ? depth : left.Start.CompareTo(right.Start);
        });
        return _inlineDecorationRanges = ranges;
    }

    private static void CollectInlineDecorationRanges(Inline inline, ref int offset,
        int depth, List<InlineDecorationRange> ranges)
    {
        var start = offset;
        switch (inline)
        {
            case Run run:
                offset += run.Text.Length;
                break;
            case LineBreak:
                offset++;
                break;
            case Span span:
                foreach (var child in span.Inlines)
                    CollectInlineDecorationRanges(child, ref offset, depth + 1, ranges);
                break;
        }
        if (offset == start || CssDisplayProperties.SuppressesBoxTree(inline)) return;

        var cssOwnsDecorations = inline.GetEffectiveValueLayer(TextElement.TextDecorationsProperty) is
            (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState);
        var cssLines = cssOwnsDecorations
            ? CssTextDecorationProperties.Line(inline) : CssTextDecorationLine.None;
        var native = cssOwnsDecorations
            ? null : inline.TextDecorations;
        if (cssLines != CssTextDecorationLine.None || native is { Count: > 0 })
            ranges.Add(new InlineDecorationRange(inline, start, offset, depth, cssLines, native));
    }

    private readonly record struct InlineDecorationRange(TextElement Source, int Start,
        int End, int Depth, CssTextDecorationLine CssLines, TextDecorationCollection? Native);

    /// <summary>
    /// Computes the vertical offset that visually centers the rendered glyphs inside the
    /// arranged height. Two-fold concern handled here:
    ///
    /// 1. Extra height from a Stretch parent (e.g. ContentPresenter) or the +2 padding
    ///    from <see cref="MeasureOverride"/> — without compensation that slack lands
    ///    entirely at the bottom and the text looks top-aligned.
    ///
    /// 2. <c>LineHeight</c> includes <c>LineGap</c> (font-designer-recommended space
    ///    BETWEEN lines), but the glyphs only occupy <c>Ascent + Descent</c> at the
    ///    top of the layout box (DirectWrite's default near-paragraph alignment).
    ///    Centering on <c>LineHeight</c> would still leave the glyph block shifted
    ///    upward by <c>LineGap / 2</c> per line — exactly the "Border looks taller
    ///    on the bottom" symptom this method exists to fix.
    ///
    /// We center on the actual glyph extent (<c>Ascent + Descent</c>), which is the
    /// pixel area the user perceives as "the text".
    /// </summary>
    private double GetVerticalContentOffset(double lineHeight)
        => HasCssInlineFragments ? 0 : GetVerticalContentOffset(lineHeight, TextContentSize.Height);

    internal double GetFirstBaselineOffset(double renderHeight) => GetBaselineOffset(renderHeight, last: false);
    internal double GetLastBaselineOffset(double renderHeight) => GetBaselineOffset(renderHeight, last: true);

    private double GetBaselineOffset(double renderHeight, bool last)
    {
        if (HasCssInlineBoxes)
        {
            if (IsCssInlineFrame && CssInlineBox is {} fragments) return last?fragments.LastBaseline:fragments.FirstBaseline;
            if (CssDisplayLayout.IsFlow(this))
            {var flow=CssDisplayLayout.FlowFor(this);return last?flow.LastBaseline:flow.FirstBaseline;}
            if (this is not ICssAnonymousBox && CssDisplayLayout.TryGetBaseline(this,renderHeight,last,out var inlineBaseline)) return inlineBaseline;
        }
        if (!UsesCssTextBody) return GetNativeBaselineOffset(renderHeight, last);
        if (CssDisplayLayout.TryGetBaseline(this, renderHeight, last, out var baseline)) return baseline;
        return TextContentOffset.Y + GetNativeBaselineOffset(TextContentSize.Height, last);
    }

    private double GetNativeBaselineOffset(double renderHeight, bool last)
    {
        if (_layoutLines.Count == 0 || string.IsNullOrEmpty(_displayText)) return double.NaN;
        if (HasCssInlineFragments)
        {
            var line = last ? _layoutLines[^1] : _layoutLines[0];
            return TextLineTop(line, 0) + (TextWritingMode == CssWritingMode.VerticalLr ? line.Height - line.Baseline : line.Baseline);
        }
        var offset = GetVerticalContentOffset(GetLineHeight(), renderHeight);
        if (last)
            for (var i = 0; i < _layoutLines.Count - 1; i++) offset += _layoutLines[i].Height;
        return offset + (last ? _layoutLines[^1].Baseline : _layoutLines[0].Baseline);
    }

    private double GetVerticalContentOffset(double lineHeight, double renderHeight)
    {
        var insets = GetTextInsets();
        if (UsesCssLineMetrics) return insets.Top;
        if (lineHeight <= 0 || _layoutLines.Count == 0)
        {
            return insets.Top;
        }

        var totalVisibleHeight = _layoutLines[^1].GlyphHeight;
        for (var i = 0; i < _layoutLines.Count - 1; i++)
            totalVisibleHeight += _layoutLines[i].Height;
        var slack = Math.Max(0, renderHeight - insets.Top - insets.Bottom) - totalVisibleHeight;
        return insets.Top + (slack > 0 ? slack / 2 : 0);
    }

    private void DrawSelection(DrawingContext dc, int? first = null, int? count = null, Brush? forcedBrush = null)
    {
        if (forcedBrush is null && CssInlineSelectionPainter.Suppressed(this)) return;
        var selectedStart = first ?? _selectionStart; var selectedLength = count ?? _selectionLength;
        var selectionBrush = forcedBrush ?? ResolveSelectionBrush();
        if (selectionBrush == null)
        {
            return;
        }

        var selectionEnd = selectedStart + selectedLength;
        var lineHeight = GetLineHeight();
        var renderWidth = GetContentWidth(TextContentSize.Width);
        var verticalOffset = GetVerticalContentOffset(lineHeight);

        double SelectionWidth(double width) => Math.Max(1,this is InlineSourceText || _inlines is { Count: > 0 } ? width : Math.Round(width));
        void PaintSelection(Rect rect)
        {
            rect = TextWritingRect(rect, rect.Top, rect.Height);
            if (this is InlineSourceText && dc is IOffsetDrawingContext offset)
            {
                var matrix = GetRenderMatrix();
                if (matrix.M11 == 1 && matrix.M22 == 1 && matrix.M12 == 0 && matrix.M21 == 0)
                {
                    var dpi = VisualTreeHelper.GetDpi(VisualTreeHelper.GetRoot(this) as Visual ?? this);
                    var selectionOffsetX = offset.Offset.X; var selectionOffsetY = offset.Offset.Y;
                    var left = Math.Round((rect.Left+selectionOffsetX)*dpi.DpiScaleX)/dpi.DpiScaleX-selectionOffsetX;
                    var right = Math.Round((rect.Right+selectionOffsetX)*dpi.DpiScaleX)/dpi.DpiScaleX-selectionOffsetX;
                    var top = Math.Round((rect.Top+selectionOffsetY)*dpi.DpiScaleY)/dpi.DpiScaleY-selectionOffsetY;
                    var bottom = Math.Round((rect.Bottom+selectionOffsetY)*dpi.DpiScaleY)/dpi.DpiScaleY-selectionOffsetY;
                    rect = new(left,top,Math.Max(1/dpi.DpiScaleX,right-left),Math.Max(0,bottom-top));
                }
            }
            rect = InitialLetterSelectionRect(rect);
            if (rect.Width > 0 && rect.Height > 0) dc.DrawRectangle(selectionBrush,null,rect);
        }


        var y = verticalOffset;
        for (int i = 0; i < _layoutLines.Count; i++)
        {
            var line = _layoutLines[i];
            var lineY = TextLineTop(line, y);
            y += line.Height;
            var lineEnd = line.StartIndex + line.Length;
            var intersectsLine = selectedStart <= lineEnd && selectionEnd >= line.StartIndex;
            if (!intersectsLine)
            {
                continue;
            }

            var lineText = line.Length > 0 ? _displayText.Substring(line.StartIndex, line.Length) : string.Empty;
            var startInLine = Math.Max(0, selectedStart - line.StartIndex);
            var endInLine = Math.Min(line.Length, selectionEnd - line.StartIndex);
            var lineOriginX = GetLineOriginX(line, renderWidth);
            var fragmented = _usesInlineFontLayout || _hasWordSpacing || _hasLetterSpacing || _usesCapsShaping || _usesAutospaceShaping ||
                ContainsPreservedTab(line.StartIndex, line.Length);
            var justified = TryGetJustifiedLine(line, renderWidth, out _);
            if (TryGetTrimmedLine(line, renderWidth, out var trimmed))
            {
                startInLine = Math.Min(startInLine, trimmed.PrefixLength);
                endInLine = Math.Min(endInLine, trimmed.PrefixLength);
                fragmented = trimmed.Fragmented;
            }

            if (endInLine > startInLine)
            {
                if (TryCssBidiLine(line,out var cssBidi))
                {
                    foreach(var rect in cssBidi.Selection(startInLine,endInLine-startInLine))
                        PaintSelection(new Rect(lineOriginX+rect.X,lineY,Math.Max(1,rect.Width),line.Height));
                }
                else if (SharedShape(line) is { } shared)
                {
                    foreach (var rect in shared.Selection(line.StartIndex + startInLine, endInLine - startInLine))
                        PaintSelection(new Rect(lineOriginX + rect.X, lineY, Math.Max(1, rect.Width), line.Height));
                }
                else if (TryNativeBidiLine(line, out var paragraph))
                {
                    var x = lineOriginX - paragraph.Lines[0].Metrics.Line.X;
                    foreach (var rect in paragraph.Selection(0, startInLine, endInLine - startInLine))
                        PaintSelection(new Rect(x + rect.X, lineY, Math.Max(1, rect.Width), line.Height));
                }
                else
                {
                    var textBefore = lineText.Substring(0, startInLine);
                    var selectedText = lineText.Substring(startInLine, endInLine - startInLine);
                    var startX = _inlines is { Count: > 0 } || justified || _hasWordSpacing || _hasLetterSpacing || _usesCapsShaping || _usesAutospaceShaping ||
                        ContainsPreservedTab(line.StartIndex, line.Length) || _hasPaintTextChanges ||
                        line.SoftHyphenVisible || line.HyphenChange is { HasChanges: true }
                        ? GetInlineBoundaryX(line, lineOriginX, line.StartIndex + startInLine)
                        : fragmented
                        ? lineOriginX + MeasureInlineRange(line.StartIndex, startInLine,
                            includeTrailingWhitespace: true, forceFragmented: true)
                        : lineOriginX + MeasureTextWidth(textBefore, includeTrailingWhitespace: true);
                    var width = _inlines is { Count: > 0 } || justified || _hasWordSpacing || _hasLetterSpacing || _usesCapsShaping || _usesAutospaceShaping ||
                        ContainsPreservedTab(line.StartIndex, line.Length) || _hasPaintTextChanges ||
                        line.SoftHyphenVisible || line.HyphenChange is { HasChanges: true }
                        ? SelectionWidth(GetInlineBoundaryX(line, lineOriginX,
                            line.StartIndex + endInLine) - startX)
                        : fragmented
                        ? SelectionWidth(lineOriginX + MeasureInlineRange(line.StartIndex,
                            endInLine, includeTrailingWhitespace: true, forceFragmented: true) - startX)
                        : SelectionWidth(MeasureTextWidth(selectedText,includeTrailingWhitespace:true));

                    PaintSelection(new Rect(startX, lineY, width, line.Height));
                }
            }

            if (selectionEnd > lineEnd && line.HasLineBreakAfter)
            {
                var breakX = GetInlineBoundaryX(line, lineOriginX, lineEnd);
                PaintSelection(new Rect(breakX, lineY,
                    Math.Max(1, FontSize * 0.3), line.Height));
            }
        }
    }

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!CanStartSelection())
        {
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        Focus();
        var index = GetCharacterIndexFromPosition(e.GetPosition(this));

        if (CssUserSelectProperties.Resolve(this) == CssUserSelect.All || e.ClickCount >= 3)
        {
            SelectAll();
            _isWordSelecting = false;
        }
        else if (e.ClickCount == 2)
        {
            SelectWordAt(index);
            _wordSelectionAnchorStart = _selectionStart;
            _wordSelectionAnchorEnd = _selectionStart + _selectionLength;
            _isWordSelecting = _selectionLength > 0;
            _isSelecting = true;
            CaptureMouse();
        }
        else
        {
            CaptureMouse();
            _isSelecting = true;
            _isWordSelecting = false;
            _selectionAnchor = index;
            ApplySelection(index, 0);
        }

        e.Handled = true;
    }

    private void OnMouseEnterHandler(object sender, MouseEventArgs e)
    {
        UpdateHoverCursor();
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        UpdateHoverCursor();

        if (!_isSelecting)
        {
            return;
        }

        var index = GetCharacterIndexFromPosition(e.GetPosition(this));
        if (_isWordSelecting)
        {
            ExtendWordSelection(index);
        }
        else
        {
            UpdateSelectionFromAnchor(index);
        }
        e.Handled = true;
    }

    private void OnMouseLeaveHandler(object sender, MouseEventArgs e)
    {
        ClearSelectionCursor();
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _isSelecting = false;
        _isWordSelecting = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (e.Handled || !IsUserSelectionEnabled())
        {
            return;
        }

        if (e.IsControlDown && e.Key == Key.C && _selectionLength > 0)
        {
            Copy();
            e.Handled = true;
            return;
        }

        if (e.IsControlDown && e.Key == Key.A && !string.IsNullOrEmpty(_displayText))
        {
            SelectAll();
            e.Handled = true;
        }
    }

    private void OnKeyboardFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Keep the current selection when focus moves away, but repaint so the
        // selection highlight stays in sync with the new focus state.
        InvalidateVisual();
    }

    private void UpdateHoverCursor()
    {
        if (_ownsSelectionCursor && Cursor != Jalium.UI.Input.Cursors.IBeam)
            _ownsSelectionCursor = false;
        if (HasLocalValue(CursorProperty) && !_ownsSelectionCursor) return;
        if (CssRuntimeState?.Applied?.ContainsKey(CursorProperty) == true)
        {
            ClearSelectionCursor();
            return;
        }
        if (CanShowTextSelectionCursor())
        {
            Cursor = Jalium.UI.Input.Cursors.IBeam;
            _ownsSelectionCursor = true;
        }
        else
            ClearSelectionCursor();
    }

    private void ClearSelectionCursor()
    {
        if (!_ownsSelectionCursor) return;
        _ownsSelectionCursor = false;
        if (Cursor == Jalium.UI.Input.Cursors.IBeam)
            ClearValue(CursorProperty);
    }

    internal void RefreshCssUserSelect()
    {
        if (_isSelecting && !CanStartSelection())
        {
            _isSelecting = false;
            _isWordSelecting = false;
            ReleaseMouseCapture();
        }
        if (IsMouseOver || _ownsSelectionCursor) UpdateHoverCursor();
        InvalidateVisual();
    }

    private bool IsUserSelectionEnabled() => CssUserSelectProperties.AllowsSelection(this,
        IsTextSelectionEnabled, HasLocalValue(IsTextSelectionEnabledProperty));

    private bool CanStartSelection()
    {
        if (!IsEnabled || !IsUserSelectionEnabled() || string.IsNullOrEmpty(_displayText))
        {
            return false;
        }

        return !IsSelectionBlockedByInteractiveAncestor() || HasLocalValue(IsTextSelectionEnabledProperty) ||
            CssUserSelectProperties.Resolve(this) is { } mode && mode != CssUserSelect.None;
    }

    private bool CanShowTextSelectionCursor()
    {
        return IsEnabled &&
            IsUserSelectionEnabled() &&
            !string.IsNullOrEmpty(_displayText) &&
            (!IsSelectionBlockedByInteractiveAncestor() || HasLocalValue(IsTextSelectionEnabledProperty) ||
             CssUserSelectProperties.Resolve(this) is { } mode && mode != CssUserSelect.None);
    }

    private bool IsSelectionBlockedByInteractiveAncestor()
    {
        for (Visual? current = ParentVisual; current != null; current = current.VisualParent)
        {
            if (current is ButtonBase or MenuFlyoutItem or MenuItem or MenuBarItem or Thumb or ListBoxItem
                or ComboBoxItem or TabItem or TreeViewItem or AutoCompleteBox or DataGrid
                or DataGridColumnHeader or DataGridRowHeader or NavigationViewItem or TitleBar
                or StatusBarItem or ToolBar or Popup or Jalium.UI.Controls.ContextMenu or ScrollBar
                or CalendarButton or CalendarDayButton)
            {
                return true;
            }

            if (current is Control control && control.Focusable && current is not Label)
            {
                return true;
            }
        }

        return false;
    }

    private void SelectWordAt(int index)
    {
        var text = _displayText;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // Walk word boundaries in grapheme-cluster space so the selection edges
        // land on whole user-perceived characters.
        int pos = GraphemeClusters.Snap(text, Math.Clamp(index, 0, text.Length), forward: false);

        int start = pos;
        while (start > 0
               && !IsWordSeparatorAt(text, GraphemeClusters.PreviousBoundary(text, start), includePunctuation: true))
        {
            start = GraphemeClusters.PreviousBoundary(text, start);
        }

        int end = pos;
        while (end < text.Length && !IsWordSeparatorAt(text, end, includePunctuation: true))
        {
            end = GraphemeClusters.NextBoundary(text, end);
        }

        _selectionAnchor = start;
        ApplySelection(start, end - start);
    }

    /// <summary>
    /// Classifies the grapheme cluster beginning at <paramref name="start"/>. A
    /// cluster is a word separator only when it is a single whitespace code
    /// unit, or — with <paramref name="includePunctuation"/> — a single
    /// punctuation code unit. A multi-code-unit cluster (emoji, ZWJ or combining
    /// sequence) is always a word character, so word selection never splits one.
    /// </summary>
    private static bool IsWordSeparatorAt(string text, int start, bool includePunctuation)
    {
        if (start < 0 || start >= text.Length)
            return false;
        if (GraphemeClusters.NextBoundary(text, start) - start != 1)
            return false;
        char c = text[start];
        return char.IsWhiteSpace(c) || (includePunctuation && char.IsPunctuation(c));
    }

    private void ExtendWordSelection(int caretIndex)
    {
        if (string.IsNullOrEmpty(_displayText))
        {
            ApplySelection(0, 0);
            return;
        }

        var (currentWordStart, currentWordEnd) = GetWordRangeAtIndex(caretIndex);
        int selectionStart;
        int selectionEnd;

        if (currentWordEnd <= _wordSelectionAnchorStart)
        {
            selectionStart = currentWordStart;
            selectionEnd = _wordSelectionAnchorEnd;
        }
        else if (currentWordStart >= _wordSelectionAnchorEnd)
        {
            selectionStart = _wordSelectionAnchorStart;
            selectionEnd = currentWordEnd;
        }
        else
        {
            selectionStart = _wordSelectionAnchorStart;
            selectionEnd = _wordSelectionAnchorEnd;
        }

        ApplySelection(selectionStart, Math.Max(0, selectionEnd - selectionStart));
    }

    private (int start, int end) GetWordRangeAtIndex(int index)
    {
        var text = _displayText;
        if (string.IsNullOrEmpty(text))
        {
            return (0, 0);
        }

        int length = text.Length;
        // Operate on grapheme-cluster boundaries so an emoji is one indivisible
        // unit — never half-selected by a double-click.
        int pos = GraphemeClusters.Snap(text, Math.Clamp(index, 0, length), forward: false);

        if (pos == length && pos > 0)
        {
            int prev = GraphemeClusters.PreviousBoundary(text, pos);
            if (!IsWordSeparatorAt(text, prev, includePunctuation: true))
            {
                pos = prev;
            }
        }

        if (pos < length && IsWordSeparatorAt(text, pos, includePunctuation: true))
        {
            int prev = GraphemeClusters.PreviousBoundary(text, pos);
            if (pos > 0 && !IsWordSeparatorAt(text, prev, includePunctuation: true))
            {
                pos = prev;
            }
            else
            {
                while (pos < length && IsWordSeparatorAt(text, pos, includePunctuation: true))
                {
                    pos = GraphemeClusters.NextBoundary(text, pos);
                }

                if (pos >= length)
                {
                    return (length, length);
                }
            }
        }

        int start = pos;
        while (start > 0
               && !IsWordSeparatorAt(text, GraphemeClusters.PreviousBoundary(text, start), includePunctuation: true))
        {
            start = GraphemeClusters.PreviousBoundary(text, start);
        }

        int end = pos;
        while (end < length && !IsWordSeparatorAt(text, end, includePunctuation: true))
        {
            end = GraphemeClusters.NextBoundary(text, end);
        }

        return (start, end);
    }

    private void UpdateSelectionFromAnchor(int caretIndex)
    {
        var start = Math.Min(_selectionAnchor, caretIndex);
        var length = Math.Abs(caretIndex - _selectionAnchor);
        ApplySelection(start, length);
    }

    private void ApplySelection(int start, int length)
    {
        var text = _displayText;
        var textLength = text.Length;
        var rawStart = Math.Clamp(start, 0, textLength);
        var rawLength = Math.Clamp(length, 0, textLength - rawStart);
        // Snap the selection outward onto grapheme-cluster boundaries so an emoji
        // is never partially highlighted.
        var clampedStart = GraphemeClusters.Snap(text, rawStart, forward: false);
        var clampedEnd = rawLength <= 0
            ? clampedStart
            : GraphemeClusters.Snap(text, rawStart + rawLength, forward: true);
        var clampedLength = clampedEnd - clampedStart;

        if (_selectionStart == clampedStart && _selectionLength == clampedLength)
        {
            return;
        }

        _selectionStart = clampedStart;
        _selectionLength = clampedLength;
        foreach(var inlineText in _inlineSourceTexts)inlineText.InvalidateVisual();
        InvalidateVisual();
        RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        RaiseEvent(new RoutedEventArgs(SelectionChangedEvent, this));
    }

    private Brush? ResolveSelectionBrush()
    {
        if(this is InlineSourceText inlineText)return inlineText.SourceOwner.ResolveSelectionBrush();
        if (HasLocalValue(SelectionBrushProperty))
        {
            return SelectionBrush;
        }

        return SelectionBrush
            ?? TryFindResource("SelectionBackground") as Brush
            ?? TryFindResource("AccentFillColorSelectedTextBackgroundBrush") as Brush
            ?? s_defaultSelectionBrush;
    }

    private int GetCharacterIndexFromPosition(Point position)
    {
        if(HasCssInlineBoxes)return InlineSourceIndex(position);
        var contentOffset = TextContentOffset;
        position = new Point(position.X - contentOffset.X, position.Y - contentOffset.Y);
        EnsureLayout(GetLayoutConstraintWidth(TextContentSize.Width));
        position = LogicalTextPoint(position);
        if (TryCssClampMarkerHit(position, out var clampEnd)) return clampEnd;
        if (_layoutLines.Count == 0)
        {
            return 0;
        }

        var verticalOffset = GetVerticalContentOffset(GetLineHeight());
        var adjustedY = position.Y - verticalOffset;
        var lineIndex = 0;
        var lineTop = 0.0;
        while (lineIndex + 1 < _layoutLines.Count &&
            adjustedY >= lineTop + _layoutLines[lineIndex].Height)
        {
            lineTop += _layoutLines[lineIndex].Height;
            lineIndex++;
        }

        if (HasCssInlineFragments)
        {
            var nearest = double.PositiveInfinity;
            for (var i = 0; i < _layoutLines.Count; i++)
            {
                var candidate = _layoutLines[i]; var top = TextLineTop(candidate, 0);
                var dy = position.Y < top ? top - position.Y : Math.Max(0, position.Y - top - candidate.Height);
                var fragment = InlineFragment(candidate);
                var bounds = fragment is null ? (Rect?)null : IsVerticalTextLayout ? fragment.LogicalRect : fragment.Rect;
                var left = bounds?.Left ?? GetLineOriginX(candidate, GetContentWidth(TextContentSize.Width));
                var right = bounds?.Right ?? left + candidate.Width + candidate.StartPadding + candidate.EndPadding;
                var dx = position.X < left ? left - position.X : Math.Max(0, position.X - right);
                var distance = dy * dy + dx * dx;
                if (distance < nearest) { nearest = distance; lineIndex = i; }
            }
        }
        var line = _layoutLines[lineIndex];
        var lineOriginX = GetLineOriginX(line, GetContentWidth(TextContentSize.Width));
        var relativeX = position.X - lineOriginX;
        if (TryCssBidiLine(line,out var cssBidi)) return line.StartIndex+cssBidi.Hit(relativeX);
        if (SharedShape(line) is { } shared) return shared.Hit(relativeX);
        if (TryNativeBidiLine(line, out var nativeParagraph))
            return line.StartIndex + (int)nativeParagraph.HitTest(0, relativeX + nativeParagraph.Lines[0].Metrics.Line.X).TextPosition;
        if (relativeX <= 0 || line.Length == 0)
        {
            return line.StartIndex;
        }

        var visibleLength = line.Length;
        var fragmented = _usesInlineFontLayout;
        if (TryGetTrimmedLine(line, GetContentWidth(TextContentSize.Width), out var trimmed))
        {
            visibleLength = trimmed.PrefixLength;
            fragmented = trimmed.Fragmented;
            if (relativeX >= trimmed.PrefixWidth)
                return line.StartIndex + visibleLength;
        }

        var lineText = _displayText.Substring(line.StartIndex, visibleLength);
        int columnIndex = visibleLength;
        double previousWidth = 0;
        var previousIndex = 0;
        var justified = TryGetJustifiedLine(line, GetContentWidth(TextContentSize.Width), out _);

        // Partial UTF-16 prefixes can reshape an emoji sequence into several
        // wider glyphs. Only complete grapheme advances are meaningful hit
        // boundaries, just as they are for wrapping and source selection.
        foreach (var i in GraphemeClusters.GetBoundaries(lineText))
        {
            var width = justified || _hasWordSpacing || _hasLetterSpacing || _usesCapsShaping || _usesAutospaceShaping ||
                ContainsPreservedTab(line.StartIndex, line.Length) || _hasPaintTextChanges ||
                line.SoftHyphenVisible || line.HyphenChange is { HasChanges: true }
                ? GetInlineBoundaryX(line, lineOriginX, line.StartIndex + i) - lineOriginX
                : fragmented
                ? MeasureInlineRange(line.StartIndex, i,
                    includeTrailingWhitespace: true, forceFragmented: true)
                : MeasureTextWidth(lineText.Substring(0, i), includeTrailingWhitespace: true);
            if (width >= relativeX)
            {
                columnIndex = i;
                if (i > 0 && (relativeX - previousWidth) < (width - relativeX))
                {
                    columnIndex = previousIndex;
                }

                break;
            }

            previousWidth = width;
            previousIndex = i;
        }

        // Snap onto a grapheme-cluster edge so a click never lands inside an emoji.
        return line.StartIndex + GraphemeClusters.SnapNearest(lineText, columnIndex);
    }

    private double GetLineOriginX(in TextLayoutLine line, double renderWidth)
    {
        var initialOffset = CssInitialLetterProperties.Metrics(this)?.InlineOffset ?? 0;
        if (InlineFragment(line) is { } fragment) return fragment.TextX + initialOffset;
        var leftInset = GetTextInsets().Left + initialOffset;
        var indent = ResolveTextIndent(line.IndentApplies, renderWidth);
        var leftPadding = GetLineLeftPadding(line);
        var rightPadding = GetLineRightPadding(line);
        var groupPadding = GetGroupAlignmentPadding(renderWidth);
        var lineBoxWidth = renderWidth - indent - groupPadding;
        var markerAdvance = CssClampAdvance(line);
        var paddedLineWidth = line.Width + leftPadding + rightPadding + markerAdvance;
        var lineDirection = CssLineDirection(line.StartIndex);
        var lineBoxLeft = (lineDirection == FlowDirection.RightToLeft ? 0 : indent) +
            CssFlowProperties.GroupStartPadding(this, groupPadding);
        var hangingLeft = line.HangingStart + NativeBidiOverflow(line) - (UsedTextFlowDirection == FlowDirection.RightToLeft ? markerAdvance : 0);
        var alignment = CssLineTextAlignment(line, isLast: !line.CanJustify);
        if (alignment == TextAlignment.Justify &&
            (_cssRubyJustify || !HasLocalOrAnimatedValue(TextAlignmentProperty)) &&
            !(TryCssBidiLine(line, out var bidiAlignment)
                ? bidiAlignment.Owner.Justification?.Expanded.Contains(line.StartIndex) == true
                : line.PaintWrap is not null
                ? TryGetPaintWrapJustification(line, renderWidth, out _)
                : TryGetJustifiedLine(line, renderWidth, out _)))
        {
            var last = CssFlowProperties.LineAlignment(this, true, TextAlignmentProperty);
            alignment = last == CssFlowTextAlignment.Justify
                ? TextAlignment.Center
                : CssFlowProperties.ToNativeTextAlignment(last,
                    lineDirection == FlowDirection.RightToLeft);
        }
        var free = lineBoxWidth - paddedLineWidth;
        var plaintext = CssUnicodeBidiProperties.Value(this) == CssUnicodeBidi.Plaintext && !CssInlineOuter;
        if (plaintext && free < 0 && !HasLocalOrAnimatedValue(TextAlignmentProperty))
            alignment = lineDirection == FlowDirection.RightToLeft ? TextAlignment.Right : TextAlignment.Left;
        if (alignment == TextAlignment.Center)
        {
            return leftInset + lineBoxLeft + leftPadding +
                Math.Max(0, (lineBoxWidth - paddedLineWidth) / 2) - hangingLeft;
        }

        if (alignment == TextAlignment.Right)
        {
            return leftInset + lineBoxLeft + leftPadding +
                (plaintext && !HasLocalOrAnimatedValue(TextAlignmentProperty) ? free : Math.Max(0, free)) - hangingLeft;
        }

        return leftInset + lineBoxLeft + leftPadding - hangingLeft;
    }

    private double ResolveTextIndent(bool applies, double contentWidth)
    {
        if (!applies) return 0;
        var indent = ((CssTextIndent)GetValue(CssFlowProperties.TextIndentProperty)!).Resolve(contentWidth);
        return double.IsFinite(indent) ? indent : 0;
    }

    internal void InvalidateCssTextLayout() => InvalidateCaches();

    private void EnsureLayout(double constraintWidth, bool intrinsic = false)
    {
        if (_layoutRubyFontArea != CssRubyBoxMetrics.IsFontArea(this))
        {
            ClearCssInlineFragments(); _layoutDirty = true; _cachedLineHeight = null;
        }
        if (CssLineClampLayout.PassStamp != 0 && _cssClampStamp != CssLineClampLayout.PassStamp) _layoutDirty = true;
        if (HasCssInlineFragments) return;
        if (_cssInlineFragments is not null) { ClearCssInlineFragments(); _layoutDirty = true; }
        if (!_layoutDirty &&
            string.Equals(_displayText, _layoutText, StringComparison.Ordinal) &&
            string.Equals(_layoutLanguageTag, Language.IetfLanguageTag, StringComparison.Ordinal) &&
            _layoutIntrinsic == intrinsic &&
            ConstraintWidthUnchanged(_layoutConstraintWidth, constraintWidth))
        {
            return;
        }

        RebuildLayout(constraintWidth, intrinsic);
    }

    /// <summary>
    /// Returns whether two layout constraint widths are effectively the same.
    /// <see cref="GetLayoutConstraintWidth"/> hands back <see cref="double.PositiveInfinity"/>
    /// as the "no horizontal wrap limit" sentinel (NoWrap, or no finite width
    /// available). A plain <c>Math.Abs(a - b) &lt; eps</c> test fails for that case
    /// because <c>∞ - ∞</c> is <c>NaN</c> and <c>NaN &lt; eps</c> is false — which made
    /// EnsureLayout rebuild the layout (and, with the cache-invalidation fix, the
    /// per-line FormattedText cache) on every single render of any NoWrap
    /// TextBlock. Compare infinities by equality and only finite widths by
    /// tolerance.
    /// </summary>
    private static bool ConstraintWidthUnchanged(double previous, double current)
    {
        if (double.IsInfinity(previous) || double.IsInfinity(current))
        {
            return previous.Equals(current);
        }

        return Math.Abs(previous - current) < 0.001;
    }

    private void RebuildLayout(double constraintWidth, bool intrinsic)
    {
        _layoutRubyFontArea = CssRubyBoxMetrics.IsFontArea(this);
        ClearNativeBidiLines();
        _cssLineInkPadding = 0;
        _layoutLines = new List<TextLayoutLine>();
        _groupAlignmentCacheWidth = double.NaN;
        RebuildTextCombine();
        RebuildVisualWhitespace();
        RebuildTextTransform();
        RebuildHyphenation();
        RebuildAutospaceReplacement();
        RebuildLinePadding();
        _hasPreservedTabs = (_whiteSpaceMode is CssWhiteSpaceCollapse.Preserve or
            CssWhiteSpaceCollapse.BreakSpaces) && _visualText.Contains('\t') &&
            HasAuthoredWhiteSpace();
        _usesInlineFontLayout = HasInlineFontOverrides();
        RebuildCapsContext();
        _layoutText = _displayText;
        _layoutConstraintWidth = constraintWidth;
        _layoutIntrinsic = intrinsic;
        _layoutWordBreak = (CssWordBreak)GetValue(CssFlowProperties.WordBreakProperty)!;
        _layoutLineBreak = (CssLineBreak)GetValue(CssFlowProperties.LineBreakProperty)!;
        var languageTag = Language.IetfLanguageTag;
        _layoutLanguageTag = languageTag;
        _layoutUsesCjkWritingSystem = UsesChineseOrJapaneseWritingSystem(languageTag);
        RebuildWordSpacing();
        RebuildLetterSpacing();
        RebuildHangingPunctuation();
        PrepareCssClamp();
        _layoutDirty = false;

        // Recomputing the wrapped lines invalidates the per-line FormattedText
        // cache: each cached entry bakes in its line's text *and* the render
        // width that was current when it was built. A relayout that lands on the
        // same line *count* but different break points (the common width-change
        // case) would otherwise keep the stale fragments — the wider, previously
        // computed line text gets redrawn into the now-narrower box and clipped.
        // Marking the cache dirty here guarantees DrawTextLines rebuilds it from
        // the fresh _layoutLines. The count-mismatch check below is no longer the
        // only safeguard.
        _formattedLinesCacheDirty = true;

        if (string.IsNullOrEmpty(_visualText))
        {
            _layoutLines.Add(CreateTextLayoutLine(0, 0, 0, false));
            return;
        }

        var indent = (CssTextIndent)GetValue(CssFlowProperties.TextIndentProperty)!;
        var index = 0;
        var clampSourceEnd = CssClampSourceEnd;
        if (clampSourceEnd == 0 && CssLineClampLayout.Limit(this) is { Count: > 0 })
            _layoutLines.Add(CreateTextLayoutLine(0, 0, 0, false));
        while (index < clampSourceEnd)
        {
            var lineStart = index;
            while (index < clampSourceEnd && !IsTextLineBreak(_visualText[index]))
            {
                index++;
            }

            var lineLength = index - lineStart;
            var hasLineBreakAfter = false;
            if (index < clampSourceEnd)
            {
                hasLineBreakAfter = true;
                if (_visualText[index] == '\r' && index + 1 < _visualText.Length && _visualText[index + 1] == '\n')
                {
                    index += 2;
                }
                else
                {
                    index++;
                }
            }

            AppendLayoutLines(lineStart, lineLength, hasLineBreakAfter, constraintWidth,
                lineStart == 0, indent, intrinsic);
        }

        if (clampSourceEnd > 0 && IsTextLineBreak(_visualText[clampSourceEnd - 1]))
        {
            _layoutLines.Add(CreateTextLayoutLine(_visualText.Length, 0, 0, false,
                indentApplies: indent.Applies(false, true)));
        }
        ApplyCssClamp(constraintWidth);
    }

    private TextLayoutLine CreateTextLayoutLine(int start, int length, double width,
        bool hasLineBreakAfter, FormattedText? formattedText = null,
        bool canJustify = false, bool indentApplies = false,
        PaintWrapLine? paintWrap = null, bool softHyphenVisible = false,
        double availableWidth = double.PositiveInfinity,
        bool lineStart = true, bool lineEnd = true,
        LineHyphenChange? hyphenChange = null)
    {
        var metrics = GetInlineLineMetrics(start, length);
        var startPadding = lineStart ? GetLineStartPadding(start, start + length) : 0;
        var endPadding = lineEnd ? GetLineEndPadding(start, start + length) : 0;
        var hanging = UsedHangingPunctuation(start, start + length, width,
            availableWidth - startPadding - endPadding, softHyphenVisible);
        hanging = new(lineStart ? hanging.Start : 0, lineEnd ? hanging.End : 0);
        if (CssUnicodeBidiProperties.Value(this) == CssUnicodeBidi.Plaintext &&
            CssLineDirection(start) == FlowDirection.RightToLeft)
            hanging = new(hanging.End, hanging.Start);
        return new TextLayoutLine(start, length,
            Math.Max(0, width - hanging.Start - hanging.End), hasLineBreakAfter,
            metrics.Height, UsesCentralTextBaseline ? metrics.Height / 2 : metrics.Baseline, metrics.GlyphHeight, formattedText,
            canJustify, indentApplies, paintWrap, softHyphenVisible,
            startPadding, endPadding, hanging.Start, hanging.End, hyphenChange);
    }

    private void AppendLayoutLines(int lineStart, int lineLength, bool hasLineBreakAfter,
        double constraintWidth, bool firstLogicalLine, CssTextIndent indent, bool intrinsic)
    {
        var groupStart = _layoutLines.Count;
        AppendLayoutLinesCore(lineStart, lineLength, hasLineBreakAfter,
            constraintWidth, constraintWidth, firstLogicalLine, indent, intrinsic);
        RebalanceLayoutLines(lineStart, lineLength, hasLineBreakAfter,
            constraintWidth, firstLogicalLine, indent, intrinsic, groupStart);
    }

    private void AppendLayoutLinesCore(int lineStart, int lineLength, bool hasLineBreakAfter,
        double wrapWidth, double indentPercentBasis, bool firstLogicalLine,
        CssTextIndent indent, bool intrinsic)
    {
        var firstIndentApplies = indent.Applies(firstLogicalLine, !firstLogicalLine);
        if (lineLength > 0 && WantsPaintExpansionJustification() &&
            TryAppendPaintWrapLines(lineStart, lineLength, hasLineBreakAfter,
                wrapWidth, indentPercentBasis, firstLogicalLine, indent, intrinsic))
            return;
        if (EffectiveTextWrapping() == TextWrapping.NoWrap || double.IsInfinity(wrapWidth) || wrapWidth < 0)
        {
            var lineText = PaintSlice(lineStart, lineLength);
            var measurement = _usesInlineFontLayout ? default : MeasureText(lineText);
            var cssWhitespace = HasAuthoredWhiteSpace();
            var tabOrigin = ResolveTextIndent(firstIndentApplies, indentPercentBasis) +
                GetLineStartPadding(lineStart, lineStart + lineLength);
            var lineWidth = TryMeasureCssBidiRange(lineStart, lineLength, cssWhitespace || _countTrailingWhitespace, out var bidiWidth, tabOrigin) ? bidiWidth
                : _usesInlineFontLayout || _hasWordSpacing || _hasLetterSpacing || _usesCapsShaping || _usesAutospaceShaping || WantsShapedJustification || ContainsPreservedTab(lineStart, lineLength)
                ? MeasureInlineRange(lineStart, lineLength, includeTrailingWhitespace: cssWhitespace || _countTrailingWhitespace, tabOrigin: tabOrigin)
                : cssWhitespace || _countTrailingWhitespace ? measurement.WidthIncludingTrailingWhitespace : measurement.Width;
            lineWidth = UsedCssWhitespaceAdvance(lineStart, lineLength, lineWidth,
                wrapWidth - ResolveTextIndent(firstIndentApplies, indentPercentBasis), true, tabOrigin);
            _layoutLines.Add(CreateTextLayoutLine(
                lineStart,
                lineLength,
                lineWidth,
                hasLineBreakAfter,
                measurement.FormattedText,
                indentApplies: firstIndentApplies,
                availableWidth: wrapWidth - ResolveTextIndent(firstIndentApplies,
                    indentPercentBasis)));
            return;
        }

        if (lineLength == 0)
        {
            _layoutLines.Add(CreateTextLayoutLine(lineStart, 0, 0, hasLineBreakAfter,
                indentApplies: firstIndentApplies));
            return;
        }

        if ((_layoutLineBreak == CssLineBreak.Anywhere ||
             _layoutWordBreak == CssWordBreak.BreakAll || AllowsEmergencyWrap(intrinsic)) &&
            TryAppendPaintWrapLines(lineStart, lineLength, hasLineBreakAfter,
                wrapWidth, indentPercentBasis, firstLogicalLine, indent, intrinsic))
            return;

        var consumed = 0;
        var consecutiveHyphenatedLines = 0;
        var prevHyphenBreakIndex = -1;
        var hyphenatedLineLimit = ((CssHyphenateLimitLines)GetValue(
            CssFlowProperties.HyphenateLimitLinesProperty)!).Maximum;
        var avoidHyphenOnLastFullLine = !hasLineBreakAfter &&
            (CssHyphenateLimitLast)GetValue(CssFlowProperties.HyphenateLimitLastProperty)! ==
            CssHyphenateLimitLast.Always;
        while (consumed < lineLength)
        {
            var remaining = lineLength - consumed;
            var currentStart = lineStart + consumed;
            var indentApplies = indent.Applies(firstLogicalLine && consumed == 0,
                !firstLogicalLine && consumed == 0);
            var currentIndent = ResolveTextIndent(indentApplies, indentPercentBasis);
            var tabOrigin = currentIndent + GetLineStartPadding(currentStart,
                currentStart + remaining);
            var currentWidth = wrapWidth - currentIndent;
            var allowSoftHyphen = hyphenatedLineLimit is null ||
                consecutiveHyphenatedLines < hyphenatedLineLimit.Value;
            _activeLeadHyphenBreakIndex = prevHyphenBreakIndex;
            var currentLength = FindWrapLength(currentStart, remaining, currentWidth, intrinsic,
                tabOrigin, allowSoftHyphen, indentPercentBasis);
            if (avoidHyphenOnLastFullLine && allowSoftHyphen &&
                currentLength > 0 && currentLength < remaining &&
                IsAllowedSoftHyphen(currentStart + currentLength - 1))
            {
                // If the remainder would occupy one final line without another
                // hyphenation break, this line is the last full line of the element.
                var nextStart = currentStart + currentLength;
                var nextLength = remaining - currentLength;
                var nextIndent = ResolveTextIndent(indent.Applies(false, false),
                    indentPercentBasis);
                _activeLeadHyphenBreakIndex = currentStart + currentLength - 1;
                if (FindWrapLength(nextStart, nextLength, wrapWidth - nextIndent,
                        intrinsic, nextIndent + GetLineStartPadding(nextStart,
                            nextStart + nextLength), allowSoftHyphen: false,
                        lineBoxWidth: indentPercentBasis) >= nextLength)
                {
                    allowSoftHyphen = false;
                    _activeLeadHyphenBreakIndex = prevHyphenBreakIndex;
                    currentLength = FindWrapLength(currentStart, remaining, currentWidth,
                        intrinsic, tabOrigin, allowSoftHyphen, indentPercentBasis);
                }
            }
            _activeLeadHyphenBreakIndex = -1;
            if (currentLength <= 0)
            {
                currentLength = 1;
            }

            var currentText = PaintSlice(currentStart, currentLength);
            var isLastFragment = consumed + currentLength >= lineLength;
            var softHyphenVisible = allowSoftHyphen && !isLastFragment &&
                IsAllowedSoftHyphen(currentStart + currentLength - 1);
            var hyphenChange = ResolveLineHyphenChange(currentStart, currentLength,
                prevHyphenBreakIndex, softHyphenVisible);
            var cssWhitespace = HasAuthoredWhiteSpace();
            double measuredWidth;
            FormattedText? formattedText = null;
            if (hyphenChange is { HasChanges: true } || softHyphenVisible)
            {
                measuredWidth = MeasureLineWithHyphenChange(currentStart, currentLength,
                    hyphenChange, softHyphenVisible, tabOrigin,
                    includeTrailingWhitespace: cssWhitespace || _countTrailingWhitespace);
                measuredWidth = UsedCssWhitespaceAdvance(currentStart, currentLength,
                    measuredWidth, currentWidth, isLastFragment, tabOrigin);
            }
            else
            {
                var measurement = _usesInlineFontLayout ? default : MeasureText(currentText);
                formattedText = measurement.FormattedText;
                measuredWidth = TryMeasureCssBidiRange(currentStart, currentLength, cssWhitespace || _countTrailingWhitespace, out var bidiWidth, tabOrigin) ? bidiWidth
                    : _usesInlineFontLayout || _hasWordSpacing || _hasLetterSpacing || _usesCapsShaping || _usesAutospaceShaping || WantsShapedJustification ||
                    ContainsPreservedTab(currentStart, currentLength)
                        ? MeasureInlineRange(currentStart, currentLength, includeTrailingWhitespace: cssWhitespace || _countTrailingWhitespace, tabOrigin: tabOrigin)
                        : cssWhitespace || _countTrailingWhitespace ? measurement.WidthIncludingTrailingWhitespace : measurement.Width;
                measuredWidth = UsedCssWhitespaceAdvance(currentStart, currentLength, measuredWidth, currentWidth, isLastFragment, tabOrigin);
                measuredWidth = Math.Max(0, measuredWidth -
                    EdgeAutospaceReplacementWidth(currentStart, currentStart + currentLength));
            }
            _layoutLines.Add(CreateTextLayoutLine(
                currentStart,
                currentLength,
                measuredWidth,
                isLastFragment && hasLineBreakAfter,
                formattedText,
                canJustify: !isLastFragment,
                indentApplies: indentApplies,
                softHyphenVisible: softHyphenVisible,
                availableWidth: currentWidth,
                hyphenChange: hyphenChange));

            prevHyphenBreakIndex = softHyphenVisible ? currentStart + currentLength - 1 : -1;
            consecutiveHyphenatedLines = softHyphenVisible ? consecutiveHyphenatedLines + 1 : 0;
            consumed += currentLength;
        }
    }

    /// <summary>
    /// Finds the logical length of the next visual line. Width fitting is done
    /// only at grapheme boundaries; word-boundary selection then decides whether
    /// to use a standard break, an emergency break, or an overflowing token.
    /// </summary>
    private int FindWrapLength(int startIndex, int maxLength, double availableWidth,
        bool intrinsic, double tabOrigin = 0, bool allowSoftHyphen = true,
        double lineBoxWidth = double.NaN)
    {
        if (maxLength <= 0)
        {
            return 0;
        }

        if (intrinsic && HasAuthoredWhiteSpace() && _whiteSpaceMode == CssWhiteSpaceCollapse.Preserve)
        {
            var afterSpaces = startIndex;
            while (afterSpaces < startIndex + maxLength && CombinedUnitAt(afterSpaces) is null && _visualText[afterSpaces] is ' ' or '\t') afterSpaces++;
            if (afterSpaces > startIndex && afterSpaces < startIndex + maxLength) return afterSpaces - startIndex;
        }

        var startPadding = GetLineStartPadding(startIndex, startIndex + maxLength);
        var fittingLength = FindLargestFittingGraphemeLength(startIndex, maxLength,
            availableWidth, tabOrigin, startPadding);
        if (fittingLength >= maxLength)
        {
            return maxLength;
        }

        if (_layoutLineBreak == CssLineBreak.Anywhere)
        {
            // These opportunities have equal priority, including beside NBSP,
            // punctuation, and within words. Always take the longest fitting
            // sequence of complete grapheme clusters.
            return fittingLength > 0
                ? fittingLength
                : NextTextLayoutBoundary(startIndex) - startIndex;
        }

        var paragraphEnd = startIndex + maxLength;
        var fittingEnd = startIndex + fittingLength;
        var whitespaceLength = 0;
        var whitespaceContentLength = 0;
        if (_whiteSpaceMode != CssWhiteSpaceCollapse.BreakSpaces)
            _ = TryFindWhitespaceBreak(
                startIndex,
                fittingEnd,
                paragraphEnd,
                out whitespaceLength,
                out whitespaceContentLength);
        var standardLength = FindStandardBreakAtOrBefore(startIndex, fittingEnd, paragraphEnd,
            availableWidth, tabOrigin, startPadding, allowSoftHyphen);

        if (standardLength > 0 && IsAllowedSoftHyphen(startIndex + standardLength - 1))
        {
            var zone = ((CssLayoutLength)GetValue(CssFlowProperties.HyphenateLimitZoneProperty)!)
                .Resolve(double.IsNaN(lineBoxWidth) ? availableWidth + tabOrigin : lineBoxWidth, 0);
            if (zone > 0)
            {
                var plainStandardLength = FindStandardBreakAtOrBefore(startIndex, fittingEnd,
                    paragraphEnd, availableWidth, tabOrigin, startPadding,
                    allowSoftHyphen: false);
                var plainContentLength = Math.Max(whitespaceContentLength, plainStandardLength);
                if (plainContentLength > 0 &&
                    availableWidth - startPadding -
                    GetLineEndPadding(startIndex, startIndex + plainContentLength) -
                    MeasureInlineRange(startIndex, plainContentLength,
                        tabOrigin: tabOrigin) <= zone)
                    standardLength = plainStandardLength;
            }
        }

        if (whitespaceLength > 0 || standardLength > 0)
        {
            // Compare the visible boundary, not the consumed whitespace tail.
            // This lets a later CJK/hyphen break win over an earlier space while
            // still consuming all spaces when the whitespace break is selected.
            return standardLength > whitespaceContentLength
                ? standardLength
                : whitespaceLength;
        }

        if (!intrinsic && _layoutWordBreak == CssWordBreak.KeepAll &&
            (CssOverflowWrap)GetValue(CssFlowProperties.OverflowWrapProperty)! == CssOverflowWrap.Normal)
        {
            var relaxedLength = FindStandardBreakAtOrBefore(startIndex, fittingEnd, paragraphEnd,
                availableWidth, tabOrigin, startPadding, allowSoftHyphen,
                ignoreKeepAll: true);
            if (relaxedLength > 0) return relaxedLength;
        }

        if (AllowsEmergencyWrap(intrinsic))
        {
            // Emergency wrapping must always make progress, but never split a
            // surrogate pair, combining sequence, emoji modifier, or ZWJ emoji.
            return fittingLength > 0
                ? fittingLength
                : NextTextLayoutBoundary(startIndex) - startIndex;
        }

        // WrapWithOverflow preserves an unbreakable token as one line. Search
        // forward for its next legal boundary; if there is none, consume the
        // remainder and allow that line to exceed the constraint.
        return FindNextStandardBreak(startIndex, startIndex, paragraphEnd, allowSoftHyphen);
    }

    private int FindLargestFittingGraphemeLength(
        int startIndex,
        int maxLength,
        double availableWidth,
        double tabOrigin,
        double startPadding)
    {
        var boundaries = TextLayoutBoundaries();
        var paragraphEnd = startIndex + maxLength;
        var startBoundaryIndex = Array.BinarySearch(boundaries, startIndex);
        if (startBoundaryIndex < 0)
        {
            startBoundaryIndex = ~startBoundaryIndex;
        }

        var endBoundaryIndex = Array.BinarySearch(boundaries, paragraphEnd);
        if (endBoundaryIndex < 0)
        {
            endBoundaryIndex = ~endBoundaryIndex - 1;
        }

        var bestBoundaryIndex = startBoundaryIndex;
        var variableEndPadding = HasLinePaddingChange(startIndex, paragraphEnd);
        for (var groupStart = startBoundaryIndex + 1; groupStart <= endBoundaryIndex;)
        {
            var endPadding = GetLineEndPadding(startIndex, boundaries[groupStart]);
            var groupEnd = groupStart;
            if (variableEndPadding)
            {
                while (groupEnd < endBoundaryIndex &&
                       GetLineEndPadding(startIndex, boundaries[groupEnd + 1]) == endPadding)
                    groupEnd++;
            }
            else groupEnd = endBoundaryIndex;

            var low = groupStart;
            var high = groupEnd;
            while (low <= high)
            {
                var midpoint = low + (high - low) / 2;
                var candidateLength = boundaries[midpoint] - startIndex;
                var measured = MeasureCandidatePrefixWidth(startIndex, candidateLength, tabOrigin);
                if (HangingFitWidth(startIndex, startIndex + candidateLength, measured) +
                    startPadding + endPadding <= availableWidth)
                {
                    bestBoundaryIndex = Math.Max(bestBoundaryIndex, midpoint);
                    low = midpoint + 1;
                }
                else high = midpoint - 1;
            }
            groupStart = groupEnd + 1;
        }

        return boundaries[bestBoundaryIndex] - startIndex;
    }

    private bool TryFindWhitespaceBreak(
        int lineStart,
        int fittingEnd,
        int paragraphEnd,
        out int consumedLength,
        out int contentLength)
    {
        consumedLength = 0;
        contentLength = 0;
        if (lineStart >= paragraphEnd)
        {
            return false;
        }

        // Include the character immediately after the fitting prefix. A word
        // that fits must remain on this line even when its following separator
        // advance does not fit (the regression visible after "spacing,").
        var searchIndex = Math.Min(fittingEnd, paragraphEnd - 1);
        while (searchIndex >= lineStart && (!IsBreakableWhitespace(_visualText[searchIndex]) || CombinedUnitAt(searchIndex) is not null))
        {
            searchIndex--;
        }

        if (searchIndex < lineStart)
        {
            return false;
        }

        var whitespaceStart = searchIndex;
        while (whitespaceStart > lineStart && IsBreakableWhitespace(_visualText[whitespaceStart - 1]))
        {
            whitespaceStart--;
        }

        // Leading indentation is content, not a blank break opportunity.
        if (whitespaceStart == lineStart)
        {
            return false;
        }

        var whitespaceEnd = searchIndex + 1;
        while (whitespaceEnd < paragraphEnd && IsBreakableWhitespace(_visualText[whitespaceEnd]))
        {
            whitespaceEnd++;
        }

        consumedLength = whitespaceEnd - lineStart;
        contentLength = whitespaceStart - lineStart;
        return true;
    }

    private int FindStandardBreakAtOrBefore(int lineStart, int fittingEnd, int paragraphEnd,
        double availableWidth, double tabOrigin, double startPadding, bool allowSoftHyphen,
        bool ignoreKeepAll = false)
    {
        var boundary = fittingEnd;
        if (allowSoftHyphen && _hyphenBreaks is { Count: > 0 } && boundary < paragraphEnd)
        {
            for (var extra = 0; extra < 2 && boundary < paragraphEnd; extra++)
            {
                var next = NextTextLayoutBoundary(boundary);
                if (next <= boundary || next >= paragraphEnd) break;
                boundary = next;
            }
        }
        while (boundary > lineStart)
        {
            var isSoftHyphen = IsAllowedSoftHyphen(boundary - 1);
            if ((boundary <= fittingEnd || isSoftHyphen) &&
                (allowSoftHyphen || !isSoftHyphen) &&
                IsStandardLineBreakOpportunity(boundary, lineStart, paragraphEnd, ignoreKeepAll) &&
                (!isSoftHyphen ||
                 MeasureHyphenatedCandidateWidth(lineStart, boundary, tabOrigin) + startPadding +
                 GetLineEndPadding(lineStart, boundary) <= availableWidth))
            {
                return boundary - lineStart;
            }
            boundary = PreviousTextLayoutBoundary(boundary);
        }
        return 0;
    }

    private int FindNextStandardBreak(int lineStart, int searchStart, int paragraphEnd,
        bool allowSoftHyphen)
    {
        var boundary = Math.Max(lineStart, searchStart);
        while (boundary < paragraphEnd)
        {
            if (CombinedUnitAt(boundary) is null && IsBreakableWhitespace(_visualText[boundary]))
            {
                if (_whiteSpaceMode == CssWhiteSpaceCollapse.BreakSpaces)
                    return boundary + 1 - lineStart;
                var whitespaceEnd = boundary + 1;
                while (whitespaceEnd < paragraphEnd && IsBreakableWhitespace(_visualText[whitespaceEnd]))
                {
                    whitespaceEnd++;
                }
                return whitespaceEnd - lineStart;
            }

            if (boundary > lineStart &&
                (allowSoftHyphen || !IsAllowedSoftHyphen(boundary - 1)) &&
                IsStandardLineBreakOpportunity(boundary, lineStart, paragraphEnd))
            {
                return boundary - lineStart;
            }

            var next = NextTextLayoutBoundary(boundary);
            if (next <= boundary)
            {
                break;
            }
            boundary = next;
        }
        return paragraphEnd - lineStart;
    }

    private bool IsStandardLineBreakOpportunity(int boundary, int lineStart, int paragraphEnd,
        bool ignoreKeepAll = false)
    {
        if (boundary <= lineStart || boundary >= paragraphEnd || IsCombinedTextInterior(boundary))
        {
            return false;
        }

        var previousStart = GraphemeClusters.PreviousBoundary(_visualText, boundary);
        var nextStart = boundary;
        if (_displayHiddenUnits is { } hidden)
        {
            while (previousStart >= lineStart && hidden[previousStart] && _visualText[previousStart] != '\u00AD') previousStart--;
            while (nextStart < paragraphEnd && hidden[nextStart]) nextStart++;
            if (previousStart < lineStart || nextStart >= paragraphEnd) return false;
            if (previousStart > 0 && char.IsLowSurrogate(_visualText[previousStart])) previousStart--;
        }
        if (_autospaceReplacementWidths.TryGetValue(previousStart, out var replaced))
        {
            previousStart = replaced.LeftStart;
            nextStart = replaced.RightStart;
        }
        if (!Rune.TryGetRuneAt(_visualText, previousStart, out var previous) ||
            !Rune.TryGetRuneAt(_visualText, nextStart, out var next))
        {
            return false;
        }
        if (_textTransformClassification?.TryGetValue(previousStart, out var classifiedPrevious) == true)
            previous = classifiedPrevious;
        if (_textTransformClassification?.TryGetValue(nextStart, out var classifiedNext) == true)
            next = classifiedNext;

        if (previous.Value is 0x00A0 or 0x202F or 0x2060 or 0xFEFF ||
            next.Value is 0x00A0 or 0x202F or 0x2060 or 0xFEFF)
        {
            return false;
        }

        if (FrenchPunctuationSpace(previous, next) != '\0')
        {
            var ranges = GetInlineTextRanges();
            var owner = AutospaceOwner(InlineRunAt(ranges, previousStart),
                InlineRunAt(ranges, nextStart));
            if (((CssTextAutospace)owner.GetValue(CssFlowProperties.TextAutospaceProperty)! &
                    CssTextAutospace.Punctuation) != 0 && UsesFrenchPunctuation(owner))
                return false;
        }

        if (previous.Value == 0x00AD)
            return IsAllowedSoftHyphen(previousStart);

        if (IsAllowedSoftHyphen(boundary - 1) || IsAllowedSoftHyphen(previousStart))
            return true;

        if (_whiteSpaceMode == CssWhiteSpaceCollapse.BreakSpaces &&
            (previous.Value == '\t' ||
             Rune.GetUnicodeCategory(previous) == UnicodeCategory.SpaceSeparator))
            return true;

        if (_layoutLineBreak is not CssLineBreak.Auto and not CssLineBreak.Anywhere)
        {
            var tailored = IsTailoredLineBreakOpportunity(previous, next);
            if (tailored is { } allowed)
                return allowed && (ignoreKeepAll || _layoutWordBreak != CssWordBreak.KeepAll ||
                    !Rune.IsLetterOrDigit(previous) || !Rune.IsLetterOrDigit(next));
        }

        if (previous.Value is '-' or 0x058A or 0x05BE or 0x1400 or 0x1806 or
            0x2010 or 0x2012 or 0x2013 or 0x2E17 or 0x2E1A or 0x2E40 or 0x301C or 0x30A0 or 0x200B)
        {
            return true;
        }

        if (_layoutWordBreak == CssWordBreak.BreakAll &&
            Rune.IsLetterOrDigit(previous) && Rune.IsLetterOrDigit(next))
            return true;

        if (!ignoreKeepAll && _layoutWordBreak == CssWordBreak.KeepAll &&
            Rune.IsLetterOrDigit(previous) && Rune.IsLetterOrDigit(next))
            return false;

        return (IsCjkLineBreakRune(previous) || IsCjkLineBreakRune(next)) &&
               !IsProhibitedLineEnd(previous) &&
               !IsProhibitedLineStart(next);
    }

    private bool? IsTailoredLineBreakOpportunity(Rune previous, Rune next)
    {
        // CSS Text 3 defines these minimum distinctions. The wider Unicode
        // line-break algorithm still supplies opportunities outside this subset.
        if (next.Value is 0x301C or 0x30A0)
            return _layoutLineBreak != CssLineBreak.Strict && _layoutUsesCjkWritingSystem &&
                   !IsProhibitedLineEnd(previous);

        if (next.Value is 0x2010 or 0x2013)
            return _layoutLineBreak == CssLineBreak.Loose &&
                   (CssUnicodeLineBreakData.IsIdeographic(previous) ||
                    _layoutWordBreak == CssWordBreak.BreakAll && Rune.IsLetterOrDigit(previous));

        if (CssUnicodeLineBreakData.IsConditionalJapanese(next) || IsIterationMark(next))
            return _layoutLineBreak == CssLineBreak.Loose &&
                   !IsProhibitedLineEnd(previous);

        if (CssUnicodeLineBreakData.IsInseparable(next))
            return _layoutLineBreak == CssLineBreak.Loose &&
                   !IsProhibitedLineEnd(previous);

        if (IsCenteredCjkPunctuation(next))
            return _layoutLineBreak == CssLineBreak.Loose && _layoutUsesCjkWritingSystem &&
                   !IsProhibitedLineEnd(previous);

        if (CssUnicodeLineBreakData.IsWidePostfix(next))
            return _layoutLineBreak == CssLineBreak.Loose && _layoutUsesCjkWritingSystem &&
                   !IsProhibitedLineEnd(previous);

        if (CssUnicodeLineBreakData.IsWidePrefix(previous))
            return _layoutLineBreak == CssLineBreak.Loose && _layoutUsesCjkWritingSystem &&
                   !IsProhibitedLineStart(next);

        return null;
    }

    private static bool UsesChineseOrJapaneseWritingSystem(string tag)
    {
        var subtags = tag.Split('-');
        if (subtags.Length > 1 && subtags[1].Length == 4)
            return subtags[1] is "hant" or "hans" or "hani" or "hanb" or "bopo" or
                "jpan" or "hrkt" or "hira" or "kana";
        return subtags[0] is "zh" or "ja";
    }

    private static bool IsIterationMark(Rune rune) => rune.Value is
        0x3005 or 0x303B or 0x309D or 0x309E or 0x30FD or 0x30FE;

    private static bool IsCenteredCjkPunctuation(Rune rune) => rune.Value is
        0x30FB or 0xFF1A or 0xFF1B or 0xFF65 or 0x203C or
        >= 0x2047 and <= 0x2049 or 0xFF01 or 0xFF1F;

    private static bool IsBreakableWhitespace(char value)
    {
        return value is not '\r' and not '\n' and not '\u00A0' and not '\u202F' and not '\u2060' and not '\uFEFF' &&
               char.IsWhiteSpace(value);
    }

    private static bool IsCjkLineBreakRune(Rune rune)
    {
        var value = rune.Value;
        return value is >= 0x2E80 and <= 0x2FFF or
               >= 0x3000 and <= 0x30FF or
               >= 0x3100 and <= 0x31BF or
               >= 0x31F0 and <= 0x31FF or
               >= 0x3400 and <= 0x4DBF or
               >= 0x4E00 and <= 0x9FFF or
               >= 0xAC00 and <= 0xD7AF or
               >= 0xF900 and <= 0xFAFF or
               >= 0xFF01 and <= 0xFF60 or
               >= 0xFF71 and <= 0xFF9D or
               >= 0x20000 and <= 0x323AF;
    }

    private static bool IsProhibitedLineEnd(Rune rune)
    {
        return rune.Value is '(' or '[' or '{' or 0x2018 or 0x201C or
            0xFF08 or 0xFF3B or 0xFF5B or
            0x3008 or 0x300A or 0x300C or 0x300E or 0x3010 or 0x3014 or 0x3016 or 0x3018 or 0x301A;
    }

    private static bool IsProhibitedLineStart(Rune rune)
    {
        return rune.Value is ')' or ']' or '}' or '!' or ',' or '.' or ':' or ';' or '?' or
            0x2019 or 0x201D or 0x2026 or 0x3001 or 0x3002 or 0x3009 or 0x300B or
            0x300D or 0x300F or 0x3011 or 0x3015 or 0x3017 or 0x3019 or 0x301B or
            0xFF01 or 0xFF09 or 0xFF0C or 0xFF0E or 0xFF1A or 0xFF1B or 0xFF1F or 0xFF3D or 0xFF5D;
    }

    private double GetLayoutConstraintWidth(double availableWidth)
    {
        if (EffectiveTextWrapping() == TextWrapping.NoWrap)
        {
            return double.PositiveInfinity;
        }

        if (availableWidth == 0 && (UsesCssWrapMode() || CssIntrinsicMeasure || UsesCssTextBody)) return 0;
        if (double.IsNaN(availableWidth) || double.IsInfinity(availableWidth) || availableWidth <= 0)
        {
            return double.PositiveInfinity;
        }

        return GetContentWidth(availableWidth);
    }

    private bool UsesCssWrapMode()
    {
        if (HasLocalOrAnimatedValue(TextWrappingProperty)) return false;
        return GetEffectiveValueLayer(TextWrappingProperty) is
            DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState ||
            HasAuthoredFlowValue(this, CssFlowProperties.OverflowWrapProperty) ||
            HasAuthoredFlowValue(this, CssFlowProperties.WordBreakProperty) ||
            HasAuthoredFlowValue(this, CssFlowProperties.LineBreakProperty) ||
            HasAuthoredFlowValue(this, CssFlowProperties.LinePaddingProperty) ||
            HasAuthoredLinePaddingOnInlines() ||
            HasAuthoredFlowValue(this, CssFlowProperties.WordSpacingProperty) ||
            HasAuthoredWordSpacingOnInlines() ||
            HasAuthoredFlowValue(this, CssFlowProperties.WordSpaceTransformProperty) ||
            HasAuthoredWordSpaceTransformOnInlines() ||
            HasAuthoredFlowValue(this, CssFlowProperties.LetterSpacingProperty) ||
            HasAuthoredFlowValue(this, CssFlowProperties.TextAutospaceProperty) ||
            HasAuthoredFlowValue(this, CssFlowProperties.HangingPunctuationProperty) ||
            HasAuthoredHangingPunctuationOnInlines() ||
            HasAuthoredFlowValue(this, CssFlowProperties.HyphensProperty) ||
            HasAuthoredFlowValue(this, CssFlowProperties.HyphenateCharacterProperty) ||
            HasAuthoredFlowValue(this, CssFlowProperties.HyphenateLimitLinesProperty) ||
            HasAuthoredFlowValue(this, CssFlowProperties.HyphenateLimitLastProperty) ||
            HasAuthoredFlowValue(this, CssFlowProperties.HyphenateLimitZoneProperty) ||
            HasAuthoredFlowValue(this, CssFlowProperties.HyphenateLimitCharsProperty) ||
            HasAuthoredHyphensOnInlines() ||
            HasAuthoredLetterSpacingOnInlines() ||
            HasAuthoredWhiteSpace();
    }

    private static bool HasAuthoredFlowValue(DependencyObject owner, DependencyProperty property)
    {
        // The diagnostic value source reports Inherited whenever an inheritable
        // property has a parent, even if the entire chain only has metadata defaults.
        // Resolve the actual provider before enabling CSS's default wrapping rules.
        // An explicitly authored initial value still counts, including on an ancestor.
        return owner.HasAnimatedValue(property) || owner.HasCssAnimatedValue(property) ||
            owner.GetUncoercedBaseValueInternal(property).source != BaseValueSource.Default;
    }

    private TextWrapping EffectiveTextWrapping()
    {
        var wrapping = TextWrapping;
        if (HasLocalOrAnimatedValue(TextWrappingProperty)) return wrapping;
        if (GetEffectiveValueLayer(TextWrappingProperty) is
            DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState)
            return wrapping;
        if (HasAuthoredWhiteSpace())
        {
            var mode = (CssTextWrapMode)GetValue(CssFlowProperties.TextWrapModeProperty)!;
            return mode == CssTextWrapMode.NoWrap ? TextWrapping.NoWrap : TextWrapping.Wrap;
        }
        if (wrapping != TextWrapping.NoWrap || !UsesCssWrapMode())
            return wrapping;
        // CSS has white-space: normal unless a white-space declaration says otherwise.
        return TextWrapping.Wrap;
    }

    private bool AllowsEmergencyWrap(bool intrinsic)
    {
        if (!UsesCssWrapMode()) return TextWrapping == TextWrapping.Wrap;
        if (_layoutWordBreak == CssWordBreak.BreakWord) return true;
        return (CssOverflowWrap)GetValue(CssFlowProperties.OverflowWrapProperty)! switch
        {
            CssOverflowWrap.Anywhere => true,
            CssOverflowWrap.BreakWord => !intrinsic,
            _ => false,
        };
    }

    private double GetContentWidth(double width)
    {
        if (double.IsPositiveInfinity(width))
        {
            return width;
        }

        var insets = GetTextInsets();
        return Math.Max(0, width - insets.Left - insets.Right);
    }

    private Thickness GetTextInsets()
    {
        var insets = UsesCssTextBody ? default : LogicalTextInsets(CssBoxMetrics.ContentInsets(this,
            CssLayout?.ContainingWidthCache ?? RenderSize.Width));
        return _cssRubyEdgeInset == 0 ? insets : new(insets.Left + _cssRubyEdgeInset,
            insets.Top, insets.Right + _cssRubyEdgeInset, insets.Bottom);
    }

    private double MeasureTextWidth(string text, bool includeTrailingWhitespace = false)
    {
        var measurement = MeasureText(text);
        return includeTrailingWhitespace
            ? measurement.WidthIncludingTrailingWhitespace
            : measurement.Width;
    }

    private TextMeasurementCacheEntry MeasureText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return default;
        }

        var fontFamily = FontFamily.GetRenderingSource(this);
        var fontSize = FontSize;
        var fontWeight = FontWeight.ToOpenTypeWeight();
        var fontStyle = FontStyle.ToOpenTypeStyle();
        var fontStretch = FontStretch.ToOpenTypeStretch();

        if (!string.Equals(_cachedFontFamily, fontFamily, StringComparison.Ordinal) ||
            Math.Abs(_cachedFontSize - fontSize) > 0.001 ||
            _cachedFontWeight != fontWeight ||
            _cachedFontStyle != fontStyle ||
            _cachedFontStretch != fontStretch)
        {
            _textWidthCache.Clear();
            _cachedFontFamily = fontFamily;
            _cachedFontSize = fontSize;
            _cachedFontWeight = fontWeight;
            _cachedFontStyle = fontStyle;
            _cachedFontStretch = fontStretch;
        }

        if (_textWidthCache.TryGetValue(text, out var cached))
        {
            return cached;
        }

        var formattedText = new FormattedText(text, fontFamily, fontSize)
        {
            FontWeight = fontWeight,
            FontStyle = fontStyle,
            FontStretch = fontStretch
        };

        double width;
        double widthIncludingTrailingWhitespace;
        if (TextMeasurement.MeasureText(formattedText) && formattedText.IsMeasured)
        {
            width = formattedText.Width;
            widthIncludingTrailingWhitespace = Math.Max(
                width,
                formattedText.WidthIncludingTrailingWhitespace);
        }
        else
        {
            widthIncludingTrailingWhitespace = EstimateTextWidth(text, fontSize);
            width = EstimateTextWidth(
                text,
                fontSize,
                GetTextLengthWithoutTrailingLayoutWhitespace(text));
        }
        cached = new TextMeasurementCacheEntry(
            width,
            widthIncludingTrailingWhitespace,
            formattedText);

        if (_textWidthCache.Count >= MaxTextWidthCacheEntries)
        {
            var keysToRemove = _textWidthCache.Keys.Take(MaxTextWidthCacheEntries / 2).ToList();
            foreach (var key in keysToRemove)
            {
                _textWidthCache.Remove(key);
            }
        }

        _textWidthCache[text] = cached;
        return cached;
    }

    /// <summary>
    /// The clip <see cref="OnRender"/> pushes around its own drawing.
    /// CSS plaintext rows retain visible horizontal overflow; explicit outer
    /// overflow and native clips still apply.
    ///
    /// <para>
    /// This clip is HORIZONTAL. Overflow along x — the rare single-word-too-long fallback — must be
    /// cut at the edge so it reads as clipped rather than being silently swallowed. Along y there is
    /// no such intent, and bounding y at <see cref="FrameworkElement.RenderSize"/> was actively
    /// wrong: that height is the LINE BOX (ascent + descent + line gap), which is a typographic
    /// box, not an ink box. Font metrics put the line box exactly at the descent line, so any glyph
    /// whose ink reaches that line — every descender, plus the antialiasing row beneath it — was
    /// being shaved by the element's own clip. DirectWrite ships GetOverhangMetrics precisely
    /// because ink routinely exceeds the layout box; this backend does not surface it, so the box
    /// must simply not constrain y.
    /// </para>
    ///
    /// <para>
    /// Inflating by a whole line height on each side is the bound, not a guess: ink cannot overshoot
    /// its line box by more than another entire line box. Nothing new gets drawn — the clip only
    /// bounds what this element already renders at its own line positions — so the sole effect is
    /// that glyphs stop being cut by the box that was measured for them.
    /// </para>
    /// </summary>
    private RectangleGeometry GetRenderClip()
    {
        var verticalSlack = Math.Max(GetLineHeight(), _cssLineInkPadding);
        var hangingLeft = 0.0;
        var hangingRight = 0.0;
        foreach (var line in _layoutLines)
        {
            verticalSlack = Math.Max(verticalSlack, line.Height);
            var bidiOverflow = TryCssBidiJustificationOverflow(line, out var justifiedOverflow)
                ? justifiedOverflow : TryCssBidiParagraphOverflow(line, out var autospaceOverflow)
                ? autospaceOverflow : CssBidiTabOverflow(line);
            hangingLeft = Math.Max(hangingLeft, line.HangingStart + bidiOverflow);
            hangingRight = Math.Max(hangingRight, line.HangingEnd);
            if (UsesVerticalGlyphForms && TryCssBidiLine(line, out var verticalLine) &&
                verticalLine.Owner.Paragraph is { } verticalParagraph)
            {
                var originX = GetLineOriginX(line, GetContentWidth(TextContentSize.Width));
                // Preserve the physical viewport for overfull nowrap columns.
                // A fitting line can still have ink outside its source cells.
                if (originX >= 0 && originX + verticalLine.Width <= TextContentSize.Width + .001 &&
                    verticalLine.SourceInkBounds is { Width: > 0 } verticalInk)
                {
                    var x = originX - verticalParagraph.Lines[verticalLine.NativeLine].Metrics.Line.X + verticalInk.X;
                    var pixel = 1 / Math.Max(.01, DpiScale.DpiScaleY);
                    hangingLeft = Math.Max(hangingLeft, -x + pixel);
                    hangingRight = Math.Max(hangingRight, x + verticalInk.Width + pixel - TextContentSize.Width);
                }
            }
            if (CssUnicodeBidiProperties.Value(this) == CssUnicodeBidi.Plaintext && !HasCssInlineFragments)
            {
                // CSS overflow remains visible around an overfull plaintext
                // row. Native/CSS ancestor clips still enclose this drawing.
                double x = GetLineOriginX(line, GetContentWidth(TextContentSize.Width));
                double physical = TryCssBidiLine(line, out var bidi) ? bidi.Width :
                    line.Width + line.HangingStart + line.HangingEnd;
                hangingLeft = Math.Max(hangingLeft, -x);
                hangingRight = Math.Max(hangingRight, x + physical - TextContentSize.Width);
            }
        }
        if (HasCssInlineFragments)
        {
            // Coverage padding belongs to this source's ink, while the
            // paragraph clip retains its exact physical container boundary.
            hangingLeft = Math.Max(hangingLeft, _cssLineInkPadding);
            hangingRight = Math.Max(hangingRight, _cssLineInkPadding);
            foreach (var fragment in CssInlineFragmentValues)
            {
                // A source's advance box is not a clipping boundary inside a
                // shared line. Authored overflow clips still surround the draw.
                hangingLeft = Math.Max(hangingLeft, -(fragment.ParagraphClipLeft ?? 0));
                hangingRight = Math.Max(hangingRight, (fragment.ParagraphClipRight ?? TextContentSize.Width) - TextContentSize.Width);
            }
        }
        if (InitialLetterInkInText() is { } initialInk)
        {
            // Antialias coverage can touch the pixel beyond the mathematical
            // ink bound. Keep one device pixel without widening selection.
            var pixel = 1 / Math.Max(.01, DpiScale.DpiScaleX);
            hangingLeft = Math.Max(hangingLeft, -initialInk.Left + pixel);
            hangingRight = Math.Max(hangingRight, initialInk.Right + pixel - TextContentSize.Width);
        }
        var clipRect = new Rect(
            -hangingLeft,
            -verticalSlack,
            TextContentSize.Width + hangingLeft + hangingRight,
            TextContentSize.Height + verticalSlack * 2);
        clipRect = TextWritingClip(clipRect);
        if (_renderClipCache is null || _renderClipCache.Rect != clipRect)
        {
            var geometry = new RectangleGeometry(clipRect);
            geometry.Freeze();
            _renderClipCache = geometry;
        }

        return _renderClipCache;
    }

    private static int GetTextLengthWithoutTrailingLayoutWhitespace(string text)
    {
        var length = text.Length;
        while (length > 0 && IsBreakableWhitespace(text[length - 1]))
        {
            length--;
        }
        return length;
    }

    private static double EstimateTextWidth(string text, double fontSize, int length = -1)
    {
        double width = 0;
        foreach (var c in text.AsSpan(0, length < 0 ? text.Length : length))
        {
            if (c == '\t')
            {
                width += fontSize * 2.4;
            }
            else if (char.IsWhiteSpace(c))
            {
                width += fontSize * 0.3;
            }
            else if ((c >= 0x4E00 && c <= 0x9FFF) ||
                     (c >= 0x3000 && c <= 0x303F) ||
                     (c >= 0xFF00 && c <= 0xFFEF))
            {
                width += fontSize;
            }
            else if (c is 'i' or 'l' or '|' or '!' or '.' or ',')
            {
                width += fontSize * 0.3;
            }
            else if (c is 'm' or 'w' or 'M' or 'W')
            {
                width += fontSize * 0.85;
            }
            else if (char.IsUpper(c))
            {
                width += fontSize * 0.65;
            }
            else if (char.IsLower(c))
            {
                width += fontSize * 0.55;
            }
            else if (char.IsDigit(c))
            {
                width += fontSize * 0.6;
            }
            else
            {
                width += fontSize * 0.6;
            }
        }

        return width;
    }

    private double GetLineHeight()
    {
        var contextGeneration = RenderContext.Current is { IsValid: true } context ? context.Generation : 0;
        var metricsEpoch = TextMeasurement.MetricsCacheEpoch;
        var inputVersions = (FontFamilyProperty.InheritanceVersion, FontSizeProperty.InheritanceVersion,
            FontWeightProperty.InheritanceVersion, FontStyleProperty.InheritanceVersion,
            LineHeightProperty.InheritanceVersion, LineStackingStrategyProperty.InheritanceVersion,
            CssComputedLineHeightValue.Property.InheritanceVersion,
            CssRubyBoxMetrics.ContextProperty.InheritanceVersion,
            FrameworkElement.InheritanceTreeVersion);
        if (_cachedLineHeight is double cached &&
            _lineHeightContextGeneration == contextGeneration &&
            _lineHeightMetricsEpoch == metricsEpoch && _lineHeightInputVersions == inputVersions)
            return cached;

        _lineHeightContextGeneration = contextGeneration;
        _lineHeightMetricsEpoch = metricsEpoch;
        _lineHeightInputVersions = inputVersions;

        var fontFamily = FontFamily.GetRenderingSource(this);
        var fontSize = FontSize;
        var naturalLineHeight = TextMeasurement.GetLineHeight(
            fontFamily,
            fontSize,
            FontWeight.ToOpenTypeWeight(),
            FontStyle.ToOpenTypeStyle());
        if (CssRubyBoxMetrics.IsFontArea(this))
        {
            var fontMetrics = TextMeasurement.GetFontMetrics(fontFamily, fontSize, FontWeight.ToOpenTypeWeight(), FontStyle.ToOpenTypeStyle());
            _cachedLineHeight = CssRubyBoxMetrics.FontBounds(fontSize, fontMetrics).Height;
            return _cachedLineHeight.Value;
        }
        if (double.IsNaN(LineHeight))
        {
            _cachedLineHeight = naturalLineHeight;
            return naturalLineHeight;
        }

        var lineHeight = UsesCssLineMetrics
            ? CssComputedLineHeightValue.LayoutBounds(this, fontSize,
                TextMeasurement.GetFontMetrics(fontFamily, fontSize, FontWeight.ToOpenTypeWeight(), FontStyle.ToOpenTypeStyle())).Height
            : LineStackingStrategy == LineStackingStrategy.MaxHeight
            ? Math.Max(naturalLineHeight, LineHeight)
            : Math.Max(0, LineHeight);
        _cachedLineHeight = lineHeight;
        return lineHeight;
    }

    private static bool EndsWithLineBreak(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var last = text[^1];
        return last == '\n' || last == '\r';
    }

    internal void InvalidateCaches()
    {
        _firstLetterRevision = -1;
        _inlineBoxesDirty=true;
        ClearNativeBidiLines();
        _cssInlineMetricRanges = null;
        // A fragment's source can change without changing its measured envelope.
        // Rebuild the shared paragraph even when the new text has identical metrics.
        var inlineOwner = CssInlineLayoutOwner ?? _cssInlineParagraph?.Owner;
        if (inlineOwner is null)
            foreach (var leaf in _inlineSourceTexts)
                if ((leaf._cssInlineParagraph?.Owner ?? leaf.CssInlineLayoutOwner) is { } leafOwner)
                {
                    inlineOwner = leafOwner;
                    break;
                }
        if (inlineOwner is not null && !ReferenceEquals(inlineOwner, this))
        {
            for (Visual? v = this; v is not null; v = v.VisualParent)
            {
                if (v is UIElement u) u.InvalidateMeasure();
                if (ReferenceEquals(v, inlineOwner)) break;
            }
            inlineOwner.InvalidateMeasure();
        }
        ClearCssInlineFragments();
        _cssTextBody?.InvalidateMeasure();
        _inlineDecorationRanges = null;
        _inlineTextRanges = null;
        _inlineFontMeasurements.Clear();
        _justifiedLineCache.Clear();
        _paintWrapJustificationCache.Clear();
        _cachedLineHeight = null;
        _layoutDirty = true;
        _formattedLinesCacheDirty = true;
        _layoutText = null;
        _layoutConstraintWidth = double.NaN;
        _textWidthCache.Clear();
    }

    internal override void OnFontResourcesChanged()
    {
        InvalidateCaches();
        base.OnFontResourcesChanged();
    }

    private void SynchronizeInlinesFromText(string text)
    {
        if (_inlines is { } inlines)
        {
            _synchronizingTextAndInlines = true;
            try
            {
                if (text.Length == 0)
                {
                    inlines.Clear();
                }
                else if (!_inlinesExplicitlyModified &&
                         inlines.Count == 1 &&
                         inlines[0] is Run implicitRun)
                {
                    // Keep a retained Inlines view live without replacing its
                    // implicit Run on every Text binding update.
                    implicitRun.Text = text;
                }
                else
                {
                    inlines.Clear();
                    inlines.Add(new Run(text));
                }
            }
            finally
            {
                _synchronizingTextAndInlines = false;
            }
        }

        _displayText = text;
        _inlinesExplicitlyModified = false;
    }

    private void OnInlinesChanged()
    {
        if (_synchronizingTextAndInlines)
        {
            return;
        }

        _displayText = _inlines!.GetText();
        _inlinesExplicitlyModified = true;
        _synchronizingTextAndInlines = true;
        try
        {
            SetCurrentValue(TextProperty, _displayText);
        }
        finally
        {
            _synchronizingTextAndInlines = false;
        }
        InvalidateTextContent();
    }

    private void InvalidateTextContent()
    {
        if (!IsCssTypographyProjection) { CssFirstLetter.ContentChanged(); CssFirstLine.ContentChanged(); }
        _contentPointerDocument = null;
        _contentPointerText = null;
        InvalidateCaches();
        CoerceSelectionIntoBounds();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private FlowDocument EnsureContentPointerDocument()
    {
        if (_contentPointerDocument == null ||
            !string.Equals(_contentPointerText, _displayText, StringComparison.Ordinal))
        {
            _contentPointerText = _displayText;
            _contentPointerDocument = FlowDocument.FromText(_displayText);
        }

        return _contentPointerDocument;
    }

    private IEnumerable<IInputElement> EnumerateHostedElements()
    {
        foreach (var inline in Inlines)
        {
            foreach (var element in EnumerateHostedElements(inline))
                yield return element;
        }
    }

    private static IEnumerable<IInputElement> EnumerateHostedElements(Inline inline)
    {
        if (inline is InlineUIContainer { Child: IInputElement child })
            yield return child;

        if (inline is Span span)
        {
            foreach (var nested in span.Inlines)
            {
                foreach (var element in EnumerateHostedElements(nested))
                    yield return element;
            }
        }
    }

    private static void OnBaselineOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock)
            OnTextChanged(d, e);
        else if (d is UIElement element)
            element.InvalidateMeasure();

        // A descendant's baseline can change the size and position of every
        // containing baseline group. Propagate in detached layout trees too,
        // where no LayoutManager is present to invalidate ancestors for us.
        if (d is not UIElement source || source.IsLayoutIsolated) return;
        for (var parent = source.VisualParent as UIElement;
            parent is not null; parent = parent.VisualParent as UIElement)
        {
            if (parent.IsMeasureValid) parent.InvalidateMeasure();
            if (parent.IsLayoutIsolated) break;
        }
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
        {
            return;
        }

        if (e.Property == TextProperty && !textBlock._synchronizingTextAndInlines)
        {
            textBlock.SynchronizeInlinesFromText((string?)e.NewValue ?? string.Empty);
            if (!textBlock.IsCssTypographyProjection) { CssFirstLetter.ContentChanged(); CssFirstLine.ContentChanged(); }
        }

        textBlock._contentPointerDocument = null;
        textBlock._contentPointerText = null;
        textBlock.InvalidateCaches();
        textBlock.CoerceSelectionIntoBounds();

        if (e.Property != TextProperty &&
            e.Property != PaddingProperty &&
            string.IsNullOrEmpty(textBlock._displayText))
        {
            return;
        }

        textBlock.InvalidateMeasure();
        textBlock.InvalidateVisual();
    }

    private static void OnIsTextSelectionEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
        {
            return;
        }

        if (!(bool)(e.NewValue ?? false))
        {
            textBlock._isSelecting = false;
            textBlock._isWordSelecting = false;
            textBlock.ReleaseMouseCapture();
            textBlock.ClearSelection();
        }

        if (textBlock.IsMouseOver || textBlock._ownsSelectionCursor)
            textBlock.UpdateHoverCursor();
        textBlock.InvalidateVisual();
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Shared paragraphs position their original sources during layout. A
        // native attached alignment on any flow host must invalidate that plan,
        // including a later local-to-local change with the same value source.
        if (e.Property == TextAlignmentProperty && d is FrameworkElement host && CssDisplayLayout.IsFlow(host))
        {
            CssFlowProperties.InvalidateInheritedTextLayout(host);
            if (host.VisualParent is UIElement parent) parent.InvalidateMeasure();
            return;
        }
        if (d is not TextBlock textBlock || Equals(e.OldValue, e.NewValue))
        {
            return;
        }

        if (e.Property == ForegroundProperty)
        {
            // Foreground 不影响布局/字体度量，但要把新 brush 推到 _cachedFormattedLines。
            textBlock._foregroundCacheNeedsSync = true;
            textBlock.InvalidateVisual();
            return;
        }

        if (e.Property == TextAlignmentProperty)
        {
            // Alignment can change retained justification advances and the
            // optional ligatures used to measure the paragraph.
            textBlock.InvalidateCaches();
            textBlock.InvalidateMeasure();
            textBlock.InvalidateVisual();
            return;
        }

        if (e.Property == SelectionBrushProperty)
        {
            textBlock.InvalidateVisual();
            return;
        }

        textBlock.InvalidateCaches();
        textBlock.InvalidateMeasure();
        textBlock.InvalidateVisual();
    }

    private void CoerceSelectionIntoBounds()
    {
        var textLength = _displayText.Length;
        var newSelectionStart = Math.Clamp(_selectionStart, 0, textLength);
        var newSelectionLength = Math.Clamp(_selectionLength, 0, textLength - newSelectionStart);
        var changed = newSelectionStart != _selectionStart || newSelectionLength != _selectionLength;

        _selectionStart = newSelectionStart;
        _selectionLength = newSelectionLength;
        _selectionAnchor = Math.Clamp(_selectionAnchor, 0, textLength);

        if (changed)
        {
            RaiseSelectionChanged();
        }
    }

    private readonly struct TextLayoutLine
    {
        public TextLayoutLine(
            int startIndex,
            int length,
            double width,
            bool hasLineBreakAfter,
            double height,
            double baseline,
            double glyphHeight,
            FormattedText? formattedText = null,
            bool canJustify = false,
            bool indentApplies = false,
            PaintWrapLine? paintWrap = null,
            bool softHyphenVisible = false,
            double startPadding = 0,
            double endPadding = 0,
            double hangingStart = 0,
            double hangingEnd = 0,
            LineHyphenChange? hyphenChange = null)
        {
            StartIndex = startIndex;
            Length = length;
            Width = width;
            HasLineBreakAfter = hasLineBreakAfter;
            Height = height;
            Baseline = baseline;
            GlyphHeight = glyphHeight;
            FormattedText = formattedText;
            CanJustify = canJustify;
            IndentApplies = indentApplies;
            PaintWrap = paintWrap;
            SoftHyphenVisible = softHyphenVisible;
            StartPadding = startPadding;
            EndPadding = endPadding;
            HangingStart = hangingStart;
            HangingEnd = hangingEnd;
            HyphenChange = hyphenChange;
        }

        public int StartIndex { get; }

        public int Length { get; }

        public double Width { get; }

        public bool HasLineBreakAfter { get; }

        public double Height { get; }

        public double Baseline { get; }

        public double GlyphHeight { get; }

        public FormattedText? FormattedText { get; }

        public bool CanJustify { get; }

        public bool IndentApplies { get; }

        public PaintWrapLine? PaintWrap { get; }

        public bool SoftHyphenVisible { get; }

        public double StartPadding { get; }

        public double EndPadding { get; }

        public double HangingStart { get; }

        public double HangingEnd { get; }

        public LineHyphenChange? HyphenChange { get; }

        public int InlineFragmentIndex { get; init; } = -1;

        public TextLayoutLine WithInlineFragmentIndex(int index) =>
            new(StartIndex, Length, Width, HasLineBreakAfter, Height, Baseline, GlyphHeight,
                FormattedText, CanJustify, IndentApplies, PaintWrap, SoftHyphenVisible,
                StartPadding, EndPadding, HangingStart, HangingEnd, HyphenChange)
            {
                InlineFragmentIndex = index
            };
    }

    private readonly record struct TextMeasurementCacheEntry(
        double Width,
        double WidthIncludingTrailingWhitespace,
        FormattedText? FormattedText);
}
