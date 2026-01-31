using System.Text;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// A control for editing plain text.
/// </summary>
public class TextBox : TextBoxBase, IImeSupport
{
    #region Fields

    // Multi-line support
    private List<TextLine> _lines = new();
    private bool _linesDirty = true;

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

    // Spell checking
    private List<SpellingError> _spellingErrors = new();
    private bool _spellCheckDirty = true;
    private DateTime _lastSpellCheckTime;
    private const int SpellCheckDelayMs = 500;

    // Auto-formatting
    private List<FormattedRegion> _formattedRegions = new();

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Text dependency property.
    /// </summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(TextBox),
            new PropertyMetadata(string.Empty, OnTextChanged));

    /// <summary>
    /// Identifies the MaxLength dependency property.
    /// </summary>
    public static readonly DependencyProperty MaxLengthProperty =
        DependencyProperty.Register(nameof(MaxLength), typeof(int), typeof(TextBox),
            new PropertyMetadata(0));

    /// <summary>
    /// Identifies the TextWrapping dependency property.
    /// </summary>
    public static readonly DependencyProperty TextWrappingProperty =
        DependencyProperty.Register(nameof(TextWrapping), typeof(TextWrapping), typeof(TextBox),
            new PropertyMetadata(TextWrapping.NoWrap, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the TextAlignment dependency property.
    /// </summary>
    public static readonly DependencyProperty TextAlignmentProperty =
        DependencyProperty.Register(nameof(TextAlignment), typeof(TextAlignment), typeof(TextBox),
            new PropertyMetadata(TextAlignment.Left, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the Placeholder dependency property.
    /// </summary>
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(TextBox),
            new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsSpellCheckEnabled dependency property.
    /// </summary>
    public static readonly DependencyProperty IsSpellCheckEnabledProperty =
        DependencyProperty.Register(nameof(IsSpellCheckEnabled), typeof(bool), typeof(TextBox),
            new PropertyMetadata(false, OnSpellCheckEnabledChanged));

    /// <summary>
    /// Identifies the SpellCheckLanguage dependency property.
    /// </summary>
    public static readonly DependencyProperty SpellCheckLanguageProperty =
        DependencyProperty.Register(nameof(SpellCheckLanguage), typeof(string), typeof(TextBox),
            new PropertyMetadata("en-US"));

    /// <summary>
    /// Identifies the IsAutoCorrectEnabled dependency property.
    /// </summary>
    public static readonly DependencyProperty IsAutoCorrectEnabledProperty =
        DependencyProperty.Register(nameof(IsAutoCorrectEnabled), typeof(bool), typeof(TextBox),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsAutoCapitalizationEnabled dependency property.
    /// </summary>
    public static readonly DependencyProperty IsAutoCapitalizationEnabledProperty =
        DependencyProperty.Register(nameof(IsAutoCapitalizationEnabled), typeof(bool), typeof(TextBox),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the DetectUrls dependency property.
    /// </summary>
    public static readonly DependencyProperty DetectUrlsProperty =
        DependencyProperty.Register(nameof(DetectUrls), typeof(bool), typeof(TextBox),
            new PropertyMetadata(false, OnFormatDetectionChanged));

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
    /// Gets or sets the maximum number of characters (0 = unlimited).
    /// </summary>
    public int MaxLength
    {
        get => (int)(GetValue(MaxLengthProperty) ?? 0);
        set => SetValue(MaxLengthProperty, value);
    }

    /// <summary>
    /// Gets or sets the text wrapping mode.
    /// </summary>
    public TextWrapping TextWrapping
    {
        get => (TextWrapping)(GetValue(TextWrappingProperty) ?? TextWrapping.NoWrap);
        set => SetValue(TextWrappingProperty, value);
    }

    /// <summary>
    /// Gets or sets the text alignment.
    /// </summary>
    public TextAlignment TextAlignment
    {
        get => (TextAlignment)(GetValue(TextAlignmentProperty) ?? TextAlignment.Left);
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets the placeholder text shown when the text box is empty.
    /// </summary>
    public string Placeholder
    {
        get => (string)(GetValue(PlaceholderProperty) ?? string.Empty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>
    /// Gets or sets whether spell checking is enabled.
    /// </summary>
    public bool IsSpellCheckEnabled
    {
        get => (bool)(GetValue(IsSpellCheckEnabledProperty) ?? false);
        set => SetValue(IsSpellCheckEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the spell check language (e.g., "en-US", "zh-CN").
    /// </summary>
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
    public bool IsAutoCorrectEnabled
    {
        get => (bool)(GetValue(IsAutoCorrectEnabledProperty) ?? false);
        set => SetValue(IsAutoCorrectEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets whether auto-capitalization is enabled.
    /// </summary>
    public bool IsAutoCapitalizationEnabled
    {
        get => (bool)(GetValue(IsAutoCapitalizationEnabledProperty) ?? false);
        set => SetValue(IsAutoCapitalizationEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets whether URL detection is enabled.
    /// </summary>
    public bool DetectUrls
    {
        get => (bool)(GetValue(DetectUrlsProperty) ?? false);
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
        // Dark theme appearance
        Background = new SolidColorBrush(Color.FromRgb(32, 32, 32));
        BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70));
        Foreground = new SolidColorBrush(Color.White);
        BorderThickness = new Thickness(1);
        Padding = new Thickness(6, 4, 6, 4);
        FontSize = 14;

        // Set IBeam cursor for text input
        Cursor = Jalium.UI.Cursors.IBeam;

        // Subscribe to IME events
        InputMethod.CompositionStarted += OnImeCompositionStarted;
        InputMethod.CompositionUpdated += OnImeCompositionUpdated;
        InputMethod.CompositionEnded += OnImeCompositionEnded;

        // Subscribe to focus events for IME target management
        AddHandler(GotKeyboardFocusEvent, new RoutedEventHandler(OnGotFocusHandler));
        AddHandler(LostKeyboardFocusEvent, new RoutedEventHandler(OnLostFocusHandler));
    }

    private void OnGotFocusHandler(object sender, RoutedEventArgs e)
    {
        InputMethod.SetTarget(this);
    }

    private void OnLostFocusHandler(object sender, RoutedEventArgs e)
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
        var fontFamily = FontFamily ?? "Segoe UI";
        var fontSize = FontSize > 0 ? FontSize : 14;
        var fontMetrics = TextMeasurement.GetFontMetrics(fontFamily, fontSize);
        return fontMetrics.LineHeight;
    }

    /// <inheritdoc />
    protected override double MeasureTextWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var fontFamily = FontFamily ?? "Segoe UI";
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
            else if (char.IsUpper(c))
            {
                // Uppercase letters are wider
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
            else if (c == 'i' || c == 'l' || c == '|' || c == '!' || c == '.' || c == ',')
            {
                // Narrow characters
                width += fontSize * 0.3;
            }
            else if (c == 'm' || c == 'w' || c == 'M' || c == 'W')
            {
                // Wide characters
                width += fontSize * 0.85;
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
        VerticalOffset = lineIndex * lineHeight;
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

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var padding = Padding;
        var border = BorderThickness;
        // Round line height for consistent layout
        var lineHeight = Math.Round(GetLineHeight());

        EnsureLinesValid();

        double textHeight;
        if (AcceptsReturn)
        {
            // Multi-line: height based on line count
            textHeight = Math.Max(1, _lines.Count) * lineHeight;
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

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        base.OnRender(drawingContext);

        if (drawingContext is not DrawingContext dc)
            return;

        var border = BorderThickness;
        var padding = Padding;
        var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);
        // Round line height to prevent sub-pixel jittering from floating-point accumulation
        var lineHeight = Math.Round(GetLineHeight());

        EnsureLinesValid();

        // Draw background
        if (Background != null)
        {
            var cornerRadius = CornerRadius;
            if (cornerRadius.TopLeft > 0)
            {
                dc.DrawRoundedRectangle(Background, null, bounds, cornerRadius.TopLeft, cornerRadius.TopLeft);
            }
            else
            {
                dc.DrawRectangle(Background, null, bounds);
            }
        }

        // Draw border
        if (BorderBrush != null && border.Left > 0)
        {
            var borderPen = new Pen(BorderBrush, border.Left);
            var cornerRadius = CornerRadius;
            if (cornerRadius.TopLeft > 0)
            {
                dc.DrawRoundedRectangle(null, borderPen, bounds, cornerRadius.TopLeft, cornerRadius.TopLeft);
            }
            else
            {
                dc.DrawRectangle(null, borderPen, bounds);
            }
        }

        // Content area - round to pixel boundaries to prevent sub-pixel jittering
        var contentRect = new Rect(
            Math.Round(border.Left + padding.Left),
            Math.Round(border.Top + padding.Top),
            Math.Max(0, Math.Round(bounds.Width - border.Left - border.Right - padding.Left - padding.Right)),
            Math.Max(0, Math.Round(bounds.Height - border.Top - border.Bottom - padding.Top - padding.Bottom)));

        // Clip to content area
        dc.PushClip(new RectangleGeometry(contentRect));

        var text = Text;

        // Draw selection background
        if (_selectionLength > 0 && IsKeyboardFocused)
        {
            DrawSelection(dc, contentRect, lineHeight);
        }

        // Round scroll offsets for consistent rendering
        var roundedHorizontalOffset = Math.Round(_horizontalOffset);
        var roundedVerticalOffset = Math.Round(_verticalOffset);

        // Draw text or placeholder
        if (string.IsNullOrEmpty(text))
        {
            if (!string.IsNullOrEmpty(Placeholder))
            {
                var placeholderBrush = new SolidColorBrush(Color.FromRgb(128, 128, 128));
                var formattedPlaceholder = new FormattedText(Placeholder, FontFamily ?? "Segoe UI", FontSize)
                {
                    Foreground = placeholderBrush,
                    MaxTextWidth = contentRect.Width,
                    MaxTextHeight = contentRect.Height
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

        // Draw caret - always call DrawCaret when focused to keep animation running
        // DrawCaret will internally check opacity and skip drawing when hidden
        if (IsFocused && !IsReadOnly)
        {
            DrawCaret(dc, contentRect, lineHeight);
        }

        dc.Pop(); // Pop clip

        // Draw focus indicator
        if (IsKeyboardFocused)
        {
            var focusPen = new Pen(new SolidColorBrush(Color.FromRgb(0, 120, 212)), 1);
            if (CornerRadius.TopLeft > 0)
            {
                dc.DrawRoundedRectangle(null, focusPen, bounds, CornerRadius.TopLeft, CornerRadius.TopLeft);
            }
            else
            {
                dc.DrawRectangle(null, focusPen, bounds);
            }
        }
    }

    private void DrawText(DrawingContext dc, Rect contentRect, double lineHeight)
    {
        var text = Text;
        var textBrush = Foreground ?? new SolidColorBrush(Color.White);
        // Round scroll offsets to prevent sub-pixel jittering
        var roundedVerticalOffset = Math.Round(_verticalOffset);
        var roundedHorizontalOffset = Math.Round(_horizontalOffset);

        for (int i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            // Round y position to prevent sub-pixel jittering from floating-point accumulation errors
            var y = Math.Round(contentRect.Y + i * lineHeight - roundedVerticalOffset);

            // Skip lines outside visible area
            if (y + lineHeight < contentRect.Y || y > contentRect.Y + contentRect.Height)
                continue;

            if (line.Length == 0)
                continue;

            var lineText = text.Substring(line.StartIndex, line.Length);
            var formattedText = new FormattedText(lineText, FontFamily ?? "Segoe UI", FontSize)
            {
                Foreground = textBrush,
                MaxTextWidth = Math.Max(0, contentRect.Width + roundedHorizontalOffset),
                MaxTextHeight = lineHeight
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
        var errorPen = new Pen(new SolidColorBrush(Color.FromRgb(255, 0, 0)), 1);
        var text = Text;
        // Round scroll offsets to prevent sub-pixel jittering
        var roundedVerticalOffset = Math.Round(_verticalOffset);
        var roundedHorizontalOffset = Math.Round(_horizontalOffset);

        foreach (var error in _spellingErrors)
        {
            // Find which line(s) this error spans
            for (int i = 0; i < _lines.Count; i++)
            {
                var line = _lines[i];
                var lineEnd = line.StartIndex + line.Length;
                // Round y position to prevent sub-pixel jittering from floating-point accumulation errors
                var y = Math.Round(contentRect.Y + i * lineHeight - roundedVerticalOffset);

                // Skip lines outside visible area
                if (y + lineHeight < contentRect.Y || y > contentRect.Y + contentRect.Height)
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
                        var textBefore = lineText.Substring(0, startInLine);
                        var errorText = lineText.Substring(startInLine, endInLine - startInLine);

                        var startX = Math.Round(contentRect.X + MeasureTextWidth(textBefore) - roundedHorizontalOffset);
                        var endX = startX + MeasureTextWidth(errorText);
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
        if (SelectionBrush == null)
            return;

        var text = Text;
        var selectionEnd = _selectionStart + _selectionLength;
        // Round scroll offsets to prevent sub-pixel jittering
        var roundedVerticalOffset = Math.Round(_verticalOffset);
        var roundedHorizontalOffset = Math.Round(_horizontalOffset);

        for (int i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            var lineEnd = line.StartIndex + line.Length;
            // Round y position to prevent sub-pixel jittering from floating-point accumulation errors
            var y = Math.Round(contentRect.Y + i * lineHeight - roundedVerticalOffset);

            // Skip lines outside visible area
            if (y + lineHeight < contentRect.Y || y > contentRect.Y + contentRect.Height)
                continue;

            // Check if selection intersects this line
            if (_selectionStart <= lineEnd && selectionEnd >= line.StartIndex)
            {
                var startInLine = Math.Max(0, _selectionStart - line.StartIndex);
                var endInLine = Math.Min(line.Length, selectionEnd - line.StartIndex);

                if (endInLine > startInLine)
                {
                    var lineText = text.Substring(line.StartIndex, line.Length);
                    var textBefore = lineText.Substring(0, startInLine);
                    var selectedText = lineText.Substring(startInLine, endInLine - startInLine);

                    // Use measured text widths for accurate selection positioning
                    var startX = Math.Round(contentRect.X + MeasureTextWidth(textBefore) - roundedHorizontalOffset);
                    var width = MeasureTextWidth(selectedText);
                    // Ensure minimum width for visibility (especially for spaces)
                    width = Math.Max(Math.Round(width), 1);

                    var selRect = new Rect(startX, y, width, lineHeight);
                    dc.DrawRectangle(SelectionBrush, null, selRect);
                }

                // Selection extends past line end (include newline in selection visual)
                if (selectionEnd > lineEnd && i < _lines.Count - 1)
                {
                    var lineText = text.Substring(line.StartIndex, line.Length);
                    var startX = Math.Round(contentRect.X + MeasureTextWidth(lineText) - roundedHorizontalOffset);
                    var selRect = new Rect(startX, y, Math.Round(FontSize * 0.3), lineHeight);
                    dc.DrawRectangle(SelectionBrush, null, selRect);
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
        var textBeforeCaret = lineText.Substring(0, Math.Min(columnIndex, lineText.Length));

        // Round scroll offsets to prevent sub-pixel jittering
        var roundedHorizontalOffset = Math.Round(_horizontalOffset);
        var roundedVerticalOffset = Math.Round(_verticalOffset);

        var x = Math.Round(contentRect.X + MeasureTextWidth(textBeforeCaret) - roundedHorizontalOffset);
        // Round y position to prevent sub-pixel jittering from floating-point accumulation errors
        var y = Math.Round(contentRect.Y + lineIndex * lineHeight - roundedVerticalOffset);

        // Draw composition background
        var compositionWidth = MeasureTextWidth(_imeCompositionString);
        var compositionBgBrush = new SolidColorBrush(Color.FromRgb(60, 60, 80));
        dc.DrawRectangle(compositionBgBrush, null, new Rect(x, y, compositionWidth, lineHeight));

        // Draw composition text
        var compositionText = new FormattedText(_imeCompositionString, FontFamily ?? "Segoe UI", FontSize)
        {
            Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 200)),
            MaxTextWidth = contentRect.Width,
            MaxTextHeight = lineHeight
        };
        dc.DrawText(compositionText, new Point(x, y));

        // Draw underline for composition string
        var underlinePen = new Pen(new SolidColorBrush(Color.FromRgb(200, 200, 100)), 1);
        dc.DrawLine(underlinePen, new Point(x, y + lineHeight - 2), new Point(x + compositionWidth, y + lineHeight - 2));

        // Draw cursor within composition string
        if (_imeCompositionCursor >= 0 && _imeCompositionCursor <= _imeCompositionString.Length)
        {
            var cursorTextWidth = MeasureTextWidth(_imeCompositionString.Substring(0, _imeCompositionCursor));
            var cursorX = x + cursorTextWidth;
            var cursorPen = new Pen(new SolidColorBrush(Color.White), 1);
            dc.DrawLine(cursorPen, new Point(cursorX, y + 2), new Point(cursorX, y + lineHeight - 2));
        }
    }

    private void DrawCaret(DrawingContext dc, Rect contentRect, double lineHeight)
    {
        // Update and get the current caret opacity first (needed for animation to progress)
        var caretOpacity = UpdateCaretAnimation();

        // Note: Caret animation is handled by the timer in TextBoxBase.StartCaretTimer()
        // which calls InvalidateVisual() at regular intervals. No fallback needed here.

        if (CaretBrush == null)
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
        var textBeforeCaret = lineText.Substring(0, Math.Min(columnIndex, lineText.Length));

        // Round scroll offsets to prevent sub-pixel jittering
        var roundedHorizontalOffset = Math.Round(_horizontalOffset);
        var roundedVerticalOffset = Math.Round(_verticalOffset);

        var x = Math.Round(contentRect.X + MeasureTextWidth(textBeforeCaret) - roundedHorizontalOffset);
        // Round y position to prevent sub-pixel jittering from floating-point accumulation errors
        var y = Math.Round(contentRect.Y + lineIndex * lineHeight - roundedVerticalOffset);

        // Create a brush with the animated opacity
        Brush caretBrushWithOpacity;
        if (CaretBrush is SolidColorBrush solidBrush)
        {
            var color = solidBrush.Color;
            var alpha = (byte)(color.A * caretOpacity);
            caretBrushWithOpacity = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        }
        else
        {
            caretBrushWithOpacity = CaretBrush;
        }

        var caretPen = new Pen(caretBrushWithOpacity, 1.5);
        dc.DrawLine(caretPen, new Point(x, y), new Point(x, y + lineHeight));
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

            // Update undo tracking
            if (!textBox._isUndoRedoing)
            {
                textBox._lastText = newText;
            }

            // Invalidate spell check
            textBox._spellCheckDirty = true;
            textBox._lastSpellCheckTime = DateTime.Now;

            // Raise TextChanged event
            var eventArgs = new TextChangedEventArgs(TextChangedEvent, textBox);
            textBox.RaiseEvent(eventArgs);
        }
    }

    private static void OnSpellCheckEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox textBox)
        {
            textBox._spellCheckDirty = true;
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
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
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
        // Get the caret position in local coordinates and convert to screen
        var caretPos = GetCaretScreenPosition();

        // Transform to window coordinates by traversing visual tree
        var element = this as UIElement;
        var parent = element?.VisualParent;
        while (parent != null)
        {
            if (parent is FrameworkElement fe)
            {
                // Add the margin/position offset
                caretPos = new Point(caretPos.X + fe.Margin.Left, caretPos.Y + fe.Margin.Top);
            }
            if (parent is Window)
                break;
            parent = parent.VisualParent;
        }

        return caretPos;
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
        var textBeforeCaret = lineText.Substring(0, Math.Min(columnIndex, lineText.Length));

        // Calculate x position using measured text width
        double x = Padding.Left - _horizontalOffset + MeasureTextWidth(textBeforeCaret);

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
            _spellCheckDirty = false;
            return;
        }

        var text = Text;
        if (string.IsNullOrEmpty(text))
        {
            _spellingErrors.Clear();
            _spellCheckDirty = false;
            return;
        }

        // Perform spell check
        var errors = SpellChecker.Default.Check(text);
        _spellingErrors = errors.ToList();
        _spellCheckDirty = false;

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
/// Provides data for the TextChanged event.
/// </summary>
public class TextChangedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextChangedEventArgs"/> class.
    /// </summary>
    public TextChangedEventArgs(RoutedEvent routedEvent, object source)
        : base(routedEvent, source)
    {
    }
}

/// <summary>
/// Delegate for handling TextChanged events.
/// </summary>
public delegate void TextChangedEventHandler(object sender, TextChangedEventArgs e);

/// <summary>
/// Provides static methods for clipboard operations.
/// Delegates to native Windows clipboard via P/Invoke.
/// </summary>
public static class Clipboard
{
    /// <summary>
    /// Sets text to the clipboard.
    /// </summary>
    /// <param name="text">The text to set.</param>
    public static void SetText(string text)
    {
        Interop.Clipboard.SetText(text);
    }

    /// <summary>
    /// Gets text from the clipboard.
    /// </summary>
    /// <returns>The text from the clipboard, or empty string if no text is available.</returns>
    public static string GetText()
    {
        return Interop.Clipboard.GetText() ?? string.Empty;
    }

    /// <summary>
    /// Checks if the clipboard contains text.
    /// </summary>
    /// <returns>True if the clipboard contains text, false otherwise.</returns>
    public static bool ContainsText()
    {
        return Interop.Clipboard.ContainsText();
    }

    /// <summary>
    /// Clears the clipboard.
    /// </summary>
    public static void Clear()
    {
        Interop.Clipboard.Clear();
    }
}
