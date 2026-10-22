using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class WritingToolsDecorationColorTests
{
    private const string Text = "Miii中文🙂e\u0301";

    public static IEnumerable<object[]> Colors()
    {
        foreach (string kind in new[] { "underline", "strike", "both" })
        foreach (uint color in new uint[] { 0, 0x00ff00ff, 0x01010203, 0x7f123456, 0xff010203, 0xffffffff, 0xff000000, 0x80ef3012 })
            yield return [kind, color];
    }

    [Theory, MemberData(nameof(Colors))]
    public void AttributedReplacementPreservesIndependentDecorationArgb(string kind, uint color)
    {
        var packet = Packet(kind); var style = packet["runs"]![0]!.AsObject();
        uint strike = color ^ 0x00ffffff;
        if (kind != "strike") style["underlineColor"] = JsonValue.Create(color);
        if (kind != "underline") style["strikeColor"] = JsonValue.Create(strike);
        var (editor, run) = Insert(packet);
        Assert.Equal(kind == "both" ? 2 : 1, run.TextDecorations!.Count);
        void Check(TextDecorationLocation location, uint expected)
        {
            var decoration = Assert.Single(run.TextDecorations.Where(item => item.Location == location));
            var brush = Assert.IsType<SolidColorBrush>(decoration.Pen?.Brush);
            Assert.Equal(Color.FromArgb((byte)(expected >> 24), (byte)(expected >> 16), (byte)(expected >> 8), (byte)expected), brush.Color);
        }
        if (kind != "strike") Check(TextDecorationLocation.Underline, color);
        if (kind != "underline") Check(TextDecorationLocation.Strikethrough, strike);
        var exported = editor.GetAutomationTextStyles().First().Style;
        Assert.Equal(kind != "strike" ? (uint?)color : null, exported.UnderlineColor);
        Assert.Equal(kind != "underline" ? (uint?)strike : null, exported.StrikethroughColor);
        Assert.Equal("Miii中文🙂e\u0301\n", editor.Document.GetText());
        Assert.Equal(0xff223344u, exported.Foreground);
    }

    [Theory]
    [InlineData("underline", false)] [InlineData("underline", true)]
    [InlineData("strike", false)] [InlineData("strike", true)]
    [InlineData("both", false)] [InlineData("both", true)]
    public void MissingOrNullColorsKeepTheDefaultDecorationBrush(string kind, bool explicitNull)
    {
        var packet = Packet(kind);
        if (explicitNull)
        {
            packet["runs"]![0]!["underlineColor"] = null;
            packet["runs"]![0]!["strikeColor"] = null;
        }
        var (_, run) = Insert(packet);
        Assert.All(run.TextDecorations!, decoration => Assert.Null(decoration.Pen));
    }

    public static IEnumerable<object[]> InvalidColors()
    {
        foreach (string field in new[] { "underlineColor", "strikeColor" })
        foreach (string json in new[] { "-1", "4294967296", "\"red\"", "true", "{}", "[]" })
            yield return [field, json];
    }

    [Theory, MemberData(nameof(InvalidColors))]
    public void InvalidDecorationColorsAreRejectedBeforeTheDocumentChanges(string field, string json)
    {
        var original = new Paragraph(new Run("keep")); var document = new FlowDocument(original);
        var packet = Packet("both"); packet["runs"]![0]![field] = JsonNode.Parse(json);
        Assert.Throws<FormatException>(() =>
        {
            using var data = JsonDocument.Parse(packet.ToJsonString());
            var fragment = WritingToolsDocumentFragment.Parse(data.RootElement);
            fragment.Insert(document, 0, document.GetText().Length, () => true);
        });
        Assert.Equal("keep\n", document.GetText());
        Assert.Same(original, Assert.Single(document.Blocks));
    }

    [Theory]
    [InlineData("underlineColor")] [InlineData("strikeColor")]
    public void AColorAloneDoesNotEnableADecoration(string field)
    {
        var packet = Packet("none"); packet["runs"]![0]![field] = JsonValue.Create(0xffff0000u);
        var (_, run) = Insert(packet);
        Assert.True(run.TextDecorations is null || run.TextDecorations.Count == 0);
    }

    private static (RichTextBox Editor, Run Run) Insert(JsonObject packet)
    {
        using var data = JsonDocument.Parse(packet.ToJsonString());
        var fragment = WritingToolsDocumentFragment.Parse(data.RootElement);
        var document = new FlowDocument(new Paragraph(new Run("old")));
        fragment.Insert(document, 0, document.GetText().Length, () => true);
        var run = Assert.Single(Assert.IsType<Paragraph>(Assert.Single(document.Blocks)).Inlines.OfType<Run>());
        return (new RichTextBox(document), run);
    }

    private static JsonObject Packet(string kind)
    {
        string text = Text + "\n";
        return new JsonObject
        {
            ["version"] = 1,
            ["text16"] = Convert.ToBase64String(MemoryMarshal.AsBytes(text.AsSpan())),
            ["runs"] = new JsonArray(new JsonObject
            {
                ["start"] = 0, ["length"] = text.Length, ["family"] = "Helvetica Neue", ["size"] = 23,
                ["weight"] = 700, ["italic"] = true, ["foreground"] = JsonValue.Create(0xff223344u),
                ["underline"] = kind is "underline" or "both" ? 1 : 0,
                ["strike"] = kind is "strike" or "both" ? 1 : 0,
            }),
            ["paragraphs"] = new JsonArray(new JsonObject
            {
                ["start"] = 0, ["length"] = text.Length, ["alignment"] = 0, ["direction"] = 0,
                ["lists"] = new JsonArray(), ["tables"] = new JsonArray(),
            }),
        };
    }
}
