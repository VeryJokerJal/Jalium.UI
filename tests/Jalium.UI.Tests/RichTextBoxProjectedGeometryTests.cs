using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class RichTextBoxProjectedGeometryTests
{
    [Theory]
    [InlineData("line-height:2;padding-left:11px")]
    [InlineData("text-transform:uppercase;letter-spacing:2px")]
    [InlineData("direction:rtl;line-height:28px")]
    public void StyledCharacterRectanglesFollowCompleteAuthorGraphemes(string style)
    {
        const string text = "Miii中文🙂e\u0301";
        var run = new Run(text); var paragraph = new Paragraph(run); Css.SetStyle(paragraph, style);
        var editor = Editor(new FlowDocument(paragraph));
        var first = run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        var last = run.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
        Assert.True(first.Width > 0 && first.Height > 0, $"The first visible grapheme has no geometry: {first}");
        Assert.True(last.Width > 0 && last.Height > 0, $"The last visible grapheme has no geometry: {last}");
        var emoji = run.ContentStart.GetPositionAtOffset(6)!.GetCharacterRect(LogicalDirection.Forward);
        var surrogate = run.ContentStart.GetPositionAtOffset(7)!.GetCharacterRect(LogicalDirection.Forward);
        Assert.True(emoji.Width > 0 && emoji.Height > 0); Assert.Equal(emoji, surrogate);
        var combining = run.ContentStart.GetPositionAtOffset(9)!.GetCharacterRect(LogicalDirection.Backward);
        Assert.Equal(last, combining);
        Assert.Equal(text, new TextRange(run.ContentStart, run.ContentEnd).Text);
    }

    [Fact]
    public void APreviousOwnerCannotReturnItsCurrentLayoutForAMovedDocument()
    {
        var run = new Run("Miii中文🙂"); var document = new FlowDocument(new Paragraph(run));
        var original = Editor(document); var pointer = run.ContentStart;
        original.Document = new FlowDocument(new Paragraph(new Run("current")));
        var successor = Editor(document);
        Assert.True(pointer.GetCharacterRect(LogicalDirection.Forward).Height > 0);
        Assert.Equal(Rect.Empty, original.GetTextPositionCharacterRect(pointer, LogicalDirection.Forward));
        Assert.Same(document, successor.Document);
    }

    private static RichTextBox Editor(FlowDocument document)
    {
        var editor = new RichTextBox(document) { Width = 340, Height = 180, Padding = new Thickness(8) };
        editor.Measure(new Size(340, 180)); editor.Arrange(new Rect(0, 0, 340, 180)); return editor;
    }
}
