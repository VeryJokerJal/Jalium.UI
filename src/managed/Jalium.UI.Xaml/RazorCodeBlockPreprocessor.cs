using System.Text;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Jalium.UI.Markup;

/// <summary>
/// Pre-processes JALXAML content to expand <c>@{ }</c> Razor code blocks that appear in
/// element text content before XML parsing. Code blocks may contain C# control flow mixed
/// with inline XML markup (Razor-style), which is compiled and executed to produce valid
/// XAML that replaces the original <c>@{ }</c> block.
/// </summary>
internal static class RazorCodeBlockPreprocessor
{
    /// <summary>
    /// Expands all <c>@{ }</c> code blocks in element text content, returning valid XML.
    /// </summary>
    public static string Process(string xaml)
    {
        if (!xaml.Contains("@{"))
            return xaml;

        var blocks = FindCodeBlocksInTextContent(xaml);
        if (blocks.Count == 0)
            return xaml;

        var sb = new StringBuilder(xaml.Length * 2);
        var lastEnd = 0;

        foreach (var block in blocks)
        {
            sb.Append(xaml, lastEnd, block.Start - lastEnd);
            var expanded = ExpandCodeBlock(block.Code);
            sb.Append(expanded);
            lastEnd = block.End;
        }

        sb.Append(xaml, lastEnd, xaml.Length - lastEnd);
        return sb.ToString();
    }

    #region Find code blocks in text content

    private readonly record struct CodeBlockInfo(int Start, int End, string Code);

    /// <summary>
    /// Finds <c>@{...}</c> blocks that appear in XML text content (not inside attributes or tags).
    /// </summary>
    private static List<CodeBlockInfo> FindCodeBlocksInTextContent(string xaml)
    {
        var blocks = new List<CodeBlockInfo>();
        var i = 0;
        var inTag = false;
        var inAttr = false;
        var inComment = false;
        var inCdata = false;
        char attrQuote = '\0';

        while (i < xaml.Length)
        {
            if (inComment)
            {
                if (i + 2 < xaml.Length && xaml[i] == '-' && xaml[i + 1] == '-' && xaml[i + 2] == '>')
                {
                    inComment = false;
                    i += 3;
                }
                else
                    i++;
                continue;
            }

            if (inCdata)
            {
                if (i + 2 < xaml.Length && xaml[i] == ']' && xaml[i + 1] == ']' && xaml[i + 2] == '>')
                {
                    inCdata = false;
                    i += 3;
                }
                else
                    i++;
                continue;
            }

            if (inAttr)
            {
                if (xaml[i] == attrQuote)
                {
                    inAttr = false;
                    attrQuote = '\0';
                }

                i++;
                continue;
            }

            if (inTag)
            {
                if (xaml[i] == '"' || xaml[i] == '\'')
                {
                    inAttr = true;
                    attrQuote = xaml[i];
                    i++;
                    continue;
                }

                if (xaml[i] == '>')
                    inTag = false;
                i++;
                continue;
            }

            // Text content
            if (i + 3 < xaml.Length && xaml[i] == '<' && xaml[i + 1] == '!' && xaml[i + 2] == '-' && xaml[i + 3] == '-')
            {
                inComment = true;
                i += 4;
                continue;
            }

            if (i + 8 < xaml.Length && xaml.AsSpan(i, 9).SequenceEqual("<![CDATA["))
            {
                inCdata = true;
                i += 9;
                continue;
            }

            if (xaml[i] == '<')
            {
                inTag = true;
                i++;
                continue;
            }

            // Check for @{ in text content
            if (xaml[i] == '@' && i + 1 < xaml.Length && xaml[i + 1] == '{')
            {
                var start = i;
                var codeStart = i + 2;
                var codeEnd = FindMatchingBrace(xaml, codeStart);
                if (codeEnd >= 0)
                {
                    var code = xaml[codeStart..codeEnd].Trim();
                    blocks.Add(new CodeBlockInfo(start, codeEnd + 1, code));
                    i = codeEnd + 1;
                    continue;
                }
            }

            i++;
        }

        return blocks;
    }

    /// <summary>
    /// Finds the matching closing <c>}</c> from the given start position, handling C# strings,
    /// comments, char literals, and nested braces. Also correctly skips braces inside XML
    /// attribute values (which appear as <c>"..."</c> pairs in the raw string).
    /// </summary>
    private static int FindMatchingBrace(string input, int start)
    {
        var pos = start;
        var depth = 1;
        var inString = false;
        var inChar = false;
        var inLineComment = false;
        var inBlockComment = false;
        var escaped = false;
        var verbatimString = false;
        char stringQuote = '\0';

        while (pos < input.Length)
        {
            var current = input[pos];
            var next = pos + 1 < input.Length ? input[pos + 1] : '\0';

            if (inLineComment)
            {
                if (current == '\r' || current == '\n')
                    inLineComment = false;
                pos++;
                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    pos += 2;
                    continue;
                }

                pos++;
                continue;
            }

            if (inString)
            {
                if (!verbatimString && escaped)
                {
                    escaped = false;
                    pos++;
                    continue;
                }

                if (!verbatimString && current == '\\')
                {
                    escaped = true;
                    pos++;
                    continue;
                }

                if (current == stringQuote)
                {
                    if (verbatimString && next == stringQuote)
                    {
                        pos += 2;
                        continue;
                    }

                    inString = false;
                    verbatimString = false;
                    stringQuote = '\0';
                }

                pos++;
                continue;
            }

            if (inChar)
            {
                if (escaped)
                {
                    escaped = false;
                    pos++;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    pos++;
                    continue;
                }

                if (current == '\'')
                    inChar = false;
                pos++;
                continue;
            }

            if (current == '/' && next == '/')
            {
                inLineComment = true;
                pos += 2;
                continue;
            }

            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                pos += 2;
                continue;
            }

            if (current == '\'')
            {
                inChar = true;
                pos++;
                continue;
            }

            if (current == '"')
            {
                inString = true;
                verbatimString =
                    (pos > 0 && input[pos - 1] == '@')
                    || (pos > 1 && ((input[pos - 1] == '$' && input[pos - 2] == '@') || (input[pos - 1] == '@' && input[pos - 2] == '$')));
                stringQuote = current;
                pos++;
                continue;
            }

            if (current == '{')
            {
                depth++;
                pos++;
                continue;
            }

            if (current == '}')
            {
                depth--;
                if (depth == 0)
                    return pos;
                pos++;
                continue;
            }

            pos++;
        }

        return -1;
    }

    #endregion

    #region Expand code block

    /// <summary>
    /// Compiles and executes a Razor code block, returning the generated XAML string.
    /// </summary>
    private static string ExpandCodeBlock(string code)
    {
        var script = BuildCodeBlockScript(code);
        return ExecuteScript(script);
    }

    /// <summary>
    /// Parses a code block that mixes C# code and XML markup (Razor-style) into a
    /// C# script that generates the XAML output via a StringBuilder.
    /// </summary>
    private static string BuildCodeBlockScript(string code)
    {
        var sb = new StringBuilder();
        sb.AppendLine("var __sb = new System.Text.StringBuilder();");

        var segments = ParseCodeBlockSegments(code);
        foreach (var segment in segments)
        {
            if (segment.IsMarkup)
                EmitMarkupAsWrite(sb, segment.Text);
            else
                sb.AppendLine(segment.Text);
        }

        sb.AppendLine("return __sb.ToString();");
        return sb.ToString();
    }

    private readonly record struct CodeSegment(string Text, bool IsMarkup);

    /// <summary>
    /// Splits a code block into alternating C# code and XML markup segments.
    /// XML markup starts when <c>&lt;UppercaseLetter</c> appears at statement level
    /// (not inside a C# string, comment, or expression).
    /// </summary>
    private static List<CodeSegment> ParseCodeBlockSegments(string code)
    {
        var segments = new List<CodeSegment>();
        var pos = 0;
        var codeStart = 0;

        while (pos < code.Length)
        {
            // Skip C# string literals, comments, char literals
            if (SkipCSharpLiteral(code, ref pos))
                continue;

            // Detect XML markup start: < followed by a letter (at statement level)
            if (code[pos] == '<' && pos + 1 < code.Length && char.IsLetter(code[pos + 1]))
            {
                // Emit accumulated C# code
                if (pos > codeStart)
                    segments.Add(new CodeSegment(code[codeStart..pos], false));

                // Read the full XML element (including nested elements)
                var markupEnd = ReadXmlElement(code, pos);
                segments.Add(new CodeSegment(code[pos..markupEnd], true));

                pos = markupEnd;
                codeStart = pos;
                continue;
            }

            pos++;
        }

        if (pos > codeStart)
            segments.Add(new CodeSegment(code[codeStart..pos], false));

        return segments;
    }

    /// <summary>
    /// Skips a C# string literal, char literal, line comment, or block comment.
    /// Returns true if something was skipped.
    /// </summary>
    private static bool SkipCSharpLiteral(string code, ref int pos)
    {
        if (pos >= code.Length) return false;
        var c = code[pos];
        var next = pos + 1 < code.Length ? code[pos + 1] : '\0';

        // Line comment
        if (c == '/' && next == '/')
        {
            pos += 2;
            while (pos < code.Length && code[pos] != '\n') pos++;
            return true;
        }

        // Block comment
        if (c == '/' && next == '*')
        {
            pos += 2;
            while (pos + 1 < code.Length)
            {
                if (code[pos] == '*' && code[pos + 1] == '/')
                {
                    pos += 2;
                    return true;
                }

                pos++;
            }

            pos = code.Length;
            return true;
        }

        // Char literal
        if (c == '\'')
        {
            pos++;
            while (pos < code.Length)
            {
                if (code[pos] == '\\')
                {
                    pos += 2;
                    continue;
                }

                if (code[pos] == '\'')
                {
                    pos++;
                    return true;
                }

                pos++;
            }

            return true;
        }

        // String literal (regular, verbatim, interpolated)
        if (c == '"' || (c == '$' && next == '"') || (c == '@' && next == '"')
            || (c == '$' && next == '@') || (c == '@' && next == '$'))
        {
            var isVerbatim = c == '@' || (pos + 1 < code.Length && code[pos + 1] == '@')
                || (pos + 2 < code.Length && code[pos + 2] == '@');

            // Skip to the opening quote
            while (pos < code.Length && code[pos] != '"') pos++;
            if (pos >= code.Length) return true;
            pos++; // skip opening "

            while (pos < code.Length)
            {
                if (isVerbatim)
                {
                    if (code[pos] == '"')
                    {
                        if (pos + 1 < code.Length && code[pos + 1] == '"')
                        {
                            pos += 2; // escaped quote in verbatim string
                            continue;
                        }

                        pos++;
                        return true;
                    }
                }
                else
                {
                    if (code[pos] == '\\')
                    {
                        pos += 2;
                        continue;
                    }

                    if (code[pos] == '"')
                    {
                        pos++;
                        return true;
                    }
                }

                pos++;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads a complete XML element from the code block (including nested elements).
    /// Returns the position after the element's closing tag or self-close.
    /// </summary>
    private static int ReadXmlElement(string code, int start)
    {
        var pos = start + 1; // skip '<'

        // Read element name
        while (pos < code.Length && !char.IsWhiteSpace(code[pos]) && code[pos] != '>' && code[pos] != '/')
            pos++;

        var elementName = code[(start + 1)..pos];

        // Skip attributes until '>' or '/>'
        var inAttrValue = false;
        char attrQuote = '\0';

        while (pos < code.Length)
        {
            if (inAttrValue)
            {
                if (code[pos] == attrQuote)
                    inAttrValue = false;
                pos++;
                continue;
            }

            if (code[pos] == '"' || code[pos] == '\'')
            {
                inAttrValue = true;
                attrQuote = code[pos];
                pos++;
                continue;
            }

            if (code[pos] == '/' && pos + 1 < code.Length && code[pos + 1] == '>')
                return pos + 2; // self-closing

            if (code[pos] == '>')
            {
                pos++;
                break;
            }

            pos++;
        }

        // Find the matching closing tag, tracking depth
        var depth = 1;
        while (pos < code.Length && depth > 0)
        {
            if (code[pos] == '<')
            {
                if (pos + 1 < code.Length && code[pos + 1] == '/')
                {
                    // Closing tag - any closing tag decrements depth
                    var closeNameStart = pos + 2;
                    var closeEnd = code.IndexOf('>', closeNameStart);
                    if (closeEnd >= 0)
                    {
                        depth--;
                        pos = closeEnd + 1;
                        if (depth == 0)
                            return pos;
                        continue;
                    }
                }
                else if (pos + 1 < code.Length && char.IsLetter(code[pos + 1]))
                {
                    // Opening tag - check if self-closing
                    var selfClose = false;
                    var tagEnd = pos + 1;
                    var inAV = false;
                    char aq = '\0';

                    while (tagEnd < code.Length)
                    {
                        if (inAV)
                        {
                            if (code[tagEnd] == aq)
                                inAV = false;
                            tagEnd++;
                            continue;
                        }

                        if (code[tagEnd] == '"' || code[tagEnd] == '\'')
                        {
                            inAV = true;
                            aq = code[tagEnd];
                            tagEnd++;
                            continue;
                        }

                        if (code[tagEnd] == '/' && tagEnd + 1 < code.Length && code[tagEnd + 1] == '>')
                        {
                            selfClose = true;
                            tagEnd += 2;
                            break;
                        }

                        if (code[tagEnd] == '>')
                        {
                            tagEnd++;
                            break;
                        }

                        tagEnd++;
                    }

                    if (!selfClose)
                        depth++;
                    pos = tagEnd;
                    continue;
                }
            }

            pos++;
        }

        return pos;
    }

    /// <summary>
    /// Converts XML markup containing <c>@identifier</c> and <c>@(expression)</c> into
    /// <c>__sb.Append(...)</c> calls in the generated script.
    /// </summary>
    private static void EmitMarkupAsWrite(StringBuilder sb, string markup)
    {
        var literal = new StringBuilder();
        var i = 0;

        while (i < markup.Length)
        {
            if (markup[i] == '@' && i + 1 < markup.Length)
            {
                // @@ → literal @
                if (markup[i + 1] == '@')
                {
                    literal.Append('@');
                    i += 2;
                    continue;
                }

                // @(expression)
                if (markup[i + 1] == '(')
                {
                    FlushLiteral(sb, literal);
                    var exprStart = i + 2;
                    var depth = 1;
                    var p = exprStart;
                    var inStr = false;
                    var inChr = false;
                    var esc = false;
                    char sq = '\0';

                    while (p < markup.Length && depth > 0)
                    {
                        var ch = markup[p];

                        if (inStr)
                        {
                            if (esc) { esc = false; p++; continue; }
                            if (ch == '\\') { esc = true; p++; continue; }
                            if (ch == sq) inStr = false;
                            p++;
                            continue;
                        }

                        if (inChr)
                        {
                            if (esc) { esc = false; p++; continue; }
                            if (ch == '\\') { esc = true; p++; continue; }
                            if (ch == '\'') inChr = false;
                            p++;
                            continue;
                        }

                        if (ch == '"') { inStr = true; sq = '"'; p++; continue; }
                        if (ch == '\'') { inChr = true; p++; continue; }
                        if (ch == '(') { depth++; p++; continue; }
                        if (ch == ')') { depth--; if (depth > 0) p++; continue; }
                        p++;
                    }

                    var expr = markup[exprStart..p];
                    sb.AppendLine($"__sb.Append({expr});");
                    i = p + 1; // skip closing )
                    continue;
                }

                // @identifier
                if (char.IsLetter(markup[i + 1]) || markup[i + 1] == '_')
                {
                    FlushLiteral(sb, literal);
                    var idStart = i + 1;
                    var p = idStart;
                    while (p < markup.Length && (char.IsLetterOrDigit(markup[p]) || markup[p] == '_' || markup[p] == '.'))
                        p++;
                    var identifier = markup[idStart..p];
                    sb.AppendLine($"__sb.Append({identifier});");
                    i = p;
                    continue;
                }
            }

            literal.Append(markup[i]);
            i++;
        }

        FlushLiteral(sb, literal);
    }

    private static void FlushLiteral(StringBuilder sb, StringBuilder literal)
    {
        if (literal.Length == 0)
            return;

        var escaped = literal.ToString()
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
        sb.AppendLine($"__sb.Append(\"{escaped}\");");
        literal.Clear();
    }

    /// <summary>
    /// Executes the generated C# script and returns the XAML output string.
    /// </summary>
    private static string ExecuteScript(string script)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(static a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .GroupBy(static a => a.FullName, StringComparer.Ordinal)
            .Select(static g => g.First())
            .Cast<System.Reflection.Assembly>()
            .ToArray();

        var options = ScriptOptions.Default
            .WithReferences(references)
            .WithImports("System", "System.Linq", "System.Collections.Generic", "System.Text");

        try
        {
            var result = CSharpScript.EvaluateAsync<string>(script, options).GetAwaiter().GetResult();
            return result ?? string.Empty;
        }
        catch (CompilationErrorException ex)
        {
            throw new XamlParseException(
                $"Razor code block compilation failed: {string.Join(Environment.NewLine, ex.Diagnostics)}", ex);
        }
    }

    #endregion
}
