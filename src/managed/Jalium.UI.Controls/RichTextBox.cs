using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// A control that displays and allows editing of rich text content using a FlowDocument.
/// </summary>
public class RichTextBox : Control, IImeSupport
{
    #region Static Brushes

    private static readonly SolidColorBrush s_defaultForegroundBrush = new(Color.White);
    private static readonly SolidColorBrush s_defaultSelectionBrush = new(Color.FromArgb(180, 0, 120, 212));
    private static readonly SolidColorBrush s_defaultCaretBrush = new(Color.White);
    private static readonly SolidColorBrush s_compositionBgBrush = new(Color.FromRgb(60, 60, 80));
    private static readonly SolidColorBrush s_compositionTextBrush = new(Color.FromRgb(255, 255, 200));
    private static readonly SolidColorBrush s_compositionUnderlineBrush = new(Color.FromRgb(200, 200, 100));
    private static readonly Pen s_compositionUnderlinePen = new(s_compositionUnderlineBrush, 1);
    private static readonly Pen s_compositionCursorPen = new(s_defaultCaretBrush, 1);

    #endregion

    #region Fields

    private FlowDocument _document;
    private TextPointer? _caretPosition;
    private TextRange? _selection;

    /// <summary>
    /// Whether the caret is currently visible (for blinking).
    /// </summary>
    private bool _caretVisible = true;

    /// <summary>
    /// The current caret opacity (0.0 to 1.0) for smooth animation.
    /// </summary>
    private double _caretOpacity = 1.0;

    /// <summary>
    /// The last time the caret blinked.
    /// </summary>
    private DateTime _lastCaretBlink;

    /// <summary>
    /// The caret blink interval in milliseconds.
    /// </summary>
    private const int CaretBlinkInterval = 530;

    /// <summary>
    /// The duration of the fade animation in milliseconds.
    /// </summary>
    private const int CaretFadeDuration = 150;

    /// <summary>
    /// Timer for caret animation.
    /// </summary>
    private DispatcherTimer? _caretTimer;

    /// <summary>
    /// Interval for caret animation timer in milliseconds.
    /// </summary>
    private static int CaretTimerInterval => CompositionTarget.FrameIntervalMs;

    /// <summary>
    /// Whether the user is currently selecting text.
    /// </summary>
    private bool _isSelecting;

    /// <summary>
    /// The anchor point for selection extension.
    /// </summary>
    private TextPointer? _selectionAnchor;

    /// <summary>
    /// Whether the current drag gesture should extend selection by whole words.
    /// </summary>
    private bool _isWordSelecting;

    /// <summary>
    /// The starting document offset of the word selected by the double-click anchor.
    /// </summary>
    private int _wordSelectionAnchorStartOffset;

    /// <summary>
    /// The ending document offset of the word selected by the double-click anchor.
    /// </summary>
    private int _wordSelectionAnchorEndOffset;

    /// <summary>
    /// The horizontal scroll offset.
    /// </summary>
    private double _horizontalOffset;

    /// <summary>
    /// The vertical scroll offset.
    /// </summary>
    private double _verticalOffset;

    /// <summary>
    /// The undo stack.
    /// </summary>
    private readonly Stack<DocumentState> _undoStack = new();

    /// <summary>
    /// The redo stack.
    /// </summary>
    private readonly Stack<DocumentState> _redoStack = new();

    /// <summary>
    /// Whether an undo/redo operation is in progress.
    /// </summary>
    private bool _isUndoRedoing;

    // Double/Triple click
    private DateTime _lastClickTime;
    private int _clickCount;
    private Point _lastClickPosition;
    private const int DoubleClickTime = 500;
    private const double DoubleClickDistance = 4;

    // Layout cache
    private FlowDocumentLayoutInfo? _layoutCache;
    private bool _layoutDirty = true;

    // IME composition state
    private bool _isImeComposing;
    private string _imeCompositionString = string.Empty;
    private int _imeCompositionCursor;
    private int _imeCompositionStart;

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the IsReadOnly dependency property.
    /// </summary>
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(RichTextBox),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the AcceptsTab dependency property.
    /// </summary>
    public static readonly DependencyProperty AcceptsTabProperty =
        DependencyProperty.Register(nameof(AcceptsTab), typeof(bool), typeof(RichTextBox),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the SelectionBrush dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectionBrushProperty =
        DependencyProperty.Register(nameof(SelectionBrush), typeof(Brush), typeof(RichTextBox),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the CaretBrush dependency property.
    /// </summary>
    public static readonly DependencyProperty CaretBrushProperty =
        DependencyProperty.Register(nameof(CaretBrush), typeof(Brush), typeof(RichTextBox),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsUndoEnabled dependency property.
    /// </summary>
    public static readonly DependencyProperty IsUndoEnabledProperty =
        DependencyProperty.Register(nameof(IsUndoEnabled), typeof(bool), typeof(RichTextBox),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the UndoLimit dependency property.
    /// </summary>
    public static readonly DependencyProperty UndoLimitProperty =
        DependencyProperty.Register(nameof(UndoLimit), typeof(int), typeof(RichTextBox),
            new PropertyMetadata(100));

    /// <summary>
    /// Identifies the HorizontalScrollBarVisibility dependency property.
    /// </summary>
    public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty =
        DependencyProperty.Register(nameof(HorizontalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(RichTextBox),
            new PropertyMetadata(ScrollBarVisibility.Auto));

    /// <summary>
    /// Identifies the VerticalScrollBarVisibility dependency property.
    /// </summary>
    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty =
        DependencyProperty.Register(nameof(VerticalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(RichTextBox),
            new PropertyMetadata(ScrollBarVisibility.Auto));

    /// <summary>
    /// Identifies the IsSpellCheckEnabled dependency property.
    /// </summary>
    public static readonly DependencyProperty IsSpellCheckEnabledProperty =
        DependencyProperty.Register(nameof(IsSpellCheckEnabled), typeof(bool), typeof(RichTextBox),
            new PropertyMetadata(false));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets whether the control is read-only.
    /// </summary>
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty)!;
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the control accepts Tab key for tab characters.
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
    /// Gets or sets whether spell checking is enabled.
    /// </summary>
    public bool IsSpellCheckEnabled
    {
        get => (bool)GetValue(IsSpellCheckEnabledProperty)!;
        set => SetValue(IsSpellCheckEnabledProperty, value);
    }

    /// <summary>
    /// Gets the FlowDocument that contains the content of this RichTextBox.
    /// </summary>
    public FlowDocument Document
    {
        get => _document;
        set
        {
            if (_document != value)
            {
                _document = value ?? new FlowDocument();
                _caretPosition = _document.ContentStart;
                _selection = new TextRange(_document.ContentStart, _document.ContentStart);
                InvalidateLayout();
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Gets or sets the position of the caret within the content.
    /// </summary>
    public TextPointer? CaretPosition
    {
        get => _caretPosition;
        set
        {
            if (value != null && ReferenceEquals(value.Document, _document))
            {
                _caretPosition = value;
                ResetCaretBlink();
                EnsureCaretVisible();
                UpdateImeWindowIfComposing();
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Gets the current selection as a TextRange.
    /// </summary>
    public TextRange Selection
    {
        get
        {
            _selection ??= new TextRange(_document.ContentStart, _document.ContentStart);
            return _selection;
        }
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
                UpdateImeWindowIfComposing();
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
                UpdateImeWindowIfComposing();
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Gets whether IME composition is currently active.
    /// </summary>
    public bool IsImeComposing => _isImeComposing;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="RichTextBox"/> class.
    /// </summary>
    public RichTextBox()
    {
        _document = new FlowDocument();
        _caretPosition = _document.ContentStart;
        _selection = new TextRange(_document.ContentStart, _document.ContentStart);

        Focusable = true;
        _lastCaretBlink = DateTime.Now;
        _lastClickTime = DateTime.MinValue;

        InputMethod.CompositionStarted += OnImeCompositionStarted;
        InputMethod.CompositionUpdated += OnImeCompositionUpdated;
        InputMethod.CompositionEnded += OnImeCompositionEnded;

        // Register input event handlers
        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new RoutedEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new RoutedEventHandler(OnMouseMoveHandler));
        AddHandler(KeyDownEvent, new RoutedEventHandler(OnKeyDownHandler));
        AddHandler(TextInputEvent, new RoutedEventHandler(OnTextInputHandler));
        AddHandler(MouseWheelEvent, new RoutedEventHandler(OnMouseWheelHandler));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RichTextBox"/> class with a document.
    /// </summary>
    /// <param name="document">The FlowDocument to display.</param>
    public RichTextBox(FlowDocument document) : this()
    {
        Document = document;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Selects all content in the control.
    /// </summary>
    public void SelectAll()
    {
        _selection = new TextRange(_document.ContentStart, _document.ContentEnd);
        _caretPosition = _document.ContentEnd;
        UpdateImeWindowIfComposing();
        InvalidateVisual();
        OnSelectionChanged();
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    public void ClearSelection()
    {
        if (_caretPosition != null)
        {
            _selection = new TextRange(_caretPosition, _caretPosition);
            UpdateImeWindowIfComposing();
            InvalidateVisual();
            OnSelectionChanged();
        }
    }

    /// <summary>
    /// Copies the selected content to the clipboard.
    /// </summary>
    public void Copy()
    {
        if (_selection != null && !_selection.IsEmpty)
        {
            Clipboard.SetText(_selection.Text);
        }
    }

    /// <summary>
    /// Cuts the selected content to the clipboard.
    /// </summary>
    public void Cut()
    {
        if (IsReadOnly || _selection == null || _selection.IsEmpty)
            return;

        Copy();
        DeleteSelection();
    }

    /// <summary>
    /// Pastes content from the clipboard.
    /// </summary>
    public void Paste()
    {
        if (IsReadOnly)
            return;

        var clipboardText = Clipboard.GetText();
        if (!string.IsNullOrEmpty(clipboardText))
        {
            InsertText(clipboardText);
        }
    }

    /// <summary>
    /// Undoes the last edit operation.
    /// </summary>
    public void Undo()
    {
        if (!IsUndoEnabled || _undoStack.Count == 0)
            return;

        _isUndoRedoing = true;
        try
        {
            var currentState = SaveDocumentState();
            _redoStack.Push(currentState);

            var state = _undoStack.Pop();
            RestoreDocumentState(state);
        }
        finally
        {
            _isUndoRedoing = false;
        }

        InvalidateLayout();
        UpdateImeWindowIfComposing();
        InvalidateVisual();
    }

    /// <summary>
    /// Redoes the last undone operation.
    /// </summary>
    public void Redo()
    {
        if (!IsUndoEnabled || _redoStack.Count == 0)
            return;

        _isUndoRedoing = true;
        try
        {
            var currentState = SaveDocumentState();
            _undoStack.Push(currentState);

            var state = _redoStack.Pop();
            RestoreDocumentState(state);
        }
        finally
        {
            _isUndoRedoing = false;
        }

        InvalidateLayout();
        UpdateImeWindowIfComposing();
        InvalidateVisual();
    }

    /// <summary>
    /// Scrolls to make the caret visible.
    /// </summary>
    public void ScrollToCaretPosition()
    {
        EnsureCaretVisible();
    }

    /// <summary>
    /// Gets all text content as plain text.
    /// </summary>
    /// <returns>The plain text content of the document.</returns>
    public string GetText()
    {
        return _document.GetText();
    }

    /// <summary>
    /// Sets the document content from plain text.
    /// </summary>
    /// <param name="text">The plain text to set.</param>
    public void SetText(string text)
    {
        PushUndo();
        _document = FlowDocument.FromText(text);
        _caretPosition = _document.ContentEnd;
        _selection = new TextRange(_document.ContentStart, _document.ContentStart);
        InvalidateLayout();
        UpdateImeWindowIfComposing();
        InvalidateVisual();
    }

    #endregion

    #region Formatting Commands

    /// <summary>
    /// Toggles bold formatting on the current selection.
    /// </summary>
    public void ToggleBold()
    {
        if (IsReadOnly || _selection == null || _selection.IsEmpty)
            return;

        PushUndo();

        var currentWeight = _selection.GetPropertyValue(TextElement.FontWeightProperty);
        var newWeight = currentWeight is FontWeight fw && fw == FontWeights.Bold
            ? FontWeights.Normal
            : FontWeights.Bold;

        _selection.ApplyPropertyValue(TextElement.FontWeightProperty, newWeight);
        InvalidateLayout();
        InvalidateVisual();
    }

    /// <summary>
    /// Toggles italic formatting on the current selection.
    /// </summary>
    public void ToggleItalic()
    {
        if (IsReadOnly || _selection == null || _selection.IsEmpty)
            return;

        PushUndo();

        var currentStyle = _selection.GetPropertyValue(TextElement.FontStyleProperty);
        var newStyle = currentStyle is FontStyle fs && fs == FontStyles.Italic
            ? FontStyles.Normal
            : FontStyles.Italic;

        _selection.ApplyPropertyValue(TextElement.FontStyleProperty, newStyle);
        InvalidateLayout();
        InvalidateVisual();
    }

    /// <summary>
    /// Toggles underline formatting on the current selection.
    /// </summary>
    public void ToggleUnderline()
    {
        if (IsReadOnly || _selection == null || _selection.IsEmpty)
            return;

        PushUndo();

        var currentDecorations = _selection.GetPropertyValue(TextElement.TextDecorationsProperty);
        TextDecorationCollection? newDecorations;

        if (currentDecorations is TextDecorationCollection decorations &&
            decorations.HasDecoration(TextDecorationLocation.Underline))
        {
            // Remove underline
            newDecorations = new TextDecorationCollection(decorations);
            newDecorations.RemoveDecoration(TextDecorationLocation.Underline);
            if (newDecorations.Count == 0)
                newDecorations = null;
        }
        else
        {
            // Add underline
            newDecorations = currentDecorations is TextDecorationCollection existing
                ? new TextDecorationCollection(existing)
                : new TextDecorationCollection();
            newDecorations.Add(new TextDecoration { Location = TextDecorationLocation.Underline });
        }

        _selection.ApplyPropertyValue(TextElement.TextDecorationsProperty, newDecorations);
        InvalidateLayout();
        InvalidateVisual();
    }

    /// <summary>
    /// Sets the font family on the current selection.
    /// </summary>
    /// <param name="fontFamily">The font family name.</param>
    public void SetFontFamily(string fontFamily)
    {
        if (IsReadOnly || _selection == null || _selection.IsEmpty)
            return;

        PushUndo();
        _selection.ApplyPropertyValue(TextElement.FontFamilyProperty, fontFamily);
        InvalidateLayout();
        InvalidateVisual();
    }

    /// <summary>
    /// Sets the font size on the current selection.
    /// </summary>
    /// <param name="fontSize">The font size.</param>
    public void SetFontSize(double fontSize)
    {
        if (IsReadOnly || _selection == null || _selection.IsEmpty)
            return;

        PushUndo();
        _selection.ApplyPropertyValue(TextElement.FontSizeProperty, fontSize);
        InvalidateLayout();
        InvalidateVisual();
    }

    /// <summary>
    /// Sets the foreground color on the current selection.
    /// </summary>
    /// <param name="brush">The foreground brush.</param>
    public void SetForeground(Brush brush)
    {
        if (IsReadOnly || _selection == null || _selection.IsEmpty)
            return;

        PushUndo();
        _selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
        InvalidateVisual();
    }

    #endregion

    #region Protected Methods

    /// <summary>
    /// Inserts text at the current caret position.
    /// </summary>
    protected void InsertText(string textToInsert)
    {
        if (IsReadOnly || string.IsNullOrEmpty(textToInsert))
            return;

        PushUndo();

        // Delete selection if any
        if (_selection != null && !_selection.IsEmpty)
        {
            DeleteSelectionInternal();
        }

        // Insert text at caret position
        if (_caretPosition != null)
        {
            // For now, insert text by modifying the document
            // This is a simplified implementation
            InsertTextAtPosition(_caretPosition, textToInsert);
        }

        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateLayout();
        InvalidateVisual();
    }

    private void InsertTextAtPosition(TextPointer position, string text)
    {
        if (position.Parent is Run run)
        {
            var offset = position.Offset;
            run.Text = run.Text.Insert(offset, text);

            // Update caret position
            _caretPosition = _document.GetPositionAtOffset(
                position.DocumentOffset + text.Length, LogicalDirection.Forward);
        }
        else if (position.Parent is Paragraph paragraph)
        {
            // Insert a new Run at the position
            var newRun = new Run(text);

            if (position.Offset == 0 && paragraph.Inlines.Count > 0)
            {
                paragraph.Inlines.Insert(0, newRun);
            }
            else
            {
                paragraph.Inlines.Add(newRun);
            }

            // Update caret position
            _caretPosition = _document.GetPositionAtOffset(
                position.DocumentOffset + text.Length, LogicalDirection.Forward);
        }
        else if (_document.Blocks.Count == 0)
        {
            // Document is empty, create a new paragraph
            var newParagraph = new Paragraph(new Run(text));
            _document.Blocks.Add(newParagraph);
            _caretPosition = _document.ContentEnd;
        }

        // Update selection to be empty at new caret position
        if (_caretPosition != null)
        {
            _selection = new TextRange(_caretPosition, _caretPosition);
        }
    }

    /// <summary>
    /// Deletes the current selection.
    /// </summary>
    protected void DeleteSelection()
    {
        if (_selection == null || _selection.IsEmpty)
            return;

        PushUndo();
        DeleteSelectionInternal();
        InvalidateLayout();
        InvalidateVisual();
    }

    private void DeleteSelectionInternal()
    {
        if (_selection == null || _selection.IsEmpty)
            return;

        // Delete the selected text
        _selection.Text = string.Empty;

        // Update caret to selection start
        _caretPosition = _selection.Start;
        _selection = new TextRange(_caretPosition, _caretPosition);

        UpdateImeWindowIfComposing();
        OnSelectionChanged();
    }

    /// <summary>
    /// Pushes the current state to the undo stack.
    /// </summary>
    protected void PushUndo()
    {
        if (!IsUndoEnabled || _isUndoRedoing)
            return;

        var state = SaveDocumentState();
        _undoStack.Push(state);
        _redoStack.Clear();

        // Limit stack size
        while (_undoStack.Count > UndoLimit)
        {
            var temp = new Stack<DocumentState>();
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
    }

    private DocumentState SaveDocumentState()
    {
        return new DocumentState(
            _document.GetText(),
            _caretPosition?.DocumentOffset ?? 0,
            _selection?.Start.DocumentOffset ?? 0,
            _selection?.End.DocumentOffset ?? 0);
    }

    private void RestoreDocumentState(DocumentState state)
    {
        _document = FlowDocument.FromText(state.Text);
        _caretPosition = _document.GetPositionAtOffset(state.CaretOffset, LogicalDirection.Forward);
        var start = _document.GetPositionAtOffset(state.SelectionStart, LogicalDirection.Forward);
        var end = _document.GetPositionAtOffset(state.SelectionEnd, LogicalDirection.Forward);
        if (start != null && end != null)
        {
            _selection = new TextRange(start, end);
        }
    }

    /// <summary>
    /// Resets the caret blink state.
    /// </summary>
    protected void ResetCaretBlink()
    {
        _caretVisible = true;
        _caretOpacity = 1.0;
        _lastCaretBlink = DateTime.Now;
    }

    /// <summary>
    /// Ensures the caret is visible by scrolling if necessary.
    /// </summary>
    protected void EnsureCaretVisible()
    {
        UpdateImeWindowIfComposing();
    }

    /// <summary>
    /// Called when the selection changes.
    /// </summary>
    protected void OnSelectionChanged()
    {
        UpdateImeWindowIfComposing();
        // Raise selection changed event if needed
    }

    /// <summary>
    /// Invalidates the layout cache.
    /// </summary>
    protected void InvalidateLayout()
    {
        _layoutDirty = true;
        _layoutCache = null;
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        base.OnRender(drawingContext);

        if (drawingContext is not DrawingContext dc)
            return;

        var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);

        // Draw background
        if (Background != null)
        {
            dc.DrawRectangle(Background, null, bounds);
        }

        // Draw border
        if (BorderBrush != null && BorderThickness.Left > 0)
        {
            dc.DrawRectangle(null, new Pen(BorderBrush, BorderThickness.Left), bounds);
        }

        // Calculate content area
        var contentBounds = new Rect(
            BorderThickness.Left + Padding.Left,
            BorderThickness.Top + Padding.Top,
            Math.Max(0, RenderSize.Width - BorderThickness.Left - BorderThickness.Right - Padding.Left - Padding.Right),
            Math.Max(0, RenderSize.Height - BorderThickness.Top - BorderThickness.Bottom - Padding.Top - Padding.Bottom));

        if (contentBounds.Width <= 0 || contentBounds.Height <= 0)
            return;

        // Apply clipping
        dc.PushClip(new RectangleGeometry(contentBounds));

        try
        {
            // Render document content
            RenderDocument(dc, contentBounds);

            if (_isImeComposing && !string.IsNullOrEmpty(_imeCompositionString))
            {
                RenderImeComposition(dc, contentBounds);
            }

            // Render selection
            if (_selection != null && !_selection.IsEmpty)
            {
                RenderSelection(dc, contentBounds);
            }

            // Render caret
            if (IsKeyboardFocused && !IsReadOnly && _caretPosition != null)
            {
                RenderCaret(dc, contentBounds);
            }
        }
        finally
        {
            dc.Pop();
        }
    }

    private void RenderDocument(DrawingContext dc, Rect contentBounds)
    {
        var layout = EnsureLayout(contentBounds.Width);
        if (layout == null)
            return;

        var y = contentBounds.Top - _verticalOffset;

        foreach (var blockLayout in layout.Blocks)
        {
            RenderBlockLayout(dc, blockLayout, contentBounds.Left - _horizontalOffset, ref y, contentBounds);
        }
    }

    private void RenderBlockLayout(DrawingContext dc, BlockLayoutInfo blockLayout, double x, ref double y, Rect contentBounds)
    {
        if (blockLayout.Block is Paragraph)
        {
            foreach (var lineLayout in blockLayout.Lines)
            {
                if (y + lineLayout.Height > contentBounds.Top && y < contentBounds.Bottom)
                {
                    RenderLine(dc, lineLayout, x + blockLayout.Margin.Left, y);
                }
                y += lineLayout.Height;
            }

            // Add paragraph spacing
            y += blockLayout.Margin.Bottom;
        }
        else if (blockLayout.Block is Section)
        {
            foreach (var childLayout in blockLayout.ChildBlocks)
            {
                RenderBlockLayout(dc, childLayout, x, ref y, contentBounds);
            }
        }
        else if (blockLayout.Block is List)
        {
            foreach (var childLayout in blockLayout.ChildBlocks)
            {
                RenderBlockLayout(dc, childLayout, x + 20, ref y, contentBounds); // Indent list items
            }
        }
    }

    private void RenderLine(DrawingContext dc, LineLayoutInfo lineLayout, double x, double y)
    {
        foreach (var runLayout in lineLayout.Runs)
        {
            if (runLayout.Run != null)
            {
                var text = runLayout.Run.Text;
                var foreground = runLayout.Run.Foreground
                    ?? _document.Foreground
                    ?? ResolveDocumentForegroundBrush();
                var fontFamily = runLayout.Run.FontFamily
                    ?? _document.FontFamily
                    ?? "Segoe UI";
                var fontSize = runLayout.Run.FontSize;
                if (fontSize <= 0)
                    fontSize = _document.FontSize;
                var fontWeight = runLayout.Run.FontWeight;
                var fontStyle = runLayout.Run.FontStyle;

                var formattedText = new FormattedText(text, fontFamily, fontSize)
                {
                    Foreground = foreground,
                    FontWeight = fontWeight.ToOpenTypeWeight(),
                    FontStyle = fontStyle.ToOpenTypeStyle()
                };

                dc.DrawText(formattedText, new Point(x + runLayout.X, y));
            }
        }
    }

    private void RenderSelection(DrawingContext dc, Rect contentBounds)
    {
        if (ResolveSelectionBrush() == null)
            return;

        // Simplified selection rendering - just highlight the text area
        // Full implementation would calculate exact selection rectangles
    }

    private void RenderCaret(DrawingContext dc, Rect contentBounds)
    {
        if (_isImeComposing)
            return;

        UpdateCaretAnimation();

        if (_caretOpacity < 0.01)
            return;

        var caretPos = GetCaretScreenPosition(contentBounds);
        if (caretPos == null)
            return;

        var lineHeight = GetDefaultLineHeight();
        var caretBrush = ResolveCaretBrush();
        if (caretBrush == null)
            return;

        // Apply opacity for animation
        if (_caretOpacity < 1.0 && caretBrush is SolidColorBrush solidBrush)
        {
            var color = solidBrush.Color;
            caretBrush = new SolidColorBrush(Color.FromArgb(
                (byte)(color.A * _caretOpacity), color.R, color.G, color.B));
        }

        dc.DrawRectangle(caretBrush, null,
            new Rect(caretPos.Value.X, caretPos.Value.Y, 2, lineHeight));
    }

    private void RenderImeComposition(DrawingContext dc, Rect contentBounds)
    {
        if (!_isImeComposing || string.IsNullOrEmpty(_imeCompositionString))
            return;

        int startOffset = GetImeAnchorOffset();
        var anchorPosition = _document.GetPositionAtOffset(startOffset, LogicalDirection.Forward) ?? _document.ContentStart;
        var anchorPoint = GetCaretScreenPosition(contentBounds, anchorPosition) ?? new Point(contentBounds.Left, contentBounds.Top);
        var formatting = GetImeFormatting(anchorPosition);
        var text = new FormattedText(_imeCompositionString, formatting.FontFamily, formatting.FontSize)
        {
            Foreground = s_compositionTextBrush,
            FontWeight = formatting.FontWeight.ToOpenTypeWeight(),
            FontStyle = formatting.FontStyle.ToOpenTypeStyle()
        };
        TextMeasurement.MeasureText(text);

        double lineHeight = GetLineHeightForFormatting(formatting.FontSize);
        double width = Math.Max(1, text.Width);

        dc.DrawRectangle(s_compositionBgBrush, null, new Rect(anchorPoint.X, anchorPoint.Y, width, lineHeight));
        dc.DrawText(text, anchorPoint);
        dc.DrawLine(
            s_compositionUnderlinePen,
            new Point(anchorPoint.X, anchorPoint.Y + lineHeight - 1),
            new Point(anchorPoint.X + width, anchorPoint.Y + lineHeight - 1));

        if (_imeCompositionCursor >= 0 && _imeCompositionCursor <= _imeCompositionString.Length)
        {
            string beforeCursor = _imeCompositionString.Substring(0, _imeCompositionCursor);
            var cursorText = new FormattedText(beforeCursor, formatting.FontFamily, formatting.FontSize)
            {
                FontWeight = formatting.FontWeight.ToOpenTypeWeight(),
                FontStyle = formatting.FontStyle.ToOpenTypeStyle()
            };
            TextMeasurement.MeasureText(cursorText);
            double cursorX = anchorPoint.X + cursorText.Width;

            dc.DrawLine(
                s_compositionCursorPen,
                new Point(cursorX, anchorPoint.Y + 2),
                new Point(cursorX, anchorPoint.Y + lineHeight - 2));
        }
    }

    private Point? GetCaretScreenPosition(Rect contentBounds)
    {
        return GetCaretScreenPosition(contentBounds, _caretPosition);
    }

    private Point? GetCaretScreenPosition(Rect contentBounds, TextPointer? position)
    {
        if (position == null)
            return null;

        var layout = EnsureLayout(contentBounds.Width);
        if (layout == null)
            return null;

        // Find the caret position in the layout
        var offset = position.DocumentOffset;
        var y = contentBounds.Top - _verticalOffset;
        var x = contentBounds.Left - _horizontalOffset;

        foreach (var blockLayout in layout.Blocks)
        {
            var result = FindPositionInBlock(blockLayout, offset, x, ref y);
            if (result != null)
                return result;
        }

        return new Point(contentBounds.Left, contentBounds.Top);
    }

    private Point? FindPositionInBlock(BlockLayoutInfo blockLayout, int targetOffset, double x, ref double y)
    {
        x += blockLayout.Margin.Left;

        foreach (var lineLayout in blockLayout.Lines)
        {
            if (targetOffset >= lineLayout.StartOffset && targetOffset <= lineLayout.EndOffset)
            {
                // Found the line containing the offset
                var lineX = x;
                foreach (var runLayout in lineLayout.Runs)
                {
                    if (targetOffset >= runLayout.StartOffset && targetOffset <= runLayout.EndOffset)
                    {
                        // Found the run containing the offset
                        var offsetInRun = targetOffset - runLayout.StartOffset;
                        if (runLayout.Run != null)
                        {
                            var textBeforeCaret = runLayout.Run.Text.Substring(0, Math.Min(offsetInRun, runLayout.Run.Text.Length));
                            var textWidth = MeasureText(textBeforeCaret, runLayout.Run);
                            return new Point(lineX + runLayout.X + textWidth, y);
                        }
                        return new Point(lineX + runLayout.X, y);
                    }
                }
                return new Point(lineX + lineLayout.Width, y);
            }
            y += lineLayout.Height;
        }

        foreach (var childLayout in blockLayout.ChildBlocks)
        {
            var result = FindPositionInBlock(childLayout, targetOffset, x, ref y);
            if (result != null)
                return result;
        }

        y += blockLayout.Margin.Bottom;
        return null;
    }

    private double MeasureText(string text, Run run)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var fontFamily = run.FontFamily
            ?? _document.FontFamily
            ?? "Segoe UI";
        var fontSize = run.FontSize;
        if (fontSize <= 0)
            fontSize = _document.FontSize;

        var formattedText = new FormattedText(text, fontFamily, fontSize)
        {
            FontWeight = run.FontWeight.ToOpenTypeWeight(),
            FontStyle = run.FontStyle.ToOpenTypeStyle()
        };
        TextMeasurement.MeasureText(formattedText);
        return formattedText.Width;
    }

    private double GetDefaultLineHeight()
    {
        var fontSize = _document.FontSize;
        return fontSize * 1.5;
    }

    private double UpdateCaretAnimation()
    {
        var now = DateTime.Now;
        var elapsed = (now - _lastCaretBlink).TotalMilliseconds;

        var fullCycleTime = (CaretBlinkInterval + CaretFadeDuration) * 2.0;
        var timeInCycle = elapsed % fullCycleTime;

        double targetOpacity;

        double visibleEnd = CaretBlinkInterval;
        double fadeOutEnd = CaretBlinkInterval + CaretFadeDuration;
        double hiddenEnd = CaretBlinkInterval * 2 + CaretFadeDuration;

        if (timeInCycle < visibleEnd)
        {
            targetOpacity = 1.0;
        }
        else if (timeInCycle < fadeOutEnd)
        {
            double progress = (timeInCycle - visibleEnd) / CaretFadeDuration;
            targetOpacity = 1.0 - EaseInOutQuad(progress);
        }
        else if (timeInCycle < hiddenEnd)
        {
            targetOpacity = 0.0;
        }
        else
        {
            double progress = (timeInCycle - hiddenEnd) / CaretFadeDuration;
            targetOpacity = EaseInOutQuad(progress);
        }

        _caretOpacity = targetOpacity;
        _caretVisible = _caretOpacity > 0.01;

        return _caretOpacity;
    }

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

    private Brush ResolveDocumentForegroundBrush()
    {
        if (HasLocalValue(Control.ForegroundProperty) && Foreground != null)
            return Foreground;

        return ResolveThemeBrush("TextPrimary", s_defaultForegroundBrush, "TextFillColorPrimaryBrush");
    }

    private Brush? ResolveSelectionBrush()
    {
        if (HasLocalValue(SelectionBrushProperty))
            return SelectionBrush;

        return SelectionBrush
            ?? ResolveThemeBrush("SelectionBackground", s_defaultSelectionBrush, "AccentFillColorSelectedTextBackgroundBrush");
    }

    private Brush? ResolveCaretBrush()
    {
        if (HasLocalValue(CaretBrushProperty))
            return CaretBrush;

        return CaretBrush
            ?? ((HasLocalValue(Control.ForegroundProperty) && Foreground != null) ? Foreground : null)
            ?? ResolveThemeBrush("TextPrimary", s_defaultCaretBrush, "TextFillColorPrimaryBrush");
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

    #region Layout

    private FlowDocumentLayoutInfo? EnsureLayout(double maxWidth)
    {
        if (!_layoutDirty && _layoutCache != null)
            return _layoutCache;

        _layoutCache = LayoutDocument(maxWidth);
        _layoutDirty = false;
        return _layoutCache;
    }

    private FlowDocumentLayoutInfo LayoutDocument(double maxWidth)
    {
        var layout = new FlowDocumentLayoutInfo();
        int currentOffset = 0;

        foreach (var block in _document.Blocks)
        {
            var blockLayout = LayoutBlock(block, maxWidth, ref currentOffset);
            layout.Blocks.Add(blockLayout);
        }

        return layout;
    }

    private BlockLayoutInfo LayoutBlock(Block block, double maxWidth, ref int currentOffset)
    {
        var blockLayout = new BlockLayoutInfo
        {
            Block = block,
            Margin = block.Margin
        };

        if (block is Paragraph paragraph)
        {
            LayoutParagraph(paragraph, blockLayout, maxWidth - block.Margin.Left - block.Margin.Right, ref currentOffset);
            currentOffset++; // Paragraph break
        }
        else if (block is Section section)
        {
            foreach (var childBlock in section.Blocks)
            {
                var childLayout = LayoutBlock(childBlock, maxWidth - block.Margin.Left - block.Margin.Right, ref currentOffset);
                blockLayout.ChildBlocks.Add(childLayout);
            }
        }
        else if (block is List list)
        {
            foreach (var item in list.ListItems)
            {
                foreach (var itemBlock in item.Blocks)
                {
                    var childLayout = LayoutBlock(itemBlock, maxWidth - block.Margin.Left - block.Margin.Right - 20, ref currentOffset);
                    blockLayout.ChildBlocks.Add(childLayout);
                }
            }
        }

        return blockLayout;
    }

    private void LayoutParagraph(Paragraph paragraph, BlockLayoutInfo blockLayout, double maxWidth, ref int currentOffset)
    {
        var lineLayout = new LineLayoutInfo
        {
            StartOffset = currentOffset,
            Height = GetDefaultLineHeight(),
            Baseline = GetDefaultLineHeight() * 0.8
        };

        double x = 0;

        foreach (var inline in paragraph.Inlines)
        {
            LayoutInline(inline, blockLayout, lineLayout, maxWidth, ref x, ref currentOffset);
        }

        lineLayout.EndOffset = currentOffset;
        lineLayout.Width = x;

        if (lineLayout.Runs.Count > 0 || blockLayout.Lines.Count == 0)
        {
            blockLayout.Lines.Add(lineLayout);
        }
    }

    private void LayoutInline(Inline inline, BlockLayoutInfo blockLayout, LineLayoutInfo lineLayout,
        double maxWidth, ref double x, ref int currentOffset)
    {
        if (inline is Run run)
        {
            var text = run.Text;
            var fontFamily = run.FontFamily
                ?? _document.FontFamily
                ?? "Segoe UI";
            var fontSize = run.FontSize;
            if (fontSize <= 0)
                fontSize = _document.FontSize;

            var formattedText = new FormattedText(text, fontFamily, fontSize)
            {
                FontWeight = run.FontWeight.ToOpenTypeWeight(),
                FontStyle = run.FontStyle.ToOpenTypeStyle()
            };
            TextMeasurement.MeasureText(formattedText);
            var textWidth = formattedText.Width;

            // Check if we need to wrap
            if (x + textWidth > maxWidth && x > 0)
            {
                // Start a new line
                lineLayout.EndOffset = currentOffset;
                lineLayout.Width = x;
                blockLayout.Lines.Add(lineLayout);

                lineLayout = new LineLayoutInfo
                {
                    StartOffset = currentOffset,
                    Height = GetDefaultLineHeight(),
                    Baseline = GetDefaultLineHeight() * 0.8
                };
                x = 0;
            }

            var runLayout = new RunLayoutInfo
            {
                Run = run,
                X = x,
                Width = textWidth,
                StartOffset = currentOffset,
                EndOffset = currentOffset + text.Length
            };

            lineLayout.Runs.Add(runLayout);
            x += textWidth;
            currentOffset += text.Length;
        }
        else if (inline is Span span)
        {
            foreach (var child in span.Inlines)
            {
                LayoutInline(child, blockLayout, lineLayout, maxWidth, ref x, ref currentOffset);
            }
        }
        else if (inline is LineBreak)
        {
            lineLayout.EndOffset = currentOffset;
            lineLayout.Width = x;
            blockLayout.Lines.Add(lineLayout);

            lineLayout = new LineLayoutInfo
            {
                StartOffset = currentOffset + 1,
                Height = GetDefaultLineHeight(),
                Baseline = GetDefaultLineHeight() * 0.8
            };
            x = 0;
            currentOffset++;
        }
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
    protected void OnKeyDown(KeyEventArgs e)
    {
        var shift = e.IsShiftDown;
        var ctrl = e.IsControlDown;

        if (_isImeComposing && ShouldDeferKeyToIme(e.Key, ctrl))
        {
            return;
        }

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
                InsertText("\n");
                e.Handled = true;
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

            case Key.B:
                if (ctrl)
                {
                    ToggleBold();
                    e.Handled = true;
                }
                break;

            case Key.I:
                if (ctrl)
                {
                    ToggleItalic();
                    e.Handled = true;
                }
                break;

            case Key.U:
                if (ctrl)
                {
                    ToggleUnderline();
                    e.Handled = true;
                }
                break;
        }
    }

    private static bool ShouldDeferKeyToIme(Key key, bool ctrl)
    {
        return key switch
        {
            Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.Back or Key.Delete or Key.Enter or Key.Tab => true,
            Key.A or Key.B or Key.C or Key.I or Key.U or Key.V or Key.X or Key.Y or Key.Z when ctrl => true,
            _ => false
        };
    }

    private void OnTextInputHandler(object sender, RoutedEventArgs e)
    {
        if (e is not TextCompositionEventArgs textArgs || textArgs.Handled || IsReadOnly)
            return;

        if (!string.IsNullOrEmpty(textArgs.Text))
        {
            var text = textArgs.Text;
            if (text.Length == 1 && char.IsControl(text[0]) && text[0] != '\t')
                return;

            InsertText(text);
            textArgs.Handled = true;
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

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        if (!IsEnabled) return;

        if (e is MouseButtonEventArgs mouseArgs && mouseArgs.ChangedButton == MouseButton.Left)
        {
            Focus();

            var position = mouseArgs.GetPosition(this);
            var now = DateTime.Now;

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
            var newCaretPosition = GetTextPositionFromPoint(position);

            if (_clickCount == 3)
            {
                SelectAll();
                _isWordSelecting = false;
                _clickCount = 0;
            }
            else if (_clickCount == 2)
            {
                if (newCaretPosition != null)
                {
                    SelectWordAt(newCaretPosition);
                    _wordSelectionAnchorStartOffset = _selection?.Start.DocumentOffset ?? 0;
                    _wordSelectionAnchorEndOffset = _selection?.End.DocumentOffset ?? _wordSelectionAnchorStartOffset;
                    _isWordSelecting = _selection is { IsEmpty: false };
                    _isSelecting = true;
                    CaptureMouse();
                }
            }
            else
            {
                CaptureMouse();

                if ((mouseArgs.KeyboardModifiers & ModifierKeys.Shift) != 0 && _caretPosition != null && newCaretPosition != null)
                {
                    _selection = new TextRange(_caretPosition, newCaretPosition);
                }
                else
                {
                    _caretPosition = newCaretPosition;
                    _selectionAnchor = newCaretPosition;
                    if (newCaretPosition != null)
                    {
                        _selection = new TextRange(newCaretPosition, newCaretPosition);
                    }
                    _isWordSelecting = false;
                    _isSelecting = true;
                    OnSelectionChanged();
                }

                ResetCaretBlink();
                UpdateImeWindowIfComposing();
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
                _isWordSelecting = false;
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
            var newCaretPosition = GetTextPositionFromPoint(position);

            if (newCaretPosition != null)
            {
                if (_isWordSelecting)
                {
                    ExtendWordSelection(newCaretPosition);
                }
                else if (_selectionAnchor != null)
                {
                    _selection = new TextRange(_selectionAnchor, newCaretPosition);
                    _caretPosition = newCaretPosition;
                }
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
            var lineHeight = GetDefaultLineHeight();
            var delta = wheelArgs.Delta > 0 ? -3 : 3;
            VerticalOffset += delta * lineHeight;
            e.Handled = true;
        }
    }

    private TextPointer? GetTextPositionFromPoint(Point point)
    {
        var contentBounds = new Rect(
            BorderThickness.Left + Padding.Left,
            BorderThickness.Top + Padding.Top,
            Math.Max(0, RenderSize.Width - BorderThickness.Left - BorderThickness.Right - Padding.Left - Padding.Right),
            Math.Max(0, RenderSize.Height - BorderThickness.Top - BorderThickness.Bottom - Padding.Top - Padding.Bottom));

        var layout = EnsureLayout(contentBounds.Width);
        if (layout == null)
            return _document.ContentStart;

        var y = contentBounds.Top - _verticalOffset;
        var targetY = point.Y;
        var targetX = point.X - contentBounds.Left + _horizontalOffset;

        foreach (var blockLayout in layout.Blocks)
        {
            var result = FindPositionFromPoint(blockLayout, targetX, targetY, ref y);
            if (result != null)
                return result;
        }

        return _document.ContentEnd;
    }

    private TextPointer? FindPositionFromPoint(BlockLayoutInfo blockLayout, double targetX, double targetY, ref double y)
    {
        foreach (var lineLayout in blockLayout.Lines)
        {
            if (targetY >= y && targetY < y + lineLayout.Height)
            {
                // Found the line
                double x = blockLayout.Margin.Left;
                foreach (var runLayout in lineLayout.Runs)
                {
                    if (targetX >= x + runLayout.X && targetX < x + runLayout.X + runLayout.Width)
                    {
                        // Found the run, find exact offset
                        if (runLayout.Run != null)
                        {
                            var localX = targetX - x - runLayout.X;
                            var charIndex = FindCharIndexFromX(runLayout.Run, localX);
                            var offset = runLayout.StartOffset + charIndex;
                            return _document.GetPositionAtOffset(offset, LogicalDirection.Forward);
                        }
                        return _document.GetPositionAtOffset(runLayout.StartOffset, LogicalDirection.Forward);
                    }
                }
                // Clicked past the end of the line
                return _document.GetPositionAtOffset(lineLayout.EndOffset, LogicalDirection.Forward);
            }
            y += lineLayout.Height;
        }

        foreach (var childLayout in blockLayout.ChildBlocks)
        {
            var result = FindPositionFromPoint(childLayout, targetX, targetY, ref y);
            if (result != null)
                return result;
        }

        y += blockLayout.Margin.Bottom;
        return null;
    }

    private int FindCharIndexFromX(Run run, double x)
    {
        var text = run.Text;
        var fontFamily = run.FontFamily
            ?? _document.FontFamily
            ?? "Segoe UI";
        var fontSize = run.FontSize;
        if (fontSize <= 0)
            fontSize = _document.FontSize;

        double prevWidth = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            var formattedText = new FormattedText(text.Substring(0, i), fontFamily, fontSize)
            {
                FontWeight = run.FontWeight.ToOpenTypeWeight(),
                FontStyle = run.FontStyle.ToOpenTypeStyle()
            };
            TextMeasurement.MeasureText(formattedText);
            var width = formattedText.Width;
            if (width >= x)
            {
                if (i > 0 && (x - prevWidth) < (width - x))
                    return i - 1;
                return i;
            }
            prevWidth = width;
        }
        return text.Length;
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
            InputMethod.SetTarget(this);
            ResetCaretBlink();
            StartCaretTimer();
        }
        else
        {
            StopCaretTimer();
            if (InputMethod.Current == this)
            {
                InputMethod.SetTarget(null);
            }
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
            _caretTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, CaretTimerInterval));
            _caretTimer.Tick += OnCaretTimerTick;
        }

        _caretTimer.Start();
    }

    private void StopCaretTimer()
    {
        _caretTimer?.Stop();
    }

    private void OnCaretTimerTick(object? sender, EventArgs e)
    {
        if (IsKeyboardFocused && !IsReadOnly)
        {
            InvalidateVisual();
        }
    }

    #endregion

    #region Key Handlers

    private void HandleLeftKey(bool shift, bool ctrl)
    {
        if (_caretPosition == null)
            return;

        var newPosition = ctrl
            ? FindPreviousWordBoundary(_caretPosition)
            : _caretPosition.GetNextInsertionPosition(LogicalDirection.Backward);

                    newPosition ??= _document.ContentStart;

        if (shift)
        {
            ExtendSelection(newPosition);
        }
        else
        {
            _caretPosition = newPosition;
            ClearSelection();
        }

        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateVisual();
    }

    private void HandleRightKey(bool shift, bool ctrl)
    {
        if (_caretPosition == null)
            return;

        var newPosition = ctrl
            ? FindNextWordBoundary(_caretPosition)
            : _caretPosition.GetNextInsertionPosition(LogicalDirection.Forward);

                    newPosition ??= _document.ContentEnd;

        if (shift)
        {
            ExtendSelection(newPosition);
        }
        else
        {
            _caretPosition = newPosition;
            ClearSelection();
        }

        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateVisual();
    }

    private void HandleUpKey(bool shift)
    {
        // Simplified: move to start
        var newPosition = _document.ContentStart;

        if (shift)
        {
            ExtendSelection(newPosition);
        }
        else
        {
            _caretPosition = newPosition;
            ClearSelection();
        }

        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateVisual();
    }

    private void HandleDownKey(bool shift)
    {
        // Simplified: move to end
        var newPosition = _document.ContentEnd;

        if (shift)
        {
            ExtendSelection(newPosition);
        }
        else
        {
            _caretPosition = newPosition;
            ClearSelection();
        }

        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateVisual();
    }

    private void HandleHomeKey(bool shift, bool ctrl)
    {
        TextPointer? newPosition;

        if (ctrl)
        {
            // Ctrl+Home: go to document start
            newPosition = _document.ContentStart;
        }
        else
        {
            // Home: go to current line start
            newPosition = FindLineStart(_caretPosition);
        }

        if (newPosition == null)
            return;

        if (shift)
        {
            ExtendSelection(newPosition);
        }
        else
        {
            _caretPosition = newPosition;
            ClearSelection();
        }

        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateVisual();
    }

    private void HandleEndKey(bool shift, bool ctrl)
    {
        TextPointer? newPosition;

        if (ctrl)
        {
            // Ctrl+End: go to document end
            newPosition = _document.ContentEnd;
        }
        else
        {
            // End: go to current line end
            newPosition = FindLineEnd(_caretPosition);
        }

        if (newPosition == null)
            return;

        if (shift)
        {
            ExtendSelection(newPosition);
        }
        else
        {
            _caretPosition = newPosition;
            ClearSelection();
        }

        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateVisual();
    }

    /// <summary>
    /// Finds the start of the current line by searching backward from the given position
    /// for a newline character or paragraph boundary.
    /// </summary>
    private TextPointer? FindLineStart(TextPointer? position)
    {
        if (position == null)
            return _document.ContentStart;

        var offset = position.DocumentOffset;
        var text = _document.GetText();

        if (offset <= 0)
            return _document.ContentStart;

        // Search backward from current position for a newline
        int searchPos = offset - 1;
        while (searchPos >= 0)
        {
            char c = text[searchPos];
            if (c == '\n' || c == '\r')
            {
                // Found a newline; line starts at the character after it
                return _document.GetPositionAtOffset(searchPos + 1, LogicalDirection.Forward)
                    ?? _document.ContentStart;
            }
            searchPos--;
        }

        // No newline found; we're on the first line
        return _document.ContentStart;
    }

    /// <summary>
    /// Finds the end of the current line by searching forward from the given position
    /// for a newline character or paragraph boundary.
    /// </summary>
    private TextPointer? FindLineEnd(TextPointer? position)
    {
        if (position == null)
            return _document.ContentEnd;

        var offset = position.DocumentOffset;
        var text = _document.GetText();

        if (offset >= text.Length)
            return _document.ContentEnd;

        // Search forward from current position for a newline
        int searchPos = offset;
        while (searchPos < text.Length)
        {
            char c = text[searchPos];
            if (c == '\n' || c == '\r')
            {
                // Found a newline; line ends just before it
                return _document.GetPositionAtOffset(searchPos, LogicalDirection.Backward)
                    ?? _document.ContentEnd;
            }
            searchPos++;
        }

        // No newline found; we're on the last line
        return _document.ContentEnd;
    }

    private void HandleBackspace(bool ctrl)
    {
        if (IsReadOnly)
            return;

        if (_selection != null && !_selection.IsEmpty)
        {
            DeleteSelection();
        }
        else if (_caretPosition != null)
        {
            var prevPosition = ctrl
                ? FindPreviousWordBoundary(_caretPosition)
                : _caretPosition.GetNextInsertionPosition(LogicalDirection.Backward);

            if (prevPosition != null)
            {
                PushUndo();
                var range = new TextRange(prevPosition, _caretPosition);
                range.Text = string.Empty;
                _caretPosition = prevPosition;
                _selection = new TextRange(prevPosition, prevPosition);
                InvalidateLayout();
            }
        }

        EnsureCaretVisible();
        InvalidateVisual();
    }

    private void HandleDelete(bool ctrl)
    {
        if (IsReadOnly)
            return;

        if (_selection != null && !_selection.IsEmpty)
        {
            DeleteSelection();
        }
        else if (_caretPosition != null)
        {
            var nextPosition = ctrl
                ? FindNextWordBoundary(_caretPosition)
                : _caretPosition.GetNextInsertionPosition(LogicalDirection.Forward);

            if (nextPosition != null)
            {
                PushUndo();
                var range = new TextRange(_caretPosition, nextPosition);
                range.Text = string.Empty;
                _selection = new TextRange(_caretPosition, _caretPosition);
                InvalidateLayout();
            }
        }

        EnsureCaretVisible();
        InvalidateVisual();
    }

    private void ExtendSelection(TextPointer newPosition)
    {
        _selectionAnchor ??= _caretPosition;

        if (_selectionAnchor != null)
        {
            _selection = new TextRange(_selectionAnchor, newPosition);
        }
        _caretPosition = newPosition;

        OnSelectionChanged();
    }

    private void SelectWordAt(TextPointer position)
    {
        var (start, end) = GetWordRangeAtOffset(position.DocumentOffset);
        var startPosition = _document.GetPositionAtOffset(start, LogicalDirection.Forward) ?? _document.ContentStart;
        var endPosition = _document.GetPositionAtOffset(end, LogicalDirection.Forward) ?? _document.ContentEnd;
        _selection = new TextRange(startPosition, endPosition);
        _caretPosition = endPosition;
        _selectionAnchor = startPosition;
        UpdateImeWindowIfComposing();
        OnSelectionChanged();
    }

    private void ExtendWordSelection(TextPointer position)
    {
        var (currentStart, currentEnd) = GetWordRangeAtOffset(position.DocumentOffset);
        int selectionStart;
        int selectionEnd;
        int caretOffset;

        if (currentEnd <= _wordSelectionAnchorStartOffset)
        {
            selectionStart = currentStart;
            selectionEnd = _wordSelectionAnchorEndOffset;
            caretOffset = selectionStart;
        }
        else if (currentStart >= _wordSelectionAnchorEndOffset)
        {
            selectionStart = _wordSelectionAnchorStartOffset;
            selectionEnd = currentEnd;
            caretOffset = selectionEnd;
        }
        else
        {
            selectionStart = _wordSelectionAnchorStartOffset;
            selectionEnd = _wordSelectionAnchorEndOffset;
            caretOffset = selectionEnd;
        }

        var startPosition = _document.GetPositionAtOffset(selectionStart, LogicalDirection.Forward) ?? _document.ContentStart;
        var endPosition = _document.GetPositionAtOffset(selectionEnd, LogicalDirection.Forward) ?? _document.ContentEnd;
        _selection = new TextRange(startPosition, endPosition);
        _caretPosition = _document.GetPositionAtOffset(caretOffset, LogicalDirection.Forward) ?? _document.ContentEnd;
        UpdateImeWindowIfComposing();
        OnSelectionChanged();
    }

    private (int start, int end) GetWordRangeAtOffset(int offset)
    {
        var text = _document.GetText();
        if (string.IsNullOrEmpty(text))
        {
            return (0, 0);
        }

        var clampedOffset = Math.Clamp(offset, 0, text.Length);
        if (clampedOffset == text.Length && clampedOffset > 0 && !IsWordBoundary(text[clampedOffset - 1]))
        {
            clampedOffset--;
        }

        if (clampedOffset < text.Length && IsWordBoundary(text[clampedOffset]))
        {
            if (clampedOffset > 0 && !IsWordBoundary(text[clampedOffset - 1]))
            {
                clampedOffset--;
            }
            else
            {
                while (clampedOffset < text.Length && IsWordBoundary(text[clampedOffset]))
                {
                    clampedOffset++;
                }

                if (clampedOffset >= text.Length)
                {
                    return (text.Length, text.Length);
                }
            }
        }

        var start = clampedOffset;
        while (start > 0 && !IsWordBoundary(text[start - 1]))
        {
            start--;
        }

        var end = clampedOffset;
        while (end < text.Length && !IsWordBoundary(text[end]))
        {
            end++;
        }

        return (start, end);
    }

    private static bool IsWordBoundary(char c)
    {
        return char.IsWhiteSpace(c) || char.IsPunctuation(c);
    }

    #endregion

    #region IME Support

    /// <inheritdoc />
    public Point GetImeCaretPosition()
    {
        var contentBounds = GetContentBounds();
        double lineHeight = GetDefaultLineHeight();
        int anchorOffset = _isImeComposing ? GetImeAnchorOffset() : (_caretPosition?.DocumentOffset ?? 0);
        var anchorPosition = _document.GetPositionAtOffset(anchorOffset, LogicalDirection.Forward) ?? _document.ContentStart;
        var caretPoint = GetCaretScreenPosition(contentBounds, anchorPosition) ?? new Point(contentBounds.Left, contentBounds.Top);

        if (_isImeComposing && !string.IsNullOrEmpty(_imeCompositionString) && _imeCompositionCursor > 0)
        {
            var formatting = GetImeFormatting(anchorPosition);
            string beforeCursor = _imeCompositionString.Substring(0, Math.Min(_imeCompositionCursor, _imeCompositionString.Length));
            var text = new FormattedText(beforeCursor, formatting.FontFamily, formatting.FontSize)
            {
                FontWeight = formatting.FontWeight.ToOpenTypeWeight(),
                FontStyle = formatting.FontStyle.ToOpenTypeStyle()
            };
            TextMeasurement.MeasureText(text);
            caretPoint = new Point(caretPoint.X + text.Width, caretPoint.Y);
            lineHeight = GetLineHeightForFormatting(formatting.FontSize);
        }

        return new Point(caretPoint.X, caretPoint.Y + lineHeight);
    }

    /// <inheritdoc />
    public void OnImeCompositionStart()
    {
        _isImeComposing = true;
        _imeCompositionStart = _caretPosition?.DocumentOffset ?? 0;
        _imeCompositionString = string.Empty;
        _imeCompositionCursor = 0;

        if (_selection != null && !_selection.IsEmpty)
        {
            DeleteSelection();
            _imeCompositionStart = _caretPosition?.DocumentOffset ?? _imeCompositionStart;
        }

        UpdateImeWindowIfComposing();
        InvalidateVisual();
    }

    /// <inheritdoc />
    public void OnImeCompositionUpdate(string compositionString, int cursorPosition)
    {
        _imeCompositionString = compositionString ?? string.Empty;
        _imeCompositionCursor = Math.Clamp(cursorPosition, 0, _imeCompositionString.Length);
        UpdateImeWindowIfComposing();
        InvalidateVisual();
    }

    /// <inheritdoc />
    public void OnImeCompositionEnd(string? resultString)
    {
        _isImeComposing = false;
        _imeCompositionString = string.Empty;
        _imeCompositionCursor = 0;
        _imeCompositionStart = _caretPosition?.DocumentOffset ?? 0;
        InvalidateVisual();
    }

    private void UpdateImeWindowIfComposing()
    {
        if (!_isImeComposing)
            return;

        for (Visual? current = this; current != null; current = current.VisualParent)
        {
            if (current is Window window)
            {
                window.UpdateImeCompositionWindow();
                break;
            }
        }
    }

    private Rect GetContentBounds()
    {
        return new Rect(
            BorderThickness.Left + Padding.Left,
            BorderThickness.Top + Padding.Top,
            Math.Max(0, RenderSize.Width - BorderThickness.Left - BorderThickness.Right - Padding.Left - Padding.Right),
            Math.Max(0, RenderSize.Height - BorderThickness.Top - BorderThickness.Bottom - Padding.Top - Padding.Bottom));
    }

    private int GetImeAnchorOffset()
    {
        return Math.Clamp(_imeCompositionStart, 0, Math.Max(0, _document.GetText().Length));
    }

    private (string FontFamily, double FontSize, FontWeight FontWeight, FontStyle FontStyle) GetImeFormatting(TextPointer? position)
    {
        if (position?.Parent is Run run)
        {
            string fontFamily = !string.IsNullOrWhiteSpace(run.FontFamily) ? run.FontFamily : (_document.FontFamily ?? "Segoe UI");
            double fontSize = run.FontSize > 0 ? run.FontSize : _document.FontSize;
            return (fontFamily, fontSize, run.FontWeight, run.FontStyle);
        }

        return (_document.FontFamily ?? "Segoe UI", _document.FontSize, FontWeights.Normal, FontStyles.Normal);
    }

    private double GetLineHeightForFormatting(double fontSize)
    {
        return Math.Max(1, fontSize * 1.5);
    }

    private TextPointer? FindPreviousWordBoundary(TextPointer position)
    {
        var offset = position.DocumentOffset;
        var text = _document.GetText();

        if (offset <= 0)
            return _document.ContentStart;

        offset--;

        // Skip whitespace
        while (offset > 0 && char.IsWhiteSpace(text[offset]))
            offset--;

        // Find start of word
        while (offset > 0 && !char.IsWhiteSpace(text[offset - 1]))
            offset--;

        return _document.GetPositionAtOffset(offset, LogicalDirection.Forward);
    }

    private TextPointer? FindNextWordBoundary(TextPointer position)
    {
        var offset = position.DocumentOffset;
        var text = _document.GetText();

        if (offset >= text.Length)
            return _document.ContentEnd;

        // Skip current word
        while (offset < text.Length && !char.IsWhiteSpace(text[offset]))
            offset++;

        // Skip whitespace
        while (offset < text.Length && char.IsWhiteSpace(text[offset]))
            offset++;

        return _document.GetPositionAtOffset(offset, LogicalDirection.Forward);
    }

    #endregion

    #region Property Changed

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RichTextBox richTextBox)
        {
            richTextBox.InvalidateVisual();
        }
    }

    #endregion

    #region Helper Types

    /// <summary>
    /// Represents the state of a document for undo/redo.
    /// </summary>
    private class DocumentState
    {
        public string Text { get; }
        public int CaretOffset { get; }
        public int SelectionStart { get; }
        public int SelectionEnd { get; }

        public DocumentState(string text, int caretOffset, int selectionStart, int selectionEnd)
        {
            Text = text;
            CaretOffset = caretOffset;
            SelectionStart = selectionStart;
            SelectionEnd = selectionEnd;
        }
    }

    /// <summary>
    /// Layout information for the entire document.
    /// </summary>
    private class FlowDocumentLayoutInfo
    {
        public List<BlockLayoutInfo> Blocks { get; } = new();
        public double TotalHeight { get; set; }
    }

    /// <summary>
    /// Layout information for a block element.
    /// </summary>
    private class BlockLayoutInfo
    {
        public Block Block { get; set; } = null!;
        public Thickness Margin { get; set; }
        public List<LineLayoutInfo> Lines { get; } = new();
        public List<BlockLayoutInfo> ChildBlocks { get; } = new();
    }

    /// <summary>
    /// Layout information for a line of text.
    /// </summary>
    private class LineLayoutInfo
    {
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Baseline { get; set; }
        public List<RunLayoutInfo> Runs { get; } = new();
    }

    /// <summary>
    /// Layout information for a run of text.
    /// </summary>
    private class RunLayoutInfo
    {
        public Run? Run { get; set; }
        public double X { get; set; }
        public double Width { get; set; }
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
    }

    #endregion
}
