using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Jalium.UI.Build;

/// <summary>
/// Expands <c>@{ }</c> Razor code blocks in JALXAML content at build time.
/// Code blocks may contain C# control flow mixed with inline XML markup (Razor-style),
/// which is compiled and executed to produce valid XAML.
/// Uses SyntaxFactory API to programmatically construct C# syntax trees for compilation.
/// </summary>
internal static class RazorCodeBlockExpander
{
    /// <summary>
    /// Expands all <c>@{ }</c> code blocks in element text content, returning valid XML.
    /// Returns null if no code blocks were found (content unchanged).
    /// </summary>
    public static string? Expand(string xaml)
    {
        if (!xaml.Contains('@'))
            return null;

        var blocks = FindCodeBlocksInTextContent(xaml);
        if (blocks.Count == 0)
            return null;

        var merged = MergeAdjacentBlocks(xaml, blocks);

        var sb = new StringBuilder(xaml.Length * 2);
        var lastEnd = 0;

        foreach (var block in merged)
        {
            sb.Append(xaml, lastEnd, block.Start - lastEnd);
            var expanded = ExpandCodeBlock(block.Code);
            sb.Append(expanded);
            lastEnd = block.End;
        }

        sb.Append(xaml, lastEnd, xaml.Length - lastEnd);
        return sb.ToString();
    }

    private static List<CodeBlockInfo> MergeAdjacentBlocks(string xaml, List<CodeBlockInfo> blocks)
    {
        if (blocks.Count <= 1)
            return blocks;

        var result = new List<CodeBlockInfo>();
        var groupStart = blocks[0].Start;
        var groupEnd = blocks[0].End;
        var groupCode = new StringBuilder(blocks[0].Code);

        for (var i = 1; i < blocks.Count; i++)
        {
            var gap = xaml.AsSpan(groupEnd, blocks[i].Start - groupEnd);
            if (gap.IsWhiteSpace())
            {
                if (groupCode.Length > 0 && blocks[i].Code.Length > 0)
                    groupCode.AppendLine();
                groupCode.Append(blocks[i].Code);
                groupEnd = blocks[i].End;
            }
            else
            {
                result.Add(new CodeBlockInfo(groupStart, groupEnd, groupCode.ToString()));
                groupStart = blocks[i].Start;
                groupEnd = blocks[i].End;
                groupCode.Clear();
                groupCode.Append(blocks[i].Code);
            }
        }

        result.Add(new CodeBlockInfo(groupStart, groupEnd, groupCode.ToString()));
        return result;
    }

    #region Find code blocks in text content

    private readonly record struct CodeBlockInfo(int Start, int End, string Code);

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

            if (xaml[i] == '@' && i + 1 < xaml.Length)
            {
                // @* comment *@
                if (xaml[i + 1] == '*')
                {
                    var commentEnd = xaml.IndexOf("*@", i + 2, StringComparison.Ordinal);
                    if (commentEnd >= 0)
                    {
                        blocks.Add(new CodeBlockInfo(i, commentEnd + 2, ""));
                        i = commentEnd + 2;
                        continue;
                    }
                }

                // @{ code block }
                if (xaml[i + 1] == '{')
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

                // @keyword directives: for, foreach, while, switch, using, lock, do, try
                if (char.IsLetter(xaml[i + 1]))
                {
                    int dEnd;
                    string dCode;
                    if (TryMatchBlockDirective(xaml, i, out dEnd, out dCode)
                        || TryMatchDoWhileDirective(xaml, i, out dEnd, out dCode)
                        || TryMatchTryCatchDirective(xaml, i, out dEnd, out dCode))
                    {
                        blocks.Add(new CodeBlockInfo(i, dEnd, dCode));
                        i = dEnd;
                        continue;
                    }
                }
            }

            i++;
        }

        return blocks;
    }

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
                if (!verbatimString && escaped) { escaped = false; pos++; continue; }
                if (!verbatimString && current == '\\') { escaped = true; pos++; continue; }

                if (current == stringQuote)
                {
                    if (verbatimString && next == stringQuote) { pos += 2; continue; }
                    inString = false;
                    verbatimString = false;
                    stringQuote = '\0';
                }

                pos++;
                continue;
            }

            if (inChar)
            {
                if (escaped) { escaped = false; pos++; continue; }
                if (current == '\\') { escaped = true; pos++; continue; }
                if (current == '\'') inChar = false;
                pos++;
                continue;
            }

            if (current == '/' && next == '/') { inLineComment = true; pos += 2; continue; }
            if (current == '/' && next == '*') { inBlockComment = true; pos += 2; continue; }
            if (current == '\'') { inChar = true; pos++; continue; }

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

            if (current == '{') { depth++; pos++; continue; }
            if (current == '}')
            {
                depth--;
                if (depth == 0) return pos;
                pos++;
                continue;
            }

            pos++;
        }

        return -1;
    }

    private static int FindMatchingParen(string input, int start)
    {
        var p = start;
        var depth = 1;
        var inString = false;
        var inChar = false;
        var escaped = false;
        var verbatimString = false;
        char stringQuote = '\0';

        while (p < input.Length && depth > 0)
        {
            var c = input[p];
            if (escaped) { escaped = false; p++; continue; }
            if (inString)
            {
                if (!verbatimString && c == '\\') { escaped = true; p++; continue; }
                if (c == stringQuote)
                {
                    if (verbatimString && p + 1 < input.Length && input[p + 1] == stringQuote) { p += 2; continue; }
                    inString = false; verbatimString = false; stringQuote = '\0';
                }
                p++; continue;
            }
            if (inChar)
            {
                if (c == '\\') { escaped = true; p++; continue; }
                if (c == '\'') inChar = false;
                p++; continue;
            }
            if (c == '\'') { inChar = true; p++; continue; }
            if (c == '"')
            {
                inString = true;
                verbatimString = p > 0 && input[p - 1] == '@';
                stringQuote = '"';
                p++; continue;
            }
            if (c == '(') { depth++; p++; continue; }
            if (c == ')')
            {
                depth--;
                if (depth == 0) return p + 1;
            }
            p++;
        }

        return -1;
    }

    private static bool TryMatchBlockDirective(string input, int atPos, out int blockEnd, out string code)
    {
        blockEnd = 0;
        code = "";

        var pos = atPos + 1;
        var remaining = input.Length - pos;

        // Optional "await" prefix (for "await foreach" / "await using")
        if (remaining >= 6 && input.AsSpan(pos, 5).SequenceEqual("await") &&
            !char.IsLetterOrDigit(input[pos + 5]))
        {
            var afterAwait = pos + 5;
            while (afterAwait < input.Length && char.IsWhiteSpace(input[afterAwait])) afterAwait++;
            var r = input.Length - afterAwait;

            var isAwaitForeach = r >= 7 && input.AsSpan(afterAwait, 7).SequenceEqual("foreach") &&
                (r == 7 || !char.IsLetterOrDigit(input[afterAwait + 7]));
            var isAwaitUsing = r >= 5 && input.AsSpan(afterAwait, 5).SequenceEqual("using") &&
                (r == 5 || !char.IsLetterOrDigit(input[afterAwait + 5]));

            if (isAwaitForeach || isAwaitUsing)
            {
                pos = afterAwait;
                remaining = r;
            }
            else
            {
                return false;
            }
        }

        int keywordLen;
        if (remaining >= 7 && input.AsSpan(pos, 7).SequenceEqual("foreach") &&
            (remaining == 7 || !char.IsLetterOrDigit(input[pos + 7])))
            keywordLen = 7;
        else if (remaining >= 6 && input.AsSpan(pos, 6).SequenceEqual("switch") &&
                 (remaining == 6 || !char.IsLetterOrDigit(input[pos + 6])))
            keywordLen = 6;
        else if (remaining >= 5 && input.AsSpan(pos, 5).SequenceEqual("while") &&
                 (remaining == 5 || !char.IsLetterOrDigit(input[pos + 5])))
            keywordLen = 5;
        else if (remaining >= 5 && input.AsSpan(pos, 5).SequenceEqual("using") &&
                 (remaining == 5 || !char.IsLetterOrDigit(input[pos + 5])))
            keywordLen = 5;
        else if (remaining >= 4 && input.AsSpan(pos, 4).SequenceEqual("lock") &&
                 (remaining == 4 || !char.IsLetterOrDigit(input[pos + 4])))
            keywordLen = 4;
        else if (remaining >= 3 && input.AsSpan(pos, 3).SequenceEqual("for") &&
                 (remaining == 3 || !char.IsLetterOrDigit(input[pos + 3])))
            keywordLen = 3;
        else
            return false;

        var p = pos + keywordLen;
        while (p < input.Length && char.IsWhiteSpace(input[p])) p++;

        if (p >= input.Length || input[p] != '(') return false;

        var afterParen = FindMatchingParen(input, p + 1);
        if (afterParen < 0) return false;
        p = afterParen;

        while (p < input.Length && char.IsWhiteSpace(input[p])) p++;
        if (p >= input.Length || input[p] != '{') return false;

        var braceEnd = FindMatchingBrace(input, p + 1);
        if (braceEnd < 0) return false;

        blockEnd = braceEnd + 1;
        code = input[(atPos + 1)..blockEnd].Trim();
        return true;
    }

    private static bool TryMatchDoWhileDirective(string input, int atPos, out int blockEnd, out string code)
    {
        blockEnd = 0;
        code = "";

        var pos = atPos + 1;
        var remaining = input.Length - pos;

        if (remaining < 2 || input[pos] != 'd' || input[pos + 1] != 'o')
            return false;
        if (remaining > 2 && char.IsLetterOrDigit(input[pos + 2]))
            return false;

        var p = pos + 2;
        while (p < input.Length && char.IsWhiteSpace(input[p])) p++;
        if (p >= input.Length || input[p] != '{') return false;

        var braceEnd = FindMatchingBrace(input, p + 1);
        if (braceEnd < 0) return false;
        p = braceEnd + 1;

        while (p < input.Length && char.IsWhiteSpace(input[p])) p++;
        if (p + 5 > input.Length || !input.AsSpan(p, 5).SequenceEqual("while"))
            return false;
        if (p + 5 < input.Length && char.IsLetterOrDigit(input[p + 5]))
            return false;
        p += 5;

        while (p < input.Length && char.IsWhiteSpace(input[p])) p++;
        if (p >= input.Length || input[p] != '(') return false;

        var afterParen = FindMatchingParen(input, p + 1);
        if (afterParen < 0) return false;
        p = afterParen;

        while (p < input.Length && char.IsWhiteSpace(input[p])) p++;
        if (p < input.Length && input[p] == ';') p++;

        blockEnd = p;
        code = input[(atPos + 1)..blockEnd].Trim();
        return true;
    }

    private static bool TryMatchTryCatchDirective(string input, int atPos, out int blockEnd, out string code)
    {
        blockEnd = 0;
        code = "";

        var pos = atPos + 1;
        var remaining = input.Length - pos;

        if (remaining < 3 || input[pos] != 't' || input[pos + 1] != 'r' || input[pos + 2] != 'y')
            return false;
        if (remaining > 3 && char.IsLetterOrDigit(input[pos + 3]))
            return false;

        var p = pos + 3;
        while (p < input.Length && char.IsWhiteSpace(input[p])) p++;
        if (p >= input.Length || input[p] != '{') return false;

        var braceEnd = FindMatchingBrace(input, p + 1);
        if (braceEnd < 0) return false;
        p = braceEnd + 1;

        var hasCatchOrFinally = false;

        while (true)
        {
            var save = p;
            while (p < input.Length && char.IsWhiteSpace(input[p])) p++;

            if (p + 5 <= input.Length && input.AsSpan(p, 5).SequenceEqual("catch") &&
                (p + 5 >= input.Length || !char.IsLetterOrDigit(input[p + 5])))
            {
                hasCatchOrFinally = true;
                p += 5;
                while (p < input.Length && char.IsWhiteSpace(input[p])) p++;

                if (p < input.Length && input[p] == '(')
                {
                    var afterParen = FindMatchingParen(input, p + 1);
                    if (afterParen < 0) return false;
                    p = afterParen;
                    while (p < input.Length && char.IsWhiteSpace(input[p])) p++;
                }

                if (p + 4 <= input.Length && input.AsSpan(p, 4).SequenceEqual("when") &&
                    (p + 4 >= input.Length || !char.IsLetterOrDigit(input[p + 4])))
                {
                    p += 4;
                    while (p < input.Length && char.IsWhiteSpace(input[p])) p++;
                    if (p < input.Length && input[p] == '(')
                    {
                        var afterWhen = FindMatchingParen(input, p + 1);
                        if (afterWhen < 0) return false;
                        p = afterWhen;
                        while (p < input.Length && char.IsWhiteSpace(input[p])) p++;
                    }
                }

                if (p >= input.Length || input[p] != '{') return false;
                braceEnd = FindMatchingBrace(input, p + 1);
                if (braceEnd < 0) return false;
                p = braceEnd + 1;
                continue;
            }

            p = save;
            break;
        }

        {
            var save = p;
            while (p < input.Length && char.IsWhiteSpace(input[p])) p++;

            if (p + 7 <= input.Length && input.AsSpan(p, 7).SequenceEqual("finally") &&
                (p + 7 >= input.Length || !char.IsLetterOrDigit(input[p + 7])))
            {
                hasCatchOrFinally = true;
                p += 7;
                while (p < input.Length && char.IsWhiteSpace(input[p])) p++;
                if (p >= input.Length || input[p] != '{') return false;
                braceEnd = FindMatchingBrace(input, p + 1);
                if (braceEnd < 0) return false;
                p = braceEnd + 1;
            }
            else
            {
                p = save;
            }
        }

        if (!hasCatchOrFinally) return false;

        blockEnd = p;
        code = input[(atPos + 1)..blockEnd].Trim();
        return true;
    }

    #endregion

    #region Expand code block

    private static string ExpandCodeBlock(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;
        var script = BuildCodeBlockScript(code);
        return ExecuteScript(script);
    }

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

    private static List<CodeSegment> ParseCodeBlockSegments(string code)
    {
        var segments = new List<CodeSegment>();
        var pos = 0;
        var codeStart = 0;

        while (pos < code.Length)
        {
            if (SkipCSharpLiteral(code, ref pos))
                continue;

            if (code[pos] == '<' && pos + 1 < code.Length && char.IsLetter(code[pos + 1]))
            {
                if (pos > codeStart)
                    segments.Add(new CodeSegment(code[codeStart..pos], false));

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

    private static bool SkipCSharpLiteral(string code, ref int pos)
    {
        if (pos >= code.Length) return false;
        var c = code[pos];
        var next = pos + 1 < code.Length ? code[pos + 1] : '\0';

        if (c == '/' && next == '/')
        {
            pos += 2;
            while (pos < code.Length && code[pos] != '\n') pos++;
            return true;
        }

        if (c == '/' && next == '*')
        {
            pos += 2;
            while (pos + 1 < code.Length)
            {
                if (code[pos] == '*' && code[pos + 1] == '/') { pos += 2; return true; }
                pos++;
            }

            pos = code.Length;
            return true;
        }

        if (c == '\'')
        {
            pos++;
            while (pos < code.Length)
            {
                if (code[pos] == '\\') { pos += 2; continue; }
                if (code[pos] == '\'') { pos++; return true; }
                pos++;
            }

            return true;
        }

        if (c == '"' || (c == '$' && next == '"') || (c == '@' && next == '"')
            || (c == '$' && next == '@') || (c == '@' && next == '$'))
        {
            var isVerbatim = c == '@' || (pos + 1 < code.Length && code[pos + 1] == '@')
                || (pos + 2 < code.Length && code[pos + 2] == '@');

            while (pos < code.Length && code[pos] != '"') pos++;
            if (pos >= code.Length) return true;
            pos++;

            while (pos < code.Length)
            {
                if (isVerbatim)
                {
                    if (code[pos] == '"')
                    {
                        if (pos + 1 < code.Length && code[pos + 1] == '"') { pos += 2; continue; }
                        pos++;
                        return true;
                    }
                }
                else
                {
                    if (code[pos] == '\\') { pos += 2; continue; }
                    if (code[pos] == '"') { pos++; return true; }
                }

                pos++;
            }

            return true;
        }

        return false;
    }

    private static int ReadXmlElement(string code, int start)
    {
        var pos = start + 1;
        while (pos < code.Length && !char.IsWhiteSpace(code[pos]) && code[pos] != '>' && code[pos] != '/')
            pos++;

        var inAttrValue = false;
        char attrQuote = '\0';

        while (pos < code.Length)
        {
            if (inAttrValue)
            {
                if (code[pos] == attrQuote) inAttrValue = false;
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
                return pos + 2;

            if (code[pos] == '>')
            {
                pos++;
                break;
            }

            pos++;
        }

        var depth = 1;
        while (pos < code.Length && depth > 0)
        {
            if (code[pos] == '<')
            {
                if (pos + 1 < code.Length && code[pos + 1] == '/')
                {
                    var closeEnd = code.IndexOf('>', pos + 2);
                    if (closeEnd >= 0)
                    {
                        depth--;
                        pos = closeEnd + 1;
                        if (depth == 0) return pos;
                        continue;
                    }
                }
                else if (pos + 1 < code.Length && char.IsLetter(code[pos + 1]))
                {
                    var selfClose = false;
                    var tagEnd = pos + 1;
                    var inAV = false;
                    char aq = '\0';

                    while (tagEnd < code.Length)
                    {
                        if (inAV) { if (code[tagEnd] == aq) inAV = false; tagEnd++; continue; }
                        if (code[tagEnd] == '"' || code[tagEnd] == '\'') { inAV = true; aq = code[tagEnd]; tagEnd++; continue; }
                        if (code[tagEnd] == '/' && tagEnd + 1 < code.Length && code[tagEnd + 1] == '>') { selfClose = true; tagEnd += 2; break; }
                        if (code[tagEnd] == '>') { tagEnd++; break; }
                        tagEnd++;
                    }

                    if (!selfClose) depth++;
                    pos = tagEnd;
                    continue;
                }
            }

            pos++;
        }

        return pos;
    }

    private static void EmitMarkupAsWrite(StringBuilder sb, string markup)
    {
        var literal = new StringBuilder();
        var i = 0;

        while (i < markup.Length)
        {
            if (markup[i] == '@' && i + 1 < markup.Length)
            {
                if (markup[i + 1] == '@') { literal.Append('@'); i += 2; continue; }

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
                        if (inStr) { if (esc) { esc = false; p++; continue; } if (ch == '\\') { esc = true; p++; continue; } if (ch == sq) inStr = false; p++; continue; }
                        if (inChr) { if (esc) { esc = false; p++; continue; } if (ch == '\\') { esc = true; p++; continue; } if (ch == '\'') inChr = false; p++; continue; }
                        if (ch == '"') { inStr = true; sq = '"'; p++; continue; }
                        if (ch == '\'') { inChr = true; p++; continue; }
                        if (ch == '(') { depth++; p++; continue; }
                        if (ch == ')') { depth--; if (depth > 0) p++; continue; }
                        p++;
                    }

                    sb.AppendLine($"__sb.Append({markup[exprStart..p]});");
                    i = p + 1;
                    continue;
                }

                // @* comment *@
                if (markup[i + 1] == '*')
                {
                    FlushLiteral(sb, literal);
                    var commentEnd = markup.IndexOf("*@", i + 2, StringComparison.Ordinal);
                    if (commentEnd >= 0) { i = commentEnd + 2; continue; }
                }

                // @{ inline code }
                if (markup[i + 1] == '{')
                {
                    FlushLiteral(sb, literal);
                    var codeStart = i + 2;
                    var codeEnd = FindMatchingBrace(markup, codeStart);
                    if (codeEnd >= 0)
                    {
                        sb.AppendLine(markup[codeStart..codeEnd].Trim());
                        i = codeEnd + 1;
                        continue;
                    }
                }

                // @keyword(...){}  or  @identifier
                if (char.IsLetter(markup[i + 1]) || markup[i + 1] == '_')
                {
                    if (TryParseBlockDirectiveInMarkup(markup, i, out var dEnd, out var dHeader, out var dBody))
                    {
                        FlushLiteral(sb, literal);
                        sb.AppendLine(dHeader);
                        EmitMarkupAsWrite(sb, dBody);
                        sb.AppendLine("}");
                        i = dEnd;
                        continue;
                    }

                    FlushLiteral(sb, literal);
                    var idStart = i + 1;
                    var p = idStart;
                    while (p < markup.Length && (char.IsLetterOrDigit(markup[p]) || markup[p] == '_' || markup[p] == '.'))
                        p++;
                    sb.AppendLine($"__sb.Append({markup[idStart..p]});");
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
        if (literal.Length == 0) return;

        var escaped = literal.ToString()
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
        sb.AppendLine($"__sb.Append(\"{escaped}\");");
        literal.Clear();
    }

    private static bool TryParseBlockDirectiveInMarkup(string markup, int atPos, out int directiveEnd, out string header, out string body)
    {
        directiveEnd = 0;
        header = "";
        body = "";

        var pos = atPos + 1;
        var remaining = markup.Length - pos;

        int keywordLen;
        if (remaining >= 7 && markup.AsSpan(pos, 7).SequenceEqual("foreach") &&
            (remaining == 7 || !char.IsLetterOrDigit(markup[pos + 7])))
            keywordLen = 7;
        else if (remaining >= 6 && markup.AsSpan(pos, 6).SequenceEqual("switch") &&
                 (remaining == 6 || !char.IsLetterOrDigit(markup[pos + 6])))
            keywordLen = 6;
        else if (remaining >= 5 && markup.AsSpan(pos, 5).SequenceEqual("while") &&
                 (remaining == 5 || !char.IsLetterOrDigit(markup[pos + 5])))
            keywordLen = 5;
        else if (remaining >= 5 && markup.AsSpan(pos, 5).SequenceEqual("using") &&
                 (remaining == 5 || !char.IsLetterOrDigit(markup[pos + 5])))
            keywordLen = 5;
        else if (remaining >= 4 && markup.AsSpan(pos, 4).SequenceEqual("lock") &&
                 (remaining == 4 || !char.IsLetterOrDigit(markup[pos + 4])))
            keywordLen = 4;
        else if (remaining >= 3 && markup.AsSpan(pos, 3).SequenceEqual("for") &&
                 (remaining == 3 || !char.IsLetterOrDigit(markup[pos + 3])))
            keywordLen = 3;
        else if (remaining >= 2 && markup.AsSpan(pos, 2).SequenceEqual("if") &&
                 (remaining == 2 || !char.IsLetterOrDigit(markup[pos + 2])))
            keywordLen = 2;
        else
            return false;

        var p = pos + keywordLen;
        while (p < markup.Length && char.IsWhiteSpace(markup[p])) p++;
        if (p >= markup.Length || markup[p] != '(') return false;

        var afterParen = FindMatchingParen(markup, p + 1);
        if (afterParen < 0) return false;

        var hp = afterParen;
        while (hp < markup.Length && char.IsWhiteSpace(markup[hp])) hp++;
        if (hp >= markup.Length || markup[hp] != '{') return false;

        header = markup[pos..(hp + 1)];

        var bodyStart = hp + 1;
        var bodyEnd = FindMatchingBrace(markup, bodyStart);
        if (bodyEnd < 0) return false;

        body = markup[bodyStart..bodyEnd];
        directiveEnd = bodyEnd + 1;
        return true;
    }

    /// <summary>
    /// Compiles and executes the generated script using SyntaxFactory to construct a
    /// complete C# syntax tree, then compiles it with CSharpCompilation and runs the
    /// emitted assembly in-memory.
    /// </summary>
    private static string ExecuteScript(string script)
    {
        // Build a complete compilation unit using SyntaxFactory:
        //
        //   using System;
        //   using System.Linq;
        //   using System.Collections.Generic;
        //   using System.Text;
        //
        //   public static class __RazorCodeBlockRunner
        //   {
        //       public static string Run()
        //       {
        //           <user script body>
        //       }
        //   }

        var usingDirectives = List(new UsingDirectiveSyntax[]
        {
            UsingDirective(IdentifierName("System")),
            UsingDirective(QualifiedName(IdentifierName("System"), IdentifierName("Linq"))),
            UsingDirective(QualifiedName(
                QualifiedName(IdentifierName("System"), IdentifierName("Collections")),
                IdentifierName("Generic"))),
            UsingDirective(QualifiedName(IdentifierName("System"), IdentifierName("Text"))),
        });

        // Parse the script body as statements — the script is dynamically generated from
        // code blocks, so we parse it rather than constructing each statement with SyntaxFactory.
        var bodyStatements = ParseStatementList(script);

        var runMethod = MethodDeclaration(
                PredefinedType(Token(SyntaxKind.StringKeyword)),
                Identifier("Run"))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
            .WithBody(Block(bodyStatements));

        var classDecl = ClassDeclaration("__RazorCodeBlockRunner")
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
            .AddMembers(runMethod);

        var compilationUnit = CompilationUnit()
            .WithUsings(usingDirectives)
            .AddMembers(classDecl)
            .NormalizeWhitespace();

        var syntaxTree = CSharpSyntaxTree.Create(compilationUnit);

        // Gather references: core runtime assemblies needed for StringBuilder, Linq, etc.
        var references = GetMetadataReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName: "__RazorCodeBlock_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release));

        using var ms = new System.IO.MemoryStream();
        var emitResult = compilation.Emit(ms);

        if (!emitResult.Success)
        {
            var errors = emitResult.Diagnostics
                .Where(static d => d.Severity == DiagnosticSeverity.Error)
                .Select(static d => d.ToString());
            throw new InvalidOperationException(
                $"Razor code block compilation failed:\n{string.Join("\n", errors)}\n\nGenerated source:\n{compilationUnit.ToFullString()}");
        }

        ms.Seek(0, System.IO.SeekOrigin.Begin);
        var assembly = Assembly.Load(ms.ToArray());
        var type = assembly.GetType("__RazorCodeBlockRunner")!;
        var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
        var result = (string?)method.Invoke(null, null);
        return result ?? string.Empty;
    }

    /// <summary>
    /// Parses a sequence of C# statements from script text.
    /// Uses SyntaxFactory.ParseStatement to parse individual statements while handling
    /// the script as a sequence of top-level statements.
    /// </summary>
    private static SyntaxList<StatementSyntax> ParseStatementList(string script)
    {
        // Parse as a method body by wrapping in a method, then extracting statements.
        var wrapper = $"void __M() {{ {script} }}";
        var tree = CSharpSyntaxTree.ParseText(wrapper);
        var root = tree.GetRoot();

        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method?.Body != null)
            return method.Body.Statements;

        // Fallback: parse as a single statement block
        var block = (BlockSyntax)ParseStatement($"{{ {script} }}");
        return block.Statements;
    }

    /// <summary>
    /// Collects metadata references for the core runtime assemblies needed by the generated code.
    /// </summary>
    private static MetadataReference[] GetMetadataReferences()
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrEmpty(trustedAssemblies))
        {
            // Fallback: use currently loaded assemblies
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(static a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
                .Select(static a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
                .ToArray();
        }

        var separator = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows) ? ';' : ':';

        return trustedAssemblies
            .Split(separator, StringSplitOptions.RemoveEmptyEntries)
            .Where(static p => System.IO.File.Exists(p))
            .Select(static p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToArray();
    }

    #endregion
}
