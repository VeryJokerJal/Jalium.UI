using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Threading;

using static Jalium.UI.Cursors;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Base class for text editing controls.
/// </summary>
public abstract class TextBoxBase : Control
{
    #region Content Host Fields

    /// <summary>
    /// The content host element from the template (PART_ContentHost).
    /// </summary>
    private FrameworkElement? _contentHost;

    /// <summary>
    /// The text rendering element inserted into the content host.
    /// </summary>
    private TextBoxContentHost? _textBoxContentHost;

    /// <summary>
    /// The bounds of the content area for text rendering (set by ArrangeTextContent).
    /// </summary>
    protected Size _textContentSize;

    /// <summary>
    /// Whether content host mode is active (template with PART_ContentHost).
    /// </summary>
    protected bool HasContentHost => _textBoxContentHost != null;

    #endregion

    #region Fields

    /// <summary>
    /// The caret index (character position).
    /// </summary>
    protected int _caretIndex;

    /// <summary>
    /// The selection start index.
    /// </summary>
    protected int _selectionStart;

    /// <summary>
    /// The selection length.
    /// </summary>
    protected int _selectionLength;

    /// <summary>
    /// Whether the caret is currently visible (for blinking).
    /// </summary>
    protected bool _caretVisible = true;

    /// <summary>
    /// The current caret opacity (0.0 to 1.0) for smooth animation.
    /// </summary>
    protected double _caretOpacity = 1.0;

    /// <summary>
    /// The start time of the current animation cycle.
    /// </summary>
    protected DateTime _caretAnimationStart;

    /// <summary>
    /// The last time the caret blinked.
    /// </summary>
    protected DateTime _lastCaretBlink;

    /// <summary>
    /// The caret blink interval in milliseconds.
    /// </summary>
    protected const int CaretBlinkInterval = 530;

    /// <summary>
    /// The duration of the fade animation in milliseconds.
    /// </summary>
    protected const int CaretFadeDuration = 150;

    /// <summary>
    /// Timer that drives caret blinking/fading without forcing full-frame rendering.
    /// </summary>
    private DispatcherTimer? _caretTimer;

    /// <summary>
    /// Tick interval during fade phases (ms). Hold phases use longer dynamic intervals.
    /// </summary>
    private const int CaretAnimationTickMs = 33;

    /// <summary>
    /// Whether the user is currently selecting text.
    /// </summary>
    protected bool _isSelecting;

    /// <summary>
    /// Whether the current drag gesture should expand by whole-word ranges.
    /// </summary>
    protected bool _isWordSelecting;

    /// <summary>
    /// The start of the word range captured when a double-click selection begins.
    /// </summary>
    protected int _wordSelectionAnchorStart;

    /// <summary>
    /// The end of the word range captured when a double-click selection begins.
    /// </summary>
    protected int _wordSelectionAnchorEnd;

    /// <summary>
    /// The anchor point for selection extension.
    /// </summary>
    protected int _selectionAnchor;

    /// <summary>
    /// The horizontal scroll offset.
    /// </summary>
    protected double _horizontalOffset;

    /// <summary>
    /// The vertical scroll offset.
    /// </summary>
    protected double _verticalOffset;

    /// <summary>
    /// The undo stack.
    /// </summary>
    protected readonly Stack<UndoEntry> _undoStack = new();

    /// <summary>
    /// The redo stack.
    /// </summary>
    protected readonly Stack<UndoEntry> _redoStack = new();

    /// <summary>
    /// Whether an undo/redo operation is in progress.
    /// </summary>
    protected bool _isUndoRedoing;

    /// <summary>
    /// The last text value (for undo tracking).
    /// </summary>
    protected string _lastText = string.Empty;

    // Double/Triple click
    private DateTime _lastClickTime;
    private int _clickCount;
    private Point _lastClickPosition;
    private const int DoubleClickTime = 500;
    private const double DoubleClickDistance = 4;

    // Context menu
    private Popup? _contextMenuPopup;
    private DispatcherTimer? _contextMenuAnimTimer;
    private bool _isContextMenuCloseAnimating;

    private const double ContextMenuOpenMs = 200;
    private const double ContextMenuCloseMs = 150;
    private static readonly CubicEase ContextMenuOpenEase = new() { EasingMode = EasingMode.EaseOut };
    private static readonly CubicEase ContextMenuCloseEase = new() { EasingMode = EasingMode.EaseIn };

    private static readonly SolidColorBrush s_defaultSelectionBrush = new(Color.FromArgb(180, 0, 120, 212));
    private static readonly SolidColorBrush s_defaultCaretBrush = new(Color.White);

    // Static brushes for context menu rendering
    private static readonly SolidColorBrush s_ctxMenuBgBrush = new(Color.FromRgb(45, 45, 48));
    private static readonly SolidColorBrush s_ctxMenuBorderBrush = new(Color.FromRgb(67, 67, 70));
    private static readonly SolidColorBrush s_ctxMenuEnabledTextBrush = new(Color.White);
    private static readonly SolidColorBrush s_ctxMenuDisabledTextBrush = new(Color.FromRgb(90, 90, 90));
    private static readonly SolidColorBrush s_ctxMenuShortcutBrush = new(Color.FromRgb(140, 140, 140));
    private static readonly SolidColorBrush s_ctxMenuTransparentBrush = new(Color.Transparent);
    private static readonly SolidColorBrush s_ctxMenuHoverBrush = new(Color.FromRgb(60, 60, 60));
    private static readonly SolidColorBrush s_ctxMenuSeparatorBrush = new(Color.FromRgb(67, 67, 70));

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsReadOnly dependency property.
    /// </summary>
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(TextBoxBase),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the AcceptsReturn dependency property.
    /// </summary>
    public static readonly DependencyProperty AcceptsReturnProperty =
        DependencyProperty.Register(nameof(AcceptsReturn), typeof(bool), typeof(TextBoxBase),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the AcceptsTab dependency property.
    /// </summary>
    public static readonly DependencyProperty AcceptsTabProperty =
        DependencyProperty.Register(nameof(AcceptsTab), typeof(bool), typeof(TextBoxBase),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the SelectionBrush dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectionBrushProperty =
        DependencyProperty.Register(nameof(SelectionBrush), typeof(Brush), typeof(TextBoxBase),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the CaretBrush dependency property.
    /// </summary>
    public static readonly DependencyProperty CaretBrushProperty =
        DependencyProperty.Register(nameof(CaretBrush), typeof(Brush), typeof(TextBoxBase),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsUndoEnabled dependency property.
    /// </summary>
    public static readonly DependencyProperty IsUndoEnabledProperty =
        DependencyProperty.Register(nameof(IsUndoEnabled), typeof(bool), typeof(TextBoxBase),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the UndoLimit dependency property.
    /// </summary>
    public static readonly DependencyProperty UndoLimitProperty =
        DependencyProperty.Register(nameof(UndoLimit), typeof(int), typeof(TextBoxBase),
            new PropertyMetadata(100));

    /// <summary>
    /// Identifies the HorizontalScrollBarVisibility dependency property.
    /// </summary>
    public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty =
        DependencyProperty.Register(nameof(HorizontalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(TextBoxBase),
            new PropertyMetadata(ScrollBarVisibility.Hidden));

    /// <summary>
    /// Identifies the VerticalScrollBarVisibility dependency property.
    /// </summary>
    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty =
        DependencyProperty.Register(nameof(VerticalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(TextBoxBase),
            new PropertyMetadata(ScrollBarVisibility.Hidden));

    /// <summary>
    /// Identifies the TextTrimming dependency property.
    /// </summary>
    public static readonly DependencyProperty TextTrimmingProperty =
        DependencyProperty.Register(nameof(TextTrimming), typeof(TextTrimming), typeof(TextBoxBase),
            new PropertyMetadata(TextTrimming.CharacterEllipsis, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets whether the text box is read-only.
    /// </summary>
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty)!;
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the text box accepts Enter key for new lines.
    /// </summary>
    public bool AcceptsReturn
    {
        get => (bool)GetValue(AcceptsReturnProperty)!;
        set => SetValue(AcceptsReturnProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the text box accepts Tab key for tab characters.
    /// </summary>
    public bool AcceptsTab
    {
        get => (bool)GetValue(AcceptsTabProperty)!;
        set => SetValue(AcceptsTabProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for text selection highlighting.
    /// </summary>
    public Brush? SelectionBrush
    {
        get => (Brush?)GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for the caret.
    /// </summary>
    public Brush? CaretBrush
    {
        get => (Brush?)GetValue(CaretBrushProperty);
        set => SetValue(CaretBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets whether undo is enabled.
    /// </summary>
    public bool IsUndoEnabled
    {
        get => (bool)GetValue(IsUndoEnabledProperty)!;
        set => SetValue(IsUndoEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum number of undo entries.
    /// </summary>
    public int UndoLimit
    {
        get => (int)GetValue(UndoLimitProperty)!;
        set => SetValue(UndoLimitProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal scroll bar visibility.
    /// </summary>
    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(HorizontalScrollBarVisibilityProperty)!;
        set => SetValue(HorizontalScrollBarVisibilityProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical scroll bar visibility.
    /// </summary>
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty)!;
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
    }

    /// <summary>
    /// Gets or sets the trimming behavior for visible text when it overflows the content area.
    /// </summary>
    public TextTrimming TextTrimming
    {
        get => (TextTrimming)GetValue(TextTrimmingProperty)!;
        set => SetValue(TextTrimmingProperty, value);
    }

    /// <summary>
    /// Gets whether undo can be performed.
    /// </summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>
    /// Gets whether redo can be performed.
    /// </summary>
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// Gets or sets the horizontal scroll offset.
    /// </summary>
    public double HorizontalOffset
    {
        get => _horizontalOffset;
        set
        {
            var newValue = Math.Max(0, value);
            if (Math.Abs(_horizontalOffset - newValue) > 0.001)
            {
                _horizontalOffset = newValue;
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Gets or sets the vertical scroll offset.
    /// </summary>
    public double VerticalOffset
    {
        get => _verticalOffset;
        set
        {
            var newValue = Math.Max(0, value);
            if (Math.Abs(_verticalOffset - newValue) > 0.001)
            {
                _verticalOffset = newValue;
                InvalidateVisual();
            }
        }
    }

    #endregion

    #region Abstract/Virtual Members

    /// <summary>
    /// Gets the text content.
    /// </summary>
    protected abstract string GetText();

    /// <summary>
    /// Sets the text content.
    /// </summary>
    protected abstract void SetText(string value);

    /// <summary>
    /// Gets the line height.
    /// </summary>
    protected abstract double GetLineHeight();

    /// <summary>
    /// Measures the width of text using the current font settings.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <returns>The width of the text.</returns>
    protected abstract double MeasureTextWidth(string text);

    /// <summary>
    /// Gets the line count.
    /// </summary>
    protected abstract int GetLineCount();

    /// <summary>
    /// Gets the line and column from a character index.
    /// </summary>
    protected abstract (int lineIndex, int columnIndex) GetLineColumnFromCharIndex(int charIndex);

    /// <summary>
    /// Gets the character index from a line and column.
    /// </summary>
    protected abstract int GetCharIndexFromLineColumn(int lineIndex, int columnIndex);

    /// <summary>
    /// Gets the text of a specific line.
    /// </summary>
    protected abstract string GetLineTextInternal(int lineIndex);

    /// <summary>
    /// Gets the start index of a line.
    /// </summary>
    protected abstract int GetLineStartIndex(int lineIndex);

    /// <summary>
    /// Gets the length of a line.
    /// </summary>
    protected abstract int GetLineLengthInternal(int lineIndex);

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="TextBoxBase"/> class.
    /// </summary>
    protected TextBoxBase()
    {
        Focusable = true;
        Cursor = IBeam; // Text input cursor for text editing controls

        _lastCaretBlink = DateTime.Now;
        _lastClickTime = DateTime.MinValue;

        // Register input event handlers
        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new RoutedEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new RoutedEventHandler(OnMouseMoveHandler));
        AddHandler(KeyDownEvent, new RoutedEventHandler(OnKeyDownHandler));
        AddHandler(TextInputEvent, new RoutedEventHandler(OnTextInputHandler));
        AddHandler(MouseWheelEvent, new RoutedEventHandler(OnMouseWheelHandler));
    }

    #endregion

    #region Template Handling

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Clean up previous content host
        if (_contentHost != null && _textBoxContentHost != null)
        {
            RemoveContentHostChild(_contentHost, _textBoxContentHost);
            _textBoxContentHost = null;
            _contentHost = null;
        }

        // Find PART_ContentHost in the template
        if (GetTemplateChild("PART_ContentHost") is FrameworkElement contentHost)
        {
            _contentHost = contentHost;

            // Create the text rendering element
            _textBoxContentHost = new TextBoxContentHost(this);

            // Insert into the content host
            AddContentHostChild(_contentHost, _textBoxContentHost);
        }
    }

    /// <summary>
    /// Adds the text rendering element to the content host.
    /// </summary>
    private void AddContentHostChild(FrameworkElement host, TextBoxContentHost child)
    {
        // Support different container types
        if (host is Border border)
        {
            border.Child = child;
        }
        else if (host is ContentControl contentControl)
        {
            contentControl.Content = child;
        }
        else if (host is ScrollViewer scrollViewer)
        {
            scrollViewer.Content = child;
        }
        else if (host is Panel panel)
        {
            panel.Children.Add(child);
        }
    }

    /// <summary>
    /// Removes the text rendering element from the content host.
    /// </summary>
    private void RemoveContentHostChild(FrameworkElement host, TextBoxContentHost child)
    {
        if (host is Border border)
        {
            if (border.Child == child)
                border.Child = null;
        }
        else if (host is ContentControl contentControl)
        {
            if (contentControl.Content == child)
                contentControl.Content = null;
        }
        else if (host is ScrollViewer scrollViewer)
        {
            if (scrollViewer.Content == child)
                scrollViewer.Content = null;
        }
        else if (host is Panel panel)
        {
            panel.Children.Remove(child);
        }
    }

    /// <summary>
    /// Measures the text content. Called by TextBoxContentHost.
    /// </summary>
    internal virtual Size MeasureTextContent(Size availableSize)
    {
        // Default implementation - subclasses should override
        var lineHeight = Math.Round(GetLineHeight());
        var lineCount = GetLineCount();

        double textHeight;
        if (lineCount > 1)
        {
            textHeight = lineCount * lineHeight;
        }
        else
        {
            textHeight = lineHeight;
        }

        return new Size(availableSize.Width, Math.Min(textHeight, availableSize.Height));
    }

    /// <summary>
    /// Arranges the text content. Called by TextBoxContentHost.
    /// </summary>
    internal virtual void ArrangeTextContent(Size finalSize)
    {
        _textContentSize = finalSize;
    }

    /// <summary>
    /// Renders the text content. Called by TextBoxContentHost.
    /// Override in subclasses to implement actual text rendering.
    /// </summary>
    internal abstract void RenderTextContent(object drawingContext);

    #endregion

    #region Selection Properties

    /// <summary>
    /// Gets or sets the caret position (character index).
    /// </summary>
    public int CaretIndex
    {
        get => _caretIndex;
        set
        {
            var text = GetText();
            var newValue = Math.Clamp(text.Length, 0, value);
            if (_caretIndex != newValue)
            {
                _caretIndex = newValue;
                ResetCaretBlink();
                EnsureCaretVisible();
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Gets or sets the starting position of selected text.
    /// </summary>
    public int SelectionStart
    {
        get => _selectionStart;
        set
        {
            var text = GetText();
            var newValue = Math.Clamp(text.Length, 0, value);
            if (_selectionStart != newValue)
            {
                _selectionStart = newValue;
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Gets or sets the number of characters selected.
    /// </summary>
    public int SelectionLength
    {
        get => _selectionLength;
        set
        {
            var text = GetText();
            var maxLength = text.Length - _selectionStart;
            var newValue = Math.Clamp(maxLength, 0, value);
            if (_selectionLength != newValue)
            {
                _selectionLength = newValue;
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Gets the selected text.
    /// </summary>
    public string SelectedText
    {
        get
        {
            if (_selectionLength == 0)
                return string.Empty;

            var text = GetText();
            var start = Math.Min(_selectionStart, text.Length);
            var length = Math.Min(_selectionLength, text.Length - start);
            return text.Substring(start, length);
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Selects all text in the text box.
    /// </summary>
    public void SelectAll()
    {
        var text = GetText();
        _selectionStart = 0;
        _selectionLength = text.Length;
        _caretIndex = text.Length;
        InvalidateVisual();
        OnSelectionChanged();
    }

    /// <summary>
    /// Selects a range of text.
    /// </summary>
    public void Select(int start, int length)
    {
        var text = GetText();
        _selectionStart = Math.Clamp(text.Length, 0, start);
        _selectionLength = Math.Clamp(text.Length - _selectionStart, 0, length);
        _caretIndex = _selectionStart + _selectionLength;
        InvalidateVisual();
        OnSelectionChanged();
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    public void ClearSelection()
    {
        if (_selectionLength > 0)
        {
            _selectionLength = 0;
            InvalidateVisual();
            OnSelectionChanged();
        }
    }

    /// <summary>
    /// Copies the selected text to the clipboard.
    /// </summary>
    public void Copy()
    {
        var selectedText = SelectedText;
        if (!string.IsNullOrEmpty(selectedText))
        {
            Clipboard.SetText(selectedText);
        }
    }

    /// <summary>
    /// Cuts the selected text to the clipboard.
    /// </summary>
    public void Cut()
    {
        if (IsReadOnly || _selectionLength == 0)
            return;

        Copy();
        DeleteSelection();
    }

    /// <summary>
    /// Pastes text from the clipboard.
    /// </summary>
    public void Paste()
    {
        if (IsReadOnly)
            return;

        var clipboardText = Clipboard.GetText();
        if (!string.IsNullOrEmpty(clipboardText))
        {
            // Filter out newlines if AcceptsReturn is false
            if (!AcceptsReturn)
            {
                clipboardText = clipboardText.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
            }
            InsertText(clipboardText);
        }
    }

    /// <summary>
    /// Undoes the last edit operation.
    /// </summary>
    public virtual void Undo()
    {
        if (!IsUndoEnabled || _undoStack.Count == 0)
            return;

        _isUndoRedoing = true;
        try
        {
            var entry = _undoStack.Pop();
            _redoStack.Push(new UndoEntry(GetText(), _caretIndex, _selectionStart, _selectionLength));

            SetText(entry.Text);
            _caretIndex = entry.CaretIndex;
            _selectionStart = entry.SelectionStart;
            _selectionLength = entry.SelectionLength;
        }
        finally
        {
            _isUndoRedoing = false;
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Redoes the last undone operation.
    /// </summary>
    public virtual void Redo()
    {
        if (!IsUndoEnabled || _redoStack.Count == 0)
            return;

        _isUndoRedoing = true;
        try
        {
            var entry = _redoStack.Pop();
            _undoStack.Push(new UndoEntry(GetText(), _caretIndex, _selectionStart, _selectionLength));

            SetText(entry.Text);
            _caretIndex = entry.CaretIndex;
            _selectionStart = entry.SelectionStart;
            _selectionLength = entry.SelectionLength;
        }
        finally
        {
            _isUndoRedoing = false;
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Scrolls to make the caret visible.
    /// </summary>
    public void ScrollToCaretPosition()
    {
        EnsureCaretVisible();
    }

    #endregion

    #region Protected Methods

    /// <summary>
    /// Inserts text at the current caret position.
    /// </summary>
    protected virtual void InsertText(string textToInsert)
    {
        if (IsReadOnly || string.IsNullOrEmpty(textToInsert))
            return;

        PushUndo();

        // Delete selection if any
        if (_selectionLength > 0)
        {
            DeleteSelectionInternal();
        }

        var text = GetText();

        // Insert text
        SetText(text.Substring(0, _caretIndex) + textToInsert + text.Substring(_caretIndex));
        _caretIndex += textToInsert.Length;

        ResetCaretBlink();
        EnsureCaretVisible();
    }

    /// <summary>
    /// Deletes the current selection.
    /// </summary>
    protected virtual void DeleteSelection()
    {
        if (_selectionLength == 0)
            return;

        PushUndo();
        DeleteSelectionInternal();
        EnsureCaretVisible();
    }

    /// <summary>
    /// Deletes the selection without pushing undo.
    /// </summary>
    protected void DeleteSelectionInternal()
    {
        if (_selectionLength == 0)
            return;

        var text = GetText();

        // Ensure selection bounds are valid
        if (_selectionStart < 0) _selectionStart = 0;
        if (_selectionStart > text.Length) _selectionStart = text.Length;
        if (_selectionStart + _selectionLength > text.Length)
            _selectionLength = text.Length - _selectionStart;

        SetText(text.Substring(0, _selectionStart) + text.Substring(_selectionStart + _selectionLength));
        _caretIndex = _selectionStart;
        _selectionLength = 0;

        OnSelectionChanged();
    }

    /// <summary>
    /// Extends the selection to the new caret position.
    /// </summary>
    protected void ExtendSelection(int newCaretIndex)
    {
        if (_selectionLength == 0)
        {
            _selectionAnchor = _caretIndex;
        }

        _selectionStart = Math.Min(_selectionAnchor, newCaretIndex);
        _selectionLength = Math.Abs(newCaretIndex - _selectionAnchor);
        _caretIndex = newCaretIndex;

        OnSelectionChanged();
    }

    /// <summary>
    /// Pushes the current state to the undo stack.
    /// </summary>
    protected void PushUndo()
    {
        if (!IsUndoEnabled || _isUndoRedoing)
            return;

        var currentText = GetText();

        // Don't push if text hasn't changed
        if (currentText == _lastText)
            return;

        _undoStack.Push(new UndoEntry(_lastText, _caretIndex, _selectionStart, _selectionLength));
        _redoStack.Clear();

        // Limit stack size
        while (_undoStack.Count > UndoLimit)
        {
            var temp = new Stack<UndoEntry>();
            while (_undoStack.Count > 1)
            {
                temp.Push(_undoStack.Pop());
            }
            _undoStack.Pop(); // Remove oldest
            while (temp.Count > 0)
            {
                _undoStack.Push(temp.Pop());
            }
        }

        _lastText = currentText;
    }

    /// <summary>
    /// Resets the caret blink state.
    /// </summary>
    protected void ResetCaretBlink()
    {
        _caretVisible = true;
        _caretOpacity = 1.0;
        _lastCaretBlink = DateTime.Now;
        _caretAnimationStart = DateTime.Now;

        if (_caretTimer is { IsEnabled: true })
        {
            ScheduleNextCaretTick(_lastCaretBlink);
        }
    }

    /// <summary>
    /// Updates the caret animation state and returns the current opacity.
    /// Call this during rendering to get smooth animated opacity.
    /// </summary>
    /// <returns>The current caret opacity (0.0 to 1.0).</returns>
    protected double UpdateCaretAnimation()
    {
        var now = DateTime.Now;
        var elapsed = (now - _lastCaretBlink).TotalMilliseconds;

        // Full cycle: visible -> fade out -> hidden -> fade in
        // Total cycle time = (BlinkInterval + FadeDuration) * 2
        var fullCycleTime = (CaretBlinkInterval + CaretFadeDuration) * 2.0;
        var timeInCycle = elapsed % fullCycleTime;

        double targetOpacity;

        // Phase boundaries:
        // 0 to BlinkInterval: fully visible
        // BlinkInterval to BlinkInterval+FadeDuration: fading out
        // BlinkInterval+FadeDuration to BlinkInterval*2+FadeDuration: fully hidden
        // BlinkInterval*2+FadeDuration to fullCycleTime: fading in

        double visibleEnd = CaretBlinkInterval;
        double fadeOutEnd = CaretBlinkInterval + CaretFadeDuration;
        double hiddenEnd = CaretBlinkInterval * 2 + CaretFadeDuration;
        // fadeInEnd = fullCycleTime

        if (timeInCycle < visibleEnd)
        {
            // Fully visible phase
            targetOpacity = 1.0;
        }
        else if (timeInCycle < fadeOutEnd)
        {
            // Fading out phase
            double progress = (timeInCycle - visibleEnd) / CaretFadeDuration;
            targetOpacity = 1.0 - EaseInOutQuad(progress);
        }
        else if (timeInCycle < hiddenEnd)
        {
            // Fully hidden phase
            targetOpacity = 0.0;
        }
        else
        {
            // Fading in phase
            double progress = (timeInCycle - hiddenEnd) / CaretFadeDuration;
            targetOpacity = EaseInOutQuad(progress);
        }

        _caretOpacity = targetOpacity;
        _caretVisible = _caretOpacity > 0.01;

        return _caretOpacity;
    }

    /// <summary>
    /// Ease-in-out quadratic easing function for smooth animation.
    /// </summary>
    /// <param name="t">Progress value from 0.0 to 1.0.</param>
    /// <returns>The eased value from 0.0 to 1.0.</returns>
    private static double EaseInOutQuad(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        if (t < 0.5)
        {
            return 2.0 * t * t;
        }
        else
        {
            return 1.0 - Math.Pow(-2.0 * t + 2.0, 2) / 2.0;
        }
    }

    /// <summary>
    /// Schedules the next caret timer tick based on the current blink/fade phase.
    /// This avoids continuous per-frame invalidation when caret is fully visible/hidden.
    /// </summary>
    private void ScheduleNextCaretTick(DateTime now)
    {
        if (_caretTimer == null)
        {
            return;
        }

        var elapsed = (now - _lastCaretBlink).TotalMilliseconds;
        var fullCycleTime = (CaretBlinkInterval + CaretFadeDuration) * 2.0;
        var timeInCycle = elapsed % fullCycleTime;

        double visibleEnd = CaretBlinkInterval;
        double fadeOutEnd = CaretBlinkInterval + CaretFadeDuration;
        double hiddenEnd = CaretBlinkInterval * 2 + CaretFadeDuration;

        double intervalMs;
        if (timeInCycle < visibleEnd)
        {
            // Fully visible hold phase.
            intervalMs = visibleEnd - timeInCycle;
        }
        else if (timeInCycle < fadeOutEnd)
        {
            // Fade-out phase.
            intervalMs = Math.Min(CaretAnimationTickMs, fadeOutEnd - timeInCycle);
        }
        else if (timeInCycle < hiddenEnd)
        {
            // Fully hidden hold phase.
            intervalMs = hiddenEnd - timeInCycle;
        }
        else
        {
            // Fade-in phase.
            intervalMs = Math.Min(CaretAnimationTickMs, fullCycleTime - timeInCycle);
        }

        _caretTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, Math.Ceiling(intervalMs)));
    }

    /// <summary>
    /// Ensures the caret is visible by scrolling if necessary.
    /// </summary>
    protected virtual void EnsureCaretVisible()
    {
        var border = BorderThickness;
        var padding = Padding;
        // Round content dimensions to prevent sub-pixel scroll offset calculations
        // that can cause text jittering when the offset alternates between values
        var contentWidth = Math.Round(RenderSize.Width - border.Left - border.Right - padding.Left - padding.Right);
        var contentHeight = Math.Round(RenderSize.Height - border.Top - border.Bottom - padding.Top - padding.Bottom);

        if (contentWidth <= 0 || contentHeight <= 0)
            return;

        // Round line height for consistent calculations
        var lineHeight = Math.Round(GetLineHeight());

        var (lineIndex, columnIndex) = GetLineColumnFromCharIndex(_caretIndex);

        // Ensure valid indices
        if (lineIndex < 0) lineIndex = 0;
        if (columnIndex < 0) columnIndex = 0;

        // Get the text before the caret to measure its width
        var lineText = GetLineTextInternal(lineIndex);
        var textBeforeCaret = lineText.Substring(0, Math.Clamp(lineText.Length, 0, columnIndex));
        var caretX = Math.Round(MeasureTextWidth(textBeforeCaret));
        var caretY = lineIndex * lineHeight;

        // Horizontal scrolling
        if (caretX < _horizontalOffset)
        {
            _horizontalOffset = caretX;
        }
        else if (caretX > _horizontalOffset + contentWidth - 2)
        {
            _horizontalOffset = caretX - contentWidth + 2;
        }

        // Vertical scrolling
        // Handle edge case: if content height is less than line height (single-line box smaller than text),
        // don't scroll vertically to avoid oscillation between offset=0 and offset=1
        if (contentHeight >= lineHeight)
        {
            if (caretY < _verticalOffset)
            {
                _verticalOffset = caretY;
            }
            else if (caretY + lineHeight > _verticalOffset + contentHeight)
            {
                _verticalOffset = caretY + lineHeight - contentHeight;
            }
        }
        else
        {
            // Box is too small for even one line - keep offset at 0
            _verticalOffset = 0;
        }

        // Round final offsets to prevent sub-pixel jittering
        _horizontalOffset = Math.Round(Math.Max(0, _horizontalOffset));
        _verticalOffset = Math.Round(Math.Max(0, _verticalOffset));
    }

    /// <summary>
    /// Called when the selection changes.
    /// </summary>
    protected virtual void OnSelectionChanged()
    {
    }

    /// <summary>
    /// Gets the caret index from a mouse position.
    /// </summary>
    protected virtual int GetCaretIndexFromPosition(Point position)
    {
        var border = BorderThickness;
        var padding = Padding;
        // Round line height for consistent calculations
        var lineHeight = Math.Round(GetLineHeight());

        var contentX = position.X - border.Left - padding.Left + _horizontalOffset;
        var contentY = position.Y - border.Top - padding.Top + _verticalOffset;

        var lineCount = GetLineCount();
        var lineIndex = Math.Max(0, Math.Min((int)(contentY / lineHeight), lineCount - 1));

        // Get the line text and find the column by measuring
        var lineText = GetLineTextInternal(lineIndex);
        var lineStart = GetLineStartIndex(lineIndex);

        // Binary search or linear search for the column that matches contentX
        int columnIndex = 0;
        double prevWidth = 0;

        for (int i = 0; i <= lineText.Length; i++)
        {
            var width = MeasureTextWidth(lineText.Substring(0, i));
            if (width >= contentX)
            {
                // Determine if we're closer to this position or the previous
                if (i > 0 && (contentX - prevWidth) < (width - contentX))
                {
                    columnIndex = i - 1;
                }
                else
                {
                    columnIndex = i;
                }
                break;
            }
            prevWidth = width;
            columnIndex = i;
        }

        return lineStart + columnIndex;
    }

    #endregion

    #region Input Handling

    private void OnKeyDownHandler(object sender, RoutedEventArgs e)
    {
        if (e is not KeyEventArgs keyArgs || keyArgs.Handled)
            return;

        OnKeyDown(keyArgs);
    }

    /// <summary>
    /// Handles key down events.
    /// </summary>
    protected virtual void OnKeyDown(KeyEventArgs e)
    {
        var shift = e.IsShiftDown;
        var ctrl = e.IsControlDown;

        switch (e.Key)
        {
            case Key.Left:
                HandleLeftKey(shift, ctrl);
                e.Handled = true;
                break;

            case Key.Right:
                HandleRightKey(shift, ctrl);
                e.Handled = true;
                break;

            case Key.Up:
                HandleUpKey(shift);
                e.Handled = true;
                break;

            case Key.Down:
                HandleDownKey(shift);
                e.Handled = true;
                break;

            case Key.Home:
                HandleHomeKey(shift, ctrl);
                e.Handled = true;
                break;

            case Key.End:
                HandleEndKey(shift, ctrl);
                e.Handled = true;
                break;

            case Key.Back:
                HandleBackspace(ctrl);
                e.Handled = true;
                break;

            case Key.Delete:
                HandleDelete(ctrl);
                e.Handled = true;
                break;

            case Key.Enter:
                if (AcceptsReturn)
                {
                    InsertText("\n");
                    e.Handled = true;
                }
                break;

            case Key.Tab:
                if (AcceptsTab)
                {
                    InsertText("\t");
                    e.Handled = true;
                }
                break;

            case Key.A:
                if (ctrl)
                {
                    SelectAll();
                    e.Handled = true;
                }
                break;

            case Key.C:
                if (ctrl)
                {
                    Copy();
                    e.Handled = true;
                }
                break;

            case Key.X:
                if (ctrl)
                {
                    Cut();
                    e.Handled = true;
                }
                break;

            case Key.V:
                if (ctrl)
                {
                    Paste();
                    e.Handled = true;
                }
                break;

            case Key.Z:
                if (ctrl)
                {
                    if (shift)
                        Redo();
                    else
                        Undo();
                    e.Handled = true;
                }
                break;

            case Key.Y:
                if (ctrl)
                {
                    Redo();
                    e.Handled = true;
                }
                break;
        }
    }

    private void OnTextInputHandler(object sender, RoutedEventArgs e)
    {
        if (e is not TextCompositionEventArgs textArgs || textArgs.Handled || IsReadOnly)
            return;

        if (!string.IsNullOrEmpty(textArgs.Text))
        {
            // Filter control characters
            var text = textArgs.Text;
            if (text.Length == 1 && char.IsControl(text[0]) && text[0] != '\t')
                return;

            InsertText(text);
            textArgs.Handled = true;
        }
    }

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        // Don't handle clicks that originated from buttons in the template
        if (e.OriginalSource is DependencyObject source && IsInsideButton(source))
            return;

        if (e is MouseButtonEventArgs mouseArgs)
        {
            if (mouseArgs.ChangedButton == MouseButton.Left)
            {
                // Close context menu if open
                _contextMenuAnimTimer?.Stop();
                _isContextMenuCloseAnimating = false;
                CloseContextMenuImmediate();

                Focus();

                var position = mouseArgs.GetPosition(this);
                var now = DateTime.Now;

                // Detect double/triple click
                var timeSinceLastClick = (now - _lastClickTime).TotalMilliseconds;
                var distanceFromLastClick = Math.Abs(position.X - _lastClickPosition.X) + Math.Abs(position.Y - _lastClickPosition.Y);

                if (timeSinceLastClick < DoubleClickTime && distanceFromLastClick < DoubleClickDistance)
                {
                    _clickCount++;
                }
                else
                {
                    _clickCount = 1;
                }

                _lastClickTime = now;
                _lastClickPosition = position;

                if (_clickCount == 3)
                {
                    // Triple-click: select line or all
                    if (AcceptsReturn)
                    {
                        SelectCurrentLine();
                    }
                    else
                    {
                        SelectAll();
                    }
                    _clickCount = 0;
                }
                else if (_clickCount == 2)
                {
                    // Double-click: select word
                    SelectCurrentWord();
                    _wordSelectionAnchorStart = _selectionStart;
                    _wordSelectionAnchorEnd = _selectionStart + _selectionLength;
                    _isWordSelecting = _selectionLength > 0;
                    _isSelecting = true;
                    CaptureMouse();
                }
                else
                {
                    // Single click
                    CaptureMouse();
                    var newCaretIndex = GetCaretIndexFromPosition(position);

                    if ((mouseArgs.KeyboardModifiers & ModifierKeys.Shift) != 0)
                    {
                        ExtendSelection(newCaretIndex);
                    }
                    else
                    {
                        _caretIndex = newCaretIndex;
                        _selectionAnchor = newCaretIndex;
                        _selectionStart = newCaretIndex;
                        _selectionLength = 0;
                        _isWordSelecting = false;
                        _isSelecting = true;
                        OnSelectionChanged();
                    }

                    ResetCaretBlink();
                }

                InvalidateVisual();
                e.Handled = true;
            }
            else if (mouseArgs.ChangedButton == MouseButton.Right)
            {
                Focus();
                ShowContextMenu();
                e.Handled = true;
            }
        }
    }

    private void OnMouseUpHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        // Don't handle clicks that originated from buttons in the template
        if (e.OriginalSource is DependencyObject source && IsInsideButton(source))
            return;

        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            if (_isSelecting)
            {
                _isSelecting = false;
                _isWordSelecting = false;
                ReleaseMouseCapture();
            }

            e.Handled = true;
        }
    }

    /// <summary>
    /// Checks if the given element is inside a ButtonBase control.
    /// Used to allow clicks on buttons in templates to pass through.
    /// </summary>
    private static bool IsInsideButton(DependencyObject element)
    {
        var current = element;
        while (current != null)
        {
            if (current is ButtonBase)
                return true;
            current = (current as UIElement)?.VisualParent;
        }
        return false;
    }

    private void OnMouseMoveHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled || !_isSelecting) return;

        if (e is MouseEventArgs mouseArgs)
        {
            var position = mouseArgs.GetPosition(this);
            var newCaretIndex = GetCaretIndexFromPosition(position);

            if (_isWordSelecting)
            {
                ExtendWordSelection(newCaretIndex);
            }
            else
            {
                _selectionStart = Math.Min(_selectionAnchor, newCaretIndex);
                _selectionLength = Math.Abs(newCaretIndex - _selectionAnchor);
                _caretIndex = newCaretIndex;
            }

            EnsureCaretVisible();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    private void OnMouseWheelHandler(object sender, RoutedEventArgs e)
    {
        if (e is MouseWheelEventArgs wheelArgs)
        {
            // Round line height for consistent scrolling
            var lineHeight = Math.Round(GetLineHeight());
            var delta = wheelArgs.Delta > 0 ? -3 : 3;
            VerticalOffset += delta * lineHeight;

            // Clamp
            var maxOffset = Math.Max(0, GetLineCount() * lineHeight - RenderSize.Height);
            VerticalOffset = Math.Min(VerticalOffset, maxOffset);

            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnLostMouseCapture()
    {
        base.OnLostMouseCapture();
        if (_isSelecting)
        {
            _isSelecting = false;
        }

        _isWordSelecting = false;
    }

    /// <inheritdoc />
    protected override void OnIsKeyboardFocusedChanged(bool isFocused)
    {
        base.OnIsKeyboardFocusedChanged(isFocused);

        if (isFocused)
        {
            ResetCaretBlink();
            StartCaretTimer();
        }
        else
        {
            StopCaretTimer();
        }

        InvalidateVisual();
    }

    private void StartCaretTimer()
    {
        if (IsReadOnly)
            return;

        if (_caretTimer == null)
        {
            _caretTimer = new DispatcherTimer(DispatcherPriority.Background);
            _caretTimer.Tick += OnCaretTimerTick;
        }

        ScheduleNextCaretTick(DateTime.Now);
        _caretTimer.Start();
    }

    private void StopCaretTimer()
    {
        if (_caretTimer != null)
        {
            _caretTimer.Stop();
        }
    }

    private void OnCaretTimerTick(object? sender, EventArgs e)
    {
        if (!IsKeyboardFocused || IsReadOnly)
        {
            StopCaretTimer();
            return;
        }

        InvalidateVisual();
        ScheduleNextCaretTick(DateTime.Now);
    }

    #endregion

    #region Key Handlers

    private void HandleLeftKey(bool shift, bool ctrl)
    {
        int newIndex;

        if (ctrl)
        {
            newIndex = FindPreviousWordBoundary(_caretIndex);
        }
        else
        {
            newIndex = Math.Max(0, _caretIndex - 1);
        }

        if (shift)
        {
            ExtendSelection(newIndex);
        }
        else
        {
            if (_selectionLength > 0)
            {
                newIndex = _selectionStart;
                ClearSelection();
            }
            _caretIndex = newIndex;
        }

        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateVisual();
    }

    private void HandleRightKey(bool shift, bool ctrl)
    {
        var text = GetText();
        int newIndex;

        if (ctrl)
        {
            newIndex = FindNextWordBoundary(_caretIndex);
        }
        else
        {
            newIndex = Math.Min(text.Length, _caretIndex + 1);
        }

        if (shift)
        {
            ExtendSelection(newIndex);
        }
        else
        {
            if (_selectionLength > 0)
            {
                newIndex = _selectionStart + _selectionLength;
                ClearSelection();
            }
            _caretIndex = newIndex;
        }

        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateVisual();
    }

    private void HandleUpKey(bool shift)
    {
        if (!AcceptsReturn)
            return;

        var (lineIndex, columnIndex) = GetLineColumnFromCharIndex(_caretIndex);
        if (lineIndex == 0)
        {
            // Move to start of first line
            var newIndex = 0;
            if (shift)
                ExtendSelection(newIndex);
            else
            {
                ClearSelection();
                _caretIndex = newIndex;
            }
        }
        else
        {
            var newIndex = GetCharIndexFromLineColumn(lineIndex - 1, columnIndex);
            if (shift)
                ExtendSelection(newIndex);
            else
            {
                ClearSelection();
                _caretIndex = newIndex;
            }
        }

        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateVisual();
    }

    private void HandleDownKey(bool shift)
    {
        if (!AcceptsReturn)
            return;

        var lineCount = GetLineCount();
        var (lineIndex, columnIndex) = GetLineColumnFromCharIndex(_caretIndex);

        if (lineIndex >= lineCount - 1)
        {
            // Move to end of last line
            var newIndex = GetText().Length;
            if (shift)
                ExtendSelection(newIndex);
            else
            {
                ClearSelection();
                _caretIndex = newIndex;
            }
        }
        else
        {
            var newIndex = GetCharIndexFromLineColumn(lineIndex + 1, columnIndex);
            if (shift)
                ExtendSelection(newIndex);
            else
            {
                ClearSelection();
                _caretIndex = newIndex;
            }
        }

        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateVisual();
    }

    private void HandleHomeKey(bool shift, bool ctrl)
    {
        int newIndex;

        if (ctrl)
        {
            // Go to start of text
            newIndex = 0;
        }
        else
        {
            // Go to start of current line
            var (lineIndex, _) = GetLineColumnFromCharIndex(_caretIndex);
            newIndex = GetLineStartIndex(lineIndex);
        }

        if (shift)
        {
            ExtendSelection(newIndex);
        }
        else
        {
            ClearSelection();
            _caretIndex = newIndex;
        }

        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateVisual();
    }

    private void HandleEndKey(bool shift, bool ctrl)
    {
        int newIndex;

        if (ctrl)
        {
            // Go to end of text
            newIndex = GetText().Length;
        }
        else
        {
            // Go to end of current line
            var (lineIndex, _) = GetLineColumnFromCharIndex(_caretIndex);
            newIndex = GetLineStartIndex(lineIndex) + GetLineLengthInternal(lineIndex);
        }

        if (shift)
        {
            ExtendSelection(newIndex);
        }
        else
        {
            ClearSelection();
            _caretIndex = newIndex;
        }

        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateVisual();
    }

    private void HandleBackspace(bool ctrl)
    {
        if (IsReadOnly)
            return;

        if (_selectionLength > 0)
        {
            DeleteSelection();
        }
        else if (_caretIndex > 0)
        {
            PushUndo();

            int deleteCount;
            if (ctrl)
            {
                // Delete previous word
                var newIndex = FindPreviousWordBoundary(_caretIndex);
                deleteCount = _caretIndex - newIndex;
            }
            else
            {
                deleteCount = 1;
            }

            var text = GetText();
            var startIndex = Math.Max(0, _caretIndex - deleteCount);
            SetText(text.Substring(0, startIndex) + text.Substring(_caretIndex));
            _caretIndex = startIndex;

            // Ensure caret index doesn't go negative
            if (_caretIndex < 0) _caretIndex = 0;
        }

        EnsureCaretVisible();
    }

    private void HandleDelete(bool ctrl)
    {
        if (IsReadOnly)
            return;

        if (_selectionLength > 0)
        {
            DeleteSelection();
        }
        else
        {
            var text = GetText();
            if (_caretIndex < text.Length)
            {
                PushUndo();

                int deleteCount;
                if (ctrl)
                {
                    // Delete next word
                    var newIndex = FindNextWordBoundary(_caretIndex);
                    deleteCount = newIndex - _caretIndex;
                }
                else
                {
                    deleteCount = 1;
                }

                SetText(text.Substring(0, _caretIndex) + text.Substring(Math.Min(_caretIndex + deleteCount, text.Length)));
            }
        }

        EnsureCaretVisible();
    }

    private int FindPreviousWordBoundary(int index)
    {
        var text = GetText();
        if (index <= 0)
            return 0;

        index--;

        // Skip whitespace
        while (index > 0 && char.IsWhiteSpace(text[index]))
            index--;

        // Find start of word
        while (index > 0 && !char.IsWhiteSpace(text[index - 1]))
            index--;

        return index;
    }

    private int FindNextWordBoundary(int index)
    {
        var text = GetText();
        if (index >= text.Length)
            return text.Length;

        // Skip current word
        while (index < text.Length && !char.IsWhiteSpace(text[index]))
            index++;

        // Skip whitespace
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;

        return index;
    }

    private void ExtendWordSelection(int newCaretIndex)
    {
        var text = GetText();
        if (string.IsNullOrEmpty(text))
        {
            _selectionStart = 0;
            _selectionLength = 0;
            _caretIndex = 0;
            OnSelectionChanged();
            return;
        }

        var (currentWordStart, currentWordEnd) = GetWordRangeAtIndex(newCaretIndex);

        int selectionStart;
        int selectionEnd;
        if (currentWordEnd <= _wordSelectionAnchorStart)
        {
            selectionStart = currentWordStart;
            selectionEnd = _wordSelectionAnchorEnd;
            _caretIndex = selectionStart;
        }
        else if (currentWordStart >= _wordSelectionAnchorEnd)
        {
            selectionStart = _wordSelectionAnchorStart;
            selectionEnd = currentWordEnd;
            _caretIndex = selectionEnd;
        }
        else
        {
            selectionStart = _wordSelectionAnchorStart;
            selectionEnd = _wordSelectionAnchorEnd;
            _caretIndex = selectionEnd;
        }

        _selectionStart = selectionStart;
        _selectionLength = Math.Max(0, selectionEnd - selectionStart);
        OnSelectionChanged();
    }

    private (int start, int end) GetWordRangeAtIndex(int index)
    {
        var text = GetText();
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

    private void SelectCurrentWord()
    {
        var text = GetText();
        if (string.IsNullOrEmpty(text))
            return;

        var pos = _caretIndex;

        // Find word boundaries
        var start = pos;
        while (start > 0 && !IsWordBoundary(text[start - 1]))
            start--;

        var end = pos;
        while (end < text.Length && !IsWordBoundary(text[end]))
            end++;

        _selectionStart = start;
        _selectionLength = end - start;
        _caretIndex = end;
        _selectionAnchor = start;

        OnSelectionChanged();
    }

    private void SelectCurrentLine()
    {
        var (lineIndex, _) = GetLineColumnFromCharIndex(_caretIndex);
        var lineStart = GetLineStartIndex(lineIndex);
        var lineLength = GetLineLengthInternal(lineIndex);

        _selectionStart = lineStart;
        _selectionLength = lineLength;
        _caretIndex = lineStart + lineLength;
        _selectionAnchor = lineStart;

        OnSelectionChanged();
    }

    private static bool IsWordBoundary(char c)
    {
        return char.IsWhiteSpace(c) || char.IsPunctuation(c);
    }

    protected Brush? ResolveSelectionBrush()
    {
        if (HasLocalValue(SelectionBrushProperty))
            return SelectionBrush;

        return SelectionBrush
            ?? ResolveThemeBrush("SelectionBackground", s_defaultSelectionBrush, "AccentFillColorSelectedTextBackgroundBrush");
    }

    protected Brush? ResolveCaretBrush()
    {
        if (HasLocalValue(CaretBrushProperty))
            return CaretBrush;

        return CaretBrush
            ?? ((HasLocalValue(Control.ForegroundProperty) && Foreground != null) ? Foreground : null)
            ?? ResolveThemeBrush("TextPrimary", s_defaultCaretBrush, "TextFillColorPrimaryBrush");
    }

    protected Brush ResolveTextForegroundBrush()
    {
        if (HasLocalValue(Control.ForegroundProperty) && Foreground != null)
            return Foreground;

        return ResolveThemeBrush("TextPrimary", s_defaultCaretBrush, "TextFillColorPrimaryBrush");
    }

    private Brush ResolveThemeBrush(string primaryKey, Brush fallback, string? secondaryKey = null)
    {
        if (TryFindResource(primaryKey) is Brush primary)
            return primary;
        if (!string.IsNullOrWhiteSpace(secondaryKey) && TryFindResource(secondaryKey) is Brush secondary)
            return secondary;
        return fallback;
    }

    #endregion

    #region Context Menu

    private Brush ResolveContextMenuBackgroundBrush()
    {
        return ResolveThemeBrush("MenuFlyoutPresenterBackground", s_ctxMenuBgBrush, "SurfaceBackground");
    }

    private Brush ResolveContextMenuBorderBrush()
    {
        return ResolveThemeBrush("MenuFlyoutPresenterBorderBrush", s_ctxMenuBorderBrush, "ControlBorder");
    }

    private Brush ResolveContextMenuForegroundBrush()
    {
        return ResolveThemeBrush("TextPrimary", s_ctxMenuEnabledTextBrush, "TextFillColorPrimaryBrush");
    }

    private Brush ResolveContextMenuDisabledForegroundBrush()
    {
        return ResolveThemeBrush("TextDisabled", s_ctxMenuDisabledTextBrush, "TextFillColorDisabledBrush");
    }

    private Brush ResolveContextMenuShortcutForegroundBrush()
    {
        return ResolveThemeBrush("TextSecondary", s_ctxMenuShortcutBrush, "TextFillColorSecondaryBrush");
    }

    private Brush ResolveContextMenuHoverBackgroundBrush()
    {
        return ResolveThemeBrush("MenuFlyoutItemBackgroundHover", s_ctxMenuHoverBrush, "HighlightBackground");
    }

    private Brush ResolveContextMenuSeparatorBrush()
    {
        return ResolveThemeBrush("MenuFlyoutPresenterBorderBrush", s_ctxMenuSeparatorBrush, "ControlBorder");
    }

    private void ShowContextMenu()
    {
        // If close animation is in progress, stop it and close immediately
        if (_isContextMenuCloseAnimating)
        {
            _contextMenuAnimTimer?.Stop();
            _isContextMenuCloseAnimating = false;
        }
        CloseContextMenuImmediate();

        var hasSelection = _selectionLength > 0;
        var hasText = GetText().Length > 0;
        var hasClipboard = !string.IsNullOrEmpty(Clipboard.GetText());

        var panel = new StackPanel { MinWidth = 140 };

        if (!IsReadOnly)
        {
            AddContextMenuItem(panel, "还原", "Ctrl+Z", CanUndo, () => AnimateCloseContextMenu(() => Undo()));
            AddContextMenuItem(panel, "重做", "Ctrl+Y", CanRedo, () => AnimateCloseContextMenu(() => Redo()));
            AddContextMenuSeparator(panel);
            AddContextMenuItem(panel, "剪切", "Ctrl+X", hasSelection, () => AnimateCloseContextMenu(() => Cut()));
        }
        AddContextMenuItem(panel, "复制", "Ctrl+C", hasSelection, () => AnimateCloseContextMenu(() => Copy()));
        if (!IsReadOnly)
        {
            AddContextMenuItem(panel, "粘贴", "Ctrl+V", hasClipboard, () => AnimateCloseContextMenu(() => Paste()));
        }
        AddContextMenuSeparator(panel);
        AddContextMenuItem(panel, "全选", "Ctrl+A", hasText, () => AnimateCloseContextMenu(() => SelectAll()));

        var border = new Border
        {
            Background = ResolveContextMenuBackgroundBrush(),
            BorderBrush = ResolveContextMenuBorderBrush(),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(4),
            Child = panel
        };

        _contextMenuPopup = new Popup
        {
            StaysOpen = false,
            Placement = PlacementMode.MousePoint,
            PlacementTarget = this,
            Child = border
        };

        // Set initial animation state: invisible + shifted up
        border.Opacity = 0;
        border.RenderOffset = new Point(0, -6);

        _contextMenuPopup.IsOpen = true;

        // Animate open: fade in + slide down
        AnimateOpenContextMenu(border);
    }

    private void AnimateOpenContextMenu(FrameworkElement target)
    {
        _contextMenuAnimTimer?.Stop();

        var startTime = Environment.TickCount64;
        _contextMenuAnimTimer = new DispatcherTimer { Interval = CompositionTarget.FrameInterval };
        _contextMenuAnimTimer.Tick += (s, e) =>
        {
            var elapsed = Environment.TickCount64 - startTime;
            var progress = Math.Min(1.0, elapsed / ContextMenuOpenMs);
            var eased = ContextMenuOpenEase.Ease(progress);

            target.Opacity = eased;
            target.RenderOffset = new Point(0, -6 * (1.0 - eased));

            if (progress >= 1.0)
            {
                _contextMenuAnimTimer!.Stop();
                target.Opacity = 1;
                target.RenderOffset = default;
            }
        };
        _contextMenuAnimTimer.Start();
    }

    private void AnimateCloseContextMenu(Action? onComplete = null)
    {
        if (_contextMenuPopup == null) return;

        var target = _contextMenuPopup.Child as FrameworkElement;
        if (target == null)
        {
            onComplete?.Invoke();
            CloseContextMenuImmediate();
            return;
        }

        _contextMenuAnimTimer?.Stop();
        _isContextMenuCloseAnimating = true;

        var startOpacity = target.Opacity;
        var startOffsetY = target.RenderOffset.Y;
        var startTime = Environment.TickCount64;

        _contextMenuAnimTimer = new DispatcherTimer { Interval = CompositionTarget.FrameInterval };
        _contextMenuAnimTimer.Tick += (s, e) =>
        {
            var elapsed = Environment.TickCount64 - startTime;
            var progress = Math.Min(1.0, elapsed / ContextMenuCloseMs);
            var eased = ContextMenuCloseEase.Ease(progress);

            target.Opacity = startOpacity * (1.0 - eased);
            target.RenderOffset = new Point(0, startOffsetY + (-6 - startOffsetY) * eased);

            if (progress >= 1.0)
            {
                _contextMenuAnimTimer!.Stop();
                _isContextMenuCloseAnimating = false;
                onComplete?.Invoke();
                CloseContextMenuImmediate();
            }
        };
        _contextMenuAnimTimer.Start();
    }

    private void CloseContextMenu()
    {
        if (_isContextMenuCloseAnimating) return; // Animation will handle it
        if (_contextMenuPopup != null)
        {
            AnimateCloseContextMenu();
        }
    }

    private void CloseContextMenuImmediate()
    {
        if (_contextMenuPopup != null)
        {
            _contextMenuPopup.IsOpen = false;
            _contextMenuPopup = null;
        }
    }

    private void AddContextMenuItem(StackPanel panel, string text, string shortcut, bool isEnabled, Action onClick)
    {
        var foregroundBrush = isEnabled
            ? ResolveContextMenuForegroundBrush()
            : ResolveContextMenuDisabledForegroundBrush();
        var shortcutBrush = isEnabled
            ? ResolveContextMenuShortcutForegroundBrush()
            : ResolveContextMenuDisabledForegroundBrush();
        var hoverBrush = ResolveContextMenuHoverBackgroundBrush();

        var itemPanel = new Grid();
        itemPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        itemPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = text,
            FontSize = 13,
            Foreground = foregroundBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);
        itemPanel.Children.Add(label);

        var shortcutLabel = new TextBlock
        {
            Text = shortcut,
            FontSize = 12,
            Foreground = shortcutBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24, 0, 0, 0)
        };
        Grid.SetColumn(shortcutLabel, 1);
        itemPanel.Children.Add(shortcutLabel);

        var itemBorder = new Border
        {
            Background = s_ctxMenuTransparentBrush,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 6, 10, 6),
            Child = itemPanel
        };

        if (isEnabled)
        {
            itemBorder.AddHandler(MouseEnterEvent, new RoutedEventHandler((s, e) =>
            {
                itemBorder.Background = hoverBrush;
            }));
            itemBorder.AddHandler(MouseLeaveEvent, new RoutedEventHandler((s, e) =>
            {
                itemBorder.Background = s_ctxMenuTransparentBrush;
            }));
            itemBorder.AddHandler(MouseUpEvent, new RoutedEventHandler((s, e) =>
            {
                if (e is MouseButtonEventArgs { ChangedButton: MouseButton.Left })
                {
                    onClick();
                    e.Handled = true;
                }
            }));
        }

        panel.Children.Add(itemBorder);
    }

    private void AddContextMenuSeparator(StackPanel panel)
    {
        var separator = new Border
        {
            Height = 1,
            Background = ResolveContextMenuSeparatorBrush(),
            Margin = new Thickness(8, 4, 8, 4)
        };
        panel.Children.Add(separator);
    }

    #endregion

    #region Property Changed

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBoxBase textBox)
        {
            textBox.InvalidateVisual();
        }
    }

    #endregion

    #region Helper Types

    /// <summary>
    /// Represents an undo entry.
    /// </summary>
    protected class UndoEntry
    {
        public string Text { get; }
        public int CaretIndex { get; }
        public int SelectionStart { get; }
        public int SelectionLength { get; }

        public UndoEntry(string text, int caretIndex, int selectionStart, int selectionLength)
        {
            Text = text;
            CaretIndex = caretIndex;
            SelectionStart = selectionStart;
            SelectionLength = selectionLength;
        }
    }

    #endregion
}
