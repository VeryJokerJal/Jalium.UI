using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using System.Collections.ObjectModel;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Displays text content.
/// </summary>
[ContentProperty(nameof(Inlines))]
public class TextBlock : FrameworkElement, IAddChild, IServiceProvider, IContentHost
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.TextBlockAutomationPeer(this);
    }

    private const int MaxTextWidthCacheEntries = 256;
    private static readonly SolidColorBrush s_defaultSelectionBrush = new(Color.FromArgb(180, 0x1E, 0x79, 0x3F));

    private readonly Dictionary<string, double> _textWidthCache = new(StringComparer.Ordinal);
    private readonly InlineCollection _inlines;
    private string _displayText = string.Empty;
    private bool _synchronizingTextAndInlines;
    private bool _inlinesExplicitlyModified;

    private List<TextLayoutLine> _layoutLines = new();
    private bool _layoutDirty = true;
    private double _layoutConstraintWidth = double.NaN;
    private string? _layoutText;
    private List<FormattedText>? _cachedFormattedLines;
    private bool _formattedLinesCacheDirty = true;
    // Foreground 是“绘制属性”而非“布局属性”：变了不需要重 layout，但必须同步到
    // _cachedFormattedLines 里每个 FormattedText.Foreground，否则 OnRender 用旧 brush。
    // 用单独的 dirty 标志驱动，平时 OnRender 0 开销，只在 Foreground 真变后下一帧同步一次。
    private bool _foregroundCacheNeedsSync;

    private string? _cachedFontFamily;
    private double _cachedFontSize;
    private int _cachedFontWeight;
    private int _cachedFontStyle;
    private int _cachedFontStretch;

    private int _selectionStart;
    private int _selectionLength;
    private int _selectionAnchor;
    private bool _isSelecting;
    private bool _isWordSelecting;
    private int _wordSelectionAnchorStart;
    private int _wordSelectionAnchorEnd;
    private bool _isRenderingText;
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
            new PropertyMetadata(double.NaN, OnTextChanged));

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
        _inlines = new InlineCollection(OnInlinesChanged);
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
    public InlineCollection Inlines => _inlines;

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
        if (!snapToText && !new Rect(RenderSize).Contains(point))
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
            Clipboard.SetText(SelectedText);
        }
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var horizontalPadding = Padding.Left + Padding.Right;
        var verticalPadding = Padding.Top + Padding.Bottom;
        if (string.IsNullOrEmpty(_displayText))
        {
            return new Size(horizontalPadding, verticalPadding);
        }

        EnsureLayout(GetLayoutConstraintWidth(availableSize.Width));

        var lineHeight = GetLineHeight();
        var maxLineWidth = 0.0;
        for (int i = 0; i < _layoutLines.Count; i++)
        {
            maxLineWidth = Math.Max(maxLineWidth, _layoutLines[i].Width);
        }

        var measuredWidth = maxLineWidth + horizontalPadding + 2;
        var measuredHeight = Math.Max(_layoutLines.Count, 1) * lineHeight + verticalPadding + 2;

        if (TextTrimming != TextTrimming.None &&
            TextWrapping == TextWrapping.NoWrap &&
            !double.IsInfinity(availableSize.Width))
        {
            measuredWidth = Math.Min(measuredWidth, availableSize.Width);
        }

        if (!double.IsInfinity(availableSize.Height))
        {
            measuredHeight = Math.Min(measuredHeight, availableSize.Height);
        }

        return new Size(measuredWidth, measuredHeight);
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_isRenderingText)
        {
            return;
        }

        _isRenderingText = true;
        try
        {
            base.OnRender(drawingContext);

            if (Background != null)
            {
                drawingContext.DrawRectangle(Background, null, new Rect(RenderSize));
            }

            if (string.IsNullOrEmpty(_displayText) || Foreground == null)
            {
                return;
            }
            var dc = drawingContext;

            EnsureLayout(GetLayoutConstraintWidth(RenderSize.Width));

            dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));

            if (_selectionLength > 0)
            {
                DrawSelection(dc);
            }

            DrawTextLines(dc);
            dc.Pop();
        }
        finally
        {
            _isRenderingText = false;
        }
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
        var lineHeight = GetLineHeight();
        var renderWidth = GetContentWidth(RenderSize.Width);
        var verticalOffset = GetVerticalContentOffset(lineHeight);

        // Rebuild formatted text cache only when layout or text properties changed
        if (_formattedLinesCacheDirty || _cachedFormattedLines == null ||
            _cachedFormattedLines.Count != _layoutLines.Count)
        {
            var fontFamily = FontFamily.Source;
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

                var lineText = _displayText.Substring(line.StartIndex, line.Length);
                var formattedText = new FormattedText(lineText, fontFamily, fontSize)
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
                    MaxTextHeight = lineHeight,
                    FontWeight = fontWeight,
                    FontStyle = fontStyle,
                    FontStretch = fontStretch,
                    Trimming = TextWrapping == TextWrapping.NoWrap ? TextTrimming : TextTrimming.None
                };
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
        // a line's absolute band is [Offset.Y + lineY, Offset.Y + lineY + lineHeight]
        // where lineY = verticalOffset + i * lineHeight (the same y handed to
        // DrawText). Skipping a long off-screen block (e.g. a 10 000-line TextBlock
        // scrolled to the middle) turns O(lines) DrawText calls into O(visible lines).
        Rect? clipBounds = null;
        var offsetY = 0.0;
        if (dc is IClipBoundsDrawingContext { CurrentClipBounds: Rect cb } &&
            dc is IOffsetDrawingContext offsetContext)
        {
            clipBounds = cb;
            offsetY = offsetContext.Offset.Y;
        }

        for (int i = 0; i < _cachedFormattedLines.Count; i++)
        {
            var ft = _cachedFormattedLines[i];
            if (ft == null) continue;

            var lineY = verticalOffset + i * lineHeight;

            if (clipBounds is Rect clip)
            {
                var top = offsetY + lineY;
                var bottom = top + lineHeight;
                // Cull when the band is fully above or fully below the clip. Touching
                // edges (bottom == clip top, or top == clip bottom) contribute no
                // visible pixels, so they are culled too.
                if (bottom <= clip.Y || top >= clip.Y + clip.Height)
                {
                    continue;
                }
            }

            var lineOriginX = GetLineOriginX(_layoutLines[i], renderWidth);
            dc.DrawText(ft, new Point(lineOriginX, lineY));
            DrawTextDecorations(dc, _layoutLines[i], lineOriginX, lineY, lineHeight);
        }
    }

    private void DrawTextDecorations(
        DrawingContext drawingContext,
        in TextLayoutLine line,
        double lineOriginX,
        double lineY,
        double lineHeight)
    {
        if (line.Width <= 0 || GetValue(TextDecorationsProperty) is not TextDecorationCollection decorations || decorations.Count == 0)
        {
            return;
        }

        var metrics = TextMeasurement.GetFontMetrics(
            FontFamily.Source,
            FontSize > 0 ? FontSize : 14,
            FontWeight.ToOpenTypeWeight(),
            FontStyle.ToOpenTypeStyle());
        var baseline = lineY + (metrics.Ascent > 0 ? metrics.Ascent : lineHeight * 0.8);

        foreach (var decoration in decorations)
        {
            var brush = decoration.Brush ?? Foreground;
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
                new Point(lineOriginX + line.Width, y));
        }
    }

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
    {
        if (lineHeight <= 0 || _layoutLines.Count == 0)
        {
            return Padding.Top;
        }

        var fontFamily = FontFamily.Source;
        var fontSize = FontSize > 0 ? FontSize : 14;
        var metrics = TextMeasurement.GetFontMetrics(
            fontFamily,
            fontSize,
            FontWeight.ToOpenTypeWeight(),
            FontStyle.ToOpenTypeStyle());

        // Visible glyph height per line: Ascent + Descent (no LineGap).
        var glyphHeight = metrics.Ascent + metrics.Descent;
        if (glyphHeight <= 0)
        {
            // Native metrics unavailable; fall back to box-based centering.
            var totalLinesHeight = _layoutLines.Count * lineHeight;
            var fallbackSlack = Math.Max(0, RenderSize.Height - Padding.Top - Padding.Bottom) - totalLinesHeight;
            return Padding.Top + (fallbackSlack > 0 ? fallbackSlack / 2 : 0);
        }

        // For N lines: lines 1..N-1 each advance by a full lineHeight (since the
        // glyph + lineGap stack up between baselines), and the last line contributes
        // its glyph height. So visible extent = (N-1) * lineHeight + glyphHeight.
        var totalVisibleHeight = (_layoutLines.Count - 1) * lineHeight + glyphHeight;
        var slack = Math.Max(0, RenderSize.Height - Padding.Top - Padding.Bottom) - totalVisibleHeight;
        return Padding.Top + (slack > 0 ? slack / 2 : 0);
    }

    private void DrawSelection(DrawingContext dc)
    {
        var selectionBrush = ResolveSelectionBrush();
        if (selectionBrush == null)
        {
            return;
        }

        var selectionEnd = _selectionStart + _selectionLength;
        var lineHeight = GetLineHeight();
        var renderWidth = GetContentWidth(RenderSize.Width);
        var verticalOffset = GetVerticalContentOffset(lineHeight);

        for (int i = 0; i < _layoutLines.Count; i++)
        {
            var line = _layoutLines[i];
            var lineEnd = line.StartIndex + line.Length;
            var intersectsLine = _selectionStart <= lineEnd && selectionEnd >= line.StartIndex;
            if (!intersectsLine)
            {
                continue;
            }

            var lineText = line.Length > 0 ? _displayText.Substring(line.StartIndex, line.Length) : string.Empty;
            var startInLine = Math.Max(0, _selectionStart - line.StartIndex);
            var endInLine = Math.Min(line.Length, selectionEnd - line.StartIndex);
            var lineOriginX = GetLineOriginX(line, renderWidth);
            var y = verticalOffset + i * lineHeight;

            if (endInLine > startInLine)
            {
                var textBefore = lineText.Substring(0, startInLine);
                var selectedText = lineText.Substring(startInLine, endInLine - startInLine);
                var startX = lineOriginX + MeasureTextWidth(textBefore);
                var width = Math.Max(1, Math.Round(MeasureTextWidth(selectedText)));

                dc.DrawRectangle(selectionBrush, null, new Rect(startX, y, width, lineHeight));
            }

            if (selectionEnd > lineEnd && line.HasLineBreakAfter)
            {
                var breakX = lineOriginX + line.Width;
                dc.DrawRectangle(selectionBrush, null, new Rect(breakX, y, Math.Max(1, FontSize * 0.3), lineHeight));
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

        if (e.ClickCount >= 3)
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
        Cursor = null;
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
        if (e.Handled || !IsTextSelectionEnabled)
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
        Cursor = CanShowTextSelectionCursor() ? Jalium.UI.Input.Cursors.IBeam : null;
    }

    private bool CanStartSelection()
    {
        if (!IsEnabled || !IsTextSelectionEnabled || string.IsNullOrEmpty(_displayText))
        {
            return false;
        }

        return !IsSelectionBlockedByInteractiveAncestor() || HasLocalValue(IsTextSelectionEnabledProperty);
    }

    private bool CanShowTextSelectionCursor()
    {
        return IsEnabled &&
            IsTextSelectionEnabled &&
            !string.IsNullOrEmpty(_displayText) &&
            (!IsSelectionBlockedByInteractiveAncestor() || HasLocalValue(IsTextSelectionEnabledProperty));
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
        InvalidateVisual();
        RaiseSelectionChanged();
    }

    private void RaiseSelectionChanged()
    {
        RaiseEvent(new RoutedEventArgs(SelectionChangedEvent, this));
    }

    private Brush? ResolveSelectionBrush()
    {
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
        EnsureLayout(GetLayoutConstraintWidth(RenderSize.Width));
        if (_layoutLines.Count == 0)
        {
            return 0;
        }

        var lineHeight = GetLineHeight();
        var verticalOffset = GetVerticalContentOffset(lineHeight);
        var adjustedY = position.Y - verticalOffset;
        var lineIndex = lineHeight <= 0
            ? 0
            : Math.Clamp((int)Math.Floor(Math.Max(0, adjustedY) / lineHeight), 0, _layoutLines.Count - 1);

        var line = _layoutLines[lineIndex];
        var lineOriginX = GetLineOriginX(line, GetContentWidth(RenderSize.Width));
        var relativeX = position.X - lineOriginX;
        if (relativeX <= 0 || line.Length == 0)
        {
            return line.StartIndex;
        }

        var lineText = _displayText.Substring(line.StartIndex, line.Length);
        int columnIndex = line.Length;
        double previousWidth = 0;

        for (int i = 0; i <= line.Length; i++)
        {
            var width = MeasureTextWidth(lineText.Substring(0, i));
            if (width >= relativeX)
            {
                columnIndex = i;
                if (i > 0 && (relativeX - previousWidth) < (width - relativeX))
                {
                    columnIndex = i - 1;
                }

                break;
            }

            previousWidth = width;
        }

        // Snap onto a grapheme-cluster edge so a click never lands inside an emoji.
        return line.StartIndex + GraphemeClusters.SnapNearest(lineText, columnIndex);
    }

    private double GetLineOriginX(in TextLayoutLine line, double renderWidth)
    {
        if (TextAlignment == TextAlignment.Center)
        {
            return Padding.Left + Math.Max(0, (renderWidth - line.Width) / 2);
        }

        if (TextAlignment == TextAlignment.Right)
        {
            return Padding.Left + Math.Max(0, renderWidth - line.Width);
        }

        return Padding.Left;
    }

    private void EnsureLayout(double constraintWidth)
    {
        if (!_layoutDirty &&
            string.Equals(_displayText, _layoutText, StringComparison.Ordinal) &&
            ConstraintWidthUnchanged(_layoutConstraintWidth, constraintWidth))
        {
            return;
        }

        RebuildLayout(constraintWidth);
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

    private void RebuildLayout(double constraintWidth)
    {
        _layoutLines = new List<TextLayoutLine>();
        _layoutText = _displayText;
        _layoutConstraintWidth = constraintWidth;
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

        if (string.IsNullOrEmpty(_displayText))
        {
            _layoutLines.Add(new TextLayoutLine(0, 0, 0, false));
            return;
        }

        var index = 0;
        while (index < _displayText.Length)
        {
            var lineStart = index;
            while (index < _displayText.Length && _displayText[index] != '\r' && _displayText[index] != '\n')
            {
                index++;
            }

            var lineLength = index - lineStart;
            var hasLineBreakAfter = false;
            if (index < _displayText.Length)
            {
                hasLineBreakAfter = true;
                if (_displayText[index] == '\r' && index + 1 < _displayText.Length && _displayText[index + 1] == '\n')
                {
                    index += 2;
                }
                else
                {
                    index++;
                }
            }

            AppendLayoutLines(lineStart, lineLength, hasLineBreakAfter, constraintWidth);
        }

        if (EndsWithLineBreak(_displayText))
        {
            _layoutLines.Add(new TextLayoutLine(_displayText.Length, 0, 0, false));
        }
    }

    private void AppendLayoutLines(int lineStart, int lineLength, bool hasLineBreakAfter, double constraintWidth)
    {
        if (TextWrapping == TextWrapping.NoWrap || double.IsInfinity(constraintWidth) || constraintWidth <= 0)
        {
            var lineText = lineLength > 0 ? _displayText.Substring(lineStart, lineLength) : string.Empty;
            _layoutLines.Add(new TextLayoutLine(lineStart, lineLength, MeasureTextWidth(lineText), hasLineBreakAfter));
            return;
        }

        if (lineLength == 0)
        {
            _layoutLines.Add(new TextLayoutLine(lineStart, 0, 0, hasLineBreakAfter));
            return;
        }

        var consumed = 0;
        while (consumed < lineLength)
        {
            var remaining = lineLength - consumed;
            var currentStart = lineStart + consumed;
            var currentLength = FindWrapLength(currentStart, remaining, constraintWidth);
            if (currentLength <= 0)
            {
                currentLength = 1;
            }

            var currentText = _displayText.Substring(currentStart, currentLength);
            var isLastFragment = consumed + currentLength >= lineLength;
            _layoutLines.Add(new TextLayoutLine(
                currentStart,
                currentLength,
                MeasureTextWidth(currentText),
                isLastFragment && hasLineBreakAfter));

            consumed += currentLength;
        }
    }

    private int FindWrapLength(int startIndex, int maxLength, double availableWidth)
    {
        if (maxLength <= 0)
        {
            return 0;
        }

        int low = 1;
        int high = maxLength;
        int best = 1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);
            var candidate = _displayText.Substring(startIndex, mid);
            var width = MeasureTextWidth(candidate);

            if (width <= availableWidth)
            {
                best = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        if (best >= maxLength || TextWrapping == TextWrapping.WrapWithOverflow)
        {
            return best;
        }

        for (int i = best - 1; i > 0; i--)
        {
            if (char.IsWhiteSpace(_displayText[startIndex + i]))
            {
                return i + 1;
            }
        }

        return best;
    }

    private double GetLayoutConstraintWidth(double availableWidth)
    {
        if (TextWrapping == TextWrapping.NoWrap)
        {
            return double.PositiveInfinity;
        }

        if (double.IsNaN(availableWidth) || double.IsInfinity(availableWidth) || availableWidth <= 0)
        {
            return double.PositiveInfinity;
        }

        return GetContentWidth(availableWidth);
    }

    private double GetContentWidth(double width)
    {
        if (double.IsPositiveInfinity(width))
        {
            return width;
        }

        return Math.Max(0, width - Padding.Left - Padding.Right);
    }

    private double MeasureTextWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var fontFamily = FontFamily.Source;
        var fontSize = FontSize > 0 ? FontSize : 14;
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

        if (_textWidthCache.TryGetValue(text, out var width))
        {
            return width;
        }

        var formattedText = new FormattedText(text, fontFamily, fontSize)
        {
            FontWeight = fontWeight,
            FontStyle = fontStyle,
            FontStretch = fontStretch
        };

        if (TextMeasurement.MeasureText(formattedText) && formattedText.IsMeasured)
        {
            width = formattedText.Width;
        }
        else
        {
            width = EstimateTextWidth(text, fontSize);
        }

        if (_textWidthCache.Count >= MaxTextWidthCacheEntries)
        {
            var keysToRemove = _textWidthCache.Keys.Take(MaxTextWidthCacheEntries / 2).ToList();
            foreach (var key in keysToRemove)
            {
                _textWidthCache.Remove(key);
            }
        }

        _textWidthCache[text] = width;
        return width;
    }

    private static double EstimateTextWidth(string text, double fontSize)
    {
        double width = 0;
        foreach (var c in text)
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
        var fontFamily = FontFamily.Source;
        var fontSize = FontSize > 0 ? FontSize : 14;
        var naturalLineHeight = TextMeasurement.GetLineHeight(
            fontFamily,
            fontSize,
            FontWeight.ToOpenTypeWeight(),
            FontStyle.ToOpenTypeStyle());
        if (double.IsNaN(LineHeight))
        {
            return naturalLineHeight;
        }

        return LineStackingStrategy == LineStackingStrategy.MaxHeight
            ? Math.Max(naturalLineHeight, LineHeight)
            : Math.Max(0, LineHeight);
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

    private void InvalidateCaches()
    {
        _layoutDirty = true;
        _formattedLinesCacheDirty = true;
        _layoutText = null;
        _layoutConstraintWidth = double.NaN;
        _textWidthCache.Clear();
    }

    private void SynchronizeInlinesFromText(string text)
    {
        _synchronizingTextAndInlines = true;
        try
        {
            _inlines.Clear();
            if (text.Length > 0)
            {
                _inlines.Add(new Run(text));
            }

            _displayText = text;
            _inlinesExplicitlyModified = false;
        }
        finally
        {
            _synchronizingTextAndInlines = false;
        }
    }

    private void OnInlinesChanged()
    {
        if (_synchronizingTextAndInlines)
        {
            return;
        }

        _displayText = _inlines.GetText();
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

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
        {
            return;
        }

        if (e.Property == TextProperty && !textBlock._synchronizingTextAndInlines)
        {
            textBlock.SynchronizeInlinesFromText((string?)e.NewValue ?? string.Empty);
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

        textBlock.UpdateHoverCursor();
        textBlock.InvalidateVisual();
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
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

        if (e.Property == TextAlignmentProperty || e.Property == SelectionBrushProperty)
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
        public TextLayoutLine(int startIndex, int length, double width, bool hasLineBreakAfter)
        {
            StartIndex = startIndex;
            Length = length;
            Width = width;
            HasLineBreakAfter = hasLineBreakAfter;
        }

        public int StartIndex { get; }

        public int Length { get; }

        public double Width { get; }

        public bool HasLineBreakAfter { get; }
    }
}

