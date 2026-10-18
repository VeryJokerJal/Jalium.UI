using System.Runtime.InteropServices;
using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class WritingToolsParagraphFormattingTests
{
    public static IEnumerable<object[]> Styles()
    {
        foreach (int alignment in new[] { 0, 1, 2, 3 })
        foreach (int direction in new[] { 0, 1 })
        foreach (bool wholeParagraph in new[] { true, false }) yield return [alignment, direction, wholeParagraph];
    }

    [Theory, MemberData(nameof(Styles))]
    public void PartialDocumentReplacementRetainsTheIncomingFirstParagraphStyle(int alignment, int direction, bool wholeParagraph)
    {
        var first = new Paragraph(new Run("前段")) { TextAlignment = TextAlignment.Right };
        var middle = new Paragraph(new Run("旧段🙂")) { TextAlignment = TextAlignment.Left, FlowDirection = FlowDirection.LeftToRight };
        var last = new Paragraph(new Run("后段")) { TextAlignment = TextAlignment.Center };
        var document = new FlowDocument(); document.Blocks.Add(first); document.Blocks.Add(middle); document.Blocks.Add(last);
        int start = "前段\n".Length + (wholeParagraph ? 0 : 1), length = wholeParagraph ? "旧段🙂".Length : 1;
        string before = document.GetText(); const string replacement = "新文🙂";
        WritingToolsDocumentFragment.Parse(Packet(replacement, alignment, direction)).Insert(document, start, length, () => true);
        Assert.Equal(before[..start] + replacement + before[(start + length)..], document.GetText());
        var changed = Assert.Single(document.EnumerateParagraphs().Where(paragraph => paragraph.Inlines.OfType<Run>().Any(run => run.Text.Contains("新文"))));
        Assert.Equal(Alignment(alignment), changed.TextAlignment); Assert.Equal((FlowDirection)direction, changed.FlowDirection);
        Assert.Same(first, document.Blocks[0]); Assert.Equal(TextAlignment.Right, first.TextAlignment);
        Assert.Same(last, document.Blocks[document.Blocks.Count - 1]); Assert.Equal(TextAlignment.Center, last.TextAlignment);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void RichReplacementUndoAndRedoRestoreParagraphStyleAndIdentity(bool rtl)
    {
        var paragraph = new Paragraph(new Run("旧段🙂")) { TextAlignment = TextAlignment.Left, FlowDirection = FlowDirection.LeftToRight };
        var document = new FlowDocument(); document.Blocks.Add(new Paragraph(new Run("前段"))); document.Blocks.Add(paragraph);
        var editor = new RichTextBox(document); string before = document.GetText();
        var args = new TextCompositionEventArgs(UIElement.TextInputEvent, "新文🙂", 0)
        {
            ImeReplacementRange = ("前段\n".Length, "旧段🙂".Length), NativeEditLifetime = () => true,
            NativeTextServicesPayload = JsonSerializer.Serialize(new { proposal = Packet("新文🙂", 2, rtl ? 1 : 0) })
        };
        editor.RaiseEvent(args); Assert.True(args.Handled);
        var changed = document.EnumerateParagraphs().Last(); Assert.Equal(TextAlignment.Center, changed.TextAlignment);
        Assert.Equal(rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, changed.FlowDirection);
        Assert.True(editor.Undo()); Assert.Equal(before, document.GetText()); Assert.Same(paragraph, document.Blocks[1]);
        Assert.Equal(TextAlignment.Left, paragraph.TextAlignment); Assert.Equal(FlowDirection.LeftToRight, paragraph.FlowDirection);
        Assert.True(editor.Redo()); changed = document.EnumerateParagraphs().Last();
        Assert.Equal(TextAlignment.Center, changed.TextAlignment); Assert.Equal(rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, changed.FlowDirection);
    }

    [Fact]
    public void WholeDocumentReplacementAlreadyRetainsParagraphStyle()
    {
        var document = new FlowDocument(new Paragraph(new Run("old")));
        WritingToolsDocumentFragment.Parse(Packet("新🙂", 2, 1)).Insert(document, 0, document.GetText().Length, () => true);
        var paragraph = Assert.IsType<Paragraph>(Assert.Single(document.Blocks));
        Assert.Equal(TextAlignment.Center, paragraph.TextAlignment); Assert.Equal(FlowDirection.RightToLeft, paragraph.FlowDirection);
    }

    private static TextAlignment Alignment(int value) => value switch { 1 => TextAlignment.Right, 2 => TextAlignment.Center, 3 => TextAlignment.Justify, _ => TextAlignment.Left };
    private static JsonElement Packet(string text, int alignment, int direction) => JsonSerializer.SerializeToElement(new
    {
        version = 1, text16 = Convert.ToBase64String(MemoryMarshal.AsBytes(text.AsSpan())),
        runs = new[] { new { start = 0, length = text.Length, family = "Helvetica Neue", size = 20, weight = 400, italic = false, underline = 0, strike = 0, foreground = 0xff222222u } },
        paragraphs = new[] { new { start = 0, length = text.Length, alignment, direction, lists = Array.Empty<object>(), tables = Array.Empty<object>() } }
    });
}
