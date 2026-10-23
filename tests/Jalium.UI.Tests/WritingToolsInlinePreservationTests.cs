using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class WritingToolsInlinePreservationTests
{
    public static IEnumerable<object[]> Backgrounds()
    {
        yield return [0xffff0000u, null!, 0xffff0000u];
        yield return [0xffff0000u, 0x800000ffu, 0xff7f0080u];
        yield return [0x80ff0000u, 0x800000ffu, 0xc05500aau];
        yield return [0xffff0000u, 0x000000ffu, 0xffff0000u];
        yield return [0xffff0000u, 0xff00ff00u, 0xff00ff00u];
    }

    [Theory, MemberData(nameof(Backgrounds))]
    public void SnapshotUsesThePaintedInlineBackgroundLayers(uint parent, uint? child, uint expected)
    {
        var run = new Run("Miii中文🙂e\u0301");
        if (child is { } value) run.Background = Brush(value);
        var span = new Span(run) { Background = Brush(parent) };
        var editor = new RichTextBox(new FlowDocument(new Paragraph(span)));
        using var snapshot = JsonDocument.Parse(Snapshot(editor));
        var first = snapshot.RootElement.GetProperty("runs")[0];
        Assert.True(first.TryGetProperty("background", out var background));
        Assert.Equal(expected, background.GetUInt32());
        Assert.False(snapshot.RootElement.GetProperty("runs").EnumerateArray().Last().TryGetProperty("background", out _));
        var provider = (Jalium.UI.Automation.Provider.ITextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
        Assert.Equal((int)((expected & 255) << 16 | (expected & 0xff00) | (expected >> 16 & 255)),
            provider.DocumentRange.FindText("Miii", false, false)!.GetAttributeValue(40001));
    }

    [Theory, InlineData(false), InlineData(true)]
    public void AnOpaqueChildMasksAnUnrepresentableAncestorButATranslucentChildDoesNot(bool opaque)
    {
        var run = new Run("中文🙂") { Background = Brush(opaque ? 0xff00ff00u : 0x8000ff00u) };
        var span = new Span(run) { Background = new LinearGradientBrush(Colors.Red, Colors.Blue, 0) };
        using var snapshot = JsonDocument.Parse(Snapshot(new RichTextBox(new FlowDocument(new Paragraph(span)))));
        var style = snapshot.RootElement.GetProperty("runs")[0];
        if (opaque) Assert.Equal(0xff00ff00u, style.GetProperty("background").GetUInt32());
        else Assert.False(style.TryGetProperty("background", out _));
    }

    [Theory, InlineData(false), InlineData(true)]
    public void AcceptingTheWholeDocumentKeepsTheOriginalSpanAndItsBackground(bool gradient)
    {
        Brush background = gradient ? new LinearGradientBrush(Colors.Red, Colors.Blue, 0) : Brush(0x80ff0000);
        var run = new Run("汉🙂Miii"); var span = new Span(run) { Background = background };
        var editor = new RichTextBox(new FlowDocument(new Paragraph(span)));
        var source = Packet(editor); var proposal = source.DeepClone().AsObject();
        Apply(editor, source, proposal);
        Assert.Same(span, Assert.Single(editor.Document.EnumerateParagraphs().Single().Inlines));
        Assert.Same(background, span.Background); Assert.Same(run, Assert.Single(span.Inlines));
        Assert.Equal("汉🙂Miii\n", editor.Document.GetText());
        Assert.Same(background, span.Background);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void WholeDocumentAcceptanceRetainsHyperlinkIdentityNavigationCommandAndClick(bool rewrite)
    {
        var run = new Run("旧链接🙂"); var link = new Hyperlink(run) { NavigateUri = new Uri("https://jalium.invalid/kept"), TargetName = "original-target" };
        var outer = new Span(link) { Background = Brush(0x80334455) };
        var editor = new RichTextBox(new FlowDocument(new Paragraph(outer)));
        int clicks = 0, commands = 0, navigations = 0; object parameter = new();
        var command = new ActionCommand(value => { Assert.Same(parameter, value); commands++; });
        link.Command = command; link.CommandParameter = parameter;
        link.Click += (_, _) => clicks++;
        link.RequestNavigate += (_, e) => { Assert.Equal(link.NavigateUri, e.Uri); navigations++; e.Handled = true; };
        var source = Packet(editor); var proposal = source.DeepClone().AsObject();
        if (rewrite)
        {
            string text = "新文字 Miii 中文🙂\n";
            proposal["text16"] = Encode(text);
            var body = source["runs"]![0]!.DeepClone().AsObject();
            body["start"] = 0; body["length"] = text.Length - 1;
            body["family"] = "Helvetica Neue"; body["size"] = 23; body["weight"] = 700; body["italic"] = true;
            body["inlineContext"] = WritingToolsDocumentFragment.OriginFor(outer) + "/" + WritingToolsDocumentFragment.OriginFor(link);
            var terminator = source["runs"]!.AsArray()[^1]!.DeepClone(); terminator["start"] = text.Length - 1; terminator["length"] = 1;
            proposal["runs"] = new JsonArray(body, terminator);
            proposal["paragraphs"]![0]!["length"] = text.Length;
        }
        Apply(editor, source, proposal);
        var liveOuter = Assert.IsType<Span>(editor.Document.EnumerateParagraphs().Single().Inlines[0]);
        var liveLink = Assert.IsType<Hyperlink>(liveOuter.Inlines[0]);
        Assert.Same(outer, liveOuter); Assert.Same(link, liveLink); Assert.Same(command, liveLink.Command);
        Assert.Equal("original-target", liveLink.TargetName); liveLink.DoClick();
        Assert.Equal((1, 1, 1), (clicks, commands, navigations));
        Assert.Equal(rewrite ? "新文字 Miii 中文🙂\n" : "旧链接🙂\n", editor.Document.GetText());
        if (rewrite)
        {
            Assert.Same(run, liveLink.Inlines[0]);
            Assert.Equal("Helvetica Neue", run.FontFamily.Source); Assert.Equal(23, run.FontSize);
            Assert.Equal(FontWeights.Bold, run.FontWeight); Assert.Equal(FontStyles.Italic, run.FontStyle);
            Assert.True(editor.Undo()); Assert.Same(link, Assert.IsType<Span>(editor.Document.EnumerateParagraphs().Single().Inlines[0]).Inlines[0]);
            Assert.Equal("旧链接🙂\n", editor.Document.GetText());
            Assert.True(editor.Redo()); Assert.Same(link, Assert.IsType<Span>(editor.Document.EnumerateParagraphs().Single().Inlines[0]).Inlines[0]);
            liveLink.DoClick(); Assert.Equal((2, 2, 2), (clicks, commands, navigations));
        }
    }

    private static void Apply(RichTextBox editor, JsonObject source, JsonObject proposal)
    {
        string text = new(MemoryMarshal.Cast<byte, char>(Convert.FromBase64String(proposal["text16"]!.GetValue<string>())));
        var args = new TextCompositionEventArgs(UIElement.TextInputEvent, text, 0)
        {
            ImeReplacementRange = (0, editor.Document.GetText().Length), NativeEditLifetime = () => true,
            NativeTextServicesPayload = new JsonObject { ["source"] = source, ["sourceStart"] = 0, ["proposal"] = proposal }.ToJsonString()
        };
        editor.RaiseEvent(args); Assert.True(args.Handled);
    }

    [Fact]
    public void PlainSpanProvenanceDoesNotSplitThePublicFormatUnit()
    {
        var paragraph = new Paragraph(new Span(new Run("first")));
        paragraph.Inlines.Add(new Span(new Run("second")));
        var editor = new RichTextBox(new FlowDocument(paragraph));
        var provider = (Jalium.UI.Automation.Provider.ITextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
        var range = provider.DocumentRange.FindText("first", false, false)!;
        range.ExpandToEnclosingUnit(Jalium.UI.Automation.Text.TextUnit.Format);
        Assert.StartsWith("firstsecond", range.GetText(-1));
    }

    [Theory, InlineData("unknown"), InlineData("reversed"), InlineData("duplicate")]
    public void InvalidInlinePathsAreRejectedBeforeChangingTheDocument(string kind)
    {
        var link = new Hyperlink(new Run("keep")); var outer = new Span(link);
        var paragraph = new Paragraph(outer); var editor = new RichTextBox(new FlowDocument(paragraph));
        var source = Packet(editor); var proposal = source.DeepClone().AsObject();
        string a = WritingToolsDocumentFragment.OriginFor(outer), b = WritingToolsDocumentFragment.OriginFor(link);
        proposal["runs"]![0]!["inlineContext"] = kind switch
        { "unknown" => Guid.NewGuid().ToString("N"), "reversed" => b + "/" + a, _ => a + "/" + a };
        if (kind == "duplicate")
            Assert.Throws<FormatException>(() => TryApply(editor, source, proposal));
        else Assert.False(TryApply(editor, source, proposal));
        Assert.Equal("keep\n", editor.Document.GetText()); Assert.Same(paragraph, editor.Document.Blocks[0]);
        Assert.Same(outer, paragraph.Inlines[0]); Assert.Same(link, outer.Inlines[0]); Assert.False(editor.CanUndo);
    }

    [Fact]
    public void RetirementDuringTreeMutationRestoresTheOriginalIdentityWithoutAnUndoRecord()
    {
        var run = new Run("keep"); var link = new Hyperlink(run); var paragraph = new Paragraph(link);
        var editor = new RichTextBox(new FlowDocument(paragraph)); var source = Packet(editor);
        Assert.False(TryApply(editor, source, source.DeepClone().AsObject(), () => editor.Document.Blocks.Count != 0));
        Assert.Equal("keep\n", editor.Document.GetText()); Assert.Same(paragraph, editor.Document.Blocks[0]);
        Assert.Same(link, paragraph.Inlines[0]); Assert.Same(run, link.Inlines[0]); Assert.False(editor.CanUndo);
    }

    [Fact]
    public void UnchangedMultipleParagraphsKeepTheirInlineAndParagraphInstances()
    {
        var first = new Paragraph(new Hyperlink(new Run("first🙂")));
        var second = new Paragraph(new Span(new Run("second中文")) { Background = Brush(0x80abcdef) });
        var editor = new RichTextBox(new FlowDocument(first)); editor.Document.Blocks.Add(second);
        var firstInline = first.Inlines[0]; var secondInline = second.Inlines[0]; var source = Packet(editor);
        Apply(editor, source, source.DeepClone().AsObject());
        Assert.Same(first, editor.Document.Blocks[0]); Assert.Same(second, editor.Document.Blocks[1]);
        Assert.Same(firstInline, first.Inlines[0]); Assert.Same(secondInline, second.Inlines[0]);
        Assert.Equal("first🙂\nsecond中文\n", editor.Document.GetText());
    }

    [Fact]
    public void AnUnchangedInlineBreakInsideAHyperlinkKeepsItsIdentity()
    {
        var lineBreak = new LineBreak(); var link = new Hyperlink(new Run("first"));
        link.Inlines.Add(lineBreak); link.Inlines.Add(new Run("second"));
        var editor = new RichTextBox(new FlowDocument(new Paragraph(link))); var before = editor.Document.GetText();
        var source = Packet(editor); Apply(editor, source, source.DeepClone().AsObject());
        Assert.Same(link, editor.Document.EnumerateParagraphs().Single().Inlines[0]);
        Assert.Same(lineBreak, link.Inlines[1]); Assert.Equal(before, editor.Document.GetText());
    }

    [Fact]
    public void ForgedSourceInlineProvenanceCannotRetargetAnExistingLink()
    {
        var first = new Hyperlink(new Run("same")); var second = new Hyperlink(new Run("same"));
        var paragraph = new Paragraph(first); paragraph.Inlines.Add(second);
        var editor = new RichTextBox(new FlowDocument(paragraph)); var source = Packet(editor);
        source["runs"]![0]!["inlineContext"] = WritingToolsDocumentFragment.OriginFor(second);
        Assert.False(TryApply(editor, source, source.DeepClone().AsObject()));
        Assert.Equal("samesame\n", editor.Document.GetText());
        Assert.Same(first, paragraph.Inlines[0]); Assert.Same(second, paragraph.Inlines[1]); Assert.False(editor.CanUndo);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void MissingProposalProvenancePreservesAUniformPathAndRejectsAmbiguousFlattening(bool mixed)
    {
        var link = new Hyperlink(new Run("keep")); var paragraph = new Paragraph(link);
        if (mixed) paragraph.Inlines.Add(new Run("plain"));
        var editor = new RichTextBox(new FlowDocument(paragraph)); var source = Packet(editor);
        var proposal = source.DeepClone().AsObject();
        foreach (var run in proposal["runs"]!.AsArray()) run!.AsObject().Remove("inlineContext");
        proposal["text16"] = Encode(mixed ? "KEEPplain\n" : "KEEP\n");
        Assert.Equal(!mixed, TryApply(editor, source, proposal));
        Assert.Same(paragraph, editor.Document.Blocks[0]);
        Assert.Same(link, paragraph.Inlines[0]); Assert.Equal(mixed ? "keepplain\n" : "KEEP\n", editor.Document.GetText());
        if (mixed) Assert.False(editor.CanUndo);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void ACallbackMovingSourceToAnotherDocumentIsNeverReclaimed(bool moveParagraph)
    {
        var run = new Run("keep"); var link = new Hyperlink(run); var paragraph = new Paragraph(link);
        var editor = new RichTextBox(new FlowDocument(paragraph)); var foreignParagraph = new Paragraph();
        var foreign = new FlowDocument(foreignParagraph); var source = Packet(editor); var proposal = source.DeepClone().AsObject();
        proposal["text16"] = Encode("KEEP\n"); bool moved = false;
        if (moveParagraph)
            editor.Document.Blocks.CollectionChanged += (_, e) =>
            {
                if (moved || e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Reset) return;
                moved = true; foreign.Blocks.Add(paragraph);
            };
        else
            run.TextContentChanged += (_, _) =>
            {
                if (moved || run.Text != "KEEP") return;
                moved = true; link.Inlines.Remove(run); foreignParagraph.Inlines.Add(run);
            };
        Assert.False(TryApply(editor, source, proposal)); Assert.True(moved);
        if (moveParagraph)
        { Assert.Same(paragraph, foreign.Blocks[1]); Assert.Equal("keep", run.Text); }
        else
        { Assert.Same(run, foreignParagraph.Inlines[0]); Assert.Equal("KEEP", run.Text); }
        Assert.Same(foreign, run.GetFlowDocument());
    }

    [Fact]
    public void UnchangedProjectionKeepsRawTranslucentLayersAndInheritedParagraphProperties()
    {
        var run = new Run("Miii中文🙂") { Background = Brush(0x800000ff), FontStretch = FontStretches.Condensed };
        var span = new Span(run) { Background = Brush(0x80ff0000) }; var paragraph = new Paragraph(span);
        var editor = new RichTextBox(new FlowDocument(paragraph)); var source = Packet(editor);
        var rawChild = run.Background; var rawParent = span.Background;
        object alignment = paragraph.ReadLocalValue(Paragraph.TextAlignmentProperty), direction = paragraph.ReadLocalValue(Paragraph.FlowDirectionProperty);
        Apply(editor, source, source.DeepClone().AsObject());
        Assert.Same(paragraph, editor.Document.Blocks[0]); Assert.Same(span, paragraph.Inlines[0]);
        Assert.Same(rawChild, run.Background); Assert.Same(rawParent, span.Background);
        Assert.Equal(FontStretches.Condensed, run.FontStretch);
        Assert.Equal(alignment, paragraph.ReadLocalValue(Paragraph.TextAlignmentProperty));
        Assert.Equal(direction, paragraph.ReadLocalValue(Paragraph.FlowDirectionProperty));
        Assert.Equal(source["runs"]![0]!["background"]!.GetValue<uint>(), Packet(editor)["runs"]![0]!["background"]!.GetValue<uint>());
    }

    [Fact]
    public void EveryStyleRecordValidatesProvenanceIncludingTheUnmaterializedTerminator()
    {
        var editor = new RichTextBox(new FlowDocument(new Paragraph(new Hyperlink(new Run("keep")))));
        var source = Packet(editor); var proposal = source.DeepClone().AsObject();
        proposal["runs"]!.AsArray()[^1]!["inlineContext"] = "bad";
        Assert.Throws<FormatException>(() => TryApply(editor, source, proposal));
        Assert.Equal("keep\n", editor.Document.GetText()); Assert.False(editor.CanUndo);
    }

    [Fact]
    public void ManySiblingLinksKeepTheirDistinctInstancesAndFormatBoundaries()
    {
        var paragraph = new Paragraph(); var links = Enumerable.Range(0, 256).Select(i => new Hyperlink(new Run($"link{i}🙂"))).ToArray();
        foreach (var link in links) paragraph.Inlines.Add(link);
        var editor = new RichTextBox(new FlowDocument(paragraph)); var source = Packet(editor); var text = editor.Document.GetText();
        Apply(editor, source, source.DeepClone().AsObject());
        Assert.Same(paragraph, editor.Document.Blocks[0]);
        Assert.Equal(text, editor.Document.GetText()); Assert.Equal(links.Length, paragraph.Inlines.Count);
        for (int i = 0; i < links.Length; ++i) Assert.Same(links[i], paragraph.Inlines[i]);
    }

    [Fact]
    public void AnUnchangedEmptySemanticInlineRetainsItsIdentity()
    {
        var run = new Run(); var link = new Hyperlink(run) { NavigateUri = new Uri("https://jalium.invalid/empty") };
        var span = new Span(link); var paragraph = new Paragraph(span);
        var editor = new RichTextBox(new FlowDocument(paragraph)); var source = Packet(editor);
        Apply(editor, source, source.DeepClone().AsObject());
        Assert.Same(paragraph, editor.Document.Blocks[0]); Assert.Same(span, paragraph.Inlines[0]);
        Assert.Same(link, span.Inlines[0]); Assert.Same(run, link.Inlines[0]);
        Assert.Equal(new Uri("https://jalium.invalid/empty"), link.NavigateUri);
    }

    [Fact]
    public void ARewrittenLinkSplitIntoDifferentFontRunsKeepsTheRawPaintLayers()
    {
        var brush = Brush(0x800000ff); var run = new Run("firstsecond") { Background = brush };
        var link = new Hyperlink(run); var span = new Span(link) { Background = Brush(0x80ff0000) };
        var editor = new RichTextBox(new FlowDocument(new Paragraph(span))); var source = Packet(editor); var proposal = source.DeepClone().AsObject();
        proposal["text16"] = Encode("FIRSTSECOND\n"); var first = proposal["runs"]![0]!.DeepClone(); var second = first.DeepClone();
        first["length"] = 5; second["start"] = 5; second["length"] = 6; second["weight"] = 700;
        proposal["runs"] = new JsonArray(first, second, proposal["runs"]!.AsArray()[^1]!.DeepClone());
        Apply(editor, source, proposal);
        Assert.Same(span, editor.Document.EnumerateParagraphs().Single().Inlines[0]);
        Assert.Same(link, span.Inlines[0]); Assert.Equal(2, link.Inlines.Count); Assert.Same(run, link.Inlines[0]);
        var added = Assert.IsType<Run>(link.Inlines[1]); Assert.Same(brush, run.Background); Assert.Same(brush, added.Background);
        Assert.Equal(FontWeights.Bold, added.FontWeight); Assert.Equal("FIRSTSECOND\n", editor.Document.GetText());
        var after = Packet(editor); Assert.Equal(source["runs"]![0]!["background"]!.GetValue<uint>(), after["runs"]![1]!["background"]!.GetValue<uint>());
        Assert.True(editor.Undo()); Assert.Same(run, Assert.Single(link.Inlines)); Assert.Equal("firstsecond\n", editor.Document.GetText());
        Assert.True(editor.Redo()); Assert.Same(added, link.Inlines[1]);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void BackgroundChangesKeepTheSourceTreeAndRejectUnrepresentableTranslucentFlattening(bool opaque)
    {
        var run = new Run("keep") { Background = Brush(0x800000ff) }; var span = new Span(run) { Background = Brush(0x80ff0000) };
        var paragraph = new Paragraph(span); var editor = new RichTextBox(new FlowDocument(paragraph));
        var source = Packet(editor); var proposal = source.DeepClone().AsObject(); uint desired = opaque ? 0xff00ff00u : 0x8000ff00u;
        proposal["runs"]![0]!["background"] = desired;
        Assert.Equal(opaque, TryApply(editor, source, proposal)); Assert.Same(paragraph, editor.Document.Blocks[0]);
        Assert.Same(span, paragraph.Inlines[0]); Assert.Same(run, span.Inlines[0]);
        if (opaque) Assert.Equal(desired, Packet(editor)["runs"]![0]!["background"]!.GetValue<uint>());
        else { Assert.Equal(source["runs"]![0]!["background"]!.GetValue<uint>(), Packet(editor)["runs"]![0]!["background"]!.GetValue<uint>()); Assert.False(editor.CanUndo); }
    }

    [Theory, InlineData(false), InlineData(true)]
    public void DecorationDeltasDoNotDuplicateOrEraseThePreservedAncestorLine(bool removeInherited)
    {
        var run = new Run("keep"); var link = new Hyperlink(run) { TextDecorations = TextDecorations.Underline };
        var paragraph = new Paragraph(link); var editor = new RichTextBox(new FlowDocument(paragraph));
        var source = Packet(editor); var proposal = source.DeepClone().AsObject();
        if (removeInherited) proposal["runs"]![0]!["underline"] = 0;
        else proposal["runs"]![0]!["strike"] = 1;
        Assert.Equal(!removeInherited, TryApply(editor, source, proposal)); Assert.Same(paragraph, editor.Document.Blocks[0]); Assert.Same(link, paragraph.Inlines[0]); Assert.Same(run, link.Inlines[0]);
        if (removeInherited) { Assert.Null(run.TextDecorations); Assert.False(editor.CanUndo); }
        else
        {
            Assert.Equal(TextDecorationLocation.Strikethrough, Assert.Single(run.TextDecorations!).Location);
            var style = Packet(editor)["runs"]![0]!; Assert.Equal(1, style["underline"]!.GetValue<int>()); Assert.Equal(1, style["strike"]!.GetValue<int>());
            Assert.True(editor.Undo()); Assert.Null(run.TextDecorations); Assert.True(editor.Redo());
            Assert.Equal(TextDecorationLocation.Strikethrough, Assert.Single(run.TextDecorations!).Location);
        }
    }

    [Fact]
    public void CachedInlinePathsFollowReparentingAndRejectThePreviousSnapshot()
    {
        var link = new Hyperlink(new Run("same")); var first = new Span(link); var second = new Span();
        var paragraph = new Paragraph(first); paragraph.Inlines.Add(second); var editor = new RichTextBox(new FlowDocument(paragraph));
        var source = Packet(editor); Assert.NotNull(source["runs"]![0]!["inlineContext"]);
        string before = source["runs"]![0]!["inlineContext"]!.GetValue<string>();
        Assert.Equal(before, Packet(editor)["runs"]![0]!["inlineContext"]!.GetValue<string>());
        first.Inlines.Remove(link); second.Inlines.Add(link);
        var undo = (System.Collections.ICollection)typeof(RichTextBox).GetField("_undoStack", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)!;
        int existingUndoRecords = undo.Count;
        string after = Packet(editor)["runs"]![0]!["inlineContext"]!.GetValue<string>();
        Assert.NotEqual(before, after);
        Assert.Equal(WritingToolsDocumentFragment.OriginFor(second) + "/" + WritingToolsDocumentFragment.OriginFor(link), after);
        Assert.False(TryApply(editor, source, source.DeepClone().AsObject()));
        Assert.Equal("same\n", editor.Document.GetText()); Assert.Same(paragraph, editor.Document.Blocks[0]);
        Assert.Same(link, second.Inlines[0]); Assert.Empty(first.Inlines); Assert.Equal(existingUndoRecords, undo.Count);
    }

    private static bool TryApply(RichTextBox editor, JsonObject source, JsonObject proposal, Func<bool>? current = null)
    {
        string text = new(MemoryMarshal.Cast<byte, char>(Convert.FromBase64String(proposal["text16"]!.GetValue<string>())));
        var args = new TextCompositionEventArgs(UIElement.TextInputEvent, text, 0)
        {
            ImeReplacementRange = (0, editor.Document.GetText().Length), NativeEditLifetime = current ?? (() => true),
            NativeTextServicesPayload = new JsonObject { ["source"] = source, ["sourceStart"] = 0, ["proposal"] = proposal }.ToJsonString()
        };
        editor.RaiseEvent(args); return args.Handled;
    }

    private static string Snapshot(RichTextBox editor) => MacOSAccessibilityTree.CreateTextStyleSnapshot(
        (IAutomationTextProviderSource)UIElementAutomationPeer.CreatePeerForElement(editor)!, 0, editor.Document.GetText().Length, () => true)!;

    private static JsonObject Packet(RichTextBox editor)
    {
        var packet = JsonNode.Parse(Snapshot(editor))!.AsObject();
        foreach (var run in packet["runs"]!.AsArray()) run!["italic"] = run["italic"]!.GetValue<int>() != 0;
        var rows = new JsonArray();
        foreach (var record in editor.GetWritingToolsStructure())
        {
            var row = JsonSerializer.SerializeToNode(record, new JsonSerializerOptions(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull })!.AsObject();
            row["alignment"] = 0; row["direction"] = -1; rows.Add(row);
        }
        packet["paragraphs"] = rows; return packet;
    }

    private static string Encode(string text) => Convert.ToBase64String(MemoryMarshal.AsBytes(text.AsSpan()));
    private static SolidColorBrush Brush(uint value) => new(Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value));
    private sealed class ActionCommand(Action<object?> action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action(parameter);
    }
}
