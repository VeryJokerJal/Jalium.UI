using System.Linq;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class MarkdownCopyTests
{
    private const string Sample = """
        # Heading

        This is **bold** and *italic* text with a [link](https://example.com).

        - First item
        - Second item

        ```csharp
        var x = 1;
        ```
        """;

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
    public void Serializer_PlainText_StripsMarkers()
    {
        var blocks = MarkdownParser.Parse(Sample, null);
        var plain = MarkdownSerializer.ToPlainText(blocks);

        Assert.Contains("Heading", plain, StringComparison.Ordinal);
        Assert.Contains("bold", plain, StringComparison.Ordinal);
        Assert.Contains("italic", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("**", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("# ", plain, StringComparison.Ordinal);
        // The link text is kept, the URL/markup is dropped.
        Assert.Contains("link", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("](https", plain, StringComparison.Ordinal);
    }

    [Fact]
    public void Serializer_Html_EmitsTags()
    {
        var blocks = MarkdownParser.Parse(Sample, null);
        var html = MarkdownSerializer.ToHtmlDocument(blocks);

        Assert.Contains("<h1>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>bold</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<em>italic</em>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://example.com\">link</a>", html, StringComparison.Ordinal);
        Assert.Contains("<ul>", html, StringComparison.Ordinal);
        Assert.Contains("<pre><code", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Serializer_Rtf_EmitsDocument()
    {
        var blocks = MarkdownParser.Parse(Sample, null);
        var rtf = MarkdownSerializer.ToRtf(blocks);

        Assert.StartsWith(@"{\rtf1", rtf, StringComparison.Ordinal);
        Assert.EndsWith("}", rtf, StringComparison.Ordinal);
        Assert.Contains(@"\b ", rtf, StringComparison.Ordinal);
    }

    [Fact]
    public void Serializer_Rtf_EscapesUnicode()
    {
        var blocks = MarkdownParser.Parse("中文测试", null);
        var rtf = MarkdownSerializer.ToRtf(blocks);

        // CJK characters must be escaped as \uN? rather than emitted raw.
        Assert.Contains(@"\u", rtf, StringComparison.Ordinal);
        Assert.DoesNotContain("中", rtf, StringComparison.Ordinal);
    }

    [Fact]
    public void Serializer_Rtf_HandlesAstralCharacters()
    {
        // Emoji are UTF-16 surrogate pairs; they must be escaped, not emitted raw.
        var blocks = MarkdownParser.Parse("emoji 😀 done", null);
        var rtf = MarkdownSerializer.ToRtf(blocks);

        Assert.Contains(@"\u", rtf, StringComparison.Ordinal);
        Assert.DoesNotContain("😀", rtf, StringComparison.Ordinal);
    }

    [Fact]
    public void Serializer_Markdown_PreservesMarkers()
    {
        var blocks = MarkdownParser.Parse("# Title\n\nSome **bold** words.", null);
        var markdown = MarkdownSerializer.ToMarkdown(blocks);

        Assert.Contains("# Title", markdown, StringComparison.Ordinal);
        Assert.Contains("**bold**", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Clipboard_BuildCfHtml_ProducesValidOffsets()
    {
        const string fragment = "<p>Hello 世界</p>";
        var cf = Clipboard.BuildCfHtml(fragment);

        Assert.Contains("Version:0.9", cf, StringComparison.Ordinal);
        Assert.Contains("<!--StartFragment-->" + fragment + "<!--EndFragment-->", cf, StringComparison.Ordinal);

        var startFragment = ParseOffset(cf, "StartFragment:");
        var endFragment = ParseOffset(cf, "EndFragment:");
        var utf8 = System.Text.Encoding.UTF8.GetBytes(cf);

        // The bytes between the StartFragment/EndFragment offsets must equal the fragment.
        var sliced = System.Text.Encoding.UTF8.GetString(utf8, startFragment, endFragment - startFragment);
        Assert.Equal(fragment, sliced);
    }

    [Fact]
    public void Markdown_ExtractionApi_Works()
    {
        ResetApplicationState();
        _ = new Application();
        try
        {
            var markdown = new Markdown { Text = Sample };

            Assert.Contains("Heading", markdown.GetPlainText(), StringComparison.Ordinal);
            Assert.DoesNotContain("**", markdown.GetPlainText(), StringComparison.Ordinal);
            Assert.Equal(Sample, markdown.GetMarkdownText());
            Assert.Contains("<strong>bold</strong>", markdown.GetHtml(), StringComparison.Ordinal);
            Assert.StartsWith(@"{\rtf1", markdown.GetRtf(), StringComparison.Ordinal);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void Markdown_SelectAll_SelectsRenderedText()
    {
        ResetApplicationState();
        _ = new Application();
        try
        {
            var markdown = new Markdown { Text = Sample };
            var host = new StackPanel { Width = 480, Height = 360 };
            host.Children.Add(markdown);
            host.Measure(new Size(480, 360));
            host.Arrange(new Rect(0, 0, 480, 360));

            Assert.False(markdown.HasSelection);
            markdown.SelectAll();

            Assert.True(markdown.HasSelection);
            var selected = markdown.SelectedText;
            Assert.Contains("Heading", selected, StringComparison.Ordinal);
            Assert.Contains("bold", selected, StringComparison.Ordinal);
            Assert.Contains("First item", selected, StringComparison.Ordinal);

            markdown.ClearSelection();
            Assert.False(markdown.HasSelection);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void Markdown_SelectionDisabled_DoesNotSelect()
    {
        ResetApplicationState();
        _ = new Application();
        try
        {
            var markdown = new Markdown { Text = Sample, IsTextSelectionEnabled = false };
            var host = new StackPanel { Width = 480, Height = 360 };
            host.Children.Add(markdown);
            host.Measure(new Size(480, 360));
            host.Arrange(new Rect(0, 0, 480, 360));

            markdown.SelectAll();
            Assert.False(markdown.HasSelection);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static int ParseOffset(string cfHtml, string key)
    {
        var index = cfHtml.IndexOf(key, StringComparison.Ordinal) + key.Length;
        var end = cfHtml.IndexOf('\r', index);
        return int.Parse(cfHtml.Substring(index, end - index));
    }
}
