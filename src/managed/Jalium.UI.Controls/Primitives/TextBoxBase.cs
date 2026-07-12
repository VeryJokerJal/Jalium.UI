using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using Jalium.UI.Threading;

using static Jalium.UI.Input.Cursors;

namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Base class for text editing controls.
/// </summary>
public abstract class TextBoxBase : Control
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Automation.Peers.TextBoxBaseAutomationPeer(this);
    }

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

    /// <summary>
    /// Invalidates the measure of the inner text-rendering element (the
    /// <c>TextBoxContentHost</c> inserted into <c>PART_ContentHost</c>).
    /// WPF's layout cache keys off each element's own <c>IsMeasureValid</c>
    /// flag, so calling <c>InvalidateMeasure</c> on this control only marks
    /// the outer chrome dirty — the host child stays "valid" and its
    /// <c>MeasureOverride</c> is skipped on the next pass. That means the
    /// ScrollViewer above the host keeps using a stale DesiredSize (often
    /// from when Text was empty), so its extent under-reports the real
    /// content height and the scrollbar can't reach the end of long text.
    /// Subclasses call this whenever Text / TextWrapping / FontSize / any
    /// other property that changes the text-layout size changes.
    /// </summary>
    protected void InvalidateTextContentMeasure()
    {
        _textBoxContentHost?.InvalidateMeasure();
    }

    #endregion

    #region Grapheme Cluster Boundary Helpers

    /// <summary>
    /// Snaps <paramref name="offset"/> to the nearest legal grapheme-cluster
    /// boundary in <paramref name="text"/>. A grapheme cluster is one
    /// user-perceived character; it can span several UTF-16 code units — an
    /// emoji, a skin-tone or ZWJ sequence, a flag, or a base letter plus
    /// combining marks. Snapping stops a caret, selection endpoint or edit from
    /// cutting such a cluster apart, which would render broken glyph fragments
    /// and let the user select or delete only part of one emoji.
    /// </summary>
    /// <remarks>
    /// An <paramref name="offset"/> that falls inside a cluster is moved past it
    /// (<paramref name="snapForward"/>=true — the normal case while extending a
    /// selection or moving the caret right) or before it
    /// (<paramref name="snapForward"/>=false — used when moving left). Values
    /// outside <c>[0, text.Length]</c> are clamped. See <see cref="GraphemeClusters"/>.
    /// </remarks>
    protected static int SnapToCharacterBoundary(string text, int offset, bool snapForward = true)
        => GraphemeClusters.Snap(text, offset, snapForward);

    /// <summary>
    /// Returns the offset of the next grapheme-cluster boundary after
    /// <paramref name="offset"/>, so caret motion and forward deletion always
    /// move over one whole user-perceived character — a multi-code-unit emoji,
    /// ZWJ sequence or combining sequence travels as a single unit.
    /// </summary>
    protected static int StepForwardGrapheme(string text, int offset)
        => GraphemeClusters.NextBoundary(text, offset);

    /// <summary>
    /// Returns the offset of the previous grapheme-cluster boundary before
    /// <paramref name="offset"/>, so Backspace and the Left arrow consume an
    /// entire user-perceived character rather than a fragment of one.
    /// </summary>
    protected static int StepBackwardGrapheme(string text, int offset)
        => GraphemeClusters.PreviousBoundary(text, offset);

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
    /// The local-space caret rectangle that was last rendered.  Subclasses
    /// should update this at the end of their DrawCaret path so caret blink
    /// ticks can invalidate ONLY this rect instead of the entire control —
    /// a multi-line TextBox 600 px wide would otherwise mark its whole
    /// visual bounds dirty 2 × per blink cycle, which quickly compounds past
    /// the 50 % partial-to-full promotion threshold.
    /// Empty when the caret has not been rendered yet (first frame) — the
    /// blink tick then falls back to whole-element invalidation.
    /// </summary>
    protected Rect _lastRenderedCaretRect = Rect.Empty;

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

    private int _changeBlockLevel;
    private UndoEntry? _changeBlockStart;
    private bool _changeBlockUndoCaptured;

    /// <summary>
    /// Whether an undo/redo operation is in progress.
    /// </summary>
    protected bool _isUndoRedoing;

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

    private static readonly SolidColorBrush s_defaultSelectionBrush = new(Color.FromArgb(180, 0x1E, 0x79, 0x3F));
    private static readonly SolidColorBrush s_defaultCaretBrush = new(Color.White);

    private SolidColorBrush? _selectionOpacityBrush;
    private SolidColorBrush? _selectionOpacitySource;
    private double _selectionOpacityCache = double.NaN;

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
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(TextBoxBase),
            new PropertyMetadata(false, OnIsReadOnlyPropertyChanged));

    private static void OnIsReadOnlyPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBoxBase tb)
        {
            tb.OnIsReadOnlyChanged((bool)e.OldValue!, (bool)e.NewValue!);
        }
    }

    /// <summary>
    /// Called when <see cref="IsReadOnly"/> changes. Detaches the host window's
    /// IME context while read-only so the candidate / composition window stays
    /// hidden, even if the control is currently focused.
    /// </summary>
    protected virtual void OnIsReadOnlyChanged(bool oldValue, bool newValue)
    {
        if (this is IImeSupport && IsKeyboardFocused)
        {
            FindHostWindow()?.RefreshInputMethodAssociation();
        }
    }

    /// <summary>
    /// Walks the visual tree to locate the containing <see cref="Window"/>,
    /// or <c>null</c> when the control has not been parented yet.
    /// </summary>
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
    /// Identifies the AcceptsReturn dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty AcceptsReturnProperty =
        DependencyProperty.Register(nameof(AcceptsReturn), typeof(bool), typeof(TextBoxBase),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the AcceptsTab dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty AcceptsTabProperty =
        DependencyProperty.Register(nameof(AcceptsTab), typeof(bool), typeof(TextBoxBase),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the SelectionBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SelectionBrushProperty =
        DependencyProperty.Register(nameof(SelectionBrush), typeof(Brush), typeof(TextBoxBase),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the SelectionTextBrush dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectionTextBrushProperty =
        DependencyProperty.Register(nameof(SelectionTextBrush), typeof(Brush), typeof(TextBoxBase),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the SelectionOpacity dependency property.
    /// </summary>
    public static readonly DependencyProperty SelectionOpacityProperty =
        DependencyProperty.Register(nameof(SelectionOpacity), typeof(double), typeof(TextBoxBase),
            new PropertyMetadata(1.0, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the AutoWordSelection dependency property.
    /// </summary>
    public static readonly DependencyProperty AutoWordSelectionProperty =
        DependencyProperty.Register(nameof(AutoWordSelection), typeof(bool), typeof(TextBoxBase),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the IsReadOnlyCaretVisible dependency property.
    /// </summary>
    public static readonly DependencyProperty IsReadOnlyCaretVisibleProperty =
        DependencyProperty.Register(nameof(IsReadOnlyCaretVisible), typeof(bool), typeof(TextBoxBase),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsInactiveSelectionHighlightEnabled dependency property.
    /// </summary>
    public static readonly DependencyProperty IsInactiveSelectionHighlightEnabledProperty =
        DependencyProperty.Register(
            nameof(IsInactiveSelectionHighlightEnabled),
            typeof(bool),
            typeof(TextBoxBase),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    internal static readonly DependencyPropertyKey IsSelectionActivePropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(IsSelectionActive),
            typeof(bool),
            typeof(TextBoxBase),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the read-only IsSelectionActive dependency property.
    /// </summary>
    public static readonly DependencyProperty IsSelectionActiveProperty =
        IsSelectionActivePropertyKey.DependencyProperty;

    /// <summary>
    /// Identifies the CaretBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CaretBrushProperty =
        DependencyProperty.Register(nameof(CaretBrush), typeof(Brush), typeof(TextBoxBase),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsUndoEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsUndoEnabledProperty =
        DependencyProperty.Register(nameof(IsUndoEnabled), typeof(bool), typeof(TextBoxBase),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the UndoLimit dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty UndoLimitProperty =
        DependencyProperty.Register(nameof(UndoLimit), typeof(int), typeof(TextBoxBase),
            new PropertyMetadata(100));

    /// <summary>
    /// Identifies the HorizontalScrollBarVisibility dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty HorizontalScrollBarVisibilityProperty =
        DependencyProperty.Register(nameof(HorizontalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(TextBoxBase),
            new PropertyMetadata(ScrollBarVisibility.Hidden));

    /// <summary>
    /// Identifies the VerticalScrollBarVisibility dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty VerticalScrollBarVisibilityProperty =
        DependencyProperty.Register(nameof(VerticalScrollBarVisibility), typeof(ScrollBarVisibility), typeof(TextBoxBase),
            new PropertyMetadata(ScrollBarVisibility.Hidden));

    /// <summary>
    /// Identifies the TextTrimming dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextTrimmingProperty =
        DependencyProperty.Register(nameof(TextTrimming), typeof(TextTrimming), typeof(TextBoxBase),
            new PropertyMetadata(TextTrimming.CharacterEllipsis, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the TextChanged routed event.
    /// </summary>
    public static readonly RoutedEvent TextChangedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(TextChanged),
            RoutingStrategy.Bubble,
            typeof(TextChangedEventHandler),
            typeof(TextBoxBase));

    /// <summary>
    /// Identifies the SelectionChanged routed event.
    /// </summary>
    public static readonly RoutedEvent SelectionChangedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(SelectionChanged),
            RoutingStrategy.Bubble,
            typeof(RoutedEventHandler),
            typeof(TextBoxBase));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets whether the text box is read-only.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty)!;
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the text box accepts Enter key for new lines.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public bool AcceptsReturn
    {
        get => (bool)GetValue(AcceptsReturnProperty)!;
        set => SetValue(AcceptsReturnProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the text box accepts Tab key for tab characters.
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
    /// Gets or sets the brush used to draw selected text.
    /// </summary>
    public Brush? SelectionTextBrush
    {
        get => (Brush?)GetValue(SelectionTextBrushProperty);
        set => SetValue(SelectionTextBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the opacity applied to the selection highlight.
    /// </summary>
    public double SelectionOpacity
    {
        get => (double)GetValue(SelectionOpacityProperty)!;
        set => SetValue(SelectionOpacityProperty, value);
    }

    /// <summary>
    /// Gets or sets whether mouse drags expand the selection by whole words.
    /// </summary>
    public bool AutoWordSelection
    {
        get => (bool)GetValue(AutoWordSelectionProperty)!;
        set => SetValue(AutoWordSelectionProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the caret remains visible while the control is read-only.
    /// </summary>
    public bool IsReadOnlyCaretVisible
    {
        get => (bool)GetValue(IsReadOnlyCaretVisibleProperty)!;
        set => SetValue(IsReadOnlyCaretVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets whether a selection remains highlighted while keyboard focus is elsewhere.
    /// </summary>
    public bool IsInactiveSelectionHighlightEnabled
    {
        get => (bool)GetValue(IsInactiveSelectionHighlightEnabledProperty)!;
        set => SetValue(IsInactiveSelectionHighlightEnabledProperty, value);
    }

    /// <summary>
    /// Gets whether this control currently owns the active selection.
    /// </summary>
    public bool IsSelectionActive => (bool)GetValue(IsSelectionActiveProperty)!;

    /// <summary>
    /// Gets the spell-checking options associated with this text box.
    /// </summary>
    public SpellCheck SpellCheck => new(this);

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
    /// Gets or sets the trimming behavior for visible text when it overflows the content area.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
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

    /// <summary>
    /// Gets the horizontal size of the scrollable text content.
    /// </summary>
    public double ExtentWidth => ScrollHost?.ExtentWidth ?? 0.0;

    /// <summary>
    /// Gets the vertical size of the scrollable text content.
    /// </summary>
    public double ExtentHeight => ScrollHost?.ExtentHeight ?? 0.0;

    /// <summary>
    /// Gets the horizontal size of the visible text viewport.
    /// </summary>
    public double ViewportWidth => ScrollHost?.ViewportWidth ?? 0.0;

    /// <summary>
    /// Gets the vertical size of the visible text viewport.
    /// </summary>
    public double ViewportHeight => ScrollHost?.ViewportHeight ?? 0.0;

    private ScrollViewer? ScrollHost => _contentHost as ScrollViewer;

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
    /// Gets the X position of a character at a given column within a line of text.
    /// Uses DirectWrite's native hit testing to ensure the position matches
    /// the actual character layout used during text rendering.
    /// Falls back to prefix substring measurement if native context is unavailable.
    /// </summary>
    /// <param name="lineText">The full line text.</param>
    /// <param name="column">The column index (0-based).</param>
    /// <returns>The X pixel offset from the start of the line.</returns>
    protected double GetCharacterXInLine(string lineText, int column)
    {
        if (string.IsNullOrEmpty(lineText) || column <= 0)
            return 0;

        int clampedColumn = Math.Clamp(column, 0, lineText.Length);
        if (clampedColumn <= 0)
            return 0;

        var fontFamily = FontFamily?.Source ?? FrameworkElement.DefaultFontFamilyName;
        var fontSize = FontSize > 0 ? FontSize : 14;

        if (clampedColumn < lineText.Length)
        {
            if (TextMeasurement.HitTestTextPosition(lineText, fontFamily, fontSize, (uint)clampedColumn, false, out var hitResult)
                && hitResult.CaretX > 0)
                return hitResult.CaretX;
        }
        else
        {
            if (TextMeasurement.HitTestTextPosition(lineText, fontFamily, fontSize, (uint)(clampedColumn - 1), true, out var hitResult)
                && hitResult.CaretX > 0)
                return hitResult.CaretX;
        }

        // Fallback: measure prefix substring
        return MeasureTextWidth(lineText.Substring(0, clampedColumn));
    }

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
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        AddHandler(TextInputEvent, new TextCompositionEventHandler(OnTextInputHandler));
        AddHandler(MouseWheelEvent, new MouseWheelEventHandler(OnMouseWheelHandler));
    }

    #endregion

    #region Template Handling

    /// <inheritdoc />
    public override void OnApplyTemplate()
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
    /// Gets the vertical extent used by the built-in mouse-wheel scroller.
    /// </summary>
    protected virtual double GetVerticalScrollExtentHeight(double lineHeight)
    {
        return Math.Max(1, GetLineCount()) * lineHeight;
    }

    /// <summary>
    /// Gets the vertical viewport used by the built-in mouse-wheel scroller.
    /// </summary>
    protected virtual double GetVerticalScrollViewportHeight()
    {
        return RenderSize.Height;
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
    internal abstract void RenderTextContent(DrawingContext drawingContext);

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
            // Snap forward so a caret placed inside a surrogate pair lands on
            // the next whole code-point boundary instead of splitting an emoji.
            var newValue = SnapToCharacterBoundary(text, Math.Clamp(value, 0, text.Length), snapForward: true);
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
            // Snap backward: a selection that starts in the middle of a
            // surrogate pair should grow to include the whole emoji.
            var newValue = SnapToCharacterBoundary(text, Math.Clamp(value, 0, text.Length), snapForward: false);
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
            var raw = Math.Clamp(value, 0, maxLength);
            // Snap selection end forward: include the trailing half of a
            // surrogate pair so the selected text never contains a lone half-emoji.
            var snappedEnd = SnapToCharacterBoundary(text, _selectionStart + raw, snapForward: true);
            var newValue = snappedEnd - _selectionStart;
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
    /// Appends text to the current content.
    /// </summary>
    public void AppendText(string textData)
    {
        if (textData is null)
        {
            return;
        }

        string text = GetText() + textData;
        SetText(text);
        _caretIndex = text.Length;
        _selectionStart = _caretIndex;
        _selectionLength = 0;
        EnsureCaretVisible();
    }

    /// <summary>
    /// Begins an undo change block. Nested blocks are coalesced into one undo unit.
    /// </summary>
    public void BeginChange()
    {
        if (_changeBlockLevel++ == 0)
        {
            _changeBlockStart = new UndoEntry(
                GetText(),
                _caretIndex,
                _selectionStart,
                _selectionLength);
            _changeBlockUndoCaptured = false;
        }
    }

    /// <summary>
    /// Ends the current undo change block.
    /// </summary>
    public void EndChange()
    {
        if (_changeBlockLevel == 0)
        {
            throw new InvalidOperationException("EndChange must be paired with a preceding BeginChange call.");
        }

        if (--_changeBlockLevel == 0)
        {
            _changeBlockStart = null;
            _changeBlockUndoCaptured = false;
        }
    }

    /// <summary>
    /// Begins a change block that ends when the returned object is disposed.
    /// </summary>
    public IDisposable DeclareChangeBlock()
    {
        BeginChange();
        return new ChangeBlock(this);
    }

    /// <summary>
    /// Prevents the current undo unit from being reopened. Jalium undo units are
    /// closed eagerly, so the current unit is already locked when this returns.
    /// </summary>
    public void LockCurrentUndoUnit()
    {
    }

    /// <summary>Scrolls one line to the left.</summary>
    public void LineLeft() => ScrollByHorizontalLine(-1);

    /// <summary>Scrolls one line to the right.</summary>
    public void LineRight() => ScrollByHorizontalLine(1);

    /// <summary>Scrolls one line upward.</summary>
    public void LineUp() => ScrollByVerticalLine(-1);

    /// <summary>Scrolls one line downward.</summary>
    public void LineDown() => ScrollByVerticalLine(1);

    /// <summary>Scrolls one horizontal page to the left.</summary>
    public void PageLeft()
    {
        if (ScrollHost is { } scrollHost)
        {
            FindHostWindow()?.EnsureLayoutValidForInput();
            scrollHost.PageLeft();
            return;
        }

        HorizontalOffset = Math.Max(0, HorizontalOffset - Math.Max(0, RenderSize.Width));
    }

    /// <summary>Scrolls one horizontal page to the right.</summary>
    public void PageRight()
    {
        if (ScrollHost is { } scrollHost)
        {
            FindHostWindow()?.EnsureLayoutValidForInput();
            scrollHost.PageRight();
            return;
        }

        HorizontalOffset += Math.Max(0, RenderSize.Width);
    }

    /// <summary>Scrolls one vertical page upward.</summary>
    public void PageUp()
    {
        if (ScrollHost is { } scrollHost)
        {
            FindHostWindow()?.EnsureLayoutValidForInput();
            scrollHost.PageUp();
            return;
        }

        VerticalOffset = Math.Max(0, VerticalOffset - Math.Max(0, GetVerticalScrollViewportHeight()));
    }

    /// <summary>Scrolls one vertical page downward.</summary>
    public void PageDown()
    {
        if (ScrollHost is { } scrollHost)
        {
            FindHostWindow()?.EnsureLayoutValidForInput();
            scrollHost.PageDown();
            return;
        }

        double viewport = Math.Max(0, GetVerticalScrollViewportHeight());
        double extent = GetVerticalScrollExtentHeight(Math.Round(GetLineHeight()));
        VerticalOffset = Math.Min(Math.Max(0, extent - viewport), VerticalOffset + viewport);
    }

    /// <summary>Scrolls to the beginning of the content.</summary>
    public void ScrollToHome()
    {
        if (ScrollHost is { } scrollHost)
        {
            FindHostWindow()?.EnsureLayoutValidForInput();
            scrollHost.ScrollToHome();
            return;
        }

        HorizontalOffset = 0;
    }

    /// <summary>Scrolls to the end of the content.</summary>
    public void ScrollToEnd()
    {
        if (ScrollHost is { } scrollHost)
        {
            FindHostWindow()?.EnsureLayoutValidForInput();
            scrollHost.ScrollToEnd();
            return;
        }

        double maxWidth = 0;
        for (int index = 0; index < GetLineCount(); index++)
        {
            maxWidth = Math.Max(maxWidth, MeasureTextWidth(GetLineTextInternal(index)));
        }

        HorizontalOffset = Math.Max(0, maxWidth - RenderSize.Width);
    }

    /// <summary>Scrolls to a horizontal offset.</summary>
    public void ScrollToHorizontalOffset(double offset)
    {
        if (double.IsNaN(offset))
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (ScrollHost is { } scrollHost)
        {
            FindHostWindow()?.EnsureLayoutValidForInput();
            scrollHost.ScrollToHorizontalOffset(offset);
            return;
        }

        HorizontalOffset = offset;
    }

    /// <summary>Scrolls to a vertical offset.</summary>
    public void ScrollToVerticalOffset(double offset)
    {
        if (double.IsNaN(offset))
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (ScrollHost is { } scrollHost)
        {
            FindHostWindow()?.EnsureLayoutValidForInput();
            scrollHost.ScrollToVerticalOffset(offset);
            return;
        }

        VerticalOffset = offset;
    }

    private void ScrollByHorizontalLine(int direction)
    {
        if (ScrollHost is { } scrollHost)
        {
            FindHostWindow()?.EnsureLayoutValidForInput();
            if (direction < 0)
            {
                scrollHost.LineLeft();
            }
            else
            {
                scrollHost.LineRight();
            }

            return;
        }

        HorizontalOffset = Math.Max(0, HorizontalOffset + (direction * 16.0));
    }

    private void ScrollByVerticalLine(int direction)
    {
        if (ScrollHost is { } scrollHost)
        {
            FindHostWindow()?.EnsureLayoutValidForInput();
            if (direction < 0)
            {
                scrollHost.LineUp();
            }
            else
            {
                scrollHost.LineDown();
            }

            return;
        }

        double lineHeight = Math.Round(GetLineHeight());
        double viewport = Math.Max(0, GetVerticalScrollViewportHeight());
        double extent = GetVerticalScrollExtentHeight(lineHeight);
        VerticalOffset = Math.Clamp(
            VerticalOffset + (direction * lineHeight),
            0,
            Math.Max(0, extent - viewport));
    }

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
        // Snap both endpoints so a programmatic Select() that lands inside a
        // surrogate pair (emoji) widens to include the full code point.
        var snappedStart = SnapToCharacterBoundary(text, Math.Clamp(start, 0, text.Length), snapForward: false);
        var rawEnd = Math.Clamp(snappedStart + length, snappedStart, text.Length);
        var snappedEnd = SnapToCharacterBoundary(text, rawEnd, snapForward: true);
        _selectionStart = snappedStart;
        _selectionLength = snappedEnd - snappedStart;
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
    public virtual bool Undo()
    {
        if (!IsUndoEnabled || _undoStack.Count == 0)
            return false;

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
        return true;
    }

    /// <summary>
    /// Redoes the last undone operation.
    /// </summary>
    public virtual bool Redo()
    {
        if (!IsUndoEnabled || _redoStack.Count == 0)
            return false;

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
        return true;
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

        // Snap both endpoints to code-point boundaries so Shift+Arrow over an
        // emoji never leaves half a surrogate pair selected (which then renders
        // as a tofu glyph and corrupts Copy/Cut).
        var text = GetText();
        bool extendingRight = newCaretIndex >= _selectionAnchor;
        int anchor = SnapToCharacterBoundary(text, _selectionAnchor, snapForward: !extendingRight);
        int snappedCaret = SnapToCharacterBoundary(text, newCaretIndex, snapForward: extendingRight);

        _selectionStart = Math.Min(anchor, snappedCaret);
        _selectionLength = Math.Abs(snappedCaret - anchor);
        _caretIndex = snappedCaret;

        OnSelectionChanged();
    }

    /// <summary>
    /// Pushes the current state to the undo stack.
    /// </summary>
    protected void PushUndo()
    {
        if (!IsUndoEnabled || _isUndoRedoing)
            return;

        if (_changeBlockLevel > 0)
        {
            if (_changeBlockUndoCaptured || _changeBlockStart is null)
            {
                return;
            }

            _changeBlockUndoCaptured = true;
            PushUndoCore(_changeBlockStart);
            return;
        }

        PushUndoCore(new UndoEntry(GetText(), _caretIndex, _selectionStart, _selectionLength));
    }

    private void PushUndoCore(UndoEntry undoEntry)
    {
        if (_undoStack.TryPeek(out var entry) &&
            entry.Text == undoEntry.Text &&
            entry.CaretIndex == undoEntry.CaretIndex &&
            entry.SelectionStart == undoEntry.SelectionStart &&
            entry.SelectionLength == undoEntry.SelectionLength)
        {
            return;
        }

        _undoStack.Push(undoEntry);
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

        // Get the character X position within the line using native hit testing
        var lineText = GetLineTextInternal(lineIndex);
        var clampedColumn = Math.Clamp(columnIndex, 0, lineText.Length);
        var caretX = Math.Round(GetCharacterXInLine(lineText, clampedColumn));
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

        // Clamp horizontal offset so we don't over-scroll when text gets shorter (e.g. after deletion).
        // Measure the full line width and ensure we never scroll past the point where the text ends.
        var fullLineWidth = Math.Round(GetCharacterXInLine(lineText, lineText.Length));
        if (fullLineWidth > contentWidth)
        {
            var maxOffset = fullLineWidth - contentWidth + 2;
            if (_horizontalOffset > maxOffset)
                _horizontalOffset = maxOffset;
        }
        else
        {
            _horizontalOffset = 0;
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
        // Notify UI Automation so external clients (screen readers, dictation, translation/look-up
        // tools) can detect the new selection. Only do the work when a client has attached and the
        // element's peer actually exposes the Text pattern — otherwise this is a cheap null check.
        if (Jalium.UI.Automation.Peers.AutomationPeer.ListenerExists())
        {
            var peer = GetAutomationPeer();
            if (peer?.GetPattern(Jalium.UI.Automation.Peers.PatternInterface.Text) != null)
                peer.RaiseAutomationEvent(Jalium.UI.Automation.Peers.AutomationEvents.TextPatternOnTextSelectionChanged);
        }

        OnSelectionChanged(new RoutedEventArgs(SelectionChangedEvent, this));
    }

    /// <summary>
    /// Raises the SelectionChanged routed event.
    /// </summary>
    protected virtual void OnSelectionChanged(RoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(e);
    }

    /// <summary>
    /// Raises the TextChanged routed event.
    /// </summary>
    protected virtual void OnTextChanged(TextChangedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        RaiseEvent(e);
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

        if (string.IsNullOrEmpty(lineText))
            return lineStart;

        // Use DirectWrite's native hit testing for accurate character mapping.
        // This ensures the hit position matches exactly how DirectWrite lays out
        // characters within the rendered text, avoiding prefix-measurement drift.
        var fontFamily = FontFamily?.Source ?? FrameworkElement.DefaultFontFamilyName;
        var fontSize = FontSize > 0 ? FontSize : 14;
        if (TextMeasurement.HitTestPoint(lineText, fontFamily, fontSize, (float)contentX, out var hitResult))
        {
            int column = (int)hitResult.TextPosition;
            if (hitResult.IsTrailingHit != 0)
                column++;
            return lineStart + Math.Clamp(column, 0, lineText.Length);
        }

        // Fallback: linear search using prefix measurement
        int columnIndex = 0;
        double prevWidth = 0;

        for (int i = 0; i <= lineText.Length; i++)
        {
            var width = MeasureTextWidth(lineText.Substring(0, i));
            if (width >= contentX)
            {
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

    private void OnTextInputHandler(object sender, TextCompositionEventArgs e)
    {
        if (e.Handled || IsReadOnly)
            return;

        if (!string.IsNullOrEmpty(e.Text))
        {
            // Filter control characters
            var text = e.Text;
            if (text.Length == 1 && char.IsControl(text[0]) && text[0] != '\t')
                return;

            InsertText(text);
            e.Handled = true;
        }
    }

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        // Don't handle clicks that originated from buttons in the template
        if (e.OriginalSource is DependencyObject source && IsInsideButton(source))
            return;

        if (e.ChangedButton == MouseButton.Left)
        {
            // Close context menu if open
            _contextMenuAnimTimer?.Stop();
            _isContextMenuCloseAnimating = false;
            CloseContextMenuImmediate();

            Focus();

            var position = e.GetPosition(this);
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
                // Snap a hit that fell between the two halves of a surrogate
                // pair to the closest legal boundary so the caret never lands
                // mid-emoji.
                var text = GetText();
                newCaretIndex = SnapToCharacterBoundary(text, newCaretIndex, snapForward: true);

                if ((e.KeyboardModifiers & ModifierKeys.Shift) != 0)
                {
                    ExtendSelection(newCaretIndex);
                }
                else
                {
                    _caretIndex = newCaretIndex;
                    if (AutoWordSelection && text.Length > 0)
                    {
                        SelectCurrentWord();
                        _wordSelectionAnchorStart = _selectionStart;
                        _wordSelectionAnchorEnd = _selectionStart + _selectionLength;
                        _isWordSelecting = _selectionLength > 0;
                    }
                    else
                    {
                        _selectionAnchor = newCaretIndex;
                        _selectionStart = newCaretIndex;
                        _selectionLength = 0;
                        _isWordSelecting = false;
                        OnSelectionChanged();
                    }

                    _isSelecting = true;
                }

                ResetCaretBlink();
            }

            InvalidateVisual();
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            Focus();
            ShowContextMenu();
            e.Handled = true;
        }
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;

        // Don't handle clicks that originated from buttons in the template
        if (e.OriginalSource is DependencyObject source && IsInsideButton(source))
            return;

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

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (!IsEnabled || !_isSelecting) return;

        var position = e.GetPosition(this);
        var newCaretIndex = GetCaretIndexFromPosition(position);

        if (_isWordSelecting)
        {
            ExtendWordSelection(newCaretIndex);
        }
        else
        {
            // Snap both endpoints so a drag that lands inside a surrogate pair
            // widens to include the whole emoji rather than splitting it.
            var text = GetText();
            bool draggingRight = newCaretIndex >= _selectionAnchor;
            int anchor = SnapToCharacterBoundary(text, _selectionAnchor, snapForward: !draggingRight);
            int snappedCaret = SnapToCharacterBoundary(text, newCaretIndex, snapForward: draggingRight);
            _selectionStart = Math.Min(anchor, snappedCaret);
            _selectionLength = Math.Abs(snappedCaret - anchor);
            _caretIndex = snappedCaret;
        }

        EnsureCaretVisible();
        InvalidateVisual();
        e.Handled = true;
    }

    private void OnMouseWheelHandler(object sender, MouseWheelEventArgs e)
    {
        // Round line height for consistent scrolling
        var lineHeight = Math.Round(GetLineHeight());
        var delta = e.Delta > 0 ? -3 : 3;

        var maxOffset = Math.Max(0, GetVerticalScrollExtentHeight(lineHeight) - GetVerticalScrollViewportHeight());
        if (maxOffset <= 0)
            return;

        var oldOffset = VerticalOffset;
        var newOffset = Math.Clamp(oldOffset + delta * lineHeight, 0, maxOffset);
        if (Math.Abs(newOffset - oldOffset) <= 0.001)
            return;

        VerticalOffset = newOffset;
        e.Handled = true;
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
        SetValue(IsSelectionActivePropertyKey, isFocused);

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
        if (IsReadOnly && !IsReadOnlyCaretVisible)
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
        if (!IsKeyboardFocused || (IsReadOnly && !IsReadOnlyCaretVisible))
        {
            StopCaretTimer();
            return;
        }

        // Invalidate ONLY the caret rect when a subclass has published one.
        // Fallback to whole-element invalidation on the first frame (before
        // DrawCaret has run once) and when the cached rect is empty.
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

    #endregion

    #region Key Handlers

    private void HandleLeftKey(bool shift, bool ctrl)
    {
        var text = GetText();
        int newIndex;

        if (ctrl)
        {
            newIndex = FindPreviousWordBoundary(_caretIndex);
        }
        else
        {
            // Step over a whole grapheme cluster so an emoji — including a
            // multi-scalar ZWJ or skin-tone sequence — is one Left-arrow press.
            newIndex = StepBackwardGrapheme(text, _caretIndex);
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
            // Step over a whole grapheme cluster (symmetric with HandleLeftKey).
            newIndex = StepForwardGrapheme(text, _caretIndex);
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
            // Carrying a column across lines can land mid-cluster; snap so the
            // caret never settles inside an emoji on the line above.
            var newIndex = SnapToCharacterBoundary(
                GetText(), GetCharIndexFromLineColumn(lineIndex - 1, columnIndex), snapForward: true);
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
            // Snap the carried column so the caret never settles inside an
            // emoji on the line below.
            var newIndex = SnapToCharacterBoundary(
                GetText(), GetCharIndexFromLineColumn(lineIndex + 1, columnIndex), snapForward: true);
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

            var text = GetText();
            int startIndex;
            if (ctrl)
            {
                // Delete previous word
                startIndex = FindPreviousWordBoundary(_caretIndex);
            }
            else
            {
                // Delete a whole grapheme cluster so Backspace removes the whole
                // emoji — ZWJ sequence, skin tone or flag included — never a fragment.
                startIndex = StepBackwardGrapheme(text, _caretIndex);
            }

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

                int endIndex;
                if (ctrl)
                {
                    endIndex = FindNextWordBoundary(_caretIndex);
                }
                else
                {
                    // Delete a whole grapheme cluster forward (symmetric with Backspace).
                    endIndex = StepForwardGrapheme(text, _caretIndex);
                }

                SetText(text.Substring(0, _caretIndex) + text.Substring(Math.Min(endIndex, text.Length)));
            }
        }

        EnsureCaretVisible();
    }

    /// <summary>
    /// Classifies the grapheme cluster that begins at boundary
    /// <paramref name="start"/>. A cluster is a word separator when it is a
    /// single whitespace code unit, or — when <paramref name="includePunctuation"/>
    /// is set — a single punctuation code unit. Any multi-code-unit cluster
    /// (emoji, ZWJ or combining sequence) is always a word character, so a
    /// word-wise caret jump or a double-click never splits one.
    /// </summary>
    private static bool IsWordSeparatorAt(string text, int start, bool includePunctuation)
    {
        if (start < 0 || start >= text.Length)
            return false;
        // A separator is always a lone code unit; a wider cluster is an emoji
        // or combining sequence, which counts as a word character.
        if (GraphemeClusters.NextBoundary(text, start) - start != 1)
            return false;
        char c = text[start];
        return char.IsWhiteSpace(c) || (includePunctuation && char.IsPunctuation(c));
    }

    private int FindPreviousWordBoundary(int index)
    {
        var text = GetText();
        if (index <= 0)
            return 0;

        // Walk in grapheme-cluster steps: start one cluster back from the caret.
        int i = GraphemeClusters.PreviousBoundary(text, index);

        // Skip whitespace clusters.
        while (i > 0 && IsWordSeparatorAt(text, i, includePunctuation: false))
            i = GraphemeClusters.PreviousBoundary(text, i);

        // Walk back to the start of the word.
        while (i > 0 && !IsWordSeparatorAt(text, GraphemeClusters.PreviousBoundary(text, i), includePunctuation: false))
            i = GraphemeClusters.PreviousBoundary(text, i);

        return i;
    }

    private int FindNextWordBoundary(int index)
    {
        var text = GetText();
        if (index >= text.Length)
            return text.Length;

        int i = GraphemeClusters.Snap(text, index, forward: true);

        // Skip the current word.
        while (i < text.Length && !IsWordSeparatorAt(text, i, includePunctuation: false))
            i = GraphemeClusters.NextBoundary(text, i);

        // Skip whitespace clusters.
        while (i < text.Length && IsWordSeparatorAt(text, i, includePunctuation: false))
            i = GraphemeClusters.NextBoundary(text, i);

        return i;
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

        int length = text.Length;
        // Operate on grapheme-cluster boundaries so an emoji is one indivisible
        // unit — never half-selected by a double-click.
        int pos = GraphemeClusters.Snap(text, Math.Clamp(index, 0, length), forward: false);

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

    private void SelectCurrentWord()
    {
        var text = GetText();
        if (string.IsNullOrEmpty(text))
            return;

        // Walk word boundaries in grapheme-cluster space so the selection edges
        // land on whole user-perceived characters.
        int pos = GraphemeClusters.Snap(text, _caretIndex, forward: false);

        int start = pos;
        while (start > 0
               && !IsWordSeparatorAt(text, GraphemeClusters.PreviousBoundary(text, start), includePunctuation: true))
        {
            start = GraphemeClusters.PreviousBoundary(text, start);
        }

        int end = pos;
        while (end < text.Length && !IsWordSeparatorAt(text, end, includePunctuation: true))
        {
            end = GraphemeClusters.NextBoundary(text, end);
        }

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

    protected Brush? ResolveSelectionBrush()
    {
        Brush? source = HasLocalValue(SelectionBrushProperty)
            ? SelectionBrush
            : SelectionBrush
                ?? ResolveThemeBrush("SelectionBackground", s_defaultSelectionBrush, "AccentFillColorSelectedTextBackgroundBrush");

        double opacity = Math.Clamp(SelectionOpacity, 0.0, 1.0);
        if (source is not SolidColorBrush solidColorBrush || opacity >= 1.0)
        {
            return source;
        }

        if (!ReferenceEquals(_selectionOpacitySource, solidColorBrush)
            || _selectionOpacityCache != opacity
            || _selectionOpacityBrush is null
            || _selectionOpacityBrush.Color != solidColorBrush.Color
            || _selectionOpacityBrush.Opacity != solidColorBrush.Opacity * opacity)
        {
            _selectionOpacitySource = solidColorBrush;
            _selectionOpacityCache = opacity;
            _selectionOpacityBrush = new SolidColorBrush(solidColorBrush.Color)
            {
                Opacity = solidColorBrush.Opacity * opacity,
            };
        }

        return _selectionOpacityBrush;
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
            AddContextMenuItem(panel, "Undo", "Ctrl+Z", CanUndo, () => AnimateCloseContextMenu(() => Undo()));
            AddContextMenuItem(panel, "Redo", "Ctrl+Y", CanRedo, () => AnimateCloseContextMenu(() => Redo()));
            AddContextMenuSeparator(panel);
            AddContextMenuItem(panel, "Cut", "Ctrl+X", hasSelection, () => AnimateCloseContextMenu(() => Cut()));
        }
        AddContextMenuItem(panel, "Copy", "Ctrl+C", hasSelection, () => AnimateCloseContextMenu(() => Copy()));
        if (!IsReadOnly)
        {
            AddContextMenuItem(panel, "Paste", "Ctrl+V", hasClipboard, () => AnimateCloseContextMenu(() => Paste()));
        }
        AddContextMenuSeparator(panel);
        AddContextMenuItem(panel, "Select All", "Ctrl+A", hasText, () => AnimateCloseContextMenu(() => SelectAll()));

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
        _contextMenuPopup.RequestHostRender();

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

            _contextMenuPopup?.RequestHostRender();
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
            _contextMenuPopup?.RequestHostRender();

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
            itemBorder.AddHandler(MouseEnterEvent, new Input.MouseEventHandler((s, e) =>
            {
                itemBorder.Background = hoverBrush;
            }));
            itemBorder.AddHandler(MouseLeaveEvent, new Input.MouseEventHandler((s, e) =>
            {
                itemBorder.Background = s_ctxMenuTransparentBrush;
            }));
            itemBorder.AddHandler(MouseUpEvent, new Input.MouseButtonEventHandler((s, e) =>
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

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBoxBase textBox)
        {
            textBox.InvalidateVisual();
        }
    }

    #endregion

    #region Helper Types

    private sealed class ChangeBlock : IDisposable
    {
        private TextBoxBase? _owner;

        internal ChangeBlock(TextBoxBase owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            TextBoxBase? owner = Interlocked.Exchange(ref _owner, null);
            owner?.EndChange();
        }
    }

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
