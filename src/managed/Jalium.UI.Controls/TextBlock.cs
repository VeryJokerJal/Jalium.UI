using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Displays text content.
/// </summary>
public class TextBlock : FrameworkElement
{
    private const int MaxTextWidthCacheEntries = 256;
    private static readonly SolidColorBrush s_defaultSelectionBrush = new(Color.FromArgb(180, 0, 120, 212));

    private readonly Dictionary<string, double> _textWidthCache = new(StringComparer.Ordinal);

    private List<TextLayoutLine> _layoutLines = new();
    private bool _layoutDirty = true;
    private double _layoutConstraintWidth = double.NaN;
    private string? _layoutText;

    private string? _cachedFontFamily;
    private double _cachedFontSize;
    private int _cachedFontWeight;
    private int _cachedFontStyle;

    private int _selectionStart;
    private int _selectionLength;
    private int _selectionAnchor;
    private bool _isSelecting;
    private bool _isWordSelecting;
    private int _wordSelectionAnchorStart;
    private int _wordSelectionAnchorEnd;
    private bool _isRenderingText;

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

    /// <summary>
    /// Identifies the IsTextSelectionEnabled dependency property.
    /// </summary>
    public static readonly DependencyProperty IsTextSelectionEnabledProperty =
        DependencyProperty.Register(nameof(IsTextSelectionEnabled), typeof(bool), typeof(TextBlock),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the SelectionBrush dependency property.
    /// </summary>
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

        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
        AddHandler(MouseEnterEvent, new RoutedEventHandler(OnMouseEnterHandler));
        AddHandler(MouseMoveEvent, new RoutedEventHandler(OnMouseMoveHandler));
        AddHandler(MouseLeaveEvent, new RoutedEventHandler(OnMouseLeaveHandler));
        AddHandler(MouseUpEvent, new RoutedEventHandler(OnMouseUpHandler));
        AddHandler(KeyDownEvent, new RoutedEventHandler(OnKeyDownHandler));
        AddHandler(GotKeyboardFocusEvent, new RoutedEventHandler(OnKeyboardFocusChanged));
        AddHandler(LostKeyboardFocusEvent, new RoutedEventHandler(OnKeyboardFocusChanged));
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

    /// <summary>
    /// Gets or sets a value indicating whether the text can be selected with the mouse.
    /// </summary>
    public bool IsTextSelectionEnabled
    {
        get => (bool)GetValue(IsTextSelectionEnabledProperty)!;
        set => SetValue(IsTextSelectionEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush used to paint the selected text background.
    /// </summary>
    public Brush? SelectionBrush
    {
        get => (Brush?)GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
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
            if (_selectionLength == 0 || string.IsNullOrEmpty(Text))
            {
                return string.Empty;
            }

            var start = Math.Clamp(_selectionStart, 0, Text.Length);
            var length = Math.Clamp(_selectionLength, 0, Text.Length - start);
            return Text.Substring(start, length);
        }
    }

    /// <summary>
    /// Selects the entire text.
    /// </summary>
    public void SelectAll()
    {
        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        _selectionAnchor = 0;
        ApplySelection(0, Text.Length);
    }

    /// <summary>
    /// Selects a specific range of text.
    /// </summary>
    public void Select(int start, int length)
    {
        if (string.IsNullOrEmpty(Text))
        {
            ClearSelection();
            return;
        }

        var clampedStart = Math.Clamp(start, 0, Text.Length);
        var clampedLength = Math.Clamp(length, 0, Text.Length - clampedStart);
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
        if (string.IsNullOrEmpty(Text))
        {
            return Size.Empty;
        }

        EnsureLayout(GetLayoutConstraintWidth(availableSize.Width));

        var lineHeight = GetLineHeight();
        var maxLineWidth = 0.0;
        for (int i = 0; i < _layoutLines.Count; i++)
        {
            maxLineWidth = Math.Max(maxLineWidth, _layoutLines[i].Width);
        }

        var measuredWidth = maxLineWidth + 2;
        var measuredHeight = Math.Max(_layoutLines.Count, 1) * lineHeight + 2;

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
    protected override void OnRender(object drawingContext)
    {
        if (_isRenderingText)
        {
            return;
        }

        _isRenderingText = true;
        try
        {
            base.OnRender(drawingContext);

            if (drawingContext is not DrawingContext dc || string.IsNullOrEmpty(Text) || Foreground == null)
            {
                return;
            }

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
        var renderWidth = RenderSize.Width;
        var fontFamily = FontFamily;
        var fontSize = FontSize;
        var fontWeight = FontWeight.ToOpenTypeWeight();
        var fontStyle = FontStyle.ToOpenTypeStyle();

        for (int i = 0; i < _layoutLines.Count; i++)
        {
            var line = _layoutLines[i];
            if (line.Length == 0)
            {
                continue;
            }

            var lineText = Text.Substring(line.StartIndex, line.Length);
            var formattedText = new FormattedText(lineText, fontFamily, fontSize)
            {
                Foreground = Foreground,
                MaxTextWidth = TextWrapping == TextWrapping.NoWrap ? double.MaxValue : Math.Max(renderWidth, line.Width),
                MaxTextHeight = lineHeight,
                FontWeight = fontWeight,
                FontStyle = fontStyle,
                Trimming = TextWrapping == TextWrapping.NoWrap ? TextTrimming : TextTrimming.None
            };

            dc.DrawText(formattedText, new Point(GetLineOriginX(line, renderWidth), i * lineHeight));
        }
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
        var renderWidth = RenderSize.Width;

        for (int i = 0; i < _layoutLines.Count; i++)
        {
            var line = _layoutLines[i];
            var lineEnd = line.StartIndex + line.Length;
            var intersectsLine = _selectionStart <= lineEnd && selectionEnd >= line.StartIndex;
            if (!intersectsLine)
            {
                continue;
            }

            var lineText = line.Length > 0 ? Text.Substring(line.StartIndex, line.Length) : string.Empty;
            var startInLine = Math.Max(0, _selectionStart - line.StartIndex);
            var endInLine = Math.Min(line.Length, selectionEnd - line.StartIndex);
            var lineOriginX = GetLineOriginX(line, renderWidth);
            var y = i * lineHeight;

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

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        if (!CanStartSelection() || e is not MouseButtonEventArgs mouseArgs)
        {
            return;
        }

        if (mouseArgs.ChangedButton != MouseButton.Left)
        {
            return;
        }

        Focus();
        var index = GetCharacterIndexFromPosition(mouseArgs.GetPosition(this));

        if (mouseArgs.ClickCount >= 3)
        {
            SelectAll();
            _isWordSelecting = false;
        }
        else if (mouseArgs.ClickCount == 2)
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

    private void OnMouseEnterHandler(object sender, RoutedEventArgs e)
    {
        UpdateHoverCursor();
    }

    private void OnMouseMoveHandler(object sender, RoutedEventArgs e)
    {
        UpdateHoverCursor();

        if (!_isSelecting || e is not MouseEventArgs mouseArgs)
        {
            return;
        }

        var index = GetCharacterIndexFromPosition(mouseArgs.GetPosition(this));
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

    private void OnMouseLeaveHandler(object sender, RoutedEventArgs e)
    {
        Cursor = null;
    }

    private void OnMouseUpHandler(object sender, RoutedEventArgs e)
    {
        if (!_isSelecting || e is not MouseButtonEventArgs mouseArgs || mouseArgs.ChangedButton != MouseButton.Left)
        {
            return;
        }

        _isSelecting = false;
        _isWordSelecting = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnKeyDownHandler(object sender, RoutedEventArgs e)
    {
        if (e is not KeyEventArgs keyArgs || keyArgs.Handled || !IsTextSelectionEnabled)
        {
            return;
        }

        if (keyArgs.IsControlDown && keyArgs.Key == Key.C && _selectionLength > 0)
        {
            Copy();
            keyArgs.Handled = true;
            return;
        }

        if (keyArgs.IsControlDown && keyArgs.Key == Key.A && !string.IsNullOrEmpty(Text))
        {
            SelectAll();
            keyArgs.Handled = true;
        }
    }

    private void OnKeyboardFocusChanged(object sender, RoutedEventArgs e)
    {
        // Keep the current selection when focus moves away, but repaint so the
        // selection highlight stays in sync with the new focus state.
        InvalidateVisual();
    }

    private void UpdateHoverCursor()
    {
        Cursor = CanShowTextSelectionCursor() ? Jalium.UI.Cursors.IBeam : null;
    }

    private bool CanStartSelection()
    {
        if (!IsEnabled || !IsTextSelectionEnabled || string.IsNullOrEmpty(Text))
        {
            return false;
        }

        return !IsSelectionBlockedByInteractiveAncestor() || HasLocalValue(IsTextSelectionEnabledProperty);
    }

    private bool CanShowTextSelectionCursor()
    {
        return IsEnabled &&
            IsTextSelectionEnabled &&
            !string.IsNullOrEmpty(Text) &&
            (!IsSelectionBlockedByInteractiveAncestor() || HasLocalValue(IsTextSelectionEnabledProperty));
    }

    private bool IsSelectionBlockedByInteractiveAncestor()
    {
        for (Visual? current = VisualParent; current != null; current = current.VisualParent)
        {
            if (current is ButtonBase or MenuFlyoutItem or MenuItem or MenuBarItem or Thumb or ListBoxItem
                or ComboBoxItem or TabItem or TreeViewItem or AutoCompleteBox or DataGrid
                or DataGridColumnHeader or DataGridRowHeader or NavigationViewItem or TitleBar
                or StatusBarItem or ToolBar or Popup or ContextMenu or ScrollBar
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
        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        var clampedIndex = Math.Clamp(index, 0, Text.Length);
        var start = clampedIndex;
        while (start > 0 && !IsWordBoundary(Text[start - 1]))
        {
            start--;
        }

        var end = clampedIndex;
        while (end < Text.Length && !IsWordBoundary(Text[end]))
        {
            end++;
        }

        _selectionAnchor = start;
        ApplySelection(start, end - start);
    }

    private static bool IsWordBoundary(char c)
    {
        return char.IsWhiteSpace(c) || char.IsPunctuation(c);
    }

    private void ExtendWordSelection(int caretIndex)
    {
        if (string.IsNullOrEmpty(Text))
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
        if (string.IsNullOrEmpty(Text))
        {
            return (0, 0);
        }

        var clampedIndex = Math.Clamp(index, 0, Text.Length);
        if (clampedIndex == Text.Length && clampedIndex > 0 && !IsWordBoundary(Text[clampedIndex - 1]))
        {
            clampedIndex--;
        }

        if (clampedIndex < Text.Length && IsWordBoundary(Text[clampedIndex]))
        {
            if (clampedIndex > 0 && !IsWordBoundary(Text[clampedIndex - 1]))
            {
                clampedIndex--;
            }
            else
            {
                while (clampedIndex < Text.Length && IsWordBoundary(Text[clampedIndex]))
                {
                    clampedIndex++;
                }

                if (clampedIndex >= Text.Length)
                {
                    return (Text.Length, Text.Length);
                }
            }
        }

        var start = clampedIndex;
        while (start > 0 && !IsWordBoundary(Text[start - 1]))
        {
            start--;
        }

        var end = clampedIndex;
        while (end < Text.Length && !IsWordBoundary(Text[end]))
        {
            end++;
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
        var textLength = Text.Length;
        var clampedStart = Math.Clamp(start, 0, textLength);
        var clampedLength = Math.Clamp(length, 0, textLength - clampedStart);

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
        var lineIndex = lineHeight <= 0
            ? 0
            : Math.Clamp((int)Math.Floor(Math.Max(0, position.Y) / lineHeight), 0, _layoutLines.Count - 1);

        var line = _layoutLines[lineIndex];
        var lineOriginX = GetLineOriginX(line, RenderSize.Width);
        var relativeX = position.X - lineOriginX;
        if (relativeX <= 0 || line.Length == 0)
        {
            return line.StartIndex;
        }

        var lineText = Text.Substring(line.StartIndex, line.Length);
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

        return line.StartIndex + columnIndex;
    }

    private double GetLineOriginX(in TextLayoutLine line, double renderWidth)
    {
        if (TextAlignment == TextAlignment.Center)
        {
            return Math.Max(0, (renderWidth - line.Width) / 2);
        }

        if (TextAlignment == TextAlignment.Right)
        {
            return Math.Max(0, renderWidth - line.Width);
        }

        return 0;
    }

    private void EnsureLayout(double constraintWidth)
    {
        if (!_layoutDirty &&
            string.Equals(Text, _layoutText, StringComparison.Ordinal) &&
            Math.Abs(_layoutConstraintWidth - constraintWidth) < 0.001)
        {
            return;
        }

        RebuildLayout(constraintWidth);
    }

    private void RebuildLayout(double constraintWidth)
    {
        _layoutLines = new List<TextLayoutLine>();
        _layoutText = Text;
        _layoutConstraintWidth = constraintWidth;
        _layoutDirty = false;

        if (string.IsNullOrEmpty(Text))
        {
            _layoutLines.Add(new TextLayoutLine(0, 0, 0, false));
            return;
        }

        var index = 0;
        while (index < Text.Length)
        {
            var lineStart = index;
            while (index < Text.Length && Text[index] != '\r' && Text[index] != '\n')
            {
                index++;
            }

            var lineLength = index - lineStart;
            var hasLineBreakAfter = false;
            if (index < Text.Length)
            {
                hasLineBreakAfter = true;
                if (Text[index] == '\r' && index + 1 < Text.Length && Text[index + 1] == '\n')
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

        if (EndsWithLineBreak(Text))
        {
            _layoutLines.Add(new TextLayoutLine(Text.Length, 0, 0, false));
        }
    }

    private void AppendLayoutLines(int lineStart, int lineLength, bool hasLineBreakAfter, double constraintWidth)
    {
        if (TextWrapping == TextWrapping.NoWrap || double.IsInfinity(constraintWidth) || constraintWidth <= 0)
        {
            var lineText = lineLength > 0 ? Text.Substring(lineStart, lineLength) : string.Empty;
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

            var currentText = Text.Substring(currentStart, currentLength);
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
            var candidate = Text.Substring(startIndex, mid);
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
            if (char.IsWhiteSpace(Text[startIndex + i]))
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

        return availableWidth;
    }

    private double MeasureTextWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var fontFamily = FontFamily ?? "Segoe UI";
        var fontSize = FontSize > 0 ? FontSize : 14;
        var fontWeight = FontWeight.ToOpenTypeWeight();
        var fontStyle = FontStyle.ToOpenTypeStyle();

        if (!string.Equals(_cachedFontFamily, fontFamily, StringComparison.Ordinal) ||
            Math.Abs(_cachedFontSize - fontSize) > 0.001 ||
            _cachedFontWeight != fontWeight ||
            _cachedFontStyle != fontStyle)
        {
            _textWidthCache.Clear();
            _cachedFontFamily = fontFamily;
            _cachedFontSize = fontSize;
            _cachedFontWeight = fontWeight;
            _cachedFontStyle = fontStyle;
        }

        if (_textWidthCache.TryGetValue(text, out var width))
        {
            return width;
        }

        var formattedText = new FormattedText(text, fontFamily, fontSize)
        {
            FontWeight = fontWeight,
            FontStyle = fontStyle
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
        var fontFamily = FontFamily ?? "Segoe UI";
        var fontSize = FontSize > 0 ? FontSize : 14;
        return TextMeasurement.GetLineHeight(fontFamily, fontSize, FontWeight.ToOpenTypeWeight(), FontStyle.ToOpenTypeStyle());
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
        _layoutText = null;
        _layoutConstraintWidth = double.NaN;
        _textWidthCache.Clear();
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
        {
            return;
        }

        textBlock.InvalidateCaches();
        textBlock.CoerceSelectionIntoBounds();

        if (e.Property != TextProperty && string.IsNullOrEmpty(textBlock.Text))
        {
            return;
        }

        textBlock.InvalidateMeasure();
        textBlock.InvalidateVisual();
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock || Equals(e.OldValue, e.NewValue))
        {
            return;
        }

        if (e.Property == TextAlignmentProperty || e.Property == SelectionBrushProperty || e.Property == ForegroundProperty)
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
        var textLength = Text.Length;
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
