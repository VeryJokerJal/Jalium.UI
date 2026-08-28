using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildTextDisplaySection() => Section(
        "Text & Icons",
        "Read-only text, rich markdown, and the icon element family.",
        Card("TextBlock", BuildTextBlockDemo()),
        Card("Label", new Label { Content = "Account name" }),
        Card("Markdown", BuildMarkdownDemo(), width: 360),
        Card("Markdown (restyled)", BuildStyledMarkdownDemo(), width: 360),
        Card("Markdown (GFM)", BuildGfmMarkdownDemo(), width: 360),
        Card("Markdown (streaming)", BuildStreamingMarkdownDemo(), width: 360),
        Card("FontIcon", BuildFontIconRow()),
        Card("SymbolIcon", BuildSymbolIconRow()),
        Card("PathIcon", BuildPathIconRow()));

    private static UIElement BuildTextBlockDemo()
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(new TextBlock
        {
            Text = "Headline text",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary
        });
        panel.Children.Add(new TextBlock
        {
            Text = "A secondary supporting line that wraps across the available width when the content is long.",
            FontSize = 13,
            Foreground = TextSecondary,
            TextWrapping = TextWrapping.Wrap
        });
        return panel;
    }

    private static UIElement BuildMarkdownDemo() => new Markdown
    {
        Text =
            "### Markdown\n" +
            "Renders **bold**, *italic*, and `inline code`.\n\n" +
            "- First item\n" +
            "- Second item\n" +
            "- Third item\n",
        Width = 320
    };

    // 逐字重放一段 Markdown，用来肉眼确认流式渲染不会「改主意」：
    // 粗体不该先变斜体，代码围栏收尾的 ``` 不该先冒出来再消失，表格不该先以一行原文出现。
    private static UIElement BuildStreamingMarkdownDemo()
    {
        const string script =
            "## Streaming\n" +
            "Watch **bold**, `code` and the table settle without flicker.\n\n" +
            "| step | state |\n" +
            "| :--- | ----: |\n" +
            "| 1 | typing |\n" +
            "| 2 | done |\n\n" +
            "```csharp\n" +
            "var x = 1;\n" +
            "```\n";

        var markdown = new Markdown
        {
            Height = 220,
            AutoScrollToEnd = true,
        };

        var replay = new Button { Content = "Replay", Margin = new Thickness(0, 8, 0, 0) };

        var timer = new Jalium.UI.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(18) };
        var cursor = 0;

        timer.Tick += (_, _) =>
        {
            if (cursor >= script.Length)
            {
                timer.Stop();
                markdown.IsStreaming = false;   // 收尾：按最终语义再解析一次
                return;
            }

            // 一次吐几个字符，更接近模型的分块输出。
            var take = Math.Min(3, script.Length - cursor);
            markdown.AppendText(script.Substring(cursor, take));
            cursor += take;
        };

        void Restart()
        {
            timer.Stop();
            cursor = 0;
            markdown.Clear();
            markdown.IsStreaming = true;
            timer.Start();
        }

        replay.Click += (_, _) => Restart();
        markdown.Loaded += (_, _) => Restart();

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(markdown);
        panel.Children.Add(replay);
        return panel;
    }

    // GFM 扩展与 CommonMark 里几处容易出错的地方放在一张卡里，方便肉眼回归：
    // 标识符不该被下划线拆成斜体，表格要认对齐冒号，脚注与裸链接要成形。
    private static UIElement BuildGfmMarkdownDemo() => new Markdown
    {
        Text =
            "### GFM\n" +
            "~~Struck out~~, `get_user_name` stays whole, and MAX_INT_VALUE too.\n\n" +
            "| Left | Center | Right |\n" +
            "| :--- | :----: | ----: |\n" +
            "| a | b | c |\n\n" +
            "- [x] Task done\n" +
            "- [ ] Task pending\n\n" +
            "Visit https://jalium.dev for the docs[^1].\n\n" +
            "[^1]: Footnotes render as their own block.\n",
        Width = 320
    };

    // 同样的 Markdown 源，外观全部从控件外部改：块级走 Style + 默认模板，
    // 行内走 MarkdownInlineStyle，一行 OnRender 都不用碰。
    private static UIElement BuildStyledMarkdownDemo()
    {
        var accent = new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED));

        var headingStyle = new Style(typeof(MarkdownHeadingPresenter))
        {
            Setters =
            {
                new Setter(Control.ForegroundProperty, accent),
                new Setter(MarkdownHeadingPresenter.HasSeparatorProperty, false),
                new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 8)),
            }
        };

        var quoteStyle = new Style(typeof(MarkdownQuotePresenter))
        {
            Setters =
            {
                new Setter(Control.BackgroundProperty, null),
                new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B))),
                new Setter(Control.BorderThicknessProperty, new Thickness(3, 0, 0, 0)),
                new Setter(Control.PaddingProperty, new Thickness(10, 4, 0, 0)),
                new Setter(Control.CornerRadiusProperty, new CornerRadius(0)),
            }
        };

        var listItemStyle = new Style(typeof(MarkdownListItemPresenter))
        {
            Setters =
            {
                new Setter(MarkdownListItemPresenter.BulletGlyphProperty, "→"),
                new Setter(MarkdownListItemPresenter.MarkerForegroundProperty, accent),
                new Setter(MarkdownListItemPresenter.MarkerWidthProperty, 20.0),
            }
        };

        return new Markdown
        {
            Text =
                "### Restyled\n" +
                "Headings, quotes, bullets and `inline code` all restyled from outside.\n\n" +
                "> A quote with its own rule.\n\n" +
                "- First item\n" +
                "- Second item\n",
            HeadingStyle = headingStyle,
            QuoteStyle = quoteStyle,
            ListItemStyle = listItemStyle,
            InlineCodeForeground = accent,
            InlineCodeBackground = new SolidColorBrush(Color.FromArgb(0x28, 0x7C, 0x3A, 0xED)),
            InlineCodeStyle = new MarkdownInlineStyle
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(7, 2, 7, 2),
                FontSizeRatio = 0.92,
            },
            LinkStyle = new MarkdownInlineStyle
            {
                Decorations = MarkdownTextDecorations.None,
                FontWeight = FontWeights.SemiBold,
            },
            Width = 320
        };
    }

    private static UIElement BuildFontIconRow()
    {
        var family = new FontFamily("Segoe MDL2 Assets");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        // Segoe MDL2 Assets glyphs: Home, Mail, Settings, Save.
        row.Children.Add(new FontIcon { Glyph = "\ue80f", FontFamily = family, FontSize = 24, Margin = new Thickness(0, 0, 16, 0) });
        row.Children.Add(new FontIcon { Glyph = "\ue715", FontFamily = family, FontSize = 24, Margin = new Thickness(0, 0, 16, 0) });
        row.Children.Add(new FontIcon { Glyph = "\ue713", FontFamily = family, FontSize = 24, Margin = new Thickness(0, 0, 16, 0) });
        row.Children.Add(new FontIcon { Glyph = "\ue74e", FontFamily = family, FontSize = 24 });
        return row;
    }

    private static UIElement BuildSymbolIconRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new SymbolIcon(Symbol.Home) { Margin = new Thickness(0, 0, 16, 0) });
        row.Children.Add(new SymbolIcon(Symbol.Save) { Margin = new Thickness(0, 0, 16, 0) });
        row.Children.Add(new SymbolIcon(Symbol.Setting) { Margin = new Thickness(0, 0, 16, 0) });
        row.Children.Add(new SymbolIcon(Symbol.Mail));
        return row;
    }

    private static UIElement BuildPathIconRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        // Simple house outline drawn in the 0..24 coordinate box.
        row.Children.Add(new PathIcon
        {
            Data = Geometry.Parse("M 12,2 L 22,11 L 19,11 L 19,21 L 14,21 L 14,14 L 10,14 L 10,21 L 5,21 L 5,11 L 2,11 Z"),
            Width = 28,
            Height = 28,
            Margin = new Thickness(0, 0, 16, 0)
        });
        // A diamond.
        row.Children.Add(new PathIcon
        {
            Data = Geometry.Parse("M 12,2 L 22,12 L 12,22 L 2,12 Z"),
            Width = 28,
            Height = 28,
            Margin = new Thickness(0, 0, 16, 0)
        });
        // A plus / cross.
        row.Children.Add(new PathIcon
        {
            Data = Geometry.Parse("M 9,2 L 15,2 L 15,9 L 22,9 L 22,15 L 15,15 L 15,22 L 9,22 L 9,15 L 2,15 L 2,9 L 9,9 Z"),
            Width = 28,
            Height = 28,
            Margin = new Thickness(0, 0, 16, 0)
        });
        // A square ring (frame): two nested same-winding contours with the
        // default EvenOdd fill rule — the inner square must be a HOLE. Exercises
        // compound/hole rendering; under PathAntiAliasing.Analytic this used to
        // fill solid (winding-sign hole classification), now renders as a frame.
        row.Children.Add(new PathIcon
        {
            Data = Geometry.Parse("M 2,2 L 22,2 L 22,22 L 2,22 Z M 7,7 L 17,7 L 17,17 L 7,17 Z"),
            Width = 28,
            Height = 28,
            Margin = new Thickness(0, 0, 16, 0)
        });
        // A round donut: outer + inner circle, EvenOdd — the centre is a hole.
        row.Children.Add(new PathIcon
        {
            Data = Geometry.Parse("M 2,12 A 10,10 0 1 1 22,12 A 10,10 0 1 1 2,12 Z M 7,12 A 5,5 0 1 1 17,12 A 5,5 0 1 1 7,12 Z"),
            Width = 28,
            Height = 28,
            Margin = new Thickness(0, 0, 16, 0)
        });
        // A pentagram (five-point star) drawn as ONE self-intersecting figure
        // with the default EvenOdd rule — the centre pentagon must be a HOLE.
        // Exercises the single-figure self-intersection path (no MoveTo); under
        // Analytic this must render hollow, not as a solid star.
        row.Children.Add(new PathIcon
        {
            Data = Geometry.Parse("M 12,1 L 18.5,20.9 L 1.5,8.6 L 22.5,8.6 L 5.5,20.9 Z"),
            Width = 28,
            Height = 28
        });
        return row;
    }
}
