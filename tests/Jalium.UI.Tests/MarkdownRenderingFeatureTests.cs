using System.Linq;
using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// 图片、删除线、表格列对齐与脚注在可视树上的落地效果。
/// </summary>
[Collection("Application")]
public class MarkdownRenderingFeatureTests
{
    private static void ResetApplicationState()
    {
        typeof(Application)
            .GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)
            ?.SetValue(null, null);

        typeof(ThemeManager)
            .GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, null);
    }

    private static Markdown Realize(Markdown markdown, double width = 480, double height = 400)
    {
        var host = new StackPanel { Width = width, Height = height };
        host.Children.Add(markdown);
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        return markdown;
    }

    private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        var results = new List<T>();
        Walk(root);
        return results;

        void Walk(DependencyObject node)
        {
            if (node is T match)
            {
                results.Add(match);
            }

            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                if (VisualTreeHelper.GetChild(node, i) is { } child)
                {
                    Walk(child);
                }
            }
        }
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

    #region Images

    [Fact]
    public void StandaloneImage_BecomesImagePresenter()
    {
        WithApplication(() =>
        {
            var markdown = Realize(new Markdown
            {
                BaseUri = new Uri("https://cdn.example/assets/"),
                Text = "![The cat](cat.png \"A cat\")",
            });

            var presenter = Assert.Single(FindAll<MarkdownImagePresenter>(markdown));
            Assert.Equal("The cat", presenter.Alt);
            Assert.Equal("cat.png", presenter.ImageTarget);
            Assert.Equal("A cat", presenter.Caption);
            Assert.True(presenter.HasCaption);
            Assert.True(presenter.HasSource);

            // 独占一行的图片不应该再被包进一个文本段落里。
            Assert.Empty(FindAll<MarkdownParagraphPresenter>(markdown));
        });
    }

    [Fact]
    public void StandaloneImage_WithUnresolvableTarget_FallsBackToAltText()
    {
        WithApplication(() =>
        {
            // 没有 BaseUri 的相对路径解析不出绝对 URI，模板要退回显示替代文本。
            var markdown = Realize(new Markdown { Text = "![missing](nowhere.png)" });

            var presenter = Assert.Single(FindAll<MarkdownImagePresenter>(markdown));
            Assert.False(presenter.HasSource);
            Assert.Null(presenter.Source);
            Assert.Equal("missing", presenter.Alt);
        });
    }

    [Fact]
    public void SeveralImagesOnOneLine_EachBecomeTheirOwnBlock()
    {
        WithApplication(() =>
        {
            var markdown = Realize(new Markdown
            {
                BaseUri = new Uri("https://cdn.example/"),
                Text = "![a](a.png) ![b](b.png)",
            });

            var presenters = FindAll<MarkdownImagePresenter>(markdown);
            Assert.Equal(2, presenters.Count);
            Assert.Equal(new[] { "a", "b" }, presenters.Select(static p => p.Alt));
        });
    }

    [Fact]
    public void ImageMixedWithText_StaysInlineInsideTheTextPresenter()
    {
        WithApplication(() =>
        {
            var markdown = Realize(new Markdown
            {
                BaseUri = new Uri("https://cdn.example/"),
                Text = "before ![icon](icon.png) after",
            });

            // 混排的图片跟着文字走，所以段落还在，块级图片容器不该出现。
            Assert.Empty(FindAll<MarkdownImagePresenter>(markdown));
            var text = Assert.Single(FindAll<MarkdownTextPresenter>(markdown));

            var image = Assert.Single(FindAll<Image>(text));
            Assert.NotNull(image.Source);
        });
    }

    [Fact]
    public void InlineImageElements_AreReusedRetargetedAndReleasedAsSpansChange()
    {
        WithApplication(() =>
        {
            // 直接喂 Spans：这条不变式属于 MarkdownTextPresenter 自己，与谁在上面重建可视树无关。
            var presenter = new MarkdownTextPresenter { Spans = SpansWith("a.png") };
            var first = Assert.Single(FindAll<Image>(presenter));
            var firstSource = first.Source;

            // 目标没变：元素连同它已下载的位图一起留用，否则流式追加每改一个字都要重拉一遍图。
            presenter.Spans = SpansWith("a.png");
            Assert.Same(first, Assert.Single(FindAll<Image>(presenter)));
            Assert.Same(firstSource, Assert.Single(FindAll<Image>(presenter)).Source);

            // 目标变了：元素还是那一个，换的是 Source。
            presenter.Spans = SpansWith("b.png");
            var retargeted = Assert.Single(FindAll<Image>(presenter));
            Assert.Same(first, retargeted);
            Assert.NotSame(firstSource, retargeted.Source);

            // 图片没了：子元素跟着释放，不能留在可视树里继续画。
            presenter.Spans = new[] { new MarkdownTextSpan("plain", default) };
            Assert.Empty(FindAll<Image>(presenter));
        });

        static MarkdownTextSpan[] SpansWith(string target) =>
        [
            new MarkdownTextSpan("before ", default),
            new MarkdownTextSpan(
                "alt",
                default,
                IsLineBreak: false,
                Image: new MarkdownInlineImage(new Uri("https://cdn.example/" + target), "alt", target, null)),
            new MarkdownTextSpan(" after", default),
        ];
    }

    #endregion

    #region Strikethrough

    [Fact]
    public void Strikethrough_ReachesTheTextSpans()
    {
        WithApplication(() =>
        {
            var markdown = Realize(new Markdown { Text = "keep ~~drop~~ keep" });
            var text = Assert.Single(FindAll<MarkdownTextPresenter>(markdown));

            var struck = text.Spans.Where(static s => s.Style.Strikethrough).ToList();
            Assert.Equal("drop", string.Concat(struck.Select(static s => s.Text)));
            Assert.DoesNotContain(text.Spans.Where(static s => !s.Style.Strikethrough),
                static s => s.Text.Contains("drop", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Strikethrough_CombinesWithBoldAndLinks()
    {
        WithApplication(() =>
        {
            var markdown = Realize(new Markdown { Text = "~~**gone** [away](https://e.com)~~" });
            var text = Assert.Single(FindAll<MarkdownTextPresenter>(markdown));

            Assert.All(text.Spans.Where(static s => s.Text.Length > 0), static s => Assert.True(s.Style.Strikethrough));
            Assert.Contains(text.Spans, static s => s.Style.Strikethrough && s.Style.Bold);
            Assert.Contains(text.Spans, static s => s.Style.Strikethrough && s.Style.LinkUri != null);
        });
    }

    #endregion

    #region Tables

    [Fact]
    public void TableColumnAlignment_ReachesTheCells()
    {
        WithApplication(() =>
        {
            var markdown = Realize(new Markdown
            {
                Text = "| l | c | r | n |\n|:--|:-:|--:|---|\n| 1 | 2 | 3 | 4 |",
            });

            var cells = FindAll<MarkdownTableCellPresenter>(markdown);
            Assert.Equal(8, cells.Count);

            foreach (var row in cells.GroupBy(static c => c.RowIndex))
            {
                Assert.Equal(
                    new[]
                    {
                        HorizontalAlignment.Left,
                        HorizontalAlignment.Center,
                        HorizontalAlignment.Right,
                        HorizontalAlignment.Stretch,
                    },
                    row.OrderBy(static c => c.ColumnIndex).Select(static c => c.ColumnAlignment));
            }
        });
    }

    [Fact]
    public void TableRowsAreRectangular_EvenWhenSourceRowsAreRagged()
    {
        WithApplication(() =>
        {
            var markdown = Realize(new Markdown
            {
                Text = "| a | b | c |\n|---|---|---|\n| 1 |\n| 1 | 2 | 3 | 4 |",
            });

            var table = Assert.Single(FindAll<MarkdownTablePresenter>(markdown));
            Assert.Equal(3, table.ColumnCount);
            Assert.Equal(3, table.RowCount);
            Assert.Equal(9, FindAll<MarkdownTableCellPresenter>(markdown).Count);
        });
    }

    #endregion

    #region Footnotes

    [Fact]
    public void FootnoteDefinition_BecomesItsOwnPresenter()
    {
        WithApplication(() =>
        {
            var markdown = Realize(new Markdown
            {
                Text = "See the note[^n].\n\n[^n]: Explained here.",
            });

            var footnote = Assert.Single(FindAll<MarkdownFootnotePresenter>(markdown));
            Assert.Equal("n", footnote.Label);
            Assert.Equal(1, footnote.Number);
            Assert.Equal("1.", footnote.Marker);

            // 引用点上留下的是可读的序号，而不是 [^n] 原文。
            var body = FindAll<MarkdownTextPresenter>(markdown)[0];
            Assert.Contains("[1]", string.Concat(body.Spans.Select(static s => s.Text)), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void FootnoteReference_IsARelativeLinkToItsDefinition()
    {
        WithApplication(() =>
        {
            // 带 BaseUri 也必须保持相对：一旦被解析成绝对地址，点击就会去调系统浏览器。
            var markdown = Realize(new Markdown
            {
                BaseUri = new Uri("https://e.com/docs/"),
                Text = "See[^n].\n\n[^n]: note",
            });

            var body = FindAll<MarkdownTextPresenter>(markdown)[0];
            var reference = Assert.Single(body.Spans.Where(static s => s.Text == "[1]"));

            Assert.NotNull(reference.Style.LinkUri);
            Assert.False(reference.Style.LinkUri!.IsAbsoluteUri);
            Assert.Equal("#fn-n", reference.Style.LinkUri.OriginalString);
        });
    }

    [Fact]
    public void ImagePresenter_TracksWhetherItHasAUsableSource()
    {
        WithApplication(() =>
        {
            var presenter = new MarkdownImagePresenter { Alt = "alt" };
            Assert.False(presenter.HasSource);

            presenter.Source = new Jalium.UI.Media.Imaging.BitmapImage(new Uri("https://cdn.example/a.png"));
            Assert.True(presenter.HasSource);

            presenter.Source = null;
            Assert.False(presenter.HasSource);
        });
    }

    #endregion

    #region Container styles

    [Fact]
    public void ImageStyleAndFootnoteStyle_ApplyToTheirContainers()
    {
        WithApplication(() =>
        {
            var imageStyle = new Style(typeof(MarkdownImagePresenter));
            imageStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(7)));

            var footnoteStyle = new Style(typeof(MarkdownFootnotePresenter));
            footnoteStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(9)));

            var markdown = Realize(new Markdown
            {
                BaseUri = new Uri("https://cdn.example/"),
                ImageStyle = imageStyle,
                FootnoteStyle = footnoteStyle,
                Text = "![a](a.png)\n\ntext[^n]\n\n[^n]: note",
            });

            Assert.Equal(new Thickness(7), Assert.Single(FindAll<MarkdownImagePresenter>(markdown)).Margin);
            Assert.Equal(new Thickness(9), Assert.Single(FindAll<MarkdownFootnotePresenter>(markdown)).Margin);
        });
    }

    #endregion

    #region Draw-run geometry

    private static MarkdownTextPresenter FirstTextPresenter(Markdown markdown) =>
        FindAll<MarkdownTextPresenter>(markdown)[0];

    [Theory]
    // 换款式的地方绘制批次会断开，下一批的起点是「前面各片段宽度累加」出来的。
    // 只要有片段把宽度报小了（空格 token 曾经报 0），这个起点就会落在上一批实际画出的字上。
    [InlineData("Read more in the [control reference](https://e.com/ref).")]
    [InlineData("plain **bold** and `code` end")]
    [InlineData("a [link](https://e.com/x) b")]
    [InlineData("one ~~two~~ three *four* five")]
    [InlineData("**a** **b** **c**")]
    [InlineData("trailing spaces   [link](https://e.com/x)")]
    public void DrawRuns_DeclareEnoughWidthForWhatTheyPaint(string source)
    {
        WithApplication(() =>
        {
            var markdown = Realize(new Markdown { Text = source }, width: 600);
            var presenter = FirstTextPresenter(markdown);
            var runs = presenter.DebugGetDrawRuns();

            Assert.NotEmpty(runs);

            foreach (var run in runs)
            {
                var painted = presenter.DebugMeasureRunWidth(run.Text, run.Style);
                Assert.True(
                    run.Width >= painted - 0.5,
                    $"批次 [{run.Text}] 声明宽度 {run.Width:F2}，实际要画 {painted:F2}——下一批会叠上来。");
            }
        });
    }

    [Theory]
    [InlineData("Read more in the [control reference](https://e.com/ref).")]
    [InlineData("plain **bold** and `code` end")]
    [InlineData("one ~~two~~ three *four* five")]
    public void DrawRuns_DoNotOverlapEachOther(string source)
    {
        WithApplication(() =>
        {
            var markdown = Realize(new Markdown { Text = source }, width: 600);
            var runs = FirstTextPresenter(markdown).DebugGetDrawRuns();

            for (var index = 1; index < runs.Count; index++)
            {
                var previous = runs[index - 1];
                var current = runs[index];

                // 同一行内才有可比性：换行会把 x 拨回 0。
                if (current.X < previous.X)
                {
                    continue;
                }

                Assert.True(
                    current.X >= previous.X + previous.Width - 0.5,
                    $"批次 [{current.Text}] 起点 {current.X:F2} 落在了 [{previous.Text}] " +
                    $"（{previous.X:F2}+{previous.Width:F2}）的里面。");
            }
        });
    }

    [Fact]
    public void WhitespaceTokens_HaveRealWidth()
    {
        WithApplication(() =>
        {
            // 直击根因：词与词之间的空白是独立 token，宽度报 0 就等于把后面的字往前拽。
            var markdown = Realize(new Markdown { Text = "aaa [bbb](https://e.com/x)" }, width: 600);
            var presenter = FirstTextPresenter(markdown);

            var space = presenter.DebugMeasureRunWidth(" ", default);
            Assert.True(space > 0.5, $"空格被测成了 {space:F2} 宽。");

            var runs = presenter.DebugGetDrawRuns();
            Assert.Equal(2, runs.Count);
            Assert.True(
                runs[1].X >= runs[0].X + runs[0].Width - 0.5,
                $"链接批次起点 {runs[1].X:F2} 没有让开前一批的 {runs[0].Width:F2} 宽。");
        });
    }

    [Fact]
    public void TrailingWhitespace_DoesNotInflateTheDesiredWidth()
    {
        WithApplication(() =>
        {
            // 行宽要按 WPF 的规矩去掉行尾空白，否则看不见的空格会把 DesiredSize 撑大。
            var withTrailing = Realize(new Markdown { Text = "word   " }, width: 600);
            var withoutTrailing = Realize(new Markdown { Text = "word" }, width: 600);

            Assert.Equal(
                FirstTextPresenter(withoutTrailing).DesiredSize.Width,
                FirstTextPresenter(withTrailing).DesiredSize.Width,
                precision: 1);
        });
    }

    #endregion

    #region Streaming

    [Fact]
    public void StreamingAppend_StaysOnTheFastPathWithoutDefinitions()
    {
        WithApplication(() =>
        {
            var markdown = Realize(new Markdown { Text = "# Title\n\nBody" });
            var rebuildsBefore = Markdown.DebugFullRebuilds;

            markdown.Text = "# Title\n\nBody with more";
            Realize(markdown);

            Assert.Equal(rebuildsBefore, Markdown.DebugFullRebuilds);
        });
    }

    [Fact]
    public void StreamingAppend_FallsBackToFullParseOnceADefinitionAppears()
    {
        WithApplication(() =>
        {
            // 定义是全文档可见的：块与块之间不再无状态，尾部增量的前提没了。
            var markdown = Realize(new Markdown { Text = "See [d].\n\nBody" });
            var rebuildsBefore = Markdown.DebugFullRebuilds;

            markdown.Text = "See [d].\n\nBody\n\n[d]: https://e.com/d";
            Realize(markdown);

            Assert.True(
                Markdown.DebugFullRebuilds > rebuildsBefore,
                "追加链接定义后必须重扫定义表，不能沿用尾部增量的结果。");

            // 而且定义要真的生效——它出现在使用点之后。
            var text = FindAll<MarkdownTextPresenter>(markdown)[0];
            Assert.Contains(text.Spans, static s => s.Style.LinkUri?.ToString() == "https://e.com/d");
        });
    }

    #endregion
}
