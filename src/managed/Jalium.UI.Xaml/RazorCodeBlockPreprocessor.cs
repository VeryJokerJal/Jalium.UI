using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Jalium.UI.Markup;

/// <summary>
/// Pre-processes JALXAML content to expand <c>@{ }</c> Razor code blocks that appear in
/// element text content before XML parsing. Code blocks may contain C# control flow mixed
/// with inline XML markup (Razor-style), which is compiled and executed to produce valid
/// XAML that replaces the original <c>@{ }</c> block.
/// </summary>
internal static class RazorCodeBlockPreprocessor
{
    private static readonly ConcurrentDictionary<string, string> ProcessCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> SectionCache = new(StringComparer.Ordinal);
    private const int MaxProcessCacheSize = 512;

    /// <summary>
    /// Expands all <c>@{ }</c> code blocks in element text content, returning valid XML.
    /// Also processes <c>@section Name { ... }</c> definitions and <c>@RenderSection("Name")</c> calls.
    /// </summary>
    public static string Process(string xaml)
    {
        if (!xaml.Contains('@'))
            return xaml;

        var originalXaml = xaml;
        if (ProcessCache.TryGetValue(originalXaml, out var cached))
        {
            // Re-register sections even on cache hit (page may have been reloaded)
            ReRegisterSections(originalXaml);
            return cached;
        }

        // First pass: extract @section definitions and collect their content
        var sections = new Dictionary<string, string>(StringComparer.Ordinal);
        xaml = ExtractSectionDefinitions(xaml, sections);
        if (sections.Count > 0)
            SectionCache[originalXaml] = sections;

        var hasRenderSection = xaml.Contains("@RenderSection(", StringComparison.Ordinal);
        var blocks = FindCodeBlocksInTextContent(xaml);
        if (blocks.Count == 0 && sections.Count == 0 && !hasRenderSection)
            return xaml;

        // Merge adjacent blocks (separated only by whitespace) so variables are shared
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
        var result = sb.ToString();

        // Second pass: replace @RenderSection("Name") calls with section content
        if (sections.Count > 0 || hasRenderSection)
            result = ReplaceSectionRenders(result, sections);

        if (ProcessCache.Count >= MaxProcessCacheSize)
            ProcessCache.Clear();
        ProcessCache[originalXaml] = result;

        return result;
    }

    /// <summary>
    /// Merges consecutive code blocks that are only separated by whitespace into a single
    /// execution unit so that variables declared in one block are accessible in the next.
    /// </summary>
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

    private static void ReRegisterSections(string originalXaml)
    {
        if (SectionCache.TryGetValue(originalXaml, out var sections))
        {
            foreach (var (name, body) in sections)
                RazorExpressionRegistry.RegisterSection(name, body);
        }
    }

    #region Section support

    /// <summary>
    /// Scans XAML for <c>@section Name { ... }</c> definitions, extracts them, and returns
    /// the XAML with those definitions removed. Section bodies are stored for later
    /// replacement when <c>@RenderSection("Name")</c> is encountered.
    /// </summary>
    private static string ExtractSectionDefinitions(string xaml, Dictionary<string, string> sections)
    {
        var sb = new StringBuilder(xaml.Length);
        var i = 0;

        while (i < xaml.Length)
        {
            // Skip @@ escape sequences
            if (xaml[i] == '@' && i + 1 < xaml.Length && xaml[i + 1] == '@')
            {
                sb.Append(xaml[i]);
                sb.Append(xaml[i + 1]);
                i += 2;
                continue;
            }

            if (xaml[i] == '@' && i + 8 < xaml.Length &&
                xaml.AsSpan(i + 1, 7).SequenceEqual("section") &&
                (i + 8 >= xaml.Length || !char.IsLetterOrDigit(xaml[i + 8])))
            {
                var p = i + 8;
                while (p < xaml.Length && char.IsWhiteSpace(xaml[p])) p++;

                // Read section name
                var nameStart = p;
                while (p < xaml.Length && (char.IsLetterOrDigit(xaml[p]) || xaml[p] == '_')) p++;
                if (p == nameStart) { sb.Append(xaml[i]); i++; continue; }
                var sectionName = xaml[nameStart..p];

                while (p < xaml.Length && char.IsWhiteSpace(xaml[p])) p++;
                if (p >= xaml.Length || xaml[p] != '{') { sb.Append(xaml[i]); i++; continue; }

                var bodyEnd = FindMatchingBrace(xaml, p + 1);
                if (bodyEnd < 0) { sb.Append(xaml[i]); i++; continue; }

                var body = xaml[(p + 1)..bodyEnd].Trim();
                sections[sectionName] = body;
                RazorExpressionRegistry.RegisterSection(sectionName, body);

                // Skip the entire @section block and trailing whitespace (don't emit it)
                i = bodyEnd + 1;
                while (i < xaml.Length && (xaml[i] == ' ' || xaml[i] == '\t' || xaml[i] == '\r' || xaml[i] == '\n'))
                    i++;
                continue;
            }

            sb.Append(xaml[i]);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Replaces all <c>@RenderSection("Name")</c> calls with the content of the
    /// corresponding section. Supports both <c>@RenderSection("Name")</c> and
    /// <c>@RenderSection("Name", required: false)</c> syntax.
    /// </summary>
    private static string ReplaceSectionRenders(string xaml, Dictionary<string, string> sections)
    {
        var sb = new StringBuilder(xaml.Length);
        var i = 0;

        while (i < xaml.Length)
        {
            // Skip @@ escape sequences
            if (xaml[i] == '@' && i + 1 < xaml.Length && xaml[i + 1] == '@')
            {
                sb.Append(xaml[i]);
                sb.Append(xaml[i + 1]);
                i += 2;
                continue;
            }

            if (xaml[i] == '@' && i + 14 < xaml.Length &&
                xaml.AsSpan(i + 1, 13).SequenceEqual("RenderSection") &&
                (i + 14 >= xaml.Length || xaml[i + 14] == '('))
            {
                var p = i + 14;
                while (p < xaml.Length && char.IsWhiteSpace(xaml[p])) p++;
                if (p < xaml.Length && xaml[p] == '(')
                {
                    var argsStart = p + 1;
                    var parenEnd = FindMatchingParen(xaml, argsStart);
                    if (parenEnd >= 0)
                    {
                        var args = xaml[argsStart..parenEnd].Trim();
                        var sectionName = ExtractSectionNameFromArgs(args);
                        if (sectionName != null)
                        {
                            // Try local sections first, then global registry
                            if (sections.TryGetValue(sectionName, out var content))
                                sb.Append(content);
                            else if (RazorExpressionRegistry.TryGetGlobalSection(sectionName, out var globalContent))
                                sb.Append(globalContent);
                            else
                                // Section not yet registered — emit a dynamic host that will
                                // load the content when the section is registered at runtime
                                sb.Append($"<RazorSectionHost SectionName=\"{sectionName}\"/>");
                        }
                        i = parenEnd + 1;
                        continue;
                    }
                }
            }

            sb.Append(xaml[i]);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Extracts the section name from RenderSection arguments.
    /// Handles: <c>"Name"</c>, <c>"Name", required: false</c>, <c>"Name", false</c>
    /// </summary>
    internal static string? ExtractSectionNameFromArgs(string args)
    {
        var trimmed = args.Trim();
        if (trimmed.Length < 2) return null;

        char quote = trimmed[0];
        if (quote != '"' && quote != '\'') return null;

        var endQuote = trimmed.IndexOf(quote, 1);
        if (endQuote < 0) return null;

        return trimmed[1..endQuote];
    }

    #endregion

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

    /// <summary>
    /// Finds the matching closing <c>}</c> from the given start position, handling C# strings,
    /// comments, char literals, and nested braces. Also correctly skips braces inside XML
    /// attribute values (which appear as <c>"..."</c> pairs in the raw string).
    /// </summary>
    internal static int FindMatchingBrace(string input, int start)
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

    /// <summary>
    /// Starting from the position after an opening <c>(</c>, finds the matching <c>)</c>.
    /// Returns the position after <c>)</c>, or -1 if unbalanced.
    /// </summary>
    internal static int FindMatchingParen(string input, int start)
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

    /// <summary>
    /// Tries to match a <c>@keyword(...){...}</c> directive at the given position.
    /// Supported keywords: for, foreach, while, switch, using, lock.
    /// Also supports <c>@await foreach</c> and <c>@await using</c>.
    /// </summary>
    internal static bool TryMatchBlockDirective(string input, int atPos, out int blockEnd, out string code)
    {
        blockEnd = 0;
        code = "";

        var pos = atPos + 1; // skip '@'
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

        // Match keyword (check longest first to avoid prefix conflicts)
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

    /// <summary>
    /// Tries to match <c>@do { ... } while(expr);</c> at the given position.
    /// </summary>
    internal static bool TryMatchDoWhileDirective(string input, int atPos, out int blockEnd, out string code)
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

        // Match "while"
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

    /// <summary>
    /// Tries to match <c>@try { ... } catch(...) { ... } finally { ... }</c> at the given position.
    /// </summary>
    internal static bool TryMatchTryCatchDirective(string input, int atPos, out int blockEnd, out string code)
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

        // Match zero or more catch blocks
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

                // Optional (ExceptionType ex)
                if (p < input.Length && input[p] == '(')
                {
                    var afterParen = FindMatchingParen(input, p + 1);
                    if (afterParen < 0) return false;
                    p = afterParen;
                    while (p < input.Length && char.IsWhiteSpace(input[p])) p++;
                }

                // Optional "when (expr)"
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

        // Match optional finally block
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

    /// <summary>
    /// Compiles and executes a Razor code block, returning the generated XAML string.
    /// </summary>
    internal static string ExpandCodeBlock(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.Empty;

        var segments = ParseCodeBlockSegments(code);

        // If the code block has no XML markup segments (only pure C# code with Write()/WriteLiteral()),
        // it may reference runtime variables (DataContext). Preserve it as @{...} for the runtime
        // RazorBindingEngine to handle, instead of expanding at preprocess time.
        var hasMarkup = segments.Any(s => s.IsMarkup);
        if (!hasMarkup)
            return "@{" + code + "}";

        return RazorLightweightCodeBlockInterpreter.Expand(segments);
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

    internal readonly record struct CodeSegment(string Text, bool IsMarkup);

    /// <summary>
    /// Splits a code block into alternating C# code and XML markup segments.
    /// XML markup starts when <c>&lt;UppercaseLetter</c> appears at statement level
    /// (not inside a C# string, comment, or expression).
    /// </summary>
    internal static List<CodeSegment> ParseCodeBlockSegments(string code)
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

                // @* comment *@ (strip from output)
                if (markup[i + 1] == '*')
                {
                    FlushLiteral(sb, literal);
                    var commentEnd = markup.IndexOf("*@", i + 2, StringComparison.Ordinal);
                    if (commentEnd >= 0) { i = commentEnd + 2; continue; }
                }

                // @{ inline code } (emit code directly, no output wrapping)
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
                    // Try nested block directive first (@for, @foreach, @while, @if, @switch, @using, @lock)
                    if (TryParseBlockDirectiveInMarkup(markup, i, out var dEnd, out var dHeader, out var dBody))
                    {
                        FlushLiteral(sb, literal);
                        sb.AppendLine(dHeader);
                        EmitMarkupAsWrite(sb, dBody);
                        sb.AppendLine("}");
                        i = dEnd;
                        continue;
                    }

                    // Regular identifier
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
    /// Tries to parse a block directive (<c>@for</c>, <c>@foreach</c>, <c>@while</c>, <c>@if</c>,
    /// <c>@switch</c>, <c>@using</c>, <c>@lock</c>) within markup content.
    /// Returns the C# header (e.g. <c>for(...) {</c>) and the body content separately.
    /// </summary>
    private static bool TryParseBlockDirectiveInMarkup(string markup, int atPos, out int directiveEnd, out string header, out string body)
    {
        directiveEnd = 0;
        header = "";
        body = "";

        var pos = atPos + 1; // skip '@'
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

        header = markup[pos..(hp + 1)]; // e.g. "for(var col = 1; col <= 4; col++) {"

        var bodyStart = hp + 1;
        var bodyEnd = FindMatchingBrace(markup, bodyStart);
        if (bodyEnd < 0) return false;

        body = markup[bodyStart..bodyEnd];
        directiveEnd = bodyEnd + 1;
        return true;
    }

    /// <summary>
    /// Compiles and executes the generated script using SyntaxFactory to construct a
    /// complete C# syntax tree, then compiles with CSharpCompilation and runs in-memory.
    /// </summary>
    private static string ExecuteScript(string script)
    {
        var usingDirectives = List(new UsingDirectiveSyntax[]
        {
            UsingDirective(IdentifierName("System")),
            UsingDirective(QualifiedName(IdentifierName("System"), IdentifierName("Linq"))),
            UsingDirective(QualifiedName(
                QualifiedName(IdentifierName("System"), IdentifierName("Collections")),
                IdentifierName("Generic"))),
            UsingDirective(QualifiedName(IdentifierName("System"), IdentifierName("Text"))),
        });

        // Parse script body as statements
        var bodyStatements = ParseStatementList(script);

        var runMethod = MethodDeclaration(
                PredefinedType(Token(SyntaxKind.StringKeyword)),
                Identifier("Run"))
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
            .WithBody(Block(bodyStatements));

        var classDecl = ClassDeclaration("__RazorRunner")
            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword))
            .AddMembers(runMethod);

        var compilationUnit = CompilationUnit()
            .WithUsings(usingDirectives)
            .AddMembers(classDecl)
            .NormalizeWhitespace();

        var syntaxTree = CSharpSyntaxTree.Create(compilationUnit);
        var metadataReferences = GetMetadataReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName: "__RazorBlock_" + Guid.NewGuid().ToString("N"),
            syntaxTrees: [syntaxTree],
            references: metadataReferences,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(OptimizationLevel.Release));

        using var ms = new System.IO.MemoryStream();
        var emitResult = compilation.Emit(ms);

        if (!emitResult.Success)
        {
            var errors = emitResult.Diagnostics
                .Where(static d => d.Severity == DiagnosticSeverity.Error)
                .Select(static d => d.ToString());
            throw new XamlParseException(
                $"Razor code block compilation failed:\n{string.Join("\n", errors)}\n\nGenerated source:\n{compilationUnit.ToFullString()}");
        }

        ms.Seek(0, System.IO.SeekOrigin.Begin);
        var assembly = Assembly.Load(ms.ToArray());
        var type = assembly.GetType("__RazorRunner")!;
        var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
        var result = (string?)method.Invoke(null, null);
        return result ?? string.Empty;
    }

    private static SyntaxList<StatementSyntax> ParseStatementList(string script)
    {
        var wrapper = $"void __M() {{ {script} }}";
        var tree = CSharpSyntaxTree.ParseText(wrapper);
        var root = tree.GetRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (method?.Body != null)
            return method.Body.Statements;

        var block = (BlockSyntax)ParseStatement($"{{ {script} }}");
        return block.Statements;
    }

    private static MetadataReference[] GetMetadataReferences()
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrEmpty(trustedAssemblies))
        {
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
