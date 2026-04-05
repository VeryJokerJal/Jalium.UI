namespace Jalium.UI.Controls.Editor;

/// <summary>
/// State object for JALXAML syntax highlighting across lines.
/// Tracks multi-line constructs: comments, tag context, attribute values, and markup extensions.
/// </summary>
internal sealed class JalxamlHighlighterState
{
    public bool InComment { get; init; }
    public bool InTag { get; init; }
    public bool InAttributeValue { get; init; }
    public int MarkupExtensionDepth { get; init; }
    public int RazorBraceDepth { get; init; }
    public bool InRazorBlockComment { get; init; }
    /// <summary>
    /// Set when a Razor directive (e.g. <c>@for(...)</c>) was scanned but the opening <c>{</c>
    /// was not found on the same line. The next line should look for <c>{</c> to enter code mode.
    /// </summary>
    public bool RazorPendingBlock { get; init; }

    public static readonly JalxamlHighlighterState Default = new();

    public override bool Equals(object? obj) =>
        obj is JalxamlHighlighterState other &&
        InComment == other.InComment &&
        InTag == other.InTag &&
        InAttributeValue == other.InAttributeValue &&
        MarkupExtensionDepth == other.MarkupExtensionDepth &&
        RazorBraceDepth == other.RazorBraceDepth &&
        InRazorBlockComment == other.InRazorBlockComment &&
        RazorPendingBlock == other.RazorPendingBlock;

    public override int GetHashCode() =>
        HashCode.Combine(InComment, InTag, InAttributeValue, MarkupExtensionDepth, RazorBraceDepth, InRazorBlockComment, RazorPendingBlock);
}

/// <summary>
/// Hand-written state-machine syntax highlighter for JALXAML (XML/XAML-like) files.
/// Handles multi-line comments, tag context, attribute values, and nested markup extensions.
/// </summary>
public sealed class JalxamlSyntaxHighlighter : ISyntaxHighlighter
{
    private static readonly HashSet<string> s_bindingExtensionNames = new(StringComparer.Ordinal)
    {
        "Binding",
        "TemplateBinding",
        "CompiledBinding",
        "x:Bind",
    };

    private static readonly HashSet<string> s_bindingParameterNames = new(StringComparer.Ordinal)
    {
        "Path",
        "XPath",
        "Mode",
        "Source",
        "ElementName",
        "RelativeSource",
        "Converter",
        "ConverterParameter",
        "StringFormat",
        "FallbackValue",
        "TargetNullValue",
        "UpdateSourceTrigger",
        "ValidatesOnDataErrors",
        "ValidatesOnNotifyDataErrors",
        "ValidatesOnExceptions",
        "NotifyOnSourceUpdated",
        "NotifyOnTargetUpdated",
        "NotifyOnValidationError",
        "BindsDirectlyToSource",
        "Delay",
    };

    private static readonly HashSet<string> s_razorDirectiveKeywords = new(StringComparer.Ordinal)
    {
        "if",
        "else",
        "for",
        "foreach",
        "while",
        "do",
        "switch",
        "try",
        "catch",
        "finally",
        "using",
        "lock",
        "section",
        "code",
    };

    private static readonly HashSet<string> s_razorContinuationKeywords = new(StringComparer.Ordinal)
    {
        "else",
        "catch",
        "finally",
        "while", // for do { } while();
    };

    private static readonly HashSet<string> s_razorExpressionKeywords = new(StringComparer.Ordinal)
    {
        "true",
        "false",
        "null",
        "new",
        "this",
        "base",
        "typeof",
        "nameof",
        "global",
    };

    private static readonly HashSet<string> s_razorExpressionControlKeywords = new(StringComparer.Ordinal)
    {
        "if",
        "else",
        "for",
        "foreach",
        "while",
        "switch",
        "case",
        "default",
        "return",
        "break",
        "continue",
        "try",
        "catch",
        "finally",
        "do",
        "await",
        "throw",
        "using",
        "lock",
        "when",
        "in",
    };

    private static readonly HashSet<string> s_razorCodeKeywords = new(StringComparer.Ordinal)
    {
        "void",
        "var",
        "string",
        "object",
        "bool",
        "byte",
        "char",
        "decimal",
        "double",
        "float",
        "int",
        "long",
        "short",
        "uint",
        "ulong",
        "ushort",
        "null",
        "true",
        "false",
        "new",
        "this",
        "base",
        "return",
    };

    public object? GetInitialState() => JalxamlHighlighterState.Default;

    public static JalxamlSyntaxHighlighter Create() => new();

    public (SyntaxToken[] tokens, object? stateAtLineEnd) HighlightLine(
        int lineNumber, string lineText, object? stateAtLineStart)
    {
        var state = stateAtLineStart as JalxamlHighlighterState ?? JalxamlHighlighterState.Default;
        var tokens = new List<SyntaxToken>();
        int pos = 0;
        bool inComment = state.InComment;
        bool inTag = state.InTag;
        bool inAttrValue = state.InAttributeValue;
        int meDepth = state.MarkupExtensionDepth;
        int razorBraceDepth = state.RazorBraceDepth;
        bool inRazorBlockComment = state.InRazorBlockComment;
        bool razorPendingBlock = state.RazorPendingBlock;

        // Handle pending Razor block: previous line had @directive(...) without {
        if (razorPendingBlock && pos < lineText.Length)
        {
            razorPendingBlock = false;
            int ws = pos;
            while (ws < lineText.Length && char.IsWhiteSpace(lineText[ws]))
                ws++;
            if (ws < lineText.Length && lineText[ws] == '{')
            {
                if (ws > pos)
                    tokens.Add(new SyntaxToken(pos, ws - pos, TokenClassification.PlainText));
                tokens.Add(new SyntaxToken(ws, 1, TokenClassification.Punctuation));
                pos = ws + 1;
                razorBraceDepth = 1;
                pos = ScanRazorCodeBlockMultiLine(lineText, pos, tokens, ref razorBraceDepth, ref inRazorBlockComment, ref razorPendingBlock);
            }
        }

        // Continue multi-line Razor block comment /* ... */
        if (inRazorBlockComment)
        {
            int endIdx = lineText.IndexOf("*/", pos, StringComparison.Ordinal);
            if (endIdx >= 0)
            {
                tokens.Add(new SyntaxToken(pos, endIdx + 2 - pos, TokenClassification.Comment));
                pos = endIdx + 2;
                inRazorBlockComment = false;
            }
            else
            {
                tokens.Add(new SyntaxToken(pos, lineText.Length - pos, TokenClassification.Comment));
                return (FillPlainText(tokens, lineText.Length), new JalxamlHighlighterState
                {
                    InComment = inComment, InTag = inTag, InAttributeValue = inAttrValue,
                    MarkupExtensionDepth = meDepth, RazorBraceDepth = razorBraceDepth,
                    InRazorBlockComment = true
                });
            }
        }

        // Continue multi-line Razor code block from previous line
        if (razorBraceDepth > 0 && pos < lineText.Length)
        {
            pos = ScanRazorCodeBlockMultiLine(lineText, pos, tokens, ref razorBraceDepth, ref inRazorBlockComment, ref razorPendingBlock);
        }

        // Continue multi-line comment from previous line
        if (inComment)
        {
            pos = ScanCommentContinuation(lineText, 0, tokens, out inComment);
            if (inComment)
            {
                // Entire line is inside the comment
                return (FillPlainText(tokens, lineText.Length), new JalxamlHighlighterState
                {
                    InComment = true, InTag = inTag, InAttributeValue = inAttrValue,
                    MarkupExtensionDepth = meDepth
                });
            }
        }

        // Continue multi-line attribute value from previous line
        if (inAttrValue)
        {
            if (meDepth > 0)
            {
                pos = ScanMarkupExtensionContent(lineText, pos, tokens, ref meDepth, ref inAttrValue);
            }
            else
            {
                pos = ScanAttributeValueContinuation(lineText, pos, tokens, ref inAttrValue, ref meDepth);
            }

            if (inAttrValue && pos >= lineText.Length)
            {
                return (FillPlainText(tokens, lineText.Length), new JalxamlHighlighterState
                {
                    InComment = false, InTag = inTag, InAttributeValue = true,
                    MarkupExtensionDepth = meDepth
                });
            }
        }

        // Continue tag context from previous line (attribute scanning)
        if (inTag && pos < lineText.Length)
        {
            pos = ScanTagContent(lineText, pos, tokens, ref inTag, ref inAttrValue, ref meDepth);
        }

        // Main scanning loop
        while (pos < lineText.Length)
        {
            if (inTag)
            {
                pos = ScanTagContent(lineText, pos, tokens, ref inTag, ref inAttrValue, ref meDepth);
                continue;
            }

            // Comment start: <!--
            if (pos + 3 < lineText.Length && lineText[pos] == '<' && lineText[pos + 1] == '!' &&
                lineText[pos + 2] == '-' && lineText[pos + 3] == '-')
            {
                int commentStart = pos;
                pos += 4;
                int endIdx = lineText.IndexOf("-->", pos, StringComparison.Ordinal);
                if (endIdx >= 0)
                {
                    tokens.Add(new SyntaxToken(commentStart, endIdx + 3 - commentStart, TokenClassification.Comment));
                    pos = endIdx + 3;
                }
                else
                {
                    tokens.Add(new SyntaxToken(commentStart, lineText.Length - commentStart, TokenClassification.Comment));
                    inComment = true;
                    pos = lineText.Length;
                }
                continue;
            }

            // Processing instruction: <?...?>
            if (pos + 1 < lineText.Length && lineText[pos] == '<' && lineText[pos + 1] == '?')
            {
                int piStart = pos;
                int piEnd = lineText.IndexOf("?>", pos + 2, StringComparison.Ordinal);
                if (piEnd >= 0)
                {
                    tokens.Add(new SyntaxToken(piStart, piEnd + 2 - piStart, TokenClassification.Preprocessor));
                    pos = piEnd + 2;
                }
                else
                {
                    tokens.Add(new SyntaxToken(piStart, lineText.Length - piStart, TokenClassification.Preprocessor));
                    pos = lineText.Length;
                }
                continue;
            }

            // CDATA section: <![CDATA[...]]>
            if (pos + 8 < lineText.Length && lineText.AsSpan(pos).StartsWith("<![CDATA[", StringComparison.Ordinal))
            {
                int cdataStart = pos;
                int cdataEnd = lineText.IndexOf("]]>", pos + 9, StringComparison.Ordinal);
                if (cdataEnd >= 0)
                {
                    tokens.Add(new SyntaxToken(cdataStart, cdataEnd + 3 - cdataStart, TokenClassification.String));
                    pos = cdataEnd + 3;
                }
                else
                {
                    tokens.Add(new SyntaxToken(cdataStart, lineText.Length - cdataStart, TokenClassification.String));
                    pos = lineText.Length;
                }
                continue;
            }

            // Tag open: < or </
            if (lineText[pos] == '<')
            {
                pos = ScanTagOpen(lineText, pos, tokens, ref inTag, ref inAttrValue, ref meDepth);
                continue;
            }

            pos = ScanTextContentWithRazor(lineText, pos, tokens, ref razorBraceDepth, ref inRazorBlockComment, ref razorPendingBlock);
        }

        var endState = new JalxamlHighlighterState
        {
            InComment = inComment,
            InTag = inTag,
            InAttributeValue = inAttrValue,
            MarkupExtensionDepth = meDepth,
            RazorBraceDepth = razorBraceDepth,
            InRazorBlockComment = inRazorBlockComment,
            RazorPendingBlock = razorPendingBlock,
        };

        return (FillPlainText(tokens, lineText.Length), endState);
    }

    /// <summary>
    /// Continues scanning inside a multi-line comment. Returns the position after the comment ends.
    /// </summary>
    private static int ScanCommentContinuation(string text, int pos, List<SyntaxToken> tokens, out bool stillInComment)
    {
        int endIdx = text.IndexOf("-->", pos, StringComparison.Ordinal);
        if (endIdx >= 0)
        {
            tokens.Add(new SyntaxToken(pos, endIdx + 3 - pos, TokenClassification.Comment));
            stillInComment = false;
            return endIdx + 3;
        }

        tokens.Add(new SyntaxToken(pos, text.Length - pos, TokenClassification.Comment));
        stillInComment = true;
        return text.Length;
    }

    /// <summary>
    /// Scans from a tag open (&lt;) through the tag name, then into attributes.
    /// </summary>
    private static int ScanTagOpen(string text, int pos, List<SyntaxToken> tokens,
        ref bool inTag, ref bool inAttrValue, ref int meDepth)
    {
        int start = pos;
        bool isClosing = pos + 1 < text.Length && text[pos + 1] == '/';
        int bracketLen = isClosing ? 2 : 1;

        // Emit < or </
        tokens.Add(new SyntaxToken(pos, bracketLen, TokenClassification.Punctuation));
        pos += bracketLen;

        // Skip whitespace after <
        pos = SkipWhitespace(text, pos);

        // Scan tag name
        pos = ScanTagName(text, pos, tokens);

        // Now scan attributes / close bracket
        inTag = true;
        pos = ScanTagContent(text, pos, tokens, ref inTag, ref inAttrValue, ref meDepth);

        return pos;
    }

    /// <summary>
    /// Scans a tag name, handling property element syntax (Button.Content) and x: prefix.
    /// </summary>
    private static int ScanTagName(string text, int pos, List<SyntaxToken> tokens)
    {
        if (pos >= text.Length || !IsNameStartChar(text[pos]))
            return pos;

        int nameStart = pos;
        pos = ScanNameChars(text, pos);
        string name = text[nameStart..pos];

        // Check for x: prefix (e.g., x:Array)
        if (name == "x" && pos < text.Length && text[pos] == ':')
        {
            // x:TagName → all as Attribute
            int fullStart = nameStart;
            pos++; // skip ':'
            pos = ScanNameChars(text, pos);
            tokens.Add(new SyntaxToken(fullStart, pos - fullStart, TokenClassification.Attribute));
            return pos;
        }

        // Check for namespace prefix (e.g., local:MyControl)
        if (pos < text.Length && text[pos] == ':')
        {
            // prefix:TagName
            tokens.Add(new SyntaxToken(nameStart, pos - nameStart, TokenClassification.Namespace));
            tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Punctuation)); // ':'
            pos++;
            int localStart = pos;
            pos = ScanNameChars(text, pos);
            if (pos > localStart)
            {
                // Check for property element (prefix:Type.Prop)
                if (pos < text.Length && text[pos] == '.')
                {
                    tokens.Add(new SyntaxToken(localStart, pos - localStart, TokenClassification.TypeName));
                    tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Punctuation)); // '.'
                    pos++;
                    int propStart = pos;
                    pos = ScanNameChars(text, pos);
                    if (pos > propStart)
                        tokens.Add(new SyntaxToken(propStart, pos - propStart, TokenClassification.Property));
                }
                else
                {
                    tokens.Add(new SyntaxToken(localStart, pos - localStart, TokenClassification.TypeName));
                }
            }
            return pos;
        }

        // Check for property element syntax: Type.Property
        if (pos < text.Length && text[pos] == '.')
        {
            tokens.Add(new SyntaxToken(nameStart, pos - nameStart, TokenClassification.TypeName));
            tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Punctuation)); // '.'
            pos++;
            int propStart = pos;
            pos = ScanNameChars(text, pos);
            if (pos > propStart)
                tokens.Add(new SyntaxToken(propStart, pos - propStart, TokenClassification.Property));
            return pos;
        }

        // Simple tag name
        tokens.Add(new SyntaxToken(nameStart, pos - nameStart, TokenClassification.TypeName));
        return pos;
    }

    /// <summary>
    /// Scans inside a tag: attributes, closing brackets (&gt; or /&gt;).
    /// </summary>
    private static int ScanTagContent(string text, int pos, List<SyntaxToken> tokens,
        ref bool inTag, ref bool inAttrValue, ref int meDepth)
    {
        while (pos < text.Length)
        {
            pos = SkipWhitespace(text, pos);
            if (pos >= text.Length) break;

            char c = text[pos];

            // Self-closing />
            if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '>')
            {
                tokens.Add(new SyntaxToken(pos, 2, TokenClassification.Punctuation));
                inTag = false;
                return pos + 2;
            }

            // Closing >
            if (c == '>')
            {
                tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Punctuation));
                inTag = false;
                return pos + 1;
            }

            // Attribute name
            if (IsNameStartChar(c))
            {
                pos = ScanAttributeName(text, pos, tokens);

                pos = SkipWhitespace(text, pos);

                // '='
                if (pos < text.Length && text[pos] == '=')
                {
                    tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Operator));
                    pos++;

                    pos = SkipWhitespace(text, pos);

                    // Attribute value
                    if (pos < text.Length && text[pos] == '"')
                    {
                        pos = ScanAttributeValue(text, pos, tokens, ref inAttrValue, ref meDepth);
                    }
                    else if (pos < text.Length && text[pos] == '\'')
                    {
                        pos = ScanAttributeValueSingleQuote(text, pos, tokens);
                    }
                }
                continue;
            }

            // Unknown character inside tag — skip
            pos++;
        }

        return pos;
    }

    /// <summary>
    /// Scans an attribute name: simple, attached (Grid.Row), x: directive, or xmlns.
    /// </summary>
    private static int ScanAttributeName(string text, int pos, List<SyntaxToken> tokens)
    {
        int nameStart = pos;
        pos = ScanNameChars(text, pos);
        string name = text[nameStart..pos];

        // xmlns or xmlns:prefix
        if (name == "xmlns")
        {
            if (pos < text.Length && text[pos] == ':')
            {
                pos++; // include ':'
                pos = ScanNameChars(text, pos);
            }
            tokens.Add(new SyntaxToken(nameStart, pos - nameStart, TokenClassification.Namespace));
            return pos;
        }

        // x:Name, x:Key, x:Class etc.
        if (name == "x" && pos < text.Length && text[pos] == ':')
        {
            int fullStart = nameStart;
            pos++; // skip ':'
            pos = ScanNameChars(text, pos);
            tokens.Add(new SyntaxToken(fullStart, pos - fullStart, TokenClassification.Attribute));
            return pos;
        }

        // Custom namespace prefix:PropName (rare in attributes, but possible)
        if (pos < text.Length && text[pos] == ':')
        {
            tokens.Add(new SyntaxToken(nameStart, pos - nameStart, TokenClassification.Namespace));
            tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Punctuation));
            pos++;
            int localStart = pos;
            pos = ScanNameChars(text, pos);
            if (pos > localStart)
            {
                // Check for attached property: prefix:Type.Property
                if (pos < text.Length && text[pos] == '.')
                {
                    tokens.Add(new SyntaxToken(localStart, pos - localStart, TokenClassification.TypeName));
                    tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Punctuation));
                    pos++;
                    int propStart = pos;
                    pos = ScanNameChars(text, pos);
                    if (pos > propStart)
                        tokens.Add(new SyntaxToken(propStart, pos - propStart, TokenClassification.Property));
                }
                else
                {
                    tokens.Add(new SyntaxToken(localStart, pos - localStart, TokenClassification.Property));
                }
            }
            return pos;
        }

        // Attached property: Grid.Row
        if (pos < text.Length && text[pos] == '.')
        {
            tokens.Add(new SyntaxToken(nameStart, pos - nameStart, TokenClassification.TypeName));
            tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Punctuation)); // '.'
            pos++;
            int propStart = pos;
            pos = ScanNameChars(text, pos);
            if (pos > propStart)
                tokens.Add(new SyntaxToken(propStart, pos - propStart, TokenClassification.Property));
            return pos;
        }

        // Simple attribute name
        tokens.Add(new SyntaxToken(nameStart, pos - nameStart, TokenClassification.Property));
        return pos;
    }

    /// <summary>
    /// Scans a double-quoted attribute value, detecting markup extensions.
    /// </summary>
    private static int ScanAttributeValue(string text, int pos, List<SyntaxToken> tokens,
        ref bool inAttrValue, ref int meDepth)
    {
        // Opening quote
        int quotePos = pos;
        pos++;

        // Check for markup extension start: "{" but not "{{"
        if (pos < text.Length && text[pos] == '{')
        {
            if (pos + 1 < text.Length && text[pos + 1] == '{')
            {
                // Escaped literal {{ — treat as string
                return ScanPlainStringValue(text, quotePos, pos, tokens, ref inAttrValue);
            }

            // Emit opening quote as String
            tokens.Add(new SyntaxToken(quotePos, 1, TokenClassification.String));

            // Enter markup extension
            bool isBindingExtension = IsBindingExtensionAt(text, pos + 1);
            meDepth = 1;
            tokens.Add(new SyntaxToken(pos, 1,
                isBindingExtension ? TokenClassification.BindingOperator : TokenClassification.Operator)); // '{'
            pos++;
            inAttrValue = true;

            pos = ScanMarkupExtensionContent(text, pos, tokens, ref meDepth, ref inAttrValue, isBindingExtension);
            return pos;
        }

        // Plain string value
        return ScanQuotedAttributeValue(text, quotePos, pos, '"', tokens, ref inAttrValue);
    }

    /// <summary>
    /// Scans a plain (non-markup-extension) string value from current position.
    /// </summary>
    private static int ScanPlainStringValue(string text, int quotePos, int pos, List<SyntaxToken> tokens,
        ref bool inAttrValue)
    {
        // Scan to closing quote
        while (pos < text.Length && text[pos] != '"')
            pos++;

        if (pos < text.Length)
        {
            int closingQuotePos = pos;
            pos++;
            if (!TryEmitNumericQuotedValueTokens(text, quotePos, closingQuotePos, tokens))
                tokens.Add(new SyntaxToken(quotePos, pos - quotePos, TokenClassification.String));
            inAttrValue = false;
        }
        else
        {
            // Line ended inside string
            tokens.Add(new SyntaxToken(quotePos, pos - quotePos, TokenClassification.String));
            inAttrValue = true;
        }

        return pos;
    }

    /// <summary>
    /// Scans a single-quoted attribute value (less common in JALXAML).
    /// </summary>
    private static int ScanAttributeValueSingleQuote(string text, int pos, List<SyntaxToken> tokens)
    {
        bool inAttrValue = false;
        return ScanQuotedAttributeValue(text, pos, pos + 1, '\'', tokens, ref inAttrValue);
    }

    /// <summary>
    /// Continues scanning inside an attribute value string (multi-line continuation).
    /// </summary>
    private static int ScanAttributeValueContinuation(string text, int pos, List<SyntaxToken> tokens,
        ref bool inAttrValue, ref int meDepth)
    {
        // We're inside a string that started on a previous line
        int start = pos;
        while (pos < text.Length && text[pos] != '"')
        {
            // Check for markup extension start inside continued string
            if (text[pos] == '{' && !(pos + 1 < text.Length && text[pos + 1] == '{'))
            {
                if (pos > start)
                    tokens.Add(new SyntaxToken(start, pos - start, TokenClassification.String));

                bool isBindingExtension = IsBindingExtensionAt(text, pos + 1);
                meDepth = 1;
                tokens.Add(new SyntaxToken(pos, 1,
                    isBindingExtension ? TokenClassification.BindingOperator : TokenClassification.Operator));
                pos++;
                inAttrValue = true;
                pos = ScanMarkupExtensionContent(text, pos, tokens, ref meDepth, ref inAttrValue, isBindingExtension);
                return pos;
            }
            pos++;
        }

        if (pos < text.Length)
        {
            // Include closing quote
            pos++;
            tokens.Add(new SyntaxToken(start, pos - start, TokenClassification.String));
            inAttrValue = false;
        }
        else
        {
            tokens.Add(new SyntaxToken(start, pos - start, TokenClassification.String));
            inAttrValue = true;
        }

        return pos;
    }

    /// <summary>
    /// Scans markup extension content after the opening '{'.
    /// Handles extension name, parameters, nested extensions, and closing '}'.
    /// </summary>
    private static int ScanMarkupExtensionContent(string text, int pos, List<SyntaxToken> tokens,
        ref int meDepth, ref bool inAttrValue, bool assumeBindingContext = false)
    {
        bool isBindingExtension = assumeBindingContext;

        // Skip whitespace
        pos = SkipWhitespace(text, pos);

        // Extension name (first identifier is Keyword)
        if (pos < text.Length && IsNameStartChar(text[pos]))
        {
            int nameStart = pos;
            pos = ScanNameChars(text, pos);

            // Check for x:Null, x:Type etc
            if (pos < text.Length && text[pos] == ':' && text[nameStart..pos] == "x")
            {
                pos++;
                pos = ScanNameChars(text, pos);
            }

            string extensionName = text[nameStart..pos];
            isBindingExtension = IsBindingExtensionName(extensionName);
            tokens.Add(new SyntaxToken(nameStart, pos - nameStart,
                isBindingExtension ? TokenClassification.BindingKeyword : TokenClassification.Keyword));
        }

        // Scan parameters
        pos = ScanMarkupExtensionParams(text, pos, tokens, ref meDepth, ref inAttrValue, isBindingExtension);

        return pos;
    }

    /// <summary>
    /// Scans a quoted attribute value and emits Razor-aware tokens for inline
    /// path/expression syntax while preserving the surrounding quote tokens.
    /// </summary>
    private static int ScanQuotedAttributeValue(
        string text,
        int quotePos,
        int pos,
        char quoteChar,
        List<SyntaxToken> tokens,
        ref bool inAttrValue)
    {
        tokens.Add(new SyntaxToken(quotePos, 1, TokenClassification.String));

        int contentStart = pos;
        bool sawRazor = false;

        while (pos < text.Length)
        {
            if (text[pos] == quoteChar)
            {
                EmitQuotedValueContent(text, contentStart, pos, tokens, sawRazor);
                tokens.Add(new SyntaxToken(pos, 1, TokenClassification.String));
                inAttrValue = false;
                return pos + 1;
            }

            if (TryGetRazorEscapeLength(text, pos, out int escapeLength))
            {
                pos += escapeLength;
                continue;
            }

            if (text[pos] == '@')
            {
                var razorTokens = new List<SyntaxToken>();
                int razorEnd = ScanRazorInline(text, pos, razorTokens, allowDirective: false);
                if (razorEnd > pos)
                {
                    EmitQuotedValueContent(text, contentStart, pos, tokens, sawRazor);
                    sawRazor = true;
                    tokens.AddRange(razorTokens);
                    pos = razorEnd;
                    contentStart = pos;
                    continue;
                }
            }

            pos++;
        }

        EmitQuotedValueContent(text, contentStart, pos, tokens, sawRazor);
        inAttrValue = true;
        return pos;
    }

    private static void EmitQuotedValueContent(
        string text,
        int start,
        int end,
        List<SyntaxToken> tokens,
        bool sawRazor)
    {
        if (end <= start)
            return;

        if (!sawRazor && IsNumericAttributeLiteral(text.AsSpan(start, end - start)))
        {
            tokens.Add(new SyntaxToken(start, end - start, TokenClassification.Number));
            return;
        }

        tokens.Add(new SyntaxToken(start, end - start, TokenClassification.String));
    }

    /// <summary>
    /// Scans markup extension parameters: positional values, named parameters (Name=Value), nested extensions.
    /// </summary>
    private static int ScanMarkupExtensionParams(string text, int pos, List<SyntaxToken> tokens,
        ref int meDepth, ref bool inAttrValue, bool inBindingContext)
    {
        bool bindingPositionalValueConsumed = false;

        while (pos < text.Length)
        {
            pos = SkipWhitespace(text, pos);
            if (pos >= text.Length) break;

            char c = text[pos];

            // Closing }
            if (c == '}')
            {
                tokens.Add(new SyntaxToken(pos, 1,
                    inBindingContext ? TokenClassification.BindingOperator : TokenClassification.Operator));
                pos++;
                meDepth--;

                if (meDepth <= 0)
                {
                    meDepth = 0;
                    // After closing the outermost ME, scan for the closing quote
                    if (pos < text.Length && text[pos] == '"')
                    {
                        tokens.Add(new SyntaxToken(pos, 1, TokenClassification.String));
                        pos++;
                        inAttrValue = false;
                    }
                    return pos;
                }
                return pos; // Return to parent ME scanning
            }

            // Nested markup extension
            if (c == '{')
            {
                bool nestedBindingContext = IsBindingExtensionAt(text, pos + 1);
                meDepth++;
                tokens.Add(new SyntaxToken(pos, 1,
                    nestedBindingContext ? TokenClassification.BindingOperator : TokenClassification.Operator));
                pos++;
                pos = ScanMarkupExtensionContent(text, pos, tokens, ref meDepth, ref inAttrValue, nestedBindingContext);
                continue;
            }

            // Comma separator
            if (c == ',')
            {
                tokens.Add(new SyntaxToken(pos, 1,
                    inBindingContext ? TokenClassification.BindingOperator : TokenClassification.Operator));
                pos++;
                continue;
            }

            // Closing quote (unexpected but handle gracefully)
            if (c == '"')
            {
                tokens.Add(new SyntaxToken(pos, 1, TokenClassification.String));
                pos++;
                meDepth = 0;
                inAttrValue = false;
                return pos;
            }

            // Identifier — could be named parameter or value
            if (IsNameStartChar(c))
            {
                int idStart = pos;
                pos = ScanNameChars(text, pos);
                string identifier = text[idStart..pos];

                // Check if followed by '=' (named parameter)
                int peekPos = SkipWhitespace(text, pos);
                if (peekPos < text.Length && text[peekPos] == '=')
                {
                    // Check it's not followed by '{' which would be a nested ME value
                    // Named parameter: Name=Value
                    tokens.Add(new SyntaxToken(idStart, pos - idStart,
                        inBindingContext && IsBindingParameterName(identifier)
                            ? TokenClassification.BindingParameter
                            : TokenClassification.Property));
                    pos = peekPos;
                    tokens.Add(new SyntaxToken(pos, 1,
                        inBindingContext ? TokenClassification.BindingOperator : TokenClassification.Operator)); // '='
                    pos++;

                    pos = SkipWhitespace(text, pos);

                    // Value: could be nested ME, string literal, or plain value
                    if (pos < text.Length && text[pos] == '{')
                    {
                        bool nestedBindingContext = IsBindingExtensionAt(text, pos + 1);
                        meDepth++;
                        tokens.Add(new SyntaxToken(pos, 1,
                            nestedBindingContext ? TokenClassification.BindingOperator : TokenClassification.Operator));
                        pos++;
                        pos = ScanMarkupExtensionContent(text, pos, tokens, ref meDepth, ref inAttrValue, nestedBindingContext);
                    }
                    else if (pos < text.Length && text[pos] == '\'')
                    {
                        // Quoted value in ME
                        int qStart = pos;
                        pos++;
                        while (pos < text.Length && text[pos] != '\'')
                            pos++;
                        if (pos < text.Length) pos++;
                        tokens.Add(new SyntaxToken(qStart, pos - qStart, TokenClassification.String));
                    }
                    else
                    {
                        // Plain value (up to , or } or whitespace)
                        var valueClassification = inBindingContext && IsBindingPathParameter(identifier)
                            ? TokenClassification.BindingPath
                            : TokenClassification.PlainText;
                        pos = ScanMarkupExtensionPlainValue(text, pos, tokens, valueClassification);
                    }
                }
                else
                {
                    if (inBindingContext && !bindingPositionalValueConsumed)
                    {
                        pos = ScanMarkupExtensionPlainValue(text, idStart, tokens, TokenClassification.BindingPath);
                        bindingPositionalValueConsumed = true;
                        continue;
                    }

                    // Positional value or type reference
                    // Check if it looks like a type name (contains '.')
                    if (pos < text.Length && text[pos] == '.')
                    {
                        // Could be Type.Member reference — scan the rest
                        pos = ScanNameChars(text, pos + 1);
                        tokens.Add(new SyntaxToken(idStart, pos - idStart, TokenClassification.TypeName));
                    }
                    else if (pos < text.Length && text[pos] == ':')
                    {
                        // Namespace prefix in value (e.g., local:MyType)
                        tokens.Add(new SyntaxToken(idStart, pos - idStart, TokenClassification.Namespace));
                        tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Punctuation));
                        pos++;
                        int localStart = pos;
                        pos = ScanNameChars(text, pos);
                        if (pos > localStart)
                            tokens.Add(new SyntaxToken(localStart, pos - localStart, TokenClassification.TypeName));
                    }
                    else
                    {
                        // Plain positional value
                        tokens.Add(new SyntaxToken(idStart, pos - idStart, TokenClassification.PlainText));
                    }
                }
                continue;
            }

            // Number or other literal
            if (char.IsDigit(c) || c == '-' || c == '.')
            {
                int numStart = pos;
                while (pos < text.Length && (char.IsDigit(text[pos]) || text[pos] == '.' || text[pos] == '-'))
                    pos++;
                tokens.Add(new SyntaxToken(numStart, pos - numStart, TokenClassification.Number));
                continue;
            }

            // Skip any other character
            pos++;
        }

        return pos;
    }

    /// <summary>
    /// Scans a plain value inside a markup extension (up to , } or whitespace before } or ,).
    /// </summary>
    private static int ScanMarkupExtensionPlainValue(string text, int pos, List<SyntaxToken> tokens,
        TokenClassification classification = TokenClassification.PlainText)
    {
        int start = pos;
        while (pos < text.Length)
        {
            char c = text[pos];
            if (c == ',' || c == '}' || c == '"') break;
            pos++;
        }

        if (pos > start)
        {
            // Trim trailing whitespace from the token
            int end = pos;
            while (end > start && char.IsWhiteSpace(text[end - 1]))
                end--;

            if (end > start)
                tokens.Add(new SyntaxToken(start, end - start, classification));
        }

        return pos;
    }

    /// <summary>
    /// Scans text content outside tags and extracts Razor path / expression / directive tokens.
    /// </summary>
    private static int ScanTextContent(string text, int pos, List<SyntaxToken> tokens)
    {
        int segmentStart = pos;

        while (pos < text.Length && text[pos] != '<')
        {
            if (TryGetRazorEscapeLength(text, pos, out int escapeLength))
            {
                pos += escapeLength;
                continue;
            }

            if (text[pos] == '@')
            {
                var razorTokens = new List<SyntaxToken>();
                int razorEnd = ScanRazorInline(text, pos, razorTokens, allowDirective: true);
                if (razorEnd > pos)
                {
                    if (pos > segmentStart)
                        tokens.Add(new SyntaxToken(segmentStart, pos - segmentStart, TokenClassification.PlainText));

                    tokens.AddRange(razorTokens);
                    pos = razorEnd;
                    segmentStart = pos;
                    continue;
                }
            }

            if (text[pos] is '{' or '}')
            {
                if (pos > segmentStart)
                    tokens.Add(new SyntaxToken(segmentStart, pos - segmentStart, TokenClassification.PlainText));

                tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Operator));
                pos++;
                segmentStart = pos;
                continue;
            }

            pos++;
        }

        if (pos > segmentStart)
            tokens.Add(new SyntaxToken(segmentStart, pos - segmentStart, TokenClassification.PlainText));

        return pos;
    }

    /// <summary>
    /// Scans text content with Razor brace-depth tracking so that multi-line Razor code blocks
    /// receive C# highlighting. When a Razor directive opens a <c>{</c>, the brace depth increments
    /// and subsequent content is highlighted as C# until all braces close.
    /// </summary>
    private static int ScanTextContentWithRazor(
        string text, int pos, List<SyntaxToken> tokens,
        ref int razorBraceDepth, ref bool inRazorBlockComment,
        ref bool razorPendingBlock)
    {
        int segmentStart = pos;

        while (pos < text.Length && text[pos] != '<')
        {
            if (TryGetRazorEscapeLength(text, pos, out int escapeLength))
            {
                pos += escapeLength;
                continue;
            }

            if (text[pos] == '@')
            {
                // Flush plain text before @
                if (pos > segmentStart)
                {
                    tokens.Add(new SyntaxToken(segmentStart, pos - segmentStart, TokenClassification.PlainText));
                    segmentStart = pos;
                }

                var razorTokens = new List<SyntaxToken>();
                bool pendingBlock = false;
                int razorEnd = ScanRazorInlineWithBraceTracking(text, pos, razorTokens, ref razorBraceDepth, ref inRazorBlockComment, ref pendingBlock);
                if (pendingBlock)
                    razorPendingBlock = true;
                if (razorEnd > pos)
                {
                    tokens.AddRange(razorTokens);
                    pos = razorEnd;
                    segmentStart = pos;

                    // If we entered a Razor code block, continue scanning as C# on this line
                    if (razorBraceDepth > 0)
                    {
                        pos = ScanRazorCodeBlockMultiLine(text, pos, tokens, ref razorBraceDepth, ref inRazorBlockComment, ref razorPendingBlock);
                        segmentStart = pos;
                    }

                    continue;
                }
            }

            if (text[pos] is '{' or '}')
            {
                if (pos > segmentStart)
                    tokens.Add(new SyntaxToken(segmentStart, pos - segmentStart, TokenClassification.PlainText));

                tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Operator));
                pos++;
                segmentStart = pos;
                continue;
            }

            pos++;
        }

        if (pos > segmentStart)
            tokens.Add(new SyntaxToken(segmentStart, pos - segmentStart, TokenClassification.PlainText));

        return pos;
    }

    /// <summary>
    /// Like <see cref="ScanRazorInline"/> but tracks brace depth for multi-line block support.
    /// After scanning <c>@keyword(...)</c>, if a <c>{</c> follows, it increments <paramref name="razorBraceDepth"/>
    /// instead of trying to scan the entire block on one line.
    /// </summary>
    private static int ScanRazorInlineWithBraceTracking(
        string text, int pos, List<SyntaxToken> tokens,
        ref int razorBraceDepth, ref bool inRazorBlockComment,
        ref bool razorPendingBlock)
    {
        if (pos >= text.Length || text[pos] != '@')
            return pos;

        if (TryGetRazorEscapeLength(text, pos, out _))
            return pos;

        // @(expression) — single-line, no brace tracking needed
        if (pos + 1 < text.Length && text[pos + 1] == '(')
        {
            tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Operator));
            tokens.Add(new SyntaxToken(pos + 1, 1, TokenClassification.Punctuation));
            return ScanRazorExpression(text, pos + 2, tokens, initialParenDepth: 1);
        }

        // @{ code block — track brace depth
        if (pos + 1 < text.Length && text[pos + 1] == '{')
        {
            tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Operator));
            tokens.Add(new SyntaxToken(pos + 1, 1, TokenClassification.Punctuation));
            razorBraceDepth = 1;
            return pos + 2;
        }

        // @* comment *@
        if (pos + 1 < text.Length && text[pos + 1] == '*')
        {
            int commentStart = pos;
            int commentEnd = text.IndexOf("*@", pos + 2, StringComparison.Ordinal);
            if (commentEnd >= 0)
            {
                tokens.Add(new SyntaxToken(commentStart, commentEnd + 2 - commentStart, TokenClassification.Comment));
                return commentEnd + 2;
            }
            else
            {
                tokens.Add(new SyntaxToken(commentStart, text.Length - commentStart, TokenClassification.Comment));
                return text.Length;
            }
        }

        // @directive with brace tracking
        if (pos + 1 < text.Length && IsRazorIdentifierStart(text[pos + 1]))
        {
            int keywordStart = pos + 1;
            int keywordEnd = ScanRazorIdentifier(text, keywordStart);
            string keyword = text[keywordStart..keywordEnd];

            if (s_razorDirectiveKeywords.Contains(keyword))
            {
                tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Operator));
                tokens.Add(new SyntaxToken(keywordStart, keywordEnd - keywordStart,
                    s_razorExpressionControlKeywords.Contains(keyword)
                        ? TokenClassification.ControlKeyword
                        : TokenClassification.Keyword));

                int cursor = keywordEnd;

                // Scan parenthesized condition if present
                if (keyword is "if" or "for" or "foreach" or "while" or "switch" or "using" or "lock" or "catch")
                {
                    while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
                        cursor++;

                    if (cursor < text.Length && text[cursor] == '(')
                    {
                        tokens.Add(new SyntaxToken(cursor, 1, TokenClassification.Punctuation));
                        cursor = ScanRazorExpression(text, cursor + 1, tokens, initialParenDepth: 1);
                    }
                }

                // Scan optional section name for @section
                if (keyword == "section")
                {
                    while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
                        cursor++;
                    int nameStart = cursor;
                    while (cursor < text.Length && (char.IsLetterOrDigit(text[cursor]) || text[cursor] == '_'))
                        cursor++;
                    if (cursor > nameStart)
                        tokens.Add(new SyntaxToken(nameStart, cursor - nameStart, TokenClassification.Identifier));
                }

                // Skip to opening brace
                while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
                    cursor++;

                if (cursor < text.Length && text[cursor] == '{')
                {
                    tokens.Add(new SyntaxToken(cursor, 1, TokenClassification.Punctuation));
                    cursor++;
                    razorBraceDepth = 1;
                }
                else
                {
                    // { is on the next line — mark pending
                    razorPendingBlock = true;
                }

                return cursor;
            }
        }

        // @path
        if (pos + 1 < text.Length && TryScanRazorPath(text, pos + 1, out int pathEnd))
        {
            tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Operator));
            tokens.Add(new SyntaxToken(pos + 1, pathEnd - (pos + 1), TokenClassification.BindingPath));
            return pathEnd;
        }

        return pos;
    }

    /// <summary>
    /// Scans C# code inside a multi-line Razor code block, tracking brace depth.
    /// When depth reaches 0, checks for continuation keywords (else, catch, finally)
    /// and re-enters code block mode if found.
    /// </summary>
    private static int ScanRazorCodeBlockMultiLine(
        string text, int pos, List<SyntaxToken> tokens,
        ref int razorBraceDepth, ref bool inRazorBlockComment,
        ref bool razorPendingBlock)
    {
        while (pos < text.Length)
        {
            char c = text[pos];

            // Whitespace
            if (char.IsWhiteSpace(c))
            {
                int ws = pos;
                while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                    pos++;
                tokens.Add(new SyntaxToken(ws, pos - ws, TokenClassification.PlainText));
                continue;
            }

            // Line comment
            if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
            {
                tokens.Add(new SyntaxToken(pos, text.Length - pos, TokenClassification.Comment));
                pos = text.Length;
                return pos;
            }

            // Block comment
            if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '*')
            {
                int commentStart = pos;
                int endIdx = text.IndexOf("*/", pos + 2, StringComparison.Ordinal);
                if (endIdx >= 0)
                {
                    tokens.Add(new SyntaxToken(commentStart, endIdx + 2 - commentStart, TokenClassification.Comment));
                    pos = endIdx + 2;
                }
                else
                {
                    tokens.Add(new SyntaxToken(commentStart, text.Length - commentStart, TokenClassification.Comment));
                    inRazorBlockComment = true;
                    pos = text.Length;
                }
                continue;
            }

            // String literals
            if (c is '"' or '\'')
            {
                int stringStart = pos;
                char quote = c;
                pos++;
                while (pos < text.Length)
                {
                    if (text[pos] == '\\')
                    {
                        pos += 2;
                        continue;
                    }
                    if (text[pos] == quote)
                    {
                        pos++;
                        break;
                    }
                    pos++;
                }
                tokens.Add(new SyntaxToken(stringStart, pos - stringStart, TokenClassification.String));
                continue;
            }

            // Identifiers and keywords
            if (IsRazorIdentifierStart(c))
            {
                int idStart = pos;
                int idEnd = ScanRazorIdentifier(text, pos);
                string identifier = text[idStart..idEnd];

                int lookahead = idEnd;
                while (lookahead < text.Length && char.IsWhiteSpace(text[lookahead]))
                    lookahead++;

                var classification = s_razorExpressionControlKeywords.Contains(identifier)
                    ? TokenClassification.ControlKeyword
                    : s_razorCodeKeywords.Contains(identifier)
                        ? TokenClassification.Keyword
                        : lookahead < text.Length && text[lookahead] == '('
                            ? TokenClassification.Method
                            : TokenClassification.Identifier;

                tokens.Add(new SyntaxToken(idStart, idEnd - idStart, classification));
                pos = idEnd;
                continue;
            }

            // Numbers
            if (char.IsDigit(c) || (c == '-' && pos + 1 < text.Length && char.IsDigit(text[pos + 1])))
            {
                int numStart = pos;
                pos++;
                while (pos < text.Length && (char.IsDigit(text[pos]) || text[pos] is '.' or '_' or 'e' or 'E'
                       or '+' or '-' or 'f' or 'F' or 'd' or 'D' or 'm' or 'M' or 'l' or 'L' or 'u' or 'U'))
                    pos++;
                tokens.Add(new SyntaxToken(numStart, pos - numStart, TokenClassification.Number));
                continue;
            }

            // Braces
            if (c == '{')
            {
                tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Punctuation));
                pos++;
                razorBraceDepth++;
                continue;
            }

            if (c == '}')
            {
                tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Punctuation));
                pos++;
                razorBraceDepth--;
                if (razorBraceDepth <= 0)
                {
                    razorBraceDepth = 0;
                    // Check for continuation keyword after }
                    pos = TryScanRazorContinuation(text, pos, tokens, ref razorBraceDepth, ref razorPendingBlock);
                    if (razorBraceDepth <= 0)
                        return pos;
                }
                continue;
            }

            // Punctuation
            if (c is '(' or ')' or '[' or ']' or ',' or '.' or ';')
            {
                tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Punctuation));
                pos++;
                continue;
            }

            // Operators
            if (c is '?' or ':' or '+' or '-' or '*' or '/' or '%' or '=' or '!' or '<' or '>' or '&' or '|' or '^' or '~')
            {
                int opStart = pos;
                pos++;
                while (pos < text.Length && text[pos] is '=' or '&' or '|' or '>' or '<' or '?')
                    pos++;
                tokens.Add(new SyntaxToken(opStart, pos - opStart, TokenClassification.Operator));
                continue;
            }

            // @ inside code block (nested Razor)
            if (c == '@')
            {
                tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Operator));
                pos++;
                continue;
            }

            tokens.Add(new SyntaxToken(pos, 1, TokenClassification.PlainText));
            pos++;
        }

        return pos;
    }

    /// <summary>
    /// After a closing <c>}</c> at Razor depth 0, checks for continuation keywords
    /// (else, catch, finally, while) and if found, scans them and re-enters the code block.
    /// </summary>
    private static int TryScanRazorContinuation(
        string text, int pos, List<SyntaxToken> tokens, ref int razorBraceDepth, ref bool razorPendingBlock)
    {
        // Save position for rollback
        int savedPos = pos;

        // Skip whitespace (including newline within the same line scan)
        int wsStart = pos;
        while (pos < text.Length && char.IsWhiteSpace(text[pos]))
            pos++;
        if (pos > wsStart)
            tokens.Add(new SyntaxToken(wsStart, pos - wsStart, TokenClassification.PlainText));

        // Check for continuation keyword
        if (pos < text.Length && IsRazorIdentifierStart(text[pos]))
        {
            int kwStart = pos;
            int kwEnd = ScanRazorIdentifier(text, pos);
            string keyword = text[kwStart..kwEnd];

            if (s_razorContinuationKeywords.Contains(keyword))
            {
                tokens.Add(new SyntaxToken(kwStart, kwEnd - kwStart, TokenClassification.ControlKeyword));
                int cursor = kwEnd;

                // Scan condition clause for catch(...), else if(...), while(...)
                while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
                    cursor++;

                // "else if" pattern
                if (keyword == "else" && cursor < text.Length && IsRazorIdentifierStart(text[cursor]))
                {
                    int nextKwStart = cursor;
                    int nextKwEnd = ScanRazorIdentifier(text, cursor);
                    string nextKw = text[nextKwStart..nextKwEnd];
                    if (nextKw == "if")
                    {
                        tokens.Add(new SyntaxToken(nextKwStart, nextKwEnd - nextKwStart, TokenClassification.ControlKeyword));
                        cursor = nextKwEnd;
                        while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
                            cursor++;
                    }
                }

                if (cursor < text.Length && text[cursor] == '(')
                {
                    tokens.Add(new SyntaxToken(cursor, 1, TokenClassification.Punctuation));
                    cursor = ScanRazorExpression(text, cursor + 1, tokens, initialParenDepth: 1);
                }

                // Skip to {
                while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
                    cursor++;

                if (cursor < text.Length && text[cursor] == '{')
                {
                    tokens.Add(new SyntaxToken(cursor, 1, TokenClassification.Punctuation));
                    cursor++;
                    razorBraceDepth = 1;
                }
                // do { } while(expr); — while without { is a terminator, not continuation
                else if (keyword == "while")
                {
                    while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
                        cursor++;
                    if (cursor < text.Length && text[cursor] == ';')
                    {
                        tokens.Add(new SyntaxToken(cursor, 1, TokenClassification.Punctuation));
                        cursor++;
                    }
                }
                else
                {
                    // { is on the next line
                    razorPendingBlock = true;
                }

                return cursor;
            }
        }

        // No continuation found — already added whitespace tokens, just return
        return pos;
    }

    private static int ScanRazorInline(string text, int pos, List<SyntaxToken> tokens, bool allowDirective)
    {
        if (pos >= text.Length || text[pos] != '@')
            return pos;

        if (TryGetRazorEscapeLength(text, pos, out _))
            return pos;

        if (pos + 1 < text.Length && text[pos + 1] == '(')
        {
            tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Operator));
            tokens.Add(new SyntaxToken(pos + 1, 1, TokenClassification.Punctuation));
            return ScanRazorExpression(text, pos + 2, tokens, initialParenDepth: 1);
        }

        if (pos + 1 < text.Length && text[pos + 1] == '{')
        {
            tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Operator));
            tokens.Add(new SyntaxToken(pos + 1, 1, TokenClassification.Punctuation));
            return ScanRazorCodeBlock(text, pos + 2, tokens, initialBraceDepth: 1);
        }

        if (allowDirective && TryScanRazorDirective(text, pos, tokens, out int directiveEnd))
            return directiveEnd;

        if (TryScanRazorPath(text, pos + 1, out int pathEnd))
        {
            tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Operator));
            tokens.Add(new SyntaxToken(pos + 1, pathEnd - (pos + 1), TokenClassification.BindingPath));
            return pathEnd;
        }

        return pos;
    }

    private static int ScanRazorCodeBlock(string text, int pos, List<SyntaxToken> tokens, int initialBraceDepth)
    {
        int braceDepth = initialBraceDepth;

        while (pos < text.Length)
        {
            char c = text[pos];

            if (char.IsWhiteSpace(c))
            {
                int whitespaceStart = pos;
                while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                    pos++;

                tokens.Add(new SyntaxToken(whitespaceStart, pos - whitespaceStart, TokenClassification.PlainText));
                continue;
            }

            if (c == '"' || c == '\'')
            {
                int stringStart = pos;
                char quote = c;
                pos++;

                bool escaped = false;
                while (pos < text.Length)
                {
                    char inner = text[pos];
                    if (escaped)
                    {
                        escaped = false;
                        pos++;
                        continue;
                    }

                    if (inner == '\\')
                    {
                        escaped = true;
                        pos++;
                        continue;
                    }

                    pos++;
                    if (inner == quote)
                        break;
                }

                tokens.Add(new SyntaxToken(stringStart, pos - stringStart, TokenClassification.String));
                continue;
            }

            if (TryScanRazorPath(text, pos, out int identifierEnd))
            {
                string identifier = text[pos..identifierEnd];
                int lookahead = identifierEnd;
                while (lookahead < text.Length && char.IsWhiteSpace(text[lookahead]))
                    lookahead++;

                var classification = s_razorExpressionControlKeywords.Contains(identifier)
                    ? TokenClassification.ControlKeyword
                    : s_razorCodeKeywords.Contains(identifier)
                        ? TokenClassification.Keyword
                        : lookahead < text.Length && text[lookahead] == '('
                            ? TokenClassification.Method
                            : TokenClassification.Identifier;

                tokens.Add(new SyntaxToken(pos, identifierEnd - pos, classification));
                pos = identifierEnd;
                continue;
            }

            if (char.IsDigit(c) || (c == '-' && pos + 1 < text.Length && char.IsDigit(text[pos + 1])))
            {
                int numberStart = pos;
                pos++;
                while (pos < text.Length && (char.IsDigit(text[pos]) || text[pos] == '.' ||
                       text[pos] == '_' || text[pos] == 'e' || text[pos] == 'E' ||
                       text[pos] == '+' || text[pos] == '-'))
                {
                    pos++;
                }

                tokens.Add(new SyntaxToken(numberStart, pos - numberStart, TokenClassification.Number));
                continue;
            }

            if (c == '{')
            {
                tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Punctuation));
                pos++;
                braceDepth++;
                continue;
            }

            if (c == '}')
            {
                tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Punctuation));
                pos++;
                braceDepth--;
                if (braceDepth <= 0)
                    return pos;
                continue;
            }

            if (c is '(' or ')' or '[' or ']' or ',' or '.' or ';')
            {
                tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Punctuation));
                pos++;
                continue;
            }

            if (c is '?' or ':' or '+' or '-' or '*' or '/' or '%' or '=' or '!' or '<' or '>' or '&' or '|' or '^')
            {
                int operatorStart = pos;
                pos++;
                while (pos < text.Length && text[pos] is '=' or '&' or '|' or '>' or '<')
                    pos++;

                tokens.Add(new SyntaxToken(operatorStart, pos - operatorStart, TokenClassification.Operator));
                continue;
            }

            tokens.Add(new SyntaxToken(pos, 1, TokenClassification.PlainText));
            pos++;
        }

        return pos;
    }

    private static bool TryScanRazorDirective(string text, int pos, List<SyntaxToken> tokens, out int end)
    {
        end = pos;
        if (pos + 1 >= text.Length || text[pos] != '@' || !IsRazorIdentifierStart(text[pos + 1]))
            return false;

        int keywordStart = pos + 1;
        int keywordEnd = ScanRazorIdentifier(text, keywordStart);
        string keyword = text[keywordStart..keywordEnd];
        if (!s_razorDirectiveKeywords.Contains(keyword))
            return false;

        tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Operator));
        tokens.Add(new SyntaxToken(keywordStart, keywordEnd - keywordStart,
            s_razorExpressionControlKeywords.Contains(keyword)
                ? TokenClassification.ControlKeyword
                : TokenClassification.Keyword));

        int cursor = keywordEnd;
        if (keyword is "if" or "for" or "foreach" or "while" or "switch" or "using" or "lock" or "catch")
        {
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
                cursor++;

            if (cursor < text.Length && text[cursor] == '(')
            {
                tokens.Add(new SyntaxToken(cursor, 1, TokenClassification.Punctuation));
                cursor = ScanRazorExpression(text, cursor + 1, tokens, initialParenDepth: 1);
            }
        }

        while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
            cursor++;

        if (cursor < text.Length && text[cursor] == '{')
        {
            tokens.Add(new SyntaxToken(cursor, 1, TokenClassification.Operator));
            cursor++;
        }

        end = cursor;
        return true;
    }

    private static int ScanRazorExpression(string text, int pos, List<SyntaxToken> tokens, int initialParenDepth)
    {
        int parenDepth = initialParenDepth;

        while (pos < text.Length)
        {
            char c = text[pos];

            if (char.IsWhiteSpace(c))
            {
                int whitespaceStart = pos;
                while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                    pos++;

                tokens.Add(new SyntaxToken(whitespaceStart, pos - whitespaceStart, TokenClassification.PlainText));
                continue;
            }

            if (c == '"' || c == '\'')
            {
                int stringStart = pos;
                char quote = c;
                pos++;

                bool escaped = false;
                while (pos < text.Length)
                {
                    char inner = text[pos];
                    if (escaped)
                    {
                        escaped = false;
                        pos++;
                        continue;
                    }

                    if (inner == '\\')
                    {
                        escaped = true;
                        pos++;
                        continue;
                    }

                    pos++;
                    if (inner == quote)
                        break;
                }

                tokens.Add(new SyntaxToken(stringStart, pos - stringStart, TokenClassification.String));
                continue;
            }

            if (TryScanRazorPath(text, pos, out int pathEnd))
            {
                string identifier = text[pos..pathEnd];
                var classification = s_razorExpressionControlKeywords.Contains(identifier)
                    ? TokenClassification.ControlKeyword
                    : s_razorExpressionKeywords.Contains(identifier)
                        ? TokenClassification.Keyword
                        : TokenClassification.BindingPath;
                tokens.Add(new SyntaxToken(pos, pathEnd - pos, classification));
                pos = pathEnd;
                continue;
            }

            if (char.IsDigit(c) || (c == '-' && pos + 1 < text.Length && char.IsDigit(text[pos + 1])))
            {
                int numberStart = pos;
                pos++;
                while (pos < text.Length && (char.IsDigit(text[pos]) || text[pos] == '.' ||
                       text[pos] == '_' || text[pos] == 'e' || text[pos] == 'E' ||
                       text[pos] == '+' || text[pos] == '-'))
                {
                    pos++;
                }

                tokens.Add(new SyntaxToken(numberStart, pos - numberStart, TokenClassification.Number));
                continue;
            }

            if (c == '(')
            {
                tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Punctuation));
                pos++;
                parenDepth++;
                continue;
            }

            if (c == ')')
            {
                tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Punctuation));
                pos++;
                parenDepth--;
                if (parenDepth <= 0)
                    return pos;
                continue;
            }

            if (c is '[' or ']' or '{' or '}' or ',' or '.')
            {
                tokens.Add(new SyntaxToken(pos, 1, TokenClassification.Punctuation));
                pos++;
                continue;
            }

            if (c is '?' or ':' or '+' or '-' or '*' or '/' or '%' or '=' or '!' or '<' or '>' or '&' or '|' or '^')
            {
                int operatorStart = pos;
                pos++;
                while (pos < text.Length && text[pos] is '=' or '&' or '|' or '>' or '<')
                    pos++;

                tokens.Add(new SyntaxToken(operatorStart, pos - operatorStart, TokenClassification.Operator));
                continue;
            }

            tokens.Add(new SyntaxToken(pos, 1, TokenClassification.PlainText));
            pos++;
        }

        return pos;
    }

    #region Helpers

    private static bool TryGetRazorEscapeLength(string text, int pos, out int escapeLength)
    {
        escapeLength = 0;
        if (pos < 0 || pos >= text.Length)
            return false;

        if (text[pos] == '\\' && pos + 1 < text.Length && text[pos + 1] == '@')
        {
            escapeLength = 2;
            return true;
        }

        if (text[pos] == '@' && pos + 1 < text.Length && text[pos + 1] == '@')
        {
            escapeLength = 2;
            return true;
        }

        return false;
    }

    private static int SkipWhitespace(string text, int pos)
    {
        while (pos < text.Length && char.IsWhiteSpace(text[pos]))
            pos++;
        return pos;
    }

    private static int ScanNameChars(string text, int pos)
    {
        while (pos < text.Length && IsNameChar(text[pos]))
            pos++;
        return pos;
    }

    private static bool TryScanRazorPath(string text, int pos, out int end)
    {
        end = pos;
        if (pos >= text.Length || !IsRazorIdentifierStart(text[pos]))
            return false;

        end = pos + 1;
        while (end < text.Length && IsRazorIdentifierPart(text[end]))
            end++;
        return true;
    }

    private static int ScanRazorIdentifier(string text, int pos)
    {
        while (pos < text.Length && (char.IsLetter(text[pos]) || text[pos] == '_'))
            pos++;
        return pos;
    }

    private static bool IsRazorIdentifierStart(char c) =>
        c == '_' || c == '$' || char.IsLetter(c);

    private static bool IsRazorIdentifierPart(char c) =>
        char.IsLetterOrDigit(c) || c is '.' or '_' or '[' or ']' or '$';

    private static bool IsNameStartChar(char c) =>
        char.IsLetter(c) || c == '_';

    private static bool IsNameChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_' || c == '-';

    private static bool IsBindingExtensionName(string name) =>
        s_bindingExtensionNames.Contains(name);

    private static bool IsBindingParameterName(string name) =>
        s_bindingParameterNames.Contains(name);

    private static bool IsBindingPathParameter(string name) =>
        name is "Path" or "XPath";

    private static bool IsBindingExtensionAt(string text, int pos)
    {
        pos = SkipWhitespace(text, pos);
        if (pos >= text.Length || !IsNameStartChar(text[pos]))
            return false;

        int start = pos;
        pos = ScanNameChars(text, pos);

        if (pos < text.Length && text[pos] == ':' && text[start..pos] == "x")
        {
            pos++;
            pos = ScanNameChars(text, pos);
        }

        return IsBindingExtensionName(text[start..pos]);
    }

    private static bool TryEmitNumericQuotedValueTokens(string text, int openingQuotePos, int closingQuotePos,
        List<SyntaxToken> tokens)
    {
        int valueStart = openingQuotePos + 1;
        int valueLength = closingQuotePos - valueStart;
        if (valueLength <= 0)
            return false;

        if (!IsNumericAttributeLiteral(text.AsSpan(valueStart, valueLength)))
            return false;

        tokens.Add(new SyntaxToken(openingQuotePos, 1, TokenClassification.String));
        tokens.Add(new SyntaxToken(valueStart, valueLength, TokenClassification.Number));
        tokens.Add(new SyntaxToken(closingQuotePos, 1, TokenClassification.String));
        return true;
    }

    private static bool IsNumericAttributeLiteral(ReadOnlySpan<char> value)
    {
        int pos = 0;
        bool sawComponent = false;

        while (pos < value.Length)
        {
            while (pos < value.Length && char.IsWhiteSpace(value[pos]))
                pos++;

            if (pos >= value.Length)
                break;

            int componentStart = pos;
            if (!IsNumericComponent(value, ref pos))
                return false;

            sawComponent = pos > componentStart;

            while (pos < value.Length && char.IsWhiteSpace(value[pos]))
                pos++;

            if (pos >= value.Length)
                break;

            if (value[pos] != ',')
                return false;

            pos++; // skip comma
        }

        return sawComponent;
    }

    private static bool IsNumericComponent(ReadOnlySpan<char> value, ref int pos)
    {
        if (pos < value.Length && (value[pos] == '+' || value[pos] == '-'))
            pos++;

        bool hasIntegerDigits = false;
        while (pos < value.Length && char.IsDigit(value[pos]))
        {
            hasIntegerDigits = true;
            pos++;
        }

        bool hasFractionDigits = false;
        if (pos < value.Length && value[pos] == '.')
        {
            pos++;
            while (pos < value.Length && char.IsDigit(value[pos]))
            {
                hasFractionDigits = true;
                pos++;
            }
        }

        if (!hasIntegerDigits && !hasFractionDigits)
            return false;

        if (pos < value.Length && (value[pos] == 'e' || value[pos] == 'E'))
        {
            int exponentPos = pos + 1;
            if (exponentPos < value.Length && (value[exponentPos] == '+' || value[exponentPos] == '-'))
                exponentPos++;

            int exponentDigits = 0;
            while (exponentPos < value.Length && char.IsDigit(value[exponentPos]))
            {
                exponentPos++;
                exponentDigits++;
            }

            if (exponentDigits == 0)
                return false;

            pos = exponentPos;
        }

        return true;
    }

    /// <summary>
    /// Fills gaps in the token list with PlainText tokens and returns sorted array.
    /// </summary>
    private static SyntaxToken[] FillPlainText(List<SyntaxToken> tokens, int lineLength)
    {
        if (tokens.Count == 0)
        {
            return lineLength > 0
                ? [new SyntaxToken(0, lineLength, TokenClassification.PlainText)]
                : [];
        }

        tokens.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));

        var result = new List<SyntaxToken>(tokens.Count + 2);
        int lastEnd = 0;

        foreach (var token in tokens)
        {
            if (token.StartOffset > lastEnd)
                result.Add(new SyntaxToken(lastEnd, token.StartOffset - lastEnd, TokenClassification.PlainText));
            if (token.Length > 0)
                result.Add(token);
            int tokenEnd = token.StartOffset + token.Length;
            if (tokenEnd > lastEnd)
                lastEnd = tokenEnd;
        }

        if (lastEnd < lineLength)
            result.Add(new SyntaxToken(lastEnd, lineLength - lastEnd, TokenClassification.PlainText));

        return result.ToArray();
    }

    #endregion
}
