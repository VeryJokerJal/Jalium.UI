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

    public static readonly JalxamlHighlighterState Default = new();

    public override bool Equals(object? obj) =>
        obj is JalxamlHighlighterState other &&
        InComment == other.InComment &&
        InTag == other.InTag &&
        InAttributeValue == other.InAttributeValue &&
        MarkupExtensionDepth == other.MarkupExtensionDepth;

    public override int GetHashCode() =>
        HashCode.Combine(InComment, InTag, InAttributeValue, MarkupExtensionDepth);
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

            // Text content outside tags
            int textStart = pos;
            while (pos < lineText.Length && lineText[pos] != '<')
                pos++;
            if (pos > textStart)
                tokens.Add(new SyntaxToken(textStart, pos - textStart, TokenClassification.PlainText));
        }

        var endState = new JalxamlHighlighterState
        {
            InComment = inComment,
            InTag = inTag,
            InAttributeValue = inAttrValue,
            MarkupExtensionDepth = meDepth
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
        return ScanPlainStringValue(text, quotePos, pos, tokens, ref inAttrValue);
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
        int start = pos;
        pos++; // skip opening '
        while (pos < text.Length && text[pos] != '\'')
            pos++;
        if (pos < text.Length)
        {
            int closingQuotePos = pos;
            pos++;
            if (!TryEmitNumericQuotedValueTokens(text, start, closingQuotePos, tokens))
                tokens.Add(new SyntaxToken(start, pos - start, TokenClassification.String));
        }
        else
        {
            tokens.Add(new SyntaxToken(start, pos - start, TokenClassification.String));
        }

        return pos;
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

    #region Helpers

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
