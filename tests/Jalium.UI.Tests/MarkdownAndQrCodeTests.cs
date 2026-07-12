using System.Reflection;
using System.Linq;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Editor;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class MarkdownAndQrCodeTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void Markdown_And_QRCode_ShouldBeParsableFromXaml()
    {
        const string xaml = """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Markdown BaseUri="https://example.com/docs/">
            # Heading

            This is **bold** text.
                </Markdown>
                <QRCode Text="https://example.com" QuietZoneModules="2" />
            </StackPanel>
            """;

        var panel = Assert.IsType<StackPanel>(XamlReader.Parse(xaml));

        var markdown = Assert.IsType<Markdown>(panel.Children[0]);
        var qrCode = Assert.IsType<QRCode>(panel.Children[1]);

        Assert.Contains("Heading", markdown.Text, StringComparison.Ordinal);
        Assert.Equal("https://example.com", qrCode.Text);
        Assert.Equal(2, qrCode.QuietZoneModules);
    }

    [Fact]
    public void MarkdownParser_ShouldBuildNativeBlocks_AndResolveRelativeLinks()
    {
        var blocks = MarkdownParser.Parse(
            """
            # Heading

            This is **bold** text with a [guide](guide.md) link.

            - [x] Implement parser
            - Ship gallery sample

            | Feature | Status |
            | --- | --- |
            | Native renderer | Ready |
            """,
            new Uri("https://example.com/docs/"));

        Assert.Collection(blocks,
            block =>
            {
                var heading = Assert.IsType<MarkdownHeadingBlock>(block);
                Assert.Equal(1, heading.Level);
            },
            block =>
            {
                var paragraph = Assert.IsType<MarkdownParagraphBlock>(block);
                Assert.Contains(paragraph.Inlines, inline => inline is MarkdownStrongInline);
                var link = Assert.IsType<MarkdownLinkInline>(paragraph.Inlines.OfType<MarkdownLinkInline>().Single());
                Assert.Equal("https://example.com/docs/guide.md", link.Uri?.AbsoluteUri);
            },
            block =>
            {
                var list = Assert.IsType<MarkdownListBlock>(block);
                Assert.Equal(2, list.Items.Count);
                Assert.True(list.Items[0].TaskState);
            },
            block =>
            {
                var table = Assert.IsType<MarkdownTableBlock>(block);
                Assert.Single(table.HeaderRows);
                Assert.Single(table.Rows);
            });
    }

    [Fact]
    public void Markdown_Control_ShouldRenderNativeVisualTreeWithoutWebView()
    {
        ResetApplicationState();
        _ = new Application();

        try
        {
            var markdown = new Markdown
            {
                Text = "# Native Markdown\n\nParagraph with a [link](https://example.com)."
            };

            var host = new StackPanel { Width = 480, Height = 240 };
            host.Children.Add(markdown);
            host.Measure(new Size(480, 240));
            host.Arrange(new Rect(0, 0, 480, 240));

            Assert.True(markdown.DebugBlocks.Count >= 2);
            Assert.True(ContainsVisualOfType<ScrollViewer>(markdown));
            Assert.False(ContainsVisualOfType<WebView>(markdown));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void MarkdownCodeBlockView_ShouldHighlightXaml_CSharp_AndGeneric_WithLineNumbers()
    {
        var xamlView = new MarkdownCodeBlockView
        {
            Language = "xaml",
            Text = "<Grid Width=\"120\" />"
        };
        Assert.Single(xamlView.DebugLines);
        Assert.Contains(xamlView.DebugLines[0].Tokens, token =>
            token.Classification == TokenClassification.TypeName ||
            token.Classification == TokenClassification.Property);

        var csharpView = new MarkdownCodeBlockView
        {
            Language = "csharp",
            Text = "public sealed class Demo { }"
        };
        Assert.Contains(csharpView.DebugLines[0].Tokens, token => token.Classification == TokenClassification.Keyword);

        var genericView = new MarkdownCodeBlockView
        {
            Language = "shell",
            Text = "if value == 1"
        };
        Assert.Contains(genericView.DebugLines[0].Tokens, token =>
            token.Classification == TokenClassification.Keyword ||
            token.Classification == TokenClassification.Operator ||
            token.Classification == TokenClassification.Number);

        var shortView = new MarkdownCodeBlockView { Text = "first\nsecond" };
        shortView.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        var longView = new MarkdownCodeBlockView
        {
            Text = string.Join("\n", Enumerable.Range(1, 120).Select(index => $"line {index}"))
        };
        longView.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        Assert.True(longView.DebugGutterWidth > shortView.DebugGutterWidth);
    }

    [Fact]
    public void Markdown_Control_ShouldUseSyntaxHighlightedCodeBlockView_ForFencedBlocks()
    {
        ResetApplicationState();
        _ = new Application();

        try
        {
            var markdown = new Markdown
            {
                Text = """
                ```xaml
                <Grid Width="120" />
                ```
                """
            };

            var host = new StackPanel { Width = 480, Height = 240 };
            host.Children.Add(markdown);
            host.Measure(new Size(480, 240));
            host.Arrange(new Rect(0, 0, 480, 240));

            Assert.True(ContainsVisualOfType<MarkdownCodeBlockView>(markdown));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void Markdown_UnorderedLists_ShouldRenderBulletDotsInsteadOfAsteriskMarkers()
    {
        ResetApplicationState();
        _ = new Application();

        try
        {
            var markdown = new Markdown
            {
                Text = """
                - First item
                - Second item
                """
            };

            var host = new StackPanel { Width = 320, Height = 200 };
            host.Children.Add(markdown);
            host.Measure(new Size(320, 200));
            host.Arrange(new Rect(0, 0, 320, 200));

            Assert.True(ContainsVisualOfType<Border>(markdown));
            Assert.DoesNotContain(GetAllTextBlocks(markdown), text => text == "*");
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void Markdown_Level1AndLevel2Heading_ShouldRenderUnderlineSeparator()
    {
        ResetApplicationState();
        _ = new Application();

        try
        {
            var markdownLevel1 = new Markdown
            {
                Text = """
                # Primary Section
                """
            };

            var markdownLevel2 = new Markdown
            {
                Text = """
                ## Sample Section
                """
            };

            var host = new StackPanel { Width = 320, Height = 160 };
            host.Children.Add(markdownLevel1);
            host.Children.Add(markdownLevel2);
            host.Measure(new Size(320, 160));
            host.Arrange(new Rect(0, 0, 320, 160));

            Assert.True(ContainsBorderWithHeight(markdownLevel1, 1));
            Assert.True(ContainsBorderWithHeight(markdownLevel2, 1));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void QRCode_Render_ShouldDrawForegroundModules()
    {
        var foreground = new SolidColorBrush(Color.FromRgb(12, 34, 56));
        var qrCode = new QRCode
        {
            Text = "https://jalium.dev",
            Foreground = foreground,
            Background = new SolidColorBrush(Color.White),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Width = 160,
            Height = 160
        };

        qrCode.Measure(new Size(160, 160));
        qrCode.Arrange(new Rect(0, 0, 160, 160));

        var drawingContext = new RecordingDrawingContext(foreground);
        qrCode.Render(drawingContext);

        Assert.True(qrCode.ModuleCount > 0);
        Assert.True(drawingContext.ForegroundRectangleCount > 0);
    }

    [Fact]
    public void Markdown_And_QRCode_ImplicitThemeStyles_ShouldApplyWithoutLocalOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var markdown = new Markdown();
            var qrCode = new QRCode { Text = "https://example.com" };

            var host = new StackPanel { Width = 480, Height = 360 };
            host.Children.Add(markdown);
            host.Children.Add(qrCode);

            host.Measure(new Size(480, 360));
            host.Arrange(new Rect(0, 0, 480, 360));

            Assert.True(app.Resources.TryGetValue(typeof(Markdown), out var markdownStyle));
            Assert.True(app.Resources.TryGetValue(typeof(QRCode), out var qrStyle));
            Assert.IsType<Style>(markdownStyle);
            Assert.IsType<Style>(qrStyle);

            Assert.False(markdown.HasLocalValue(Control.BackgroundProperty));
            Assert.False(markdown.HasLocalValue(Control.PaddingProperty));
            Assert.Equal(120, markdown.MinHeight);

            Assert.False(qrCode.HasLocalValue(Control.BackgroundProperty));
            Assert.False(qrCode.HasLocalValue(Control.ForegroundProperty));
            Assert.Equal(96, qrCode.MinWidth);
            Assert.Equal(96, qrCode.MinHeight);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private sealed class RecordingDrawingContext : DrawingContextAdapter
    {
        private readonly Brush _trackedBrush;

        public RecordingDrawingContext(Brush trackedBrush)
        {
            _trackedBrush = trackedBrush;
        }

        public int ForegroundRectangleCount { get; private set; }

        public override void DrawLine(Pen pen, Point point0, Point point1)
        {
        }

        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
        {
            if (ReferenceEquals(brush, _trackedBrush))
            {
                ForegroundRectangleCount++;
            }
        }

        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY)
        {
        }

        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY)
        {
        }

        public override void DrawText(FormattedText formattedText, Point origin)
        {
        }

        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
        {
        }

        public override void DrawImage(ImageSource imageSource, Rect rectangle)
        {
        }

        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius)
        {
        }

        public override void PushTransform(Transform transform)
        {
        }

        public override void PushClip(Geometry clipGeometry)
        {
        }

        public override void PushOpacity(double opacity)
        {
        }

        public override void Pop()
        {
        }

        public override void Close()
        {
        }
    }

    private static bool ContainsVisualOfType<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T)
        {
            return true;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child != null && ContainsVisualOfType<T>(child))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> GetAllTextBlocks(DependencyObject root)
    {
        var texts = new List<string>();
        CollectTextBlocks(root, texts);
        return texts;
    }

    private static void CollectTextBlocks(DependencyObject root, List<string> texts)
    {
        if (root is TextBlock textBlock)
        {
            texts.Add(textBlock.Text);
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child != null)
            {
                CollectTextBlocks(child, texts);
            }
        }
    }

    private static bool ContainsBorderWithHeight(DependencyObject root, double height)
    {
        if (root is Border border && Math.Abs(border.Height - height) < 0.01)
        {
            return true;
        }

        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child != null && ContainsBorderWithHeight(child, height))
            {
                return true;
            }
        }

        return false;
    }
}
