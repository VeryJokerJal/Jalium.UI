using Jalium.UI.Media;

namespace Jalium.UI.Controls.TerminalEmulator;

/// <summary>
/// Manages the terminal character grid and scrollback buffer.
/// Provides cursor movement, scrolling, line manipulation, and selection support.
/// </summary>
internal class TerminalBuffer
{
    #region Fields

    /// <summary>
    /// The active screen buffer (visible rows x columns).
    /// </summary>
    private TerminalChar[,] _screen;

    /// <summary>
    /// The scrollback buffer (oldest lines first).
    /// </summary>
    private readonly List<TerminalChar[]> _scrollback = new();

    /// <summary>
    /// Maximum number of scrollback lines retained.
    /// </summary>
    private int _maxScrollback;

    /// <summary>
    /// Current cursor column (0-based).
    /// </summary>
    private int _cursorCol;

    /// <summary>
    /// Current cursor row (0-based, relative to visible screen).
    /// </summary>
    private int _cursorRow;

    /// <summary>
    /// Number of columns in the terminal.
    /// </summary>
    private int _columns;

    /// <summary>
    /// Number of visible rows in the terminal.
    /// </summary>
    private int _rows;

    /// <summary>
    /// Current foreground color index for new characters.
    /// </summary>
    private int _currentFgIndex = -1;

    /// <summary>
    /// Current background color index for new characters.
    /// </summary>
    private int _currentBgIndex = -1;

    /// <summary>
    /// Current true-color foreground (null = use palette index).
    /// </summary>
    private Color? _currentFgRgb;

    /// <summary>
    /// Current true-color background (null = use palette index).
    /// </summary>
    private Color? _currentBgRgb;

    /// <summary>
    /// Current character attributes for new characters.
    /// </summary>
    private CharAttributes _currentAttributes;

    /// <summary>
    /// Saved cursor position (DEC save/restore).
    /// </summary>
    private (int row, int col) _savedCursor;

    /// <summary>
    /// Top of the scrolling region (0-based, inclusive).
    /// </summary>
    private int _scrollTop;

    /// <summary>
    /// Bottom of the scrolling region (0-based, inclusive).
    /// </summary>
    private int _scrollBottom;

    /// <summary>
    /// Whether auto-wrap mode is enabled.
    /// </summary>
    private bool _autoWrap = true;

    /// <summary>
    /// Whether the cursor is pending wrap (at the right margin after writing the last column).
    /// </summary>
    private bool _wrapPending;

    /// <summary>
    /// Alternate screen buffer (used by full-screen apps like vim, less).
    /// </summary>
    private TerminalChar[,]? _alternateScreen;

    /// <summary>
    /// Saved primary cursor when switching to alternate screen.
    /// </summary>
    private (int row, int col) _primaryCursor;

    /// <summary>
    /// Whether the alternate screen buffer is active.
    /// </summary>
    private bool _isAlternateScreen;

    #endregion

    #region Properties

    /// <summary>
    /// Gets the number of columns.
    /// </summary>
    public int Columns => _columns;

    /// <summary>
    /// Gets the number of visible rows.
    /// </summary>
    public int Rows => _rows;

    /// <summary>
    /// Gets the current cursor column.
    /// </summary>
    public int CursorCol => _cursorCol;

    /// <summary>
    /// Gets the current cursor row.
    /// </summary>
    public int CursorRow => _cursorRow;

    /// <summary>
    /// Gets the total number of scrollback lines.
    /// </summary>
    public int ScrollbackCount => _scrollback.Count;

    /// <summary>
    /// Gets or sets the maximum scrollback lines.
    /// </summary>
    public int MaxScrollback
    {
        get => _maxScrollback;
        set
        {
            _maxScrollback = Math.Max(0, value);
            TrimScrollback();
        }
    }

    /// <summary>
    /// Gets whether the alternate screen buffer is active.
    /// </summary>
    public bool IsAlternateScreen => _isAlternateScreen;

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new terminal buffer with the specified dimensions.
    /// </summary>
    public TerminalBuffer(int columns, int rows, int maxScrollback = 10000)
    {
        _columns = Math.Max(1, columns);
        _rows = Math.Max(1, rows);
        _maxScrollback = maxScrollback;
        _screen = new TerminalChar[_rows, _columns];
        ClearScreen();
        _scrollTop = 0;
        _scrollBottom = _rows - 1;
    }

    #endregion

    #region Character Writing

    /// <summary>
    /// Writes a character at the current cursor position and advances the cursor.
    /// </summary>
    public void WriteChar(char c)
    {
        if (_wrapPending && _autoWrap)
        {
            _wrapPending = false;
            _cursorCol = 0;
            LineFeed();
        }

        if (_cursorRow >= 0 && _cursorRow < _rows && _cursorCol >= 0 && _cursorCol < _columns)
        {
            _screen[_cursorRow, _cursorCol] = new TerminalChar
            {
                Character = c,
                ForegroundIndex = _currentFgIndex,
                BackgroundIndex = _currentBgIndex,
                ForegroundRgb = _currentFgRgb,
                BackgroundRgb = _currentBgRgb,
                Attributes = _currentAttributes
            };
        }

        if (_cursorCol < _columns - 1)
        {
            _cursorCol++;
        }
        else
        {
            // At the last column, set wrap pending
            _wrapPending = true;
        }
    }

    #endregion

    #region Cursor Movement

    /// <summary>
    /// Moves the cursor to the specified position (0-based).
    /// </summary>
    public void SetCursorPosition(int row, int col)
    {
        _cursorRow = Math.Clamp(row, 0, _rows - 1);
        _cursorCol = Math.Clamp(col, 0, _columns - 1);
        _wrapPending = false;
    }

    /// <summary>
    /// Moves the cursor up by the specified number of rows.
    /// </summary>
    public void CursorUp(int count = 1)
    {
        _cursorRow = Math.Max(_scrollTop, _cursorRow - count);
        _wrapPending = false;
    }

    /// <summary>
    /// Moves the cursor down by the specified number of rows.
    /// </summary>
    public void CursorDown(int count = 1)
    {
        _cursorRow = Math.Min(_scrollBottom, _cursorRow + count);
        _wrapPending = false;
    }

    /// <summary>
    /// Moves the cursor forward (right) by the specified number of columns.
    /// </summary>
    public void CursorForward(int count = 1)
    {
        _cursorCol = Math.Min(_columns - 1, _cursorCol + count);
        _wrapPending = false;
    }

    /// <summary>
    /// Moves the cursor backward (left) by the specified number of columns.
    /// </summary>
    public void CursorBackward(int count = 1)
    {
        _cursorCol = Math.Max(0, _cursorCol - count);
        _wrapPending = false;
    }

    /// <summary>
    /// Performs a carriage return (moves cursor to column 0).
    /// </summary>
    public void CarriageReturn()
    {
        _cursorCol = 0;
        _wrapPending = false;
    }

    /// <summary>
    /// Performs a line feed (moves cursor down, scrolling if at the bottom of the scroll region).
    /// </summary>
    public void LineFeed()
    {
        _wrapPending = false;
        if (_cursorRow == _scrollBottom)
        {
            ScrollUp(1);
        }
        else if (_cursorRow < _rows - 1)
        {
            _cursorRow++;
        }
    }

    /// <summary>
    /// Performs a reverse line feed (moves cursor up, scrolling down if at the top of the scroll region).
    /// </summary>
    public void ReverseLineFeed()
    {
        _wrapPending = false;
        if (_cursorRow == _scrollTop)
        {
            ScrollDown(1);
        }
        else if (_cursorRow > 0)
        {
            _cursorRow--;
        }
    }

    /// <summary>
    /// Saves the current cursor position.
    /// </summary>
    public void SaveCursor()
    {
        _savedCursor = (_cursorRow, _cursorCol);
    }

    /// <summary>
    /// Restores the previously saved cursor position.
    /// </summary>
    public void RestoreCursor()
    {
        _cursorRow = Math.Clamp(_savedCursor.row, 0, _rows - 1);
        _cursorCol = Math.Clamp(_savedCursor.col, 0, _columns - 1);
        _wrapPending = false;
    }

    /// <summary>
    /// Performs a tab stop (moves to next multiple-of-8 column).
    /// </summary>
    public void Tab()
    {
        int nextTab = ((_cursorCol / 8) + 1) * 8;
        _cursorCol = Math.Min(nextTab, _columns - 1);
        _wrapPending = false;
    }

    /// <summary>
    /// Performs a backspace (moves cursor left by 1, does not erase).
    /// </summary>
    public void Backspace()
    {
        if (_cursorCol > 0)
            _cursorCol--;
        _wrapPending = false;
    }

    #endregion

    #region Scrolling

    /// <summary>
    /// Scrolls the scroll region up by the specified number of lines.
    /// The top line is pushed to scrollback (if in the primary screen).
    /// </summary>
    public void ScrollUp(int lines = 1)
    {
        for (int n = 0; n < lines; n++)
        {
            // Push the top scroll-region line to scrollback (primary screen only)
            if (!_isAlternateScreen && _scrollTop == 0)
            {
                var scrolledLine = new TerminalChar[_columns];
                for (int c = 0; c < _columns; c++)
                    scrolledLine[c] = _screen[_scrollTop, c];
                _scrollback.Add(scrolledLine);
                TrimScrollback();
            }

            // Shift lines up within the scroll region
            for (int r = _scrollTop; r < _scrollBottom; r++)
            {
                for (int c = 0; c < _columns; c++)
                    _screen[r, c] = _screen[r + 1, c];
            }

            // Clear the bottom line of the scroll region
            for (int c = 0; c < _columns; c++)
                _screen[_scrollBottom, c] = TerminalChar.Blank;
        }
    }

    /// <summary>
    /// Scrolls the scroll region down by the specified number of lines.
    /// </summary>
    public void ScrollDown(int lines = 1)
    {
        for (int n = 0; n < lines; n++)
        {
            // Shift lines down within the scroll region
            for (int r = _scrollBottom; r > _scrollTop; r--)
            {
                for (int c = 0; c < _columns; c++)
                    _screen[r, c] = _screen[r - 1, c];
            }

            // Clear the top line of the scroll region
            for (int c = 0; c < _columns; c++)
                _screen[_scrollTop, c] = TerminalChar.Blank;
        }
    }

    /// <summary>
    /// Sets the scrolling region (1-based, inclusive, as received from ANSI sequences).
    /// </summary>
    public void SetScrollRegion(int top, int bottom)
    {
        _scrollTop = Math.Clamp(top - 1, 0, _rows - 1);
        _scrollBottom = Math.Clamp(bottom - 1, _scrollTop, _rows - 1);
        SetCursorPosition(0, 0);
    }

    /// <summary>
    /// Resets the scroll region to the full screen.
    /// </summary>
    public void ResetScrollRegion()
    {
        _scrollTop = 0;
        _scrollBottom = _rows - 1;
    }

    #endregion

    #region Erasing

    /// <summary>
    /// Erases from cursor to end of screen (ESC[0J).
    /// </summary>
    public void EraseToEndOfScreen()
    {
        // Erase from cursor to end of current line
        EraseToEndOfLine();

        // Erase all subsequent lines
        for (int r = _cursorRow + 1; r < _rows; r++)
            for (int c = 0; c < _columns; c++)
                _screen[r, c] = TerminalChar.Blank;
    }

    /// <summary>
    /// Erases from beginning of screen to cursor (ESC[1J).
    /// </summary>
    public void EraseToStartOfScreen()
    {
        // Erase all preceding lines
        for (int r = 0; r < _cursorRow; r++)
            for (int c = 0; c < _columns; c++)
                _screen[r, c] = TerminalChar.Blank;

        // Erase from beginning of current line to cursor
        EraseToStartOfLine();
    }

    /// <summary>
    /// Erases the entire screen (ESC[2J).
    /// </summary>
    public void ClearScreen()
    {
        for (int r = 0; r < _rows; r++)
            for (int c = 0; c < _columns; c++)
                _screen[r, c] = TerminalChar.Blank;
    }

    /// <summary>
    /// Erases from cursor to end of line (ESC[0K).
    /// </summary>
    public void EraseToEndOfLine()
    {
        for (int c = _cursorCol; c < _columns; c++)
            _screen[_cursorRow, c] = TerminalChar.Blank;
    }

    /// <summary>
    /// Erases from beginning of line to cursor (ESC[1K).
    /// </summary>
    public void EraseToStartOfLine()
    {
        for (int c = 0; c <= _cursorCol && c < _columns; c++)
            _screen[_cursorRow, c] = TerminalChar.Blank;
    }

    /// <summary>
    /// Erases the entire current line (ESC[2K).
    /// </summary>
    public void EraseLine()
    {
        for (int c = 0; c < _columns; c++)
            _screen[_cursorRow, c] = TerminalChar.Blank;
    }

    /// <summary>
    /// Erases the specified number of characters starting at the cursor position.
    /// </summary>
    public void EraseCharacters(int count)
    {
        for (int i = 0; i < count && _cursorCol + i < _columns; i++)
            _screen[_cursorRow, _cursorCol + i] = TerminalChar.Blank;
    }

    #endregion

    #region Line Operations

    /// <summary>
    /// Inserts blank lines at the cursor row, pushing existing lines down within the scroll region.
    /// </summary>
    public void InsertLines(int count)
    {
        for (int n = 0; n < count; n++)
        {
            for (int r = _scrollBottom; r > _cursorRow; r--)
                for (int c = 0; c < _columns; c++)
                    _screen[r, c] = _screen[r - 1, c];

            for (int c = 0; c < _columns; c++)
                _screen[_cursorRow, c] = TerminalChar.Blank;
        }
    }

    /// <summary>
    /// Deletes lines at the cursor row, pulling lines up from below within the scroll region.
    /// </summary>
    public void DeleteLines(int count)
    {
        for (int n = 0; n < count; n++)
        {
            for (int r = _cursorRow; r < _scrollBottom; r++)
                for (int c = 0; c < _columns; c++)
                    _screen[r, c] = _screen[r + 1, c];

            for (int c = 0; c < _columns; c++)
                _screen[_scrollBottom, c] = TerminalChar.Blank;
        }
    }

    /// <summary>
    /// Inserts blank characters at the cursor position, shifting existing characters right.
    /// </summary>
    public void InsertCharacters(int count)
    {
        for (int n = 0; n < count; n++)
        {
            for (int c = _columns - 1; c > _cursorCol; c--)
                _screen[_cursorRow, c] = _screen[_cursorRow, c - 1];

            _screen[_cursorRow, _cursorCol] = TerminalChar.Blank;
        }
    }

    /// <summary>
    /// Deletes characters at the cursor position, shifting characters left.
    /// </summary>
    public void DeleteCharacters(int count)
    {
        for (int n = 0; n < count; n++)
        {
            for (int c = _cursorCol; c < _columns - 1; c++)
                _screen[_cursorRow, c] = _screen[_cursorRow, c + 1];

            _screen[_cursorRow, _columns - 1] = TerminalChar.Blank;
        }
    }

    #endregion

    #region Color / Attribute Management

    /// <summary>
    /// Sets the current foreground color by palette index.
    /// </summary>
    public void SetForegroundIndex(int index)
    {
        _currentFgIndex = index;
        _currentFgRgb = null;
    }

    /// <summary>
    /// Sets the current background color by palette index.
    /// </summary>
    public void SetBackgroundIndex(int index)
    {
        _currentBgIndex = index;
        _currentBgRgb = null;
    }

    /// <summary>
    /// Sets the current foreground to a true-color RGB value.
    /// </summary>
    public void SetForegroundRgb(byte r, byte g, byte b)
    {
        _currentFgRgb = Color.FromRgb(r, g, b);
        _currentFgIndex = -1;
    }

    /// <summary>
    /// Sets the current background to a true-color RGB value.
    /// </summary>
    public void SetBackgroundRgb(byte r, byte g, byte b)
    {
        _currentBgRgb = Color.FromRgb(r, g, b);
        _currentBgIndex = -1;
    }

    /// <summary>
    /// Resets foreground to default.
    /// </summary>
    public void ResetForeground()
    {
        _currentFgIndex = -1;
        _currentFgRgb = null;
    }

    /// <summary>
    /// Resets background to default.
    /// </summary>
    public void ResetBackground()
    {
        _currentBgIndex = -1;
        _currentBgRgb = null;
    }

    /// <summary>
    /// Resets all attributes and colors to default.
    /// </summary>
    public void ResetAttributes()
    {
        _currentFgIndex = -1;
        _currentBgIndex = -1;
        _currentFgRgb = null;
        _currentBgRgb = null;
        _currentAttributes = CharAttributes.None;
    }

    /// <summary>
    /// Adds a character attribute flag.
    /// </summary>
    public void SetAttribute(CharAttributes attr)
    {
        _currentAttributes |= attr;
    }

    /// <summary>
    /// Removes a character attribute flag.
    /// </summary>
    public void ClearAttribute(CharAttributes attr)
    {
        _currentAttributes &= ~attr;
    }

    #endregion

    #region Alternate Screen Buffer

    /// <summary>
    /// Switches to the alternate screen buffer (used by full-screen terminal apps).
    /// </summary>
    public void EnableAlternateScreen()
    {
        if (_isAlternateScreen) return;
        _isAlternateScreen = true;
        _primaryCursor = (_cursorRow, _cursorCol);
        _alternateScreen = _screen;
        _screen = new TerminalChar[_rows, _columns];
        ClearScreen();
        ResetScrollRegion();
    }

    /// <summary>
    /// Switches back to the primary screen buffer.
    /// </summary>
    public void DisableAlternateScreen()
    {
        if (!_isAlternateScreen) return;
        _isAlternateScreen = false;
        _screen = _alternateScreen ?? new TerminalChar[_rows, _columns];
        _alternateScreen = null;
        _cursorRow = Math.Clamp(_primaryCursor.row, 0, _rows - 1);
        _cursorCol = Math.Clamp(_primaryCursor.col, 0, _columns - 1);
        ResetScrollRegion();
    }

    #endregion

    #region Resize

    /// <summary>
    /// Resizes the terminal buffer to new dimensions, preserving content where possible.
    /// </summary>
    public void Resize(int newColumns, int newRows)
    {
        if (newColumns == _columns && newRows == _rows) return;

        newColumns = Math.Max(1, newColumns);
        newRows = Math.Max(1, newRows);

        var newScreen = new TerminalChar[newRows, newColumns];

        // Copy existing content
        int copyRows = Math.Min(_rows, newRows);
        int copyCols = Math.Min(_columns, newColumns);
        for (int r = 0; r < copyRows; r++)
            for (int c = 0; c < copyCols; c++)
                newScreen[r, c] = _screen[r, c];

        // Fill new cells with blanks
        for (int r = 0; r < newRows; r++)
            for (int c = (r < copyRows ? copyCols : 0); c < newColumns; c++)
                newScreen[r, c] = TerminalChar.Blank;

        _screen = newScreen;
        _columns = newColumns;
        _rows = newRows;
        _cursorRow = Math.Clamp(_cursorRow, 0, _rows - 1);
        _cursorCol = Math.Clamp(_cursorCol, 0, _columns - 1);
        _scrollTop = 0;
        _scrollBottom = _rows - 1;

        // Resize alternate screen if it exists
        if (_alternateScreen != null)
        {
            var newAlt = new TerminalChar[newRows, newColumns];
            int altCopyRows = Math.Min(_alternateScreen.GetLength(0), newRows);
            int altCopyCols = Math.Min(_alternateScreen.GetLength(1), newColumns);
            for (int r = 0; r < altCopyRows; r++)
                for (int c = 0; c < altCopyCols; c++)
                    newAlt[r, c] = _alternateScreen[r, c];
            for (int r = 0; r < newRows; r++)
                for (int c = (r < altCopyRows ? altCopyCols : 0); c < newColumns; c++)
                    newAlt[r, c] = TerminalChar.Blank;
            _alternateScreen = newAlt;
        }
    }

    #endregion

    #region Cell Access

    /// <summary>
    /// Gets the character cell at the specified screen position.
    /// </summary>
    public TerminalChar GetCell(int row, int col)
    {
        if (row < 0 || row >= _rows || col < 0 || col >= _columns)
            return TerminalChar.Blank;
        return _screen[row, col];
    }

    /// <summary>
    /// Gets a scrollback line by index (0 = oldest).
    /// </summary>
    public TerminalChar[] GetScrollbackLine(int index)
    {
        if (index < 0 || index >= _scrollback.Count)
            return Array.Empty<TerminalChar>();
        return _scrollback[index];
    }

    /// <summary>
    /// Gets the text content of a screen row as a string.
    /// </summary>
    public string GetRowText(int row)
    {
        if (row < 0 || row >= _rows) return string.Empty;

        var chars = new char[_columns];
        for (int c = 0; c < _columns; c++)
            chars[c] = _screen[row, c].Character;

        return new string(chars).TrimEnd();
    }

    /// <summary>
    /// Gets the raw text content of a screen row (including trailing spaces).
    /// Used for rendering measurement where trailing spaces affect cursor positioning.
    /// </summary>
    public string GetRowTextRaw(int row)
    {
        if (row < 0 || row >= _rows) return string.Empty;

        var chars = new char[_columns];
        for (int c = 0; c < _columns; c++)
            chars[c] = _screen[row, c].Character;

        return new string(chars);
    }

    /// <summary>
    /// Gets the text content of a scrollback line as a string.
    /// </summary>
    public string GetScrollbackLineText(int index)
    {
        if (index < 0 || index >= _scrollback.Count) return string.Empty;

        var line = _scrollback[index];
        var chars = new char[line.Length];
        for (int i = 0; i < line.Length; i++)
            chars[i] = line[i].Character;

        return new string(chars).TrimEnd();
    }

    #endregion

    #region Private Helpers

    private void TrimScrollback()
    {
        while (_scrollback.Count > _maxScrollback)
            _scrollback.RemoveAt(0);
    }

    #endregion
}
