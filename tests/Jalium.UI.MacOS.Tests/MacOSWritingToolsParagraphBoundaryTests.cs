using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Documents;
using System.Runtime.InteropServices;
using System.Text.Json;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSWritingToolsParagraphBoundaryTests
{
    public static IEnumerable<object[]> Markers()
    {
        yield return [TextMarkerStyle.None, ""];
        yield return [TextMarkerStyle.Disc, "{disc}"];
        yield return [TextMarkerStyle.Circle, "{circle}"];
        yield return [TextMarkerStyle.Square, "{square}"];
        yield return [TextMarkerStyle.Box, "{box}"];
        yield return [TextMarkerStyle.Decimal, "{decimal}"];
        yield return [TextMarkerStyle.LowerLatin, "{lower-alpha}"];
        yield return [TextMarkerStyle.UpperLatin, "{upper-alpha}"];
        yield return [TextMarkerStyle.LowerRoman, "{lower-roman}"];
        yield return [TextMarkerStyle.UpperRoman, "{upper-roman}"];
    }

    [Theory]
    [MemberData(nameof(Markers))]
    public void ExportRetainsEveryListMarker(TextMarkerStyle marker, string native)
    {
        var list = new DocList { MarkerStyle = marker, StartIndex = 4 };
        list.ListItems.Add(new ListItem(new Paragraph(new Run("中文🙂"))));
        var editor = new RichTextBox(new FlowDocument(list));
        Assert.Equal(native, Assert.Single(Assert.Single(editor.GetWritingToolsStructure()).Lists).Format);
    }

    [Theory]
    [MemberData(nameof(Markers))]
    public void ImportRetainsEveryListMarker(TextMarkerStyle marker, string native)
    {
        var packet = Packet("中文🙂\n", null, native);
        var list = Assert.IsType<DocList>(Assert.Single(WritingToolsDocumentFragment.Parse(packet).Blocks));
        Assert.Equal(marker, list.MarkerStyle);
        Assert.Equal(4, list.StartIndex);
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(1, 2)]
    [InlineData(4, 1)]
    [InlineData(5, 3)]
    [InlineData(10, 0)]
    public void ExportMarksOnlyAuthoredBreaksWithinTheRequestedRange(int start, int length)
    {
        const string text = "a\r\nb\nc\u2029def";
        var editor = new RichTextBox(new FlowDocument(new Paragraph(new Run(text))));
        var records = editor.GetWritingToolsStructure(start, length);
        var response = new MacOSTextServicesResponse { Structure = records };
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, MacOSTextServicesJsonContext.Default.MacOSTextServicesResponse));
        var actual = new List<int>();
        foreach (var record in json.RootElement.GetProperty("structure").EnumerateArray())
            if (record.TryGetProperty("inlineBreaks", out var breaks))
                actual.AddRange(breaks.EnumerateArray().Select(value => value.GetInt32()));
        int count = length < 0 ? editor.Document.GetText().Length - start : length;
        var expected = new[] { 1, 2, 4, 6 }.Where(i => i >= start && i < start + count).Select(i => i - start);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    [InlineData("\u2029")]
    public void ImportedTrailingInlineBreakStaysInsideItsParagraph(string ending)
    {
        string text = "文🙂" + ending;
        var packet = Packet(text, Enumerable.Range(3, ending.Length).ToArray());
        var document = new FlowDocument();
        WritingToolsDocumentFragment.Parse(packet).Insert(document, 0, 0, () => true);
        Assert.Single(document.Blocks);
        Assert.Equal(text + "\n", document.GetText());
    }

    [Fact]
    public void AnUnmarkedTrailingBreakRemainsAParagraphTerminator()
    {
        var document = new FlowDocument(new Paragraph(new Run("prefixsuffix")));
        WritingToolsDocumentFragment.Parse(Packet("new\n", null)).Insert(document, 6, 0, () => true);
        Assert.Equal("prefixnew\nsuffix\n", document.GetText());
        Assert.Equal(2, document.Blocks.Count);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    [InlineData("\u2029")]
    public void PartialInlineBreakReplacementMergesTheSurvivingSuffix(string ending)
    {
        string text = "文🙂" + ending;
        var document = new FlowDocument(new Paragraph(new Run("prefixsuffix")));
        WritingToolsDocumentFragment.Parse(Packet(text, Enumerable.Range(3, ending.Length).ToArray()))
            .Insert(document, 6, 0, () => true);
        Assert.Single(document.Blocks);
        Assert.Equal("prefix" + text + "suffix\n", document.GetText());
    }

    public static IEnumerable<object[]> InvalidBreaks()
    {
        yield return [new[] { -1 }];
        yield return [new[] { 9 }];
        yield return [new[] { 0 }];
        yield return [new[] { 1, 1 }];
        yield return [new[] { 3, 1 }];
        yield return [new[] { 1.5 }];
        yield return [new[] { true }];
        yield return ["1"];
    }

    [Theory]
    [MemberData(nameof(InvalidBreaks))]
    public void InvalidInlineBreakMetadataIsRejectedBeforeMutation(object invalid)
    {
        var document = new FlowDocument(new Paragraph(new Run("original")));
        Assert.Throws<FormatException>(() => WritingToolsDocumentFragment.Parse(Packet("a\nb\n", invalid)));
        Assert.Equal("original\n", document.GetText());
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\u2029")]
    public void AuthoredBreakCannotAlsoBeDiscardedAsVirtualStorage(string text)
    {
        Assert.Throws<FormatException>(() => WritingToolsDocumentFragment.Parse(
            Packet(text, Enumerable.Range(0, text.Length).ToArray(), storageSeparator: true)));
    }

    private static JsonElement Packet(string text, object? inlineBreaks, string? marker = null, bool storageSeparator = false)
    {
        var paragraph = new Dictionary<string, object?>
        {
            ["start"] = 0, ["length"] = text.Length, ["alignment"] = 0, ["direction"] = 0,
            ["tables"] = Array.Empty<object>(),
            ["lists"] = marker is null ? Array.Empty<object>() : [new { id = "list", format = marker, start = 4 }]
        };
        if (inlineBreaks is not null) paragraph["inlineBreaks"] = inlineBreaks;
        if (storageSeparator) paragraph["storageSeparator"] = true;
        return JsonSerializer.SerializeToElement(new
        {
            version = 1, text16 = Convert.ToBase64String(MemoryMarshal.AsBytes(text.AsSpan())),
            runs = new[] { new { start = 0, length = text.Length, family = "Helvetica Neue", size = 18, weight = 400, italic = false, underline = 0, strike = 0 } },
            paragraphs = new[] { paragraph }
        });
    }
}
