using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Effects;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using WpfClipboard = global::Jalium.UI.Clipboard;

namespace Jalium.UI.Controls;

/// <summary>
/// A control that displays and allows editing of rich text content using a FlowDocument.
/// </summary>
public partial class RichTextBox : TextBoxBase, IImeSupport
{
    private InputMethodWeakSubscription<RichTextBox>? _imeSubscription;
    internal override bool UsesDefaultTextInputHandlers => false;

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
    private DocumentChangeSubscription? _documentChangeSubscription;
    private TextPointer? _caretPosition;
    private TextSelection? _selection;
    private List<SpellingError> _spellingErrors = new();
    private string? _spellCheckedText;
    private readonly Dictionary<UIElement, bool> _documentElementEnabledStates =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Whether the caret is currently visible (for blinking).
    /// </summary>
    private new bool _caretVisible = true;

    /// <summary>
    /// The current caret opacity (0.0 to 1.0) for smooth animation.
    /// </summary>
    private new double _caretOpacity = 1.0;

    /// <summary>
    /// The last time the caret blinked.
    /// </summary>
    private new DateTime _lastCaretBlink;

    /// <summary>
    /// The caret blink interval in milliseconds.
    /// </summary>
    private new const int CaretBlinkInterval = 530;

    /// <summary>
    /// The duration of the fade animation in milliseconds.
    /// </summary>
    private new const int CaretFadeDuration = 150;

    /// <summary>
    /// Timer for caret animation.
    /// </summary>
    private DispatcherTimer? _caretTimer;

    /// <summary>
    /// 光标淡入淡出用的画刷。淡变期间每一帧 alpha 都不同，若每帧新建实例，渲染后端那份
    /// 按实例身份键控的原生画刷缓存就会每帧多一条并触发 LRU 淘汰，把别的控件挤出去。
    /// </summary>
    private SolidColorBrush? _caretFadeBrush;

    /// <summary>边框画笔。BorderBrush/厚度不变时跨帧复用，光标闪烁的重绘不再每帧建 Pen。</summary>
    private RenderPenCache _borderPen;
    private Border? _cssBorderPainter;

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
    private new Rect _lastRenderedCaretRect = Rect.Empty;

    /// <summary>
    /// Whether the user is currently selecting text.
    /// </summary>
    private new bool _isSelecting;

    /// <summary>
    /// The anchor point for selection extension.
    /// </summary>
    private new TextPointer? _selectionAnchor;

    /// <summary>
    /// Whether the current drag gesture should extend selection by whole words.
    /// </summary>
    private new bool _isWordSelecting;

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
    private new double _horizontalOffset;

    /// <summary>
    /// The vertical scroll offset.
    /// </summary>
    private new double _verticalOffset;

    /// <summary>
    /// The undo stack.
    /// </summary>
    private new readonly Stack<DocumentState> _undoStack = new();

    /// <summary>
    /// The redo stack.
    /// </summary>
    private new readonly Stack<DocumentState> _redoStack = new();

    /// <summary>
    /// Whether an undo/redo operation is in progress.
    /// </summary>
    private new bool _isUndoRedoing;
    private DocumentState? _documentChangeBlockStart;
    private DocumentState? _documentChangeBlockNotificationStart;
    private DocumentState? _observedDocumentState;
    private UndoAction _documentChangeBlockUndoAction;

    // Double/Triple click
    private DateTime _lastClickTime;
    private int _clickCount;
    private Point _lastClickPosition;

    // Layout cache
    private FlowDocumentLayoutInfo? _layoutCache;
    private RenderContext? _layoutContext;
    private double _layoutWidth = double.NaN;
    private bool _layoutDirty = true;

    // IME composition state
    private bool _isImeComposing;
    private string _imeCompositionString = string.Empty;
    private int _imeCompositionCursor;
    private int _imeCompositionStart;

    #endregion

    #region Dependency Properties

    /// <summary>Identifies the IsDocumentEnabled dependency property.</summary>
    public static readonly DependencyProperty IsDocumentEnabledProperty =
        DependencyProperty.Register(nameof(IsDocumentEnabled), typeof(bool), typeof(RichTextBox),
            new PropertyMetadata(false, OnIsDocumentEnabledChanged));

    #endregion

    #region CLR Properties

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
            using var change = DeclareChangeBlock();
            ReplaceDocument(value ?? new FlowDocument(), clearHistory: true);
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
                if (value.Parent is null && _document.Blocks.Count != 0 &&
                    value.DocumentOffset == _document.GetText().Length)
                    value = _document.GetPositionAtOffset(value.DocumentOffset - 1, LogicalDirection.Backward) ?? value;
                _caretPosition = value;
                _selectionAnchor = value;
                if (_selection is null or { IsEmpty: true })
                    _selection = new TextSelection(value, value);
                ResetCaretBlink();
                EnsureCaretVisible();
                OnSelectionChanged();
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

    internal override bool CanUndoCore => IsUndoEnabled && UndoLimit != 0 && !IsChangeBlockOpen && _undoStack.Count > 0;

    internal override bool CanRedoCore => IsUndoEnabled && UndoLimit != 0 && !IsChangeBlockOpen && _redoStack.Count > 0;

    internal override double HorizontalOffsetCore
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

    internal override double VerticalOffsetCore
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
    internal bool IsImeComposing => _isImeComposing;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="RichTextBox"/> class.
    /// </summary>
    public RichTextBox()
    {
        _document = new FlowDocument();
        AddLogicalChild(_document);
        _caretPosition = _document.ContentStart;
        _selection = new TextSelection(_document.ContentStart, _document.ContentStart);
        TrackDocumentChanges();

        Focusable = true;
        Cursor = Cursors.IBeam;
        AcceptsReturn = true;
        _lastCaretBlink = DateTime.Now;
        _lastClickTime = DateTime.MinValue;

        Loaded += OnImeOwnerLoaded;
        Unloaded += OnImeOwnerUnloaded;

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

    private void TrackDocumentChanges()
    {
        _documentChangeSubscription?.Dispose();
        _observedDocumentState = SaveDocumentState();
        _documentChangeSubscription = new DocumentChangeSubscription(this, _document);
    }

    private void ReplaceDocument(FlowDocument document, bool clearHistory)
    {
        if (ReferenceEquals(_document, document)) return;
        if (document.Parent is { } parent && !ReferenceEquals(parent, this))
            throw new InvalidOperationException("The FlowDocument already belongs to another logical parent.");
        _documentChangeSubscription?.Dispose();
        RestoreDocumentElementEnabledStates();
        RemoveLogicalChild(_document);
        _document = document;
        AddLogicalChild(_document);
        _caretPosition = _document.ContentStart;
        _selectionAnchor = _caretPosition;
        _selection = new TextSelection(_caretPosition, _caretPosition);
        TrackDocumentChanges();
        _spellCheckedText = null;
        if (clearHistory)
        {
            ClearUndoHistory();
            _documentChangeBlockUndoAction = UndoAction.Clear;
        }
        ApplyDocumentEnabledState();
        InvalidateLayout();
        InvalidateVisual();
    }

    private sealed class DocumentChangeSubscription : IDisposable
    {
        private readonly WeakReference<RichTextBox> _owner;
        private FlowDocument? _document;

        internal DocumentChangeSubscription(RichTextBox owner, FlowDocument document)
        {
            _owner = new(owner);
            _document = document;
            document.ViewerPaginationChanged += Changed;
            document.ContentChanged += ContentChanged;
        }

        private void Changed(object? _, EventArgs __)
        {
            if (_document is not { } document) return;
            if (_owner.TryGetTarget(out var owner) && ReferenceEquals(owner._document, document))
            {
                owner.InvalidateLayout();
                owner.InvalidateVisual();
                if (!owner.IsChangeBlockOpen && !owner._isUndoRedoing)
                    owner._observedDocumentState = owner.SaveDocumentState();
            }
            else Dispose();
        }

        private void ContentChanged(object? _, EventArgs __)
        {
            if (_document is not { } document) return;
            if (_owner.TryGetTarget(out var owner) && ReferenceEquals(owner._document, document))
                owner.OnDocumentContentChanged();
            else Dispose();
        }

        public void Dispose()
        {
            if (_document is not { } document) return;
            _document = null;
            document.ViewerPaginationChanged -= Changed;
            document.ContentChanged -= ContentChanged;
        }
    }

    #endregion

    #region Public Methods

    internal override void SelectAllCore()
    {
        _selection = new TextSelection(_document.ContentStart, _document.ContentEnd);
        _selectionAnchor = _document.ContentStart;
        _caretPosition = _document.ContentEnd;
        UpdateImeWindowIfComposing();
        InvalidateVisual();
        OnSelectionChanged();
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    internal override void ClearSelectionCore()
    {
        if (_caretPosition != null)
        {
            _selection = new TextSelection(_caretPosition, _caretPosition);
            _selectionAnchor = _caretPosition;
            UpdateImeWindowIfComposing();
            InvalidateVisual();
            OnSelectionChanged();
        }
    }

    internal override void CopyCore()
    {
        if (_selection != null && !_selection.IsEmpty)
        {
            WpfClipboard.SetText(_selection.Text);
        }
    }

    internal override void CutCore()
    {
        if (IsReadOnly || _selection == null || _selection.IsEmpty)
            return;

        CopyCore();
        DeleteSelection();
    }

    internal override void PasteCore()
    {
        if (IsReadOnly)
            return;

        var clipboardText = WpfClipboard.GetText();
        if (!string.IsNullOrEmpty(clipboardText))
        {
            InsertText(clipboardText);
        }
    }

    /// <summary>
    /// Undoes the last edit operation.
    /// </summary>
    internal override bool UndoCore()
    {
        if (!IsUndoEnabled || UndoLimit == 0 || IsChangeBlockOpen || _undoStack.Count == 0)
            return false;

        if (!_undoStack.Peek().Snapshot.CanRestore(_document, this))
            return false;

        _isUndoRedoing = true;
        BeginChange();
        _documentChangeBlockUndoAction = UndoAction.Undo;
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
            EndChange();
        }

        InvalidateLayout();
        UpdateImeWindowIfComposing();
        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// Redoes the last undone operation.
    /// </summary>
    internal override bool RedoCore()
    {
        if (!IsUndoEnabled || UndoLimit == 0 || IsChangeBlockOpen || _redoStack.Count == 0)
            return false;

        if (!_redoStack.Peek().Snapshot.CanRestore(_document, this))
            return false;

        _isUndoRedoing = true;
        BeginChange();
        _documentChangeBlockUndoAction = UndoAction.Redo;
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
            EndChange();
        }

        InvalidateLayout();
        UpdateImeWindowIfComposing();
        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// Gets all text content as plain text.
    /// </summary>
    /// <returns>The plain text content of the document.</returns>
    protected override string GetText()
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
    internal void LoadFromFile(string path, System.Text.Encoding? encoding = null)
        => SetText(TextFile.ReadAllText(path, encoding));

    /// <summary>
    /// Writes the document's plain text to a file using <paramref name="encoding"/>
    /// — UTF-8 when it is <see langword="null"/>.
    /// </summary>
    internal void SaveToFile(string path, System.Text.Encoding? encoding = null)
        => TextFile.WriteAllText(path, GetText(), encoding);

    /// <summary>
    /// Sets the document content from plain text.
    /// </summary>
    /// <param name="text">The plain text to set.</param>
    protected override void SetText(string text)
    {
        using var change = DeclareChangeBlock();
        PushUndo();
        ReplaceDocument(FlowDocument.FromText(text), clearHistory: false);
        _caretPosition = _document.ContentEnd;
        _selection = new TextSelection(_document.ContentStart, _document.ContentStart);
        _selectionAnchor = _document.ContentStart;
        _spellCheckedText = null;
        ApplyDocumentEnabledState();
        InvalidateLayout();
        UpdateImeWindowIfComposing();
        InvalidateVisual();
        OnSelectionChanged();
    }

    internal string GetPlainText() => GetText();

    internal void SetPlainText(string text) => SetText(text);

    /// <inheritdoc />
    protected override double GetLineHeight()
    {
        var fontFamily = ResolveNativeRunFamily(null);
        var fontSize = _document.FontSize;
        return TextMeasurement.GetFontMetrics(fontFamily, fontSize).LineHeight;
    }

    /// <inheritdoc />
    protected override double MeasureTextWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var fontFamily = ResolveNativeRunFamily(null);
        var fontSize = _document.FontSize;
        var formattedText = new FormattedText(text, fontFamily, fontSize)
        {
            FontWeight = FontWeight.ToOpenTypeWeight(),
            FontStyle = FontStyle.ToOpenTypeStyle(),
            FontStretch = FontStretch.ToOpenTypeStretch()
        };

        return TextMeasurement.MeasureText(formattedText) && formattedText.IsMeasured
            ? formattedText.Width
            : text.Length * fontSize * 0.6;
    }

    /// <inheritdoc />
    protected override int GetLineCount() => GetPlainTextLines().Count;

    /// <inheritdoc />
    protected override (int lineIndex, int columnIndex) GetLineColumnFromCharIndex(int charIndex)
    {
        string text = GetText();
        int index = Math.Clamp(charIndex, 0, text.Length);
        var lines = GetPlainTextLines();
        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            if (index <= line.Start + line.Length)
                return (lineIndex, index - line.Start);

            if (lineIndex + 1 < lines.Count && index < lines[lineIndex + 1].Start)
                return (lineIndex, line.Length);
        }

        var last = lines[^1];
        return (lines.Count - 1, last.Length);
    }

    /// <inheritdoc />
    protected override int GetCharIndexFromLineColumn(int lineIndex, int columnIndex)
    {
        var lines = GetPlainTextLines();
        if ((uint)lineIndex >= (uint)lines.Count)
            return GetText().Length;

        var line = lines[lineIndex];
        return line.Start + Math.Clamp(columnIndex, 0, line.Length);
    }

    /// <inheritdoc />
    protected override string GetLineTextInternal(int lineIndex)
    {
        var lines = GetPlainTextLines();
        if ((uint)lineIndex >= (uint)lines.Count)
            return string.Empty;

        var line = lines[lineIndex];
        return GetText().Substring(line.Start, line.Length);
    }

    /// <inheritdoc />
    protected override int GetLineStartIndex(int lineIndex)
    {
        var lines = GetPlainTextLines();
        return (uint)lineIndex < (uint)lines.Count ? lines[lineIndex].Start : 0;
    }

    /// <inheritdoc />
    protected override int GetLineLengthInternal(int lineIndex)
    {
        var lines = GetPlainTextLines();
        return (uint)lineIndex < (uint)lines.Count ? lines[lineIndex].Length : 0;
    }

    /// <inheritdoc />
    internal override void RenderTextContent(DrawingContext drawingContext)
    {
        // RichTextBox owns its document renderer. The template content host is
        // transparent; the document is painted by OnRender to keep TextPointer
        // layout, embedded elements, selection, IME and caret in one pass.
    }

    private List<(int Start, int Length)> GetPlainTextLines()
    {
        string text = GetText();
        var lines = new List<(int Start, int Length)>();
        int start = 0;

        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] is not ('\r' or '\n'))
                continue;

            lines.Add((start, index - start));
            if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                index++;
            start = index + 1;
        }

        lines.Add((start, text.Length - start));
        return lines;
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
        if (!SpellCheck.GetIsEnabled(this) || SpellChecker.Default is not { IsAvailable: true } checker || text.Length == 0)
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

    internal void OnSpellCheckSettingChanged(bool isEnabled)
    {
        _spellCheckedText = null;
        _spellingErrors.Clear();
        if (isEnabled)
        {
            EnsureSpellingErrors();
        }

        InvalidateVisual();
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
    internal void ToggleBold()
    {
        if (IsReadOnly || _selection == null || _selection.IsEmpty)
            return;

        using var change = DeclareChangeBlock();
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
    internal void ToggleItalic()
    {
        if (IsReadOnly || _selection == null || _selection.IsEmpty)
            return;

        using var change = DeclareChangeBlock();
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
    internal void ToggleUnderline()
    {
        if (IsReadOnly || _selection == null || _selection.IsEmpty)
            return;

        using var change = DeclareChangeBlock();
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
    internal void SetFontFamily(string fontFamily)
    {
        if (IsReadOnly || _selection == null || _selection.IsEmpty)
            return;

        using var change = DeclareChangeBlock();
        PushUndo();
        _selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(fontFamily));
        InvalidateLayout();
        InvalidateVisual();
    }

    /// <summary>
    /// Sets the font size on the current selection.
    /// </summary>
    /// <param name="fontSize">The font size.</param>
    internal void SetFontSize(double fontSize)
    {
        if (IsReadOnly || _selection == null || _selection.IsEmpty)
            return;

        using var change = DeclareChangeBlock();
        PushUndo();
        _selection.ApplyPropertyValue(TextElement.FontSizeProperty, fontSize);
        InvalidateLayout();
        InvalidateVisual();
    }

    /// <summary>
    /// Sets the foreground color on the current selection.
    /// </summary>
    /// <param name="brush">The foreground brush.</param>
    internal void SetForeground(Brush brush)
    {
        if (IsReadOnly || _selection == null || _selection.IsEmpty)
            return;

        using var change = DeclareChangeBlock();
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
        if (IsReadOnly) return;
        using var change = DeclareChangeBlock();
        if (_selection is { IsEmpty: false }) DeleteSelectionInternal();
        if (_document.Blocks.Count == 0)
            _document.Blocks.Add(new Paragraph());
        int start = _selection?.Start.DocumentOffset ?? _caretPosition?.DocumentOffset ?? 0;
        TryReplaceImeText(start, 0, "\n");
    }

    /// <summary>
    /// Inserts text at the current caret position.
    /// </summary>
    protected override void InsertText(string textToInsert)
    {
        if (IsReadOnly || string.IsNullOrEmpty(textToInsert)) return;
        int start = _selection?.Start.DocumentOffset ?? _caretPosition?.DocumentOffset ?? 0;
        int length = _selection is { IsEmpty: false } ? _selection.End.DocumentOffset - start : 0;
        TryReplaceImeText(start, length, textToInsert);
    }

    /// <summary>
    /// Deletes the current selection.
    /// </summary>
    protected override void DeleteSelection()
    {
        if (_selection == null || _selection.IsEmpty)
            return;

        using var change = DeclareChangeBlock();
        PushUndo();
        DeleteSelectionInternal();
        InvalidateLayout();
        InvalidateVisual();
    }

    private new void DeleteSelectionInternal()
    {
        if (_selection == null || _selection.IsEmpty)
            return;

        int start = _selection.Start.DocumentOffset;
        // A whole Run can be removed by TextRange. Resolve a fresh position in
        // the live graph rather than retaining its now detached start pointer.
        _selection.Text = string.Empty;

        // Update caret to selection start
        _caretPosition = _document.GetPositionAtOffset(start, LogicalDirection.Forward) ?? _document.ContentEnd;
        _selection = new TextSelection(_caretPosition, _caretPosition);
        _selectionAnchor = _caretPosition;

        UpdateImeWindowIfComposing();
        OnSelectionChanged();
    }

    /// <summary>
    /// Pushes the current state to the undo stack.
    /// </summary>
    protected new void PushUndo()
    {
        if (!IsUndoEnabled || UndoLimit == 0 || _isUndoRedoing)
            return;

        if (IsChangeBlockOpen)
        {
            // The outer boundary compares the graph before committing history.
            // An identical replacement must preserve the existing redo queue.
            return;
        }
        PushDocumentUndo(SaveDocumentState());
    }

    protected override void OnChangeBlockStarted()
    {
        base.OnChangeBlockStarted();
        _documentChangeBlockNotificationStart = SaveDocumentState();
        _documentChangeBlockStart = IsUndoEnabled && UndoLimit != 0 && !_isUndoRedoing
            ? _documentChangeBlockNotificationStart : null;
        _documentChangeBlockUndoAction = _isUndoRedoing ? UndoAction.None
            : IsUndoEnabled && UndoLimit != 0 ? UndoAction.Create : UndoAction.None;
    }

    protected override void OnChangeBlockEnded()
    {
        var start = _documentChangeBlockStart;
        var notificationStart = _documentChangeBlockNotificationStart;
        var action = _documentChangeBlockUndoAction;
        _documentChangeBlockStart = null;
        _documentChangeBlockNotificationStart = null;
        if (action != UndoAction.Undo && action != UndoAction.Redo && IsUndoEnabled && UndoLimit != 0 && start is not null &&
            (!ReferenceEquals(start.Snapshot.Document, _document) || start.Snapshot.HasChanges))
            PushDocumentUndo(start);
        TextChangedEventArgs? changed = notificationStart is not null &&
            (!ReferenceEquals(notificationStart.Snapshot.Document, _document) || notificationStart.Snapshot.HasChanges)
            ? CreateDocumentChangedEvent(notificationStart.Text, _document.GetText(), action) : null;
        if (changed is not null && notificationStart is not null &&
            ReferenceEquals(_caretPosition, notificationStart.Caret))
            RebindDocumentPositions(notificationStart, _document.GetText(), changed.Changes.FirstOrDefault());
        _observedDocumentState = SaveDocumentState();
        if (changed is not null && notificationStart is not null)
        {
            base.OnTextChanged(changed);
            if (notificationStart.CaretOffset != _observedDocumentState.CaretOffset ||
                notificationStart.AnchorOffset != _observedDocumentState.AnchorOffset ||
                notificationStart.MovingOffset != _observedDocumentState.MovingOffset)
                OnSelectionChanged();
        }
        base.OnChangeBlockEnded();
    }

    protected override void ClearUndoHistory()
    {
        base.ClearUndoHistory();
        _undoStack.Clear();
        _redoStack.Clear();
        _documentChangeBlockStart = IsChangeBlockOpen && IsUndoEnabled && UndoLimit != 0 ? SaveDocumentState() : null;
    }

    private void PushDocumentUndo(DocumentState state)
    {
        _undoStack.Push(state);
        _redoStack.Clear();

        // Limit stack size
        while (UndoLimit >= 0 && _undoStack.Count > UndoLimit)
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
        var caret = _caretPosition ?? _document.ContentStart;
        return new DocumentState(
            DocumentSnapshot.Capture(_document), caret,
            _selection?.AnchorPosition ?? caret,
            _selection?.MovingPosition ?? caret,
            _selectionAnchor ?? caret, _document.GetText());
    }

    protected override void OnSelectionChanged()
    {
        if (!IsChangeBlockOpen && !_isUndoRedoing && _observedDocumentState is { } observed &&
            ReferenceEquals(observed.Snapshot.Document, _document) &&
            (!observed.Snapshot.HasChanges || observed.Text == _document.GetText()))
        {
            // Writing the same effective typography value can add a local
            // value without a content notification. It must not leave stale
            // selection positions in the next formatting undo snapshot.
            var caret = _caretPosition ?? _document.ContentStart;
            _observedDocumentState = new DocumentState(observed.Snapshot, caret,
                _selection?.AnchorPosition ?? caret, _selection?.MovingPosition ?? caret,
                _selectionAnchor ?? caret, observed.Text);
        }
        if (!IsChangeBlockOpen)
        {
            UpdateImeWindowIfComposing();
            RefreshLinuxImeContext();
        }
        base.OnSelectionChanged();
    }

    private void OnDocumentContentChanged()
    {
        InvalidateLayout();
        InvalidateVisual();
        if (IsChangeBlockOpen || _isUndoRedoing || _observedDocumentState is not { } previous ||
            !previous.Snapshot.HasChanges) return;
        using var change = DeclareChangeBlock();
        var text = _document.GetText();
        var args = CreateDocumentChangedEvent(previous.Text, text,
            IsUndoEnabled && UndoLimit != 0 ? UndoAction.Create : UndoAction.None);
        RebindDocumentPositions(previous, text, args.Changes.FirstOrDefault());
        if (previous.CaretOffset != _caretPosition?.DocumentOffset || previous.AnchorOffset != _selection?.AnchorPosition.DocumentOffset ||
            previous.MovingOffset != _selection?.MovingPosition.DocumentOffset)
            OnSelectionChanged();
        if (IsUndoEnabled && UndoLimit != 0) PushDocumentUndo(previous);
        _observedDocumentState = SaveDocumentState();
        base.OnTextChanged(args);
    }

    private void RebindDocumentPositions(DocumentState previous, string text, TextChange? difference)
    {
        TextPointer Map(int offset, LogicalDirection direction)
        {
            int mapped = offset;
            if (difference is not null)
            {
                int end = difference.Offset + difference.RemovedLength;
                mapped = offset < difference.Offset ? offset
                    : offset >= end ? offset + difference.AddedLength - difference.RemovedLength
                    : difference.Offset + Math.Min(offset - difference.Offset, difference.AddedLength);
            }
            mapped = ImeTextEncoding.SnapToGraphemeBoundary(text, mapped, direction == LogicalDirection.Forward);
            return _document.GetPositionAtOffset(mapped, direction) ?? _document.ContentEnd;
        }
        _caretPosition = Map(previous.CaretOffset, previous.Caret.LogicalDirection);
        _selection = new TextSelection(Map(previous.AnchorOffset, previous.SelectionAnchor.LogicalDirection),
            Map(previous.MovingOffset, previous.SelectionMoving.LogicalDirection));
        _selectionAnchor = Map(previous.CaretAnchorOffset, previous.CaretAnchor.LogicalDirection);
    }

    private TextChangedEventArgs CreateDocumentChangedEvent(string before, string after, UndoAction action)
    {
        ICollection<TextChange> changes = Array.Empty<TextChange>();
        if (before != after)
        {
            int prefix = 0;
            while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix]) prefix++;
            int suffix = 0;
            while (suffix < before.Length - prefix && suffix < after.Length - prefix &&
                before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix]) suffix++;
            changes = new[] { new TextChange { Offset = prefix, RemovedLength = before.Length - prefix - suffix,
                AddedLength = after.Length - prefix - suffix } };
        }
        return new TextChangedEventArgs(TextChangedEvent, action, changes) { Source = this };
    }

    private void RestoreDocumentState(DocumentState state)
    {
        RestoreDocumentElementEnabledStates();
        state.Snapshot.Restore(_document);
        ReplaceDocument(state.Snapshot.Document, clearHistory: false);
        // TextPointer is immutable. Its original parent and affinity remain
        // valid after restoring the original graph, including bidi boundaries.
        _caretPosition = state.Caret;
        _selection = new TextSelection(state.SelectionAnchor, state.SelectionMoving);
        _selectionAnchor = state.CaretAnchor;
        _spellCheckedText = null;
        ApplyDocumentEnabledState();
        OnSelectionChanged();
        ResetCaretBlink();
    }

    /// <summary>
    /// Resets the caret blink state.
    /// </summary>
    protected new void ResetCaretBlink()
    {
        _caretVisible = true;
        _caretOpacity = 1.0;
        _lastCaretBlink = DateTime.Now;

        if (_caretTimer is { IsEnabled: true })
        {
            ScheduleNextCaretTick(_lastCaretBlink);
        }

        RefreshLinuxImeContext();
    }

    /// <summary>
    /// Ensures the caret is visible by scrolling if necessary.
    /// </summary>
    protected override void EnsureCaretVisible()
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

        var lineHeight = GetCaretLineHeight();
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
    /// Invalidates the layout cache.
    /// </summary>
    protected void InvalidateLayout()
    {
        _layoutDirty = true;
        _layoutCache?.Dispose();
        _layoutCache = null;
        RefreshLinuxImeContext();
    }

    internal override void OnFontResourcesChanged()
    {
        InvalidateLayout();
        base.OnFontResourcesChanged();
    }

    private void RefreshLinuxImeContext()
    {
        for (Visual? current = this; current != null; current = current.VisualParent)
        {
            if (current is Window window)
            {
                window.RefreshLinuxImeContext();
                break;
            }
        }
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
            if (!CssBackgroundPainter.TryDraw(this, BackgroundProperty, Background, dc,
                    bounds, default, BorderThickness, Padding,
                    brush => dc.DrawRectangle(brush, null, bounds)))
                dc.DrawRectangle(Background, null, bounds);
        }

        // Draw border
        if (CssBorderPaintProperties.Get(this) is null && BorderBrush != null && BorderThickness.Left > 0)
        {
            dc.DrawRectangle(null, _borderPen.Get(BorderBrush, BorderThickness.Left), bounds);
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

    /// <inheritdoc />
    protected override void OnPostRender(DrawingContext drawingContext)
    {
        base.OnPostRender(drawingContext);
        CssBorderAdornment.Draw(this, drawingContext, ref _cssBorderPainter);
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
        var pad = Math.Max(1, lineLayout.Height);
        var bounds = new Rect(x - pad, y - pad,
            Math.Max(1, lineLayout.Width) + 2 * pad, lineLayout.Height + 2 * pad);
        void DrawLineContent()
        {
            if (lineLayout.NativeLine is { } nativeLine)
            {
                var range = nativeLine.Metrics.Line;
                var formatted = new FormattedText(nativeLine.Paragraph.Text.Substring((int)range.TextPosition, (int)range.Length),
                    _document.FontFamily ?? FrameworkElement.DefaultFontFamilyName, _document.FontSize)
                {
                    PlatformTextLine = nativeLine,
                    Foreground = ResolveDocumentForegroundBrush(),
                    MaxTextWidth = Math.Max(1, lineLayout.Width),
                    MaxTextHeight = Math.Max(1, lineLayout.Height)
                };
                dc.DrawText(formatted, new Point(x, y));
            }
            Dictionary<TextElement, DecorationSegment>? decorations = null;
            foreach (var runLayout in lineLayout.Runs)
            {
                if (runLayout.Run != null)
                {
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

                    var runX = x + runLayout.X;
                    if (lineLayout.NativeLine is null)
                    {
                        var formattedText = new FormattedText(runLayout.Run.Text, fontFamily, fontSize)
                        {
                            Foreground = foreground,
                            FontWeight = fontWeight.ToOpenTypeWeight(),
                            FontStyle = fontStyle.ToOpenTypeStyle()
                        };
                        dc.DrawText(formattedText, new Point(runX, y));
                    }
                    CollectRunTextDecorations(runLayout, runX, fontFamily, fontSize,
                        fontWeight, fontStyle, ref decorations);
                }
            }
            if (decorations is not null)
                foreach (var segment in decorations.Values.OrderBy(static item => item.Depth))
                    DrawTextDecorationSegment(dc, segment, lineLayout.NativeLine is null
                        ? y : y + lineLayout.Baseline - segment.Ascent);
        }
        if (TryDrawDocumentTextShadows(dc, lineLayout, x, y, bounds,
                DrawLineContent))
            return;
        var shadowCapture = CssTextShadowPainter.Begin(dc, this, bounds);
        try { DrawLineContent(); }
        finally
        {
            shadowCapture?.End();
        }
    }

    private bool TryDrawDocumentTextShadows(DrawingContext context,
        LineLayoutInfo line, double x, double y, Rect bounds, Action drawLine)
    {
        if (context is not IEffectDrawingContext
                { IsElementEffectCaptureEnabled: true, SupportsCssTextShadowsOnly: true } ||
            context is not IOffsetDrawingContext)
            return false;

        var rootEffect = CssTextShadowProperties.Value(this);
        var segments = new List<(double Start, double End, Effect? Effect)>();
        var hasOverride = false;
        void Add(double start, double end, Effect? effect)
        {
            if (end <= start) return;
            hasOverride |= !ReferenceEquals(effect, rootEffect);
            if (segments.Count > 0 && segments[^1] is var previous &&
                Math.Abs(previous.End - start) < 0.001 &&
                ReferenceEquals(previous.Effect, effect))
                segments[^1] = (previous.Start, end, effect);
            else
                segments.Add((start, end, effect));
        }

        var cursor = x;
        foreach (var run in line.Runs)
        {
            var start = x + run.X;
            var end = start + run.Width;
            Add(cursor, start, rootEffect);
            Add(start, end, run.Run is { } source
                ? ResolveRunTextShadow(source, rootEffect) : rootEffect);
            cursor = end;
        }
        Add(cursor, x + line.Width, rootEffect);
        if (!hasOverride) return false;

        var pad = Math.Max(1, line.Height);
        foreach (var segment in segments)
        {
            if (segment.Effect?.HasEffect != true) continue;
            var capture = CssTextShadowPainter.Begin(context, segment.Effect,
                bounds, shadowsOnly: true);
            if (capture is null) continue;
            context.PushClip(new RectangleGeometry(new Rect(segment.Start,
                y - pad, segment.End - segment.Start, 3 * pad)));
            try { drawLine(); }
            finally
            {
                context.Pop();
                capture.Value.End();
            }
        }
        drawLine();
        return true;
    }

    private Effect? ResolveRunTextShadow(Run run, Effect? controlEffect)
    {
        // FlowDocument is rendered by RichTextBox but has no inheritance parent
        // pointing at the control. Preserve explicit document/inline `none`;
        // otherwise the control's inherited CSS shadow is the effective value.
        for (TextElement? source = run; source is not null; source = source.Parent)
            if (source.GetEffectiveValueLayer(CssTextShadowProperties.ValueProperty) is not null)
                return CssTextShadowProperties.Value(source);
        var documentEffect = CssTextShadowProperties.Value(_document);
        if (documentEffect is not null ||
            _document.GetEffectiveValueLayer(CssTextShadowProperties.ValueProperty) is not null)
            return documentEffect;
        return controlEffect;
    }

    private static void CollectRunTextDecorations(RunLayoutInfo layout, double x,
        string fontFamily, double fontSize, FontWeight fontWeight, FontStyle fontStyle,
        ref Dictionary<TextElement, DecorationSegment>? segments)
    {
        if (layout.Run is not { } run || layout.Width <= 0) return;

        var ascent = double.NaN;
        var descent = double.NaN;
        for (TextElement? source = run; source is not null; source = source.Parent as TextElement)
        {
            var cssOwnsDecorations = source.GetEffectiveValueLayer(TextElement.TextDecorationsProperty) is
                (DependencyValueStore.Layer.CssBase or DependencyValueStore.Layer.CssState);
            var cssLines = cssOwnsDecorations
                ? CssTextDecorationProperties.Line(source) : CssTextDecorationLine.None;
            var native = cssOwnsDecorations
                ? null : source.GetValue(TextElement.TextDecorationsProperty) as TextDecorationCollection;
            // An explicit CSS `none` starts no line and leaves ancestor lines intact.
            if (cssLines == CssTextDecorationLine.None && native is not { Count: > 0 }) continue;

            if (double.IsNaN(ascent))
            {
                var metrics = TextMeasurement.GetFontMetrics(fontFamily, fontSize,
                    fontWeight.ToOpenTypeWeight(), fontStyle.ToOpenTypeStyle());
                ascent = metrics.Ascent > 0 ? metrics.Ascent : fontSize * 0.8;
                descent = metrics.Descent > 0 ? metrics.Descent : fontSize * 0.2;
            }
            segments ??= new(ReferenceEqualityComparer.Instance);
            if (!segments.TryGetValue(source, out var segment))
            {
                var depth = 0;
                for (var parent = source.Parent as TextElement; parent is not null;
                    parent = parent.Parent as TextElement) depth++;
                segment = new DecorationSegment(source, cssLines, native, x, x + layout.Width,
                    fontSize, ascent, descent, depth, layout.StartOffset, layout.EndOffset);
                segments.Add(source, segment);
            }
            else
            {
                segment.StartX = Math.Min(segment.StartX, x);
                segment.EndX = Math.Max(segment.EndX, x + layout.Width);
                segment.FontSize = Math.Max(segment.FontSize, fontSize);
                segment.Ascent = Math.Max(segment.Ascent, ascent);
                segment.Descent = Math.Max(segment.Descent, descent);
                segment.StartOffset = Math.Min(segment.StartOffset, layout.StartOffset);
                segment.EndOffset = Math.Max(segment.EndOffset, layout.EndOffset);
            }
        }
    }

    private void DrawTextDecorationSegment(DrawingContext context, DecorationSegment segment, double lineTop)
    {
        var source = segment.Source;
        var foreground = source.GetEffectiveForeground();
        var baseline = lineTop + segment.Ascent;
        if (segment.CssLines != CssTextDecorationLine.None)
        {
            var brush = CssTextDecorationProperties.Color(source) ?? foreground;
            DecorationFragmentMetrics? fragments = null;
            _layoutCache?.DecorationFragments.TryGetValue(source, out fragments);
            var inlineSizeBefore = -1.0;
            var inlineSizeAfter = -1.0;
            if (fragments is not null)
            {
                if (fragments.BeforeOffset.TryGetValue(segment.StartOffset, out var before))
                    inlineSizeBefore = before;
                if (fragments.ThroughOffset.TryGetValue(segment.EndOffset, out var through))
                    inlineSizeAfter = Math.Max(0, fragments.InlineSize - through);
            }
            CssTextDecorationPainter.Draw(context, source, this, brush, segment.CssLines,
                segment.StartX, segment.EndX, lineTop, baseline, segment.FontSize,
                baseline + segment.Descent, fragments?.InlineSize ?? -1,
                fragments is null || segment.StartOffset == fragments.FirstOffset,
                fragments is null || segment.EndOffset == fragments.LastOffset,
                inlineSizeBefore, inlineSizeAfter);
            return;
        }

        foreach (var decoration in segment.Native!)
        {
            var brush = decoration.Brush ?? foreground;
            var thickness = decoration.Thickness > 0 ? decoration.Thickness : 1;
            var offset = decoration.OffsetUnit == TextDecorationUnit.Pixel
                ? decoration.Offset : decoration.Offset * segment.FontSize;
            var lineY = decoration.Location switch
            {
                TextDecorationLocation.OverLine => lineTop + offset,
                TextDecorationLocation.Strikethrough => lineTop + segment.Ascent * 0.55 + offset,
                TextDecorationLocation.Baseline => baseline + offset,
                _ => baseline + Math.Max(1, segment.FontSize * 0.08) + offset,
            };
            context.DrawLine(new Pen(brush, thickness), new Point(segment.StartX, lineY),
                new Point(segment.EndX, lineY));
        }
    }

    private sealed class DecorationSegment(
        TextElement source, CssTextDecorationLine cssLines, TextDecorationCollection? native,
        double startX, double endX, double fontSize, double ascent, double descent, int depth,
        int startOffset, int endOffset)
    {
        internal TextElement Source { get; } = source;
        internal CssTextDecorationLine CssLines { get; } = cssLines;
        internal TextDecorationCollection? Native { get; } = native;
        internal double StartX = startX;
        internal double EndX = endX;
        internal double FontSize = fontSize;
        internal double Ascent = ascent;
        internal double Descent = descent;
        internal int Depth { get; } = depth;
        internal int StartOffset = startOffset;
        internal int EndOffset = endOffset;
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
            if (lineLayout.NativeLine is { } nativeLine)
            {
                if (y + lineLayout.Height > contentBounds.Top && y < contentBounds.Bottom)
                {
                    int start = Math.Clamp(selStart - lineLayout.ParagraphOffset, 0, nativeLine.Paragraph.Text.Length);
                    int end = Math.Clamp(selEnd - lineLayout.ParagraphOffset, start, nativeLine.Paragraph.Text.Length);
                    foreach (var rectangle in nativeLine.Paragraph.Selection(nativeLine.Index, start, end - start))
                        dc.DrawRectangle(selBrush, null, new Rect(x + rectangle.X, y, rectangle.Width, lineLayout.Height));
                }
                y += lineLayout.Height;
                continue;
            }
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

    private double GetXOffsetInLine(LineLayoutInfo lineLayout, int targetOffset, bool trailing = false, bool backward = false)
    {
        if (lineLayout.NativeLine is { } nativeLine)
        {
            int offset = targetOffset - lineLayout.ParagraphOffset;
            if (trailing)
            {
                offset = GraphemeClusters.NextBoundary(nativeLine.Paragraph.Text, offset);
                backward = true;
            }
            offset = Math.Clamp(offset, (int)nativeLine.Metrics.Line.TextPosition,
                (int)(nativeLine.Metrics.Line.TextPosition + nativeLine.Metrics.Line.Length));
            return nativeLine.Paragraph.Caret(nativeLine.Index, offset, backward).X;
        }
        foreach (var runLayout in lineLayout.Runs)
        {
            if (targetOffset >= runLayout.StartOffset &&
                (targetOffset < runLayout.EndOffset || targetOffset == lineLayout.EndOffset && targetOffset == runLayout.EndOffset) &&
                runLayout.Run is { } run)
            {
                int offset = Math.Clamp(targetOffset - runLayout.StartOffset, 0, run.Text.Length);
                string family = run.FontFamily?.Source ?? _document.FontFamily ?? FrameworkElement.DefaultFontFamilyName;
                double size = run.FontSize > 0 ? run.FontSize : _document.FontSize;
                if (run.Text.Length > 0 && TextMeasurement.HitTestTextPositionWrapped(run.Text, family, size,
                    run.FontWeight.ToOpenTypeWeight(), run.FontStyle.ToOpenTypeStyle(), 100000f, (uint)offset, trailing, out var hit))
                    return runLayout.X + hit.CaretX;
                if (trailing) offset = GraphemeClusters.NextBoundary(run.Text, offset);
                return runLayout.X + MeasureText(run.Text[..offset], run);
            }
        }

        // If past all runs, return line width
        return lineLayout.Width;
    }

    private void RenderCaret(DrawingContext dc, Rect contentBounds)
    {
        if (_isImeComposing)
            return;

        UpdateRichCaretAnimation();

        if (_caretOpacity < 0.01)
            return;

        var caretPos = GetCaretScreenPosition(contentBounds);
        if (caretPos == null)
            return;

        var lineHeight = GetCaretLineHeight();
        var caretBrush = ResolveCaretBrush();
        if (caretBrush == null)
            return;

        // Apply opacity for animation
        caretBrush = ApplyCaretFade(caretBrush, _caretOpacity);

        var caretShape = CssCaretShapeProperties.Get(this);
        if (caretShape != CssCaretShape.Auto)
        {
            var text = GetText();
            var index = Math.Clamp(_caretPosition!.DocumentOffset, 0, text.Length);
            double CaretXAt(int offset)
            {
                var position = _document.GetPositionAtOffset(offset, LogicalDirection.Forward);
                var point = GetCaretScreenPosition(contentBounds, position);
                return point is { } value && Math.Abs(value.Y - caretPos.Value.Y) < 1
                    ? value.X : double.NaN;
            }

            var advance = CssCaretPainter.NextAdvance(text, index, CaretXAt,
                Math.Max(1, _document.FontSize * 0.6));
            _lastRenderedCaretRect = CssCaretPainter.Draw(dc, caretBrush, caretShape,
                caretPos.Value.X, caretPos.Value.Y, lineHeight, advance, 2);
            return;
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
            var result = FindPositionInBlock(blockLayout, offset, position.LogicalDirection, x, ref y);
            if (result != null)
                return result;
        }

        return new Point(contentBounds.Left, contentBounds.Top);
    }

    private Point? FindPositionInBlock(BlockLayoutInfo blockLayout, int targetOffset,
        LogicalDirection direction, double x, ref double y)
    {
        x += blockLayout.Margin.Left;

        for (int i = 0; i < blockLayout.Lines.Count; i++)
        {
            var lineLayout = blockLayout.Lines[i];
            bool nextRow = direction == LogicalDirection.Forward && targetOffset == lineLayout.EndOffset &&
                i + 1 < blockLayout.Lines.Count && blockLayout.Lines[i + 1].StartOffset == targetOffset;
            if (targetOffset >= lineLayout.StartOffset && targetOffset <= lineLayout.EndOffset && !nextRow)
            {
                // Found the line containing the offset
                var lineX = x;
                foreach (var runLayout in lineLayout.Runs)
                {
                    if (targetOffset >= runLayout.StartOffset && targetOffset <= runLayout.EndOffset)
                    {
                        // Found the run containing the offset
                        return new Point(lineX + GetXOffsetInLine(lineLayout, targetOffset,
                            backward: direction == LogicalDirection.Backward), y);
                    }
                }
                return new Point(lineX + lineLayout.Width, y);
            }
            y += lineLayout.Height;
        }

        foreach (var childLayout in blockLayout.ChildBlocks)
        {
            var result = FindPositionInBlock(childLayout, targetOffset, direction, x, ref y);
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

    private double GetCaretLineHeight()
    {
        if (_caretPosition is null) return GetDefaultLineHeight();
        var layout = EnsureLayout(GetContentBounds().Width);
        if (layout is null) return GetDefaultLineHeight();
        var lines = new List<(LineLayoutInfo line, double y, double x)>();
        double y = 0;
        CollectAllLines(layout.Blocks, 0, ref y, lines);
        int offset = _caretPosition.DocumentOffset;
        for (int i = 0; i < lines.Count; ++i)
        {
            var line = lines[i].line;
            if (offset < line.StartOffset || offset > line.EndOffset) continue;
            if (offset == line.EndOffset && _caretPosition.LogicalDirection == LogicalDirection.Forward &&
                i + 1 < lines.Count && lines[i + 1].line.StartOffset == offset) continue;
            return line.Height;
        }
        return GetDefaultLineHeight();
    }

    private double UpdateRichCaretAnimation()
    {
        if (CssCaretAnimationProperties.IsManual(this))
        {
            _caretOpacity = 1.0;
            _caretVisible = true;
            return 1.0;
        }

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

    private new Brush? ResolveSelectionBrush()
    {
        if (HasLocalValue(SelectionBrushProperty))
            return SelectionBrush;

        return SelectionBrush
            ?? ResolveThemeBrush("SelectionBackground", s_defaultSelectionBrush, "AccentFillColorSelectedTextBackgroundBrush");
    }

    /// <summary>
    /// 把光标画刷按 <paramref name="opacity"/> 淡化。淡变期间每一帧 alpha 都不同，但这里
    /// 复用同一支画刷实例只改颜色——渲染后端那份原生画刷缓存按实例身份键控，每帧新建会
    /// 让它每帧落空、无休止地新增条目并触发 LRU 把别的控件挤掉。
    /// 每次绘制只改一次色、只画一次，所以录制型 DrawingContext 回放时拿到的也是本帧应有的颜色。
    /// </summary>
    private Brush ApplyCaretFade(Brush caretBrush, double opacity)
    {
        if (opacity >= 1.0 || caretBrush is not SolidColorBrush solidBrush)
            return caretBrush;

        var color = solidBrush.Color;
        var faded = Color.FromArgb((byte)(color.A * opacity), color.R, color.G, color.B);

        var fadeBrush = _caretFadeBrush;
        if (fadeBrush is null || fadeBrush.IsFrozen)
        {
            fadeBrush = new SolidColorBrush(faded);
            _caretFadeBrush = fadeBrush;
        }
        else
        {
            fadeBrush.Color = faded;
        }

        return fadeBrush;
    }

    private new Brush? ResolveCaretBrush()
    {
        if (HasLocalValue(CaretBrushProperty))
            return CaretBrush;

        return CssCaretColorProperties.Get(this)
            ?? CaretBrush
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
        if (!_layoutDirty && _layoutCache != null && _layoutWidth == maxWidth &&
            ReferenceEquals(_layoutContext, RenderContext.Current) && NativePresentationIsCurrent(_layoutCache.Blocks))
            return _layoutCache;

        _layoutCache?.Dispose();
        _layoutCache = LayoutDocument(maxWidth);
        _layoutContext = RenderContext.Current;
        _layoutWidth = maxWidth;
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

        foreach (var block in layout.Blocks)
            CollectDecorationFragments(block, layout.DecorationFragments);

        var lines = new List<(LineLayoutInfo line, double y, double x)>();
        double height = 0;
        CollectAllLines(layout.Blocks, 0, ref height, lines);
        layout.TotalHeight = height;
        foreach (var line in lines)
            layout.TotalWidth = Math.Max(layout.TotalWidth, line.x + line.line.Width);

        return layout;
    }

    private static void CollectDecorationFragments(BlockLayoutInfo block,
        Dictionary<TextElement, DecorationFragmentMetrics> fragments)
    {
        foreach (var line in block.Lines)
            foreach (var layout in line.Runs)
            {
                if (layout.Run is not { } run || layout.Width <= 0) continue;
                for (TextElement? source = run; source is not null; source = source.Parent)
                {
                    if (CssTextDecorationProperties.Inset(source) == CssTextDecorationInset.Zero)
                        continue;
                    if (!fragments.TryGetValue(source, out var metrics))
                    {
                        metrics = new DecorationFragmentMetrics(layout.StartOffset, layout.EndOffset);
                        fragments.Add(source, metrics);
                    }
                    else
                    {
                        metrics.FirstOffset = Math.Min(metrics.FirstOffset, layout.StartOffset);
                        metrics.LastOffset = Math.Max(metrics.LastOffset, layout.EndOffset);
                    }
                    metrics.BeforeOffset[layout.StartOffset] = metrics.InlineSize;
                    metrics.InlineSize += layout.Width;
                    metrics.ThroughOffset[layout.EndOffset] = metrics.InlineSize;
                }
            }
        foreach (var child in block.ChildBlocks)
            CollectDecorationFragments(child, fragments);
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
        if (TryLayoutNativeParagraph(paragraph, blockLayout, maxWidth, ref currentOffset))
            return;
        var lineLayout = new LineLayoutInfo
        {
            StartOffset = currentOffset,
            Height = GetDefaultLineHeight(),
            Baseline = GetDefaultLineHeight() * 0.8
        };

        double x = 0;

        foreach (var inline in paragraph.Inlines)
        {
            LayoutInline(inline, blockLayout, ref lineLayout, maxWidth, ref x, ref currentOffset);
        }

        lineLayout.EndOffset = currentOffset;
        lineLayout.Width = x;

        if (lineLayout.Runs.Count > 0 || blockLayout.Lines.Count == 0)
        {
            blockLayout.Lines.Add(lineLayout);
        }
    }

    private bool TryLayoutNativeParagraph(Paragraph paragraph, BlockLayoutInfo blockLayout,
        double maxWidth, ref int currentOffset)
    {
        if (!OperatingSystem.IsMacOS() || RenderContext.Current?.Backend != RenderBackend.Metal)
            return false;
        var spans = new List<NativeTextParagraph.Span>();
        var sources = new List<Run?>();
        var family = ResolveNativeRunFamily(null);
        void Add(string text, Run? run)
        {
            if (text.Length == 0) return;
            var color = ResolveNativeRunColor(run);
            var font = ResolveNativeRunFont(run);
            spans.Add(new NativeTextParagraph.Span(text, font.Family, font.Size,
                font.Weight, font.Style, color.Color, color.Opacity));
            sources.Add(run);
        }
        void Flatten(Inline inline)
        {
            if (inline is Run run) Add(run.Text, run);
            else if (inline is LineBreak) Add("\n", null);
            else if (inline is Span span) foreach (var child in span.Inlines) Flatten(child);
        }
        foreach (var inline in paragraph.Inlines) Flatten(inline);
        int direction = GetNativeParagraphDirection(paragraph);
        var native = NativeTextParagraph.TryCreate(spans, family, _document.FontSize,
            Math.Max(1, maxWidth), double.IsFinite(paragraph.LineHeight) && paragraph.LineHeight > 0
                ? paragraph.LineHeight : GetDefaultLineHeight(), paragraph.TextAlignment,
            direction == 1 ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, naturalDirection: direction < 0);
        if (native is null) return false;
        blockLayout.NativeParagraph = native;
        blockLayout.NativeDirection = direction;
        for (int i = 0; i < sources.Count; ++i)
        {
            blockLayout.NativeColors.Add((sources[i], spans[i].Color, spans[i].Opacity));
            blockLayout.NativeFonts.Add((sources[i], spans[i].FontFamily, spans[i].FontSize,
                spans[i].FontWeight, spans[i].FontStyle));
        }
        blockLayout.NativeFontEpoch = TextMeasurement.FontCacheEpoch;
        int paragraphOffset = currentOffset;
        foreach (var row in native.Lines)
        {
            var metrics = row.Metrics;
            var line = new LineLayoutInfo
            {
                NativeLine = row, ParagraphOffset = paragraphOffset,
                StartOffset = paragraphOffset + (int)metrics.Line.TextPosition,
                EndOffset = paragraphOffset + (int)(metrics.Line.TextPosition + metrics.Line.Length),
                Width = metrics.Line.X + metrics.Line.Width, Height = metrics.Line.Height, Baseline = metrics.Baseline
            };
            foreach (var fragment in row.Fragments)
                line.Runs.Add(new RunLayoutInfo
                {
                    Run = sources[(int)fragment.SpanIndex], X = fragment.X, Width = fragment.Width,
                    StartOffset = paragraphOffset + (int)fragment.TextPosition,
                    EndOffset = paragraphOffset + (int)(fragment.TextPosition + fragment.Length)
                });
            line.Runs.Sort(static (a, b) => a.X.CompareTo(b.X));
            blockLayout.Lines.Add(line);
        }
        currentOffset += native.Text.Length;
        return true;
    }

    private string ResolveNativeRunFamily(Run? run) => run?.FontFamily?.GetRenderingSource(run) ??
        _document.FontFamily?.GetRenderingSource(_document) ?? FrameworkElement.DefaultFontFamilyName;

    private (string Family, double Size, int Weight, int Style) ResolveNativeRunFont(Run? run) =>
        (ResolveNativeRunFamily(run), run is { FontSize: > 0 } ? run.FontSize : _document.FontSize,
            run?.FontWeight.ToOpenTypeWeight() ?? 400, run?.FontStyle.ToOpenTypeStyle() ?? 0);

    private (Color Color, double Opacity) ResolveNativeRunColor(Run? run)
    {
        Brush? brush = run?.Foreground ?? _document.Foreground ?? ResolveDocumentForegroundBrush();
        var color = brush switch
        {
            SolidColorBrush solid => solid.Color,
            GradientBrush { GradientStops.Count: > 0 } gradient => gradient.GradientStops[0].Color,
            _ => Colors.White
        };
        return (color, Jalium.UI.Styling.CssFontFaces.IsBlocked(ResolveNativeRunFamily(run)) ? 0 : brush?.Opacity ?? 1);
    }

    private int GetNativeParagraphDirection(Paragraph paragraph)
    {
        // An unspecified macOS paragraph follows its first strong character.
        // A declared direction, including explicit LTR, overrides that default.
        for (TextElement? element = paragraph; element is not null; element = element.Parent)
            if (element.HasValueAboveInherited(Block.FlowDirectionProperty))
                return (FlowDirection)element.GetValue(Block.FlowDirectionProperty)! == FlowDirection.RightToLeft ? 1 : 0;
        if (_document.HasValueAboveInherited(FlowDocument.FlowDirectionProperty))
            return _document.FlowDirection == FlowDirection.RightToLeft ? 1 : 0;
        for (FrameworkElement? element = this; element is not null; element = element.FrameworkParent)
            if (element.HasValueAboveInherited(FlowDirectionProperty))
                return FlowDirection == FlowDirection.RightToLeft ? 1 : 0;
        return -1;
    }

    private bool NativePresentationIsCurrent(List<BlockLayoutInfo> blocks)
    {
        foreach (var block in blocks)
        {
            if (block.NativeParagraph is not null && (block.NativeFontEpoch != TextMeasurement.FontCacheEpoch ||
                block.Block is Paragraph paragraph && block.NativeDirection != GetNativeParagraphDirection(paragraph))) return false;
            // Inherited typography can change without a Run content notification.
            // Never reuse shaped glyphs or IME geometry with a different font.
            foreach (var (run, family, size, weight, style) in block.NativeFonts)
                if (ResolveNativeRunFont(run) != (family, size, weight, style)) return false;
            foreach (var (run, color, opacity) in block.NativeColors)
                if (ResolveNativeRunColor(run) != (color, opacity)) return false;
            if (!NativePresentationIsCurrent(block.ChildBlocks)) return false;
        }
        return true;
    }

    private void LayoutInline(Inline inline, BlockLayoutInfo blockLayout, ref LineLayoutInfo lineLayout,
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
                LayoutInline(child, blockLayout, ref lineLayout, maxWidth, ref x, ref currentOffset);
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
        var editingKey = MacOSTextKeyBehavior.ResolveEditingKey(e);
        var ctrl = e.IsControlDown && editingKey == e.Key;
        var commandNavigation = MacOSTextKeyBehavior.IsCommandNavigation(e);
        var optionWord = MacOSTextKeyBehavior.IsOptionWord(e);

        if (_isImeComposing && ShouldDeferKeyToIme(e.Key, ctrl))
        {
            return;
        }

        switch (editingKey)
        {
            case Key.Left:
                if (commandNavigation) HandleMacOSVisualLineBoundary(shift, end: false);
                else HandleLeftKey(shift, ctrl, optionWord);
                e.Handled = true;
                break;

            case Key.Right:
                if (commandNavigation) HandleMacOSVisualLineBoundary(shift, end: true);
                else HandleRightKey(shift, ctrl, optionWord);
                e.Handled = true;
                break;

            case Key.Up:
                if (commandNavigation) HandleHomeKey(shift, ctrl: true);
                else HandleUpKey(shift);
                e.Handled = true;
                break;

            case Key.Down:
                if (commandNavigation) HandleEndKey(shift, ctrl: true);
                else HandleDownKey(shift);
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
                HandleBackspace(ctrl, optionWord);
                e.Handled = true;
                break;

            case Key.Delete:
                HandleDelete(ctrl, optionWord);
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

        if (e.ImeReplacementRange is { } range)
        {
            e.Handled = TryReplaceImeText(range.Start, range.Length, e.Text);
            return;
        }

        if (!string.IsNullOrEmpty(e.Text))
        {
            var text = e.Text;
            // Editing keys (including AcceptsTab) are handled by KeyDown.
            if (text.Length == 1 && char.IsControl(text[0]))
                return;

            InsertText(text);
            e.Handled = true;
        }
    }

    private void OnImeOwnerLoaded(object? sender, RoutedEventArgs e)
    {
        EnsureImeSubscriptionAttached();
    }

    private void EnsureImeSubscriptionAttached()
    {
        _imeSubscription ??= new InputMethodWeakSubscription<RichTextBox>(
            this,
            static (owner, source, args) => owner.OnImeCompositionStarted(source, args),
            static (owner, source, args) => owner.OnImeCompositionUpdated(source, args),
            static (owner, source, args) => owner.OnImeCompositionEnded(source, args));
        _imeSubscription.Attach();
    }

    private void OnImeOwnerUnloaded(object? sender, RoutedEventArgs e)
    {
        _imeSubscription?.Detach();
        if (ReferenceEquals(InputMethod.CurrentTarget, this))
        {
            InputMethod.SetTarget(null);
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
            var distanceFromLastClick = Math.Max(
                Math.Abs(position.X - _lastClickPosition.X),
                Math.Abs(position.Y - _lastClickPosition.Y));

            if (timeSinceLastClick <= global::Jalium.UI.SystemParameters.DoubleClickTime &&
                distanceFromLastClick <= global::Jalium.UI.SystemParameters.MouseDoubleClickDistance)
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
                    int wordIndex = MacOSTextKeyBehavior.GetWordIndexFromPoint(this, _document.GetText(), position,
                        newCaretPosition.DocumentOffset);
                    newCaretPosition = _document.GetPositionAtOffset(wordIndex, LogicalDirection.Forward) ?? newCaretPosition;
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
                int wordIndex = MacOSTextKeyBehavior.GetWordIndexFromPoint(this, _document.GetText(), position,
                    newCaretPosition.DocumentOffset);
                newCaretPosition = _document.GetPositionAtOffset(wordIndex, LogicalDirection.Forward) ?? newCaretPosition;
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
        var input = new MouseWheelScrollInput(e, 3 * Math.Max(1, MeasureTextWidth("M")), 3 * GetDefaultLineHeight());
        if (input.Horizontal == 0 && input.Vertical == 0) return;
        var bounds = GetContentBounds();
        var layout = EnsureLayout(bounds.Width);
        if (layout == null) return;
        double oldX = HorizontalOffset;
        double oldY = VerticalOffset;
        if (input.Horizontal != 0)
            HorizontalOffsetCore = Math.Clamp(oldX + input.Horizontal, 0, Math.Max(0, layout.TotalWidth - bounds.Width));
        if (input.Vertical != 0)
            VerticalOffsetCore = Math.Clamp(oldY + input.Vertical, 0, Math.Max(0, layout.TotalHeight - bounds.Height));
        input.MarkHandled(e, horizontal: HorizontalOffset != oldX, vertical: VerticalOffset != oldY);
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
                if (lineLayout.NativeLine is { } nativeLine)
                {
                    var hit = nativeLine.Paragraph.HitTest(nativeLine.Index, targetX - blockLayout.Margin.Left);
                    return _document.GetPositionAtOffset(lineLayout.ParagraphOffset + (int)hit.TextPosition,
                        hit.BackwardAffinity != 0 ? LogicalDirection.Backward : LogicalDirection.Forward);
                }
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
                            return _document.GetPositionAtOffset(offset, offset == lineLayout.EndOffset ?
                                LogicalDirection.Backward : LogicalDirection.Forward);
                        }
                        return _document.GetPositionAtOffset(runLayout.StartOffset, LogicalDirection.Forward);
                    }
                }
                // Clicked past the end of the line
                return _document.GetPositionAtOffset(lineLayout.EndOffset, LogicalDirection.Backward);
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

        if (TextMeasurement.HitTestPointWrapped(text, fontFamily, fontSize,
            run.FontWeight.ToOpenTypeWeight(), run.FontStyle.ToOpenTypeStyle(), 100000f, (float)x, 0, out var hit))
        {
            int offset = Math.Clamp((int)hit.TextPosition, 0, text.Length);
            return hit.IsTrailingHit != 0 ? GraphemeClusters.NextBoundary(text, offset) : GraphemeClusters.SnapNearest(text, offset);
        }

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
            EnsureImeSubscriptionAttached();
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
        if (IsReadOnly || CssCaretAnimationProperties.IsManual(this))
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
        if (!IsKeyboardFocused || IsReadOnly || CssCaretAnimationProperties.IsManual(this))
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

    protected override void OnCssCaretAnimationChanged()
    {
        base.OnCssCaretAnimationChanged();
        if (CssCaretAnimationProperties.IsManual(this))
        {
            StopCaretTimer();
            _caretOpacity = 1.0;
            _caretVisible = true;
        }
        else
        {
            ResetCaretBlink();
            if (IsKeyboardFocused) StartCaretTimer();
        }

        InvalidateVisual();
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

    private TextPointer? GetMacOSWordPosition(bool right, bool shift)
    {
        if (_caretPosition is null) return null;
        string text = _document.GetText();
        var layout = EnsureLayout(GetContentBounds().Width);
        var sources = new List<(NativeTextParagraph Paragraph, int Offset)>();
        void Collect(List<BlockLayoutInfo> blocks)
        {
            foreach (var block in blocks)
            {
                if (block.NativeParagraph is { } paragraph && block.Lines.Count > 0)
                    sources.Add((paragraph, block.Lines[0].ParagraphOffset));
                Collect(block.ChildBlocks);
            }
        }
        if (layout is not null) Collect(layout.Blocks);
        int start = _selection?.Start.DocumentOffset ?? 0;
        int length = shift ? 0 : (_selection?.End.DocumentOffset ?? 0) - start;
        if (NativeTextParagraph.TryNavigateWord(text, sources, _caretPosition.DocumentOffset,
            start, length, right, _caretPosition.LogicalDirection == LogicalDirection.Backward, out var destination))
            return _document.GetPositionAtOffset((int)destination.TextPosition,
                destination.BackwardAffinity != 0 ? LogicalDirection.Backward : LogicalDirection.Forward);
        return _document.GetPositionAtOffset(MacOSTextKeyBehavior.FindWordBoundary(text,
            _caretPosition.DocumentOffset, right, physical: true, selectionStart: start, selectionLength: length),
            right ? LogicalDirection.Forward : LogicalDirection.Backward);
    }

    private void HandleLeftKey(bool shift, bool ctrl, bool optionWord = false)
    {
        if (_caretPosition == null)
            return;

        var newPosition = optionWord
            ? GetMacOSWordPosition(false, shift)
            : ctrl
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

    private void HandleRightKey(bool shift, bool ctrl, bool optionWord = false)
    {
        if (_caretPosition == null)
            return;

        var newPosition = optionWord
            ? GetMacOSWordPosition(true, shift)
            : ctrl
            ? FindNextWordBoundary(_caretPosition)
            : _caretPosition.GetNextInsertionPosition(LogicalDirection.Forward);

        newPosition ??= _caretPosition;

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
            bool nextRow = _caretPosition.LogicalDirection == LogicalDirection.Forward && caretOffset == line.EndOffset &&
                i + 1 < allLines.Count && allLines[i + 1].line.StartOffset == caretOffset;
            if (caretOffset >= line.StartOffset && caretOffset <= line.EndOffset && !nextRow)
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
        var targetX = caretPos.Value.X;

        if (targetLine.line.NativeLine is { } nativeLine)
        {
            var hit = nativeLine.Paragraph.HitTest(nativeLine.Index, targetX - targetLine.x);
            return _document.GetPositionAtOffset(targetLine.line.ParagraphOffset + (int)hit.TextPosition,
                hit.BackwardAffinity != 0 ? LogicalDirection.Backward : LogicalDirection.Forward) ?? _document.ContentEnd;
        }

        // Find the position on the target line at the same X offset
        foreach (var runLayout in targetLine.line.Runs)
        {
            if (runLayout.Run != null && targetX >= targetLine.x + runLayout.X &&
                targetX <= targetLine.x + runLayout.X + runLayout.Width)
            {
                var localX = targetX - targetLine.x - runLayout.X;
                var charIndex = FindCharIndexFromX(runLayout.Run, localX);
                var offset = runLayout.StartOffset + charIndex;
                return _document.GetPositionAtOffset(offset, offset == targetLine.line.EndOffset ?
                    LogicalDirection.Backward : LogicalDirection.Forward) ?? _document.ContentEnd;
            }
        }

        // X is past the end of the target line
        if (targetX > targetLine.x + targetLine.line.Width)
            return _document.GetPositionAtOffset(targetLine.line.EndOffset, LogicalDirection.Backward) ?? _document.ContentEnd;

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

    private void HandleMacOSVisualLineBoundary(bool shift, bool end)
    {
        if (_caretPosition == null) return;
        var layout = EnsureLayout(GetContentBounds().Width);
        if (layout == null) return;
        var lines = new List<(LineLayoutInfo line, double y, double x)>();
        double y = 0;
        CollectAllLines(layout.Blocks, 0, ref y, lines);
        int offset = _caretPosition.DocumentOffset;
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].line;
            if (offset < line.StartOffset || offset > line.EndOffset) continue;
            if (offset == line.EndOffset && _caretPosition.LogicalDirection == LogicalDirection.Forward &&
                i + 1 < lines.Count && lines[i + 1].line.StartOffset == offset) continue;
            int target = end ? line.EndOffset : line.StartOffset;
            bool backward = end;
            if (line.NativeLine is { } nativeLine)
            {
                var metrics = nativeLine.Metrics.Line;
                target = line.ParagraphOffset + (int)(end ? metrics.RightCaretPosition : metrics.LeftCaretPosition);
                backward = (end ? metrics.RightBackwardAffinity : metrics.LeftBackwardAffinity) != 0;
            }
            var destination = _document.GetPositionAtOffset(target,
                backward ? LogicalDirection.Backward : LogicalDirection.Forward);
            if (destination == null) return;
            if (shift) ExtendSelection(destination);
            else { _caretPosition = destination; ClearSelection(); }
            ResetCaretBlink();
            EnsureCaretVisible();
            InvalidateVisual();
            return;
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

    private void HandleBackspace(bool ctrl, bool optionWord = false)
    {
        if (IsReadOnly) return;
        if (_selection is { IsEmpty: false }) DeleteSelection();
        else if (_caretPosition is { } caret)
        {
            // ContentEnd can select the final paragraph terminator. Editing
            // from that position belongs just before the immutable terminator.
            int textLength = _document.GetText().Length;
            if (textLength > 0 && caret.DocumentOffset == textLength)
                caret = _document.GetPositionAtOffset(textLength - 1, LogicalDirection.Backward) ?? caret;
            var previous = optionWord
                ? _document.GetPositionAtOffset(MacOSTextKeyBehavior.FindWordBoundary(
                    _document.GetText(), caret.DocumentOffset, forward: false), LogicalDirection.Backward)
                : ctrl ? FindPreviousWordBoundary(caret) : caret.GetNextInsertionPosition(LogicalDirection.Backward);
            if (previous is not null)
                TryReplaceImeText(previous.DocumentOffset, caret.DocumentOffset - previous.DocumentOffset, string.Empty);
        }
        EnsureCaretVisible();
        InvalidateVisual();
    }

    private void HandleDelete(bool ctrl, bool optionWord = false)
    {
        if (IsReadOnly) return;
        if (_selection is { IsEmpty: false }) DeleteSelection();
        else if (_caretPosition is { } caret)
        {
            var next = optionWord
                ? _document.GetPositionAtOffset(MacOSTextKeyBehavior.FindWordBoundary(
                    _document.GetText(), caret.DocumentOffset, forward: true), LogicalDirection.Forward)
                : ctrl ? FindNextWordBoundary(caret) : caret.GetNextInsertionPosition(LogicalDirection.Forward);
            if (next is not null)
                TryReplaceImeText(caret.DocumentOffset, next.DocumentOffset - caret.DocumentOffset, string.Empty);
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
        if (MacOSTextKeyBehavior.TryGetWordRange(text, offset, out int nativeStart, out int nativeLength))
            return (nativeStart, nativeStart + nativeLength);
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
    internal bool IsImeAllowed => !IsReadOnly;

    /// <inheritdoc />
    internal bool TryGetImeSurroundingText(out ImeSurroundingTextSnapshot snapshot)
    {
        if (IsReadOnly)
        {
            snapshot = default;
            return false;
        }

        string text = _document.GetText();
        int cursor = Math.Clamp(_caretPosition?.DocumentOffset ?? 0, 0, text.Length);
        int anchor = cursor;
        if (_selection is { IsEmpty: false })
        {
            int start = Math.Clamp(_selection.Start.DocumentOffset, 0, text.Length);
            int end = Math.Clamp(_selection.End.DocumentOffset, start, text.Length);
            bool activeAtStart = cursor <= start;
            cursor = activeAtStart ? start : end;
            anchor = activeAtStart ? end : start;
        }

        snapshot = new ImeSurroundingTextSnapshot(text, cursor, anchor);
        return true;
    }

    /// <inheritdoc />
    internal bool DeleteImeSurroundingText(int beforeUtf8ByteCount, int afterUtf8ByteCount)
    {
        if (IsReadOnly ||
            !TryGetImeSurroundingText(out ImeSurroundingTextSnapshot snapshot) ||
            !ImeTextEncoding.TryGetDeleteRange(
                snapshot,
                beforeUtf8ByteCount,
                afterUtf8ByteCount,
                out int start,
                out int length))
        {
            return false;
        }

        if (length == 0)
            return true;

        TextPointer startPosition = _document.GetPositionAtOffset(start, LogicalDirection.Forward)
            ?? _document.ContentStart;
        TextPointer endPosition = _document.GetPositionAtOffset(start + length, LogicalDirection.Backward)
            ?? _document.ContentEnd;
        _selection = new TextSelection(startPosition, endPosition);
        _caretPosition = endPosition;
        DeleteSelection();
        return true;
    }

    private bool TrySetImeSelection(int start, int length)
    {
        if (!ImeTextEncoding.TryNormalizeUtf16Range(_document.GetText(), start, length,
                out start, out length))
            return false;
        var beginning = _document.GetPositionAtOffset(start, LogicalDirection.Forward) ?? _document.ContentStart;
        var end = _document.GetPositionAtOffset(start + length,
            length == 0 ? LogicalDirection.Forward : LogicalDirection.Backward) ?? _document.ContentEnd;
        _selection = new TextSelection(beginning, end);
        _selectionAnchor = beginning;
        _caretPosition = end;
        OnSelectionChanged();
        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateVisual();
        return true;
    }

    private bool TryReplaceImeText(int start, int length, string text)
    {
        if (IsReadOnly || !ImeTextEncoding.TryNormalizeUtf16Range(_document.GetText(), start, length,
                out start, out length)) return false;
        using var change = DeclareChangeBlock();
        PushUndo();
        TrySetImeSelection(start, length);
        _caretPosition = DocumentTextEditing.Replace(_document, start, length, text);
        _selection = new TextSelection(_caretPosition, _caretPosition);
        _selectionAnchor = _caretPosition;
        OnSelectionChanged();
        ResetCaretBlink();
        EnsureCaretVisible();
        InvalidateLayout();
        InvalidateVisual();
        return true;
    }

    /// <inheritdoc />
    internal Point GetImeCaretPosition()
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
    internal Rect GetImeCaretRectangle()
    {
        if (((IImeSupport)this).TryGetImeTextRangeGeometry(_isImeComposing ?
            Math.Clamp(_imeCompositionCursor, 0, _imeCompositionString.Length) : (_caretPosition?.DocumentOffset ?? 0),
            0, _isImeComposing, out var geometry))
            return new Rect(geometry.Rectangle.X, geometry.Rectangle.Y, 1, geometry.Rectangle.Height);
        Point bottom = GetImeCaretPosition();
        double height = Math.Max(1, GetDefaultLineHeight());
        return new Rect(bottom.X, bottom.Y - height, 1, height);
    }

    private Rect GetImeCompositionCaret(int index, bool trailing)
    {
        var anchor = _document.GetPositionAtOffset(GetImeAnchorOffset(), LogicalDirection.Forward) ?? _document.ContentStart;
        var point = GetCaretScreenPosition(GetContentBounds(), anchor) ?? new Point(GetContentBounds().X, GetContentBounds().Y);
        var format = GetImeFormatting(anchor);
        return ImeTextGeometry.GetFormattedCaret(_imeCompositionString, index, trailing, point,
            GetLineHeightForFormatting(format.FontSize), format.FontFamily, format.FontSize,
            format.FontWeight.ToOpenTypeWeight(), format.FontStyle.ToOpenTypeStyle(), 100000f,
            text =>
            {
                var formatted = new FormattedText(text, format.FontFamily, format.FontSize)
                { FontWeight = format.FontWeight.ToOpenTypeWeight(), FontStyle = format.FontStyle.ToOpenTypeStyle() };
                TextMeasurement.MeasureText(formatted);
                return formatted.Width;
            });
    }

    bool IImeSupport.TryGetImeTextRangeGeometry(int start, int length, bool composition, out ImeTextRangeGeometry geometry)
    {
        geometry = default;
        if (composition)
            return _isImeComposing && ImeTextGeometry.TryGetFirstLineRange(_imeCompositionString,
                start, length, GetImeCompositionCaret, out geometry);
        string text = _document.GetText();
        if (!ImeTextEncoding.TryNormalizeUtf16Range(text, start, length, out start, out length)) return false;
        Rect bounds = GetContentBounds();
        var layout = EnsureLayout(bounds.Width);
        if (layout == null) return false;
        double y = bounds.Top - _verticalOffset;
        var lines = new List<(LineLayoutInfo line, double y, double x)>();
        CollectAllLines(layout.Blocks, bounds.Left - _horizontalOffset, ref y, lines);
        for (int i = 0; i < lines.Count; i++)
        {
            var entry = lines[i];
            int limit = i + 1 < lines.Count ? lines[i + 1].line.StartOffset : text.Length;
            bool activeRowEnd = length == 0 && _caretPosition?.DocumentOffset == start &&
                _caretPosition.LogicalDirection == LogicalDirection.Backward && start == entry.line.EndOffset;
            if (start < entry.line.StartOffset || start > limit ||
                (start == limit && i + 1 < lines.Count && !activeRowEnd)) continue;
            int end = Math.Min(start + length, limit);
            if (length > 0 && entry.line.NativeLine is { } nativeLine)
            {
                int nativeStart = Math.Clamp(start - entry.line.ParagraphOffset, 0, nativeLine.Paragraph.Text.Length);
                int nativeEnd = Math.Clamp(end - entry.line.ParagraphOffset, nativeStart, nativeLine.Paragraph.Text.Length);
                var rectangles = nativeLine.Paragraph.Selection(nativeLine.Index, nativeStart, nativeEnd - nativeStart);
                if (rectangles.Length > 0)
                {
                    double left = rectangles.Min(static rectangle => rectangle.X);
                    double right = rectangles.Max(static rectangle => rectangle.X + rectangle.Width);
                    geometry = new ImeTextRangeGeometry(new Rect(entry.x + left, entry.y,
                        right - left, entry.line.Height), start, end - start);
                    return true;
                }
            }
            Rect caret(int offset, bool trailing) => new(entry.x +
                GetXOffsetInLine(entry.line, Math.Min(offset, entry.line.EndOffset), trailing,
                    backward: length == 0 && _caretPosition?.DocumentOffset == start &&
                        _caretPosition.LogicalDirection == LogicalDirection.Backward),
                entry.y, 0, Math.Max(1, entry.line.Height));
            if (!ImeTextGeometry.TryGetFirstLineRange(text, start, end - start, caret, out geometry)) return false;
            return true;
        }
        return false;
    }

    bool IImeSupport.TryGetImeCharacterIndex(Point point, bool composition, out int index)
    {
        index = -1;
        if (!GetContentBounds().Contains(point)) return false;
        if (!composition)
        {
            var position = GetTextPositionFromPoint(point);
            if (position == null) return false;
            index = ImeTextEncoding.SnapToGraphemeBoundary(_document.GetText(), position.DocumentOffset, false);
            return true;
        }
        if (!_isImeComposing) return false;
        var anchor = _document.GetPositionAtOffset(GetImeAnchorOffset(), LogicalDirection.Forward) ?? _document.ContentStart;
        var format = GetImeFormatting(anchor);
        Rect caret = GetImeCompositionCaret(0, false);
        return ImeTextGeometry.TryHitTest(_imeCompositionString, point, new Point(caret.X, caret.Y), caret.Height,
            format.FontFamily, format.FontSize, format.FontWeight.ToOpenTypeWeight(), format.FontStyle.ToOpenTypeStyle(),
            100000f, text =>
            {
                var formatted = new FormattedText(text, format.FontFamily, format.FontSize)
                { FontWeight = format.FontWeight.ToOpenTypeWeight(), FontStyle = format.FontStyle.ToOpenTypeStyle() };
                TextMeasurement.MeasureText(formatted);
                return formatted.Width;
            }, true, out index);
    }

    /// <inheritdoc />
    internal void OnImeCompositionStart()
    {
        _isImeComposing = true;
        _imeCompositionStart = _caretPosition?.DocumentOffset ?? 0;
        _imeCompositionString = string.Empty;
        _imeCompositionCursor = 0;

        if (OperatingSystem.IsMacOS())
        {
            _imeCompositionStart = _selection is { IsEmpty: false }
                ? _selection.Start.DocumentOffset : _caretPosition?.DocumentOffset ?? 0;
        }
        else if (_selection != null && !_selection.IsEmpty)
        {
            DeleteSelection();
            _imeCompositionStart = _caretPosition?.DocumentOffset ?? _imeCompositionStart;
        }

        UpdateImeWindowIfComposing();
        InvalidateVisual();
    }

    /// <inheritdoc />
    internal void OnImeCompositionUpdate(string compositionString, int cursorPosition)
    {
        _imeCompositionString = compositionString ?? string.Empty;
        _imeCompositionCursor = Math.Clamp(cursorPosition, 0, _imeCompositionString.Length);
        UpdateImeWindowIfComposing();
        InvalidateVisual();
    }

    /// <inheritdoc />
    internal void OnImeCompositionEnd(string? resultString)
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

    bool IImeSupport.IsImeAllowed => IsImeAllowed;

    bool IImeSupport.TryGetImeSurroundingText(out ImeSurroundingTextSnapshot snapshot)
        => TryGetImeSurroundingText(out snapshot);

    bool IImeSupport.DeleteImeSurroundingText(int beforeUtf8ByteCount, int afterUtf8ByteCount)
        => DeleteImeSurroundingText(beforeUtf8ByteCount, afterUtf8ByteCount);

    bool IImeSupport.TrySetImeSelection(int start, int length) => TrySetImeSelection(start, length);

    bool IImeSupport.TryReplaceImeText(int start, int length, string text) => TryReplaceImeText(start, length, text);

    Point IImeSupport.GetImeCaretPosition() => GetImeCaretPosition();

    Rect IImeSupport.GetImeCaretRectangle() => GetImeCaretRectangle();

    void IImeSupport.OnImeCompositionStart() => OnImeCompositionStart();

    void IImeSupport.OnImeCompositionUpdate(string compositionString, int cursorPosition)
        => OnImeCompositionUpdate(compositionString, cursorPosition);

    void IImeSupport.OnImeCompositionEnd(string? resultString) => OnImeCompositionEnd(resultString);

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
            double fontSize = run.FontSize;
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

    #region Helper Types

    /// <summary>
    /// Represents the state of a document for undo/redo.
    /// </summary>
    private sealed class DocumentState(
        DocumentSnapshot snapshot, TextPointer caret, TextPointer selectionAnchor,
        TextPointer selectionMoving, TextPointer caretAnchor, string text)
    {
        internal DocumentSnapshot Snapshot { get; } = snapshot;
        internal TextPointer Caret { get; } = caret;
        internal TextPointer SelectionAnchor { get; } = selectionAnchor;
        internal TextPointer SelectionMoving { get; } = selectionMoving;
        internal TextPointer CaretAnchor { get; } = caretAnchor;
        internal string Text { get; } = text;
        internal int CaretOffset { get; } = caret.DocumentOffset;
        internal int AnchorOffset { get; } = selectionAnchor.DocumentOffset;
        internal int MovingOffset { get; } = selectionMoving.DocumentOffset;
        internal int CaretAnchorOffset { get; } = caretAnchor.DocumentOffset;
    }

    /// <summary>
    /// Layout information for the entire document.
    /// </summary>
    private class FlowDocumentLayoutInfo : IDisposable
    {
        public List<BlockLayoutInfo> Blocks { get; } = new();
        public Dictionary<TextElement, DecorationFragmentMetrics> DecorationFragments { get; } =
            new(ReferenceEqualityComparer.Instance);
        public double TotalHeight { get; set; }
        public double TotalWidth { get; set; }
        public void Dispose() { foreach (var block in Blocks) block.Dispose(); }
    }

    private sealed class DecorationFragmentMetrics(int firstOffset, int lastOffset)
    {
        internal int FirstOffset = firstOffset;
        internal int LastOffset = lastOffset;
        internal double InlineSize;
        internal Dictionary<int, double> BeforeOffset { get; } = new();
        internal Dictionary<int, double> ThroughOffset { get; } = new();
    }

    /// <summary>
    /// Layout information for a block element.
    /// </summary>
    private class BlockLayoutInfo : IDisposable
    {
        public Block Block { get; set; } = null!;
        public Thickness Margin { get; set; }
        public List<LineLayoutInfo> Lines { get; } = new();
        public List<BlockLayoutInfo> ChildBlocks { get; } = new();
        public NativeTextParagraph? NativeParagraph { get; set; }
        public int NativeDirection { get; set; }
        public List<(Run? Run, Color Color, double Opacity)> NativeColors { get; } = new();
        public List<(Run? Run, string Family, double Size, int Weight, int Style)> NativeFonts { get; } = new();
        public long NativeFontEpoch { get; set; }
        public void Dispose()
        {
            NativeParagraph?.Dispose();
            foreach (var block in ChildBlocks) block.Dispose();
        }
    }

    /// <summary>
    /// Layout information for a line of text.
    /// </summary>
    private class LineLayoutInfo
    {
        public NativeTextParagraph.Line? NativeLine { get; set; }
        public int ParagraphOffset { get; set; }
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
