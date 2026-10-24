using System.Text.Json;
using System.Text.Json.Nodes;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Automation.Text;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class WritingToolsBackgroundTests
{
    public static IEnumerable<object[]> BackgroundCases()
    {
        yield return [0u, 1d, 0u];
        yield return [0x00112233u, 1d, 0x00112233u];
        yield return [0x01123456u, 1d, 0x01123456u];
        yield return [0x7f123456u, 1d, 0x7f123456u];
        yield return [0xffabcdefu, 1d, 0xffabcdefu];
        yield return [0xff223344u, .5d, 0x80223344u];
        yield return [0x80223344u, .5d, 0x40223344u];
        yield return [0xff556677u, 0d, 0x00556677u];
        yield return [0xffffffffu, 1d, 0xffffffffu];
        yield return [0xff000000u, .25d, 0x40000000u];
    }

    [Theory, MemberData(nameof(BackgroundCases))]
    public void SnapshotPreservesTheDirectRunBrushArgbAndOpacity(uint color, double opacity, uint expected)
    {
        var run = new Run("Miii中文🙂e\u0301") { Background = Brush(color, opacity), Foreground = Brush(0xff102030) };
        var editor = new RichTextBox(new FlowDocument(new Paragraph(run)));
        using var snapshot = JsonDocument.Parse(Snapshot(editor));
        var first = snapshot.RootElement.GetProperty("runs")[0];
        Assert.Equal(expected, first.GetProperty("background").GetUInt32());
        Assert.Equal(0xff102030u, first.GetProperty("foreground").GetUInt32());
        Assert.Equal(Bgr(expected), Assert.IsType<int>(Provider(editor).CreateRange(0, run.Text.Length).GetAttributeValue(40001)));
    }

    [Theory, MemberData(nameof(BackgroundCases))]
    public void AnUnchangedAttributedRoundTripKeepsTheRunBackground(uint color, double opacity, uint expected)
    {
        var editor = new RichTextBox(new FlowDocument(new Paragraph(new Run("汉🙂Miii") { Background = Brush(color, opacity) })));
        var proposal = JsonNode.Parse(Snapshot(editor))!.AsObject();
        foreach (var item in proposal["runs"]!.AsArray())
            item!["italic"] = item["italic"]!.GetValue<int>() != 0;
        proposal["paragraphs"] = new JsonArray(new JsonObject
        {
            ["start"] = 0, ["length"] = editor.Document.GetText().Length,
            ["alignment"] = 0, ["direction"] = -1, ["lists"] = new JsonArray(), ["tables"] = new JsonArray(),
        });
        using var packet = JsonDocument.Parse(proposal.ToJsonString());
        WritingToolsDocumentFragment.Parse(packet.RootElement).Insert(editor.Document, 0, editor.Document.GetText().Length, () => true);
        var run = editor.Document.EnumerateParagraphs().Single().Inlines.OfType<Run>().First();
        var brush = Assert.IsType<SolidColorBrush>(run.Background);
        Assert.Equal(Argb(expected), brush.Color);
        Assert.Equal("汉🙂Miii\n", editor.Document.GetText());
        using var result = JsonDocument.Parse(Snapshot(editor));
        Assert.Equal(expected, result.RootElement.GetProperty("runs")[0].GetProperty("background").GetUInt32());
    }

    [Fact]
    public void AdjacentBackgroundsKeepUtf16RangesFormatUnitsAndAttributeSearch()
    {
        var first = new Run("汉🙂") { Background = Brush(0xff112233) };
        var second = new Run("日本語") { Background = Brush(0xff445566) };
        var paragraph = new Paragraph(first); paragraph.Inlines.Add(second);
        var editor = new RichTextBox(new FlowDocument(paragraph));
        using var snapshot = JsonDocument.Parse(Snapshot(editor));
        var runs = snapshot.RootElement.GetProperty("runs").EnumerateArray().ToArray();
        Assert.Contains(runs, x => x.GetProperty("start").GetInt32() == 0 && x.GetProperty("length").GetInt32() == 3 && x.GetProperty("background").GetUInt32() == 0xff112233u);
        Assert.Contains(runs, x => x.GetProperty("start").GetInt32() == 3 && x.GetProperty("length").GetInt32() == 3 && x.GetProperty("background").GetUInt32() == 0xff445566u);
        var provider = Provider(editor); var range = provider.CreateRange(0, 6);
        Assert.Same(AutomationTextAttributeValues.Mixed, range.GetAttributeValue(40001));
        Assert.Equal("汉🙂", range.FindAttribute(40001, 0x332211, false)!.GetText(-1));
        Assert.Equal("日本語", range.FindAttribute(40001, 0x665544, true)!.GetText(-1));
        var unit = provider.CreateRange(1, 1); unit.ExpandToEnclosingUnit(TextUnit.Format);
        Assert.Equal("汉🙂", unit.GetText(-1));
        unit = provider.CreateRange(4, 4); unit.ExpandToEnclosingUnit(TextUnit.Format);
        Assert.Equal("日本語", unit.GetText(-1));
    }

    [Theory]
    [InlineData("none")] [InlineData("editor")] [InlineData("document")] [InlineData("gradient")]
    public void MissingAndUnrepresentableRunColorsAreNotInvented(string kind)
    {
        var run = new Run("Miii中文🙂"); var document = new FlowDocument(new Paragraph(run));
        var editor = new RichTextBox(document);
        if (kind == "editor") editor.Background = Brushes.Red;
        if (kind == "document") document.Background = Brushes.Blue;
        if (kind == "gradient") run.Background = new LinearGradientBrush(Colors.Red, Colors.Blue, 0);
        using var snapshot = JsonDocument.Parse(Snapshot(editor));
        Assert.All(snapshot.RootElement.GetProperty("runs").EnumerateArray(), x => Assert.False(x.TryGetProperty("background", out _)));
        Assert.Null(Provider(editor).CreateRange(0, run.Text.Length).GetAttributeValue(40001));
    }

    [Fact]
    public void MutatingTheRunBrushRefreshesTheSnapshotWithoutChangingTextOrSelection()
    {
        var brush = Brush(0xff123456); var run = new Run("Miii中文🙂"){ Background = brush };
        var editor = new RichTextBox(new FlowDocument(new Paragraph(run)));
        editor.Selection.Select(run.ContentStart.GetPositionAtOffset(1)!, run.ContentStart.GetPositionAtOffset(4)!);
        int start = editor.Selection.Start.DocumentOffset, end = editor.Selection.End.DocumentOffset;
        string text = editor.Document.GetText();
        using (var before = JsonDocument.Parse(Snapshot(editor)))
            Assert.Equal(0xff123456u, before.RootElement.GetProperty("runs")[0].GetProperty("background").GetUInt32());
        brush.Opacity = .5;
        using (var after = JsonDocument.Parse(Snapshot(editor)))
            Assert.Equal(0x80123456u, after.RootElement.GetProperty("runs")[0].GetProperty("background").GetUInt32());
        Assert.Equal(text, editor.Document.GetText());
        Assert.Equal(start, editor.Selection.Start.DocumentOffset); Assert.Equal(end, editor.Selection.End.DocumentOffset);
    }

    private static AutomationTextProvider Provider(RichTextBox editor) =>
        (AutomationTextProvider)UIElementAutomationPeer.CreatePeerForElement(editor)!.GetPattern(PatternInterface.Text)!;
    private static string Snapshot(RichTextBox editor)
    {
        var source = Provider(editor).Source;
        return MacOSAccessibilityTree.CreateTextStyleSnapshot(source, 0, source.Text.Length, () => true)!;
    }
    private static Color Argb(uint n) => Color.FromArgb((byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n);
    private static SolidColorBrush Brush(uint n, double opacity = 1) => new(Argb(n)){ Opacity = opacity };
    private static int Bgr(uint n) => (int)((n & 255) << 16 | (n & 0xff00) | (n >> 16 & 255));
}
