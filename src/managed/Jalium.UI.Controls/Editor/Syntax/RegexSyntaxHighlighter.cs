using System.Text.RegularExpressions;

namespace Jalium.UI.Controls.Editor;

/// <summary>
/// A rule that matches a regex pattern and assigns a token classification.
/// </summary>
public sealed class HighlightingRule
{
    public Regex Pattern { get; }
    public TokenClassification Classification { get; }

    public HighlightingRule(string pattern, TokenClassification classification, RegexOptions options = RegexOptions.None)
    {
        Pattern = new Regex(pattern, options | RegexOptions.Compiled);
        Classification = classification;
    }

    public HighlightingRule(Regex pattern, TokenClassification classification)
    {
        Pattern = pattern;
        Classification = classification;
    }
}

/// <summary>
/// A rule for multi-line spans (e.g., block comments, multi-line strings).
/// </summary>
public sealed class SpanRule
{
    public Regex StartPattern { get; }
    public Regex EndPattern { get; }
    public TokenClassification Classification { get; }

    public SpanRule(string startPattern, string endPattern, TokenClassification classification)
    {
        StartPattern = new Regex(startPattern, RegexOptions.Compiled);
        EndPattern = new Regex(endPattern, RegexOptions.Compiled);
        Classification = classification;
    }
}

/// <summary>
/// State object for tracking multi-line spans across lines.
/// </summary>
internal sealed class HighlighterState
{
    /// <summary>
    /// The currently active span rule (null if not inside a multi-line span).
    /// </summary>
    public SpanRule? ActiveSpan { get; init; }

    public static readonly HighlighterState Default = new() { ActiveSpan = null };

    public override bool Equals(object? obj) =>
        obj is HighlighterState other && ReferenceEquals(ActiveSpan, other.ActiveSpan);

    public override int GetHashCode() => ActiveSpan?.GetHashCode() ?? 0;
}

/// <summary>
/// Regex-based syntax highlighter with support for single-line rules and multi-line span rules.
/// </summary>
public sealed class RegexSyntaxHighlighter : ISyntaxHighlighter
{
    private readonly List<HighlightingRule> _rules = [];
    private readonly List<SpanRule> _spanRules = [];

    /// <summary>
    /// Gets the list of single-line highlighting rules.
    /// Rules are applied in order; first match wins for any character position.
    /// </summary>
    public IList<HighlightingRule> Rules => _rules;

    /// <summary>
    /// Gets the list of multi-line span rules (e.g., block comments).
    /// </summary>
    public IList<SpanRule> SpanRules => _spanRules;

    public object? GetInitialState() => HighlighterState.Default;

    public (SyntaxToken[] tokens, object? stateAtLineEnd) HighlightLine(
        int lineNumber, string lineText, object? stateAtLineStart)
    {
        var state = stateAtLineStart as HighlighterState ?? HighlighterState.Default;
        var tokens = new List<SyntaxToken>();
        var covered = new bool[lineText.Length];
        SpanRule? activeSpanAtEnd = state.ActiveSpan;

        int scanStart = 0;

        // If we're inside a multi-line span from a previous line, continue it
        if (state.ActiveSpan != null)
        {
            var endMatch = state.ActiveSpan.EndPattern.Match(lineText);
            if (endMatch.Success)
            {
                int spanEnd = endMatch.Index + endMatch.Length;
                tokens.Add(new SyntaxToken(0, spanEnd, state.ActiveSpan.Classification));
                MarkCovered(covered, 0, spanEnd);
                scanStart = spanEnd;
                activeSpanAtEnd = null;
            }
            else
            {
                // Entire line is inside the span
                tokens.Add(new SyntaxToken(0, lineText.Length, state.ActiveSpan.Classification));
                return (tokens.ToArray(), new HighlighterState { ActiveSpan = state.ActiveSpan });
            }
        }

        // Scan for span starts
        for (int pos = scanStart; pos < lineText.Length; pos++)
        {
            if (covered[pos]) continue;

            bool matchedSpan = false;
            foreach (var spanRule in _spanRules)
            {
                var startMatch = spanRule.StartPattern.Match(lineText, pos);
                if (startMatch.Success && startMatch.Index == pos)
                {
                    // Check if span ends on same line
                    int afterStart = startMatch.Index + startMatch.Length;
                    var endMatch = spanRule.EndPattern.Match(lineText, afterStart);
                    if (endMatch.Success)
                    {
                        int spanEnd = endMatch.Index + endMatch.Length;
                        tokens.Add(new SyntaxToken(pos, spanEnd - pos, spanRule.Classification));
                        MarkCovered(covered, pos, spanEnd);
                        pos = spanEnd - 1;
                    }
                    else
                    {
                        // Span continues to next line
                        tokens.Add(new SyntaxToken(pos, lineText.Length - pos, spanRule.Classification));
                        MarkCovered(covered, pos, lineText.Length);
                        activeSpanAtEnd = spanRule;
                        pos = lineText.Length;
                    }
                    matchedSpan = true;
                    break;
                }
            }

            if (matchedSpan) continue;

            // Try single-line rules at this position
            foreach (var rule in _rules)
            {
                var match = rule.Pattern.Match(lineText, pos);
                if (match.Success && match.Index == pos && match.Length > 0)
                {
                    if (!IsAnyCovered(covered, match.Index, match.Index + match.Length))
                    {
                        tokens.Add(new SyntaxToken(match.Index, match.Length, rule.Classification));
                        MarkCovered(covered, match.Index, match.Index + match.Length);
                        pos = match.Index + match.Length - 1;
                    }
                    break;
                }
            }
        }

        // Fill uncovered ranges with PlainText
        // Sort tokens by position first
        tokens.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));

        var result = new List<SyntaxToken>();
        int lastEnd = 0;
        foreach (var token in tokens)
        {
            if (token.StartOffset > lastEnd)
            {
                result.Add(new SyntaxToken(lastEnd, token.StartOffset - lastEnd, TokenClassification.PlainText));
            }
            result.Add(token);
            lastEnd = token.StartOffset + token.Length;
        }
        if (lastEnd < lineText.Length)
        {
            result.Add(new SyntaxToken(lastEnd, lineText.Length - lastEnd, TokenClassification.PlainText));
        }

        var endState = new HighlighterState { ActiveSpan = activeSpanAtEnd };
        return (result.ToArray(), endState);
    }

    private static void MarkCovered(bool[] covered, int start, int end)
    {
        for (int i = start; i < end && i < covered.Length; i++)
            covered[i] = true;
    }

    private static bool IsAnyCovered(bool[] covered, int start, int end)
    {
        for (int i = start; i < end && i < covered.Length; i++)
            if (covered[i]) return true;
        return false;
    }

    /// <summary>
    /// Creates a C# syntax highlighter.
    /// </summary>
    public static RegexSyntaxHighlighter CreateCSharpHighlighter()
    {
        var h = new RegexSyntaxHighlighter();
        h.SpanRules.Add(new SpanRule(@"/\*", @"\*/", TokenClassification.Comment));
        h.Rules.Add(new HighlightingRule(@"///.*$", TokenClassification.XmlDoc, RegexOptions.Multiline));
        h.Rules.Add(new HighlightingRule(@"//.*$", TokenClassification.Comment, RegexOptions.Multiline));
        h.Rules.Add(new HighlightingRule(@"#\w+", TokenClassification.Preprocessor));
        h.Rules.Add(new HighlightingRule(@"@""(?:[^""]|"""")*""", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"\$""(?:[^""\\]|\\.)*""", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"""(?:[^""\\]|\\.)*""", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"'(?:[^'\\]|\\.)'", TokenClassification.Character));
        h.Rules.Add(new HighlightingRule(
            @"\b(if|else|for|foreach|while|do|switch|case|default|break|continue|return|throw|try|catch|finally|goto|yield)\b",
            TokenClassification.ControlKeyword));
        h.Rules.Add(new HighlightingRule(
            @"\b(abstract|async|await|base|bool|byte|char|checked|class|const|decimal|delegate|double|enum|event|explicit|extern|false|fixed|float|implicit|in|int|interface|internal|is|lock|long|namespace|new|null|object|operator|out|override|params|partial|private|protected|public|readonly|record|ref|sbyte|sealed|short|sizeof|stackalloc|static|string|struct|this|true|typeof|uint|ulong|unchecked|unsafe|ushort|using|var|virtual|void|volatile|where|when)\b",
            TokenClassification.Keyword));
        h.Rules.Add(new HighlightingRule(@"\b\d+(\.\d+)?[fFdDmMlLuU]?\b", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0x[0-9a-fA-F]+[lLuU]?", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"\[[\w]+\]", TokenClassification.Attribute));
        h.Rules.Add(new HighlightingRule(@"[+\-*/%=!<>&|^~?:]", TokenClassification.Operator));
        h.Rules.Add(new HighlightingRule(@"[{}()\[\];,.]", TokenClassification.Punctuation));
        return h;
    }

    /// <summary>
    /// Creates a C/C++ syntax highlighter.
    /// </summary>
    public static RegexSyntaxHighlighter CreateCppHighlighter()
    {
        var h = new RegexSyntaxHighlighter();
        h.SpanRules.Add(new SpanRule(@"/\*", @"\*/", TokenClassification.Comment));
        h.Rules.Add(new HighlightingRule(@"//.*$", TokenClassification.Comment, RegexOptions.Multiline));
        h.Rules.Add(new HighlightingRule(@"#\s*\w+", TokenClassification.Preprocessor));
        h.Rules.Add(new HighlightingRule(@"R""(\w*)\(", TokenClassification.String)); // raw string start
        h.Rules.Add(new HighlightingRule(@"""(?:[^""\\]|\\.)*""", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"'(?:[^'\\]|\\.)'", TokenClassification.Character));
        h.Rules.Add(new HighlightingRule(
            @"\b(if|else|for|while|do|switch|case|default|break|continue|return|throw|try|catch|goto)\b",
            TokenClassification.ControlKeyword));
        h.Rules.Add(new HighlightingRule(
            @"\b(alignas|alignof|and|and_eq|asm|auto|bitand|bitor|bool|char|char8_t|char16_t|char32_t|class|co_await|co_return|co_yield|compl|concept|const|consteval|constexpr|constinit|const_cast|decltype|delete|double|dynamic_cast|enum|explicit|export|extern|false|float|friend|import|inline|int|long|module|mutable|namespace|new|noexcept|not|not_eq|nullptr|operator|or|or_eq|override|private|protected|public|register|reinterpret_cast|requires|short|signed|sizeof|static|static_assert|static_cast|struct|template|this|thread_local|true|typedef|typeid|typename|union|unsigned|using|virtual|void|volatile|wchar_t|xor|xor_eq|final)\b",
            TokenClassification.Keyword));
        h.Rules.Add(new HighlightingRule(@"\b\d+(\.\d+)?([eE][+-]?\d+)?[fFlLuU]*\b", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0[xX][0-9a-fA-F]+[uUlL]*", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0[bB][01]+[uUlL]*", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"[+\-*/%=!<>&|^~?:]", TokenClassification.Operator));
        h.Rules.Add(new HighlightingRule(@"[{}()\[\];,.]", TokenClassification.Punctuation));
        return h;
    }

    /// <summary>
    /// Creates a JavaScript/TypeScript syntax highlighter.
    /// </summary>
    public static RegexSyntaxHighlighter CreateJavaScriptHighlighter()
    {
        var h = new RegexSyntaxHighlighter();
        h.SpanRules.Add(new SpanRule(@"/\*", @"\*/", TokenClassification.Comment));
        h.Rules.Add(new HighlightingRule(@"//.*$", TokenClassification.Comment, RegexOptions.Multiline));
        h.Rules.Add(new HighlightingRule(@"`(?:[^`\\]|\\.)*`", TokenClassification.String)); // template literals
        h.Rules.Add(new HighlightingRule(@"""(?:[^""\\]|\\.)*""", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"'(?:[^'\\]|\\.)*'", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(
            @"\b(if|else|for|while|do|switch|case|default|break|continue|return|throw|try|catch|finally|yield)\b",
            TokenClassification.ControlKeyword));
        h.Rules.Add(new HighlightingRule(
            @"\b(abstract|arguments|async|await|class|const|constructor|debugger|delete|enum|export|extends|false|function|get|implements|import|in|instanceof|interface|let|module|new|null|of|package|private|protected|public|readonly|require|set|static|super|this|true|type|typeof|undefined|var|void|with)\b",
            TokenClassification.Keyword));
        // TypeScript-specific
        h.Rules.Add(new HighlightingRule(
            @"\b(any|boolean|bigint|declare|infer|is|keyof|never|number|object|string|symbol|unknown)\b",
            TokenClassification.TypeName));
        h.Rules.Add(new HighlightingRule(@"@\w+", TokenClassification.Attribute)); // decorators
        h.Rules.Add(new HighlightingRule(@"\b\d+(\.\d+)?([eE][+-]?\d+)?\b", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0[xX][0-9a-fA-F]+", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0[bB][01]+", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0[oO][0-7]+", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"=>", TokenClassification.Operator));
        h.Rules.Add(new HighlightingRule(@"[+\-*/%=!<>&|^~?:]", TokenClassification.Operator));
        h.Rules.Add(new HighlightingRule(@"[{}()\[\];,.]", TokenClassification.Punctuation));
        return h;
    }

    /// <summary>
    /// Creates a JSON syntax highlighter.
    /// </summary>
    public static RegexSyntaxHighlighter CreateJsonHighlighter()
    {
        var h = new RegexSyntaxHighlighter();
        // JSON property keys (before colon)
        h.Rules.Add(new HighlightingRule(@"""(?:[^""\\]|\\.)*""\s*(?=:)", TokenClassification.Property));
        // JSON string values
        h.Rules.Add(new HighlightingRule(@"""(?:[^""\\]|\\.)*""", TokenClassification.String));
        // JSON keywords
        h.Rules.Add(new HighlightingRule(@"\b(true|false|null)\b", TokenClassification.Keyword));
        // Numbers
        h.Rules.Add(new HighlightingRule(@"-?\b\d+(\.\d+)?([eE][+-]?\d+)?\b", TokenClassification.Number));
        // Structural characters
        h.Rules.Add(new HighlightingRule(@"[{}()\[\]]", TokenClassification.Punctuation));
        h.Rules.Add(new HighlightingRule(@"[:,]", TokenClassification.Operator));
        return h;
    }

    /// <summary>
    /// Creates an HTML syntax highlighter.
    /// </summary>
    public static RegexSyntaxHighlighter CreateHtmlHighlighter()
    {
        var h = new RegexSyntaxHighlighter();
        h.SpanRules.Add(new SpanRule(@"<!--", @"-->", TokenClassification.Comment));
        // Script/style blocks treated as spans would need a more complex highlighter;
        // for now we highlight tags, attributes, and strings.
        h.Rules.Add(new HighlightingRule(@"<!DOCTYPE\b[^>]*>", TokenClassification.Preprocessor));
        h.Rules.Add(new HighlightingRule(@"</?\w[\w\-]*", TokenClassification.Keyword)); // tag names
        h.Rules.Add(new HighlightingRule(@"/?>", TokenClassification.Keyword)); // tag close
        h.Rules.Add(new HighlightingRule(@"""(?:[^""\\]|\\.)*""", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"'(?:[^'\\]|\\.)*'", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"\b[\w\-]+(?=\s*=)", TokenClassification.Property)); // attribute names
        h.Rules.Add(new HighlightingRule(@"&\w+;", TokenClassification.Identifier)); // HTML entities
        h.Rules.Add(new HighlightingRule(@"[=]", TokenClassification.Operator));
        return h;
    }

    /// <summary>
    /// Creates a CSS/SCSS/LESS syntax highlighter.
    /// </summary>
    public static RegexSyntaxHighlighter CreateCssHighlighter()
    {
        var h = new RegexSyntaxHighlighter();
        h.SpanRules.Add(new SpanRule(@"/\*", @"\*/", TokenClassification.Comment));
        h.Rules.Add(new HighlightingRule(@"//.*$", TokenClassification.Comment, RegexOptions.Multiline)); // SCSS
        h.Rules.Add(new HighlightingRule(@"""(?:[^""\\]|\\.)*""", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"'(?:[^'\\]|\\.)*'", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"@\w[\w\-]*", TokenClassification.Preprocessor)); // @media, @import, SCSS vars
        h.Rules.Add(new HighlightingRule(@"\$[\w\-]+", TokenClassification.Identifier)); // SCSS variables
        h.Rules.Add(new HighlightingRule(@"#[0-9a-fA-F]{3,8}\b", TokenClassification.Number)); // color hex
        h.Rules.Add(new HighlightingRule(@"-?\b\d+(\.\d+)?(px|em|rem|%|vh|vw|vmin|vmax|pt|cm|mm|in|s|ms|deg|rad|fr|ch|ex)?\b", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"\b(inherit|initial|unset|revert|none|auto|normal|bold|italic|underline|block|inline|flex|grid|absolute|relative|fixed|sticky|hidden|visible|solid|dashed|dotted|transparent|currentColor|!important)\b", TokenClassification.Keyword));
        h.Rules.Add(new HighlightingRule(@"[\w\-]+(?=\s*:)", TokenClassification.Property)); // property names
        h.Rules.Add(new HighlightingRule(@"[.#][\w\-]+", TokenClassification.TypeName)); // selectors
        h.Rules.Add(new HighlightingRule(@"::?[\w\-]+", TokenClassification.Identifier)); // pseudo-elements/classes
        h.Rules.Add(new HighlightingRule(@"[{}();:,>+~]", TokenClassification.Punctuation));
        return h;
    }

    /// <summary>
    /// Creates a Python syntax highlighter.
    /// </summary>
    public static RegexSyntaxHighlighter CreatePythonHighlighter()
    {
        var h = new RegexSyntaxHighlighter();
        h.SpanRules.Add(new SpanRule(@"\""\""\""|'''", @"\""\""\""|'''", TokenClassification.String)); // triple-quoted strings
        h.Rules.Add(new HighlightingRule(@"#.*$", TokenClassification.Comment, RegexOptions.Multiline));
        h.Rules.Add(new HighlightingRule(@"[fFbBrRuU]?""(?:[^""\\]|\\.)*""", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"[fFbBrRuU]?'(?:[^'\\]|\\.)*'", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(
            @"\b(if|elif|else|for|while|break|continue|return|yield|try|except|finally|raise|with|as|pass)\b",
            TokenClassification.ControlKeyword));
        h.Rules.Add(new HighlightingRule(
            @"\b(False|True|None|and|assert|async|await|class|def|del|from|global|import|in|is|lambda|nonlocal|not|or|self|super)\b",
            TokenClassification.Keyword));
        h.Rules.Add(new HighlightingRule(@"\b(int|float|str|bool|list|dict|tuple|set|bytes|bytearray|type|object|complex|range|frozenset|memoryview)\b",
            TokenClassification.TypeName));
        h.Rules.Add(new HighlightingRule(@"@\w+", TokenClassification.Attribute)); // decorators
        h.Rules.Add(new HighlightingRule(@"\b\d+(\.\d+)?([eE][+-]?\d+)?[jJ]?\b", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0[xX][0-9a-fA-F]+", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0[oO][0-7]+", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0[bB][01]+", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"[+\-*/%=!<>&|^~@:]", TokenClassification.Operator));
        h.Rules.Add(new HighlightingRule(@"[{}()\[\];,.]", TokenClassification.Punctuation));
        return h;
    }

    /// <summary>
    /// Creates a Rust syntax highlighter.
    /// </summary>
    public static RegexSyntaxHighlighter CreateRustHighlighter()
    {
        var h = new RegexSyntaxHighlighter();
        h.SpanRules.Add(new SpanRule(@"/\*", @"\*/", TokenClassification.Comment));
        h.Rules.Add(new HighlightingRule(@"///.*$", TokenClassification.XmlDoc, RegexOptions.Multiline)); // doc comments
        h.Rules.Add(new HighlightingRule(@"//.*$", TokenClassification.Comment, RegexOptions.Multiline));
        h.Rules.Add(new HighlightingRule(@"r#*""", TokenClassification.String)); // raw strings
        h.Rules.Add(new HighlightingRule(@"""(?:[^""\\]|\\.)*""", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"'(?:[^'\\]|\\.)'", TokenClassification.Character));
        h.Rules.Add(new HighlightingRule(@"b'(?:[^'\\]|\\.)'", TokenClassification.Character));
        h.Rules.Add(new HighlightingRule(
            @"\b(if|else|for|while|loop|break|continue|return|match|yield)\b",
            TokenClassification.ControlKeyword));
        h.Rules.Add(new HighlightingRule(
            @"\b(as|async|await|const|crate|dyn|enum|extern|false|fn|impl|in|let|mod|move|mut|pub|ref|self|Self|static|struct|super|trait|true|type|union|unsafe|use|where)\b",
            TokenClassification.Keyword));
        h.Rules.Add(new HighlightingRule(
            @"\b(bool|char|f32|f64|i8|i16|i32|i64|i128|isize|str|u8|u16|u32|u64|u128|usize|String|Vec|Box|Option|Result)\b",
            TokenClassification.TypeName));
        h.Rules.Add(new HighlightingRule(@"#\[[\w:]+", TokenClassification.Attribute)); // attributes
        h.Rules.Add(new HighlightingRule(@"#!\[[\w:]+", TokenClassification.Attribute)); // inner attributes
        h.Rules.Add(new HighlightingRule(@"\b\d[\d_]*(\.\d[\d_]*)?([eE][+-]?\d+)?(_?[fiu]\d+)?\b", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0[xX][0-9a-fA-F_]+", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0[bB][01_]+", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0[oO][0-7_]+", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"[+\-*/%=!<>&|^~?:]", TokenClassification.Operator));
        h.Rules.Add(new HighlightingRule(@"[{}()\[\];,.]", TokenClassification.Punctuation));
        return h;
    }

    /// <summary>
    /// Creates a Go syntax highlighter.
    /// </summary>
    public static RegexSyntaxHighlighter CreateGoHighlighter()
    {
        var h = new RegexSyntaxHighlighter();
        h.SpanRules.Add(new SpanRule(@"/\*", @"\*/", TokenClassification.Comment));
        h.Rules.Add(new HighlightingRule(@"//.*$", TokenClassification.Comment, RegexOptions.Multiline));
        h.Rules.Add(new HighlightingRule(@"`[^`]*`", TokenClassification.String)); // raw strings
        h.Rules.Add(new HighlightingRule(@"""(?:[^""\\]|\\.)*""", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"'(?:[^'\\]|\\.)'", TokenClassification.Character));
        h.Rules.Add(new HighlightingRule(
            @"\b(if|else|for|switch|case|default|break|continue|return|goto|select|fallthrough|range)\b",
            TokenClassification.ControlKeyword));
        h.Rules.Add(new HighlightingRule(
            @"\b(chan|const|defer|func|go|import|interface|map|package|struct|type|var|false|true|nil|iota)\b",
            TokenClassification.Keyword));
        h.Rules.Add(new HighlightingRule(
            @"\b(bool|byte|complex64|complex128|error|float32|float64|int|int8|int16|int32|int64|rune|string|uint|uint8|uint16|uint32|uint64|uintptr|any|comparable)\b",
            TokenClassification.TypeName));
        h.Rules.Add(new HighlightingRule(
            @"\b(append|cap|close|complex|copy|delete|imag|len|make|new|panic|print|println|real|recover)\b",
            TokenClassification.Method));
        h.Rules.Add(new HighlightingRule(@"\b\d+(\.\d+)?([eE][+-]?\d+)?\b", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0[xX][0-9a-fA-F]+", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0[bB][01]+", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0[oO][0-7]+", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@":=|<-", TokenClassification.Operator));
        h.Rules.Add(new HighlightingRule(@"[+\-*/%=!<>&|^~]", TokenClassification.Operator));
        h.Rules.Add(new HighlightingRule(@"[{}()\[\];,.]", TokenClassification.Punctuation));
        return h;
    }

    /// <summary>
    /// Creates a Java syntax highlighter.
    /// </summary>
    public static RegexSyntaxHighlighter CreateJavaHighlighter()
    {
        var h = new RegexSyntaxHighlighter();
        h.SpanRules.Add(new SpanRule(@"/\*", @"\*/", TokenClassification.Comment));
        h.Rules.Add(new HighlightingRule(@"//.*$", TokenClassification.Comment, RegexOptions.Multiline));
        h.Rules.Add(new HighlightingRule(@"""(?:[^""\\]|\\.)*""", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"'(?:[^'\\]|\\.)'", TokenClassification.Character));
        h.Rules.Add(new HighlightingRule(
            @"\b(if|else|for|while|do|switch|case|default|break|continue|return|throw|try|catch|finally|yield)\b",
            TokenClassification.ControlKeyword));
        h.Rules.Add(new HighlightingRule(
            @"\b(abstract|assert|boolean|byte|char|class|const|double|enum|extends|false|final|float|implements|import|instanceof|int|interface|long|native|new|null|package|private|protected|public|record|sealed|short|static|strictfp|super|synchronized|this|throws|transient|true|var|void|volatile)\b",
            TokenClassification.Keyword));
        h.Rules.Add(new HighlightingRule(@"@\w+", TokenClassification.Attribute));
        h.Rules.Add(new HighlightingRule(@"\b\d+(\.\d+)?([eE][+-]?\d+)?[fFdDlL]?\b", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0[xX][0-9a-fA-F]+[lL]?", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0[bB][01]+[lL]?", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"[+\-*/%=!<>&|^~?:]", TokenClassification.Operator));
        h.Rules.Add(new HighlightingRule(@"[{}()\[\];,.]", TokenClassification.Punctuation));
        return h;
    }

    /// <summary>
    /// Creates a Lua syntax highlighter.
    /// </summary>
    public static RegexSyntaxHighlighter CreateLuaHighlighter()
    {
        var h = new RegexSyntaxHighlighter();
        h.SpanRules.Add(new SpanRule(@"--\[\[", @"\]\]", TokenClassification.Comment));
        h.Rules.Add(new HighlightingRule(@"--.*$", TokenClassification.Comment, RegexOptions.Multiline));
        h.Rules.Add(new HighlightingRule(@"\[\[(?:[^\]]|\][^\]])*\]\]", TokenClassification.String)); // long strings
        h.Rules.Add(new HighlightingRule(@"""(?:[^""\\]|\\.)*""", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"'(?:[^'\\]|\\.)*'", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(
            @"\b(if|then|elseif|else|for|while|repeat|until|break|return|goto|in|do|end)\b",
            TokenClassification.ControlKeyword));
        h.Rules.Add(new HighlightingRule(
            @"\b(and|false|function|local|nil|not|or|true)\b",
            TokenClassification.Keyword));
        h.Rules.Add(new HighlightingRule(@"\b\d+(\.\d+)?([eE][+-]?\d+)?\b", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0[xX][0-9a-fA-F]+", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"[+\-*/%=<>~#]", TokenClassification.Operator));
        h.Rules.Add(new HighlightingRule(@"\.\.", TokenClassification.Operator));
        h.Rules.Add(new HighlightingRule(@"[{}()\[\];,.]", TokenClassification.Punctuation));
        return h;
    }

    /// <summary>
    /// Creates a YAML syntax highlighter.
    /// </summary>
    public static RegexSyntaxHighlighter CreateYamlHighlighter()
    {
        var h = new RegexSyntaxHighlighter();
        h.Rules.Add(new HighlightingRule(@"#.*$", TokenClassification.Comment, RegexOptions.Multiline));
        h.Rules.Add(new HighlightingRule(@"---", TokenClassification.Punctuation)); // document start
        h.Rules.Add(new HighlightingRule(@"\.\.\.", TokenClassification.Punctuation)); // document end
        h.Rules.Add(new HighlightingRule(@"""(?:[^""\\]|\\.)*""", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"'(?:[^'\\]|\\.)*'", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"[\w\.\-/]+(?=\s*:)", TokenClassification.Property)); // keys
        h.Rules.Add(new HighlightingRule(@"\b(true|false|yes|no|on|off|null|~)\b", TokenClassification.Keyword));
        h.Rules.Add(new HighlightingRule(@"[&*]\w+", TokenClassification.Identifier)); // anchors & aliases
        h.Rules.Add(new HighlightingRule(@"!!\w+", TokenClassification.TypeName)); // tags
        h.Rules.Add(new HighlightingRule(@"<<", TokenClassification.Operator)); // merge key
        h.Rules.Add(new HighlightingRule(@"-?\b\d+(\.\d+)?([eE][+-]?\d+)?\b", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"[:\-\[\]{}|>]", TokenClassification.Punctuation));
        return h;
    }

    /// <summary>
    /// Creates a TOML syntax highlighter.
    /// </summary>
    public static RegexSyntaxHighlighter CreateTomlHighlighter()
    {
        var h = new RegexSyntaxHighlighter();
        h.Rules.Add(new HighlightingRule(@"#.*$", TokenClassification.Comment, RegexOptions.Multiline));
        h.Rules.Add(new HighlightingRule(@"\[\[[\w\.\-]+\]\]", TokenClassification.TypeName)); // array of tables
        h.Rules.Add(new HighlightingRule(@"\[[\w\.\-]+\]", TokenClassification.TypeName)); // tables
        h.Rules.Add(new HighlightingRule(@"\""\""\""|'''", TokenClassification.String)); // multi-line string markers
        h.Rules.Add(new HighlightingRule(@"""(?:[^""\\]|\\.)*""", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"'[^']*'", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"[\w\-]+(?=\s*=)", TokenClassification.Property)); // keys
        h.Rules.Add(new HighlightingRule(@"\b(true|false)\b", TokenClassification.Keyword));
        h.Rules.Add(new HighlightingRule(@"\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}", TokenClassification.Number)); // datetime
        h.Rules.Add(new HighlightingRule(@"-?\b\d+(\.\d+)?([eE][+-]?\d+)?\b", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0[xX][0-9a-fA-F_]+", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0[oO][0-7_]+", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"0[bB][01_]+", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"[=]", TokenClassification.Operator));
        h.Rules.Add(new HighlightingRule(@"[\[\]{},.]", TokenClassification.Punctuation));
        return h;
    }

    /// <summary>
    /// Creates a Markdown syntax highlighter.
    /// </summary>
    public static RegexSyntaxHighlighter CreateMarkdownHighlighter()
    {
        var h = new RegexSyntaxHighlighter();
        h.SpanRules.Add(new SpanRule(@"```", @"```", TokenClassification.String)); // fenced code blocks
        h.Rules.Add(new HighlightingRule(@"^#{1,6}\s.*$", TokenClassification.Keyword, RegexOptions.Multiline)); // headings
        h.Rules.Add(new HighlightingRule(@"`[^`]+`", TokenClassification.String)); // inline code
        h.Rules.Add(new HighlightingRule(@"\*\*[^*]+\*\*", TokenClassification.Keyword)); // bold
        h.Rules.Add(new HighlightingRule(@"__[^_]+__", TokenClassification.Keyword)); // bold
        h.Rules.Add(new HighlightingRule(@"\*[^*]+\*", TokenClassification.Comment)); // italic
        h.Rules.Add(new HighlightingRule(@"_[^_]+_", TokenClassification.Comment)); // italic
        h.Rules.Add(new HighlightingRule(@"^\s*[-*+]\s", TokenClassification.Operator, RegexOptions.Multiline)); // list markers
        h.Rules.Add(new HighlightingRule(@"^\s*\d+\.\s", TokenClassification.Operator, RegexOptions.Multiline)); // ordered list
        h.Rules.Add(new HighlightingRule(@"\[([^\]]+)\]\(([^)]+)\)", TokenClassification.Identifier)); // links
        h.Rules.Add(new HighlightingRule(@"!\[([^\]]*)\]\(([^)]+)\)", TokenClassification.Identifier)); // images
        h.Rules.Add(new HighlightingRule(@"^\s*>", TokenClassification.Preprocessor, RegexOptions.Multiline)); // blockquotes
        return h;
    }

    /// <summary>
    /// Creates a Shell/Bash syntax highlighter.
    /// </summary>
    public static RegexSyntaxHighlighter CreateShellHighlighter()
    {
        var h = new RegexSyntaxHighlighter();
        h.Rules.Add(new HighlightingRule(@"#.*$", TokenClassification.Comment, RegexOptions.Multiline));
        h.Rules.Add(new HighlightingRule(@"""(?:[^""\\]|\\.)*""", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"'[^']*'", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"\$\{[^}]+\}", TokenClassification.Identifier)); // variable expansion
        h.Rules.Add(new HighlightingRule(@"\$\w+", TokenClassification.Identifier)); // variables
        h.Rules.Add(new HighlightingRule(@"\$\([^)]+\)", TokenClassification.Identifier)); // command substitution
        h.Rules.Add(new HighlightingRule(
            @"\b(if|then|elif|else|fi|for|while|until|do|done|case|esac|in|function|select)\b",
            TokenClassification.ControlKeyword));
        h.Rules.Add(new HighlightingRule(
            @"\b(echo|exit|export|source|alias|unalias|set|unset|local|return|shift|eval|exec|test|read|declare|typeset|readonly|trap|cd|pwd|pushd|popd)\b",
            TokenClassification.Keyword));
        h.Rules.Add(new HighlightingRule(@"\b\d+\b", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"[|&;><]", TokenClassification.Operator));
        h.Rules.Add(new HighlightingRule(@"[{}()\[\]]", TokenClassification.Punctuation));
        return h;
    }

    /// <summary>
    /// Creates a PowerShell syntax highlighter.
    /// </summary>
    public static RegexSyntaxHighlighter CreatePowerShellHighlighter()
    {
        var h = new RegexSyntaxHighlighter();
        h.SpanRules.Add(new SpanRule(@"<#", @"#>", TokenClassification.Comment));
        h.Rules.Add(new HighlightingRule(@"#.*$", TokenClassification.Comment, RegexOptions.Multiline));
        h.Rules.Add(new HighlightingRule(@"@""", TokenClassification.String)); // here-string
        h.Rules.Add(new HighlightingRule(@"""(?:[^""\\]|\\.)*""", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"'[^']*'", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"\$[\w:]+", TokenClassification.Identifier)); // variables
        h.Rules.Add(new HighlightingRule(@"\$\{[^}]+\}", TokenClassification.Identifier));
        h.Rules.Add(new HighlightingRule(
            @"\b(?i)(if|elseif|else|for|foreach|while|do|until|switch|break|continue|return|throw|try|catch|finally|trap|exit|in)\b",
            TokenClassification.ControlKeyword));
        h.Rules.Add(new HighlightingRule(
            @"\b(?i)(function|filter|param|begin|process|end|class|enum|using|true|false|null)\b",
            TokenClassification.Keyword));
        h.Rules.Add(new HighlightingRule(@"-\w+", TokenClassification.Property)); // parameters
        h.Rules.Add(new HighlightingRule(@"\[[\w\.]+\]", TokenClassification.TypeName)); // type literals
        h.Rules.Add(new HighlightingRule(@"\b\d+(\.\d+)?\b", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"[+\-*/%=!<>&|^]", TokenClassification.Operator));
        h.Rules.Add(new HighlightingRule(@"[{}()\[\];,@.]", TokenClassification.Punctuation));
        return h;
    }

    /// <summary>
    /// Creates a SQL syntax highlighter.
    /// </summary>
    public static RegexSyntaxHighlighter CreateSqlHighlighter()
    {
        var h = new RegexSyntaxHighlighter();
        h.SpanRules.Add(new SpanRule(@"/\*", @"\*/", TokenClassification.Comment));
        h.Rules.Add(new HighlightingRule(@"--.*$", TokenClassification.Comment, RegexOptions.Multiline));
        h.Rules.Add(new HighlightingRule(@"'(?:[^'\\]|\\.)*'", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(@"N'(?:[^'\\]|\\.)*'", TokenClassification.String));
        h.Rules.Add(new HighlightingRule(
            @"\b(?i)(SELECT|FROM|WHERE|AND|OR|NOT|IN|IS|NULL|AS|ON|JOIN|INNER|LEFT|RIGHT|OUTER|FULL|CROSS|INSERT|INTO|VALUES|UPDATE|SET|DELETE|CREATE|ALTER|DROP|TABLE|VIEW|INDEX|PROCEDURE|FUNCTION|TRIGGER|DATABASE|SCHEMA|GRANT|REVOKE|UNION|ALL|DISTINCT|TOP|LIMIT|OFFSET|ORDER|BY|GROUP|HAVING|EXISTS|BETWEEN|LIKE|CASE|WHEN|THEN|ELSE|END|BEGIN|COMMIT|ROLLBACK|TRANSACTION|DECLARE|EXEC|EXECUTE|IF|WHILE|RETURN|GO|USE|PRINT|CAST|CONVERT|COALESCE|ISNULL|PRIMARY|KEY|FOREIGN|REFERENCES|CONSTRAINT|UNIQUE|CHECK|DEFAULT|IDENTITY|WITH|NOLOCK|ASC|DESC)\b",
            TokenClassification.Keyword));
        h.Rules.Add(new HighlightingRule(
            @"\b(?i)(INT|BIGINT|SMALLINT|TINYINT|BIT|DECIMAL|NUMERIC|MONEY|FLOAT|REAL|DATE|DATETIME|DATETIME2|DATETIMEOFFSET|TIME|CHAR|VARCHAR|NCHAR|NVARCHAR|TEXT|NTEXT|BINARY|VARBINARY|IMAGE|XML|UNIQUEIDENTIFIER|SQL_VARIANT|CURSOR|TABLE|TIMESTAMP|ROWVERSION|BOOLEAN|SERIAL|BLOB|CLOB)\b",
            TokenClassification.TypeName));
        h.Rules.Add(new HighlightingRule(
            @"\b(?i)(COUNT|SUM|AVG|MIN|MAX|ABS|CEILING|FLOOR|ROUND|POWER|SQRT|LEN|SUBSTRING|UPPER|LOWER|TRIM|LTRIM|RTRIM|REPLACE|CHARINDEX|PATINDEX|STUFF|FORMAT|CONCAT|GETDATE|GETUTCDATE|DATEADD|DATEDIFF|DATENAME|DATEPART|YEAR|MONTH|DAY|NEWID|ROW_NUMBER|RANK|DENSE_RANK|NTILE|LAG|LEAD|FIRST_VALUE|LAST_VALUE)\b",
            TokenClassification.Method));
        h.Rules.Add(new HighlightingRule(@"@\w+", TokenClassification.Identifier)); // variables
        h.Rules.Add(new HighlightingRule(@"\b\d+(\.\d+)?\b", TokenClassification.Number));
        h.Rules.Add(new HighlightingRule(@"[+\-*/%=<>!]", TokenClassification.Operator));
        h.Rules.Add(new HighlightingRule(@"[{}()\[\];,.]", TokenClassification.Punctuation));
        return h;
    }

    /// <summary>
    /// Creates a highlighter for a language by name.
    /// Returns null if the language is not recognized.
    /// </summary>
    public static RegexSyntaxHighlighter? CreateForLanguage(string language)
    {
        return (language ?? "").ToLowerInvariant() switch
        {
            "csharp" or "cs" or "c#" => CreateCSharpHighlighter(),
            "cpp" or "c++" or "c" or "h" or "hpp" or "cc" or "cxx" or "hxx" or "objc" or "objective-c" => CreateCppHighlighter(),
            "javascript" or "js" or "jsx" or "typescript" or "ts" or "tsx" or "mjs" or "cjs" => CreateJavaScriptHighlighter(),
            "json" or "jsonc" or "json5" => CreateJsonHighlighter(),
            "html" or "htm" or "vue" or "svelte" or "razor" or "cshtml" => CreateHtmlHighlighter(),
            "css" or "scss" or "sass" or "less" or "stylus" => CreateCssHighlighter(),
            "python" or "py" => CreatePythonHighlighter(),
            "rust" or "rs" => CreateRustHighlighter(),
            "go" or "golang" => CreateGoHighlighter(),
            "java" or "kotlin" or "kt" or "scala" => CreateJavaHighlighter(),
            "lua" => CreateLuaHighlighter(),
            "yaml" or "yml" => CreateYamlHighlighter(),
            "toml" => CreateTomlHighlighter(),
            "markdown" or "md" => CreateMarkdownHighlighter(),
            "shellscript" or "bash" or "sh" or "zsh" or "fish" => CreateShellHighlighter(),
            "powershell" or "ps1" or "psm1" => CreatePowerShellHighlighter(),
            "sql" or "mysql" or "postgresql" or "sqlite" or "tsql" => CreateSqlHighlighter(),
            _ => null,
        };
    }
}
