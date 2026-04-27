using System.Text;
using Jalium.UI.Automation;
using Jalium.UI.Controls.Automation;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// A control for editing plain text.
/// </summary>
public class TextBox : TextBoxBase, IImeSupport
{
    #region Automation

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer()
    {
        return new TextBoxAutomationPeer(this);
    }

    #endregion

    #region Fields

    // Multi-line support
    private List<TextLine> _lines = new();
    private bool _linesDirty = true;

    // Visual line counts (logical lines may wrap to multiple visual rows when
    // TextWrapping != NoWrap). Parallel to _lines. Invalidated whenever _lines
    // changes or the wrap width / font metrics change.
    private readonly List<int> _lineVisualCounts = new();
    private double _cachedWrapWidth = double.NaN;
    private TextWrapping _cachedWrapMode = TextWrapping.NoWrap;
    private double _cachedVisualLineHeight = double.NaN;
    private string? _cachedVisualFontFamily;
    private double _cachedVisualFontSize = double.NaN;
    private int _cachedVisualFontWeight;
    private int _cachedVisualFontStyle;

    // Text width measurement cache for accurate selection/caret positioning
    private Dictionary<string, double> _textWidthCache = new();
    private string? _cachedFontFamily;
    private double _cachedFontSize;
    private int _cachedFontWeight;
    private int _cachedFontStyle;
    private int _cachedFontStretch;
    private const int MaxCacheSize = 256;

    // IME support
    private bool _isImeComposing;
    private string _imeCompositionString = string.Empty;
    private int _imeCompositionCursor;
    private int _imeCompositionStart;

    // Cached caret pen
    private Pen? _caretPen;
    private Brush? _caretPenBrush;
    private double _caretPenOpacity;

    // Spell checking
    private List<SpellingError> _spellingErrors = new();

    // Auto-formatting
    private List<FormattedRegion> _formattedRegions = new();

    // Fallback brushes & pens for rendering (used when theme resources are unavailable)
    private static readonly SolidColorBrush s_fallbackFocusBorderBrush = new(ThemeColors.ControlBorderFocused);
    private static readonly SolidColorBrush s_fallbackPlaceholderTextBrush = new(Color.FromRgb(128, 128, 128));
    private static readonly SolidColorBrush s_fallbackWhiteBrush = new(Color.White);
    private static readonly SolidColorBrush s_spellErrorBrush = new(Color.FromRgb(255, 0, 0));
    private static readonly Pen s_spellErrorPen = new(s_spellErrorBrush, 1);
    private static readonly SolidColorBrush s_compositionBgBrush = new(Color.FromRgb(60, 60, 80));
    private static readonly SolidColorBrush s_compositionTextBrush = new(Color.FromRgb(255, 255, 200));
    private static readonly SolidColorBrush s_compositionUnderlineBrush = new(Color.FromRgb(200, 200, 100));
    private static readonly Pen s_compositionUnderlinePen = new(s_compositionUnderlineBrush, 1);
    private static readonly Pen s_compositionCursorPen = new(s_fallbackWhiteBrush, 1);

    // Theme-aware brush accessors
    private Brush FocusBorderBrush => TryFindResource("AccentBrush") as Brush ?? s_fallbackFocusBorderBrush;
    private Brush PlaceholderTextBrush => TryFindResource("TextFillColorTertiaryBrush") as Brush ?? s_fallbackPlaceholderTextBrush;
    private Brush TextBoxWhiteBrush => TryFindResource("TextFillColorPrimaryBrush") as Brush ?? s_fallbackWhiteBrush;

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Text dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(TextBox),
            new PropertyMetadata(string.Empty, OnTextChanged));

    /// <summary>
    /// Identifies the MaxLength dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaxLengthProperty =
        DependencyProperty.Register(nameof(MaxLength), typeof(int), typeof(TextBox),
            new PropertyMetadata(0));

    /// <summary>
    /// Identifies the TextWrapping dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextWrappingProperty =
        DependencyProperty.Register(nameof(TextWrapping), typeof(TextWrapping), typeof(TextBox),
            new PropertyMetadata(TextWrapping.NoWrap, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the TextAlignment dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextAlignmentProperty =
        DependencyProperty.Register(nameof(TextAlignment), typeof(TextAlignment), typeof(TextBox),
            new PropertyMetadata(TextAlignment.Left, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the PlaceholderText dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.Register(nameof(PlaceholderText), typeof(string), typeof(TextBox),
            new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsSpellCheckEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSpellCheckEnabledProperty =
        DependencyProperty.Register(nameof(IsSpellCheckEnabled), typeof(bool), typeof(TextBox),
            new PropertyMetadata(false, OnSpellCheckEnabledChanged));

    /// <summary>
    /// Identifies the SpellCheckLanguage dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SpellCheckLanguageProperty =
        DependencyProperty.Register(nameof(SpellCheckLanguage), typeof(string), typeof(TextBox),
            new PropertyMetadata("en-US"));

    /// <summary>
    /// Identifies the IsAutoCorrectEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsAutoCorrectEnabledProperty =
        DependencyProperty.Register(nameof(IsAutoCorrectEnabled), typeof(bool), typeof(TextBox),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsAutoCapitalizationEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsAutoCapitalizationEnabledProperty =
        DependencyProperty.Register(nameof(IsAutoCapitalizationEnabled), typeof(bool), typeof(TextBox),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the DetectUrls dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DetectUrlsProperty =
        DependencyProperty.Register(nameof(DetectUrls), typeof(bool), typeof(TextBox),
            new PropertyMetadata(false, OnFormatDetectionChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Text
    {
        get => (string)(GetValue(TextProperty) ?? string.Empty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum number of characters (0 = unlimited).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public int MaxLength
    {
        get => (int)GetValue(MaxLengthProperty)!;
        set => SetValue(MaxLengthProperty, value);
    }

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
    /// Gets or sets the placeholder text shown when the text box is empty.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string PlaceholderText
    {
        get => (string)(GetValue(PlaceholderTextProperty) ?? string.Empty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>
    /// Gets or sets whether spell checking is enabled.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsSpellCheckEnabled
    {
        get => (bool)GetValue(IsSpellCheckEnabledProperty)!;
        set => SetValue(IsSpellCheckEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the spell check language (e.g., "en-US", "zh-CN").
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string SpellCheckLanguage
    {
        get => (string)(GetValue(SpellCheckLanguageProperty) ?? "en-US");
        set => SetValue(SpellCheckLanguageProperty, value);
    }

    /// <summary>
    /// Gets the current spelling errors.
    /// </summary>
    public IReadOnlyList<SpellingError> SpellingErrors => _spellingErrors;

    /// <summary>
    /// Gets or sets whether auto-correction is enabled.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsAutoCorrectEnabled
    {
        get => (bool)GetValue(IsAutoCorrectEnabledProperty)!;
        set => SetValue(IsAutoCorrectEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets whether auto-capitalization is enabled.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsAutoCapitalizationEnabled
    {
        get => (bool)GetValue(IsAutoCapitalizationEnabledProperty)!;
        set => SetValue(IsAutoCapitalizationEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets whether URL detection is enabled.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool DetectUrls
    {
        get => (bool)GetValue(DetectUrlsProperty)!;
        set => SetValue(DetectUrlsProperty, value);
    }

    /// <summary>
    /// Gets the detected formatted regions (URLs, emails, etc.).
    /// </summary>
    public IReadOnlyList<FormattedRegion> FormattedRegions => _formattedRegions;

    /// <summary>
    /// Gets the number of lines.
    /// </summary>
    public int LineCount
    {
        get
        {
            EnsureLinesValid();
            return _lines.Count;
        }
    }

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the TextChanged routed event.
    /// </summary>
    public static readonly RoutedEvent TextChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(TextChanged), RoutingStrategy.Bubble,
            typeof(TextChangedEventHandler), typeof(TextBox));

    /// <summary>
    /// Identifies the SelectionChanged routed event.
    /// </summary>
    public static readonly RoutedEvent SelectionChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(SelectionChanged), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(TextBox));

    /// <summary>
    /// Occurs when the text content changes.
    /// </summary>
    public event TextChangedEventHandler TextChanged
    {
        add => AddHandler(TextChangedEvent, value);
        remove => RemoveHandler(TextChangedEvent, value);
    }

    /// <summary>
    /// Occurs when the selection changes.
    /// </summary>
    public event RoutedEventHandler SelectionChanged
    {
        add => AddHandler(SelectionChangedEvent, value);
        remove => RemoveHandler(SelectionChangedEvent, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="TextBox"/> class.
    /// </summary>
    public TextBox()
    {
        // Set IBeam cursor for text input
        Cursor = Jalium.UI.Cursors.IBeam;

        // Subscribe to IME events
        InputMethod.CompositionStarted += OnImeCompositionStarted;
        InputMethod.CompositionUpdated += OnImeCompositionUpdated;
        InputMethod.CompositionEnded += OnImeCompositionEnded;

        // Subscribe to focus events for IME target management
        AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGotFocusHandler));
        AddHandler(LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnLostFocusHandler));
    }

    private void OnGotFocusHandler(object sender, KeyboardFocusChangedEventArgs e)
    {
        InputMethod.SetTarget(this);
    }

    private void OnLostFocusHandler(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (InputMethod.Current == this)
        {
            InputMethod.SetTarget(null);
        }
    }

    private void OnImeCompositionStarted(object? sender, EventArgs e)
    {
        if (InputMethod.Current == this)
        {
            OnImeCompositionStart();
        }
    }

    private void OnImeCompositionUpdated(object? sender, CompositionEventArgs e)
    {
        if (InputMethod.Current == this)
        {
            OnImeCompositionUpdate(e.Text, e.CursorPosition);
        }
    }

    private void OnImeCompositionEnded(object? sender, CompositionResultEventArgs e)
    {
        if (InputMethod.Current == this)
        {
            OnImeCompositionEnd(e.Result);
        }
    }

    #endregion

    #region Abstract Method Implementations

    /// <inheritdoc />
    protected override string GetText() => Text;

    /// <inheritdoc />
    protected override void SetText(string value) => Text = value;

    /// <inheritdoc />
    protected override double GetLineHeight()
    {
        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var fontSize = FontSize > 0 ? FontSize : 14;
        var fontMetrics = TextMeasurement.GetFontMetrics(fontFamily, fontSize);
        return fontMetrics.LineHeight;
    }

    /// <inheritdoc />
    protected override double MeasureTextWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var fontSize = FontSize > 0 ? FontSize : 14;
        var fontWeight = FontWeight.ToOpenTypeWeight();
        var fontStyle = FontStyle.ToOpenTypeStyle();
        var fontStretch = FontStretch.ToOpenTypeStretch();

        // Check if font settings changed, invalidate cache if so
        if (_cachedFontFamily != fontFamily ||
            _cachedFontSize != fontSize ||
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

        // Check cache first
        if (_textWidthCache.TryGetValue(text, out var cachedWidth))
            return cachedWidth;

        // Use DirectWrite native measurement via FormattedText
        var formattedText = new FormattedText(text, fontFamily, fontSize)
        {
            FontWeight = fontWeight,
            FontStyle = fontStyle,
            FontStretch = fontStretch
        };

        // Measure using native DirectWrite
        var usedNative = TextMeasurement.MeasureText(formattedText);

        double width;
        if (usedNative && formattedText.IsMeasured)
        {
            // Use accurate native measurement
            width = formattedText.Width;
        }
        else
        {
            // Fall back to estimation only when native is unavailable
            width = EstimateTextWidth(text);
        }

        // Cache the result (with size limit to prevent memory issues)
        if (_textWidthCache.Count >= MaxCacheSize)
        {
            // Simple eviction: clear half the cache
            var keysToRemove = _textWidthCache.Keys.Take(MaxCacheSize / 2).ToList();
            foreach (var key in keysToRemove)
                _textWidthCache.Remove(key);
        }
        _textWidthCache[text] = width;

        return width;
    }

    private double EstimateTextWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        // Use weighted character widths for more accurate estimation
        double width = 0;
        double fontSize = FontSize > 0 ? FontSize : 14;

        foreach (char c in text)
        {
            if (c == '\t')
            {
                // Tab is typically 4 spaces
                width += fontSize * 0.6 * 4;
            }
            else if (char.IsWhiteSpace(c))
            {
                // Space
                width += fontSize * 0.3;
            }
            else if (c >= 0x4E00 && c <= 0x9FFF)
            {
                // CJK characters are typically full-width
                width += fontSize;
            }
            else if (c >= 0x3000 && c <= 0x303F)
            {
                // CJK symbols and punctuation
                width += fontSize;
            }
            else if (c >= 0xFF00 && c <= 0xFFEF)
            {
                // Fullwidth forms
                width += fontSize;
            }
            else if (c == 'i' || c == 'l' || c == '|' || c == '!' || c == '.' || c == ',')
            {
                // Narrow characters (must check before general IsLower/IsUpper)
                width += fontSize * 0.3;
            }
            else if (c == 'm' || c == 'w' || c == 'M' || c == 'W')
            {
                // Wide characters (must check before general IsLower/IsUpper)
                width += fontSize * 0.85;
            }
            else if (char.IsUpper(c))
            {
                // Uppercase letters
                width += fontSize * 0.65;
            }
            else if (char.IsLower(c))
            {
                // Lowercase letters
                width += fontSize * 0.55;
            }
            else if (char.IsDigit(c))
            {
                // Digits
                width += fontSize * 0.6;
            }
            else
            {
                // Default width
                width += fontSize * 0.6;
            }
        }

        return width;
    }

    /// <inheritdoc />
    protected override int GetLineCount()
    {
        EnsureLinesValid();
        return _lines.Count;
    }

    /// <inheritdoc />
    protected override (int lineIndex, int columnIndex) GetLineColumnFromCharIndex(int charIndex)
    {
        EnsureLinesValid();
        for (int i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            if (charIndex <= line.StartIndex + line.Length)
            {
                return (i, charIndex - line.StartIndex);
            }
        }

        // At end of text
        if (_lines.Count > 0)
        {
            var lastLine = _lines[_lines.Count - 1];
            return (_lines.Count - 1, lastLine.Length);
        }

        return (0, 0);
    }

    /// <inheritdoc />
    protected override int GetCharIndexFromLineColumn(int lineIndex, int columnIndex)
    {
        EnsureLinesValid();
        if (lineIndex < 0 || lineIndex >= _lines.Count)
            return Text.Length;

        var line = _lines[lineIndex];
        return line.StartIndex + Math.Min(columnIndex, line.Length);
    }

    /// <inheritdoc />
    protected override double GetVerticalScrollExtentHeight(double lineHeight)
    {
        EnsureLinesValid();
        EnsureVisualLineCounts(GetCurrentTextContentWidth(), lineHeight);
        return Math.Max(1, GetTotalVisualLineCount()) * lineHeight;
    }

    /// <inheritdoc />
    protected override double GetVerticalScrollViewportHeight()
    {
        return GetCurrentTextContentHeight();
    }

    private double GetCurrentTextContentWidth()
    {
        var border = BorderThickness;
        var padding = Padding;

        double contentWidth = HasContentHost
            ? _textContentSize.Width
            : Math.Max(0, RenderSize.Width - border.Left - border.Right - padding.Left - padding.Right);

        if (contentWidth <= 0 || double.IsNaN(contentWidth) || double.IsInfinity(contentWidth))
        {
            contentWidth = Math.Max(0, ActualWidth - border.Left - border.Right - padding.Left - padding.Right);
        }

        if ((contentWidth <= 0 || double.IsNaN(contentWidth) || double.IsInfinity(contentWidth))
            && _cachedWrapWidth > 0
            && !double.IsNaN(_cachedWrapWidth)
            && !double.IsInfinity(_cachedWrapWidth))
        {
            contentWidth = _cachedWrapWidth;
        }

        return Math.Max(0, contentWidth);
    }

    private double GetCurrentTextContentHeight()
    {
        var border = BorderThickness;
        var padding = Padding;

        double contentHeight = Math.Max(0, RenderSize.Height - border.Top - border.Bottom - padding.Top - padding.Bottom);

        if (contentHeight <= 0 || double.IsNaN(contentHeight) || double.IsInfinity(contentHeight))
        {
            contentHeight = Math.Max(0, ActualHeight - border.Top - border.Bottom - padding.Top - padding.Bottom);
        }

        if ((contentHeight <= 0 || double.IsNaN(contentHeight) || double.IsInfinity(contentHeight))
            && _textContentSize.Height > 0
            && !double.IsNaN(_textContentSize.Height)
            && !double.IsInfinity(_textContentSize.Height))
        {
            contentHeight = _textContentSize.Height;
        }

        return Math.Max(0, contentHeight);
    }

    /// <summary>
    /// Wrap-aware caret visibility. The base class computes the caret's x as
    /// <c>GetCharacterXInLine(lineText, column)</c> — a single-line layout —
    /// and then adjusts <see cref="TextBoxBase._horizontalOffset"/> so that x
    /// fits inside the visible content width. On a wrapped paragraph that x
    /// can be the end-of-paragraph position of a 400-character run rendered
    /// as a single line (thousands of pixels), which snaps horizontalOffset
    /// way past the content and scrolls every glyph off-screen — the user
    /// reports "一选择就往右滚动视图,文字全没了".
    ///
    /// When <see cref="TextWrapping"/> is anything other than NoWrap the
    /// paragraph never overflows horizontally, so horizontalOffset must stay
    /// at 0. We only need vertical scrolling, and it has to be wrap-aware
    /// too — walk the cumulative visual-row offset to the caret's logical
    /// line, then ask DirectWrite for the caret's wrapped y within that
    /// line. In NoWrap mode the base path is already correct.
    /// </summary>
    protected override void EnsureCaretVisible()
    {
        if (TextWrapping == TextWrapping.NoWrap)
        {
            base.EnsureCaretVisible();
            return;
        }

        _horizontalOffset = 0;

        var border = BorderThickness;
        var padding = Padding;
        var lineHeight = Math.Round(GetLineHeight());

        double contentWidth = HasContentHost
            ? _textContentSize.Width
            : Math.Max(0, RenderSize.Width - border.Left - border.Right - padding.Left - padding.Right);
        double contentHeight = HasContentHost
            ? _textContentSize.Height
            : Math.Max(0, RenderSize.Height - border.Top - border.Bottom - padding.Top - padding.Bottom);

        if (contentWidth <= 0 || contentHeight <= 0)
        {
            _horizontalOffset = 0;
            _verticalOffset = Math.Max(0, Math.Round(_verticalOffset));
            return;
        }

        EnsureLinesValid();
        EnsureVisualLineCounts(contentWidth, lineHeight);

        var (lineIndex, columnIndex) = GetLineColumnFromCharIndex(_caretIndex);
        if (lineIndex < 0) lineIndex = 0;
        if (columnIndex < 0) columnIndex = 0;

        var lineText = GetLineTextInternal(lineIndex);
        var clampedColumn = Math.Clamp(columnIndex, 0, lineText.Length);

        double logicalTop = GetVisualRowsBeforeLogicalLine(lineIndex) * lineHeight;
        double caretTop = logicalTop;
        double caretHeight = lineHeight;

        if (lineText.Length > 0)
        {
            var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
            var fontSize = FontSize > 0 ? FontSize : 14;
            var fontWeight = FontWeight.ToOpenTypeWeight();
            var fontStyle = FontStyle.ToOpenTypeStyle();

            if (TextMeasurement.HitTestTextPositionWrapped(
                    lineText, fontFamily, fontSize, fontWeight, fontStyle,
                    (float)contentWidth, (uint)clampedColumn, false, out var hit))
            {
                caretTop = logicalTop + hit.CaretY;
                if (hit.CaretHeight > 0) caretHeight = hit.CaretHeight;
            }
        }

        if (contentHeight >= caretHeight)
        {
            if (caretTop < _verticalOffset)
                _verticalOffset = caretTop;
            else if (caretTop + caretHeight > _verticalOffset + contentHeight)
                _verticalOffset = caretTop + caretHeight - contentHeight;
        }
        else
        {
            _verticalOffset = 0;
        }

        _horizontalOffset = 0;
        _verticalOffset = Math.Round(Math.Max(0, _verticalOffset));
    }

    /// <summary>
    /// Wrap-aware mouse-to-caret mapping. The base implementation treats
    /// contentY / lineHeight as a logical-line index and does a single-line
    /// hit-test, which is wrong whenever a logical line wraps to multiple
    /// visual rows: clicking anywhere in rows 2+ snaps to the wrong logical
    /// line and the x-axis hit-test runs against a single-line layout whose
    /// character positions don't match the wrapped glyphs. We walk the same
    /// accumulated-visual-row offset DrawText uses to find the clicked
    /// logical line, then hit-test inside the wrapped layout at (x, localY)
    /// so the resulting caret index matches the glyph the user clicked on.
    /// </summary>
    protected override int GetCaretIndexFromPosition(Point position)
    {
        var border = BorderThickness;
        var padding = Padding;
        var lineHeight = Math.Round(GetLineHeight());

        // Mouse event positions come from e.GetPosition(this) which is
        // always relative to the TextBox itself — even when there's a
        // PART_ContentHost in the template, that host is positioned inside
        // the TextBox's border+padding, so we still have to subtract both
        // to reach the content-area origin where DrawText paints (this
        // matches the base class's GetCaretIndexFromPosition).
        var contentX = position.X - border.Left - padding.Left + _horizontalOffset;
        var contentY = position.Y - border.Top - padding.Top + _verticalOffset;

        // The wrap width used during rendering is the content-area width.
        // In PART_ContentHost mode that's the size the host was arranged to
        // (stored in _textContentSize); in direct mode we derive it the same
        // way OnRender does — RenderSize minus chrome. Using ActualWidth
        // unconditionally (as an earlier revision did) overshoots in
        // content-host mode, and if it happens to be 0 during first layout
        // the hit-test degenerates, yanking _horizontalOffset off-screen.
        double contentWidth = HasContentHost
            ? _textContentSize.Width
            : Math.Max(0, RenderSize.Width - border.Left - border.Right - padding.Left - padding.Right);
        if (contentWidth <= 0) contentWidth = Math.Max(0, ActualWidth - border.Left - border.Right - padding.Left - padding.Right);

        EnsureLinesValid();
        EnsureVisualLineCounts(contentWidth, lineHeight);

        // Mirror the render-side VerticalContentAlignment shift so clicks in
        // the empty space above vertically-centered text still map to the
        // first visible row rather than a non-existent row above the glyphs.
        double contentHeightForShift = HasContentHost
            ? _textContentSize.Height
            : Math.Max(0, RenderSize.Height - border.Top - border.Bottom - padding.Top - padding.Bottom);
        if (contentHeightForShift <= 0)
            contentHeightForShift = Math.Max(0, ActualHeight - border.Top - border.Bottom - padding.Top - padding.Bottom);
        var verticalContentShift = ComputeVerticalContentOffset(contentWidth, contentHeightForShift, lineHeight);
        contentY -= verticalContentShift;

        if (_lines.Count == 0)
            return 0;

        // Walk cumulative visual heights to find the logical line that owns
        // contentY. Past the last line, clamp to the last one (targetLineTopY
        // ends up pointing at that last line's top, not past it).
        int targetLineIndex = 0;
        double targetLineTopY = 0;
        double accumulatedY = 0;
        for (int i = 0; i < _lines.Count; i++)
        {
            int visualRows = i < _lineVisualCounts.Count ? _lineVisualCounts[i] : 1;
            if (visualRows < 1) visualRows = 1;
            double blockHeight = visualRows * lineHeight;

            targetLineIndex = i;
            targetLineTopY = accumulatedY;
            if (contentY < accumulatedY + blockHeight)
                break;
            accumulatedY += blockHeight;
        }

        var targetLine = _lines[targetLineIndex];
        if (targetLine.Length == 0)
            return targetLine.StartIndex;

        var lineText = Text.Substring(targetLine.StartIndex, targetLine.Length);
        var localY = (float)Math.Max(0, contentY - targetLineTopY);

        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var fontSize = FontSize > 0 ? FontSize : 14;
        var fontWeight = FontWeight.ToOpenTypeWeight();
        var fontStyle = FontStyle.ToOpenTypeStyle();

        // Use the wrap-aware hit-test. When the current TextWrapping is
        // NoWrap the render path passes MaxTextWidth=infinity, so mirror that
        // here: pass a very large maxWidth so DirectWrite doesn't wrap during
        // the hit-test either, keeping hit-test and glyph positions aligned.
        float hitMaxWidth = TextWrapping == TextWrapping.NoWrap
            ? 100000f
            : (float)Math.Max(1, contentWidth);

        // Row-boundary tolerance: DirectWrite assigns every pixel strictly by
        // line pitch (ascent+descent+lineGap). When the user clicks the
        // visual "bottom" of a wrapped line — below the glyphs, inside the
        // lineGap — DirectWrite technically lands that point in the NEXT
        // row's top band (row boundary - 0 to +lineGap/2), which feels like
        // the caret jumped a whole row down. Nudge pointY upward by half the
        // line pitch minus the font em-height so that clicks in that gap
        // still resolve to the intended row. In practice ~2 px for 12pt UI.
        float nudge = 0f;
        if (TextWrapping != TextWrapping.NoWrap)
        {
            double lineGapApprox = Math.Max(0, lineHeight - fontSize);
            nudge = (float)Math.Min(lineGapApprox * 0.5, 3.0);
        }
        float hitPointY = Math.Max(0f, localY - nudge);

        if (TextMeasurement.HitTestPointWrapped(
                lineText, fontFamily, fontSize, fontWeight, fontStyle,
                hitMaxWidth, (float)contentX, hitPointY, out var hit))
        {
            int column = (int)hit.TextPosition;
            if (hit.IsTrailingHit != 0)
                column++;
            return targetLine.StartIndex + Math.Clamp(column, 0, targetLine.Length);
        }

        // Native failed — fall back to the base single-line path.
        return base.GetCaretIndexFromPosition(position);
    }

    /// <inheritdoc />
    protected override string GetLineTextInternal(int lineIndex)
    {
        EnsureLinesValid();
        if (lineIndex < 0 || lineIndex >= _lines.Count)
            return string.Empty;

        var line = _lines[lineIndex];
        return Text.Substring(line.StartIndex, line.Length);
    }

    /// <inheritdoc />
    protected override int GetLineStartIndex(int lineIndex)
    {
        EnsureLinesValid();
        if (lineIndex < 0 || lineIndex >= _lines.Count)
            return 0;

        return _lines[lineIndex].StartIndex;
    }

    /// <inheritdoc />
    protected override int GetLineLengthInternal(int lineIndex)
    {
        EnsureLinesValid();
        if (lineIndex < 0 || lineIndex >= _lines.Count)
            return 0;

        return _lines[lineIndex].Length;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Gets the text of a specific line.
    /// </summary>
    public string GetLineText(int lineIndex) => GetLineTextInternal(lineIndex);

    /// <summary>
    /// Gets the length of a specific line.
    /// </summary>
    public int GetLineLength(int lineIndex) => GetLineLengthInternal(lineIndex);

    /// <summary>
    /// Gets the character index at the beginning of a line.
    /// </summary>
    public int GetCharacterIndexFromLineIndex(int lineIndex) => GetLineStartIndex(lineIndex);

    /// <summary>
    /// Gets the line index containing the specified character index.
    /// </summary>
    public int GetLineIndexFromCharacterIndex(int charIndex)
    {
        var (lineIndex, _) = GetLineColumnFromCharIndex(charIndex);
        return lineIndex;
    }

    /// <summary>
    /// Scrolls to a specific line.
    /// </summary>
    public void ScrollToLine(int lineIndex)
    {
        EnsureLinesValid();
        if (lineIndex < 0 || lineIndex >= _lines.Count)
            return;

        // Round line height for consistent scrolling
        var lineHeight = Math.Round(GetLineHeight());
        if (TextWrapping != TextWrapping.NoWrap)
        {
            EnsureVisualLineCounts(GetCurrentTextContentWidth(), lineHeight);
            VerticalOffset = GetVisualRowsBeforeLogicalLine(lineIndex) * lineHeight;
        }
        else
        {
            VerticalOffset = lineIndex * lineHeight;
        }
    }

    /// <summary>
    /// Appends text to the end.
    /// </summary>
    public void AppendText(string textData)
    {
        if (string.IsNullOrEmpty(textData))
            return;

        Text += textData;
        CaretIndex = Text.Length;
    }

    /// <summary>
    /// Clears all text.
    /// </summary>
    public void Clear()
    {
        Text = string.Empty;
        _caretIndex = 0;
        _selectionStart = 0;
        _selectionLength = 0;
    }

    #endregion

    #region Line Management

    private void EnsureLinesValid()
    {
        if (!_linesDirty)
            return;

        _lines.Clear();
        _lineVisualCounts.Clear();
        _cachedWrapWidth = double.NaN;
        var text = Text;

        if (string.IsNullOrEmpty(text))
        {
            _lines.Add(new TextLine(0, 0));
            _linesDirty = false;
            return;
        }

        var lineStart = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                _lines.Add(new TextLine(lineStart, i - lineStart));
                lineStart = i + 1;
            }
            else if (text[i] == '\r')
            {
                var length = i - lineStart;
                if (i + 1 < text.Length && text[i + 1] == '\n')
                {
                    _lines.Add(new TextLine(lineStart, length));
                    i++;
                    lineStart = i + 1;
                }
                else
                {
                    _lines.Add(new TextLine(lineStart, length));
                    lineStart = i + 1;
                }
            }
        }

        // Add last line
        _lines.Add(new TextLine(lineStart, text.Length - lineStart));
        _linesDirty = false;
    }

    /// <summary>
    /// Computes how many visual rows each logical line (as split by \n) actually
    /// occupies when the current <see cref="TextWrapping"/> is applied at the
    /// given wrap width. Results are cached until wrap width, wrap mode, or
    /// font metrics change. Without this, each logical line is assumed to be
    /// exactly one row tall, so a long wrap-enabled line gets DirectWrite to
    /// wrap its glyphs into multiple rows but the next logical line is drawn
    /// at y = index * lineHeight and paints over those extra wrapped rows.
    /// </summary>
    private void EnsureVisualLineCounts(double wrapWidth, double lineHeight)
    {
        EnsureLinesValid();

        var wrapMode = TextWrapping;
        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        var fontSize = FontSize > 0 ? FontSize : 14;
        var fontWeight = FontWeight.ToOpenTypeWeight();
        var fontStyle = FontStyle.ToOpenTypeStyle();

        if (_lineVisualCounts.Count == _lines.Count
            && _cachedWrapWidth == wrapWidth
            && _cachedWrapMode == wrapMode
            && _cachedVisualLineHeight == lineHeight
            && string.Equals(_cachedVisualFontFamily, fontFamily, StringComparison.Ordinal)
            && _cachedVisualFontSize == fontSize
            && _cachedVisualFontWeight == fontWeight
            && _cachedVisualFontStyle == fontStyle)
        {
            return;
        }

        _lineVisualCounts.Clear();
        _cachedWrapWidth = wrapWidth;
        _cachedWrapMode = wrapMode;
        _cachedVisualLineHeight = lineHeight;
        _cachedVisualFontFamily = fontFamily;
        _cachedVisualFontSize = fontSize;
        _cachedVisualFontWeight = fontWeight;
        _cachedVisualFontStyle = fontStyle;

        var text = Text;
        bool canWrap = wrapMode != TextWrapping.NoWrap && wrapWidth > 0 && !double.IsInfinity(wrapWidth);

        for (int i = 0; i < _lines.Count; i++)
        {
            int count = 1;
            var line = _lines[i];
            if (canWrap && line.Length > 0)
            {
                var lineText = text.Substring(line.StartIndex, line.Length);
                // Always ask DirectWrite: a managed-width short-circuit
                // ("if lineWidth > wrapWidth") can disagree with the real
                // DirectWrite layout at the edge (trailing whitespace, wide
                // CJK characters, kerning) and would mark a paragraph as
                // 1-row when the renderer actually wraps to 2, making the
                // next logical line paint over the wrapped tail.
                var ft = new FormattedText(lineText, fontFamily, fontSize)
                {
                    MaxTextWidth = wrapWidth,
                    MaxTextHeight = double.MaxValue,
                    FontWeight = fontWeight,
                    FontStyle = fontStyle,
                };
                if (TextMeasurement.MeasureText(ft) && ft.LineCount > 0)
                {
                    count = ft.LineCount;
                }
                else
                {
                    // Fallback: approximate with managed width measurement.
                    var lineWidth = MeasureTextWidth(lineText);
                    if (lineWidth > wrapWidth)
                        count = Math.Max(2, (int)Math.Ceiling(lineWidth / wrapWidth));
                }
            }
            _lineVisualCounts.Add(count);
        }
    }

    /// <summary>
    /// Returns the total visual-row count across all logical lines (including
    /// wrapping). Call <see cref="EnsureVisualLineCounts"/> first to populate.
    /// </summary>
    private int GetTotalVisualLineCount()
    {
        int total = 0;
        for (int i = 0; i < _lineVisualCounts.Count; i++)
            total += _lineVisualCounts[i];
        return total == 0 ? Math.Max(1, _lines.Count) : total;
    }

    /// <summary>
    /// Returns how many visual rows precede the given logical line (the sum
    /// of visual-row counts for lines [0..logicalLineIndex)). Used by caret /
    /// selection / IME overlay code so their y-coordinate tracks wrapping in
    /// earlier paragraphs. Call <see cref="EnsureVisualLineCounts"/> first.
    /// </summary>
    private int GetVisualRowsBeforeLogicalLine(int logicalLineIndex)
    {
        int sum = 0;
        int limit = Math.Min(logicalLineIndex, _lineVisualCounts.Count);
        for (int i = 0; i < limit; i++)
            sum += _lineVisualCounts[i];
        return sum;
    }

    /// <summary>
    /// Returns how far the rendered text should be nudged downward inside the
    /// content area so it honors <see cref="Control.VerticalContentAlignment"/>.
    /// Returns 0 when the alignment is Top/Stretch or the text already fills
    /// the content area (scrolling / multi-line cases must start at the top so
    /// the ScrollViewer above this control can expose subsequent rows). Used
    /// by both the render path and mouse-to-caret hit-testing so clicks land
    /// on the glyph that is actually painted.
    /// </summary>
    private double ComputeVerticalContentOffset(double contentWidth, double contentHeight, double lineHeight)
    {
        var alignment = VerticalContentAlignment;
        if (alignment == VerticalAlignment.Top || alignment == VerticalAlignment.Stretch)
            return 0;
        if (contentHeight <= 0 || lineHeight <= 0)
            return 0;

        EnsureLinesValid();
        EnsureVisualLineCounts(Math.Max(0, contentWidth), lineHeight);
        double totalTextHeight = Math.Max(1, GetTotalVisualLineCount()) * lineHeight;
        if (totalTextHeight >= contentHeight)
            return 0;

        var slack = contentHeight - totalTextHeight;
        return alignment == VerticalAlignment.Center
            ? Math.Floor(slack / 2)
            : Math.Floor(slack);
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // If using template, delegate to base class which handles template root
        // The TextBoxContentHost will call MeasureTextContent for the text area
        if (Template != null)
        {
            return base.MeasureOverride(availableSize);
        }

        // Direct rendering mode - calculate size based on content
        var padding = Padding;
        var border = BorderThickness;
        // Round line height for consistent layout
        var lineHeight = Math.Round(GetLineHeight());

        EnsureLinesValid();
        var wrapWidth = Math.Max(0, availableSize.Width - padding.Left - padding.Right - border.Left - border.Right);
        EnsureVisualLineCounts(wrapWidth, lineHeight);

        double textHeight;
        if (AcceptsReturn)
        {
            // Multi-line: height based on total visual rows (logical lines +
            // any extra rows produced by wrapping). Without the wrap-aware
            // count, wrapped content of one logical line bleeds into the row
            // of the next logical line and paints on top of it.
            textHeight = Math.Max(1, GetTotalVisualLineCount()) * lineHeight;
        }
        else
        {
            // Single-line
            textHeight = lineHeight;
        }

        // Add padding and border
        var desiredHeight = textHeight + padding.Top + padding.Bottom + border.Top + border.Bottom;

        // Minimum size
        var minHeight = lineHeight + padding.Top + padding.Bottom + border.Top + border.Bottom;
        desiredHeight = Math.Max(desiredHeight, minHeight);

        return new Size(
            availableSize.Width,
            Math.Min(desiredHeight, availableSize.Height));
    }

    /// <inheritdoc />
    internal override Size MeasureTextContent(Size availableSize)
    {
        EnsureLinesValid();

        var lineHeight = Math.Round(GetLineHeight());

        // Pick the wrap width the renderer will actually use at Arrange time.
        // If the measure pass gave us Infinity (e.g. ScrollViewer's overflow
        // pass, a stack-panel, or any measure-with-unbounded-width caller),
        // wrap-counting against Infinity reports the paragraph as "1 row"
        // (canWrap=false) and DesiredSize.Height under-reports the real
        // wrapped height — the enclosing ScrollViewer's extent ends up short
        // and the user can't scroll past the first N pre-wrap rows. Fall
        // back to the last arranged width so Measure and Draw wrap to the
        // same width.
        double wrapWidth = availableSize.Width;
        if (double.IsInfinity(wrapWidth) || double.IsNaN(wrapWidth) || wrapWidth <= 0)
        {
            if (_textContentSize.Width > 0)
                wrapWidth = _textContentSize.Width;
        }

        EnsureVisualLineCounts(wrapWidth, lineHeight);

        double textHeight;
        if (AcceptsReturn)
        {
            textHeight = Math.Max(1, GetTotalVisualLineCount()) * lineHeight;
        }
        else
        {
            textHeight = lineHeight;
        }

        // Return the TRUE desired height, even when it exceeds availableSize.
        // Clamping here would tell the enclosing ScrollViewer that the content
        // fits in the viewport, so its extent would equal the viewport height
        // and the vertical scrollbar would never expose the rows past the
        // first page — exactly the "can only scroll partway" symptom users
        // see on long readme / wrapped text. ScrollViewer is responsible for
        // clipping; our job is to report the full content size.
        //
        // Width: use the ACTUAL wrap width we measured against (not the
        // potentially-Infinity availableSize.Width) so the ScrollViewer
        // extent isn't Infinity on the horizontal axis either.
        double desiredWidth = (double.IsInfinity(availableSize.Width) || double.IsNaN(availableSize.Width) || availableSize.Width <= 0)
            ? (wrapWidth > 0 ? wrapWidth : 0)
            : availableSize.Width;
        return new Size(desiredWidth, textHeight);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        base.OnRender(drawingContext);

        // If using content host, text rendering is handled by TextBoxContentHost
        if (HasContentHost)
            return;

        // Direct rendering mode
        if (drawingContext is not DrawingContext dc)
            return;

        var border = BorderThickness;
        var padding = Padding;
        var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);
        var lineHeight = Math.Round(GetLineHeight());

        EnsureLinesValid();

        // Draw background and border (no template = direct rendering)
        var cornerRadius = CornerRadius;
        var strokeThickness = border.Left;
        var borderRect = ControlRenderGeometry.GetStrokeAlignedRect(bounds, strokeThickness);
        var borderRadius = ControlRenderGeometry.GetStrokeAlignedCornerRadius(cornerRadius, strokeThickness);
        if (Background != null)
        {
            dc.DrawRoundedRectangle(Background, null, borderRect, borderRadius);
        }

        if (BorderBrush != null && strokeThickness > 0)
        {
            var borderPen = new Pen(BorderBrush, strokeThickness);
            dc.DrawRoundedRectangle(null, borderPen, borderRect, borderRadius);
        }

        // Content area
        var contentRect = new Rect(
            Math.Round(border.Left + padding.Left),
            Math.Round(border.Top + padding.Top),
            Math.Max(0, Math.Round(bounds.Width - border.Left - border.Right - padding.Left - padding.Right)),
            Math.Max(0, Math.Round(bounds.Height - border.Top - border.Bottom - padding.Top - padding.Bottom)));

        // Render text content
        RenderTextContentCore(dc, contentRect, lineHeight);

        // Focus indicator is painted by FocusVisualManager into the adorner layer.
    }

    /// <inheritdoc />
    internal override void RenderTextContent(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc)
            return;

        EnsureLinesValid();

        var lineHeight = Math.Round(GetLineHeight());
        var contentRect = new Rect(0, 0, _textContentSize.Width, _textContentSize.Height);

        RenderTextContentCore(dc, contentRect, lineHeight);
    }

    /// <summary>
    /// Core text rendering logic used by both direct rendering and content host modes.
    /// </summary>
    private void RenderTextContentCore(DrawingContext dc, Rect contentRect, double lineHeight)
    {
        // Clip to the original content area — VerticalContentAlignment only
        // shifts where the text is painted inside that rectangle, it must not
        // trim padding off the bottom (selection / caret reuse the same clip).
        dc.PushClip(new RectangleGeometry(contentRect));

        // Offset the rendering rectangle so the text, caret, selection, IME
        // overlay and placeholder all honor VerticalContentAlignment. Keep
        // the original width/height so the horizontal wrap width and the
        // visible-row cull test stay identical to pre-offset behavior.
        var verticalContentShift = ComputeVerticalContentOffset(contentRect.Width, contentRect.Height, lineHeight);
        if (verticalContentShift > 0)
        {
            contentRect = new Rect(contentRect.X, contentRect.Y + verticalContentShift, contentRect.Width, contentRect.Height);
        }

        var text = Text;

        // Draw selection background
        if (_selectionLength > 0 && IsKeyboardFocused)
        {
            DrawSelection(dc, contentRect, lineHeight);
        }

        // Draw text or placeholder
        if (string.IsNullOrEmpty(text))
        {
            if (!string.IsNullOrEmpty(PlaceholderText))
            {
                var placeholderBrush = ResolvePlaceholderBrush();
                var roundedHorizontalOffset = Math.Round(_horizontalOffset);
                var roundedVerticalOffset = Math.Round(_verticalOffset);
                var formattedPlaceholder = new FormattedText(PlaceholderText, FontFamily ?? FrameworkElement.DefaultFontFamilyName, FontSize)
                {
                    Foreground = placeholderBrush,
                    MaxTextWidth = contentRect.Width,
                    MaxTextHeight = contentRect.Height,
                    Trimming = TextTrimming
                };
                dc.DrawText(formattedPlaceholder, new Point(contentRect.X - roundedHorizontalOffset, contentRect.Y - roundedVerticalOffset));
            }
        }
        else
        {
            DrawText(dc, contentRect, lineHeight);

            // Draw spelling error underlines
            if (IsSpellCheckEnabled && _spellingErrors.Count > 0)
            {
                DrawSpellingErrors(dc, contentRect, lineHeight);
            }
        }

        // Draw IME composition string
        if (_isImeComposing && !string.IsNullOrEmpty(_imeCompositionString))
        {
            DrawImeComposition(dc, contentRect, lineHeight);
        }

        // Draw caret
        if (IsFocused && !IsReadOnly)
        {
            DrawCaret(dc, contentRect, lineHeight);
        }

        dc.Pop(); // Pop clip
    }

    private Brush ResolveFocusedBorderBrush()
    {
        return TryFindResource("ControlBorderFocused") as Brush
            ?? TryFindResource("AccentBrush") as Brush
            ?? s_fallbackFocusBorderBrush;
    }

    private Brush ResolvePlaceholderBrush()
    {
        return TryFindResource("TextPlaceholder") as Brush
            ?? TryFindResource("TextFillColorTertiaryBrush") as Brush
            ?? s_fallbackPlaceholderTextBrush;
    }

    private void DrawText(DrawingContext dc, Rect contentRect, double lineHeight)
    {
        var text = Text;
        var textBrush = ResolveTextForegroundBrush();
        // Round scroll offsets to prevent sub-pixel jittering
        var roundedVerticalOffset = Math.Round(_verticalOffset);
        var roundedHorizontalOffset = Math.Round(_horizontalOffset);

        var wrapMode = TextWrapping;
        // Wrap width for DirectWrite must match the width the wrap-count cache
        // was measured against so our y accounting and DirectWrite's wrapping
        // behavior stay in lockstep.
        var wrapWidth = Math.Max(0, contentRect.Width);
        EnsureVisualLineCounts(wrapWidth, lineHeight);

        double accumulatedY = 0;
        for (int i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            int visualRows = i < _lineVisualCounts.Count ? _lineVisualCounts[i] : 1;
            if (visualRows < 1) visualRows = 1;
            double lineBlockHeight = visualRows * lineHeight;

            // Round y position to prevent sub-pixel jittering from floating-point accumulation errors
            var y = Math.Round(contentRect.Y + accumulatedY - roundedVerticalOffset);
            accumulatedY += lineBlockHeight;

            // Skip lines outside visible area
            if (y + lineBlockHeight < contentRect.Y || y > contentRect.Y + contentRect.Height)
                continue;

            if (line.Length == 0)
                continue;

            var lineText = text.Substring(line.StartIndex, line.Length);
            // When wrap is disabled, let the line extend horizontally past the
            // viewport and rely on the dirty-region clip to hide the overflow.
            // If we instead hand DirectWrite a finite MaxTextWidth, it wraps
            // the glyphs into additional visual rows that paint over the next
            // logical line's y position and produce the "overlapping text"
            // artifact users see in PropertyGrid inline editors.
            double maxTextWidth = wrapMode == TextWrapping.NoWrap
                ? double.MaxValue
                : wrapWidth;

            var formattedText = new FormattedText(lineText, FontFamily ?? FrameworkElement.DefaultFontFamilyName, FontSize)
            {
                Foreground = textBrush,
                MaxTextWidth = maxTextWidth,
                MaxTextHeight = lineBlockHeight,
                Trimming = TextTrimming,
                FontWeight = FontWeight.ToOpenTypeWeight(),
                FontStyle = FontStyle.ToOpenTypeStyle(),
            };

            var x = contentRect.X - roundedHorizontalOffset;

            // Apply text alignment
            var lineWidth = MeasureTextWidth(lineText);
            if (TextAlignment == TextAlignment.Center)
            {
                x = contentRect.X + (contentRect.Width - lineWidth) / 2;
            }
            else if (TextAlignment == TextAlignment.Right)
            {
                x = contentRect.X + contentRect.Width - lineWidth;
            }

            // Round to pixel boundaries to prevent sub-pixel jittering
            dc.DrawText(formattedText, new Point(x, y));
        }
    }

    private void DrawSpellingErrors(DrawingContext dc, Rect contentRect, double lineHeight)
    {
        // Red wavy underline pen for spelling errors
        var errorPen = s_spellErrorPen;
        var text = Text;
        // Round scroll offsets to prevent sub-pixel jittering
        var roundedVerticalOffset = Math.Round(_verticalOffset);
        var roundedHorizontalOffset = Math.Round(_horizontalOffset);
        EnsureVisualLineCounts(Math.Max(0, contentRect.Width), lineHeight);

        foreach (var error in _spellingErrors)
        {
            // Find which line(s) this error spans
            double accumulatedY = 0;
            for (int i = 0; i < _lines.Count; i++)
            {
                var line = _lines[i];
                var lineEnd = line.StartIndex + line.Length;
                int visualRows = i < _lineVisualCounts.Count ? _lineVisualCounts[i] : 1;
                if (visualRows < 1) visualRows = 1;
                double lineBlockHeight = visualRows * lineHeight;
                var y = Math.Round(contentRect.Y + accumulatedY - roundedVerticalOffset);
                accumulatedY += lineBlockHeight;

                // Skip lines outside visible area
                if (y + lineBlockHeight < contentRect.Y || y > contentRect.Y + contentRect.Height)
                    continue;

                // Check if error intersects this line
                var errorEnd = error.StartIndex + error.Length;
                if (error.StartIndex < lineEnd && errorEnd > line.StartIndex)
                {
                    var startInLine = Math.Max(0, error.StartIndex - line.StartIndex);
                    var endInLine = Math.Min(line.Length, errorEnd - line.StartIndex);

                    if (endInLine > startInLine)
                    {
                        var lineText = text.Substring(line.StartIndex, line.Length);

                        var startX = Math.Round(contentRect.X + GetCharacterXInLine(lineText, startInLine) - roundedHorizontalOffset);
                        var endX = Math.Round(contentRect.X + GetCharacterXInLine(lineText, endInLine) - roundedHorizontalOffset);
                        var underlineY = y + lineHeight - 2;

                        // Draw wavy underline
                        DrawWavyLine(dc, errorPen, startX, endX, underlineY);
                    }
                }
            }
        }
    }

    private void DrawWavyLine(DrawingContext dc, Pen pen, double startX, double endX, double y)
    {
        // Draw a simple wavy line approximation using short line segments
        const double waveHeight = 2;
        const double waveWidth = 4;

        double x = startX;
        bool up = true;

        while (x < endX)
        {
            double nextX = Math.Min(x + waveWidth, endX);
            double y1 = up ? y - waveHeight : y;
            double y2 = up ? y : y - waveHeight;

            dc.DrawLine(pen, new Point(x, y1), new Point(nextX, y2));

            x = nextX;
            up = !up;
        }
    }

    private void DrawSelection(DrawingContext dc, Rect contentRect, double lineHeight)
    {
        var selectionBrush = ResolveSelectionBrush();
        if (selectionBrush == null)
            return;

        var text = Text;
        var selectionEnd = _selectionStart + _selectionLength;
        // Round scroll offsets to prevent sub-pixel jittering
        var roundedVerticalOffset = Math.Round(_verticalOffset);
        var roundedHorizontalOffset = Math.Round(_horizontalOffset);

        // Keep y-accounting aligned with DrawText when wrapping is on — each
        // logical line may span multiple visual rows so we walk the same
        // cumulative offset instead of i * lineHeight.
        var wrapWidth = Math.Max(0, contentRect.Width);
        EnsureVisualLineCounts(wrapWidth, lineHeight);
        double accumulatedY = 0;
        for (int i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            var lineEnd = line.StartIndex + line.Length;
            int visualRows = i < _lineVisualCounts.Count ? _lineVisualCounts[i] : 1;
            if (visualRows < 1) visualRows = 1;
            double lineBlockHeight = visualRows * lineHeight;
            var y = Math.Round(contentRect.Y + accumulatedY - roundedVerticalOffset);
            accumulatedY += lineBlockHeight;

            // Skip lines outside visible area
            if (y + lineBlockHeight < contentRect.Y || y > contentRect.Y + contentRect.Height)
                continue;

            // Check if selection intersects this line
            if (_selectionStart <= lineEnd && selectionEnd >= line.StartIndex)
            {
                var startInLine = Math.Max(0, _selectionStart - line.StartIndex);
                var endInLine = Math.Min(line.Length, selectionEnd - line.StartIndex);

                if (endInLine > startInLine)
                {
                    var lineText = text.Substring(line.StartIndex, line.Length);

                    if (visualRows > 1)
                    {
                        // Wrapped paragraph: ask DirectWrite for the real (x, y)
                        // of both selection endpoints inside the wrapped layout,
                        // then paint one rect per visual row — first row from
                        // startCaret to the right edge, middle rows full width,
                        // last row from the left edge to endCaret. This matches
                        // the glyph extents exactly, so there is no trailing
                        // whitespace tail on the final wrapped row.
                        var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
                        var fontSize = FontSize > 0 ? FontSize : 14;
                        var fontWeight = FontWeight.ToOpenTypeWeight();
                        var fontStyle = FontStyle.ToOpenTypeStyle();
                        float wrapWidthF = (float)wrapWidth;

                        bool gotStart = TextMeasurement.HitTestTextPositionWrapped(
                            lineText, fontFamily, fontSize, fontWeight, fontStyle,
                            wrapWidthF, (uint)startInLine, false, out var startHit);
                        bool gotEnd = TextMeasurement.HitTestTextPositionWrapped(
                            lineText, fontFamily, fontSize, fontWeight, fontStyle,
                            wrapWidthF, (uint)Math.Max(0, endInLine - 1), true, out var endHit);

                        if (gotStart && gotEnd)
                        {
                            double rowHeight = endHit.CaretHeight > 0 ? endHit.CaretHeight : lineHeight;
                            double startRowTop = y + startHit.CaretY;
                            double endRowTop = y + endHit.CaretY;
                            double leftEdge = contentRect.X;
                            double rightEdge = contentRect.X + contentRect.Width;

                            if (Math.Abs(endHit.CaretY - startHit.CaretY) < 0.5)
                            {
                                // Selection stays on one wrapped row.
                                double left = Math.Round(contentRect.X + startHit.CaretX - roundedHorizontalOffset);
                                double right = Math.Round(contentRect.X + endHit.CaretX - roundedHorizontalOffset);
                                if (right < left) (left, right) = (right, left);
                                var selRect = new Rect(left, Math.Round(startRowTop), Math.Max(right - left, 1), rowHeight);
                                dc.DrawRectangle(selectionBrush, null, selRect);
                            }
                            else
                            {
                                // First wrapped row: from start caret to right edge.
                                double firstLeft = Math.Round(contentRect.X + startHit.CaretX - roundedHorizontalOffset);
                                var firstRect = new Rect(
                                    firstLeft,
                                    Math.Round(startRowTop),
                                    Math.Max(rightEdge - firstLeft, 1),
                                    rowHeight);
                                dc.DrawRectangle(selectionBrush, null, firstRect);

                                // Middle rows: full content width. Step by
                                // rowHeight between startRowTop and endRowTop.
                                for (double rowTop = startRowTop + rowHeight; rowTop < endRowTop - 0.5; rowTop += rowHeight)
                                {
                                    var midRect = new Rect(
                                        leftEdge,
                                        Math.Round(rowTop),
                                        Math.Max(rightEdge - leftEdge, 1),
                                        rowHeight);
                                    dc.DrawRectangle(selectionBrush, null, midRect);
                                }

                                // Last wrapped row: from left edge to end caret.
                                double lastRight = Math.Round(contentRect.X + endHit.CaretX - roundedHorizontalOffset);
                                if (lastRight > leftEdge)
                                {
                                    var lastRect = new Rect(
                                        leftEdge,
                                        Math.Round(endRowTop),
                                        Math.Max(lastRight - leftEdge, 1),
                                        rowHeight);
                                    dc.DrawRectangle(selectionBrush, null, lastRect);
                                }
                            }
                        }
                        else
                        {
                            // Native hit-test unavailable — fall back to a
                            // block-fill covering the paragraph so the user
                            // still sees selection, just without per-row
                            // precision on the trailing edge.
                            var selRect = new Rect(contentRect.X, y, contentRect.Width, lineBlockHeight);
                            dc.DrawRectangle(selectionBrush, null, selRect);
                        }
                    }
                    else
                    {
                        // Non-wrapped line: precise selection rectangle.
                        var startXOffset = GetCharacterXInLine(lineText, startInLine);
                        var endXOffset = GetCharacterXInLine(lineText, endInLine);
                        var startX = Math.Round(contentRect.X + startXOffset - roundedHorizontalOffset);
                        var width = Math.Max(Math.Round(endXOffset - startXOffset), 1);

                        var selRect = new Rect(startX, y, width, lineHeight);
                        dc.DrawRectangle(selectionBrush, null, selRect);
                    }
                }

                // Selection extends past line end (include newline in selection visual)
                if (selectionEnd > lineEnd && i < _lines.Count - 1)
                {
                    var lineText = text.Substring(line.StartIndex, line.Length);
                    var startX = Math.Round(contentRect.X + GetCharacterXInLine(lineText, lineText.Length) - roundedHorizontalOffset);
                    var selRect = new Rect(startX, y, Math.Round(FontSize * 0.3), lineHeight);
                    dc.DrawRectangle(selectionBrush, null, selRect);
                }
            }
        }
    }

    private void DrawImeComposition(DrawingContext dc, Rect contentRect, double lineHeight)
    {
        if (string.IsNullOrEmpty(_imeCompositionString))
            return;

        var text = Text;
        var (lineIndex, columnIndex) = GetLineColumnFromCharIndex(_caretIndex);

        // Ensure valid indices
        if (lineIndex < 0) lineIndex = 0;
        if (columnIndex < 0) columnIndex = 0;

        var lineText = GetLineTextInternal(lineIndex);
        var clampedColumn = Math.Clamp(columnIndex, 0, lineText.Length);

        // Round scroll offsets to prevent sub-pixel jittering
        var roundedHorizontalOffset = Math.Round(_horizontalOffset);
        var roundedVerticalOffset = Math.Round(_verticalOffset);

        var x = Math.Round(contentRect.X + GetCharacterXInLine(lineText, clampedColumn) - roundedHorizontalOffset);
        // Use wrap-aware visual row offset so IME overlay stays on the same
        // line as the text it is composing in.
        EnsureVisualLineCounts(Math.Max(0, contentRect.Width), lineHeight);
        var visualRowOffset = GetVisualRowsBeforeLogicalLine(lineIndex);
        var y = Math.Round(contentRect.Y + visualRowOffset * lineHeight - roundedVerticalOffset);

        // Draw composition background
        var compositionWidth = MeasureTextWidth(_imeCompositionString);
        var compositionBgBrush = s_compositionBgBrush;
        dc.DrawRectangle(compositionBgBrush, null, new Rect(x, y, compositionWidth, lineHeight));

        // Draw composition text
        var compositionText = new FormattedText(_imeCompositionString, FontFamily ?? FrameworkElement.DefaultFontFamilyName, FontSize)
        {
            Foreground = s_compositionTextBrush,
            MaxTextWidth = contentRect.Width,
            MaxTextHeight = lineHeight
        };
        dc.DrawText(compositionText, new Point(x, y));

        // Draw underline for composition string
        var underlinePen = s_compositionUnderlinePen;
        dc.DrawLine(underlinePen, new Point(x, y + lineHeight - 2), new Point(x + compositionWidth, y + lineHeight - 2));

        // Draw cursor within composition string
        if (_imeCompositionCursor >= 0 && _imeCompositionCursor <= _imeCompositionString.Length)
        {
            var cursorTextWidth = MeasureTextWidth(_imeCompositionString.Substring(0, _imeCompositionCursor));
            var cursorX = x + cursorTextWidth;
            var cursorPen = s_compositionCursorPen;
            dc.DrawLine(cursorPen, new Point(cursorX, y + 2), new Point(cursorX, y + lineHeight - 2));
        }
    }

    private void DrawCaret(DrawingContext dc, Rect contentRect, double lineHeight)
    {
        // Update and get the current caret opacity first (needed for animation to progress)
        var caretOpacity = UpdateCaretAnimation();

        // Note: Caret animation is handled by the timer in TextBoxBase.StartCaretTimer()
        // which calls InvalidateVisual() at regular intervals. No fallback needed here.

        var caretBrush = ResolveCaretBrush();
        if (caretBrush == null)
            return;

        // During IME composition, don't draw regular caret
        if (_isImeComposing)
            return;

        // Skip drawing if fully transparent
        if (caretOpacity < 0.01)
            return;

        var (lineIndex, columnIndex) = GetLineColumnFromCharIndex(_caretIndex);

        // Ensure valid indices
        if (lineIndex < 0) lineIndex = 0;
        if (columnIndex < 0) columnIndex = 0;

        var lineText = GetLineTextInternal(lineIndex);
        var clampedColumn = Math.Clamp(columnIndex, 0, lineText.Length);

        // Round scroll offsets to prevent sub-pixel jittering
        var roundedHorizontalOffset = Math.Round(_horizontalOffset);
        var roundedVerticalOffset = Math.Round(_verticalOffset);

        var wrapWidthForCaret = Math.Max(0, contentRect.Width);
        EnsureVisualLineCounts(wrapWidthForCaret, lineHeight);
        var visualRowOffset = GetVisualRowsBeforeLogicalLine(lineIndex);
        var paragraphTop = contentRect.Y + visualRowOffset * lineHeight - roundedVerticalOffset;

        double caretX;
        double caretY;
        double caretHeight = lineHeight;

        int visualRowsForLine = lineIndex < _lineVisualCounts.Count ? _lineVisualCounts[lineIndex] : 1;
        if (visualRowsForLine > 1 && lineText.Length > 0)
        {
            // Caret lives inside a wrapped paragraph — the base-class
            // GetCharacterXInLine returns a single-line x that bears no
            // relation to the wrapped glyph position, and anchoring y to the
            // paragraph top makes the caret jump to the first wrapped row
            // regardless of which wrap row the caret index actually belongs
            // to. Use DirectWrite's wrapped hit-test to land on the exact
            // (x, y) of the glyph the user clicked.
            var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
            var fontSize = FontSize > 0 ? FontSize : 14;
            var fontWeight = FontWeight.ToOpenTypeWeight();
            var fontStyle = FontStyle.ToOpenTypeStyle();

            if (TextMeasurement.HitTestTextPositionWrapped(
                    lineText, fontFamily, fontSize, fontWeight, fontStyle,
                    (float)wrapWidthForCaret, (uint)clampedColumn, false, out var caretHit))
            {
                caretX = Math.Round(contentRect.X + caretHit.CaretX - roundedHorizontalOffset);
                caretY = Math.Round(paragraphTop + caretHit.CaretY);
                if (caretHit.CaretHeight > 0) caretHeight = caretHit.CaretHeight;
            }
            else
            {
                caretX = Math.Round(contentRect.X + GetCharacterXInLine(lineText, clampedColumn) - roundedHorizontalOffset);
                caretY = Math.Round(paragraphTop);
            }
        }
        else
        {
            // Single-row line: base-class geometry is correct.
            caretX = Math.Round(contentRect.X + GetCharacterXInLine(lineText, clampedColumn) - roundedHorizontalOffset);
            caretY = Math.Round(paragraphTop);
        }

        var x = caretX;
        var y = caretY;

        // Create a brush with the animated opacity
        Brush caretBrushWithOpacity;
        if (caretBrush is SolidColorBrush solidBrush)
        {
            var color = solidBrush.Color;
            var alpha = (byte)(color.A * caretOpacity);
            caretBrushWithOpacity = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        }
        else
        {
            caretBrushWithOpacity = caretBrush;
        }

        if (_caretPen == null || _caretPenBrush != caretBrushWithOpacity || _caretPenOpacity != caretOpacity)
        {
            _caretPen = new Pen(caretBrushWithOpacity, 1.5);
            _caretPenBrush = caretBrushWithOpacity;
            _caretPenOpacity = caretOpacity;
        }
        dc.DrawLine(_caretPen, new Point(x, y), new Point(x, y + caretHeight));

        // Publish the caret rect (inflated for the 1.5-wide stroke + AA fringe)
        // so the blink timer in TextBoxBase can invalidate only this region
        // instead of the whole control. Rect is in LOCAL coordinates — exactly
        // what InvalidateVisual(Rect) expects.
        _lastRenderedCaretRect = new Rect(x - 2, y - 1, 5, caretHeight + 2);
    }

    #endregion

    #region Overrides

    /// <inheritdoc />
    protected override void InsertText(string textToInsert)
    {
        if (IsReadOnly || string.IsNullOrEmpty(textToInsert))
            return;

        PushUndo();

        // Delete selection if any
        if (_selectionLength > 0)
        {
            DeleteSelectionInternal();
        }

        var text = Text;
        var maxLength = MaxLength;

        // Ensure caret is within bounds
        if (_caretIndex < 0) _caretIndex = 0;
        if (_caretIndex > text.Length) _caretIndex = text.Length;

        // Enforce max length
        if (maxLength > 0)
        {
            var availableSpace = maxLength - text.Length;
            if (availableSpace <= 0)
                return;

            if (textToInsert.Length > availableSpace)
            {
                textToInsert = textToInsert.Substring(0, availableSpace);
            }
        }

        // Insert text
        Text = text.Substring(0, _caretIndex) + textToInsert + text.Substring(_caretIndex);
        _caretIndex += textToInsert.Length;

        ResetCaretBlink();
        EnsureCaretVisible();
    }

    /// <inheritdoc />
    protected override void OnSelectionChanged()
    {
        var e = new RoutedEventArgs(SelectionChangedEvent, this);
        RaiseEvent(e);
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox textBox)
        {
            textBox._linesDirty = true;
            textBox.InvalidateMeasure();
            textBox.InvalidateTextContentMeasure();

            var newText = (string)(e.NewValue ?? string.Empty);

            // Ensure caret is within bounds
            if (textBox._caretIndex < 0)
            {
                textBox._caretIndex = 0;
            }
            else if (textBox._caretIndex > newText.Length)
            {
                textBox._caretIndex = newText.Length;
            }

            // Clear selection if text changed externally
            if (textBox._selectionStart < 0)
            {
                textBox._selectionStart = 0;
            }
            if (textBox._selectionStart + textBox._selectionLength > newText.Length)
            {
                textBox._selectionStart = Math.Min(textBox._selectionStart, newText.Length);
                textBox._selectionLength = 0;
            }

            // Invalidate spell check
            // Raise TextChanged event
            var eventArgs = new TextChangedEventArgs(TextChangedEvent, textBox);
            textBox.RaiseEvent(eventArgs);
        }
    }

    private static void OnSpellCheckEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox textBox)
        {
            if ((bool)(e.NewValue ?? false))
            {
                textBox.PerformSpellCheck();
            }
            else
            {
                textBox._spellingErrors.Clear();
            }
            textBox.InvalidateVisual();
        }
    }

    private static void OnFormatDetectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox textBox)
        {
            textBox.DetectFormattedRegions();
            textBox.InvalidateVisual();
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox textBox)
        {
            textBox._linesDirty = true;
            textBox.InvalidateMeasure();
            textBox.InvalidateTextContentMeasure();
        }
    }

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox textBox)
        {
            textBox.InvalidateVisual();
        }
    }

    #endregion

    #region Helper Types

    private struct TextLine
    {
        public int StartIndex;
        public int Length;

        public TextLine(int startIndex, int length)
        {
            StartIndex = startIndex;
            Length = length;
        }
    }

    #endregion

    #region IME Support

    /// <summary>
    /// Gets whether IME composition is currently active.
    /// </summary>
    public bool IsImeComposing => _isImeComposing;

    /// <inheritdoc />
    public Point GetImeCaretPosition()
    {
        return GetCaretScreenPosition();
    }

    private Point GetCaretScreenPosition()
    {
        EnsureLinesValid();

        // Round line height for consistent positioning
        var lineHeight = Math.Round(GetLineHeight());
        var (lineIndex, columnIndex) = GetLineColumnFromCharIndex(_caretIndex);

        // Ensure valid indices
        if (lineIndex < 0) lineIndex = 0;
        if (columnIndex < 0) columnIndex = 0;

        var lineText = GetLineTextInternal(lineIndex);
        var clampedColumn = Math.Clamp(columnIndex, 0, lineText.Length);

        // Calculate x position using native hit testing for accuracy
        double x = Padding.Left - _horizontalOffset + GetCharacterXInLine(lineText, clampedColumn);

        // Calculate y position
        double y = Padding.Top + (lineIndex * lineHeight) - _verticalOffset;

        return new Point(x, y + lineHeight); // Position below the text line
    }

    /// <inheritdoc />
    public void OnImeCompositionStart()
    {
        _isImeComposing = true;
        _imeCompositionStart = _caretIndex;
        _imeCompositionString = string.Empty;
        _imeCompositionCursor = 0;

        // Delete any selected text first
        if (_selectionLength > 0)
        {
            DeleteSelection();
        }

        InvalidateVisual();
    }

    /// <inheritdoc />
    public void OnImeCompositionUpdate(string compositionString, int cursorPosition)
    {
        _imeCompositionString = compositionString;
        _imeCompositionCursor = cursorPosition;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public void OnImeCompositionEnd(string? resultString)
    {
        _isImeComposing = false;
        _imeCompositionString = string.Empty;
        _imeCompositionCursor = 0;

        // Result string is already inserted via TextInput event
        InvalidateVisual();
    }

    #endregion

    #region Spell Checking

    /// <summary>
    /// Performs spell checking on the text.
    /// </summary>
    public void PerformSpellCheck()
    {
        if (!IsSpellCheckEnabled || SpellChecker.Default == null || !SpellChecker.Default.IsAvailable)
        {
            _spellingErrors.Clear();
            return;
        }
        var text = Text;
        if (string.IsNullOrEmpty(text))
        {
            _spellingErrors.Clear();
            return;
        }

        var errors = SpellChecker.Default.Check(text);
        _spellingErrors = errors.ToList();
        InvalidateVisual();
    }

    /// <summary>
    /// Gets spelling suggestions for the word at the specified position.
    /// </summary>
    /// <param name="position">The character position.</param>
    /// <returns>The spelling error at the position, or null.</returns>
    public SpellingError? GetSpellingErrorAtPosition(int position)
    {
        foreach (var error in _spellingErrors)
        {
            if (position >= error.StartIndex && position < error.StartIndex + error.Length)
            {
                return error;
            }
        }
        return null;
    }

    /// <summary>
    /// Replaces a misspelled word with a correction.
    /// </summary>
    /// <param name="error">The spelling error.</param>
    /// <param name="correction">The correction to apply.</param>
    public void ReplaceSpellingError(SpellingError error, string correction)
    {
        if (IsReadOnly)
            return;

        var text = Text;
        if (error.StartIndex >= 0 && error.StartIndex + error.Length <= text.Length)
        {
            // Select the misspelled word
            _selectionStart = error.StartIndex;
            _selectionLength = error.Length;
            _caretIndex = error.StartIndex + error.Length;

            // Replace with correction
            InsertText(correction);
        }
    }

    /// <summary>
    /// Ignores a spelling error for this session.
    /// </summary>
    /// <param name="error">The spelling error to ignore.</param>
    public void IgnoreSpellingError(SpellingError error)
    {
        SpellChecker.Default?.IgnoreWord(error.Word);
        PerformSpellCheck();
    }

    /// <summary>
    /// Adds a word to the user dictionary.
    /// </summary>
    /// <param name="error">The spelling error containing the word to add.</param>
    public void AddToDictionary(SpellingError error)
    {
        SpellChecker.Default?.AddToDictionary(error.Word);
        PerformSpellCheck();
    }

    #endregion

    #region Auto-Formatting

    /// <summary>
    /// Detects formatted regions (URLs, emails, etc.) in the text.
    /// </summary>
    public void DetectFormattedRegions()
    {
        if (!DetectUrls)
        {
            _formattedRegions.Clear();
            return;
        }

        var formatter = TextFormatter.Default;
        formatter.DetectUrls = DetectUrls;
        formatter.DetectEmails = DetectUrls;
        formatter.DetectPhoneNumbers = DetectUrls;
        formatter.DetectDates = DetectUrls;

        var regions = formatter.DetectFormattedRegions(Text);
        _formattedRegions = regions.ToList();
    }

    /// <summary>
    /// Gets the formatted region at the specified position.
    /// </summary>
    /// <param name="position">The character position.</param>
    /// <returns>The formatted region at the position, or null.</returns>
    public FormattedRegion? GetFormattedRegionAtPosition(int position)
    {
        foreach (var region in _formattedRegions)
        {
            if (position >= region.StartIndex && position < region.StartIndex + region.Length)
            {
                return region;
            }
        }
        return null;
    }

    #endregion
}

/// <summary>
/// Specifies how the undo stack is affected by a text change.
/// </summary>
public enum UndoAction
{
    /// <summary>
    /// No undo action.
    /// </summary>
    None = 0,

    /// <summary>
    /// The undo action should be merged with the previous action.
    /// </summary>
    Merge = 1,

    /// <summary>
    /// The change was caused by an undo operation.
    /// </summary>
    Undo = 2,

    /// <summary>
    /// The change was caused by a redo operation.
    /// </summary>
    Redo = 3,

    /// <summary>
    /// The undo stack should be cleared.
    /// </summary>
    Clear = 4,

    /// <summary>
    /// A new undo unit should be created.
    /// </summary>
    Create = 5,
}

/// <summary>
/// Provides data for the TextChanged event.
/// </summary>
public sealed class TextChangedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextChangedEventArgs"/> class.
    /// </summary>
    public TextChangedEventArgs(RoutedEvent routedEvent, object source)
        : base(routedEvent, source)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TextChangedEventArgs"/> class with the specified undo action.
    /// </summary>
    public TextChangedEventArgs(RoutedEvent routedEvent, object source, UndoAction undoAction)
        : base(routedEvent, source)
    {
        UndoAction = undoAction;
    }

    /// <summary>
    /// Gets the undo action associated with this text change.
    /// </summary>
    public UndoAction UndoAction { get; }
}

/// <summary>
/// Delegate for handling TextChanged events.
/// </summary>
public delegate void TextChangedEventHandler(object sender, TextChangedEventArgs e);

