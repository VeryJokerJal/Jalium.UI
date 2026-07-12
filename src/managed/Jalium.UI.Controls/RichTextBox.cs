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
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.RichTextBoxAutomationPeer(this);
    }

    #region Static Brushes

    private static readonly SolidColorBrush s_defaultForegroundBrush = new(Color.White);
    private static readonly SolidColorBrush s_defaultSelectionBrush = new(Color.FromArgb(180, 0x1E, 0x79, 0x3F));
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
    private TextSelection? _selection;
    private List<SpellingError> _spellingErrors = new();
    private string? _spellCheckedText;
    private readonly Dictionary<UIElement, bool> _documentElementEnabledStates =
        new(ReferenceEqualityComparer.Instance);

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
    /// Tick interval during fade phases (ms). Hold phases use longer dynamic intervals.
    /// </summary>
    private const int CaretAnimationTickMs = 33;

    /// <summary>
    /// Caret rect in local coordinates, published by <see cref="RenderCaret"/> so
    /// the blink timer can invalidate only this region instead of the entire
    /// RichTextBox — crucial because RichTextBox typically has a large visual
    /// surface (document body) that would otherwise be redrawn every 530ms.
    /// </summary>
    private Rect _lastRenderedCaretRect = Rect.Empty;

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
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(RichTextBox),
            new PropertyMetadata(false, OnIsReadOnlyChangedStatic));

    private static void OnIsReadOnlyChangedStatic(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RichTextBox rtb && rtb.IsKeyboardFocused)
        {
            rtb.FindHostWindow()?.RefreshInputMethodAssociation();
        }
    }

    private Window? FindHostWindow()
    {
        for (Visual? current = this; current != null; current = current.VisualParent)
        {
            if (current is Window w)
                return w;
        }
        return null;
    }

    /// <summary>
    /// Identifies the AcceptsTab dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty AcceptsTabProperty =
        DependencyProperty.Register(nameof(AcceptsTab), typeof(bool), typeof(RichTextBox),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the SelectionBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SelectionBrushProperty =
        DependencyProperty.Register(nameof(SelectionBrush), typeof(Brush), typeof(RichTextBox),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the CaretBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CaretBrushProperty =
        DependencyProperty.Register(nameof(CaretBrush), typeof(Brush), typeof(RichTextBox),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsUndoEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsUndoEnabledProperty =
        DependencyProperty.Register(nameof(IsUndoEnabled), typeof(bool), typeof(RichTextBox),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the UndoLimit dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty UndoLimitProperty =
        DependencyProperty.Register(nameof(UndoLimit), typeof(int), typeof(RichTextBox),
            new PropertyMetadata(100));

    /// <summary>
    /// Identifies the HorizontalScrollBarVisibility dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty =
        DependencyProperty.Register(nameof(HorizontalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(RichTextBox),
            new PropertyMetadata(ScrollBarVisibility.Auto));

    /// <summary>
    /// Identifies the VerticalScrollBarVisibility dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty =
        DependencyProperty.Register(nameof(VerticalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(RichTextBox),
            new PropertyMetadata(ScrollBarVisibility.Auto));

    /// <summary>
    /// Identifies the IsSpellCheckEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsSpellCheckEnabledProperty =
        DependencyProperty.Register(nameof(IsSpellCheckEnabled), typeof(bool), typeof(RichTextBox),
            new PropertyMetadata(false, OnSpellCheckEnabledChanged));

    /// <summary>Identifies the IsDocumentEnabled dependency property.</summary>
    public static readonly DependencyProperty IsDocumentEnabledProperty =
        DependencyProperty.Register(nameof(IsDocumentEnabled), typeof(bool), typeof(RichTextBox),
            new PropertyMetadata(false, OnIsDocumentEnabledChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets whether the control is read-only.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty)!;
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the control accepts Tab key for tab characters.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public bool AcceptsTab
    {
        get => (bool)GetValue(AcceptsTabProperty)!;
        set => SetValue(AcceptsTabProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for text selection highlighting.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? SelectionBrush
    {
        get => (Brush?)GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the brush for the caret.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? CaretBrush
    {
        get => (Brush?)GetValue(CaretBrushProperty);
        set => SetValue(CaretBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets whether undo is enabled.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsUndoEnabled
    {
        get => (bool)GetValue(IsUndoEnabledProperty)!;
        set => SetValue(IsUndoEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum number of undo entries.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public int UndoLimit
    {
        get => (int)GetValue(UndoLimitProperty)!;
        set => SetValue(UndoLimitProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal scroll bar visibility.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(HorizontalScrollBarVisibilityProperty)!;
        set => SetValue(HorizontalScrollBarVisibilityProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical scroll bar visibility.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get => (ScrollBarVisibility)GetValue(VerticalScrollBarVisibilityProperty)!;
        set => SetValue(VerticalScrollBarVisibilityProperty, value);
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

    /// <summary>Gets or sets whether UI elements hosted in the document are enabled.</summary>
    public bool IsDocumentEnabled
    {
        get => (bool)GetValue(IsDocumentEnabledProperty)!;
        set => SetValue(IsDocumentEnabledProperty, value);
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
                RestoreDocumentElementEnabledStates();
                _document = value ?? new FlowDocument();
                _caretPosition = _document.ContentStart;
                _selection = new TextSelection(_document.ContentStart, _document.ContentStart);
                _spellCheckedText = null;
                ApplyDocumentEnabledState();
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
    public TextSelection Selection
    {
        get
        {
            _selection ??= new TextSelection(_document.ContentStart, _document.ContentStart);
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
        _selection = new TextSelection(_document.ContentStart, _document.ContentStart);

        Focusable = true;
        Cursor = Cursors.IBeam;
        _lastCaretBlink = DateTime.Now;
        _lastClickTime = DateTime.MinValue;

        InputMethod.CompositionStarted += OnImeCompositionStarted;
        InputMethod.CompositionUpdated += OnImeCompositionUpdated;
        InputMethod.CompositionEnded += OnImeCompositionEnded;

        // Register input event handlers
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        AddHandler(TextInputEvent, new TextCompositionEventHandler(OnTextInputHandler));
        AddHandler(MouseWheelEvent, new MouseWheelEventHandler(OnMouseWheelHandler));
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
        _selection = new TextSelection(_document.ContentStart, _document.ContentEnd);
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
            _selection = new TextSelection(_caretPosition, _caretPosition);
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

    /// <summary>Returns the text position nearest to a point in control coordinates.</summary>
    public TextPointer? GetPositionFromPoint(Point point, bool snapToText)
    {
        if (!snapToText && (!new Rect(RenderSize).Contains(point) || !IsPointOverDocumentText(point)))
            return null;

        return GetTextPositionFromPoint(point);
    }

    /// <summary>Gets the spelling error at a document position.</summary>
    public SpellingError? GetSpellingError(TextPointer position)
    {
        ValidateDocumentPosition(position);
        EnsureSpellingErrors();
        var offset = position.DocumentOffset;
        return _spellingErrors.FirstOrDefault(error =>
            offset >= error.StartIndex && offset < error.StartIndex + error.Length);
    }

    /// <summary>Gets the range occupied by the spelling error at a document position.</summary>
    public TextRange? GetSpellingErrorRange(TextPointer position)
    {
        var error = GetSpellingError(position);
        if (error == null)
            return null;

        var start = _document.GetPositionAtOffset(error.StartIndex, LogicalDirection.Forward) ?? _document.ContentStart;
        var end = _document.GetPositionAtOffset(error.StartIndex + error.Length, LogicalDirection.Backward) ?? _document.ContentEnd;
        return new TextRange(start, end);
    }

    /// <summary>Returns the next spelling-error position in the requested direction.</summary>
    public TextPointer? GetNextSpellingErrorPosition(TextPointer position, LogicalDirection direction)
    {
        ValidateDocumentPosition(position);
        EnsureSpellingErrors();
        var offset = position.DocumentOffset;
        SpellingError? error = direction == LogicalDirection.Forward
            ? _spellingErrors.Where(candidate => candidate.StartIndex >= offset)
                .OrderBy(candidate => candidate.StartIndex).FirstOrDefault()
            : _spellingErrors.Where(candidate => candidate.StartIndex < offset)
                .OrderByDescending(candidate => candidate.StartIndex).FirstOrDefault();
        return error == null
            ? null
            : _document.GetPositionAtOffset(error.StartIndex, direction);
    }

    /// <summary>Returns whether the current document contains serializable content.</summary>
    public bool ShouldSerializeDocument() => _document.Blocks.Count > 0;

    /// <summary>
    /// Loads a text file as plain text, replacing the document. A byte-order
    /// mark, if present, decides the encoding; otherwise <paramref name="encoding"/>
    /// is used — UTF-8 when it is <see langword="null"/>. Legacy code pages such
    /// as <c>Encoding.GetEncoding(936)</c> (GBK) are supported.
    /// </summary>
    public void LoadFromFile(string path, System.Text.Encoding? encoding = null)
        => SetText(TextFile.ReadAllText(path, encoding));

    /// <summary>
    /// Writes the document's plain text to a file using <paramref name="encoding"/>
    /// — UTF-8 when it is <see langword="null"/>.
    /// </summary>
    public void SaveToFile(string path, System.Text.Encoding? encoding = null)
        => TextFile.WriteAllText(path, GetText(), encoding);

    /// <summary>
    /// Sets the document content from plain text.
    /// </summary>
    /// <param name="text">The plain text to set.</param>
    public void SetText(string text)
    {
        PushUndo();
        RestoreDocumentElementEnabledStates();
        _document = FlowDocument.FromText(text);
        _caretPosition = _document.ContentEnd;
        _selection = new TextSelection(_document.ContentStart, _document.ContentStart);
        _spellCheckedText = null;
        ApplyDocumentEnabledState();
        InvalidateLayout();
        UpdateImeWindowIfComposing();
        InvalidateVisual();
    }

    private void ValidateDocumentPosition(TextPointer position)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (!ReferenceEquals(position.Document, _document))
            throw new ArgumentException("The text position does not belong to this RichTextBox document.", nameof(position));
    }

    private void EnsureSpellingErrors()
    {
        var text = GetText();
        if (string.Equals(_spellCheckedText, text, StringComparison.Ordinal))
            return;

        _spellCheckedText = text;
        if (!IsSpellCheckEnabled || SpellChecker.Default is not { IsAvailable: true } checker || text.Length == 0)
        {
            _spellingErrors.Clear();
            return;
        }

        _spellingErrors = checker.Check(text)
            .Select(error => error.WithHandlers(CorrectSpellingError, IgnoreSpellingError))
            .ToList();
    }

    private void CorrectSpellingError(SpellingError error, string correctedText)
    {
        if (IsReadOnly)
        {
            return;
        }

        var start = _document.GetPositionAtOffset(error.StartIndex, LogicalDirection.Forward);
        var end = _document.GetPositionAtOffset(
            error.StartIndex + error.Length,
            LogicalDirection.Backward);
        if (start == null || end == null)
        {
            return;
        }

        new TextRange(start, end).Text = correctedText;
        _spellCheckedText = null;
        EnsureSpellingErrors();
        InvalidateVisual();
    }

    private void IgnoreSpellingError(SpellingError error)
    {
        SpellChecker.Default?.IgnoreWord(error.Word);
        _spellCheckedText = null;
        EnsureSpellingErrors();
        InvalidateVisual();
    }

    private static void OnSpellCheckEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var richTextBox = (RichTextBox)d;
        richTextBox._spellCheckedText = null;
        richTextBox._spellingErrors.Clear();
        if (e.NewValue is true)
        {
            richTextBox.EnsureSpellingErrors();
        }

        richTextBox.InvalidateVisual();
    }

    private static void OnIsDocumentEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var richTextBox = (RichTextBox)d;
        richTextBox.ApplyDocumentEnabledState();
        richTextBox.InvalidateVisual();
    }

    private void ApplyDocumentEnabledState()
    {
        if (IsDocumentEnabled)
        {
            RestoreDocumentElementEnabledStates();
            return;
        }

        foreach (var element in EnumerateDocumentElements(_document))
        {
            if (_documentElementEnabledStates.TryAdd(element, element.IsEnabled))
            {
                element.IsEnabled = false;
            }
        }
    }

    private void RestoreDocumentElementEnabledStates()
    {
        foreach (var entry in _documentElementEnabledStates)
        {
            entry.Key.IsEnabled = entry.Value;
        }

        _documentElementEnabledStates.Clear();
    }

    private static IEnumerable<UIElement> EnumerateDocumentElements(FlowDocument document)
    {
        foreach (var block in document.Blocks)
        {
            foreach (var element in EnumerateDocumentElements(block))
                yield return element;
        }
    }

    private static IEnumerable<UIElement> EnumerateDocumentElements(Block block)
    {
        switch (block)
        {
            case BlockUIContainer { Child: { } child }:
                yield return child;
                break;
            case Paragraph paragraph:
                foreach (var inline in paragraph.Inlines)
                {
                    foreach (var element in EnumerateDocumentElements(inline))
                        yield return element;
                }
                break;
            case Section section:
                foreach (var nestedBlock in section.Blocks)
                {
                    foreach (var element in EnumerateDocumentElements(nestedBlock))
                        yield return element;
                }
                break;
            case Jalium.UI.Documents.List list:
                foreach (var item in list.ListItems)
                {
                    foreach (var nestedBlock in item.Blocks)
                    {
                        foreach (var element in EnumerateDocumentElements(nestedBlock))
                            yield return element;
                    }
                }
                break;
        }
    }

    private static IEnumerable<UIElement> EnumerateDocumentElements(Inline inline)
    {
        if (inline is InlineUIContainer { Child: { } child })
            yield return child;

        if (inline is AnchoredBlock anchoredBlock)
        {
            foreach (var nestedBlock in anchoredBlock.Blocks)
            {
                foreach (var element in EnumerateDocumentElements(nestedBlock))
                    yield return element;
            }
        }

        if (inline is Span span)
        {
            foreach (var nestedInline in span.Inlines)
            {
                foreach (var element in EnumerateDocumentElements(nestedInline))
                    yield return element;
            }
        }
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
        _selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(fontFamily));
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
    /// Inserts a paragraph break at the current caret position, splitting the current paragraph into two.
    /// </summary>
    protected void InsertParagraphBreak()
    {
        if (IsReadOnly)
            return;

        PushUndo();

        if (_selection != null && !_selection.IsEmpty)
        {
            DeleteSelectionInternal();
        }

        if (_caretPosition == null)
            return;

        if (_caretPosition.Parent is Run run)
        {
            var paragraph = run.Parent as Paragraph;
            if (paragraph == null)
                return;

            var offset = _caretPosition.Offset;
            var textBefore = run.Text.Substring(0, offset);
            var textAfter = run.Text.Substring(offset);

            // Find the index of this run in the paragraph
            var runIndex = paragraph.Inlines.IndexOf(run);

            // Create the new paragraph with the text after the split point
            var newParagraph = new Paragraph();

            // Add remaining text from current run to new paragraph
            if (!string.IsNullOrEmpty(textAfter))
            {
                newParagraph.Inlines.Add(new Run(textAfter));
            }

            // Move all inlines after the current run to the new paragraph
            for (int i = paragraph.Inlines.Count - 1; i > runIndex; i--)
            {
                var inline = paragraph.Inlines[i];
                paragraph.Inlines.RemoveAt(i);
                newParagraph.Inlines.Insert(0, inline);
            }

            // Update the current run to only contain text before the split
            run.Text = textBefore;

            // If current run is now empty, remove it (but keep paragraph)
            if (string.IsNullOrEmpty(textBefore) && paragraph.Inlines.Count > 1)
            {
                paragraph.Inlines.Remove(run);
            }

            // If new paragraph has no inlines, add an empty run
            if (newParagraph.Inlines.Count == 0)
            {
                newParagraph.Inlines.Add(new Run(string.Empty));
            }

            // Insert the new paragraph after the current one
            var blockIndex = _document.Blocks.IndexOf(paragraph);
            if (blockIndex >= 0 && blockIndex < _document.Blocks.Count - 1)
            {
                _document.Blocks.Insert(blockIndex + 1, newParagraph);
            }
            else
            {
                _document.Blocks.Add(newParagraph);
            }

            // Move caret to the start of the new paragraph
            _caretPosition = _document.GetPositionAtOffset(
                _caretPosition.DocumentOffset + 1, LogicalDirection.Forward) ?? _document.ContentEnd;
            _selection = new TextSelection(_caretPosition, _caretPosition);
        }
        else if (_caretPosition.Parent is Paragraph para)
        {
            // Caret is directly in the paragraph (no runs), add a new empty paragraph
            var newParagraph = new Paragraph(new Run(string.Empty));
            var blockIndex = _document.Blocks.IndexOf(para);
            if (blockIndex >= 0 && blockIndex < _document.Blocks.Count - 1)
            {
                _document.Blocks.Insert(blockIndex + 1, newParagraph);
            }
            else
            {
                _document.Blocks.Add(newParagraph);
            }

            _caretPosition = _document.GetPositionAtOffset(
                _caretPosition.DocumentOffset + 1, LogicalDirection.Forward) ?? _document.ContentEnd;
            _selection = new TextSelection(_caretPosition, _caretPosition);
        }
        else
        {
            // Fallback: just insert newline as text
            InsertText("\n");
            return;
        }

        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateLayout();
        InvalidateVisual();
    }

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
            // Find the appropriate Run to insert into based on offset within the paragraph
            int paragraphOffset = position.Offset;
            int accumulated = 0;

            // Try to find the Run at or near the offset position
            Run? targetRun = null;
            int insertOffset = 0;

            foreach (var inline in paragraph.Inlines)
            {
                if (inline is Run r)
                {
                    if (paragraphOffset >= accumulated && paragraphOffset <= accumulated + r.Text.Length)
                    {
                        targetRun = r;
                        insertOffset = paragraphOffset - accumulated;
                        break;
                    }
                    accumulated += r.Text.Length;
                }
            }

            if (targetRun != null)
            {
                // Insert into the existing Run
                targetRun.Text = targetRun.Text.Insert(insertOffset, text);
            }
            else if (paragraph.Inlines.Count > 0 && paragraph.Inlines[paragraph.Inlines.Count - 1] is Run lastRun)
            {
                // Append to the last Run
                lastRun.Text += text;
            }
            else
            {
                // Create a new Run
                paragraph.Inlines.Add(new Run(text));
            }

            // Update caret position
            _caretPosition = _document.GetPositionAtOffset(
                position.DocumentOffset + text.Length, LogicalDirection.Forward);
        }
        else if (_document.Blocks.Count == 0)
        {
            // Document is empty, create a new paragraph
            var newRun = new Run(text);
            var newParagraph = new Paragraph(newRun);
            _document.Blocks.Add(newParagraph);

            // Point caret to the Run, not the Paragraph
            _caretPosition = _document.GetPositionAtOffset(text.Length, LogicalDirection.Forward);
        }

        // Update selection to be empty at new caret position
        if (_caretPosition != null)
        {
            _selection = new TextSelection(_caretPosition, _caretPosition);
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
        _selection = new TextSelection(_caretPosition, _caretPosition);

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
            _selection = new TextSelection(start, end);
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

        if (_caretTimer is { IsEnabled: true })
        {
            ScheduleNextCaretTick(_lastCaretBlink);
        }
    }

    /// <summary>
    /// Ensures the caret is visible by scrolling if necessary.
    /// </summary>
    protected void EnsureCaretVisible()
    {
        if (_caretPosition == null)
        {
            UpdateImeWindowIfComposing();
            return;
        }

        var contentBounds = GetContentBounds();
        if (contentBounds.Width <= 0 || contentBounds.Height <= 0)
        {
            UpdateImeWindowIfComposing();
            return;
        }

        var caretPos = GetCaretScreenPosition(contentBounds);
        if (caretPos == null)
        {
            UpdateImeWindowIfComposing();
            return;
        }

        var lineHeight = GetDefaultLineHeight();
        var caretX = caretPos.Value.X;
        var caretY = caretPos.Value.Y;

        // Horizontal scrolling: adjust so caret is within content bounds
        if (caretX < contentBounds.Left)
        {
            _horizontalOffset -= (contentBounds.Left - caretX);
        }
        else if (caretX > contentBounds.Right - 2)
        {
            _horizontalOffset += (caretX - contentBounds.Right + 2);
        }

        // Vertical scrolling: adjust so caret line is within content bounds
        if (caretY < contentBounds.Top)
        {
            _verticalOffset -= (contentBounds.Top - caretY);
        }
        else if (caretY + lineHeight > contentBounds.Bottom)
        {
            _verticalOffset += (caretY + lineHeight - contentBounds.Bottom);
        }

        _horizontalOffset = Math.Max(0, Math.Round(_horizontalOffset));
        _verticalOffset = Math.Max(0, Math.Round(_verticalOffset));

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
    protected override void OnRender(DrawingContext drawingContext)
    {
        ApplyDocumentEnabledState();
        base.OnRender(drawingContext);

        var dc = drawingContext;

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

        // Focus indicator is painted by FocusVisualManager into the adorner layer.

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
                var fontFamily = runLayout.Run.FontFamily?.Source
                    ?? _document.FontFamily
                    ?? FrameworkElement.DefaultFontFamilyName;
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
        var selBrush = ResolveSelectionBrush();
        if (selBrush == null || _selection == null || _selection.IsEmpty)
            return;

        var layout = EnsureLayout(contentBounds.Width);
        if (layout == null)
            return;

        var selStart = _selection.Start.DocumentOffset;
        var selEnd = _selection.End.DocumentOffset;
        if (selStart > selEnd)
            (selStart, selEnd) = (selEnd, selStart);

        var y = contentBounds.Top - _verticalOffset;

        foreach (var blockLayout in layout.Blocks)
        {
            RenderSelectionInBlock(dc, blockLayout, contentBounds, selBrush, selStart, selEnd,
                contentBounds.Left - _horizontalOffset, ref y);
        }
    }

    private void RenderSelectionInBlock(DrawingContext dc, BlockLayoutInfo blockLayout, Rect contentBounds,
        Brush selBrush, int selStart, int selEnd, double baseX, ref double y)
    {
        var x = baseX + blockLayout.Margin.Left;

        foreach (var lineLayout in blockLayout.Lines)
        {
            // Check if this line intersects the selection
            if (lineLayout.EndOffset > selStart && lineLayout.StartOffset < selEnd)
            {
                // Calculate the X range of the selection within this line
                double startX, endX;

                if (selStart <= lineLayout.StartOffset)
                {
                    startX = x;
                }
                else
                {
                    startX = x + GetXOffsetInLine(lineLayout, selStart);
                }

                if (selEnd >= lineLayout.EndOffset)
                {
                    endX = x + lineLayout.Width;
                }
                else
                {
                    endX = x + GetXOffsetInLine(lineLayout, selEnd);
                }

                if (endX > startX && y + lineLayout.Height > contentBounds.Top && y < contentBounds.Bottom)
                {
                    dc.DrawRectangle(selBrush, null,
                        new Rect(startX, y, endX - startX, lineLayout.Height));
                }
            }
            y += lineLayout.Height;
        }

        foreach (var childLayout in blockLayout.ChildBlocks)
        {
            RenderSelectionInBlock(dc, childLayout, contentBounds, selBrush, selStart, selEnd, x, ref y);
        }

        y += blockLayout.Margin.Bottom;
    }

    private double GetXOffsetInLine(LineLayoutInfo lineLayout, int targetOffset)
    {
        foreach (var runLayout in lineLayout.Runs)
        {
            if (targetOffset >= runLayout.StartOffset && targetOffset <= runLayout.EndOffset && runLayout.Run != null)
            {
                var offsetInRun = targetOffset - runLayout.StartOffset;
                var textBefore = runLayout.Run.Text.Substring(0, Math.Min(offsetInRun, runLayout.Run.Text.Length));
                return runLayout.X + MeasureText(textBefore, runLayout.Run);
            }
        }

        // If past all runs, return line width
        return lineLayout.Width;
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

        // Publish caret rect (local coords) so the blink timer can invalidate
        // only this region instead of the whole RichTextBox.
        _lastRenderedCaretRect = new Rect(
            caretPos.Value.X - 2, caretPos.Value.Y - 1,
            6, lineHeight + 2);
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

        var fontFamily = run.FontFamily?.Source
            ?? _document.FontFamily
            ?? FrameworkElement.DefaultFontFamilyName;
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
            var fontFamily = run.FontFamily?.Source
                ?? _document.FontFamily
                ?? FrameworkElement.DefaultFontFamilyName;
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

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (e.Handled)
            return;

        OnKeyDown(e);
    }

    /// <summary>
    /// Handles key down events.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
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
                InsertParagraphBreak();
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

    private void OnTextInputHandler(object sender, TextCompositionEventArgs e)
    {
        if (e.Handled || IsReadOnly)
            return;

        if (!string.IsNullOrEmpty(e.Text))
        {
            var text = e.Text;
            if (text.Length == 1 && char.IsControl(text[0]) && text[0] != '\t')
                return;

            InsertText(text);
            e.Handled = true;
        }
    }

    private void OnImeCompositionStarted(object? sender, EventArgs e)
    {
        if (InputMethod.CurrentTarget == this)
        {
            OnImeCompositionStart();
        }
    }

    private void OnImeCompositionUpdated(object? sender, CompositionEventArgs e)
    {
        if (InputMethod.CurrentTarget == this)
        {
            OnImeCompositionUpdate(e.Text, e.CursorPosition);
        }
    }

    private void OnImeCompositionEnded(object? sender, CompositionResultEventArgs e)
    {
        if (InputMethod.CurrentTarget == this)
        {
            OnImeCompositionEnd(e.Result);
        }
    }

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        if (e.ChangedButton == MouseButton.Left)
        {
            Focus();

            var position = e.GetPosition(this);
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

                if ((e.KeyboardModifiers & ModifierKeys.Shift) != 0 && _caretPosition != null && newCaretPosition != null)
                {
                    _selection = new TextSelection(_caretPosition, newCaretPosition);
                }
                else
                {
                    _caretPosition = newCaretPosition;
                    _selectionAnchor = newCaretPosition;
                    if (newCaretPosition != null)
                    {
                        _selection = new TextSelection(newCaretPosition, newCaretPosition);
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

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        if (e.ChangedButton == MouseButton.Left)
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

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (!IsEnabled || !_isSelecting) return;

        var position = e.GetPosition(this);
        var newCaretPosition = GetTextPositionFromPoint(position);

        if (newCaretPosition != null)
        {
            if (_isWordSelecting)
            {
                ExtendWordSelection(newCaretPosition);
            }
            else if (_selectionAnchor != null)
            {
                _selection = new TextSelection(_selectionAnchor, newCaretPosition);
                _caretPosition = newCaretPosition;
            }
        }

        EnsureCaretVisible();
        InvalidateVisual();
        e.Handled = true;
    }

    private void OnMouseWheelHandler(object sender, MouseWheelEventArgs e)
    {
        var lineHeight = GetDefaultLineHeight();
        var delta = e.Delta > 0 ? -3 : 3;
        VerticalOffset += delta * lineHeight;
        e.Handled = true;
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

    private bool IsPointOverDocumentText(Point point)
    {
        var contentBounds = new Rect(
            BorderThickness.Left + Padding.Left,
            BorderThickness.Top + Padding.Top,
            Math.Max(0, RenderSize.Width - BorderThickness.Left - BorderThickness.Right - Padding.Left - Padding.Right),
            Math.Max(0, RenderSize.Height - BorderThickness.Top - BorderThickness.Bottom - Padding.Top - Padding.Bottom));
        if (!contentBounds.Contains(point))
            return false;

        var layout = EnsureLayout(contentBounds.Width);
        if (layout == null)
            return false;

        var y = contentBounds.Top - _verticalOffset;
        var targetX = point.X - contentBounds.Left + _horizontalOffset;
        foreach (var blockLayout in layout.Blocks)
        {
            var overText = IsPointOverDocumentText(blockLayout, targetX, point.Y, ref y, out var verticalHit);
            if (verticalHit)
                return overText;
        }

        return false;
    }

    private static bool IsPointOverDocumentText(
        BlockLayoutInfo blockLayout,
        double targetX,
        double targetY,
        ref double y,
        out bool verticalHit)
    {
        foreach (var lineLayout in blockLayout.Lines)
        {
            if (targetY >= y && targetY < y + lineLayout.Height)
            {
                verticalHit = true;
                var x = blockLayout.Margin.Left;
                return lineLayout.Runs.Any(runLayout =>
                    runLayout.Width > 0 &&
                    targetX >= x + runLayout.X &&
                    targetX < x + runLayout.X + runLayout.Width);
            }

            y += lineLayout.Height;
        }

        foreach (var childLayout in blockLayout.ChildBlocks)
        {
            var overText = IsPointOverDocumentText(childLayout, targetX, targetY, ref y, out verticalHit);
            if (verticalHit)
                return overText;
        }

        y += blockLayout.Margin.Bottom;
        verticalHit = false;
        return false;
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
        var fontFamily = run.FontFamily?.Source
            ?? _document.FontFamily
            ?? FrameworkElement.DefaultFontFamilyName;
        var fontSize = run.FontSize;
        if (fontSize <= 0)
            fontSize = _document.FontSize;

        int index = text.Length;
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
                index = (i > 0 && (x - prevWidth) < (width - x)) ? i - 1 : i;
                break;
            }
            prevWidth = width;
        }

        // Snap onto a grapheme-cluster edge so a hit never lands inside an emoji.
        return GraphemeClusters.SnapNearest(text, index);
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
            if (InputMethod.CurrentTarget == this)
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
            _caretTimer.Tick += OnCaretTimerTick;
        }

        ScheduleNextCaretTick(DateTime.Now);
        _caretTimer.Start();
    }

    private void StopCaretTimer()
    {
        _caretTimer?.Stop();
    }

    private void OnCaretTimerTick(object? sender, EventArgs e)
    {
        if (!IsKeyboardFocused || IsReadOnly)
        {
            StopCaretTimer();
            return;
        }

        if (!_lastRenderedCaretRect.IsEmpty)
        {
            InvalidateVisual(_lastRenderedCaretRect);
        }
        else
        {
            InvalidateVisual();
        }
        ScheduleNextCaretTick(DateTime.Now);
    }

    /// <summary>
    /// Schedules the next caret timer tick based on the current blink/fade phase.
    /// Uses longer intervals during hold phases to avoid unnecessary invalidations.
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
        double hiddenEnd = fadeOutEnd + CaretBlinkInterval;

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
        var newPosition = FindPositionOnAdjacentLine(-1);

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
        var newPosition = FindPositionOnAdjacentLine(1);

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

    private TextPointer FindPositionOnAdjacentLine(int direction)
    {
        if (_caretPosition == null)
            return direction < 0 ? _document.ContentStart : _document.ContentEnd;

        var contentBounds = GetContentBounds();
        var layout = EnsureLayout(contentBounds.Width);
        if (layout == null)
            return direction < 0 ? _document.ContentStart : _document.ContentEnd;

        // Find the current caret screen position to preserve horizontal position
        var caretPos = GetCaretScreenPosition(contentBounds);
        if (caretPos == null)
            return direction < 0 ? _document.ContentStart : _document.ContentEnd;

        var caretOffset = _caretPosition.DocumentOffset;
        var lineHeight = GetDefaultLineHeight();

        // Collect all lines in order with their Y positions
        var allLines = new List<(LineLayoutInfo line, double y, double x)>();
        var y = contentBounds.Top - _verticalOffset;
        CollectAllLines(layout.Blocks, contentBounds.Left - _horizontalOffset, ref y, allLines);

        // Find which line the caret is on
        int currentLineIndex = -1;
        for (int i = 0; i < allLines.Count; i++)
        {
            var (line, _, _) = allLines[i];
            if (caretOffset >= line.StartOffset && caretOffset <= line.EndOffset)
            {
                currentLineIndex = i;
                break;
            }
        }

        if (currentLineIndex < 0)
            return direction < 0 ? _document.ContentStart : _document.ContentEnd;

        int targetLineIndex = currentLineIndex + direction;
        if (targetLineIndex < 0)
            return _document.ContentStart;
        if (targetLineIndex >= allLines.Count)
            return _document.ContentEnd;

        // Use the current caret X position to find the nearest character on the target line
        var targetLine = allLines[targetLineIndex];
        var targetY = targetLine.y + lineHeight / 2;
        var targetX = caretPos.Value.X - (contentBounds.Left - _horizontalOffset);

        // Find the position on the target line at the same X offset
        foreach (var runLayout in targetLine.line.Runs)
        {
            if (runLayout.Run != null && targetX >= targetLine.x + runLayout.X &&
                targetX <= targetLine.x + runLayout.X + runLayout.Width)
            {
                var localX = targetX - targetLine.x - runLayout.X;
                var charIndex = FindCharIndexFromX(runLayout.Run, localX);
                var offset = runLayout.StartOffset + charIndex;
                return _document.GetPositionAtOffset(offset, LogicalDirection.Forward) ?? _document.ContentEnd;
            }
        }

        // X is past the end of the target line
        if (targetX > targetLine.x + targetLine.line.Width)
            return _document.GetPositionAtOffset(targetLine.line.EndOffset, LogicalDirection.Forward) ?? _document.ContentEnd;

        // X is before the start of the target line
        return _document.GetPositionAtOffset(targetLine.line.StartOffset, LogicalDirection.Forward) ?? _document.ContentStart;
    }

    private void CollectAllLines(List<BlockLayoutInfo> blocks, double baseX, ref double y,
        List<(LineLayoutInfo line, double y, double x)> result)
    {
        foreach (var blockLayout in blocks)
        {
            var x = baseX + blockLayout.Margin.Left;

            foreach (var lineLayout in blockLayout.Lines)
            {
                result.Add((lineLayout, y, x));
                y += lineLayout.Height;
            }

            CollectAllLines(blockLayout.ChildBlocks, x, ref y, result);
            y += blockLayout.Margin.Bottom;
        }
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
                _selection = new TextSelection(prevPosition, prevPosition);
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
                _selection = new TextSelection(_caretPosition, _caretPosition);
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
            _selection = new TextSelection(_selectionAnchor, newPosition);
        }
        _caretPosition = newPosition;

        OnSelectionChanged();
    }

    private void SelectWordAt(TextPointer position)
    {
        var (start, end) = GetWordRangeAtOffset(position.DocumentOffset);
        var startPosition = _document.GetPositionAtOffset(start, LogicalDirection.Forward) ?? _document.ContentStart;
        var endPosition = _document.GetPositionAtOffset(end, LogicalDirection.Forward) ?? _document.ContentEnd;
        _selection = new TextSelection(startPosition, endPosition);
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
        _selection = new TextSelection(startPosition, endPosition);
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

        int length = text.Length;
        // Operate on grapheme-cluster boundaries so an emoji is one indivisible
        // unit — never half-selected by a double-click.
        int pos = GraphemeClusters.Snap(text, Math.Clamp(offset, 0, length), forward: false);

        // A hit past the last cluster snaps back onto it when it is a word
        // character.
        if (pos == length && pos > 0)
        {
            int prev = GraphemeClusters.PreviousBoundary(text, pos);
            if (!IsWordSeparatorAt(text, prev, includePunctuation: true))
            {
                pos = prev;
            }
        }

        // Landed on a separator cluster: take the word immediately before it,
        // or the whole run of separators when there is no such word.
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

    #endregion

    #region IME Support

    /// <inheritdoc />
    public bool IsImeAllowed => !IsReadOnly;

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
        // Result is routed via TextInput in Window.OnImeComposition — do NOT
        // InsertText here. Inserting both ways was the root cause of duplicate
        // characters when picking from the Win+. emoji panel, which raises
        // WM_IME_COMPOSITION (GCS_RESULTSTR) and the TextInput bubble already
        // commits the chosen glyph.
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
            string fontFamily = run.FontFamily?.Source is { } runFamily && !string.IsNullOrWhiteSpace(runFamily)
                ? runFamily
                : _document.FontFamily ?? FrameworkElement.DefaultFontFamilyName;
            double fontSize = run.FontSize > 0 ? run.FontSize : _document.FontSize;
            return (fontFamily, fontSize, run.FontWeight, run.FontStyle);
        }

        return (_document.FontFamily ?? FrameworkElement.DefaultFontFamilyName, _document.FontSize, FontWeights.Normal, FontStyles.Normal);
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

        // Walk in grapheme-cluster steps so an emoji is never entered mid-cluster.
        int i = GraphemeClusters.PreviousBoundary(text, offset);

        // Skip whitespace clusters.
        while (i > 0 && IsWordSeparatorAt(text, i, includePunctuation: false))
            i = GraphemeClusters.PreviousBoundary(text, i);

        // Walk back to the start of the word.
        while (i > 0 && !IsWordSeparatorAt(text, GraphemeClusters.PreviousBoundary(text, i), includePunctuation: false))
            i = GraphemeClusters.PreviousBoundary(text, i);

        return _document.GetPositionAtOffset(i, LogicalDirection.Forward);
    }

    private TextPointer? FindNextWordBoundary(TextPointer position)
    {
        var offset = position.DocumentOffset;
        var text = _document.GetText();

        if (offset >= text.Length)
            return _document.ContentEnd;

        int i = GraphemeClusters.Snap(text, offset, forward: true);

        // Skip the current word.
        while (i < text.Length && !IsWordSeparatorAt(text, i, includePunctuation: false))
            i = GraphemeClusters.NextBoundary(text, i);

        // Skip whitespace clusters.
        while (i < text.Length && IsWordSeparatorAt(text, i, includePunctuation: false))
            i = GraphemeClusters.NextBoundary(text, i);

        return _document.GetPositionAtOffset(i, LogicalDirection.Forward);
    }

    #endregion

    #region Property Changed

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
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
