using Jalium.UI.Controls.Editor;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// A code editor control with syntax highlighting, line numbers, and efficient text rendering.
/// Uses a Rope-based document model and renders directly via DrawingContext.
/// </summary>
public class EditControl : Control, IImeSupport
{
    #region Static Brushes

    private static readonly SolidColorBrush s_defaultForegroundBrush = new(Color.FromRgb(212, 212, 212));
    private static readonly SolidColorBrush s_defaultSelectionBrush = new(Color.FromArgb(100, 38, 79, 120));
    private static readonly SolidColorBrush s_defaultCaretBrush = new(Color.FromRgb(220, 220, 220));
    private static readonly SolidColorBrush s_defaultLineNumberBrush = new(Color.FromRgb(133, 133, 133));
    private static readonly SolidColorBrush s_defaultCurrentLineBrush = new(Color.FromArgb(20, 78, 114, 148));
    private static readonly SolidColorBrush s_defaultGutterBrush = new(Color.FromRgb(30, 30, 30));
    private static readonly SolidColorBrush s_defaultSearchResultBrush = new(Color.FromArgb(60, 255, 200, 0));
    private static readonly SolidColorBrush s_defaultActiveSearchResultBrush = new(Color.FromArgb(120, 255, 160, 0));
    private static readonly SolidColorBrush s_selectedTextOccurrenceBrush = new(Color.FromArgb(52, 95, 153, 224));
    private static readonly SolidColorBrush s_symbolOccurrenceBrush = new(Color.FromArgb(44, 84, 178, 128));
    private static readonly SolidColorBrush s_defaultBracketHighlightBrush = new(Color.FromArgb(80, 97, 175, 239));
    private static readonly Pen s_defaultBracketHighlightPen = new(new SolidColorBrush(Color.FromArgb(200, 97, 175, 239)), 1);
    private static readonly SolidColorBrush s_imeCompositionBackgroundBrush = new(Color.FromArgb(60, 86, 156, 214));
    private static readonly SolidColorBrush s_imeCompositionTextBrush = new(Color.FromRgb(235, 235, 235));
    private static readonly Pen s_imeCompositionUnderlinePen = new(new SolidColorBrush(Color.FromRgb(86, 156, 214)), 1);
    private static readonly SolidColorBrush s_scrollBarTrackBrush = new(Color.FromArgb(200, 68, 68, 68));
    private static readonly SolidColorBrush s_scrollBarThumbBrush = new(Color.FromArgb(220, 180, 180, 180));
    private static readonly SolidColorBrush s_scrollBarActiveThumbBrush = new(Color.FromArgb(235, 212, 212, 212));
    private static readonly BlurEffect s_gutterOverflowBlurEffect = new(12f);
    private static readonly SolidColorBrush s_gutterOverflowOverlayBrush = new(Color.FromArgb(52, 20, 20, 20));
    private static readonly Pen s_foldingGuidePen = new(new SolidColorBrush(Color.FromArgb(120, 125, 137, 149)), 1);
    private static readonly Pen s_foldingChevronPen = new(new SolidColorBrush(Color.FromRgb(197, 206, 214)), 1.2);
    private static readonly Pen s_foldingChevronSelectedPen = new(new SolidColorBrush(Color.FromRgb(153, 218, 255)), 1.3);
    private static readonly SolidColorBrush s_foldingMarkerSelectedBackgroundBrush = new(Color.FromArgb(72, 70, 106, 152));
    private static readonly SolidColorBrush s_foldedHintBackgroundBrush = new(Color.FromArgb(198, 58, 63, 70));
    private static readonly Pen s_foldedHintBorderPen = new(new SolidColorBrush(Color.FromArgb(220, 142, 151, 161)), 1);
    private static readonly SolidColorBrush s_foldedHintSelectedBackgroundBrush = new(Color.FromArgb(224, 71, 92, 116));
    private static readonly Pen s_foldedHintSelectedBorderPen = new(new SolidColorBrush(Color.FromArgb(240, 149, 205, 255)), 1);
    private static readonly SolidColorBrush s_foldedHintTextBrush = new(Color.FromRgb(226, 231, 236));
    private static readonly Pen s_scopeGuidePen = new(new SolidColorBrush(Color.FromArgb(92, 104, 136, 176)), 1);
    private static readonly Pen s_scopeGuideActivePen = new(new SolidColorBrush(Color.FromArgb(196, 138, 185, 240)), 1.2);
    private static readonly SolidColorBrush s_scopeGuideTooltipBackgroundBrush = new(Color.FromArgb(236, 31, 34, 38));
    private static readonly Pen s_scopeGuideTooltipBorderPen = new(new SolidColorBrush(Color.FromArgb(220, 96, 108, 122)), 1);
    private static readonly SolidColorBrush s_scopeGuideTooltipTextBrush = new(Color.FromRgb(226, 231, 236));

    #endregion

    #region Fields

    private readonly EditorView _view = new();
    private readonly CaretManager _caret = new();
    private readonly SelectionManager _selection = new();
    private TextDocument _document = new();
    private readonly FindReplaceManager _findReplace = new();
    private readonly FoldingManager _foldingManager = new();
    private IFoldingStrategy _foldingStrategy = new BraceFoldingStrategy();
    private FindResult? _activeFindResult;
    private (int bracketOffset, int matchOffset)? _activeBracketPair;
    private bool _hasSearchQuery;
    private readonly HashSet<EditFeature> _enabledFeatures = [];
    private readonly List<EditorKeyBinding> _userKeyBindings = [];
    private readonly List<EditorKeyBinding> _defaultKeyBindings = CreateDefaultKeyBindings();
    private EditControlBehaviorOptions _behaviorOptions = new();
    private IEditProfiler? _profiler;
    private readonly Dictionary<TokenClassification, Brush> _classificationBrushCache = [];
    private IReactiveSyntaxHighlighter? _reactiveSyntaxHighlighter;

    // Input state
    private bool _isDragging;
    private DateTime _lastClickTime;
    private Point _lastClickPosition;
    private int _clickCount;
    private const double DoubleClickTime = 500;
    private const double DoubleClickDistance = 4;

    // IME state
    private bool _isImeComposing;
    private string _imeCompositionString = string.Empty;
    private int _imeCompositionCursor;
    private int _imeCompositionStart;

    // Keyboard chord state
    private Key? _pendingChordKey;
    private DateTime _pendingChordStartedUtc;
    private const double ChordTimeoutMs = 1200;

    // Scrollbar state
    private const double ScrollBarThickness = 12;
    private const double MinScrollBarThumbSize = 24;
    private const double ScrollBarCornerRadius = 6;
    private const double ScrollBarInnerPadding = 2;
    private const double FoldingMarkerSize = 10;
    private const double FoldedHintHorizontalPadding = 5;
    private const double FoldedHintVerticalPadding = 2;
    private const int FoldedHintPreviewMaxLines = 30;
    private const double ScopeGuideHoverTolerance = 4;
    private const double ScopeGuideInnerStartInset = 0.5;
    private const double ScopeGuideInnerEndInset = 0.5;
    private const double ScopeGuideTooltipOffsetX = 12;
    private const double ScopeGuideTooltipOffsetY = 10;
    private const double ScopeGuideTooltipPaddingX = 8;
    private const double ScopeGuideTooltipPaddingY = 4;
    private Rect _verticalScrollTrackRect = Rect.Empty;
    private Rect _verticalScrollThumbRect = Rect.Empty;
    private Rect _horizontalScrollTrackRect = Rect.Empty;
    private Rect _horizontalScrollThumbRect = Rect.Empty;
    private bool _isVerticalScrollBarVisible;
    private bool _isHorizontalScrollBarVisible;
    private ScrollBarDragMode _scrollBarDragMode;
    private double _scrollBarDragStartMouseCoordinate;
    private double _scrollBarDragStartOffset;
    private double _effectiveViewportHeight;
    private double _effectiveTextViewportWidth;
    private int _cachedMaxLineLength = -1;
    private int _cachedMaxLineLengthVersion = -1;
    private const int MaxSemanticHighlightMatches = 1024;
    private const int LargeDocumentFoldingThrottleLineCount = 2000;
    private const int LargeDocumentFoldingThrottleTextLength = 150_000;
    private static readonly TimeSpan LargeDocumentFoldingDebounceInterval = TimeSpan.FromMilliseconds(160);
    private string _cachedSelectionOccurrenceQuery = string.Empty;
    private int _cachedSelectionOccurrenceVersion = -1;
    private readonly List<FindResult> _cachedSelectionOccurrences = [];
    private string _cachedSymbolOccurrenceQuery = string.Empty;
    private int _cachedSymbolOccurrenceVersion = -1;
    private readonly List<FindResult> _cachedSymbolOccurrences = [];

    // Caret blink timer
    private DispatcherTimer? _caretTimer;
    private DispatcherTimer? _foldingRefreshTimer;
    private bool _hasPendingFoldingRefresh;
    private Point _lastPointerPosition;
    private bool _hasPointerPosition;
    private FoldingSection? _hoveredScopeGuideSection;
    private FoldingSection? _hoveredFoldedHintSection;

    private enum ScrollBarDragMode
    {
        None,
        Vertical,
        Horizontal
    }

    #endregion

    #region Dependency Properties

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(EditControl),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty LanguageProperty =
        DependencyProperty.Register(nameof(Language), typeof(string), typeof(EditControl),
            new PropertyMetadata("plaintext", OnLanguageChanged));

    public static readonly DependencyProperty DocumentFilePathProperty =
        DependencyProperty.Register(nameof(DocumentFilePath), typeof(string), typeof(EditControl),
            new PropertyMetadata(string.Empty, OnDocumentFilePathChanged));

    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(EditControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty ShowLineNumbersProperty =
        DependencyProperty.Register(nameof(ShowLineNumbers), typeof(bool), typeof(EditControl),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    public static readonly DependencyProperty HighlightCurrentLineProperty =
        DependencyProperty.Register(nameof(HighlightCurrentLine), typeof(bool), typeof(EditControl),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    public static readonly DependencyProperty TabSizeProperty =
        DependencyProperty.Register(nameof(TabSize), typeof(int), typeof(EditControl),
            new PropertyMetadata(4));

    public static readonly DependencyProperty ConvertTabsToSpacesProperty =
        DependencyProperty.Register(nameof(ConvertTabsToSpaces), typeof(bool), typeof(EditControl),
            new PropertyMetadata(true));

    public static readonly DependencyProperty SyntaxHighlighterProperty =
        DependencyProperty.Register(nameof(SyntaxHighlighter), typeof(ISyntaxHighlighter), typeof(EditControl),
            new PropertyMetadata(null, OnSyntaxHighlighterChanged));

    public static readonly DependencyProperty SelectionBrushProperty =
        DependencyProperty.Register(nameof(SelectionBrush), typeof(Brush), typeof(EditControl),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(100, 38, 79, 120)), OnVisualPropertyChanged));

    public static readonly DependencyProperty CaretBrushProperty =
        DependencyProperty.Register(nameof(CaretBrush), typeof(Brush), typeof(EditControl),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(220, 220, 220)), OnVisualPropertyChanged));

    public static readonly DependencyProperty LineNumberForegroundProperty =
        DependencyProperty.Register(nameof(LineNumberForeground), typeof(Brush), typeof(EditControl),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(133, 133, 133)), OnVisualPropertyChanged));

    public static readonly DependencyProperty CurrentLineBackgroundProperty =
        DependencyProperty.Register(nameof(CurrentLineBackground), typeof(Brush), typeof(EditControl),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(20, 78, 114, 148)), OnVisualPropertyChanged));

    public static readonly DependencyProperty GutterBackgroundProperty =
        DependencyProperty.Register(nameof(GutterBackground), typeof(Brush), typeof(EditControl),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(30, 30, 30)), OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    public string Text
    {
        get => (string)(GetValue(TextProperty) ?? string.Empty);
        set => SetValue(TextProperty, value);
    }

    public string Language
    {
        get => (string)(GetValue(LanguageProperty) ?? "plaintext");
        set => SetValue(LanguageProperty, value);
    }

    public string DocumentFilePath
    {
        get => (string)(GetValue(DocumentFilePathProperty) ?? string.Empty);
        set => SetValue(DocumentFilePathProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty)!;
        set => SetValue(IsReadOnlyProperty, value);
    }

    public bool ShowLineNumbers
    {
        get => (bool)GetValue(ShowLineNumbersProperty)!;
        set => SetValue(ShowLineNumbersProperty, value);
    }

    public bool HighlightCurrentLine
    {
        get => (bool)GetValue(HighlightCurrentLineProperty)!;
        set => SetValue(HighlightCurrentLineProperty, value);
    }

    public int TabSize
    {
        get => (int)GetValue(TabSizeProperty)!;
        set => SetValue(TabSizeProperty, value);
    }

    public bool ConvertTabsToSpaces
    {
        get => (bool)GetValue(ConvertTabsToSpacesProperty)!;
        set => SetValue(ConvertTabsToSpacesProperty, value);
    }

    public ISyntaxHighlighter? SyntaxHighlighter
    {
        get => (ISyntaxHighlighter?)GetValue(SyntaxHighlighterProperty);
        set => SetValue(SyntaxHighlighterProperty, value);
    }

    public Brush? SelectionBrush
    {
        get => (Brush?)GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    public Brush? CaretBrush
    {
        get => (Brush?)GetValue(CaretBrushProperty);
        set => SetValue(CaretBrushProperty, value);
    }

    public Brush? LineNumberForeground
    {
        get => (Brush?)GetValue(LineNumberForegroundProperty);
        set => SetValue(LineNumberForegroundProperty, value);
    }

    public Brush? CurrentLineBackground
    {
        get => (Brush?)GetValue(CurrentLineBackgroundProperty);
        set => SetValue(CurrentLineBackgroundProperty, value);
    }

    public Brush? GutterBackground
    {
        get => (Brush?)GetValue(GutterBackgroundProperty);
        set => SetValue(GutterBackgroundProperty, value);
    }

    public EditControlBehaviorOptions BehaviorOptions
    {
        get => _behaviorOptions;
        set
        {
            _behaviorOptions = value ?? new EditControlBehaviorOptions();
            _document.UndoStack.MergeTypingWindowMs = _behaviorOptions.UndoMergeWindowMs;
        }
    }

    public IReadOnlyList<EditorKeyBinding> UserKeyBindings => _userKeyBindings;

    /// <summary>
    /// Gets the underlying document model.
    /// </summary>
    public TextDocument Document => _document;

    /// <summary>
    /// Gets or sets the caret offset in the document.
    /// </summary>
    public int CaretOffset
    {
        get => _caret.Offset;
        set
        {
            int oldCaret = _caret.Offset;
            bool hadSelection = _selection.HasSelection;

            _caret.Offset = Math.Clamp(value, 0, _document.TextLength);
            _selection.ClearSelection(_caret.Offset);
            _caret.ResetBlink();
            EnsureCaretVisible();
            UpdateActiveBracketPair();
            if (hadSelection)
                OnSelectionChanged();
            if (oldCaret != _caret.Offset)
                OnCaretPositionChanged();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Gets the selected text.
    /// </summary>
    public string SelectedText => _selection.GetSelectedText(_document);

    public bool CanUndo => _document.UndoStack.CanUndo;

    public bool CanRedo => _document.UndoStack.CanRedo;

    public int SelectionStart => _selection.StartOffset;

    public int SelectionLength => _selection.Length;

    public int CaretLine => _caret.GetLineColumn(_document).line;

    public int CaretColumn => _caret.GetLineColumn(_document).column;

    public bool IsImeComposing => _isImeComposing;

    public event EventHandler<TextChangeEventArgs>? TextChanged;

    public event EventHandler? SelectionChanged;

    public event EventHandler? CaretPositionChanged;

    public event EventHandler? SearchResultsChanged;

    public event EventHandler? FoldingChanged;

    internal bool IsVerticalScrollBarVisibleForTesting => _isVerticalScrollBarVisible;

    internal bool IsHorizontalScrollBarVisibleForTesting => _isHorizontalScrollBarVisible;

    internal Rect VerticalScrollBarThumbRectForTesting => _verticalScrollThumbRect;

    internal Rect HorizontalScrollBarThumbRectForTesting => _horizontalScrollThumbRect;

    internal double VerticalOffsetForTesting => _view.VerticalOffset;

    internal double HorizontalOffsetForTesting => _view.HorizontalOffset;

    internal void UpdateScrollBarsForTesting(Size viewportSize)
    {
        if (viewportSize.Width <= 0 || viewportSize.Height <= 0)
            return;

        EnsureViewLayoutMetrics();
        _view.ViewportWidth = viewportSize.Width;
        _view.ViewportHeight = viewportSize.Height;
        UpdateScrollBarLayout(viewportSize);
    }

    public bool IsFeatureEnabled(EditFeature feature) => _enabledFeatures.Contains(feature);

    public void SetFeatureEnabled(EditFeature feature, bool enabled)
    {
        if (enabled)
            _enabledFeatures.Add(feature);
        else
            _enabledFeatures.Remove(feature);

        if (feature == EditFeature.ImeShortcutSuppression)
            _behaviorOptions.SuppressShortcutsDuringIme = enabled;
        else if (feature == EditFeature.PreserveCopyLineEndings)
            _behaviorOptions.PreserveLineEndingsOnCopy = enabled;

        if (feature is EditFeature.HighContrastMode or EditFeature.AccessibilityMode)
            InvalidateVisual();
    }

    public void SetUserKeyBindings(IEnumerable<EditorKeyBinding>? keyBindings)
    {
        _userKeyBindings.Clear();
        if (keyBindings == null)
            return;

        var seen = new HashSet<(Key key, ModifierKeys modifiers)>();
        foreach (var binding in keyBindings)
        {
            if (string.IsNullOrWhiteSpace(binding.CommandId))
                continue;

            var signature = (binding.Key, binding.Modifiers);
            if (!seen.Add(signature))
                continue;

            _userKeyBindings.Add(binding);
        }
    }

    internal void SetEditProfilerForTesting(IEditProfiler? profiler)
    {
        _profiler = profiler;
    }

    #endregion

    #region Constructor

    public EditControl()
    {
        // Dark theme defaults
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
        Foreground = new SolidColorBrush(Color.FromRgb(212, 212, 212));
        FontFamily = "Cascadia Code";
        FontSize = 14;

        Focusable = true;
        ClipToBounds = true;
        Cursor = Jalium.UI.Cursors.IBeam;

        // Wire document events
        _document.Changed += OnDocumentChanged;
        _document.UndoStack.StateChanged += OnUndoStateChanged;
        _document.UndoStack.MergeTypingWindowMs = _behaviorOptions.UndoMergeWindowMs;

        // Wire internal view dependencies
        _view.Document = _document;
        _view.Folding = _foldingManager;
        _view.ClassificationBrushResolver = ResolveClassificationBrush;
        _findReplace.Document = _document;
        _foldingManager.Document = _document;

        // Input event handlers
        AddHandler(KeyDownEvent, new RoutedEventHandler(OnKeyDownHandler));
        AddHandler(TextInputEvent, new RoutedEventHandler(OnTextInputHandler));
        AddHandler(MouseDownEvent, new RoutedEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new RoutedEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new RoutedEventHandler(OnMouseMoveHandler));
        AddHandler(MouseLeaveEvent, new RoutedEventHandler(OnMouseLeaveHandler));
        AddHandler(MouseWheelEvent, new RoutedEventHandler(OnMouseWheelHandler));
        AddHandler(GotKeyboardFocusEvent, new RoutedEventHandler(OnGotFocusHandler));
        AddHandler(LostKeyboardFocusEvent, new RoutedEventHandler(OnLostFocusHandler));

        // IME
        InputMethod.CompositionStarted += OnImeCompositionStarted;
        InputMethod.CompositionUpdated += OnImeCompositionUpdated;
        InputMethod.CompositionEnded += OnImeCompositionEnded;

        ApplyLanguageDefaults(Language);
        UpdateFoldingState();
    }

    #endregion

    #region Layout

    protected override Size MeasureOverride(Size availableSize)
    {
        // Editor fills available space
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        return finalSize;
    }

    protected override void OnResourcesChanged()
    {
        base.OnResourcesChanged();
        _classificationBrushCache.Clear();
        _view.InvalidateVisibleLines();
        InvalidateVisual();
    }

    #endregion

    #region Rendering

    protected override void OnRender(object drawingContext)
    {
        if (drawingContext is not DrawingContext dc) return;
        if (RenderSize.Width <= 0 || RenderSize.Height <= 0) return;

        var fontFamily = FontFamily ?? "Cascadia Code";
        var fontSize = FontSize > 0 ? FontSize : 14;

        _view.UpdateLayout(fontFamily, fontSize);
        UpdateScrollBarLayout(RenderSize);

        double contentWidth = Math.Max(0, RenderSize.Width - (_isVerticalScrollBarVisible ? ScrollBarThickness : 0));
        double contentHeight = Math.Max(0, RenderSize.Height - (_isHorizontalScrollBarVisible ? ScrollBarThickness : 0));
        var contentSize = new Size(contentWidth, contentHeight);

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, RenderSize.Width, RenderSize.Height)));
        try
        {
            // Draw background
            if (Background != null)
            {
                dc.DrawRectangle(Background, null, new Rect(0, 0, RenderSize.Width, RenderSize.Height));
            }

            bool hasContentClip = contentWidth > 0 && contentHeight > 0;
            if (hasContentClip)
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, contentWidth, contentHeight)));

            try
            {
                bool applyGutterOverflowShield = ShouldApplyGutterOverflowShield(contentHeight);
                if (_hasPointerPosition && !_isDragging && _scrollBarDragMode == ScrollBarDragMode.None)
                {
                    UpdateHoveredScopeGuide(_lastPointerPosition, contentWidth, contentHeight);
                    UpdateHoveredFoldedHint(_lastPointerPosition, contentWidth, contentHeight);
                }
                else
                {
                    _hoveredScopeGuideSection = null;
                    _hoveredFoldedHintSection = null;
                }

                _view.Render(dc, contentSize, _caret, _selection,
                    ShowLineNumbers, HighlightCurrentLine,
                    Foreground ?? s_defaultForegroundBrush,
                    SelectionBrush ?? s_defaultSelectionBrush,
                    CaretBrush ?? s_defaultCaretBrush,
                    LineNumberForeground ?? s_defaultLineNumberBrush,
                    CurrentLineBackground ?? s_defaultCurrentLineBrush,
                    GutterBackground ?? s_defaultGutterBrush,
                    fontFamily, fontSize, FontWeight, FontStyle,
                    renderLineNumbers: !applyGutterOverflowShield,
                    suppressCaret: _isImeComposing);

                if (_profiler != null && IsFeatureEnabled(EditFeature.RenderProfiling))
                {
                    int renderedLines = _view.VisibleLineCount;
                    int highlightedLines = SyntaxHighlighter == null ? 0 : renderedLines;
                    _profiler.OnRenderCompleted(new RenderStats(
                        _view.VisibleLineCount,
                        renderedLines,
                        highlightedLines,
                        _view.CachedLineCount));
                }

                DrawSelectedTextOccurrences(dc);
                DrawCaretSymbolOccurrences(dc);
                DrawSearchHighlights(dc);
                DrawBracketHighlights(dc);
                DrawScopeGuides(dc, contentWidth, contentHeight);
                DrawFoldedSectionHints(dc, contentWidth, contentHeight, fontFamily, fontSize);
                DrawImeComposition(dc, fontFamily, fontSize);
                if (applyGutterOverflowShield)
                    DrawGutterOverflowShield(dc, contentHeight, fontFamily, fontSize);
                DrawFoldingMarkers(dc, contentHeight);
                bool hasFoldedHintTooltip = DrawFoldedSectionHoverTooltip(dc, contentWidth, contentHeight, fontFamily, fontSize);
                if (!hasFoldedHintTooltip)
                    DrawScopeGuideHoverTooltip(dc, contentWidth, contentHeight, fontFamily, fontSize);
            }
            finally
            {
                if (hasContentClip)
                    dc.Pop();
            }

            DrawScrollBars(dc);
        }
        finally
        {
            dc.Pop();
        }
    }

    #endregion

    #region Keyboard Input

    private static List<EditorKeyBinding> CreateDefaultKeyBindings()
    {
        return
        [
            new EditorKeyBinding(Key.A, ModifierKeys.Control, EditorCommands.SelectAll),
            new EditorKeyBinding(Key.C, ModifierKeys.Control, EditorCommands.Copy),
            new EditorKeyBinding(Key.X, ModifierKeys.Control, EditorCommands.Cut),
            new EditorKeyBinding(Key.V, ModifierKeys.Control, EditorCommands.Paste),
            new EditorKeyBinding(Key.Z, ModifierKeys.Control, EditorCommands.Undo),
            new EditorKeyBinding(Key.Z, ModifierKeys.Control | ModifierKeys.Shift, EditorCommands.Redo),
            new EditorKeyBinding(Key.Y, ModifierKeys.Control, EditorCommands.Redo),
            new EditorKeyBinding(Key.F, ModifierKeys.Control, EditorCommands.Find),
            new EditorKeyBinding(Key.H, ModifierKeys.Control, EditorCommands.Replace),
            new EditorKeyBinding(Key.G, ModifierKeys.Control, EditorCommands.GoToLine),
            new EditorKeyBinding(Key.L, ModifierKeys.Control | ModifierKeys.Shift, EditorCommands.SelectAllLines),
            new EditorKeyBinding(Key.F3, ModifierKeys.None, EditorCommands.FindNext),
            new EditorKeyBinding(Key.F3, ModifierKeys.Shift, EditorCommands.FindPrevious),
            new EditorKeyBinding(Key.Up, ModifierKeys.Alt | ModifierKeys.Shift, EditorCommands.MoveLineUp),
            new EditorKeyBinding(Key.Down, ModifierKeys.Alt | ModifierKeys.Shift, EditorCommands.MoveLineDown)
        ];
    }

    private bool HasUserBinding(Key key, ModifierKeys modifiers)
    {
        for (int i = 0; i < _userKeyBindings.Count; i++)
        {
            if (_userKeyBindings[i].Key == key && _userKeyBindings[i].Modifiers == modifiers)
                return true;
        }

        return false;
    }

    private bool TryExecuteKeyBinding(KeyEventArgs keyArgs)
    {
        for (int i = 0; i < _userKeyBindings.Count; i++)
        {
            var binding = _userKeyBindings[i];
            if (binding.Matches(keyArgs))
                return ExecuteEditorCommand(binding.CommandId);
        }

        for (int i = 0; i < _defaultKeyBindings.Count; i++)
        {
            var binding = _defaultKeyBindings[i];
            if (HasUserBinding(binding.Key, binding.Modifiers))
                continue;

            if (binding.Matches(keyArgs))
                return ExecuteEditorCommand(binding.CommandId);
        }

        return false;
    }

    private void OnKeyDownHandler(object sender, RoutedEventArgs e)
    {
        if (e is not KeyEventArgs keyArgs || keyArgs.Handled)
            return;

        var shift = keyArgs.IsShiftDown;
        var ctrl = keyArgs.IsControlDown;

        if (_isImeComposing && (_behaviorOptions.SuppressShortcutsDuringIme || IsFeatureEnabled(EditFeature.ImeShortcutSuppression)))
        {
            bool hasModifier = keyArgs.IsControlDown || keyArgs.IsAltDown;
            if (hasModifier)
            {
                keyArgs.Handled = false;
                return;
            }
        }

        if (TryHandleChordShortcut(keyArgs, ctrl))
            return;

        if (TryExecuteKeyBinding(keyArgs))
        {
            keyArgs.Handled = true;
            return;
        }

        if (TryHandleDirectShortcut(keyArgs, ctrl, shift))
            return;

        switch (keyArgs.Key)
        {
            case Key.Left:
                MoveCaret(ctrl ? MoveToWordBoundary(_caret.Offset, -1) : _caret.Offset - 1, shift);
                _caret.DesiredColumn = -1;
                keyArgs.Handled = true;
                break;

            case Key.Right:
                MoveCaret(ctrl ? MoveToWordBoundary(_caret.Offset, 1) : _caret.Offset + 1, shift);
                _caret.DesiredColumn = -1;
                keyArgs.Handled = true;
                break;

            case Key.Up:
                MoveCaretVertically(-1, shift);
                keyArgs.Handled = true;
                break;

            case Key.Down:
                MoveCaretVertically(1, shift);
                keyArgs.Handled = true;
                break;

            case Key.Home:
                if (ctrl)
                    MoveCaret(0, shift);
                else
                    MoveCaretToLineStart(shift);
                _caret.DesiredColumn = -1;
                keyArgs.Handled = true;
                break;

            case Key.End:
                if (ctrl)
                    MoveCaret(_document.TextLength, shift);
                else
                    MoveCaretToLineEnd(shift);
                _caret.DesiredColumn = -1;
                keyArgs.Handled = true;
                break;

            case Key.PageUp:
                MoveCaretVertically(-Math.Max(1, _view.VisibleLineCount), shift);
                keyArgs.Handled = true;
                break;

            case Key.PageDown:
                MoveCaretVertically(Math.Max(1, _view.VisibleLineCount), shift);
                keyArgs.Handled = true;
                break;

            case Key.Back:
                HandleBackspace(ctrl);
                keyArgs.Handled = true;
                break;

            case Key.Delete:
                HandleDelete(ctrl);
                keyArgs.Handled = true;
                break;

            case Key.Enter:
                HandleEnter();
                keyArgs.Handled = true;
                break;

            case Key.Tab:
                HandleTab(shift);
                keyArgs.Handled = true;
                break;
        }
    }

    private bool TryHandleDirectShortcut(KeyEventArgs keyArgs, bool ctrl, bool shift)
    {
        if (!ctrl)
            return false;

        switch (keyArgs.Key)
        {
            case Key.F:
                if (ExecuteEditorCommand(EditorCommands.Find))
                {
                    keyArgs.Handled = true;
                    return true;
                }
                return false;

            case Key.H:
                if (ExecuteEditorCommand(EditorCommands.Replace))
                {
                    keyArgs.Handled = true;
                    return true;
                }
                return false;

            case Key.G:
                if (ExecuteEditorCommand(EditorCommands.GoToLine))
                {
                    keyArgs.Handled = true;
                    return true;
                }
                return false;

            case Key.D:
                if (_behaviorOptions.CtrlDSelectNextOccurrence || IsFeatureEnabled(EditFeature.MultiCaret))
                    SelectNextOccurrenceByWord();
                else
                    DuplicateLineOrSelection();
                keyArgs.Handled = true;
                return true;

            case Key.L:
                if (shift)
                {
                    ExecuteEditorCommand(EditorCommands.SelectAllLines);
                    keyArgs.Handled = true;
                    return true;
                }

                if (_behaviorOptions.UseLegacyCtrlLDeleteLineShortcut)
                    DeleteLineOrSelection();
                else
                    SelectCurrentLine();
                keyArgs.Handled = true;
                return true;
        }

        return false;
    }

    private bool TryHandleChordShortcut(KeyEventArgs keyArgs, bool ctrl)
    {
        if (_pendingChordKey.HasValue)
        {
            if (!ctrl || (DateTime.UtcNow - _pendingChordStartedUtc).TotalMilliseconds > ChordTimeoutMs)
            {
                ClearChordState();
                return false;
            }

            bool handled = _pendingChordKey.Value switch
            {
                Key.K when keyArgs.Key == Key.C => ExecuteChordAction(CommentSelection),
                Key.K when keyArgs.Key == Key.U => ExecuteChordAction(UncommentSelection),
                Key.M when keyArgs.Key == Key.M => ExecuteChordAction(() =>
                {
                    ToggleFold(CaretLine);
                }),
                Key.M when keyArgs.Key == Key.L => ExecuteChordAction(() =>
                {
                    if (_foldingManager.HasFoldedSections) UnfoldAll();
                    else FoldAll();
                }),
                _ => false
            };

            ClearChordState();
            if (handled)
            {
                keyArgs.Handled = true;
                return true;
            }
        }

        if (!ctrl)
            return false;

        if (keyArgs.Key == Key.K || keyArgs.Key == Key.M)
        {
            _pendingChordKey = keyArgs.Key;
            _pendingChordStartedUtc = DateTime.UtcNow;
            keyArgs.Handled = true;
            return true;
        }

        return false;
    }

    private bool ExecuteChordAction(Action action)
    {
        action();
        return true;
    }

    private void ClearChordState()
    {
        _pendingChordKey = null;
    }

    private void OnTextInputHandler(object sender, RoutedEventArgs e)
    {
        if (e is not TextCompositionEventArgs textArgs || textArgs.Handled || IsReadOnly)
            return;

        var text = textArgs.Text;
        if (string.IsNullOrEmpty(text)) return;

        // Filter control characters (except tab handled by KeyDown)
        if (text.Length == 1 && char.IsControl(text[0]))
            return;

        InsertText(text);
        textArgs.Handled = true;
    }

    #endregion

    #region Mouse Input

    private void OnMouseDownHandler(object sender, RoutedEventArgs e)
    {
        if (e is not MouseButtonEventArgs mouseArgs || mouseArgs.ChangedButton != MouseButton.Left)
            return;

        int oldSelectionStart = _selection.StartOffset;
        int oldSelectionLength = _selection.Length;
        int oldCaret = _caret.Offset;
        Focus();

        var position = mouseArgs.GetPosition(this);
        EnsureViewLayoutMetrics();
        UpdateScrollBarLayout(RenderSize);
        UpdateCursorForPointer(position);
        if (TryHandleScrollBarMouseDown(mouseArgs, position))
            return;
        if (TryHandleFoldingMarkerMouseDown(mouseArgs, position))
            return;
        if (TryHandleFoldedSectionHintMouseDown(mouseArgs, position))
            return;

        UpdateClickCount(position);

        int offset = _view.GetOffsetFromPoint(position, ShowLineNumbers);

        if (_clickCount == 3)
        {
            // Triple-click: select line
            var line = _document.GetLineByOffset(offset);
            _selection.SetSelection(line.Offset, line.Length);
            _caret.Offset = line.Offset + line.Length;
            _clickCount = 0;
        }
        else if (_clickCount == 2)
        {
            // Double-click: select word
            SelectWordAt(offset);
        }
        else
        {
            // Single click
            if (mouseArgs.KeyboardModifiers.HasFlag(ModifierKeys.Shift))
            {
                _selection.ExtendTo(offset);
                _caret.Offset = offset;
            }
            else
            {
                _caret.Offset = offset;
                _selection.ClearSelection(offset);
            }
            _isDragging = true;
            CaptureMouse();
        }

        _caret.DesiredColumn = -1;
        _caret.ResetBlink();
        UpdateActiveBracketPair();
        if (oldSelectionStart != _selection.StartOffset || oldSelectionLength != _selection.Length)
            OnSelectionChanged();
        if (oldCaret != _caret.Offset)
            OnCaretPositionChanged();
        InvalidateVisual();
        mouseArgs.Handled = true;
    }

    private int UpdateClickCount(Point position)
    {
        var now = DateTime.Now;
        double timeSinceLastClick = (now - _lastClickTime).TotalMilliseconds;
        double distanceFromLastClick = Math.Abs(position.X - _lastClickPosition.X) + Math.Abs(position.Y - _lastClickPosition.Y);

        if (timeSinceLastClick < DoubleClickTime && distanceFromLastClick < DoubleClickDistance)
            _clickCount++;
        else
            _clickCount = 1;

        _lastClickTime = now;
        _lastClickPosition = position;
        return _clickCount;
    }

    private void OnMouseUpHandler(object sender, RoutedEventArgs e)
    {
        if (e is not MouseButtonEventArgs mouseArgs || mouseArgs.ChangedButton != MouseButton.Left)
            return;

        if (_scrollBarDragMode != ScrollBarDragMode.None)
        {
            _scrollBarDragMode = ScrollBarDragMode.None;
            ReleaseMouseCapture();
            InvalidateVisual();
            mouseArgs.Handled = true;
            return;
        }

        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();
        }

        UpdateCursorForPointer(mouseArgs.GetPosition(this));
    }

    private void OnMouseMoveHandler(object sender, RoutedEventArgs e)
    {
        if (e is not MouseEventArgs mouseArgs)
            return;

        var position = mouseArgs.GetPosition(this);
        _lastPointerPosition = position;
        _hasPointerPosition = true;
        UpdateCursorForPointer(position);
        double contentWidth = Math.Max(0, RenderSize.Width - (_isVerticalScrollBarVisible ? ScrollBarThickness : 0));
        double contentHeight = Math.Max(0, RenderSize.Height - (_isHorizontalScrollBarVisible ? ScrollBarThickness : 0));

        if (_scrollBarDragMode != ScrollBarDragMode.None)
        {
            _hoveredScopeGuideSection = null;
            _hoveredFoldedHintSection = null;
            HandleScrollBarMouseDrag(mouseArgs);
            return;
        }

        if (!_isDragging)
        {
            bool scopeHoverChanged = UpdateHoveredScopeGuide(position, contentWidth, contentHeight);
            bool foldedHintHoverChanged = UpdateHoveredFoldedHint(position, contentWidth, contentHeight);
            if (scopeHoverChanged || foldedHintHoverChanged || _hoveredScopeGuideSection != null || _hoveredFoldedHintSection != null)
                InvalidateVisual();
            return;
        }

        _hoveredScopeGuideSection = null;
        _hoveredFoldedHintSection = null;

        int oldSelectionStart = _selection.StartOffset;
        int oldSelectionLength = _selection.Length;
        int oldCaret = _caret.Offset;
        int offset = _view.GetOffsetFromPoint(position, ShowLineNumbers);

        _selection.ExtendTo(offset);
        _caret.Offset = offset;
        _caret.DesiredColumn = -1;
        _caret.ResetBlink();
        EnsureCaretVisible();
        UpdateActiveBracketPair();
        if (oldSelectionStart != _selection.StartOffset || oldSelectionLength != _selection.Length)
            OnSelectionChanged();
        if (oldCaret != _caret.Offset)
            OnCaretPositionChanged();
        InvalidateVisual();
    }

    private void OnMouseLeaveHandler(object sender, RoutedEventArgs e)
    {
        _hasPointerPosition = false;
        if (_hoveredScopeGuideSection == null && _hoveredFoldedHintSection == null)
            return;

        _hoveredScopeGuideSection = null;
        _hoveredFoldedHintSection = null;
        InvalidateVisual();
    }

    private void OnMouseWheelHandler(object sender, RoutedEventArgs e)
    {
        if (e is not MouseWheelEventArgs wheelArgs)
            return;

        EnsureViewLayoutMetrics();
        UpdateScrollBarLayout(RenderSize);

        bool horizontal = wheelArgs.KeyboardModifiers.HasFlag(ModifierKeys.Shift);
        if (horizontal)
        {
            double columnsToScroll = 6;
            double delta = -wheelArgs.Delta / 120.0 * columnsToScroll * Math.Max(1, _view.CharWidth);
            _view.HorizontalOffset += delta;
        }
        else
        {
            double linesToScroll = 3;
            double delta = -wheelArgs.Delta / 120.0 * linesToScroll * Math.Max(1, _view.LineHeight);
            _view.VerticalOffset += delta;
        }

        ClampScrollOffsetsToViewport();
        UpdateImeWindowIfComposing();
        InvalidateVisual();
        wheelArgs.Handled = true;
    }

    private void UpdateCursorForPointer(Point position)
    {
        var desiredCursor = IsPointerOverScrollBar(position) || IsPointerOverFoldingMarker(position) || IsPointerOverFoldedSectionHint(position)
            ? Jalium.UI.Cursors.Arrow
            : Jalium.UI.Cursors.IBeam;

        if (!ReferenceEquals(Cursor, desiredCursor))
            Cursor = desiredCursor;
    }

    private bool IsPointerOverScrollBar(Point position)
    {
        return (_isVerticalScrollBarVisible && _verticalScrollTrackRect.Contains(position))
            || (_isHorizontalScrollBarVisible && _horizontalScrollTrackRect.Contains(position));
    }

    private bool IsPointerOverFoldingMarker(Point position)
    {
        if (!ShowLineNumbers || _foldingManager.Foldings.Count == 0 || _view.LineHeight <= 0)
            return false;

        return TryGetFoldingMarkerAt(position, out _, out _);
    }

    private bool IsPointerOverFoldedSectionHint(Point position)
    {
        if (_foldingManager.Foldings.Count == 0 || _view.LineHeight <= 0)
            return false;

        return TryGetFoldedSectionHintAt(position, out _, out _);
    }

    private bool TryHandleScrollBarMouseDown(MouseButtonEventArgs mouseArgs, Point position)
    {
        if (_isVerticalScrollBarVisible && _verticalScrollTrackRect.Contains(position))
        {
            if (_verticalScrollThumbRect.Contains(position))
            {
                _scrollBarDragMode = ScrollBarDragMode.Vertical;
                _scrollBarDragStartMouseCoordinate = position.Y;
                _scrollBarDragStartOffset = _view.VerticalOffset;
                _isDragging = false;
                CaptureMouse();
            }
            else
            {
                double page = Math.Max(_effectiveViewportHeight * 0.9, Math.Max(1, _view.LineHeight));
                _view.VerticalOffset += position.Y < _verticalScrollThumbRect.Y ? -page : page;
                ClampScrollOffsetsToViewport();
                UpdateImeWindowIfComposing();
            }

            InvalidateVisual();
            mouseArgs.Handled = true;
            return true;
        }

        if (_isHorizontalScrollBarVisible && _horizontalScrollTrackRect.Contains(position))
        {
            if (_horizontalScrollThumbRect.Contains(position))
            {
                _scrollBarDragMode = ScrollBarDragMode.Horizontal;
                _scrollBarDragStartMouseCoordinate = position.X;
                _scrollBarDragStartOffset = _view.HorizontalOffset;
                _isDragging = false;
                CaptureMouse();
            }
            else
            {
                double page = Math.Max(_effectiveTextViewportWidth * 0.9, Math.Max(1, _view.CharWidth));
                _view.HorizontalOffset += position.X < _horizontalScrollThumbRect.X ? -page : page;
                ClampScrollOffsetsToViewport();
            }

            InvalidateVisual();
            mouseArgs.Handled = true;
            return true;
        }

        return false;
    }

    private bool TryHandleFoldingMarkerMouseDown(MouseButtonEventArgs mouseArgs, Point position)
    {
        if (!ShowLineNumbers || _foldingManager.Foldings.Count == 0 || _view.LineHeight <= 0)
            return false;

        if (!TryGetFoldingMarkerAt(position, out var section, out _))
            return false;

        int oldSelectionStart = _selection.StartOffset;
        int oldSelectionLength = _selection.Length;
        int oldCaret = _caret.Offset;

        bool toggled = _foldingManager.ToggleFold(section!.StartLine);
        if (toggled)
        {
            CompleteFoldingStateChange(oldSelectionStart, oldSelectionLength, oldCaret);
        }

        mouseArgs.Handled = true;
        return true;
    }

    private bool TryHandleFoldedSectionHintMouseDown(MouseButtonEventArgs mouseArgs, Point position)
    {
        if (_foldingManager.Foldings.Count == 0 || _view.LineHeight <= 0)
            return false;

        if (!TryGetFoldedSectionHintAt(position, out var section, out _))
            return false;

        int clickCount = UpdateClickCount(position);
        if (clickCount >= 2)
        {
            int oldSelectionStart = _selection.StartOffset;
            int oldSelectionLength = _selection.Length;
            int oldCaret = _caret.Offset;

            bool toggled = _foldingManager.ToggleFold(section!.StartLine);
            if (toggled)
            {
                CompleteFoldingStateChange(oldSelectionStart, oldSelectionLength, oldCaret);
                _hoveredFoldedHintSection = null;
            }

            _clickCount = 0;
        }
        else
        {
            SelectFoldedSectionContent(section!);
        }

        mouseArgs.Handled = true;
        return true;
    }

    private void SelectFoldedSectionContent(FoldingSection section)
    {
        if (!TryGetFoldingSectionSelectionRange(section, out int hiddenStartOffset, out int hiddenEndOffset))
            return;

        Select(hiddenStartOffset, hiddenEndOffset - hiddenStartOffset);
    }

    private bool IsFoldingSectionSelected(FoldingSection section)
    {
        if (!_selection.HasSelection)
            return false;
        if (!TryGetFoldingSectionSelectionRange(section, out int sectionStartOffset, out int sectionEndOffset))
            return false;

        int selectionStart = Math.Clamp(_selection.StartOffset, 0, _document.TextLength);
        int selectionEnd = Math.Clamp(_selection.EndOffset, selectionStart, _document.TextLength);
        return selectionStart < sectionEndOffset && selectionEnd > sectionStartOffset;
    }

    private bool TryGetFoldingSectionSelectionRange(FoldingSection section, out int startOffset, out int endOffset)
    {
        startOffset = 0;
        endOffset = 0;

        int lineCount = _document.LineCount;
        if (lineCount <= 0)
            return false;

        int startLineNumber = Math.Clamp(section.StartLine, 1, lineCount);
        int endLineNumber = Math.Clamp(section.EndLine, startLineNumber, lineCount);
        var startLine = _document.GetLineByNumber(startLineNumber);
        var endLine = _document.GetLineByNumber(endLineNumber);

        startOffset = Math.Clamp(startLine.Offset + startLine.TotalLength, 0, _document.TextLength);
        endOffset = Math.Clamp(endLine.Offset + endLine.TotalLength, startOffset, _document.TextLength);

        if (endOffset <= startOffset)
        {
            startOffset = Math.Clamp(startLine.Offset, 0, _document.TextLength);
            endOffset = Math.Clamp(endLine.Offset + endLine.TotalLength, startOffset, _document.TextLength);
        }

        return endOffset > startOffset;
    }

    private bool TryGetFoldingMarkerAt(Point position, out FoldingSection? section, out Rect markerRect)
    {
        section = null;
        markerRect = Rect.Empty;

        if (!ShowLineNumbers || _view.LineHeight <= 0 || _foldingManager.Foldings.Count == 0)
            return false;

        int lineNumber = _view.GetLineNumberFromY(position.Y);
        section = _foldingManager.GetFoldingAt(lineNumber);
        if (section == null)
            return false;

        if (!_view.TryGetLineTop(section.StartLine, out double lineTop))
            return false;

        markerRect = GetFoldingMarkerRect(lineTop);
        return markerRect.Contains(position);
    }

    private bool TryGetFoldedSectionHintAt(Point position, out FoldingSection? section, out Rect hintRect)
    {
        double contentWidth = Math.Max(0, RenderSize.Width - (_isVerticalScrollBarVisible ? ScrollBarThickness : 0));
        double contentHeight = Math.Max(0, RenderSize.Height - (_isHorizontalScrollBarVisible ? ScrollBarThickness : 0));
        return TryGetFoldedSectionHintAt(position, contentWidth, contentHeight, out section, out hintRect);
    }

    private bool TryGetFoldedSectionHintAt(Point position, double contentWidth, double contentHeight, out FoldingSection? section, out Rect hintRect)
    {
        section = null;
        hintRect = Rect.Empty;

        if (_foldingManager.Foldings.Count == 0 || _view.LineHeight <= 0)
            return false;

        if (contentWidth <= 0 || contentHeight <= 0)
            return false;
        if (position.X < 0 || position.X > contentWidth || position.Y < 0 || position.Y > contentHeight)
            return false;

        int lineNumber = _view.GetLineNumberFromY(position.Y);
        var candidate = _foldingManager.GetFoldingAt(lineNumber);
        if (candidate == null || !candidate.IsFolded)
            return false;

        string hintText = GetFoldedSectionHintText(candidate);
        string fontFamily = FontFamily ?? "Cascadia Code";
        double fontSize = FontSize > 0 ? FontSize : 14;
        var hintLayout = CreateFoldedSectionHintLayout(hintText, fontFamily, fontSize);
        if (!TryGetFoldedSectionHintRect(candidate, hintLayout, contentWidth, contentHeight, out hintRect))
            return false;

        section = candidate;
        return hintRect.Contains(position);
    }

    private void HandleScrollBarMouseDrag(MouseEventArgs mouseArgs)
    {
        var position = mouseArgs.GetPosition(this);
        EnsureViewLayoutMetrics();
        UpdateScrollBarLayout(RenderSize);

        if (_scrollBarDragMode == ScrollBarDragMode.Vertical && !_verticalScrollTrackRect.IsEmpty)
        {
            double trackTravel = Math.Max(1, _verticalScrollTrackRect.Height - _verticalScrollThumbRect.Height);
            double maxOffset = GetMaxVerticalOffset();
            if (maxOffset > 0)
            {
                double pixelDelta = position.Y - _scrollBarDragStartMouseCoordinate;
                _view.VerticalOffset = Math.Clamp(_scrollBarDragStartOffset + (pixelDelta / trackTravel) * maxOffset, 0, maxOffset);
            }
        }
        else if (_scrollBarDragMode == ScrollBarDragMode.Horizontal && !_horizontalScrollTrackRect.IsEmpty)
        {
            double trackTravel = Math.Max(1, _horizontalScrollTrackRect.Width - _horizontalScrollThumbRect.Width);
            double maxOffset = GetMaxHorizontalOffset();
            if (maxOffset > 0)
            {
                double pixelDelta = position.X - _scrollBarDragStartMouseCoordinate;
                _view.HorizontalOffset = Math.Clamp(_scrollBarDragStartOffset + (pixelDelta / trackTravel) * maxOffset, 0, maxOffset);
            }
        }

        ClampScrollOffsetsToViewport();
        UpdateImeWindowIfComposing();
        InvalidateVisual();
        mouseArgs.Handled = true;
    }

    #endregion

    #region Focus

    private void OnGotFocusHandler(object sender, RoutedEventArgs e)
    {
        InputMethod.SetTarget(this);
        StartCaretTimer();
        _caret.ResetBlink();
        _caret.StartBlinking();
        InvalidateVisual();
    }

    private void OnLostFocusHandler(object sender, RoutedEventArgs e)
    {
        if (InputMethod.Current == this)
            InputMethod.SetTarget(null);

        _scrollBarDragMode = ScrollBarDragMode.None;
        _isDragging = false;
        ReleaseMouseCapture();
        ClearChordState();
        StopCaretTimer();
        _caret.StopBlinking();
        InvalidateVisual();
    }

    private void StartCaretTimer()
    {
        if (_caretTimer != null) return;

        _caretTimer = new DispatcherTimer();
        _caretTimer.Interval = _caret.BlinkInterval;
        _caretTimer.Tick += (_, _) => InvalidateVisual();
        _caretTimer.Start();
    }

    private void StopCaretTimer()
    {
        _caretTimer?.Stop();
        _caretTimer = null;
    }

    #endregion

    #region Caret Movement

    private void MoveCaret(int newOffset, bool extendSelection)
    {
        int oldCaret = _caret.Offset;
        int oldSelectionStart = _selection.StartOffset;
        int oldSelectionLength = _selection.Length;
        newOffset = Math.Clamp(newOffset, 0, _document.TextLength);

        if (extendSelection)
        {
            if (!_selection.HasSelection)
                _selection.AnchorOffset = _caret.Offset;
            _selection.ExtendTo(newOffset);
        }
        else
        {
            _selection.ClearSelection(newOffset);
        }

        _caret.Offset = newOffset;
        _caret.ResetBlink();
        EnsureCaretVisible();
        UpdateActiveBracketPair();
        if (oldSelectionStart != _selection.StartOffset || oldSelectionLength != _selection.Length)
            OnSelectionChanged();
        if (oldCaret != _caret.Offset)
            OnCaretPositionChanged();
        InvalidateVisual();
    }

    private void MoveCaretVertically(int lineDelta, bool extendSelection)
    {
        int oldCaret = _caret.Offset;
        int oldSelectionStart = _selection.StartOffset;
        int oldSelectionLength = _selection.Length;
        var (currentLine, currentColumn) = _caret.GetLineColumn(_document);

        // Use desired column if available
        if (_caret.DesiredColumn < 0)
            _caret.DesiredColumn = currentColumn;

        int targetLine = _view.MoveVisibleLine(currentLine, lineDelta);
        var targetDocLine = _document.GetLineByNumber(targetLine);
        int targetColumn = Math.Min(_caret.DesiredColumn, targetDocLine.Length);
        int newOffset = targetDocLine.Offset + targetColumn;

        if (extendSelection)
        {
            if (!_selection.HasSelection)
                _selection.AnchorOffset = _caret.Offset;
            _selection.ExtendTo(newOffset);
        }
        else
        {
            _selection.ClearSelection(newOffset);
        }

        _caret.Offset = newOffset;
        _caret.ResetBlink();
        EnsureCaretVisible();
        UpdateActiveBracketPair();
        if (oldSelectionStart != _selection.StartOffset || oldSelectionLength != _selection.Length)
            OnSelectionChanged();
        if (oldCaret != _caret.Offset)
            OnCaretPositionChanged();
        InvalidateVisual();
    }

    private void MoveCaretToLineStart(bool extendSelection)
    {
        var line = _document.GetLineByOffset(_caret.Offset);

        // Smart Home: first non-whitespace, then column 0
        var lineText = _document.GetLineText(line.LineNumber);
        int firstNonWhitespace = 0;
        while (firstNonWhitespace < lineText.Length && char.IsWhiteSpace(lineText[firstNonWhitespace]))
            firstNonWhitespace++;

        int currentColumn = _caret.Offset - line.Offset;
        int targetOffset = currentColumn == firstNonWhitespace
            ? line.Offset
            : line.Offset + firstNonWhitespace;

        MoveCaret(targetOffset, extendSelection);
    }

    private void MoveCaretToLineEnd(bool extendSelection)
    {
        var line = _document.GetLineByOffset(_caret.Offset);
        MoveCaret(line.Offset + line.Length, extendSelection);
    }

    private int MoveToWordBoundary(int offset, int direction)
    {
        if (_document.TextLength == 0) return 0;
        offset = Math.Clamp(offset, 0, _document.TextLength);

        if (direction > 0)
        {
            // Move right: skip current word chars, then skip whitespace
            while (offset < _document.TextLength && IsWordChar(_document.GetCharAt(offset)))
                offset++;
            while (offset < _document.TextLength && !IsWordChar(_document.GetCharAt(offset)))
                offset++;
        }
        else
        {
            // Move left: skip whitespace, then skip word chars
            if (offset > 0) offset--;
            while (offset > 0 && !IsWordChar(_document.GetCharAt(offset)))
                offset--;
            while (offset > 0 && IsWordChar(_document.GetCharAt(offset - 1)))
                offset--;
        }

        return offset;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private void EnsureCaretVisible()
    {
        EnsureViewLayoutMetrics();
        UpdateScrollBarLayout(RenderSize);
        if (_view.ViewportWidth <= 0 || _view.ViewportHeight <= 0)
        {
            _view.VerticalOffset = 0;
            _view.HorizontalOffset = 0;
            return;
        }

        var caretPoint = _view.GetPointFromOffset(_caret.Offset, ShowLineNumbers);

        // Vertical scrolling
        if (caretPoint.Y < 0)
        {
            _view.VerticalOffset += caretPoint.Y;
        }
        else if (caretPoint.Y + _view.LineHeight > _view.ViewportHeight)
        {
            _view.VerticalOffset += caretPoint.Y + _view.LineHeight - _view.ViewportHeight;
        }

        // Horizontal scrolling
        double textAreaLeft = ShowLineNumbers ? _view.TextAreaLeft : 0;
        if (caretPoint.X < textAreaLeft)
        {
            _view.HorizontalOffset -= textAreaLeft - caretPoint.X;
        }
        else if (caretPoint.X > _view.ViewportWidth - 20)
        {
            _view.HorizontalOffset += caretPoint.X - _view.ViewportWidth + 40;
        }

        ClampScrollOffsetsToViewport();

        UpdateImeWindowIfComposing();
    }

    #endregion

    #region Text Editing

    private void InsertText(string text)
    {
        if (IsReadOnly) return;

        int oldSelectionStart = _selection.StartOffset;
        int oldSelectionLength = _selection.Length;
        int oldCaret = _caret.Offset;
        _document.BeginUpdate();

        if (_selection.HasSelection)
        {
            _document.Replace(_selection.StartOffset, _selection.Length, text);
            _caret.Offset = _selection.StartOffset + text.Length;
        }
        else
        {
            _document.Insert(_caret.Offset, text);
            _caret.Offset += text.Length;
        }

        _document.EndUpdate();

        _selection.ClearSelection(_caret.Offset);
        _caret.DesiredColumn = -1;
        _caret.ResetBlink();
        EnsureCaretVisible();
        UpdateActiveBracketPair();
        if (oldSelectionStart != _selection.StartOffset || oldSelectionLength != _selection.Length)
            OnSelectionChanged();
        if (oldCaret != _caret.Offset)
            OnCaretPositionChanged();
        InvalidateVisual();
    }

    private void HandleBackspace(bool ctrl)
    {
        if (IsReadOnly) return;

        if (_selection.HasSelection)
        {
            DeleteSelection();
            return;
        }

        if (_caret.Offset == 0) return;

        int deleteFrom = ctrl ? MoveToWordBoundary(_caret.Offset, -1) : _caret.Offset - 1;
        int length = _caret.Offset - deleteFrom;

        _document.Remove(deleteFrom, length);
        int oldCaret = _caret.Offset;
        _caret.Offset = deleteFrom;

        _selection.ClearSelection(_caret.Offset);
        _caret.DesiredColumn = -1;
        _caret.ResetBlink();
        EnsureCaretVisible();
        UpdateActiveBracketPair();
        if (oldCaret != _caret.Offset)
            OnCaretPositionChanged();
        OnSelectionChanged();
        InvalidateVisual();
    }

    private void HandleDelete(bool ctrl)
    {
        if (IsReadOnly) return;

        if (_selection.HasSelection)
        {
            DeleteSelection();
            return;
        }

        if (_caret.Offset >= _document.TextLength) return;

        int deleteTo = ctrl ? MoveToWordBoundary(_caret.Offset, 1) : _caret.Offset + 1;
        int length = deleteTo - _caret.Offset;

        _document.Remove(_caret.Offset, length);

        _selection.ClearSelection(_caret.Offset);
        _caret.DesiredColumn = -1;
        _caret.ResetBlink();
        UpdateActiveBracketPair();
        OnSelectionChanged();
        InvalidateVisual();
    }

    private void HandleEnter()
    {
        if (IsReadOnly) return;

        // Auto-indent: copy leading whitespace from current line
        var line = _document.GetLineByOffset(_caret.Offset);
        var lineText = _document.GetLineText(line.LineNumber);
        int indent = 0;
        while (indent < lineText.Length && (lineText[indent] == ' ' || lineText[indent] == '\t'))
            indent++;

        var insertText = "\n" + lineText[..indent];
        InsertText(insertText);
    }

    private void HandleTab(bool shift)
    {
        if (IsReadOnly) return;

        if (_selection.HasSelection)
        {
            HandleTabWithSelection(shift);
            return;
        }

        if (shift)
        {
            // Unindent current line
            var line = _document.GetLineByOffset(_caret.Offset);
            var lineText = _document.GetLineText(line.LineNumber);

            int removeCount = 0;
            if (ConvertTabsToSpaces)
            {
                int spaces = 0;
                while (spaces < TabSize && spaces < lineText.Length && lineText[spaces] == ' ')
                    spaces++;
                removeCount = spaces;
            }
            else if (lineText.Length > 0 && lineText[0] == '\t')
            {
                removeCount = 1;
            }

            if (removeCount > 0)
            {
                _document.Remove(line.Offset, removeCount);
                _caret.Offset = Math.Max(line.Offset, _caret.Offset - removeCount);
                _selection.ClearSelection(_caret.Offset);
                _caret.DesiredColumn = -1;
                _caret.ResetBlink();
                InvalidateVisual();
            }
        }
        else
        {
            // Insert tab/spaces
            var tabText = ConvertTabsToSpaces ? new string(' ', TabSize) : "\t";
            InsertText(tabText);
        }
    }

    private void HandleTabWithSelection(bool shift)
    {
        int startOffset = _selection.StartOffset;
        int endOffset = _selection.EndOffset;
        int startLine = _document.GetLineByOffset(startOffset).LineNumber;
        int endLine = GetSelectionEndLine();
        int caretOffset = _caret.Offset;

        string tabText = ConvertTabsToSpaces ? new string(' ', Math.Max(1, TabSize)) : "\t";
        int tabLength = tabText.Length;

        _document.BeginUpdate();
        for (int lineNumber = startLine; lineNumber <= endLine; lineNumber++)
        {
            var line = _document.GetLineByNumber(lineNumber);

            if (!shift)
            {
                int insertOffset = line.Offset;
                _document.Insert(insertOffset, tabText);

                if (insertOffset <= startOffset) startOffset += tabLength;
                if (insertOffset < endOffset) endOffset += tabLength;
                if (insertOffset <= caretOffset) caretOffset += tabLength;
                continue;
            }

            var lineText = _document.GetLineText(lineNumber);
            int removeCount = GetLineUnindentCount(lineText);
            if (removeCount <= 0)
                continue;

            _document.Remove(line.Offset, removeCount);

            if (line.Offset < startOffset) startOffset -= Math.Min(removeCount, startOffset - line.Offset);
            if (line.Offset < endOffset) endOffset -= Math.Min(removeCount, endOffset - line.Offset);
            if (line.Offset < caretOffset) caretOffset -= Math.Min(removeCount, caretOffset - line.Offset);
        }
        _document.EndUpdate();

        startOffset = Math.Clamp(startOffset, 0, _document.TextLength);
        endOffset = Math.Clamp(endOffset, startOffset, _document.TextLength);
        caretOffset = Math.Clamp(caretOffset, 0, _document.TextLength);

        _selection.SetSelection(startOffset, endOffset - startOffset);
        _caret.Offset = caretOffset;
        _caret.DesiredColumn = -1;
        _caret.ResetBlink();
        EnsureCaretVisible();
        UpdateActiveBracketPair();
        OnSelectionChanged();
        OnCaretPositionChanged();
        InvalidateVisual();
    }

    private int GetLineUnindentCount(string lineText)
    {
        if (lineText.Length == 0)
            return 0;

        if (ConvertTabsToSpaces)
        {
            int spaces = 0;
            int maxSpaces = Math.Max(1, TabSize);
            while (spaces < maxSpaces && spaces < lineText.Length && lineText[spaces] == ' ')
                spaces++;
            return spaces;
        }

        return lineText[0] == '\t' ? 1 : 0;
    }

    private void DeleteSelection()
    {
        if (!_selection.HasSelection) return;

        int oldSelectionStart = _selection.StartOffset;
        int oldSelectionLength = _selection.Length;
        int oldCaret = _caret.Offset;
        int start = _selection.StartOffset;
        _document.Remove(start, _selection.Length);
        _caret.Offset = start;
        _selection.ClearSelection(start);
        _caret.DesiredColumn = -1;
        _caret.ResetBlink();
        EnsureCaretVisible();
        UpdateActiveBracketPair();
        if (oldSelectionStart != _selection.StartOffset || oldSelectionLength != _selection.Length)
            OnSelectionChanged();
        if (oldCaret != _caret.Offset)
            OnCaretPositionChanged();
        InvalidateVisual();
    }

    #endregion

    #region Clipboard

    public void Copy()
    {
        TryCopySelectionToClipboard();
    }

    public void Cut()
    {
        if (IsReadOnly || !_selection.HasSelection) return;
        if (TryCopySelectionToClipboard())
            DeleteSelection();
    }

    public void Paste()
    {
        if (IsReadOnly) return;

        string? text;
        try
        {
            text = Jalium.UI.Interop.Clipboard.GetText();
        }
        catch
        {
            return;
        }

        if (!string.IsNullOrEmpty(text))
            InsertText(text);
    }

    private bool TryCopySelectionToClipboard()
    {
        if (!_selection.HasSelection)
            return false;

        try
        {
            var text = _selection.GetSelectedText(_document);
            if (_behaviorOptions.PreserveLineEndingsOnCopy || IsFeatureEnabled(EditFeature.PreserveCopyLineEndings))
                text = NormalizeLineEndingsForDocument(text);

            return Jalium.UI.Interop.Clipboard.SetText(text);
        }
        catch
        {
            return false;
        }
    }

    private string NormalizeLineEndingsForDocument(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        string fullText = _document.Text;
        bool usesCrLf = fullText.IndexOf("\r\n", StringComparison.Ordinal) >= 0;
        if (usesCrLf)
        {
            var normalizedLf = text.Replace("\r\n", "\n", StringComparison.Ordinal);
            return normalizedLf.Replace("\n", "\r\n", StringComparison.Ordinal);
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public void Undo()
    {
        _document.UndoStack.Undo(_document);
        SyncCaretAfterUndoRedo();
    }

    public void Redo()
    {
        _document.UndoStack.Redo(_document);
        SyncCaretAfterUndoRedo();
    }

    private void SyncCaretAfterUndoRedo()
    {
        int oldCaret = _caret.Offset;
        int oldSelectionStart = _selection.StartOffset;
        int oldSelectionLength = _selection.Length;
        _caret.Offset = Math.Clamp(_caret.Offset, 0, _document.TextLength);
        _selection.ClearSelection(_caret.Offset);
        _caret.DesiredColumn = -1;
        _caret.ResetBlink();
        _view.InvalidateVisibleLines();
        UpdateActiveBracketPair();
        if (oldSelectionStart != _selection.StartOffset || oldSelectionLength != _selection.Length)
            OnSelectionChanged();
        if (oldCaret != _caret.Offset)
            OnCaretPositionChanged();
        EnsureCaretVisible();
        InvalidateVisual();
    }

    public void SelectAll()
    {
        int oldSelectionStart = _selection.StartOffset;
        int oldSelectionLength = _selection.Length;
        int oldCaret = _caret.Offset;
        _selection.SelectAll(_document);
        _caret.Offset = _document.TextLength;
        _caret.ResetBlink();
        if (oldSelectionStart != _selection.StartOffset || oldSelectionLength != _selection.Length)
            OnSelectionChanged();
        if (oldCaret != _caret.Offset)
            OnCaretPositionChanged();
        InvalidateVisual();
    }

    #endregion

    #region Word Selection

    private (int start, int length) GetWordRangeAtOffset(int offset)
    {
        if (_document.TextLength == 0)
            return (0, 0);

        offset = Math.Clamp(offset, 0, _document.TextLength - 1);
        if (!IsWordChar(_document.GetCharAt(offset)))
            return (offset, 1);

        int start = offset;
        int end = offset;
        while (start > 0 && IsWordChar(_document.GetCharAt(start - 1)))
            start--;
        while (end < _document.TextLength && IsWordChar(_document.GetCharAt(end)))
            end++;

        return (start, end - start);
    }

    private void SelectNextOccurrenceByWord()
    {
        if (_document.TextLength == 0)
            return;

        string needle;
        int fromOffset;

        if (_selection.HasSelection)
        {
            needle = _selection.GetSelectedText(_document);
            fromOffset = _selection.EndOffset;
        }
        else
        {
            var (start, length) = GetWordRangeAtOffset(_caret.Offset >= _document.TextLength
                ? Math.Max(0, _document.TextLength - 1)
                : _caret.Offset);
            if (length <= 0)
                return;

            needle = _document.GetText(start, length);
            Select(start, length);
            fromOffset = start + length;
        }

        if (string.IsNullOrEmpty(needle))
            return;

        var text = _document.Text;
        int found = text.IndexOf(needle, fromOffset, StringComparison.Ordinal);
        if (found < 0 && fromOffset > 0)
            found = text.IndexOf(needle, 0, StringComparison.Ordinal);

        if (found >= 0)
            Select(found, needle.Length);
    }

    private void SelectWordAt(int offset)
    {
        if (_document.TextLength == 0) return;
        offset = Math.Clamp(offset, 0, _document.TextLength - 1);

        int oldSelectionStart = _selection.StartOffset;
        int oldSelectionLength = _selection.Length;
        int oldCaret = _caret.Offset;
        var (start, length) = GetWordRangeAtOffset(offset);
        int end = start + length;

        _selection.SetSelection(start, length);
        _caret.Offset = end;
        if (oldSelectionStart != _selection.StartOffset || oldSelectionLength != _selection.Length)
            OnSelectionChanged();
        if (oldCaret != _caret.Offset)
            OnCaretPositionChanged();
    }

    #endregion

    #region Public API

    /// <summary>
    /// Scrolls the view to make the specified line visible.
    /// </summary>
    public void ScrollToLine(int lineNumber)
    {
        if (lineNumber < 1 || lineNumber > _document.LineCount) return;
        EnsureViewLayoutMetrics();
        UpdateScrollBarLayout(RenderSize);
        _view.VerticalOffset = _view.GetAbsoluteLineTop(lineNumber);
        ClampScrollOffsetsToViewport();
        UpdateImeWindowIfComposing();
        InvalidateVisual();
    }

    /// <summary>
    /// Selects the specified range of text.
    /// </summary>
    public void Select(int start, int length)
    {
        start = Math.Clamp(start, 0, _document.TextLength);
        length = Math.Clamp(length, 0, _document.TextLength - start);

        int oldSelectionStart = _selection.StartOffset;
        int oldSelectionLength = _selection.Length;
        int oldCaret = _caret.Offset;
        _selection.SetSelection(start, length);
        _caret.Offset = start + length;
        _caret.ResetBlink();
        EnsureCaretVisible();
        UpdateActiveBracketPair();
        if (oldSelectionStart != _selection.StartOffset || oldSelectionLength != _selection.Length)
            OnSelectionChanged();
        if (oldCaret != _caret.Offset)
            OnCaretPositionChanged();
        InvalidateVisual();
    }

    /// <summary>
    /// Selects the specified range of text.
    /// </summary>
    public void SetSelection(int start, int length) => Select(start, length);

    public void MoveCaretLeft(bool extendSelection = false)
    {
        MoveCaret(_caret.Offset - 1, extendSelection);
        _caret.DesiredColumn = -1;
    }

    public void MoveCaretRight(bool extendSelection = false)
    {
        MoveCaret(_caret.Offset + 1, extendSelection);
        _caret.DesiredColumn = -1;
    }

    public void MoveCaretUp(bool extendSelection = false)
    {
        MoveCaretVertically(-1, extendSelection);
    }

    public void MoveCaretDown(bool extendSelection = false)
    {
        MoveCaretVertically(1, extendSelection);
    }

    public void MoveCaretWordLeft(bool extendSelection = false)
    {
        MoveCaret(MoveToWordBoundary(_caret.Offset, -1), extendSelection);
        _caret.DesiredColumn = -1;
    }

    public void MoveCaretWordRight(bool extendSelection = false)
    {
        MoveCaret(MoveToWordBoundary(_caret.Offset, 1), extendSelection);
        _caret.DesiredColumn = -1;
    }

    public void MoveCaretToDocumentStart(bool extendSelection = false)
    {
        MoveCaret(0, extendSelection);
        _caret.DesiredColumn = -1;
    }

    public void MoveCaretToDocumentEnd(bool extendSelection = false)
    {
        MoveCaret(_document.TextLength, extendSelection);
        _caret.DesiredColumn = -1;
    }

    public void MoveCaretToStartOfLine(bool extendSelection = false)
    {
        MoveCaretToLineStart(extendSelection);
        _caret.DesiredColumn = -1;
    }

    public void MoveCaretToEndOfLine(bool extendSelection = false)
    {
        MoveCaretToLineEnd(extendSelection);
        _caret.DesiredColumn = -1;
    }

    public void MoveCaretPageUp(bool extendSelection = false)
    {
        MoveCaretVertically(-Math.Max(1, _view.VisibleLineCount), extendSelection);
    }

    public void MoveCaretPageDown(bool extendSelection = false)
    {
        MoveCaretVertically(Math.Max(1, _view.VisibleLineCount), extendSelection);
    }

    public void SelectCurrentWord()
    {
        if (_document.TextLength == 0)
            return;

        int offset = _caret.Offset;
        if (offset >= _document.TextLength)
            offset = _document.TextLength - 1;

        SelectWordAt(offset);
        _caret.ResetBlink();
        EnsureCaretVisible();
        UpdateActiveBracketPair();
        InvalidateVisual();
    }

    public void SelectCurrentLine()
    {
        var line = _document.GetLineByOffset(_caret.Offset);
        Select(line.Offset, line.Length);
    }

    public void ClearSelection()
    {
        if (!_selection.HasSelection)
            return;

        int oldSelectionStart = _selection.StartOffset;
        int oldSelectionLength = _selection.Length;
        _selection.ClearSelection(_caret.Offset);
        if (oldSelectionStart != _selection.StartOffset || oldSelectionLength != _selection.Length)
            OnSelectionChanged();
        InvalidateVisual();
    }

    public void DeleteLeft() => HandleBackspace(ctrl: false);

    public void DeleteRight() => HandleDelete(ctrl: false);

    public void DeleteWordLeft() => HandleBackspace(ctrl: true);

    public void DeleteWordRight() => HandleDelete(ctrl: true);

    public void InsertNewLine() => HandleEnter();

    public void InsertTab() => HandleTab(shift: false);

    public void Unindent() => HandleTab(shift: true);

    public void ScrollLineUp() => ScrollVerticallyBy(-Math.Max(1, _view.LineHeight));

    public void ScrollLineDown() => ScrollVerticallyBy(Math.Max(1, _view.LineHeight));

    public void ScrollPageUp()
    {
        double page = _effectiveViewportHeight > 0
            ? _effectiveViewportHeight
            : Math.Max(1, _view.LineHeight * Math.Max(1, _view.VisibleLineCount));
        ScrollVerticallyBy(-Math.Max(page, Math.Max(1, _view.LineHeight)));
    }

    public void ScrollPageDown()
    {
        double page = _effectiveViewportHeight > 0
            ? _effectiveViewportHeight
            : Math.Max(1, _view.LineHeight * Math.Max(1, _view.VisibleLineCount));
        ScrollVerticallyBy(Math.Max(page, Math.Max(1, _view.LineHeight)));
    }

    private void ScrollVerticallyBy(double delta)
    {
        EnsureViewLayoutMetrics();
        UpdateScrollBarLayout(RenderSize);
        if (_view.ViewportHeight <= 0)
            return;

        _view.VerticalOffset += delta;
        ClampScrollOffsetsToViewport();
        UpdateImeWindowIfComposing();
        InvalidateVisual();
    }

    public void ScrollToOffset(int offset)
    {
        EnsureViewLayoutMetrics();
        UpdateScrollBarLayout(RenderSize);
        if (_view.ViewportWidth <= 0 || _view.ViewportHeight <= 0)
            return;

        offset = Math.Clamp(offset, 0, _document.TextLength);
        var point = _view.GetPointFromOffset(offset, ShowLineNumbers);
        if (point.Y < 0)
            _view.VerticalOffset += point.Y;
        else if (point.Y + _view.LineHeight > _view.ViewportHeight)
            _view.VerticalOffset += point.Y + _view.LineHeight - _view.ViewportHeight;

        double textAreaLeft = ShowLineNumbers ? _view.TextAreaLeft : 0;
        if (point.X < textAreaLeft)
            _view.HorizontalOffset -= textAreaLeft - point.X;
        else if (point.X > _view.ViewportWidth - 20)
            _view.HorizontalOffset += point.X - _view.ViewportWidth + 40;

        ClampScrollOffsetsToViewport();
        UpdateImeWindowIfComposing();
        InvalidateVisual();
    }

    public void ScrollToCaret()
    {
        EnsureCaretVisible();
        InvalidateVisual();
    }

    /// <summary>
    /// Loads text into the document, replacing all existing content.
    /// </summary>
    public void LoadText(string text)
    {
        _document.Text = text;
        _caret.Offset = 0;
        _selection.ClearSelection(0);
        _view.VerticalOffset = 0;
        _view.HorizontalOffset = 0;
        _view.InvalidateVisibleLines();
        InvalidateSemanticOccurrenceCaches();
        _activeFindResult = null;
        _hasSearchQuery = false;
        EnsureViewLayoutMetrics();
        UpdateScrollBarLayout(RenderSize);
        UpdateActiveBracketPair();
        UpdateFoldingState();
        OnSelectionChanged();
        OnCaretPositionChanged();
        InvalidateVisual();
    }

    public void DuplicateLineOrSelection()
    {
        if (IsReadOnly)
            return;

        if (_selection.HasSelection)
        {
            int selectionStart = _selection.StartOffset;
            string selectionText = _selection.GetSelectedText(_document);
            int selectionInsertOffset = _selection.EndOffset;
            _document.Insert(selectionInsertOffset, selectionText);
            Select(selectionInsertOffset, selectionText.Length);
            return;
        }

        var line = _document.GetLineByOffset(_caret.Offset);
        int column = _caret.Offset - line.Offset;
        string lineText = _document.GetText(line.Offset, line.Length);
        string insertText = line.DelimiterLength > 0
            ? _document.GetText(line.Offset, line.TotalLength)
            : "\n" + lineText;

        int insertOffset = line.Offset + line.TotalLength;
        _document.Insert(insertOffset, insertText);
        _caret.Offset = Math.Clamp(insertOffset + column, 0, _document.TextLength);
        _selection.ClearSelection(_caret.Offset);
        _caret.DesiredColumn = -1;
        _caret.ResetBlink();
        UpdateActiveBracketPair();
        OnSelectionChanged();
        OnCaretPositionChanged();
        EnsureCaretVisible();
        InvalidateVisual();
    }

    public void DeleteLineOrSelection()
    {
        if (IsReadOnly)
            return;

        if (_selection.HasSelection)
        {
            DeleteSelection();
            return;
        }

        var line = _document.GetLineByOffset(_caret.Offset);
        int removeStart = line.Offset;
        int removeLength = line.TotalLength;

        if (line.DelimiterLength == 0 && line.Offset > 0)
        {
            removeStart--;
            if (removeStart > 0 && _document.GetCharAt(removeStart - 1) == '\r' && _document.GetCharAt(removeStart) == '\n')
                removeStart--;
            removeLength = line.TotalLength + (line.Offset - removeStart);
        }

        if (removeLength <= 0)
            return;

        _document.Remove(removeStart, removeLength);
        _caret.Offset = Math.Clamp(removeStart, 0, _document.TextLength);
        _selection.ClearSelection(_caret.Offset);
        _caret.DesiredColumn = -1;
        _caret.ResetBlink();
        UpdateActiveBracketPair();
        OnSelectionChanged();
        OnCaretPositionChanged();
        EnsureCaretVisible();
        InvalidateVisual();
    }

    public bool MoveLineUp()
    {
        if (IsReadOnly)
            return false;

        var (firstLine, lastLine) = GetSelectedLineRange();
        if (firstLine <= 1)
            return false;

        var above = _document.GetLineByNumber(firstLine - 1);
        var startLine = _document.GetLineByNumber(firstLine);
        var endLine = _document.GetLineByNumber(lastLine);

        string aboveText = _document.GetText(above.Offset, above.TotalLength);
        string blockText = _document.GetText(startLine.Offset, endLine.Offset + endLine.TotalLength - startLine.Offset);
        _document.Replace(above.Offset, aboveText.Length + blockText.Length, blockText + aboveText);

        int delta = aboveText.Length;
        _caret.Offset = Math.Clamp(_caret.Offset - delta, 0, _document.TextLength);
        if (_selection.HasSelection)
        {
            int start = Math.Clamp(_selection.StartOffset - delta, 0, _document.TextLength);
            int end = Math.Clamp(_selection.EndOffset - delta, 0, _document.TextLength);
            _selection.SetSelection(start, Math.Max(0, end - start));
            OnSelectionChanged();
        }
        else
        {
            _selection.ClearSelection(_caret.Offset);
            OnSelectionChanged();
        }

        _caret.ResetBlink();
        _caret.DesiredColumn = -1;
        EnsureCaretVisible();
        UpdateActiveBracketPair();
        OnCaretPositionChanged();
        InvalidateVisual();
        return true;
    }

    public bool MoveLineDown()
    {
        if (IsReadOnly)
            return false;

        var (firstLine, lastLine) = GetSelectedLineRange();
        if (lastLine >= _document.LineCount)
            return false;

        var startLine = _document.GetLineByNumber(firstLine);
        var endLine = _document.GetLineByNumber(lastLine);
        var below = _document.GetLineByNumber(lastLine + 1);

        string blockText = _document.GetText(startLine.Offset, endLine.Offset + endLine.TotalLength - startLine.Offset);
        string belowText = _document.GetText(below.Offset, below.TotalLength);
        _document.Replace(startLine.Offset, blockText.Length + belowText.Length, belowText + blockText);

        int delta = belowText.Length;
        _caret.Offset = Math.Clamp(_caret.Offset + delta, 0, _document.TextLength);
        if (_selection.HasSelection)
        {
            int start = Math.Clamp(_selection.StartOffset + delta, 0, _document.TextLength);
            int end = Math.Clamp(_selection.EndOffset + delta, 0, _document.TextLength);
            _selection.SetSelection(start, Math.Max(0, end - start));
            OnSelectionChanged();
        }
        else
        {
            _selection.ClearSelection(_caret.Offset);
            OnSelectionChanged();
        }

        _caret.ResetBlink();
        _caret.DesiredColumn = -1;
        EnsureCaretVisible();
        UpdateActiveBracketPair();
        OnCaretPositionChanged();
        InvalidateVisual();
        return true;
    }

    public void CommentSelection()
    {
        string? linePrefix = GetLineCommentPrefix(Language);
        if (linePrefix != null)
        {
            CommentLines(linePrefix);
            return;
        }

        WrapSelectionWithBlockComment();
    }

    public void UncommentSelection()
    {
        string? linePrefix = GetLineCommentPrefix(Language);
        if (linePrefix != null)
        {
            UncommentLines(linePrefix);
            return;
        }

        UnwrapSelectionFromBlockComment();
    }

    public void ToggleLineComment()
    {
        string? linePrefix = GetLineCommentPrefix(Language);
        if (linePrefix == null)
        {
            WrapSelectionWithBlockComment();
            return;
        }

        var (firstLine, lastLine) = GetSelectedLineRange();
        bool allCommented = true;
        for (int lineNumber = firstLine; lineNumber <= lastLine; lineNumber++)
        {
            var lineText = _document.GetLineText(lineNumber);
            if (!lineText.StartsWith(linePrefix, StringComparison.Ordinal))
            {
                allCommented = false;
                break;
            }
        }

        if (allCommented) UncommentLines(linePrefix);
        else CommentLines(linePrefix);
    }

    public IReadOnlyList<FindResult> FindAll(string text, bool caseSensitive = false, bool wholeWord = false, bool useRegex = false)
    {
        _findReplace.SearchText = text ?? string.Empty;
        _findReplace.CaseSensitive = caseSensitive;
        _findReplace.WholeWord = wholeWord;
        _findReplace.UseRegex = useRegex;
        _findReplace.FindAll();
        _activeFindResult = _findReplace.CurrentResult;
        _hasSearchQuery = !string.IsNullOrEmpty(_findReplace.SearchText);
        SearchResultsChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        return _findReplace.Results;
    }

    public FindResult? FindNext()
    {
        if (string.IsNullOrEmpty(_findReplace.SearchText))
            return null;

        int fromOffset = _selection.HasSelection ? _selection.EndOffset : _caret.Offset;
        var result = _findReplace.FindNext(fromOffset);
        if (result.HasValue)
        {
            _activeFindResult = result;
            Select(result.Value.Offset, result.Value.Length);
            SearchResultsChanged?.Invoke(this, EventArgs.Empty);
        }

        InvalidateVisual();
        return result;
    }

    public FindResult? FindPrevious()
    {
        if (string.IsNullOrEmpty(_findReplace.SearchText))
            return null;

        int fromOffset = _selection.HasSelection ? _selection.StartOffset : _caret.Offset;
        var result = _findReplace.FindPrevious(fromOffset);
        if (result.HasValue)
        {
            _activeFindResult = result;
            Select(result.Value.Offset, result.Value.Length);
            SearchResultsChanged?.Invoke(this, EventArgs.Empty);
        }

        InvalidateVisual();
        return result;
    }

    public bool ReplaceCurrent(string replacement)
    {
        bool replaced = _findReplace.ReplaceCurrent(replacement ?? string.Empty);
        if (replaced)
        {
            _activeFindResult = _findReplace.CurrentResult;
            SearchResultsChanged?.Invoke(this, EventArgs.Empty);
            EnsureCaretVisible();
            UpdateActiveBracketPair();
            InvalidateVisual();
        }

        return replaced;
    }

    public int ReplaceAll(string replacement)
    {
        int replacedCount = _findReplace.ReplaceAll(replacement ?? string.Empty);
        if (replacedCount > 0)
        {
            _activeFindResult = _findReplace.CurrentResult;
            SearchResultsChanged?.Invoke(this, EventArgs.Empty);
            EnsureCaretVisible();
            UpdateActiveBracketPair();
            InvalidateVisual();
        }

        return replacedCount;
    }

    public bool ToggleFold(int lineNumber)
    {
        int oldSelectionStart = _selection.StartOffset;
        int oldSelectionLength = _selection.Length;
        int oldCaret = _caret.Offset;

        bool result = _foldingManager.ToggleFold(lineNumber);
        if (result)
        {
            CompleteFoldingStateChange(oldSelectionStart, oldSelectionLength, oldCaret);
        }

        return result;
    }

    public void FoldAll()
    {
        int oldSelectionStart = _selection.StartOffset;
        int oldSelectionLength = _selection.Length;
        int oldCaret = _caret.Offset;

        _foldingManager.CollapseAll();
        CompleteFoldingStateChange(oldSelectionStart, oldSelectionLength, oldCaret);
    }

    public void UnfoldAll()
    {
        int oldSelectionStart = _selection.StartOffset;
        int oldSelectionLength = _selection.Length;
        int oldCaret = _caret.Offset;

        _foldingManager.ExpandAll();
        CompleteFoldingStateChange(oldSelectionStart, oldSelectionLength, oldCaret);
    }

    private static bool IsMutatingCommand(string commandId)
    {
        return commandId switch
        {
            EditorCommands.Cut or
            EditorCommands.Paste or
            EditorCommands.Replace or
            EditorCommands.ReplaceNext or
            EditorCommands.ReplaceSelection or
            EditorCommands.ReplaceAll or
            EditorCommands.IndentLine or
            EditorCommands.UnindentLine or
            EditorCommands.CommentLine or
            EditorCommands.UncommentLine or
            EditorCommands.ToggleLineComment or
            EditorCommands.DuplicateLine or
            EditorCommands.DuplicateLineOrSelection or
            EditorCommands.DeleteLine or
            EditorCommands.DeleteLineOrSelection or
            EditorCommands.MoveLineUp or
            EditorCommands.MoveLineDown or
            EditorCommands.DeleteLeft or
            EditorCommands.DeleteRight or
            EditorCommands.DeleteWordLeft or
            EditorCommands.DeleteWordRight or
            EditorCommands.InsertNewLine or
            EditorCommands.InsertTab or
            EditorCommands.Unindent => true,
            _ => false
        };
    }

    public bool CanExecuteEditorCommand(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId))
            return false;

        if (!EditorCommands.Metadata.ContainsKey(commandId))
            return false;

        if (IsMutatingCommand(commandId) && IsReadOnly)
            return false;

        return commandId switch
        {
            EditorCommands.Undo => CanUndo,
            EditorCommands.Redo => CanRedo,
            EditorCommands.Cut => _selection.HasSelection,
            EditorCommands.Copy => _selection.HasSelection,
            EditorCommands.SearchFromSelection => _selection.HasSelection,
            EditorCommands.FindNext or EditorCommands.FindPrevious or
            EditorCommands.FindAll or
            EditorCommands.FindNextResult or EditorCommands.FindPreviousResult or
            EditorCommands.NextSearchResult or EditorCommands.PreviousSearchResult => !string.IsNullOrEmpty(_findReplace.SearchText),
            EditorCommands.Replace or EditorCommands.ReplaceNext or EditorCommands.ReplaceSelection or EditorCommands.ReplaceAll => !string.IsNullOrEmpty(_findReplace.SearchText),
            _ => true
        };
    }

    public bool ExecuteEditorCommand(string commandId)
    {
        if (!CanExecuteEditorCommand(commandId))
            return false;

        switch (commandId)
        {
            case EditorCommands.Undo:
                Undo();
                return true;
            case EditorCommands.Redo:
                Redo();
                return true;
            case EditorCommands.Cut:
                Cut();
                return true;
            case EditorCommands.Copy:
                Copy();
                return true;
            case EditorCommands.Paste:
                Paste();
                return true;
            case EditorCommands.SelectAll:
                SelectAll();
                return true;
            case EditorCommands.SelectAllLines:
                Select(0, _document.TextLength);
                return true;
            case EditorCommands.Find:
                HandleFindShortcut();
                return true;
            case EditorCommands.FindAll:
                _findReplace.FindAll();
                _activeFindResult = _findReplace.CurrentResult;
                _hasSearchQuery = !string.IsNullOrEmpty(_findReplace.SearchText);
                SearchResultsChanged?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
                return true;
            case EditorCommands.FindNext:
                return FindNext().HasValue;
            case EditorCommands.FindPrevious:
                return FindPrevious().HasValue;
            case EditorCommands.FindNextResult:
            case EditorCommands.NextSearchResult:
                return FindNext().HasValue;
            case EditorCommands.FindPreviousResult:
            case EditorCommands.PreviousSearchResult:
                return FindPrevious().HasValue;
            case EditorCommands.Replace:
                HandleReplaceShortcut();
                return true;
            case EditorCommands.ReplaceNext:
            {
                string? clipboardText;
                try
                {
                    clipboardText = Jalium.UI.Interop.Clipboard.GetText();
                }
                catch
                {
                    return false;
                }

                if (!ReplaceCurrent(clipboardText ?? string.Empty))
                    return false;
                FindNext();
                return true;
            }
            case EditorCommands.SearchFromSelection:
                FindAll(_selection.GetSelectedText(_document));
                return true;
            case EditorCommands.GoToLine:
                ScrollToCaret();
                return true;
            case EditorCommands.GoToMatchingBracket:
                if (!_activeBracketPair.HasValue)
                    return false;
                MoveCaret(_activeBracketPair.Value.matchOffset, extendSelection: false);
                _caret.DesiredColumn = -1;
                return true;
            case EditorCommands.SelectMatchingBracket:
                if (!_activeBracketPair.HasValue)
                    return false;
                int anchor = _activeBracketPair.Value.bracketOffset;
                int match = _activeBracketPair.Value.matchOffset;
                int start = Math.Min(anchor, match);
                int length = Math.Max(0, Math.Abs(anchor - match) + 1);
                Select(start, length);
                return true;
            case EditorCommands.ToggleFold:
                ToggleFold(CaretLine);
                return true;
            case EditorCommands.FoldAll:
                FoldAll();
                return true;
            case EditorCommands.UnfoldAll:
                UnfoldAll();
                return true;
            case EditorCommands.IndentLine:
                HandleTab(shift: false);
                return true;
            case EditorCommands.UnindentLine:
                HandleTab(shift: true);
                return true;
            case EditorCommands.CommentLine:
                CommentSelection();
                return true;
            case EditorCommands.UncommentLine:
                UncommentSelection();
                return true;
            case EditorCommands.ToggleLineComment:
                ToggleLineComment();
                return true;
            case EditorCommands.DuplicateLine:
            case EditorCommands.DuplicateLineOrSelection:
                DuplicateLineOrSelection();
                return true;
            case EditorCommands.DeleteLine:
            case EditorCommands.DeleteLineOrSelection:
                DeleteLineOrSelection();
                return true;
            case EditorCommands.MoveLineUp:
                return MoveLineUp();
            case EditorCommands.MoveLineDown:
                return MoveLineDown();
            case EditorCommands.CaretLeft:
                MoveCaretLeft();
                return true;
            case EditorCommands.CaretRight:
                MoveCaretRight();
                return true;
            case EditorCommands.CaretUp:
                MoveCaretUp();
                return true;
            case EditorCommands.CaretDown:
                MoveCaretDown();
                return true;
            case EditorCommands.WordLeft:
                MoveCaretWordLeft();
                return true;
            case EditorCommands.WordRight:
                MoveCaretWordRight();
                return true;
            case EditorCommands.LineStart:
                MoveCaretToStartOfLine();
                return true;
            case EditorCommands.LineEnd:
                MoveCaretToEndOfLine();
                return true;
            case EditorCommands.DocumentStart:
                MoveCaretToDocumentStart();
                return true;
            case EditorCommands.DocumentEnd:
                MoveCaretToDocumentEnd();
                return true;
            case EditorCommands.PageUp:
                MoveCaretPageUp();
                return true;
            case EditorCommands.PageDown:
                MoveCaretPageDown();
                return true;
            case EditorCommands.SelectLeft:
                MoveCaretLeft(extendSelection: true);
                return true;
            case EditorCommands.SelectRight:
                MoveCaretRight(extendSelection: true);
                return true;
            case EditorCommands.SelectUp:
                MoveCaretUp(extendSelection: true);
                return true;
            case EditorCommands.SelectDown:
                MoveCaretDown(extendSelection: true);
                return true;
            case EditorCommands.SelectWordLeft:
                MoveCaretWordLeft(extendSelection: true);
                return true;
            case EditorCommands.SelectWordRight:
                MoveCaretWordRight(extendSelection: true);
                return true;
            case EditorCommands.SelectLineStart:
                MoveCaretToStartOfLine(extendSelection: true);
                return true;
            case EditorCommands.SelectLineEnd:
                MoveCaretToEndOfLine(extendSelection: true);
                return true;
            case EditorCommands.SelectDocumentStart:
                MoveCaretToDocumentStart(extendSelection: true);
                return true;
            case EditorCommands.SelectDocumentEnd:
                MoveCaretToDocumentEnd(extendSelection: true);
                return true;
            case EditorCommands.SelectPageUp:
                MoveCaretPageUp(extendSelection: true);
                return true;
            case EditorCommands.SelectPageDown:
                MoveCaretPageDown(extendSelection: true);
                return true;
            case EditorCommands.SelectCurrentWord:
                SelectCurrentWord();
                return true;
            case EditorCommands.SelectCurrentLine:
                SelectCurrentLine();
                return true;
            case EditorCommands.ClearSelection:
                ClearSelection();
                return true;
            case EditorCommands.DeleteLeft:
                DeleteLeft();
                return true;
            case EditorCommands.DeleteRight:
                DeleteRight();
                return true;
            case EditorCommands.DeleteWordLeft:
                DeleteWordLeft();
                return true;
            case EditorCommands.DeleteWordRight:
                DeleteWordRight();
                return true;
            case EditorCommands.InsertNewLine:
                InsertNewLine();
                return true;
            case EditorCommands.InsertTab:
                InsertTab();
                return true;
            case EditorCommands.Unindent:
                Unindent();
                return true;
            case EditorCommands.ScrollLineUp:
                ScrollLineUp();
                return true;
            case EditorCommands.ScrollLineDown:
                ScrollLineDown();
                return true;
            case EditorCommands.ScrollPageUp:
                ScrollPageUp();
                return true;
            case EditorCommands.ScrollPageDown:
                ScrollPageDown();
                return true;
            default:
                return false;
        }
    }

    #endregion

    #region IME Support

    public Point GetImeCaretPosition()
    {
        var caretPos = _view.GetPointFromOffset(_caret.Offset, ShowLineNumbers);
        caretPos = new Point(caretPos.X, caretPos.Y + _view.LineHeight);

        // Transform to window coordinates
        var element = this as UIElement;
        var parent = element?.VisualParent;
        while (parent != null)
        {
            if (parent is FrameworkElement fe)
                caretPos = new Point(caretPos.X + fe.Margin.Left, caretPos.Y + fe.Margin.Top);
            if (parent is Window)
                break;
            parent = parent.VisualParent;
        }

        return caretPos;
    }

    public void OnImeCompositionStart()
    {
        _isImeComposing = true;
        _imeCompositionStart = _caret.Offset;
        _imeCompositionString = string.Empty;
        _imeCompositionCursor = 0;

        if (_selection.HasSelection)
            DeleteSelection();

        UpdateImeWindowIfComposing();
        InvalidateVisual();
    }

    public void OnImeCompositionUpdate(string compositionString, int cursorPosition)
    {
        _imeCompositionString = compositionString;
        _imeCompositionCursor = cursorPosition;
        UpdateImeWindowIfComposing();
        InvalidateVisual();
    }

    public void OnImeCompositionEnd(string? resultString)
    {
        _isImeComposing = false;
        _imeCompositionString = string.Empty;
        _imeCompositionCursor = 0;
        _imeCompositionStart = _caret.Offset;
        InvalidateVisual();
    }

    private void OnImeCompositionStarted(object? sender, EventArgs e)
    {
        if (InputMethod.Current == this)
            OnImeCompositionStart();
    }

    private void OnImeCompositionUpdated(object? sender, CompositionEventArgs e)
    {
        if (InputMethod.Current == this)
            OnImeCompositionUpdate(e.Text, e.CursorPosition);
    }

    private void OnImeCompositionEnded(object? sender, CompositionResultEventArgs e)
    {
        if (InputMethod.Current == this)
            OnImeCompositionEnd(e.Result);
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EditControl editor)
        {
            var newText = (string)(e.NewValue ?? string.Empty);
            // Avoid recursive update if document already has this text
            if (editor._document.Text != newText)
            {
                editor._document.Text = newText;
            }

            editor._caret.CoerceToDocument(editor._document);
            editor._selection.AnchorOffset = Math.Clamp(editor._selection.AnchorOffset, 0, editor._document.TextLength);
            editor._selection.ActiveOffset = Math.Clamp(editor._selection.ActiveOffset, 0, editor._document.TextLength);
            editor.UpdateActiveBracketPair();
            editor.EnsureViewLayoutMetrics();
            editor.UpdateScrollBarLayout(editor.RenderSize);
            editor._view.InvalidateVisibleLines();
            editor.InvalidateVisual();
        }
    }

    private static void OnSyntaxHighlighterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EditControl editor)
        {
            editor.DetachReactiveHighlighter(e.OldValue as IReactiveSyntaxHighlighter);
            editor._view.Highlighter = e.NewValue as ISyntaxHighlighter;
            editor.AttachReactiveHighlighter(e.NewValue as IReactiveSyntaxHighlighter);
            editor._view.InvalidateVisibleLines();
            editor.InvalidateVisual();
        }
    }

    private static void OnDocumentFilePathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EditControl editor)
            editor.RefreshReactiveHighlighterContext();
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EditControl editor)
            editor.InvalidateVisual();
    }

    private static void OnLanguageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is EditControl editor)
        {
            editor.ApplyLanguageDefaults((string)(e.NewValue ?? "plaintext"));
            editor.UpdateFoldingState();
            editor.InvalidateVisual();
        }
    }

    private void OnDocumentChanged(object? sender, TextChangeEventArgs e)
    {
        // Sync the Text DP without triggering a re-parse
        var currentText = _document.Text;
        if (Text != currentText)
            SetValue(TextProperty, currentText);

        _caret.CoerceToDocument(_document);
        _selection.AnchorOffset = Math.Clamp(_selection.AnchorOffset, 0, _document.TextLength);
        _selection.ActiveOffset = Math.Clamp(_selection.ActiveOffset, 0, _document.TextLength);

        int changedLineNumber;
        if (_document.TextLength <= 0)
        {
            changedLineNumber = 1;
        }
        else
        {
            int lookupOffset = Math.Min(Math.Clamp(e.Offset, 0, _document.TextLength), _document.TextLength - 1);
            changedLineNumber = _document.GetLineByOffset(lookupOffset).LineNumber;
        }

        _view.InvalidateFromLine(changedLineNumber);
        _reactiveSyntaxHighlighter?.NotifyDocumentChanged(e);
        InvalidateSemanticOccurrenceCaches();
        UpdateActiveBracketPair();
        ScheduleFoldingRefreshFromDocumentChange();
        EnsureViewLayoutMetrics();
        UpdateScrollBarLayout(RenderSize);

        if (_hasSearchQuery && !string.IsNullOrEmpty(_findReplace.SearchText))
        {
            _findReplace.FindAll();
            _activeFindResult = _findReplace.CurrentResult;
            SearchResultsChanged?.Invoke(this, EventArgs.Empty);
        }

        TextChanged?.Invoke(this, e);
        InvalidateVisual();
    }

    private void OnUndoStateChanged(object? sender, EventArgs e)
    {
        InvalidateVisual();
    }

    #endregion

    #region Rendering Helpers

    private void DrawSelectedTextOccurrences(DrawingContext dc)
    {
        if (!_selection.HasSelection || _selection.Length <= 0 || _view.LineHeight <= 0)
            return;

        if (_selection.Length > 256)
            return;

        string selectedText = _selection.GetSelectedText(_document);
        if (string.IsNullOrWhiteSpace(selectedText))
            return;

        var matches = GetSelectionOccurrences(selectedText);
        if (matches.Count == 0)
            return;

        int selectionStart = _selection.StartOffset;
        int selectionLength = _selection.Length;

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            if (match.Offset == selectionStart && match.Length == selectionLength)
                continue;

            DrawDocumentRangeHighlight(dc, match.Offset, match.Length, s_selectedTextOccurrenceBrush);
        }
    }

    private void DrawCaretSymbolOccurrences(DrawingContext dc)
    {
        if (_selection.HasSelection || _view.LineHeight <= 0 || SyntaxHighlighter == null)
            return;

        if (!TryGetCaretSymbolForHighlight(out string symbol))
            return;

        if (symbol.Length > 128)
            return;

        var matches = GetSymbolOccurrences(symbol);
        if (matches.Count <= 1)
            return;

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            DrawDocumentRangeHighlight(dc, match.Offset, match.Length, s_symbolOccurrenceBrush);
        }
    }

    private bool TryGetCaretSymbolForHighlight(out string symbol)
    {
        symbol = string.Empty;
        if (SyntaxHighlighter == null || _document.TextLength == 0)
            return false;

        if (!_view.TryGetTokenAtOffset(_caret.Offset, out int lineNumber, out var token, out var lineText))
            return false;

        if (!IsSymbolHighlightClassification(token.Classification) || token.Length <= 0)
            return false;

        var line = _document.GetLineByNumber(lineNumber);
        int caretColumn = Math.Clamp(_caret.Offset - line.Offset, 0, lineText.Length);
        int tokenStart = Math.Clamp(token.StartOffset, 0, lineText.Length);
        int tokenEnd = Math.Clamp(token.StartOffset + token.Length, tokenStart, lineText.Length);
        if (tokenStart >= tokenEnd)
            return false;

        int probe = Math.Clamp(caretColumn, tokenStart, tokenEnd - 1);
        if (caretColumn == tokenEnd && tokenEnd > tokenStart)
            probe = tokenEnd - 1;

        if (probe < 0 || probe >= lineText.Length)
            return false;

        if (!IsSymbolCharacter(lineText[probe]))
        {
            if (token.Classification != TokenClassification.PlainText)
            {
                symbol = lineText.Substring(tokenStart, tokenEnd - tokenStart).Trim();
                return symbol.Length > 0;
            }

            return false;
        }

        int left = probe;
        while (left > tokenStart && IsSymbolCharacter(lineText[left - 1]))
            left--;

        int right = probe + 1;
        while (right < tokenEnd && right < lineText.Length && IsSymbolCharacter(lineText[right]))
            right++;

        if (right <= left)
            return false;

        symbol = lineText.Substring(left, right - left);
        return symbol.Length > 0;
    }

    private static bool IsSymbolHighlightClassification(TokenClassification classification)
    {
        return classification switch
        {
            TokenClassification.Keyword or
            TokenClassification.ControlKeyword or
            TokenClassification.String or
            TokenClassification.Character or
            TokenClassification.Number or
            TokenClassification.Comment or
            TokenClassification.XmlDoc or
            TokenClassification.Preprocessor or
            TokenClassification.Operator or
            TokenClassification.Punctuation or
            TokenClassification.BindingKeyword or
            TokenClassification.BindingParameter or
            TokenClassification.BindingOperator or
            TokenClassification.Error => false,
            _ => true
        };
    }

    private IReadOnlyList<FindResult> GetSelectionOccurrences(string selectedText)
    {
        if (_cachedSelectionOccurrenceVersion == _document.Version &&
            string.Equals(_cachedSelectionOccurrenceQuery, selectedText, StringComparison.Ordinal))
        {
            return _cachedSelectionOccurrences;
        }

        _cachedSelectionOccurrenceQuery = selectedText;
        _cachedSelectionOccurrenceVersion = _document.Version;
        _cachedSelectionOccurrences.Clear();
        FillOccurrences(selectedText, wholeWord: false, _cachedSelectionOccurrences);
        return _cachedSelectionOccurrences;
    }

    private IReadOnlyList<FindResult> GetSymbolOccurrences(string symbol)
    {
        if (_cachedSymbolOccurrenceVersion == _document.Version &&
            string.Equals(_cachedSymbolOccurrenceQuery, symbol, StringComparison.Ordinal))
        {
            return _cachedSymbolOccurrences;
        }

        _cachedSymbolOccurrenceQuery = symbol;
        _cachedSymbolOccurrenceVersion = _document.Version;
        _cachedSymbolOccurrences.Clear();
        FillOccurrences(symbol, wholeWord: true, _cachedSymbolOccurrences);
        return _cachedSymbolOccurrences;
    }

    private void FillOccurrences(string query, bool wholeWord, List<FindResult> target)
    {
        if (string.IsNullOrEmpty(query))
            return;

        string text = _document.Text;
        int position = 0;
        while (position < text.Length && target.Count < MaxSemanticHighlightMatches)
        {
            int found = text.IndexOf(query, position, StringComparison.Ordinal);
            if (found < 0)
                break;

            if (!wholeWord || IsWholeWordOccurrence(text, found, query.Length))
                target.Add(new FindResult(found, query.Length));

            position = found + Math.Max(1, query.Length);
        }
    }

    private static bool IsWholeWordOccurrence(string text, int offset, int length)
    {
        if (offset > 0 && IsSymbolCharacter(text[offset - 1]))
            return false;

        int end = offset + length;
        if (end < text.Length && IsSymbolCharacter(text[end]))
            return false;

        return true;
    }

    private static bool IsSymbolCharacter(char c) =>
        char.IsLetterOrDigit(c) || c is '_' or '-';

    private void DrawSearchHighlights(DrawingContext dc)
    {
        if (_findReplace.Results.Count == 0 || _view.LineHeight <= 0)
            return;

        for (int i = 0; i < _findReplace.Results.Count; i++)
        {
            var result = _findReplace.Results[i];
            bool isActive = _activeFindResult.HasValue &&
                _activeFindResult.Value.Offset == result.Offset &&
                _activeFindResult.Value.Length == result.Length;
            DrawDocumentRangeHighlight(dc, result.Offset, result.Length, isActive ? s_defaultActiveSearchResultBrush : s_defaultSearchResultBrush);
        }
    }

    private void DrawBracketHighlights(DrawingContext dc)
    {
        if (!_activeBracketPair.HasValue || _view.LineHeight <= 0 || _view.CharWidth <= 0)
            return;

        DrawSingleCharacterHighlight(dc, _activeBracketPair.Value.bracketOffset);
        DrawSingleCharacterHighlight(dc, _activeBracketPair.Value.matchOffset);
    }

    private void DrawSingleCharacterHighlight(DrawingContext dc, int offset)
    {
        if (offset < 0 || offset >= _document.TextLength)
            return;

        var line = _document.GetLineByOffset(offset);
        if (!_view.TryGetLineTop(line.LineNumber, out double y))
            return;

        if (y + _view.LineHeight < 0 || y > RenderSize.Height)
            return;

        var point = _view.GetPointFromOffset(offset, ShowLineNumbers);
        double textAreaLeft = ShowLineNumbers ? _view.TextAreaLeft : 0;
        double x = Math.Max(textAreaLeft, point.X);
        if (x > RenderSize.Width)
            return;

        dc.DrawRectangle(s_defaultBracketHighlightBrush, s_defaultBracketHighlightPen, new Rect(x, y, _view.CharWidth, _view.LineHeight));
    }

    private void DrawScopeGuides(DrawingContext dc, double contentWidth, double contentHeight)
    {
        if (_foldingManager.Foldings.Count == 0 || _view.LineHeight <= 0 || _view.CharWidth <= 0 ||
            contentWidth <= 0 || contentHeight <= 0)
        {
            return;
        }

        int firstVisibleLine = _view.FirstVisibleLineNumber;
        int lastVisibleLine = _view.LastVisibleLineNumber;
        var hoveredSection = _hoveredScopeGuideSection;
        var foldings = _foldingManager.Foldings;
        for (int i = 0; i < foldings.Count; i++)
        {
            var section = foldings[i];
            if (section.StartLine > lastVisibleLine + 1)
                break;

            GetScopeGuideLineRange(section, out int scopeStartLine, out int scopeEndLine);
            if (scopeEndLine < firstVisibleLine - 1)
                continue;
            if (section.IsFolded || scopeEndLine <= scopeStartLine)
                continue;

            if (!TryGetScopeGuideGeometry(section, contentWidth, contentHeight, out double guideX, out double guideStartY, out double guideEndY))
                continue;

            var guidePen = ReferenceEquals(section, hoveredSection) ? s_scopeGuideActivePen : s_scopeGuidePen;
            dc.DrawLine(guidePen, new Point(guideX, guideStartY), new Point(guideX, guideEndY));
        }
    }

    private bool UpdateHoveredScopeGuide(Point pointerPosition, double contentWidth, double contentHeight)
    {
        var hovered = FindScopeGuideAt(pointerPosition, contentWidth, contentHeight);
        if (ReferenceEquals(hovered, _hoveredScopeGuideSection))
            return false;

        _hoveredScopeGuideSection = hovered;
        return true;
    }

    private bool UpdateHoveredFoldedHint(Point pointerPosition, double contentWidth, double contentHeight)
    {
        var hovered = FindFoldedHintAt(pointerPosition, contentWidth, contentHeight);
        if (ReferenceEquals(hovered, _hoveredFoldedHintSection))
            return false;

        _hoveredFoldedHintSection = hovered;
        return true;
    }

    private FoldingSection? FindFoldedHintAt(Point pointerPosition, double contentWidth, double contentHeight)
    {
        return TryGetFoldedSectionHintAt(pointerPosition, contentWidth, contentHeight, out var section, out _)
            ? section
            : null;
    }

    private FoldingSection? FindScopeGuideAt(Point pointerPosition, double contentWidth, double contentHeight)
    {
        if (_foldingManager.Foldings.Count == 0 || _view.LineHeight <= 0 || _view.CharWidth <= 0 ||
            contentWidth <= 0 || contentHeight <= 0)
        {
            return null;
        }

        double textAreaLeft = ShowLineNumbers ? _view.TextAreaLeft : 0;
        if (pointerPosition.X < textAreaLeft || pointerPosition.X > contentWidth || pointerPosition.Y < 0 || pointerPosition.Y > contentHeight)
            return null;

        int hoveredLine = _view.GetLineNumberFromY(pointerPosition.Y);
        int firstVisibleLine = _view.FirstVisibleLineNumber;
        int lastVisibleLine = _view.LastVisibleLineNumber;

        double bestDistance = ScopeGuideHoverTolerance + 0.001;
        int bestSpan = int.MaxValue;
        FoldingSection? bestSection = null;

        var foldings = _foldingManager.Foldings;
        for (int i = 0; i < foldings.Count; i++)
        {
            var section = foldings[i];
            if (section.StartLine > lastVisibleLine + 1)
                break;

            GetScopeGuideLineRange(section, out int scopeStartLine, out int scopeEndLine);
            if (scopeEndLine < firstVisibleLine - 1)
                continue;
            if (section.IsFolded || hoveredLine < scopeStartLine || hoveredLine > scopeEndLine)
                continue;

            if (!TryGetScopeGuideGeometry(section, contentWidth, contentHeight, out double guideX, out double guideStartY, out double guideEndY))
                continue;

            if (pointerPosition.Y < guideStartY - ScopeGuideHoverTolerance || pointerPosition.Y > guideEndY + ScopeGuideHoverTolerance)
                continue;

            double distance = Math.Abs(pointerPosition.X - guideX);
            if (distance > ScopeGuideHoverTolerance)
                continue;

            int span = scopeEndLine - scopeStartLine;
            if (distance < bestDistance || (Math.Abs(distance - bestDistance) <= 0.1 && span < bestSpan))
            {
                bestDistance = distance;
                bestSpan = span;
                bestSection = section;
            }
        }

        return bestSection;
    }

    private void GetScopeGuideLineRange(FoldingSection section, out int scopeStartLine, out int scopeEndLine)
    {
        int lineCount = _document.LineCount;
        if (lineCount <= 0)
        {
            scopeStartLine = 1;
            scopeEndLine = 1;
            return;
        }

        int rawStartLine = section.GuideStartLine > 0 ? section.GuideStartLine : section.StartLine;
        int rawEndLine = section.GuideEndLine > 0 ? section.GuideEndLine : section.EndLine;
        scopeStartLine = Math.Clamp(rawStartLine, 1, lineCount);
        scopeEndLine = Math.Clamp(rawEndLine, scopeStartLine, lineCount);
    }

    private bool TryGetScopeGuideGeometry(FoldingSection section, double contentWidth, double contentHeight, out double guideX, out double guideStartY, out double guideEndY)
    {
        guideX = 0;
        guideStartY = 0;
        guideEndY = 0;

        if (_view.LineHeight <= 0 || _view.CharWidth <= 0)
            return false;

        GetScopeGuideLineRange(section, out int scopeStartLine, out int scopeEndLine);
        if (scopeEndLine <= scopeStartLine)
            return false;

        if (!_view.TryGetLineTop(scopeStartLine, out double startLineTop))
            return false;
        if (!_view.TryGetLineTop(scopeEndLine, out double endLineTop))
            return false;

        int guideColumn = ResolveScopeGuideColumn(section);
        double textAreaLeft = ShowLineNumbers ? _view.TextAreaLeft : 0;
        guideX = textAreaLeft + (guideColumn + 0.5) * _view.CharWidth - _view.HorizontalOffset;
        if (guideX < textAreaLeft - ScopeGuideHoverTolerance || guideX > contentWidth + ScopeGuideHoverTolerance)
            return false;

        guideStartY = startLineTop + _view.LineHeight + ScopeGuideInnerStartInset;
        guideEndY = endLineTop - ScopeGuideInnerEndInset;
        if (guideEndY < 0 || guideStartY > contentHeight)
            return false;

        guideStartY = Math.Clamp(guideStartY, 0, contentHeight);
        guideEndY = Math.Clamp(guideEndY, 0, contentHeight);
        return guideEndY > guideStartY + 0.5;
    }

    private int ResolveScopeGuideColumn(FoldingSection section)
    {
        int lineCount = _document.LineCount;
        if (lineCount <= 0)
            return 0;

        string normalizedLanguage = (Language ?? "plaintext").ToLowerInvariant();
        if (IsXmlLikeLanguage(normalizedLanguage) && section.StartColumn >= 0)
            return section.StartColumn;

        GetScopeGuideLineRange(section, out int startLine, out _);
        string startLineText = _document.GetLineText(startLine);
        int preferredColumn = startLine == section.StartLine ? section.StartColumn : -1;
        int braceColumn = FindOpeningBraceColumn(startLineText, preferredColumn);
        if (braceColumn >= 0)
            return braceColumn;

        if (startLine < lineCount)
        {
            string nextLineText = _document.GetLineText(startLine + 1);
            braceColumn = FindOpeningBraceColumn(nextLineText, -1);
            if (braceColumn >= 0)
                return braceColumn;

            int nextIndent = CountLeadingWhitespace(nextLineText);
            if (nextIndent < nextLineText.Length)
                return nextIndent;
        }

        int startIndent = CountLeadingWhitespace(startLineText);
        if (startIndent < startLineText.Length)
            return startIndent;

        return Math.Max(0, section.StartColumn);
    }

    private static int FindOpeningBraceColumn(string lineText, int preferredColumn)
    {
        if (string.IsNullOrEmpty(lineText))
            return -1;

        if (preferredColumn >= 0 && preferredColumn < lineText.Length && lineText[preferredColumn] == '{')
            return preferredColumn;

        if (preferredColumn >= 0 && preferredColumn < lineText.Length)
        {
            int rightSearch = lineText.IndexOf('{', preferredColumn);
            if (rightSearch >= 0)
                return rightSearch;
        }

        return lineText.IndexOf('{');
    }

    private static int CountLeadingWhitespace(string text)
    {
        int index = 0;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;
        return index;
    }

    private bool DrawFoldedSectionHoverTooltip(DrawingContext dc, double contentWidth, double contentHeight, string fontFamily, double fontSize)
    {
        if (!_hasPointerPosition || _hoveredFoldedHintSection == null || contentWidth <= 0 || contentHeight <= 0)
            return false;

        string previewText = GetFoldedSectionPreviewText(_hoveredFoldedHintSection);
        if (string.IsNullOrEmpty(previewText))
            return false;

        var tooltip = new FormattedText(previewText, fontFamily, Math.Max(11, fontSize - 1))
        {
            Foreground = s_scopeGuideTooltipTextBrush,
            MaxTextWidth = Math.Max(24, contentWidth - ScopeGuideTooltipPaddingX * 2 - 8),
            MaxTextHeight = Math.Max(24, contentHeight - ScopeGuideTooltipPaddingY * 2 - 8),
            Trimming = TextTrimming.None
        };
        TextMeasurement.MeasureText(tooltip);

        double tooltipWidth = Math.Max(24, Math.Ceiling(tooltip.Width + ScopeGuideTooltipPaddingX * 2));
        double tooltipHeight = Math.Max(20, Math.Ceiling(tooltip.Height + ScopeGuideTooltipPaddingY * 2));
        double tooltipX = _lastPointerPosition.X + ScopeGuideTooltipOffsetX;
        double tooltipY = _lastPointerPosition.Y + ScopeGuideTooltipOffsetY;

        if (tooltipX + tooltipWidth > contentWidth - 2)
            tooltipX = Math.Max(2, contentWidth - tooltipWidth - 2);

        if (tooltipY + tooltipHeight > contentHeight - 2)
            tooltipY = Math.Max(2, _lastPointerPosition.Y - tooltipHeight - ScopeGuideTooltipOffsetY);

        if (tooltipY < 2)
            tooltipY = Math.Max(2, Math.Min(contentHeight - tooltipHeight - 2, _lastPointerPosition.Y + ScopeGuideTooltipOffsetY));

        var tooltipRect = new Rect(tooltipX, tooltipY, tooltipWidth, tooltipHeight);
        dc.DrawRoundedRectangle(s_scopeGuideTooltipBackgroundBrush, s_scopeGuideTooltipBorderPen, tooltipRect, 3, 3);
        dc.DrawText(tooltip, new Point(tooltipRect.X + ScopeGuideTooltipPaddingX, tooltipRect.Y + ScopeGuideTooltipPaddingY));
        return true;
    }

    private string GetFoldedSectionPreviewText(FoldingSection section)
    {
        int lineCount = _document.LineCount;
        if (lineCount <= 0)
            return string.Empty;

        int firstPreviewLine = Math.Clamp(section.StartLine + 1, 1, lineCount);
        int lastPreviewLine = Math.Clamp(section.EndLine, firstPreviewLine, lineCount);
        int maxLineCount = Math.Max(1, FoldedHintPreviewMaxLines);

        var lines = new List<string>(Math.Min(maxLineCount, lastPreviewLine - firstPreviewLine + 1));
        for (int lineNumber = firstPreviewLine; lineNumber <= lastPreviewLine && lines.Count < maxLineCount; lineNumber++)
            lines.Add(_document.GetLineText(lineNumber));

        if (lines.Count == 0)
            lines.Add(_document.GetLineText(Math.Clamp(section.StartLine, 1, lineCount)));

        return string.Join('\n', lines);
    }

    private void DrawScopeGuideHoverTooltip(DrawingContext dc, double contentWidth, double contentHeight, string fontFamily, double fontSize)
    {
        if (!_hasPointerPosition || _hoveredScopeGuideSection == null || contentWidth <= 0 || contentHeight <= 0)
            return;

        string tooltipText = GetScopeGuideTooltipText(_hoveredScopeGuideSection);
        if (string.IsNullOrWhiteSpace(tooltipText))
            return;

        var tooltip = new FormattedText(tooltipText, fontFamily, Math.Max(11, fontSize - 1))
        {
            Foreground = s_scopeGuideTooltipTextBrush
        };
        TextMeasurement.MeasureText(tooltip);

        double tooltipWidth = Math.Max(24, Math.Ceiling(tooltip.Width + ScopeGuideTooltipPaddingX * 2));
        double tooltipHeight = Math.Max(Math.Ceiling(_view.LineHeight - 2), Math.Ceiling(tooltip.Height + ScopeGuideTooltipPaddingY * 2));
        double tooltipX = _lastPointerPosition.X + ScopeGuideTooltipOffsetX;
        double tooltipY = _lastPointerPosition.Y - tooltipHeight - ScopeGuideTooltipOffsetY;

        if (tooltipX + tooltipWidth > contentWidth - 2)
            tooltipX = Math.Max(2, contentWidth - tooltipWidth - 2);

        if (tooltipY < 2)
            tooltipY = Math.Min(Math.Max(2, contentHeight - tooltipHeight - 2), _lastPointerPosition.Y + ScopeGuideTooltipOffsetY);

        if (tooltipY + tooltipHeight > contentHeight - 2)
            tooltipY = Math.Max(2, contentHeight - tooltipHeight - 2);

        var tooltipRect = new Rect(tooltipX, tooltipY, tooltipWidth, tooltipHeight);
        dc.DrawRoundedRectangle(s_scopeGuideTooltipBackgroundBrush, s_scopeGuideTooltipBorderPen, tooltipRect, 3, 3);
        double textX = tooltipRect.X + ScopeGuideTooltipPaddingX;
        double textY = tooltipRect.Y + (tooltipRect.Height - tooltip.Height) * 0.5;
        dc.DrawText(tooltip, new Point(textX, textY));
    }

    private string GetScopeGuideTooltipText(FoldingSection section)
    {
        var scopeChain = BuildScopeGuideHierarchy(section);
        if (scopeChain.Count == 0)
            return ResolveScopeGuideTooltipLabel(section);

        var lines = new string[scopeChain.Count];
        for (int i = 0; i < scopeChain.Count; i++)
        {
            string indent = new string(' ', i * 4);
            lines[i] = indent + ResolveScopeGuideTooltipLabel(scopeChain[i]);
        }

        return string.Join('\n', lines);
    }

    private List<FoldingSection> BuildScopeGuideHierarchy(FoldingSection section)
    {
        var hierarchy = new List<FoldingSection>();
        var foldings = _foldingManager.Foldings;
        for (int i = 0; i < foldings.Count; i++)
        {
            var candidate = foldings[i];
            if (candidate.StartLine > section.StartLine || candidate.EndLine < section.EndLine)
                continue;

            hierarchy.Add(candidate);
        }

        hierarchy.Sort(static (a, b) =>
        {
            int byStart = a.StartLine.CompareTo(b.StartLine);
            if (byStart != 0)
                return byStart;

            int byEndDescending = b.EndLine.CompareTo(a.EndLine);
            if (byEndDescending != 0)
                return byEndDescending;

            return a.StartColumn.CompareTo(b.StartColumn);
        });

        if (hierarchy.Count == 0)
            return hierarchy;

        var unique = new List<FoldingSection>(hierarchy.Count);
        for (int i = 0; i < hierarchy.Count; i++)
        {
            var current = hierarchy[i];
            if (unique.Count == 0)
            {
                unique.Add(current);
                continue;
            }

            var previous = unique[unique.Count - 1];
            if (previous.StartLine == current.StartLine &&
                previous.EndLine == current.EndLine &&
                previous.StartColumn == current.StartColumn)
            {
                continue;
            }

            unique.Add(current);
        }

        return unique;
    }

    private string ResolveScopeGuideTooltipLabel(FoldingSection section)
    {
        int lineCount = _document.LineCount;
        if (lineCount <= 0)
            return "{";

        int startLine = Math.Clamp(section.StartLine, 1, lineCount);
        string lineText = _document.GetLineText(startLine);
        string label = TryExtractScopeLabel(lineText, section.StartColumn);

        if (label.Length > 0)
            return label;

        if (startLine < lineCount)
        {
            label = TryExtractScopeLabel(_document.GetLineText(startLine + 1), -1);
            if (label.Length > 0)
                return label;
        }

        if (startLine > 1)
        {
            label = TryExtractScopeLabel(_document.GetLineText(startLine - 1), -1);
            if (label.Length > 0)
                return label;
        }

        string raw = lineText.Trim();
        return raw.Length > 0 ? raw : "{";
    }

    private static string TryExtractScopeLabel(string lineText, int startColumn)
    {
        if (string.IsNullOrEmpty(lineText))
            return string.Empty;

        int cutColumn = Math.Clamp(startColumn, 0, lineText.Length);
        if (startColumn >= 0 && cutColumn > 0)
        {
            string beforeBrace = lineText[..cutColumn].TrimEnd();
            if (!string.IsNullOrWhiteSpace(beforeBrace))
                return beforeBrace.TrimStart();
        }

        string trimmed = lineText.Trim();
        if (trimmed.Length == 0)
            return string.Empty;

        if (trimmed == "{")
            return string.Empty;

        return trimmed;
    }

    private void DrawDocumentRangeHighlight(DrawingContext dc, int offset, int length, Brush brush)
    {
        if (length <= 0 || _document.TextLength == 0)
            return;

        int start = Math.Clamp(offset, 0, _document.TextLength);
        int end = Math.Clamp(offset + length, 0, _document.TextLength);
        if (end <= start)
            return;

        var startLine = _document.GetLineByOffset(start);
        var endLine = _document.GetLineByOffset(end - 1);

        for (int lineNumber = startLine.LineNumber; lineNumber <= endLine.LineNumber; lineNumber++)
        {
            if (!_view.IsLineVisible(lineNumber))
                continue;

            var line = _document.GetLineByNumber(lineNumber);
            int segStart = lineNumber == startLine.LineNumber ? start : line.Offset;
            int segEnd = lineNumber == endLine.LineNumber ? end : line.Offset + line.Length;

            if (!_view.TryGetLineTop(line.LineNumber, out double y))
                continue;

            if (y + _view.LineHeight < 0 || y > RenderSize.Height)
                continue;

            var p1 = _view.GetPointFromOffset(segStart, ShowLineNumbers);
            var p2 = _view.GetPointFromOffset(segEnd, ShowLineNumbers);
            double textAreaLeft = ShowLineNumbers ? _view.TextAreaLeft : 0;
            double x1 = Math.Max(textAreaLeft, p1.X);
            double x2 = segEnd >= line.Offset + line.Length && lineNumber != endLine.LineNumber
                ? RenderSize.Width
                : Math.Max(x1 + 1, p2.X);

            if (x1 >= RenderSize.Width)
                continue;

            dc.DrawRectangle(brush, null, new Rect(x1, y, Math.Max(0, x2 - x1), _view.LineHeight));
        }
    }

    private void DrawFoldedSectionHints(DrawingContext dc, double contentWidth, double contentHeight, string fontFamily, double fontSize)
    {
        if (_foldingManager.Foldings.Count == 0 || _view.LineHeight <= 0 || _view.CharWidth <= 0 ||
            contentWidth <= 0 || contentHeight <= 0)
        {
            return;
        }

        int firstVisibleLine = _view.FirstVisibleLineNumber;
        int lastVisibleLine = _view.LastVisibleLineNumber;
        int previousStartLine = -1;
        var foldings = _foldingManager.Foldings;
        for (int i = 0; i < foldings.Count; i++)
        {
            var section = foldings[i];
            if (section.StartLine < firstVisibleLine - 1)
                continue;
            if (section.StartLine > lastVisibleLine + 1)
                break;

            if (!section.IsFolded)
                continue;
            if (section.StartLine == previousStartLine)
                continue;
            previousStartLine = section.StartLine;

            string hintTextValue = GetFoldedSectionHintText(section);
            var foldedText = CreateFoldedSectionHintLayout(hintTextValue, fontFamily, fontSize);
            if (!TryGetFoldedSectionHintRect(section, foldedText, contentWidth, contentHeight, out var hintRect))
                continue;

            bool isSelected = IsFoldingSectionSelected(section);
            dc.DrawRoundedRectangle(
                isSelected ? s_foldedHintSelectedBackgroundBrush : s_foldedHintBackgroundBrush,
                isSelected ? s_foldedHintSelectedBorderPen : s_foldedHintBorderPen,
                hintRect,
                2,
                2);

            foldedText.MaxTextWidth = Math.Max(0, hintRect.Width - FoldedHintHorizontalPadding * 2);
            foldedText.MaxTextHeight = Math.Max(0, hintRect.Height - FoldedHintVerticalPadding * 2);
            foldedText.Trimming = TextTrimming.CharacterEllipsis;
            TextMeasurement.MeasureText(foldedText);
            double textX = hintRect.X + FoldedHintHorizontalPadding;
            double textY = hintRect.Y + (hintRect.Height - foldedText.Height) * 0.5;
            dc.DrawText(foldedText, new Point(textX, textY));
        }
    }

    private bool TryGetFoldedSectionHintRect(FoldingSection section, FormattedText hintLayout, double contentWidth, double contentHeight, out Rect hintRect)
    {
        hintRect = Rect.Empty;
        if (!section.IsFolded || _view.LineHeight <= 0 || _view.CharWidth <= 0)
            return false;

        if (!_view.TryGetLineTop(section.StartLine, out double lineTop))
            return false;

        if (lineTop + _view.LineHeight < 0 || lineTop > contentHeight)
            return false;

        var lineText = _document.GetLineText(section.StartLine);
        int previewColumn = GetCollapsedPreviewColumn(section, lineText);
        double textAreaLeft = ShowLineNumbers ? _view.TextAreaLeft : 0;
        double anchorX = textAreaLeft + previewColumn * _view.CharWidth - _view.HorizontalOffset;
        double x = Math.Max(textAreaLeft + 1, anchorX + Math.Max(2, _view.CharWidth * 0.3));
        if (x >= contentWidth)
            return false;

        double measuredWidth = hintLayout.IsMeasured
            ? hintLayout.Width
            : Math.Max(3, hintLayout.Text.Length) * _view.CharWidth;
        double width = Math.Max(14, Math.Ceiling(measuredWidth + FoldedHintHorizontalPadding * 2));
        if (x + width > contentWidth - 1)
            width = Math.Max(0, contentWidth - x - 1);
        if (width < 8)
            return false;

        double maxHintHeight = Math.Max(8, Math.Ceiling(_view.LineHeight - 1));
        double measuredHeight = hintLayout.IsMeasured
            ? hintLayout.Height
            : Math.Max(8, _view.LineHeight - 4);
        double desiredHintHeight = Math.Ceiling(measuredHeight + FoldedHintVerticalPadding * 2);
        double height = Math.Max(8, Math.Min(maxHintHeight, desiredHintHeight));
        double y = lineTop + (_view.LineHeight - height) * 0.5;
        hintRect = new Rect(x, y, width, height);
        return true;
    }

    private static FormattedText CreateFoldedSectionHintLayout(string text, string fontFamily, double fontSize)
    {
        var formatted = new FormattedText(text, fontFamily, Math.Max(10, fontSize - 1))
        {
            Foreground = s_foldedHintTextBrush
        };
        TextMeasurement.MeasureText(formatted);
        return formatted;
    }

    private string GetFoldedSectionHintText(FoldingSection section)
    {
        if (TryGetRegionHintText(section, out string regionText))
            return regionText;

        return "...";
    }

    private bool TryGetRegionHintText(FoldingSection section, out string text)
    {
        text = string.Empty;

        int lineCount = _document.LineCount;
        if (lineCount <= 0)
            return false;

        int startLine = Math.Clamp(section.StartLine, 1, lineCount);
        string lineText = _document.GetLineText(startLine).Trim();

        const string regionToken = "#region";
        if (!lineText.StartsWith(regionToken, StringComparison.OrdinalIgnoreCase))
            return false;

        string regionName = lineText[regionToken.Length..].Trim();
        text = regionName.Length > 0 ? regionName : regionToken;
        return true;
    }

    private static int GetCollapsedPreviewColumn(FoldingSection section, string lineText)
    {
        if (lineText.Length == 0)
            return 0;

        int collapseColumn = section.StartColumn >= 0
            ? Math.Clamp(section.StartColumn, 0, lineText.Length)
            : lineText.Length;

        if (collapseColumn < lineText.Length && lineText[collapseColumn] == '#')
            return collapseColumn;

        int previewColumn = collapseColumn;
        while (previewColumn > 0 && char.IsWhiteSpace(lineText[previewColumn - 1]))
            previewColumn--;

        return previewColumn;
    }

    private void DrawImeComposition(DrawingContext dc, string fontFamily, double fontSize)
    {
        if (!_isImeComposing || string.IsNullOrEmpty(_imeCompositionString) || _view.LineHeight <= 0 || _view.CharWidth <= 0)
            return;

        int startOffset = Math.Clamp(_imeCompositionStart, 0, _document.TextLength);
        var startLine = _document.GetLineByOffset(startOffset);
        if (!_view.TryGetLineTop(startLine.LineNumber, out double y))
            return;

        if (y + _view.LineHeight < 0 || y > RenderSize.Height)
            return;

        var point = _view.GetPointFromOffset(startOffset, ShowLineNumbers);
        double textAreaLeft = ShowLineNumbers ? _view.TextAreaLeft : 0;
        double x = Math.Max(textAreaLeft, point.X);
        double width = Math.Max(_view.CharWidth, _imeCompositionString.Length * _view.CharWidth);

        dc.DrawRectangle(s_imeCompositionBackgroundBrush, null, new Rect(x, y, width, _view.LineHeight));
        var text = new FormattedText(_imeCompositionString, fontFamily, fontSize)
        {
            Foreground = s_imeCompositionTextBrush
        };
        dc.DrawText(text, new Point(x, y));
        dc.DrawLine(s_imeCompositionUnderlinePen, new Point(x, y + _view.LineHeight - 1), new Point(x + width, y + _view.LineHeight - 1));
    }

    private bool ShouldApplyGutterOverflowShield(double contentHeight)
    {
        return ShowLineNumbers && _view.HorizontalOffset > 0 && contentHeight > 0;
    }

    private void DrawGutterOverflowShield(DrawingContext dc, double contentHeight, string fontFamily, double fontSize)
    {
        if (!ShowLineNumbers || _view.HorizontalOffset <= 0 || contentHeight <= 0)
            return;

        double gutterRight = Math.Min(RenderSize.Width, Math.Max(0, _view.TextAreaLeft));
        if (gutterRight <= 0)
            return;

        var shieldRect = new Rect(0, 0, gutterRight, contentHeight);
        if (s_gutterOverflowBlurEffect.HasEffect)
            dc.DrawBackdropEffect(shieldRect, s_gutterOverflowBlurEffect, new CornerRadius(0));

        dc.DrawRectangle(s_gutterOverflowOverlayBrush, null, shieldRect);
        _view.RenderLineNumbers(
            dc,
            _caret,
            Foreground ?? s_defaultForegroundBrush,
            LineNumberForeground ?? s_defaultLineNumberBrush,
            fontFamily,
            fontSize);
    }

    private void DrawFoldingMarkers(DrawingContext dc, double contentHeight)
    {
        if (!ShowLineNumbers || _view.LineHeight <= 0 || _foldingManager.Foldings.Count == 0 || contentHeight <= 0)
            return;

        int firstVisibleLine = _view.FirstVisibleLineNumber;
        int lastVisibleLine = _view.LastVisibleLineNumber;
        int previousStartLine = -1;
        var foldings = _foldingManager.Foldings;
        for (int i = 0; i < foldings.Count; i++)
        {
            var section = foldings[i];
            if (section.StartLine < firstVisibleLine - 1)
                continue;
            if (section.StartLine > lastVisibleLine + 1)
                break;
            if (section.StartLine == previousStartLine)
                continue;
            previousStartLine = section.StartLine;

            if (!_view.TryGetLineTop(section.StartLine, out double lineTop))
                continue;

            if (lineTop + _view.LineHeight < 0 || lineTop > contentHeight)
                continue;

            var markerRect = GetFoldingMarkerRect(lineTop);
            if (markerRect.IsEmpty)
                continue;

            bool isSelected = IsFoldingSectionSelected(section);
            if (isSelected)
                dc.DrawRoundedRectangle(s_foldingMarkerSelectedBackgroundBrush, null, markerRect, 2, 2);

            double centerX = markerRect.X + markerRect.Width * 0.5;
            double centerY = markerRect.Y + markerRect.Height * 0.5;

            if (!section.IsFolded)
            {
                GetScopeGuideLineRange(section, out int scopeStartLine, out int scopeEndLine);
                if (scopeEndLine > scopeStartLine &&
                    _view.TryGetLineTop(scopeStartLine, out double scopeStartLineTop) &&
                    _view.TryGetLineTop(scopeEndLine, out double endLineTop))
                {
                    double guideStartY = scopeStartLineTop + _view.LineHeight + ScopeGuideInnerStartInset;
                    double guideEndY = endLineTop - ScopeGuideInnerEndInset;
                    if (guideEndY > guideStartY + 0.5)
                    {
                        dc.DrawLine(
                            s_foldingGuidePen,
                            new Point(centerX, guideStartY),
                            new Point(centerX, guideEndY));

                        dc.DrawLine(
                            s_foldingGuidePen,
                            new Point(centerX, guideEndY),
                            new Point(centerX + Math.Max(4, markerRect.Width * 0.45), guideEndY));
                    }
                }
            }
            else if (section.IsFolded)
            {
                dc.DrawLine(
                    s_foldingGuidePen,
                    new Point(centerX, centerY),
                    new Point(centerX + Math.Max(3, markerRect.Width * 0.4), centerY));
            }

            DrawFoldingChevron(dc, markerRect, section.IsFolded, isSelected);
        }
    }

    private void DrawFoldingChevron(DrawingContext dc, Rect markerRect, bool folded, bool selected)
    {
        var chevronPen = selected ? s_foldingChevronSelectedPen : s_foldingChevronPen;
        double centerX = markerRect.X + markerRect.Width * 0.5;
        double centerY = markerRect.Y + markerRect.Height * 0.5;
        double glyph = Math.Max(2.4, markerRect.Width * 0.26);

        if (folded)
        {
            dc.DrawLine(
                chevronPen,
                new Point(centerX - glyph * 0.7, centerY - glyph),
                new Point(centerX + glyph * 0.45, centerY));
            dc.DrawLine(
                chevronPen,
                new Point(centerX - glyph * 0.7, centerY + glyph),
                new Point(centerX + glyph * 0.45, centerY));
        }
        else
        {
            dc.DrawLine(
                chevronPen,
                new Point(centerX - glyph, centerY - glyph * 0.55),
                new Point(centerX, centerY + glyph * 0.55));
            dc.DrawLine(
                chevronPen,
                new Point(centerX, centerY + glyph * 0.55),
                new Point(centerX + glyph, centerY - glyph * 0.55));
        }
    }

    private Rect GetFoldingMarkerRect(double lineTop)
    {
        if (!ShowLineNumbers || _view.LineHeight <= 0)
            return Rect.Empty;

        double laneWidth = Math.Max(0, _view.FoldingLaneWidth);
        if (laneWidth <= 2)
            return Rect.Empty;

        double markerSize = Math.Min(FoldingMarkerSize, Math.Max(6, _view.LineHeight - 4));
        markerSize = Math.Min(markerSize, Math.Max(4, laneWidth - 2));

        double x = _view.FoldingLaneLeft + (laneWidth - markerSize) * 0.5;
        double y = lineTop + (_view.LineHeight - markerSize) * 0.5;
        return new Rect(x, y, markerSize, markerSize);
    }

    private void DrawScrollBars(DrawingContext dc)
    {
        static Rect InsetRect(Rect rect, double inset)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return Rect.Empty;

            double insetX = Math.Min(inset, rect.Width * 0.5);
            double insetY = Math.Min(inset, rect.Height * 0.5);
            return new Rect(
                rect.X + insetX,
                rect.Y + insetY,
                Math.Max(0, rect.Width - insetX * 2),
                Math.Max(0, rect.Height - insetY * 2));
        }

        void DrawRoundedBar(Brush brush, Rect rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            double radius = Math.Min(ScrollBarCornerRadius, Math.Min(rect.Width, rect.Height) * 0.5);
            dc.DrawRoundedRectangle(brush, null, rect, radius, radius);
        }

        if (_isVerticalScrollBarVisible)
        {
            DrawRoundedBar(s_scrollBarTrackBrush, InsetRect(_verticalScrollTrackRect, ScrollBarInnerPadding));
            DrawRoundedBar(
                _scrollBarDragMode == ScrollBarDragMode.Vertical ? s_scrollBarActiveThumbBrush : s_scrollBarThumbBrush,
                InsetRect(_verticalScrollThumbRect, ScrollBarInnerPadding));
        }

        if (_isHorizontalScrollBarVisible)
        {
            DrawRoundedBar(s_scrollBarTrackBrush, InsetRect(_horizontalScrollTrackRect, ScrollBarInnerPadding));
            DrawRoundedBar(
                _scrollBarDragMode == ScrollBarDragMode.Horizontal ? s_scrollBarActiveThumbBrush : s_scrollBarThumbBrush,
                InsetRect(_horizontalScrollThumbRect, ScrollBarInnerPadding));
        }

        if (_isVerticalScrollBarVisible && _isHorizontalScrollBarVisible)
        {
            var corner = new Rect(
                Math.Max(0, RenderSize.Width - ScrollBarThickness),
                Math.Max(0, RenderSize.Height - ScrollBarThickness),
                ScrollBarThickness,
                ScrollBarThickness);
            DrawRoundedBar(s_scrollBarTrackBrush, InsetRect(corner, ScrollBarInnerPadding));
        }
    }

    private void EnsureViewLayoutMetrics()
    {
        var fontFamily = FontFamily ?? "Cascadia Code";
        var fontSize = FontSize > 0 ? FontSize : 14;
        _view.UpdateLayout(fontFamily, fontSize);

        if (RenderSize.Width > 0)
            _view.ViewportWidth = RenderSize.Width;
        if (RenderSize.Height > 0)
            _view.ViewportHeight = RenderSize.Height;
    }

    private void UpdateScrollBarLayout(Size viewportSize)
    {
        _verticalScrollTrackRect = Rect.Empty;
        _verticalScrollThumbRect = Rect.Empty;
        _horizontalScrollTrackRect = Rect.Empty;
        _horizontalScrollThumbRect = Rect.Empty;
        _isVerticalScrollBarVisible = false;
        _isHorizontalScrollBarVisible = false;
        _effectiveViewportHeight = Math.Max(0, viewportSize.Height);
        _effectiveTextViewportWidth = Math.Max(0, GetTextViewportWidth(viewportSize.Width));

        if (viewportSize.Width <= 0 || viewportSize.Height <= 0 || _view.LineHeight <= 0 || _view.CharWidth <= 0)
            return;

        bool needVertical = false;
        bool needHorizontal = false;
        for (int i = 0; i < 4; i++)
        {
            double availableWidth = Math.Max(0, viewportSize.Width - (needVertical ? ScrollBarThickness : 0));
            double availableHeight = Math.Max(0, viewportSize.Height - (needHorizontal ? ScrollBarThickness : 0));
            double textViewportWidth = Math.Max(0, GetTextViewportWidth(availableWidth));

            bool nextVertical = GetMaxVerticalOffset(availableHeight) > 0.5;
            bool nextHorizontal = GetDocumentTextContentWidth() > textViewportWidth + 0.5;
            if (nextVertical == needVertical && nextHorizontal == needHorizontal)
                break;

            needVertical = nextVertical;
            needHorizontal = nextHorizontal;
        }

        double finalAvailableWidth = Math.Max(0, viewportSize.Width - (needVertical ? ScrollBarThickness : 0));
        double finalAvailableHeight = Math.Max(0, viewportSize.Height - (needHorizontal ? ScrollBarThickness : 0));
        _effectiveViewportHeight = finalAvailableHeight;
        _effectiveTextViewportWidth = Math.Max(0, GetTextViewportWidth(finalAvailableWidth));

        _isVerticalScrollBarVisible = needVertical;
        _isHorizontalScrollBarVisible = needHorizontal;
        ClampScrollOffsetsToViewport();

        if (_isVerticalScrollBarVisible)
        {
            double trackHeight = finalAvailableHeight;
            _verticalScrollTrackRect = new Rect(
                Math.Max(0, viewportSize.Width - ScrollBarThickness),
                0,
                ScrollBarThickness,
                trackHeight);

            double extent = Math.Max(1, GetVerticalScrollExtent(_effectiveViewportHeight));
            double thumbHeight = Math.Clamp(trackHeight * (_effectiveViewportHeight / extent), MinScrollBarThumbSize, trackHeight);
            double maxOffset = GetMaxVerticalOffset();
            double travel = Math.Max(0, trackHeight - thumbHeight);
            double thumbY = _verticalScrollTrackRect.Y + (maxOffset <= 0 ? 0 : (_view.VerticalOffset / maxOffset) * travel);

            _verticalScrollThumbRect = new Rect(
                _verticalScrollTrackRect.X,
                thumbY,
                ScrollBarThickness,
                thumbHeight);
        }

        if (_isHorizontalScrollBarVisible)
        {
            double trackWidth = finalAvailableWidth;
            _horizontalScrollTrackRect = new Rect(
                0,
                Math.Max(0, viewportSize.Height - ScrollBarThickness),
                trackWidth,
                ScrollBarThickness);

            double extent = Math.Max(1, GetDocumentTextContentWidth());
            double thumbWidth = Math.Clamp(trackWidth * (_effectiveTextViewportWidth / extent), MinScrollBarThumbSize, trackWidth);
            double maxOffset = GetMaxHorizontalOffset();
            double travel = Math.Max(0, trackWidth - thumbWidth);
            double thumbX = _horizontalScrollTrackRect.X + (maxOffset <= 0 ? 0 : (_view.HorizontalOffset / maxOffset) * travel);

            _horizontalScrollThumbRect = new Rect(
                thumbX,
                _horizontalScrollTrackRect.Y,
                thumbWidth,
                ScrollBarThickness);
        }
    }

    private double GetTextViewportWidth(double viewportWidth)
    {
        double textAreaLeft = ShowLineNumbers ? _view.TextAreaLeft : 0;
        return Math.Max(0, viewportWidth - textAreaLeft);
    }

    private double GetDocumentTextContentWidth()
    {
        if (_view.CharWidth <= 0 || _document.LineCount <= 0)
            return 0;

        if (_cachedMaxLineLengthVersion != _document.Version)
        {
            int maxLength = 0;
            for (int lineNumber = 1; lineNumber <= _document.LineCount; lineNumber++)
            {
                var line = _document.GetLineByNumber(lineNumber);
                if (line.Length > maxLength)
                    maxLength = line.Length;
            }

            _cachedMaxLineLength = maxLength;
            _cachedMaxLineLengthVersion = _document.Version;
        }

        return _cachedMaxLineLength * _view.CharWidth + 16;
    }

    private double GetMaxVerticalOffset(double viewportHeight)
    {
        double contentHeight = Math.Max(0, _view.TotalContentHeight);
        double safeViewportHeight = Math.Max(0, viewportHeight);
        double lineHeight = Math.Max(1, _view.LineHeight);

        // Allow scrolling past EOF so the last line can reach the top of the viewport.
        double defaultMax = Math.Max(0, contentHeight - safeViewportHeight);
        double lastLineTopMax = Math.Max(0, contentHeight - lineHeight);
        return Math.Max(defaultMax, lastLineTopMax);
    }

    private double GetVerticalScrollExtent(double viewportHeight)
    {
        double contentHeight = Math.Max(0, _view.TotalContentHeight);
        double safeViewportHeight = Math.Max(0, viewportHeight);
        double maxOffset = GetMaxVerticalOffset(viewportHeight);
        return Math.Max(contentHeight, maxOffset + safeViewportHeight);
    }

    private double GetMaxVerticalOffset()
    {
        return GetMaxVerticalOffset(_effectiveViewportHeight);
    }

    private double GetMaxHorizontalOffset()
    {
        return Math.Max(0, GetDocumentTextContentWidth() - Math.Max(0, _effectiveTextViewportWidth));
    }

    private void ClampScrollOffsetsToViewport()
    {
        _view.VerticalOffset = Math.Clamp(_view.VerticalOffset, 0, GetMaxVerticalOffset());
        _view.HorizontalOffset = Math.Clamp(_view.HorizontalOffset, 0, GetMaxHorizontalOffset());
    }

    #endregion

    #region Behavior Helpers

    private int GetSelectionEndLine()
    {
        if (!_selection.HasSelection)
            return _document.GetLineByOffset(_caret.Offset).LineNumber;

        int effectiveEnd = _selection.EndOffset;
        if (effectiveEnd > _selection.StartOffset && effectiveEnd > 0)
        {
            var endLine = _document.GetLineByOffset(effectiveEnd);
            if (endLine.Offset == effectiveEnd)
                effectiveEnd--;
        }

        return _document.GetLineByOffset(Math.Clamp(effectiveEnd, 0, _document.TextLength)).LineNumber;
    }

    private (int firstLine, int lastLine) GetSelectedLineRange()
    {
        int firstLine = _document.GetLineByOffset(_selection.HasSelection ? _selection.StartOffset : _caret.Offset).LineNumber;
        int lastLine = _selection.HasSelection ? GetSelectionEndLine() : firstLine;
        return (firstLine, lastLine);
    }

    private void CommentLines(string linePrefix)
    {
        if (IsReadOnly)
            return;

        var (firstLine, lastLine) = GetSelectedLineRange();
        string insertion = linePrefix + " ";
        int insertionLength = insertion.Length;
        int startOffset = _selection.StartOffset;
        int endOffset = _selection.EndOffset;
        int caretOffset = _caret.Offset;

        _document.BeginUpdate();
        for (int lineNumber = firstLine; lineNumber <= lastLine; lineNumber++)
        {
            var line = _document.GetLineByNumber(lineNumber);
            _document.Insert(line.Offset, insertion);

            if (line.Offset <= startOffset) startOffset += insertionLength;
            if (line.Offset < endOffset) endOffset += insertionLength;
            if (line.Offset <= caretOffset) caretOffset += insertionLength;
        }
        _document.EndUpdate();

        int clampedStart = Math.Clamp(startOffset, 0, _document.TextLength);
        int clampedLength = Math.Clamp(endOffset - startOffset, 0, _document.TextLength - clampedStart);
        _selection.SetSelection(clampedStart, clampedLength);
        _caret.Offset = Math.Clamp(caretOffset, 0, _document.TextLength);
        _caret.ResetBlink();
        _caret.DesiredColumn = -1;
        EnsureCaretVisible();
        UpdateActiveBracketPair();
        OnSelectionChanged();
        OnCaretPositionChanged();
        InvalidateVisual();
    }

    private void UncommentLines(string linePrefix)
    {
        if (IsReadOnly)
            return;

        var (firstLine, lastLine) = GetSelectedLineRange();
        int startOffset = _selection.StartOffset;
        int endOffset = _selection.EndOffset;
        int caretOffset = _caret.Offset;

        _document.BeginUpdate();
        for (int lineNumber = firstLine; lineNumber <= lastLine; lineNumber++)
        {
            var line = _document.GetLineByNumber(lineNumber);
            var lineText = _document.GetLineText(lineNumber);
            int removeLength = 0;

            if (lineText.StartsWith(linePrefix + " ", StringComparison.Ordinal))
                removeLength = linePrefix.Length + 1;
            else if (lineText.StartsWith(linePrefix, StringComparison.Ordinal))
                removeLength = linePrefix.Length;

            if (removeLength <= 0)
                continue;

            _document.Remove(line.Offset, removeLength);
            if (line.Offset < startOffset) startOffset -= Math.Min(removeLength, startOffset - line.Offset);
            if (line.Offset < endOffset) endOffset -= Math.Min(removeLength, endOffset - line.Offset);
            if (line.Offset < caretOffset) caretOffset -= Math.Min(removeLength, caretOffset - line.Offset);
        }
        _document.EndUpdate();

        startOffset = Math.Clamp(startOffset, 0, _document.TextLength);
        endOffset = Math.Clamp(endOffset, startOffset, _document.TextLength);
        caretOffset = Math.Clamp(caretOffset, 0, _document.TextLength);

        _selection.SetSelection(startOffset, endOffset - startOffset);
        _caret.Offset = caretOffset;
        _caret.ResetBlink();
        _caret.DesiredColumn = -1;
        EnsureCaretVisible();
        UpdateActiveBracketPair();
        OnSelectionChanged();
        OnCaretPositionChanged();
        InvalidateVisual();
    }

    private void WrapSelectionWithBlockComment()
    {
        if (IsReadOnly)
            return;

        var (startMarker, endMarker) = GetBlockCommentMarkers(Language);
        if (_selection.HasSelection)
        {
            int start = _selection.StartOffset;
            int end = _selection.EndOffset;
            _document.BeginUpdate();
            _document.Insert(end, endMarker);
            _document.Insert(start, startMarker);
            _document.EndUpdate();
            Select(start + startMarker.Length, end - start);
            return;
        }

        var line = _document.GetLineByOffset(_caret.Offset);
        _document.BeginUpdate();
        _document.Insert(line.Offset + line.Length, endMarker);
        _document.Insert(line.Offset, startMarker);
        _document.EndUpdate();
        _caret.Offset = Math.Clamp(_caret.Offset + startMarker.Length, 0, _document.TextLength);
        _selection.ClearSelection(_caret.Offset);
        _caret.ResetBlink();
        EnsureCaretVisible();
        UpdateActiveBracketPair();
        OnSelectionChanged();
        OnCaretPositionChanged();
        InvalidateVisual();
    }

    private void UnwrapSelectionFromBlockComment()
    {
        if (IsReadOnly || !_selection.HasSelection)
            return;

        var (startMarker, endMarker) = GetBlockCommentMarkers(Language);
        string selected = _selection.GetSelectedText(_document);
        if (!selected.StartsWith(startMarker, StringComparison.Ordinal) || !selected.EndsWith(endMarker, StringComparison.Ordinal))
            return;

        int start = _selection.StartOffset;
        _document.Replace(start, _selection.Length, selected[startMarker.Length..^endMarker.Length]);
        Select(start, selected.Length - startMarker.Length - endMarker.Length);
    }

    private static string? GetLineCommentPrefix(string language)
    {
        return language.ToLowerInvariant() switch
        {
            "csharp" or "cs" or "javascript" or "js" or "typescript" or "ts" or "java" or "cpp" or "c" or "go" or "rust" => "//",
            _ => null
        };
    }

    private static bool IsXmlLikeLanguage(string normalizedLanguage)
    {
        return normalizedLanguage is "xml" or "xaml" or "jalxaml" or "axaml";
    }

    private static (string start, string end) GetBlockCommentMarkers(string language)
    {
        return language.ToLowerInvariant() switch
        {
            "xml" or "xaml" or "jalxaml" or "axaml" or "html" => ("<!--", "-->"),
            _ => ("/*", "*/")
        };
    }

    private void HandleFindShortcut()
    {
        if (_selection.HasSelection && _selection.Length > 0)
        {
            FindAll(_selection.GetSelectedText(_document));
            FindNext();
            return;
        }

        if (!string.IsNullOrEmpty(_findReplace.SearchText))
        {
            FindNext();
        }
    }

    private void HandleReplaceShortcut()
    {
        string? text = Jalium.UI.Interop.Clipboard.GetText();
        if (!string.IsNullOrEmpty(text))
            ReplaceCurrent(text);
    }

    private void ApplyLanguageDefaults(string language)
    {
        var normalized = (language ?? "plaintext").ToLowerInvariant();
        bool isXmlLike = IsXmlLikeLanguage(normalized);
        if (SyntaxHighlighterRegistry.TryCreate(this, normalized, out var registeredHighlighter))
        {
            SyntaxHighlighter = registeredHighlighter;
        }
        else
        {
            SyntaxHighlighter = normalized switch
            {
                "csharp" or "cs" => RegexSyntaxHighlighter.CreateCSharpHighlighter(),
                "jalxaml" or "xml" or "xaml" or "axaml" => JalxamlSyntaxHighlighter.Create(),
                _ => null
            };
        }

        _foldingStrategy = isXmlLike
            ? new XmlFoldingStrategy()
            : new BraceFoldingStrategy();
    }

    private void AttachReactiveHighlighter(IReactiveSyntaxHighlighter? reactiveHighlighter)
    {
        if (reactiveHighlighter is null)
            return;

        _reactiveSyntaxHighlighter = reactiveHighlighter;
        reactiveHighlighter.HighlightingInvalidated += OnReactiveHighlighterInvalidated;
        reactiveHighlighter.Attach(_document, GetDocumentFilePath);
    }

    private void DetachReactiveHighlighter(IReactiveSyntaxHighlighter? reactiveHighlighter)
    {
        if (reactiveHighlighter is null)
            return;

        reactiveHighlighter.HighlightingInvalidated -= OnReactiveHighlighterInvalidated;
        reactiveHighlighter.Detach();

        if (ReferenceEquals(_reactiveSyntaxHighlighter, reactiveHighlighter))
            _reactiveSyntaxHighlighter = null;
    }

    private void RefreshReactiveHighlighterContext()
    {
        if (_reactiveSyntaxHighlighter is null)
            return;

        _reactiveSyntaxHighlighter.Detach();
        _reactiveSyntaxHighlighter.Attach(_document, GetDocumentFilePath);
        _view.InvalidateVisibleLines();
        InvalidateVisual();
    }

    private string? GetDocumentFilePath()
    {
        return string.IsNullOrWhiteSpace(DocumentFilePath)
            ? null
            : DocumentFilePath;
    }

    private void OnReactiveHighlighterInvalidated(object? sender, SyntaxHighlightInvalidatedEventArgs e)
    {
        if (!CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnReactiveHighlighterInvalidated(sender, e));
            return;
        }

        if (e.AffectsWholeDocument)
            _view.InvalidateVisibleLines();
        else
            _view.InvalidateFromLine(e.StartLine);

        InvalidateVisual();
    }

    private Brush ResolveClassificationBrush(TokenClassification classification, Brush fallbackBrush)
    {
        if (_classificationBrushCache.TryGetValue(classification, out var cachedBrush))
            return cachedBrush;

        if (!TryResolveClassificationBrushFromResources(classification, out var resolvedBrush))
            return fallbackBrush;

        _classificationBrushCache[classification] = resolvedBrush;
        return resolvedBrush;
    }

    private bool TryResolveClassificationBrushFromResources(TokenClassification classification, out Brush brush)
    {
        if (TryFindResource(GetOneSyntaxBrushKey(classification)) is Brush oneBrush)
        {
            brush = oneBrush;
            return true;
        }

        if (TryFindResource(GetEditorSyntaxBrushKey(classification)) is Brush editorBrush)
        {
            brush = editorBrush;
            return true;
        }

        brush = null!;
        return false;
    }

    private static string GetOneSyntaxBrushKey(TokenClassification classification)
    {
        return classification switch
        {
            TokenClassification.PlainText => "OneEditorSyntaxPlainText",
            TokenClassification.Keyword => "OneEditorSyntaxKeyword",
            TokenClassification.ControlKeyword => "OneEditorSyntaxControlKeyword",
            TokenClassification.TypeName => "OneEditorSyntaxTypeName",
            TokenClassification.String => "OneEditorSyntaxString",
            TokenClassification.Character => "OneEditorSyntaxCharacter",
            TokenClassification.Number => "OneEditorSyntaxNumber",
            TokenClassification.Comment => "OneEditorSyntaxComment",
            TokenClassification.XmlDoc => "OneEditorSyntaxXmlDoc",
            TokenClassification.Preprocessor => "OneEditorSyntaxPreprocessor",
            TokenClassification.Operator => "OneEditorSyntaxOperator",
            TokenClassification.Punctuation => "OneEditorSyntaxPunctuation",
            TokenClassification.Identifier => "OneEditorSyntaxIdentifier",
            TokenClassification.LocalVariable => "OneEditorSyntaxLocalVariable",
            TokenClassification.Parameter => "OneEditorSyntaxParameter",
            TokenClassification.Field => "OneEditorSyntaxField",
            TokenClassification.Property => "OneEditorSyntaxProperty",
            TokenClassification.Method => "OneEditorSyntaxMethod",
            TokenClassification.Namespace => "OneEditorSyntaxNamespace",
            TokenClassification.Attribute => "OneEditorSyntaxAttribute",
            TokenClassification.BindingKeyword => "OneEditorSyntaxBindingKeyword",
            TokenClassification.BindingParameter => "OneEditorSyntaxBindingParameter",
            TokenClassification.BindingPath => "OneEditorSyntaxBindingPath",
            TokenClassification.BindingOperator => "OneEditorSyntaxBindingOperator",
            TokenClassification.Error => "OneEditorSyntaxError",
            _ => "OneEditorSyntaxPlainText",
        };
    }

    private static string GetEditorSyntaxBrushKey(TokenClassification classification)
    {
        return classification switch
        {
            TokenClassification.PlainText => "EditorSyntaxPlainText",
            TokenClassification.Keyword => "EditorSyntaxKeyword",
            TokenClassification.ControlKeyword => "EditorSyntaxControlKeyword",
            TokenClassification.TypeName => "EditorSyntaxTypeName",
            TokenClassification.String => "EditorSyntaxString",
            TokenClassification.Character => "EditorSyntaxCharacter",
            TokenClassification.Number => "EditorSyntaxNumber",
            TokenClassification.Comment => "EditorSyntaxComment",
            TokenClassification.XmlDoc => "EditorSyntaxXmlDoc",
            TokenClassification.Preprocessor => "EditorSyntaxPreprocessor",
            TokenClassification.Operator => "EditorSyntaxOperator",
            TokenClassification.Punctuation => "EditorSyntaxPunctuation",
            TokenClassification.Identifier => "EditorSyntaxIdentifier",
            TokenClassification.LocalVariable => "EditorSyntaxLocalVariable",
            TokenClassification.Parameter => "EditorSyntaxParameter",
            TokenClassification.Field => "EditorSyntaxField",
            TokenClassification.Property => "EditorSyntaxProperty",
            TokenClassification.Method => "EditorSyntaxMethod",
            TokenClassification.Namespace => "EditorSyntaxNamespace",
            TokenClassification.Attribute => "EditorSyntaxAttribute",
            TokenClassification.BindingKeyword => "EditorSyntaxBindingKeyword",
            TokenClassification.BindingParameter => "EditorSyntaxBindingParameter",
            TokenClassification.BindingPath => "EditorSyntaxBindingPath",
            TokenClassification.BindingOperator => "EditorSyntaxBindingOperator",
            TokenClassification.Error => "EditorSyntaxError",
            _ => "EditorSyntaxPlainText",
        };
    }

    private void ScheduleFoldingRefreshFromDocumentChange()
    {
        if (!ShouldThrottleFoldingRefresh())
        {
            UpdateFoldingState(forceViewInvalidation: false);
            return;
        }

        if (_foldingRefreshTimer == null)
        {
            _foldingRefreshTimer = new DispatcherTimer
            {
                Interval = LargeDocumentFoldingDebounceInterval
            };
            _foldingRefreshTimer.Tick += OnFoldingRefreshTimerTick;
        }

        _hasPendingFoldingRefresh = true;
        _foldingRefreshTimer.Stop();
        _foldingRefreshTimer.Start();
    }

    private bool ShouldThrottleFoldingRefresh()
    {
        return _document.LineCount >= LargeDocumentFoldingThrottleLineCount ||
               _document.TextLength >= LargeDocumentFoldingThrottleTextLength;
    }

    private void OnFoldingRefreshTimerTick(object? sender, EventArgs e)
    {
        if (_foldingRefreshTimer == null)
            return;

        _foldingRefreshTimer.Stop();
        if (!_hasPendingFoldingRefresh)
            return;

        _hasPendingFoldingRefresh = false;
        UpdateFoldingState(forceViewInvalidation: false);
        InvalidateVisual();
    }

    private void UpdateFoldingState(bool forceViewInvalidation = true)
    {
        _hasPendingFoldingRefresh = false;
        _foldingRefreshTimer?.Stop();

        int previousVersion = _foldingManager.Version;
        _foldingManager.UpdateFoldings(_foldingStrategy);
        bool foldingChanged = _foldingManager.Version != previousVersion;
        if (forceViewInvalidation || foldingChanged)
            _view.InvalidateVisibleLines();

        CoerceCaretIntoVisibleLine();
    }

    private void InvalidateSemanticOccurrenceCaches()
    {
        _cachedSelectionOccurrenceVersion = -1;
        _cachedSymbolOccurrenceVersion = -1;
    }

    private void CompleteFoldingStateChange(int oldSelectionStart, int oldSelectionLength, int oldCaret)
    {
        EnsureViewLayoutMetrics();
        int preservedTopLine = _view.FirstVisibleLineNumber;
        double preservedVerticalOffset = _view.VerticalOffset;
        double preservedHorizontalOffset = _view.HorizontalOffset;
        double preservedLineOffset = preservedVerticalOffset - _view.GetAbsoluteLineTop(preservedTopLine);

        bool caretMoved = CoerceCaretIntoVisibleLine();
        _view.InvalidateVisibleLines();

        if (caretMoved)
        {
            EnsureCaretVisible();
        }
        else
        {
            EnsureViewLayoutMetrics();
            double restoredTop = _view.GetAbsoluteLineTop(preservedTopLine) + preservedLineOffset;
            _view.VerticalOffset = Math.Max(0, restoredTop);
            _view.HorizontalOffset = Math.Max(0, preservedHorizontalOffset);
            UpdateScrollBarLayout(RenderSize);
            ClampScrollOffsetsToViewport();
            UpdateImeWindowIfComposing();
        }

        UpdateActiveBracketPair();

        if (oldSelectionStart != _selection.StartOffset || oldSelectionLength != _selection.Length)
            OnSelectionChanged();
        if (oldCaret != _caret.Offset)
            OnCaretPositionChanged();

        FoldingChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private bool CoerceCaretIntoVisibleLine()
    {
        if (_document.LineCount <= 0)
            return false;

        int caretOffset = Math.Clamp(_caret.Offset, 0, _document.TextLength);
        var caretLine = _document.GetLineByOffset(caretOffset);
        if (!_foldingManager.IsLineHidden(caretLine.LineNumber))
            return false;

        var containing = _foldingManager.GetContainingFolding(caretLine.LineNumber);
        if (containing == null)
            return false;

        int targetLineNumber = Math.Clamp(containing.StartLine, 1, _document.LineCount);
        var targetLine = _document.GetLineByNumber(targetLineNumber);
        int newOffset = Math.Clamp(targetLine.Offset + targetLine.Length, 0, _document.TextLength);

        if (_caret.Offset == newOffset && !_selection.HasSelection)
            return false;

        _caret.Offset = newOffset;
        _selection.ClearSelection(newOffset);
        _caret.DesiredColumn = -1;
        _caret.ResetBlink();
        return true;
    }

    private void UpdateActiveBracketPair()
    {
        _activeBracketPair = BracketMatcher.FindMatchingBracketPair(_document, _caret.Offset);
    }

    private void OnSelectionChanged()
    {
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnCaretPositionChanged()
    {
        CaretPositionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateImeWindowIfComposing()
    {
        if (!_isImeComposing)
            return;

        var element = this as UIElement;
        var parent = element?.VisualParent;
        while (parent != null)
        {
            if (parent is Window window)
            {
                window.UpdateImeCompositionWindow();
                break;
            }

            parent = parent.VisualParent;
        }
    }

    #endregion
}
