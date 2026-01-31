using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Base class for text editing controls.
/// </summary>
public abstract class TextBoxBase : Control
{
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
    /// Whether the user is currently selecting text.
    /// </summary>
    protected bool _isSelecting;

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
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(180, 0, 120, 212)), OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the CaretBrush dependency property.
    /// </summary>
    public static readonly DependencyProperty CaretBrushProperty =
        DependencyProperty.Register(nameof(CaretBrush), typeof(Brush), typeof(TextBoxBase),
            new PropertyMetadata(new SolidColorBrush(Color.White), OnVisualPropertyChanged));

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

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets whether the text box is read-only.
    /// </summary>
    public bool IsReadOnly
    {
        get => (bool)(GetValue(IsReadOnlyProperty) ?? false);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the text box accepts Enter key for new lines.
    /// </summary>
    public bool AcceptsReturn
    {
        get => (bool)(GetValue(AcceptsReturnProperty) ?? false);
        set => SetValue(AcceptsReturnProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the text box accepts Tab key for tab characters.
    /// </summary>
    public bool AcceptsTab
    {
        get => (bool)(GetValue(AcceptsTabProperty) ?? false);
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
        get => (bool)(GetValue(IsUndoEnabledProperty) ?? true);
        set => SetValue(IsUndoEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum number of undo entries.
    /// </summary>
    public int UndoLimit
    {
        get => (int)(GetValue(UndoLimitProperty) ?? 100);
        set => SetValue(UndoLimitProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal scroll bar visibility.
    /// </summary>
    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get => (ScrollBarVisibility)(GetValue(HorizontalScrollBarVisibilityProperty) ?? ScrollBarVisibility.Hidden);
        set => SetValue(HorizontalScrollBarVisibilityProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical scroll bar visibility.
    /// </summary>
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => (ScrollBarVisibility)(GetValue(VerticalScrollBarVisibilityProperty) ?? ScrollBarVisibility.Hidden);
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
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
            var newValue = Math.Max(0, Math.Min(value, text.Length));
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
            var newValue = Math.Max(0, Math.Min(value, text.Length));
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
            var newValue = Math.Max(0, Math.Min(value, maxLength));
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
        _selectionStart = Math.Max(0, Math.Min(start, text.Length));
        _selectionLength = Math.Max(0, Math.Min(length, text.Length - _selectionStart));
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

        // Calculate which phase we're in
        var cycleTime = CaretBlinkInterval + CaretFadeDuration;
        var timeInCycle = elapsed % (cycleTime * 2);

        double targetOpacity;
        double animationProgress;

        if (timeInCycle < CaretBlinkInterval)
        {
            // Fully visible phase
            targetOpacity = 1.0;
            animationProgress = 1.0;
        }
        else if (timeInCycle < CaretBlinkInterval + CaretFadeDuration)
        {
            // Fading out phase
            animationProgress = (timeInCycle - CaretBlinkInterval) / CaretFadeDuration;
            targetOpacity = 1.0 - EaseInOutQuad(animationProgress);
        }
        else if (timeInCycle < CaretBlinkInterval + CaretFadeDuration + CaretBlinkInterval)
        {
            // Fully hidden phase
            targetOpacity = 0.0;
            animationProgress = 1.0;
        }
        else
        {
            // Fading in phase
            animationProgress = (timeInCycle - CaretBlinkInterval * 2 - CaretFadeDuration) / CaretFadeDuration;
            targetOpacity = EaseInOutQuad(animationProgress);
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
    /// Ensures the caret is visible by scrolling if necessary.
    /// </summary>
    protected virtual void EnsureCaretVisible()
    {
        var border = BorderThickness;
        var padding = Padding;
        var contentWidth = RenderSize.Width - border.Left - border.Right - padding.Left - padding.Right;
        var contentHeight = RenderSize.Height - border.Top - border.Bottom - padding.Top - padding.Bottom;

        if (contentWidth <= 0 || contentHeight <= 0)
            return;

        var lineHeight = GetLineHeight();

        var (lineIndex, columnIndex) = GetLineColumnFromCharIndex(_caretIndex);

        // Get the text before the caret to measure its width
        var lineText = GetLineTextInternal(lineIndex);
        var textBeforeCaret = lineText.Substring(0, Math.Min(columnIndex, lineText.Length));
        var caretX = MeasureTextWidth(textBeforeCaret);
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
        if (caretY < _verticalOffset)
        {
            _verticalOffset = caretY;
        }
        else if (caretY + lineHeight > _verticalOffset + contentHeight)
        {
            _verticalOffset = caretY + lineHeight - contentHeight;
        }

        _horizontalOffset = Math.Max(0, _horizontalOffset);
        _verticalOffset = Math.Max(0, _verticalOffset);
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
        var lineHeight = GetLineHeight();

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

        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
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
                    _isSelecting = true;
                    OnSelectionChanged();
                }

                ResetCaretBlink();
            }

            InvalidateVisual();
            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            if (_isSelecting)
            {
                _isSelecting = false;
                ReleaseMouseCapture();
            }

            e.Handled = true;
        }
    }

    private void OnMouseMoveHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled || !_isSelecting) return;

        if (e is MouseEventArgs mouseArgs)
        {
            var position = mouseArgs.GetPosition(this);
            var newCaretIndex = GetCaretIndexFromPosition(position);

            _selectionStart = Math.Min(_selectionAnchor, newCaretIndex);
            _selectionLength = Math.Abs(newCaretIndex - _selectionAnchor);
            _caretIndex = newCaretIndex;

            EnsureCaretVisible();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    private void OnMouseWheelHandler(object sender, RoutedEventArgs e)
    {
        if (e is MouseWheelEventArgs wheelArgs)
        {
            var lineHeight = GetLineHeight();
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
    }

    /// <inheritdoc />
    protected override void OnIsKeyboardFocusedChanged(bool isFocused)
    {
        base.OnIsKeyboardFocusedChanged(isFocused);

        if (isFocused)
        {
            ResetCaretBlink();
        }

        InvalidateVisual();
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
            SetText(text.Substring(0, _caretIndex - deleteCount) + text.Substring(_caretIndex));
            _caretIndex -= deleteCount;
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
