using System.Text;
using Jalium.UI.Automation;
using Jalium.UI.Controls.TerminalEmulator;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// A terminal emulator control with VT100/ANSI escape sequence support,
/// shell process integration, and full keyboard/IME input handling.
/// </summary>
public class Terminal : Control, IImeSupport
{
    #region Automation

    /// <inheritdoc />
    protected override AutomationPeer? OnCreateAutomationPeer()
    {
        return null; // Can add TerminalAutomationPeer later
    }

    #endregion

    #region Static ANSI Color Palette

    /// <summary>
    /// Standard 16-color ANSI palette (0-7 normal, 8-15 bright).
    /// </summary>
    private static readonly Color[] s_ansiPalette = new Color[]
    {
        // Normal colors (0-7)
        Color.FromRgb(12, 12, 12),      // 0: Black
        Color.FromRgb(197, 15, 31),     // 1: Red
        Color.FromRgb(19, 161, 14),     // 2: Green
        Color.FromRgb(193, 156, 0),     // 3: Yellow
        Color.FromRgb(0, 55, 218),      // 4: Blue
        Color.FromRgb(136, 23, 152),    // 5: Magenta
        Color.FromRgb(58, 150, 221),    // 6: Cyan
        Color.FromRgb(204, 204, 204),   // 7: White
        // Bright colors (8-15)
        Color.FromRgb(118, 118, 118),   // 8: Bright Black
        Color.FromRgb(231, 72, 86),     // 9: Bright Red
        Color.FromRgb(22, 198, 12),     // 10: Bright Green
        Color.FromRgb(249, 241, 165),   // 11: Bright Yellow
        Color.FromRgb(59, 120, 255),    // 12: Bright Blue
        Color.FromRgb(180, 0, 158),     // 13: Bright Magenta
        Color.FromRgb(97, 214, 214),    // 14: Bright Cyan
        Color.FromRgb(242, 242, 242),   // 15: Bright White
    };

    /// <summary>
    /// Extended 256-color palette (lazily computed for indices 16-255).
    /// </summary>
    private static Color[]? s_extendedPalette;

    #endregion

    #region Static Brushes

    private static readonly SolidColorBrush s_defaultForegroundBrush = new(Color.FromRgb(204, 204, 204));
    private static readonly SolidColorBrush s_defaultBackgroundBrush = new(Color.FromRgb(12, 12, 12));
    private static readonly SolidColorBrush s_defaultSelectionBrush = new(Color.FromArgb(100, 38, 79, 120));
    private static readonly SolidColorBrush s_defaultCaretBrush = new(Color.FromRgb(220, 220, 220));
    private static readonly SolidColorBrush s_compositionBgBrush = new(Color.FromRgb(60, 60, 80));
    private static readonly SolidColorBrush s_compositionUnderlineBrush = new(Color.FromRgb(200, 200, 100));
    private static readonly Pen s_compositionUnderlinePen = new(s_compositionUnderlineBrush, 1);
    private static readonly SolidColorBrush s_scrollBarTrackBrush = new(Color.FromArgb(72, 68, 68, 68));
    private static readonly SolidColorBrush s_scrollBarThumbBrush = new(Color.FromArgb(220, 180, 180, 180));
    private static readonly SolidColorBrush s_scrollBarActiveThumbBrush = new(Color.FromArgb(235, 212, 212, 212));

    // Theme-aware brush accessors
    private Brush TerminalForeground => TryFindResource("TerminalForeground") as Brush ?? Foreground ?? s_defaultForegroundBrush;
    private Brush TerminalBackground => TryFindResource("TerminalBackground") as Brush ?? Background ?? s_defaultBackgroundBrush;

    #endregion

    #region Fields

    /// <summary>
    /// The terminal character buffer.
    /// </summary>
    private TerminalBuffer _buffer;

    /// <summary>
    /// The ANSI escape sequence parser.
    /// </summary>
    private AnsiParser _parser;

    /// <summary>
    /// The shell process.
    /// </summary>
    private TerminalProcess? _process;

    /// <summary>
    /// Cached cell width (monospace character width).
    /// </summary>
    private double _cellWidth;

    /// <summary>
    /// Cached cell height (line height).
    /// </summary>
    private double _cellHeight;

    /// <summary>
    /// The vertical scroll offset (in scrollback lines).
    /// </summary>
    private int _scrollOffset;

    /// <summary>
    /// Selection start position (row in total buffer, col).
    /// </summary>
    private (int row, int col) _selectionStart;

    /// <summary>
    /// Selection end position (row in total buffer, col).
    /// </summary>
    private (int row, int col) _selectionEnd;

    /// <summary>
    /// Whether a selection is active.
    /// </summary>
    private bool _hasSelection;

    /// <summary>
    /// Whether the user is currently dragging to select.
    /// </summary>
    private bool _isDragging;

    /// <summary>
    /// Whether the caret is currently visible (opacity > 0).
    /// </summary>
    private bool _caretVisible = true;

    /// <summary>
    /// Current caret opacity (0.0 to 1.0) for smooth fade animation.
    /// </summary>
    private double _caretOpacity = 1.0;

    /// <summary>
    /// The last time the caret blink cycle started.
    /// </summary>
    private DateTime _lastCaretBlink;

    /// <summary>
    /// Caret blink interval in milliseconds.
    /// </summary>
    private const int CaretBlinkInterval = 530;

    /// <summary>
    /// Duration of the fade in/out animation in milliseconds.
    /// </summary>
    private const int CaretFadeDuration = 150;

    /// <summary>
    /// Tick interval during fade phases (ms).
    /// </summary>
    private const int CaretAnimationTickMs = 33;

    // Scrollbar
    private const double ScrollBarThickness = 10;
    private const double MinScrollBarThumbSize = 20;
    private const double ScrollBarCornerRadius = 5;
    private const double ScrollBarInnerPadding = 2;
    private Rect _scrollBarTrackRect = Rect.Empty;
    private Rect _scrollBarThumbRect = Rect.Empty;
    private bool _isScrollBarDragging;
    private double _scrollBarDragStartY;
    private int _scrollBarDragStartOffset;

    // IME support
    private bool _isImeComposing;
    private string _imeCompositionString = string.Empty;
    private int _imeCompositionCursor;

    /// <summary>
    /// Brush cache for ANSI colors (avoids creating new brushes every frame).
    /// </summary>
    private readonly Dictionary<Color, SolidColorBrush> _brushCache = new();

    /// <summary>
    /// Cached Pen for caret rendering.
    /// </summary>
    private Pen? _caretPen;

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the Shell dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ShellProperty =
        DependencyProperty.Register(nameof(Shell), typeof(string), typeof(Terminal),
            new PropertyMetadata(GetDefaultShell()));

    /// <summary>
    /// Identifies the ShellArguments dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty ShellArgumentsProperty =
        DependencyProperty.Register(nameof(ShellArguments), typeof(string), typeof(Terminal),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the WorkingDirectory dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty WorkingDirectoryProperty =
        DependencyProperty.Register(nameof(WorkingDirectory), typeof(string), typeof(Terminal),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the TerminalColumns dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty TerminalColumnsProperty =
        DependencyProperty.Register(nameof(TerminalColumns), typeof(int), typeof(Terminal),
            new PropertyMetadata(80, OnTerminalSizeChanged));

    /// <summary>
    /// Identifies the TerminalRows dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty TerminalRowsProperty =
        DependencyProperty.Register(nameof(TerminalRows), typeof(int), typeof(Terminal),
            new PropertyMetadata(24, OnTerminalSizeChanged));

    /// <summary>
    /// Identifies the AutoSize dependency property.
    /// When true, the terminal automatically adjusts columns/rows to fit the control size.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty AutoSizeProperty =
        DependencyProperty.Register(nameof(AutoSize), typeof(bool), typeof(Terminal),
            new PropertyMetadata(true, OnAutoSizeChanged));

    /// <summary>
    /// Identifies the MaxScrollbackLines dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty MaxScrollbackLinesProperty =
        DependencyProperty.Register(nameof(MaxScrollbackLines), typeof(int), typeof(Terminal),
            new PropertyMetadata(10000, OnMaxScrollbackChanged));

    /// <summary>
    /// Identifies the SelectionBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty SelectionBrushProperty =
        DependencyProperty.Register(nameof(SelectionBrush), typeof(Brush), typeof(Terminal),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the CaretBrush dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CaretBrushProperty =
        DependencyProperty.Register(nameof(CaretBrush), typeof(Brush), typeof(Terminal),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the IsReadOnly dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(Terminal),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the Title dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(Terminal),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the AutoStartShell dependency property.
    /// When true, the shell is started automatically when the control is loaded.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty AutoStartShellProperty =
        DependencyProperty.Register(nameof(AutoStartShell), typeof(bool), typeof(Terminal),
            new PropertyMetadata(true));

    /// <summary>
    /// Identifies the CursorStyle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CursorStyleProperty =
        DependencyProperty.Register(nameof(CursorStyle), typeof(TerminalCursorStyle), typeof(Terminal),
            new PropertyMetadata(TerminalCursorStyle.Bar, OnVisualPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the shell executable path (e.g., "cmd.exe", "powershell.exe", "bash").
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public string Shell
    {
        get => (string)(GetValue(ShellProperty) ?? GetDefaultShell());
        set => SetValue(ShellProperty, value);
    }

    /// <summary>
    /// Gets or sets the shell command-line arguments.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public string ShellArguments
    {
        get => (string)(GetValue(ShellArgumentsProperty) ?? string.Empty);
        set => SetValue(ShellArgumentsProperty, value);
    }

    /// <summary>
    /// Gets or sets the initial working directory.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public string WorkingDirectory
    {
        get => (string)(GetValue(WorkingDirectoryProperty) ?? string.Empty);
        set => SetValue(WorkingDirectoryProperty, value);
    }

    /// <summary>
    /// Gets or sets the number of terminal columns.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public int TerminalColumns
    {
        get => (int)GetValue(TerminalColumnsProperty)!;
        set => SetValue(TerminalColumnsProperty, value);
    }

    /// <summary>
    /// Gets or sets the number of terminal rows.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public int TerminalRows
    {
        get => (int)GetValue(TerminalRowsProperty)!;
        set => SetValue(TerminalRowsProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the terminal auto-sizes to fit the control bounds.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public bool AutoSize
    {
        get => (bool)GetValue(AutoSizeProperty)!;
        set => SetValue(AutoSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum number of scrollback lines.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public int MaxScrollbackLines
    {
        get => (int)GetValue(MaxScrollbackLinesProperty)!;
        set => SetValue(MaxScrollbackLinesProperty, value);
    }

    /// <summary>
    /// Gets or sets the selection highlight brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? SelectionBrush
    {
        get => GetValue(SelectionBrushProperty) as Brush;
        set => SetValue(SelectionBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the caret brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? CaretBrush
    {
        get => GetValue(CaretBrushProperty) as Brush;
        set => SetValue(CaretBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the terminal is read-only (input disabled).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty)!;
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>
    /// Gets the terminal title (set by the shell via OSC escape sequences).
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Title
    {
        get => (string)(GetValue(TitleProperty) ?? string.Empty);
        private set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to automatically start the shell on load.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public bool AutoStartShell
    {
        get => (bool)GetValue(AutoStartShellProperty)!;
        set => SetValue(AutoStartShellProperty, value);
    }

    /// <summary>
    /// Gets or sets the cursor style.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public TerminalCursorStyle CursorStyle
    {
        get => (TerminalCursorStyle)GetValue(CursorStyleProperty)!;
        set => SetValue(CursorStyleProperty, value);
    }

    /// <summary>
    /// Gets whether the shell process is currently running.
    /// </summary>
    public bool IsProcessRunning => _process?.IsRunning == true;

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the OutputReceived routed event.
    /// </summary>
    public static readonly RoutedEvent OutputReceivedEvent =
        EventManager.RegisterRoutedEvent(nameof(OutputReceived), RoutingStrategy.Bubble,
            typeof(TerminalOutputEventHandler), typeof(Terminal));

    /// <summary>
    /// Occurs when output is received from the shell process.
    /// </summary>
    public event TerminalOutputEventHandler OutputReceived
    {
        add => AddHandler(OutputReceivedEvent, value);
        remove => RemoveHandler(OutputReceivedEvent, value);
    }

    /// <summary>
    /// Identifies the ProcessExited routed event.
    /// </summary>
    public static readonly RoutedEvent ProcessExitedEvent =
        EventManager.RegisterRoutedEvent(nameof(ProcessExited), RoutingStrategy.Bubble,
            typeof(TerminalProcessExitedEventHandler), typeof(Terminal));

    /// <summary>
    /// Occurs when the shell process exits.
    /// </summary>
    public event TerminalProcessExitedEventHandler ProcessExited
    {
        add => AddHandler(ProcessExitedEvent, value);
        remove => RemoveHandler(ProcessExitedEvent, value);
    }

    /// <summary>
    /// Identifies the TitleChanged routed event.
    /// </summary>
    public static readonly RoutedEvent TitleChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(TitleChanged), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(Terminal));

    /// <summary>
    /// Occurs when the terminal title changes.
    /// </summary>
    public event RoutedEventHandler TitleChanged
    {
        add => AddHandler(TitleChangedEvent, value);
        remove => RemoveHandler(TitleChangedEvent, value);
    }

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the Terminal control.
    /// </summary>
    public Terminal()
    {
        _buffer = new TerminalBuffer(TerminalColumns, TerminalRows, MaxScrollbackLines);
        _parser = new AnsiParser(_buffer);

        _parser.TitleChanged += OnParserTitleChanged;
        _parser.BellRung += OnParserBellRung;

        Focusable = true;
        Cursor = Cursors.IBeam;
        _lastCaretBlink = DateTime.Now;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    #endregion

    #region Lifecycle

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Register input handlers
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDownHandler));
        AddHandler(TextInputEvent, new TextCompositionEventHandler(OnTextInputHandler));
        AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnMouseDownHandler));
        AddHandler(MouseMoveEvent, new MouseEventHandler(OnMouseMoveHandler));
        AddHandler(MouseUpEvent, new MouseButtonEventHandler(OnMouseUpHandler));
        AddHandler(MouseWheelEvent, new MouseWheelEventHandler(OnMouseWheelHandler));
        GotFocus += OnGotFocusHandler;
        LostFocus += OnLostFocusHandler;

        // Start caret blink timer
        _lastCaretBlink = DateTime.Now;
        StartCaretAnimation();

        // Auto-start shell
        if (AutoStartShell && _process == null)
        {
            StartShell();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopCaretAnimation();
        _process?.Dispose();
        _process = null;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Starts the shell process.
    /// </summary>
    public void StartShell()
    {
        _process?.Dispose();
        _process = new TerminalProcess();
        _process.OutputReceived += OnProcessOutput;
        _process.ProcessExited += OnProcessExited;

        string workDir = string.IsNullOrEmpty(WorkingDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : WorkingDirectory;

        _process.Start(Shell, ShellArguments, workDir, columns: _buffer.Columns, rows: _buffer.Rows);
    }

    /// <summary>
    /// Stops the shell process.
    /// </summary>
    public void StopShell()
    {
        _process?.Stop();
    }

    /// <summary>
    /// Writes data directly to the terminal (bypassing the shell process).
    /// Useful for displaying text programmatically.
    /// </summary>
    public void Write(string text)
    {
        _parser.Feed(text);
        _scrollOffset = 0;
        InvalidateVisual();
    }

    /// <summary>
    /// Writes a line of text directly to the terminal.
    /// </summary>
    public void WriteLine(string text)
    {
        Write(text + "\r\n");
    }

    /// <summary>
    /// Sends input to the shell process stdin.
    /// </summary>
    public void SendInput(string text)
    {
        _process?.WriteInput(text);
    }

    /// <summary>
    /// Clears the terminal screen and scrollback buffer.
    /// </summary>
    public void Clear()
    {
        _buffer.ResetAttributes();
        _buffer.ClearScreen();
        _buffer.SetCursorPosition(0, 0);
        _scrollOffset = 0;
        ClearSelection();
        InvalidateVisual();
    }

    /// <summary>
    /// Gets the selected text, or empty if no selection.
    /// </summary>
    public string GetSelectedText()
    {
        if (!_hasSelection) return string.Empty;

        var (startRow, startCol) = NormalizeSelectionStart();
        var (endRow, endCol) = NormalizeSelectionEnd();

        var sb = new StringBuilder();
        int scrollbackCount = _buffer.ScrollbackCount;

        for (int row = startRow; row <= endRow; row++)
        {
            int colStart = (row == startRow) ? startCol : 0;
            int colEnd = (row == endRow) ? endCol : _buffer.Columns;

            if (row < scrollbackCount)
            {
                // Scrollback line
                var line = _buffer.GetScrollbackLine(row);
                for (int c = colStart; c < colEnd && c < line.Length; c++)
                    sb.Append(line[c].Character);
            }
            else
            {
                // Screen line
                int screenRow = row - scrollbackCount;
                for (int c = colStart; c < colEnd; c++)
                    sb.Append(_buffer.GetCell(screenRow, c).Character);
            }

            if (row < endRow)
                sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Copies the selected text to the clipboard.
    /// </summary>
    public void Copy()
    {
        var text = GetSelectedText();
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }

    /// <summary>
    /// Pastes text from the clipboard to the shell process.
    /// </summary>
    public void Paste()
    {
        if (IsReadOnly) return;

        var text = Clipboard.GetText();
        if (string.IsNullOrEmpty(text)) return;

        if (_parser.BracketedPasteMode)
            _process?.WriteInput("\x1b[200~" + text + "\x1b[201~");
        else
            _process?.WriteInput(text);
    }

    /// <summary>
    /// Selects all text in the terminal buffer.
    /// </summary>
    public void SelectAll()
    {
        _selectionStart = (0, 0);
        _selectionEnd = (_buffer.ScrollbackCount + _buffer.Rows - 1, _buffer.Columns);
        _hasSelection = true;
        InvalidateVisual();
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnTerminalSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Terminal t && !t.AutoSize)
        {
            t._buffer.Resize(t.TerminalColumns, t.TerminalRows);
            t._process?.NotifyResize(t.TerminalColumns, t.TerminalRows);
            t.InvalidateVisual();
        }
    }

    private static void OnAutoSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Terminal t && t.AutoSize)
        {
            t.UpdateAutoSize(t.RenderSize);
        }
    }

    private static void OnMaxScrollbackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Terminal t)
        {
            t._buffer.MaxScrollback = t.MaxScrollbackLines;
        }
    }

    private static new void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Terminal t)
        {
            t._caretPen = null;
            t.InvalidateVisual();
        }
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureCellMetrics();

        if (AutoSize)
        {
            // Fill available space
            return availableSize;
        }

        // Fixed size based on columns/rows
        var padding = Padding;
        var border = BorderThickness;
        double width = _cellWidth * TerminalColumns + padding.Left + padding.Right + border.Left + border.Right;
        double height = _cellHeight * TerminalRows + padding.Top + padding.Bottom + border.Top + border.Bottom;
        return new Size(
            Math.Min(width, availableSize.Width),
            Math.Min(height, availableSize.Height));
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (AutoSize)
        {
            UpdateAutoSize(finalSize);
        }
        return finalSize;
    }

    private int _lastAutoSizeCols;
    private int _lastAutoSizeRows;
    private double _lastAutoSizeCellWidth;
    private double _lastAutoSizeCellHeight;
    private double _lastAutoSizeWidth;
    private double _lastAutoSizeHeight;

    private void UpdateAutoSize(Size finalSize)
    {
        EnsureCellMetrics();

        var padding = Padding;
        var border = BorderThickness;
        double availableWidth = finalSize.Width - padding.Left - padding.Right - border.Left - border.Right;
        double availableHeight = finalSize.Height - padding.Top - padding.Bottom - border.Top - border.Bottom;

        if (availableWidth <= 0 || availableHeight <= 0 || _cellWidth <= 0 || _cellHeight <= 0)
            return;

        int newCols = Math.Max(1, (int)(availableWidth / _cellWidth));
        int newRows = Math.Max(1, (int)(availableHeight / _cellHeight));

        // Skip if nothing changed (dimensions, cell size, and available space all the same)
        if (newCols == _lastAutoSizeCols && newRows == _lastAutoSizeRows
            && _cellWidth == _lastAutoSizeCellWidth && _cellHeight == _lastAutoSizeCellHeight
            && availableWidth == _lastAutoSizeWidth && availableHeight == _lastAutoSizeHeight)
            return;

        _lastAutoSizeCols = newCols;
        _lastAutoSizeRows = newRows;
        _lastAutoSizeCellWidth = _cellWidth;
        _lastAutoSizeCellHeight = _cellHeight;
        _lastAutoSizeWidth = availableWidth;
        _lastAutoSizeHeight = availableHeight;

        _buffer.Resize(newCols, newRows);
        _process?.NotifyResize(newCols, newRows);
        TerminalColumns = newCols;
        TerminalRows = newRows;
    }

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        base.OnRender(drawingContext);

        if (drawingContext is not DrawingContext dc)
            return;

        EnsureCellMetrics();

        // Re-check auto-size on every render in case font metrics changed
        if (AutoSize)
            UpdateAutoSize(RenderSize);

        var bounds = new Rect(0, 0, RenderSize.Width, RenderSize.Height);
        var border = BorderThickness;
        var padding = Padding;
        var cornerRadius = CornerRadius;

        // Draw background and border
        var strokeThickness = border.Left;
        var borderRect = ControlRenderGeometry.GetStrokeAlignedRect(bounds, strokeThickness);
        var borderRadius = ControlRenderGeometry.GetStrokeAlignedCornerRadius(cornerRadius, strokeThickness);

        var bg = TerminalBackground;
        dc.DrawRoundedRectangle(bg, null, borderRect, borderRadius);

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

        dc.PushClip(new RectangleGeometry(contentRect));

        // Render terminal content
        RenderCells(dc, contentRect);

        // Render selection overlay
        if (_hasSelection)
            RenderSelection(dc, contentRect);

        // Render IME composition
        if (_isImeComposing && !string.IsNullOrEmpty(_imeCompositionString))
            RenderImeComposition(dc, contentRect);

        // Render cursor
        if (IsFocused && _parser.CursorVisible && _scrollOffset == 0)
            RenderCursor(dc, contentRect);

        dc.Pop(); // Pop clip

        // Draw scrollbar (outside clip region so it's always visible)
        RenderScrollBar(dc, contentRect);

        // Draw focus indicator
        if (IsKeyboardFocused)
        {
            ControlFocusVisual.Draw(dc, this, bounds, cornerRadius);
        }
    }

    private void RenderCells(DrawingContext dc, Rect contentRect)
    {
        int cols = _buffer.Columns;
        int rows = _buffer.Rows;
        int scrollbackCount = _buffer.ScrollbackCount;
        double x0 = contentRect.X;
        double y0 = contentRect.Y;

        var defaultFg = TerminalForeground;
        string fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        double fontSize = FontSize;

        for (int row = 0; row < rows; row++)
        {
            int bufferRow = row;
            TerminalChar[]? scrollbackLine = null;

            if (_scrollOffset > 0)
            {
                int scrollbackRow = scrollbackCount - _scrollOffset + row;
                if (scrollbackRow < 0) continue;
                if (scrollbackRow < scrollbackCount)
                {
                    scrollbackLine = _buffer.GetScrollbackLine(scrollbackRow);
                    bufferRow = -1;
                }
                else
                {
                    bufferRow = scrollbackRow - scrollbackCount;
                }
            }

            double y = y0 + row * _cellHeight;

            // Build the full row text for GetColumnX measurements
            string rowText;
            if (scrollbackLine != null)
            {
                int sbIndex = scrollbackCount - _scrollOffset + row;
                rowText = _buffer.GetScrollbackLineText(sbIndex);
            }
            else
            {
                rowText = _buffer.GetRowTextRaw(bufferRow);
            }

            // Render runs: background + text, positioned via GetColumnX for consistency
            // with selection, cursor, and mouse hit testing.
            int runStart = 0;
            while (runStart < cols)
            {
                TerminalChar startCell = GetCellAt(scrollbackLine, bufferRow, runStart);
                int runEnd = runStart + 1;
                while (runEnd < cols && CellsHaveSameStyle(startCell, GetCellAt(scrollbackLine, bufferRow, runEnd)))
                    runEnd++;

                var (fgColor, bgColor) = ResolveCellColors(startCell);
                double rx = x0 + GetColumnX(rowText, runStart, fontFamily, fontSize);
                double rxEnd = x0 + GetColumnX(rowText, runEnd, fontFamily, fontSize);
                double rw = rxEnd - rx;

                // Background
                if (bgColor != null)
                    dc.DrawRectangle(GetCachedBrush(bgColor.Value), null, new Rect(rx, y, rw, _cellHeight));

                // Text — build run string and draw as one piece
                var sb = new StringBuilder(runEnd - runStart);
                for (int c = runStart; c < runEnd; c++)
                    sb.Append(GetCellAt(scrollbackLine, bufferRow, c).Character);

                string runText = sb.ToString();
                if (!string.IsNullOrWhiteSpace(runText))
                {
                    var fgBrush = fgColor != null ? GetCachedBrush(fgColor.Value) : defaultFg;
                    var ft = new FormattedText(runText, fontFamily, fontSize)
                    {
                        Foreground = fgBrush,
                    };
                    if (startCell.Attributes.HasFlag(CharAttributes.Bold))
                        ft.FontWeight = 700;
                    if (startCell.Attributes.HasFlag(CharAttributes.Italic))
                        ft.FontStyle = 1;

                    dc.DrawText(ft, new Point(rx, y));
                }

                // Underline / strikethrough
                if (startCell.Attributes.HasFlag(CharAttributes.Underline) || startCell.Attributes.HasFlag(CharAttributes.Strikethrough))
                {
                    var lineBrush = fgColor != null ? GetCachedBrush(fgColor.Value) : defaultFg;
                    var pen = new Pen(lineBrush, 1);
                    if (startCell.Attributes.HasFlag(CharAttributes.Underline))
                        dc.DrawLine(pen, new Point(rx, y + _cellHeight - 1), new Point(rxEnd, y + _cellHeight - 1));
                    if (startCell.Attributes.HasFlag(CharAttributes.Strikethrough))
                        dc.DrawLine(pen, new Point(rx, y + _cellHeight / 2), new Point(rxEnd, y + _cellHeight / 2));
                }

                runStart = runEnd;
            }
        }
    }

    private void RenderSelection(DrawingContext dc, Rect contentRect)
    {
        var selBrush = SelectionBrush ?? s_defaultSelectionBrush;

        var (startRow, startCol) = NormalizeSelectionStart();
        var (endRow, endCol) = NormalizeSelectionEnd();

        int scrollbackCount = _buffer.ScrollbackCount;
        double x0 = contentRect.X;
        double y0 = contentRect.Y;
        string fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        double fontSize = FontSize > 0 ? FontSize : 14;

        for (int screenRow = 0; screenRow < _buffer.Rows; screenRow++)
        {
            int totalRow = scrollbackCount - _scrollOffset + screenRow;
            if (totalRow < startRow || totalRow > endRow) continue;

            int colStart = (totalRow == startRow) ? startCol : 0;
            int colEnd = (totalRow == endRow) ? endCol : _buffer.Columns;

            // Get row text for precise measurement
            string rowText;
            if (totalRow < scrollbackCount)
                rowText = _buffer.GetScrollbackLineText(totalRow);
            else
            {
                int sr = totalRow - scrollbackCount;
                rowText = sr >= 0 && sr < _buffer.Rows ? _buffer.GetRowTextRaw(sr) : string.Empty;
            }

            double sx = x0 + GetColumnX(rowText, colStart, fontFamily, fontSize);
            double sy = y0 + screenRow * _cellHeight;
            double ex = (colEnd >= _buffer.Columns)
                ? contentRect.Right
                : x0 + GetColumnX(rowText, colEnd, fontFamily, fontSize);

            dc.DrawRectangle(selBrush, null, new Rect(sx, sy, Math.Max(0, ex - sx), _cellHeight));
        }
    }

    private void RenderCursor(DrawingContext dc, Rect contentRect)
    {
        double opacity = UpdateCaretAnimation();
        if (opacity <= 0.01) return;

        var baseBrush = CaretBrush
            ?? TryFindResource("TextPrimary") as Brush
            ?? s_defaultCaretBrush;

        // Apply opacity to caret brush
        Brush caretBrush;
        if (opacity >= 0.99)
        {
            caretBrush = baseBrush;
        }
        else
        {
            var color = (baseBrush as SolidColorBrush)?.Color ?? Color.White;
            caretBrush = new SolidColorBrush(Color.FromArgb((byte)(opacity * color.A), color.R, color.G, color.B));
        }
        _caretPen = null; // Force pen recreation with new brush

        string fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        double fontSize = FontSize > 0 ? FontSize : 14;
        string cursorRowText = _buffer.GetRowTextRaw(_buffer.CursorRow);
        double x = contentRect.X + GetColumnX(cursorRowText, _buffer.CursorCol, fontFamily, fontSize);
        double y = contentRect.Y + _buffer.CursorRow * _cellHeight;

        switch (CursorStyle)
        {
            case TerminalCursorStyle.Block:
                var baseColor = (caretBrush as SolidColorBrush)?.Color ?? Color.White;
                var blockBrush = new SolidColorBrush(Color.FromArgb(128, baseColor.R, baseColor.G, baseColor.B));
                dc.DrawRectangle(blockBrush, null, new Rect(x, y, _cellWidth, _cellHeight));
                break;

            case TerminalCursorStyle.Underline:
                if (_caretPen == null || _caretPen.Brush != caretBrush)
                    _caretPen = new Pen(caretBrush, 2);
                dc.DrawLine(_caretPen, new Point(x, y + _cellHeight - 1),
                    new Point(x + _cellWidth, y + _cellHeight - 1));
                break;

            case TerminalCursorStyle.Bar:
            default:
                if (_caretPen == null || _caretPen.Brush != caretBrush)
                    _caretPen = new Pen(caretBrush, 1);
                dc.DrawLine(_caretPen, new Point(x, y), new Point(x, y + _cellHeight));
                break;
        }
    }

    private void RenderImeComposition(DrawingContext dc, Rect contentRect)
    {
        string fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        double fontSize = FontSize > 0 ? FontSize : 14;
        string rowText = _buffer.GetRowTextRaw(_buffer.CursorRow);
        double x = contentRect.X + GetColumnX(rowText, _buffer.CursorCol, fontFamily, fontSize);
        double y = contentRect.Y + _buffer.CursorRow * _cellHeight;
        var ft = new FormattedText(_imeCompositionString, fontFamily, FontSize)
        {
            Foreground = s_defaultForegroundBrush,
        };

        double compWidth = ft.Width;
        dc.DrawRectangle(s_compositionBgBrush, null, new Rect(x, y, compWidth, _cellHeight));
        dc.DrawText(ft, new Point(x, y));
        dc.DrawLine(s_compositionUnderlinePen, new Point(x, y + _cellHeight - 1),
            new Point(x + compWidth, y + _cellHeight - 1));
    }

    private void RenderScrollBar(DrawingContext dc, Rect contentRect)
    {
        int maxScroll = _buffer.ScrollbackCount;
        if (maxScroll <= 0)
        {
            _scrollBarTrackRect = Rect.Empty;
            _scrollBarThumbRect = Rect.Empty;
            return;
        }

        // Track occupies the right edge of the content area
        double trackX = contentRect.Right - ScrollBarThickness;
        double trackY = contentRect.Y;
        double trackHeight = contentRect.Height;
        _scrollBarTrackRect = new Rect(trackX, trackY, ScrollBarThickness, trackHeight);

        // Draw track
        double trackRadius = Math.Min(ScrollBarCornerRadius, ScrollBarThickness * 0.5);
        var trackInset = new Rect(
            _scrollBarTrackRect.X + ScrollBarInnerPadding,
            _scrollBarTrackRect.Y + ScrollBarInnerPadding,
            Math.Max(0, _scrollBarTrackRect.Width - ScrollBarInnerPadding * 2),
            Math.Max(0, _scrollBarTrackRect.Height - ScrollBarInnerPadding * 2));
        dc.DrawRoundedRectangle(s_scrollBarTrackBrush, null, trackInset, trackRadius, trackRadius);

        // Compute thumb size and position
        int totalLines = maxScroll + _buffer.Rows;
        double viewportRatio = (double)_buffer.Rows / totalLines;
        double thumbHeight = Math.Max(MinScrollBarThumbSize, trackInset.Height * viewportRatio);

        double scrollRatio = maxScroll > 0 ? (double)(maxScroll - _scrollOffset) / maxScroll : 0;
        double thumbY = trackInset.Y + scrollRatio * (trackInset.Height - thumbHeight);

        _scrollBarThumbRect = new Rect(trackInset.X, thumbY, trackInset.Width, thumbHeight);

        // Draw thumb
        var thumbBrush = _isScrollBarDragging ? s_scrollBarActiveThumbBrush : s_scrollBarThumbBrush;
        dc.DrawRoundedRectangle(thumbBrush, null, _scrollBarThumbRect, trackRadius, trackRadius);
    }

    #endregion

    #region Cell Metrics

    private void EnsureCellMetrics()
    {
        string fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        double fontSize = FontSize > 0 ? FontSize : 14;

        // Use DirectWrite font metrics for line height, snapped to pixel grid
        var fontMetrics = TextMeasurement.GetFontMetrics(fontFamily, fontSize);
        _cellHeight = fontMetrics.LineHeight > 0
            ? Math.Ceiling(fontMetrics.LineHeight)
            : Math.Ceiling(fontSize * 1.35);

        // Measure actual rendered advance width by measuring a prefix string.
        // This gives the true per-character advance that DrawText uses for layout.
        var ft = new FormattedText("MMMMMMMMMM", fontFamily, fontSize);
        TextMeasurement.MeasureText(ft);
        _cellWidth = ft.IsMeasured && ft.Width > 0
            ? ft.Width / 10.0
            : fontSize * 0.6;
    }

    #endregion

    #region Color Resolution

    private (Color? fg, Color? bg) ResolveCellColors(TerminalChar cell)
    {
        Color? fg = null;
        Color? bg = null;

        // Resolve foreground
        if (cell.ForegroundRgb != null)
            fg = cell.ForegroundRgb;
        else if (cell.ForegroundIndex >= 0)
            fg = GetPaletteColor(cell.ForegroundIndex);

        // Resolve background
        if (cell.BackgroundRgb != null)
            bg = cell.BackgroundRgb;
        else if (cell.BackgroundIndex >= 0)
            bg = GetPaletteColor(cell.BackgroundIndex);

        // Apply bold to foreground (bright variant)
        if (fg != null && cell.Attributes.HasFlag(CharAttributes.Bold) && cell.ForegroundIndex is >= 0 and < 8)
            fg = GetPaletteColor(cell.ForegroundIndex + 8);

        // Apply dim
        if (fg != null && cell.Attributes.HasFlag(CharAttributes.Dim))
            fg = Color.FromArgb((byte)(fg.Value.A / 2), fg.Value.R, fg.Value.G, fg.Value.B);

        // Apply inverse
        if (cell.Attributes.HasFlag(CharAttributes.Inverse))
            (fg, bg) = (bg, fg);

        // Apply hidden
        if (cell.Attributes.HasFlag(CharAttributes.Hidden))
            fg = bg;

        return (fg, bg);
    }

    private static Color GetPaletteColor(int index)
    {
        if (index < 16)
            return s_ansiPalette[Math.Clamp(index, 0, 15)];

        // Extended 256-color palette
        if (s_extendedPalette == null)
            s_extendedPalette = Build256Palette();

        return s_extendedPalette[Math.Clamp(index, 0, 255)];
    }

    private static Color[] Build256Palette()
    {
        var palette = new Color[256];
        // Copy standard 16 colors
        for (int i = 0; i < 16; i++)
            palette[i] = s_ansiPalette[i];

        // 216-color cube (indices 16-231): 6x6x6 RGB
        for (int i = 0; i < 216; i++)
        {
            int r = i / 36;
            int g = (i / 6) % 6;
            int b = i % 6;
            palette[16 + i] = Color.FromRgb(
                (byte)(r == 0 ? 0 : 55 + r * 40),
                (byte)(g == 0 ? 0 : 55 + g * 40),
                (byte)(b == 0 ? 0 : 55 + b * 40));
        }

        // Grayscale ramp (indices 232-255)
        for (int i = 0; i < 24; i++)
        {
            byte v = (byte)(8 + i * 10);
            palette[232 + i] = Color.FromRgb(v, v, v);
        }

        return palette;
    }

    private static bool CellsHaveSameStyle(TerminalChar a, TerminalChar b)
    {
        return a.ForegroundIndex == b.ForegroundIndex
            && a.BackgroundIndex == b.BackgroundIndex
            && a.ForegroundRgb == b.ForegroundRgb
            && a.BackgroundRgb == b.BackgroundRgb
            && a.Attributes == b.Attributes;
    }

    /// <summary>
    /// Gets the X position of a column within a row, using the same measurement
    /// that DirectWrite uses for rendering (HitTestTextPosition or prefix substring).
    /// Falls back to col * _cellWidth for positions beyond text.
    /// </summary>
    private double GetColumnX(string rowText, int column, string fontFamily, double fontSize)
    {
        if (column <= 0) return 0;

        int textLen = rowText.Length;
        if (textLen == 0)
            return column * _cellWidth;

        if (column <= textLen)
        {
            // Use HitTestTextPosition for accuracy (same as EditControl.MeasurePrefixWidth)
            if (column < textLen)
            {
                if (TextMeasurement.HitTestTextPosition(rowText, fontFamily, fontSize,
                        (uint)column, false, out var hitResult) && hitResult.CaretX > 0)
                    return hitResult.CaretX;
            }
            else
            {
                // End of text: trailing edge of last char
                if (TextMeasurement.HitTestTextPosition(rowText, fontFamily, fontSize,
                        (uint)(column - 1), true, out var hitResult) && hitResult.CaretX > 0)
                    return hitResult.CaretX;
            }

            // Fallback: measure prefix substring
            var ft = new FormattedText(rowText.Substring(0, column), fontFamily, fontSize);
            TextMeasurement.MeasureText(ft);
            return ft.Width;
        }

        // Beyond text: measure full text, then extend with _cellWidth grid
        double fullWidth;
        if (TextMeasurement.HitTestTextPosition(rowText, fontFamily, fontSize,
                (uint)(textLen - 1), true, out var endResult) && endResult.CaretX > 0)
            fullWidth = endResult.CaretX;
        else
        {
            var ft = new FormattedText(rowText, fontFamily, fontSize);
            TextMeasurement.MeasureText(ft);
            fullWidth = ft.Width;
        }
        return fullWidth + (column - textLen) * _cellWidth;
    }

    private TerminalChar GetCellAt(TerminalChar[]? scrollbackLine, int bufferRow, int col)
    {
        if (scrollbackLine != null)
            return col < scrollbackLine.Length ? scrollbackLine[col] : TerminalChar.Blank;
        return _buffer.GetCell(bufferRow, col);
    }

    private SolidColorBrush GetCachedBrush(Color color)
    {
        if (!_brushCache.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush(color);
            _brushCache[color] = brush;

            // Prevent unbounded cache growth
            if (_brushCache.Count > 512)
                _brushCache.Clear();
        }
        return brush;
    }

    #endregion

    #region Input Handling

    private void OnKeyDownHandler(object sender, KeyEventArgs e)
    {
        if (IsReadOnly)
        {
            // Allow scrolling even in read-only mode
            HandleReadOnlyKeys(e);
            return;
        }

        // Handle Ctrl+key shortcuts
        if (e.IsControlDown)
        {
            switch (e.Key)
            {
                case Key.C:
                    if (_hasSelection)
                    {
                        Copy();
                        e.Handled = true;
                        return;
                    }
                    // Ctrl+C without selection = send break
                    _process?.SendBreak();
                    e.Handled = true;
                    return;

                case Key.V:
                    Paste();
                    e.Handled = true;
                    return;

                case Key.A:
                    SelectAll();
                    e.Handled = true;
                    return;

                case Key.L:
                    // Ctrl+L = clear screen (like bash)
                    _process?.WriteInput("\x0c");
                    e.Handled = true;
                    return;

                case Key.D:
                    // Ctrl+D = send EOF
                    _process?.WriteInput("\x04");
                    e.Handled = true;
                    return;

                case Key.Z:
                    // Ctrl+Z = suspend (SIGTSTP)
                    _process?.WriteInput("\x1a");
                    e.Handled = true;
                    return;
            }
        }

        // Handle special keys
        string? sequence = GetKeySequence(e);
        if (sequence != null)
        {
            ClearSelection();
            _scrollOffset = 0;
            _process?.WriteInput(sequence);
            e.Handled = true;
        }
    }

    private void HandleReadOnlyKeys(KeyEventArgs e)
    {
        if (e.IsControlDown && e.Key == Key.C && _hasSelection)
        {
            Copy();
            e.Handled = true;
        }
        else if (e.IsControlDown && e.Key == Key.A)
        {
            SelectAll();
            e.Handled = true;
        }
    }

    private string? GetKeySequence(KeyEventArgs e)
    {
        bool appCursorKeys = _parser.ApplicationCursorKeys;
        string csiPrefix = appCursorKeys ? "\x1bO" : "\x1b[";

        return e.Key switch
        {
            Key.Enter => "\r",
            Key.Back => "\x7f",
            Key.Tab => e.IsShiftDown ? "\x1b[Z" : "\t",
            Key.Escape => "\x1b",
            Key.Up => csiPrefix + "A",
            Key.Down => csiPrefix + "B",
            Key.Right => csiPrefix + "C",
            Key.Left => csiPrefix + "D",
            Key.Home => e.IsControlDown ? "\x1b[1;5H" : "\x1b[H",
            Key.End => e.IsControlDown ? "\x1b[1;5F" : "\x1b[F",
            Key.Insert => "\x1b[2~",
            Key.Delete => "\x1b[3~",
            Key.PageUp => "\x1b[5~",
            Key.PageDown => "\x1b[6~",
            Key.F1 => "\x1bOP",
            Key.F2 => "\x1bOQ",
            Key.F3 => "\x1bOR",
            Key.F4 => "\x1bOS",
            Key.F5 => "\x1b[15~",
            Key.F6 => "\x1b[17~",
            Key.F7 => "\x1b[18~",
            Key.F8 => "\x1b[19~",
            Key.F9 => "\x1b[20~",
            Key.F10 => "\x1b[21~",
            Key.F11 => "\x1b[23~",
            Key.F12 => "\x1b[24~",
            _ => null
        };
    }

    private void OnTextInputHandler(object sender, TextCompositionEventArgs e)
    {
        if (IsReadOnly || string.IsNullOrEmpty(e.Text)) return;

        ClearSelection();
        _scrollOffset = 0;
        ResetCaretBlink();
        _process?.WriteInput(e.Text);
        e.Handled = true;
    }

    #endregion

    #region Mouse Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        Focus();

        var pos = e.GetPosition(this);

        // Check if clicking on scrollbar thumb
        if (_scrollBarThumbRect != Rect.Empty && _scrollBarThumbRect.Contains(pos))
        {
            _isScrollBarDragging = true;
            _scrollBarDragStartY = pos.Y;
            _scrollBarDragStartOffset = _scrollOffset;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        // Check if clicking on scrollbar track (jump to position)
        if (_scrollBarTrackRect != Rect.Empty && _scrollBarTrackRect.Contains(pos))
        {
            int maxScroll = _buffer.ScrollbackCount;
            double trackInsetY = _scrollBarTrackRect.Y + ScrollBarInnerPadding;
            double trackInsetHeight = _scrollBarTrackRect.Height - ScrollBarInnerPadding * 2;
            double ratio = Math.Clamp((pos.Y - trackInsetY) / trackInsetHeight, 0, 1);
            _scrollOffset = Math.Clamp(maxScroll - (int)(ratio * maxScroll), 0, maxScroll);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var (row, col) = ScreenPosToCell(pos);

        if (e.ClickCount == 2)
        {
            // Double-click: select word
            SelectWord(row, col);
        }
        else if (e.ClickCount == 3)
        {
            // Triple-click: select line
            SelectLine(row);
        }
        else
        {
            // Single click: start selection
            int totalRow = _buffer.ScrollbackCount - _scrollOffset + row;
            _selectionStart = (totalRow, col);
            _selectionEnd = _selectionStart;
            _hasSelection = false;
            _isDragging = true;
            CaptureMouse();
        }

        InvalidateVisual();
        e.Handled = true;
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(this);

        // Handle scrollbar dragging
        if (_isScrollBarDragging)
        {
            int maxScroll = _buffer.ScrollbackCount;
            double trackInsetHeight = _scrollBarTrackRect.Height - ScrollBarInnerPadding * 2;
            double thumbHeight = _scrollBarThumbRect.Height;
            double trackTravel = Math.Max(1, trackInsetHeight - thumbHeight);
            double deltaY = pos.Y - _scrollBarDragStartY;
            double deltaRatio = deltaY / trackTravel;
            int deltaLines = (int)(deltaRatio * maxScroll);
            _scrollOffset = Math.Clamp(_scrollBarDragStartOffset - deltaLines, 0, maxScroll);
            InvalidateVisual();
            return;
        }

        if (!_isDragging) return;

        var (row, col) = ScreenPosToCell(pos);
        int totalRow = _buffer.ScrollbackCount - _scrollOffset + row;

        _selectionEnd = (totalRow, col);
        _hasSelection = _selectionStart != _selectionEnd;
        InvalidateVisual();
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        if (_isScrollBarDragging)
        {
            _isScrollBarDragging = false;
            ReleaseMouseCapture();
            return;
        }

        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();
        }
    }

    private void OnMouseWheelHandler(object sender, MouseWheelEventArgs e)
    {
        int delta = e.Delta > 0 ? -3 : 3; // Scroll 3 lines at a time
        int maxScroll = _buffer.ScrollbackCount;
        _scrollOffset = Math.Clamp(_scrollOffset + delta, 0, maxScroll);
        InvalidateVisual();
        e.Handled = true;
    }

    private (int row, int col) ScreenPosToCell(Point pos)
    {
        var padding = Padding;
        var border = BorderThickness;
        double x = pos.X - border.Left - padding.Left;
        double y = pos.Y - border.Top - padding.Top;

        int row = _cellHeight > 0 ? (int)(y / _cellHeight) : 0;
        row = Math.Clamp(row, 0, _buffer.Rows - 1);

        // Get the row text for precise hit testing
        int scrollbackCount = _buffer.ScrollbackCount;
        int totalRow = scrollbackCount - _scrollOffset + row;
        string rowText;
        if (totalRow < scrollbackCount)
            rowText = _buffer.GetScrollbackLineText(totalRow);
        else if (totalRow - scrollbackCount < _buffer.Rows)
            rowText = _buffer.GetRowTextRaw(totalRow - scrollbackCount);
        else
            rowText = string.Empty;

        int col;
        string fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        double fontSize = FontSize > 0 ? FontSize : 14;

        if (rowText.Length > 0)
        {
            // Use DirectWrite hit testing within the text range
            double textEndX = GetColumnX(rowText, rowText.Length, fontFamily, fontSize);
            if (x <= textEndX)
            {
                // Within text — use HitTestPoint for accuracy
                if (TextMeasurement.HitTestPoint(rowText, fontFamily, fontSize, (float)x, out var hitResult))
                {
                    col = (int)hitResult.TextPosition;
                    if (hitResult.IsTrailingHit != 0)
                        col++;
                }
                else
                {
                    col = _cellWidth > 0 ? (int)(x / _cellWidth) : 0;
                }
            }
            else
            {
                // Beyond text — use _cellWidth grid from text end
                col = rowText.Length + (_cellWidth > 0 ? (int)((x - textEndX) / _cellWidth) : 0);
            }
        }
        else
        {
            // Empty row — pure grid
            col = _cellWidth > 0 ? (int)(x / _cellWidth) : 0;
        }

        col = Math.Clamp(col, 0, _buffer.Columns);
        return (row, col);
    }

    #endregion

    #region Selection

    private void ClearSelection()
    {
        if (_hasSelection)
        {
            _hasSelection = false;
            InvalidateVisual();
        }
    }

    private (int row, int col) NormalizeSelectionStart()
    {
        if (_selectionStart.row < _selectionEnd.row ||
            (_selectionStart.row == _selectionEnd.row && _selectionStart.col <= _selectionEnd.col))
            return _selectionStart;
        return _selectionEnd;
    }

    private (int row, int col) NormalizeSelectionEnd()
    {
        if (_selectionStart.row < _selectionEnd.row ||
            (_selectionStart.row == _selectionEnd.row && _selectionStart.col <= _selectionEnd.col))
            return _selectionEnd;
        return _selectionStart;
    }

    private void SelectWord(int screenRow, int col)
    {
        int totalRow = _buffer.ScrollbackCount - _scrollOffset + screenRow;
        string lineText;

        if (totalRow < _buffer.ScrollbackCount)
            lineText = _buffer.GetScrollbackLineText(totalRow);
        else
            lineText = _buffer.GetRowTextRaw(totalRow - _buffer.ScrollbackCount);

        // Find word boundaries
        int start = col;
        int end = col;

        while (start > 0 && IsWordChar(lineText, start - 1))
            start--;
        while (end < lineText.Length - 1 && IsWordChar(lineText, end + 1))
            end++;

        _selectionStart = (totalRow, start);
        _selectionEnd = (totalRow, end);
        _hasSelection = true;
    }

    private void SelectLine(int screenRow)
    {
        int totalRow = _buffer.ScrollbackCount - _scrollOffset + screenRow;
        _selectionStart = (totalRow, 0);
        _selectionEnd = (totalRow, _buffer.Columns);
        _hasSelection = true;
    }

    private static bool IsWordChar(string text, int index)
    {
        if (index < 0 || index >= text.Length) return false;
        char c = text[index];
        return char.IsLetterOrDigit(c) || c == '_' || c == '-';
    }

    #endregion

    #region Process I/O

    private void OnProcessOutput(string data)
    {
        // Process output comes on a background thread; dispatch to UI thread
        Dispatcher.InvokeAsync(() =>
        {
            _parser.Feed(data);

            // Auto-scroll to bottom when new output arrives
            if (_scrollOffset > 0 && !_isDragging)
                _scrollOffset = 0;

            InvalidateVisual();

            // Raise event
            var args = new TerminalOutputEventArgs(OutputReceivedEvent, this, data);
            RaiseEvent(args);
        });
    }

    private void OnProcessExited(int exitCode)
    {
        Dispatcher.InvokeAsync(() =>
        {
            var args = new TerminalProcessExitedEventArgs(ProcessExitedEvent, this, exitCode);
            RaiseEvent(args);
        });
    }

    private void OnParserTitleChanged(string title)
    {
        Dispatcher.InvokeAsync(() =>
        {
            Title = title;
            RaiseEvent(new RoutedEventArgs(TitleChangedEvent, this));
        });
    }

    private void OnParserBellRung()
    {
        // Could flash the terminal or play a sound
        Dispatcher.InvokeAsync(() =>
        {
            // Visual bell - briefly flash background
            InvalidateVisual();
        });
    }

    #endregion

    #region Caret Blinking

    /// <summary>
    /// Whether we are subscribed to CompositionTarget.Rendering for caret animation.
    /// </summary>
    private bool _caretAnimationActive;

    private void StartCaretAnimation()
    {
        if (_caretAnimationActive) return;
        _caretAnimationActive = true;
        CompositionTarget.Rendering += OnCompositionTargetRendering;
        CompositionTarget.Subscribe();
    }

    private void StopCaretAnimation()
    {
        if (!_caretAnimationActive) return;
        _caretAnimationActive = false;
        CompositionTarget.Rendering -= OnCompositionTargetRendering;
        CompositionTarget.Unsubscribe();
    }

    private void OnCompositionTargetRendering(object? sender, EventArgs e)
    {
        if (!IsFocused || !_parser.CursorVisible)
            return;

        var prevOpacity = _caretOpacity;
        UpdateCaretAnimation();

        // Only invalidate when opacity changes (hold phases skip redraw)
        if (Math.Abs(_caretOpacity - prevOpacity) > 0.005)
            InvalidateVisual();
    }

    private double UpdateCaretAnimation()
    {
        var elapsed = (DateTime.Now - _lastCaretBlink).TotalMilliseconds;
        var fullCycleTime = (CaretBlinkInterval + CaretFadeDuration) * 2.0;
        var timeInCycle = elapsed % fullCycleTime;

        double visibleEnd = CaretBlinkInterval;
        double fadeOutEnd = CaretBlinkInterval + CaretFadeDuration;
        double hiddenEnd = CaretBlinkInterval * 2 + CaretFadeDuration;

        if (timeInCycle < visibleEnd)
            _caretOpacity = 1.0;
        else if (timeInCycle < fadeOutEnd)
            _caretOpacity = 1.0 - EaseInOutQuad((timeInCycle - visibleEnd) / CaretFadeDuration);
        else if (timeInCycle < hiddenEnd)
            _caretOpacity = 0.0;
        else
            _caretOpacity = EaseInOutQuad((timeInCycle - hiddenEnd) / CaretFadeDuration);

        _caretVisible = _caretOpacity > 0.01;
        return _caretOpacity;
    }

    private static double EaseInOutQuad(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return t < 0.5 ? 2.0 * t * t : 1.0 - Math.Pow(-2.0 * t + 2.0, 2) / 2.0;
    }

    private void ResetCaretBlink()
    {
        _caretVisible = true;
        _caretOpacity = 1.0;
        _lastCaretBlink = DateTime.Now;
        StartCaretAnimation();
    }

    private void OnGotFocusHandler(object sender, RoutedEventArgs e)
    {
        ResetCaretBlink();
        InvalidateVisual();
    }

    private void OnLostFocusHandler(object sender, RoutedEventArgs e)
    {
        StopCaretAnimation();
        _caretVisible = false;
        _caretOpacity = 0.0;
        InvalidateVisual();
    }

    #endregion

    #region IME Support

    /// <inheritdoc />
    public Point GetImeCaretPosition()
    {
        var padding = Padding;
        var border = BorderThickness;
        string fontFamily = FontFamily ?? FrameworkElement.DefaultFontFamilyName;
        double fontSize = FontSize > 0 ? FontSize : 14;
        string rowText = _buffer.GetRowTextRaw(_buffer.CursorRow);
        double x = border.Left + padding.Left + GetColumnX(rowText, _buffer.CursorCol, fontFamily, fontSize);
        double y = border.Top + padding.Top + _buffer.CursorRow * _cellHeight;
        return new Point(x, y);
    }

    /// <inheritdoc />
    public void OnImeCompositionStart()
    {
        _isImeComposing = true;
        _imeCompositionString = string.Empty;
        _imeCompositionCursor = 0;
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

        if (!string.IsNullOrEmpty(resultString) && !IsReadOnly)
        {
            _process?.WriteInput(resultString);
        }

        InvalidateVisual();
    }

    #endregion

    #region Helpers

    private static string GetDefaultShell()
    {
        if (OperatingSystem.IsWindows())
        {
            // Prefer PowerShell Core, fall back to Windows PowerShell, then cmd
            string? pwshPath = Environment.GetEnvironmentVariable("ProgramFiles");
            if (pwshPath != null)
            {
                string pwshExe = Path.Combine(pwshPath, "PowerShell", "7", "pwsh.exe");
                if (File.Exists(pwshExe))
                    return pwshExe;
            }

            string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
            string windowsPowerShell = Path.Combine(systemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(windowsPowerShell))
                return windowsPowerShell;

            return Path.Combine(systemRoot, "System32", "cmd.exe");
        }

        // Unix-like systems
        string? shellEnv = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(shellEnv))
            return shellEnv;

        return "/bin/bash";
    }

    #endregion
}

#region Event Args & Delegates

/// <summary>
/// Delegate for terminal output events.
/// </summary>
public delegate void TerminalOutputEventHandler(object sender, TerminalOutputEventArgs e);

/// <summary>
/// Event data for terminal output.
/// </summary>
public class TerminalOutputEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the output data string.
    /// </summary>
    public string Data { get; }

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public TerminalOutputEventArgs(RoutedEvent routedEvent, object source, string data)
        : base(routedEvent, source)
    {
        Data = data;
    }
}

/// <summary>
/// Delegate for terminal process exited events.
/// </summary>
public delegate void TerminalProcessExitedEventHandler(object sender, TerminalProcessExitedEventArgs e);

/// <summary>
/// Event data for process exit.
/// </summary>
public class TerminalProcessExitedEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the process exit code.
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public TerminalProcessExitedEventArgs(RoutedEvent routedEvent, object source, int exitCode)
        : base(routedEvent, source)
    {
        ExitCode = exitCode;
    }
}

/// <summary>
/// Terminal cursor display styles.
/// </summary>
public enum TerminalCursorStyle
{
    /// <summary>
    /// Vertical bar cursor (default).
    /// </summary>
    Bar,

    /// <summary>
    /// Block cursor (fills the entire cell).
    /// </summary>
    Block,

    /// <summary>
    /// Underline cursor.
    /// </summary>
    Underline
}

#endregion
