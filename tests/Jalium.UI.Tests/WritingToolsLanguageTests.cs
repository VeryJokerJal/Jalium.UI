using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jalium.UI.Automation;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class WritingToolsLanguageTests
{
    [Theory]
    [InlineData("editor", "zh-Hans")]
    [InlineData("document", "ja-JP")]
    [InlineData("section", "ar")]
    [InlineData("paragraph", "th")]
    [InlineData("span", "sr-Latn")]
    [InlineData("run", "x-jalium-test")]
    public void SnapshotUsesTheEffectiveLanguageAtEachRun(string level, string tag)
    {
        var run = new Run("Miii中文🙂"); var span = new Span(); span.Inlines.Add(run);
        var paragraph = new Paragraph(span); var section = new Section(); section.Blocks.Add(paragraph);
        var document = new FlowDocument(section); var editor = new RichTextBox(document) { Language = Lang("en-GB") };
        switch (level)
        {
            case "editor": editor.Language = Lang(tag); break;
            case "document": document.Language = Lang(tag); break;
            case "section": section.Language = Lang(tag); break;
            case "paragraph": paragraph.Language = Lang(tag); break;
            case "span": span.Language = Lang(tag); break;
            case "run": run.Language = Lang(tag); break;
        }
        AssertLanguage(editor, run.ContentStart.DocumentOffset, tag);
    }

    [Fact]
    public void SnapshotRetainsAdjacentLanguagesAndTheirUtf16Ranges()
    {
        var first = new Run("汉🙂") { Language = Lang("zh-Hans") };
        var second = new Run("日本語") { Language = Lang("ja-JP") };
        var paragraph = new Paragraph(first); paragraph.Inlines.Add(second);
        var editor = new RichTextBox(new FlowDocument(paragraph)) { Language = Lang("en-GB") };
        var spans = editor.GetAutomationTextStyles();
        Assert.Contains(spans, item => item.Start == 0 && item.Length == first.Text.Length && item.Style.Language == "zh-hans");
        Assert.Contains(spans, item => item.Start == first.Text.Length && item.Length == second.Text.Length && item.Style.Language == "ja-jp");
        var provider = (AutomationTextProvider)UIElementAutomationPeer.CreatePeerForElement(editor)!.GetPattern(PatternInterface.Text)!;
        string payload = MacOSAccessibilityTree.CreateTextStyleSnapshot(provider.Source, 0, provider.Source.Text.Length, () => true)!;
        using var packet = JsonDocument.Parse(payload);
        var runs = packet.RootElement.GetProperty("runs").EnumerateArray().ToArray();
        Assert.Contains(runs, item => item.GetProperty("start").GetInt32() == 0 && item.GetProperty("length").GetInt32() == 3 && item.GetProperty("language").GetString() == "zh-hans");
        Assert.Contains(runs, item => item.GetProperty("start").GetInt32() == 3 && item.GetProperty("length").GetInt32() == 3 && item.GetProperty("language").GetString() == "ja-jp");
    }

    [Theory]
    [InlineData("line-break")]
    [InlineData("inline-object")]
    [InlineData("block-object")]
    public void StructuralSlotsUseTheLanguageOfTheirContentElement(string kind)
    {
        var document = new FlowDocument(); TextElement target;
        if (kind == "block-object")
        {
            var block = new BlockUIContainer { Language = Lang("ar") };
            document.Blocks.Add(block); target = block;
        }
        else
        {
            Inline inline = kind == "line-break" ? new LineBreak() : new InlineUIContainer();
            var span = new Span { Language = Lang("ar") }; span.Inlines.Add(inline);
            document.Blocks.Add(new Paragraph(span)); target = inline;
        }
        var editor = new RichTextBox(document) { Language = Lang("en-GB") };
        AssertLanguage(editor, target.ContentStart.DocumentOffset, "ar");
    }

    [Theory]
    [InlineData("en-GB")] [InlineData("zh-Hans")] [InlineData("ja-JP")]
    [InlineData("ar")] [InlineData("th")] [InlineData("sr-Latn")]
    [InlineData("x-jalium-test")] [InlineData("")]
    public void ImportedRunLanguageSurvivesInsertionAndSnapshot(string tag)
    {
        var packet = Packet(); packet["runs"]![0]!["language"] = tag;
        var (editor, run) = Insert(packet);
        Assert.Same(Lang(tag), run.Language);
        Assert.Same(Lang(tag), run.ReadLocalValue(FrameworkContentElement.LanguageProperty));
        AssertLanguage(editor, 0, tag);
        Assert.Equal("Miii中文🙂\n", editor.Document.GetText());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void AbsentAndNullLanguageKeepInheritance(bool explicitNull)
    {
        var packet = Packet();
        if (explicitNull) packet["runs"]![0]!["language"] = null;
        var (editor, run) = Insert(packet);
        Assert.Same(DependencyProperty.UnsetValue, run.ReadLocalValue(FrameworkContentElement.LanguageProperty));
        AssertLanguage(editor, 0, "fr-FR");
        editor.Language = Lang("tr-TR");
        AssertLanguage(editor, 0, "tr-TR");
    }

    public static IEnumerable<object[]> InvalidLanguages()
    {
        foreach (string json in new[] { "123", "true", "{}", "[]", "\"en--US\"", "\"en_US\"", "\"en US\"", "\"中文\"", "\"-en\"", "\"en-\"" })
            yield return [json];
        yield return [JsonSerializer.Serialize(new string('a', 1025))];
    }

    [Theory, MemberData(nameof(InvalidLanguages))]
    public void InvalidLanguageIsRejectedBeforeTheDocumentChanges(string json)
    {
        var paragraph = new Paragraph(new Run("keep")); var document = new FlowDocument(paragraph);
        var packet = Packet(); packet["runs"]![0]!["language"] = JsonNode.Parse(json);
        Assert.Throws<FormatException>(() =>
        {
            using var data = JsonDocument.Parse(packet.ToJsonString());
            WritingToolsDocumentFragment.Parse(data.RootElement).Insert(document, 0, document.GetText().Length, () => true);
        });
        Assert.Equal("keep\n", document.GetText()); Assert.Same(paragraph, Assert.Single(document.Blocks));
    }

    [Fact]
    public void NativePopupLanguageFollowsTheCurrentOwnerAndChildOverrides()
    {
        var owner = new Window { Language = Lang("zh-Hans") };
        var nextOwner = new Window { Language = Lang("ar") };
        var editor = new RichTextBox(new FlowDocument(new Paragraph(new Run("汉🙂"))));
        var popup = new Popup(); var root = new PopupRoot(popup, editor, isLightDismiss: false);
        var host = new MacOSPopupWindowHost(owner, popup, root);
        try
        {
            Assert.Equal(nint.Zero, host.Handle);
            Assert.Same(owner.Language, host.Language); AssertLanguage(editor, 0, "zh-Hans");
            owner.Language = Lang("ja-JP");
            Assert.Same(owner.Language, host.Language); AssertLanguage(editor, 0, "ja-JP");
            editor.Language = Lang("th"); owner.Language = Lang("tr-TR");
            AssertLanguage(editor, 0, "th");
            editor.ClearValue(FrameworkElement.LanguageProperty); AssertLanguage(editor, 0, "tr-TR");
            host.BindOwnerTextProperties(nextOwner); owner.Language = Lang("sr-Latn");
            Assert.Same(nextOwner.Language, host.Language); AssertLanguage(editor, 0, "ar");
            nextOwner.Language = Lang("zh-Hant"); AssertLanguage(editor, 0, "zh-Hant");
            Assert.Equal(nint.Zero, host.Handle);
        }
        finally { host.Close(); owner.Close(); nextOwner.Close(); }
    }

    [Fact]
    public void DocumentSnapshotsRestoreLanguageWithoutReplacingTheRun()
    {
        var run = new Run("汉🙂") { Language = Lang("zh-Hans") };
        var document = new FlowDocument(new Paragraph(run)); var editor = new RichTextBox(document);
        var snapshot = DocumentSnapshot.Capture(document); run.Language = Lang("ja-JP");
        snapshot.Restore(document);
        Assert.Same(run, document.EnumerateParagraphs().Single().Inlines.Single()); AssertLanguage(editor, 0, "zh-Hans");
    }

    private static XmlLanguage Lang(string tag) => XmlLanguage.GetLanguage(tag);
    private static void AssertLanguage(RichTextBox editor, int offset, string expected)
    {
        var style = Assert.Single(editor.GetAutomationTextStyles().Where(span => span.Start <= offset && span.Start + span.Length > offset));
        Assert.Equal(Lang(expected).IetfLanguageTag, style.Style.Language);
    }
    private static (RichTextBox Editor, Run Run) Insert(JsonObject packet)
    {
        var document = new FlowDocument(new Paragraph(new Run("old")));
        var editor = new RichTextBox(document) { Language = Lang("fr-FR") };
        using var data = JsonDocument.Parse(packet.ToJsonString());
        WritingToolsDocumentFragment.Parse(data.RootElement).Insert(document, 0, document.GetText().Length, () => true);
        return (editor, document.EnumerateParagraphs().Single().Inlines.OfType<Run>().Single());
    }
    private static JsonObject Packet()
    {
        string text = "Miii中文🙂\n";
        return new JsonObject
        {
            ["version"] = 1, ["text16"] = Convert.ToBase64String(MemoryMarshal.AsBytes(text.AsSpan())),
            ["runs"] = new JsonArray(new JsonObject { ["start"] = 0, ["length"] = text.Length, ["family"] = "Helvetica Neue",
                ["size"] = 21, ["weight"] = 400, ["italic"] = false, ["underline"] = 0, ["strike"] = 0 }),
            ["paragraphs"] = new JsonArray(new JsonObject { ["start"] = 0, ["length"] = text.Length,
                ["alignment"] = 0, ["direction"] = -1, ["lists"] = new JsonArray(), ["tables"] = new JsonArray() }),
        };
    }
}
