using System.Globalization;
using System.Reflection;
using System.Text;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

public partial class DevToolsWindow
{
    private Terminal? _replTerminal;
    private Border? _replFocusHost;
    private readonly StringBuilder _replLineBuffer = new();
    private readonly Dictionary<string, object?> _replLocals = new(StringComparer.Ordinal);
    private readonly List<string> _replHistory = new();
    private int _replHistoryCursor = -1;
    private bool _replGreeted;

    private const string ReplPrompt = "devtools> ";

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools REPL evaluates user input via reflection-based runtime member resolution.")]
    private UIElement BuildReplTab()
    {
        _replTerminal = new Terminal
        {
            AutoStartShell = false,
            IsReadOnly = true,                   // Terminal never processes typed input directly.
            TerminalColumns = 120,
            TerminalRows = 40,
            Focusable = false,                   // Focus stays on the host Border so we can own input.
            Background = new SolidColorBrush(DevToolsTheme.ChromeColor),
            Foreground = new SolidColorBrush(DevToolsTheme.TextPrimaryColor),
            FontFamily = DevToolsTheme.MonoFont,
        };

        _replFocusHost = new Border
        {
            Background = new SolidColorBrush(DevToolsTheme.ChromeColor),
            BorderBrush = new SolidColorBrush(DevToolsTheme.BorderColor),
            BorderThickness = new Thickness(1),
            Focusable = true,
            Child = _replTerminal,
            ClipToBounds = true,
        };

        // Bubble KeyDown/TextInput fire first on the focused element — that's the host Border,
        // not the Terminal. So we receive input here before the Terminal (which in read-only
        // mode would simply ignore character input anyway).
        _replFocusHost.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnReplKeyDown));
        _replFocusHost.AddHandler(UIElement.TextInputEvent, new TextCompositionEventHandler(OnReplTextInput));
        _replFocusHost.MouseDown += (_, _) => _replFocusHost.Focus();

        return MakeTabShell(_replFocusHost);
    }

    partial void OnReplTabActivated()
    {
        EnsureReplGreeting();
        _replLocals["window"] = _targetWindow;
        try { _replLocals["app"] = Application.Current; } catch { }
        _replLocals["root"] = _targetWindow;
        _replFocusHost?.Focus();
    }

    private void EnsureReplGreeting()
    {
        if (_replGreeted || _replTerminal == null) return;
        _replGreeted = true;
        _replTerminal.Write("\x1b[1;33mJalium.UI DevTools REPL\x1b[0m\r\n");
        _replTerminal.Write("Type expressions. ");
        _replTerminal.Write("\x1b[2m$ = selected visual, window, app, root; let name = expr; name.Prop = val;\x1b[0m\r\n");
        _replTerminal.Write("\x1b[2mEnter to run, Backspace to delete, Up/Down history, Ctrl+L clears, Ctrl+C cancels.\x1b[0m\r\n\r\n");
        WritePrompt();
    }

    private void WritePrompt()
    {
        _replTerminal?.Write($"\x1b[1;32m{ReplPrompt}\x1b[0m");
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools REPL evaluates user input via reflection-based runtime member resolution.")]
    private void OnReplKeyDown(object? sender, RoutedEventArgs e)
    {
        if (_replTerminal == null || e is not KeyEventArgs ke) return;

        var mods = ke.KeyboardModifiers;
        bool ctrl = (mods & ModifierKeys.Control) != 0;

        switch (ke.Key)
        {
            case Key.Enter:
                _replTerminal.Write("\r\n");
                RunReplLine(_replLineBuffer.ToString());
                _replLineBuffer.Clear();
                WritePrompt();
                ke.Handled = true;
                return;

            case Key.Back:
                if (_replLineBuffer.Length > 0)
                {
                    _replLineBuffer.Length--;
                    _replTerminal.Write("\b \b");
                }
                ke.Handled = true;
                return;

            case Key.Escape:
                CancelCurrentLine();
                ke.Handled = true;
                return;

            case Key.L when ctrl:
                _replTerminal.Clear();
                _replTerminal.Write("\x1b[1;33mJalium.UI DevTools REPL\x1b[0m\r\n\r\n");
                WritePrompt();
                // Re-echo any partial line the user had typed before Ctrl+L
                if (_replLineBuffer.Length > 0)
                    _replTerminal.Write(_replLineBuffer.ToString());
                ke.Handled = true;
                return;

            case Key.C when ctrl:
                CancelCurrentLine();
                ke.Handled = true;
                return;

            case Key.Up:
                ShowHistory(-1);
                ke.Handled = true;
                return;

            case Key.Down:
                ShowHistory(+1);
                ke.Handled = true;
                return;
        }
    }

    private void OnReplTextInput(object? sender, RoutedEventArgs e)
    {
        if (_replTerminal == null || e is not TextCompositionEventArgs te) return;
        var text = te.Text;
        if (string.IsNullOrEmpty(text)) return;

        // Skip control characters — Enter/Backspace/Escape are handled in KeyDown.
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (ch < 0x20 || ch == 0x7f) continue;
            sb.Append(ch);
        }
        if (sb.Length == 0) { te.Handled = true; return; }

        var printable = sb.ToString();
        _replLineBuffer.Append(printable);
        _replTerminal.Write(printable);
        te.Handled = true;
    }

    private void CancelCurrentLine()
    {
        if (_replTerminal == null) return;
        _replLineBuffer.Clear();
        _replTerminal.Write("^C\r\n");
        WritePrompt();
    }

    private void ShowHistory(int direction)
    {
        if (_replHistory.Count == 0 || _replTerminal == null) return;

        int newCursor;
        if (direction < 0)
        {
            if (_replHistoryCursor < 0) newCursor = _replHistory.Count - 1;
            else newCursor = Math.Max(0, _replHistoryCursor - 1);
        }
        else
        {
            if (_replHistoryCursor < 0) return;
            newCursor = _replHistoryCursor + 1;
            if (newCursor >= _replHistory.Count) newCursor = -1;
        }

        // Clear existing input visually
        for (int i = 0; i < _replLineBuffer.Length; i++)
            _replTerminal.Write("\b \b");
        _replLineBuffer.Clear();

        _replHistoryCursor = newCursor;
        if (newCursor >= 0 && newCursor < _replHistory.Count)
        {
            var cmd = _replHistory[newCursor];
            _replLineBuffer.Append(cmd);
            _replTerminal.Write(cmd);
        }
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools REPL evaluates expressions and assigns to runtime-resolved members via reflection.")]
    private void RunReplLine(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || _replTerminal == null) return;

        _replHistory.Add(code);
        _replHistoryCursor = -1;
        _replLocals["$"] = _selectedVisual;

        try
        {
            var tokens = ReplTokenizer.Tokenize(code);
            int pos = 0;
            while (pos < tokens.Count)
            {
                var stmtTokens = new List<ReplTokenizer.Token>();
                while (pos < tokens.Count && tokens[pos].Kind != ReplTokenizer.TokenKind.Semicolon)
                {
                    stmtTokens.Add(tokens[pos]);
                    pos++;
                }
                if (pos < tokens.Count) pos++;
                if (stmtTokens.Count == 0) continue;
                try
                {
                    var result = EvaluateStatement(stmtTokens);
                    _replTerminal.Write($"\x1b[1;32m=> \x1b[0m\x1b[96m{FormatReplValue(result)}\x1b[0m\r\n");
                }
                catch (Exception ex)
                {
                    _replTerminal.Write($"\x1b[1;31mE: {ex.Message}\x1b[0m\r\n");
                }
            }
        }
        catch (Exception ex)
        {
            _replTerminal.Write($"\x1b[1;31mparse error: {ex.Message}\x1b[0m\r\n");
        }
    }

    private static string FormatReplValue(object? v)
    {
        if (v == null) return "null";
        if (v is string s) return $"\"{s}\"";
        return v.ToString() ?? "<?>";
    }

    // ── Tiny expression interpreter ──────────────────────────────────────

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools REPL evaluates expressions and assigns to runtime-resolved members via reflection.")]
    private object? EvaluateStatement(List<ReplTokenizer.Token> tokens)
    {
        if (tokens.Count >= 4 && tokens[0].Kind == ReplTokenizer.TokenKind.Identifier && tokens[0].Text == "let"
            && tokens[1].Kind == ReplTokenizer.TokenKind.Identifier
            && tokens[2].Kind == ReplTokenizer.TokenKind.Assign)
        {
            var name = tokens[1].Text;
            var value = EvaluateExpression(tokens, 3, tokens.Count);
            _replLocals[name] = value;
            return value;
        }

        int assignIndex = FindTopLevelAssign(tokens);
        if (assignIndex > 0)
        {
            var value = EvaluateExpression(tokens, assignIndex + 1, tokens.Count);
            AssignTo(tokens, assignIndex, value);
            return value;
        }

        return EvaluateExpression(tokens, 0, tokens.Count);
    }

    private static int FindTopLevelAssign(List<ReplTokenizer.Token> tokens)
    {
        int depth = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            switch (tokens[i].Kind)
            {
                case ReplTokenizer.TokenKind.LParen: depth++; break;
                case ReplTokenizer.TokenKind.RParen: depth--; break;
                case ReplTokenizer.TokenKind.Assign:
                    if (depth == 0) return i;
                    break;
            }
        }
        return -1;
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools REPL assigns to runtime-resolved properties/fields via reflection.")]
    private void AssignTo(List<ReplTokenizer.Token> tokens, int assignIndex, object? value)
    {
        if (assignIndex < 1) throw new InvalidOperationException("Nothing to assign to");
        if (tokens[assignIndex - 1].Kind != ReplTokenizer.TokenKind.Identifier)
            throw new InvalidOperationException("Last token before '=' must be an identifier");
        string lastName = tokens[assignIndex - 1].Text;

        object? host;
        if (assignIndex == 1)
        {
            if (!_replLocals.ContainsKey(lastName))
                throw new InvalidOperationException($"Unknown variable '{lastName}'");
            _replLocals[lastName] = value;
            return;
        }
        if (tokens[assignIndex - 2].Kind != ReplTokenizer.TokenKind.Dot)
            throw new InvalidOperationException("Expected '.' before final member");

        host = EvaluateExpression(tokens, 0, assignIndex - 2);
        if (host == null) throw new InvalidOperationException("Cannot assign member on null");

        var type = host.GetType();
        var prop = type.GetProperty(lastName, BindingFlags.Instance | BindingFlags.Public);
        if (prop != null)
        {
            prop.SetValue(host, CoerceValue(value, prop.PropertyType));
            return;
        }
        var field = type.GetField(lastName, BindingFlags.Instance | BindingFlags.Public);
        if (field != null)
        {
            field.SetValue(host, CoerceValue(value, field.FieldType));
            return;
        }
        throw new InvalidOperationException($"No writable member '{lastName}' on {type.Name}");
    }

    private static object? CoerceValue(object? value, Type targetType)
    {
        if (value == null) return null;
        if (targetType.IsInstanceOfType(value)) return value;
        if (targetType == typeof(string)) return value.ToString();
        try { return System.Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture); }
        catch { return value; }
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools REPL evaluates expressions via reflection-based runtime member resolution.")]
    private object? EvaluateExpression(List<ReplTokenizer.Token> tokens, int start, int end)
    {
        if (start >= end) throw new InvalidOperationException("Empty expression");
        object? value = EvaluatePrimary(tokens, ref start);
        while (start < end)
        {
            if (tokens[start].Kind == ReplTokenizer.TokenKind.Dot)
            {
                start++;
                if (start >= end || tokens[start].Kind != ReplTokenizer.TokenKind.Identifier)
                    throw new InvalidOperationException("Expected identifier after '.'");
                string memberName = tokens[start].Text;
                start++;
                value = ReadOrInvokeMember(value, memberName, tokens, ref start, end);
            }
            else
            {
                break;
            }
        }
        return value;
    }

    private object? EvaluatePrimary(List<ReplTokenizer.Token> tokens, ref int pos)
    {
        var tok = tokens[pos];
        switch (tok.Kind)
        {
            case ReplTokenizer.TokenKind.NumberLiteral:
                pos++;
                if (tok.Text.Contains('.')) return double.Parse(tok.Text, CultureInfo.InvariantCulture);
                if (int.TryParse(tok.Text, out var i)) return i;
                return long.Parse(tok.Text, CultureInfo.InvariantCulture);
            case ReplTokenizer.TokenKind.StringLiteral:
                pos++;
                return tok.Text;
            case ReplTokenizer.TokenKind.Identifier:
                pos++;
                switch (tok.Text)
                {
                    case "true": return true;
                    case "false": return false;
                    case "null": return null;
                }
                if (_replLocals.TryGetValue(tok.Text, out var v)) return v;
                throw new InvalidOperationException($"Unknown identifier '{tok.Text}'");
            case ReplTokenizer.TokenKind.Dollar:
                pos++;
                return _selectedVisual;
            default:
                throw new InvalidOperationException($"Unexpected token '{tok.Text}'");
        }
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("DevTools REPL reads/invokes runtime-resolved members via reflection.")]
    private object? ReadOrInvokeMember(object? target, string memberName, List<ReplTokenizer.Token> tokens, ref int pos, int end)
    {
        if (target == null)
            throw new InvalidOperationException($"Cannot access '{memberName}' on null");
        bool isCall = pos < end && tokens[pos].Kind == ReplTokenizer.TokenKind.LParen;
        var type = target.GetType();

        if (isCall)
        {
            pos++;
            var args = new List<object?>();
            while (pos < end && tokens[pos].Kind != ReplTokenizer.TokenKind.RParen)
            {
                int argEnd = FindArgEnd(tokens, pos, end);
                args.Add(EvaluateExpression(tokens, pos, argEnd));
                pos = argEnd;
                if (pos < end && tokens[pos].Kind == ReplTokenizer.TokenKind.Comma) pos++;
            }
            if (pos < end && tokens[pos].Kind == ReplTokenizer.TokenKind.RParen) pos++;
            var method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                             .FirstOrDefault(m => m.Name == memberName && m.GetParameters().Length == args.Count);
            if (method == null)
                throw new InvalidOperationException($"No method '{memberName}' on {type.Name} matching {args.Count} args");
            var parms = method.GetParameters();
            for (int k = 0; k < args.Count; k++)
                args[k] = CoerceValue(args[k], parms[k].ParameterType);
            return method.Invoke(target, args.ToArray());
        }
        else
        {
            var prop = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public);
            if (prop != null) return prop.GetValue(target);
            var field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public);
            if (field != null) return field.GetValue(target);
            throw new InvalidOperationException($"No member '{memberName}' on {type.Name}");
        }
    }

    private static int FindArgEnd(List<ReplTokenizer.Token> tokens, int start, int end)
    {
        int depth = 0;
        for (int i = start; i < end; i++)
        {
            if (tokens[i].Kind == ReplTokenizer.TokenKind.LParen) depth++;
            else if (tokens[i].Kind == ReplTokenizer.TokenKind.RParen) { if (depth == 0) return i; depth--; }
            else if (tokens[i].Kind == ReplTokenizer.TokenKind.Comma && depth == 0) return i;
        }
        return end;
    }
}

internal static class ReplTokenizer
{
    internal enum TokenKind
    {
        Identifier, NumberLiteral, StringLiteral,
        Dot, LParen, RParen, Comma, Semicolon, Assign, Dollar,
    }

    internal readonly struct Token
    {
        public Token(TokenKind kind, string text) { Kind = kind; Text = text; }
        public TokenKind Kind { get; }
        public string Text { get; }
    }

    internal static List<Token> Tokenize(string code)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < code.Length)
        {
            char c = code[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '$') { tokens.Add(new Token(TokenKind.Dollar, "$")); i++; continue; }
            if (c == '.') { tokens.Add(new Token(TokenKind.Dot, ".")); i++; continue; }
            if (c == '(') { tokens.Add(new Token(TokenKind.LParen, "(")); i++; continue; }
            if (c == ')') { tokens.Add(new Token(TokenKind.RParen, ")")); i++; continue; }
            if (c == ',') { tokens.Add(new Token(TokenKind.Comma, ",")); i++; continue; }
            if (c == ';') { tokens.Add(new Token(TokenKind.Semicolon, ";")); i++; continue; }
            if (c == '=')
            {
                tokens.Add(new Token(TokenKind.Assign, "="));
                i++;
                continue;
            }
            if (c == '"')
            {
                int end = i + 1;
                var sb = new StringBuilder();
                while (end < code.Length && code[end] != '"')
                {
                    if (code[end] == '\\' && end + 1 < code.Length)
                    {
                        sb.Append(code[end + 1] switch
                        {
                            'n' => '\n',
                            'r' => '\r',
                            't' => '\t',
                            '"' => '"',
                            '\\' => '\\',
                            _ => code[end + 1],
                        });
                        end += 2;
                    }
                    else
                    {
                        sb.Append(code[end]);
                        end++;
                    }
                }
                if (end >= code.Length) throw new InvalidOperationException("Unterminated string literal");
                tokens.Add(new Token(TokenKind.StringLiteral, sb.ToString()));
                i = end + 1;
                continue;
            }
            if (char.IsDigit(c) || (c == '-' && i + 1 < code.Length && char.IsDigit(code[i + 1])))
            {
                int end = i + 1;
                while (end < code.Length && (char.IsDigit(code[end]) || code[end] == '.')) end++;
                tokens.Add(new Token(TokenKind.NumberLiteral, code.Substring(i, end - i)));
                i = end;
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int end = i + 1;
                while (end < code.Length && (char.IsLetterOrDigit(code[end]) || code[end] == '_')) end++;
                tokens.Add(new Token(TokenKind.Identifier, code.Substring(i, end - i)));
                i = end;
                continue;
            }
            throw new InvalidOperationException($"Unexpected character '{c}' at position {i}");
        }
        return tokens;
    }
}
