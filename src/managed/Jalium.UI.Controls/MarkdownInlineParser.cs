using System.Text;
using System.Text.RegularExpressions;

namespace Jalium.UI.Controls;

/// <summary>
/// CommonMark / GFM 的行内解析：代码跨度、自动链接、原始 HTML、链接与图片（行内式与引用式）、
/// 强调与删除线、脚注引用、反斜杠转义与 HTML 实体。
/// </summary>
/// <remarks>
/// <para>
/// 强调走 CommonMark 规范的 <i>delimiter stack</i>，而不是「找下一个同样的记号」这种朴素配对——
/// 后者会把 <c>get_user_name</c> 拆成斜体，把 <c>***x***</c> 配错。关键在两条规则：
/// </para>
/// <list type="bullet">
///   <item><b>flanking</b>：一段记号能否作为开/闭，取决于两侧是空白还是标点；
///   <c>_</c> 额外禁止词内强调，<c>*</c> 不禁止。</item>
///   <item><b>rule of three</b>：当一段记号既能开又能闭时，只有两侧长度之和不是 3 的倍数
///   （或两者各自都是 3 的倍数）才允许配对。</item>
/// </list>
/// <para>
/// 解析分两趟：<see cref="Tokenize"/> 把文本切成节点序列（链接与图片在遇到 <c>]</c> 时就地闭合，
/// 因此括号内的强调是独立处理的），<c>ProcessEmphasis</c> 再在节点序列上配对强调记号。
/// </para>
/// </remarks>
internal static class MarkdownInlineParser
{
    private const string EscapableCharacters = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";

    // 全部锚在 \G 上，配合 Regex.Match(text, index) 直接从当前位置匹配。用 ^ 就得先切出
    // text[index..] 的子串，而这条路径每遇到一个 <、h、w 都要走一次——在尖括号密集的文本里是 O(n²) 分配。
    private static readonly Regex s_autolinkUriRegex =
        new(@"\G<[A-Za-z][A-Za-z0-9+.\-]{1,31}:[^<>\x00-\x20]*>", RegexOptions.Compiled);

    private static readonly Regex s_autolinkEmailRegex =
        new(@"\G<[A-Za-z0-9.!#$%&'*+/=?^_`{|}~\-]+@[A-Za-z0-9](?:[A-Za-z0-9\-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9\-]{0,61}[A-Za-z0-9])?)*>",
            RegexOptions.Compiled);

    private static readonly Regex s_htmlTagRegex =
        new(@"\G<(?:/[A-Za-z][A-Za-z0-9\-]*\s*>|[A-Za-z][A-Za-z0-9\-]*(?:\s+[A-Za-z_:][A-Za-z0-9_.:\-]*(?:\s*=\s*(?:[^\s""'=<>`]+|'[^']*'|""[^""]*""))?)*\s*/?>|!--[\s\S]*?-->|\?[\s\S]*?\?>|![A-Za-z][\s\S]*?>|!\[CDATA\[[\s\S]*?\]\]>)",
            RegexOptions.Compiled);

    private static readonly Regex s_bareUrlRegex =
        new(@"\G(?:https?://|www\.)[A-Za-z0-9\-_]+(?:\.[A-Za-z0-9\-_]+)+(?::\d+)?(?:[/?#][^\s<]*)?",
            RegexOptions.Compiled);

    /// <summary>裸邮箱的本地部分已经在文本缓冲里了，这条只负责从 <c>@</c> 起匹配域名。</summary>
    private static readonly Regex s_bareEmailDomainRegex =
        new(@"\G@[A-Za-z0-9\-_]+(?:\.[A-Za-z0-9\-_]+)+", RegexOptions.Compiled);

    /// <summary>扫一个 HTML 开标签里的属性；名字在组 1，三种引号形式的值在组 2/3/4。</summary>
    private static readonly Regex s_htmlAttributeRegex =
        new(@"([A-Za-z_:][A-Za-z0-9_.:\-]*)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'=<>`]+))",
            RegexOptions.Compiled);

    /// <summary>行内 HTML 里会被翻译成对应格式的标签；其余标签直接剥掉，只保留标签之间的文字。</summary>
    private static readonly Dictionary<string, ContainerKind> s_htmlFormatTags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["b"] = ContainerKind.Strong,
        ["strong"] = ContainerKind.Strong,
        ["i"] = ContainerKind.Emphasis,
        ["em"] = ContainerKind.Emphasis,
        ["cite"] = ContainerKind.Emphasis,
        ["del"] = ContainerKind.Strikethrough,
        ["s"] = ContainerKind.Strikethrough,
        ["strike"] = ContainerKind.Strikethrough,
    };

    public static IReadOnlyList<MarkdownInline> Parse(string text, MarkdownParseContext context)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<MarkdownInline>();
        }

        var nodes = Tokenize(text, context);

        if (context.IsStreaming)
        {
            TrimTrailingDelimiters(nodes);
        }

        ProcessEmphasis(nodes, 0, context.IsStreaming);
        return Materialize(nodes);
    }

    #region Node model

    private enum NodeKind { Text, Delimiter, Leaf, Container, Bracket }

    private enum ContainerKind { Strong, Emphasis, Strikethrough, Link }

    private sealed class Node
    {
        public NodeKind Kind;
        public string Text = string.Empty;
        public MarkdownInline? Inline;

        public char DelimiterChar;
        public int DelimiterCount;
        public int DelimiterOriginalCount;
        public bool CanOpen;
        public bool CanClose;

        public ContainerKind Container;
        public List<Node>? Children;
        public Uri? LinkUri;
        public string LinkTarget = string.Empty;
        public string? LinkTitle;

        /// <summary><see cref="NodeKind.Bracket"/> 专用：是否是图片的 <c>![</c>，以及是否还能闭合。</summary>
        public bool IsImage;
        public bool IsActive = true;

        public static Node FromText(string text) => new() { Kind = NodeKind.Text, Text = text };
        public static Node FromInline(MarkdownInline inline) => new() { Kind = NodeKind.Leaf, Inline = inline };
    }

    #endregion

    #region Tokenizer

    private static List<Node> Tokenize(string text, MarkdownParseContext context)
    {
        var nodes = new List<Node>();
        var buffer = new StringBuilder();
        var brackets = new List<int>();
        var htmlStack = new List<(int NodeIndex, string Tag, ContainerKind Kind)>();
        var index = 0;

        while (index < text.Length)
        {
            var c = text[index];

            if (c == '\\' && index + 1 < text.Length)
            {
                var next = text[index + 1];
                if (EscapableCharacters.Contains(next, StringComparison.Ordinal))
                {
                    buffer.Append(next);
                    index += 2;
                    continue;
                }
            }

            if (c == '`' && TryReadCodeSpan(text, index, out var code, out var codeLength))
            {
                Flush(nodes, buffer);
                nodes.Add(Node.FromInline(new MarkdownCodeInline(code)));
                index += codeLength;
                continue;
            }

            // 反引号还没等到配对：流式时把它当成正在输入的代码跨度，一路吃到末尾。
            if (c == '`' && context.IsStreaming)
            {
                var fence = 0;
                while (index + fence < text.Length && text[index + fence] == '`')
                {
                    fence++;
                }

                // 后面还什么都没打出来，就先当它不存在——先冒出一个空代码框再被填上，比不显示更晃眼。
                if (index + fence < text.Length)
                {
                    Flush(nodes, buffer);
                    nodes.Add(Node.FromInline(new MarkdownCodeInline(text[(index + fence)..].Replace('\n', ' '))));
                }

                index = text.Length;
                continue;
            }

            if (c == '<')
            {
                if (TryReadAutolink(text, index, out var autolink, out var autolinkLength))
                {
                    Flush(nodes, buffer);
                    nodes.Add(Node.FromInline(autolink));
                    index += autolinkLength;
                    continue;
                }

                var tagMatch = s_htmlTagRegex.Match(text, index);
                if (tagMatch.Success)
                {
                    Flush(nodes, buffer);
                    HandleHtmlTag(nodes, brackets, htmlStack, tagMatch.Value, context);
                    index += tagMatch.Length;
                    continue;
                }
            }

            if (c == '&' && MarkdownEntities.TryDecode(text, index, out var entity, out var entityLength))
            {
                buffer.Append(entity);
                index += entityLength;
                continue;
            }

            if (c == '!' && index + 1 < text.Length && text[index + 1] == '[')
            {
                Flush(nodes, buffer);
                brackets.Add(nodes.Count);
                nodes.Add(new Node { Kind = NodeKind.Bracket, Text = "![", IsImage = true });
                index += 2;
                continue;
            }

            if (c == '[')
            {
                if (TryReadFootnoteReference(text, index, context, out var footnote, out var footnoteLength))
                {
                    Flush(nodes, buffer);
                    nodes.Add(Node.FromInline(footnote));
                    index += footnoteLength;
                    continue;
                }

                Flush(nodes, buffer);
                brackets.Add(nodes.Count);
                nodes.Add(new Node { Kind = NodeKind.Bracket, Text = "[" });
                index++;
                continue;
            }

            if (c == ']')
            {
                Flush(nodes, buffer);
                if (TryCloseBracket(nodes, brackets, htmlStack, text, ref index, context))
                {
                    continue;
                }

                nodes.Add(Node.FromText("]"));
                index++;
                continue;
            }

            if (c is '*' or '_' or '~')
            {
                var runLength = 1;
                while (index + runLength < text.Length && text[index + runLength] == c)
                {
                    runLength++;
                }

                // GFM 只承认 ~ 与 ~~；更长的一串按字面处理，免得 ~~~ 围栏的残迹变成删除线。
                if (c == '~' && runLength > 2)
                {
                    buffer.Append(c, runLength);
                    index += runLength;
                    continue;
                }

                Flush(nodes, buffer);
                ClassifyDelimiterRun(text, index, runLength, c, out var canOpen, out var canClose);
                nodes.Add(new Node
                {
                    Kind = NodeKind.Delimiter,
                    DelimiterChar = c,
                    DelimiterCount = runLength,
                    DelimiterOriginalCount = runLength,
                    CanOpen = canOpen,
                    CanClose = canClose,
                });
                index += runLength;
                continue;
            }

            // 正在输入的链接目标（`[文字](https://…`）里那截地址不认成裸链接：
            // 认了的话，同一段文字会先变蓝，等 `)` 打出来再变回普通文本、然后整体重新变成链接。
            if (context.EnableBareAutolinks &&
                !(context.IsStreaming && IsInsideUnclosedLinkDestination(text, index)) &&
                TryReadBareAutolink(text, index, buffer, out var bare, out var bareLength, out var backtrack))
            {
                buffer.Length -= backtrack;
                Flush(nodes, buffer);
                nodes.Add(Node.FromInline(bare));
                index += bareLength;
                continue;
            }

            buffer.Append(c);
            index++;
        }

        Flush(nodes, buffer);

        // 没等到闭标签的格式化 HTML 标签按剥除处理：栈里留下的开标签节点直接丢掉，内容原样保留。
        // 下标可能已经被链接闭合时的成段删除带走，所以这里只认还在原位的那个占位节点。
        for (var i = htmlStack.Count - 1; i >= 0; i--)
        {
            var slot = htmlStack[i].NodeIndex;
            if (slot < nodes.Count && nodes[slot].Kind == NodeKind.Bracket && nodes[slot].Text.Length == 0)
            {
                nodes.RemoveAt(slot);
            }
        }

        foreach (var node in nodes)
        {
            if (node.Kind == NodeKind.Bracket)
            {
                node.Kind = NodeKind.Text;
            }
        }

        return nodes;
    }

    private static void Flush(List<Node> nodes, StringBuilder buffer)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        nodes.Add(Node.FromText(buffer.ToString()));
        buffer.Clear();
    }

    #endregion

    #region Code spans, autolinks, raw HTML

    /// <summary>
    /// 代码跨度以 N 个反引号开闭，N 必须严格相等——所以 <c>``a `b` c``</c> 里的单反引号是内容。
    /// 首尾同时有空格且内容不全是空格时各去掉一个（CommonMark 的 <c>`` ` ``</c> 规则）。
    /// </summary>
    private static bool TryReadCodeSpan(string text, int start, out string code, out int length)
    {
        code = string.Empty;
        length = 0;

        var fence = 0;
        while (start + fence < text.Length && text[start + fence] == '`')
        {
            fence++;
        }

        var cursor = start + fence;
        while (cursor < text.Length)
        {
            if (text[cursor] != '`')
            {
                cursor++;
                continue;
            }

            var run = 0;
            while (cursor + run < text.Length && text[cursor + run] == '`')
            {
                run++;
            }

            if (run == fence)
            {
                var content = text.Substring(start + fence, cursor - start - fence).Replace('\n', ' ');
                if (content.Length > 2 &&
                    content[0] == ' ' &&
                    content[^1] == ' ' &&
                    content.AsSpan().ContainsAnyExcept(' '))
                {
                    content = content[1..^1];
                }

                code = content;
                length = cursor + run - start;
                return true;
            }

            cursor += run;
        }

        return false;
    }

    private static bool TryReadAutolink(string text, int start, out MarkdownInline inline, out int length)
    {
        inline = null!;
        length = 0;

        var uriMatch = s_autolinkUriRegex.Match(text, start);
        if (uriMatch.Success)
        {
            var target = uriMatch.Value[1..^1];
            var decoded = MarkdownEntities.DecodeAll(target);
            inline = new MarkdownLinkInline(
                new MarkdownInline[] { new MarkdownTextInline(decoded) },
                Uri.TryCreate(decoded, UriKind.Absolute, out var uri) ? uri : null,
                decoded);
            length = uriMatch.Length;
            return true;
        }

        var emailMatch = s_autolinkEmailRegex.Match(text, start);
        if (emailMatch.Success)
        {
            var address = emailMatch.Value[1..^1];
            inline = new MarkdownLinkInline(
                new MarkdownInline[] { new MarkdownTextInline(address) },
                Uri.TryCreate("mailto:" + address, UriKind.Absolute, out var uri) ? uri : null,
                "mailto:" + address);
            length = emailMatch.Length;
            return true;
        }

        return false;
    }

    /// <summary>
    /// GFM 扩展自动链接：正文里裸写的 <c>https://…</c>、<c>www.…</c> 与邮箱地址。
    /// 邮箱的本地部分此刻已经进了文本缓冲，靠 <paramref name="backtrack"/> 告诉调用方要退掉几个字符。
    /// </summary>
    private static bool TryReadBareAutolink(
        string text, int start, StringBuilder buffer, out MarkdownInline inline, out int length, out int backtrack)
    {
        inline = null!;
        length = 0;
        backtrack = 0;

        var c = text[start];

        if (c is 'h' or 'H' or 'w' or 'W')
        {
            if (!IsAutolinkStartBoundary(buffer))
            {
                return false;
            }

            var match = s_bareUrlRegex.Match(text, start);
            if (!match.Success)
            {
                return false;
            }

            var target = TrimAutolinkTail(match.Value);
            if (target.Length == 0)
            {
                return false;
            }

            var absolute = target.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? "https://" + target
                : target;

            inline = new MarkdownLinkInline(
                new MarkdownInline[] { new MarkdownTextInline(target) },
                Uri.TryCreate(absolute, UriKind.Absolute, out var uri) ? uri : null,
                absolute);
            length = target.Length;
            return true;
        }

        if (c != '@')
        {
            return false;
        }

        // 回溯出邮箱的本地部分：只吃合法字符，并且它前面必须是行首或非单词字符。
        var localLength = 0;
        while (localLength < buffer.Length)
        {
            var ch = buffer[buffer.Length - 1 - localLength];
            if (char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-' or '_' or '+')
            {
                localLength++;
                continue;
            }

            break;
        }

        if (localLength == 0)
        {
            return false;
        }

        var domainMatch = s_bareEmailDomainRegex.Match(text, start);
        if (!domainMatch.Success)
        {
            return false;
        }

        var local = buffer.ToString(buffer.Length - localLength, localLength);
        if (local[0] is '.' or '-' or '_' or '+')
        {
            return false;
        }

        var address = (local + domainMatch.Value).TrimEnd('.', '-', '_');
        if (address.Length <= localLength + 1)
        {
            return false;
        }

        inline = new MarkdownLinkInline(
            new MarkdownInline[] { new MarkdownTextInline(address) },
            Uri.TryCreate("mailto:" + address, UriKind.Absolute, out var mailUri) ? mailUri : null,
            "mailto:" + address);
        length = address.Length - localLength;
        backtrack = localLength;
        return true;
    }

    /// <summary>
    /// <paramref name="index"/> 是否落在一个「已经写了 <c>](</c>、但还没写 <c>)</c>」的链接目标里。
    /// </summary>
    private static bool IsInsideUnclosedLinkDestination(string text, int index)
    {
        var open = text.LastIndexOf("](", index, StringComparison.Ordinal);
        return open >= 0 && text.IndexOf(')', open) < 0;
    }

    private static bool IsAutolinkStartBoundary(StringBuilder buffer)
    {
        if (buffer.Length == 0)
        {
            return true;
        }

        var previous = buffer[^1];
        return char.IsWhiteSpace(previous) || previous is '*' or '_' or '~' or '(' or '[' or '{' or '<';
    }

    /// <summary>裸链接不吞句末标点，也不吞没有配对左括号的右括号。</summary>
    private static string TrimAutolinkTail(string value)
    {
        var end = value.Length;
        while (end > 0)
        {
            var c = value[end - 1];
            if (c is '?' or '!' or '.' or ',' or ':' or ';' or '*' or '_' or '~' or '\'' or '"')
            {
                end--;
                continue;
            }

            if (c == ')')
            {
                var opens = 0;
                var closes = 0;
                for (var i = 0; i < end; i++)
                {
                    if (value[i] == '(')
                    {
                        opens++;
                    }
                    else if (value[i] == ')')
                    {
                        closes++;
                    }
                }

                if (closes > opens)
                {
                    end--;
                    continue;
                }
            }

            break;
        }

        return value[..end];
    }

    private static void HandleHtmlTag(
        List<Node> nodes,
        List<int> brackets,
        List<(int NodeIndex, string Tag, ContainerKind Kind)> htmlStack,
        string tag,
        MarkdownParseContext context)
    {
        if (tag.StartsWith("<!", StringComparison.Ordinal) || tag.StartsWith("<?", StringComparison.Ordinal))
        {
            return;
        }

        var isClosing = tag.StartsWith("</", StringComparison.Ordinal);
        var nameStart = isClosing ? 2 : 1;
        var nameEnd = nameStart;
        while (nameEnd < tag.Length && (char.IsAsciiLetterOrDigit(tag[nameEnd]) || tag[nameEnd] == '-'))
        {
            nameEnd++;
        }

        var name = tag[nameStart..nameEnd];

        if (!isClosing && string.Equals(name, "br", StringComparison.OrdinalIgnoreCase))
        {
            nodes.Add(Node.FromInline(new MarkdownLineBreakInline()));
            return;
        }

        if (!isClosing && string.Equals(name, "img", StringComparison.OrdinalIgnoreCase))
        {
            var source = ReadHtmlAttribute(tag, "src");
            if (!string.IsNullOrEmpty(source))
            {
                nodes.Add(Node.FromInline(new MarkdownImageInline(
                    ReadHtmlAttribute(tag, "alt") ?? string.Empty,
                    context.ResolveUri(source),
                    source,
                    ReadHtmlAttribute(tag, "title"))));
            }

            return;
        }

        if (!isClosing && string.Equals(name, "a", StringComparison.OrdinalIgnoreCase))
        {
            var href = ReadHtmlAttribute(tag, "href") ?? string.Empty;
            htmlStack.Add((nodes.Count, name, ContainerKind.Link));
            nodes.Add(new Node
            {
                Kind = NodeKind.Bracket,
                Text = string.Empty,
                LinkTarget = href,
                LinkUri = context.ResolveUri(href),
                LinkTitle = ReadHtmlAttribute(tag, "title"),
            });
            return;
        }

        if (!isClosing)
        {
            if (!s_htmlFormatTags.TryGetValue(name, out var kind) || tag.EndsWith("/>", StringComparison.Ordinal))
            {
                return;
            }

            htmlStack.Add((nodes.Count, name, kind));
            nodes.Add(new Node { Kind = NodeKind.Bracket, Text = string.Empty });
            return;
        }

        for (var i = htmlStack.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(htmlStack[i].Tag, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var (openIndex, _, kind) = htmlStack[i];
            htmlStack.RemoveRange(i, htmlStack.Count - i);

            // 开标签的占位节点可能已经被链接闭合时的成段删除带走：那就当这个闭标签没有匹配。
            if (openIndex >= nodes.Count || nodes[openIndex].Kind != NodeKind.Bracket)
            {
                return;
            }

            // 反向同理：括号栈里落在这一段内的下标也一并作废。
            for (var slot = brackets.Count - 1; slot >= 0 && brackets[slot] >= openIndex; slot--)
            {
                brackets.RemoveAt(slot);
            }

            var children = nodes.GetRange(openIndex + 1, nodes.Count - openIndex - 1);
            ProcessEmphasis(children, 0);
            var opener = nodes[openIndex];
            nodes.RemoveRange(openIndex, nodes.Count - openIndex);
            nodes.Add(new Node
            {
                Kind = NodeKind.Container,
                Container = kind,
                Children = children,
                LinkUri = opener.LinkUri,
                LinkTarget = opener.LinkTarget,
                LinkTitle = opener.LinkTitle,
            });
            return;
        }
    }

    private static string? ReadHtmlAttribute(string tag, string name)
    {
        foreach (Match match in s_htmlAttributeRegex.Matches(tag))
        {
            if (!string.Equals(match.Groups[1].Value, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = match.Groups[2].Success ? match.Groups[2].Value
                : match.Groups[3].Success ? match.Groups[3].Value
                : match.Groups[4].Value;
            return MarkdownEntities.DecodeAll(value);
        }

        return null;
    }

    #endregion

    #region Links, images, footnotes

    private static bool TryCloseBracket(
        List<Node> nodes,
        List<int> brackets,
        List<(int NodeIndex, string Tag, ContainerKind Kind)> htmlStack,
        string text,
        ref int index,
        MarkdownParseContext context)
    {
        var bracketSlot = -1;
        for (var i = brackets.Count - 1; i >= 0; i--)
        {
            if (brackets[i] < nodes.Count && nodes[brackets[i]].Kind == NodeKind.Bracket)
            {
                bracketSlot = i;
                break;
            }
        }

        if (bracketSlot < 0)
        {
            return false;
        }

        var openerIndex = brackets[bracketSlot];
        var opener = nodes[openerIndex];

        if (!opener.IsActive)
        {
            brackets.RemoveRange(bracketSlot, brackets.Count - bracketSlot);
            opener.Kind = NodeKind.Text;
            return false;
        }

        var labelText = ExtractLabelText(nodes, openerIndex + 1);
        if (!TryReadLinkTarget(text, index + 1, labelText, context, out var target, out var uri, out var title, out var consumed))
        {
            brackets.RemoveRange(bracketSlot, brackets.Count - bracketSlot);
            opener.Kind = NodeKind.Text;
            return false;
        }

        var children = nodes.GetRange(openerIndex + 1, nodes.Count - openerIndex - 1);
        nodes.RemoveRange(openerIndex, nodes.Count - openerIndex);
        brackets.RemoveRange(bracketSlot, brackets.Count - bracketSlot);

        // 括号里那些没等到闭标签的 HTML 开标签刚刚跟着一起被删了，它们的下标必须一并作废。
        for (var i = htmlStack.Count - 1; i >= 0 && htmlStack[i].NodeIndex >= openerIndex; i--)
        {
            htmlStack.RemoveAt(i);
        }

        if (opener.IsImage)
        {
            ProcessEmphasis(children, 0);
            nodes.Add(Node.FromInline(new MarkdownImageInline(
                InlinesToPlainText(Materialize(children)),
                uri,
                target,
                title)));
        }
        else
        {
            // 链接不能嵌套链接：更外层的待闭合方括号一律失效，只能当字面量。
            foreach (var slot in brackets)
            {
                if (slot < nodes.Count && nodes[slot].Kind == NodeKind.Bracket && !nodes[slot].IsImage)
                {
                    nodes[slot].IsActive = false;
                }
            }

            ProcessEmphasis(children, 0);
            nodes.Add(new Node
            {
                Kind = NodeKind.Container,
                Container = ContainerKind.Link,
                Children = children,
                LinkUri = uri,
                LinkTarget = target,
                LinkTitle = title,
            });
        }

        index += 1 + consumed;
        return true;
    }

    /// <summary>
    /// 读 <c>]</c> 之后的目标：行内式 <c>(dest "title")</c>，或引用式 <c>[label]</c>、<c>[]</c>、
    /// 以及省略第二个方括号的简写形式。
    /// </summary>
    private static bool TryReadLinkTarget(
        string text, int afterBracket, string labelText, MarkdownParseContext context,
        out string target, out Uri? uri, out string? title, out int consumed)
    {
        target = string.Empty;
        uri = null;
        title = null;
        consumed = 0;

        if (afterBracket < text.Length && text[afterBracket] == '(' &&
            TryReadInlineDestination(text, afterBracket, out target, out title, out var inlineLength))
        {
            uri = context.ResolveUri(target);
            consumed = inlineLength;
            return true;
        }

        if (afterBracket < text.Length && text[afterBracket] == '[')
        {
            var close = FindMatching(text, afterBracket, '[', ']');
            if (close > 0)
            {
                var label = text[(afterBracket + 1)..close];
                var lookup = label.Length == 0 ? labelText : label;
                if (context.TryGetLinkDefinition(lookup, out var definition))
                {
                    target = definition.Target;
                    uri = definition.Uri;
                    title = definition.Title;
                    consumed = close - afterBracket + 1;
                    return true;
                }

                return false;
            }
        }

        // 简写引用 [label]：只有当 label 确实有定义时才成立，否则保持字面量。
        if (context.TryGetLinkDefinition(labelText, out var shorthand))
        {
            target = shorthand.Target;
            uri = shorthand.Uri;
            title = shorthand.Title;
            consumed = 0;
            return true;
        }

        return false;
    }

    private static bool TryReadInlineDestination(string text, int openParen, out string target, out string? title, out int length)
    {
        target = string.Empty;
        title = null;
        length = 0;

        var cursor = openParen + 1;
        while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
        {
            cursor++;
        }

        if (cursor < text.Length && text[cursor] == '<')
        {
            var close = text.IndexOf('>', cursor + 1);
            if (close < 0)
            {
                return false;
            }

            target = Unescape(text[(cursor + 1)..close]);
            cursor = close + 1;
        }
        else
        {
            var start = cursor;
            var depth = 0;
            while (cursor < text.Length)
            {
                var c = text[cursor];
                if (c == '\\' && cursor + 1 < text.Length)
                {
                    cursor += 2;
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    break;
                }

                if (c == '(')
                {
                    depth++;
                }
                else if (c == ')')
                {
                    if (depth == 0)
                    {
                        break;
                    }

                    depth--;
                }

                cursor++;
            }

            target = Unescape(text[start..cursor]);
        }

        while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
        {
            cursor++;
        }

        if (cursor < text.Length && text[cursor] is '"' or '\'' or '(')
        {
            var open = text[cursor];
            var close = open == '(' ? ')' : open;
            var end = cursor + 1;
            while (end < text.Length)
            {
                if (text[end] == '\\' && end + 1 < text.Length)
                {
                    end += 2;
                    continue;
                }

                if (text[end] == close)
                {
                    break;
                }

                end++;
            }

            if (end >= text.Length)
            {
                return false;
            }

            title = MarkdownEntities.DecodeAll(Unescape(text[(cursor + 1)..end]));
            cursor = end + 1;

            while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
            {
                cursor++;
            }
        }

        if (cursor >= text.Length || text[cursor] != ')')
        {
            return false;
        }

        target = MarkdownEntities.DecodeAll(target);
        length = cursor - openParen + 1;
        return true;
    }

    private static bool TryReadFootnoteReference(
        string text, int start, MarkdownParseContext context, out MarkdownInline inline, out int length)
    {
        inline = null!;
        length = 0;

        if (start + 2 >= text.Length || text[start + 1] != '^')
        {
            return false;
        }

        var close = text.IndexOf(']', start + 2);
        if (close < 0 || close == start + 2)
        {
            return false;
        }

        var label = text[(start + 2)..close];
        if (label.Contains(' ', StringComparison.Ordinal) || !context.TryGetFootnoteNumber(label, out var number))
        {
            return false;
        }

        inline = new MarkdownFootnoteReferenceInline(label, number);
        length = close - start + 1;
        return true;
    }

    private static string ExtractLabelText(List<Node> nodes, int start)
    {
        var builder = new StringBuilder();
        for (var index = start; index < nodes.Count; index++)
        {
            AppendNodeText(builder, nodes[index]);
        }

        return builder.ToString();
    }

    private static void AppendNodeText(StringBuilder builder, Node node)
    {
        switch (node.Kind)
        {
            case NodeKind.Text:
            case NodeKind.Bracket:
                builder.Append(node.Text);
                break;

            case NodeKind.Delimiter:
                builder.Append(node.DelimiterChar, node.DelimiterCount);
                break;

            case NodeKind.Leaf when node.Inline is MarkdownTextInline text:
                builder.Append(text.Text);
                break;

            case NodeKind.Leaf when node.Inline is MarkdownCodeInline code:
                builder.Append(code.Text);
                break;

            case NodeKind.Container when node.Children != null:
                foreach (var child in node.Children)
                {
                    AppendNodeText(builder, child);
                }

                break;
        }
    }

    private static string Unescape(string value)
    {
        if (value.IndexOf('\\', StringComparison.Ordinal) < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\' &&
                index + 1 < value.Length &&
                EscapableCharacters.Contains(value[index + 1], StringComparison.Ordinal))
            {
                builder.Append(value[index + 1]);
                index++;
                continue;
            }

            builder.Append(value[index]);
        }

        return builder.ToString();
    }

    private static int FindMatching(string text, int openIndex, char openChar, char closeChar)
    {
        var depth = 0;
        for (var index = openIndex; index < text.Length; index++)
        {
            if (text[index] == '\\')
            {
                index++;
                continue;
            }

            if (text[index] == openChar)
            {
                depth++;
            }
            else if (text[index] == closeChar && --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    #endregion

    #region Emphasis

    /// <summary>
    /// CommonMark 的 flanking 判定：一段记号能否作为开或闭，只看紧邻两侧是空白、标点还是普通字符。
    /// <c>_</c> 比 <c>*</c> 多一条限制，正是它不会把 <c>snake_case_name</c> 拆成斜体的原因。
    /// </summary>
    private static void ClassifyDelimiterRun(string text, int start, int length, char delimiter, out bool canOpen, out bool canClose)
    {
        var before = start > 0 ? text[start - 1] : ' ';
        var afterIndex = start + length;
        var after = afterIndex < text.Length ? text[afterIndex] : ' ';

        var beforeIsWhitespace = char.IsWhiteSpace(before);
        var afterIsWhitespace = char.IsWhiteSpace(after);
        var beforeIsPunctuation = IsPunctuation(before);
        var afterIsPunctuation = IsPunctuation(after);

        var leftFlanking = !afterIsWhitespace && (!afterIsPunctuation || beforeIsWhitespace || beforeIsPunctuation);
        var rightFlanking = !beforeIsWhitespace && (!beforeIsPunctuation || afterIsWhitespace || afterIsPunctuation);

        if (delimiter == '_')
        {
            canOpen = leftFlanking && (!rightFlanking || beforeIsPunctuation);
            canClose = rightFlanking && (!leftFlanking || afterIsPunctuation);
            return;
        }

        canOpen = leftFlanking;
        canClose = rightFlanking;
    }

    private static bool IsPunctuation(char c) =>
        char.IsPunctuation(c) || char.IsSymbol(c);

    /// <summary>
    /// 丢掉结尾那段还没配上对的记号。
    /// </summary>
    /// <remarks>
    /// ★必须在配对之前做。末尾的记号要么是刚起头还没内容（<c>**</c>），要么是正在打出来的闭合记号
    /// （<c>**bold*</c> 里的那个 <c>*</c>）。留着它，标准配对会先把 <c>**bold*</c> 配成
    /// 「字面 <c>*</c> + 斜体 bold」，乐观闭合再往外包一层，同一个词就在斜体和粗体之间跳一次——
    /// 比什么都不做还差。
    /// </remarks>
    private static void TrimTrailingDelimiters(List<Node> nodes)
    {
        while (nodes.Count > 0 && nodes[^1].Kind == NodeKind.Delimiter)
        {
            nodes.RemoveAt(nodes.Count - 1);
        }
    }

    private static void ProcessEmphasis(List<Node> nodes, int stackBottom) =>
        ProcessEmphasis(nodes, stackBottom, optimisticallyClose: false);

    private static void ProcessEmphasis(List<Node> nodes, int stackBottom, bool optimisticallyClose)
    {
        // 存节点引用而不是下标：每次配对都会删掉一段节点、插进一个容器，下标当场就漂了。
        var openersBottom = new Dictionary<(char Delimiter, int Remainder, bool CanOpen), Node>();
        var closerIndex = stackBottom;

        while (true)
        {
            while (closerIndex < nodes.Count &&
                   !(nodes[closerIndex].Kind == NodeKind.Delimiter && nodes[closerIndex].CanClose))
            {
                closerIndex++;
            }

            if (closerIndex >= nodes.Count)
            {
                break;
            }

            var closer = nodes[closerIndex];
            var key = (closer.DelimiterChar, closer.DelimiterOriginalCount % 3, closer.CanOpen);
            openersBottom.TryGetValue(key, out var bottomNode);

            var openerIndex = -1;
            for (var index = closerIndex - 1; index >= stackBottom; index--)
            {
                var candidate = nodes[index];
                if (ReferenceEquals(candidate, bottomNode))
                {
                    break;
                }

                if (candidate.Kind == NodeKind.Delimiter && candidate.CanOpen && CanPair(candidate, closer))
                {
                    openerIndex = index;
                    break;
                }
            }

            if (openerIndex < 0)
            {
                if (closerIndex - 1 >= stackBottom)
                {
                    openersBottom[key] = nodes[closerIndex - 1];
                }

                if (!closer.CanOpen)
                {
                    ToText(closer);
                }

                closerIndex++;
                continue;
            }

            var opener = nodes[openerIndex];
            var use = closer.DelimiterChar == '~'
                ? Math.Min(Math.Min(opener.DelimiterCount, closer.DelimiterCount), 2)
                : opener.DelimiterCount >= 2 && closer.DelimiterCount >= 2 ? 2 : 1;

            var innerStart = openerIndex + 1;
            var children = nodes.GetRange(innerStart, closerIndex - innerStart);
            foreach (var child in children)
            {
                if (child.Kind == NodeKind.Delimiter)
                {
                    ToText(child);
                }
            }

            nodes.RemoveRange(innerStart, closerIndex - innerStart);
            nodes.Insert(innerStart, new Node
            {
                Kind = NodeKind.Container,
                Container = closer.DelimiterChar == '~'
                    ? ContainerKind.Strikethrough
                    : use == 2 ? ContainerKind.Strong : ContainerKind.Emphasis,
                Children = children,
            });

            closerIndex = innerStart + 1;
            opener.DelimiterCount -= use;
            closer.DelimiterCount -= use;

            if (closer.DelimiterCount == 0)
            {
                nodes.RemoveAt(closerIndex);
            }

            if (opener.DelimiterCount == 0)
            {
                nodes.RemoveAt(openerIndex);
                closerIndex--;
            }
        }

        // 走到这里还剩下的记号都没配上对。流式时把第一个「能开」的记号当作已经闭合到末尾——
        // 它吞掉后面全部内容，所以处理完就结束；其余的照常退回字面量。
        if (optimisticallyClose)
        {

            for (var index = stackBottom; index < nodes.Count; index++)
            {
                var node = nodes[index];
                if (node.Kind != NodeKind.Delimiter || !node.CanOpen)
                {
                    continue;
                }

                var children = nodes.GetRange(index + 1, nodes.Count - index - 1);
                foreach (var child in children)
                {
                    if (child.Kind == NodeKind.Delimiter)
                    {
                        ToText(child);
                    }
                }

                nodes.RemoveRange(index, nodes.Count - index);
                nodes.Add(new Node
                {
                    Kind = NodeKind.Container,
                    Container = node.DelimiterChar == '~'
                        ? ContainerKind.Strikethrough
                        : node.DelimiterCount >= 2 ? ContainerKind.Strong : ContainerKind.Emphasis,
                    Children = children,
                });
                break;
            }
        }

        for (var index = stackBottom; index < nodes.Count; index++)
        {
            if (nodes[index].Kind == NodeKind.Delimiter)
            {
                ToText(nodes[index]);
            }
        }
    }

    /// <summary>
    /// CommonMark 的 “rule of three”：当一端既能开又能闭时，两端长度之和是 3 的倍数就不许配对
    /// （除非两端各自都是 3 的倍数）。少了这条，<c>**foo*bar**</c> 之类会配错。
    /// </summary>
    private static bool CanPair(Node opener, Node closer)
    {
        if (opener.DelimiterChar != closer.DelimiterChar)
        {
            return false;
        }

        if (closer.DelimiterChar == '~')
        {
            return opener.DelimiterCount == closer.DelimiterCount;
        }

        if (!closer.CanOpen && !opener.CanClose)
        {
            return true;
        }

        if ((opener.DelimiterOriginalCount + closer.DelimiterOriginalCount) % 3 != 0)
        {
            return true;
        }

        return opener.DelimiterOriginalCount % 3 == 0 && closer.DelimiterOriginalCount % 3 == 0;
    }

    private static void ToText(Node node)
    {
        node.Text = new string(node.DelimiterChar, node.DelimiterCount);
        node.Kind = NodeKind.Text;
    }

    #endregion

    #region Materialize

    private static IReadOnlyList<MarkdownInline> Materialize(List<Node> nodes)
    {
        var result = new List<MarkdownInline>(nodes.Count);
        StringBuilder? pending = null;

        foreach (var node in nodes)
        {
            switch (node.Kind)
            {
                case NodeKind.Text:
                case NodeKind.Bracket:
                    if (node.Text.Length > 0)
                    {
                        (pending ??= new StringBuilder()).Append(node.Text);
                    }

                    continue;

                case NodeKind.Delimiter:
                    (pending ??= new StringBuilder()).Append(node.DelimiterChar, node.DelimiterCount);
                    continue;
            }

            if (pending is { Length: > 0 })
            {
                result.Add(new MarkdownTextInline(pending.ToString()));
                pending.Clear();
            }

            if (node.Kind == NodeKind.Leaf && node.Inline != null)
            {
                if (node.Inline is MarkdownTextInline text)
                {
                    (pending ??= new StringBuilder()).Append(text.Text);
                    continue;
                }

                result.Add(node.Inline);
                continue;
            }

            var children = Materialize(node.Children ?? new List<Node>());
            result.Add(node.Container switch
            {
                ContainerKind.Strong => new MarkdownStrongInline(children),
                ContainerKind.Emphasis => new MarkdownEmphasisInline(children),
                ContainerKind.Strikethrough => new MarkdownStrikethroughInline(children),
                _ => new MarkdownLinkInline(children, node.LinkUri, node.LinkTarget, node.LinkTitle),
            });
        }

        if (pending is { Length: > 0 })
        {
            result.Add(new MarkdownTextInline(pending.ToString()));
        }

        return result;
    }

    private static string InlinesToPlainText(IReadOnlyList<MarkdownInline> inlines)
    {
        var builder = new StringBuilder();
        Append(inlines);
        return builder.ToString();

        void Append(IReadOnlyList<MarkdownInline> items)
        {
            foreach (var inline in items)
            {
                switch (inline)
                {
                    case MarkdownTextInline text: builder.Append(text.Text); break;
                    case MarkdownCodeInline code: builder.Append(code.Text); break;
                    case MarkdownImageInline image: builder.Append(image.Alt); break;
                    case MarkdownStrongInline strong: Append(strong.Children); break;
                    case MarkdownEmphasisInline emphasis: Append(emphasis.Children); break;
                    case MarkdownStrikethroughInline strike: Append(strike.Children); break;
                    case MarkdownLinkInline link: Append(link.Children); break;
                    case MarkdownLineBreakInline: builder.Append(' '); break;
                }
            }
        }
    }

    #endregion
}
