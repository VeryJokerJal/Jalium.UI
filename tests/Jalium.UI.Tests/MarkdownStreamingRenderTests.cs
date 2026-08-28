using System.Linq;
using System.Reflection;
using System.Text;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;

/// <summary>
/// 流式（逐字增长）渲染的稳定性与配套 API。
/// </summary>
/// <remarks>
/// Markdown 的语义要看到闭合记号才定得下来，所以逐字渲染天生会「改主意」：<c>**bold**</c> 打到一半
/// 先是字面量，再变斜体，最后才是粗体。这里的核心度量是**整个输入过程里结构变了多少次**——
/// 只比结构不比内容，正常的文字增长不算变化，只有语义跳变才算。
/// </remarks>
public class MarkdownStreamingRenderTests
{
    /// <summary>
    /// 把块树压成「只有结构、没有内容」的形状串：文字增长不改变它，语义跳变才会。
    /// </summary>
    private static string Shape(IReadOnlyList<MarkdownBlock> blocks) =>
        string.Join("|", blocks.Select(ShapeOf));

    private static string ShapeOf(MarkdownBlock block) => block switch
    {
        MarkdownParagraphBlock p => "P(" + InlineShape(p.Inlines) + ")",
        MarkdownHeadingBlock h => "H" + h.Level + "(" + InlineShape(h.Inlines) + ")",
        MarkdownCodeBlock c => "CODE[" + (c.Language ?? "-") + "]",
        MarkdownTableBlock t => "TABLE" + t.Rows.Count,
        MarkdownListBlock l => "LIST" + l.Items.Count,
        MarkdownQuoteBlock q => "QUOTE(" + Shape(q.Blocks) + ")",
        MarkdownRuleBlock => "HR",
        MarkdownFootnoteDefinitionBlock => "FN",
        _ => "?",
    };

    private static string InlineShape(IReadOnlyList<MarkdownInline> inlines)
    {
        var builder = new StringBuilder();
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkdownTextInline: break;
                case MarkdownStrongInline s: builder.Append('b').Append(InlineShape(s.Children)); break;
                case MarkdownEmphasisInline e: builder.Append('i').Append(InlineShape(e.Children)); break;
                case MarkdownStrikethroughInline t: builder.Append('s').Append(InlineShape(t.Children)); break;
                case MarkdownCodeInline: builder.Append('c'); break;
                case MarkdownLinkInline l: builder.Append('a').Append(InlineShape(l.Children)); break;
                case MarkdownImageInline: builder.Append('m'); break;
                case MarkdownFootnoteReferenceInline: builder.Append('f'); break;
                case MarkdownLineBreakInline: builder.Append('n'); break;
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<MarkdownBlock> Parse(string source, bool streaming)
    {
        var lines = MarkdownParser.Normalize(source).Split('\n');
        var context = MarkdownParseContext.Create(lines, baseUri: null, enableBareAutolinks: true, isStreaming: streaming);
        return MarkdownParser.ParseLines(lines, context, blockLineStarts: null);
    }

    /// <summary>逐字打完整段文本，收集每一步的结构形状。</summary>
    private static List<string> ShapesWhileTyping(string source, bool streaming)
    {
        var shapes = new List<string>();
        for (var length = 1; length <= source.Length; length++)
        {
            shapes.Add(Shape(Parse(source[..length], streaming)));
        }

        return shapes;
    }

    #region Optimistic closing

    [Theory]
    // 打 `**bold**` 的中途，普通解析会经过 `**bold*` = 字面 `*` + 斜体 `bold` 这个中间态，
    // 于是这个词先变斜体再变粗体。斜体是个纯粹错误的中间语义，流式下一次都不该出现。
    [InlineData("Use **bold** here", "i")]
    [InlineData("Mix **a** plus **b** end", "i")]
    public void StreamingNeverShowsAWrongIntermediateEmphasis(string source, string forbidden)
    {
        var plain = ShapesWhileTyping(source, streaming: false);
        var streaming = ShapesWhileTyping(source, streaming: true);

        // 先确认这个错误中间态在普通解析里确实会出现，否则这条断言是空转的。
        Assert.Contains(plain, shape => shape.Contains(forbidden, StringComparison.Ordinal));
        Assert.DoesNotContain(streaming, shape => shape.Contains(forbidden, StringComparison.Ordinal));
    }

    [Theory]
    // 一旦某段文字定型成某个格式，就不该在后续输入里再变回去或改成别的格式。
    [InlineData("Use **bold** here")]
    [InlineData("Use *italic* here")]
    [InlineData("Use ~~struck~~ here")]
    [InlineData("Use `code` here")]
    [InlineData("Mix **a** and *b* and `c`")]
    [InlineData("Here is code:' + NL + NL + '```python' + NL + 'x = 1' + NL + '```' + NL + NL + 'Done.")]
    public void StreamingShapeOnlyMovesForward(string source)
    {
        var shapes = ShapesWhileTyping(source, streaming: true);
        var seen = new HashSet<string>();
        var previous = string.Empty;

        foreach (var shape in shapes)
        {
            if (shape == previous)
            {
                continue;
            }

            // 回到一个之前出现过的形状 = 之前那次变化是错的，用户会看到来回横跳。
            Assert.True(
                seen.Add(shape),
                $"形状 [{shape}] 又出现了一次——中间来回跳过：{string.Join(" -> ", shapes.Distinct())}");
            previous = shape;
        }
    }

    [Fact]
    public void StreamingIsNeverJumpierThanPlainParsing()
    {
        // 兜底：逐字过程中的形状变化次数，流式不能比普通解析更多。
        foreach (var source in new[]
                 {
                     "Use **bold** here",
                     "Use `code` here",
                     "| a | b |\n|---|---|\n| 1 | 2 |\n\nafter",
                     "Here is code:\n\n```python\nx = 1\n```\n\nDone.",
                 })
        {
            var streaming = ShapesWhileTyping(source, streaming: true).Distinct().Count();
            var plain = ShapesWhileTyping(source, streaming: false).Distinct().Count();

            Assert.True(
                streaming <= plain,
                $"[{source.Replace("\n", @"\n", StringComparison.Ordinal)}] 流式反而更抖：{streaming} > {plain}");
        }
    }

    [Theory]
    // 未闭合的强调直接按已闭合渲染，别让同一个词先字面量、再斜体、最后粗体地跳。
    [InlineData("Use **bol", "P(b)")]
    [InlineData("Use *ital", "P(i)")]
    [InlineData("Use ~~stru", "P(s)")]
    [InlineData("Use `cod", "P(c)")]
    [InlineData("Use ***both", "P(b)")]
    public void UnclosedInlineMarkers_RenderAsClosedWhileStreaming(string source, string expectedShape)
    {
        Assert.Equal(expectedShape, Shape(Parse(source, streaming: true)));

        // 流式一结束就要退回字面量语义——没闭合的记号本来就不该是格式。
        Assert.Equal("P()", Shape(Parse(source, streaming: false)));
    }

    [Fact]
    public void PartialClosingFence_IsNotShownAsCodeContent()
    {
        // 收尾的 ``` 是一下一下打出来的，别让它先作为代码内容出现再凭空消失。
        var partial = Parse("```py\nx = 1\n``", streaming: true);
        var code = Assert.IsType<MarkdownCodeBlock>(Assert.Single(partial));
        Assert.Equal("x = 1", code.Text);

        var complete = Parse("```py\nx = 1\n```", streaming: true);
        Assert.Equal("x = 1", Assert.IsType<MarkdownCodeBlock>(Assert.Single(complete)).Text);
    }

    [Fact]
    public void PartialTableSeparator_StandsTheTableUpEarly()
    {
        // 分隔行打到一半就把表格立起来，免得整张表先以一行原始文本出现再整体跳成表。
        var partial = Parse("| a | b |\n|--", streaming: true);
        Assert.IsType<MarkdownTableBlock>(Assert.Single(partial));

        // 非流式下它就只是一段普通文字。
        Assert.IsType<MarkdownParagraphBlock>(Assert.Single(Parse("| a | b |\n|--", streaming: false)));
    }

    [Fact]
    public void HalfTypedLinkTarget_DoesNotFlashAsABareAutolink()
    {
        // `[文字](https://…` 打到一半时，目标那截不该被认成裸链接——否则同一段文字先黑后蓝再变。
        var partial = Parse("See [the docs](https://e.com/x", streaming: true);
        Assert.Equal("P()", Shape(partial));

        // 闭合之后当然是链接。
        Assert.Equal("P(a)", Shape(Parse("See [the docs](https://e.com/x)", streaming: true)));
    }

    [Fact]
    public void StreamingDoesNotChangeAlreadyClosedContent()
    {
        // 乐观闭合只作用于末尾：前面已经闭合的部分，两种模式必须解析成同一棵树。
        const string source = "# Title\n\n**done** and `done` and [x](https://e.com)\n\n| a |\n|---|\n| 1 |\n\ntail";
        Assert.True(MarkdownParser.BlocksEqual(Parse(source, streaming: true), Parse(source, streaming: false)));
    }

    #endregion

    #region Streaming API

    private static void ResetApplicationState()
    {
        typeof(Application)
            .GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);

        typeof(ThemeManager)
            .GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, null);
    }

    private static Markdown Realize(Markdown markdown, double width = 480, double height = 240)
    {
        var host = new StackPanel { Width = width, Height = height };
        host.Children.Add(markdown);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        return markdown;
    }

    private static void WithApplication(Action body)
    {
        ResetApplicationState();
        _ = new Application();
        try
        {
            body();
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void AppendText_GrowsTheTextAndKeepsTheFastPath()
    {
        WithApplication(() =>
        {
            var markdown = Realize(new Markdown { Text = "# Title\n\nBody" });
            var rebuildsBefore = Markdown.DebugFullRebuilds;

            markdown.AppendText(" and more");
            Realize(markdown);

            Assert.Equal("# Title\n\nBody and more", markdown.Text);
            Assert.Equal(rebuildsBefore, Markdown.DebugFullRebuilds);
        });
    }

    [Fact]
    public void AppendText_IgnoresEmptyChunks()
    {
        WithApplication(() =>
        {
            var markdown = Realize(new Markdown { Text = "Body" });
            markdown.AppendText(null);
            markdown.AppendText(string.Empty);
            Assert.Equal("Body", markdown.Text);
        });
    }

    [Fact]
    public void Clear_EmptiesTheContent()
    {
        WithApplication(() =>
        {
            var markdown = Realize(new Markdown { Text = "# Title\n\nBody" });
            markdown.Clear();
            Realize(markdown);

            Assert.Equal(string.Empty, markdown.Text);
            Assert.Empty(markdown.GetPlainText());
        });
    }

    [Fact]
    public void IsStreaming_ReparsesWhenItFlips()
    {
        WithApplication(() =>
        {
            var markdown = Realize(new Markdown { Text = "Use **bol", IsStreaming = true });
            Assert.Contains("**", markdown.GetMarkdownText(), StringComparison.Ordinal);

            // 流式期间按粗体渲染。
            var streamingBlocks = Parse("Use **bol", streaming: true);
            Assert.Equal("P(b)", Shape(streamingBlocks));

            // 关掉流式后，同样的文本必须退回字面量——控件要真的重新解析，而不是留着旧树。
            markdown.IsStreaming = false;
            Realize(markdown);
            Assert.Equal("Use **bol", markdown.GetPlainText());
        });
    }

    [Fact]
    public void TypingCharacterByCharacter_EndsUpIdenticalToParsingItAllAtOnce()
    {
        WithApplication(() =>
        {
            const string source = "# T\n\n**a** and `b`\n\n- x\n- y\n\n```py\nz = 1\n```";

            var streamed = Realize(new Markdown { IsStreaming = true });
            foreach (var c in source)
            {
                streamed.AppendText(c.ToString());
            }

            streamed.IsStreaming = false;
            Realize(streamed);

            var atOnce = Realize(new Markdown { Text = source });

            // 逐字打完的结果必须和一次性设置完全一致，否则增量路径就是在悄悄丢东西。
            Assert.Equal(atOnce.GetPlainText(), streamed.GetPlainText());
            Assert.Equal(atOnce.GetHtml(), streamed.GetHtml());
        });
    }

    #endregion
}
