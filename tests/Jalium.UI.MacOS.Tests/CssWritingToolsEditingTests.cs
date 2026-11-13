using System.Runtime.InteropServices;
using System.Text.Json;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class CssWritingToolsEditingTests : IDisposable
{
    [Theory]
    [InlineData("prefix", "改写🙂")]
    [InlineData("middle", "改写🙂")]
    [InlineData("suffix", "改写🙂")]
    [InlineData("paragraph", "改写🙂")]
    [InlineData("prefix", "")]
    [InlineData("middle", "")]
    public void ServiceOffsetsReplaceVisibleTextAndPreserveHiddenSources(string location, string replacement)
    {
        var secret = new Run("secret🙂"); var first = new Run("AA "); var target = new Run("target"); var last = new Run(" ZZ");
        var paragraph = new Paragraph(first); var hidden = new Span(secret);
        if (location == "prefix") paragraph.Inlines.Insert(0, hidden);
        paragraph.Inlines.Add(target);
        if (location == "middle") paragraph.Inlines.Insert(1, hidden);
        paragraph.Inlines.Add(last);
        if (location == "suffix") paragraph.Inlines.Add(hidden);
        var document = new FlowDocument(paragraph);
        if (location == "paragraph") document.Blocks.Insert(0, new Paragraph(hidden));
        Css.SetStyle(location == "paragraph" ? hidden.Parent! : hidden, "display:none;");
        var editor = Editor(document); Layout(editor); ClearHistory(editor);
        var before = Text(editor); int offset = before.IndexOf("target", StringComparison.Ordinal); int changes = 0;
        editor.TextChanged += (_, _) => changes++;
        Assert.True(Input(editor, offset, 6, replacement).Handled);
        Layout(editor);
        Assert.Equal(before.Replace("target", replacement, StringComparison.Ordinal), Text(editor));
        Assert.Equal("secret🙂", secret.Text); Assert.Same(hidden, secret.Parent);
        Assert.Same(document, secret.GetFlowDocument()); Assert.Same(paragraph, first.Parent); Assert.Same(paragraph, last.Parent);
        Assert.Equal(1, changes); Assert.True(editor.Undo());
        Layout(editor); Assert.Equal(before, Text(editor)); Assert.Same(target, paragraph.Inlines.Single(i => ReferenceEquals(i, target)));
        Assert.Equal("target", target.Text); Assert.True(editor.Redo()); Layout(editor);
        Assert.Equal(before.Replace("target", replacement, StringComparison.Ordinal), Text(editor));
        if (replacement.Length > 0) AssertRichRun(document, replacement);
    }

    [Theory]
    [InlineData("UV")]
    [InlineData("one\ntwo")]
    [InlineData("")]
    public void DisjointVisibleRangesKeepHiddenInlineAndFormOneUndoUnit(string replacement)
    {
        var left = new Run("pre AA"); var secret = new Run("secret"); var hidden = new Span(secret);
        var right = new Run("BB post"); var paragraph = new Paragraph(left); paragraph.Inlines.Add(hidden); paragraph.Inlines.Add(right);
        var document = new FlowDocument(paragraph); Css.SetStyle(hidden, "display:none;"); var editor = Editor(document);
        Layout(editor); ClearHistory(editor); var before = Text(editor); var raw = document.GetText();
        Assert.Equal("pre AABB post\n", before);
        Assert.True(Input(editor, 4, 4, replacement).Handled); Layout(editor);
        Assert.Equal("pre " + replacement + " post\n", Text(editor));
        Assert.Equal("secret", secret.Text); Assert.Same(hidden, secret.Parent); Assert.Same(document, hidden.GetFlowDocument());
        Assert.True(editor.Undo()); Layout(editor); Assert.Equal(raw, document.GetText()); Assert.Equal(before, Text(editor));
        Assert.Same(paragraph, hidden.Parent); Assert.Same(left, paragraph.Inlines[0]); Assert.Same(right, paragraph.Inlines[2]);
        Assert.False(editor.CanUndo); Assert.True(editor.Redo()); Layout(editor); Assert.Equal("pre " + replacement + " post\n", Text(editor));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void InsertionsAtHiddenSeamsUseVisibleSourceAffinity(int offset)
    {
        var secret = new Run("secret"); var hidden = new Span(secret); var left = new Run("AA"); var right = new Run("BB");
        var paragraph = new Paragraph(left); paragraph.Inlines.Add(hidden); paragraph.Inlines.Add(right);
        if (offset == 0) { paragraph.Inlines.Remove(hidden); paragraph.Inlines.Insert(0, hidden); }
        if (offset == 4) { paragraph.Inlines.Remove(hidden); paragraph.Inlines.Add(hidden); }
        Css.SetStyle(hidden, "display:none;"); var document = new FlowDocument(paragraph); var editor = Editor(document);
        Layout(editor); ClearHistory(editor);
        Assert.True(Input(editor, offset, 0, "补").Handled); Layout(editor);
        Assert.Equal("AABB\n".Insert(offset, "补"), Text(editor)); Assert.Equal("secret", secret.Text);
        AssertRichRun(document, "补"); Assert.True(editor.Undo()); Layout(editor); Assert.Equal("AABB\n", Text(editor));
        Assert.Same(paragraph, hidden.Parent);
    }

    [Theory]
    [InlineData("paragraphs")]
    [InlineData("list")]
    [InlineData("table")]
    public void RichResultsRetainHiddenSiblingBlocksAndUndoRestoresOriginalGraph(string kind)
    {
        var hidden = new Paragraph(new Run("private")); var original = new Paragraph(new Run("replace"));
        var document = new FlowDocument(hidden); document.Blocks.Add(original); Css.SetStyle(hidden, "display:none;");
        var editor = Editor(document); Layout(editor); ClearHistory(editor);
        Assert.Equal("replace\n", Text(editor));
        object[]? rows = kind switch
        {
            "list" => [Row(0, 4, lists: [List()]), Row(4, 4, lists: [List()])],
            "table" => [Row(0, 4, cells: [Cell(0, 0)]), Row(4, 4, cells: [Cell(0, 1)])],
            _ => null
        };
        Assert.True(Input(editor, 0, Text(editor).Length, "one\ntwo\n", rows: rows).Handled); Layout(editor);
        Assert.Same(hidden, document.Blocks[0]); Assert.Equal("private", Assert.IsType<Run>(hidden.Inlines[0]).Text);
        Assert.DoesNotContain("private", Text(editor));
        if (kind == "list") Assert.IsType<DocList>(document.Blocks[1]);
        if (kind == "table") Assert.IsType<Table>(document.Blocks[1]);
        var after = Text(editor); Assert.True(editor.Undo()); Layout(editor);
        Assert.Equal("replace\n", Text(editor)); Assert.Same(hidden, document.Blocks[0]); Assert.Same(original, document.Blocks[1]);
        Assert.True(editor.Redo()); Layout(editor); Assert.Equal(after, Text(editor));
    }

    [Theory]
    [InlineData("list")]
    [InlineData("cell")]
    public void EditingInsideContainersUsesExposedOffsetsAndKeepsTheContainer(string kind)
    {
        var hidden = new Paragraph(new Run("private")); Css.SetStyle(hidden, "display:none;");
        var run = new Run("abc"); var paragraph = new Paragraph(run); var document = new FlowDocument(hidden);
        TextElement container;
        if (kind == "list")
        {
            var item = new ListItem(paragraph); var list = new DocList(item) { StartIndex = 5 }; document.Blocks.Add(list); container = item;
        }
        else
        {
            var cell = new TableCell(paragraph); var row = new TableRow(); row.Cells.Add(cell);
            var group = new TableRowGroup(); group.Rows.Add(row); var table = new Table(); table.RowGroups.Add(group);
            document.Blocks.Add(table); container = cell;
        }
        var editor = Editor(document); Layout(editor); ClearHistory(editor); var before = Text(editor);
        Assert.True(Input(editor, before.IndexOf('b'), 1, "UV").Handled); Layout(editor);
        Assert.Equal(before.Replace("abc", "aUVc", StringComparison.Ordinal), Text(editor)); Assert.Same(container, paragraph.Parent);
        Assert.True(editor.Undo()); Layout(editor); Assert.Equal(before, Text(editor)); Assert.Same(paragraph, run.Parent);
    }

    [Fact]
    public void WholeVisibleParagraphsAcrossHiddenBlocksKeepThoseBlocksInTheirOwner()
    {
        var first = new Paragraph(new Run("one")); var hidden = new Paragraph(new Run("private")); var last = new Paragraph(new Run("two"));
        var section = new Section(first); section.Blocks.Add(hidden); section.Blocks.Add(last); Css.SetStyle(hidden, "display:none;");
        var document = new FlowDocument(section); var editor = Editor(document); Layout(editor); ClearHistory(editor);
        Assert.True(Input(editor, 0, Text(editor).Length, "result\n").Handled); Layout(editor);
        Assert.Equal("result\n", Text(editor)); Assert.Same(section, hidden.Parent); Assert.Contains(hidden, section.Blocks);
        Assert.True(editor.Undo()); Layout(editor); Assert.Equal("one\ntwo\n", Text(editor));
        Assert.Equal(new Block[] { first, hidden, last }, section.Blocks);
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(1, 4)]
    [InlineData(3, 3)]
    [InlineData(6, 0)]
    public void StructureUsesClippedExposedOffsetsAndOmitsHiddenParagraphs(int start, int length)
    {
        var hidden = new Paragraph(new Run("private")); var a = new Run("A"); var secret = new Run("secret"); var b = new Run("B");
        var first = new Paragraph(a); first.Inlines.Add(secret); first.Inlines.Add(b);
        var second = new Paragraph(new Run("CD")); var document = new FlowDocument(hidden); document.Blocks.Add(first); document.Blocks.Add(second);
        Css.SetStyle(hidden, "display:none;"); Css.SetStyle(secret, "display:none;"); var editor = Editor(document); Layout(editor);
        Assert.Equal("AB\nCD\n", Text(editor)); int end = length < 0 ? 6 : start + length;
        var expected = new[] { (Start: 0, End: 3), (Start: 3, End: 6) }.Where(p => p.End > start && p.Start < end)
            .Select(p => (Math.Max(p.Start, start) - start, Math.Min(p.End, end) - Math.Max(p.Start, start))).ToArray();
        Assert.Equal(expected, editor.GetWritingToolsStructure(start, length).Select(p => (p.Start, p.Length)).ToArray());
    }

    [Fact]
    public void StructurePreservesOriginalTableGridAndNestedListWhenCellsAreHidden()
    {
        var hidden = new Paragraph(new Run("private")); Css.SetStyle(hidden, "display:none;");
        var hiddenCell = new TableCell(new Paragraph(new Run("secret"))) { RowSpan = 2 }; Css.SetStyle(hiddenCell, "display:none;");
        var item = new ListItem(new Paragraph(new Run("listed"))); var list = new DocList(item) { StartIndex = 7, MarkerStyle = TextMarkerStyle.UpperRoman };
        var visible = new TableCell(); visible.Blocks.Add(list);
        var row0 = new TableRow(); row0.Cells.Add(hiddenCell); row0.Cells.Add(visible);
        var row1 = new TableRow(); row1.Cells.Add(new TableCell(new Paragraph(new Run("last"))));
        var group = new TableRowGroup(); group.Rows.Add(row0); group.Rows.Add(row1); var table = new Table(); table.RowGroups.Add(group);
        var document = new FlowDocument(hidden); document.Blocks.Add(table); var editor = Editor(document); Layout(editor);
        var text = Text(editor); var structure = editor.GetWritingToolsStructure(); Assert.Equal(2, structure.Length);
        var first = structure[0]; Assert.Equal(text.IndexOf("listed", StringComparison.Ordinal), first.Start); Assert.Equal(7, first.Length);
        var marker = Assert.Single(first.Lists); Assert.Equal(7, marker.Start); Assert.Equal("{upper-roman}", marker.Format);
        var cell0 = Assert.Single(first.Tables); var cell1 = Assert.Single(structure[1].Tables);
        Assert.Equal((0, 1, 2), (cell0.Row, cell0.Column, cell0.TableColumns)); Assert.Equal((1, 1), (cell1.Row, cell1.Column));
        Assert.Equal(cell0.Id, cell1.Id); Assert.Equal(text.IndexOf("last", StringComparison.Ordinal), structure[1].Start);
        var clipped = Assert.Single(editor.GetWritingToolsStructure(first.Start + 1, 3)); Assert.Equal(0, clipped.Start); Assert.Equal(3, clipped.Length);
        Assert.Equal((0, 1), (Assert.Single(clipped.Tables).Row, Assert.Single(clipped.Tables).Column));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)]
    public void RetiringDuringAnEditRestoresTheOriginalGraphAndHistory(int acceptedChecks)
    {
        var hidden = new Run("secret"); var run = new Run("abcdef"); var paragraph = new Paragraph(hidden); paragraph.Inlines.Add(run);
        Css.SetStyle(hidden, "display:none;"); var document = new FlowDocument(paragraph); var editor = Editor(document);
        Layout(editor); ClearHistory(editor); var raw = document.GetText(); int checks = 0; int changes = 0;
        editor.TextChanged += (_, _) => changes++;
        var input = Input(editor, 1, 3, "one\ntwo", current: () => ++checks <= acceptedChecks);
        if (input.Handled) return; // Later checkpoints may lie beyond this edit's last guard.
        Layout(editor); Assert.Equal(raw, document.GetText()); Assert.Equal("abcdef\n", Text(editor));
        Assert.Equal(new Inline[] { hidden, run }, paragraph.Inlines); Assert.Same(paragraph, run.Parent);
        Assert.False(editor.CanUndo); Assert.Equal(0, changes);
    }

    [Theory]
    [InlineData("readonly")]
    [InlineData("disabled")]
    [InlineData("retired")]
    [InlineData("surrogate")]
    [InlineData("out-of-range")]
    public void InvalidOrUnavailableEditsRejectBeforeMutation(string mode)
    {
        var secret = new Run("secret"); var target = new Run("🙂abc"); var paragraph = new Paragraph(secret); paragraph.Inlines.Add(target);
        Css.SetStyle(secret, "display:none;"); var document = new FlowDocument(paragraph); var editor = Editor(document);
        Layout(editor); ClearHistory(editor); editor.IsReadOnly = mode == "readonly"; editor.IsEnabled = mode != "disabled";
        int start = mode == "surrogate" ? 1 : mode == "out-of-range" ? 100 : 2;
        Assert.False(Input(editor, start, 1, "UV", current: () => mode != "retired").Handled);
        Assert.Equal("secret🙂abc\n", document.GetText()); Assert.Same(paragraph, target.Parent); Assert.False(editor.CanUndo);
    }

    [Fact]
    public void RemovingCssReturnsReplacementAndStructureToTheOriginalDomain()
    {
        var first = new Run("AA"); var second = new Run("BB"); var paragraph = new Paragraph(first); paragraph.Inlines.Add(second);
        var document = new FlowDocument(paragraph); Css.SetStyle(first, "display:none;"); var editor = Editor(document); Layout(editor);
        Assert.Equal("BB\n", Text(editor)); Css.SetStyle(first, ""); Css.SetStyle(document, ""); Layout(editor); ClearHistory(editor);
        Assert.True(Input(editor, 2, 2, "UV").Handled); Assert.Equal("AAUV\n", document.GetText());
        Assert.Equal((0, 5), (editor.GetWritingToolsStructure()[0].Start, editor.GetWritingToolsStructure()[0].Length));
    }

    [Fact]
    public void PartialRangeAcrossAHiddenParagraphRejectsWithoutExposingOrDeletingIt()
    {
        var first = new Paragraph(new Run("before")); var hidden = new Paragraph(new Run("private")); var last = new Paragraph(new Run("after"));
        var document = new FlowDocument(first); document.Blocks.Add(hidden); document.Blocks.Add(last); Css.SetStyle(hidden, "display:none;");
        var editor = Editor(document); Layout(editor); ClearHistory(editor); var raw = document.GetText(); var visible = Text(editor);
        Assert.False(Input(editor, 2, 7, "UV").Handled); Layout(editor);
        Assert.Equal(raw, document.GetText()); Assert.Equal(visible, Text(editor)); Assert.Equal(new Block[] { first, hidden, last }, document.Blocks);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void ReplacingTheDocumentDuringALifetimeGuardDoesNotReclaimTheNewDocument()
    {
        var secret = new Run("private"); var target = new Run("abc"); var paragraph = new Paragraph(secret); paragraph.Inlines.Add(target);
        var original = new FlowDocument(paragraph); Css.SetStyle(secret, "display:none;"); var editor = Editor(original);
        var replacement = new FlowDocument(new Paragraph(new Run("external"))); Layout(editor); ClearHistory(editor); int checks = 0;
        Assert.False(Input(editor, 1, 1, "UV", current: () => { if (++checks == 3) { editor.Document = replacement; return false; } return true; }).Handled);
        Assert.Same(replacement, editor.Document); Assert.Same(editor, replacement.Parent); Assert.Equal("external\n", replacement.GetText());
        Assert.Equal("privateabc\n", original.GetText()); Assert.Same(paragraph, target.Parent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LosingEditabilityDuringTheNativeGuardRejectsAndPreservesTheSource(bool readOnly)
    {
        var secret = new Run("private"); var target = new Run("abc"); var paragraph = new Paragraph(secret); paragraph.Inlines.Add(target);
        var document = new FlowDocument(paragraph); Css.SetStyle(secret, "display:none;"); var editor = Editor(document);
        Layout(editor); ClearHistory(editor); int checks = 0;
        Assert.False(Input(editor, 1, 1, "UV", current: () =>
        {
            if (++checks == 3) { if (readOnly) editor.IsReadOnly = true; else editor.IsEnabled = false; }
            return true;
        }).Handled);
        Assert.Equal("privateabc\n", document.GetText()); Assert.Same(paragraph, target.Parent); Assert.False(editor.CanUndo);
    }

    [Fact]
    public void UndoAndRedoRefreshThePaintedProjectionBeforeAnyAutomationQuery()
    {
        var hidden = new Paragraph(new Run("private")); Css.SetStyle(hidden, "display:none;");
        var paragraph = new Paragraph(new Run("original")); var document = new FlowDocument(hidden); document.Blocks.Add(paragraph);
        var editor = Editor(document); Layout(editor); ClearHistory(editor); Assert.Equal("original\n", Text(editor));
        Assert.True(Input(editor, 0, 8, "edited\n").Handled); Layout(editor);
        Assert.Contains("edited", PaintedTexts(editor)); Assert.True(editor.Undo()); Layout(editor);
        Assert.Contains("original", PaintedTexts(editor)); Assert.DoesNotContain("edited", PaintedTexts(editor));
        Assert.True(editor.Redo()); Layout(editor);
        Assert.Contains("edited", PaintedTexts(editor)); Assert.DoesNotContain("original", PaintedTexts(editor));

        static IEnumerable<string> PaintedTexts(Visual root)
        {
            if (root is TextBlock text) yield return text.Text;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                if (VisualTreeHelper.GetChild(root, i) is Visual child)
                    foreach (var value in PaintedTexts(child)) yield return value;
        }
    }

    private static RichTextBox Editor(FlowDocument document)
    {
        document.PagePadding = new(0); document.FontFamily = "Courier New"; document.FontSize = 16;
        Css.SetStyle(document, "font-family:'Courier New';font-size:16px;line-height:24px;");
        return new(document) { Template = null, Padding = new(0), BorderThickness = new(0) };
    }
    internal static void Layout(RichTextBox editor)
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher);
            editor.Measure(new(360, 640)); editor.Arrange(new(0, 0, 360, 640));
        }
    }
    internal static string Text(RichTextBox editor) => ((AutomationTextProvider)editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!).Source.Text;
    private static void ClearHistory(RichTextBox editor) { editor.IsUndoEnabled = false; editor.IsUndoEnabled = true; }
    private static void AssertRichRun(FlowDocument document, string text)
    {
        var run = Assert.Single(document.EnumerateParagraphs().SelectMany(p => Runs(p.Inlines)), r => r.Text == text);
        Assert.Equal(23, run.FontSize); Assert.Equal(FontWeights.Bold, run.FontWeight); Assert.Equal(FontStyles.Italic, run.FontStyle);
        Assert.Equal(0xff2255ffu, Assert.IsType<SolidColorBrush>(run.Foreground).Color.ToArgb());
    }
    private static IEnumerable<Run> Runs(IEnumerable<Inline> inlines)
    {
        foreach (var inline in inlines)
            if (inline is Run run) yield return run;
            else if (inline is Span span) foreach (var child in Runs(span.Inlines)) yield return child;
    }
    internal static TextCompositionEventArgs Input(RichTextBox editor, int start, int length, string text, Func<bool>? current = null, object[]? rows = null)
    {
        var runs = text.Length == 0 ? Array.Empty<object>() : new object[] { new { start = 0, length = text.Length,
            family = "Helvetica Neue", size = 23, weight = 700, italic = true, underline = 1, strike = 0, foreground = 0xff2255ffu } };
        var paragraphs = new List<object>();
        for (int at = 0; at < text.Length;)
        {
            int next = text.IndexOf('\n', at), end = next < 0 ? text.Length : next + 1;
            paragraphs.Add(Row(at, end - at)); at = end;
        }
        var packet = new { version = 1, text16 = Convert.ToBase64String(MemoryMarshal.AsBytes(text.AsSpan())), runs, paragraphs = rows ?? paragraphs.ToArray() };
        var args = new TextCompositionEventArgs(UIElement.TextInputEvent, text, 0)
        { ImeReplacementRange = (start, length), NativeTextServicesPayload = JsonSerializer.Serialize(new { proposal = packet }), NativeEditLifetime = current ?? (() => true) };
        editor.RaiseEvent(args); return args;
    }
    private static object Row(int start, int length, object[]? lists = null, object[]? cells = null)
        => new { start, length, alignment = 0, direction = 0, lists = lists ?? [], tables = cells ?? [] };
    private static object List() => new { id = "list", format = "{decimal}", start = 3 };
    private static object Cell(int row, int column) => new { id = "table", row, column, rows = 1, columns = 1, tableColumns = 2 };
    public void Dispose() => Keyboard.ClearFocus();
}
