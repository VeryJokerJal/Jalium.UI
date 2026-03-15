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
public class EditControl : Control, IImeSupport, IEditorViewMetrics
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
    private static readonly SolidColorBrush s_scrollBarTrackBrush = new(Color.FromArgb(72, 68, 68, 68));
    private static readonly SolidColorBrush s_scrollBarThumbBrush = new(Color.FromArgb(220, 180, 180, 180));
    private static readonly SolidColorBrush s_scrollBarActiveThumbBrush = new(Color.FromArgb(235, 212, 212, 212));
    private static readonly BlurEffect s_gutterOverflowBlurEffect = new(12f);
    private static readonly BlurEffect s_scrollBarBackdropBlurEffect = new(10f);
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
    private static readonly SolidColorBrush s_minimapBackgroundBrush = new(Color.FromArgb(28, 30, 30, 30));
    private static readonly SolidColorBrush s_minimapForegroundBrush = new(Color.FromArgb(56, 200, 200, 200));
    private static readonly SolidColorBrush s_minimapViewportBrush = new(Color.FromArgb(64, 255, 255, 255));
    private static readonly Pen s_minimapViewportBorderPen = new(new SolidColorBrush(Color.FromArgb(210, 230, 230, 230)), 1);
    private static readonly SolidColorBrush s_minimapTooltipBackgroundBrush = new(Color.FromArgb(236, 31, 34, 38));
    private static readonly Pen s_minimapTooltipBorderPen = new(new SolidColorBrush(Color.FromArgb(220, 96, 108, 122)), 1);
    private static readonly SolidColorBrush s_minimapTooltipTextBrush = new(Color.FromRgb(226, 231, 236));

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
    private bool _isWordSelecting;
    private int _wordSelectionAnchorStart;
    private int _wordSelectionAnchorEnd;
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
    private const double MinimapTooltipOffsetX = 8;
    private const double MinimapTooltipPaddingX = 8;
    private const double MinimapTooltipPaddingY = 4;
    private const int MinimapTooltipPreviewLineCount = 11;
    private const int MinimapTooltipPreviewCenterIndex = MinimapTooltipPreviewLineCount / 2;
    private const int MinimapTooltipMaxPreviewCharsPerLine = 140;
    private const double MinimapViewportDragActivationDistance = 2;
    private readonly MinimapRenderer _minimapRenderer = new();
    private Rect _verticalScrollTrackRect = Rect.Empty;
    private Rect _verticalScrollThumbRect = Rect.Empty;
    private Rect _horizontalScrollTrackRect = Rect.Empty;
    private Rect _horizontalScrollThumbRect = Rect.Empty;
    private Rect _minimapRect = Rect.Empty;
    private Rect _minimapViewportRect = Rect.Empty;
    private bool _isVerticalScrollBarVisible;
    private bool _isVerticalScrollBarOverlayingMinimap;
    private bool _isHorizontalScrollBarVisible;
    private bool _isMinimapDragging;
    private bool _isMinimapViewportPressPending;
    private double _minimapDragViewportAnchorY;
    private Point _minimapViewportPressPoint;
    private ScrollBarDragMode _scrollBarDragMode;
    private double _scrollBarDragStartMouseCoordinate;
    private double _scrollBarDragStartOffset;
    private double _effectiveViewportHeight;
    private double _effectiveTextViewportWidth;
    private double _cachedMaxLineWidth = -1;
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
    private DispatcherTimer? _scrollAnimationTimer;
    private bool _isScrollAnimating;
    private bool _isApplyingScrollAnimationStep;
    private long _lastScrollAnimationTickTime;
    private double _scrollAnimationTargetVerticalOffset;
    private double _scrollAnimationTargetHorizontalOffset;
    private const double DefaultScrollInertiaDurationMs = 300.0;
    private const double SmoothScrollDurationTailRatio = 0.05;
    private const double SmoothScrollSnapThreshold = 0.5;
    private const double SmoothScrollMinSpeedPixelsPerSecond = 60.0;
    private const double SmoothScrollMaxDeltaTimeSeconds = 0.1;
    private static int SmoothScrollIntervalMs => CompositionTarget.FrameIntervalMs;
    private bool _isFollowingBottom;

    // Caret blink timer
    private DispatcherTimer? _caretTimer;
    private DispatcherTimer? _foldingRefreshTimer;
    private bool _hasPendingFoldingRefresh;
    private Point _lastPointerPosition;
    private bool _hasPointerPosition;
    private FoldingSection? _hoveredScopeGuideSection;
    private FoldingSection? _hoveredFoldedHintSection;
    private bool _isMinimapHovering;
    private int _minimapHoverLineNumber;

    private enum ScrollBarDragMode
    {
        None,
        Vertical,
        Horizontal
    }

    #endregion

    #region Dependency Properties

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(EditControl),
            new PropertyMetadata(string.Empty, OnTextChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty LanguageProperty =
        DependencyProperty.Register(nameof(Language), typeof(string), typeof(EditControl),
            new PropertyMetadata("plaintext", OnLanguageChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty DocumentFilePathProperty =
        DependencyProperty.Register(nameof(DocumentFilePath), typeof(string), typeof(EditControl),
            new PropertyMetadata(string.Empty, OnDocumentFilePathChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(EditControl),
            new PropertyMetadata(false));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ShowLineNumbersProperty =
        DependencyProperty.Register(nameof(ShowLineNumbers), typeof(bool), typeof(EditControl),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty HighlightCurrentLineProperty =
        DependencyProperty.Register(nameof(HighlightCurrentLine), typeof(bool), typeof(EditControl),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty TabSizeProperty =
        DependencyProperty.Register(nameof(TabSize), typeof(int), typeof(EditControl),
            new PropertyMetadata(4));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ConvertTabsToSpacesProperty =
        DependencyProperty.Register(nameof(ConvertTabsToSpaces), typeof(bool), typeof(EditControl),
            new PropertyMetadata(true));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty SyntaxHighlighterProperty =
        DependencyProperty.Register(nameof(SyntaxHighlighter), typeof(ISyntaxHighlighter), typeof(EditControl),
            new PropertyMetadata(null, OnSyntaxHighlighterChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SelectionBrushProperty =
        DependencyProperty.Register(nameof(SelectionBrush), typeof(Brush), typeof(EditControl),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CaretBrushProperty =
        DependencyProperty.Register(nameof(CaretBrush), typeof(Brush), typeof(EditControl),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty LineNumberForegroundProperty =
        DependencyProperty.Register(nameof(LineNumberForeground), typeof(Brush), typeof(EditControl),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty CurrentLineBackgroundProperty =
        DependencyProperty.Register(nameof(CurrentLineBackground), typeof(Brush), typeof(EditControl),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty GutterBackgroundProperty =
        DependencyProperty.Register(nameof(GutterBackground), typeof(Brush), typeof(EditControl),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ShowMinimapProperty =
        DependencyProperty.Register(nameof(ShowMinimap), typeof(bool), typeof(EditControl),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty AdornmentMinLineHeightProperty =
        DependencyProperty.Register(nameof(AdornmentMinLineHeight), typeof(double), typeof(EditControl),
            new PropertyMetadata(30.0, OnVisualPropertyChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty LeadingGutterInsetProperty =
        DependencyProperty.Register(nameof(LeadingGutterInset), typeof(double), typeof(EditControl),
            new PropertyMetadata(0.0, OnLeadingGutterInsetChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsScrollInertiaEnabledProperty =
        DependencyProperty.Register(nameof(IsScrollInertiaEnabled), typeof(bool), typeof(EditControl),
            new PropertyMetadata(true, OnScrollInertiaSettingsChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ScrollInertiaDurationMsProperty =
        DependencyProperty.Register(nameof(ScrollInertiaDurationMs), typeof(double), typeof(EditControl),
            new PropertyMetadata(DefaultScrollInertiaDurationMs, OnScrollInertiaSettingsChanged));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty AutoFollowBottomProperty =
        DependencyProperty.Register(nameof(AutoFollowBottom), typeof(bool), typeof(EditControl),
            new PropertyMetadata(false, OnAutoFollowBottomChanged));

    #endregion

    #region CLR Properties

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Text
    {
        get => (string)(GetValue(TextProperty) ?? string.Empty);
        set => SetValue(TextProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string Language
    {
        get => (string)(GetValue(LanguageProperty) ?? "plaintext");
        set => SetValue(LanguageProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string DocumentFilePath
    {
        get => (string)(GetValue(DocumentFilePathProperty) ?? string.Empty);
        set => SetValue(DocumentFilePathProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty)!;
        set => SetValue(IsReadOnlyProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public bool ShowLineNumbers
    {
        get => (bool)GetValue(ShowLineNumbersProperty)!;
        set => SetValue(ShowLineNumbersProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool HighlightCurrentLine
    {
        get => (bool)GetValue(HighlightCurrentLineProperty)!;
        set => SetValue(HighlightCurrentLineProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public int TabSize
    {
        get => (int)GetValue(TabSizeProperty)!;
        set => SetValue(TabSizeProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool ConvertTabsToSpaces
    {
        get => (bool)GetValue(ConvertTabsToSpacesProperty)!;
        set => SetValue(ConvertTabsToSpacesProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public ISyntaxHighlighter? SyntaxHighlighter
    {
        get => (ISyntaxHighlighter?)GetValue(SyntaxHighlighterProperty);
        set => SetValue(SyntaxHighlighterProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? SelectionBrush
    {
        get => (Brush?)GetValue(SelectionBrushProperty);
        set => SetValue(SelectionBrushProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? CaretBrush
    {
        get => (Brush?)GetValue(CaretBrushProperty);
        set => SetValue(CaretBrushProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Brush? LineNumberForeground
    {
        get => (Brush?)GetValue(LineNumberForegroundProperty);
        set => SetValue(LineNumberForegroundProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Brush? CurrentLineBackground
    {
        get => (Brush?)GetValue(CurrentLineBackgroundProperty);
        set => SetValue(CurrentLineBackgroundProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public Brush? GutterBackground
    {
        get => (Brush?)GetValue(GutterBackgroundProperty);
        set => SetValue(GutterBackgroundProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public bool ShowMinimap
    {
        get => (bool)GetValue(ShowMinimapProperty)!;
        set => SetValue(ShowMinimapProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double AdornmentMinLineHeight
    {
        get => (double)GetValue(AdornmentMinLineHeightProperty)!;
        set => SetValue(AdornmentMinLineHeightProperty, value);
    }

    /// <summary>
    /// Reserves horizontal space before line numbers (inside the editor bounds).
    /// Useful for custom gutters such as breakpoint toggles.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public double LeadingGutterInset
    {
        get => (double)GetValue(LeadingGutterInsetProperty)!;
        set => SetValue(LeadingGutterInsetProperty, Math.Max(0, value));
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsScrollInertiaEnabled
    {
        get => (bool)GetValue(IsScrollInertiaEnabledProperty)!;
        set => SetValue(IsScrollInertiaEnabledProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public double ScrollInertiaDurationMs
    {
        get => (double)GetValue(ScrollInertiaDurationMsProperty)!;
        set => SetValue(ScrollInertiaDurationMsProperty, value);
    }

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool AutoFollowBottom
    {
        get => (bool)GetValue(AutoFollowBottomProperty)!;
        set => SetValue(AutoFollowBottomProperty, value);
    }

    public bool IsFollowingBottom => _isFollowingBottom;

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

    internal Rect MinimapRectForTesting => _minimapRect;

    internal Rect MinimapViewportRectForTesting => _minimapViewportRect;

    internal bool IsScrollAnimatingForTesting => _isScrollAnimating;

    internal bool IsMinimapTooltipVisibleForTesting => _isMinimapHovering;

    internal int MinimapTooltipLineForTesting => _minimapHoverLineNumber;

    internal string MinimapTooltipTextForTesting =>
        _minimapHoverLineNumber > 0 ? BuildMinimapTooltipText(_minimapHoverLineNumber, MinimapTooltipMaxPreviewCharsPerLine) : string.Empty;

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
        _view.LeadingGutterInset = Math.Max(0, LeadingGutterInset);
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
        _scrollAnimationTargetVerticalOffset = 0;
        _scrollAnimationTargetHorizontalOffset = 0;
        _isFollowingBottom = AutoFollowBottom && IsAtBottomWithinTolerance();
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

        double contentWidth = GetContentRenderWidth(RenderSize.Width);
        double contentHeight = GetContentRenderHeight(RenderSize.Height);
        double contentBackdropWidth = contentWidth;
        if (!_minimapRect.IsEmpty)
            contentBackdropWidth = Math.Max(contentBackdropWidth, _minimapRect.Right);

        var contentSize = new Size(contentBackdropWidth, contentHeight);

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, RenderSize.Width, RenderSize.Height)));
        try
        {
            // Draw background
            if (Background != null)
            {
                dc.DrawRectangle(Background, null, new Rect(0, 0, RenderSize.Width, RenderSize.Height));
            }

            var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);
            var borderThickness = BorderThickness.Left;
            if (BorderBrush != null && borderThickness > 0)
            {
                var borderRect = ControlRenderGeometry.GetStrokeAlignedRect(bounds, borderThickness);
                dc.DrawRectangle(null, new Pen(BorderBrush, borderThickness), borderRect);
            }

            if (IsKeyboardFocused)
            {
                ControlFocusVisual.Draw(dc, this, bounds, CornerRadius);
            }

            bool hasContentClip = contentBackdropWidth > 0 && contentHeight > 0;
            if (hasContentClip)
                dc.PushClip(new RectangleGeometry(new Rect(0, 0, contentBackdropWidth, contentHeight)));

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
                    ResolveForegroundBrush(),
                    ResolveSelectionBrush(),
                    ResolveCaretBrush(),
                    ResolveLineNumberForegroundBrush(),
                    ResolveCurrentLineBackgroundBrush(),
                    ResolveGutterBackgroundBrush(),
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
                DrawFoldingMarkers(dc, contentWidth, contentHeight);
                bool hasFoldedHintTooltip = DrawFoldedSectionHoverTooltip(dc, contentWidth, contentHeight, fontFamily, fontSize);
                if (!hasFoldedHintTooltip)
                    DrawScopeGuideHoverTooltip(dc, contentWidth, contentHeight, fontFamily, fontSize);
            }
            finally
            {
                if (hasContentClip)
                    dc.Pop();
            }

            DrawMinimap(dc);
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
        if (TryHandleMinimapMouseDown(mouseArgs, position))
            return;
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
            _isWordSelecting = false;
            _clickCount = 0;
        }
        else if (_clickCount == 2)
        {
            // Double-click: select word
            SelectWordAt(offset);
            _wordSelectionAnchorStart = _selection.StartOffset;
            _wordSelectionAnchorEnd = _selection.EndOffset;
            _isWordSelecting = _selection.Length > 0;
            _isDragging = true;
            CaptureMouse();
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
            _isWordSelecting = false;
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

        if (_isMinimapDragging || _isMinimapViewportPressPending)
        {
            bool shouldNavigateOnRelease = _isMinimapViewportPressPending && !_isMinimapDragging;
            _isMinimapDragging = false;
            _isMinimapViewportPressPending = false;
            ReleaseMouseCapture();
            if (shouldNavigateOnRelease)
                NavigateMinimapToPosition(mouseArgs.GetPosition(this), allowAnimation: true);
            InvalidateVisual();
            mouseArgs.Handled = true;
            return;
        }

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
            _isWordSelecting = false;
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
        bool minimapHoverChanged = UpdateMinimapHover(position);
        double contentWidth = GetContentRenderWidth(RenderSize.Width);
        double contentHeight = GetContentRenderHeight(RenderSize.Height);

        if (_isMinimapViewportPressPending && !_isMinimapDragging && mouseArgs.LeftButton == MouseButtonState.Pressed)
        {
            double dx = Math.Abs(position.X - _minimapViewportPressPoint.X);
            double dy = Math.Abs(position.Y - _minimapViewportPressPoint.Y);
            if (dx + dy >= MinimapViewportDragActivationDistance)
                _isMinimapDragging = true;
        }

        if (_isMinimapDragging)
        {
            _hoveredScopeGuideSection = null;
            _hoveredFoldedHintSection = null;
            HandleMinimapMouseDrag(mouseArgs);
            return;
        }

        if (_scrollBarDragMode != ScrollBarDragMode.None)
        {
            ClearMinimapHover();
            _hoveredScopeGuideSection = null;
            _hoveredFoldedHintSection = null;
            HandleScrollBarMouseDrag(mouseArgs);
            return;
        }

        if (!_isDragging)
        {
            if (_isMinimapHovering)
            {
                _hoveredScopeGuideSection = null;
                _hoveredFoldedHintSection = null;
                InvalidateVisual();
                return;
            }

            bool scopeHoverChanged = UpdateHoveredScopeGuide(position, contentWidth, contentHeight);
            bool foldedHintHoverChanged = UpdateHoveredFoldedHint(position, contentWidth, contentHeight);
            if (minimapHoverChanged || scopeHoverChanged || foldedHintHoverChanged || _hoveredScopeGuideSection != null || _hoveredFoldedHintSection != null)
                InvalidateVisual();
            return;
        }

        ClearMinimapHover();
        _hoveredScopeGuideSection = null;
        _hoveredFoldedHintSection = null;

        int oldSelectionStart = _selection.StartOffset;
        int oldSelectionLength = _selection.Length;
        int oldCaret = _caret.Offset;
        int offset = _view.GetOffsetFromPoint(position, ShowLineNumbers);

        if (_isWordSelecting)
        {
            ExtendWordSelectionToOffset(offset);
        }
        else
        {
            _selection.ExtendTo(offset);
            _caret.Offset = offset;
        }
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
        bool minimapHoverCleared = ClearMinimapHover();
        if (_hoveredScopeGuideSection == null && _hoveredFoldedHintSection == null && !minimapHoverCleared)
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
            ScrollHorizontallyBy(delta, allowAnimation: true, userInitiated: true);
        }
        else
        {
            double linesToScroll = 3;
            double delta = -wheelArgs.Delta / 120.0 * linesToScroll * Math.Max(1, _view.LineHeight);
            ScrollVerticallyBy(delta, allowAnimation: true, userInitiated: true);
        }
        wheelArgs.Handled = true;
    }

    private void UpdateCursorForPointer(Point position)
    {
        var desiredCursor = IsPointerOverScrollBar(position)
            || IsPointerOverMinimap(position)
            || IsPointerOverFoldingMarker(position)
            || IsPointerOverFoldedSectionHint(position)
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

    private bool IsPointerOverMinimap(Point position)
    {
        return ShowMinimap && !_minimapRect.IsEmpty && _minimapRect.Contains(position);
    }

    private bool UpdateMinimapHover(Point position)
    {
        bool isHovering = ShowMinimap && !_minimapRect.IsEmpty && _minimapRect.Contains(position) && _document.LineCount > 0;
        int hoverLine = 0;
        if (isHovering)
            hoverLine = _minimapRenderer.GetLineFromPoint(position.Y, _minimapRect, _view, _document);

        if (_isMinimapHovering == isHovering && _minimapHoverLineNumber == hoverLine)
            return false;

        _isMinimapHovering = isHovering;
        _minimapHoverLineNumber = hoverLine;
        return true;
    }

    private bool ClearMinimapHover()
    {
        if (!_isMinimapHovering && _minimapHoverLineNumber == 0)
            return false;

        _isMinimapHovering = false;
        _minimapHoverLineNumber = 0;
        return true;
    }

    private bool TryHandleMinimapMouseDown(MouseButtonEventArgs mouseArgs, Point position)
    {
        if (!ShowMinimap || _minimapRect.IsEmpty || !_minimapRect.Contains(position))
            return false;

        if (!_minimapViewportRect.IsEmpty && _minimapViewportRect.Contains(position))
        {
            CancelScrollAnimation();
            _isMinimapDragging = false;
            _isMinimapViewportPressPending = true;
            _minimapViewportPressPoint = position;
            _minimapDragViewportAnchorY = position.Y - _minimapViewportRect.Y;
            _isDragging = false;
            CaptureMouse();
        }
        else
        {
            _isMinimapViewportPressPending = false;
            NavigateMinimapToPosition(position, allowAnimation: true);
        }

        InvalidateVisual();
        mouseArgs.Handled = true;
        return true;
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
                CancelScrollAnimation();
                _scrollBarDragMode = ScrollBarDragMode.Vertical;
                _scrollBarDragStartMouseCoordinate = position.Y;
                _scrollBarDragStartOffset = _view.VerticalOffset;
                _isDragging = false;
                CaptureMouse();
            }
            else
            {
                double page = Math.Max(_effectiveViewportHeight * 0.9, Math.Max(1, _view.LineHeight));
                ScrollVerticallyBy(position.Y < _verticalScrollThumbRect.Y ? -page : page, allowAnimation: true, userInitiated: true);
            }

            InvalidateVisual();
            mouseArgs.Handled = true;
            return true;
        }

        if (_isHorizontalScrollBarVisible && _horizontalScrollTrackRect.Contains(position))
        {
            if (_horizontalScrollThumbRect.Contains(position))
            {
                CancelScrollAnimation();
                _scrollBarDragMode = ScrollBarDragMode.Horizontal;
                _scrollBarDragStartMouseCoordinate = position.X;
                _scrollBarDragStartOffset = _view.HorizontalOffset;
                _isDragging = false;
                CaptureMouse();
            }
            else
            {
                double page = Math.Max(_effectiveTextViewportWidth * 0.9, Math.Max(1, _view.CharWidth));
                ScrollHorizontallyBy(position.X < _horizontalScrollThumbRect.X ? -page : page, allowAnimation: true, userInitiated: true);
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

        double contentWidth = GetContentRenderWidth(RenderSize.Width);
        double contentHeight = GetContentRenderHeight(RenderSize.Height);
        if (contentWidth <= 0 || contentHeight <= 0)
            return false;

        double gutterRight = Math.Min(contentWidth, Math.Max(0, _view.TextAreaLeft));
        if (gutterRight <= 0)
            return false;
        if (position.X < 0 || position.X > gutterRight || position.Y < 0 || position.Y > contentHeight)
            return false;

        int lineNumber = _view.GetLineNumberFromY(position.Y);
        section = _foldingManager.GetFoldingAt(lineNumber);
        if (section == null)
            return false;

        if (!_view.TryGetLineTop(section.StartLine, out double lineTop))
            return false;

        markerRect = GetFoldingMarkerRect(lineTop);
        if (markerRect.IsEmpty || markerRect.Left < 0 || markerRect.Right > gutterRight + 0.5)
        {
            markerRect = Rect.Empty;
            section = null;
            return false;
        }

        return markerRect.Contains(position);
    }

    private bool TryGetFoldedSectionHintAt(Point position, out FoldingSection? section, out Rect hintRect)
    {
        double contentWidth = GetContentRenderWidth(RenderSize.Width);
        double contentHeight = GetContentRenderHeight(RenderSize.Height);
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
                double offset = Math.Clamp(_scrollBarDragStartOffset + (pixelDelta / trackTravel) * maxOffset, 0, maxOffset);
                SetVerticalOffsetImmediate(offset, userInitiated: true, cancelAnimation: true);
            }
        }
        else if (_scrollBarDragMode == ScrollBarDragMode.Horizontal && !_horizontalScrollTrackRect.IsEmpty)
        {
            double trackTravel = Math.Max(1, _horizontalScrollTrackRect.Width - _horizontalScrollThumbRect.Width);
            double maxOffset = GetMaxHorizontalOffset();
            if (maxOffset > 0)
            {
                double pixelDelta = position.X - _scrollBarDragStartMouseCoordinate;
                double offset = Math.Clamp(_scrollBarDragStartOffset + (pixelDelta / trackTravel) * maxOffset, 0, maxOffset);
                SetHorizontalOffsetImmediate(offset, cancelAnimation: true);
            }
        }

        InvalidateVisual();
        mouseArgs.Handled = true;
    }

    private void HandleMinimapMouseDrag(MouseEventArgs mouseArgs)
    {
        if (_minimapRect.IsEmpty || _minimapRect.Height <= 0)
            return;

        var position = mouseArgs.GetPosition(this);
        double dragTop = position.Y - _minimapDragViewportAnchorY;
        double targetOffset = _minimapRenderer.GetVerticalOffsetFromViewportTop(dragTop, _minimapRect, _view);
        SetVerticalOffsetImmediate(targetOffset, userInitiated: true, cancelAnimation: true);
        InvalidateVisual();
        mouseArgs.Handled = true;
    }

    private void NavigateMinimapToPosition(Point position, bool allowAnimation)
    {
        if (_minimapRect.IsEmpty || _document.LineCount <= 0)
            return;

        Rect viewportRect = _minimapViewportRect;
        if (viewportRect.IsEmpty)
            viewportRect = _minimapRenderer.GetViewportRect(_document, _view, _minimapRect);

        double viewportHeight = Math.Max(0, viewportRect.Height);
        if (viewportHeight > 0)
        {
            // Keep the visible viewport indicator minimum height for usability, but
            // use the logical (unclamped) viewport height when mapping click-to-offset.
            // This avoids a no-op quantization zone when the visible indicator is clamped
            // to the minimum height on very large documents.
            double logicalViewportHeight = viewportHeight;
            double maxOffset = GetMaxVerticalOffset();
            double extent = Math.Max(Math.Max(0, _view.TotalContentHeight), maxOffset + Math.Max(0, _view.ViewportHeight));
            if (extent > 0 && _minimapRect.Height > 0 && _view.ViewportHeight > 0)
            {
                double scaledViewportHeight = (_view.ViewportHeight / extent) * _minimapRect.Height;
                logicalViewportHeight = Math.Clamp(scaledViewportHeight, 0, _minimapRect.Height);
            }

            double targetViewportTop = position.Y - logicalViewportHeight * 0.5;
            double targetOffset = _minimapRenderer.GetVerticalOffsetFromViewportTop(targetViewportTop, _minimapRect, _view);
            SetVerticalOffset(targetOffset, allowAnimation: allowAnimation, userInitiated: true);
            return;
        }

        int targetLine = _minimapRenderer.GetLineFromPoint(position.Y, _minimapRect, _view, _document);
        double targetLineTop = _view.GetAbsoluteLineTop(targetLine);
        double targetOffsetFallback = targetLineTop - (_effectiveViewportHeight * 0.5);
        SetVerticalOffset(targetOffsetFallback, allowAnimation: allowAnimation, userInitiated: true);
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
        _isMinimapDragging = false;
        _isMinimapViewportPressPending = false;
        ClearMinimapHover();
        _isDragging = false;
        _isWordSelecting = false;
        CancelScrollAnimation();
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

    private void EnsureCaretVisible(bool allowAnimation = false)
    {
        EnsureViewLayoutMetrics();
        UpdateScrollBarLayout(RenderSize);
        if (_view.ViewportWidth <= 0 || _view.ViewportHeight <= 0)
        {
            SetScrollOffsetsImmediate(0, 0, userInitiated: false, cancelAnimation: true);
            return;
        }

        var caretPoint = _view.GetPointFromOffset(_caret.Offset, ShowLineNumbers);
        double targetVerticalOffset = _view.VerticalOffset;
        double targetHorizontalOffset = _view.HorizontalOffset;

        // Vertical scrolling
        if (caretPoint.Y < 0)
        {
            targetVerticalOffset += caretPoint.Y;
        }
        else if (caretPoint.Y + _view.LineHeight > _view.ViewportHeight)
        {
            targetVerticalOffset += caretPoint.Y + _view.LineHeight - _view.ViewportHeight;
        }

        // Horizontal scrolling
        double textAreaLeft = ShowLineNumbers ? _view.TextAreaLeft : 0;
        if (caretPoint.X < textAreaLeft)
        {
            targetHorizontalOffset -= textAreaLeft - caretPoint.X;
        }
        else if (caretPoint.X > _view.ViewportWidth - 20)
        {
            targetHorizontalOffset += caretPoint.X - _view.ViewportWidth + 40;
        }

        SetScrollOffsets(targetVerticalOffset, targetHorizontalOffset, allowAnimation, userInitiated: false);
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
            text = Clipboard.GetText();
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

            return Clipboard.SetText(text);
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

    private void ExtendWordSelectionToOffset(int offset)
    {
        if (_document.TextLength == 0)
        {
            _selection.ClearSelection(0);
            _caret.Offset = 0;
            return;
        }

        var (currentStart, currentLength) = GetWordRangeAtOffset(Math.Clamp(offset, 0, _document.TextLength - 1));
        int currentEnd = currentStart + currentLength;

        if (currentEnd <= _wordSelectionAnchorStart)
        {
            _selection.AnchorOffset = _wordSelectionAnchorEnd;
            _selection.ActiveOffset = currentStart;
            _caret.Offset = currentStart;
        }
        else if (currentStart >= _wordSelectionAnchorEnd)
        {
            _selection.AnchorOffset = _wordSelectionAnchorStart;
            _selection.ActiveOffset = currentEnd;
            _caret.Offset = currentEnd;
        }
        else
        {
            _selection.AnchorOffset = _wordSelectionAnchorStart;
            _selection.ActiveOffset = _wordSelectionAnchorEnd;
            _caret.Offset = _wordSelectionAnchorEnd;
        }
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
        SetVerticalOffset(_view.GetAbsoluteLineTop(lineNumber), allowAnimation: true, userInitiated: false);
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

    public void ScrollLineUp() => ScrollVerticallyBy(-Math.Max(1, _view.LineHeight), allowAnimation: true, userInitiated: false);

    public void ScrollLineDown() => ScrollVerticallyBy(Math.Max(1, _view.LineHeight), allowAnimation: true, userInitiated: false);

    public void ScrollPageUp()
    {
        double page = _effectiveViewportHeight > 0
            ? _effectiveViewportHeight
            : Math.Max(1, _view.LineHeight * Math.Max(1, _view.VisibleLineCount));
        ScrollVerticallyBy(-Math.Max(page, Math.Max(1, _view.LineHeight)), allowAnimation: true, userInitiated: false);
    }

    public void ScrollPageDown()
    {
        double page = _effectiveViewportHeight > 0
            ? _effectiveViewportHeight
            : Math.Max(1, _view.LineHeight * Math.Max(1, _view.VisibleLineCount));
        ScrollVerticallyBy(Math.Max(page, Math.Max(1, _view.LineHeight)), allowAnimation: true, userInitiated: false);
    }

    private void ScrollVerticallyBy(double delta, bool allowAnimation, bool userInitiated)
    {
        EnsureViewLayoutMetrics();
        UpdateScrollBarLayout(RenderSize);
        if (_view.ViewportHeight <= 0)
            return;

        double baseOffset = _isScrollAnimating ? _scrollAnimationTargetVerticalOffset : _view.VerticalOffset;
        SetVerticalOffset(baseOffset + delta, allowAnimation, userInitiated);
    }

    private void ScrollHorizontallyBy(double delta, bool allowAnimation, bool userInitiated)
    {
        EnsureViewLayoutMetrics();
        UpdateScrollBarLayout(RenderSize);
        if (_view.ViewportWidth <= 0)
            return;

        double baseOffset = _isScrollAnimating ? _scrollAnimationTargetHorizontalOffset : _view.HorizontalOffset;
        SetHorizontalOffset(baseOffset + delta, allowAnimation, userInitiated);
    }

    public void ScrollToOffset(int offset)
    {
        EnsureViewLayoutMetrics();
        UpdateScrollBarLayout(RenderSize);
        if (_view.ViewportWidth <= 0 || _view.ViewportHeight <= 0)
            return;

        offset = Math.Clamp(offset, 0, _document.TextLength);
        var point = _view.GetPointFromOffset(offset, ShowLineNumbers);
        double targetVerticalOffset = _view.VerticalOffset;
        if (point.Y < 0)
            targetVerticalOffset += point.Y;
        else if (point.Y + _view.LineHeight > _view.ViewportHeight)
            targetVerticalOffset += point.Y + _view.LineHeight - _view.ViewportHeight;

        double textAreaLeft = ShowLineNumbers ? _view.TextAreaLeft : 0;
        double targetHorizontalOffset = _view.HorizontalOffset;
        if (point.X < textAreaLeft)
            targetHorizontalOffset -= textAreaLeft - point.X;
        else if (point.X > _view.ViewportWidth - 20)
            targetHorizontalOffset += point.X - _view.ViewportWidth + 40;

        SetScrollOffsets(targetVerticalOffset, targetHorizontalOffset, allowAnimation: true, userInitiated: false);
    }

    public void ScrollToCaret()
    {
        EnsureCaretVisible(allowAnimation: true);
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
        SetScrollOffsetsImmediate(0, 0, userInitiated: false, cancelAnimation: true);
        _view.InvalidateVisibleLines();
        InvalidateSemanticOccurrenceCaches();
        _activeFindResult = null;
        _hasSearchQuery = false;
        EnsureViewLayoutMetrics();
        UpdateScrollBarLayout(RenderSize);
        UpdateFollowingBottomStateAfterVerticalChange(userInitiated: false);
        UpdateActiveBracketPair();
        UpdateFoldingState();
        OnSelectionChanged();
        OnCaretPositionChanged();
        InvalidateVisual();
    }

    public void AppendText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        bool shouldFollowBottom = false;
        if (AutoFollowBottom)
        {
            EnsureViewLayoutMetrics();
            UpdateScrollBarLayout(RenderSize);
            shouldFollowBottom = _isFollowingBottom || IsAtBottomWithinTolerance();
            if (shouldFollowBottom)
                SetFollowingBottomState(true);
        }

        _document.Insert(_document.TextLength, text);

        if (shouldFollowBottom)
            SetVerticalOffsetImmediate(GetMaxVerticalOffset(), userInitiated: false, cancelAnimation: true);
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
                    clipboardText = Clipboard.GetText();
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
            editor.UpdateFollowingBottomStateAfterVerticalChange(userInitiated: false);
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

    private static void OnLeadingGutterInsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not EditControl editor)
            return;

        editor._view.LeadingGutterInset = Math.Max(0, editor.LeadingGutterInset);
        editor.InvalidateVisual();
    }

    private static void OnScrollInertiaSettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not EditControl editor)
            return;

        if (editor.IsScrollInertiaEnabled && editor.GetEffectiveScrollInertiaDurationMs() > 0)
            return;

        editor.SnapScrollAnimationToTargetAndStop();
    }

    private static void OnAutoFollowBottomChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not EditControl editor)
            return;

        if (!editor.AutoFollowBottom)
        {
            editor.SetFollowingBottomState(false);
            return;
        }

        editor.EnsureViewLayoutMetrics();
        editor.UpdateScrollBarLayout(editor.RenderSize);
        editor.SetFollowingBottomState(editor.IsAtBottomWithinTolerance());
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

        if (AutoFollowBottom && _isFollowingBottom)
        {
            SetVerticalOffsetImmediate(GetMaxVerticalOffset(), userInitiated: false, cancelAnimation: true);
        }
        else
        {
            UpdateFollowingBottomStateAfterVerticalChange(userInitiated: false);
        }

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

            DrawDocumentRangeHighlight(dc, match.Offset, match.Length, ResolveSelectedTextOccurrenceBrush());
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
            DrawDocumentRangeHighlight(dc, match.Offset, match.Length, ResolveSymbolOccurrenceBrush());
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

    private Brush ResolveThemeBrush(string primaryKey, Brush fallback, string? secondaryKey = null)
    {
        if (TryFindResource(primaryKey) is Brush primary)
            return primary;
        if (!string.IsNullOrWhiteSpace(secondaryKey) && TryFindResource(secondaryKey) is Brush secondary)
            return secondary;
        return fallback;
    }

    private Brush ResolveForegroundBrush()
    {
        if (HasLocalValue(Control.ForegroundProperty) && Foreground != null)
            return Foreground;

        return ResolveThemeBrush("EditorSyntaxPlainText", s_defaultForegroundBrush, "TextPrimary");
    }

    private Brush ResolveSelectionBrush()
    {
        return SelectionBrush
            ?? ResolveThemeBrush("SelectionBackground", s_defaultSelectionBrush, "AccentFillColorSelectedTextBackgroundBrush");
    }

    private Brush ResolveCaretBrush()
    {
        return CaretBrush
            ?? ((HasLocalValue(Control.ForegroundProperty) && Foreground != null) ? Foreground : null)
            ?? ResolveThemeBrush("TextPrimary", s_defaultCaretBrush, "TextFillColorPrimaryBrush");
    }

    private Brush ResolveLineNumberForegroundBrush()
    {
        return LineNumberForeground
            ?? ResolveThemeBrush("TextSecondary", s_defaultLineNumberBrush, "TextFillColorSecondaryBrush");
    }

    private Brush ResolveCurrentLineBackgroundBrush()
    {
        return CurrentLineBackground
            ?? ResolveThemeBrush("HighlightBackground", s_defaultCurrentLineBrush, "ControlFillColorTertiaryBrush");
    }

    private Brush ResolveGutterBackgroundBrush()
    {
        return GutterBackground
            ?? ResolveThemeBrush("ControlBackground", s_defaultGutterBrush, "ControlFillColorDefaultBrush");
    }

    private Brush ResolveSelectedTextOccurrenceBrush()
    {
        return ResolveThemeBrush("HighlightBackground", s_selectedTextOccurrenceBrush, "SelectionBackground");
    }

    private Brush ResolveSymbolOccurrenceBrush()
    {
        return ResolveThemeBrush("OneAccentSubtle", s_symbolOccurrenceBrush, "SelectionBackground");
    }

    private Brush ResolveImeCompositionBackgroundBrush()
    {
        return ResolveThemeBrush("SelectionBackground", s_imeCompositionBackgroundBrush, "AccentFillColorSelectedTextBackgroundBrush");
    }

    private Brush ResolveImeCompositionTextBrush()
    {
        return ResolveThemeBrush("TextPrimary", s_imeCompositionTextBrush, "TextFillColorPrimaryBrush");
    }

    private Pen ResolveImeCompositionUnderlinePen()
    {
        return ResolveThemePen("AccentBrush", s_imeCompositionUnderlinePen, "ControlBorderFocused");
    }

    private Brush ResolveGutterOverflowOverlayBrush()
    {
        return ResolveThemeBrush("TooltipBackground", s_gutterOverflowOverlayBrush, "ControlBackground");
    }

    private Brush ResolveFoldingMarkerSelectedBackgroundBrush()
    {
        return ResolveThemeBrush("HighlightBackground", s_foldingMarkerSelectedBackgroundBrush, "SelectionBackground");
    }

    private Pen ResolveFoldingGuidePen()
    {
        return ResolveThemePen("OneEditorIndentGuide", s_foldingGuidePen, "ControlBorder");
    }

    private Pen ResolveFoldingChevronPen(bool selected)
    {
        return selected
            ? ResolveThemePen("OneBorderFocused", s_foldingChevronSelectedPen, "AccentBrush")
            : ResolveThemePen("TextSecondary", s_foldingChevronPen, "OneEditorIndentGuide");
    }

    private Pen ResolveThemePen(string primaryKey, Pen fallback, string? secondaryKey = null, double? thickness = null)
    {
        if (TryFindResource(primaryKey) is Brush primary)
            return CreatePen(primary, fallback, thickness);
        if (!string.IsNullOrWhiteSpace(secondaryKey) && TryFindResource(secondaryKey) is Brush secondary)
            return CreatePen(secondary, fallback, thickness);
        if (thickness.HasValue && fallback.Brush is Brush fallbackBrush && Math.Abs(thickness.Value - fallback.Thickness) > double.Epsilon)
            return CreatePen(fallbackBrush, fallback, thickness);
        return fallback;
    }

    private static Pen CreatePen(Brush brush, Pen template, double? thickness = null)
    {
        return new Pen(brush, thickness ?? template.Thickness)
        {
            LineJoin = template.LineJoin,
            StartLineCap = template.StartLineCap,
            EndLineCap = template.EndLineCap
        };
    }

    private void DrawSearchHighlights(DrawingContext dc)
    {
        if (_findReplace.Results.Count == 0 || _view.LineHeight <= 0)
            return;

        var searchResultBrush = ResolveThemeBrush("OneEditorFindMatch", s_defaultSearchResultBrush, "HighlightBackground");
        var activeSearchResultBrush = ResolveThemeBrush("OneEditorFindMatch", s_defaultActiveSearchResultBrush, "SelectionBackground");

        for (int i = 0; i < _findReplace.Results.Count; i++)
        {
            var result = _findReplace.Results[i];
            bool isActive = _activeFindResult.HasValue &&
                _activeFindResult.Value.Offset == result.Offset &&
                _activeFindResult.Value.Length == result.Length;
            DrawDocumentRangeHighlight(dc, result.Offset, result.Length, isActive ? activeSearchResultBrush : searchResultBrush);
        }
    }

    private void DrawBracketHighlights(DrawingContext dc)
    {
        if (!_activeBracketPair.HasValue || _view.LineHeight <= 0)
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
        int nextOffset = Math.Clamp(offset + 1, 0, _document.TextLength);
        var nextPoint = _view.GetPointFromOffset(nextOffset, ShowLineNumbers);
        double textAreaLeft = ShowLineNumbers ? _view.TextAreaLeft : 0;
        double x = Math.Max(textAreaLeft, point.X);
        if (x > RenderSize.Width)
            return;

        double charWidth = Math.Abs(nextPoint.Y - point.Y) <= 0.1
            ? Math.Max(1, nextPoint.X - point.X)
            : Math.Max(1, _view.CharWidth);
        var bracketHighlightBrush = ResolveThemeBrush("OneAccentSubtle", s_defaultBracketHighlightBrush, "SelectionBackground");
        var bracketHighlightPen = ResolveThemePen("OneBorderFocused", s_defaultBracketHighlightPen, "AccentBrush");
        dc.DrawRectangle(bracketHighlightBrush, bracketHighlightPen, new Rect(x, y, charWidth, _view.LineHeight));
    }

    private void DrawScopeGuides(DrawingContext dc, double contentWidth, double contentHeight)
    {
        if (_foldingManager.Foldings.Count == 0 || _view.LineHeight <= 0 ||
            contentWidth <= 0 || contentHeight <= 0)
        {
            return;
        }

        int firstVisibleLine = _view.FirstVisibleLineNumber;
        int lastVisibleLine = _view.LastVisibleLineNumber;
        var hoveredSection = _hoveredScopeGuideSection;
        var scopeGuidePen = ResolveThemePen("OneEditorIndentGuide", s_scopeGuidePen, "ControlBorder");
        var scopeGuideActivePen = ResolveThemePen("OneBorderFocused", s_scopeGuideActivePen, "AccentBrush");
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

            var guidePen = ReferenceEquals(section, hoveredSection) ? scopeGuideActivePen : scopeGuidePen;
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
        if (_foldingManager.Foldings.Count == 0 || _view.LineHeight <= 0 ||
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

        if (_view.LineHeight <= 0)
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
        var guideLine = _document.GetLineByNumber(scopeStartLine);
        int startColumn = Math.Clamp(guideColumn, 0, guideLine.Length);
        int endColumn = Math.Clamp(startColumn + 1, 0, guideLine.Length);
        int startOffset = guideLine.Offset + startColumn;
        int endOffset = guideLine.Offset + endColumn;
        var p0 = _view.GetPointFromOffset(startOffset, ShowLineNumbers);
        var p1 = _view.GetPointFromOffset(endOffset, ShowLineNumbers);
        double guideWidth = Math.Abs(p1.Y - p0.Y) <= 0.1
            ? Math.Max(1, p1.X - p0.X)
            : Math.Max(1, _view.CharWidth);
        guideX = p0.X + guideWidth * 0.5;
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
            Foreground = ResolveThemeBrush("OnePopupText", s_scopeGuideTooltipTextBrush, "TextPrimary"),
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
        var tooltipBackgroundBrush = ResolveThemeBrush("OnePopupBackground", s_scopeGuideTooltipBackgroundBrush, "TooltipBackground");
        var tooltipBorderPen = ResolveThemePen("OnePopupBorder", s_scopeGuideTooltipBorderPen, "TooltipBorder");
        dc.DrawRoundedRectangle(tooltipBackgroundBrush, tooltipBorderPen, tooltipRect, 3, 3);
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
            Foreground = ResolveThemeBrush("OnePopupText", s_scopeGuideTooltipTextBrush, "TextPrimary")
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
        var tooltipBackgroundBrush = ResolveThemeBrush("OnePopupBackground", s_scopeGuideTooltipBackgroundBrush, "TooltipBackground");
        var tooltipBorderPen = ResolveThemePen("OnePopupBorder", s_scopeGuideTooltipBorderPen, "TooltipBorder");
        dc.DrawRoundedRectangle(tooltipBackgroundBrush, tooltipBorderPen, tooltipRect, 3, 3);
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
        if (_foldingManager.Foldings.Count == 0 || _view.LineHeight <= 0 ||
            contentWidth <= 0 || contentHeight <= 0)
        {
            return;
        }

        int firstVisibleLine = _view.FirstVisibleLineNumber;
        int lastVisibleLine = _view.LastVisibleLineNumber;
        int previousStartLine = -1;
        var foldedHintBackgroundBrush = ResolveThemeBrush("OneSurfaceDefault", s_foldedHintBackgroundBrush, "ControlBackground");
        var foldedHintSelectedBackgroundBrush = ResolveThemeBrush("OneSurfaceSelected", s_foldedHintSelectedBackgroundBrush, "SelectionBackground");
        var foldedHintBorderPen = ResolveThemePen("OneBorderDefault", s_foldedHintBorderPen, "ControlBorder");
        var foldedHintSelectedBorderPen = ResolveThemePen("OneBorderFocused", s_foldedHintSelectedBorderPen, "AccentBrush");
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
                isSelected ? foldedHintSelectedBackgroundBrush : foldedHintBackgroundBrush,
                isSelected ? foldedHintSelectedBorderPen : foldedHintBorderPen,
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
        if (!section.IsFolded || _view.LineHeight <= 0)
            return false;

        if (!_view.TryGetLineTop(section.StartLine, out double lineTop))
            return false;

        if (lineTop + _view.LineHeight < 0 || lineTop > contentHeight)
            return false;

        var lineText = _document.GetLineText(section.StartLine);
        int previewColumn = GetCollapsedPreviewColumn(section, lineText);
        double textAreaLeft = ShowLineNumbers ? _view.TextAreaLeft : 0;
        var line = _document.GetLineByNumber(section.StartLine);
        int previewOffset = line.Offset + Math.Clamp(previewColumn, 0, line.Length);
        var previewPoint = _view.GetPointFromOffset(previewOffset, ShowLineNumbers);
        double anchorX = previewPoint.X;
        double x = Math.Max(textAreaLeft + 1, anchorX + 2);
        if (x >= contentWidth)
            return false;

        if (!hintLayout.IsMeasured)
            TextMeasurement.MeasureText(hintLayout);

        double measuredWidth = hintLayout.Width > 0
            ? hintLayout.Width
            : Math.Max(18, hintLayout.Text.Length * 6.5);
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

    private FormattedText CreateFoldedSectionHintLayout(string text, string fontFamily, double fontSize)
    {
        var formatted = new FormattedText(text, fontFamily, Math.Max(10, fontSize - 1))
        {
            Foreground = ResolveThemeBrush("OneTextSecondary", s_foldedHintTextBrush, "TextSecondary")
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
        if (!_isImeComposing || string.IsNullOrEmpty(_imeCompositionString) || _view.LineHeight <= 0)
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
        var text = new FormattedText(_imeCompositionString, fontFamily, fontSize)
        {
            Foreground = ResolveImeCompositionTextBrush()
        };
        TextMeasurement.MeasureText(text);
        double measuredWidth = text.Width > 0
            ? text.Width
            : _imeCompositionString.Length * Math.Max(1, _view.CharWidth);
        double width = Math.Max(1, measuredWidth);

        dc.DrawRectangle(ResolveImeCompositionBackgroundBrush(), null, new Rect(x, y, width, _view.LineHeight));
        dc.DrawText(text, new Point(x, y));
        dc.DrawLine(ResolveImeCompositionUnderlinePen(), new Point(x, y + _view.LineHeight - 1), new Point(x + width, y + _view.LineHeight - 1));
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

        dc.DrawRectangle(ResolveGutterOverflowOverlayBrush(), null, shieldRect);
        _view.RenderLineNumbers(
            dc,
            _caret,
            ResolveForegroundBrush(),
            ResolveLineNumberForegroundBrush(),
            fontFamily,
            fontSize);
    }

    private void DrawFoldingMarkers(DrawingContext dc, double contentWidth, double contentHeight)
    {
        if (!ShowLineNumbers ||
            _view.LineHeight <= 0 ||
            _foldingManager.Foldings.Count == 0 ||
            contentWidth <= 0 ||
            contentHeight <= 0)
            return;

        // Folding glyphs belong to the fixed gutter lane only.
        // Guard with an explicit gutter clip so they never leak into the editor content
        // area or right-side scrollbar track when host clipping/layout is tight.
        double gutterRight = Math.Min(contentWidth, Math.Max(0, _view.TextAreaLeft));
        if (gutterRight <= 0)
            return;

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, gutterRight, contentHeight)));
        try
        {
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
                if (markerRect.Left < 0 || markerRect.Right > gutterRight + 0.5)
                    continue;

                bool isSelected = IsFoldingSectionSelected(section);
                if (isSelected)
                    dc.DrawRoundedRectangle(ResolveFoldingMarkerSelectedBackgroundBrush(), null, markerRect, 2, 2);

                double centerX = markerRect.X + markerRect.Width * 0.5;
                double centerY = markerRect.Y + markerRect.Height * 0.5;
                var foldingGuidePen = ResolveFoldingGuidePen();

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
                                foldingGuidePen,
                                new Point(centerX, guideStartY),
                                new Point(centerX, guideEndY));

                            dc.DrawLine(
                                foldingGuidePen,
                                new Point(centerX, guideEndY),
                                new Point(centerX + Math.Max(4, markerRect.Width * 0.45), guideEndY));
                        }
                    }
                }
                else if (section.IsFolded)
                {
                    dc.DrawLine(
                        foldingGuidePen,
                        new Point(centerX, centerY),
                        new Point(centerX + Math.Max(3, markerRect.Width * 0.4), centerY));
                }

                DrawFoldingChevron(dc, markerRect, section.IsFolded, isSelected);
            }
        }
        finally
        {
            dc.Pop();
        }
    }

    private void DrawFoldingChevron(DrawingContext dc, Rect markerRect, bool folded, bool selected)
    {
        var chevronPen = ResolveFoldingChevronPen(selected);
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

    private double GetMinimapReservedWidth(double viewportWidth)
    {
        if (!ShowMinimap || viewportWidth <= 0)
            return 0;

        return Math.Clamp(_minimapRenderer.Width, 0, viewportWidth);
    }

    private double GetContentRenderWidth(double viewportWidth)
    {
        bool overlayVerticalScrollBar = ShouldOverlayVerticalScrollBarOnMinimap(viewportWidth);
        double verticalScrollReserve = _isVerticalScrollBarVisible && !overlayVerticalScrollBar ? ScrollBarThickness : 0;
        return Math.Max(0, viewportWidth - verticalScrollReserve - GetMinimapReservedWidth(viewportWidth));
    }

    private double GetContentRenderHeight(double viewportHeight)
    {
        return Math.Max(0, viewportHeight - (_isHorizontalScrollBarVisible ? ScrollBarThickness : 0));
    }

    private void DrawMinimap(DrawingContext dc)
    {
        _minimapViewportRect = Rect.Empty;
        if (!ShowMinimap || _minimapRect.IsEmpty || _document.LineCount <= 0)
            return;

        var minimapBackgroundBrush = ResolveThemeBrush("OneMinimapBackground", s_minimapBackgroundBrush, "WindowBackground");
        var minimapForegroundBrush = ResolveThemeBrush("OneMinimapContent", s_minimapForegroundBrush);
        var minimapViewportBrush = ResolveThemeBrush("OneMinimapSlider", s_minimapViewportBrush);
        var minimapViewportBorderPen = ResolveThemePen("OnePaneChromeBorder", s_minimapViewportBorderPen, "OneBorderDefault");

        _minimapRenderer.Render(
            dc,
            _document,
            _view,
            SyntaxHighlighter,
            _minimapRect,
            minimapBackgroundBrush,
            minimapForegroundBrush,
            minimapViewportBrush);
        _minimapViewportRect = _minimapRenderer.GetViewportRect(_document, _view, _minimapRect);
        if (!_minimapViewportRect.IsEmpty)
        {
            var borderRect = new Rect(
                _minimapViewportRect.X + 0.5,
                _minimapViewportRect.Y + 0.5,
                Math.Max(0, _minimapViewportRect.Width - 1),
                Math.Max(0, _minimapViewportRect.Height - 1));
            dc.DrawRectangle(null, minimapViewportBorderPen, borderRect);
        }

        DrawMinimapHoverTooltip(dc);
    }

    private void DrawMinimapHoverTooltip(DrawingContext dc)
    {
        if (!_isMinimapHovering || !_hasPointerPosition || _minimapRect.IsEmpty || _document.LineCount <= 0)
            return;

        int lineNumber = _minimapRenderer.GetLineFromPoint(_lastPointerPosition.Y, _minimapRect, _view, _document);
        lineNumber = Math.Clamp(lineNumber, 1, _document.LineCount);
        _minimapHoverLineNumber = lineNumber;

        double availableTextWidth = Math.Max(40, _minimapRect.X - (MinimapTooltipOffsetX + MinimapTooltipPaddingX * 2 + 4));
        int digits = Math.Max(1, _document.LineCount.ToString().Length);
        int prefixChars = digits + 3; // marker + line number + space
        int maxPreviewChars = Math.Clamp((int)Math.Floor(availableTextWidth / Math.Max(1, _view.CharWidth)) - prefixChars, 8, MinimapTooltipMaxPreviewCharsPerLine);
        string tooltipText = BuildMinimapTooltipText(lineNumber, maxPreviewChars);
        if (string.IsNullOrWhiteSpace(tooltipText))
            return;

        string fontFamily = FontFamily ?? "Cascadia Code";
        double fontSize = FontSize > 0 ? FontSize : 14;
        var tooltip = new FormattedText(tooltipText, fontFamily, Math.Max(11, fontSize - 1))
        {
            Foreground = ResolveThemeBrush("OnePopupText", s_minimapTooltipTextBrush, "TextPrimary"),
            MaxTextWidth = availableTextWidth,
            MaxTextHeight = Math.Max(18, _minimapRect.Height - 4),
            Trimming = TextTrimming.CharacterEllipsis,
        };
        TextMeasurement.MeasureText(tooltip);

        double tooltipWidth = Math.Max(24, Math.Ceiling(tooltip.Width + MinimapTooltipPaddingX * 2));
        double tooltipHeight = Math.Max(20, Math.Ceiling(tooltip.Height + MinimapTooltipPaddingY * 2));
        double tooltipX = _minimapRect.X - tooltipWidth - MinimapTooltipOffsetX;
        if (tooltipX < 2)
            tooltipX = 2;

        double tooltipMaxY = Math.Max(2, _minimapRect.Bottom - tooltipHeight - 2);
        double textLineHeight = Math.Max(1, tooltip.Height / MinimapTooltipPreviewLineCount);
        double targetLineCenterY = _lastPointerPosition.Y;
        double desiredTooltipY = targetLineCenterY - MinimapTooltipPaddingY - (MinimapTooltipPreviewCenterIndex + 0.5) * textLineHeight;
        double tooltipY = Math.Clamp(desiredTooltipY, 2, tooltipMaxY);

        var tooltipRect = new Rect(tooltipX, tooltipY, tooltipWidth, tooltipHeight);
        var tooltipBackgroundBrush = ResolveThemeBrush("OnePopupBackground", s_minimapTooltipBackgroundBrush, "TooltipBackground");
        var tooltipBorderPen = ResolveThemePen("OnePopupBorder", s_minimapTooltipBorderPen, "TooltipBorder");
        dc.DrawRoundedRectangle(tooltipBackgroundBrush, tooltipBorderPen, tooltipRect, 3, 3);
        dc.DrawText(tooltip, new Point(tooltipRect.X + MinimapTooltipPaddingX, tooltipRect.Y + MinimapTooltipPaddingY));
    }

    private string BuildMinimapTooltipText(int lineNumber, int maxPreviewCharsPerLine)
    {
        if (_document.LineCount <= 0)
            return string.Empty;

        int centerLine = Math.Clamp(lineNumber, 1, _document.LineCount);
        int digits = Math.Max(1, _document.LineCount.ToString().Length);
        int maxChars = Math.Clamp(maxPreviewCharsPerLine, 8, MinimapTooltipMaxPreviewCharsPerLine);
        var lines = new string[MinimapTooltipPreviewLineCount];
        for (int i = 0; i < MinimapTooltipPreviewLineCount; i++)
        {
            int relative = i - MinimapTooltipPreviewCenterIndex;
            int previewLine = centerLine + relative;
            bool inRange = previewLine >= 1 && previewLine <= _document.LineCount;
            string marker = relative == 0 ? ">" : " ";
            string lineLabel = inRange ? previewLine.ToString().PadLeft(digits) : new string(' ', digits);
            string lineText = inRange ? TruncateMinimapPreviewLine(_document.GetLineText(previewLine), maxChars) : string.Empty;
            lines[i] = $"{marker}{lineLabel} {lineText}";
        }

        return string.Join('\n', lines);
    }

    private static string TruncateMinimapPreviewLine(string text, int maxChars)
    {
        string normalized = text.TrimEnd();
        if (normalized.Length <= maxChars)
            return normalized;

        return normalized[..Math.Max(0, maxChars - 3)].TrimEnd() + "...";
    }

    private void DrawScrollBars(DrawingContext dc)
    {
        var scrollBarTrackBrush = ResolveThemeBrush("OneScrollbarBackground", s_scrollBarTrackBrush, "ControlBackground");
        var scrollBarThumbBrush = ResolveThemeBrush("OneScrollbarThumb", s_scrollBarThumbBrush);
        var scrollBarActiveThumbBrush = ResolveThemeBrush("OneScrollbarThumbActive", s_scrollBarActiveThumbBrush);

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

        void DrawBackdropBlur(Rect rect)
        {
            if (!s_scrollBarBackdropBlurEffect.HasEffect || rect.Width <= 0 || rect.Height <= 0)
                return;

            double radius = Math.Min(ScrollBarCornerRadius, Math.Min(rect.Width, rect.Height) * 0.5);
            dc.DrawBackdropEffect(rect, s_scrollBarBackdropBlurEffect, new CornerRadius(radius));
        }

        void DrawRoundedBar(Brush brush, Rect rect)
        {
            if (rect.Width <= 0 || rect.Height <= 0)
                return;

            double radius = Math.Min(ScrollBarCornerRadius, Math.Min(rect.Width, rect.Height) * 0.5);
            dc.DrawRoundedRectangle(brush, null, rect, radius, radius);
        }

        if (_isVerticalScrollBarVisible && !_isVerticalScrollBarOverlayingMinimap)
        {
            var verticalTrackRect = InsetRect(_verticalScrollTrackRect, ScrollBarInnerPadding);
            DrawBackdropBlur(verticalTrackRect);
            DrawRoundedBar(scrollBarTrackBrush, verticalTrackRect);
            DrawRoundedBar(
                _scrollBarDragMode == ScrollBarDragMode.Vertical ? scrollBarActiveThumbBrush : scrollBarThumbBrush,
                InsetRect(_verticalScrollThumbRect, ScrollBarInnerPadding));
        }

        if (_isHorizontalScrollBarVisible)
        {
            var horizontalTrackRect = InsetRect(_horizontalScrollTrackRect, ScrollBarInnerPadding);
            DrawBackdropBlur(horizontalTrackRect);
            DrawRoundedBar(scrollBarTrackBrush, horizontalTrackRect);
            DrawRoundedBar(
                _scrollBarDragMode == ScrollBarDragMode.Horizontal ? scrollBarActiveThumbBrush : scrollBarThumbBrush,
                InsetRect(_horizontalScrollThumbRect, ScrollBarInnerPadding));
        }

        if (_isVerticalScrollBarVisible && _isHorizontalScrollBarVisible && !_isVerticalScrollBarOverlayingMinimap)
        {
            var corner = new Rect(
                Math.Max(0, RenderSize.Width - ScrollBarThickness),
                Math.Max(0, RenderSize.Height - ScrollBarThickness),
                ScrollBarThickness,
                ScrollBarThickness);
            var cornerTrackRect = InsetRect(corner, ScrollBarInnerPadding);
            DrawBackdropBlur(cornerTrackRect);
            DrawRoundedBar(scrollBarTrackBrush, cornerTrackRect);
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
        _minimapRect = Rect.Empty;
        _minimapViewportRect = Rect.Empty;
        _isVerticalScrollBarVisible = false;
        _isVerticalScrollBarOverlayingMinimap = false;
        _isHorizontalScrollBarVisible = false;
        _effectiveViewportHeight = Math.Max(0, viewportSize.Height);
        _effectiveTextViewportWidth = Math.Max(0, GetTextViewportWidth(Math.Max(0, viewportSize.Width - GetMinimapReservedWidth(viewportSize.Width))));
        _view.ViewportWidth = Math.Max(0, viewportSize.Width);
        _view.ViewportHeight = Math.Max(0, viewportSize.Height);

        if (viewportSize.Width <= 0 || viewportSize.Height <= 0 || _view.LineHeight <= 0)
            return;

        double minimapReservedWidth = GetMinimapReservedWidth(viewportSize.Width);
        bool overlayVerticalScrollBar = ShouldOverlayVerticalScrollBarOnMinimap(viewportSize.Width);
        bool needVertical = false;
        bool needHorizontal = false;
        for (int i = 0; i < 4; i++)
        {
            double verticalScrollReserve = needVertical && !overlayVerticalScrollBar ? ScrollBarThickness : 0;
            double availableWidth = Math.Max(0, viewportSize.Width - minimapReservedWidth - verticalScrollReserve);
            double availableHeight = Math.Max(0, viewportSize.Height - (needHorizontal ? ScrollBarThickness : 0));
            double textViewportWidth = Math.Max(0, GetTextViewportWidth(availableWidth));

            bool nextVertical = GetMaxVerticalOffset(availableHeight) > 0.5;
            bool nextHorizontal = GetDocumentTextContentWidth() > textViewportWidth + 0.5;
            if (nextVertical == needVertical && nextHorizontal == needHorizontal)
                break;

            needVertical = nextVertical;
            needHorizontal = nextHorizontal;
        }

        double finalVerticalScrollReserve = needVertical && !overlayVerticalScrollBar ? ScrollBarThickness : 0;
        double finalAvailableWidth = Math.Max(0, viewportSize.Width - minimapReservedWidth - finalVerticalScrollReserve);
        double finalAvailableHeight = Math.Max(0, viewportSize.Height - (needHorizontal ? ScrollBarThickness : 0));
        _effectiveViewportHeight = finalAvailableHeight;
        _effectiveTextViewportWidth = Math.Max(0, GetTextViewportWidth(finalAvailableWidth));
        _view.ViewportWidth = finalAvailableWidth;
        _view.ViewportHeight = finalAvailableHeight;

        _isVerticalScrollBarVisible = needVertical;
        _isHorizontalScrollBarVisible = needHorizontal;
        ClampScrollOffsetsToViewport();
        if (_isScrollAnimating)
        {
            _scrollAnimationTargetVerticalOffset = Math.Clamp(_scrollAnimationTargetVerticalOffset, 0, GetMaxVerticalOffset());
            _scrollAnimationTargetHorizontalOffset = Math.Clamp(_scrollAnimationTargetHorizontalOffset, 0, GetMaxHorizontalOffset());
        }

        if (ShowMinimap && minimapReservedWidth > 0 && finalAvailableHeight > 0)
        {
            double minimapX = Math.Max(0, viewportSize.Width - minimapReservedWidth - finalVerticalScrollReserve);
            _minimapRect = new Rect(minimapX, 0, minimapReservedWidth, finalAvailableHeight);
            _minimapViewportRect = _minimapRenderer.GetViewportRect(_document, _view, _minimapRect);
        }

        if (_isVerticalScrollBarVisible)
        {
            if (overlayVerticalScrollBar && !_minimapRect.IsEmpty)
            {
                _isVerticalScrollBarOverlayingMinimap = true;
                _verticalScrollTrackRect = Rect.Empty;
                _verticalScrollThumbRect = Rect.Empty;
            }
            else
            {
                double trackHeight = finalAvailableHeight;
                double trackX = Math.Max(0, viewportSize.Width - ScrollBarThickness);

                _verticalScrollTrackRect = new Rect(
                    trackX,
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

    private bool ShouldOverlayVerticalScrollBarOnMinimap(double viewportWidth)
    {
        return ShowMinimap && GetMinimapReservedWidth(viewportWidth) > 0;
    }

    private double GetTextViewportWidth(double viewportWidth)
    {
        double textAreaLeft = ShowLineNumbers ? _view.TextAreaLeft : 0;
        return Math.Max(0, viewportWidth - textAreaLeft);
    }

    private double GetDocumentTextContentWidth()
    {
        if (_document.LineCount <= 0)
            return 0;

        if (_cachedMaxLineLengthVersion != _document.Version)
        {
            double maxWidth = 0;
            for (int lineNumber = 1; lineNumber <= _document.LineCount; lineNumber++)
            {
                double lineWidth = _view.GetLineTextWidth(lineNumber);
                if (lineWidth > maxWidth)
                    maxWidth = lineWidth;
            }

            _cachedMaxLineWidth = maxWidth;
            _cachedMaxLineLengthVersion = _document.Version;
        }

        double trailingAdornmentWidth = Math.Max(0, GetAdditionalHorizontalContentWidth());
        return _cachedMaxLineWidth + 16 + trailingAdornmentWidth;
    }

    /// <summary>
    /// Allows derived editors to reserve extra horizontal content width for
    /// adornments rendered to the right of line text (for example ErrorLens-like overlays).
    /// </summary>
    protected virtual double GetAdditionalHorizontalContentWidth() => 0;

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
        double maxVerticalOffset = GetMaxVerticalOffset();
        double maxHorizontalOffset = GetMaxHorizontalOffset();
        _view.VerticalOffset = Math.Clamp(_view.VerticalOffset, 0, maxVerticalOffset);
        _view.HorizontalOffset = Math.Clamp(_view.HorizontalOffset, 0, maxHorizontalOffset);
    }

    private void SetFollowingBottomState(bool value)
    {
        _isFollowingBottom = value;
    }

    private double GetBottomFollowTolerance()
    {
        return Math.Max(1, _view.LineHeight * 0.5);
    }

    private bool IsAtBottomWithinTolerance()
    {
        return IsAtBottomWithinTolerance(GetMaxVerticalOffset());
    }

    private bool IsAtBottomWithinTolerance(double maxVerticalOffset)
    {
        return maxVerticalOffset - _view.VerticalOffset <= GetBottomFollowTolerance();
    }

    private void UpdateFollowingBottomStateAfterVerticalChange(bool userInitiated)
    {
        if (!AutoFollowBottom)
        {
            SetFollowingBottomState(false);
            return;
        }

        double maxOffset = GetMaxVerticalOffset();
        bool atBottom = maxOffset - _view.VerticalOffset <= GetBottomFollowTolerance();
        if (userInitiated || _isFollowingBottom || atBottom)
            SetFollowingBottomState(atBottom);
    }

    private void SetScrollOffsetsImmediate(double verticalOffset, double horizontalOffset, bool userInitiated, bool cancelAnimation)
    {
        if (cancelAnimation && !_isApplyingScrollAnimationStep)
            CancelScrollAnimation();

        EnsureViewLayoutMetrics();
        UpdateScrollBarLayout(RenderSize);
        _view.VerticalOffset = verticalOffset;
        _view.HorizontalOffset = horizontalOffset;
        ClampScrollOffsetsToViewport();
        UpdateFollowingBottomStateAfterVerticalChange(userInitiated);
        UpdateImeWindowIfComposing();
        InvalidateVisual();
    }

    private void SetVerticalOffsetImmediate(double verticalOffset, bool userInitiated, bool cancelAnimation)
    {
        SetScrollOffsetsImmediate(verticalOffset, _view.HorizontalOffset, userInitiated, cancelAnimation);
    }

    private void SetHorizontalOffsetImmediate(double horizontalOffset, bool cancelAnimation)
    {
        SetScrollOffsetsImmediate(_view.VerticalOffset, horizontalOffset, userInitiated: false, cancelAnimation: cancelAnimation);
    }

    private void SetVerticalOffset(double verticalOffset, bool allowAnimation, bool userInitiated)
    {
        double horizontalTarget = _isScrollAnimating ? _scrollAnimationTargetHorizontalOffset : _view.HorizontalOffset;
        SetScrollOffsets(verticalOffset, horizontalTarget, allowAnimation, userInitiated);
    }

    private void SetHorizontalOffset(double horizontalOffset, bool allowAnimation, bool userInitiated)
    {
        double verticalTarget = _isScrollAnimating ? _scrollAnimationTargetVerticalOffset : _view.VerticalOffset;
        SetScrollOffsets(verticalTarget, horizontalOffset, allowAnimation, userInitiated);
    }

    double IEditorViewMetrics.VerticalOffset => _view.VerticalOffset;

    double IEditorViewMetrics.HorizontalOffset => _view.HorizontalOffset;

    double IEditorViewMetrics.LineHeight => _view.LineHeight;

    double IEditorViewMetrics.ViewportWidth => _view.ViewportWidth;

    double IEditorViewMetrics.LineNumberAreaLeft => _view.LineNumberAreaLeft;

    double IEditorViewMetrics.GutterWidth => _view.GutterWidth;

    double IEditorViewMetrics.FoldingLaneLeft => _view.FoldingLaneLeft;

    double IEditorViewMetrics.TextAreaLeft => _view.TextAreaLeft;

    int IEditorViewMetrics.FirstVisibleLineNumber => _view.FirstVisibleLineNumber;

    Rect IEditorViewMetrics.MinimapRect => _minimapRect;

    Rect IEditorViewMetrics.VerticalScrollTrackRect => _verticalScrollTrackRect;

    Point IEditorViewMetrics.GetPointFromOffset(int offset, bool showLineNumbers)
    {
        int clampedOffset = Math.Clamp(offset, 0, _document.TextLength);
        return _view.GetPointFromOffset(clampedOffset, showLineNumbers);
    }

    double IEditorViewMetrics.GetAbsoluteLineTop(int lineNumber)
    {
        return _view.GetAbsoluteLineTop(lineNumber);
    }

    void IEditorViewMetrics.SetVerticalOffset(double verticalOffset, bool allowAnimation, bool userInitiated)
    {
        SetVerticalOffset(verticalOffset, allowAnimation, userInitiated);
    }

    private void SetScrollOffsets(double verticalOffset, double horizontalOffset, bool allowAnimation, bool userInitiated)
    {
        EnsureViewLayoutMetrics();
        UpdateScrollBarLayout(RenderSize);

        double maxVerticalOffset = GetMaxVerticalOffset();
        double maxHorizontalOffset = GetMaxHorizontalOffset();
        double targetVerticalOffset = Math.Clamp(verticalOffset, 0, maxVerticalOffset);
        double targetHorizontalOffset = Math.Clamp(horizontalOffset, 0, maxHorizontalOffset);
        bool animate = allowAnimation && IsScrollInertiaEnabled && GetEffectiveScrollInertiaDurationMs() > 0;

        if (!animate)
        {
            SetScrollOffsetsImmediate(targetVerticalOffset, targetHorizontalOffset, userInitiated, cancelAnimation: true);
            return;
        }

        _scrollAnimationTargetVerticalOffset = targetVerticalOffset;
        _scrollAnimationTargetHorizontalOffset = targetHorizontalOffset;
        if (userInitiated && AutoFollowBottom)
        {
            bool targetAtBottom = maxVerticalOffset - targetVerticalOffset <= GetBottomFollowTolerance();
            SetFollowingBottomState(targetAtBottom);
        }

        StartScrollAnimation();
    }

    private void StartScrollAnimation()
    {
        if (!IsScrollInertiaEnabled || GetEffectiveScrollInertiaDurationMs() <= 0)
        {
            SnapScrollAnimationToTargetAndStop();
            return;
        }

        _isScrollAnimating = true;
        if (_scrollAnimationTimer == null)
        {
            _scrollAnimationTimer = new DispatcherTimer();
            _scrollAnimationTimer.Interval = TimeSpan.FromMilliseconds(SmoothScrollIntervalMs);
            _scrollAnimationTimer.Tick += OnScrollAnimationTick;
        }

        if (!_scrollAnimationTimer.IsEnabled)
        {
            _lastScrollAnimationTickTime = Environment.TickCount64;
            _scrollAnimationTimer.Start();
        }

        OnScrollAnimationTick(null, EventArgs.Empty);
    }

    private void StopScrollAnimation()
    {
        _scrollAnimationTimer?.Stop();
        _isScrollAnimating = false;
    }

    private void CancelScrollAnimation()
    {
        if (!_isScrollAnimating)
            return;

        StopScrollAnimation();
        _scrollAnimationTargetVerticalOffset = _view.VerticalOffset;
        _scrollAnimationTargetHorizontalOffset = _view.HorizontalOffset;
    }

    private void SnapScrollAnimationToTargetAndStop()
    {
        if (!_isScrollAnimating)
            return;

        _scrollAnimationTargetVerticalOffset = Math.Clamp(_scrollAnimationTargetVerticalOffset, 0, GetMaxVerticalOffset());
        _scrollAnimationTargetHorizontalOffset = Math.Clamp(_scrollAnimationTargetHorizontalOffset, 0, GetMaxHorizontalOffset());
        _isApplyingScrollAnimationStep = true;
        try
        {
            _view.VerticalOffset = _scrollAnimationTargetVerticalOffset;
            _view.HorizontalOffset = _scrollAnimationTargetHorizontalOffset;
            ClampScrollOffsetsToViewport();
        }
        finally
        {
            _isApplyingScrollAnimationStep = false;
        }

        UpdateFollowingBottomStateAfterVerticalChange(userInitiated: false);
        UpdateImeWindowIfComposing();
        InvalidateVisual();
        StopScrollAnimation();
    }

    private void OnScrollAnimationTick(object? sender, EventArgs e)
    {
        if (!_isScrollAnimating)
            return;

        EnsureViewLayoutMetrics();
        UpdateScrollBarLayout(RenderSize);

        _scrollAnimationTargetVerticalOffset = Math.Clamp(_scrollAnimationTargetVerticalOffset, 0, GetMaxVerticalOffset());
        _scrollAnimationTargetHorizontalOffset = Math.Clamp(_scrollAnimationTargetHorizontalOffset, 0, GetMaxHorizontalOffset());

        long now = Environment.TickCount64;
        long elapsedMs = now - _lastScrollAnimationTickTime;
        if (elapsedMs <= 0)
            elapsedMs = Math.Max(1, SmoothScrollIntervalMs);
        _lastScrollAnimationTickTime = now;

        double dtSeconds = Math.Min(elapsedMs / 1000.0, SmoothScrollMaxDeltaTimeSeconds);
        double alpha = ComputeSmoothAlpha(dtSeconds);
        double minStep = SmoothScrollMinSpeedPixelsPerSecond * dtSeconds;

        double nextVerticalOffset = _view.VerticalOffset;
        double nextHorizontalOffset = _view.HorizontalOffset;
        bool verticalMoved = StepSmoothAxis(_scrollAnimationTargetVerticalOffset, _view.VerticalOffset, alpha, minStep, out nextVerticalOffset);
        bool horizontalMoved = StepSmoothAxis(_scrollAnimationTargetHorizontalOffset, _view.HorizontalOffset, alpha, minStep, out nextHorizontalOffset);

        if (!verticalMoved && !horizontalMoved)
        {
            StopScrollAnimation();
            return;
        }

        _isApplyingScrollAnimationStep = true;
        try
        {
            _view.VerticalOffset = nextVerticalOffset;
            _view.HorizontalOffset = nextHorizontalOffset;
            ClampScrollOffsetsToViewport();
        }
        finally
        {
            _isApplyingScrollAnimationStep = false;
        }

        UpdateFollowingBottomStateAfterVerticalChange(userInitiated: false);
        UpdateImeWindowIfComposing();
        InvalidateVisual();
    }

    private double GetEffectiveScrollInertiaDurationMs()
    {
        double duration = ScrollInertiaDurationMs;
        if (double.IsNaN(duration) || double.IsInfinity(duration))
            return DefaultScrollInertiaDurationMs;
        return duration;
    }

    private double ComputeSmoothAlpha(double dtSeconds)
    {
        double durationMs = GetEffectiveScrollInertiaDurationMs();
        if (durationMs <= 0 || dtSeconds <= 0)
            return 1.0;

        double durationSeconds = durationMs / 1000.0;
        double decay = -Math.Log(SmoothScrollDurationTailRatio) / durationSeconds;
        double alpha = 1.0 - Math.Exp(-decay * dtSeconds);
        return Math.Clamp(alpha, 0.0, 1.0);
    }

    private static bool StepSmoothAxis(double target, double current, double alpha, double minStep, out double next)
    {
        next = current;
        double remaining = target - current;
        if (Math.Abs(remaining) <= 0.01)
            return false;

        if (Math.Abs(remaining) <= SmoothScrollSnapThreshold)
        {
            next = target;
            return true;
        }

        double step = remaining * alpha;
        if (Math.Abs(step) < minStep)
            step = Math.Sign(remaining) * minStep;
        if (Math.Abs(step) > Math.Abs(remaining))
            step = remaining;

        next = current + step;
        return true;
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
        string? text = Clipboard.GetText();
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
