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
    /// The internal view hosted inside the template's ScrollViewer.
    /// Owns rendering and IScrollInfo.
    /// </summary>
    private TerminalView? _view;

    /// <summary>
    /// The ScrollViewer found in the template.
    /// </summary>
    private ScrollViewer? _scrollViewer;

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
        _view?.ScrollToBottom();
        InvalidateView();
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
        _view?.ScrollToBottom();
        ClearSelection();
        InvalidateView();
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
        InvalidateView();
    }

    #endregion

    #region Property Changed Callbacks

    private static void OnTerminalSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Terminal t && !t.AutoSize)
        {
            t._buffer.Resize(t.TerminalColumns, t.TerminalRows);
            t._process?.NotifyResize(t.TerminalColumns, t.TerminalRows);
            t._view?.InvalidateMeasure();
            t.InvalidateView();
        }
    }

    private static void OnAutoSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Terminal t && t.AutoSize)
        {
            t._view?.InvalidateMeasure();
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
            t.InvalidateView();
        }
    }

    /// <summary>
    /// Invalidates the render-surface view (used instead of Terminal.InvalidateVisual
    /// since rendering is delegated to the inner TerminalView).
    /// </summary>
    private void InvalidateView() => _view?.InvalidateVisual();

    /// <summary>
    /// First visible total-buffer row (0 = top of scrollback,
    /// _buffer.ScrollbackCount = top of live screen = "at bottom").
    /// </summary>
    private int TopLine => _view?.TopLine ?? _buffer.ScrollbackCount;

    #endregion

    #region Layout / Autosize

    private int _lastAutoSizeCols;
    private int _lastAutoSizeRows;
    private double _lastAutoSizeCellWidth;
    private double _lastAutoSizeCellHeight;
    private double _lastAutoSizeWidth;
    private double _lastAutoSizeHeight;

    /// <summary>
    /// Called by TerminalView.MeasureOverride — takes the view's available size
    /// (which is inside border+padding) and adjusts TerminalColumns/Rows to fit.
    /// </summary>
    internal void UpdateAutoSize(Size viewAvailable)
    {
        EnsureCellMetrics();

        double availableWidth = viewAvailable.Width;
        double availableHeight = viewAvailable.Height;

        if (availableWidth <= 0 || availableHeight <= 0 || _cellWidth <= 0 || _cellHeight <= 0)
            return;

        int newCols = Math.Max(1, (int)(availableWidth / _cellWidth));
        int newRows = Math.Max(1, (int)(availableHeight / _cellHeight));

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

    /// <summary>
    /// Entry point for the inner TerminalView. Draws cells, selection, IME and caret
    /// into the view-local coordinate system (origin at the view's top-left).
    /// Chrome (background/border/corners) is provided by the outer Border in the template.
    /// </summary>
    internal void RenderView(DrawingContext dc, Size viewSize)
    {
        EnsureCellMetrics();

        var clipRect = new Rect(0, 0,
            Math.Max(0, Math.Round(viewSize.Width)),
            Math.Max(0, Math.Round(viewSize.Height)));

        dc.PushClip(new RectangleGeometry(clipRect));

        double fracY = FractionalY;
        dc.PushTransform(new TranslateTransform(0, -fracY));

        var contentRect = new Rect(0, 0, clipRect.Width, clipRect.Height + fracY);

        RenderCells(dc, contentRect);

        if (_hasSelection)
            RenderSelection(dc, contentRect);

        if (_isImeComposing && !string.IsNullOrEmpty(_imeCompositionString))
            RenderImeComposition(dc, contentRect);

        if (IsFocused && _parser.CursorVisible && TopLine >= _buffer.ScrollbackCount)
            RenderCursor(dc, contentRect);

        dc.Pop(); // transform
        dc.Pop(); // clip
    }

    private void RenderCells(DrawingContext dc, Rect contentRect)
    {
        int cols = _buffer.Columns;
        int rows = _buffer.Rows;
        int scrollbackCount = _buffer.ScrollbackCount;
        int topLine = TopLine;
        double x0 = contentRect.X;
        double y0 = contentRect.Y;
        int totalLines = scrollbackCount + rows;
        int rowsToDraw = _cellHeight > 0
            ? (int)Math.Ceiling(contentRect.Height / _cellHeight) + 1
            : rows;

        var defaultFg = TerminalForeground;
        string fontFamily = ResolveFontFamily();
        double fontSize = FontSize;

        for (int row = 0; row < rowsToDraw; row++)
        {
            int totalRow = topLine + row;
            int bufferRow;
            TerminalChar[]? scrollbackLine = null;

            if (totalRow < 0) continue;
            if (totalRow >= totalLines) break;

            if (totalRow < scrollbackCount)
            {
                scrollbackLine = _buffer.GetScrollbackLine(totalRow);
                bufferRow = -1;
            }
            else
            {
                bufferRow = totalRow - scrollbackCount;
            }

            double y = y0 + row * _cellHeight;

            // Build the full row text for GetColumnX measurements
            string rowText;
            if (scrollbackLine != null)
            {
                rowText = _buffer.GetScrollbackLineText(totalRow);
            }
            else
            {
                rowText = _buffer.GetRowTextRaw(bufferRow);
            }

            // Draw same-style runs as a single FormattedText positioned at
            // the grid — this lets DirectWrite shape the run naturally while
            // the run's origin is locked to col * cellWidth. For a monospace
            // font the run's own advance width equals (runLen * cellWidth),
            // so glyphs fill their cells with no drift against hit-test.
            int runStart = 0;
            while (runStart < cols)
            {
                TerminalChar startCell = GetCellAt(scrollbackLine, bufferRow, runStart);
                int runEnd = runStart + 1;
                while (runEnd < cols && CellsHaveSameStyle(startCell, GetCellAt(scrollbackLine, bufferRow, runEnd)))
                    runEnd++;

                var (fgColor, bgColor) = ResolveCellColors(startCell);
                double rx = x0 + runStart * _cellWidth;
                double rxEnd = x0 + runEnd * _cellWidth;
                double rw = rxEnd - rx;

                // Background
                if (bgColor != null)
                    dc.DrawRectangle(GetCachedBrush(bgColor.Value), null, new Rect(rx, y, rw, _cellHeight));

                // Text — draw the whole run at its grid origin
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

                // Underline / strikethrough span the whole run
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
        int topLine = TopLine;
        double x0 = contentRect.X;
        double y0 = contentRect.Y;
        int rowsToDraw = _cellHeight > 0
            ? (int)Math.Ceiling(contentRect.Height / _cellHeight) + 1
            : _buffer.Rows;
        int totalLines = scrollbackCount + _buffer.Rows;
        string fontFamily = ResolveFontFamily();
        double fontSize = FontSize > 0 ? FontSize : 14;

        for (int screenRow = 0; screenRow < rowsToDraw; screenRow++)
        {
            int totalRow = topLine + screenRow;
            if (totalRow >= totalLines) break;
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

            // Grid-based positions — RenderCells now also draws each glyph
            // at col * cellWidth, so sx/ex from the same formula stay in
            // lockstep with the visible text.
            double sx = x0 + colStart * _cellWidth;
            double sy = y0 + screenRow * _cellHeight;
            double ex = (colEnd >= _buffer.Columns)
                ? contentRect.Right
                : x0 + colEnd * _cellWidth;

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

        string fontFamily = ResolveFontFamily();
        double fontSize = FontSize > 0 ? FontSize : 14;
        string cursorRowText = _buffer.GetRowTextRaw(_buffer.CursorRow);
        int cursorTotalRow = _buffer.ScrollbackCount + _buffer.CursorRow;
        double x = contentRect.X + _buffer.CursorCol * _cellWidth;
        double y = contentRect.Y + (cursorTotalRow - TopLine) * _cellHeight;

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
        string fontFamily = ResolveFontFamily();
        double fontSize = FontSize > 0 ? FontSize : 14;
        string rowText = _buffer.GetRowTextRaw(_buffer.CursorRow);
        int cursorTotalRow = _buffer.ScrollbackCount + _buffer.CursorRow;
        double x = contentRect.X + _buffer.CursorCol * _cellWidth;
        double y = contentRect.Y + (cursorTotalRow - TopLine) * _cellHeight;
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

    #endregion

    #region Cell Metrics

    internal double CellWidth => _cellWidth;
    internal double CellHeight => _cellHeight;
    internal int BufferRows => _buffer.Rows;
    internal int BufferColumns => _buffer.Columns;
    internal int BufferScrollbackCount => _buffer.ScrollbackCount;
    internal void EnsureCellMetricsPublic() => EnsureCellMetrics();

    private double FractionalY => _view?.FractionalY ?? 0;

    /// <summary>
    /// Resolves the effective font family, falling back to a monospace
    /// font (Cascadia Code → Consolas → Courier New) rather than Segoe UI
    /// when the theme resource fails to resolve. A proportional fallback
    /// breaks the grid-based hit-test / rendering contract because M is
    /// much wider than the average glyph.
    /// </summary>
    private string ResolveFontFamily()
    {
        var ff = FontFamily;
        if (!string.IsNullOrEmpty(ff) && !string.Equals(ff, "Segoe UI", StringComparison.OrdinalIgnoreCase))
            return ff;
        // Consolas ships with every supported version of Windows and is
        // guaranteed monospace — safest fallback when the theme resource
        // fails to resolve (which would otherwise drop us onto Segoe UI,
        // a proportional font that breaks the monospace grid contract).
        return "Consolas";
    }

    private void EnsureCellMetrics()
    {
        string fontFamily = ResolveFontFamily();
        double fontSize = FontSize > 0 ? FontSize : 14;

        // Use DirectWrite font metrics for line height, snapped to pixel grid
        var fontMetrics = TextMeasurement.GetFontMetrics(fontFamily, fontSize);
        _cellHeight = fontMetrics.LineHeight > 0
            ? Math.Ceiling(fontMetrics.LineHeight)
            : Math.Ceiling(fontSize * 1.35);

        const string probe = "MMMMMMMMMM";
        var ft = new FormattedText(probe, fontFamily, fontSize);
        TextMeasurement.MeasureText(ft);
        _cellWidth = ft.IsMeasured && ft.Width > 0
            ? ft.Width / probe.Length
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

    private double GetSelectionColumnX(string rowText, int column, string fontFamily, double fontSize)
    {
        string visibleText = TrimTerminalTrailingSpaces(rowText);
        int visibleLength = visibleText.Length;

        if (column <= visibleLength)
            return GetColumnX(visibleText, column, fontFamily, fontSize);

        double visibleEndX = GetColumnX(visibleText, visibleLength, fontFamily, fontSize);
        return visibleEndX + (column - visibleLength) * _cellWidth;
    }

    private static string TrimTerminalTrailingSpaces(string rowText)
    {
        int end = rowText.Length;
        while (end > 0 && rowText[end - 1] == ' ')
            end--;

        return end == rowText.Length ? rowText : rowText[..end];
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
            _view?.ScrollToBottom();
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
        if (e.Handled || IsReadOnly || string.IsNullOrEmpty(e.Text)) return;

        // Filter control characters — KeyDown already translated them
        // (Enter/Tab/Backspace/Escape/etc.) into the appropriate VT sequence.
        // Without this filter the shell receives every Enter twice.
        var text = e.Text;
        if (text.Length == 1 && char.IsControl(text[0]))
            return;

        ClearSelection();
        _view?.ScrollToBottom();
        ResetCaretBlink();
        _process?.WriteInput(text);
        e.Handled = true;
    }

    #endregion

    #region Mouse Handling

    private void OnMouseDownHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        Focus();

        if (_view == null) return;
        var pos = e.GetPosition(_view);

        var (totalRow, charCol, side) = ScreenPosToCell(pos);

        if (e.ClickCount == 2)
        {
            // Word selection uses the character index, not the half-step.
            SelectWordAt(totalRow, charCol);
        }
        else if (e.ClickCount == 3)
        {
            SelectLineAt(totalRow);
        }
        else
        {
            int effectiveCol = charCol + (side == CellSide.Right ? 1 : 0);
            _selectionStart = (totalRow, effectiveCol);
            _selectionEnd = _selectionStart;
            _hasSelection = false;
            _isDragging = true;
            CaptureMouse();
        }

        InvalidateView();
        e.Handled = true;
    }

    private void OnMouseMoveHandler(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _view == null) return;

        var pos = e.GetPosition(_view);
        var (totalRow, charCol, side) = ScreenPosToCell(pos);
        int effectiveCol = charCol + (side == CellSide.Right ? 1 : 0);

        _selectionEnd = (totalRow, effectiveCol);
        _hasSelection = _selectionStart != _selectionEnd;
        InvalidateView();
    }

    private void OnMouseUpHandler(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();
        }
    }

    private void OnMouseWheelHandler(object sender, MouseWheelEventArgs e)
    {
        if (_view == null) return;

        // 3 lines per wheel notch
        double delta = (e.Delta > 0 ? -3.0 : 3.0) * _cellHeight;
        _view.SetVerticalOffset(_view.VerticalOffset + delta);
        e.Handled = true;
    }

    /// <summary>
    /// Which half of a cell a hit test landed in — used for Alacritty-style
    /// selection anchors where the boundary character is only included once
    /// the cursor crosses its midpoint.
    /// </summary>
    private enum CellSide { Left, Right }

    /// <summary>
    /// Converts a position in the TerminalView's local coordinate system to
    /// (totalRow, charCol, side) — see <see cref="CellSide"/>. `charCol` is
    /// the character index (0..Columns-1), `side` indicates which half of
    /// that cell was hit. Callers either use `charCol` directly (word
    /// selection) or combine with `side` to get an exclusive boundary
    /// (`charCol + (side == Right ? 1 : 0)`) for drag selection.
    /// </summary>
    private (int totalRow, int charCol, CellSide side) ScreenPosToCell(Point pos)
    {
        double x = pos.X;
        double y = pos.Y + FractionalY;

        int scrollbackCount = _buffer.ScrollbackCount;
        int totalLines = scrollbackCount + _buffer.Rows;

        int rowOffset = _cellHeight > 0 ? (int)Math.Floor(y / _cellHeight) : 0;
        int totalRow = TopLine + rowOffset;
        if (totalRow < 0) totalRow = 0;
        if (totalRow > totalLines - 1) totalRow = Math.Max(0, totalLines - 1);

        // Pure grid — matches RenderCells which draws each glyph at col*cellWidth.
        int charCol;
        CellSide side;
        if (_cellWidth > 0)
        {
            double fx = x / _cellWidth;
            charCol = (int)Math.Floor(fx);
            side = (fx - charCol) < 0.5 ? CellSide.Left : CellSide.Right;
        }
        else
        {
            charCol = 0;
            side = CellSide.Left;
        }

        if (charCol < 0) { charCol = 0; side = CellSide.Left; }
        if (charCol >= _buffer.Columns)
        {
            charCol = _buffer.Columns - 1;
            side = CellSide.Right;
        }

        return (totalRow, charCol, side);
    }

    #endregion

    #region Selection

    private void ClearSelection()
    {
        if (_hasSelection)
        {
            _hasSelection = false;
            InvalidateView();
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

    private void SelectWordAt(int totalRow, int charCol)
    {
        string lineText;

        if (totalRow < _buffer.ScrollbackCount)
            lineText = _buffer.GetScrollbackLineText(totalRow);
        else
            lineText = _buffer.GetRowTextRaw(totalRow - _buffer.ScrollbackCount);

        // If click lands outside the line or on a non-word character, just
        // make a caret-style empty selection at that column.
        if (charCol < 0 || charCol >= lineText.Length || !IsWordChar(lineText, charCol))
        {
            _selectionStart = (totalRow, charCol);
            _selectionEnd = (totalRow, charCol);
            _hasSelection = false;
            return;
        }

        // Expand left and right to the full word. `end` is the inclusive
        // index of the last word character; the selection uses an exclusive
        // end boundary, so we store `end + 1`.
        int start = charCol;
        int end = charCol;

        while (start > 0 && IsWordChar(lineText, start - 1))
            start--;
        while (end + 1 < lineText.Length && IsWordChar(lineText, end + 1))
            end++;

        _selectionStart = (totalRow, start);
        _selectionEnd = (totalRow, end + 1);
        _hasSelection = true;
    }

    private void SelectLineAt(int totalRow)
    {
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

            // Scrollback may have grown — refresh extent and optionally stick to bottom.
            _view?.InvalidateMeasure();
            if (_view != null && !_view.IsAtBottom && !_isDragging)
                _view.ScrollToBottom();

            InvalidateView();

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
            InvalidateView();
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
            InvalidateView();
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
        InvalidateView();
    }

    private void OnLostFocusHandler(object sender, RoutedEventArgs e)
    {
        StopCaretAnimation();
        _caretVisible = false;
        _caretOpacity = 0.0;
        InvalidateView();
    }

    #endregion

    #region IME Support

    /// <inheritdoc />
    public Point GetImeCaretPosition()
    {
        double localX = _buffer.CursorCol * _cellWidth;
        double localY = _buffer.CursorRow * _cellHeight;

        // View is nested inside Border(Padding) → ScrollViewer → TerminalView.
        var border = BorderThickness;
        var padding = Padding;
        return new Point(
            border.Left + padding.Left + localX,
            border.Top + padding.Top + localY);
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
        InvalidateView();
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

        InvalidateView();
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

    #region Template

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _scrollViewer = GetTemplateChild("PART_ScrollViewer") as ScrollViewer;
        if (_scrollViewer != null)
        {
            if (_view == null)
                _view = new TerminalView(this);
            _scrollViewer.Content = _view;
        }
    }

    #endregion
}

/// <summary>
/// Inner render surface for the Terminal control. Owns the character grid
/// rendering and reports scroll extent/offset via IScrollInfo so an outer
/// ScrollViewer can provide a real scrollbar.
/// </summary>
public sealed class TerminalView : FrameworkElement, IScrollInfo
{
    private readonly Terminal _owner;
    private double _verticalOffset;
    private Size _viewport;

    internal TerminalView(Terminal owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// First visible total-buffer row (0 = top of scrollback,
    /// owner._buffer.ScrollbackCount = top of live screen / "at bottom").
    /// </summary>
    internal int TopLine
    {
        get
        {
            double cellHeight = _owner.CellHeight;
            if (cellHeight <= 0) return _owner.BufferScrollbackCount;
            int topLine = (int)Math.Floor(_verticalOffset / cellHeight);
            int totalLines = _owner.BufferScrollbackCount + _owner.BufferRows;
            if (topLine < 0) topLine = 0;
            if (topLine > totalLines - 1) topLine = Math.Max(0, totalLines - 1);
            return topLine;
        }
    }

    /// <summary>
    /// Sub-pixel Y offset within the top row, in pixels [0, cellHeight).
    /// Used for smooth pixel-precise scrolling.
    /// </summary>
    internal double FractionalY
    {
        get
        {
            double cellHeight = _owner.CellHeight;
            if (cellHeight <= 0) return 0;
            double frac = _verticalOffset - TopLine * cellHeight;
            if (frac < 0) frac = 0;
            if (frac >= cellHeight) frac = cellHeight - 0.001;
            return frac;
        }
    }

    internal bool IsAtBottom
    {
        get
        {
            double maxOffset = Math.Max(0, ExtentHeight - ViewportHeight);
            return _verticalOffset >= maxOffset - 0.5;
        }
    }

    internal void ScrollToBottom()
    {
        double newOffset = Math.Max(0, ExtentHeight - ViewportHeight);
        if (Math.Abs(newOffset - _verticalOffset) > 0.01)
        {
            _verticalOffset = newOffset;
            ScrollOwner?.InvalidateArrange();
            InvalidateVisual();
        }
    }

    #region IScrollInfo

    /// <inheritdoc />
    public bool CanHorizontallyScroll { get; set; }

    /// <inheritdoc />
    public bool CanVerticallyScroll { get; set; } = true;

    /// <inheritdoc />
    public double ExtentWidth => _viewport.Width;

    /// <inheritdoc />
    public double ExtentHeight
    {
        get
        {
            double cellHeight = _owner.CellHeight;
            if (cellHeight <= 0) return _viewport.Height;
            int totalLines = _owner.BufferScrollbackCount + _owner.BufferRows;
            return totalLines * cellHeight;
        }
    }

    /// <inheritdoc />
    public double ViewportWidth => _viewport.Width;

    /// <summary>
    /// Report viewport as an integer multiple of cellHeight so that the
    /// at-bottom offset (ExtentHeight - ViewportHeight) always lands on a
    /// row boundary — keeps row-based rendering and hit-testing in sync
    /// with the scrollbar's "at bottom" position.
    /// </summary>
    public double ViewportHeight
    {
        get
        {
            double cellHeight = _owner.CellHeight;
            if (cellHeight <= 0) return _viewport.Height;
            return _owner.BufferRows * cellHeight;
        }
    }

    /// <inheritdoc />
    public double HorizontalOffset => 0;

    /// <inheritdoc />
    public double VerticalOffset => _verticalOffset;

    /// <inheritdoc />
    public ScrollViewer? ScrollOwner { get; set; }

    /// <inheritdoc />
    public void LineUp() => SetVerticalOffset(_verticalOffset - _owner.CellHeight);

    /// <inheritdoc />
    public void LineDown() => SetVerticalOffset(_verticalOffset + _owner.CellHeight);

    /// <inheritdoc />
    public void LineLeft() { }

    /// <inheritdoc />
    public void LineRight() { }

    /// <inheritdoc />
    public void PageUp() => SetVerticalOffset(_verticalOffset - ViewportHeight);

    /// <inheritdoc />
    public void PageDown() => SetVerticalOffset(_verticalOffset + ViewportHeight);

    /// <inheritdoc />
    public void PageLeft() { }

    /// <inheritdoc />
    public void PageRight() { }

    /// <inheritdoc />
    public void MouseWheelUp() => SetVerticalOffset(_verticalOffset - 3 * _owner.CellHeight);

    /// <inheritdoc />
    public void MouseWheelDown() => SetVerticalOffset(_verticalOffset + 3 * _owner.CellHeight);

    /// <inheritdoc />
    public void MouseWheelLeft() { }

    /// <inheritdoc />
    public void MouseWheelRight() { }

    /// <inheritdoc />
    public void SetHorizontalOffset(double offset) { }

    /// <inheritdoc />
    public void SetVerticalOffset(double offset)
    {
        double maxOffset = Math.Max(0, ExtentHeight - ViewportHeight);
        double clamped = Math.Clamp(offset, 0, maxOffset);
        if (Math.Abs(clamped - _verticalOffset) < 0.01) return;
        _verticalOffset = clamped;
        ScrollOwner?.InvalidateArrange();
        InvalidateVisual();
    }

    /// <inheritdoc />
    public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;

    #endregion

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        _owner.EnsureCellMetricsPublic();

        double width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        double height = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;

        if (_owner.AutoSize)
        {
            _owner.UpdateAutoSize(new Size(width, height));
        }

        // Report only the viewport size as desired — scrolling is handled via IScrollInfo.
        double desiredWidth = _owner.AutoSize
            ? width
            : Math.Min(width, _owner.BufferColumns * _owner.CellWidth);
        double desiredHeight = _owner.AutoSize
            ? height
            : Math.Min(height, _owner.BufferRows * _owner.CellHeight);

        return new Size(desiredWidth, desiredHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var oldViewport = _viewport;
        _viewport = finalSize;

        // Clamp vertical offset to new extent.
        double maxOffset = Math.Max(0, ExtentHeight - ViewportHeight);
        if (_verticalOffset > maxOffset) _verticalOffset = maxOffset;

        if (oldViewport != _viewport)
            ScrollOwner?.InvalidateArrange();

        return finalSize;
    }

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        base.OnRender(drawingContext);
        if (drawingContext is not DrawingContext dc) return;
        _owner.RenderView(dc, _viewport);
    }
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
