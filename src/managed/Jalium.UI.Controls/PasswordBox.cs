using Jalium.UI.Automation;
using Jalium.UI.Controls.Automation;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// A control for entering passwords with masked display.
/// Inherits from Control for security reasons - passwords should not be exposed via data binding.
/// </summary>
public class PasswordBox : Control, IImeSupport
{
    #region Automation

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer()
    {
        return new PasswordBoxAutomationPeer(this);
    }

    #endregion

    #region Fields

    // Internal password storage (not exposed as dependency property for security)
    private string _password = string.Empty;

    // Caret management
    private int _caretIndex;
    private int _selectionStart;
    private int _selectionLength;
    private bool _caretVisible = true;
    private double _caretOpacity = 1.0;
    private DateTime _lastCaretBlink;
    private const int CaretBlinkInterval = 530;
    private const int CaretFadeDuration = 150;
    private DispatcherTimer? _caretTimer;
    private const int CaretAnimationTickMs = 33;

    // Selection state
    private bool _isSelecting;
    private int _selectionAnchor;

    // Scrolling
    private double _horizontalOffset;

    // Undo/Redo
    private readonly Stack<UndoEntry> _undoStack = new();
    private readonly Stack<UndoEntry> _redoStack = new();
    private bool _isUndoRedoing;
    private string _lastPassword = string.Empty;

    // Double/Triple click
    private DateTime _lastClickTime;
    private int _clickCount;
    private Point _lastClickPosition;
    private const int DoubleClickTime = 500;
    private const double DoubleClickDistance = 4;

    // Text width measurement cache
    private Dictionary<string, double> _textWidthCache = new();
    private string? _cachedFontFamily;
    private double _cachedFontSize;
    private const int MaxCacheSize = 256;

    // IME support
    private bool _isImeComposing;
    private string _imeCompositionString = string.Empty;
    private int _imeCompositionCursor;

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the PasswordChar dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty PasswordCharProperty =
        DependencyProperty.Register(nameof(PasswordChar), typeof(char), typeof(PasswordBox),
            new PropertyMetadata('\u2022', OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the MaxLength dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaxLengthProperty =
        DependencyProperty.Register(nameof(MaxLength), typeof(int), typeof(PasswordBox),
            new PropertyMetadata(0));

    /// <summary>
    /// Identifies the Placeholder dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty PlaceholderTextProperty =
        DependencyProperty.Register(nameof(PlaceholderText), typeof(string), typeof(PasswordBox),
            new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the RevealMode dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty RevealModeProperty =
        DependencyProperty.Register(nameof(RevealMode), typeof(PasswordRevealMode), typeof(PasswordBox),
            new PropertyMetadata(PasswordRevealMode.Hidden, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsPasswordRevealed dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsPasswordRevealedProperty =
        DependencyProperty.Register(nameof(IsPasswordRevealed), typeof(bool), typeof(PasswordBox),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsReadOnly dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(PasswordBox),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the SelectionBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SelectionBrushProperty =
        DependencyProperty.Register(nameof(SelectionBrush), typeof(Brush), typeof(PasswordBox),
            new PropertyMetadata(new SolidColorBrush(Color.FromArgb(180, 0, 120, 212)), OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the CaretBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CaretBrushProperty =
        DependencyProperty.Register(nameof(CaretBrush), typeof(Brush), typeof(PasswordBox),
            new PropertyMetadata(new SolidColorBrush(Color.White), OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the TextTrimming dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextTrimmingProperty =
        DependencyProperty.Register(nameof(TextTrimming), typeof(TextTrimming), typeof(PasswordBox),
            new PropertyMetadata(TextTrimming.CharacterEllipsis, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsUndoEnabled dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsUndoEnabledProperty =
        DependencyProperty.Register(nameof(IsUndoEnabled), typeof(bool), typeof(PasswordBox),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the UndoLimit dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty UndoLimitProperty =
        DependencyProperty.Register(nameof(UndoLimit), typeof(int), typeof(PasswordBox),
            new PropertyMetadata(100));

    #endregion

    #region Static Brushes & Pens

    private static readonly SolidColorBrush s_fallbackPlaceholderBrush = new(Color.FromRgb(128, 128, 128));
    private static readonly SolidColorBrush s_fallbackFocusBorderBrush = new(Color.FromRgb(0, 120, 212));
    private static readonly SolidColorBrush s_fallbackWhiteBrush = new(Color.White);
    private static readonly SolidColorBrush s_compositionBgBrush = new(Color.FromRgb(60, 60, 80));
    private static readonly SolidColorBrush s_compositionTextBrush = new(Color.FromRgb(255, 255, 200));
    private static readonly SolidColorBrush s_compositionUnderlineBrush = new(Color.FromRgb(200, 200, 100));
    private static readonly Pen s_compositionUnderlinePen = new(s_compositionUnderlineBrush, 1);
    private static readonly SolidColorBrush s_revealIconBrush = new(Color.FromRgb(150, 150, 150));

    #endregion

    #region Password Property (Non-dependency for security)

    /// <summary>
    /// Gets or sets the password.
    /// This is intentionally NOT a dependency property to prevent binding and secure the value.
    /// </summary>
    public string Password
    {
        get => _password;
        set
        {
            if (_password != value)
            {
                _password = value ?? string.Empty;

                // Clamp caret index
                if (_caretIndex > _password.Length)
                {
                    _caretIndex = _password.Length;
                }

                // Clear selection if out of bounds
                if (_selectionStart + _selectionLength > _password.Length)
                {
                    _selectionStart = Math.Min(_selectionStart, _password.Length);
                    _selectionLength = 0;
                }

                InvalidateMeasure();
                RaisePasswordChanged();
            }
        }
    }

    /// <summary>
    /// Gets the password as a SecureString (for enhanced security scenarios).
    /// </summary>
    public System.Security.SecureString SecurePassword
    {
        get
        {
            var secureString = new System.Security.SecureString();
            foreach (char c in _password)
            {
                secureString.AppendChar(c);
            }
            secureString.MakeReadOnly();
            return secureString;
        }
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the masking character.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public char PasswordChar
    {
        get
        {
            var value = GetValue(PasswordCharProperty);
            if (value is char c)
                return c;
            if (value is string s && s.Length > 0)
                return s[0];
            return '\u2022';
        }
        set => SetValue(PasswordCharProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum password length (0 = unlimited).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public int MaxLength
    {
        get => (int)GetValue(MaxLengthProperty)!;
        set => SetValue(MaxLengthProperty, value);
    }

    /// <summary>
    /// Gets or sets the placeholder text shown when the password is empty.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string PlaceholderText
    {
        get => (string)(GetValue(PlaceholderTextProperty) ?? string.Empty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>
    /// Gets or sets the password reveal mode.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public PasswordRevealMode RevealMode
    {
        get => (PasswordRevealMode)(GetValue(RevealModeProperty) ?? PasswordRevealMode.Hidden);
        set => SetValue(RevealModeProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the password is currently revealed.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsPasswordRevealed
    {
        get => (bool)GetValue(IsPasswordRevealedProperty)!;
        set => SetValue(IsPasswordRevealedProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the password box is read-only.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Input)]
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty)!;
        set => SetValue(IsReadOnlyProperty, value);
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
    /// Gets or sets the trimming behavior for visible text when it overflows the content area.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public TextTrimming TextTrimming
    {
        get => (TextTrimming)GetValue(TextTrimmingProperty)!;
        set => SetValue(TextTrimmingProperty, value);
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
    /// Gets whether undo can be performed.
    /// </summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>
    /// Gets whether redo can be performed.
    /// </summary>
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// Gets or sets the caret position (character index).
    /// </summary>
    public int CaretIndex
    {
        get => _caretIndex;
        set
        {
            var newValue = Math.Clamp(_password.Length, 0, value);
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
            var newValue = Math.Clamp(_password.Length, 0, value);
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
            var maxLength = _password.Length - _selectionStart;
            var newValue = Math.Clamp(maxLength, 0, value);
            if (_selectionLength != newValue)
            {
                _selectionLength = newValue;
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Gets the selected text (masked or revealed based on IsPasswordRevealed).
    /// </summary>
    public string SelectedText
    {
        get
        {
            if (_selectionLength == 0)
                return string.Empty;

            var start = Math.Min(_selectionStart, _password.Length);
            var length = Math.Min(_selectionLength, _password.Length - start);
            return _password.Substring(start, length);
        }
    }

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the PasswordChanged routed event.
    /// </summary>
    public static readonly RoutedEvent PasswordChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(PasswordChanged), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(PasswordBox));

    /// <summary>
    /// Occurs when the password content changes.
    /// </summary>
    public event RoutedEventHandler PasswordChanged
    {
        add => AddHandler(PasswordChangedEvent, value);
        remove => RemoveHandler(PasswordChangedEvent, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="PasswordBox"/> class.
    /// </summary>
    public PasswordBox()
    {
        Focusable = true;

        // Set IBeam cursor for text input
        Cursor = Jalium.UI.Cursors.IBeam;

        _lastCaretBlink = DateTime.Now;
        _lastClickTime = DateTime.MinValue;

        // Register input event handlers
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        AddHandler(TextInputEvent, new TextCompositionEventHandler(OnTextInputHandler));

        // Subscribe to IME events
        InputMethod.CompositionStarted += OnImeCompositionStarted;
        InputMethod.CompositionUpdated += OnImeCompositionUpdated;
        InputMethod.CompositionEnded += OnImeCompositionEnded;

        // Subscribe to focus events for IME target management
        AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGotFocusHandler));
        AddHandler(LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnLostFocusHandler));
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Clears the password.
    /// </summary>
    public void Clear()
    {
        Password = string.Empty;
        _caretIndex = 0;
        _selectionStart = 0;
        _selectionLength = 0;
    }

    /// <summary>
    /// Selects all text in the password box.
    /// </summary>
    public void SelectAll()
    {
        _selectionStart = 0;
        _selectionLength = _password.Length;
        _caretIndex = _password.Length;
        InvalidateVisual();
    }

    /// <summary>
    /// Selects a range of text.
    /// </summary>
    public void Select(int start, int length)
    {
        _selectionStart = Math.Clamp(_password.Length, 0, start);
        _selectionLength = Math.Clamp(_password.Length - _selectionStart, 0, length);
        _caretIndex = _selectionStart + _selectionLength;
        InvalidateVisual();
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
        }
    }

    /// <summary>
    /// Pastes text from the clipboard as password.
    /// PasswordBox does not support Copy operation for security.
    /// </summary>
    public void Paste()
    {
        if (IsReadOnly)
            return;

        var clipboardText = Clipboard.GetText();
        if (!string.IsNullOrEmpty(clipboardText))
        {
            // Remove newlines from pasted text
            clipboardText = clipboardText.Replace("\r\n", "").Replace("\n", "").Replace("\r", "");
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
            var entry = _undoStack.Pop();
            _redoStack.Push(new UndoEntry(_password, _caretIndex, _selectionStart, _selectionLength));

            _password = entry.Text;
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
    public void Redo()
    {
        if (!IsUndoEnabled || _redoStack.Count == 0)
            return;

        _isUndoRedoing = true;
        try
        {
            var entry = _redoStack.Pop();
            _undoStack.Push(new UndoEntry(_password, _caretIndex, _selectionStart, _selectionLength));

            _password = entry.Text;
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

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        // PasswordBox always uses direct rendering (doesn't extend TextBoxBase for security)
        var padding = Padding;
        var border = BorderThickness;
        var lineHeight = Math.Round(GetLineHeight());

        var desiredHeight = lineHeight + padding.Top + padding.Bottom + border.Top + border.Bottom;

        return new Size(
            availableSize.Width,
            Math.Min(desiredHeight, availableSize.Height));
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        base.OnRender(drawingContext);

        if (drawingContext is not DrawingContext dc)
            return;

        var border = BorderThickness;
        var padding = Padding;
        var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);
        var lineHeight = Math.Round(GetLineHeight());

        // Draw background and border
        var cornerRadius = CornerRadius;
        var strokeThickness = border.Left;
        var borderRect = ControlRenderGeometry.GetStrokeAlignedRect(bounds, strokeThickness);
        var borderRadius = ControlRenderGeometry.GetStrokeAlignedCornerRadius(cornerRadius, strokeThickness);
        if (Background != null)
        {
            dc.DrawRoundedRectangle(Background, null, borderRect, borderRadius);
        }

        if (BorderBrush != null && strokeThickness > 0)
        {
            var borderPen = new Pen(BorderBrush, strokeThickness);
            dc.DrawRoundedRectangle(null, borderPen, borderRect, borderRadius);
        }

        // Content area
        var contentRect = new Rect(
            Math.Round(border.Left + padding.Left),
            Math.Round(border.Top + padding.Top),
            Math.Max(0, Math.Round(bounds.Width - border.Left - border.Right - padding.Left - padding.Right)),
            Math.Max(0, Math.Round(bounds.Height - border.Top - border.Bottom - padding.Top - padding.Bottom)));

        // Clip to content area
        dc.PushClip(new RectangleGeometry(contentRect));

        // Draw selection background
        if (_selectionLength > 0 && IsKeyboardFocused)
        {
            DrawSelection(dc, contentRect, lineHeight);
        }

        // Draw text or placeholder
        if (string.IsNullOrEmpty(_password))
        {
            if (!string.IsNullOrEmpty(PlaceholderText))
            {
                var placeholderBrush = ResolvePlaceholderBrush();
                var formattedPlaceholder = new FormattedText(PlaceholderText, FontFamily ?? "Segoe UI", FontSize)
                {
                    Foreground = placeholderBrush,
                    MaxTextWidth = contentRect.Width,
                    MaxTextHeight = contentRect.Height,
                    Trimming = TextTrimming
                };
                dc.DrawText(formattedPlaceholder, new Point(contentRect.X - Math.Round(_horizontalOffset), contentRect.Y));
            }
        }
        else
        {
            DrawText(dc, contentRect, lineHeight);
        }

        // Draw IME composition string
        if (_isImeComposing && !string.IsNullOrEmpty(_imeCompositionString))
        {
            DrawImeComposition(dc, contentRect, lineHeight);
        }

        // Draw caret
        if (IsFocused && !IsReadOnly)
        {
            DrawCaret(dc, contentRect, lineHeight);
        }

        dc.Pop(); // Pop clip

        // Draw focus indicator
        if (IsKeyboardFocused)
        {
            ControlFocusVisual.Draw(dc, this, bounds, CornerRadius);
        }

        // Draw reveal button if mode allows
        if (RevealMode == PasswordRevealMode.Peek && !string.IsNullOrEmpty(_password))
        {
            DrawRevealButton(dc, bounds);
        }
    }

    private Brush ResolveFocusedBorderBrush()
    {
        return TryFindResource("ControlBorderFocused") as Brush
            ?? TryFindResource("AccentBrush") as Brush
            ?? s_fallbackFocusBorderBrush;
    }

    private Brush ResolvePlaceholderBrush()
    {
        return TryFindResource("TextPlaceholder") as Brush
            ?? TryFindResource("TextFillColorTertiaryBrush") as Brush
            ?? s_fallbackPlaceholderBrush;
    }

    private Brush ResolveSecondaryTextBrush()
    {
        return TryFindResource("TextSecondary") as Brush ?? s_revealIconBrush;
    }

    private void DrawText(DrawingContext dc, Rect contentRect, double lineHeight)
    {
        var textBrush = Foreground
            ?? TryFindResource("TextFillColorPrimaryBrush") as Brush
            ?? s_fallbackWhiteBrush;
        var roundedHorizontalOffset = Math.Round(_horizontalOffset);

        var displayText = IsPasswordRevealed ? _password : new string(PasswordChar, _password.Length);
        var formattedText = new FormattedText(displayText, FontFamily ?? "Segoe UI", FontSize)
        {
            Foreground = textBrush,
            MaxTextWidth = Math.Max(0, contentRect.Width + roundedHorizontalOffset),
            MaxTextHeight = lineHeight,
            Trimming = TextTrimming
        };

        var x = contentRect.X - roundedHorizontalOffset;
        var y = contentRect.Y;

        dc.DrawText(formattedText, new Point(x, y));
    }

    private void DrawSelection(DrawingContext dc, Rect contentRect, double lineHeight)
    {
        if (SelectionBrush == null)
            return;

        var roundedHorizontalOffset = Math.Round(_horizontalOffset);
        var displayText = GetDisplayText();

        var textBefore = displayText.Substring(0, Math.Min(_selectionStart, displayText.Length));
        var selectedText = displayText.Substring(_selectionStart, Math.Min(_selectionLength, displayText.Length - _selectionStart));

        var startX = Math.Round(contentRect.X + MeasureDisplayTextWidth(textBefore) - roundedHorizontalOffset);
        var width = Math.Max(Math.Round(MeasureDisplayTextWidth(selectedText)), 1);

        var selRect = new Rect(startX, contentRect.Y, width, lineHeight);
        dc.DrawRectangle(SelectionBrush, null, selRect);
    }

    private void DrawImeComposition(DrawingContext dc, Rect contentRect, double lineHeight)
    {
        if (string.IsNullOrEmpty(_imeCompositionString))
            return;

        var roundedHorizontalOffset = Math.Round(_horizontalOffset);
        var textBeforeCaret = GetDisplayText().Substring(0, Math.Min(_caretIndex, _password.Length));
        var x = Math.Round(contentRect.X + MeasureDisplayTextWidth(textBeforeCaret) - roundedHorizontalOffset);
        var y = contentRect.Y;

        // Draw composition background
        var compositionWidth = MeasureDisplayTextWidth(_imeCompositionString);
        var compositionBgBrush = s_compositionBgBrush;
        dc.DrawRectangle(compositionBgBrush, null, new Rect(x, y, compositionWidth, lineHeight));

        // Draw composition text
        var compositionText = new FormattedText(_imeCompositionString, FontFamily ?? "Segoe UI", FontSize)
        {
            Foreground = s_compositionTextBrush,
            MaxTextWidth = contentRect.Width,
            MaxTextHeight = lineHeight
        };
        dc.DrawText(compositionText, new Point(x, y));

        // Draw underline
        var underlinePen = s_compositionUnderlinePen;
        dc.DrawLine(underlinePen, new Point(x, y + lineHeight - 2), new Point(x + compositionWidth, y + lineHeight - 2));
    }

    private void DrawCaret(DrawingContext dc, Rect contentRect, double lineHeight)
    {
        var caretOpacity = UpdateCaretAnimation();

        if (CaretBrush == null || _isImeComposing || caretOpacity < 0.01)
            return;

        var columnIndex = Math.Min(_caretIndex, _password.Length);
        var displayTextBeforeCaret = GetDisplayText().Substring(0, columnIndex);

        var roundedHorizontalOffset = Math.Round(_horizontalOffset);
        var x = Math.Round(contentRect.X + MeasureDisplayTextWidth(displayTextBeforeCaret) - roundedHorizontalOffset);
        var y = contentRect.Y;

        // Create brush with animated opacity
        Brush caretBrushWithOpacity;
        if (CaretBrush is SolidColorBrush solidBrush)
        {
            var color = solidBrush.Color;
            var alpha = (byte)(color.A * caretOpacity);
            caretBrushWithOpacity = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        }
        else
        {
            caretBrushWithOpacity = CaretBrush;
        }

        var caretPen = new Pen(caretBrushWithOpacity, 1.5);
        dc.DrawLine(caretPen, new Point(x, y), new Point(x, y + lineHeight));
    }

    private void DrawRevealButton(DrawingContext dc, Rect bounds)
    {
        var buttonSize = 24;
        var buttonRect = new Rect(bounds.Right - buttonSize - 4, (bounds.Height - buttonSize) / 2, buttonSize, buttonSize);

        // Draw eye icon placeholder
        var iconBrush = ResolveSecondaryTextBrush();
        var iconPen = new Pen(iconBrush, 1.5);
        var centerX = buttonRect.X + buttonRect.Width / 2;
        var centerY = buttonRect.Y + buttonRect.Height / 2;

        // Simple eye shape
        dc.DrawEllipse(null, iconPen, new Point(centerX, centerY), 6, 4);
        dc.DrawEllipse(iconBrush, null, new Point(centerX, centerY), 2, 2);
    }

    #endregion

    #region Text Operations

    private void InsertText(string textToInsert)
    {
        if (IsReadOnly || string.IsNullOrEmpty(textToInsert))
            return;

        PushUndo();

        // Delete selection if any
        if (_selectionLength > 0)
        {
            DeleteSelectionInternal();
        }

        var maxLength = MaxLength;

        // Ensure caret is within bounds
        if (_caretIndex < 0) _caretIndex = 0;
        if (_caretIndex > _password.Length) _caretIndex = _password.Length;

        // Enforce max length
        if (maxLength > 0)
        {
            var availableSpace = maxLength - _password.Length;
            if (availableSpace <= 0)
                return;

            if (textToInsert.Length > availableSpace)
            {
                textToInsert = textToInsert.Substring(0, availableSpace);
            }
        }

        // Insert text
        _password = _password.Substring(0, _caretIndex) + textToInsert + _password.Substring(_caretIndex);
        _caretIndex += textToInsert.Length;

        ResetCaretBlink();
        EnsureCaretVisible();
        RaisePasswordChanged();
    }

    private void DeleteSelection()
    {
        if (_selectionLength == 0)
            return;

        PushUndo();
        DeleteSelectionInternal();
        EnsureCaretVisible();
    }

    private void DeleteSelectionInternal()
    {
        if (_selectionLength == 0)
            return;

        // Ensure selection bounds are valid
        if (_selectionStart < 0) _selectionStart = 0;
        if (_selectionStart > _password.Length) _selectionStart = _password.Length;
        if (_selectionStart + _selectionLength > _password.Length)
            _selectionLength = _password.Length - _selectionStart;

        _password = _password.Substring(0, _selectionStart) + _password.Substring(_selectionStart + _selectionLength);
        _caretIndex = _selectionStart;
        _selectionLength = 0;

        RaisePasswordChanged();
    }

    private void ExtendSelection(int newCaretIndex)
    {
        if (_selectionLength == 0)
        {
            _selectionAnchor = _caretIndex;
        }

        _selectionStart = Math.Min(_selectionAnchor, newCaretIndex);
        _selectionLength = Math.Abs(newCaretIndex - _selectionAnchor);
        _caretIndex = newCaretIndex;

        InvalidateVisual();
    }

    private void PushUndo()
    {
        if (!IsUndoEnabled || _isUndoRedoing)
            return;

        // Don't push if password hasn't changed
        if (_password == _lastPassword)
            return;

        _undoStack.Push(new UndoEntry(_lastPassword, _caretIndex, _selectionStart, _selectionLength));
        _redoStack.Clear();

        // Limit stack size
        while (_undoStack.Count > UndoLimit)
        {
            var temp = new Stack<UndoEntry>();
            while (_undoStack.Count > 1)
            {
                temp.Push(_undoStack.Pop());
            }
            _undoStack.Pop();
            while (temp.Count > 0)
            {
                _undoStack.Push(temp.Pop());
            }
        }

        _lastPassword = _password;
    }

    #endregion

    #region Helper Methods

    private double GetLineHeight()
    {
        var fontFamily = FontFamily ?? "Segoe UI";
        var fontSize = FontSize > 0 ? FontSize : 14;
        var fontMetrics = TextMeasurement.GetFontMetrics(fontFamily, fontSize);
        return fontMetrics.LineHeight;
    }

    private string GetDisplayText()
    {
        return IsPasswordRevealed ? _password : new string(PasswordChar, _password.Length);
    }

    private double MeasureDisplayTextWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var fontFamily = FontFamily ?? "Segoe UI";
        var fontSize = FontSize > 0 ? FontSize : 14;

        // Check if font settings changed, invalidate cache if so
        if (_cachedFontFamily != fontFamily || _cachedFontSize != fontSize)
        {
            _textWidthCache.Clear();
            _cachedFontFamily = fontFamily;
            _cachedFontSize = fontSize;
        }

        // Check cache first
        if (_textWidthCache.TryGetValue(text, out var cachedWidth))
            return cachedWidth;

        // Use DirectWrite native measurement via FormattedText
        var formattedText = new FormattedText(text, fontFamily, fontSize);
        var usedNative = TextMeasurement.MeasureText(formattedText);

        double width;
        if (usedNative && formattedText.IsMeasured)
        {
            width = formattedText.Width;
        }
        else
        {
            // Fall back to estimation
            width = text.Length * fontSize * 0.6;
        }

        // Cache the result
        if (_textWidthCache.Count >= MaxCacheSize)
        {
            var keysToRemove = _textWidthCache.Keys.Take(MaxCacheSize / 2).ToList();
            foreach (var key in keysToRemove)
                _textWidthCache.Remove(key);
        }
        _textWidthCache[text] = width;

        return width;
    }

    private int GetCaretIndexFromPosition(Point position)
    {
        var border = BorderThickness;
        var padding = Padding;

        var contentX = position.X - border.Left - padding.Left + _horizontalOffset;
        var displayText = GetDisplayText();

        // Find the column that matches contentX
        int columnIndex = 0;
        double prevWidth = 0;

        for (int i = 0; i <= displayText.Length; i++)
        {
            var width = MeasureDisplayTextWidth(displayText.Substring(0, i));
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

        return columnIndex;
    }

    private void ResetCaretBlink()
    {
        _caretVisible = true;
        _caretOpacity = 1.0;
        _lastCaretBlink = DateTime.Now;

        if (_caretTimer is { IsEnabled: true })
        {
            ScheduleNextCaretTick(_lastCaretBlink);
        }
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

    private void EnsureCaretVisible()
    {
        var border = BorderThickness;
        var padding = Padding;
        var contentWidth = Math.Round(RenderSize.Width - border.Left - border.Right - padding.Left - padding.Right);

        if (contentWidth <= 0)
            return;

        var displayText = GetDisplayText();
        var textBeforeCaret = displayText.Substring(0, Math.Min(_caretIndex, displayText.Length));
        var caretX = Math.Round(MeasureDisplayTextWidth(textBeforeCaret));

        if (caretX < _horizontalOffset)
        {
            _horizontalOffset = caretX;
        }
        else if (caretX > _horizontalOffset + contentWidth - 2)
        {
            _horizontalOffset = caretX - contentWidth + 2;
        }

        _horizontalOffset = Math.Round(Math.Max(0, _horizontalOffset));
    }

    private void RaisePasswordChanged()
    {
        var e = new RoutedEventArgs(PasswordChangedEvent, this);
        RaiseEvent(e);
        InvalidateVisual();
    }

    #endregion

    #region Input Handling

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (e.Handled)
            return;

        OnKeyDown(e);
    }

    private void OnKeyDown(KeyEventArgs e)
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

            case Key.Home:
                HandleHomeKey(shift);
                e.Handled = true;
                break;

            case Key.End:
                HandleEndKey(shift);
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

            case Key.A:
                if (ctrl)
                {
                    SelectAll();
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

            // Note: Copy (Ctrl+C) and Cut (Ctrl+X) are intentionally not implemented
            // for security reasons - passwords should not be copied to clipboard
        }
    }

    private void OnTextInputHandler(object sender, TextCompositionEventArgs e)
    {
        if (e.Handled || IsReadOnly)
            return;

        if (!string.IsNullOrEmpty(e.Text))
        {
            var text = e.Text;
            // Filter control characters and newlines
            if (text.Length == 1 && (char.IsControl(text[0]) || text[0] == '\n' || text[0] == '\r'))
                return;

            InsertText(text);
            e.Handled = true;
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

            if (_clickCount >= 2)
            {
                // Double-click or more: select all (passwords don't have word boundaries)
                SelectAll();
                _clickCount = 0;
            }
            else
            {
                // Single click
                CaptureMouse();
                var newCaretIndex = GetCaretIndexFromPosition(position);

                if ((e.KeyboardModifiers & ModifierKeys.Shift) != 0)
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
                }

                ResetCaretBlink();
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
                ReleaseMouseCapture();
            }

            e.Handled = true;
        }
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (!IsEnabled || !_isSelecting) return;

        var position = e.GetPosition(this);
        var newCaretIndex = GetCaretIndexFromPosition(position);

        _selectionStart = Math.Min(_selectionAnchor, newCaretIndex);
        _selectionLength = Math.Abs(newCaretIndex - _selectionAnchor);
        _caretIndex = newCaretIndex;

        EnsureCaretVisible();
        InvalidateVisual();
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
        _caretTimer?.Stop();
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
        int newIndex;

        if (ctrl)
        {
            newIndex = 0; // For passwords, Ctrl+Left goes to start
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
        int newIndex;

        if (ctrl)
        {
            newIndex = _password.Length; // For passwords, Ctrl+Right goes to end
        }
        else
        {
            newIndex = Math.Min(_password.Length, _caretIndex + 1);
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

    private void HandleHomeKey(bool shift)
    {
        int newIndex = 0;

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

    private void HandleEndKey(bool shift)
    {
        int newIndex = _password.Length;

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
                // For passwords, Ctrl+Backspace deletes to start
                deleteCount = _caretIndex;
            }
            else
            {
                deleteCount = 1;
            }

            var startIndex = Math.Max(0, _caretIndex - deleteCount);
            _password = _password.Substring(0, startIndex) + _password.Substring(_caretIndex);
            _caretIndex = startIndex;

            if (_caretIndex < 0) _caretIndex = 0;

            RaisePasswordChanged();
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
        else if (_caretIndex < _password.Length)
        {
            PushUndo();

            int deleteCount;
            if (ctrl)
            {
                // For passwords, Ctrl+Delete deletes to end
                deleteCount = _password.Length - _caretIndex;
            }
            else
            {
                deleteCount = 1;
            }

            _password = _password.Substring(0, _caretIndex) + _password.Substring(Math.Min(_caretIndex + deleteCount, _password.Length));

            RaisePasswordChanged();
        }

        EnsureCaretVisible();
    }

    #endregion

    #region IME Support

    private void OnGotFocusHandler(object sender, KeyboardFocusChangedEventArgs e)
    {
        InputMethod.SetTarget(this);
    }

    private void OnLostFocusHandler(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (InputMethod.Current == this)
        {
            InputMethod.SetTarget(null);
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

    /// <summary>
    /// Gets whether IME composition is currently active.
    /// </summary>
    public bool IsImeComposing => _isImeComposing;

    /// <inheritdoc />
    public Point GetImeCaretPosition()
    {
        return GetCaretScreenPosition();
    }

    private Point GetCaretScreenPosition()
    {
        var lineHeight = Math.Round(GetLineHeight());
        var displayText = GetDisplayText();
        var textBeforeCaret = displayText.Substring(0, Math.Min(_caretIndex, displayText.Length));

        double x = Padding.Left - _horizontalOffset + MeasureDisplayTextWidth(textBeforeCaret);
        double y = Padding.Top;

        return new Point(x, y + lineHeight);
    }

    /// <inheritdoc />
    public void OnImeCompositionStart()
    {
        _isImeComposing = true;
        _imeCompositionString = string.Empty;
        _imeCompositionCursor = 0;

        if (_selectionLength > 0)
        {
            DeleteSelection();
        }

        InvalidateVisual();
    }

    /// <inheritdoc />
    public void OnImeCompositionUpdate(string compositionString, int cursorPosition)
    {
        _imeCompositionString = compositionString;
        _imeCompositionCursor = cursorPosition;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public void OnImeCompositionEnd(string? resultString)
    {
        _isImeComposing = false;
        _imeCompositionString = string.Empty;
        _imeCompositionCursor = 0;
        InvalidateVisual();
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PasswordBox passwordBox)
        {
            passwordBox._textWidthCache.Clear();
            passwordBox.InvalidateVisual();
        }
    }

    #endregion

    #region Helper Types

    private class UndoEntry
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

/// <summary>
/// Specifies the password reveal mode.
/// </summary>
public enum PasswordRevealMode
{
    /// <summary>
    /// Password is always hidden.
    /// </summary>
    Hidden,

    /// <summary>
    /// Password can be revealed by holding a button.
    /// </summary>
    Peek,

    /// <summary>
    /// Password is always visible.
    /// </summary>
    Visible
}
