using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a label control that displays text and can be associated with a target element.
/// When the label's access key is pressed, focus moves to the target element.
/// </summary>
public class Label : ContentControl
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.LabelAutomationPeer(this);
    }

    private static readonly SolidColorBrush s_defaultSelectionBrush = new(Color.FromArgb(180, 0x1E, 0x79, 0x3F));

    private bool _pendingTemplateTextFocus;
    private bool _isSelectingDirectText;
    private int _directSelectionStart;
    private int _directSelectionLength;
    private int _directSelectionAnchor;
    private bool _isDirectWordSelecting;
    private int _directWordSelectionAnchorStart;
    private int _directWordSelectionAnchorEnd;

    #region Dependency Properties

    /// <summary>
    /// Identifies the Target dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty TargetProperty =
        DependencyProperty.Register(nameof(Target), typeof(UIElement), typeof(Label),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the AccessKey dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty AccessKeyProperty =
        DependencyProperty.Register(nameof(AccessKey), typeof(char?), typeof(Label),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsTextSelectionEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsTextSelectionEnabledProperty =
        DependencyProperty.Register(nameof(IsTextSelectionEnabled), typeof(bool), typeof(Label),
            new PropertyMetadata(false, OnIsTextSelectionEnabledChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the target element that receives focus when the label's access key is pressed.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public UIElement? Target
    {
        get => (UIElement?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>
    /// Gets or sets the access key character for this label.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public char? AccessKey
    {
        get => (char?)GetValue(AccessKeyProperty);
        set => SetValue(AccessKeyProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the label text can be selected with the mouse.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsTextSelectionEnabled
    {
        get => (bool)GetValue(IsTextSelectionEnabledProperty)!;
        set => SetValue(IsTextSelectionEnabledProperty, value);
    }

    /// <summary>
    /// Gets the currently selected text.
    /// </summary>
    public string SelectedText
    {
        get
        {
            if (_labelBorder != null &&
                FindDescendantTextBlock(_labelBorder) is TextBlock textBlock)
            {
                return textBlock.SelectedText;
            }

            if (Content is string text && _directSelectionLength > 0)
            {
                return text.Substring(_directSelectionStart, _directSelectionLength);
            }

            return string.Empty;
        }
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="Label"/> class.
    /// </summary>
    public Label()
    {
        UseTemplateContentManagement();

        Focusable = true;
        KeyboardNavigation.SetIsTabStop(this, false);

        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler), handledEventsToo: true);
        AddHandler(MouseEnterEvent, new MouseEventHandler(OnMouseEnterHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
        AddHandler(MouseLeaveEvent, new MouseEventHandler(OnMouseLeaveHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler), handledEventsToo: true);
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnKeyboardFocusChanged));
        AddHandler(LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnKeyboardFocusChanged));
    }

    #endregion

    #region Template Parts

    private Border? _labelBorder;

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _labelBorder = GetTemplateChild("LabelBorder") as Border;
        ApplyPresentedTextStyle();
    }

    /// <inheritdoc />
    protected override void OnContentChanged(object? oldContent, object? newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        CoerceDirectSelectionIntoBounds();
        ApplyPresentedTextStyle();
    }

    #endregion

    #region Input Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (TryHandleTemplateTextMouseDown(e) || TryHandleDirectTextMouseDown(e))
        {
            e.Handled = true;
            return;
        }

        FocusTarget();
        e.Handled = true;
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        UpdateDirectTextCursor();

        if (!_isSelectingDirectText || Content is not string text)
        {
            return;
        }

        var index = GetDirectTextCharacterIndex(e.GetPosition(this), text);
        UpdateDirectSelection(index);
        e.Handled = true;
    }

    private void OnMouseEnterHandler(object sender, MouseEventArgs e)
    {
        UpdateDirectTextCursor();
    }

    private void OnMouseLeaveHandler(object sender, MouseEventArgs e)
    {
        if (_labelBorder == null)
        {
            Cursor = null;
        }
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (_pendingTemplateTextFocus)
        {
            _pendingTemplateTextFocus = false;
            if (_labelBorder != null &&
                FindDescendantTextBlock(_labelBorder) is TextBlock textBlock &&
                textBlock.SelectionLength == 0)
            {
                FocusTarget();
                e.Handled = true;
            }

            return;
        }

        if (!_isSelectingDirectText)
        {
            return;
        }

        _isSelectingDirectText = false;
        _isDirectWordSelecting = false;
        ReleaseMouseCapture();

        if (_directSelectionLength == 0)
        {
            FocusTarget();
        }

        e.Handled = true;
    }

    /// <summary>
    /// Moves focus to the target element if one is set.
    /// </summary>
    private void FocusTarget()
    {
        if (Target is FrameworkElement fe && fe.Focusable)
        {
            fe.Focus();
        }
    }

    /// <summary>
    /// Copies the current text selection to the clipboard.
    /// </summary>
    public void Copy()
    {
        if (!string.IsNullOrEmpty(SelectedText))
        {
            Clipboard.SetText(SelectedText);
        }
    }

    /// <inheritdoc />
    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();
        _isSelectingDirectText = false;
        _isDirectWordSelecting = false;
    }

    private bool TryHandleTemplateTextMouseDown(MouseButtonEventArgs mouseArgs)
    {
        if (!IsTextSelectionEnabled)
        {
            return false;
        }

        if (_labelBorder == null)
        {
            return false;
        }

        if (FindDescendantTextBlock(_labelBorder) is not TextBlock textBlock)
        {
            return false;
        }

        if (!IsSourceWithinPresentedText(mouseArgs.OriginalSource, textBlock))
        {
            return false;
        }

        _pendingTemplateTextFocus = true;
        return true;
    }

    private bool TryHandleDirectTextMouseDown(MouseButtonEventArgs mouseArgs)
    {
        if (!IsTextSelectionEnabled || _labelBorder != null || Content is not string text || string.IsNullOrEmpty(text))
        {
            return false;
        }

        var index = GetDirectTextCharacterIndex(mouseArgs.GetPosition(this), text);

        if (mouseArgs.ClickCount >= 3)
        {
            _directSelectionAnchor = 0;
            _isDirectWordSelecting = false;
            ApplyDirectSelection(0, text.Length);
            return true;
        }

        if (mouseArgs.ClickCount == 2)
        {
            Focus();
            SelectDirectWordAt(text, index);
            _directWordSelectionAnchorStart = _directSelectionStart;
            _directWordSelectionAnchorEnd = _directSelectionStart + _directSelectionLength;
            _isDirectWordSelecting = _directSelectionLength > 0;
            _isSelectingDirectText = true;
            CaptureMouse();
            return true;
        }

        Focus();
        CaptureMouse();
        _isSelectingDirectText = true;
        _isDirectWordSelecting = false;
        _directSelectionAnchor = index;
        ApplyDirectSelection(index, 0);
        return true;
    }

    private static bool IsSourceWithinPresentedText(object? source, TextBlock textBlock)
    {
        for (var current = source as Visual; current != null; current = current.VisualParent)
        {
            if (ReferenceEquals(current, textBlock))
            {
                return true;
            }
        }

        return false;
    }

    private void SelectDirectWordAt(string text, int index)
    {
        var clampedIndex = Math.Clamp(index, 0, text.Length);
        var start = clampedIndex;
        while (start > 0 && !IsWordBoundary(text[start - 1]))
        {
            start--;
        }

        var end = clampedIndex;
        while (end < text.Length && !IsWordBoundary(text[end]))
        {
            end++;
        }

        _directSelectionAnchor = start;
        ApplyDirectSelection(start, end - start);
    }

    private static bool IsWordBoundary(char c)
    {
        return char.IsWhiteSpace(c) || char.IsPunctuation(c);
    }

    private void UpdateDirectSelection(int caretIndex)
    {
        if (_isDirectWordSelecting && Content is string text)
        {
            ExtendDirectWordSelection(text, caretIndex);
            return;
        }

        var start = Math.Min(_directSelectionAnchor, caretIndex);
        var length = Math.Abs(caretIndex - _directSelectionAnchor);
        ApplyDirectSelection(start, length);
    }

    private void ExtendDirectWordSelection(string text, int caretIndex)
    {
        var (currentWordStart, currentWordEnd) = GetDirectWordRangeAtIndex(text, caretIndex);
        int selectionStart;
        int selectionEnd;

        if (currentWordEnd <= _directWordSelectionAnchorStart)
        {
            selectionStart = currentWordStart;
            selectionEnd = _directWordSelectionAnchorEnd;
        }
        else if (currentWordStart >= _directWordSelectionAnchorEnd)
        {
            selectionStart = _directWordSelectionAnchorStart;
            selectionEnd = currentWordEnd;
        }
        else
        {
            selectionStart = _directWordSelectionAnchorStart;
            selectionEnd = _directWordSelectionAnchorEnd;
        }

        ApplyDirectSelection(selectionStart, Math.Max(0, selectionEnd - selectionStart));
    }

    private void ApplyDirectSelection(int start, int length)
    {
        if (Content is not string text)
        {
            _directSelectionStart = 0;
            _directSelectionLength = 0;
            return;
        }

        var clampedStart = Math.Clamp(start, 0, text.Length);
        var clampedLength = Math.Clamp(length, 0, text.Length - clampedStart);
        if (_directSelectionStart == clampedStart && _directSelectionLength == clampedLength)
        {
            return;
        }

        _directSelectionStart = clampedStart;
        _directSelectionLength = clampedLength;
        InvalidateVisual();
    }

    private static (int start, int end) GetDirectWordRangeAtIndex(string text, int index)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (0, 0);
        }

        var clampedIndex = Math.Clamp(index, 0, text.Length);
        if (clampedIndex == text.Length && clampedIndex > 0 && !IsWordBoundary(text[clampedIndex - 1]))
        {
            clampedIndex--;
        }

        if (clampedIndex < text.Length && IsWordBoundary(text[clampedIndex]))
        {
            if (clampedIndex > 0 && !IsWordBoundary(text[clampedIndex - 1]))
            {
                clampedIndex--;
            }
            else
            {
                while (clampedIndex < text.Length && IsWordBoundary(text[clampedIndex]))
                {
                    clampedIndex++;
                }

                if (clampedIndex >= text.Length)
                {
                    return (text.Length, text.Length);
                }
            }
        }

        var start = clampedIndex;
        while (start > 0 && !IsWordBoundary(text[start - 1]))
        {
            start--;
        }

        var end = clampedIndex;
        while (end < text.Length && !IsWordBoundary(text[end]))
        {
            end++;
        }

        return (start, end);
    }

    private void CoerceDirectSelectionIntoBounds()
    {
        if (Content is not string text)
        {
            _directSelectionStart = 0;
            _directSelectionLength = 0;
            _directSelectionAnchor = 0;
            _isSelectingDirectText = false;
            _isDirectWordSelecting = false;
            return;
        }

        _directSelectionStart = Math.Clamp(_directSelectionStart, 0, text.Length);
        _directSelectionLength = Math.Clamp(_directSelectionLength, 0, text.Length - _directSelectionStart);
        _directSelectionAnchor = Math.Clamp(_directSelectionAnchor, 0, text.Length);
    }

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (!IsTextSelectionEnabled || _labelBorder != null || e.Handled || Content is not string text)
        {
            return;
        }

        if (e.IsControlDown && e.Key == Key.C && _directSelectionLength > 0)
        {
            Copy();
            e.Handled = true;
            return;
        }

        if (e.IsControlDown && e.Key == Key.A && !string.IsNullOrEmpty(text))
        {
            _directSelectionAnchor = 0;
            ApplyDirectSelection(0, text.Length);
            e.Handled = true;
        }
    }

    private void UpdateDirectTextCursor()
    {
        if (_labelBorder == null)
        {
            Cursor = CanShowDirectTextCursor() ? Jalium.UI.Cursors.IBeam : null;
        }
    }

    private void OnKeyboardFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        InvalidateVisual();
    }

    private bool CanShowDirectTextCursor()
    {
        return IsEnabled &&
            IsTextSelectionEnabled &&
            _labelBorder == null &&
            Content is string text &&
            !string.IsNullOrEmpty(text);
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // Prefer template measurement so the ContentPresenter/TextBlock tree is
        // measured before arrange. Falling back keeps untemplated labels working.
        if (Template != null)
        {
            return base.MeasureOverride(availableSize);
        }

        var padding = Padding;
        var border = BorderThickness;
        var contentAvailable = new Size(
            Math.Max(0, availableSize.Width - padding.TotalWidth - border.TotalWidth),
            Math.Max(0, availableSize.Height - padding.TotalHeight - border.TotalHeight));

        var contentSize = MeasureContent(contentAvailable);

        return new Size(
            contentSize.Width + padding.TotalWidth + border.TotalWidth,
            contentSize.Height + padding.TotalHeight + border.TotalHeight);
    }

    private Size MeasureContent(Size availableSize)
    {
        if (Content is string text)
        {
            var fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
            var fontSize = FontSize > 0 ? FontSize : 14;
            var formattedText = new FormattedText(text, fontFamily, fontSize);
            TextMeasurement.MeasureText(formattedText);
            return new Size(formattedText.Width, formattedText.Height);
        }

        if (Content is UIElement element)
        {
            element.Measure(availableSize);
            return element.DesiredSize;
        }

        return new Size(0, 0);
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        // If using template, let the template handle rendering
        if (_labelBorder != null)
            return;

        if (drawingContext is not DrawingContext dc)
            return;

        var rect = new Rect(RenderSize);
        var padding = Padding;

        // Draw background if set
        if (Background != null)
        {
            dc.DrawRectangle(Background, null, rect);
        }

        // Draw content
        if (Content is string text && Foreground != null)
        {
            var formattedText = new FormattedText(text, FontFamily, FontSize)
            {
                Foreground = Foreground
            };

            TextMeasurement.MeasureText(formattedText);

            // Calculate text position based on alignment
            var textX = padding.Left;
            var textY = padding.Top;

            switch (HorizontalAlignment)
            {
                case HorizontalAlignment.Center:
                    textX = (rect.Width - formattedText.Width) / 2;
                    break;
                case HorizontalAlignment.Right:
                    textX = rect.Width - formattedText.Width - padding.Right;
                    break;
            }

            switch (VerticalAlignment)
            {
                case VerticalAlignment.Center:
                    textY = (rect.Height - formattedText.Height) / 2;
                    break;
                case VerticalAlignment.Bottom:
                    textY = rect.Height - formattedText.Height - padding.Bottom;
                    break;
            }

            if (_directSelectionLength > 0)
            {
                DrawDirectSelection(dc, text, textX, textY, formattedText.Height);
            }

            dc.DrawText(formattedText, new Point(textX, textY));

            // Draw access key underline if set
            if (AccessKey.HasValue)
            {
                var accessKeyIndex = text.IndexOf(AccessKey.Value.ToString(), StringComparison.OrdinalIgnoreCase);
                if (accessKeyIndex >= 0)
                {
                    // Calculate underline position (approximate)
                    var preText = text.Substring(0, accessKeyIndex);
                    var preFormattedText = new FormattedText(preText, FontFamily, FontSize);
                    TextMeasurement.MeasureText(preFormattedText);

                    var charText = text.Substring(accessKeyIndex, 1);
                    var charFormattedText = new FormattedText(charText, FontFamily, FontSize);
                    TextMeasurement.MeasureText(charFormattedText);

                    var underlineX = textX + preFormattedText.Width;
                    var underlineY = textY + formattedText.Height - 2;
                    var underlineWidth = charFormattedText.Width;

                    var underlinePen = new Pen(Foreground, 1);
                    dc.DrawLine(underlinePen, new Point(underlineX, underlineY), new Point(underlineX + underlineWidth, underlineY));
                }
            }
        }
    }

    #endregion

    #region Property Changed Callbacks

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Label label)
        {
            label.ApplyPresentedTextStyle();
            label.InvalidateVisual();
        }
    }

    private static void OnIsTextSelectionEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Label label)
        {
            return;
        }

        if (!(bool)(e.NewValue ?? false))
        {
            label.ClearDirectSelectionState();
        }

        label.ApplyPresentedTextStyle();
        label.UpdateDirectTextCursor();
        label.InvalidateVisual();
    }

    private void ApplyPresentedTextStyle()
    {
        if (_labelBorder == null || Content is UIElement)
        {
            return;
        }

        if (FindDescendantTextBlock(_labelBorder) is not TextBlock textBlock)
        {
            return;
        }

        var foreground = ResolveEffectiveForegroundBrush();
        if (foreground != null)
        {
            textBlock.Foreground = foreground;
        }

        textBlock.FontFamily = FontFamily;
        textBlock.FontSize = FontSize;
        textBlock.FontStyle = FontStyle;
        textBlock.FontWeight = FontWeight;
        textBlock.IsTextSelectionEnabled = IsTextSelectionEnabled;
        if (!IsTextSelectionEnabled)
        {
            textBlock.ClearSelection();
        }
    }

    private void ClearDirectSelectionState()
    {
        _pendingTemplateTextFocus = false;
        _isSelectingDirectText = false;
        _isDirectWordSelecting = false;
        _directSelectionAnchor = 0;
        ApplyDirectSelection(0, 0);
        ReleaseMouseCapture();
    }

    private static TextBlock? FindDescendantTextBlock(Visual root)
    {
        if (root is TextBlock textBlock)
        {
            return textBlock;
        }

        for (int i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is Visual child)
            {
                var result = FindDescendantTextBlock(child);
                if (result != null)
                {
                    return result;
                }
            }
        }

        return null;
    }

    private Brush? ResolveEffectiveForegroundBrush()
    {
        if (HasLocalValue(Control.ForegroundProperty) && Foreground != null)
        {
            return Foreground;
        }

        return TryFindResource("TextSecondary") as Brush
            ?? TryFindResource("TextPrimary") as Brush
            ?? Foreground;
    }

    private void DrawDirectSelection(DrawingContext dc, string text, double textX, double textY, double textHeight)
    {
        var selectionBrush = TryFindResource("SelectionBackground") as Brush
            ?? TryFindResource("AccentFillColorSelectedTextBackgroundBrush") as Brush
            ?? s_defaultSelectionBrush;

        var textBefore = text.Substring(0, _directSelectionStart);
        var selectedText = text.Substring(_directSelectionStart, _directSelectionLength);
        var startX = textX + MeasureDirectTextWidth(textBefore);
        var width = Math.Max(1, MeasureDirectTextWidth(selectedText));

        dc.DrawRectangle(selectionBrush, null, new Rect(startX, textY, width, textHeight));
    }

    private int GetDirectTextCharacterIndex(Point position, string text)
    {
        var origin = GetDirectTextOrigin(text);
        var relativeX = position.X - origin.X;
        if (relativeX <= 0)
        {
            return 0;
        }

        int index = text.Length;
        double previousWidth = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            var width = MeasureDirectTextWidth(text.Substring(0, i));
            if (width >= relativeX)
            {
                index = i;
                if (i > 0 && (relativeX - previousWidth) < (width - relativeX))
                {
                    index = i - 1;
                }

                break;
            }

            previousWidth = width;
        }

        return index;
    }

    private Point GetDirectTextOrigin(string text)
    {
        var rect = new Rect(RenderSize);
        var padding = Padding;
        var formattedText = new FormattedText(text, FontFamily, FontSize);
        TextMeasurement.MeasureText(formattedText);

        var textX = padding.Left;
        var textY = padding.Top;

        switch (HorizontalAlignment)
        {
            case HorizontalAlignment.Center:
                textX = (rect.Width - formattedText.Width) / 2;
                break;
            case HorizontalAlignment.Right:
                textX = rect.Width - formattedText.Width - padding.Right;
                break;
        }

        switch (VerticalAlignment)
        {
            case VerticalAlignment.Center:
                textY = (rect.Height - formattedText.Height) / 2;
                break;
            case VerticalAlignment.Bottom:
                textY = rect.Height - formattedText.Height - padding.Bottom;
                break;
        }

        return new Point(textX, textY);
    }

    private double MeasureDirectTextWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var formattedText = new FormattedText(text, FontFamily, FontSize);
        TextMeasurement.MeasureText(formattedText);
        return formattedText.Width;
    }

    #endregion
}
