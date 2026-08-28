using System.Text;
using System.Text.RegularExpressions;

namespace Jalium.UI.Controls;

/// <summary>GFM 表格的列对齐，由分隔行里的冒号决定。</summary>
internal enum MarkdownColumnAlignment
{
    None,
    Left,
    Center,
    Right,
}

internal abstract record MarkdownBlock;
internal sealed record MarkdownParagraphBlock(IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;
internal sealed record MarkdownHeadingBlock(int Level, IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;
internal sealed record MarkdownListBlock(bool Ordered, int StartIndex, IReadOnlyList<MarkdownListItemBlock> Items, bool IsLoose = false) : MarkdownBlock;
internal sealed record MarkdownListItemBlock(bool? TaskState, IReadOnlyList<MarkdownBlock> Blocks);
internal sealed record MarkdownQuoteBlock(IReadOnlyList<MarkdownBlock> Blocks) : MarkdownBlock;
internal sealed record MarkdownCodeBlock(string Text, string? Language) : MarkdownBlock;
internal sealed record MarkdownRuleBlock() : MarkdownBlock;
internal sealed record MarkdownTableBlock(
    IReadOnlyList<MarkdownTableRow> HeaderRows,
    IReadOnlyList<MarkdownTableRow> Rows,
    IReadOnlyList<MarkdownColumnAlignment>? Alignments = null) : MarkdownBlock;
internal sealed record MarkdownTableRow(IReadOnlyList<IReadOnlyList<MarkdownInline>> Cells);
internal sealed record MarkdownFootnoteDefinitionBlock(string Label, int Number, IReadOnlyList<MarkdownBlock> Blocks) : MarkdownBlock;

internal abstract record MarkdownInline;
internal sealed record MarkdownTextInline(string Text) : MarkdownInline;
internal sealed record MarkdownStrongInline(IReadOnlyList<MarkdownInline> Children) : MarkdownInline;
internal sealed record MarkdownEmphasisInline(IReadOnlyList<MarkdownInline> Children) : MarkdownInline;
internal sealed record MarkdownStrikethroughInline(IReadOnlyList<MarkdownInline> Children) : MarkdownInline;
internal sealed record MarkdownCodeInline(string Text) : MarkdownInline;
internal sealed record MarkdownLinkInline(IReadOnlyList<MarkdownInline> Children, Uri? Uri, string Target, string? Title = null) : MarkdownInline;
internal sealed record MarkdownImageInline(string Alt, Uri? Uri, string Target, string? Title = null) : MarkdownInline;
internal sealed record MarkdownFootnoteReferenceInline(string Label, int Number) : MarkdownInline;
internal sealed record MarkdownLineBreakInline() : MarkdownInline;

internal sealed record MarkdownLinkDefinition(string Target, Uri? Uri, string? Title);

/// <summary>
/// 一次解析所共享的文档级信息：相对链接的基地址，以及链接引用定义与脚注定义这两张需要全文档可见的表。
/// </summary>
/// <remarks>
/// 引用式链接（<c>[text][label]</c>）与脚注（<c>[^label]</c>）的定义可以写在文档任何位置，
/// 所以必须先扫一遍全文再解析行内——这也是块与块之间唯一的跨块状态。增量解析靠
/// <see cref="DefinitionsSignature"/> 判断追加的文本有没有引入新定义：没有才允许只重解析尾部。
/// </remarks>
internal sealed class MarkdownParseContext
{
    private static readonly MarkdownParseContext s_empty = new(null, new(), new());

    private readonly Dictionary<string, MarkdownLinkDefinition> _linkDefinitions;
    private readonly Dictionary<string, int> _footnoteNumbers;

    private MarkdownParseContext(
        Uri? baseUri,
        Dictionary<string, MarkdownLinkDefinition> linkDefinitions,
        Dictionary<string, int> footnoteNumbers)
    {
        BaseUri = baseUri;
        _linkDefinitions = linkDefinitions;
        _footnoteNumbers = footnoteNumbers;
    }

    public Uri? BaseUri { get; }

    /// <summary>GFM 的裸链接识别（<c>https://…</c>、<c>www.…</c>、邮箱）。</summary>
    public bool EnableBareAutolinks { get; private init; } = true;

    /// <summary>
    /// 文本还在增长（流式输出）。开启后，文档末尾那些「话没说完」的结构按已经闭合来解析。
    /// </summary>
    /// <remarks>
    /// Markdown 的语义要看到闭合记号才能定下来，逐字渲染因此天生会抖：<c>**bold**</c> 打到一半时
    /// 先是字面量 <c>**bold</c>，再变成斜体 <c>*bold*</c>，最后才是粗体——每个粗体词都要跳两次。
    /// 乐观闭合把「尾部未闭合」当成「即将闭合」，让渲染沿着一个方向长出来，而不是反复改主意。
    /// 只作用于文档末尾：已经闭合的部分语义不受影响。
    /// </remarks>
    public bool IsStreaming { get; private init; }

    public bool HasDefinitions => _linkDefinitions.Count > 0 || _footnoteNumbers.Count > 0;

    /// <summary>用于比对两次扫描是否得到同一张定义表，供增量解析判断能否走快路径。</summary>
    public string DefinitionsSignature { get; private init; } = string.Empty;

    public static MarkdownParseContext Empty(Uri? baseUri) =>
        baseUri == null ? s_empty : new MarkdownParseContext(baseUri, new(), new());

    public static MarkdownParseContext Create(
        string[] lines, Uri? baseUri, bool enableBareAutolinks = true, bool isStreaming = false)
    {
        var links = new Dictionary<string, MarkdownLinkDefinition>(StringComparer.Ordinal);
        var footnotes = new Dictionary<string, int>(StringComparer.Ordinal);
        var signature = new StringBuilder();

        var inFence = false;
        var fenceChar = '\0';
        var fenceLength = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();

            if (inFence)
            {
                if (trimmed.Length >= fenceLength && trimmed.TrimEnd().All(c => c == fenceChar))
                {
                    inFence = false;
                }

                continue;
            }

            if (MarkdownParser.TryGetFenceInfo(trimmed, out var marker, out var count, out _))
            {
                inFence = true;
                fenceChar = marker;
                fenceLength = count;
                continue;
            }

            // 缩进代码块里的内容不是定义。
            if (MarkdownParser.CountIndent(line) >= 4)
            {
                continue;
            }

            if (MarkdownParser.TryParseFootnoteLabel(trimmed, out var footnoteLabel) &&
                !footnotes.ContainsKey(footnoteLabel))
            {
                footnotes[footnoteLabel] = footnotes.Count + 1;
                signature.Append("F:").Append(footnoteLabel).Append('\n');
                continue;
            }

            if (MarkdownParser.TryParseLinkDefinitionLine(trimmed, baseUri, out var label, out var definition) &&
                !links.ContainsKey(label))
            {
                links[label] = definition;
                signature.Append("L:").Append(label).Append('=').Append(definition.Target).Append('\n');
            }
        }

        return new MarkdownParseContext(baseUri, links, footnotes)
        {
            DefinitionsSignature = signature.ToString(),
            EnableBareAutolinks = enableBareAutolinks,
            IsStreaming = isStreaming,
        };
    }

    public bool TryGetLinkDefinition(string label, out MarkdownLinkDefinition definition) =>
        _linkDefinitions.TryGetValue(NormalizeLabel(label), out definition!);

    public bool TryGetFootnoteNumber(string label, out int number) =>
        _footnoteNumbers.TryGetValue(NormalizeLabel(label), out number);

    public Uri? ResolveUri(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        if (Uri.TryCreate(target, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        if (BaseUri != null && Uri.TryCreate(BaseUri, target, out var relativeToBase))
        {
            return relativeToBase;
        }

        return Uri.TryCreate(target, UriKind.Relative, out var relative) ? relative : null;
    }

    /// <summary>CommonMark 的标签比较规则：大小写不敏感，连续空白折叠成一个空格。</summary>
    public static string NormalizeLabel(string label)
    {
        var builder = new StringBuilder(label.Length);
        var pendingSpace = false;

        foreach (var c in label.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}

internal static class MarkdownParser
{
    private static readonly Regex s_tableSeparatorRegex =
        new(@"^\s*\|?\s*:?-+:?\s*(?:\|\s*:?-+:?\s*)*\|?\s*$", RegexOptions.Compiled);

    private static readonly Regex s_linkDefinitionRegex =
        new(@"^\[((?:[^\[\]\\]|\\.)+)\]:\s*(?:<([^>]*)>|([^\s]+))(?:\s+(?:""([^""]*)""|'([^']*)'|\(([^)]*)\)))?\s*$",
            RegexOptions.Compiled);

    /// <summary>分隔行「打到一半」的样子，只在流式解析里用来提前把表格立起来。</summary>
    private static readonly Regex s_partialTableSeparatorRegex =
        new(@"^\s*\|?\s*:?-+:?\s*(?:\|\s*:?-*:?\s*)*\|?\s*$", RegexOptions.Compiled);

    private static readonly Regex s_footnoteDefinitionRegex =
        new(@"^\[\^([^\]\s]+)\]:", RegexOptions.Compiled);

    /// <summary>会被整体跳过、内容不做 Markdown 解析的 HTML 块标签。</summary>
    private static readonly HashSet<string> s_rawHtmlBlockTags =
        new(StringComparer.OrdinalIgnoreCase) { "script", "style", "textarea" };

    public static IReadOnlyList<MarkdownBlock> Parse(string? markdown, Uri? baseUri)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return Array.Empty<MarkdownBlock>();
        }

        return ParseLines(Normalize(markdown).Split('\n'), baseUri, blockLineStarts: null);
    }

    /// <summary>
    /// 解析一段<b>已规范化</b>的行（<see cref="Normalize"/> 的产物），并可选地回填每个顶层块的起始行号。
    /// </summary>
    internal static IReadOnlyList<MarkdownBlock> ParseLines(string[] lines, Uri? baseUri, List<int>? blockLineStarts) =>
        ParseLines(lines, MarkdownParseContext.Create(lines, baseUri), blockLineStarts);

    /// <summary>
    /// 用一份已经建好的上下文解析行。除了链接引用与脚注定义（都在上下文里），顶层块之间没有跨块状态，
    /// 因此从任意一个块的起始行重新解析，得到的结果与全量解析在该行之后的部分一致——增量解析正是靠这一点。
    /// </summary>
    internal static IReadOnlyList<MarkdownBlock> ParseLines(
        string[] lines, MarkdownParseContext context, List<int>? blockLineStarts)
    {
        var blocks = new List<MarkdownBlock>();
        var index = 0;

        while (index < lines.Length)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                index++;
                continue;
            }

            var start = index;
            var previousIndex = index;
            var block = ParseSingleBlock(lines, ref index, context);

            // 链接引用定义不产生内容；它已经被上下文收走了。
            if (block != null)
            {
                blocks.Add(block);
                blockLineStarts?.Add(start);
            }

            if (index <= previousIndex)
            {
                index = previousIndex + 1;
            }
        }

        return blocks;
    }

    private static IReadOnlyList<MarkdownBlock> ParseBlocks(string[] lines, ref int index, MarkdownParseContext context)
    {
        var blocks = new List<MarkdownBlock>();

        while (index < lines.Length)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                index++;
                continue;
            }

            var previousIndex = index;
            var block = ParseSingleBlock(lines, ref index, context);
            if (block != null)
            {
                blocks.Add(block);
            }

            if (index <= previousIndex)
            {
                index = previousIndex + 1;
            }
        }

        return blocks;
    }

    private static MarkdownBlock? ParseSingleBlock(string[] lines, ref int index, MarkdownParseContext context)
    {
        var line = lines[index];
        var trimmed = line.TrimStart();

        // 缩进 4 空格以上、又不接在段落后面的行是缩进代码块。
        if (CountIndent(line) >= 4)
        {
            return ParseIndentedCodeBlock(lines, ref index);
        }

        if (TryParseFencedCodeBlock(lines, ref index, context, out var codeBlock))
        {
            return codeBlock;
        }

        if (TryParseFootnoteLabel(trimmed, out var footnoteLabel) &&
            context.TryGetFootnoteNumber(footnoteLabel, out var footnoteNumber))
        {
            return ParseFootnoteDefinition(lines, ref index, context, footnoteLabel, footnoteNumber);
        }

        if (TryParseLinkDefinitionLine(trimmed, context.BaseUri, out _, out _))
        {
            index++;
            return null;
        }

        if (IsHorizontalRule(trimmed))
        {
            index++;
            return new MarkdownRuleBlock();
        }

        if (TryParseAtxHeading(trimmed, out var level, out var headingContent))
        {
            index++;
            return new MarkdownHeadingBlock(level, MarkdownInlineParser.Parse(headingContent, context));
        }

        if (IsBlockQuoteLine(trimmed))
        {
            return ParseQuote(lines, ref index, context);
        }

        if (TryMatchListMarker(line, out _))
        {
            return ParseList(lines, ref index, context);
        }

        if (IsTableStart(lines, index, context))
        {
            return ParseTable(lines, ref index, context);
        }

        if (IsHtmlBlockStart(trimmed))
        {
            return ParseHtmlBlock(lines, ref index, context);
        }

        return ParseParagraph(lines, ref index, context);
    }

    #region Code blocks

    private static bool TryParseFencedCodeBlock(
        string[] lines, ref int index, MarkdownParseContext context, out MarkdownCodeBlock block)
    {
        block = null!;
        var trimmed = lines[index].TrimStart();
        if (!TryGetFenceInfo(trimmed, out var fenceChar, out var fenceLength, out var language))
        {
            return false;
        }

        var indent = CountIndent(lines[index]);
        var builder = new StringBuilder();
        index++;

        while (index < lines.Length)
        {
            var current = lines[index].TrimStart();
            if (current.Length >= fenceLength &&
                current[0] == fenceChar &&
                current.TrimEnd().All(c => c == fenceChar))
            {
                index++;
                break;
            }

            // 流式时，文档最后一行如果是「正在打出来的闭合围栏」（一到两个同款记号，后面什么都没有了），
            // 先别当代码内容——否则收尾那两下会先显示成代码，再凭空消失。
            if (context.IsStreaming &&
                index == lines.Length - 1 &&
                current.Length > 0 &&
                current.Length < fenceLength &&
                current.All(c => c == fenceChar))
            {
                index++;
                break;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            // 围栏本身的缩进要从内容里扣掉，否则缩进过的代码块每行都多出前导空格。
            var content = lines[index];
            var strip = 0;
            while (strip < indent && strip < content.Length && content[strip] == ' ')
            {
                strip++;
            }

            builder.Append(content.AsSpan(strip));
            index++;
        }

        block = new MarkdownCodeBlock(builder.ToString(), language);
        return true;
    }

    private static MarkdownCodeBlock ParseIndentedCodeBlock(string[] lines, ref int index)
    {
        var collected = new List<string>();
        var lastContentLine = -1;

        while (index < lines.Length)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                collected.Add(string.Empty);
                index++;
                continue;
            }

            if (CountIndent(line) < 4)
            {
                break;
            }

            lastContentLine = collected.Count;
            collected.Add(line[4..]);
            index++;
        }

        // 结尾的空行不属于代码块，退还给外层。
        var trailing = collected.Count - 1 - lastContentLine;
        if (trailing > 0)
        {
            collected.RemoveRange(lastContentLine + 1, trailing);
            index -= trailing;
        }

        return new MarkdownCodeBlock(string.Join('\n', collected), null);
    }

    internal static bool TryGetFenceInfo(string line, out char fenceChar, out int fenceLength, out string? language)
    {
        fenceChar = '\0';
        fenceLength = 0;
        language = null;

        if (line.Length < 3)
        {
            return false;
        }

        var marker = line[0];
        if (marker != '`' && marker != '~')
        {
            return false;
        }

        var count = 0;
        while (count < line.Length && line[count] == marker)
        {
            count++;
        }

        if (count < 3)
        {
            return false;
        }

        var remainder = line[count..].Trim();

        // ``` 的信息串里不能再出现反引号，否则 `a``b` 这类行内代码会被误判成围栏。
        if (marker == '`' && remainder.Contains('`', StringComparison.Ordinal))
        {
            return false;
        }

        fenceChar = marker;
        fenceLength = count;
        language = string.IsNullOrWhiteSpace(remainder) ? null : remainder;
        return true;
    }

    #endregion

    #region Headings, rules, definitions

    private static bool IsHorizontalRule(string trimmedLine)
    {
        var content = trimmedLine.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\t", string.Empty, StringComparison.Ordinal);
        if (content.Length < 3)
        {
            return false;
        }

        var marker = content[0];
        if (marker != '-' && marker != '*' && marker != '_')
        {
            return false;
        }

        for (var index = 1; index < content.Length; index++)
        {
            if (content[index] != marker)
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseAtxHeading(string trimmedLine, out int level, out string content)
    {
        level = 0;
        content = string.Empty;

        while (level < trimmedLine.Length && level < 7 && trimmedLine[level] == '#')
        {
            level++;
        }

        if (level == 0 || level > 6)
        {
            return false;
        }

        if (level < trimmedLine.Length && trimmedLine[level] != ' ')
        {
            return false;
        }

        content = trimmedLine[level..].Trim();

        // 闭合序列 `### 标题 ###` 里结尾那串 # 只是装饰，不是内容。
        var end = content.Length;
        while (end > 0 && content[end - 1] == '#')
        {
            end--;
        }

        if (end != content.Length && (end == 0 || content[end - 1] == ' '))
        {
            content = content[..end].TrimEnd();
        }

        return true;
    }

    /// <summary>Setext 下划线：<c>===</c> 是一级标题，<c>---</c> 是二级。</summary>
    private static bool TryGetSetextLevel(string line, out int level)
    {
        level = 0;
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || CountIndent(line) >= 4)
        {
            return false;
        }

        var marker = trimmed[0];
        if (marker != '=' && marker != '-')
        {
            return false;
        }

        foreach (var c in trimmed)
        {
            if (c != marker)
            {
                return false;
            }
        }

        level = marker == '=' ? 1 : 2;
        return true;
    }

    internal static bool TryParseFootnoteLabel(string trimmedLine, out string label)
    {
        var match = s_footnoteDefinitionRegex.Match(trimmedLine);
        label = match.Success ? MarkdownParseContext.NormalizeLabel(match.Groups[1].Value) : string.Empty;
        return match.Success;
    }

    internal static bool TryParseLinkDefinitionLine(
        string trimmedLine, Uri? baseUri, out string label, out MarkdownLinkDefinition definition)
    {
        label = string.Empty;
        definition = null!;

        if (trimmedLine.Length < 4 || trimmedLine[0] != '[' || trimmedLine[1] == '^')
        {
            return false;
        }

        var match = s_linkDefinitionRegex.Match(trimmedLine);
        if (!match.Success)
        {
            return false;
        }

        var target = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
        if (string.IsNullOrEmpty(target))
        {
            return false;
        }

        var title = match.Groups[4].Success ? match.Groups[4].Value
            : match.Groups[5].Success ? match.Groups[5].Value
            : match.Groups[6].Success ? match.Groups[6].Value
            : null;

        label = MarkdownParseContext.NormalizeLabel(match.Groups[1].Value);
        if (label.Length == 0)
        {
            return false;
        }

        target = MarkdownEntities.DecodeAll(target);
        definition = new MarkdownLinkDefinition(
            target,
            MarkdownParseContext.Empty(baseUri).ResolveUri(target),
            title == null ? null : MarkdownEntities.DecodeAll(title));
        return true;
    }

    private static MarkdownFootnoteDefinitionBlock ParseFootnoteDefinition(
        string[] lines, ref int index, MarkdownParseContext context, string label, int number)
    {
        var colon = lines[index].IndexOf(':', StringComparison.Ordinal);
        var bodyLines = new List<string> { lines[index][(colon + 1)..].TrimStart() };
        index++;

        // 后续缩进行属于同一条脚注，空行只有在下一行还缩进时才保留。
        while (index < lines.Length)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                var lookahead = index + 1;
                while (lookahead < lines.Length && string.IsNullOrWhiteSpace(lines[lookahead]))
                {
                    lookahead++;
                }

                if (lookahead >= lines.Length || CountIndent(lines[lookahead]) < 4)
                {
                    break;
                }

                bodyLines.Add(string.Empty);
                index++;
                continue;
            }

            if (CountIndent(line) < 4)
            {
                break;
            }

            bodyLines.Add(line[4..]);
            index++;
        }

        var nestedIndex = 0;
        var blocks = ParseBlocks(bodyLines.ToArray(), ref nestedIndex, context);
        return new MarkdownFootnoteDefinitionBlock(label, number, blocks);
    }

    #endregion

    #region Quotes

    private static bool IsBlockQuoteLine(string trimmedLine) =>
        trimmedLine.StartsWith(">", StringComparison.Ordinal);

    private static MarkdownQuoteBlock ParseQuote(string[] lines, ref int index, MarkdownParseContext context)
    {
        var quoteLines = new List<string>();

        while (index < lines.Length)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                break;
            }

            var trimmed = lines[index].TrimStart();
            if (!trimmed.StartsWith(">", StringComparison.Ordinal))
            {
                // 懒惰续行：引用内的段落没结束时，不带 > 的普通文本行仍属于它。
                if (quoteLines.Count == 0 ||
                    quoteLines[^1].Length == 0 ||
                    StartsNewBlock(lines, index, context))
                {
                    break;
                }

                quoteLines.Add(trimmed);
                index++;
                continue;
            }

            var content = trimmed[1..];
            if (content.StartsWith(" ", StringComparison.Ordinal))
            {
                content = content[1..];
            }

            quoteLines.Add(content);
            index++;
        }

        var nestedIndex = 0;
        var nestedBlocks = ParseBlocks(quoteLines.ToArray(), ref nestedIndex, context);
        return new MarkdownQuoteBlock(nestedBlocks);
    }

    /// <summary>该行是否会开启一个新块——用来判断段落/引用的续行到哪里为止。</summary>
    private static bool StartsNewBlock(string[] lines, int index, MarkdownParseContext context)
    {
        var line = lines[index];
        var trimmed = line.TrimStart();
        return TryGetFenceInfo(trimmed, out _, out _, out _) ||
               TryParseAtxHeading(trimmed, out _, out _) ||
               IsHorizontalRule(trimmed) ||
               IsBlockQuoteLine(trimmed) ||
               TryMatchListMarker(line, out _) ||
               IsTableStart(lines, index, context) ||
               IsHtmlBlockStart(trimmed);
    }

    #endregion

    #region Lists

    private static MarkdownListBlock ParseList(string[] lines, ref int index, MarkdownParseContext context)
    {
        TryMatchListMarker(lines[index], out var firstMatch);
        var items = new List<MarkdownListItemBlock>();
        var ordered = firstMatch.Ordered;
        var indent = firstMatch.Indent;
        var startIndex = firstMatch.StartIndex;
        var isLoose = false;

        while (index < lines.Length)
        {
            if (!TryMatchListMarker(lines[index], out var match) ||
                match.Indent != indent ||
                match.Ordered != ordered)
            {
                break;
            }

            var itemLines = new List<string> { match.Content };
            var taskState = match.TaskState;
            var contentIndent = match.ContentIndent;
            index++;

            while (index < lines.Length)
            {
                var line = lines[index];
                if (string.IsNullOrWhiteSpace(line))
                {
                    var lookahead = index + 1;
                    while (lookahead < lines.Length && string.IsNullOrWhiteSpace(lines[lookahead]))
                    {
                        lookahead++;
                    }

                    if (lookahead >= lines.Length)
                    {
                        index = lookahead;
                        break;
                    }

                    var nextLine = lines[lookahead];
                    if (CountIndent(nextLine) < contentIndent)
                    {
                        // 空行后仍是同一层的列表项：列表继续，只是变成「松散」的。
                        if (TryMatchListMarker(nextLine, out var sibling) &&
                            sibling.Indent == indent &&
                            sibling.Ordered == ordered)
                        {
                            isLoose = true;
                            index = lookahead;
                        }

                        break;
                    }

                    isLoose = true;
                    itemLines.Add(string.Empty);
                    index++;
                    continue;
                }

                if (TryMatchListMarker(line, out var nextMatch) &&
                    nextMatch.Indent == indent &&
                    nextMatch.Ordered == ordered)
                {
                    break;
                }

                var lineIndent = CountIndent(line);
                if (lineIndent < contentIndent && !IsLazyListContinuation(lines, index, itemLines, context))
                {
                    break;
                }

                // 按标记宽度回退缩进，而不是固定两格——"1. " 与 "- " 的内容起点并不一样。
                itemLines.Add(line[Math.Min(lineIndent, contentIndent)..]);
                index++;
            }

            var nestedIndex = 0;
            var nestedBlocks = ParseBlocks(itemLines.ToArray(), ref nestedIndex, context);
            if (nestedBlocks.Count == 0)
            {
                nestedBlocks = new[] { new MarkdownParagraphBlock(Array.Empty<MarkdownInline>()) };
            }

            items.Add(new MarkdownListItemBlock(taskState, nestedBlocks));
        }

        return new MarkdownListBlock(ordered, startIndex, items, isLoose);
    }

    /// <summary>缩进不足但仍属于当前列表项的续行：段落还没被空行断开，且这一行不开启新块。</summary>
    private static bool IsLazyListContinuation(
        string[] lines, int index, List<string> itemLines, MarkdownParseContext context) =>
        itemLines.Count > 0 && itemLines[^1].Length > 0 && !StartsNewBlock(lines, index, context);

    private static bool TryMatchListMarker(string line, out ListMarkerMatch match)
    {
        match = default;

        var indent = CountIndent(line);

        // 缩进四格以上的是代码块，不是列表。
        if (indent >= 4 || indent >= line.Length)
        {
            return false;
        }

        var trimmed = line[indent..];
        if (trimmed.Length < 2)
        {
            return false;
        }

        if (char.IsAsciiDigit(trimmed[0]))
        {
            var cursor = 0;
            while (cursor < trimmed.Length && char.IsAsciiDigit(trimmed[cursor]) && cursor < 9)
            {
                cursor++;
            }

            if (cursor + 1 >= trimmed.Length ||
                (trimmed[cursor] != '.' && trimmed[cursor] != ')') ||
                trimmed[cursor + 1] != ' ')
            {
                return false;
            }

            var number = int.Parse(trimmed[..cursor]);
            var markerWidth = cursor + 2;
            match = new ListMarkerMatch(
                indent,
                Ordered: true,
                StartIndex: number,
                Content: trimmed[markerWidth..],
                TaskState: null,
                ContentIndent: indent + markerWidth);
            return true;
        }

        var marker = trimmed[0];
        if ((marker != '-' && marker != '+' && marker != '*') || trimmed[1] != ' ')
        {
            return false;
        }

        var content = trimmed[2..];
        var contentIndent = indent + 2;
        bool? taskState = null;
        if (content.Length >= 3 &&
            content[0] == '[' &&
            content[2] == ']' &&
            (content[1] == ' ' || content[1] == 'x' || content[1] == 'X'))
        {
            taskState = content[1] is 'x' or 'X';
            var consumed = content.Length > 3 && content[3] == ' ' ? 4 : 3;
            content = content[consumed..];
            contentIndent += consumed;
        }

        match = new ListMarkerMatch(indent, Ordered: false, StartIndex: 1, Content: content, TaskState: taskState, ContentIndent: contentIndent);
        return true;
    }

    #endregion

    #region Tables

    private static bool IsTableStart(string[] lines, int index, MarkdownParseContext context)
    {
        if (index + 1 >= lines.Length)
        {
            return false;
        }

        var header = lines[index].Trim();
        var separator = lines[index + 1].Trim();
        if (!header.Contains('|', StringComparison.Ordinal))
        {
            return false;
        }

        // 流式时分隔行还在打，先按表格渲染，剩下的列随着输入补齐——总比让整张表先以一行原始文本的
        // 样子出现、再整体跳成表格要稳。只在它确实是文档最后一行时才这么宽容。
        var isTypingSeparator = context.IsStreaming && index + 2 >= lines.Length;

        if (!s_tableSeparatorRegex.IsMatch(separator))
        {
            return isTypingSeparator && s_partialTableSeparatorRegex.IsMatch(separator);
        }

        // GFM 要求分隔行的列数与表头一致，否则 `a | b` 后面跟一条 `---` 分隔线会被误当成表。
        var headerColumns = SplitTableCells(header).Count;
        var separatorColumns = SplitTableCells(separator).Count;
        return separatorColumns == headerColumns ||
               (isTypingSeparator && separatorColumns < headerColumns);
    }

    private static MarkdownTableBlock ParseTable(string[] lines, ref int index, MarkdownParseContext context)
    {
        var headerCells = SplitTableCells(lines[index].Trim());
        var alignments = ParseAlignments(SplitTableCells(lines[index + 1].Trim()));
        var header = BuildTableRow(headerCells, headerCells.Count, context);
        index += 2;

        var rows = new List<MarkdownTableRow>();
        while (index < lines.Length)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line) || !line.Contains('|', StringComparison.Ordinal))
            {
                break;
            }

            var trimmed = line.Trim();
            if (IsHorizontalRule(trimmed) || TryGetFenceInfo(trimmed, out _, out _, out _))
            {
                break;
            }

            // GFM：多出的单元格丢弃，缺少的补空，这样表格永远是矩形。
            rows.Add(BuildTableRow(SplitTableCells(trimmed), headerCells.Count, context));
            index++;
        }

        return new MarkdownTableBlock(new[] { header }, rows, alignments);
    }

    private static MarkdownTableRow BuildTableRow(List<string> cells, int columnCount, MarkdownParseContext context)
    {
        var result = new IReadOnlyList<MarkdownInline>[columnCount];
        for (var column = 0; column < columnCount; column++)
        {
            result[column] = column < cells.Count
                ? MarkdownInlineParser.Parse(cells[column], context)
                : Array.Empty<MarkdownInline>();
        }

        return new MarkdownTableRow(result);
    }

    /// <summary>按未转义的竖线切分单元格：<c>\|</c> 是内容里的竖线，不是列分隔符。</summary>
    private static List<string> SplitTableCells(string line)
    {
        var trimmed = line.Trim();
        var start = 0;
        var end = trimmed.Length;

        if (start < end && trimmed[start] == '|')
        {
            start++;
        }

        if (end > start && trimmed[end - 1] == '|' && !IsEscaped(trimmed, end - 1))
        {
            end--;
        }

        var cells = new List<string>();
        var builder = new StringBuilder();
        for (var index = start; index < end; index++)
        {
            var c = trimmed[index];
            if (c == '\\' && index + 1 < end && trimmed[index + 1] == '|')
            {
                builder.Append('|');
                index++;
                continue;
            }

            if (c == '|')
            {
                cells.Add(builder.ToString().Trim());
                builder.Clear();
                continue;
            }

            builder.Append(c);
        }

        cells.Add(builder.ToString().Trim());
        return cells;
    }

    private static IReadOnlyList<MarkdownColumnAlignment> ParseAlignments(List<string> separatorCells)
    {
        var alignments = new MarkdownColumnAlignment[separatorCells.Count];
        for (var index = 0; index < separatorCells.Count; index++)
        {
            var cell = separatorCells[index].Trim();
            var left = cell.StartsWith(':');
            var right = cell.EndsWith(':');
            alignments[index] = (left, right) switch
            {
                (true, true) => MarkdownColumnAlignment.Center,
                (true, false) => MarkdownColumnAlignment.Left,
                (false, true) => MarkdownColumnAlignment.Right,
                _ => MarkdownColumnAlignment.None,
            };
        }

        return alignments;
    }

    private static bool IsEscaped(string text, int index)
    {
        var slashCount = 0;
        for (var cursor = index - 1; cursor >= 0 && text[cursor] == '\\'; cursor--)
        {
            slashCount++;
        }

        return slashCount % 2 == 1;
    }

    #endregion

    #region HTML blocks

    private static bool IsHtmlBlockStart(string trimmedLine)
    {
        if (trimmedLine.Length < 2 || trimmedLine[0] != '<')
        {
            return false;
        }

        if (trimmedLine.StartsWith("<!--", StringComparison.Ordinal))
        {
            return true;
        }

        var cursor = trimmedLine[1] == '/' ? 2 : 1;
        if (cursor >= trimmedLine.Length || !char.IsAsciiLetter(trimmedLine[cursor]))
        {
            return false;
        }

        var nameEnd = cursor;
        while (nameEnd < trimmedLine.Length && (char.IsAsciiLetterOrDigit(trimmedLine[nameEnd]) || trimmedLine[nameEnd] == '-'))
        {
            nameEnd++;
        }

        // 只有整行以标签开头、后面接属性或直接闭合的才算 HTML 块；`<https://…>` 之类留给行内自动链接。
        var rest = trimmedLine[nameEnd..];
        return rest.Length == 0 || rest[0] is ' ' or '>' or '/' or '\t';
    }

    /// <summary>
    /// HTML 块按「剥标签、留文字」处理：把原始标签直接当正文显示既难看又不安全，而这里只保留可读内容。
    /// <c>&lt;pre&gt;</c> 例外——它的内容本来就要保留格式，转成代码块。
    /// </summary>
    private static MarkdownBlock? ParseHtmlBlock(string[] lines, ref int index, MarkdownParseContext context)
    {
        var tagName = ReadHtmlBlockTagName(lines[index].TrimStart());
        var isPre = string.Equals(tagName, "pre", StringComparison.OrdinalIgnoreCase);
        var isRaw = s_rawHtmlBlockTags.Contains(tagName);
        var closing = isPre || isRaw ? "</" + tagName : null;

        var collected = new List<string>();
        while (index < lines.Length)
        {
            var line = lines[index];
            if (closing == null && string.IsNullOrWhiteSpace(line))
            {
                break;
            }

            collected.Add(line);
            index++;

            if (closing != null && line.Contains(closing, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
        }

        if (isRaw)
        {
            return null;
        }

        var text = string.Join('\n', collected);

        if (isPre)
        {
            var open = text.IndexOf('>', StringComparison.Ordinal);
            var close = text.LastIndexOf("</pre", StringComparison.OrdinalIgnoreCase);
            var body = open >= 0 && close > open ? text[(open + 1)..close] : text;
            return new MarkdownCodeBlock(MarkdownEntities.DecodeAll(body.Trim('\n')), null);
        }

        var inlines = TrimOuterWhitespace(MarkdownInlineParser.Parse(
            string.Join(' ', collected.Select(static l => l.Trim()).Where(static l => l.Length > 0)),
            context));

        return inlines.Count == 0 ? null : new MarkdownParagraphBlock(inlines);
    }

    /// <summary>剥掉标签后前后常会剩下空白（标签本来占着行），这里把它们收掉。</summary>
    private static IReadOnlyList<MarkdownInline> TrimOuterWhitespace(IReadOnlyList<MarkdownInline> inlines)
    {
        var result = new List<MarkdownInline>(inlines);

        if (result.Count > 0 && result[0] is MarkdownTextInline first)
        {
            var trimmed = first.Text.TrimStart();
            result[0] = new MarkdownTextInline(trimmed);
            if (trimmed.Length == 0)
            {
                result.RemoveAt(0);
            }
        }

        if (result.Count > 0 && result[^1] is MarkdownTextInline last)
        {
            var trimmed = last.Text.TrimEnd();
            result[^1] = new MarkdownTextInline(trimmed);
            if (trimmed.Length == 0)
            {
                result.RemoveAt(result.Count - 1);
            }
        }

        return result;
    }

    private static string ReadHtmlBlockTagName(string trimmedLine)
    {
        var cursor = trimmedLine.Length > 1 && trimmedLine[1] == '/' ? 2 : 1;
        var end = cursor;
        while (end < trimmedLine.Length && (char.IsAsciiLetterOrDigit(trimmedLine[end]) || trimmedLine[end] == '-'))
        {
            end++;
        }

        return end > cursor ? trimmedLine[cursor..end] : string.Empty;
    }

    #endregion

    #region Paragraphs

    private static MarkdownBlock ParseParagraph(string[] lines, ref int index, MarkdownParseContext context)
    {
        var paragraphLines = new List<string>();

        while (index < lines.Length)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                break;
            }

            if (paragraphLines.Count > 0)
            {
                // Setext 下划线接在段落后面就是标题，优先于把 --- 读成分隔线。
                if (TryGetSetextLevel(lines[index], out var setextLevel))
                {
                    index++;
                    return new MarkdownHeadingBlock(
                        setextLevel,
                        MarkdownInlineParser.Parse(JoinParagraphLines(paragraphLines, out _), context));
                }

                if (StartsNewBlock(lines, index, context))
                {
                    break;
                }
            }

            paragraphLines.Add(lines[index].TrimStart());
            index++;
        }

        var inlines = new List<MarkdownInline>();
        for (var lineIndex = 0; lineIndex < paragraphLines.Count; lineIndex++)
        {
            var line = paragraphLines[lineIndex];
            var explicitBreak = line.EndsWith("  ", StringComparison.Ordinal) ||
                                line.EndsWith("\\", StringComparison.Ordinal);
            var content = explicitBreak ? line.TrimEnd(' ', '\\') : line.Trim();

            inlines.AddRange(MarkdownInlineParser.Parse(content, context));
            if (lineIndex < paragraphLines.Count - 1)
            {
                inlines.Add(explicitBreak ? new MarkdownLineBreakInline() : new MarkdownTextInline(" "));
            }
        }

        return new MarkdownParagraphBlock(MergeTextInlines(inlines));
    }

    private static string JoinParagraphLines(List<string> paragraphLines, out bool hadBreak)
    {
        hadBreak = false;
        return string.Join(' ', paragraphLines.Select(static l => l.Trim()));
    }

    #endregion

    #region Inline helpers

    /// <summary>解析一段行内文本。保留给需要单独排版一小段 Markdown 的调用方。</summary>
    internal static IReadOnlyList<MarkdownInline> ParseInlines(string text, Uri? baseUri) =>
        MarkdownInlineParser.Parse(text, MarkdownParseContext.Empty(baseUri));

    private static IReadOnlyList<MarkdownInline> MergeTextInlines(IEnumerable<MarkdownInline> source)
    {
        var merged = new List<MarkdownInline>();
        StringBuilder? textBuffer = null;

        foreach (var inline in source)
        {
            if (inline is MarkdownTextInline textInline)
            {
                textBuffer ??= new StringBuilder();
                textBuffer.Append(textInline.Text);
                continue;
            }

            if (textBuffer is { Length: > 0 })
            {
                merged.Add(new MarkdownTextInline(textBuffer.ToString()));
                textBuffer.Clear();
            }

            merged.Add(inline);
        }

        if (textBuffer is { Length: > 0 })
        {
            merged.Add(new MarkdownTextInline(textBuffer.ToString()));
        }

        return merged;
    }

    internal static int CountIndent(string line)
    {
        var count = 0;
        while (count < line.Length && line[count] == ' ')
        {
            count++;
        }

        return count;
    }

    #endregion

    /// <summary>
    /// 把源文本规范成「只有 \n 换行、制表符已展开」的形式。除了 <c>\r\n</c> 需要看相邻两个字符外，
    /// 这个变换是逐字符的——增量解析据此只规范化新追加的那一段，前提是旧文本不以 <c>\r</c> 结尾。
    /// </summary>
    internal static string Normalize(string markdown) =>
        markdown
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\t", "    ", StringComparison.Ordinal);

    #region Structural comparison

    /// <summary>
    /// 按值比较两个块。块是 <see langword="record"/>，但成员里的 <see cref="IReadOnlyList{T}"/> 走的是
    /// 引用相等，两次解析产出的块永远不会相等，所以可视树 diff 必须用这个递归比较。
    /// </summary>
    internal static bool StructuralEquals(MarkdownBlock? left, MarkdownBlock? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return (left, right) switch
        {
            (MarkdownParagraphBlock a, MarkdownParagraphBlock b) => InlinesEqual(a.Inlines, b.Inlines),
            (MarkdownHeadingBlock a, MarkdownHeadingBlock b) => a.Level == b.Level && InlinesEqual(a.Inlines, b.Inlines),
            (MarkdownCodeBlock a, MarkdownCodeBlock b) =>
                string.Equals(a.Text, b.Text, StringComparison.Ordinal) &&
                string.Equals(a.Language, b.Language, StringComparison.Ordinal),
            (MarkdownRuleBlock, MarkdownRuleBlock) => true,
            (MarkdownQuoteBlock a, MarkdownQuoteBlock b) => BlocksEqual(a.Blocks, b.Blocks),
            (MarkdownListBlock a, MarkdownListBlock b) =>
                a.Ordered == b.Ordered && a.StartIndex == b.StartIndex && a.IsLoose == b.IsLoose && ItemsEqual(a.Items, b.Items),
            (MarkdownTableBlock a, MarkdownTableBlock b) =>
                AlignmentsEqual(a.Alignments, b.Alignments) && RowsEqual(a.HeaderRows, b.HeaderRows) && RowsEqual(a.Rows, b.Rows),
            (MarkdownFootnoteDefinitionBlock a, MarkdownFootnoteDefinitionBlock b) =>
                a.Number == b.Number &&
                string.Equals(a.Label, b.Label, StringComparison.Ordinal) &&
                BlocksEqual(a.Blocks, b.Blocks),
            _ => false,
        };
    }

    internal static bool BlocksEqual(IReadOnlyList<MarkdownBlock> left, IReadOnlyList<MarkdownBlock> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!StructuralEquals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ItemsEqual(IReadOnlyList<MarkdownListItemBlock> left, IReadOnlyList<MarkdownListItemBlock> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index].TaskState != right[index].TaskState || !BlocksEqual(left[index].Blocks, right[index].Blocks))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AlignmentsEqual(IReadOnlyList<MarkdownColumnAlignment>? left, IReadOnlyList<MarkdownColumnAlignment>? right)
    {
        if (left == null || right == null)
        {
            return left == null && right == null;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool RowsEqual(IReadOnlyList<MarkdownTableRow> left, IReadOnlyList<MarkdownTableRow> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            var leftCells = left[index].Cells;
            var rightCells = right[index].Cells;
            if (leftCells.Count != rightCells.Count)
            {
                return false;
            }

            for (var cell = 0; cell < leftCells.Count; cell++)
            {
                if (!InlinesEqual(leftCells[cell], rightCells[cell]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    internal static bool InlinesEqual(IReadOnlyList<MarkdownInline> left, IReadOnlyList<MarkdownInline> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!InlineEquals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool InlineEquals(MarkdownInline left, MarkdownInline right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return (left, right) switch
        {
            (MarkdownTextInline a, MarkdownTextInline b) => string.Equals(a.Text, b.Text, StringComparison.Ordinal),
            (MarkdownCodeInline a, MarkdownCodeInline b) => string.Equals(a.Text, b.Text, StringComparison.Ordinal),
            (MarkdownStrongInline a, MarkdownStrongInline b) => InlinesEqual(a.Children, b.Children),
            (MarkdownEmphasisInline a, MarkdownEmphasisInline b) => InlinesEqual(a.Children, b.Children),
            (MarkdownStrikethroughInline a, MarkdownStrikethroughInline b) => InlinesEqual(a.Children, b.Children),
            (MarkdownLineBreakInline, MarkdownLineBreakInline) => true,
            (MarkdownFootnoteReferenceInline a, MarkdownFootnoteReferenceInline b) =>
                a.Number == b.Number && string.Equals(a.Label, b.Label, StringComparison.Ordinal),
            (MarkdownImageInline a, MarkdownImageInline b) =>
                a.Uri == b.Uri &&
                string.Equals(a.Target, b.Target, StringComparison.Ordinal) &&
                string.Equals(a.Alt, b.Alt, StringComparison.Ordinal) &&
                string.Equals(a.Title, b.Title, StringComparison.Ordinal),
            (MarkdownLinkInline a, MarkdownLinkInline b) =>
                a.Uri == b.Uri &&
                string.Equals(a.Target, b.Target, StringComparison.Ordinal) &&
                string.Equals(a.Title, b.Title, StringComparison.Ordinal) &&
                InlinesEqual(a.Children, b.Children),
            _ => false,
        };
    }

    #endregion

    private readonly record struct ListMarkerMatch(
        int Indent, bool Ordered, int StartIndex, string Content, bool? TaskState, int ContentIndent);
}
