using System.Linq;
using System.Text;
using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

/// <summary>
/// <see cref="MarkdownParser"/> 对 CommonMark / GFM 语法的解析行为。
/// </summary>
/// <remarks>
/// 断言写成「把块树摊平成一行文本再比字符串」，而不是层层 <c>Assert.IsType</c>：
/// 这些用例大多在验证结构本身（谁包着谁、拆成了几段），摊平后的形状读起来就是预期的结构，
/// 出错时的 diff 也直接指出差在哪一段。
/// </remarks>
public class MarkdownCommonMarkTests
{
    private static readonly Uri BaseUri = new("https://base.example/docs/");

    private static string Dump(string source, Uri? baseUri = null) =>
        DumpBlocks(MarkdownParser.Parse(source, baseUri ?? BaseUri));

    private static string DumpBlocks(IEnumerable<MarkdownBlock> blocks) =>
        string.Join(";", blocks.Select(DumpBlock));

    private static string DumpBlock(MarkdownBlock block)
    {
        switch (block)
        {
            case MarkdownParagraphBlock paragraph:
                return $"P({DumpInlines(paragraph.Inlines)})";

            case MarkdownHeadingBlock heading:
                return $"H{heading.Level}({DumpInlines(heading.Inlines)})";

            case MarkdownCodeBlock code:
                return $"CODE[{code.Language ?? "-"}]({code.Text.Replace("\n", "\\n", StringComparison.Ordinal)})";

            case MarkdownRuleBlock:
                return "HR";

            case MarkdownQuoteBlock quote:
                return $"QUOTE({DumpBlocks(quote.Blocks)})";

            case MarkdownListBlock list:
                var items = string.Join(";", list.Items.Select(item =>
                    $"LI{(item.TaskState is { } task ? (task ? "[x]" : "[ ]") : string.Empty)}({DumpBlocks(item.Blocks)})"));
                return $"{(list.Ordered ? "OL" : "UL")}{(list.IsLoose ? "-loose" : "-tight")}@{list.StartIndex}({items})";

            case MarkdownTableBlock table:
                var alignments = string.Join(",", (table.Alignments ?? Array.Empty<MarkdownColumnAlignment>())
                    .Select(static a => a.ToString()[..1]));
                var rows = string.Join(";", table.HeaderRows.Concat(table.Rows).Select(static row =>
                    "ROW(" + string.Join(string.Empty, row.Cells.Select(cell => "|" + DumpInlines(cell))) + ")"));
                return $"TABLE[{alignments}]({rows})";

            case MarkdownFootnoteDefinitionBlock footnote:
                return $"FN[{footnote.Label}#{footnote.Number}]({DumpBlocks(footnote.Blocks)})";

            default:
                return "?";
        }
    }

    private static string DumpInlines(IReadOnlyList<MarkdownInline> inlines)
    {
        var builder = new StringBuilder();
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkdownTextInline text:
                    builder.Append(text.Text);
                    break;

                case MarkdownStrongInline strong:
                    builder.Append("<b>").Append(DumpInlines(strong.Children)).Append("</b>");
                    break;

                case MarkdownEmphasisInline emphasis:
                    builder.Append("<i>").Append(DumpInlines(emphasis.Children)).Append("</i>");
                    break;

                case MarkdownStrikethroughInline strike:
                    builder.Append("<s>").Append(DumpInlines(strike.Children)).Append("</s>");
                    break;

                case MarkdownCodeInline code:
                    builder.Append("<c>").Append(code.Text).Append("</c>");
                    break;

                case MarkdownLinkInline link:
                    builder.Append("<a ").Append(link.Uri?.ToString() ?? "-");
                    if (link.Title != null)
                    {
                        builder.Append(" t=").Append(link.Title);
                    }

                    builder.Append('>').Append(DumpInlines(link.Children)).Append("</a>");
                    break;

                case MarkdownImageInline image:
                    builder.Append("<img ").Append(image.Uri?.ToString() ?? "-").Append(" alt=").Append(image.Alt);
                    if (image.Title != null)
                    {
                        builder.Append(" t=").Append(image.Title);
                    }

                    builder.Append('>');
                    break;

                case MarkdownFootnoteReferenceInline footnote:
                    builder.Append("<fn ").Append(footnote.Label).Append('#').Append(footnote.Number).Append('>');
                    break;

                case MarkdownLineBreakInline:
                    builder.Append("<br>");
                    break;
            }
        }

        return builder.ToString();
    }

    #region Images

    [Theory]
    // 最常见的形式：以前 `!` 会掉出来变成字面量，图片本身退化成链接。
    [InlineData("![alt text](cat.png)", "P(<img https://base.example/docs/cat.png alt=alt text>)")]
    [InlineData("![](cat.png)", "P(<img https://base.example/docs/cat.png alt=>)")]
    [InlineData("![a](cat.png \"Title\")", "P(<img https://base.example/docs/cat.png alt=a t=Title>)")]
    [InlineData("![a](<my cat.png>)", "P(<img https://base.example/docs/my cat.png alt=a>)")]
    [InlineData("![a](https://cdn.example/x.png)", "P(<img https://cdn.example/x.png alt=a>)")]
    [InlineData("see ![a](x.png) here", "P(see <img https://base.example/docs/x.png alt=a> here)")]
    [InlineData("[![a](x.png)](https://target.example/)",
        "P(<a https://target.example/><img https://base.example/docs/x.png alt=a></a>)")]
    [InlineData("![**bold** alt](x.png)", "P(<img https://base.example/docs/x.png alt=bold alt>)")]
    public void Images(string source, string expected) => Assert.Equal(expected, Dump(source));

    #endregion

    #region Emphasis: flanking rules

    [Theory]
    // 词内的下划线不是强调 —— 这条以前会把每个标识符切成三段斜体。
    [InlineData("call get_user_name(x)", "P(call get_user_name(x))")]
    [InlineData("MAX_INT_VALUE and __init__", "P(MAX_INT_VALUE and <b>init</b>)")]
    [InlineData("snake_case_here", "P(snake_case_here)")]
    // 星号不受词内限制，这是 CommonMark 明确区分 * 与 _ 的地方。
    [InlineData("a*b*c", "P(a<i>b</i>c)")]
    [InlineData("_start_ and _end_", "P(<i>start</i> and <i>end</i>)")]
    [InlineData("***both***", "P(<i><b>both</b></i>)")]
    [InlineData("**a *b* c**", "P(<b>a <i>b</i> c</b>)")]
    [InlineData("*a **b** c*", "P(<i>a <b>b</b> c</i>)")]
    [InlineData("**bold** and *italic*", "P(<b>bold</b> and <i>italic</i>)")]
    [InlineData("* not emphasis", "UL-tight@1(LI(P(not emphasis)))")]
    [InlineData("a * b * c", "P(a * b * c)")]
    [InlineData("5 * 3 * 2 = 30", "P(5 * 3 * 2 = 30)")]
    public void EmphasisFlanking(string source, string expected) => Assert.Equal(expected, Dump(source));

    [Theory]
    [InlineData("~~gone~~", "P(<s>gone</s>)")]
    [InlineData("~one~", "P(<s>one</s>)")]
    [InlineData("~~**both**~~", "P(<s><b>both</b></s>)")]
    [InlineData("a ~~b~~ c", "P(a <s>b</s> c)")]
    // 单双不混配，也不吃掉围栏残迹。
    [InlineData("~~mismatched~", "P(~~mismatched~)")]
    [InlineData("a ~~~~quad~~~~ b", "P(a ~~~~quad~~~~ b)")]
    public void Strikethrough(string source, string expected) => Assert.Equal(expected, Dump(source));

    #endregion

    #region Links

    [Theory]
    [InlineData("[a](https://e.com \"Title\")", "P(<a https://e.com/ t=Title>a</a>)")]
    [InlineData("[a](https://e.com 'Title')", "P(<a https://e.com/ t=Title>a</a>)")]
    [InlineData("[a](<https://e.com/x y>)", "P(<a https://e.com/x y>a</a>)")]
    [InlineData("[a](guide.md)", "P(<a https://base.example/docs/guide.md>a</a>)")]
    [InlineData("[a](https://e.com/p(1))", "P(<a https://e.com/p(1)>a</a>)")]
    [InlineData("[a]()", "P(<a ->a</a>)")]
    // 链接不能嵌套链接：内层胜出，外层退回字面量。
    [InlineData("[out [in](https://i.example) out](https://o.example)",
        "P([out <a https://i.example/>in</a> out](<a https://o.example/>https://o.example</a>))")]
    public void InlineLinks(string source, string expected) => Assert.Equal(expected, Dump(source));

    [Theory]
    [InlineData("<https://example.com>", "P(<a https://example.com/>https://example.com</a>)")]
    [InlineData("<a@b.com>", "P(<a mailto:a@b.com>a@b.com</a>)")]
    [InlineData("see https://example.com/x now",
        "P(see <a https://example.com/x>https://example.com/x</a> now)")]
    [InlineData("go to www.example.com today",
        "P(go to <a https://www.example.com/>www.example.com</a> today)")]
    [InlineData("mail me@example.com please",
        "P(mail <a mailto:me@example.com>me@example.com</a> please)")]
    // 句末标点与不配对的右括号不属于链接。
    [InlineData("see https://example.com/x.",
        "P(see <a https://example.com/x>https://example.com/x</a>.)")]
    [InlineData("(see https://example.com/x)",
        "P((see <a https://example.com/x>https://example.com/x</a>))")]
    [InlineData("`https://example.com` in code", "P(<c>https://example.com</c> in code)")]
    public void Autolinks(string source, string expected) => Assert.Equal(expected, Dump(source));

    [Theory]
    [InlineData("See [the docs][d].\n\n[d]: https://e.com/docs \"D\"",
        "P(See <a https://e.com/docs t=D>the docs</a>.)")]
    [InlineData("See [docs][].\n\n[docs]: https://e.com/d",
        "P(See <a https://e.com/d>docs</a>.)")]
    [InlineData("See [docs].\n\n[docs]: https://e.com/d",
        "P(See <a https://e.com/d>docs</a>.)")]
    // 标签比较大小写不敏感、空白折叠。
    [InlineData("See [The   Docs][].\n\n[the docs]: https://e.com/d",
        "P(See <a https://e.com/d>The   Docs</a>.)")]
    [InlineData("![logo][l]\n\n[l]: logo.png \"Logo\"",
        "P(<img https://base.example/docs/logo.png alt=logo t=Logo>)")]
    // 没有定义就保持字面量，定义行本身不产出可见内容。
    [InlineData("See [missing][x].", "P(See [missing][x].)")]
    [InlineData("[d]: https://e.com/docs", "")]
    public void ReferenceLinks(string source, string expected) => Assert.Equal(expected, Dump(source));

    #endregion

    #region Entities, escapes, code spans

    [Theory]
    [InlineData("AT&amp;T", "P(AT&T)")]
    [InlineData("&lt;div&gt;", "P(<div>)")]
    [InlineData("&#65;&#x42;", "P(AB)")]
    [InlineData("&copy; &nbsp;x", "P(©  x)")]
    [InlineData("&nosuchentity;", "P(&nosuchentity;)")]
    [InlineData("100 & 200", "P(100 & 200)")]
    // 实体在代码跨度里是字面量。
    [InlineData("`&amp;`", "P(<c>&amp;</c>)")]
    public void Entities(string source, string expected) => Assert.Equal(expected, Dump(source));

    [Theory]
    [InlineData("\\*not italic\\*", "P(*not italic*)")]
    [InlineData("\\[not a link\\]", "P([not a link])")]
    [InlineData("a\\\\b", "P(a\\b)")]
    [InlineData("\\a", "P(\\a)")]
    public void BackslashEscapes(string source, string expected) => Assert.Equal(expected, Dump(source));

    [Theory]
    [InlineData("`code`", "P(<c>code</c>)")]
    [InlineData("``a `b` c``", "P(<c>a `b` c</c>)")]
    [InlineData("`` ` ``", "P(<c>`</c>)")]
    [InlineData("`a**b**c`", "P(<c>a**b**c</c>)")]
    [InlineData("`unclosed", "P(`unclosed)")]
    public void CodeSpans(string source, string expected) => Assert.Equal(expected, Dump(source));

    #endregion

    #region Headings and code blocks

    [Theory]
    [InlineData("Title\n=====", "H1(Title)")]
    [InlineData("Sub\n---", "H2(Sub)")]
    [InlineData("Two\nlines\n===", "H1(Two lines)")]
    [InlineData("### Title ###", "H3(Title)")]
    [InlineData("## Title #not-closing", "H2(Title #not-closing)")]
    [InlineData("#NoSpace", "P(#NoSpace)")]
    [InlineData("####### seven", "P(####### seven)")]
    [InlineData("#", "H1()")]
    // 前面没有段落时，--- 仍然是分隔线。
    [InlineData("---", "HR")]
    [InlineData("para\n\n---", "P(para);HR")]
    public void Headings(string source, string expected) => Assert.Equal(expected, Dump(source));

    [Theory]
    [InlineData("para\n\n    int x = 1;\n    int y = 2;\n\nafter",
        "P(para);CODE[-](int x = 1;\\nint y = 2;);P(after)")]
    [InlineData("```py\nx=1\n```", "CODE[py](x=1)")]
    [InlineData("~~~\nplain\n~~~", "CODE[-](plain)")]
    // 围栏自己的缩进要从内容里扣掉。
    [InlineData("- item\n\n  ```py\n  x = 1\n  ```", "UL-loose@1(LI(P(item);CODE[py](x = 1)))")]
    [InlineData("```\na\n\nb\n```", "CODE[-](a\\n\\nb)")]
    public void CodeBlocks(string source, string expected) => Assert.Equal(expected, Dump(source));

    #endregion

    #region Tables

    [Theory]
    // 短横线只有两个的对齐分隔行以前整张表都识别不出来。
    [InlineData("| a | b | c |\n|:--|:-:|--:|\n| 1 | 2 | 3 |",
        "TABLE[L,C,R](ROW(|a|b|c);ROW(|1|2|3))")]
    [InlineData("| a | b |\n|---|---|\n| 1 | 2 |", "TABLE[N,N](ROW(|a|b);ROW(|1|2))")]
    [InlineData("a | b\n--- | ---\n1 | 2", "TABLE[N,N](ROW(|a|b);ROW(|1|2))")]
    // 转义的竖线是内容。
    [InlineData("| a \\| b | c |\n|---|---|\n| 1 | 2 |", "TABLE[N,N](ROW(|a | b|c);ROW(|1|2))")]
    // 行不齐时按表头列数补齐/截断，表格永远是矩形。
    [InlineData("| a | b |\n|---|---|\n| 1 |\n| 1 | 2 | 3 |",
        "TABLE[N,N](ROW(|a|b);ROW(|1|);ROW(|1|2))")]
    // 分隔行列数与表头不一致时根本不是表。
    [InlineData("a | b | c\n--- | ---\n1 | 2", "P(a | b | c --- | --- 1 | 2)")]
    [InlineData("| a |\n|---|\n| **x** |", "TABLE[N](ROW(|a);ROW(|<b>x</b>))")]
    public void Tables(string source, string expected) => Assert.Equal(expected, Dump(source));

    #endregion

    #region Lists and quotes

    [Theory]
    // 空行分开的同级项是同一个（松散）列表，不是两个列表。
    [InlineData("- a\n\n- b", "UL-loose@1(LI(P(a));LI(P(b)))")]
    [InlineData("- a\n- b", "UL-tight@1(LI(P(a));LI(P(b)))")]
    [InlineData("1. a\n2. b", "OL-tight@1(LI(P(a));LI(P(b)))")]
    [InlineData("3. a\n4. b", "OL-tight@3(LI(P(a));LI(P(b)))")]
    [InlineData("- [ ] todo\n- [x] done", "UL-tight@1(LI[ ](P(todo));LI[x](P(done)))")]
    [InlineData("- a\n  - b", "UL-tight@1(LI(P(a);UL-tight@1(LI(P(b)))))")]
    // "1. " 的内容起点比 "- " 深一格，回退缩进要按实际标记宽度算。
    [InlineData("1. a\n   continued", "OL-tight@1(LI(P(a continued)))")]
    [InlineData("- a\n  continued", "UL-tight@1(LI(P(a continued)))")]
    public void Lists(string source, string expected) => Assert.Equal(expected, Dump(source));

    [Theory]
    // 懒惰续行：不带 > 的行仍属于引用里那个没结束的段落。
    [InlineData("> line one\nline two", "QUOTE(P(line one line two))")]
    [InlineData("> a\n>\n> b", "QUOTE(P(a);P(b))")]
    [InlineData("> **bold**", "QUOTE(P(<b>bold</b>))")]
    [InlineData("> a\n\nb", "QUOTE(P(a));P(b)")]
    [InlineData("> - x\n> - y", "QUOTE(UL-tight@1(LI(P(x));LI(P(y))))")]
    public void Quotes(string source, string expected) => Assert.Equal(expected, Dump(source));

    #endregion

    #region Footnotes and inline HTML

    [Theory]
    [InlineData("text[^1]\n\n[^1]: the note", "P(text<fn 1#1>);FN[1#1](P(the note))")]
    [InlineData("a[^x] b[^y]\n\n[^x]: X\n\n[^y]: Y",
        "P(a<fn x#1> b<fn y#2>);FN[x#1](P(X));FN[y#2](P(Y))")]
    // 没有定义的引用保持字面量。
    [InlineData("text[^missing]", "P(text[^missing])")]
    public void Footnotes(string source, string expected) => Assert.Equal(expected, Dump(source));

    [Theory]
    [InlineData("a<br>b", "P(a<br>b)")]
    [InlineData("a<br />b", "P(a<br>b)")]
    [InlineData("<b>bold</b>", "P(<b>bold</b>)")]
    [InlineData("<em>it</em> and <del>gone</del>", "P(<i>it</i> and <s>gone</s>)")]
    [InlineData("<img src=\"x.png\" alt=\"A\">", "P(<img https://base.example/docs/x.png alt=A>)")]
    [InlineData("<a href=\"https://e.com\">link</a>", "P(<a https://e.com/>link</a>)")]
    // 未知标签剥掉，文字留下 —— 以前会把原始标签当正文显示出来。
    [InlineData("<span class=\"x\">kept</span>", "P(kept)")]
    [InlineData("<div class=\"x\">\nhi\n</div>", "P(hi)")]
    [InlineData("<pre>\n  keep  me\n</pre>", "CODE[-](  keep  me)")]
    [InlineData("<script>evil()</script>", "")]
    public void InlineHtml(string source, string expected) => Assert.Equal(expected, Dump(source));

    #endregion

    #region Round-trip

    [Theory]
    [InlineData("![a](x.png \"T\")")]
    [InlineData("~~gone~~")]
    [InlineData("[a](https://e.com/ \"T\")")]
    [InlineData("| a | b |\n| :--- | ---: |\n| 1 | 2 |")]
    [InlineData("get_user_name and MAX_VALUE")]
    public void MarkdownSourceRoundTrips(string source)
    {
        var first = MarkdownParser.Parse(source, BaseUri);
        var reserialized = MarkdownSerializer.ToMarkdown(first);
        var second = MarkdownParser.Parse(reserialized, BaseUri);

        Assert.True(
            MarkdownParser.BlocksEqual(first, second),
            $"round-trip changed the tree.\nsource:\n{source}\nreserialized:\n{reserialized}");
    }

    [Fact]
    public void HtmlOutput_CarriesImagesStrikethroughAndAlignment()
    {
        var blocks = MarkdownParser.Parse(
            "![a](x.png \"T\")\n\n~~gone~~\n\n| a | b |\n|:--|--:|\n| 1 | 2 |",
            BaseUri);

        var html = MarkdownSerializer.ToHtmlFragment(blocks);

        Assert.Contains("<img src=\"x.png\" alt=\"a\" title=\"T\">", html, StringComparison.Ordinal);
        Assert.Contains("<del>gone</del>", html, StringComparison.Ordinal);
        Assert.Contains("text-align:left", html, StringComparison.Ordinal);
        Assert.Contains("text-align:right", html, StringComparison.Ordinal);
    }

    [Fact]
    public void PlainTextOutput_UsesAltTextForImages()
    {
        var blocks = MarkdownParser.Parse("before ![the cat](cat.png) after", BaseUri);
        Assert.Equal("before the cat after", MarkdownSerializer.ToPlainText(blocks));
    }

    #endregion

    [Theory]
    // 方括号栈与 HTML 标签栈记的都是节点下标，两边都会成段删除节点，交错闭合会让另一边的下标悬空。
    // 这些输入本身没有「正确答案」，要保的是：不抛异常、不丢字。
    [InlineData("[<b>x](https://e.com)y</b>")]
    [InlineData("<b>[x</b>](https://e.com)")]
    [InlineData("<em>a<b>b</em>c</b>")]
    [InlineData("![<b>alt](x.png)</b>")]
    [InlineData("[a](<b>https://e.com</b>)")]
    [InlineData("<b><i><del>[x](y)")]
    [InlineData("[[[[[a](b)")]
    [InlineData("~~[a~~](b)")]
    public void InterleavedBracketsAndHtmlTags_DoNotThrow(string source)
    {
        var blocks = MarkdownParser.Parse(source, BaseUri);
        Assert.NotEmpty(blocks);
    }

    [Fact]
    public void BareAutolinks_CanBeTurnedOff()
    {
        var lines = MarkdownParser.Normalize("see https://example.com/x and a@b.com").Split('\n');
        var context = MarkdownParseContext.Create(lines, BaseUri, enableBareAutolinks: false);
        var blocks = MarkdownParser.ParseLines(lines, context, blockLineStarts: null);

        Assert.Equal("P(see https://example.com/x and a@b.com)", DumpBlocks(blocks));

        // 显式写法不受开关影响。
        var explicitLines = MarkdownParser.Normalize("<https://example.com/x>").Split('\n');
        var explicitContext = MarkdownParseContext.Create(explicitLines, BaseUri, enableBareAutolinks: false);
        Assert.Equal(
            "P(<a https://example.com/x>https://example.com/x</a>)",
            DumpBlocks(MarkdownParser.ParseLines(explicitLines, explicitContext, blockLineStarts: null)));
    }

    #region Definition-table safety

    [Fact]
    public void LinkDefinitionsInsideCodeFences_AreNotDefinitions()
    {
        // 围栏里长得像定义的行不该进定义表，否则正文里的 [d] 会莫名其妙变成链接。
        var dump = Dump("```\n[d]: https://e.com/x\n```\n\nSee [d].");
        Assert.Equal("CODE[-]([d]: https://e.com/x);P(See [d].)", dump);
    }

    [Fact]
    public void DefinitionAfterUse_StillResolves()
    {
        // 定义在使用之后：这正是「必须先扫全文再解析行内」的理由。
        Assert.Equal(
            "P(See <a https://e.com/d>docs</a>.)",
            Dump("See [docs][d].\n\n[d]: https://e.com/d"));
    }

    #endregion
}
