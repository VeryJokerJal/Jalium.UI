using System.Text;

namespace Jalium.UI.Controls.TerminalEmulator;

/// <summary>
/// Parses VT100/ANSI escape sequences from a byte or character stream
/// and dispatches actions to a <see cref="TerminalBuffer"/>.
/// Supports CSI, OSC, DEC private modes, SGR (including 256-color and true-color),
/// and common xterm sequences.
/// </summary>
internal class AnsiParser
{
    #region State

    private enum State
    {
        Ground,
        Escape,
        EscapeIntermediate,
        CsiEntry,
        CsiParam,
        CsiIntermediate,
        OscString,
        DcsEntry,
        CharsetSelect
    }

    private State _state = State.Ground;

    /// <summary>
    /// Accumulated CSI parameter bytes.
    /// </summary>
    private readonly StringBuilder _params = new();

    /// <summary>
    /// Accumulated CSI intermediate bytes.
    /// </summary>
    private readonly StringBuilder _intermediates = new();

    /// <summary>
    /// Accumulated OSC string content.
    /// </summary>
    private readonly StringBuilder _oscString = new();

    /// <summary>
    /// The terminal buffer this parser drives.
    /// </summary>
    private readonly TerminalBuffer _buffer;

    /// <summary>
    /// Fired when the terminal title changes (OSC 0 or OSC 2).
    /// </summary>
    public event Action<string>? TitleChanged;

    /// <summary>
    /// Fired when the terminal bell is triggered (BEL character).
    /// </summary>
    public event Action? BellRung;

    /// <summary>
    /// Whether cursor is visible.
    /// </summary>
    public bool CursorVisible { get; private set; } = true;

    /// <summary>
    /// Whether bracketed paste mode is active.
    /// </summary>
    public bool BracketedPasteMode { get; private set; }

    /// <summary>
    /// Whether the application cursor keys mode is active (sends ESC O x instead of ESC [ x).
    /// </summary>
    public bool ApplicationCursorKeys { get; private set; }

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new ANSI parser that dispatches to the specified buffer.
    /// </summary>
    public AnsiParser(TerminalBuffer buffer)
    {
        _buffer = buffer;
    }

    #endregion

    #region Feed

    /// <summary>
    /// Feeds a string of characters to the parser.
    /// </summary>
    public void Feed(string data)
    {
        foreach (char c in data)
            ProcessChar(c);
    }

    /// <summary>
    /// Feeds a single character to the parser.
    /// </summary>
    public void ProcessChar(char c)
    {
        switch (_state)
        {
            case State.Ground:
                HandleGround(c);
                break;
            case State.Escape:
                HandleEscape(c);
                break;
            case State.EscapeIntermediate:
                HandleEscapeIntermediate(c);
                break;
            case State.CsiEntry:
            case State.CsiParam:
                HandleCsi(c);
                break;
            case State.CsiIntermediate:
                HandleCsiIntermediate(c);
                break;
            case State.OscString:
                HandleOsc(c);
                break;
            case State.CharsetSelect:
                // Consume one character (charset designation) and return to ground
                _state = State.Ground;
                break;
            default:
                _state = State.Ground;
                break;
        }
    }

    #endregion

    #region Ground State

    private void HandleGround(char c)
    {
        switch (c)
        {
            case '\x1b': // ESC
                _state = State.Escape;
                break;
            case '\n': // LF
            case '\x0b': // VT
            case '\x0c': // FF
                _buffer.LineFeed();
                break;
            case '\r': // CR
                _buffer.CarriageReturn();
                break;
            case '\t': // TAB
                _buffer.Tab();
                break;
            case '\b': // BS
                _buffer.Backspace();
                break;
            case '\a': // BEL
                BellRung?.Invoke();
                break;
            case '\x0e': // SO (Shift Out) - select G1 charset
            case '\x0f': // SI (Shift In) - select G0 charset
                // Charset switching - not fully implemented
                break;
            default:
                if (c >= ' ') // Printable character
                    _buffer.WriteChar(c);
                break;
        }
    }

    #endregion

    #region Escape State

    private void HandleEscape(char c)
    {
        switch (c)
        {
            case '[': // CSI
                _state = State.CsiEntry;
                _params.Clear();
                _intermediates.Clear();
                break;
            case ']': // OSC
                _state = State.OscString;
                _oscString.Clear();
                break;
            case '(': // Designate G0 charset
            case ')': // Designate G1 charset
            case '*': // Designate G2 charset
            case '+': // Designate G3 charset
                _state = State.CharsetSelect;
                break;
            case '7': // DECSC - save cursor
                _buffer.SaveCursor();
                _state = State.Ground;
                break;
            case '8': // DECRC - restore cursor
                _buffer.RestoreCursor();
                _state = State.Ground;
                break;
            case 'D': // IND - index (line feed)
                _buffer.LineFeed();
                _state = State.Ground;
                break;
            case 'M': // RI - reverse index
                _buffer.ReverseLineFeed();
                _state = State.Ground;
                break;
            case 'E': // NEL - next line
                _buffer.CarriageReturn();
                _buffer.LineFeed();
                _state = State.Ground;
                break;
            case 'c': // RIS - full reset
                _buffer.ResetAttributes();
                _buffer.ClearScreen();
                _buffer.SetCursorPosition(0, 0);
                _buffer.ResetScrollRegion();
                CursorVisible = true;
                ApplicationCursorKeys = false;
                BracketedPasteMode = false;
                _state = State.Ground;
                break;
            case 'H': // HTS - horizontal tab set (ignored)
                _state = State.Ground;
                break;
            case '=': // DECKPAM - application keypad mode
            case '>': // DECKPNM - numeric keypad mode
                _state = State.Ground;
                break;
            case '#': // DEC line attributes
            case ' ': // Intermediate byte
                _state = State.EscapeIntermediate;
                break;
            case 'P': // DCS - device control string
                _state = State.DcsEntry;
                break;
            case '\\': // ST - string terminator (end of DCS/OSC from ESC state)
                _state = State.Ground;
                break;
            default:
                // Unknown escape - return to ground
                _state = State.Ground;
                break;
        }
    }

    private void HandleEscapeIntermediate(char c)
    {
        // Consume and ignore intermediate + final byte, then return to ground
        _state = State.Ground;
    }

    #endregion

    #region CSI Handling

    private void HandleCsi(char c)
    {
        if (c >= '0' && c <= '9' || c == ';' || c == '?')
        {
            _params.Append(c);
            _state = State.CsiParam;
        }
        else if (c >= ' ' && c <= '/')
        {
            _intermediates.Append(c);
            _state = State.CsiIntermediate;
        }
        else if (c >= '@' && c <= '~')
        {
            // Final byte - dispatch
            DispatchCsi(c);
            _state = State.Ground;
        }
        else
        {
            // Invalid - abort
            _state = State.Ground;
        }
    }

    private void HandleCsiIntermediate(char c)
    {
        if (c >= ' ' && c <= '/')
        {
            _intermediates.Append(c);
        }
        else if (c >= '@' && c <= '~')
        {
            DispatchCsi(c);
            _state = State.Ground;
        }
        else
        {
            _state = State.Ground;
        }
    }

    private void DispatchCsi(char final)
    {
        string paramStr = _params.ToString();
        bool isPrivate = paramStr.StartsWith('?');
        if (isPrivate)
            paramStr = paramStr[1..];

        int[] args = ParseParams(paramStr);

        if (isPrivate)
        {
            DispatchDecPrivateMode(final, args);
            return;
        }

        string inter = _intermediates.ToString();
        if (inter == " ")
        {
            // CSI ... SP final - e.g., DECSCUSR (cursor style)
            // Ignored for now
            return;
        }

        switch (final)
        {
            case 'A': // CUU - cursor up
                _buffer.CursorUp(Math.Max(1, GetParam(args, 0, 1)));
                break;
            case 'B': // CUD - cursor down
                _buffer.CursorDown(Math.Max(1, GetParam(args, 0, 1)));
                break;
            case 'C': // CUF - cursor forward
                _buffer.CursorForward(Math.Max(1, GetParam(args, 0, 1)));
                break;
            case 'D': // CUB - cursor backward
                _buffer.CursorBackward(Math.Max(1, GetParam(args, 0, 1)));
                break;
            case 'E': // CNL - cursor next line
                _buffer.CursorDown(Math.Max(1, GetParam(args, 0, 1)));
                _buffer.CarriageReturn();
                break;
            case 'F': // CPL - cursor previous line
                _buffer.CursorUp(Math.Max(1, GetParam(args, 0, 1)));
                _buffer.CarriageReturn();
                break;
            case 'G': // CHA - cursor horizontal absolute
                _buffer.SetCursorPosition(_buffer.CursorRow, GetParam(args, 0, 1) - 1);
                break;
            case 'H': // CUP - cursor position
            case 'f': // HVP - horizontal vertical position
                _buffer.SetCursorPosition(GetParam(args, 0, 1) - 1, GetParam(args, 1, 1) - 1);
                break;
            case 'J': // ED - erase in display
                switch (GetParam(args, 0, 0))
                {
                    case 0: _buffer.EraseToEndOfScreen(); break;
                    case 1: _buffer.EraseToStartOfScreen(); break;
                    case 2: _buffer.ClearScreen(); break;
                    case 3: _buffer.ClearScreen(); break; // Also clear scrollback (simplified)
                }
                break;
            case 'K': // EL - erase in line
                switch (GetParam(args, 0, 0))
                {
                    case 0: _buffer.EraseToEndOfLine(); break;
                    case 1: _buffer.EraseToStartOfLine(); break;
                    case 2: _buffer.EraseLine(); break;
                }
                break;
            case 'L': // IL - insert lines
                _buffer.InsertLines(Math.Max(1, GetParam(args, 0, 1)));
                break;
            case 'M': // DL - delete lines
                _buffer.DeleteLines(Math.Max(1, GetParam(args, 0, 1)));
                break;
            case 'P': // DCH - delete characters
                _buffer.DeleteCharacters(Math.Max(1, GetParam(args, 0, 1)));
                break;
            case 'X': // ECH - erase characters
                _buffer.EraseCharacters(Math.Max(1, GetParam(args, 0, 1)));
                break;
            case '@': // ICH - insert characters
                _buffer.InsertCharacters(Math.Max(1, GetParam(args, 0, 1)));
                break;
            case 'S': // SU - scroll up
                _buffer.ScrollUp(Math.Max(1, GetParam(args, 0, 1)));
                break;
            case 'T': // SD - scroll down
                _buffer.ScrollDown(Math.Max(1, GetParam(args, 0, 1)));
                break;
            case 'd': // VPA - vertical line position absolute
                _buffer.SetCursorPosition(GetParam(args, 0, 1) - 1, _buffer.CursorCol);
                break;
            case 'r': // DECSTBM - set scroll region
                int top = GetParam(args, 0, 1);
                int bottom = GetParam(args, 1, _buffer.Rows);
                _buffer.SetScrollRegion(top, bottom);
                break;
            case 's': // SCP - save cursor position
                _buffer.SaveCursor();
                break;
            case 'u': // RCP - restore cursor position
                _buffer.RestoreCursor();
                break;
            case 'm': // SGR - select graphic rendition
                HandleSgr(args);
                break;
            case 'n': // DSR - device status report
                // Would need to send response to process - tracked but not implemented here
                break;
            case 'c': // DA - device attributes
                // Would need to send response - not implemented
                break;
            case 't': // Window manipulation (xterm)
                // Mostly ignored
                break;
        }
    }

    #endregion

    #region DEC Private Modes

    private void DispatchDecPrivateMode(char final, int[] args)
    {
        int mode = GetParam(args, 0, 0);

        switch (final)
        {
            case 'h': // DECSET - set mode
                SetDecMode(mode, true);
                break;
            case 'l': // DECRST - reset mode
                SetDecMode(mode, false);
                break;
        }
    }

    private void SetDecMode(int mode, bool enabled)
    {
        switch (mode)
        {
            case 1: // DECCKM - application cursor keys
                ApplicationCursorKeys = enabled;
                break;
            case 7: // DECAWM - auto-wrap mode
                // Auto-wrap is always on in our implementation
                break;
            case 12: // Cursor blink (att610)
                // Handled in the control's rendering
                break;
            case 25: // DECTCEM - text cursor enable
                CursorVisible = enabled;
                break;
            case 47: // Alternate screen buffer (older xterm)
                if (enabled) _buffer.EnableAlternateScreen();
                else _buffer.DisableAlternateScreen();
                break;
            case 1000: // Mouse tracking - not implemented
            case 1002: // Button-event mouse tracking
            case 1003: // Any-event mouse tracking
            case 1006: // SGR mouse mode
                break;
            case 1049: // Alternate screen buffer + save/restore cursor (xterm)
                if (enabled)
                {
                    _buffer.SaveCursor();
                    _buffer.EnableAlternateScreen();
                }
                else
                {
                    _buffer.DisableAlternateScreen();
                    _buffer.RestoreCursor();
                }
                break;
            case 2004: // Bracketed paste mode
                BracketedPasteMode = enabled;
                break;
        }
    }

    #endregion

    #region SGR (Select Graphic Rendition)

    private void HandleSgr(int[] args)
    {
        if (args.Length == 0)
        {
            _buffer.ResetAttributes();
            return;
        }

        for (int i = 0; i < args.Length; i++)
        {
            int code = args[i];

            switch (code)
            {
                case 0: // Reset
                    _buffer.ResetAttributes();
                    break;
                case 1: // Bold
                    _buffer.SetAttribute(CharAttributes.Bold);
                    break;
                case 2: // Dim
                    _buffer.SetAttribute(CharAttributes.Dim);
                    break;
                case 3: // Italic
                    _buffer.SetAttribute(CharAttributes.Italic);
                    break;
                case 4: // Underline
                    _buffer.SetAttribute(CharAttributes.Underline);
                    break;
                case 5: // Blink
                    _buffer.SetAttribute(CharAttributes.Blink);
                    break;
                case 7: // Inverse
                    _buffer.SetAttribute(CharAttributes.Inverse);
                    break;
                case 8: // Hidden
                    _buffer.SetAttribute(CharAttributes.Hidden);
                    break;
                case 9: // Strikethrough
                    _buffer.SetAttribute(CharAttributes.Strikethrough);
                    break;
                case 21: // Double underline (treated as underline)
                    _buffer.SetAttribute(CharAttributes.Underline);
                    break;
                case 22: // Normal intensity (not bold, not dim)
                    _buffer.ClearAttribute(CharAttributes.Bold);
                    _buffer.ClearAttribute(CharAttributes.Dim);
                    break;
                case 23: // Not italic
                    _buffer.ClearAttribute(CharAttributes.Italic);
                    break;
                case 24: // Not underlined
                    _buffer.ClearAttribute(CharAttributes.Underline);
                    break;
                case 25: // Not blinking
                    _buffer.ClearAttribute(CharAttributes.Blink);
                    break;
                case 27: // Not inverse
                    _buffer.ClearAttribute(CharAttributes.Inverse);
                    break;
                case 28: // Not hidden
                    _buffer.ClearAttribute(CharAttributes.Hidden);
                    break;
                case 29: // Not strikethrough
                    _buffer.ClearAttribute(CharAttributes.Strikethrough);
                    break;

                // Standard foreground colors (30-37)
                case >= 30 and <= 37:
                    _buffer.SetForegroundIndex(code - 30);
                    break;
                case 38: // Extended foreground color
                    i = HandleExtendedColor(args, i, isForeground: true);
                    break;
                case 39: // Default foreground
                    _buffer.ResetForeground();
                    break;

                // Standard background colors (40-47)
                case >= 40 and <= 47:
                    _buffer.SetBackgroundIndex(code - 40);
                    break;
                case 48: // Extended background color
                    i = HandleExtendedColor(args, i, isForeground: false);
                    break;
                case 49: // Default background
                    _buffer.ResetBackground();
                    break;

                // Bright foreground colors (90-97)
                case >= 90 and <= 97:
                    _buffer.SetForegroundIndex(code - 90 + 8);
                    break;

                // Bright background colors (100-107)
                case >= 100 and <= 107:
                    _buffer.SetBackgroundIndex(code - 100 + 8);
                    break;
            }
        }
    }

    /// <summary>
    /// Handles extended color sequences: 5;n (256-color) and 2;r;g;b (true-color).
    /// Returns the updated parameter index.
    /// </summary>
    private int HandleExtendedColor(int[] args, int i, bool isForeground)
    {
        if (i + 1 >= args.Length) return i;

        int type = args[i + 1];

        if (type == 5 && i + 2 < args.Length)
        {
            // 256-color: 38;5;n or 48;5;n
            int colorIndex = Math.Clamp(args[i + 2], 0, 255);
            if (isForeground)
                _buffer.SetForegroundIndex(colorIndex);
            else
                _buffer.SetBackgroundIndex(colorIndex);
            return i + 2;
        }

        if (type == 2 && i + 4 < args.Length)
        {
            // True-color: 38;2;r;g;b or 48;2;r;g;b
            byte r = (byte)Math.Clamp(args[i + 2], 0, 255);
            byte g = (byte)Math.Clamp(args[i + 3], 0, 255);
            byte b = (byte)Math.Clamp(args[i + 4], 0, 255);
            if (isForeground)
                _buffer.SetForegroundRgb(r, g, b);
            else
                _buffer.SetBackgroundRgb(r, g, b);
            return i + 4;
        }

        return i + 1;
    }

    #endregion

    #region OSC Handling

    private void HandleOsc(char c)
    {
        if (c == '\a') // BEL terminates OSC
        {
            DispatchOsc();
            _state = State.Ground;
        }
        else if (c == '\x1b') // ESC may start ST (ESC \)
        {
            // Peek ahead - for simplicity, just dispatch
            DispatchOsc();
            _state = State.Escape;
        }
        else
        {
            _oscString.Append(c);
        }
    }

    private void DispatchOsc()
    {
        string osc = _oscString.ToString();
        int semiIndex = osc.IndexOf(';');
        if (semiIndex < 0) return;

        if (!int.TryParse(osc.AsSpan(0, semiIndex), out int command)) return;
        string param = osc[(semiIndex + 1)..];

        switch (command)
        {
            case 0: // Set icon name + window title
            case 2: // Set window title
                TitleChanged?.Invoke(param);
                break;
            case 1: // Set icon name (ignored)
                break;
            // Other OSC commands (clipboard, hyperlinks, etc.) can be added later
        }
    }

    #endregion

    #region Parameter Helpers

    private static int[] ParseParams(string paramStr)
    {
        if (string.IsNullOrEmpty(paramStr))
            return Array.Empty<int>();

        string[] parts = paramStr.Split(';');
        int[] result = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out result[i]))
                result[i] = 0;
        }
        return result;
    }

    private static int GetParam(int[] args, int index, int defaultValue)
    {
        if (index >= args.Length || args[index] == 0)
            return defaultValue;
        return args[index];
    }

    #endregion
}
