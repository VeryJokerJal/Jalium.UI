namespace Jalium.UI.Styling;

/// <summary>
/// Parses style-sheet text into rules following the CSS error-recovery model: a malformed
/// declaration is skipped up to the next ';' at the same nesting level, a malformed
/// selector drops the whole rule, and at-rules are skipped as a balanced block. Never throws.
/// </summary>
internal static class CssParser
{
    public static CssStyleSheet Parse(string cssText, string? sourceLabel)
    {
        var diagnostics = new List<CssParseDiagnostic>();
        var rules = new List<CssRule>();
        var text = cssText.AsSpan();
        var pos = 0;
        var line = 1;

        while (true)
        {
            SkipWhitespaceAndComments(text, ref pos, ref line);
            if (pos >= text.Length)
            {
                break;
            }

            var c = text[pos];
            if (c == '@')
            {
                var atLine = line;
                var name = SkipAtRule(text, ref pos, ref line);
                diagnostics.Add(new CssParseDiagnostic(
                    CssDiagnosticSeverity.Info, $"at-rule '@{name}' is not supported and was skipped", atLine));
                continue;
            }

            if (c == '}')
            {
                diagnostics.Add(new CssParseDiagnostic(CssDiagnosticSeverity.Warning, "unexpected '}'", line));
                pos++;
                continue;
            }

            var selectorLine = line;
            var selectorStart = pos;
            ScanToTopLevelOpenBrace(text, ref pos, ref line);
            if (pos >= text.Length)
            {
                diagnostics.Add(new CssParseDiagnostic(
                    CssDiagnosticSeverity.Warning, "expected '{' before end of style sheet", selectorLine));
                break;
            }

            var selectorText = StripComments(text.Slice(selectorStart, pos - selectorStart)).Trim();
            pos++; // consume '{'

            var selectors = CssSelectorParser.ParseGroup(selectorText, out var selectorError);
            if (selectors is null)
            {
                diagnostics.Add(new CssParseDiagnostic(
                    CssDiagnosticSeverity.Warning,
                    $"invalid selector '{selectorText.ToString()}' ({selectorError}); rule dropped", selectorLine));
                SkipBlockRemainder(text, ref pos, ref line);
                continue;
            }

            var declarations = ParseDeclarationBlock(text, ref pos, ref line, diagnostics);
            if (declarations.Count == 0)
            {
                continue;
            }

            var rule = new CssRule
            {
                Selectors = selectors.ToArray(),
                Declarations = declarations.ToArray(),
                RuleIndex = rules.Count,
            };
            rules.Add(rule);
        }

        return new CssStyleSheet(rules.ToArray(), diagnostics.ToArray(), sourceLabel);
    }

    /// <summary>Parses a bare declaration list (the inline Css.Style channel).</summary>
    public static List<CssDeclaration> ParseInlineDeclarations(string text, List<CssParseDiagnostic>? diagnostics)
    {
        var span = text.AsSpan();
        var pos = 0;
        var line = 1;
        var collected = diagnostics ?? new List<CssParseDiagnostic>();
        return ParseDeclarations(span, ref pos, ref line, collected, stopAtCloseBrace: false);
    }

    private static List<CssDeclaration> ParseDeclarationBlock(
        ReadOnlySpan<char> text, ref int pos, ref int line, List<CssParseDiagnostic> diagnostics)
        => ParseDeclarations(text, ref pos, ref line, diagnostics, stopAtCloseBrace: true);

    private static List<CssDeclaration> ParseDeclarations(
        ReadOnlySpan<char> text, ref int pos, ref int line, List<CssParseDiagnostic> diagnostics, bool stopAtCloseBrace)
    {
        var declarations = new List<CssDeclaration>();
        while (true)
        {
            SkipWhitespaceAndComments(text, ref pos, ref line);
            if (pos >= text.Length)
            {
                if (stopAtCloseBrace)
                {
                    diagnostics.Add(new CssParseDiagnostic(
                        CssDiagnosticSeverity.Warning, "unterminated declaration block", line));
                }

                break;
            }

            var c = text[pos];
            if (c == '}' && stopAtCloseBrace)
            {
                pos++;
                break;
            }

            if (c == ';')
            {
                pos++;
                continue;
            }

            var declLine = line;
            var nameStart = pos;
            while (pos < text.Length && IsIdentChar(text[pos]))
            {
                pos++;
            }

            var name = text.Slice(nameStart, pos - nameStart);
            SkipWhitespaceAndComments(text, ref pos, ref line);
            if (name.IsEmpty || pos >= text.Length || text[pos] != ':')
            {
                diagnostics.Add(new CssParseDiagnostic(
                    CssDiagnosticSeverity.Warning, "malformed declaration; skipped", declLine));
                SkipToDeclarationBoundary(text, ref pos, ref line, stopAtCloseBrace);
                continue;
            }

            pos++; // consume ':'
            SkipWhitespaceAndComments(text, ref pos, ref line);
            var valueStart = pos;
            ScanValue(text, ref pos, ref line, stopAtCloseBrace);
            var rawValue = StripComments(text.Slice(valueStart, pos - valueStart)).Trim();

            var important = false;
            if (TryStripImportant(rawValue, out var stripped))
            {
                important = true;
                rawValue = stripped;
            }

            if (rawValue.IsEmpty)
            {
                diagnostics.Add(new CssParseDiagnostic(
                    CssDiagnosticSeverity.Warning, $"declaration '{name.ToString()}' has an empty value; skipped", declLine));
                continue;
            }

            declarations.Add(new CssDeclaration
            {
                PropertyName = name.ToString().ToLowerInvariant(),
                RawValue = rawValue.ToString(),
                Important = important,
            });
        }

        return declarations;
    }

    /// <summary>Scans a declaration value up to the next same-level ';' or the block's closing '}'.</summary>
    private static void ScanValue(ReadOnlySpan<char> text, ref int pos, ref int line, bool stopAtCloseBrace)
    {
        var depth = 0;
        while (pos < text.Length)
        {
            var c = text[pos];
            if (c == '\n')
            {
                line++;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')')
            {
                if (depth > 0)
                {
                    depth--;
                }
            }
            else if (c == '"' || c == '\'')
            {
                pos = CssTokenReader.SkipString(text, pos);
                continue;
            }
            else if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '*')
            {
                SkipComment(text, ref pos, ref line);
                continue;
            }
            else if (depth == 0 && (c == ';' || (stopAtCloseBrace && c == '}')))
            {
                return;
            }

            pos++;
        }
    }

    private static void SkipToDeclarationBoundary(ReadOnlySpan<char> text, ref int pos, ref int line, bool stopAtCloseBrace)
    {
        var depth = 0;
        while (pos < text.Length)
        {
            var c = text[pos];
            if (c == '\n')
            {
                line++;
            }
            else if (c == '(' || c == '{' && !stopAtCloseBrace)
            {
                depth++;
            }
            else if (c == ')')
            {
                if (depth > 0)
                {
                    depth--;
                }
            }
            else if (c == '"' || c == '\'')
            {
                pos = CssTokenReader.SkipString(text, pos);
                continue;
            }
            else if (depth == 0)
            {
                if (c == ';')
                {
                    pos++;
                    return;
                }

                if (stopAtCloseBrace && c == '}')
                {
                    return;
                }
            }

            pos++;
        }
    }

    /// <summary>Replaces comments with a single space (string-aware). Returns the input span when comment-free.</summary>
    private static ReadOnlySpan<char> StripComments(ReadOnlySpan<char> text)
    {
        if (text.IndexOf("/*", StringComparison.Ordinal) < 0)
        {
            return text;
        }

        var sb = new System.Text.StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var end = i + 2;
                while (end < text.Length && !(text[end] == '*' && end + 1 < text.Length && text[end + 1] == '/'))
                {
                    end++;
                }

                i = end < text.Length ? end + 2 : text.Length;
                sb.Append(' ');
                continue;
            }

            if (c == '"' || c == '\'')
            {
                var end = CssTokenReader.SkipString(text, i);
                sb.Append(text.Slice(i, end - i));
                i = end;
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    private static bool TryStripImportant(ReadOnlySpan<char> value, out ReadOnlySpan<char> stripped)
    {
        var bang = value.LastIndexOf('!');
        if (bang >= 0 && value.Slice(bang + 1).Trim().Equals("important", StringComparison.OrdinalIgnoreCase))
        {
            stripped = value.Slice(0, bang).TrimEnd();
            return true;
        }

        stripped = value;
        return false;
    }

    private static void ScanToTopLevelOpenBrace(ReadOnlySpan<char> text, ref int pos, ref int line)
    {
        while (pos < text.Length)
        {
            var c = text[pos];
            if (c == '{')
            {
                return;
            }

            if (c == '\n')
            {
                line++;
            }
            else if (c == '"' || c == '\'')
            {
                pos = CssTokenReader.SkipString(text, pos);
                continue;
            }
            else if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '*')
            {
                SkipComment(text, ref pos, ref line);
                continue;
            }

            pos++;
        }
    }

    /// <summary>Consumes the remainder of a rule's block after its '{' (used when a rule is dropped).</summary>
    private static void SkipBlockRemainder(ReadOnlySpan<char> text, ref int pos, ref int line)
    {
        var depth = 1;
        while (pos < text.Length && depth > 0)
        {
            var c = text[pos];
            if (c == '\n')
            {
                line++;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
            }
            else if (c == '"' || c == '\'')
            {
                pos = CssTokenReader.SkipString(text, pos);
                continue;
            }
            else if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '*')
            {
                SkipComment(text, ref pos, ref line);
                continue;
            }

            pos++;
        }
    }

    /// <summary>Skips an at-rule: either up to a top-level ';' (block-less) or over its balanced { } block.</summary>
    private static string SkipAtRule(ReadOnlySpan<char> text, ref int pos, ref int line)
    {
        pos++; // consume '@'
        var nameStart = pos;
        while (pos < text.Length && IsIdentChar(text[pos]))
        {
            pos++;
        }

        var name = text.Slice(nameStart, pos - nameStart).ToString();
        while (pos < text.Length)
        {
            var c = text[pos];
            if (c == '\n')
            {
                line++;
            }
            else if (c == ';')
            {
                pos++;
                return name;
            }
            else if (c == '{')
            {
                pos++;
                SkipBlockRemainder(text, ref pos, ref line);
                return name;
            }
            else if (c == '"' || c == '\'')
            {
                pos = CssTokenReader.SkipString(text, pos);
                continue;
            }
            else if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '*')
            {
                SkipComment(text, ref pos, ref line);
                continue;
            }

            pos++;
        }

        return name;
    }

    private static void SkipWhitespaceAndComments(ReadOnlySpan<char> text, ref int pos, ref int line)
    {
        while (pos < text.Length)
        {
            var c = text[pos];
            if (c == '\n')
            {
                line++;
                pos++;
            }
            else if (CssTokenReader.IsCssWhitespace(c))
            {
                pos++;
            }
            else if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '*')
            {
                SkipComment(text, ref pos, ref line);
            }
            else
            {
                break;
            }
        }
    }

    private static void SkipComment(ReadOnlySpan<char> text, ref int pos, ref int line)
    {
        pos += 2;
        while (pos < text.Length)
        {
            if (text[pos] == '\n')
            {
                line++;
            }
            else if (text[pos] == '*' && pos + 1 < text.Length && text[pos + 1] == '/')
            {
                pos += 2;
                return;
            }

            pos++;
        }
    }

    private static bool IsIdentChar(char c)
        => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || c > 0x7F;
}
